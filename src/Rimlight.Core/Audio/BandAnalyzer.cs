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
        return new BandEnergies(
            Rms(magnitudes, binHz, tuning.BassMinHz, tuning.BassMaxHz, false),
            Rms(magnitudes, binHz, tuning.BassMaxHz, tuning.MidMaxHz, false),
            Rms(magnitudes, binHz, tuning.MidMaxHz, tuning.HighMaxHz, true));
    }

    private static float Rms(ReadOnlySpan<float> spectrum, float binHz, float low, float high, bool includeHigh)
    {
        int start = (int)MathF.Min(spectrum.Length, MathF.Max(0, MathF.Ceiling(low / binHz)));
        float upper = includeHigh ? MathF.Floor(high / binHz) + 1 : MathF.Ceiling(high / binHz);
        int end = (int)MathF.Min(spectrum.Length, MathF.Max(0, upper));
        if (end <= start) return 0;
        float squares = 0;
        for (int k = start; k < end; k++) squares += spectrum[k] * spectrum[k];
        return MathF.Sqrt(squares / (end - start));
    }
}
