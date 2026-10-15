using System.Diagnostics;
using System.Globalization;
using Rimlight.AudioTools;
using Rimlight.Core;

namespace Rimlight.Bench;

internal sealed record SoakOptions
{
    /// <summary>Simulated hours; the default is doc 07 Phase 7's 8-hour soak.</summary>
    public double Hours { get; init; } = 8;
    public int SampleRate { get; init; } = 48000;
    public double Fps { get; init; } = 60;
    public double Jitter { get; init; } = 0.2;
    public double PacketSeconds { get; init; } = 0.01;
    public ulong Seed { get; init; } = 1;
    /// <summary>Real seconds of warm-up before measuring: the tour, repeated (at least once) until this much time has
    /// passed, so tiered JIT has installed optimized code for every path that is then measured.</summary>
    public double WarmupSeconds { get; init; } = 2;
    /// <summary>Simulated minutes between progress lines; 0 = sixteen lines per run (every 30 minutes of 8 hours).</summary>
    public double ReportEveryMinutes { get; init; }
    public SoakProgramShape Shape { get; init; } = SoakProgramShape.Default;
    /// <summary>Test seam: wraps the light engine the soak drives (null: CoreFactory's, as the app uses it).</summary>
    public Func<ILightEngine, ILightEngine>? WrapLightEngine { get; init; }

    public double Seconds => Hours * 3600;
}

/// <summary>A per-frame cost distribution, in nanoseconds.</summary>
internal sealed record CostSummary(double MeanNs, double P50Ns, double P99Ns, double P999Ns, long MaxNs)
{
    public static CostSummary From(LatencyHistogram h) =>
        new(h.Mean, h.Percentile(0.5), h.Percentile(0.99), h.Percentile(0.999), h.Count == 0 ? 0 : h.Max);
}

/// <summary>Process memory at one moment. LiveManagedBytes is measured after a full, compacting collection.</summary>
internal sealed record MemorySample(long LiveManagedBytes, long WorkingSetBytes, int Handles);

/// <summary>A progress line: costs over the interval since the previous one, everything else since measuring began.</summary>
internal sealed record SoakSnapshot(double SimulatedSeconds, long Frames, CostSummary Interval, long AllocatedBytes,
    long AllocatingFrames, long HeapBytes, long WorkingSetBytes, int Gen0, int Gen1, int Gen2, long Beats);

internal sealed record SoakResult
{
    /// <summary>Live managed memory may grow this much over a run (the progress lines are kept for the report).</summary>
    public const long MaxManagedGrowthBytes = 256 * 1024;

    public required SoakOptions Options { get; init; }
    public required SoakProgramSummary Program { get; init; }
    public required int WarmupTours { get; init; }
    public required double WarmupSimulatedSeconds { get; init; }
    public required double WarmupWallSeconds { get; init; }
    public required double WallSeconds { get; init; }
    public required long Frames { get; init; }
    /// <summary>Everything the render thread does per frame below (doc 02 render-thread steps 1–2).</summary>
    public required CostSummary Frame { get; init; }
    /// <summary>IAudioAnalyzer.Process, and Reset on a device change.</summary>
    public required CostSummary Analyzer { get; init; }
    /// <summary>ILightEngine.Update.</summary>
    public required CostSummary LightEngine { get; init; }
    /// <summary>IPaletteBlender SetTarget (on a new track), Update and FillGradient.</summary>
    public required CostSummary Palette { get; init; }
    /// <summary>Bytes allocated by the measured frames, on the render thread.</summary>
    public required long AllocatedBytes { get; init; }
    public required long AllocatingFrames { get; init; }
    /// <summary>Simulated time of the first allocating frame, or null.</summary>
    public required double? FirstAllocationSeconds { get; init; }
    /// <summary>Bytes allocated by the whole process while measuring, minus what the progress lines allocated: the
    /// harness (streaming the program between frames) and every other thread.</summary>
    public required long ProcessAllocatedBytes { get; init; }
    public required int Gen0 { get; init; }
    public required int Gen1 { get; init; }
    public required int Gen2 { get; init; }
    public required MemorySample Start { get; init; }
    public required MemorySample End { get; init; }
    public required long PeakWorkingSetBytes { get; init; }
    public required long Beats { get; init; }
    public required long SilentFrames { get; init; }
    public required long HiddenFrames { get; init; }
    public required long StaticFrames { get; init; }
    public required IReadOnlyList<SoakSnapshot> Snapshots { get; init; }

    public double BytesPerFrame => Frames == 0 ? 0 : AllocatedBytes / (double)Frames;
    public long ManagedGrowthBytes => End.LiveManagedBytes - Start.LiveManagedBytes;
    public bool AllocationFree => Frames > 0 && AllocatedBytes == 0;
    public bool ManagedMemoryFlat => ManagedGrowthBytes <= MaxManagedGrowthBytes;
    public bool Passed => AllocationFree && ManagedMemoryFlat;
    /// <summary>Mean frame cost × frame rate, as a share of one CPU core.</summary>
    public double CorePercent => Frame.MeanNs * Options.Fps / 1e9 * 100;
}

// The C12 soak (doc 07 Phase 7, doc 09 C12): the real analyzer, light engine and palette blender run for hours of
// simulated time on a synthetic listening program (SoakProgram), fed the way the render thread feeds them: jittered
// frames, whole 10 ms capture packets, empty spans while no packets arrive, a SetTarget on every new track and a Reset
// on every device change. It runs as fast as the CPU allows, so 8 hours take minutes.
//
// Per frame, exactly what the app's render thread does (MusicGlowSource.NextFrame, doc 02 steps 1–2) is timed and
// allocation-counted on this thread: Process, LightEngine.Update, and the blender's Update and FillGradient. The app
// fills the gradient only while colors change; the soak fills it every frame, an upper bound. Streaming the program
// between frames replaces draining the ring buffer and is not measured.
//
// Before measuring, the warm-up tour (every segment kind, both silent behaviors, a device change, palette fades) runs
// for at least WarmupSeconds of real time, so tiered JIT has optimized every measured path and static constructors
// have run: "steady state" is everything after it, and there allocations must be 0. Cost histograms take constant
// memory, so a long run holds no more memory than a short one.
internal static class SoakRun
{
    private static readonly TimeSpan AlbumFade = TimeSpan.FromMilliseconds(800); // the app's album crossfade (doc 05 §3)

    public static SoakResult Run(SoakOptions options, Action<SoakSnapshot>? progress = null, Action<SoakProgram>? programReady = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        SoakProgram program = SoakProgram.Generate(options.Seconds, options.SampleRate, options.Seed, options.Shape);
        SoakProgram tour = SoakProgram.Tour(options.SampleRate, options.Seed);
        programReady?.Invoke(program);
        var simulator = new Simulator(options);

        // Warm-up: the tour until WarmupSeconds of real time have passed, then nothing it did is kept.
        long warmupStart = Stopwatch.GetTimestamp();
        int tours = 0;
        do
        {
            simulator.Play(tour, double.PositiveInfinity, null);
            tours++;
        }
        while (Stopwatch.GetElapsedTime(warmupStart).TotalSeconds < options.WarmupSeconds);
        double warmupWall = Stopwatch.GetElapsedTime(warmupStart).TotalSeconds;
        int gen0 = 0, gen1 = 0, gen2 = 0;
        SoakSnapshot Snapshot(double time) => new(time, simulator.Frames, CostSummary.From(simulator.Interval), simulator.Allocated,
            simulator.AllocatingFrames, GC.GetTotalMemory(false), Environment.WorkingSet,
            GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2, simulator.Beats);
        _ = Format(Snapshot(0)); // the progress line's code runs (and is compiled) before the start sample, not after
        simulator.ClearMeasurements();

        MemorySample start = Memory();
        var snapshots = new List<SoakSnapshot>(64);
        (gen0, gen1, gen2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        long reportingBytes = 0;
        long processStart = GC.GetTotalAllocatedBytes(precise: true);
        long runStart = Stopwatch.GetTimestamp();

        double reportEvery = options.ReportEveryMinutes > 0 ? options.ReportEveryMinutes * 60 : program.Seconds / 16;
        simulator.Play(program, reportEvery, time =>
        {
            long before = GC.GetTotalAllocatedBytes(precise: true);
            SoakSnapshot snapshot = Snapshot(time);
            snapshots.Add(snapshot);
            progress?.Invoke(snapshot);
            reportingBytes += GC.GetTotalAllocatedBytes(precise: true) - before;
        });

        double wall = Stopwatch.GetElapsedTime(runStart).TotalSeconds;
        long processAllocated = GC.GetTotalAllocatedBytes(precise: true) - processStart - reportingBytes;
        (int g0, int g1, int g2) = (GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);
        MemorySample end = Memory();
        long peak;
        using (var process = Process.GetCurrentProcess()) peak = process.PeakWorkingSet64;

        return new SoakResult
        {
            Options = options,
            Program = program.Summary,
            WarmupTours = tours,
            WarmupSimulatedSeconds = tours * tour.Seconds,
            WarmupWallSeconds = warmupWall,
            WallSeconds = wall,
            Frames = simulator.Frames,
            Frame = CostSummary.From(simulator.Frame),
            Analyzer = CostSummary.From(simulator.Analyzer),
            LightEngine = CostSummary.From(simulator.Engine),
            Palette = CostSummary.From(simulator.Colors),
            AllocatedBytes = simulator.Allocated,
            AllocatingFrames = simulator.AllocatingFrames,
            FirstAllocationSeconds = simulator.FirstAllocation >= 0 ? simulator.FirstAllocation : null,
            ProcessAllocatedBytes = processAllocated,
            Gen0 = g0,
            Gen1 = g1,
            Gen2 = g2,
            Start = start,
            End = end,
            PeakWorkingSetBytes = peak,
            Beats = simulator.Beats,
            SilentFrames = simulator.SilentFrames,
            HiddenFrames = simulator.HiddenFrames,
            StaticFrames = simulator.StaticFrames,
            Snapshots = snapshots,
        };
    }

    private static MemorySample Memory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        long live = GC.GetTotalMemory(forceFullCollection: true);
        using var process = Process.GetCurrentProcess();
        return new MemorySample(live, process.WorkingSet64, process.HandleCount);
    }

    public static string Format(SoakSnapshot s)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        return string.Create(c,
            $"{Clock(s.SimulatedSeconds)}  {s.Frames,10:N0} frames | interval: mean {Micro(s.Interval.MeanNs)}, p50 {Micro(s.Interval.P50Ns)}, " +
            $"p99 {Micro(s.Interval.P99Ns)}, max {Micro(s.Interval.MaxNs)} µs | {s.AllocatedBytes:N0} B allocated ({s.AllocatingFrames:N0} frames) | " +
            $"heap {Mega(s.HeapBytes)} MB, working set {Mega(s.WorkingSetBytes)} MB | GCs {s.Gen0}/{s.Gen1}/{s.Gen2} | beats {s.Beats:N0}");
    }

    public static string Describe(SoakProgramSummary p, SoakOptions o)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        double total = Math.Max(p.Seconds, 1e-9);
        return string.Create(c,
            $"Program: {Clock(p.Seconds)} simulated, seed {o.Seed}: {Count(p.Sessions, "listening session")} (silence fades to Idle Glow / hides the light, " +
            $"alternating; each on a new output device), {Count(p.Tracks, "track")} at {p.MinBpm:0.#}–{p.MaxBpm:0.#} BPM, {Count(p.PaletteChanges, "palette change")} " +
            $"({p.SameAlbumTracks} same album); music {p.MusicSeconds / total:P1}, digital silence {p.SilenceSeconds / total:P1}, " +
            $"no packets {p.NoPacketSeconds / total:P1}; {p.Kicks:N0} kicks");
    }

    /// <summary>The end-of-run summary: Markdown, ready for docs/PERF.md.</summary>
    public static string Summarize(SoakResult r)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        var w = new StringWriter(c);
        w.WriteLine(string.Create(c, $"Simulated {Clock(r.Program.Seconds)} in {Clock(r.WallSeconds)} of real time ({r.Program.Seconds / Math.Max(r.WallSeconds, 1e-9):N0}× real time), {r.Frames:N0} measured frames after a {r.WarmupWallSeconds:0.0} s warm-up ({r.WarmupTours} tours, {Clock(r.WarmupSimulatedSeconds)} simulated)."));
        w.WriteLine();
        w.WriteLine("| per frame | mean | p50 | p99 | p99.9 | max |");
        w.WriteLine("|---|---:|---:|---:|---:|---:|");
        Row(w, "**whole frame**", r.Frame);
        Row(w, "analyzer (Process, Reset)", r.Analyzer);
        Row(w, "light engine (Update)", r.LightEngine);
        Row(w, "palette (SetTarget, Update, FillGradient)", r.Palette);
        w.WriteLine();
        w.WriteLine(string.Create(c, $"- **Allocations:** {r.AllocatedBytes:N0} B in {r.AllocatingFrames:N0} of {r.Frames:N0} frames ({r.BytesPerFrame:0.###} B/frame){(r.FirstAllocationSeconds is double first ? $", first at {Clock(first)}" : "")}. Whole process while measuring, progress lines excluded: {r.ProcessAllocatedBytes:N0} B. GCs: {r.Gen0}/{r.Gen1}/{r.Gen2} (gen 0/1/2)."));
        w.WriteLine(string.Create(c, $"- **Memory:** live managed heap {Kilo(r.Start.LiveManagedBytes)} → {Kilo(r.End.LiveManagedBytes)} KB ({Signed(r.ManagedGrowthBytes / 1024.0)} KB), working set {Mega(r.Start.WorkingSetBytes)} → {Mega(r.End.WorkingSetBytes)} MB (peak over the process lifetime {Mega(r.PeakWorkingSetBytes)} MB), handles {r.Start.Handles} → {r.End.Handles}."));
        w.WriteLine(string.Create(c, $"- **CPU:** mean frame × {r.Options.Fps:0.##} fps = {r.CorePercent:0.00} % of one core ({r.CorePercent / Environment.ProcessorCount:0.000} % of all {Environment.ProcessorCount} logical CPUs, Task Manager's scale)."));
        w.WriteLine(string.Create(c, $"- **Beats:** {r.Beats:N0} detected for {r.Program.Kicks:N0} kicks ({r.Beats / (double)Math.Max(1, r.Program.Kicks):P1})."));
        w.WriteLine(string.Create(c, $"- **Light:** silent {Share(r.SilentFrames, r.Frames)}, hidden (Visibility 0) {Share(r.HiddenFrames, r.Frames)}, static {Share(r.StaticFrames, r.Frames)} of frames."));
        w.WriteLine(string.Create(c, $"- **Verdict:** {(r.Passed ? "PASS" : "FAIL")}: {(r.AllocationFree ? "0 B allocated per frame in steady state" : "frames allocated")}; {(r.ManagedMemoryFlat ? "managed memory flat" : $"managed memory grew more than {SoakResult.MaxManagedGrowthBytes / 1024} KB")}."));
        return w.ToString();
    }

    private static void Row(TextWriter w, string name, CostSummary s) => w.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"| {name} | {Micro(s.MeanNs)} µs | {Micro(s.P50Ns)} µs | {Micro(s.P99Ns)} µs | {Micro(s.P999Ns)} µs | {Micro(s.MaxNs)} µs |"));

    internal static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}");
    }

    private static string Count(long n, string noun) => string.Create(CultureInfo.InvariantCulture, $"{n:N0} {noun}{(n == 1 ? "" : "s")}");
    private static string Micro(double ns) => (ns / 1000).ToString(ns < 10_000 ? "0.00" : "0.0", CultureInfo.InvariantCulture);
    private static string Mega(long bytes) => (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture);
    private static string Kilo(long bytes) => (bytes / 1024.0).ToString("N0", CultureInfo.InvariantCulture);
    private static string Signed(double value) => value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
    private static string Share(long part, long whole) => (part / (double)Math.Max(1, whole)).ToString("P1", CultureInfo.InvariantCulture);

    // The render thread of the app, reduced to its Core calls, plus the measurement around them. One instance plays the
    // warm-up tour and then the measured program, so the analyzer, engine and blender carry their state across.
    private sealed class Simulator
    {
        private readonly IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer();
        private readonly ILightEngine engine;
        private readonly IPaletteBlender blender = CoreFactory.CreatePaletteBlender(Palette.Default);
        private readonly float[] gradient = new float[256];
        private readonly float[] buffer;
        private readonly DeterministicRandom random;
        private readonly int rate;
        private readonly double frameSeconds, jitter;
        private readonly long packet;
        private Palette palette = Palette.Default;
        private Settings settings = SoakProgram.IdleGlowSettings;
        private int beatCount;

        public Simulator(SoakOptions options)
        {
            engine = CoreFactory.CreateLightEngine();
            if (options.WrapLightEngine is not null) engine = options.WrapLightEngine(engine);
            rate = options.SampleRate;
            frameSeconds = 1 / options.Fps;
            jitter = options.Jitter;
            packet = options.PacketSeconds > 0 ? Math.Max(1, (long)Math.Round(options.PacketSeconds * rate)) : 1;
            buffer = new float[(int)Math.Ceiling(frameSeconds * (1 + jitter) * rate) + 2 * packet + 2];
            random = new DeterministicRandom(DeterministicRandom.Hash(options.Seed, 0x4A4954544552UL));
        }

        public LatencyHistogram Frame { get; } = new();
        public LatencyHistogram Analyzer { get; } = new();
        public LatencyHistogram Engine { get; } = new();
        public LatencyHistogram Colors { get; } = new();
        public LatencyHistogram Interval { get; } = new();
        public long Frames { get; private set; }
        public long Allocated { get; private set; }
        public long AllocatingFrames { get; private set; }
        public double FirstAllocation { get; private set; } = -1;
        public long Beats { get; private set; }
        public long SilentFrames { get; private set; }
        public long HiddenFrames { get; private set; }
        public long StaticFrames { get; private set; }

        public void ClearMeasurements()
        {
            Frame.Clear();
            Analyzer.Clear();
            Engine.Clear();
            Colors.Clear();
            Interval.Clear();
            Frames = Allocated = AllocatingFrames = Beats = SilentFrames = HiddenFrames = StaticFrames = 0;
            FirstAllocation = -1;
        }

        // Plays a program from its start: report(time) every reportEvery simulated seconds and once at the end. The loop
        // allocates nothing; only report may.
        public void Play(SoakProgram program, double reportEvery, Action<double>? report)
        {
            ReadOnlySpan<SoakEvent> events = program.Events;
            double time = 0, end = program.Seconds, nextReport = reportEvery;
            long position = 0;
            int cursor = 0, next = 0;
            while (time < end)
            {
                double dt = frameSeconds * (1 + jitter * random.NextSigned());
                time += dt;
                // Whole packets that have arrived by the end of this frame; at the end, the rest of the program.
                long arrived = time >= end ? program.Length : Math.Min(program.Length, (long)(time * rate) / packet * packet);

                // What the render thread learns this frame: a new session (settings, new device) or a new track.
                bool reset = false;
                Palette? target = null;
                while (next < events.Length && events[next].Position < arrived)
                {
                    SoakEvent e = events[next++];
                    if (e.Settings is not null)
                    {
                        settings = e.Settings;
                        reset = true;
                    }
                    if (e.Palette is not null) target = palette = e.Palette;
                }
                int count = program.Read(position, arrived, buffer, ref cursor);
                position = arrived;
                float step = (float)dt;

                // The calls in MusicGlowSource.NextFrame's order: a new album palette is passed to SetTarget first,
                // then a device change resets the analyzer, the audio is analyzed, the crossfade advances, the engine
                // maps the features to a light state and the gradient is filled for the shader.
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long t0 = Stopwatch.GetTimestamp();
                if (target is not null) blender.SetTarget(target, AlbumFade);
                long t1 = Stopwatch.GetTimestamp();
                if (reset) analyzer.Reset();
                AudioFeatures features = analyzer.Process(buffer.AsSpan(0, count), rate, step);
                long t2 = Stopwatch.GetTimestamp();
                blender.Update(step);
                long t3 = Stopwatch.GetTimestamp();
                LightState state = engine.Update(step, in features, palette, settings, paused: false);
                long t4 = Stopwatch.GetTimestamp();
                blender.FillGradient(gradient, state.Ratio);
                long t5 = Stopwatch.GetTimestamp();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;

                Frame.RecordTicks(t5 - t0);
                Interval.RecordTicks(t5 - t0);
                Analyzer.RecordTicks(t2 - t1);
                Engine.RecordTicks(t4 - t3);
                Colors.RecordTicks(t1 - t0 + (t3 - t2) + (t5 - t4));
                Frames++;
                if (allocated != 0)
                {
                    Allocated += allocated;
                    AllocatingFrames++;
                    if (FirstAllocation < 0) FirstAllocation = time;
                }
                if (reset) beatCount = 0;
                int beats = analyzer.Diagnostics.BeatCount;
                Beats += beats - beatCount;
                beatCount = beats;
                if (features.IsSilent) SilentFrames++;
                if (state.Visibility <= 0) HiddenFrames++;
                if (engine.IsStatic) StaticFrames++;

                if (time >= nextReport || time >= end)
                {
                    report?.Invoke(Math.Min(time, end));
                    Interval.Clear();
                    while (nextReport <= time) nextReport += reportEvery;
                }
            }
        }
    }
}
