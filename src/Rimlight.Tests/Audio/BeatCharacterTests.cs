using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

// Maxwell's feedback, round 1 (and its review): each beat's kick-likeness and grade, the drive and punch, and the tempo
// beats. Fed directly, one 10 ms step per frame, with the music above the bass band at amplitude 1 (so an onset's rise
// is its prominence) and the kick band at level 0 between onsets. Expected values are the documented thresholds written
// out.
public sealed class BeatCharacterTests
{
    private const float Step = 0.01f;
    private static readonly AudioTuning Tuning = new();

    private static float Smooth(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    // The rise that is `db` over the music (amplitude 1).
    private static float Rise(float db) => MathF.Pow(10, db / 20);

    private static void Update(BeatCharacter c, bool fired, float level, float position = 0.9f, AudioTuning? tuning = null) =>
        c.Update(fired, level, 1, true, position, Step, Step, tuning ?? Tuning);

    private static void Wait(BeatCharacter c, float seconds, float position = 0.9f)
    {
        for (int i = 0; i < (int)MathF.Round(seconds / Step); i++)
        {
            Update(c, false, 0, position);
            c.EndFrame(Step, Tuning);
        }
    }

    // A character that has heard 1.5 s of music: past the warm-up, the music around onsets at amplitude 1.
    private static BeatCharacter Warm()
    {
        var c = new BeatCharacter();
        Wait(c, 1.5f);
        return c;
    }

    // One onset whose kick band rises by `rise`, half of it on the step that fires and all of it over the next three
    // (40 ms), then falls back on the step that closes the window. Returns the highest pulse while it was graded.
    private static float Onset(BeatCharacter c, float rise, float position = 0.9f, AudioTuning? tuning = null)
    {
        tuning ??= Tuning;
        float pulse = 0;
        for (int i = 0; i < 5; i++)
        {
            Update(c, i == 0, i == 0 ? rise / 2 : i < 4 ? rise : 0, position, tuning);
            c.EndFrame(Step, tuning);
            pulse = MathF.Max(pulse, c.Beat);
        }
        return pulse;
    }

    // A groove: kicks 0.5 s apart, each `db` over the music.
    private static void Groove(BeatCharacter c, int kicks, float db = 12)
    {
        for (int i = 0; i < kicks; i++)
        {
            Onset(c, Rise(db));
            Wait(c, 0.45f);
        }
    }

    [Fact]
    public void TheFirstSecondOfMusicOnlyListens()
    {
        // With no music heard yet there is nothing to measure an onset against: no kick-likeness, no drive.
        var c = new BeatCharacter();
        Wait(c, 0.5f);
        Onset(c, Rise(20));
        Assert.Equal(0, c.LastKickLike);
        Assert.Equal(0, c.Drive);
        Wait(c, 0.5f);
        Onset(c, Rise(20));
        Assert.Equal(1, c.LastKickLike);
    }

    [Theory]
    [InlineData(0f, 0f)]       // a note that adds as much as the music above the bass holds
    [InlineData(4f, 0f)]       // KickFromDb
    [InlineData(6.5f, 0.5f)]   // halfway
    [InlineData(9f, 1f)]       // KickAtDb
    [InlineData(20f, 1f)]
    public void KickLikenessIsTheSubBassRiseOverTheMusicAboveIt(float db, float expected)
    {
        var c = Warm();
        Onset(c, Rise(db));
        Assert.Equal(expected, c.LastKickLike, 3);
        Assert.Equal(Smooth(BeatCharacter.KickFromDb, BeatCharacter.KickAtDb, db), c.LastKickLike, 3);
    }

    [Fact]
    public void TheRiseCountsFromTheLowestLevelAroundTheAttack()
    {
        // A bass note at level 2 stops as the kick lands (the level dips to 1 on the step that fires), then the kick
        // brings it to 1 + Rise(12): the kick's whole rise counts, not the 0.6 dB the band gained over the bass note.
        var c = Warm();
        for (int i = 0; i < 20; i++) Update(c, false, 2);
        Update(c, true, 1);
        for (int i = 0; i < 4; i++) Update(c, false, 1 + Rise(12));
        Assert.Equal(1, c.LastKickLike);
        // The step before the one that fired counts too: the attack can start a step early.
        var d = Warm();
        Update(d, false, Rise(12) / 2);
        Update(d, true, Rise(12));
        for (int i = 0; i < 4; i++) Update(d, false, Rise(12));
        Assert.Equal(1, d.LastKickLike);
    }

    [Fact]
    public void TheOnsetWindowIsMeasuredInAudioNotFrames()
    {
        // With 50 ms packets a kick's attack can arrive in one packet and its body in the next, with two frames without
        // audio between them: those frames don't close the 35 ms window.
        var c = Warm();
        Update(c, true, Rise(12) / 8);
        for (int i = 0; i < 5; i++) c.Idle(Step, Tuning);
        Update(c, false, Rise(12));
        Assert.Equal(1, c.LastKickLike);
    }

    [Fact]
    public void ALoneKickLikeOnsetGivesNoPulseARunOfThemDoes()
    {
        // The grade uses the punch from before the onset: one thump in a wave is not a groove.
        var c = Warm();
        Assert.Equal(0, Onset(c, Rise(12)));
        Assert.Equal(0.5f, c.Drive, 4);                                   // DriveRise of the way to 1
        float punch = Smooth(0.3f, 0.75f, 0.5f);
        Assert.Equal(punch, c.Punch, 4);
        Wait(c, 0.45f);
        Assert.Equal(punch, Onset(c, Rise(12)), 3);                       // kick-like 1 × punch × accent 1 × loud 1
        Assert.Equal(0.75f, c.Drive, 4);
        Assert.Equal(1f, c.Punch);
        Wait(c, 0.45f);
        Assert.Equal(1f, Onset(c, Rise(12)));                             // a full kick in a groove is exactly 1
    }

    [Fact]
    public void OnsetsThatArentKicksBringTheDriveDownSlowlyAndTimeLetsItGo()
    {
        var c = Warm();
        Groove(c, 6);
        float drive = c.Drive;
        Assert.Equal(1 - MathF.Pow(0.5f, 6), drive, 4);                 // half of the way up per kick
        for (int i = 0; i < 5; i++) Onset(c, Rise(0));                    // syllables between the kicks: a tenth each
        Assert.Equal(drive * MathF.Pow(0.9f, 5), c.Drive, 3);
        // Held DriveHoldSeconds after the last kick-like onset, then fading with τ = DriveReleaseSeconds.
        var d = Warm();
        Groove(d, 6);
        drive = d.Drive;
        Wait(d, 3f - 0.46f - 0.02f);                                      // the window closed 0.46 s ago
        Assert.Equal(drive, d.Drive, 4);
        Wait(d, 2.54f);
        Assert.InRange(d.Drive, drive * MathF.Exp(-1) - 0.01f, drive * MathF.Exp(-1) + 0.01f);
        Wait(d, 30);
        Assert.Equal(0, d.Drive);                                         // flushed to 0, never a subnormal
        Assert.Equal(0, d.Punch);
    }

    [Fact]
    public void ABassLineWithAnOccasionalProminentNoteDoesNotBounce()
    {
        // One onset in ten as prominent as a kick, the rest well under KickFromDb: each prominent one meets a drive the
        // others have brought down, so it gives at most a small pulse, and the drive between them stays low.
        var c = Warm();
        float pulse = 0;
        for (int i = 0; i < 60; i++)
        {
            pulse = MathF.Max(pulse, Onset(c, Rise(i % 10 == 0 ? 12 : 0)));
            Wait(c, 0.3f);
        }
        Assert.InRange(pulse, 0, 0.1f);
        Assert.InRange(c.Drive, 0, 0.3f);
        Assert.Equal(0, c.Punch);
    }

    [Fact]
    public void AnOnsetIsGradedAgainstTheStrongestRecentKick()
    {
        // Kicks rising 24 dB over the music, then one rising 18 dB (half as much): its accent is 0.5 / 0.85 of the peak,
        // which has been halving (AccentHalfLifeSeconds 1.5 s) for the 0.49 s since the last kick's window closed.
        var c = Warm();
        Groove(c, 6, 24);
        float expected = 0.5f / (0.85f * MathF.Pow(2, -0.49f / 1.5f));
        Assert.Equal(expected, Onset(c, Rise(18)), 2);
        // Within 15 % of the strongest counts as full.
        Wait(c, 0.45f);
        Assert.Equal(1f, Onset(c, 0.86f * Rise(24)), 3);
    }

    [Theory]
    [InlineData(0.9f, 1f)]
    [InlineData(0.6f, 1f)]        // LoudAt: 9.6 dB under the song's loud parts
    [InlineData(0.45f, 0.5f)]     // halfway
    [InlineData(0.3f, 0.3f)]      // QuietFrom: only the groove's floor, LoudFloor × punch
    [InlineData(0f, 0.3f)]
    public void HowLoudTheMomentIsScalesThePulseDownToAGroovesFloor(float position, float expected)
    {
        var c = Warm();
        Groove(c, 6);
        Wait(c, 2, position);                                             // the held position has come down to it
        Assert.Equal(expected, Onset(c, Rise(12), position), 3);
        Assert.Equal(expected, c.LastGrade, 3);
    }

    [Fact]
    public void TheLoudnessBeforeABeatIsHeldOverTheDipBetweenBeats()
    {
        // The moment's loudness is taken from just before the onset (the kick can't lift its own pulse), held from the
        // music before it with a fall of 0.25 position a second (6 dB/s), so the dip just before a kick doesn't count.
        var c = Warm();
        Groove(c, 6);
        Wait(c, 0.2f, 0.9f);
        Wait(c, 0.4f, 0.2f);                                              // 0.9 − 0.25 × 0.4 = 0.8: still loud
        Assert.Equal(1f, Onset(c, Rise(12), 0.2f), 3);
        Wait(c, 2f, 0.2f);
        Assert.Equal(0.3f, Onset(c, Rise(12), 0.95f), 3);                // a kick that makes the moment loud gets the floor
    }

    [Fact]
    public void SensitivityScalesTheGradeAndClamps()
    {
        var half = Warm();
        var twice = Warm();
        Groove(half, 6);
        Groove(twice, 6);
        Assert.Equal(0.5f, Onset(half, Rise(12), tuning: Tuning with { Sensitivity = 0.5f }), 3);
        Assert.Equal(1f, Onset(twice, Rise(12), tuning: Tuning with { Sensitivity = 2f }));
        // At 2, a kick in a moment halfway down (loud 0.5) is full.
        Assert.Equal(1f, Onset(twice, Rise(12), 0.5f, Tuning with { Sensitivity = 2f }));
    }

    [Fact]
    public void BeatIsTheGradeOnItsFrameThenDecaysEveryFrame()
    {
        // As doc 03 §2: the value on the frame the beat lands, then exp(−dt / BeatDecaySeconds) per frame.
        var c = Warm();
        Groove(c, 6);
        Update(c, true, Rise(12) / 2);
        Update(c, false, Rise(12));                                       // a second step in the same frame
        c.EndFrame(2 * Step, Tuning);
        Assert.Equal(1f, c.Beat);
        c.EndFrame(0.12f, Tuning);
        Assert.Equal(MathF.Exp(-1), c.Beat, 4);
    }

    [Fact]
    public void AGradeThatGrowsRaisesThePulseButNeverHoldsIt()
    {
        var c = Warm();
        Groove(c, 6, 9);
        Update(c, true, Rise(6.5f));                                      // kick-like 0.5 so far
        c.EndFrame(Step, Tuning);
        Assert.Equal(0.5f, c.Beat, 2);
        Update(c, false, Rise(12));                                       // the kick fills the window: 1
        c.EndFrame(Step, Tuning);
        Assert.Equal(1f, c.Beat);
        Update(c, false, Rise(12));                                       // no higher: decays
        c.EndFrame(Step, Tuning);
        Assert.Equal(MathF.Exp(-Step / 0.12f), c.Beat, 5);
    }

    [Fact]
    public void TempoBeatsAreTheAccentedKicks()
    {
        // Where kicks play, a kick-like onset whose rise is at least 0.6 of the strongest recent kick's is a tempo beat,
        // reported on the step its window closes with the time since it fired; weaker or not kick-like ones are not.
        var c = Warm();
        Assert.False(c.KickContext);
        float age = -1;
        for (int i = 0; i < 5; i++)
        {
            Update(c, i == 0, i < 4 ? Rise(12) : 0);
            if (c.TempoBeatAge >= 0) age = c.TempoBeatAge;
        }
        Assert.Equal(0.04f, age, 4);
        Assert.True(c.KickContext);
        Update(c, false, 0);
        Assert.Equal(-1, c.TempoBeatAge);                                 // only on the step the window closed
        bool Counts(float rise)
        {
            bool counted = false;
            for (int i = 0; i < 5; i++)
            {
                Update(c, i == 0, i < 4 ? rise : 0);
                counted |= c.TempoBeatAge >= 0;
            }
            Wait(c, 0.2f);
            return counted;
        }
        Assert.True(Counts(Rise(24)));
        Assert.False(Counts(Rise(5)));                                    // not kick-like (0.1)
        Assert.False(Counts(Rise(24) * 0.4f));                            // kick-like, but an off-beat at under half
        // An onset AudioAnalyzer already counted as it fired (no kicks around yet) is not counted again.
        var d = Warm();
        Update(d, true, Rise(12));
        d.CountedAtFire();
        bool again = false;
        for (int i = 1; i < 5; i++)
        {
            Update(d, false, i < 4 ? Rise(12) : 0);
            again |= d.TempoBeatAge >= 0;
        }
        Assert.False(again);
        Assert.True(d.KickContext);
        Wait(d, 4.1f);
        Assert.False(d.KickContext);
    }

    [Fact]
    public void NearSilenceStartsTheWarmUpAgain()
    {
        var c = Warm();
        c.Update(false, 0, 0, false, 0, Step, Step, Tuning);
        Wait(c, 0.5f);
        Onset(c, Rise(20));
        Assert.Equal(0, c.LastKickLike);
    }

    [Fact]
    public void ResetAndBadInputLeaveAQuietFiniteState()
    {
        var c = Warm();
        Groove(c, 3);
        c.Update(true, float.NaN, float.PositiveInfinity, true, float.NaN, float.NaN, float.NaN, Tuning);
        c.Update(false, -1, -1, true, -1, -1, -1, Tuning);
        c.Idle(float.NaN, Tuning);
        c.EndFrame(float.NaN, Tuning);
        Assert.True(float.IsFinite(c.Beat) && float.IsFinite(c.Punch) && float.IsFinite(c.Drive));
        c.Reset();
        Assert.Equal(0f, c.Beat);
        Assert.Equal(0f, c.Punch);
        Assert.Equal(0f, c.Drive);
        Assert.False(c.KickContext);
    }

    [Fact]
    public void UpdateDoesNotAllocate()
    {
        var c = Warm();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20_000; i++)
        {
            c.Update(i % 50 == 0, i % 50 < 4 ? 4 : 0, 1, i % 3000 != 0, 0.9f, Step, Step, Tuning);
            if (i % 7 == 0) c.Idle(Step, Tuning);
            c.EndFrame(Step, Tuning);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
