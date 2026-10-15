using Rimlight.AudioTools;
using Rimlight.Bench;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Tests.Tools;

// C12: the render-thread frame, its schedule and the soak's statistics. Runs are short; the timings are
// informational, the allocations, paths and counts are not.
public sealed class SoakTests
{
    private const int Rate = 48000;

    [Fact]
    public void GeneratedDayCoversItsLengthWithEveryKindOfMoment()
    {
        const double Day = 8 * 3600;
        SoakSchedule day = SoakSchedule.Generate(Day, Rate, 1);

        Assert.Equal(Day, day.Seconds);
        Assert.Equal(0, day.Segments[0].Start);
        Assert.True(day.Segments[^1].End >= Day);
        for (int i = 1; i < day.Segments.Count; i++) Assert.Equal(day.Segments[i - 1].End, day.Segments[i].Start, 9);

        SoakSegment first = day.Segments[0];
        Assert.Equal(SoakAudio.Music, first.Audio);
        Assert.Equal(124, first.Track!.Bpm);
        Assert.Equal(0.2, first.Track.BassLevel);   // the C3 soak's full mix
        Assert.Equal(1, first.Track.Gain);

        SoakSegment[] music = day.Segments.Where(s => s.Audio == SoakAudio.Music).ToArray();
        SoakSegment[] gaps = day.Segments.Where(s => s.Audio != SoakAudio.Music).ToArray();
        Assert.InRange(music.Length, 70, 130);
        Assert.All(music.Skip(1), s => Assert.InRange(s.Seconds, 150, 330));
        Assert.All(music, s => Assert.Equal(Rate, s.Track!.SampleRate));
        Assert.Contains(music, s => s.Album is null);
        Assert.Contains(music, s => s.Track!.BassLevel == 0);          // kick + vocal tones only
        Assert.Contains(music, s => s.Track!.Gain < 0.1);              // below −20 dB
        Assert.Equal(music.Length - 1, gaps.Length);                  // a gap before every track but the first
        Assert.Contains(gaps, s => s.Audio == SoakAudio.SilentPackets);
        Assert.Contains(gaps, s => s.Audio == SoakAudio.NoPackets);
        Assert.Contains(gaps, s => s.Seconds < 2.5);                   // too short for IsSilent
        Assert.Contains(gaps, s => s.Seconds is >= 4 and <= 30);
        Assert.Contains(gaps, s => s.Seconds >= 60);

        SoakEvent[] events = day.Events.ToArray();
        Assert.All(events, e => Assert.InRange(e.Time, 0, Day));
        for (int i = 1; i < events.Length; i++) Assert.True(events[i].Time >= events[i - 1].Time);
        SoakEvent[] edits = events.Where(e => e.Kind == SoakEventKind.Settings && !e.Label.StartsWith("hotkey", StringComparison.Ordinal)).ToArray();
        Assert.InRange(edits.Length, 15, 60);
        Assert.True(edits[0].Time >= 120);
        Assert.Equal("WhenSilent Hide", edits[0].Label);
        foreach (string name in new[] { "WhenSilent IdleGlow", "Brightness", "Sensitivity", "PrimaryRatio" })
            Assert.Contains(edits, e => e.Label.StartsWith(name, StringComparison.Ordinal));
        Assert.All(events.Where(e => e.Settings is not null), e => Assert.InRange(e.Settings!.Sensitivity, 0.25f, 2f));

        SoakEvent[] stalls = events.Where(e => e.Kind == SoakEventKind.Stall).ToArray();
        Assert.InRange(stalls.Length, 60, 140);
        Assert.True(stalls[0].Time >= 60);
        Assert.Contains(stalls, e => e.StallSeconds > 0.25);
        Assert.All(stalls, e => Assert.InRange(e.StallSeconds, 0.03, 1.5));

        // Away periods: fullscreen pauses and the hotkey, each closed by its own end, never overlapping.
        SoakEvent[] away = events.Where(e => e.Kind is SoakEventKind.Pause or SoakEventKind.Resume || e.Label.StartsWith("hotkey", StringComparison.Ordinal)).ToArray();
        Assert.Contains(away, e => e.Kind == SoakEventKind.Pause);
        Assert.Contains(away, e => e.Label == "hotkey: off");
        Assert.True(away[0].Time >= 1200);
        for (int i = 0; i + 1 < away.Length; i += 2)
        {
            Assert.True(away[i].Kind == SoakEventKind.Pause ? away[i + 1].Kind == SoakEventKind.Resume : away[i + 1].Label == "hotkey: on");
            Assert.InRange(away[i + 1].Time - away[i].Time, 120, 900);
        }
        bool enabled = true;
        foreach (SoakEvent e in events.Where(e => e.Settings is not null))
        {
            if (e.Label == "hotkey: off") enabled = false;
            if (e.Label == "hotkey: on") enabled = true;
            Assert.Equal(enabled, e.Settings!.Enabled); // edits while the hotkey is off keep it off
        }
    }

    [Fact]
    public void GenerationIsAPureFunctionOfItsArguments()
    {
        static string Describe(SoakSchedule s) => string.Join(";",
            s.Segments.Select(x => $"{x.Start:R},{x.Seconds:R},{x.Audio},{x.Track?.Bpm},{x.Track?.Gain:R},{x.Album}")
                .Concat(s.Events.Select(e => $"{e.Time:R},{e.Kind},{e.Label},{e.StallSeconds:R}")));
        string a = Describe(SoakSchedule.Generate(3 * 3600, Rate, 7));
        Assert.Equal(a, Describe(SoakSchedule.Generate(3 * 3600, Rate, 7)));
        Assert.NotEqual(a, Describe(SoakSchedule.Generate(3 * 3600, Rate, 8)));
        Assert.StartsWith(Describe(SoakSchedule.Generate(600, Rate, 7)).Split(';')[0], a); // a shorter day starts the same way
    }

    [Fact]
    public void ScheduleRejectsHolesAndDisorder()
    {
        var settings = new Settings();
        var track = new SyntheticTrackOptions { Seconds = 10 };
        Assert.Throws<ArgumentException>(() => new SoakSchedule(10, settings, [new SoakSegment(0, 5, SoakAudio.NoPackets)], []));
        Assert.Throws<ArgumentException>(() => new SoakSchedule(10, settings, [new SoakSegment(0, 5, SoakAudio.NoPackets), new SoakSegment(6, 5, SoakAudio.NoPackets)], []));
        Assert.Throws<ArgumentException>(() => new SoakSchedule(10, settings, [new SoakSegment(0, 10, SoakAudio.Music)], []));
        Assert.Throws<ArgumentException>(() => new SoakSchedule(10, settings, [new SoakSegment(0, 10, SoakAudio.NoPackets, track)], []));
        Assert.Throws<ArgumentException>(() => new SoakSchedule(10, settings, [new SoakSegment(0, 10, SoakAudio.Music, track)],
            [new SoakEvent(5, SoakEventKind.Stall, "stall", StallSeconds: 0.1), new SoakEvent(4, SoakEventKind.Stall, "stall", StallSeconds: 0.1)]));
        Assert.Throws<ArgumentException>(() => new SoakSchedule(10, settings, [new SoakSegment(0, 10, SoakAudio.Music, track)],
            [new SoakEvent(5, SoakEventKind.Settings, "no settings")]));
    }

    [Fact]
    public void SceneTakesEveryPathOfTheFrameWithoutAllocating()
    {
        SoakSchedule scene = SoakSchedule.Scene(Rate);
        char[] line = new char[SoakRun.MaxLineLength];
        int lines = 0;
        SoakReport report = SoakRun.Execute(new SoakOptions
        {
            Minutes = scene.Seconds / 60,
            Schedule = scene,
            WarmupSeconds = 3,
            JitWarmupSeconds = 0,
            ReportEveryMinutes = 0.25,
            MemoryEverySeconds = 5,
        }, s => lines += SoakRun.Format(s, line) > 0 ? 1 : 0);

        Assert.True(report.AllocationFree, report.AllocationVerdict);
        Assert.Equal(0, report.AllocatingFrames);
        Assert.Equal(5, report.SettingsEdits);       // WhenSilent, Sensitivity + ratio, hotkey off and on, WhenSilent
        Assert.Equal(1, report.AllocatingEdits);     // the Sensitivity edit rebuilds the AudioTuning record, as in the app
        Assert.True(report.SettingsEditBytes > 0);
        Assert.Equal(0, report.HarnessBytes);        // nothing between the frames allocates, progress lines included
        Assert.Equal(report.Snapshots.Count, lines);
        Assert.Equal(report.Frames, report.Costs.Count);
        Assert.Equal(scene.Seconds - 3, report.MeasuredSeconds, 1);
        Assert.Equal(report.MeasuredSeconds, report.SecondsByPace.Sum(), 1);
        Assert.All(report.FramesByMotion, count => Assert.True(count > 0));     // full, slow, listening and still
        Assert.Equal(report.Frames, report.FramesByMotion.Sum());
        Assert.InRange(report.SecondsByPace[(int)Pace.Block], 4, 7);            // most of the pause and the hotkey off
        Assert.True(report.SecondsByPace[(int)Pace.Slow] > 3);                  // Idle Glow and listening while hidden
        Assert.Equal(4, report.Resets);                                         // pause, resume, hotkey off, hotkey on
        Assert.InRange(report.PaletteSets, 3, 3);                               // album b, manual (no art), album d
        Assert.True(report.Refills >= report.PaletteSets + 1);                  // crossfades, the ratio edit
        Assert.True(report.SilentFrames > 0 && report.HiddenFrames > 0 && report.StaticFrames > 0);
        Assert.Equal(1, report.StallFrames);
        Assert.Equal(4, report.Tracks);
        Assert.True(report.Beats > 60, $"{report.Beats} beats");
        Assert.InRange(report.Snapshots.Count, 5, 6);
        Assert.Equal(scene.Seconds, report.Snapshots[^1].SimulatedSeconds, 6);
        Assert.True(report.AfterWarmup.WorkingSetBytes > 0 && report.Peak.WorkingSetBytes >= report.AfterWarmup.WorkingSetBytes);
        Assert.InRange(report.Memory.Count, 16, 30);                             // every 5 s, plus snapshots and the end
        Assert.InRange(report.SettledAt, 3 + 77 / 8.0, 3 + 77 / 8.0 + 5.5);     // an eighth of a short run
        Assert.True(report.LiveHeapEnd > 0 && report.LiveHeapAfterWarmup > 0);
        Assert.True(report.CoreShare > 0);

        var text = new StringWriter();
        report.WriteSummary(text);
        Assert.Contains("PASS 0 B/frame", text.ToString());
        Assert.Contains("memory flat", text.ToString());
        Assert.Contains("Settings edits: 5 applied by measured frames; applying them allocated", text.ToString());
        var markdown = new StringWriter();
        report.WriteMarkdown(markdown);
        Assert.Contains("| whole frame |", markdown.ToString());
        Assert.Contains("| light engine |", markdown.ToString());
        Assert.Contains("4 tracks (1 without art, 0 with the previous track's colors), 3 gaps", markdown.ToString());
        Assert.Contains("1 fullscreen pause, 1 hotkey off, 1 stall (1 over 0.25 s)", markdown.ToString());
        Assert.Contains("| methods compiled by the JIT |", markdown.ToString());
        Assert.True(report.End.CompiledMethods >= report.AfterWarmup.CompiledMethods && report.AfterWarmup.CompiledMethods > 0);
    }

    [Fact]
    public void MemoryFlatnessFollowsTheStatedLimits()
    {
        // Readings every 10 s from the end of warm-up (5 s) to the end (80 s); the settle point is the third.
        // The working set grows 3 MB before it (one-time growth, not judged), then by `workingSetGrowth` evenly.
        MemoryLog Log(long workingSetGrowth)
        {
            var log = new MemoryLog(16);
            log.Add(5, new MemoryReading(30 << 20, 2 << 20));
            log.Add(15, new MemoryReading(32 << 20, 2 << 20));
            for (int i = 0; i <= 6; i++) log.Add(25 + 55.0 * i / 6, new MemoryReading((33 << 20) + workingSetGrowth * i / 6, 2 << 20));
            return log;
        }
        SoakReport Report(long workingSetGrowth, long heapGrowth, long allocated) => new()
        {
            Options = new SoakOptions(),
            Schedule = SoakSchedule.Scene(Rate),
            SimulatedSeconds = 80,
            MeasuredSeconds = 77,
            Snapshots = [],
            Costs = new StageCosts(),
            Frames = 1000,
            WarmupFrames = 10,
            AllocatedBytes = allocated,
            AllocatingFrames = allocated > 0 ? 1 : 0,
            MaxFrameBytes = allocated,
            SettingsEdits = 0,
            AllocatingEdits = 0,
            SettingsEditBytes = 0,
            HarnessBytes = 0,
            Gen0 = 0,
            Gen1 = 0,
            Gen2 = 0,
            Start = new MemoryReading(10 << 20, 1 << 20),
            Memory = Log(workingSetGrowth),
            SettledIndex = 2,
            LiveHeapAfterWarmup = 1 << 20,
            LiveHeapEnd = (1 << 20) + heapGrowth,
            FramesByMotion = [1000, 0, 0, 0],
            SecondsByPace = [77, 0, 0],
            Refills = 0,
            PaletteSets = 0,
            Resets = 0,
            SilentFrames = 0,
            HiddenFrames = 0,
            StaticFrames = 0,
            StallFrames = 0,
            Beats = 0,
            Tracks = 0,
            WallSeconds = 1,
            JitWarmupWallSeconds = 0,
            ClockReadNanoseconds = 20,
        };

        SoakReport flat = Report(SoakReport.WorkingSetGrowthLimit - 1, SoakReport.LiveHeapGrowthLimit - 1, 0);
        Assert.True(flat.Passed);
        Assert.Equal(SoakReport.WorkingSetGrowthLimit - 1 + (3 << 20), flat.WorkingSetGrowthSinceWarmup);
        Assert.Equal(25, flat.SettledAt);
        Assert.Equal((33 << 20) + SoakReport.WorkingSetGrowthLimit - 1, flat.Peak.WorkingSetBytes);
        Assert.Equal((SoakReport.WorkingSetGrowthLimit - 1) / 55.0 * 3600, flat.WorkingSetTrendPerHour, 1);
        Assert.Contains("trend from 0:00:25 on", flat.MemoryContext);
        Assert.True(Report(-(5 << 20), -4096, 0).MemoryFlat);                   // shrinking is flat too
        Assert.False(Report(SoakReport.WorkingSetGrowthLimit, 0, 0).MemoryFlat);
        Assert.False(Report(0, SoakReport.LiveHeapGrowthLimit, 0).MemoryFlat);
        SoakReport allocating = Report(0, 0, 24);
        Assert.False(allocating.AllocationFree);
        Assert.False(allocating.Passed);
        Assert.StartsWith("FAIL 0 B/frame: 24 bytes allocated in 1,000 measured frames", allocating.AllocationVerdict);
        Assert.Contains("must be under +1,024 KB", Report(0, 0, 0).MemoryVerdict);
        Assert.StartsWith("FAIL memory flat", Report(2 << 20, 0, 0).MemoryVerdict);
    }

    [Fact]
    public void PipelineRefillsAndReportsMotionLikeTheApp()
    {
        var settings = new Settings();
        var pipeline = new FramePipeline(settings, Palette.Default);
        float[] music = SyntheticTrack.Render(new SyntheticTrackOptions { SampleRate = Rate, Seconds = 4, Bpm = 120 }.WithFullMix());
        var album = new Palette(new Rgb(1, 0, 0), new Rgb(0, 0, 1), "album");
        const float Dt = 1 / 60f;
        int offset = 0;
        FrameMotion Frame(Settings s, Palette? latest, bool paused = false, bool changed = false)
        {
            FrameMotion motion = pipeline.NextFrame(music.AsSpan(offset, 800), Rate, Dt, s, latest, paused, changed);
            offset = (offset + 800) % (music.Length - 800);
            return motion;
        }

        Assert.Equal(FrameMotion.Full, Frame(settings, null, changed: true));
        Assert.True(pipeline.Refilled);                                   // the first frame fills the gradient
        Assert.False(pipeline.PaletteSet);
        Assert.Equal(FrameMotion.Full, Frame(settings, null));
        Assert.False(pipeline.Refilled);

        // New album colors: an 800 ms crossfade, refilled every frame and once more after it ends (H-006).
        Assert.Equal(FrameMotion.Full, Frame(settings, album));
        Assert.True(pipeline.PaletteSet && pipeline.Refilled);
        int refills = 1, frames = 1;
        while (frames < 60)
        {
            Frame(settings, album);
            frames++;
            if (pipeline.Refilled) refills++;
            else break;
        }
        Assert.InRange(refills, 48, 50);
        Frame(settings, album);
        Assert.False(pipeline.Refilled);
        Assert.True(pipeline.Gradient[19 * 4] > 0.99f && pipeline.Gradient[19 * 4 + 2] < 0.01f); // mid-Primary: the album's red

        // A ratio edit refills; a pause hides the glow and, once it is gone, the frame source asks for no frames.
        Settings ratio = settings with { PrimaryRatio = 0.4f };
        Frame(ratio, album);
        Assert.True(pipeline.Refilled);
        Assert.True(pipeline.SettingsApplied);
        Assert.Equal(0, pipeline.SettingsBytes);                          // a ratio edit allocates nothing

        // A Sensitivity edit retunes the analyzer the way the app does: `with` on the AudioTuning record, one object on
        // the render thread, which the frame reports as SettingsBytes. Nothing else in the frame allocates.
        Settings sensitive = ratio with { Sensitivity = 9 };                // clamped to 2, as the app clamps it
        long frameStart = GC.GetAllocatedBytesForCurrentThread();
        Frame(sensitive, album);
        long frameBytes = GC.GetAllocatedBytesForCurrentThread() - frameStart;
        Assert.Equal(2f, pipeline.Analyzer.Tuning.Sensitivity);
        Assert.True(pipeline.SettingsApplied);
        Assert.True(pipeline.SettingsBytes > 0);
        Assert.Equal(frameBytes, pipeline.SettingsBytes);
        Frame(sensitive, album);
        Assert.False(pipeline.SettingsApplied);
        Assert.Equal(0, pipeline.SettingsBytes);
        FrameMotion motion = FrameMotion.Full;
        for (int i = 0; i < 40 && motion != FrameMotion.Still; i++) motion = Frame(ratio, album, paused: true);
        Assert.Equal(FrameMotion.Still, motion);
        Assert.True(pipeline.Engine.IsStatic);
        Assert.Equal(0, pipeline.State.Visibility);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) Frame(i % 200 < 100 ? ratio : settings, i % 120 < 60 ? album : null, paused: i % 300 > 250, changed: i % 150 == 0);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void PaceModelFollowsTheFramePacer()
    {
        var visible = new LightState(default, default, 0.6f, 0.8f, 0.45f, 6, 0, 0, 1);
        LightState hidden = visible with { Visibility = 0 };
        var pacer = new PaceModel();
        Assert.Equal(Pace.Full, pacer.Next(10, FrameMotion.Full, visible, 1 / 60f, false));
        Assert.Equal(Pace.Full, pacer.Next(10.4, FrameMotion.Slow, visible, 0.1f, false));  // the light appeared 0.4 s ago
        Assert.Equal(Pace.Slow, pacer.Next(11, FrameMotion.Slow, visible, 0.1f, false));
        Assert.Equal(Pace.Slow, pacer.Next(11.1, FrameMotion.Listening, hidden, 0.1f, false));
        Assert.Equal(Pace.Block, pacer.Next(11.2, FrameMotion.Still, hidden, 0.1f, false));
        Assert.Equal(Pace.Slow, pacer.Next(11.3, FrameMotion.Full, hidden, 0.1f, false));   // nothing on screen
        Assert.Equal(Pace.Full, pacer.Next(11.31, FrameMotion.Full, hidden with { Visibility = 1.6e-3f }, 0.007f, false)); // fading in (C13)
        Assert.Equal(Pace.Full, pacer.Next(12, FrameMotion.Slow, visible, 0.1f, false));    // shown again: 0.5 s at full rate
        Assert.Equal(Pace.Slow, pacer.Next(12.6, FrameMotion.Slow, visible, 0.1f, false));

        pacer.NoteInputChanged(20);                                                        // busy for 1 s
        Assert.Equal(Pace.Full, pacer.Next(20.9, FrameMotion.Slow, visible, 0.1f, false));
        Assert.Equal(Pace.Slow, pacer.Next(21.1, FrameMotion.Slow, visible, 0.1f, false));
        Assert.Equal(Pace.Full, pacer.Next(22, FrameMotion.Slow, visible, 0.1f, true));     // a refill holds 0.5 s
        Assert.Equal(Pace.Full, pacer.Next(22.4, FrameMotion.Slow, visible, 0.1f, false));
        Assert.Equal(Pace.Slow, pacer.Next(22.6, FrameMotion.Slow, visible, 0.1f, false));
        Assert.Equal(Pace.Full, pacer.Next(23, FrameMotion.Slow, visible with { Intensity = 0.7f }, 0.1f, false)); // 1/s
        Assert.Equal(Pace.Block, pacer.Next(23.1, FrameMotion.Still, hidden, 0.1f, false)); // Still blocks even when busy

        Assert.Equal(1 / 60f, PaceModel.FrameDt(5, 1 / 60.0, due: true));                   // a long gap: one frame
        Assert.Equal(0.1f, PaceModel.FrameDt(0.1, 1 / 60.0, due: true));
        Assert.Equal(1 / 60f, PaceModel.FrameDt(0.05, 1 / 60.0, due: false));              // brought forward
        Assert.Equal(0.01f, PaceModel.FrameDt(0.01, 1 / 60.0, due: false));
    }

    [Fact]
    public void FrameBenchmarkAllocatesNothingAtAnyRate()
    {
        var options = new FrameBenchmarkOptions { WarmupFrames = 200, MinWarmupSeconds = 0, Frames = 600 };
        foreach (int rate in FrameBenchmark.DefaultRates)
        {
            FrameBenchmarkResult steady = FrameBenchmark.Run(rate, "steady", options);
            FrameBenchmarkResult crossfade = FrameBenchmark.Run(rate, "crossfade", options);
            Assert.Equal((int)Math.Round(rate / 60.0), steady.SamplesPerFrame);
            Assert.Equal(0, steady.BytesPerFrame);
            Assert.Equal(0, crossfade.BytesPerFrame);
            Assert.Equal(600, steady.Costs.Count);
            Assert.Equal(0, steady.Refills);
            Assert.Equal(600, crossfade.Refills);                         // every frame crossfades
            Assert.True(steady.Beats > 10, $"{rate} Hz: {steady.Beats} beats");
            foreach (Stage stage in StageCosts.All) Assert.True(crossfade.Costs[stage].Mean > 0, $"{rate} Hz {stage}");
        }
        Assert.Equal(800, FrameBenchmark.Run(48000, "steady", options with { Frames = 10 }).SamplesPerFrame);
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameBenchmark.Run(48000, "jazz", options));

        // 10 ms packets, as the capture delivers them: one or two a frame, still 0 B.
        FrameBenchmarkResult packets = FrameBenchmark.Run(48000, "crossfade", options with { PacketSeconds = 0.01 });
        Assert.Equal(480, packets.PacketSamples);
        Assert.Equal(800, packets.SamplesPerFrame);
        Assert.Equal(0, packets.BytesPerFrame);
        Assert.Equal(600, packets.Refills);
        Assert.True(packets.Beats > 10, $"{packets.Beats} beats");
        Assert.Equal(0, FrameBenchmark.Run(48000, "steady", options with { Frames = 10 }).PacketSamples);
    }

    [Fact]
    public void WorkingSetReadsWithoutAllocating()
    {
        long reading = ProcessMemory.WorkingSetBytes();
        long environment = Environment.WorkingSet;
        Assert.InRange(reading, environment / 2, environment * 2);       // the same counter, read a moment apart
        for (int i = 0; i < 10; i++) ProcessMemory.WorkingSetBytes();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) ProcessMemory.WorkingSetBytes();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(StageCosts.ClockReadNanoseconds() > 0);
    }

    [Fact]
    public void MemoryLogKeepsTheLatestReadingWhenFull()
    {
        var log = new MemoryLog(3);
        for (int i = 0; i < 6; i++) log.Add(i * 10, new MemoryReading((10 + i) << 20, 1 << 20));
        Assert.Equal(3, log.Count);
        Assert.Equal(0, log.TimeAt(0));                                     // the end of warm-up stays first
        Assert.Equal(10, log.TimeAt(1));
        Assert.Equal(50, log.TimeAt(2));                                    // the end stays last
        Assert.Equal(15 << 20, log[2].WorkingSetBytes);
        Assert.Equal(15 << 20, log.Peak.WorkingSetBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryLog(1));
    }

    [Fact]
    public void ProgressLinesFormatWithoutAllocatingAndAreBounded()
    {
        var snapshot = new SoakSnapshot(3 * 3600 + 7 * 60 + 5.4, 1_234_567, 91_678, 281_600, 32_896_267, 0, 2_031_616, 36_175_872, 0, 1, 2, 47_875, 105);
        const string Expected = "3:07:05    1234567 frames  mean   91.7 µs  p99  281.6 µs  max 32896.3 µs    0.0 B/frame  heap    1.9 MB  working set   34.5 MB  GCs 0/1/2  beats 47875  tracks 105";
        Assert.Equal(Expected, SoakRun.Format(snapshot));
        char[] line = new char[SoakRun.MaxLineLength];
        for (int i = 0; i < 10; i++) SoakRun.Format(snapshot, line);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int length = 0;
        for (int i = 0; i < 100; i++) length = SoakRun.Format(snapshot, line);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(Expected, new string(line, 0, length));

        // One line per ReportEveryMinutes after warm-up, plus the end; too many is refused before anything runs.
        Assert.Equal(11, SoakRun.ProgressLines(new SoakOptions { Minutes = 60, WarmupSeconds = 0 }));
        Assert.Equal(8 * 60 + 1, SoakRun.ProgressLines(new SoakOptions { ReportEveryMinutes = 1 }));
        var tooMany = new SoakOptions { Minutes = 60, ReportEveryMinutes = 0.005 };
        Assert.True(SoakRun.ProgressLines(tooMany) > SoakRun.MaxProgressLines);
        Assert.Throws<ArgumentOutOfRangeException>(() => SoakRun.Execute(tooMany));
    }

    [Theory]
    [InlineData("8h", 8 * 3600)]
    [InlineData("90m", 5400)]
    [InlineData("45s", 45)]
    [InlineData("1h30m", 5400)]
    [InlineData("0.5H", 1800)]
    public void DurationsParse(string text, double seconds) => Assert.Equal(seconds, BenchCli.Duration(text));

    [Theory]
    [InlineData("8")]
    [InlineData("h")]
    [InlineData("8x")]
    [InlineData("")]
    [InlineData("2s")]
    [InlineData("49h")]
    [InlineData("-1h")]
    public void BadDurationsAreUsageErrors(string text) => Assert.Throws<UsageException>(() => BenchCli.Duration(text));

    [Fact]
    public void CommandLineRunsFrameAndSoak()
    {
        var output = new StringWriter();
        Assert.Equal(0, BenchCli.Run(["frame", "--rates", "48000", "--frames", "200", "--warmup", "10"], output, TextWriter.Null));
        Assert.Contains("| 48000 | 800 | steady |", output.ToString());
        Assert.Contains("| 48000 | crossfade |", output.ToString());
        Assert.Equal(2, BenchCli.Run(["frame", "--rates", "7000"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, BenchCli.Run(["frame", "--rates", ","], TextWriter.Null, TextWriter.Null));
        var packets = new StringWriter();
        Assert.Equal(0, BenchCli.Run(["frame", "--rates", "44100", "--packet-ms", "10", "--frames", "200", "--warmup", "10"], packets, TextWriter.Null));
        Assert.Contains("audio in 10 ms packets", packets.ToString());
        Assert.Contains("| 44100 | 735 | steady |", packets.ToString());
        Assert.Equal(2, BenchCli.Run(["soak", "--duration", "8"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, BenchCli.Run(["soak", "--duration", "1m", "--minutes", "1"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, BenchCli.Run(["soak", "--duration", "8h", "--report-every", "0.01"], TextWriter.Null, TextWriter.Null));

        // The working-set verdict depends on the whole test process (other tests run in parallel), so only the
        // allocation verdict and the output are checked here.
        var soak = new StringWriter();
        int code = BenchCli.Run(["soak", "--duration", "20s", "--warmup-seconds", "3", "--markdown"], soak, TextWriter.Null);
        Assert.NotEqual(2, code);
        Assert.Contains("PASS 0 B/frame", soak.ToString());
        Assert.Contains("#### Soak: 0:00:20 simulated, seed 1", soak.ToString());
        Assert.Contains("| blender + gradient |", soak.ToString());
    }
}
