namespace Rimlight.Core.Audio;

// KickLevel: the summed magnitude of the fixed kick band (BandAnalyzer.KickBins, 30–100 Hz) in the newest spectrum, in the
// same units, for BeatCharacter (the latest one on steps without a new spectrum). It doesn't take part in detection, and
// live-tuning the beat band doesn't move it. Threshold: the one this step was held to (the looser one when the evidence
// of a kick allowed it), as the debug visualizer plots it. Strict: the flux also passed doc 03's own threshold, with the
// full median guard (a beat that needed the looser one is trusted only in music: BeatCharacter).
internal readonly record struct BeatResult(bool IsBeat, float Flux, float Threshold, float KickLevel = 0, bool Strict = false);

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

    // Maxwell's feedback, round 2: on real rap and EDM the detector fired on only 69–72 % of the kicks (a corpus of real
    // recordings with independently annotated kicks). 11–38 % stayed under the threshold at 60–90 % of it, and there
    // the median guard set it: an 808's sustain, the bass and the rapper's voice all move the 30–150 Hz flux, so ten times
    // its median is above the kicks. So, an internal exception to doc 03's rule (its defaults, the contract's, are
    // unchanged): with time-domain evidence of a kick (KickEnvelope: the kick band rose by at least KickEvidenceDb next
    // to the music above it) in music (OnsetContrast at least MusicContrast), the median guard is LooseOnsetRatio
    // instead. Measured on the corpus: kicks the detector fires on, rap 72 → 83 %, EDM 69 → 86 %.
    //
    // The evidence alone is not enough (the round 2 review): in brown noise, an ocean or rain recording the 30–100 Hz band
    // carries most of the power, its 4 ms envelope swings 10 dB and more by itself, and the evidence passed on 96 % of the
    // steps, so the looser guard fired 12–71 beats a minute on noise. What noise lacks is onsets: the music above the bass
    // band of a steady noise changes by the same small share from step to step, while notes, words and hits make that
    // share jump. OnsetContrast measures it (the coefficient of variation of the upper band's relative flux over
    // ContrastSeconds): at most 0.19 on white, pink, brown and swelling brown noise and a 50 Hz hum at −32 to −12 dBFS,
    // while on the corpus's recordings it stays at 0.23 or more 93–100 % of the time from 10 s in (its 1st percentile
    // 0.21 or more). So MusicContrast 0.23 closes the looser guard on noise and leaves the music's kicks as before.
    //
    // BeatRefractorySeconds is the contract's minimum interval between beats, and the tuned value holds (round 2 had let
    // a beat fire again inside it for a double kick or a kick after a weaker onset: on the corpus 218 full pulses came
    // within 150 ms of another, about 180 of them not on two kicks, and a tuned Beat spacing of 0.4 s still let beats
    // 51 ms apart through). Without those exceptions 4 % fewer rap and EDM kicks fire a beat (rap 88 → 83 %, EDM 89 →
    // 86 %); for 37 % of those a beat fired 20–60 ms before the kick, which is on time to the eye.
    internal const float LooseOnsetRatio = 5f;
    internal const float KickEvidenceDb = -4f;
    internal const float MusicContrast = 0.23f;
    internal const float ContrastSeconds = 4f;

    // The adaptive threshold needs some history before it means anything (a quarter of FluxHistorySeconds).
    private const float WarmUpFraction = 0.25f;

    private const int MaxHistory = 1024;         // per-step flux values; ≥ 4.2 s, as steps are ≥ 1/240 s (AudioAnalyzer)
    private const int IntervalCount = 16;        // beat intervals kept for the tempo estimate
    private const float MaxIntervalSeconds = 2f; // longer gaps (< 30 BPM) are not tempo
    private const float InlierTolerance = 0.25f; // intervals this far from the median (a share of it) are not tempo
    private const float StaleBpmSeconds = 4f;    // no tempo beat for this long: the tempo is unknown again

    private readonly float[] previous;
    private readonly float[] previousUpper;
    private readonly float[] fluxValues = new float[MaxHistory];
    private readonly float[] fluxDurations = new float[MaxHistory];
    private readonly float[] selection = new float[MaxHistory];
    private readonly float[] intervals = new float[IntervalCount];
    private readonly float[] sortedIntervals = new float[IntervalCount];
    private int fluxHead, fluxCount, intervalHead, intervalCount, seededStart, seededEnd;
    private bool hasBeat, hasTempoBeat, hasContrast, upperSeeded, wasPartlySilent = true;
    private float sinceBeat, sinceTempoBeat, sinceFlux, lastThreshold, kickLevel, contrastMean, contrastSquares;

    // How much the music above the bass band changes from step to step (above): 0 until a step has measured it.
    public float OnsetContrast { get; private set; }

    // What plays has onsets: music, not a steady noise.
    public bool IsMusic => OnsetContrast >= MusicContrast;

    public BeatDetector(int spectrumLength)
    {
        previous = new float[spectrumLength];
        previousUpper = new float[spectrumLength];
    }

    public float EstimatedBpm { get; private set; }

    // Sensitivity relaxes both beat criteria. Doc 03 says it "scales the multiplier 1.5 inversely"; a plain 1/s makes
    // k = 6 at Sensitivity 0.25, which is above typical kick flux once kicks are 5–7 % of frames, i.e. beats off.
    // 1/√s keeps the same direction with endpoints that still work: k = 3 at 0.25, 1.06 at 2.
    internal static float SensitivityScale(float sensitivity) => 1 / MathF.Sqrt(sensitivity);

    // spectrumChanged: false when no new samples arrived; such frames carry no flux information, but their time
    // still counts toward the history window, the refractory period and BPM staleness.
    // windowRms: time-domain RMS of the window the magnitudes come from.
    // kickEvidenceDb: KickEnvelope's prominence at this step (−∞: none). partlySilent: some quarter of the window is
    // near-silent (AudioFrontEnd.WindowPartlySilent): the music is starting or stopping, which OnsetContrast ignores.
    public BeatResult Update(ReadOnlySpan<float> magnitudes, bool spectrumChanged, int windowSize, int sampleRate, float dtSeconds, AudioTuning tuning, float windowRms,
        float kickEvidenceDb = float.NegativeInfinity, bool partlySilent = false)
    {
        sinceBeat += dtSeconds;
        sinceTempoBeat += dtSeconds;
        sinceFlux += dtSeconds;
        if (hasTempoBeat && sinceTempoBeat > StaleBpmSeconds)
        {
            intervalHead = intervalCount = 0;
            EstimatedBpm = 0;
        }
        if (!spectrumChanged) return new BeatResult(false, 0, lastThreshold, kickLevel);

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
        (int kickStart, int kickEnd) = BandAnalyzer.KickBins(magnitudes.Length, (float)sampleRate / windowSize);
        kickLevel = 0;
        for (int k = kickStart; k < kickEnd; k++) kickLevel += magnitudes[k] * scale;
        // The music above the bass band: its relative flux (positive changes over the total), for OnsetContrast.
        (int upperStart, int upperEnd) = BandAnalyzer.UpperBins(magnitudes.Length, (float)sampleRate / windowSize);
        float upperFlux = 0, upperSum = 0;
        for (int k = upperStart; k < upperEnd; k++)
        {
            float magnitude = magnitudes[k] * scale;
            upperFlux += MathF.Max(0, magnitude - previousUpper[k]);
            upperSum += magnitude;
            previousUpper[k] = magnitude;
        }
        bool upperWasSeeded = upperSeeded;
        upperSeeded = true;
        bool seeded = seededEnd > seededStart;
        seededStart = start;
        seededEnd = end;
        // A seed-only update adds no history entry; its time goes to the next one.
        if (!seeded) return new BeatResult(false, 0, lastThreshold, kickLevel);
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
        float spread = mean + tuning.FluxThresholdMultiplier * relax * stdDev, median = Median(used), floor = tuning.MinFlux * windowRms;
        lastThreshold = MathF.Max(MathF.Max(spread, median * (1 + (MedianOnsetRatio - 1) * relax)), floor);
        // The threshold for a step with evidence of a kick (above): the same with the lower median guard.
        float loose = MathF.Max(MathF.Max(spread, median * (1 + (LooseOnsetRatio - 1) * relax)), floor);

        bool warm = covered >= tuning.FluxHistorySeconds * WarmUpFraction || fluxCount == MaxHistory;
        // The window filling with music after a gap or a stall is no onset of the music itself, and neither is the step
        // right after (its comparison spectrum still had the silence in it).
        UpdateContrast(warm && upperWasSeeded && !partlySilent && !wasPartlySilent && upperSum > 1e-9f ? upperFlux / upperSum : -1, entryDuration);
        wasPartlySilent = partlySilent;
        bool looseOpen = kickEvidenceDb >= KickEvidenceDb && IsMusic;
        float applied = looseOpen ? loose : lastThreshold;
        bool isBeat = warm && windowRms > AudioFrontEnd.HoldRms(tuning) && (!hasBeat || sinceBeat >= tuning.BeatRefractorySeconds) && flux > applied;

        fluxValues[fluxHead] = flux;
        fluxDurations[fluxHead] = entryDuration;
        fluxHead = (fluxHead + 1) % MaxHistory;
        fluxCount = Math.Min(fluxCount + 1, MaxHistory);

        if (isBeat)
        {
            hasBeat = true;
            sinceBeat = 0;
        }
        return new BeatResult(isBeat, flux, applied, kickLevel, flux > lastThreshold);
    }

    // A tempo beat, secondsAgo before the end of the latest Update (AudioAnalyzer decides which beats count: Maxwell's
    // feedback, round 1). Four-on-the-floor EDM often puts a bass note on every off-beat; it fires like the kick, and with
    // it every interval would be half a beat (about 250 BPM for 128). Where kicks play, only the kick-like, accented ones
    // count (BeatCharacter.TempoBeatAge); elsewhere every beat counts, as in doc 03.
    public void AddTempoBeat(float secondsAgo)
    {
        float since = sinceTempoBeat - secondsAgo;
        if (hasTempoBeat && since > 0 && since <= MaxIntervalSeconds) AddInterval(since);
        hasTempoBeat = true;
        sinceTempoBeat = MathF.Max(0, secondsAgo);
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
        upperSeeded = false;
    }

    // One step's relative flux of the music above the bass band into OnsetContrast's running mean and mean square, over
    // ContrastSeconds of real time (negative: a step that doesn't count, which holds them).
    private void UpdateContrast(float relative, float seconds)
    {
        if (relative < 0) return;
        if (!hasContrast)
        {
            contrastMean = relative;
            contrastSquares = relative * relative;
            hasContrast = true;
        }
        else
        {
            float a = 1 - MathF.Exp(-seconds / ContrastSeconds);
            contrastMean += (relative - contrastMean) * a;
            contrastSquares += (relative * relative - contrastSquares) * a;
        }
        OnsetContrast = contrastMean > 1e-9f ? MathF.Sqrt(MathF.Max(0, contrastSquares - contrastMean * contrastMean)) / contrastMean : 0;
    }

    public void Reset()
    {
        Array.Clear(previous);
        Array.Clear(previousUpper);
        upperSeeded = false;
        wasPartlySilent = true;
        fluxHead = fluxCount = intervalHead = intervalCount = seededStart = seededEnd = 0;
        hasBeat = hasTempoBeat = hasContrast = false;
        sinceBeat = sinceTempoBeat = sinceFlux = lastThreshold = kickLevel = contrastMean = contrastSquares = OnsetContrast = 0;
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

    // Tempo from recent beat intervals (doc 03 §4), without allocating. The median finds the typical interval, so
    // missed beats (double intervals) and extra ones (short intervals) drop out; the tempo is then the mean of the
    // intervals within InlierTolerance of it. Spec clarification (H-016): beats are only seen at analysis steps, so at
    // a steady frame rate every interval is a whole number of steps (28 or 29 at 60 fps for 126 BPM), and a median
    // alone picks one of the two neighbours (124.1 BPM; 171.4 for 174 BPM, outside doc 03 §5's ±2). The mean over 16
    // intervals is off by at most one step in 16.
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
        float sum = 0;
        int inliers = 0;
        foreach (float value in sorted)
        {
            if (MathF.Abs(value - median) > InlierTolerance * median) continue;
            sum += value;
            inliers++;
        }
        // An even count split far apart (alternate beats missed) can leave no interval near the median: keep the median.
        float tempoInterval = inliers > 0 ? sum / inliers : median;
        EstimatedBpm = tempoInterval > 0 ? 60f / tempoInterval : 0;
    }
}
