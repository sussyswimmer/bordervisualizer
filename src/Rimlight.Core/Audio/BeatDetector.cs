namespace Rimlight.Core.Audio;

internal readonly record struct BeatResult(bool IsBeat, float Flux, float Threshold);

// Spectral-flux beat detection on the bass band (doc 03 §2). All storage is allocated up front.
// Flux is measured in amplitude units: magnitudes are scaled by 4/N, so a full-scale sine centered
// on a bin reads 1.0 (Hann coherent gain 0.5 × one-sided N/2). MinFlux is in the same units.
internal sealed class BeatDetector
{
    // Spec clarification (doc 03 leaves this open): with only mean + k·stddev, stationary noise crosses the
    // threshold on its statistical tail a few times a second (the bass band spans only ~5 bins, so its flux is
    // heavy-tailed), while doc 03 §5 requires no beats on constant white noise. A beat must also stand out from
    // the recent *median* flux: flux > median × MedianOnsetRatio. Kicks and note onsets sit far above the median
    // (×16 under heavy noise, ×hundreds in music) because they are sparse, while white noise peaks around ×7.4
    // over 5 minutes. Sensitivity relaxes the ratio toward 1 the same way it relaxes k.
    internal const float MedianOnsetRatio = 10f;

    // The adaptive threshold needs some history before it means anything (a quarter of FluxHistorySeconds).
    private const float WarmUpFraction = 0.25f;

    private const int MaxHistory = 1024;   // 1 s at up to ~1 kHz frame rates
    private const int IntervalCount = 16;  // beat intervals kept for the BPM median
    private const float MaxIntervalSeconds = 2f; // longer gaps (< 30 BPM) are not tempo

    private readonly float[] previous;
    private readonly float[] fluxValues = new float[MaxHistory];
    private readonly float[] fluxDurations = new float[MaxHistory];
    private readonly float[] sortedFlux = new float[MaxHistory];
    private readonly float[] intervals = new float[IntervalCount];
    private readonly float[] scratch = new float[IntervalCount];
    private int fluxHead, fluxCount, intervalHead, intervalCount;
    private bool hasPrevious, hasBeat;
    private float sinceBeat, lastThreshold;

    public BeatDetector(int spectrumLength) => previous = new float[spectrumLength];

    public float EstimatedBpm { get; private set; }

    // spectrumChanged: false when no new samples arrived; such frames carry no flux information.
    public BeatResult Update(ReadOnlySpan<float> magnitudes, bool spectrumChanged, int windowSize, int sampleRate, float dtSeconds, AudioTuning tuning)
    {
        sinceBeat += dtSeconds;
        if (!spectrumChanged) return new BeatResult(false, 0, lastThreshold);

        float binHz = (float)sampleRate / windowSize;
        int start = (int)MathF.Min(magnitudes.Length, MathF.Max(0, MathF.Ceiling(tuning.BassMinHz / binHz)));
        int end = (int)MathF.Min(magnitudes.Length, MathF.Max(0, MathF.Ceiling(tuning.BassMaxHz / binHz)));
        float scale = 4f / windowSize;
        float flux = 0;
        for (int k = start; k < end; k++)
        {
            float magnitude = magnitudes[k] * scale;
            if (hasPrevious) flux += MathF.Max(0, magnitude - previous[k]);
            previous[k] = magnitude;
        }
        if (!hasPrevious)
        {
            hasPrevious = true;
            return new BeatResult(false, 0, lastThreshold);
        }

        // Statistics over the flux history covering the last FluxHistorySeconds (current frame excluded).
        float sum = 0, sumSquares = 0, covered = 0;
        int used = 0;
        for (int i = 0; i < fluxCount && covered < tuning.FluxHistorySeconds; i++)
        {
            int index = (fluxHead - 1 - i + MaxHistory) % MaxHistory;
            float value = fluxValues[index];
            sum += value;
            sumSquares += value * value;
            covered += fluxDurations[index];
            used++;
        }

        float sensitivity = Math.Clamp(tuning.Sensitivity, 0.25f, 2f);
        float mean = used > 0 ? sum / used : 0;
        float stdDev = used > 1 ? MathF.Sqrt(MathF.Max(0, sumSquares / used - mean * mean)) : 0;
        float threshold = mean + tuning.FluxThresholdMultiplier / sensitivity * stdDev;
        float onsetFloor = Median(used) * (1 + (MedianOnsetRatio - 1) / sensitivity);
        lastThreshold = MathF.Max(threshold, onsetFloor);

        bool isBeat = covered >= tuning.FluxHistorySeconds * WarmUpFraction
            && flux > lastThreshold
            && flux > tuning.MinFlux
            && (!hasBeat || sinceBeat >= tuning.BeatRefractorySeconds);

        fluxValues[fluxHead] = flux;
        fluxDurations[fluxHead] = dtSeconds;
        fluxHead = (fluxHead + 1) % MaxHistory;
        fluxCount = Math.Min(fluxCount + 1, MaxHistory);

        if (isBeat)
        {
            if (hasBeat && sinceBeat <= MaxIntervalSeconds) AddInterval(sinceBeat);
            hasBeat = true;
            sinceBeat = 0;
        }
        return new BeatResult(isBeat, flux, lastThreshold);
    }

    public void Reset()
    {
        Array.Clear(previous);
        fluxHead = fluxCount = intervalHead = intervalCount = 0;
        hasPrevious = hasBeat = false;
        sinceBeat = lastThreshold = 0;
        EstimatedBpm = 0;
    }

    // Median of the newest `count` flux values, sorted in a preallocated scratch buffer.
    private float Median(int count)
    {
        if (count == 0) return 0;
        Span<float> values = sortedFlux.AsSpan(0, count);
        for (int i = 0; i < count; i++) values[i] = fluxValues[(fluxHead - 1 - i + MaxHistory) % MaxHistory];
        values.Sort();
        return count % 2 == 1 ? values[count / 2] : 0.5f * (values[count / 2 - 1] + values[count / 2]);
    }

    // Tempo from the median of recent beat intervals (doc 03 §4), without allocating.
    private void AddInterval(float seconds)
    {
        intervals[intervalHead] = seconds;
        intervalHead = (intervalHead + 1) % IntervalCount;
        intervalCount = Math.Min(intervalCount + 1, IntervalCount);
        if (intervalCount < 3) return;

        Span<float> sorted = scratch.AsSpan(0, intervalCount);
        intervals.AsSpan(0, intervalCount).CopyTo(sorted);
        sorted.Sort();
        float median = intervalCount % 2 == 1
            ? sorted[intervalCount / 2]
            : 0.5f * (sorted[intervalCount / 2 - 1] + sorted[intervalCount / 2]);
        EstimatedBpm = median > 0 ? 60f / median : 0;
    }
}
