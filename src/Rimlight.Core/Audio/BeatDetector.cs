namespace Rimlight.Core.Audio;

internal readonly record struct BeatResult(bool IsBeat, float Flux, float Threshold);

// Spectral-flux beat detection on the bass band (doc 03 §2). All storage is allocated up front.
// Flux is measured in amplitude units: magnitudes are scaled by 4/N, so a full-scale sine centered
// on a bin reads 1.0 (Hann coherent gain 0.5 × one-sided N/2).
//
// Spec clarification: doc 03's minFlux exists "to avoid noise during silence", and an absolute floor also drops
// every beat of quiet playback (−40 dB in-app volume puts kick flux below 0.01), against PRD's rule that volume must
// not change the light. So MinFlux is relative to the level: a beat needs flux > MinFlux × the window's RMS, and no
// beat fires while that RMS is at or below the near-silence level (SilenceThresholdDb − 20 dB, as for auto-gain).
internal sealed class BeatDetector
{
    // Spec clarification (doc 03 leaves this open): with only mean + k·stddev, stationary noise crosses the
    // threshold on its statistical tail a few times a second (the bass band spans only ~5 bins, so its flux is
    // heavy-tailed), while doc 03 §5 requires no beats on constant white noise. A beat must also stand out from
    // the recent *median* flux: flux > median × MedianOnsetRatio. Kicks and note onsets sit far above the median
    // (×16 under heavy noise, ×hundreds in music) because they are sparse, while white noise peaks around ×7.4
    // over 5 minutes.
    internal const float MedianOnsetRatio = 10f;

    // The adaptive threshold needs some history before it means anything (a quarter of FluxHistorySeconds).
    private const float WarmUpFraction = 0.25f;

    private const int MaxHistory = 1024;         // per-step flux values; ≥ 4.2 s, as steps are ≥ 1/240 s (AudioAnalyzer)
    private const int IntervalCount = 16;        // beat intervals kept for the BPM median
    private const float MaxIntervalSeconds = 2f; // longer gaps (< 30 BPM) are not tempo
    private const float StaleBpmSeconds = 4f;    // no beat for this long: the tempo is unknown again

    private readonly float[] previous;
    private readonly float[] fluxValues = new float[MaxHistory];
    private readonly float[] fluxDurations = new float[MaxHistory];
    private readonly float[] selection = new float[MaxHistory];
    private readonly float[] intervals = new float[IntervalCount];
    private readonly float[] sortedIntervals = new float[IntervalCount];
    private int fluxHead, fluxCount, intervalHead, intervalCount, seededStart, seededEnd;
    private bool hasBeat;
    private float sinceBeat, sinceFlux, lastThreshold;

    public BeatDetector(int spectrumLength) => previous = new float[spectrumLength];

    public float EstimatedBpm { get; private set; }

    // Sensitivity relaxes both beat criteria. Doc 03 says it "scales the multiplier 1.5 inversely"; a plain 1/s makes
    // k = 6 at Sensitivity 0.25, which is above typical kick flux once kicks are 5–7 % of frames, i.e. beats off.
    // 1/√s keeps the same direction with endpoints that still work: k = 3 at 0.25, 1.06 at 2.
    internal static float SensitivityScale(float sensitivity) => 1 / MathF.Sqrt(sensitivity);

    // spectrumChanged: false when no new samples arrived; such frames carry no flux information, but their time
    // still counts toward the history window, the refractory period and BPM staleness.
    // windowRms: time-domain RMS of the window the magnitudes come from.
    public BeatResult Update(ReadOnlySpan<float> magnitudes, bool spectrumChanged, int windowSize, int sampleRate, float dtSeconds, AudioTuning tuning, float windowRms)
    {
        sinceBeat += dtSeconds;
        sinceFlux += dtSeconds;
        if (hasBeat && sinceBeat > StaleBpmSeconds)
        {
            intervalHead = intervalCount = 0;
            EstimatedBpm = 0;
        }
        if (!spectrumChanged) return new BeatResult(false, 0, lastThreshold);

        (int start, int end) = BandAnalyzer.BassBins(magnitudes.Length, (float)sampleRate / windowSize, tuning);
        float scale = 4f / windowSize;
        float flux = 0;
        for (int k = start; k < end; k++)
        {
            float magnitude = magnitudes[k] * scale;
            // Bins that just entered the band (first frame, or live-tuned band edges) only seed their history:
            // their whole magnitude is not an onset.
            if (k >= seededStart && k < seededEnd) flux += MathF.Max(0, magnitude - previous[k]);
            previous[k] = magnitude;
        }
        bool seeded = seededEnd > seededStart;
        seededStart = start;
        seededEnd = end;
        // A seed-only update adds no history entry; its time goes to the next one.
        if (!seeded) return new BeatResult(false, 0, lastThreshold);
        float entryDuration = sinceFlux;
        sinceFlux = 0;

        // Statistics over the flux history covering the last FluxHistorySeconds (current step excluded).
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

        float relax = SensitivityScale(tuning.Sensitivity);
        float mean = used > 0 ? sum / used : 0;
        float stdDev = used > 1 ? MathF.Sqrt(MathF.Max(0, sumSquares / used - mean * mean)) : 0;
        // The effective threshold, as the debug visualizer plots it: the largest of doc 03's mean + k·σ, the median
        // onset guard and the level-relative MinFlux. Quickselect keeps the median cheap (≈ 60 values per step).
        lastThreshold = MathF.Max(
            MathF.Max(mean + tuning.FluxThresholdMultiplier * relax * stdDev, Median(used) * (1 + (MedianOnsetRatio - 1) * relax)),
            tuning.MinFlux * windowRms);

        bool warm = covered >= tuning.FluxHistorySeconds * WarmUpFraction || fluxCount == MaxHistory;
        bool isBeat = warm
            && flux > lastThreshold
            && windowRms > AudioFrontEnd.HoldRms(tuning)
            && (!hasBeat || sinceBeat >= tuning.BeatRefractorySeconds);

        fluxValues[fluxHead] = flux;
        fluxDurations[fluxHead] = entryDuration;
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

    // Real time covered by the stored flux history, for tests.
    internal float StoredSeconds
    {
        get
        {
            float seconds = 0;
            for (int i = 0; i < fluxCount; i++) seconds += fluxDurations[(fluxHead - 1 - i + MaxHistory) % MaxHistory];
            return seconds;
        }
    }

    // After a render stall: the next spectrum only seeds the comparison and the flux statistics warm up again.
    // The tempo estimate is kept (it goes stale on its own).
    public void Restart()
    {
        fluxHead = fluxCount = seededStart = seededEnd = 0;
        sinceFlux = 0;
    }

    public void Reset()
    {
        Array.Clear(previous);
        fluxHead = fluxCount = intervalHead = intervalCount = seededStart = seededEnd = 0;
        hasBeat = false;
        sinceBeat = sinceFlux = lastThreshold = 0;
        EstimatedBpm = 0;
    }

    // Median of the newest `count` flux values by quickselect in a preallocated buffer (no sort, no allocation).
    private float Median(int count)
    {
        if (count == 0) return 0;
        Span<float> values = selection.AsSpan(0, count);
        for (int i = 0; i < count; i++) values[i] = fluxValues[(fluxHead - 1 - i + MaxHistory) % MaxHistory];
        float upper = Select(values, count / 2);
        if (count % 2 == 1) return upper;
        // After selection everything left of count/2 is ≤ upper; the lower middle is the largest of those.
        float lower = values[0];
        for (int i = 1; i < count / 2; i++) lower = MathF.Max(lower, values[i]);
        return 0.5f * (lower + upper);
    }

    // Hoare quickselect: afterwards values[k] is the k-th smallest and everything before it is ≤ it.
    internal static float Select(Span<float> values, int k)
    {
        int left = 0, right = values.Length - 1;
        while (left < right)
        {
            float pivot = MedianOfThree(values[left], values[(left + right) / 2], values[right]);
            int i = left, j = right;
            while (i <= j)
            {
                while (values[i] < pivot) i++;
                while (values[j] > pivot) j--;
                if (i <= j)
                {
                    (values[i], values[j]) = (values[j], values[i]);
                    i++;
                    j--;
                }
            }
            if (k <= j) right = j;
            else if (k >= i) left = i;
            else break;
        }
        return values[k];
    }

    private static float MedianOfThree(float a, float b, float c) => MathF.Max(MathF.Min(a, b), MathF.Min(MathF.Max(a, b), c));

    // Tempo from the median of recent beat intervals (doc 03 §4), without allocating.
    private void AddInterval(float seconds)
    {
        intervals[intervalHead] = seconds;
        intervalHead = (intervalHead + 1) % IntervalCount;
        intervalCount = Math.Min(intervalCount + 1, IntervalCount);
        if (intervalCount < 3) return;

        // Insertion sort of at most 16 values.
        Span<float> sorted = sortedIntervals.AsSpan(0, intervalCount);
        for (int i = 0; i < intervalCount; i++)
        {
            float value = intervals[i];
            int j = i - 1;
            for (; j >= 0 && sorted[j] > value; j--) sorted[j + 1] = sorted[j];
            sorted[j + 1] = value;
        }
        float median = intervalCount % 2 == 1
            ? sorted[intervalCount / 2]
            : 0.5f * (sorted[intervalCount / 2 - 1] + sorted[intervalCount / 2]);
        EstimatedBpm = median > 0 ? 60f / median : 0;
    }
}
