using Rimlight.App.Overlay;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Platform.Tests;

// DemoBeat (K8, doc 04 §5): demo mode's synthetic 120 BPM track and its four palettes, what the README GIF shows.
public sealed class DemoBeatTests
{
    [Fact]
    public void ItBeatsAt120BpmWithTheDownbeatStrongest()
    {
        var demo = new DemoBeat();
        var onsets = new List<double>();
        var peaks = new List<float>();
        float previous = 0;
        const float Dt = 1 / 60f;
        for (int frame = 0; frame < 60 * 48 - 10; frame++) // to just before 48 s
        {
            AudioFeatures features = demo.Advance(Dt);
            Assert.False(features.IsSilent);
            Assert.InRange(features.Level, 0, 1);
            Assert.InRange(features.Bass, 0, 1);
            Assert.InRange(features.Beat, 0, 1);
            if (features.Beat > previous + 0.1f)
            {
                onsets.Add(demo.Time);
                peaks.Add(features.Beat);
            }
            previous = features.Beat;
        }
        // An onset on the first frame at or after every half second, from the first frame on: 96 by 47.9 s.
        Assert.Equal(96, onsets.Count);
        for (int i = 0; i < onsets.Count; i++) Assert.InRange(onsets[i] - i * DemoBeat.BeatSeconds, -1e-6, Dt + 1e-6);
        // Every bar's first beat hits hardest. (The very first is seen a whole frame late, so it is left out.)
        for (int i = 1; i < peaks.Count; i++)
        {
            bool downbeat = i % 4 == 0;
            if (downbeat) Assert.True(peaks[i] > 0.95f, $"downbeat {i} peaked at {peaks[i]}");
            else Assert.True(peaks[i] < 0.9f, $"beat {i} peaked at {peaks[i]}");
        }
    }

    [Fact]
    public void TheLevelMovesLikeMusicNeverFlat()
    {
        float min = 1, max = 0;
        for (double t = 0; t < 24; t += 0.01)
        {
            float level = DemoBeat.FeaturesAt(t).Level;
            min = MathF.Min(min, level);
            max = MathF.Max(max, level);
        }
        Assert.InRange(min, 0.2f, 0.4f);
        Assert.InRange(max, 0.7f, 0.9f);
    }

    [Fact]
    public void FourPalettesTakeTurnsEverySixSecondsOnADownbeat()
    {
        Assert.Equal(4, DemoBeat.Palettes.Length);
        Assert.Equal(TimeSpan.FromMilliseconds(800), DemoBeat.PaletteFade);
        Assert.Equal(0, DemoBeat.PaletteSeconds % (4 * DemoBeat.BeatSeconds)); // whole bars
        var demo = new DemoBeat();
        var changes = new List<(double Time, int Index)>();
        int index = demo.PaletteIndex;
        Assert.Equal(0, index);
        for (int frame = 0; frame < 60 * 30 - 10; frame++) // to just before 30 s
        {
            demo.Advance(1 / 60f);
            if (demo.PaletteIndex == index) continue;
            index = demo.PaletteIndex;
            changes.Add((demo.Time, index));
        }
        Assert.Equal(new[] { 1, 2, 3, 0 }, changes.Select(c => c.Index));
        for (int i = 0; i < changes.Count; i++) Assert.InRange(changes[i].Time - 6 * (i + 1), -1e-6, 1 / 60.0 + 1e-6);
        // Warm and cool alternate, so every change shows: the brand violet, sunset, aurora, ember.
        Assert.Equal("#7C5CFF", SrgbHex.Format(DemoBeat.Palettes[0].Primary));
        Assert.Equal("#FF8A2A", SrgbHex.Format(DemoBeat.Palettes[1].Primary));
        Assert.Equal("#14E3C5", SrgbHex.Format(DemoBeat.Palettes[2].Primary));
        Assert.Equal("#FF3A1F", SrgbHex.Format(DemoBeat.Palettes[3].Primary));
    }

    [Fact]
    public void ItIsDeterministicRestartsAndIgnoresBadSteps()
    {
        var a = new DemoBeat();
        var b = new DemoBeat();
        var random = new Random(5);
        for (int i = 0; i < 2000; i++)
        {
            float dt = (float)(random.NextDouble() / 30);
            AudioFeatures fa = a.Advance(dt);
            Assert.Equal(DemoBeat.FeaturesAt(a.Time), fa);
            Assert.Equal(fa, b.Advance(dt));
        }
        double time = a.Time;
        a.Advance(float.NaN);
        a.Advance(-1);
        a.Advance(float.PositiveInfinity);
        Assert.Equal(time, a.Time);
        a.Restart();
        Assert.Equal(0, a.Time);
        Assert.Equal(DemoBeat.FeaturesAt(0), DemoBeat.FeaturesAt(-5));
    }

    [Fact]
    public void AdvancingAllocatesNothing()
    {
        var demo = new DemoBeat();
        demo.Advance(1 / 60f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++) demo.Advance(1 / 144f);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
