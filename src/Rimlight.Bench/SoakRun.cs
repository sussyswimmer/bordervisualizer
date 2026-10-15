using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Rimlight.AudioTools;
using Rimlight.Core;

namespace Rimlight.Bench;

internal sealed record SoakOptions
{
    /// <summary>Simulated minutes; the default is doc 07 Phase 7's 8 hours.</summary>
    public double Minutes { get; init; } = 8 * 60;
    public int SampleRate { get; init; } = 48000;
    public double Fps { get; init; } = 60;
    /// <summary>Each frame interval varies by up to this share either way.</summary>
    public double Jitter { get; init; } = 0.2;
    /// <summary>The loopback packet length in seconds (doc 03 §1: about 10 ms); 0 delivers single samples.</summary>
    public double PacketSeconds { get; init; } = 0.01;
    public ulong Seed { get; init; } = 1;
    /// <summary>Simulated seconds processed before measuring starts (JIT and tier-up happen there).</summary>
    public double WarmupSeconds { get; init; } = 5;
    /// <summary>Simulated minutes between progress lines; 0 = ten lines per run.</summary>
    public double ReportEveryMinutes { get; init; }
    /// <summary>The day to simulate, at least <see cref="Minutes"/> long; null: <see cref="SoakSchedule.Generate"/>.</summary>
    public SoakSchedule? Schedule { get; init; }
    /// <summary>
    /// Wall-clock seconds <see cref="SoakSchedule.Scene"/> runs on a throwaway pipeline before the soak (at least once),
    /// so every path is compiled, tiered up and has run its static initializers before anything is measured.
    /// </summary>
    public double JitWarmupSeconds { get; init; } = 0.5;
    /// <summary>Simulated seconds between memory readings in the measured part (the progress lines read it too).</summary>
    public double MemoryEverySeconds { get; init; } = 60;
    /// <summary>
    /// Simulated minutes after warm-up (at most an eighth of the measured time) at which the flatness check takes its
    /// working-set baseline. One-time growth lands before it: each code path is compiled and tiered up, and the runtime
    /// pages it needs are faulted in, the first few times it runs, and a schedule reaches some paths only after a while.
    /// A leak keeps growing after it.
    /// </summary>
    public double SettleMinutes { get; init; } = 60;
}

internal sealed record SoakSnapshot(double SimulatedSeconds, long Frames, double MeanNs, double P99Ns, long MaxNs,
    double BytesPerFrame, long ManagedHeapBytes, long WorkingSetBytes, int Gen0, int Gen1, int Gen2, int Beats, int Tracks);

/// <summary>A memory reading.</summary>
/// <param name="WorkingSetBytes">The process working set.</param>
/// <param name="ManagedHeapBytes">The managed heap, garbage not yet collected included (GC.GetTotalMemory(false)).</param>
internal readonly record struct MemoryReading(long WorkingSetBytes, long ManagedHeapBytes)
{
    /// <summary>Reads both, without allocating.</summary>
    /// <returns>The reading.</returns>
    public static MemoryReading Now() => new(ProcessMemory.WorkingSetBytes(), GC.GetTotalMemory(false));
}

// Simulated-time soak of the app's render thread (doc 07 Phase 7, C12). Every frame runs FramePipeline, i.e. what
// MusicGlowSource.NextFrame runs (analyzer, palette blender + gradient, light engine), on the frame schedule PaceModel
// gives it (full rate for music, 10 Hz for Idle Glow and for listening while hidden, nothing while blocked), over a
// SoakSchedule: tracks with new album colors, silent gaps with and without packets, settings edits, pauses, the hotkey
// and stalls. It runs as fast as the CPU allows: 8 simulated hours take minutes.
//
// Only NextFrame is timed and allocation-counted (GC.GetAllocatedBytesForCurrentThread around it). Generating the
// audio, applying events and reading memory happen between frames. The first WarmupSeconds are processed but not
// measured; before them, SoakSchedule.Scene runs on a throwaway pipeline so one-time costs (JIT, tier-up, static
// initializers) of paths the soak reaches only hours in don't count either. Memory: the working set and managed heap
// are read every MemoryEverySeconds (allocation-free, into a MemoryLog), and the live heap after a full GC at the end
// of warm-up and at the end; see SoakReport for the pass criteria.
internal static class SoakRun
{
    /// <summary>The loopback stream opens this long after the capture is asked for it.</summary>
    internal const double CaptureOpenSeconds = 0.1;
    /// <summary>The loopback ring holds this much audio (doc 03 §1), rounded up to a power of two samples.</summary>
    internal const double RingSeconds = 2;

    /// <summary>Runs a soak and returns its progress snapshots.</summary>
    /// <param name="options">The options.</param>
    /// <param name="progress">Called with each snapshot as it is taken.</param>
    /// <returns>The snapshots.</returns>
    public static IReadOnlyList<SoakSnapshot> Run(SoakOptions options, Action<SoakSnapshot>? progress = null) =>
        Execute(options, progress).Snapshots;

    /// <summary>Runs a soak.</summary>
    /// <param name="options">The options.</param>
    /// <param name="progress">Called with each snapshot as it is taken.</param>
    /// <returns>The report.</returns>
    public static SoakReport Execute(SoakOptions options, Action<SoakSnapshot>? progress = null)
    {
        MemoryReading start = MemoryReading.Now();
        double seconds = options.Minutes * 60;
        SoakSchedule schedule = options.Schedule ?? SoakSchedule.Generate(seconds, options.SampleRate, options.Seed);
        if (schedule.Seconds < seconds - 1e-9) throw new ArgumentException("The schedule is shorter than the run.", nameof(options));

        var wall = Stopwatch.StartNew();
        SoakSchedule scene = SoakSchedule.Scene(options.SampleRate);
        do new Simulation(scene, options, scene.Seconds, measure: false, progress: null).Run();
        while (wall.Elapsed.TotalSeconds < options.JitWarmupSeconds);
        double jitWarmupWall = wall.Elapsed.TotalSeconds;
        double clockRead = StageCosts.ClockReadNanoseconds();
        // The progress line's formatting runs first an hour or so in; compile it now, outside the measured part.
        _ = Format(new SoakSnapshot(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        wall.Restart();
        var simulation = new Simulation(schedule, options, seconds, measure: true, progress);
        simulation.Run();
        double wallSeconds = wall.Elapsed.TotalSeconds;
        return simulation.Report(start, wallSeconds, jitWarmupWall, clockRead);
    }

    public static string Format(SoakSnapshot s)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        var simulated = TimeSpan.FromSeconds(s.SimulatedSeconds);
        return string.Create(c,
            $"{(int)simulated.TotalHours}:{simulated.Minutes:00}:{simulated.Seconds:00}  {s.Frames,9} frames  " +
            $"mean {s.MeanNs / 1000,6:F1} µs  p99 {s.P99Ns / 1000,6:F1} µs  max {s.MaxNs / 1000.0,7:F1} µs  " +
            $"{s.BytesPerFrame,5:F1} B/frame  heap {s.ManagedHeapBytes / 1048576.0,6:F1} MB  " +
            $"working set {s.WorkingSetBytes / 1048576.0,6:F1} MB  GCs {s.Gen0}/{s.Gen1}/{s.Gen2}  beats {s.Beats}  tracks {s.Tracks}");
    }

    // One run over a schedule: the simulated capture, media service, overlay loop and render thread.
    private sealed class Simulation
    {
        private readonly SoakSchedule schedule;
        private readonly SoakOptions options;
        private readonly double end, warmup, reportEvery, fullInterval;
        private readonly bool measure;
        private readonly Action<SoakSnapshot>? progress;
        private readonly FramePipeline pipeline;
        private readonly PaceModel pacer = new();
        private readonly DeterministicRandom random;
        private readonly long packet, ringCapacity;
        private readonly float[] buffer;

        // The stream and the capture.
        private readonly SyntheticTrack?[] tracksBySegment; // built up front, so starting a track allocates nothing
        private int segment = -1;            // the segment the stream is in, for Fill
        private long position;               // the next sample of the stream to deliver
        private bool captureActive, captureOpen, captureChanged;
        private double captureOpensAt;

        // The app's inputs.
        private SettingsSnapshot settings;
        private bool paused;
        private Palette? album;
        private int albumScan;               // the next segment whose album the media service publishes
        private int eventIndex;
        private double pendingStall;

        // Measurements.
        private readonly StageCosts costs = new();
        private readonly List<SoakSnapshot> snapshots;
        private readonly long[] framesByMotion = new long[4];
        private readonly double[] secondsByPace = new double[3];
        private bool measuring;
        private long frames, warmupFrames, allocated, allocatingFrames, maxFrameBytes;
        private long refills, paletteSets, resets, silentFrames, hiddenFrames, staticFrames, stallFrames;
        private int beats, lastBeatCount, tracks;
        private int gen0, gen1, gen2;
        private double measuredFrom, nextReport, nextMemory;
        private readonly MemoryLog memory;
        private long liveHeapAfterWarmup;

        public Simulation(SoakSchedule schedule, SoakOptions options, double end, bool measure, Action<SoakSnapshot>? progress)
        {
            this.schedule = schedule;
            this.options = options;
            this.end = end;
            this.measure = measure;
            this.progress = progress;
            warmup = Math.Min(options.WarmupSeconds, end / 2);
            reportEvery = (options.ReportEveryMinutes > 0 ? options.ReportEveryMinutes : options.Minutes / 10) * 60;
            fullInterval = 1 / options.Fps;
            settings = schedule.Initial;
            pipeline = new FramePipeline(settings, Palette.Default);
            random = new DeterministicRandom(options.Seed);
            packet = options.PacketSeconds > 0 ? Math.Max(1, (long)Math.Round(options.PacketSeconds * options.SampleRate)) : 1;
            ringCapacity = (long)BitOperations.RoundUpToPowerOf2((uint)Math.Ceiling(RingSeconds * options.SampleRate));
            // Everything the run holds is allocated and touched here, before the memory baseline: the buffer's pages
            // would otherwise be faulted in one by one as the first long stall fills it, which reads as growth.
            buffer = new float[ringCapacity];
            buffer.AsSpan().Clear();
            tracksBySegment = new SyntheticTrack?[schedule.Segments.Count];
            for (int i = 0; i < schedule.Segments.Count && schedule.Segments[i].Start < end; i++)
            {
                if (schedule.Segments[i].Track is { } track) tracksBySegment[i] = new SyntheticTrack(track);
            }
            snapshots = new List<SoakSnapshot>((int)Math.Min(100_000, end / Math.Max(reportEvery, 1) + 2));
            memory = new MemoryLog((int)Math.Min(1_000_000, end / Math.Max(options.MemoryEverySeconds, 1) + snapshots.Capacity + 4));
        }

        public void Run()
        {
            double time = 0, previousTime = 0;
            float dt = 0;                    // the app's first frame comes at once
            bool blocked = false;
            while (time < end)
            {
                ApplyEvents(time, blocked);
                PublishAlbum(time);
                int count = Capture(time, out int sampleRate);
                bool changed = captureChanged;
                captureChanged = false;
                if (measure && !measuring && time >= warmup) StartMeasuring(time);

                long before = GC.GetAllocatedBytesForCurrentThread();
                FrameMotion motion = pipeline.NextFrame(buffer.AsSpan(0, count), sampleRate, dt, settings, album, paused, changed);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

                AnalyzerDiagnostics diagnostics = pipeline.Analyzer.Diagnostics;
                if (changed || diagnostics.BeatCount < lastBeatCount) lastBeatCount = 0;
                beats += diagnostics.BeatCount - lastBeatCount;
                lastBeatCount = diagnostics.BeatCount;

                LightState state = pipeline.State;
                Pace pace = pacer.Next(time, motion, in state, dt, pipeline.Refilled);
                (double next, bool due) = NextFrameTime(time, pace);
                if (measuring) Record(motion, pace, bytes, changed, time, Math.Min(next, end) - time);
                else warmupFrames++;

                previousTime = time;
                time = next;
                blocked = pace == Pace.Block;
                dt = PaceModel.FrameDt(time - previousTime, fullInterval, due);
                if (measuring && (time >= nextReport || time >= end)) Snapshot(Math.Min(time, end));
            }
        }

        // Events up to now, applied between frames as the UI thread and the host would. A stall the render thread
        // would have slept through (it was blocked) doesn't happen.
        private void ApplyEvents(double now, bool blocked)
        {
            IReadOnlyList<SoakEvent> events = schedule.Events;
            for (; eventIndex < events.Count && events[eventIndex].Time <= now; eventIndex++)
            {
                SoakEvent e = events[eventIndex];
                switch (e.Kind)
                {
                    case SoakEventKind.Settings:
                        settings = e.Settings!;
                        pacer.NoteInputChanged(now);
                        break;
                    case SoakEventKind.Pause or SoakEventKind.Resume:
                        paused = e.Kind == SoakEventKind.Pause;
                        pacer.NoteInputChanged(now);
                        break;
                    case SoakEventKind.Stall when !blocked:
                        pendingStall += e.StallSeconds;
                        break;
                }
            }
        }

        // The media service: each track's palette appears AlbumDelaySeconds after the track starts, and the app asks the
        // overlay for a frame (OverlayHost.RequestFrame, an input change for the pacer).
        private void PublishAlbum(double now)
        {
            IReadOnlyList<SoakSegment> segments = schedule.Segments;
            for (; albumScan < segments.Count && segments[albumScan].Start + SoakSchedule.AlbumDelaySeconds <= now; albumScan++)
            {
                if (segments[albumScan].Audio != SoakAudio.Music) continue;
                album = segments[albumScan].Album;
                tracks++;
                pacer.NoteInputChanged(now);
            }
        }

        // LoopbackCapture: open while the glow listens (the stream starts CaptureOpenSeconds after it is asked for),
        // closed otherwise. Fills `buffer` with what arrived since the last frame, in whole packets; returns the count.
        private int Capture(double now, out int sampleRate)
        {
            bool listen = FramePipeline.Listens(settings.Settings, paused);
            if (listen != captureActive)
            {
                captureActive = listen;
                if (listen) captureOpensAt = now + CaptureOpenSeconds;
                else if (captureOpen)
                {
                    captureOpen = false;
                    captureChanged = true;
                }
            }
            long arrived = Arrived(now);
            if (captureActive && !captureOpen && now >= captureOpensAt)
            {
                captureOpen = true;
                captureChanged = true;
                position = Arrived(captureOpensAt);
            }
            sampleRate = captureOpen ? options.SampleRate : FramePipeline.FallbackSampleRate;
            if (!captureOpen)
            {
                position = arrived;
                return 0;
            }
            if (arrived - position > ringCapacity) position = arrived - ringCapacity; // the ring kept the newest only
            int count = Fill(position, arrived);
            position = arrived;
            return count;
        }

        private long Arrived(double now) => (long)(now * options.SampleRate) / packet * packet;

        // The stream's samples [from, to): music from the segment's track, zeros for silent packets, nothing for gaps
        // without packets.
        private int Fill(long from, long to)
        {
            IReadOnlyList<SoakSegment> segments = schedule.Segments;
            int count = 0;
            for (long i = from; i < to;)
            {
                while (segment + 1 < segments.Count && i >= StartSample(segments[segment + 1])) segment++;
                if (segment < 0) segment = 0;
                SoakSegment s = segments[segment];
                long segmentEnd = segment + 1 < segments.Count ? StartSample(segments[segment + 1]) : long.MaxValue;
                if (i >= segmentEnd) break;
                int n = (int)(Math.Min(to, segmentEnd) - i);
                if (s.Audio == SoakAudio.Music)
                {
                    SyntheticTrack track = tracksBySegment[segment]!;
                    track.Position = Math.Clamp(i - StartSample(s), 0, track.Length);
                    int read = track.Read(buffer.AsSpan(count, n));
                    buffer.AsSpan(count + read, n - read).Clear();
                    count += n;
                }
                else if (s.Audio == SoakAudio.SilentPackets)
                {
                    buffer.AsSpan(count, n).Clear();
                    count += n;
                }
                i += n;
            }
            return count;
        }

        private long StartSample(SoakSegment s) => (long)Math.Round(s.Start * options.SampleRate);

        // The overlay loop: the next frame comes after the pace's interval (jittered, plus any stall), or earlier when
        // an input arrives (settings, pause, a new album palette), though not before a stall ends; while blocked only an
        // input brings it.
        private (double Next, bool Due) NextFrameTime(double now, Pace pace)
        {
            double wake = NextInput(now);
            if (pace == Pace.Block)
            {
                pendingStall = 0;
                return (Math.Min(wake, end), false);
            }
            double interval = pace == Pace.Full ? fullInterval : Math.Max(PaceModel.SlowInterval, fullInterval);
            double due = now + interval * (1 + options.Jitter * random.NextSigned()) + pendingStall;
            double earliest = now + pendingStall;
            if (pendingStall > 0 && measuring) stallFrames++;
            pendingStall = 0;
            return wake < due ? (Math.Max(wake, earliest), false) : (due, true);
        }

        // The time of the next input after now: a settings, pause or resume event, or an album publication.
        private double NextInput(double now)
        {
            double next = double.PositiveInfinity;
            IReadOnlyList<SoakEvent> events = schedule.Events;
            for (int i = eventIndex; i < events.Count; i++)
            {
                if (events[i].Time > now && events[i].Kind != SoakEventKind.Stall)
                {
                    next = events[i].Time;
                    break;
                }
            }
            IReadOnlyList<SoakSegment> segments = schedule.Segments;
            for (int i = albumScan; i < segments.Count; i++)
            {
                double publish = segments[i].Start + SoakSchedule.AlbumDelaySeconds;
                if (segments[i].Audio == SoakAudio.Music && publish > now)
                {
                    next = Math.Min(next, publish);
                    break;
                }
            }
            return next;
        }

        private void StartMeasuring(double now)
        {
            measuring = true;
            measuredFrom = now;
            nextReport = now + reportEvery;
            nextMemory = now + options.MemoryEverySeconds;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            liveHeapAfterWarmup = GC.GetTotalMemory(true);
            memory.Add(now, MemoryReading.Now());
            (gen0, gen1, gen2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        }

        private void Record(FrameMotion motion, Pace pace, long bytes, bool reset, double now, double untilNext)
        {
            frames++;
            costs.Record(pipeline.Ticks);
            allocated += bytes;
            if (bytes > 0)
            {
                allocatingFrames++;
                maxFrameBytes = Math.Max(maxFrameBytes, bytes);
            }
            framesByMotion[(int)motion]++;
            secondsByPace[(int)pace] += Math.Max(0, untilNext);
            if (pipeline.Refilled) refills++;
            if (pipeline.PaletteSet) paletteSets++;
            if (reset) resets++;
            if (pipeline.Features.IsSilent) silentFrames++;
            if (pipeline.State.Visibility < PaceModel.MinVisibility) hiddenFrames++;
            if (pipeline.Engine.IsStatic) staticFrames++;
            if (now >= nextMemory)
            {
                memory.Add(now, MemoryReading.Now());
                nextMemory += options.MemoryEverySeconds;
            }
        }

        private void Snapshot(double now)
        {
            MemoryReading reading = MemoryReading.Now();
            memory.Add(now, reading);
            LatencyHistogram frame = costs[Stage.Frame];
            var snapshot = new SoakSnapshot(now, frames, frame.Mean, frame.Percentile(0.99), frame.Max,
                frames == 0 ? 0 : allocated / (double)frames, reading.ManagedHeapBytes, reading.WorkingSetBytes,
                GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2, beats, tracks);
            snapshots.Add(snapshot);
            progress?.Invoke(snapshot);
            while (nextReport <= now) nextReport += reportEvery;
        }

        public SoakReport Report(MemoryReading start, double wallSeconds, double jitWarmupWallSeconds, double clockReadNs)
        {
            memory.Add(end, MemoryReading.Now());
            (int g0, int g1, int g2) = (GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long liveHeapEnd = GC.GetTotalMemory(true);
            double settleAt = measuredFrom + Math.Min(options.SettleMinutes * 60, (end - measuredFrom) / 8);
            return new SoakReport
            {
                Options = options,
                Schedule = schedule,
                SimulatedSeconds = end,
                MeasuredSeconds = end - measuredFrom,
                Snapshots = snapshots,
                Costs = costs,
                Frames = frames,
                WarmupFrames = warmupFrames,
                AllocatedBytes = allocated,
                AllocatingFrames = allocatingFrames,
                MaxFrameBytes = maxFrameBytes,
                Gen0 = g0,
                Gen1 = g1,
                Gen2 = g2,
                Start = start,
                Memory = memory,
                SettledIndex = memory.IndexAt(settleAt),
                LiveHeapAfterWarmup = liveHeapAfterWarmup,
                LiveHeapEnd = liveHeapEnd,
                FramesByMotion = framesByMotion,
                SecondsByPace = secondsByPace,
                Refills = refills,
                PaletteSets = paletteSets,
                Resets = resets,
                SilentFrames = silentFrames,
                HiddenFrames = hiddenFrames,
                StaticFrames = staticFrames,
                StallFrames = stallFrames,
                Beats = beats,
                Tracks = tracks,
                WallSeconds = wallSeconds,
                JitWarmupWallSeconds = jitWarmupWallSeconds,
                ClockReadNanoseconds = clockReadNs,
            };
        }
    }
}
