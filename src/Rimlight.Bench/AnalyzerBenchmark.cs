using System.Diagnostics;
using Rimlight.AudioTools;
using Rimlight.Core;

namespace Rimlight.Bench;

internal sealed record AnalyzerBenchmarkOptions
{
    public int SampleRate { get; init; } = 48000;
    public double Fps { get; init; } = 60;
    public int WarmupFrames { get; init; } = 2000;
    public int Frames { get; init; } = 10000;
}

internal sealed record BenchmarkResult(string Scenario, LatencyHistogram Cost, double BytesPerFrame, int Gen0, int Gen1, int Gen2, int Beats);

// IAudioAnalyzer.Process cost per render frame (doc 02: the render thread runs it every frame). The signal is
// rendered before timing starts and fed in exact frame-sized chunks, so only the analyzer is measured. Allocations
// are counted around each Process call alone (GC.GetAllocatedBytesForCurrentThread), so they must be 0.
internal static class AnalyzerBenchmark
{
    public static readonly string[] Scenarios = ["music", "kick", "noise", "silence", "idle"];

    public static string Describe(string scenario) => scenario switch
    {
        "music" => "synthetic full mix, 128 BPM",
        "kick" => "kicks + noise + vocal tones, 120 BPM",
        "noise" => "white noise, -10 dBFS",
        "silence" => "digital silence, packets still arriving",
        "idle" => "no packets (empty spans), as while nothing plays",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    public static BenchmarkResult Run(string scenario, AnalyzerBenchmarkOptions options)
    {
        int frameSamples = (int)Math.Round(options.SampleRate / options.Fps);
        float dt = (float)(1 / options.Fps);
        float[] signal = Signal(scenario, options.SampleRate);
        int usable = Math.Max(1, signal.Length - frameSamples);
        IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer();
        int offset = 0;

        for (int i = 0; i < options.WarmupFrames; i++) Process(analyzer, signal, ref offset, frameSamples, usable, options.SampleRate, dt);

        var cost = new LatencyHistogram();
        int beatsBefore = analyzer.Diagnostics.BeatCount;
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long allocated = 0;
        for (int i = 0; i < options.Frames; i++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            Process(analyzer, signal, ref offset, frameSamples, usable, options.SampleRate, dt);
            long ticks = Stopwatch.GetTimestamp() - start;
            allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
            cost.RecordTicks(ticks);
        }
        return new BenchmarkResult(scenario, cost, allocated / (double)options.Frames,
            GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2,
            analyzer.Diagnostics.BeatCount - beatsBefore);
    }

    private static void Process(IAudioAnalyzer analyzer, float[] signal, ref int offset, int frameSamples, int usable, int sampleRate, float dt)
    {
        if (signal.Length == 0)
        {
            analyzer.Process(ReadOnlySpan<float>.Empty, sampleRate, dt);
            return;
        }
        analyzer.Process(signal.AsSpan(offset, frameSamples), sampleRate, dt);
        offset += frameSamples;
        if (offset >= usable) offset = 0;
    }

    // 20 s of audio, looped. "idle" has none: every frame gets an empty span.
    private static float[] Signal(string scenario, int sampleRate)
    {
        var track = new SyntheticTrackOptions { SampleRate = sampleRate, Seconds = 20 };
        return scenario switch
        {
            "music" => SyntheticTrack.Render((track with { Bpm = 128 }).WithFullMix()),
            "kick" => SyntheticTrack.Render(track),
            "noise" => SyntheticTrack.Render(track with { KickLevel = 0, VocalLevel = 0, NoiseLevel = 0.316 }),
            "silence" => new float[sampleRate * 20],
            "idle" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }
}
