using System.Diagnostics;
using Rimlight.Core;

namespace Rimlight.Bench;

internal sealed record FrameBenchmarkOptions
{
    public double Fps { get; init; } = 60;
    public int WarmupFrames { get; init; } = 2000;
    /// <summary>Keep warming up at least this long, so tiered JIT has installed optimized code before timing.</summary>
    public double MinWarmupSeconds { get; init; } = 0.5;
    public int Frames { get; init; } = 10000;
    /// <summary>
    /// 0: every frame gets exactly one frame of audio (rate / fps samples), which the analyzer handles in one step.
    /// Above 0: the audio arrives in whole packets this long, as the loopback capture delivers it (doc 03 §1: about
    /// 10 ms), and each frame gets the packets that arrived since the last one: at 60 fps one or two, and two packets
    /// are two analyzer steps (C2), so a frame costs more on average.
    /// </summary>
    public double PacketSeconds { get; init; }
}

/// <summary>One row of the frame benchmark.</summary>
/// <param name="SampleRate">The capture rate.</param>
/// <param name="Scenario">The scenario.</param>
/// <param name="SamplesPerFrame">Samples per frame on average (rate / fps).</param>
/// <param name="PacketSamples">The packet length in samples; 0 when each frame gets exactly one frame of audio.</param>
/// <param name="Costs">The measured frames' cost per stage.</param>
/// <param name="BytesPerFrame">Bytes allocated per frame.</param>
/// <param name="Gen0">Gen 0 GCs while measuring.</param>
/// <param name="Gen1">Gen 1 GCs while measuring.</param>
/// <param name="Gen2">Gen 2 GCs while measuring.</param>
/// <param name="Refills">Measured frames that refilled the gradient.</param>
/// <param name="Beats">Beats detected while measuring.</param>
internal sealed record FrameBenchmarkResult(int SampleRate, string Scenario, int SamplesPerFrame, int PacketSamples, StageCosts Costs,
    double BytesPerFrame, int Gen0, int Gen1, int Gen2, long Refills, int Beats);

// One render-thread frame as the app runs it (FramePipeline = MusicGlowSource.NextFrame: analyzer, palette blender +
// gradient, light engine), per stage, at one capture rate. The music is rendered before timing starts and fed in exact
// frame-sized chunks (the analyzer's one-step best case) or, with PacketSeconds, in whole packets on the packet clock
// (what the app gets), so only the frame is measured. Allocations are counted around each NextFrame call alone
// (GC.GetAllocatedBytesForCurrentThread), so they must be 0.
//   steady     the album colors settled during warm-up: the blender has nothing to do and the gradient isn't refilled.
//   crossfade  new album colors every 0.8 s with an 800 ms fade, so every frame crossfades and refills the gradient:
//              the most a frame ever does.
internal static class FrameBenchmark
{
    public static readonly int[] DefaultRates = [48000, 44100, 192000];
    public static readonly string[] Scenarios = ["steady", "crossfade"];

    public static string Describe(string scenario) => scenario switch
    {
        "steady" => "music, colors settled",
        "crossfade" => "music, new album colors every 0.8 s (every frame crossfades and refills the gradient)",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    public static FrameBenchmarkResult Run(int sampleRate, string scenario, FrameBenchmarkOptions options)
    {
        if (Array.IndexOf(Scenarios, scenario) < 0) throw new ArgumentOutOfRangeException(nameof(scenario));
        int frameSamples = (int)Math.Round(sampleRate / options.Fps);
        int packet = options.PacketSeconds > 0 ? Math.Max(1, (int)Math.Round(options.PacketSeconds * sampleRate)) : 0;
        float dt = (float)(1 / options.Fps);
        float[] signal = AnalyzerBenchmark.Signal("music", sampleRate);
        int usable = Math.Max(1, signal.Length - (packet > 0 ? (frameSamples / packet + 2) * packet : frameSamples));
        int changeEvery = scenario == "crossfade" ? Math.Max(1, (int)Math.Round(FramePipeline.AlbumFade.TotalSeconds * options.Fps)) : int.MaxValue;
        Palette[] albums = Albums();
        var settings = new Settings();
        var pipeline = new FramePipeline(settings, Palette.Default);
        int offset = 0;
        long frame = 0;

        long warmupStart = Stopwatch.GetTimestamp();
        for (int i = 0; i < options.WarmupFrames || Stopwatch.GetElapsedTime(warmupStart).TotalSeconds < options.MinWarmupSeconds; i++)
            Next(ref frame);

        var costs = new StageCosts();
        int beatsBefore = pipeline.Analyzer.Diagnostics.BeatCount;
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long allocated = 0, refills = 0;
        for (int i = 0; i < options.Frames; i++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            Next(ref frame);
            allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
            costs.Record(pipeline.Ticks);
            if (pipeline.Refilled) refills++;
        }
        return new FrameBenchmarkResult(sampleRate, scenario, frameSamples, packet, costs, allocated / (double)options.Frames,
            GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2, refills,
            pipeline.Analyzer.Diagnostics.BeatCount - beatsBefore);

        void Next(ref long n)
        {
            // The first frame opens the capture; the album colors change every changeEvery frames (steady: once, at 0).
            Palette album = albums[(int)(n / changeEvery % albums.Length)];
            int count = packet > 0 ? (int)(Arrived(n + 1) - Arrived(n)) : frameSamples;
            pipeline.NextFrame(signal.AsSpan(offset, count), sampleRate, dt, settings, album, paused: false, captureChanged: n == 0);
            offset += count;
            if (offset >= usable) offset = 0;
            n++;
        }

        // The samples of the packets that have arrived when frame `frame` starts.
        long Arrived(long frame) => (long)(frame * (double)sampleRate / options.Fps) / packet * packet;
    }

    // Eight distinct glow-like palettes, built before timing.
    private static Palette[] Albums()
    {
        var albums = new Palette[8];
        for (int i = 0; i < albums.Length; i++)
        {
            float h = i / 8f;
            albums[i] = new Palette(new Rgb(0.5f + 0.5f * MathF.Cos(6.2832f * h), 0.5f + 0.5f * MathF.Cos(6.2832f * (h + 0.33f)), 0.5f + 0.5f * MathF.Cos(6.2832f * (h + 0.67f))),
                new Rgb(0.5f + 0.5f * MathF.Sin(6.2832f * h), 0.3f, 0.9f - 0.5f * h), $"album {i}");
        }
        return albums;
    }
}
