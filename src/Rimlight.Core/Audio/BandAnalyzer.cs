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

    // Broadband power for SongLoudness and BeatCharacter: Σ weight × magnitude² over the three bands (each bin once,
    // with the bands' own edges), in amplitude units squared (magnitudes × 4/N, so a full-scale sine reads about 1).
    internal static float WeightedPower(ReadOnlySpan<float> magnitudes, int windowSize, int sampleRate, AudioTuning tuning,
        float bassWeight, float midWeight, float highWeight)
    {
        float binHz = (float)sampleRate / windowSize, scale = 4f / windowSize;
        (int bassStart, int bassEnd) = BassBins(magnitudes.Length, binHz, tuning);
        float power = bassWeight * Squares(magnitudes, bassStart, bassEnd)
            + midWeight * Squares(magnitudes, Bins(magnitudes.Length, binHz, tuning.BassMaxHz, tuning.MidMaxHz, false))
            + highWeight * Squares(magnitudes, Bins(magnitudes.Length, binHz, tuning.MidMaxHz, tuning.HighMaxHz, true));
        return power * scale * scale;
    }

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

    // Mid bins [start, end), as Analyze uses them.
    internal static (int Start, int End) MidBins(int spectrumLength, float binHz, AudioTuning tuning) =>
        Bins(spectrumLength, binHz, tuning.BassMaxHz, tuning.MidMaxHz, false);

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
