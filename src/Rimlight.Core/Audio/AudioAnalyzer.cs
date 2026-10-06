namespace Rimlight.Core.Audio;

// The real IAudioAnalyzer (doc 03 §2): C1's front end for Level/Bass, spectral-flux beats, silence detection
// and diagnostics. One instance belongs to the render thread; Process never allocates after construction.
internal sealed class AudioAnalyzer : IAudioAnalyzer
{
    // Doc 03 sets hop = "whatever arrived since the last frame". Flux grows with the hop, so at 30 fps, or with
    // 50 ms packets landing in one frame, kicks stop standing out from the flux history. New samples are therefore
    // analyzed in equal steps of at most one 60 fps frame of audio (800 samples at 48 kHz). The newest samples
    // are always the last step, so this adds no latency, and frames up to that size are analyzed exactly as doc 03
    // says. Shorter steps (fast render loops, small packets) are analyzed as they come, down to MinBeatStepSeconds:
    // tests at 144 and 240 fps detect the same beats, and holding them back would add latency.
    private const float MaxStepSeconds = 1f / 60;

    // On displays faster than 240 Hz, frames shorter than 1/240 s are folded into the next beat step (Level and
    // Bass still update every frame). That bounds the flux history at 240 entries a second, so its ring covers
    // FluxHistorySeconds at any refresh rate, for at most ~4 ms of extra beat latency on such displays.
    private const float MinBeatStepSeconds = 1f / 240;

    // More new audio than the window plus StallSteps steps (≈ 0.27 s) in one frame means the render loop stalled
    // (window drag, GPU reset, sleep). Only the newest window plus BurstSteps steps of such a burst is analyzed.
    private const int StallSteps = 16;
    private const int BurstSteps = 4;
    private const int MaxStep = 2048;
    private const int MinWindowSize = 1024; // 47 Hz bins at 48 kHz; at 256, 30–150 Hz falls between bins
    private const int MaxWindowSize = 16384;
    private const float MinSensitivity = 0.25f;
    private const float MaxSensitivity = 2f;
    private const float MinNoPacketTimeoutSeconds = 0.03f; // above WASAPI's 10–20 ms packet period
    private const float MaxFluxHistorySeconds = 4f;
    private const float MaxGapSeconds = 3600f;             // keeps the gap clocks finite after absurd dt values

    private AudioTuning tuning;
    private AudioFrontEnd frontEnd;
    private BeatDetector beats;
    private float[] analysisBuffer;   // decimated samples, or zeros fed in place of missing packets
    private readonly Decimator decimator = new();
    private readonly SilenceDetector silenceDetector = new();
    private int inputRate, analysisRate, maxStep, minBeatStep, pendingBeatSamples, zerosFed;
    private float sinceData, silenceSeconds, beat;

    public AudioAnalyzer(AudioTuning? tuning = null)
    {
        this.tuning = tuning ?? new AudioTuning();
        Validate(this.tuning);
        frontEnd = new AudioFrontEnd(this.tuning);
        beats = new BeatDetector(this.tuning.WindowSize / 2 + 1);
        analysisBuffer = new float[AnalysisBufferLength(this.tuning.WindowSize)];
        Diagnostics = new AnalyzerDiagnostics(this.tuning.WindowSize / 2 + 1);
    }

    public AnalyzerDiagnostics Diagnostics { get; private set; }

    // Set from the render thread only. Invalid tuning throws and keeps the previous value.
    // Changing WindowSize allocates new buffers here (never in Process), resets analysis state
    // and replaces the Diagnostics instance.
    public AudioTuning Tuning
    {
        get => tuning;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Validate(value);
            if (value.WindowSize != tuning.WindowSize)
            {
                frontEnd = new AudioFrontEnd(value);
                beats = new BeatDetector(value.WindowSize / 2 + 1);
                analysisBuffer = new float[AnalysisBufferLength(value.WindowSize)];
                Diagnostics = new AnalyzerDiagnostics(value.WindowSize / 2 + 1, Diagnostics.FluxHistory.Length);
                tuning = value;
                Reset();
                return;
            }
            frontEnd.Tuning = value;
            tuning = value;
        }
    }

    public AudioFeatures Process(ReadOnlySpan<float> newSamples, int sampleRate, float dtSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        // A clock glitch on the render thread must not throw or poison state.
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? MathF.Min(dtSeconds, MaxGapSeconds) : 0;
        if (sampleRate != inputRate) Configure(sampleRate);
        silenceSeconds = MathF.Min(silenceSeconds + dt, MaxGapSeconds);

        // WASAPI loopback sends no packets during silence (doc 03 §1). After NoPacketTimeoutSeconds the gap is
        // treated as digital silence: zeros flow through the window, so the spectrum, silence detection and
        // auto-gain hold behave exactly as they do for real silence.
        ReadOnlySpan<float> samples;
        int skipped = 0;
        bool isSilent, idleZeros = false;
        if (!newSamples.IsEmpty)
        {
            sinceData = 0;
            zerosFed = 0;
            // Silence time includes the empty frames between packets (fast render loops, 10–30 ms packets).
            isSilent = silenceDetector.Update(Rms(newSamples), silenceSeconds, tuning);
            silenceSeconds = 0;
            samples = Decimate(newSamples, out skipped);
        }
        else
        {
            sinceData = MathF.Min(sinceData + dt, MaxGapSeconds);
            if (sinceData > tuning.NoPacketTimeoutSeconds)
            {
                isSilent = silenceDetector.Update(0, silenceSeconds, tuning);
                silenceSeconds = 0;
                decimator.Reset(); // its history must match the zeros that bypass it
                if (zerosFed < tuning.WindowSize)
                {
                    int count = (int)Math.Clamp(MathF.Round(dt * analysisRate), 1, tuning.WindowSize);
                    samples = analysisBuffer.AsSpan(0, count);
                    analysisBuffer.AsSpan(0, count).Clear();
                    zerosFed += count;
                }
                else
                {
                    // The window is all zeros already, so more zeros would leave the spectrum unchanged: skip the FFT
                    // while idle. The beat detector still records the zero flux, exactly as if zeros were fed.
                    samples = default;
                    pendingBeatSamples += (int)Math.Clamp(MathF.Round(dt * analysisRate), 0, tuning.WindowSize);
                    idleZeros = pendingBeatSamples >= minBeatStep;
                    if (idleZeros) pendingBeatSamples = 0;
                }
            }
            else
            {
                isSilent = silenceDetector.IsSilent;
                samples = default;
            }
        }

        AmplitudeFeatures amplitude = default;
        int beatsFired = 0, start = 0;
        float flux = 0, threshold = 0;
        float secondsPerSample = samples.IsEmpty ? 0 : dt / (samples.Length + skipped);
        if (skipped > 0)
        {
            // A burst after a stall: the dropped audio and the refill of the window are not onsets, and the flux
            // statistics describe audio from before the stall, so the beat detector restarts from the refilled window.
            // The dropped time is attributed to the refill.
            int fill = Math.Min(tuning.WindowSize, samples.Length);
            float fillDt = secondsPerSample * (skipped + fill);
            amplitude = frontEnd.Process(samples[..fill], analysisRate, fillDt);
            beats.Restart();
            threshold = beats.Update(frontEnd.Spectrum, true, tuning.WindowSize, analysisRate, fillDt, tuning, frontEnd.WindowRms).Threshold;
            pendingBeatSamples = 0;
            start = fill;
        }
        if (samples.IsEmpty)
        {
            amplitude = frontEnd.Process(samples, analysisRate, dt);
            threshold = beats.Update(frontEnd.Spectrum, idleZeros, tuning.WindowSize, analysisRate, dt, tuning, frontEnd.WindowRms).Threshold;
        }
        else
        {
            // Equal steps of at most maxStep samples; the newest samples are always analyzed last.
            int length = samples.Length - start, steps = (length + maxStep - 1) / maxStep;
            for (int s = 1; s <= steps; s++)
            {
                int end = samples.Length - length + (int)((long)length * s / steps);
                float stepDt = secondsPerSample * (end - start);
                amplitude = frontEnd.Process(samples[start..end], analysisRate, stepDt);
                pendingBeatSamples += end - start;
                bool analyze = pendingBeatSamples >= minBeatStep;
                if (analyze) pendingBeatSamples = 0;
                BeatResult result = beats.Update(frontEnd.Spectrum, analyze, tuning.WindowSize, analysisRate, stepDt, tuning, frontEnd.WindowRms);
                if (result.IsBeat) beatsFired++;
                flux = MathF.Max(flux, result.Flux);
                threshold = result.Threshold;
                start = end;
            }
        }

        beat = beatsFired > 0 ? 1 : beat * MathF.Exp(-dt / tuning.BeatDecaySeconds);
        float level = Math.Clamp(amplitude.Level * tuning.Sensitivity, 0, 1);
        UpdateDiagnostics(flux, threshold, beatsFired);
        return new AudioFeatures(level, amplitude.Bass, beat, isSilent);
    }

    public void Reset()
    {
        frontEnd.Reset();
        beats.Reset();
        decimator.Reset();
        silenceDetector.Reset();
        inputRate = zerosFed = pendingBeatSamples = 0;
        sinceData = silenceSeconds = beat = 0;
        Array.Clear(Diagnostics.Spectrum);
        Array.Clear(Diagnostics.FluxHistory);
        Array.Clear(Diagnostics.ThresholdHistory);
        Diagnostics.BeatCount = 0;
        Diagnostics.EstimatedBpm = 0;
    }

    internal float FluxHistoryStoredSeconds => beats.StoredSeconds;

    // The rate the spectrum, bands and diagnostics are computed at: the input rate divided by the decimation
    // factor (Decimator.FactorFor). Public as CoreFactory.AnalysisSampleRate for mapping bins to Hz.
    internal static int AnalysisRate(int sampleRate) => sampleRate / Decimator.FactorFor(sampleRate);

    private void Configure(int sampleRate)
    {
        if (inputRate != 0) Reset();
        inputRate = sampleRate;
        decimator.Configure(sampleRate);
        analysisRate = AnalysisRate(sampleRate);
        maxStep = Math.Clamp((int)MathF.Round(analysisRate * MaxStepSeconds), 1, MaxStep);
        minBeatStep = Math.Max(1, (int)MathF.Round(analysisRate * MinBeatStepSeconds));
    }

    // Returns the new samples at the analysis rate. Without decimation that is the input itself (no copy).
    // skipped: how many analysis-rate samples of an oversized burst were dropped.
    private ReadOnlySpan<float> Decimate(ReadOnlySpan<float> input, out int skipped)
    {
        skipped = 0;
        if (input.Length > (tuning.WindowSize + StallSteps * maxStep) * decimator.Factor)
        {
            int keep = (tuning.WindowSize + BurstSteps * maxStep) * decimator.Factor;
            skipped = (input.Length - keep) / decimator.Factor;
            input = input[^keep..];
        }
        if (decimator.Factor == 1) return input;
        int count = decimator.Process(input, analysisBuffer);
        return analysisBuffer.AsSpan(0, count);
    }

    private static int AnalysisBufferLength(int windowSize) => windowSize + StallSteps * MaxStep + 1;

    // One history entry per Process call (including frames without new samples), oldest first: the largest flux
    // of the call's steps and the latest threshold. Spectrum is in amplitude units: |X[k]| × 4 / N, so a
    // full-scale sine centered on bin k reads ≈ 1.
    private void UpdateDiagnostics(float flux, float threshold, int beatsFired)
    {
        AnalyzerDiagnostics d = Diagnostics;
        ReadOnlySpan<float> magnitudes = frontEnd.Spectrum;
        float scale = 4f / tuning.WindowSize;
        int bins = Math.Min(d.Spectrum.Length, magnitudes.Length);
        for (int k = 0; k < bins; k++) d.Spectrum[k] = magnitudes[k] * scale;

        Shift(d.FluxHistory, flux);
        Shift(d.ThresholdHistory, threshold);
        d.BeatCount += beatsFired;
        d.EstimatedBpm = beats.EstimatedBpm;
    }

    private static void Shift(float[] history, float newest)
    {
        if (history.Length == 0) return;
        Array.Copy(history, 1, history, 0, history.Length - 1);
        history[^1] = newest;
    }

    private static float Rms(ReadOnlySpan<float> samples)
    {
        float squares = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float x = samples[i];
            if (float.IsFinite(x)) squares += x * x;
        }
        return MathF.Sqrt(squares / samples.Length);
    }

    private static void Validate(AudioTuning tuning)
    {
        AudioFrontEnd.Validate(tuning);
        if (tuning.WindowSize < MinWindowSize || tuning.WindowSize > MaxWindowSize || (tuning.WindowSize & (tuning.WindowSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(tuning.WindowSize), $"Expected a power of two from {MinWindowSize} through {MaxWindowSize}.");
        if (!float.IsFinite(tuning.FluxHistorySeconds) || tuning.FluxHistorySeconds <= 0 || tuning.FluxHistorySeconds > MaxFluxHistorySeconds)
            throw new ArgumentOutOfRangeException(nameof(tuning.FluxHistorySeconds), $"Expected more than 0 and at most {MaxFluxHistorySeconds} s.");
        Nonnegative(tuning.FluxThresholdMultiplier, nameof(tuning.FluxThresholdMultiplier));
        Nonnegative(tuning.MinFlux, nameof(tuning.MinFlux));
        Nonnegative(tuning.BeatRefractorySeconds, nameof(tuning.BeatRefractorySeconds));
        Positive(tuning.BeatDecaySeconds, nameof(tuning.BeatDecaySeconds));
        if (!(tuning.Sensitivity >= MinSensitivity && tuning.Sensitivity <= MaxSensitivity))
            throw new ArgumentOutOfRangeException(nameof(tuning.Sensitivity), $"Expected {MinSensitivity} through {MaxSensitivity} (doc 03 §2).");
        if (!(tuning.NoPacketTimeoutSeconds >= MinNoPacketTimeoutSeconds && tuning.NoPacketTimeoutSeconds <= MaxGapSeconds))
            throw new ArgumentOutOfRangeException(nameof(tuning.NoPacketTimeoutSeconds), $"Expected at least {MinNoPacketTimeoutSeconds} s.");
        if (!float.IsFinite(tuning.SilenceExitThresholdDb) || tuning.SilenceExitThresholdDb < tuning.SilenceThresholdDb)
            throw new ArgumentOutOfRangeException(nameof(tuning.SilenceExitThresholdDb), "Must be finite and at least SilenceThresholdDb.");
        if (tuning.SilenceHoldMs < 0) throw new ArgumentOutOfRangeException(nameof(tuning.SilenceHoldMs));
    }

    private static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }

    private static void Nonnegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
    }
}
