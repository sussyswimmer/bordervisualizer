using Rimlight.AudioTools;
using Rimlight.Bench;
using Xunit;

namespace Rimlight.Tests.Tools;

// C3/C12: Rimlight.Bench's measurement plumbing (the soak itself is in SoakTests). The timings themselves are informational; allocations are not.
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
        Assert.Equal(2, BenchCli.Run(["soak", "--hours", "1", "--minutes", "5"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, BenchCli.Run(["soak", "--hours", "0"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, BenchCli.Run(["soak", "--json", " "], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, BenchCli.Run(["soak", "--json"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, BenchCli.Run(["soak", "--minutes", "1", "extra"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, BenchCli.Run(["soak", "--shape", "week"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(0, BenchCli.Run(["--help"], TextWriter.Null, TextWriter.Null));
    }

    [Fact]
    public void SoakDefaultsToTheEightHourRunAndLimitsItsProgressLines()
    {
        (SoakOptions options, string? json) = BenchCli.ParseSoak([]);
        Assert.Equal(8, options.Hours);
        Assert.Equal(60, options.Fps);
        Assert.Equal(48000, options.SampleRate);
        Assert.Equal(0.2, options.Jitter);
        Assert.Equal(0.01, options.PacketSeconds);
        Assert.Equal(1UL, options.Seed);
        Assert.Equal(2, options.WarmupSeconds);
        Assert.Same(SoakProgramShape.Default, options.Shape);
        Assert.Equal(16, options.ProgressLines);
        Assert.Null(options.WrapLightEngine);
        Assert.Null(json);

        // The run keeps a record per progress line, so their number is capped.
        Assert.Equal(SoakOptions.MaxProgressLines, BenchCli.ParseSoak(["--minutes", "100", "--report-every", "0.01"]).Options.ProgressLines);
        Assert.Throws<UsageException>(() => BenchCli.ParseSoak(["--minutes", "100", "--report-every", "0.0099"]));
        Assert.Throws<UsageException>(() => BenchCli.ParseSoak(["--report-every", "0.04"])); // 12,000 lines in 8 hours
        Assert.Throws<ArgumentOutOfRangeException>(() => SoakRun.Run(new SoakOptions { Hours = 0.01, ReportEveryMinutes = 0.00005, WarmupSeconds = 0 })); // 12,000
    }

    [Fact]
    public void ASoakReportThatCantBeWrittenFailsBeforeTheRun()
    {
        // A directory where the JSON file should go: refused at once with exit code 2, not after the run.
        string directory = Directory.CreateTempSubdirectory("rimlight-soak-").FullName;
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(2, BenchCli.Run(["soak", "--minutes", "0.05", "--warmup-seconds", "0", "--json", directory], output, error));
            Assert.Equal("", output.ToString());
            Assert.Contains($"Cannot write the JSON report to {directory}: ", error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
