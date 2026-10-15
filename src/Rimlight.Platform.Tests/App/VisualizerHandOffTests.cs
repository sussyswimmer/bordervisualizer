using Rimlight.App.Visualizer;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Platform.Tests;

// The debug visualizer's line to the overlay thread (K8, doc 03 §4, H-004): snapshots one way through a triple buffer,
// tuning requests the other way, applied by MusicGlowSource's AnalyzerTap before the analyzer runs.
public sealed class VisualizerHandOffTests
{
    [Fact]
    public void TheReaderGetsTheNewestBufferOnceAndKeepsItUntilTheNext()
    {
        var buffer = new TripleBuffer<int[]>([0], [0], [0]);
        Assert.False(buffer.TryTake(out int[] none));
        Assert.Equal(0, none[0]);

        buffer.Back[0] = 1;
        buffer.Publish();
        buffer.Back[0] = 2;
        buffer.Publish(); // replaces 1, which the reader never took
        Assert.True(buffer.TryTake(out int[] latest));
        Assert.Equal(2, latest[0]);
        Assert.False(buffer.TryTake(out int[] again));
        Assert.Same(latest, again);

        buffer.Back[0] = 3; // the writer never touches the reader's buffer
        Assert.Equal(2, latest[0]);
        buffer.Publish();
        Assert.True(buffer.TryTake(out latest));
        Assert.Equal(3, latest[0]);
    }

    [Fact]
    public void TheBuffersMustBeThreeDistinctInstances()
    {
        int[] one = [0];
        Assert.Throws<ArgumentException>(() => new TripleBuffer<int[]>(one, one, [0]));
        Assert.Throws<ArgumentNullException>(() => new TripleBuffer<int[]>(one, null!, [0]));
    }

    [Fact]
    public void TwoThreadsNeverShareABufferOrGoBackInTime()
    {
        // The writer fills every slot of its buffer with one number; a reader that ever saw two numbers in one buffer,
        // or a number smaller than before, would be looking at a buffer the writer was filling.
        const int Size = 64;
        var buffer = new TripleBuffer<long[]>(new long[Size], new long[Size], new long[Size]);
        bool done = false;
        var writer = new Thread(() =>
        {
            for (long n = 1; n <= 2_000_000; n++)
            {
                long[] back = buffer.Back;
                for (int i = 0; i < Size; i++) back[i] = n;
                buffer.Publish();
            }
            Volatile.Write(ref done, true);
        });
        writer.Start();
        long last = 0, takes = 0, torn = 0, backwards = 0;
        while (!Volatile.Read(ref done))
        {
            if (!buffer.TryTake(out long[] front)) continue;
            takes++;
            long first = front[0];
            for (int i = 1; i < Size; i++)
                if (front[i] != first) torn++;
            if (first <= last) backwards++;
            last = first;
        }
        writer.Join();
        Assert.Equal(0, torn);
        Assert.Equal(0, backwards);
        Assert.True(takes > 0);
    }

    [Fact]
    public void ATuningAppliesBeforeTheAnalyzerRunsAndTheSettingKeepsItsSensitivity()
    {
        var settings = new Settings { Sensitivity = 1.5f };
        using var rig = new GlowRig(settings);
        var feed = new VisualizerFeed();
        rig.Source.SetVisualizer(feed);
        double now = 0;
        AnalyzerSnapshot Run(int frames)
        {
            for (int i = 0; i < frames; i++) rig.Frame(now += 1 / 60.0, 1 / 60f, settings, false);
            Assert.True(feed.TryTake(out AnalyzerSnapshot snapshot));
            return snapshot;
        }

        AnalyzerSnapshot first = Run(1);
        Assert.Equal(1.5f, first.Tuning.Sensitivity);
        Assert.Equal(0, first.TuningRequest);
        Assert.Equal(1025, first.SpectrumLength); // the default 2048-sample window

        // The request's Sensitivity never wins: Settings.Sensitivity is the only source (H-007).
        int id = feed.PostTuning(new AudioTuning { BeatDecaySeconds = 0.2f, Sensitivity = 0.5f });
        AnalyzerSnapshot applied = Run(1);
        Assert.Equal(id, applied.TuningRequest);
        Assert.Null(applied.TuningError);
        Assert.Equal(0.2f, applied.Tuning.BeatDecaySeconds);
        Assert.Equal(1.5f, applied.Tuning.Sensitivity);

        // A refused tuning (bands out of order) leaves the analyzer's tuning as it was and says why.
        id = feed.PostTuning(new AudioTuning { BassMaxHz = 3000, MidMaxHz = 2000 });
        AnalyzerSnapshot refused = Run(1);
        Assert.Equal(id, refused.TuningRequest);
        Assert.False(string.IsNullOrEmpty(refused.TuningError));
        Assert.Equal(0.2f, refused.Tuning.BeatDecaySeconds);

        // Only the newest of several requests applies; a larger window replaces the diagnostics and the snapshot follows.
        feed.PostTuning(new AudioTuning { WindowSize = 8192 });
        id = feed.PostTuning(new AudioTuning { WindowSize = 4096 });
        AnalyzerSnapshot bigger = Run(1);
        Assert.Equal(id, bigger.TuningRequest);
        Assert.Null(bigger.TuningError);
        Assert.Equal(2049, bigger.SpectrumLength);
        Assert.Equal(4096, bigger.WindowSize);
    }

    [Fact]
    public void SnapshotsComeAbout60TimesASecondAtAnyRateAndEveryAttachmentStartsOver()
    {
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        var feed = new VisualizerFeed();
        rig.Source.SetVisualizer(feed);
        double now = 0;
        int snapshots = 0;
        for (int i = 0; i < 144 * 5; i++)
        {
            rig.Frame(now += 1 / 144.0, 1 / 144f, settings, false);
            if (feed.TryTake(out _)) snapshots++;
        }
        Assert.InRange(snapshots / 5.0, 58, 61);

        snapshots = 0;
        for (int i = 0; i < 30 * 5; i++)
        {
            rig.Frame(now += 1 / 30.0, 1 / 30f, settings, false);
            if (feed.TryTake(out _)) snapshots++;
        }
        Assert.Equal(150, snapshots); // one a frame at 30 Hz

        // Minimized: detached for a while. The next attachment starts its Level/Beat plots over, and the flux history
        // the analyzer kept meanwhile isn't paired with stale entries.
        rig.Source.SetVisualizer(null);
        for (int i = 0; i < 60; i++) rig.Frame(now += 1 / 60.0, 1 / 60f, settings, false);
        rig.Source.SetVisualizer(feed);
        rig.Frame(now += 1 / 60.0, 1 / 60f, settings, false);
        Assert.True(feed.TryTake(out AnalyzerSnapshot restarted));
        Assert.Equal(VisualizerFeed.HistoryEntries, restarted.HistoryLength);
        int recorded = restarted.FrameSeconds.Take(restarted.HistoryLength).Count(dt => dt > 0);
        Assert.Equal(1, recorded);
        Assert.Equal(1 / 60f, restarted.FrameSeconds[restarted.HistoryLength - 1]);
    }
}
