using Rimlight.Core.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

// Maxwell's feedback, round 1 (and its review): the song-level reference behind Level and the pulse grade. Fed directly
// with weighted power in 10 ms steps; 1 is 0 dB (a full-scale sine). Round 2 narrowed the range from 24 dB to
// SongLoudness.RangeDb = 18, so the expected positions are written in terms of it; a bin is 0.5 dB, so a steady
// loudness reads back within a quarter of a bin of where it is (Tolerance).
public sealed class SongLoudnessTests
{
    private const float Step = 0.01f;
    private const float Range = SongLoudness.RangeDb;
    private const float Tolerance = 0.25f / SongLoudness.RangeDb + 0.005f;

    private static float Power(float db) => MathF.Pow(10, db / 10);

    private static void Feed(SongLoudness loudness, float db, float seconds)
    {
        for (int i = 0; i < (int)MathF.Round(seconds / Step); i++) loudness.Update(Power(db), true, Step);
    }

    // Near-silence (the analyzer's "not audible": at or below the auto-gain hold level).
    private static void Silence(SongLoudness loudness, float seconds)
    {
        for (int i = 0; i < (int)MathF.Round(seconds / Step); i++) loudness.Update(0, false, Step);
    }

    [Fact]
    public void PositionIsOneAtTheReferenceAndFallsLinearlyOverRangeDb()
    {
        var loudness = new SongLoudness();
        Feed(loudness, 0, 120);
        Assert.InRange(loudness.ReferenceDb, -0.25f, 0.25f);   // 0.5 dB bins
        Assert.InRange(loudness.Position, 1 - Tolerance, 1);
        Feed(loudness, -Range / 2, 2);              // the momentary loudness settles within 2 s (τ = 0.25 s)
        Assert.InRange(loudness.Position, 0.5f - Tolerance, 0.5f + Tolerance);   // and the reference still remembers 0 dB
        Feed(loudness, -30, 2);
        Assert.Equal(0, loudness.Position);
        Feed(loudness, 6, 1);
        Assert.Equal(1, loudness.Position);         // louder than the reference clamps
    }

    [Fact]
    public void AQuietVerseIsJudgedAgainstTheChorusForMostOfItsLength()
    {
        // 20 s of chorus, then a verse 12 dB down: a third of the way up for 15 s and more, its own reference later.
        var loudness = new SongLoudness();
        Feed(loudness, 0, 20);
        float[] at = new float[45];
        for (int s = 0; s < at.Length; s++)
        {
            Feed(loudness, -12, 1);
            at[s] = loudness.Position;
        }
        Assert.All(at[2..16], p => Assert.InRange(p, 1 - 12.5f / Range, 1 - 11.5f / Range));
        Assert.True(at[^1] >= 0.9f, $"{at[^1]}");
    }

    [Fact]
    public void TheOpeningIsJudgedFourDecibelsUnderItsLoudestUntilTheMemoryFills()
    {
        // With nothing heard yet, the prior: the loud parts are probably a little louder than the opening.
        var loudness = new SongLoudness();
        Feed(loudness, -10, 5);
        Assert.Equal(1 - SongLoudness.PriorHeadroomDb / Range, loudness.Position, 3);
        Assert.Equal(-6, loudness.ReferenceDb, 3);
        Assert.Equal(SongLoudness.MinDb, loudness.SessionDb);      // the first song after launch has no session yet
        Feed(loudness, -10, 25);
        Assert.InRange(loudness.Position, 1 - Tolerance, 1);
        // The prior follows the loudest moment so far: a louder passage moves it up at once.
        var louder = new SongLoudness();
        Feed(louder, -20, 5);
        Assert.Equal(-16, louder.ReferenceDb, 3);
        Feed(louder, -8, 3);
        Assert.Equal(-4, louder.ReferenceDb, 3);
    }

    [Fact]
    public void AGapStartsTheNextSongAgainstTheSessionFloor()
    {
        // A song at 0 dB, a second of digital silence, then a whispered opening 15 dB down. The song memory starts afresh
        // (its own reference is the prior, 4 dB over the whisper) but the session floor starts at the last song's
        // reference and falls 0.2 dB a second, so the whisper sits near the bottom (15 dB under the floor) and comes up
        // over about a minute.
        var loudness = new SongLoudness();
        Feed(loudness, 0, 40);
        Assert.InRange(loudness.SessionDb, -0.5f, 0.5f);
        Silence(loudness, 1.5f);
        Assert.Equal(0, loudness.Position);
        Feed(loudness, -15, 2);
        Assert.Equal(-11, loudness.SongReferenceDb, 1);
        Assert.InRange(loudness.ReferenceDb, -0.4f - 0.5f, -0.4f + 0.5f);
        Assert.InRange(loudness.Position, 1 - 14.6f / Range - Tolerance, 1 - 14.6f / Range + Tolerance);
        Feed(loudness, -15, 18);                    // 20 s in: the floor is 4 dB down
        Assert.InRange(loudness.Position, 1 - 11 / Range - Tolerance, 1 - 11 / Range + Tolerance);
        Feed(loudness, -15, 62);                    // 80 s in: the floor is under the song, its own reference
        Assert.InRange(loudness.Position, 0.98f, 1);
    }

    [Fact]
    public void AGapClearsTheLastSongFromTheSongReference()
    {
        // Without a gap a quieter song 12 dB down is judged against the last one for about 20 s; after a gap its own
        // reference starts at its own prior at once (the session floor alone remembers the last song).
        var noGap = new SongLoudness();
        var gap = new SongLoudness();
        Feed(noGap, 0, 30);
        Feed(gap, 0, 30);
        Silence(gap, 1.5f);
        Feed(noGap, -12, 2);
        Feed(gap, -12, 2);
        Assert.InRange(noGap.SongReferenceDb, -0.5f, 0.5f);
        Assert.Equal(-8, gap.SongReferenceDb, 1);
        // A pause shorter than GapSeconds is part of the song.
        var pause = new SongLoudness();
        Feed(pause, 0, 30);
        Silence(pause, 0.5f);
        Feed(pause, -12, 2);
        Assert.InRange(pause.SongReferenceDb, -0.5f, 0.5f);
    }

    [Fact]
    public void NearSilenceAddsNothingAndQuietMusicIsStillMusic()
    {
        var loudness = new SongLoudness();
        Feed(loudness, 0, 30);
        float reference = loudness.ReferenceDb;
        Silence(loudness, 0.5f);                    // near-silence, not yet a gap
        Assert.Equal(reference, loudness.ReferenceDb);
        Assert.Equal(0, loudness.Position);
        // Quiet but audible windows (a whisper at low volume can dip under the −60 dBFS IsSilent level) are music: the
        // position is computed as for any other level, so the light doesn't depend on the volume.
        var quiet = new SongLoudness();
        Feed(quiet, -70, 30);
        Feed(quiet, -70 - Range / 2, 2);
        Assert.InRange(quiet.Position, 0.5f - Tolerance, 0.5f + Tolerance);
    }

    [Fact]
    public void TheMemoryNeverHoldsSubnormalFloats()
    {
        // Forgetting multiplies every bin by about 0.999 each step; a float stuck at a subnormal value would cost CPU on
        // every later step. Bins under a millionth of a second are flushed to 0.
        var loudness = new SongLoudness();
        for (int db = -60; db <= 0; db += 3) Feed(loudness, db, 0.5f);
        Feed(loudness, 0, 600);
        Assert.All(loudness.Bins.ToArray(), b => Assert.True(b == 0 || b >= 1e-6f, $"{b}"));
        Assert.Contains(0f, loudness.Bins.ToArray());
    }

    [Fact]
    public void ResetForgetsTheSongAndTheSessionAndBadInputIsSilence()
    {
        var loudness = new SongLoudness();
        Feed(loudness, 0, 30);
        loudness.Reset();
        Assert.Equal(SongLoudness.MinDb, loudness.ReferenceDb);
        Assert.Equal(SongLoudness.MinDb, loudness.SessionDb);
        Assert.Equal(0, loudness.Position);
        loudness.Update(float.NaN, true, Step);
        loudness.Update(float.PositiveInfinity, true, float.NaN);
        loudness.Update(-1, true, -1);
        Assert.True(float.IsFinite(loudness.LoudnessDb) && float.IsFinite(loudness.ReferenceDb) && float.IsFinite(loudness.Position));
    }

    [Fact]
    public void UpdateDoesNotAllocate()
    {
        var loudness = new SongLoudness();
        Feed(loudness, -6, 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20_000; i++) loudness.Update(Power(-6 - i % 37), i % 1000 >= 150, Step);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
