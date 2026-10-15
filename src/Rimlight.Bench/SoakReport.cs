using System.Globalization;
using Rimlight.Core;

namespace Rimlight.Bench;

// The result of a soak: frame cost per stage, allocations, GCs, memory, what the schedule exercised, and the two pass
// criteria (doc 07 Phase 7, AGENTS.md "Definition of done for Lane A"):
//   0 B/frame     not one byte allocated by any measured frame (GC.GetAllocatedBytesForCurrentThread around NextFrame),
//                 applying a settings edit aside: that step runs once per edit, and in the app it allocates too (a
//                 Sensitivity change rebuilds the analyzer's AudioTuning record), so it is reported on its own line;
//   memory flat   the working set grew by less than WorkingSetGrowthLimit from the settle point (an hour after warm-up,
//                 SoakOptions.SettleMinutes) to the end, and the live managed heap after a full GC by less than
//                 LiveHeapGrowthLimit from the end of warm-up to the end.
// Before the settle point the working set grows once, as each path first runs: the JIT compiles and tiers it up and the
// runtime pages it needs are faulted in (the report shows that growth too). A leak keeps growing after it: one object
// per frame is 1.7 million objects in 8 hours at 60 fps, and it would fail 0 B/frame as well.
internal sealed class SoakReport
{
    /// <summary>Working-set growth from the settle point to the end below which memory counts as flat: 1 MB.</summary>
    public const long WorkingSetGrowthLimit = 1 << 20;
    /// <summary>Live managed heap growth (after a full GC) below which memory counts as flat: 64 KB.</summary>
    public const long LiveHeapGrowthLimit = 64 << 10;

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>The run's options.</summary>
    public required SoakOptions Options { get; init; }
    /// <summary>The day it simulated.</summary>
    public required SoakSchedule Schedule { get; init; }
    /// <summary>The simulated length in seconds.</summary>
    public required double SimulatedSeconds { get; init; }
    /// <summary>Simulated seconds measured (after warm-up).</summary>
    public required double MeasuredSeconds { get; init; }
    /// <summary>The progress snapshots.</summary>
    public required IReadOnlyList<SoakSnapshot> Snapshots { get; init; }
    /// <summary>The cost of the measured frames per stage.</summary>
    public required StageCosts Costs { get; init; }
    /// <summary>Measured frames.</summary>
    public required long Frames { get; init; }
    /// <summary>Frames run during warm-up.</summary>
    public required long WarmupFrames { get; init; }
    /// <summary>Bytes the measured frames allocated, applying settings edits aside (must be 0).</summary>
    public required long AllocatedBytes { get; init; }
    /// <summary>Measured frames that allocated anything, applying settings edits aside.</summary>
    public required long AllocatingFrames { get; init; }
    /// <summary>The most one measured frame allocated, applying settings edits aside.</summary>
    public required long MaxFrameBytes { get; init; }
    /// <summary>Measured frames that applied a new settings snapshot.</summary>
    public required long SettingsEdits { get; init; }
    /// <summary>Of those, the frames whose settings step allocated (a Sensitivity change, as in the app).</summary>
    public required long AllocatingEdits { get; init; }
    /// <summary>What applying the settings edits allocated in all (<see cref="FramePipeline.SettingsBytes"/>).</summary>
    public required long SettingsEditBytes { get; init; }
    /// <summary>What the harness itself allocated on the run's thread between measured frames (audio, events, memory
    /// readings, progress lines). 0 unless the progress callback allocates: garbage there would grow the working set the
    /// soak measures.</summary>
    public required long HarnessBytes { get; init; }
    /// <summary>GCs per generation during the measured part (the harness's own allocations between frames included).</summary>
    public required int Gen0 { get; init; }
    /// <summary>Gen 1 GCs during the measured part.</summary>
    public required int Gen1 { get; init; }
    /// <summary>Gen 2 GCs during the measured part.</summary>
    public required int Gen2 { get; init; }
    /// <summary>Memory when the soak started, before the schedule was built.</summary>
    public required MemoryReading Start { get; init; }
    /// <summary>The readings of the measured part: the first at the end of warm-up (after a full GC), the last at the end.</summary>
    public required MemoryLog Memory { get; init; }
    /// <summary>The reading the flatness check starts from (<see cref="SoakOptions.SettleMinutes"/>).</summary>
    public required int SettledIndex { get; init; }
    /// <summary>The live managed heap after a full GC at the end of warm-up.</summary>
    public required long LiveHeapAfterWarmup { get; init; }
    /// <summary>The live managed heap after a full GC at the end.</summary>
    public required long LiveHeapEnd { get; init; }
    /// <summary>Measured frames by <see cref="FrameMotion"/>.</summary>
    public required IReadOnlyList<long> FramesByMotion { get; init; }
    /// <summary>Measured simulated seconds by <see cref="Pace"/>.</summary>
    public required IReadOnlyList<double> SecondsByPace { get; init; }
    /// <summary>Measured frames that refilled the gradient.</summary>
    public required long Refills { get; init; }
    /// <summary>Measured frames that started a crossfade.</summary>
    public required long PaletteSets { get; init; }
    /// <summary>Measured frames that reset the analyzer (the capture opened or closed).</summary>
    public required long Resets { get; init; }
    /// <summary>Measured frames with IsSilent.</summary>
    public required long SilentFrames { get; init; }
    /// <summary>Measured frames with nothing visible.</summary>
    public required long HiddenFrames { get; init; }
    /// <summary>Measured frames after which the light engine was static.</summary>
    public required long StaticFrames { get; init; }
    /// <summary>Frames that came late because of a stall.</summary>
    public required long StallFrames { get; init; }
    /// <summary>Beats detected over the whole run.</summary>
    public required int Beats { get; init; }
    /// <summary>Tracks whose album palette was published.</summary>
    public required int Tracks { get; init; }
    /// <summary>Wall-clock seconds of the run, JIT warm-up excluded.</summary>
    public required double WallSeconds { get; init; }
    /// <summary>Wall-clock seconds of the JIT warm-up.</summary>
    public required double JitWarmupWallSeconds { get; init; }
    /// <summary>The cost of one clock read (<see cref="StageCosts.ClockReadNanoseconds"/>).</summary>
    public required double ClockReadNanoseconds { get; init; }

    /// <summary>Memory at the end of warm-up, after a full GC.</summary>
    public MemoryReading AfterWarmup => Memory[0];
    /// <summary>Memory at the settle point, the flatness check's baseline.</summary>
    public MemoryReading Settled => Memory[SettledIndex];
    /// <summary>Simulated time of <see cref="Settled"/>.</summary>
    public double SettledAt => Memory.TimeAt(SettledIndex);
    /// <summary>Memory at the end.</summary>
    public MemoryReading End => Memory[Memory.Count - 1];
    /// <summary>The largest working set and managed heap read during the measured part.</summary>
    public MemoryReading Peak => Memory.Peak;

    public bool AllocationFree => Frames > 0 && AllocatedBytes == 0;
    /// <summary>Working-set growth from the settle point to the end: the flatness criterion.</summary>
    public long WorkingSetGrowth => End.WorkingSetBytes - Settled.WorkingSetBytes;
    /// <summary>Working-set growth from the end of warm-up to the end, one-time growth included (for information).</summary>
    public long WorkingSetGrowthSinceWarmup => End.WorkingSetBytes - AfterWarmup.WorkingSetBytes;
    /// <summary>The working set's least-squares trend after the settle point, in bytes per simulated hour (for information).</summary>
    public double WorkingSetTrendPerHour => Memory.WorkingSetTrendPerHour(SettledIndex);
    public long LiveHeapGrowth => LiveHeapEnd - LiveHeapAfterWarmup;
    public bool MemoryFlat => WorkingSetGrowth < WorkingSetGrowthLimit && LiveHeapGrowth < LiveHeapGrowthLimit;
    public bool Passed => AllocationFree && MemoryFlat;

    /// <summary>The share of one CPU core the measured frames took: their total cost over the measured simulated time.</summary>
    public double CoreShare => MeasuredSeconds > 0 ? Costs[Stage.Frame].TotalNanoseconds / (MeasuredSeconds * 1e9) : 0;

    /// <summary>The "0 B/frame" verdict line.</summary>
    public string AllocationVerdict => string.Create(C,
        $"{(AllocationFree ? "PASS" : "FAIL")} 0 B/frame: {AllocatedBytes:N0} bytes allocated in {Frames:N0} measured frames after warm-up, applying settings edits aside") +
        (AllocatingFrames > 0 ? string.Create(C, $" ({AllocatingFrames:N0} frames allocated, at most {MaxFrameBytes:N0} bytes).") : ".");

    /// <summary>The line on settings edits, whose allocations the 0 B/frame verdict leaves out.</summary>
    public string SettingsEditsLine => string.Create(C,
        $"Settings edits: {SettingsEdits:N0} applied by measured frames; applying them allocated {SettingsEditBytes:N0} B, in " +
        $"{AllocatingEdits:N0} of them. That step runs once per edit, as in the app, whose MusicGlowSource.ApplySettings " +
        $"allocates there too: a Sensitivity change rebuilds the analyzer's AudioTuning record on the render thread.");

    /// <summary>The "memory flat" verdict line, with the criterion.</summary>
    public string MemoryVerdict => string.Create(C,
        $"{(MemoryFlat ? "PASS" : "FAIL")} memory flat: from {Clock(SettledAt)} to the end the working set changed by " +
        $"{ProcessMemory.SignedKilobytes(WorkingSetGrowth)} (must be under +{WorkingSetGrowthLimit / 1024:N0} KB), and from the end " +
        $"of warm-up to the end the live managed heap after a full GC changed by {ProcessMemory.SignedKilobytes(LiveHeapGrowth)} " +
        $"(must be under +{LiveHeapGrowthLimit / 1024:N0} KB).");

    /// <summary>The line on one-time growth and the trend, which the verdict doesn't judge.</summary>
    public string MemoryContext => string.Create(C,
        $"Working set from the end of warm-up ({Clock(Memory.TimeAt(0))}) to the end: " +
        $"{ProcessMemory.SignedKilobytes(WorkingSetGrowthSinceWarmup)}, one-time growth included; trend from {Clock(SettledAt)} on: " +
        $"{ProcessMemory.SignedKilobytes((long)WorkingSetTrendPerHour)} per simulated hour (least squares over {Memory.Count - SettledIndex} readings). " +
        $"The JIT had compiled {AfterWarmup.CompiledMethods:N0} methods by the end of warm-up and compiled " +
        $"{End.CompiledMethods - AfterWarmup.CompiledMethods:N0} more during the measured part (tier-ups of paths that run rarely), " +
        $"{End.CompiledMethods - Settled.CompiledMethods:N0} of them after {Clock(SettledAt)}.");

    /// <summary>Writes the plain-text summary printed after the progress lines.</summary>
    /// <param name="output">Where to write.</param>
    public void WriteSummary(TextWriter output)
    {
        output.WriteLine();
        output.WriteLine(string.Create(C, $"Simulated {Clock(SimulatedSeconds)} in {WallSeconds:0} s of wall time ({SimulatedSeconds / Math.Max(WallSeconds, 1e-9):0}x real time), measured from {Clock(SimulatedSeconds - MeasuredSeconds)}; JIT warm-up {JitWarmupWallSeconds:0.0} s."));
        output.WriteLine($"Schedule: {DescribeSchedule()}.");
        output.WriteLine($"Frames: {DescribeFrames()}.");
        output.WriteLine($"Paths: {DescribePaths()}.");
        output.WriteLine();
        output.WriteLine(string.Create(C, $"Cost per measured frame in ns ({StageCosts.ClockReadsNote}, {ClockReadNanoseconds:0} ns per read here):"));
        WriteCostTable(output);
        output.WriteLine();
        output.WriteLine(string.Create(C, $"CPU: {CoreShare * 100:0.000} % of one core on average over the measured time (sum of whole-frame costs / simulated time)."));
        output.WriteLine(string.Create(C, $"Allocations: {AllocatedBytes:N0} B in {Frames:N0} measured frames ({(Frames == 0 ? 0 : AllocatedBytes / (double)Frames):0.###} B/frame), applying settings edits aside; the harness between frames: {HarnessBytes:N0} B."));
        output.WriteLine(SettingsEditsLine);
        output.WriteLine(string.Create(C, $"GCs during the measured part (gen 0/1/2): {Gen0}/{Gen1}/{Gen2}."));
        output.WriteLine();
        WriteMemoryTable(output);
        output.WriteLine();
        output.WriteLine(MemoryContext);
        output.WriteLine(AllocationVerdict);
        output.WriteLine(MemoryVerdict);
    }

    /// <summary>Writes the summary as Markdown, for docs/PERF.md.</summary>
    /// <param name="output">Where to write.</param>
    public void WriteMarkdown(TextWriter output)
    {
        output.WriteLine(string.Create(C, $"#### Soak: {Clock(SimulatedSeconds)} simulated, seed {Options.Seed}"));
        output.WriteLine();
        output.WriteLine(string.Create(C, $"- Run: {Options.SampleRate} Hz, {Options.Fps:0.##} fps ±{Options.Jitter * 100:0.#} % jitter, {Options.PacketSeconds * 1000:0.#} ms packets; {Clock(SimulatedSeconds)} simulated in {WallSeconds:0} s of wall time; the first {Clock(SimulatedSeconds - MeasuredSeconds)} not measured."));
        output.WriteLine($"- Schedule: {DescribeSchedule()}.");
        output.WriteLine($"- Frames: {DescribeFrames()}.");
        output.WriteLine($"- Paths: {DescribePaths()}.");
        output.WriteLine();
        output.WriteLine(string.Create(C, $"Cost per measured frame, ns ({StageCosts.ClockReadsNote}, {ClockReadNanoseconds:0} ns per read here):"));
        output.WriteLine();
        WriteCostTable(output);
        output.WriteLine();
        output.WriteLine(string.Create(C, $"- CPU: **{CoreShare * 100:0.000} %** of one core on average over the measured time (sum of whole-frame costs / simulated time)."));
        output.WriteLine(string.Create(C, $"- Allocations: **{AllocatedBytes:N0} B** in {Frames:N0} measured frames ({(Frames == 0 ? 0 : AllocatedBytes / (double)Frames):0.###} B/frame), applying settings edits aside; the harness between frames: {HarnessBytes:N0} B."));
        output.WriteLine($"- {SettingsEditsLine}");
        output.WriteLine(string.Create(C, $"- GCs during the measured part (gen 0/1/2): {Gen0}/{Gen1}/{Gen2}."));
        output.WriteLine();
        WriteMemoryTable(output);
        output.WriteLine();
        WriteMemoryTimeline(output);
        output.WriteLine();
        output.WriteLine($"- {MemoryContext}");
        output.WriteLine($"- **{AllocationVerdict}**");
        output.WriteLine($"- **{MemoryVerdict}**");
    }

    private void WriteCostTable(TextWriter output)
    {
        output.WriteLine("| stage | mean | p50 | p99 | p99.9 | max |");
        output.WriteLine("|---|---:|---:|---:|---:|---:|");
        foreach (Stage stage in StageCosts.All)
        {
            LatencyHistogram h = Costs[stage];
            output.WriteLine(string.Create(C, $"| {StageCosts.Label(stage)} | {h.Mean:N0} | {h.Percentile(0.5):N0} | {h.Percentile(0.99):N0} | {h.Percentile(0.999):N0} | {h.Max:N0} |"));
        }
    }

    private void WriteMemoryTable(TextWriter output)
    {
        output.WriteLine("| memory | working set | managed heap |");
        output.WriteLine("|---|---:|---:|");
        output.WriteLine($"| start (before the schedule was built) | {ProcessMemory.Megabytes(Start.WorkingSetBytes)} | {ProcessMemory.Megabytes(Start.ManagedHeapBytes)} |");
        output.WriteLine($"| end of warm-up, {Clock(Memory.TimeAt(0))} (after a full GC) | {ProcessMemory.Megabytes(AfterWarmup.WorkingSetBytes)} | {ProcessMemory.Megabytes(AfterWarmup.ManagedHeapBytes)} |");
        output.WriteLine($"| settle point, {Clock(SettledAt)} | {ProcessMemory.Megabytes(Settled.WorkingSetBytes)} | {ProcessMemory.Megabytes(Settled.ManagedHeapBytes)} |");
        output.WriteLine($"| end, {Clock(SimulatedSeconds)} | {ProcessMemory.Megabytes(End.WorkingSetBytes)} | {ProcessMemory.Megabytes(End.ManagedHeapBytes)} |");
        output.WriteLine($"| maximum, measured part | {ProcessMemory.Megabytes(Peak.WorkingSetBytes)} | {ProcessMemory.Megabytes(Peak.ManagedHeapBytes)} |");
        output.WriteLine(string.Create(C, $"| live heap after a full GC: end of warm-up → end | | {LiveHeapAfterWarmup / 1024.0:N0} KB → {LiveHeapEnd / 1024.0:N0} KB |"));
    }

    // The working set over the run: hourly, or in eighths of a run shorter than two hours.
    private void WriteMemoryTimeline(TextWriter output)
    {
        double step = SimulatedSeconds >= 7200 ? 3600 : SimulatedSeconds / 8;
        output.WriteLine("| simulated time | working set | managed heap | methods compiled by the JIT |");
        output.WriteLine("|---|---:|---:|---:|");
        int previous = -1;
        for (double t = 0; t <= SimulatedSeconds + 1e-6; t += step)
        {
            int i = Memory.IndexAt(t);
            if (i < 0 || i == previous) continue;
            previous = i;
            Row(i);
        }
        if (previous != Memory.Count - 1) Row(Memory.Count - 1);

        void Row(int i) => output.WriteLine(string.Create(C,
            $"| {Clock(Memory.TimeAt(i))} | {Kilobytes(Memory[i].WorkingSetBytes)} | {Kilobytes(Memory[i].ManagedHeapBytes)} | {Memory[i].CompiledMethods:N0} |"));
    }

    private static string Kilobytes(long bytes) => string.Create(C, $"{bytes / 1024.0:N0} KB");

    private static string Count(long count, string singular, string plural) => string.Create(C, $"{count:N0} {(count == 1 ? singular : plural)}");

    private string DescribeSchedule()
    {
        int tracks = 0, noArt = 0, sameAlbum = 0, silentGaps = 0, emptyGaps = 0, longGaps = 0;
        Palette? previous = null;
        foreach (SoakSegment s in Schedule.Segments)
        {
            if (s.Start >= SimulatedSeconds) break;
            if (s.Audio == SoakAudio.Music)
            {
                tracks++;
                if (s.Album is null) noArt++;
                else if (previous is not null && s.Album.Primary == previous.Primary && s.Album.Secondary == previous.Secondary) sameAlbum++;
                previous = s.Album ?? previous;
                continue;
            }
            if (s.Audio == SoakAudio.SilentPackets) silentGaps++;
            else emptyGaps++;
            if (s.Seconds >= 4) longGaps++;
        }
        var edits = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int pauses = 0, hotkey = 0, stalls = 0, longStalls = 0;
        foreach (SoakEvent e in Schedule.Events)
        {
            if (e.Time >= SimulatedSeconds) break;
            switch (e.Kind)
            {
                case SoakEventKind.Pause: pauses++; break;
                case SoakEventKind.Stall:
                    stalls++;
                    if (e.StallSeconds > PaceModel.MaxFrameDtSeconds) longStalls++;
                    break;
                case SoakEventKind.Settings when e.Label == "hotkey: off": hotkey++; break;
                case SoakEventKind.Settings when !e.Label.StartsWith("hotkey", StringComparison.Ordinal):
                    string name = e.Label.Split(' ')[0];
                    edits[name] = edits.GetValueOrDefault(name) + 1;
                    break;
            }
        }
        string editList = string.Join(", ", edits.Select(p => $"{p.Key} {p.Value}"));
        return string.Create(C,
            $"{Count(tracks, "track", "tracks")} ({noArt} without art, {sameAlbum} with the previous track's colors), " +
            $"{Count(silentGaps + emptyGaps, "gap", "gaps")} ({silentGaps} with silent packets, {emptyGaps} without packets; " +
            $"{longGaps} of 4 s or more), {Count(edits.Values.Sum(), "settings edit", "settings edits")} ({editList}), " +
            $"{Count(pauses, "fullscreen pause", "fullscreen pauses")}, {Count(hotkey, "hotkey off", "hotkey offs")}, " +
            $"{Count(stalls, "stall", "stalls")} ({longStalls} over {PaceModel.MaxFrameDtSeconds} s)");
    }

    private string DescribeFrames() => string.Create(C,
        $"{Frames:N0} measured ({WarmupFrames:N0} in warm-up); motion full {FramesByMotion[(int)FrameMotion.Full]:N0}, " +
        $"slow {FramesByMotion[(int)FrameMotion.Slow]:N0}, listening {FramesByMotion[(int)FrameMotion.Listening]:N0}, " +
        $"still {FramesByMotion[(int)FrameMotion.Still]:N0}; time at the full rate {Clock(SecondsByPace[(int)Pace.Full])}, " +
        $"at 10 fps {Clock(SecondsByPace[(int)Pace.Slow])}, blocked {Clock(SecondsByPace[(int)Pace.Block])}");

    private string DescribePaths() => string.Create(C,
        $"{PaletteSets:N0} crossfades started, {Refills:N0} gradient refills, {Resets:N0} analyzer resets (capture opened " +
        $"or closed), {StallFrames:N0} stalled frames, {Beats:N0} beats; IsSilent in {Share(SilentFrames)}, the glow hidden in " +
        $"{Share(HiddenFrames)} and the engine static in {Share(StaticFrames)} of the measured frames");

    private string Share(long count) => string.Create(C, $"{(Frames == 0 ? 0 : count * 100.0 / Frames):0.0} %");

    /// <summary>Seconds as h:mm:ss.</summary>
    /// <param name="seconds">The duration.</param>
    /// <returns>The text.</returns>
    public static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(Math.Max(0, seconds)));
        return string.Create(C, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}");
    }
}
