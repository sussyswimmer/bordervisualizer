namespace Rimlight.Core.Audio;

// The real IAudioAnalyzer (doc 03 §2): C1's front end for Level/Bass, spectral-flux beats, silence detection
// and diagnostics. One instance belongs to the render thread; Process never allocates after construction.
internal sealed class AudioAnalyzer : IAudioAnalyzer
{
    private AudioTuning tuning;
    private AudioFrontEnd frontEnd;
    private BeatDetector beats;
    private float[] silence;          // zeros fed in place of missing packets
    private readonly SilenceDetector silenceDetector = new();
    private int previousSampleRate;
    private float sinceData, beat;

    public AudioAnalyzer(AudioTuning? tuning = null)
    {
        this.tuning = tuning ?? new AudioTuning();
        Validate(this.tuning);
        frontEnd = new AudioFrontEnd(this.tuning);
        beats = new BeatDetector(this.tuning.WindowSize / 2 + 1);
        silence = new float[this.tuning.WindowSize];
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
                silence = new float[value.WindowSize];
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
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        if (previousSampleRate != 0 && sampleRate != previousSampleRate) Reset();
        previousSampleRate = sampleRate;

        // WASAPI loopback sends no packets during silence (doc 03 §1). After NoPacketTimeoutSeconds the gap is
        // treated as digital silence: zeros flow through the window, so the spectrum, silence detection and
        // auto-gain hold behave exactly as they do for real silence.
        ReadOnlySpan<float> input = newSamples;
        bool hasInput = !newSamples.IsEmpty;
        if (hasInput)
        {
            sinceData = 0;
        }
        else
        {
            sinceData += dt;
            if (sinceData > tuning.NoPacketTimeoutSeconds)
            {
                int count = Math.Clamp((int)MathF.Round(dt * sampleRate), 1, silence.Length);
                input = silence.AsSpan(0, count);
                hasInput = true;
            }
        }

        AmplitudeFeatures amplitude = frontEnd.Process(input, sampleRate, dt);
        BeatResult result = beats.Update(frontEnd.Spectrum, hasInput, tuning.WindowSize, sampleRate, dt, tuning);
        bool isSilent = hasInput ? silenceDetector.Update(Rms(input), dt, tuning) : silenceDetector.IsSilent;

        beat = result.IsBeat ? 1 : beat * MathF.Exp(-dt / tuning.BeatDecaySeconds);
        float sensitivity = Math.Clamp(tuning.Sensitivity, 0.25f, 2f);
        float level = Math.Clamp(amplitude.Level * sensitivity, 0, 1);

        UpdateDiagnostics(result);
        return new AudioFeatures(level, amplitude.Bass, beat, isSilent);
    }

    public void Reset()
    {
        frontEnd.Reset();
        beats.Reset();
        silenceDetector.Reset();
        previousSampleRate = 0;
        sinceData = beat = 0;
        Array.Clear(Diagnostics.Spectrum);
        Array.Clear(Diagnostics.FluxHistory);
        Array.Clear(Diagnostics.ThresholdHistory);
        Diagnostics.BeatCount = 0;
        Diagnostics.EstimatedBpm = 0;
    }

    // One history entry per Process call (including frames without new samples), oldest first.
    // Spectrum is in amplitude units: |X[k]| × 4 / N, so a full-scale sine centered on bin k reads ≈ 1.
    private void UpdateDiagnostics(BeatResult result)
    {
        AnalyzerDiagnostics d = Diagnostics;
        ReadOnlySpan<float> magnitudes = frontEnd.Spectrum;
        float scale = 4f / tuning.WindowSize;
        int bins = Math.Min(d.Spectrum.Length, magnitudes.Length);
        for (int k = 0; k < bins; k++) d.Spectrum[k] = magnitudes[k] * scale;

        Shift(d.FluxHistory, result.Flux);
        Shift(d.ThresholdHistory, result.Threshold);
        if (result.IsBeat) d.BeatCount++;
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
        if (tuning.WindowSize < 2 || tuning.WindowSize > 1 << 20 || (tuning.WindowSize & (tuning.WindowSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(tuning.WindowSize), "Expected a power of two from 2 through 1048576.");
        Positive(tuning.FluxHistorySeconds, nameof(tuning.FluxHistorySeconds));
        Nonnegative(tuning.FluxThresholdMultiplier, nameof(tuning.FluxThresholdMultiplier));
        Nonnegative(tuning.MinFlux, nameof(tuning.MinFlux));
        Nonnegative(tuning.BeatRefractorySeconds, nameof(tuning.BeatRefractorySeconds));
        Positive(tuning.BeatDecaySeconds, nameof(tuning.BeatDecaySeconds));
        Positive(tuning.Sensitivity, nameof(tuning.Sensitivity));
        Nonnegative(tuning.NoPacketTimeoutSeconds, nameof(tuning.NoPacketTimeoutSeconds));
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
