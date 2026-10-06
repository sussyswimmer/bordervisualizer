namespace Rimlight.Core.Audio;

internal readonly record struct AmplitudeFeatures(float Level, float Bass);

// C1 building block for Level and Bass. AudioAnalyzer (C2) wraps it with beats, silence and diagnostics.
// One instance belongs to one render thread. All storage is allocated up front.
internal sealed class AudioFrontEnd
{
    private AudioTuning tuning;
    private readonly SpectrumAnalyzer spectrum;
    private readonly AutoGain bassGain = new();
    private readonly AutoGain midGain = new();
    private readonly AutoGain highGain = new();
    private readonly Envelope level = new();
    private readonly Envelope bass = new();
    private float holdRms;
    private int previousSampleRate;

    public AudioFrontEnd(AudioTuning? tuning = null)
    {
        this.tuning = tuning ?? new AudioTuning();
        Validate(this.tuning);
        spectrum = new SpectrumAnalyzer(this.tuning.WindowSize);
        holdRms = HoldRms(this.tuning);
    }

    public ReadOnlySpan<float> Spectrum => spectrum.Magnitudes;

    // Live tuning (doc 03 §4). WindowSize is fixed per instance; the analyzer rebuilds the front end to change it.
    public AudioTuning Tuning
    {
        get => tuning;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.WindowSize != tuning.WindowSize)
                throw new ArgumentException("WindowSize is fixed per front end; create a new one to change it.", nameof(value));
            Validate(value);
            tuning = value;
            holdRms = HoldRms(value);
        }
    }

    public AmplitudeFeatures Process(ReadOnlySpan<float> samples, int sampleRate, float dtSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (!float.IsFinite(dtSeconds) || dtSeconds < 0) throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        if (previousSampleRate != 0 && sampleRate != previousSampleRate) Reset();
        previousSampleRate = sampleRate;
        spectrum.Process(samples);
        BandEnergies energies = BandAnalyzer.Analyze(spectrum.Magnitudes, tuning.WindowSize, sampleRate, tuning);
        // While any quarter of the window is near-silent (digital zeros, dither), the band magnitudes collapse.
        // Auto-gain then freezes its floor, so a gap must not drag the floor down and pin Level/Bass near 1
        // once music resumes. This includes the frames where a gap or the music only partly fills the window.
        bool silent = spectrum.QuietestQuarterRms <= holdRms;
        float bassNorm = bassGain.Update(energies.Bass, dtSeconds, tuning, silent);
        float midNorm = midGain.Update(energies.Mid, dtSeconds, tuning, silent);
        float highNorm = highGain.Update(energies.High, dtSeconds, tuning, silent);
        // Weights are free-form live-tuning values (doc 03 §4), so clamp instead of requiring a sum of 1.
        float target = Math.Clamp(tuning.LevelBassWeight * bassNorm + tuning.LevelMidWeight * midNorm + tuning.LevelHighWeight * highNorm, 0, 1);
        return new AmplitudeFeatures(
            level.Update(target, dtSeconds, tuning.LevelAttackSeconds, tuning.LevelReleaseSeconds),
            bass.Update(bassNorm, dtSeconds, tuning.BassAttackSeconds, tuning.BassReleaseSeconds));
    }

    public void Reset()
    {
        spectrum.Reset();
        bassGain.Reset();
        midGain.Reset();
        highGain.Reset();
        level.Reset();
        bass.Reset();
        previousSampleRate = 0;
    }

    // The hold is for near-silence only: 20 dB below SilenceThresholdDb (-80 dBFS by default). Quiet but real music
    // between -80 and -60 dBFS keeps adapting as doc 03 describes; doc 03's 2 s IsSilent rule is C2's concern.
    internal static float HoldRms(AudioTuning tuning) => MathF.Pow(10, (tuning.SilenceThresholdDb - 20) / 20);

    internal static void Validate(AudioTuning tuning)
    {
        Positive(tuning.MagnitudeEpsilon, nameof(tuning.MagnitudeEpsilon));
        Positive(tuning.PeakHalfLifeSeconds, nameof(tuning.PeakHalfLifeSeconds));
        Nonnegative(tuning.FloorRiseDbPerSecond, nameof(tuning.FloorRiseDbPerSecond));
        Positive(tuning.MinimumRangeDb, nameof(tuning.MinimumRangeDb));
        Positive(tuning.LevelAttackSeconds, nameof(tuning.LevelAttackSeconds));
        Positive(tuning.LevelReleaseSeconds, nameof(tuning.LevelReleaseSeconds));
        Positive(tuning.BassAttackSeconds, nameof(tuning.BassAttackSeconds));
        Positive(tuning.BassReleaseSeconds, nameof(tuning.BassReleaseSeconds));
        Nonnegative(tuning.BassMinHz, nameof(tuning.BassMinHz));
        Positive(tuning.BassMaxHz, nameof(tuning.BassMaxHz));
        Positive(tuning.MidMaxHz, nameof(tuning.MidMaxHz));
        Positive(tuning.HighMaxHz, nameof(tuning.HighMaxHz));
        if (tuning.BassMinHz >= tuning.BassMaxHz || tuning.BassMaxHz >= tuning.MidMaxHz || tuning.MidMaxHz >= tuning.HighMaxHz)
            throw new ArgumentException("Band boundaries must be strictly increasing.", nameof(tuning));
        UnitInterval(tuning.LevelBassWeight, nameof(tuning.LevelBassWeight));
        UnitInterval(tuning.LevelMidWeight, nameof(tuning.LevelMidWeight));
        UnitInterval(tuning.LevelHighWeight, nameof(tuning.LevelHighWeight));
        if (!float.IsFinite(tuning.SilenceThresholdDb)) throw new ArgumentOutOfRangeException(nameof(tuning.SilenceThresholdDb));
    }

    private static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }

    private static void Nonnegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
    }

    private static void UnitInterval(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(name);
    }
}
