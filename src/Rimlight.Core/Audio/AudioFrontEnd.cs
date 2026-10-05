namespace Rimlight.Core.Audio;

internal readonly record struct AmplitudeFeatures(float Level, float Bass);

// C1 building block, deliberately not IAudioAnalyzer: C2 adds flux, beats,
// silence and diagnostics before replacing CoreFactory's FakeAnalyzer.
// One instance belongs to one render thread. All storage is allocated up front.
internal sealed class AudioFrontEnd
{
    private readonly AudioTuning tuning;
    private readonly SpectrumAnalyzer spectrum;
    private readonly AutoGain bassGain = new();
    private readonly AutoGain midGain = new();
    private readonly AutoGain highGain = new();
    private readonly Envelope level = new();
    private readonly Envelope bass = new();
    private int previousSampleRate;

    public AudioFrontEnd(AudioTuning? tuning = null)
    {
        this.tuning = tuning ?? new AudioTuning();
        Validate(this.tuning);
        spectrum = new SpectrumAnalyzer(this.tuning.WindowSize);
    }

    public ReadOnlySpan<float> Spectrum => spectrum.Magnitudes;

    public AmplitudeFeatures Process(ReadOnlySpan<float> samples, int sampleRate, float dtSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (!float.IsFinite(dtSeconds) || dtSeconds < 0) throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        if (previousSampleRate != 0 && sampleRate != previousSampleRate) Reset();
        previousSampleRate = sampleRate;
        spectrum.Process(samples);
        BandEnergies energies = BandAnalyzer.Analyze(spectrum.Magnitudes, tuning.WindowSize, sampleRate, tuning);
        float bassNorm = bassGain.Update(energies.Bass, dtSeconds, tuning);
        float midNorm = midGain.Update(energies.Mid, dtSeconds, tuning);
        float highNorm = highGain.Update(energies.High, dtSeconds, tuning);
        float target = tuning.LevelBassWeight * bassNorm + tuning.LevelMidWeight * midNorm + tuning.LevelHighWeight * highNorm;
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

    private static void Validate(AudioTuning tuning)
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
        Nonnegative(tuning.LevelBassWeight, nameof(tuning.LevelBassWeight));
        Nonnegative(tuning.LevelMidWeight, nameof(tuning.LevelMidWeight));
        Nonnegative(tuning.LevelHighWeight, nameof(tuning.LevelHighWeight));
        float sum = tuning.LevelBassWeight + tuning.LevelMidWeight + tuning.LevelHighWeight;
        if (MathF.Abs(sum - 1) > 0.00001f) throw new ArgumentException("Level weights must sum to one.", nameof(tuning));
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
