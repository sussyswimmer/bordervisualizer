namespace Rimlight.Core.Audio;

// Where this moment sits in the song's own dynamics (Maxwell's feedback, round 1: "a super big bounce when the person was
// whispering", and a chorus that should be brighter than the verse).
//
// Doc 03 §2's auto-gain judges each band against the last few seconds (peak half-life 4 s, floor +1 dB/s, 12 dB minimum
// range). After a chorus the peak falls 6 dB every 4 s, so a whispered verse 15 dB down is stretched over the full 0..1
// range again about 6 s later, and a verse ends up nearly as bright as the chorus. That stage stays as it is: it is the
// local contrast within a groove (AudioAnalyzer). This class adds the slow, song-level reference beside it.
//
// It keeps a histogram of the momentary loudness (dB, 0.5 dB bins) that forgets with a time constant of MemorySeconds.
// Its ReferencePercentile is "the loud parts of what is playing": one loud hit can't move it, a quiet verse doesn't drag
// it down while the chorus is still in memory, and a song that stays quiet becomes its own reference as the old mass
// fades. Position is 1 at the reference (and above) and falls linearly to 0 RangeDb below it.
//
// A song's opening is judged against itself, so with nothing else heard yet a quiet first verse would sit at the top.
// Until the memory fills, a prior says the loud parts are probably a little louder than anything heard so far: a
// virtual PriorSeconds of loudness PriorHeadroomDb above the loudest moment since the song started, fading with the
// memory like real loudness. It decides the reference for about the first 15 s, rising with anything louder.
//
// Songs and the listening session. GapSeconds of near-silence (the auto-gain hold level, −80 dBFS by default: the
// digital silence between two songs, or no packets at all) clears the memory and re-arms the prior: a song is judged
// against itself, not against what played before it. The session reference follows the song reference with a time
// constant of SessionSeconds once the prior is under SessionPriorShare of the song's memory (about 16 s into a song),
// and it survives gaps. After a gap it is a floor under the reference that starts there and falls
// SessionFallDbPerSecond while the song plays, so the opening of the next song is judged against the songs before it:
// a quiet first verse under the last song's loud parts stays dimmer and bounces less (the folk band after itself: a mean
// pulse of 0.57 in its first verse, against 0.82 as the first song). A quieter master is brought up as gradually,
// because the two can't be told apart: on the StyleTracks rap after itself and a 3 s gap, 10 dB down it has its full
// bounce from 5 s and its brightness from about 25 s; 14 dB down its bounce returns over about 25 s and its brightness
// over about 40 s. With the loudness normalization most streaming apps apply, songs play within a few dB of each other,
// where the floor only helps. The first song after the app starts has no session.
//
// Volume (doc 03 §2: the volume slider must not change the brightness): a volume change shifts every loudness by the
// same number of dB, so once the memory has turned over the position is exactly what it was, and the same song at any
// volume gives the same light. A change in the middle of a song can't be told from the music getting quieter or louder:
// a volume step moves every band by the same dB, but within a second a song's own band balance moves by 2–7 dB (measured
// on the StyleTracks), so no test on the spectrum separates the two. The old loudness has to fall under a tenth of the
// memory, about 2.3 × MemorySeconds. AudioAnalyzer keeps Level at the top over the top fifth of the position, which
// hides steps of up to about 5 dB, and BeatCharacter keeps a groove's bounce; MusicDynamicsTests measures the rest.
//
// Every window above near-silence counts as music, whatever its level, so the whole chain stays scale-invariant at quiet
// playback (a whispered verse at −35 dB volume dips under the −60 dBFS IsSilent level between phrases). Near-silence adds
// nothing to the memory and doesn't move its clock; the position is 0 there. Hiss or room tone between two songs (above
// −80 dBFS) is not a gap: the next song is then judged against the last one for about 20 s more. All storage is
// allocated up front; Update never allocates.
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
    internal const float PriorSeconds = 4f;
    internal const float PriorHeadroomDb = 4f;
    internal const float SessionSeconds = 30f;
    internal const float SessionPriorShare = 0.1f;
    internal const float SessionFallDbPerSecond = 0.2f;
    private const float Tiny = 1e-6f;                   // remembered seconds under this are flushed to 0

    // Loudness weights per band (power): the sub-bass of an 808 or a kick shouldn't outweigh the voice and the
    // instruments, and the high band counts a little more, roughly as K-weighting (ITU-R BS.1770) does.
    internal const float BassWeight = 0.5f;
    internal const float MidWeight = 1f;
    internal const float HighWeight = 1.5f;

    private readonly float[] bins = new float[(int)((MaxDb - MinDb) / BinDb)];
    private float total, power, gapSeconds, priorMass, priorDb = MinDb, sessionDb = MinDb, floorDb = MinDb;
    private bool hasPower, hasSession, gapApplied, priorPending = true;

    // Momentary loudness in dB of weighted amplitude units (a full-scale sine reads about 0 dB; doc 03 flux units).
    public float LoudnessDb { get; private set; } = MinDb;

    // The song reference: ReferencePercentile of the song's remembered loudness and its prior, in the same dB. MinDb
    // before any audio.
    public float SongReferenceDb { get; private set; } = MinDb;

    // What Position is judged against: the song reference, or the falling session floor after a gap where higher.
    public float ReferenceDb { get; private set; } = MinDb;

    // The session reference (MinDb until a song has established one).
    public float SessionDb => hasSession ? sessionDb : MinDb;

    // For tests: the remembered seconds per bin.
    internal ReadOnlySpan<float> Bins => bins;

    // 0..1: 1 at or above ReferenceDb, 0 at RangeDb or more below it, and 0 while the window is near-silent.
    public float Position { get; private set; }

    // weightedPower: BandAnalyzer.WeightedPower of the newest spectrum. audible: the window is above near-silence (the
    // auto-gain hold level).
    public void Update(float weightedPower, bool audible, float dtSeconds)
    {
        if (!float.IsFinite(weightedPower) || weightedPower < 0) weightedPower = 0;
        float dt = float.IsFinite(dtSeconds) && dtSeconds > 0 ? dtSeconds : 0;
        power = hasPower ? power + (weightedPower - power) * (1 - MathF.Exp(-dt / MomentarySeconds)) : weightedPower;
        if (power < 1e-12f) power = 0;
        hasPower = true;
        LoudnessDb = Math.Clamp(10 * MathF.Log10(power + 1e-12f), MinDb, MaxDb - BinDb);

        if (!audible)
        {
            gapSeconds = MathF.Min(gapSeconds + dt, GapSeconds);
            if (gapSeconds >= GapSeconds && !gapApplied)
            {
                // Between songs: the next one is judged against itself, and its opening against the session.
                Array.Clear(bins);
                total = priorMass = 0;
                priorPending = true;
                floorDb = hasSession ? sessionDb : MinDb;
                gapApplied = true;
            }
        }
        else
        {
            gapSeconds = 0;
            gapApplied = false;
            Remember(dt);
        }

        SongReferenceDb = total + priorMass > 0 ? Percentile() : MinDb;
        ReferenceDb = MathF.Max(SongReferenceDb, floorDb);
        if (audible && total > 0 && priorMass <= SessionPriorShare * total)
        {
            sessionDb = hasSession ? sessionDb + (SongReferenceDb - sessionDb) * (1 - MathF.Exp(-dt / SessionSeconds)) : SongReferenceDb;
            hasSession = true;
        }
        Position = audible && ReferenceDb > MinDb ? Math.Clamp(1 - (ReferenceDb - LoudnessDb) / RangeDb, 0, 1) : 0;
    }

    public void Reset()
    {
        Array.Clear(bins);
        total = power = gapSeconds = priorMass = 0;
        priorDb = sessionDb = floorDb = MinDb;
        hasPower = hasSession = gapApplied = false;
        priorPending = true;
        LoudnessDb = SongReferenceDb = ReferenceDb = MinDb;
        Position = 0;
    }

    private void Remember(float dt)
    {
        if (priorPending)
        {
            priorMass = PriorSeconds;
            priorDb = MinDb;
            priorPending = false;
        }
        float keep = MathF.Exp(-dt / MemorySeconds);
        for (int i = 0; i < bins.Length; i++)
        {
            float b = bins[i] * keep;
            bins[i] = b < Tiny ? 0 : b;
        }
        total *= keep;
        priorMass *= keep;
        if (priorMass < Tiny) priorMass = 0;
        priorDb = MathF.Max(priorDb, MathF.Min(LoudnessDb + PriorHeadroomDb, MaxDb - BinDb));
        floorDb = MathF.Max(MinDb, floorDb - SessionFallDbPerSecond * dt);
        bins[Bin(LoudnessDb)] += dt;
        total += dt;
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
