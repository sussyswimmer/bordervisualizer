using Rimlight.Core.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

// Maxwell's feedback, round 1: the song-level reference behind Level and the pulse grade. Fed directly with weighted
// power in 10 ms steps; 1 is 0 dB (a full-scale sine).
public sealed class SongLoudnessTests
{
    private const float Step = 0.01f;

    private static float Power(float db) => MathF.Pow(10, db / 10);

    private static void Feed(SongLoudness loudness, float db, float seconds)
    {
        for (int i = 0; i < (int)MathF.Round(seconds / Step); i++) loudness.Update(Power(db), true, false, Step);
    }

    private static void Silence(SongLoudness loudness, float seconds)
    {
        for (int i = 0; i < (int)MathF.Round(seconds / Step); i++) loudness.Update(0, false, true, Step);
    }

    [Fact]
    public void PositionIsOneAtTheReferenceAndFallsLinearlyOverTwentyFourDecibels()
    {
        var loudness = new SongLoudness();
        Feed(loudness, 0, 120);
        Assert.InRange(loudness.ReferenceDb, -0.25f, 0.25f);   // 0.5 dB bins
        Assert.InRange(loudness.Position, 0.99f, 1);
        Feed(loudness, -12, 2);                     // the momentary loudness settles within 2 s (τ = 0.25 s)
        Assert.InRange(loudness.Position, 0.49f, 0.51f);   // and the reference still remembers 0 dB
        Feed(loudness, -30, 2);
        Assert.Equal(0, loudness.Position);
        Feed(loudness, 6, 1);
        Assert.Equal(1, loudness.Position);         // louder than the reference clamps
    }

    [Fact]
    public void AQuietVerseIsJudgedAgainstTheChorusForMostOfItsLength()
    {
        // 20 s of chorus, then a verse 16 dB down: a third of the way up for 15 s and more, its own reference later.
        var loudness = new SongLoudness();
        Feed(loudness, 0, 20);
        float[] at = new float[45];
        for (int s = 0; s < at.Length; s++)
        {
            Feed(loudness, -16, 1);
            at[s] = loudness.Position;
        }
        Assert.All(at[2..16], p => Assert.InRange(p, 1 - 16.5f / 24, 1 - 15.5f / 24));
        Assert.True(at[^1] >= 0.9f, $"{at[^1]}");
    }

    [Fact]
    public void TheOpeningIsJudgedFourDecibelsUnderItsLoudestUntilTheMemoryFills()
    {
        // With nothing heard yet, the prior: the loud parts are probably a little louder than the opening.
        var loudness = new SongLoudness();
        Feed(loudness, -10, 5);
        Assert.Equal(1 - 4 / 24f, loudness.Position, 3);
        Assert.Equal(-6, loudness.ReferenceDb, 3);
        Feed(loudness, -10, 25);
        Assert.InRange(loudness.Position, 0.99f, 1);
        // The prior follows the loudest moment so far: a louder passage moves it up at once.
        var louder = new SongLoudness();
        Feed(louder, -20, 5);
        Assert.Equal(-16, louder.ReferenceDb, 3);
        Feed(louder, -8, 3);
        Assert.Equal(-4, louder.ReferenceDb, 3);
    }

    [Fact]
    public void AGapBetweenSongsLetsTheNextOneStandOnItsOwn()
    {
        // A second of silence between songs keeps a quarter of the memory, so a quieter song after it gets its own
        // reference in about 10 s instead of about 23 s.
        var noGap = new SongLoudness();
        var gap = new SongLoudness();
        Feed(noGap, 0, 30);
        Feed(gap, 0, 30);
        Silence(gap, 1.5f);
        Assert.Equal(0, gap.Position);
        Feed(noGap, -12, 14);
        Feed(gap, -12, 14);
        Assert.True(noGap.Position <= 0.6f, $"{noGap.Position}");
        Assert.True(gap.Position >= 0.8f, $"{gap.Position}");
    }

    [Fact]
    public void SilenceIsZeroAndAddsNothingToTheMemory()
    {
        var loudness = new SongLoudness();
        Feed(loudness, 0, 30);
        float reference = loudness.ReferenceDb;
        for (int i = 0; i < 500; i++) loudness.Update(0, false, false, Step);   // near-silence, not yet a gap
        Assert.Equal(reference, loudness.ReferenceDb);
        loudness.Update(Power(-70), true, true, Step);                            // under the IsSilent level
        Assert.Equal(0, loudness.Position);
    }

    [Fact]
    public void ResetForgetsTheSongAndBadInputIsSilence()
    {
        var loudness = new SongLoudness();
        Feed(loudness, 0, 30);
        loudness.Reset();
        Assert.Equal(SongLoudness.MinDb, loudness.ReferenceDb);
        Assert.Equal(0, loudness.Position);
        loudness.Update(float.NaN, true, false, Step);
        loudness.Update(float.PositiveInfinity, true, false, float.NaN);
        loudness.Update(-1, true, false, -1);
        Assert.True(float.IsFinite(loudness.LoudnessDb) && float.IsFinite(loudness.ReferenceDb) && float.IsFinite(loudness.Position));
    }

    [Fact]
    public void UpdateDoesNotAllocate()
    {
        var loudness = new SongLoudness();
        Feed(loudness, -6, 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20_000; i++) loudness.Update(Power(-6 - i % 37), i % 500 != 0, i % 1000 < 150, Step);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
