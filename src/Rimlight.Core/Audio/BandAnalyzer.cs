namespace Rimlight.Core.Audio;

internal readonly record struct BandEnergies(float Bass, float Mid, float High);

internal static class BandAnalyzer
{
    // Adjacent bands are lower-inclusive / upper-exclusive to avoid counting a bin
    // twice. The final high endpoint is inclusive. Only available bins contribute.
    public static BandEnergies Analyze(ReadOnlySpan<float> magnitudes, int windowSize, int sampleRate, AudioTuning tuning)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 2);
        if (magnitudes.Length != windowSize / 2 + 1)
            throw new ArgumentException("Expected a one-sided magnitude spectrum.", nameof(magnitudes));
        float binHz = (float)sampleRate / windowSize;
        (int bassStart, int bassEnd) = BassBins(magnitudes.Length, binHz, tuning);
        return new BandEnergies(
            Rms(magnitudes, bassStart, bassEnd),
            Rms(magnitudes, Bins(magnitudes.Length, binHz, tuning.BassMaxHz, tuning.MidMaxHz, false)),
            Rms(magnitudes, Bins(magnitudes.Length, binHz, tuning.MidMaxHz, tuning.HighMaxHz, true)));
    }

    // The music's character (SongLoudness, BeatCharacter) is measured on fixed internal bands, not on AudioTuning's
    // live-tunable edges: its thresholds were set on these, and widening the beat band in the debug visualizer (say to
    // 30–250 Hz, which takes in a baritone's harmonics) must not change how loud a song is or how kick-like an onset is.
    // Loudness bands are doc 03's defaults; the kick band holds a kick's or an 808's body and fundamental (40–90 Hz)
    // but not a baritone's fundamental (98 Hz and up) or most of a guitar's or a piano's low strings.
    internal const float LoudnessBassMinHz = 30, LoudnessBassMaxHz = 150, LoudnessMidMaxHz = 2000, LoudnessHighMaxHz = 12000;
    internal const float KickMinHz = 30, KickMaxHz = 100;

    // Broadband power for SongLoudness (Total) and BeatCharacter (Upper, the music above the bass band): Σ weight ×
    // magnitude² over the fixed loudness bands, in amplitude units squared (magnitudes × 4/N, so a full-scale sine reads
    // about 1).
    internal static (float Total, float Upper) WeightedPower(ReadOnlySpan<float> magnitudes, int windowSize, int sampleRate, float bassWeight, float midWeight, float highWeight)
    {
        float binHz = (float)sampleRate / windowSize, scale = 4f / windowSize;
        float bass = bassWeight * Squares(magnitudes, Bins(magnitudes.Length, binHz, LoudnessBassMinHz, LoudnessBassMaxHz, false));
        float upper = midWeight * Squares(magnitudes, Bins(magnitudes.Length, binHz, LoudnessBassMaxHz, LoudnessMidMaxHz, false))
            + highWeight * Squares(magnitudes, Bins(magnitudes.Length, binHz, LoudnessMidMaxHz, LoudnessHighMaxHz, true));
        return ((bass + upper) * scale * scale, upper * scale * scale);
    }

    // The fixed kick band [start, end) for BeatCharacter, at least one bin wide as BassBins is.
    internal static (int Start, int End) KickBins(int spectrumLength, float binHz)
    {
        (int start, int end) = Bins(spectrumLength, binHz, KickMinHz, KickMaxHz, false);
        if (end > start) return (start, end);
        int bin = Math.Clamp((int)MathF.Floor(0.5f * (KickMinHz + KickMaxHz) / binHz), 1, spectrumLength - 1);
        return (bin, bin + 1);
    }

    // The music above the bass band, [start, end) over the fixed loudness bands (150 Hz to 12 kHz inclusive), for the beat
    // detector's onset contrast.
    internal static (int Start, int End) UpperBins(int spectrumLength, float binHz) =>
        (Bins(spectrumLength, binHz, LoudnessBassMaxHz, LoudnessMidMaxHz, false).Start, Bins(spectrumLength, binHz, LoudnessMidMaxHz, LoudnessHighMaxHz, true).End);

    // Bass bins [start, end), shared with the beat detector. A bass band narrower than one bin (a small WindowSize,
    // or narrow live-tuned edges) would contain no bin and silence Bass and beats, so it falls back to the bin just
    // below its center: never DC, and never the Mid band's first bin. A band above Nyquist stays empty.
    internal static (int Start, int End) BassBins(int spectrumLength, float binHz, AudioTuning tuning)
    {
        (int start, int end) = Bins(spectrumLength, binHz, tuning.BassMinHz, tuning.BassMaxHz, false);
        if (end > start) return (start, end);
        float center = 0.5f * (tuning.BassMinHz + tuning.BassMaxHz) / binHz;
        if (center > spectrumLength - 1) return (start, end);
        int bin = Math.Clamp((int)MathF.Floor(center), 1, spectrumLength - 1);
        return (bin, bin + 1);
    }

    private static (int Start, int End) Bins(int spectrumLength, float binHz, float low, float high, bool includeHigh)
    {
        int start = (int)MathF.Min(spectrumLength, MathF.Max(0, MathF.Ceiling(low / binHz)));
        float upper = includeHigh ? MathF.Floor(high / binHz) + 1 : MathF.Ceiling(high / binHz);
        int end = (int)MathF.Min(spectrumLength, MathF.Max(0, upper));
        return (start, end);
    }

    private static float Rms(ReadOnlySpan<float> spectrum, (int Start, int End) bins) => Rms(spectrum, bins.Start, bins.End);

    private static float Rms(ReadOnlySpan<float> spectrum, int start, int end)
    {
        if (end <= start) return 0;
        return MathF.Sqrt(Squares(spectrum, start, end) / (end - start));
    }

    private static float Squares(ReadOnlySpan<float> spectrum, (int Start, int End) bins) => Squares(spectrum, bins.Start, bins.End);

    private static float Squares(ReadOnlySpan<float> spectrum, int start, int end)
    {
        float squares = 0;
        for (int k = start; k < end; k++) squares += spectrum[k] * spectrum[k];
        return squares;
    }
}
