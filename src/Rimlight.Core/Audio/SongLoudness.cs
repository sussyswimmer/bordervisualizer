namespace Rimlight.Core.Audio;

// Where this moment sits in the song's own dynamics (Maxwell's feedback, round 1: "a super big bounce when the person was
// whispering", and a chorus that should be brighter than the verse).
//
// Doc 03 §2's auto-gain judges each band against the last few seconds (peak half-life 4 s, floor +1 dB/s, 12 dB minimum
// range). After a chorus the peak falls 6 dB every 4 s, so a whispered verse 15 dB down is stretched over the full 0..1
// range again about 6 s later, and a verse ends up nearly as bright as the chorus. That stage stays as it is: it is the
// local contrast within a groove, which punchy music still uses (AudioAnalyzer). This class adds the slow, song-level
// reference beside it.
//
// It keeps a histogram of the momentary loudness (dB, 0.5 dB bins) that forgets with a time constant of MemorySeconds.
// Its ReferencePercentile is "the loud parts of what is playing": one loud hit can't move it, a quiet verse doesn't drag
// it down while the chorus is still in memory, and a song that stays quiet becomes its own reference as the old mass
// fades. Position is 1 at the reference (and above) and falls linearly to 0 RangeDb below it.
//
// Volume (doc 03 §2: the volume slider must not change the brightness): a volume change shifts every loudness by the
// same number of dB, so once the memory has turned over the position is exactly what it was, and the same song at any
// volume gives the same light. A change in the middle of a song takes longer than doc 03's 2 s to settle: the old mass
// must fall under a tenth of the memory, about 2.3 × MemorySeconds. Measured (MusicDynamicsTests): Intensity within 0.1
// 23.5 s after a 20 dB step down, and 6 s after a 20 dB step up, which only clamps the position at 1 meanwhile.
//
// Near-silence (the auto-gain hold level) adds nothing, so pauses don't count as quiet music. A gap of GapSeconds below
// the silence level, as between two songs, keeps only GapKeep of the memory, so a quieter next song is judged against
// itself after about 10 s instead of about 23 s.
//
// A song's opening is judged against itself, so with nothing else heard yet a quiet first verse would sit at the top.
// Until the memory fills, a prior says the loud parts are probably a little louder than anything heard so far: a
// virtual PriorSeconds of loudness PriorHeadroomDb above the loudest moment since the start (or since a gap), fading
// with the memory like real loudness. It decides the reference for about the first 15 s, rising with anything louder;
// music that starts loud and stays loud sits 4–6 dB under it for that time (Position about 0.75–0.85, where
// BeatCharacter still gives kicks their full size). All storage is allocated up front; Update never allocates.
internal sealed class SongLoudness
{
    internal const float MinDb = -100f;
    internal const float MaxDb = 20f;
    internal const float BinDb = 0.5f;
    internal const float MemorySeconds = 10f;           // a verse is judged against the last chorus for about 20 s
    internal const float ReferencePercentile = 0.9f;    // the loud parts, not the single loudest moment
    internal const float RangeDb = 24f;                 // a whisper 16 dB under the chorus sits at about a third
    internal const float MomentarySeconds = 0.25f;      // power smoothing: phrases and swells, not single syllables
    internal const float GapSeconds = 1f;
    internal const float GapKeep = 0.25f;
    internal const float PriorSeconds = 4f;
    internal const float PriorHeadroomDb = 4f;

    // Loudness weights per band (power): the sub-bass of an 808 or a kick shouldn't outweigh the voice and the
    // instruments, and the high band counts a little more, roughly as K-weighting (ITU-R BS.1770) does.
    internal const float BassWeight = 0.5f;
    internal const float MidWeight = 1f;
    internal const float HighWeight = 1.5f;

    private readonly float[] bins = new float[(int)((MaxDb - MinDb) / BinDb)];
    private float total, power, gapSeconds, priorMass, priorDb = MinDb;
    private bool hasPower, gapApplied, priorPending = true;

    // Momentary loudness in dB of weighted amplitude units (a full-scale sine reads about 0 dB; doc 03 flux units).
    public float LoudnessDb { get; private set; } = MinDb;

    // The song reference: ReferencePercentile of the remembered loudness, in the same dB. MinDb before any audio.
    public float ReferenceDb { get; private set; } = MinDb;

    // 0..1: 1 at or above the reference, 0 at RangeDb or more below it, and 0 while the window is below the silence
    // level (the momentary loudness takes over a second to fall 24 dB, and the music has stopped).
    public float Position { get; private set; }

    // weightedPower: BandAnalyzer.WeightedPower of the newest spectrum. audible: the window is above near-silence
    // (the auto-gain hold level). silent: it is below SilenceThresholdDb (a gap between songs, or a pause).
    public void Update(float weightedPower, bool audible, bool silent, float dtSeconds)
    {
        if (!float.IsFinite(weightedPower) || weightedPower < 0) weightedPower = 0;
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        power = hasPower ? power + (weightedPower - power) * (1 - MathF.Exp(-dt / MomentarySeconds)) : weightedPower;
        hasPower = true;
        LoudnessDb = Math.Clamp(10 * MathF.Log10(power + 1e-12f), MinDb, MaxDb - BinDb);

        if (audible)
        {
            gapSeconds = 0;
            gapApplied = false;
            if (priorPending)
            {
                priorMass = PriorSeconds;
                priorDb = MinDb;
                priorPending = false;
            }
            float keep = MathF.Exp(-dt / MemorySeconds);
            for (int i = 0; i < bins.Length; i++) bins[i] *= keep;
            total *= keep;
            priorMass *= keep;
            priorDb = MathF.Max(priorDb, MathF.Min(LoudnessDb + PriorHeadroomDb, MaxDb - BinDb));
            bins[Bin(LoudnessDb)] += dt;
            total += dt;
        }
        else if (silent)
        {
            gapSeconds += dt;
            if (gapSeconds >= GapSeconds && !gapApplied)
            {
                for (int i = 0; i < bins.Length; i++) bins[i] *= GapKeep;
                total *= GapKeep;
                gapApplied = true;
                priorPending = true;           // the next song gets the prior again
            }
        }

        ReferenceDb = total > 0 ? Percentile() : MinDb;
        Position = total > 0 && !silent ? Math.Clamp(1 - (ReferenceDb - LoudnessDb) / RangeDb, 0, 1) : 0;
    }

    public void Reset()
    {
        Array.Clear(bins);
        total = power = gapSeconds = priorMass = 0;
        priorDb = MinDb;
        hasPower = gapApplied = false;
        priorPending = true;
        LoudnessDb = ReferenceDb = MinDb;
        Position = 0;
    }

    // Bin i holds loudness within half a bin of MinDb + i × BinDb, so a steady loudness reads back within a quarter of
    // a bin (0.125 dB).
    private static int Bin(float db) => (int)MathF.Round((db - MinDb) / BinDb);

    // The loudness that (1 − ReferencePercentile) of the remembered time, prior included, lies above, interpolated
    // within its bin. The prior is a point mass at priorDb.
    private float Percentile()
    {
        float above = (1 - ReferencePercentile) * (total + priorMass), sum = 0;
        int priorBin = Bin(priorDb);
        for (int i = bins.Length - 1; i >= 0; i--)
        {
            if (i == priorBin && priorMass > 0)
            {
                if (sum + priorMass >= above) return priorDb;
                sum += priorMass;
            }
            float next = sum + bins[i];
            if (next >= above && bins[i] > 0)
                return MinDb + (i + 0.5f - (above - sum) / bins[i]) * BinDb;
            sum = next;
        }
        return MinDb;
    }
}
