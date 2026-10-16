using Rimlight.AudioTools;
using Xunit;

namespace Rimlight.Tests.Tools;

// The procedural style tracks behind MusicDynamicsTests (Maxwell's feedback, round 1): deterministic, mixed to their
// section levels, with honest ground truth.
public sealed class StyleTracksTests
{
    [Theory]
    [InlineData(MusicStyle.CalmBallad, 100)]
    [InlineData(MusicStyle.Rap, 40)]
    [InlineData(MusicStyle.Edm, 52)]
    [InlineData(MusicStyle.BalladWithDrums, 60)]
    public void SectionsAreContiguousAndMixedToTheirLevels(MusicStyle style, double seconds)
    {
        StyleTrack track = StyleTracks.Render(style);
        Assert.Equal(48000, track.SampleRate);
        Assert.Equal(seconds, track.Seconds, 3);
        Assert.Equal(0, track.Sections[0].Start);
        Assert.Equal(seconds, track.Sections[^1].End);
        for (int i = 1; i < track.Sections.Count; i++) Assert.Equal(track.Sections[i - 1].End, track.Sections[i].Start);
        foreach (TrackSection s in track.Sections)
        {
            // Away from the 0.5 s gain ramps at the boundaries.
            int from = (int)((s.Start + 0.5) * track.SampleRate), to = (int)((s.End - 0.5) * track.SampleRate);
            double squares = 0;
            for (int i = from; i < to; i++) squares += track.Samples[i] * (double)track.Samples[i];
            Assert.InRange(10 * Math.Log10(squares / (to - from)), s.TargetRmsDb - 1, s.TargetRmsDb + 1);
        }
        Assert.InRange(track.Samples.Max(MathF.Abs), 0.05f, 1f);
        Assert.Equal(StyleParts.All, track.Parts);
    }

    [Fact]
    public void TheBalladsVersesAreWhisperedAndItsChorusesLoud()
    {
        StyleTrack ballad = StyleTracks.Render(MusicStyle.CalmBallad);
        Assert.Equal(-24, ballad.Section("verse1").TargetRmsDb);
        Assert.Equal(-8, ballad.Section("chorus1").TargetRmsDb);
        Assert.Empty(ballad.KickTimes);
        Assert.All(ballad.Sections, s => Assert.False(s.Drums));
        Assert.Throws<ArgumentException>(() => ballad.Section("bridge"));
    }

    [Fact]
    public void KicksFallInTheDrumSectionsOnly()
    {
        foreach (MusicStyle style in new[] { MusicStyle.Rap, MusicStyle.Edm, MusicStyle.BalladWithDrums })
        {
            StyleTrack track = StyleTracks.Render(style);
            Assert.NotEmpty(track.KickTimes);
            for (int i = 1; i < track.KickTimes.Count; i++) Assert.True(track.KickTimes[i] > track.KickTimes[i - 1]);
            Assert.All(track.KickTimes, k => Assert.Contains(track.Sections, s => s.Drums && k >= s.Start && k < s.End));
        }
        // Rap: an 808 on each of the 60 beats, and on the "and" of 4 in every second bar.
        Assert.Equal(60 + 7, StyleTracks.Render(MusicStyle.Rap).KickTimes.Count);
    }

    [Fact]
    public void RenderingIsDeterministicAndPartsAreSeparable()
    {
        StyleTrack a = StyleTracks.Render(MusicStyle.BalladWithDrums, seed: 3);
        StyleTrack b = StyleTracks.Render(MusicStyle.BalladWithDrums, seed: 3);
        StyleTrack other = StyleTracks.Render(MusicStyle.BalladWithDrums, seed: 4);
        Assert.Equal(a.Samples, b.Samples);
        Assert.NotEqual(a.Samples, other.Samples);
        Assert.Equal(a.KickTimes, other.KickTimes);          // the seed humanizes; the drum pattern is fixed

        // Each instrument has its own random stream: the voice rendered alone, or with the guitar, is the same voice.
        StyleTrack voice = StyleTracks.Render(MusicStyle.BalladWithDrums, seed: 3, parts: StyleParts.Voice);
        StyleTrack again = StyleTracks.Render(MusicStyle.BalladWithDrums, seed: 3, parts: StyleParts.Voice);
        Assert.Equal(voice.Samples, again.Samples);
        Assert.Empty(voice.KickTimes);
        Assert.Equal(StyleParts.Voice, voice.Parts);
        StyleTrack drums = StyleTracks.Render(MusicStyle.BalladWithDrums, seed: 3, parts: StyleParts.Drums);
        Assert.Equal(a.KickTimes, drums.KickTimes);
        // Sections without the part are silent: no drums in the verses.
        Assert.Equal(0f, drums.Samples.Take(23 * drums.SampleRate).Max(MathF.Abs));
    }

    [Fact]
    public void BadArgumentsThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StyleTracks.Render(MusicStyle.Rap, sampleRate: 4000));
        Assert.Throws<ArgumentOutOfRangeException>(() => StyleTracks.Render(MusicStyle.Rap, parts: StyleParts.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => StyleTracks.Render((MusicStyle)99));
    }
}
