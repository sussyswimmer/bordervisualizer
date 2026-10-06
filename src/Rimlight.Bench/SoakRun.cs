using System.Diagnostics;
using System.Globalization;
using Rimlight.AudioTools;
using Rimlight.Core;

namespace Rimlight.Bench;

internal sealed record SoakOptions
{
    public double Minutes { get; init; } = 1;
    public int SampleRate { get; init; } = 48000;
    public double Fps { get; init; } = 60;
    public double Jitter { get; init; } = 0.2;
    public double PacketSeconds { get; init; } = 0.01;
    public ulong Seed { get; init; } = 1;
    /// <summary>Simulated minutes between progress lines; 0 = ten lines per run.</summary>
    public double ReportEveryMinutes { get; init; }
}

internal sealed record SoakSnapshot(double SimulatedSeconds, long Frames, double MeanNs, double P99Ns, long MaxNs,
    double BytesPerFrame, long ManagedHeapBytes, long WorkingSetBytes, int Gen0, int Gen1, int Gen2, int Beats);

// Simulated-time soak: a long synthetic track streamed through the analyzer the way the render loop feeds it (jittered
// frames, whole capture packets), as fast as the CPU allows. Only Process is timed and allocation-counted; the track
// is generated between frames and needs no memory. C12 extends this run (light engine once C6 lands, 8-hour default,
// docs/PERF.md); the frame loop, the constant-memory histogram and the snapshots are its structure.
internal static class SoakRun
{
    public static IReadOnlyList<SoakSnapshot> Run(SoakOptions options, Action<SoakSnapshot>? progress = null)
    {
        var track = new SyntheticTrack(new SyntheticTrackOptions { SampleRate = options.SampleRate, Seconds = options.Minutes * 60, Bpm = 124, Seed = options.Seed }.WithFullMix());
        IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer();
        var random = new DeterministicRandom(options.Seed);
        long packet = options.PacketSeconds > 0 ? Math.Max(1, (long)Math.Round(options.PacketSeconds * options.SampleRate)) : 1;
        double maxDt = 1 / options.Fps * (1 + options.Jitter);
        float[] buffer = new float[(int)Math.Ceiling(maxDt * options.SampleRate) + packet + 1];
        double reportEvery = (options.ReportEveryMinutes > 0 ? options.ReportEveryMinutes : options.Minutes / 10) * 60;

        var cost = new LatencyHistogram();
        var snapshots = new List<SoakSnapshot>();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        double time = 0, end = track.Length / (double)options.SampleRate, nextReport = reportEvery;
        long position = 0, allocated = 0;
        while (time < end)
        {
            double dt = 1 / options.Fps * (1 + options.Jitter * random.NextSigned());
            time += dt;
            long arrived = Math.Min(track.Length, (long)(time * options.SampleRate) / packet * packet);
            int count = track.Read(buffer.AsSpan(0, (int)Math.Max(0, arrived - position)));
            position += count;

            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            analyzer.Process(buffer.AsSpan(0, count), options.SampleRate, (float)dt);
            long ticks = Stopwatch.GetTimestamp() - start;
            allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
            cost.RecordTicks(ticks);

            if (time >= nextReport || time >= end)
            {
                var snapshot = new SoakSnapshot(Math.Min(time, end), cost.Count, cost.Mean, cost.Percentile(0.99), cost.Max,
                    allocated / (double)cost.Count, GC.GetTotalMemory(false), Environment.WorkingSet,
                    GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2,
                    analyzer.Diagnostics.BeatCount);
                snapshots.Add(snapshot);
                progress?.Invoke(snapshot);
                nextReport += reportEvery;
            }
        }
        return snapshots;
    }

    public static string Format(SoakSnapshot s)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        var simulated = TimeSpan.FromSeconds(s.SimulatedSeconds);
        return string.Create(c,
            $"{(int)simulated.TotalHours}:{simulated.Minutes:00}:{simulated.Seconds:00}  {s.Frames,9} frames  " +
            $"mean {s.MeanNs / 1000,6:F1} µs  p99 {s.P99Ns / 1000,6:F1} µs  max {s.MaxNs / 1000.0,7:F1} µs  " +
            $"{s.BytesPerFrame,5:F1} B/frame  heap {s.ManagedHeapBytes / 1048576.0,6:F1} MB  " +
            $"working set {s.WorkingSetBytes / 1048576.0,6:F1} MB  GCs {s.Gen0}/{s.Gen1}/{s.Gen2}  beats {s.Beats}");
    }
}
