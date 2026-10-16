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
    [InlineData(MusicStyle.FolkBand, 100)]
    [InlineData(MusicStyle.PianoBallad, 96)]
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
        foreach (MusicStyle style in new[] { MusicStyle.Rap, MusicStyle.Edm, MusicStyle.BalladWithDrums, MusicStyle.FolkBand })
        {
            StyleTrack track = StyleTracks.Render(style);
            Assert.NotEmpty(track.KickTimes);
            for (int i = 1; i < track.KickTimes.Count; i++) Assert.True(track.KickTimes[i] > track.KickTimes[i - 1]);
            Assert.All(track.KickTimes, k => Assert.Contains(track.Sections, s => s.Drums && k >= s.Start && k < s.End));
        }
        // Rap: an 808 on each of the 60 beats, and on the "and" of 4 in every second bar.
        Assert.Equal(60 + 7, StyleTracks.Render(MusicStyle.Rap).KickTimes.Count);
        // The piano ballad has no drums.
        StyleTrack piano = StyleTracks.Render(MusicStyle.PianoBallad);
        Assert.Empty(piano.KickTimes);
        Assert.All(piano.Sections, s => Assert.False(s.Drums));
    }

    [Fact]
    public void MixPutsEachStemAtItsLevelUnderTheMusicInEverySection()
    {
        // The folk band's guitar and voice, with its brush kit 12 dB under them: in every section the added signal is
        // the kit at the music's own section level − 12 dB (each stem is mixed to the section targets on its own, and
        // not clipped: clipped, the kit sat 14.6 dB under the music in the choruses, with flattened kicks).
        StyleTrack music = StyleTracks.Render(MusicStyle.FolkBand, parts: StyleParts.Guitar | StyleParts.Voice);
        StyleTrack drums = StyleTracks.Render(MusicStyle.FolkBand, parts: StyleParts.Drums);
        StyleTrack mix = StyleTracks.Mix(music, (drums, -12));
        Assert.Equal(StyleParts.Guitar | StyleParts.Voice | StyleParts.Drums, mix.Parts);
        Assert.Equal(drums.KickTimes, mix.KickTimes);
        Assert.Equal(music.Sections, mix.Sections);
        foreach (TrackSection s in music.Sections)
        {
            int from = (int)((s.Start + 0.5) * mix.SampleRate), to = (int)((s.End - 0.5) * mix.SampleRate);
            double musicSquares = 0, addedSquares = 0;
            for (int i = from; i < to; i++)
            {
                double added = mix.Samples[i] - (double)music.Samples[i];
                musicSquares += music.Samples[i] * (double)music.Samples[i];
                addedSquares += added * added;
            }
            Assert.InRange(10 * Math.Log10(addedSquares / musicSquares), -13, -11);
        }
        // A stem isn't clipped (the kit alone peaks above full scale in the choruses); the mix is.
        Assert.True(drums.Samples.Max(MathF.Abs) > 1);
        Assert.InRange(mix.Samples.Max(MathF.Abs), 0.05f, 1f);
        Assert.Throws<ArgumentException>(() => StyleTracks.Mix(music, (StyleTracks.Render(MusicStyle.PianoBallad), -6)));
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
