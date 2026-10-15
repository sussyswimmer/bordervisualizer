using Rimlight.Bench;
using Xunit;

namespace Rimlight.Tests.Tools;

// C3: Rimlight.Bench's measurement plumbing. The timings themselves are informational; allocations are not.
public sealed class BenchTests
{
    [Fact]
    public void AnalyzerAllocatesNothingPerFrameAtFortyEightKilohertzAndSixtyFps()
    {
        var options = new AnalyzerBenchmarkOptions { WarmupFrames = 300, MinWarmupSeconds = 0, Frames = 1500 };
        foreach (string scenario in AnalyzerBenchmark.Scenarios)
        {
            BenchmarkResult result = AnalyzerBenchmark.Run(scenario, options);
            Assert.Equal(0, result.BytesPerFrame);
            Assert.Equal(1500, result.Cost.Count);
            Assert.True(result.Cost.Mean > 0, scenario);
        }
        Assert.True(AnalyzerBenchmark.Run("music", options).Beats > 20); // the signal really has beats
    }

    [Fact]
    public void SoakReportsSnapshotsWithoutAllocating()
    {
        var reported = new List<SoakSnapshot>();
        IReadOnlyList<SoakSnapshot> snapshots = SoakRun.Run(new SoakOptions { Minutes = 0.25, WarmupSeconds = 3, ReportEveryMinutes = 0.05 }, reported.Add);
        Assert.Equal(snapshots, reported);
        Assert.InRange(snapshots.Count, 4, 5);                              // every 3 s after the 3 s warm-up
        Assert.Equal(15, snapshots[^1].SimulatedSeconds, 1);
        Assert.InRange(snapshots[^1].Frames, 12 * 60 - 20, 12 * 60 + 20);  // only measured frames count
        Assert.All(snapshots, s => Assert.Equal(0, s.BytesPerFrame));
        Assert.True(snapshots[^1].Beats > 20);
        Assert.Contains("B/frame", SoakRun.Format(snapshots[^1]));
    }

    [Fact]
    public void HistogramPercentilesAreWithinOnePercent()
    {
        var histogram = new LatencyHistogram();
        for (long v = 1; v <= 100_000; v++) histogram.Record(v * 10); // 10 ns … 1 ms, uniform
        Assert.Equal(100_000, histogram.Count);
        Assert.Equal(10, histogram.Min);
        Assert.Equal(1_000_000, histogram.Max);
        Assert.Equal(500_005, histogram.Mean, 6);
        Assert.Equal(500_000, histogram.Percentile(0.5), 500_000 * 0.01);
        Assert.Equal(990_000, histogram.Percentile(0.99), 990_000 * 0.01);
        Assert.Equal(1_000_000, histogram.Percentile(1));

        int previous = -1;
        for (long v = 0; v < 1 << 20; v += 37)
        {
            int index = LatencyHistogram.Index(v);
            Assert.True(index >= previous);
            Assert.InRange(LatencyHistogram.Midpoint(index), v * 0.99 - 1, v * 1.01 + 1);
            previous = index;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) histogram.Record(i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void CommandLineRunsAndRejectsTypos()
    {
        var output = new StringWriter();
        Assert.Equal(0, BenchCli.Run(["analyzer", "--scenario", "idle", "--frames", "200", "--warmup", "10"], output, TextWriter.Null));
        Assert.Contains("| idle |", output.ToString());
        Assert.Equal(2, BenchCli.Run(["analyzer", "--scenario", "jazz"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, BenchCli.Run(["sock"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(0, BenchCli.Run(["--help"], TextWriter.Null, TextWriter.Null));
    }
}
