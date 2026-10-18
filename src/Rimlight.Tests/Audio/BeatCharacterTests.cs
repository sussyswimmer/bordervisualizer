using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

// Maxwell's feedback, rounds 1 and 2: each beat's grade (decided on the step it fires, corrected once a frame later), the
// groove behind it (strong and weak confirmations, the hold and the memory), the moment's loudness, and the tempo beats.
// Fed directly, one 10 ms step per frame, with the music above the bass band at amplitude 1 (so an onset's FFT rise is
// its prominence) and the kick band at Floor between onsets. KickEnvelope's time-domain evidence at the step a beat fires
// is passed in. Expected values are the documented constants written out.
public sealed class BeatCharacterTests
{
    private const float Step = 0.01f;
    private const float Floor = 1f;      // the kick band between onsets: a bass line as loud as the music above it
    private static readonly AudioTuning Tuning = new();

    // KickEnvelope's reading of an unmistakable kick as it fires: well above the music, from nothing, at amplitude 1.
    private static readonly KickEvidence Clear = new(0, 30, 1);

    private static float S(float from, float to, float x) => BeatCharacter.Smoothstep(from, to, x);

    private static float Amplitude(float db) => MathF.Pow(10, db / 20);

    // One frame of one step.
    private static void Frame(BeatCharacter c, bool fired, float kick, float position = 0.9f, KickEvidence? evidence = null,
        AudioTuning? tuning = null, bool audible = true)
    {
        c.BeginFrame();
        c.Update(fired, kick, 1, audible, position, evidence ?? KickEvidence.None, Step, Step, tuning ?? Tuning);
        c.EndFrame(Step, tuning ?? Tuning);
    }

    private static void Wait(BeatCharacter c, float seconds, float position = 0.9f, float floor = Floor)
    {
        for (int i = 0; i < (int)MathF.Round(seconds / Step); i++) Frame(c, false, floor, position);
    }

    // A character that has heard 1.5 s of music: past the warm-up.
    private static BeatCharacter Warm(float floor = Floor)
    {
        var c = new BeatCharacter();
        Wait(c, 1.5f, floor: floor);
        return c;
    }

    // An onset whose FFT kick band rises prominenceDb over the music (half of it on the step that fires, all of it over the
    // next five steps, then back to the floor), with KickEnvelope's evidence as it fires. Returns the pulse on the frame it
    // fired and the highest pulse over its frames.
    private static (float AtFire, float Peak) Onset(BeatCharacter c, float prominenceDb, KickEvidence evidence, float position = 0.9f,
        float floor = Floor, AudioTuning? tuning = null)
    {
        float rise = Amplitude(prominenceDb);
        Frame(c, true, floor + rise / 2, position, evidence, tuning);
        float atFire = c.Beat, peak = c.Beat;
        for (int i = 0; i < 5; i++)
        {
            Frame(c, false, floor + rise, position, null, tuning);
            peak = MathF.Max(peak, c.Beat);
        }
        Frame(c, false, floor, position, null, tuning);
        return (atFire, peak);
    }

    // A groove: kicks 0.5 s apart, 12 dB over the music, unmistakable as they fire.
    private static void Groove(BeatCharacter c, int kicks = 6, float position = 0.9f, float floor = Floor)
    {
        for (int i = 0; i < kicks; i++)
        {
            Onset(c, 12, Clear, position, floor);
            Wait(c, 0.43f, position, floor);
        }
    }

    [Fact]
    public void TheFirstSecondOfMusicOnlyListens()
    {
        // With no music heard yet there is nothing to measure an onset against: no grade, no confirmation.
        var c = new BeatCharacter();
        Wait(c, 0.5f);
        Assert.Equal((0f, 0f), Onset(c, 20, Clear));
        Assert.Equal(0, c.LastKickLike);
        Assert.Equal(0, c.Drive);
        Wait(c, 0.5f);
        Assert.Equal((0f, 1f), Onset(c, 20, Clear));      // unmistakable: it grades itself a frame late (below)
        Assert.Equal(1, c.LastKickLike);
    }

    [Theory]
    [InlineData(0f, 0f)]          // a note that adds as much as the music above the bass holds
    [InlineData(4.5f, 0f)]        // KickFromDb
    [InlineData(7.5f, 0.5f)]      // halfway
    [InlineData(10.5f, 1f)]       // KickAtDb
    [InlineData(20f, 1f)]
    public void KickLikenessIsTheKickBandsRiseOverTheMusicAboveIt(float db, float expected)
    {
        var c = Warm();
        Onset(c, db, Clear);
        Assert.Equal(expected, c.LastKickLike, 3);
        Assert.Equal(S(BeatCharacter.KickFromDb, BeatCharacter.KickAtDb, db), c.LastKickLike, 3);
    }

    [Fact]
    public void WithoutAGrooveOnlyAnUnmistakableKickPulsesAndTwoKicksMakeAGroove()
    {
        // A prominent onset under InstantFromDb in a wave: no pulse, a third of a strong confirmation.
        var c = Warm();
        Assert.Equal((0f, 0f), Onset(c, 7, Clear));
        float strong = S(BeatCharacter.KickFromDb, BeatCharacter.KickAtDb, 7);
        Assert.InRange(c.Drive, 0.99f * strong, strong);                          // fading since (RateSeconds)

        // Kicks 0.5 s apart, 12 dB over the music. Without a groove the first two grade themselves a frame late, from the
        // FFT's view of them (InstantFromDb..InstantAtDb: 0.74 at 12 dB): the first kick of a drop isn't lost. Two strong
        // confirmations within a second pass GrooveAt, so the third lands fully on the step it fires.
        var d = Warm();
        float instant = S(BeatCharacter.InstantFromDb, BeatCharacter.InstantAtDb, 12);
        var first = Onset(d, 12, Clear);
        Assert.Equal(0, first.AtFire);
        Assert.Equal(instant, first.Peak, 3);
        Wait(d, 0.43f);
        var second = Onset(d, 12, Clear);
        Assert.InRange(second.AtFire, 0, 0.02f);                                    // only the first one's pulse, decayed
        Assert.Equal(instant, second.Peak, 3);
        Assert.Equal(1, d.Punch);
        Wait(d, 0.43f);
        Assert.Equal((1f, 1f), Onset(d, 12, Clear));

        // At InstantAtDb a lone kick is full, a frame late.
        Assert.Equal((0f, 1f), Onset(Warm(), BeatCharacter.InstantAtDb, Clear));
    }

    [Theory]
    [InlineData(-10f, 0f, 0f)]        // EvidenceFromDb
    [InlineData(-7.5f, 0f, 0.5f)]     // halfway to EvidenceAtDb
    [InlineData(-5f, 0f, 1f)]         // EvidenceAtDb
    [InlineData(-20f, 16f, 0.5f)]     // halfway from RiseFromDb to RiseAtDb: an 808 over its own sustain
    [InlineData(-20f, 20f, 1f)]
    public void InAGrooveKickEnvelopesEvidenceGradesTheBeatAsItFires(float prominenceDb, float riseDb, float expected)
    {
        // The FFT sees only 2 dB a frame later, which raises nothing: the grade is what the time domain saw at the beat.
        // (Half a second more after the groove lets its last pulse decay to nothing first.)
        var c = Warm();
        Groove(c);
        Wait(c, 0.5f);
        var (atFire, peak) = Onset(c, 2, new KickEvidence(prominenceDb, riseDb, 1));
        Assert.Equal(expected, atFire, 3);
        Assert.Equal(expected, peak, 3);
    }

    [Fact]
    public void TheFftsViewAFrameLaterOnlyRaisesTheGrade()
    {
        var c = Warm();
        Groove(c);
        // Half of the evidence as it fires, then the kick fills the FFT window (12 dB): raised to 1 a frame later.
        Assert.Equal((0.5f, 1f), Onset(c, 12, new KickEvidence(-7.5f, 0, 1)));
        Wait(c, 0.43f);
        // Full evidence as it fires and nothing in the FFT after: the pulse stays and decays, never lowered.
        c.BeginFrame();
        c.Update(true, Floor + 0.1f, 1, true, 0.9f, Clear, Step, Step, Tuning);
        c.EndFrame(Step, Tuning);
        Assert.Equal(1f, c.Beat);
        Frame(c, false, Floor + 0.2f);
        Assert.Equal(MathF.Exp(-Step / Tuning.BeatDecaySeconds), c.Beat, 5);
    }

    [Fact]
    public void AGhostNoteBouncesLessThanTheKicks()
    {
        // The kick band's amplitude next to the recent confirmed kicks' (SizeFrom..SizeAt), whose reference halves every
        // SizeHalfLifeSeconds: 0.46 s after the last kick's window closed, a tenth of their amplitude grades 0.14.
        var c = Warm();
        Groove(c);
        float expected = S(BeatCharacter.SizeFrom, BeatCharacter.SizeAt, 0.1f / MathF.Pow(2, -0.46f / BeatCharacter.SizeHalfLifeSeconds));
        var (atFire, peak) = Onset(c, 12, new KickEvidence(0, 30, 0.1f));
        Assert.Equal(expected, atFire, 3);
        Assert.Equal(expected, peak, 3);
        Assert.InRange(expected, 0.13f, 0.15f);
    }

    [Theory]
    [InlineData(0.9f, 1f)]
    [InlineData(0.55f, 1f)]        // LoudAt: 8 dB under the song's loud parts
    [InlineData(0.4f, 0.5f)]       // halfway
    [InlineData(0.25f, 0.3f)]      // QuietFrom: only the groove's floor, LoudFloor × punch
    [InlineData(0f, 0.3f)]
    public void HowLoudTheMomentIsScalesThePulseDownToAGroovesFloor(float position, float expected)
    {
        var c = Warm();
        Groove(c);
        Wait(c, 2, position);                                       // the held position has come down to it
        Assert.Equal((expected, expected), Onset(c, 12, Clear, position));
        Assert.Equal(expected, c.LastGrade, 3);
    }

    [Fact]
    public void TheLoudnessIsHeldOverTheDipBetweenKicks()
    {
        // The song position dips between kicks: its peak is held LoudHoldSeconds, then falls LoudReleasePerSecond.
        var c = Warm();
        Groove(c);
        Wait(c, 0.6f, 0.2f);                                        // within the hold: still the loud moment
        Assert.Equal((1f, 1f), Onset(c, 12, Clear, 0.2f));
        Wait(c, 2f, 0.2f);                                          // a whisper: the groove's floor
        Assert.Equal((0.3f, 0.3f), Onset(c, 12, Clear, 0.2f));
    }

    [Fact]
    public void AGrooveKeepsItsPunchOnWeakerKicksThatCantStartOne()
    {
        // A fuller chorus hides its kicks from the strong measure: onsets that rise 15.6 dB over the kick band's own level
        // but only to the music's level (prominence 0 dB, kick-likeness 0). At a drum part's rate (KeepFrom..KeepAt) they
        // keep a groove the strong kicks established; in a song without one they never start it.
        const float floor = 0.2f;
        var c = Warm(floor);
        Groove(c, floor: floor);
        for (int i = 0; i < 20; i++)
        {
            Onset(c, 0, Clear, floor: floor);
            Wait(c, 0.43f, floor: floor);
        }
        Assert.Equal(0, c.LastKickLike);
        Assert.Equal(1, c.Punch);

        var calm = Warm(floor);
        float pulse = 0;
        for (int i = 0; i < 20; i++)
        {
            pulse = MathF.Max(pulse, Onset(calm, 0, Clear, floor: floor).Peak);
            Wait(calm, 0.43f, floor: floor);
        }
        Assert.Equal(0, calm.Punch);
        Assert.Equal(0, pulse);

        // When they stop, the punch holds HoldSeconds after the last one, then follows the fading counts down at
        // ReleaseSeconds (the loop above already waited 0.45 s after it).
        Wait(c, 2.3f, floor: floor);
        Assert.Equal(1, c.Punch);
        Wait(c, 3.2f, floor: floor);
        Assert.InRange(c.Punch, 0.1f, 0.45f);
        Wait(c, 40, floor: floor);
        Assert.Equal(0, c.Punch);                                   // flushed to 0, never a subnormal
        Assert.Equal(0, c.Drive);
    }

    [Fact]
    public void ARetriggeredBeatIsTheSameMusicalEvent()
    {
        // BeatDetector may fire again inside its refractory time (a flam, a strum and its bass note); the counts take
        // only what such a beat adds over the one before it (EventSeconds), so one event never makes a groove alone.
        var c = Warm();
        Onset(c, 12, Clear);
        Wait(c, 0.01f);
        Onset(c, 12, Clear);                                        // fired 80 ms after the first
        Assert.InRange(c.Drive, 0.95f, 1);
        Assert.Equal(0, c.Punch);

        var d = Warm();
        Onset(d, 12, Clear);
        Wait(d, 0.18f);
        Onset(d, 12, Clear);                                        // 250 ms after: a second kick
        Assert.InRange(d.Drive, 1.85f, 1.95f);
    }

    [Fact]
    public void TempoBeatsAreTheAccentedKicks()
    {
        // Where kicks play, a strong kick whose rise is at least TempoAccent × AccentTolerance of the strongest recent
        // kick's is a tempo beat, reported on the step its onset window closes with the time since it fired.
        var c = Warm();
        Assert.False(c.KickContext);
        float age = -1, rise = Amplitude(12);
        Frame(c, true, Floor + rise / 2, evidence: Clear);
        for (int i = 0; i < 5; i++)
        {
            Frame(c, false, Floor + rise);
            if (c.TempoBeatAge >= 0) age = c.TempoBeatAge;
        }
        Assert.InRange(age, BeatCharacter.OnsetWindowSeconds - 1e-4f, BeatCharacter.OnsetWindowSeconds + Step + 1e-4f);
        Assert.True(c.KickContext);
        Frame(c, false, Floor);
        Assert.Equal(-1, c.TempoBeatAge);                           // only on the step the window closed
        bool Counts(float db)
        {
            bool counted = false;
            float r = Amplitude(db);
            Frame(c, true, Floor + r / 2, evidence: Clear);
            for (int i = 0; i < 6; i++)
            {
                Frame(c, false, i < 5 ? Floor + r : Floor);
                counted |= c.TempoBeatAge >= 0;
            }
            Wait(c, 0.2f);
            return counted;
        }
        Assert.True(Counts(24));
        Assert.False(Counts(5));                                    // not a strong kick (kick-likeness 0.02)
        Assert.False(Counts(16));                                   // strong, but an off-beat at 0.4 of the last one's rise
        // An onset AudioAnalyzer already counted as it fired (no kicks around yet) is not counted again.
        var d = Warm();
        d.BeginFrame();
        d.Update(true, Floor + rise / 2, 1, true, 0.9f, Clear, Step, Step, Tuning);
        d.CountedAtFire();
        d.EndFrame(Step, Tuning);
        bool again = false;
        for (int i = 0; i < 6; i++)
        {
            Frame(d, false, i < 5 ? Floor + rise : Floor);
            again |= d.TempoBeatAge >= 0;
        }
        Assert.False(again);
        Assert.True(d.KickContext);
        Wait(d, BeatCharacter.KickContextSeconds + 0.1f);
        Assert.False(d.KickContext);
    }

    [Fact]
    public void BeatIsTheGradeOnItsFrameThenDecaysEveryFrame()
    {
        // As doc 03 §2: the value on the frame the beat lands, then exp(−dt / BeatDecaySeconds) per frame.
        var c = Warm();
        Groove(c);
        c.BeginFrame();
        c.Update(true, Floor + Amplitude(12) / 2, 1, true, 0.9f, Clear, Step, Step, Tuning);
        c.Update(false, Floor + Amplitude(12), 1, true, 0.9f, KickEvidence.None, Step, Step, Tuning);   // a second step
        c.EndFrame(2 * Step, Tuning);
        Assert.Equal(1f, c.Beat);
        c.BeginFrame();
        c.EndFrame(0.12f, Tuning);
        Assert.Equal(MathF.Exp(-1), c.Beat, 4);
    }

    [Fact]
    public void SensitivityScalesTheGradeAndClamps()
    {
        var half = Warm();
        var twice = Warm();
        Groove(half);
        Groove(twice);
        Assert.Equal((0.5f, 0.5f), Onset(half, 12, Clear, tuning: Tuning with { Sensitivity = 0.5f }));
        Assert.Equal((1f, 1f), Onset(twice, 12, Clear, tuning: Tuning with { Sensitivity = 2f }));
        // At 2, a kick in a moment halfway down (loud 0.5) is full.
        Wait(twice, 2, 0.4f);
        Assert.Equal((1f, 1f), Onset(twice, 12, Clear, 0.4f, tuning: Tuning with { Sensitivity = 2f }));
    }

    [Fact]
    public void TheOnsetWindowIsMeasuredInAudioNotFrames()
    {
        // With 50 ms packets a kick's attack can arrive in one packet and its body in the next, with frames without audio
        // between them: those frames don't close the onset's window.
        var c = Warm();
        float rise = Amplitude(12);
        Frame(c, true, Floor + rise / 8, evidence: Clear);
        for (int i = 0; i < 5; i++)
        {
            c.BeginFrame();
            c.Idle(Step);
            c.EndFrame(Step, Tuning);
        }
        Assert.Equal(0, c.LastKickLike);                            // not confirmed yet
        for (int i = 0; i < 5; i++) Frame(c, false, Floor + rise);
        Assert.Equal(1, c.LastKickLike);
    }

    [Fact]
    public void NearSilenceStartsTheWarmUpAgain()
    {
        var c = Warm();
        Frame(c, false, 0, 0, audible: false);
        Wait(c, 0.5f);
        Assert.Equal((0f, 0f), Onset(c, 20, Clear));
        Assert.Equal(0, c.LastKickLike);

        // After the digital silence between two songs the first second only listens, even with the last song's punch
        // still held: the next song's first notes rise out of the silence and would all read as kicks.
        var d = Warm();
        Groove(d);
        for (int i = 0; i < 100; i++) Frame(d, false, 0, 0, audible: false);
        Wait(d, 0.5f);
        Assert.Equal(1, d.Punch);
        Assert.Equal((0f, 0f), Onset(d, 20, Clear));
    }

    [Fact]
    public void ResetAndBadInputLeaveAQuietFiniteState()
    {
        var c = Warm();
        Groove(c, 3);
        c.BeginFrame();
        c.Update(true, float.NaN, float.PositiveInfinity, true, float.NaN, new KickEvidence(float.NaN, float.PositiveInfinity, float.NaN), float.NaN, float.NaN, Tuning);
        c.Update(false, -1, -1, true, -1, new KickEvidence(float.NegativeInfinity, float.NegativeInfinity, -1), -1, -1, Tuning);
        c.Idle(float.NaN);
        c.EndFrame(float.NaN, Tuning);
        Assert.True(float.IsFinite(c.Beat) && float.IsFinite(c.Punch) && float.IsFinite(c.Drive) && float.IsFinite(c.LastGrade));
        c.Reset();
        Assert.Equal(0f, c.Beat);
        Assert.Equal(0f, c.Punch);
        Assert.Equal(0f, c.Drive);
        Assert.Equal(-1f, c.TempoBeatAge);
        Assert.False(c.KickContext);
    }

    [Fact]
    public void UpdateDoesNotAllocate()
    {
        var c = Warm();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20_000; i++)
        {
            c.BeginFrame();
            c.Update(i % 50 == 0, i % 50 < 5 ? 5 : Floor, 1, i % 3000 != 0, 0.9f, i % 50 == 0 ? Clear : KickEvidence.None, Step, Step, Tuning);
            if (i % 7 == 0) c.Idle(Step);
            c.EndFrame(Step, Tuning);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
