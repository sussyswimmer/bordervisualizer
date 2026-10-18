using System.Globalization;
using System.Text;
using Rimlight.App.Visualizer;
using Rimlight.AudioTools;
using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Platform.Tests;

// "Record 2 min" in the debug visualizer (Maxwell's feedback, round 2): what the analyzer computed and what the light
// drew, one row per overlay frame, appended by the overlay thread to a buffer the UI allocated, then saved by the UI as a
// CSV that tools/audio-tools reads back and wav-analyze plots. Numbers only: never audio. Set RIMLIGHT_SAMPLE_RECORDING
// to a folder to keep the two-minute recording the end-to-end test saves (a sample for "wav-analyze plot").
public sealed class AnalysisRecordingTests(ITestOutputHelper output) : IDisposable
{
    private const float Dt = 1 / 60f;
    private static readonly DateTime Started = new(2026, 10, 18, 15, 30, 12);
    private readonly string directory = Directory.CreateTempSubdirectory("rimlight-recording-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void TheBufferHoldsEveryFrameInOrderOnTheAnalyzersClock()
    {
        var recording = new AnalysisRecording(capacity: 16, maxSeconds: 100);
        Assert.Equal(16, recording.Capacity);
        Assert.Equal(0, recording.Count);
        Assert.Equal(0, recording.Seconds);
        Assert.Equal(RecordingEnd.None, recording.End);

        double time = 0;
        for (int i = 0; i < 5; i++)
        {
            float dt = 0.01f * (i + 1);
            time += dt;
            Assert.True(Append(recording, i, dt));
        }

        Assert.Equal(5, recording.Count);
        RecordedFrame[] frames = recording.Frames(5).ToArray();
        for (int i = 0; i < 5; i++) AssertFrame(frames[i], i);
        Assert.Equal(time, frames[^1].Time, 12);   // the sum of the time steps, this frame's included
        Assert.Equal(0.01f, frames[0].Dt);
        Assert.Equal(0.01f, frames[0].Time, 6);
        Assert.Equal(frames[^1].Time, recording.Seconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => recording.Frames(6)); // not published yet
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisRecording(capacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisRecording(maxSeconds: double.NaN));
    }

    [Fact]
    public void AFullBufferStopsTakingFramesAndNeverWraps()
    {
        var recording = new AnalysisRecording(capacity: 5, maxSeconds: 100);
        for (int i = 0; i < 4; i++) Assert.True(Append(recording, i));
        Assert.False(Append(recording, 4)); // the fifth fills it: it is in, and the recording is over
        Assert.Equal(RecordingEnd.BufferFull, recording.End);
        for (int i = 5; i < 9; i++) Assert.False(Append(recording, i));

        Assert.Equal(5, recording.Count);
        Assert.Equal([0, 1, 2, 3, 4], recording.Frames(5).ToArray().Select(f => f.BeatCount)); // the first frames, kept
    }

    [Fact]
    public void ItEndsWithTheFrameThatReachesTwoMinutes()
    {
        var recording = new AnalysisRecording();
        Assert.Equal(120 * 240, AnalysisRecording.DefaultCapacity); // 2 minutes fit up to 240 frames a second
        Assert.Equal(AnalysisRecording.DefaultCapacity, recording.Capacity);
        Assert.Equal(120, recording.MaxDuration);

        int appended = 1;
        while (Append(recording, appended - 1)) appended++;

        Assert.Equal(RecordingEnd.TimeLimit, recording.End);
        Assert.Equal(120 * 60, appended);
        Assert.Equal(120 * 60, recording.Count);
        Assert.InRange(recording.Seconds, 120, 120 + Dt);
    }

    [Fact]
    public void StopKeepsWhatItHasAndEndsOnTheNextFrame()
    {
        var recording = new AnalysisRecording();
        for (int i = 0; i < 3; i++) Append(recording, i);

        recording.Stop(); // the UI: the frames published so far can be saved at once
        Assert.True(recording.StopRequested);
        Assert.Equal(3, recording.Count);
        Assert.Equal(RecordingEnd.None, recording.End); // the overlay thread hasn't seen it yet

        Assert.False(Append(recording, 3));
        Assert.Equal(RecordingEnd.Stopped, recording.End);
        Assert.Equal(3, recording.Count);
        recording.Drop(); // letting go afterwards doesn't change why it ended
        Assert.Equal(RecordingEnd.Stopped, recording.End);

        var dropped = new AnalysisRecording();
        Append(dropped, 0);
        dropped.Drop();
        Assert.Equal(RecordingEnd.Dropped, dropped.End);
        Assert.False(Append(dropped, 1));
        Assert.Equal(1, dropped.Count);
    }

    [Fact]
    public void ABadTimeStepCountsAsZero()
    {
        var recording = new AnalysisRecording();
        float[] steps = [0.02f, float.NaN, -1, float.PositiveInfinity, 0.01f];
        for (int i = 0; i < steps.Length; i++) Append(recording, i, steps[i]);

        RecordedFrame[] frames = recording.Frames(steps.Length).ToArray();
        Assert.Equal([0.02f, 0, 0, 0, 0.01f], frames.Select(f => f.Dt));
        Assert.Equal(0.03, frames[^1].Time, 6);
    }

    [Fact]
    public void AppendingAFrameAllocatesNothing()
    {
        var recording = new AnalysisRecording();
        int i = 0;
        for (; i < 2000; i++) Append(recording, i); // tier-up of the JIT can allocate on the calling thread
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (; i < 7000; i++) Append(recording, i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(7000, recording.Count);
    }

    [Fact]
    public void TheUiReadsOnlyWholeFramesWhileTheOverlayThreadAppends()
    {
        // Every field of frame i derives from i. A reader that could see a frame before its count was published would
        // sooner or later find one half written, or a count that went back.
        var recording = new AnalysisRecording(capacity: 100_000, maxSeconds: 1e9);
        using var start = new Barrier(2);
        var writer = new Thread(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < recording.Capacity; i++) Append(recording, i, 1e-3f);
        });
        writer.Start();
        start.SignalAndWait();
        int last = 0, reads = 0;
        bool over;
        do
        {
            over = recording.End != RecordingEnd.None; // before the count, so the last pass sees every frame
            int count = recording.Count;
            Assert.True(count >= last, $"the count went back from {last} to {count}");
            ReadOnlySpan<RecordedFrame> frames = recording.Frames(count).Span;
            for (int i = Math.Max(last - 1, 0); i < count; i++) AssertFrame(frames[i], i);
            last = count;
            reads++;
        }
        while (!over);
        writer.Join();
        Assert.Equal(recording.Capacity, last);
        Assert.Equal(RecordingEnd.BufferFull, recording.End);
        output.WriteLine($"{reads} reads while the writer appended {last} frames");
    }

    [Fact]
    public void TheOverlayRecordsEveryFrameWithTheLightItDrew()
    {
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        var feed = new VisualizerFeed();
        rig.Source.SetVisualizer(feed);
        rig.Source.SetAudioWanted(true);
        double now = 0;
        for (int i = 0; i < 120; i++) rig.Frame(now += Dt, Dt, settings, false); // the capture opens, the music plays

        var recording = new AnalysisRecording();
        feed.StartRecording(recording);
        var drawn = new List<LightState>();
        long delivered = 0;
        AnalyzerSnapshot? snapshot = null;
        for (int i = 0; i < 600; i++)
        {
            drawn.Add(rig.Frame(now += Dt, Dt, settings, false).State);
            delivered += rig.Delivered.Length;
            if (feed.TryTake(out AnalyzerSnapshot taken)) snapshot = taken;
        }

        Assert.Equal(600, recording.Count);
        Assert.Equal(RecordingEnd.None, recording.End);
        RecordedFrame[] frames = recording.Frames(600).ToArray();
        for (int i = 0; i < frames.Length; i++)
        {
            // The light state the frame returned to the overlay, exactly.
            Assert.Equal(drawn[i].Intensity, frames[i].Intensity);
            Assert.Equal(drawn[i].Pulse, frames[i].Pulse);
            Assert.Equal(drawn[i].Spread, frames[i].Spread);
            Assert.Equal(drawn[i].Visibility, frames[i].Visibility);
            Assert.Equal(drawn[i].Phase, frames[i].Phase);
            Assert.Equal(Dt, frames[i].Dt);
            Assert.True(frames[i].FollowsMusic);
        }
        Assert.Equal(600 * (double)Dt, frames[^1].Time, 6);
        Assert.Equal(delivered, frames.Sum(f => (long)f.Samples)); // how many samples the analyzer drained, as counts
        // The analyzer's own numbers on the last frame: at 60 Hz every frame publishes a snapshot, so the last one has them.
        Assert.NotNull(snapshot);
        Assert.Equal(snapshot.Features, frames[^1].Features);
        Assert.Equal(snapshot.Flux[snapshot.HistoryLength - 1], frames[^1].Flux);
        Assert.Equal(snapshot.Threshold[snapshot.HistoryLength - 1], frames[^1].Threshold);
        Assert.Equal(snapshot.BeatCount, frames[^1].BeatCount);
        Assert.Equal(snapshot.EstimatedBpm, frames[^1].Bpm);
        // The beats the snapshot's ticks show over its last 240 frames are the rises of the recorded beat count.
        int ticks = (int)snapshot.Fired.Take(snapshot.HistoryLength).Sum();
        Assert.Equal(frames[^1].BeatCount - frames[^241].BeatCount, ticks);
        Assert.InRange(ticks, 6, 10); // 124 BPM: about 8 beats in 4 s
        Assert.True(frames.Select(f => f.BeatCount).Zip(frames.Skip(1).Select(f => f.BeatCount)).All(p => p.Second >= p.First));
    }

    [Fact]
    public void ARecordingAllocatesNothingPerFrameAndEndsAfterTwoMinutes()
    {
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        var feed = new VisualizerFeed();
        rig.Source.SetVisualizer(feed);
        rig.Source.SetAudioWanted(true);
        double now = 0;
        // Warm-up, a short recording included, so the JIT has compiled every path a recording takes, and its end.
        for (int i = 0; i < 1500; i++) rig.Frame(now += Dt, Dt, settings, false);
        var warmUp = new AnalysisRecording(capacity: 600);
        feed.StartRecording(warmUp);
        for (int i = 0; i < 1500; i++) rig.Frame(now += Dt, Dt, settings, false);
        Assert.Equal(RecordingEnd.BufferFull, warmUp.End);

        AnalyzerSnapshot first = TakeSnapshot(feed);
        var recording = new AnalysisRecording();
        feed.StartRecording(recording);
        long bytes = 0;
        int frames = 0;
        while (recording.End == RecordingEnd.None && frames < 130 * 60)
        {
            rig.Frame(now += Dt, Dt, settings, false);
            bytes += rig.LastFrameBytes;
            frames++;
        }
        output.WriteLine($"{bytes} B over {frames} recorded frames");
        Assert.Equal(0, bytes);
        Assert.Equal(RecordingEnd.TimeLimit, recording.End);
        Assert.Equal(120 * 60, frames);
        Assert.Equal(120 * 60, recording.Count);

        // Over: the overlay lets go of it. Later frames add nothing and still allocate nothing.
        for (int i = 0; i < 120; i++)
        {
            rig.Frame(now += Dt, Dt, settings, false);
            bytes += rig.LastFrameBytes;
        }
        Assert.Equal(120 * 60, recording.Count);
        Assert.Equal(0, bytes);

        // What the UI saves, through the tools' reader: two minutes of the rig's 124 BPM loop.
        var info = new RecordingInfo("test", Started, RecordingSource.From(first, settings),
            RecordingSource.From(TakeSnapshot(feed), settings), recording.End);
        string path = AnalysisRecordingCsv.Save(directory, info, recording.Frames(recording.Count));
        Recording back = RecordingCsv.Read(path);
        AnalysisResult analysis = back.ToAnalysis();
        output.WriteLine($"{Path.GetFileName(path)}: {new FileInfo(path).Length} bytes, {analysis.BeatTimes.Count} beats, "
            + $"tempo {BeatStats.Tempo(analysis.BeatTimes):F1} BPM");
        Assert.Equal(120 * 60, back.Rows.Count);
        Assert.Equal("after 2 minutes", back.Comment("ended"));
        Assert.DoesNotContain("Test speakers", File.ReadAllText(path), StringComparison.Ordinal); // the rig's device: never named
        Assert.InRange(BeatStats.Tempo(analysis.BeatTimes), 122, 126);
        Assert.Equal(48000, analysis.SampleRate);
        if (Environment.GetEnvironmentVariable("RIMLIGHT_SAMPLE_RECORDING") is { Length: > 0 } keep)
        {
            Directory.CreateDirectory(keep);
            File.Copy(path, Path.Combine(keep, Path.GetFileName(path)), overwrite: true);
            output.WriteLine($"Kept a copy in {keep}");
        }
    }

    [Fact]
    public void TheRecordingEndsWhenTheVisualizerLetsGoOrANewOneStarts()
    {
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        var feed = new VisualizerFeed();
        rig.Source.SetVisualizer(feed);
        double now = 0;
        void Run(int frames)
        {
            for (int i = 0; i < frames; i++) rig.Frame(now += Dt, Dt, settings, false);
        }

        var first = new AnalysisRecording();
        feed.StartRecording(first);
        Run(30);
        var second = new AnalysisRecording();
        feed.StartRecording(second); // the window allows one at a time; a newer one takes over all the same
        Run(1);
        Assert.Equal(RecordingEnd.Dropped, first.End);
        Assert.Equal(30, first.Count);
        Assert.Equal(1, second.Count);

        rig.Source.SetVisualizer(null); // the window closed (it saved what it had first)
        Run(1);
        Assert.Equal(RecordingEnd.Dropped, second.End);
        rig.Source.SetVisualizer(feed); // reopened: nothing is recorded until it starts a new one
        Run(10);
        Assert.Equal(1, second.Count);
        Assert.Equal(30, first.Count);
    }

    [Fact]
    public void FramesWhoseLightDidntHearTheMusicAreMarked()
    {
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        var feed = new VisualizerFeed();
        rig.Source.SetVisualizer(feed);
        rig.Source.SetAudioWanted(true); // as the visualizer asks, so the analyzer hears the music in every mode
        double now = 0;
        var recording = new AnalysisRecording();
        feed.StartRecording(recording);
        void Run(int frames, Settings s, bool paused = false)
        {
            for (int i = 0; i < frames; i++) rig.Frame(now += Dt, Dt, s, paused);
        }

        Run(60, settings);                                                // music sync
        Run(60, settings, paused: true);                                  // a fullscreen app, say
        Run(60, settings with { Animation = AnimationMode.IdleGlow });
        rig.Source.SetDemo(true);
        Run(60, settings);
        rig.Source.SetDemo(false);
        Run(60, settings);

        RecordedFrame[] frames = recording.Frames(recording.Count).ToArray();
        Assert.Equal(300, frames.Length);
        Assert.Equal([60, 0, 0, 0, 60], frames.Chunk(60).Select(c => c.Count(f => f.FollowsMusic)));
        Assert.True(frames[119].Visibility < 0.01f); // paused: the light faded out over 300 ms, while the analysis went on
        Assert.True(frames[60..120].Select(f => f.BeatCount).Distinct().Count() > 1);
    }

    [Fact]
    public void TheHeaderSaysWhereTheNumbersCameFromAndNeverNamesTheDevice()
    {
        var settings = new Settings { Sensitivity = 1.5f, Brightness = 0.9f };
        RecordingSource atStart = Source(settings, new AudioTuning { Sensitivity = 1.5f }, dropped: 100);
        RecordedFrame[] frames = Frames(120);
        string csv = Write(new RecordingInfo("9.8.7", Started, atStart, atStart with { DroppedSamples = 112 }, RecordingEnd.Stopped), frames);
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("# Rimlight analysis recording (\"Record 2 min\" in the debug visualizer)", lines[0]);
        Assert.Contains("# privacy: numbers only. No audio samples, no spectrum, nothing the sound could be rebuilt from.", lines);
        Assert.Contains("# app: Rimlight 9.8.7", lines);
        Assert.Contains("# started: 2026-10-18 15:30:12 local time", lines);
        Assert.Contains("# length: 2.000 s, 120 frames", lines);
        Assert.Contains("# ended: stopped by hand", lines);
        Assert.Contains(lines, l => l.StartsWith("# frameRate: 60.00 frames a second, measured; fpsCap=60 ", StringComparison.Ordinal));
        Assert.Contains("# capture: 48000 Hz, 2 ch, 32-bit float, captured at 48000 Hz, analyzed at 48000 Hz; 12 samples dropped during the recording", lines);
        Assert.Contains("# settings: enabled=true animation=MusicSync whenSilent=IdleGlow brightness=0.9 glow=0.45 coreThicknessDip=6 "
            + "cornerRadiusDip=0 primaryRatio=0.6 sensitivity=1.5 fpsCap=60 onBattery=Reduce pauseInFullscreen=true", lines);
        string tuning = lines.Single(l => l.StartsWith("# tuning: ", StringComparison.Ordinal))["# tuning: ".Length..];
        Assert.Equal(new AudioTuning { Sensitivity = 1.5f }, TuningJson.Parse(tuning)); // H-015's format: wav-analyze --tuning reads it
        Assert.Contains("# demo: off", lines);
        Assert.DoesNotContain(lines, l => l.Contains("AtEnd:", StringComparison.Ordinal)); // nothing changed meanwhile
        Assert.Contains(lines, l => l.StartsWith("# columns: ", StringComparison.Ordinal));
        Assert.Equal(AnalysisRecordingCsv.Header, lines.Single(l => !l.StartsWith('#') && l.StartsWith("time,", StringComparison.Ordinal)));
        Assert.Equal(RecordingCsv.Header, AnalysisRecordingCsv.Header); // the app writes what the tools read
        Assert.Equal(12 + 1 + 120, lines.Length); // the comments, the header row, one row per frame

        // Changes during the recording: a device change, a Sensitivity slider, a tuning edit and the demo.
        RecordingSource atEnd = Source(settings with { Sensitivity = 0.5f }, new AudioTuning { Sensitivity = 0.5f, BeatDecaySeconds = 0.2f }, dropped: 3)
            with { Format = "44100 Hz, 2 ch, 16-bit PCM", CaptureSampleRate = 44100, AnalysisSampleRate = 44100, Demo = true };
        lines = Write(new RecordingInfo("9.8.7", Started, atStart, atEnd, RecordingEnd.TimeLimit), frames).Split('\n');
        Assert.Contains("# ended: after 2 minutes", lines);
        Assert.Contains("# capture: 48000 Hz, 2 ch, 32-bit float, captured at 48000 Hz, analyzed at 48000 Hz; 3 samples dropped by the capture at the end", lines);
        Assert.Contains("# captureAtEnd: 44100 Hz, 2 ch, 16-bit PCM, captured at 44100 Hz, analyzed at 44100 Hz", lines);
        Assert.Contains(lines, l => l.StartsWith("# settingsAtEnd: ", StringComparison.Ordinal) && l.Contains(" sensitivity=0.5 ", StringComparison.Ordinal));
        string tuningAtEnd = lines.Single(l => l.StartsWith("# tuningAtEnd: ", StringComparison.Ordinal))["# tuningAtEnd: ".Length..];
        Assert.Equal(atEnd.Tuning, TuningJson.Parse(tuningAtEnd));
        Assert.Contains("# demoAtEnd: on", lines);

        // No device captured: said so, with no rates.
        lines = Write(new RecordingInfo("9.8.7", Started, atStart with { Format = null, CaptureSampleRate = 0 },
            atStart with { Format = null, CaptureSampleRate = 0 }, RecordingEnd.Dropped), frames).Split('\n');
        Assert.Contains(lines, l => l.StartsWith("# capture: none (no output device was captured);", StringComparison.Ordinal));
        Assert.Contains("# ended: when the visualizer let go of it", lines);
    }

    [Fact]
    public void TheFileReadsBackThroughTheToolsReaderOnAnyPc()
    {
        // Real frames from the frame source, written where a comma is the decimal separator.
        var settings = new Settings();
        using var rig = new GlowRig(settings);
        var feed = new VisualizerFeed();
        rig.Source.SetVisualizer(feed);
        double now = 0;
        for (int i = 0; i < 60; i++) rig.Frame(now += Dt, Dt, settings, false);
        var recording = new AnalysisRecording();
        feed.StartRecording(recording);
        for (int i = 0; i < 900; i++) rig.Frame(now += Dt, i % 7 == 3 ? Dt * 1.37f : Dt, settings, i is > 500 and < 560);
        RecordedFrame[] frames = recording.Frames(recording.Count).ToArray();
        string csv = Write(new RecordingInfo("9.8.7", Started, Source(settings, new AudioTuning(), 0), Source(settings, new AudioTuning(), 0), RecordingEnd.Stopped), frames,
            CommaDecimals());
        Assert.All(csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')), l => Assert.Equal(17, l.Split(',').Length));

        Recording back = RecordingCsv.Read(new StringReader(csv), "test.csv");

        Assert.True(back.HasLight);
        Assert.Equal(frames.Length, back.Rows.Count);
        for (int i = 0; i < frames.Length; i++)
        {
            RecordedFrame f = frames[i];
            RecordingRow r = back.Rows[i];
            Assert.Equal(f.Time, r.Time, 6);
            Assert.Equal(f.Dt, r.Dt, 6);
            Assert.Equal(f.Samples, r.Samples);
            Assert.Equal(f.Features.Level, r.Features.Level, 4);
            Assert.Equal(f.Features.Bass, r.Features.Bass, 4);
            Assert.Equal(f.Features.Beat, r.Features.Beat, 4);
            Assert.Equal(f.Features.IsSilent, r.Features.IsSilent);
            Assert.Equal(f.BeatCount, r.BeatCount);
            Assert.Equal(f.Flux, r.Flux, Math.Abs(f.Flux) * 1e-5f + 1e-12f);
            Assert.Equal(f.Threshold, r.Threshold, Math.Abs(f.Threshold) * 1e-5f + 1e-12f);
            Assert.Equal(f.Bpm, r.Bpm, 2);
            Assert.Equal(f.Intensity, r.Intensity, 4);
            Assert.Equal(f.Pulse, r.Pulse, 4);
            Assert.Equal(f.Spread, r.Spread, 4);
            Assert.Equal(f.Visibility, r.Visibility, 4);
            Assert.Equal(f.Phase, r.Phase, 5);
            Assert.Equal(f.FollowsMusic, r.FollowsMusic);
        }
        Assert.Contains(back.Rows, r => !r.FollowsMusic); // the paused stretch
        Assert.Equal("Rimlight 9.8.7", back.Comment("app"));
        Assert.Equal("off", back.Comment("demo"));

        // The beats are where the count rose, as the visualizer's ticks.
        AnalysisResult analysis = back.ToAnalysis();
        int rises = frames.Zip(frames.Skip(1), (a, b) => Math.Max(0, b.BeatCount - a.BeatCount)).Sum();
        Assert.Equal(rises, analysis.BeatTimes.Count);
        Assert.True(rises >= 20, $"{rises} beats");
        Assert.Equal(frames.Length, analysis.Frames.Count);
        Assert.Equal(frames[^1].Time, analysis.AudioSeconds, 6);
    }

    [Fact]
    public void SavingWritesANewFileNamedAfterTheStart()
    {
        string logs = Path.Combine(directory, "logs"); // created by the save
        RecordedFrame[] frames = Frames(30);
        var info = new RecordingInfo("9.8.7", Started, Source(new Settings(), new AudioTuning(), 0), Source(new Settings(), new AudioTuning(), 0), RecordingEnd.Stopped);

        string first = AnalysisRecordingCsv.Save(logs, info, frames);
        string second = AnalysisRecordingCsv.Save(logs, info, frames); // started in the same second

        Assert.Equal(Path.Combine(logs, "analysis-20261018-153012.csv"), first);
        Assert.Equal(Path.Combine(logs, "analysis-20261018-153012-2.csv"), second);
        Assert.Equal(File.ReadAllText(first), File.ReadAllText(second));
        Assert.Equal((byte)'#', File.ReadAllBytes(first)[0]); // UTF-8 without a byte order mark
        Assert.Equal(Write(info, frames), File.ReadAllText(first).ReplaceLineEndings("\n"));
        Assert.Equal(@"%APPDATA%\Rimlight\logs\analysis-20261018-153012.csv", AnalysisRecordingCsv.DisplayPath(first));
        Assert.Equal("analysis-20261018-153012.csv", AnalysisRecordingCsv.FileName(Started));
    }

    // Frame i of a test recording: every field derives from i.
    private static bool Append(AnalysisRecording recording, int i, float dt = Dt)
    {
        var features = new AudioFeatures(i * 1e-3f, i * 2e-3f, i * 3e-3f, i % 2 == 0);
        var light = new LightState(new Rgb(1, 0, 0), new Rgb(0, 0, 1), 0.6f, i * 4e-3f, 0.45f, 6, i * 5e-4f, i * 5e-3f, 1);
        return recording.Append(dt, i * 10, in features, i, i * 0.5f, i * 0.25f, 120, in light, i % 3 != 0);
    }

    private static void AssertFrame(in RecordedFrame frame, int i)
    {
        if (frame.BeatCount == i && frame.Samples == i * 10 && frame.Features.Level == i * 1e-3f && frame.Features.Beat == i * 3e-3f
            && frame.Features.IsSilent == (i % 2 == 0) && frame.Flux == i * 0.5f && frame.Threshold == i * 0.25f && frame.Bpm == 120
            && frame.Intensity == i * 4e-3f && frame.Pulse == i * 5e-3f && frame.Phase == i * 5e-4f && frame.FollowsMusic == (i % 3 != 0))
            return;
        Assert.Fail($"Frame {i} is not whole: {frame}");
    }

    private static RecordedFrame[] Frames(int count)
    {
        var recording = new AnalysisRecording(capacity: count, maxSeconds: 1e9);
        for (int i = 0; i < count; i++) Append(recording, i);
        return recording.Frames(count).ToArray();
    }

    private static RecordingSource Source(Settings settings, AudioTuning tuning, long dropped) =>
        new(settings, tuning, "48000 Hz, 2 ch, 32-bit float", 48000, 48000, dropped, Demo: false);

    private static string Write(RecordingInfo info, RecordedFrame[] frames, CultureInfo? culture = null)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture ?? before;
        try
        {
            var text = new StringWriter(new StringBuilder(), CultureInfo.CurrentCulture) { NewLine = "\n" };
            AnalysisRecordingCsv.Write(text, info, frames);
            return text.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // A culture that writes 0,5 for a half, as many PCs do; built by hand, so it exists without ICU too.
    private static CultureInfo CommaDecimals()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        return culture;
    }

    private static AnalyzerSnapshot TakeSnapshot(VisualizerFeed feed)
    {
        Assert.True(feed.TryTake(out AnalyzerSnapshot snapshot) || snapshot.Sequence > 0, "no snapshot yet");
        return snapshot;
    }
}
