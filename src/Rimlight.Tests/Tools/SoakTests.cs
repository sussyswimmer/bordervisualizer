using System.Text.Json;
using Rimlight.AudioTools;
using Rimlight.Bench;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Tests.Tools;

// The soak measures process memory (live managed heap after a full collection), so its tests run alone: xUnit runs a
// collection with DisableParallelization after the parallel ones, with nothing else in flight.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SoakCollection
{
    public const string Name = "Soak (runs alone)";
}

// C12: the soak harness. The quick soak is the CI form of the 8-hour run (docs/PERF.md): the same render-thread calls on
// a compressed program that visits every path in two simulated minutes, asserting zero steady-state allocations and
// flat managed memory.
[Collection(SoakCollection.Name)]
public sealed class SoakTests
{
    private static readonly SoakOptions Quick = new()
    {
        Hours = 2.0 / 60,
        Shape = SoakProgramShape.Compressed,
        WarmupSeconds = 1,
        ReportEveryMinutes = 0.25,
    };

    [Fact]
    public void QuickSoakAllocatesNothingAndKeepsManagedMemoryFlat()
    {
        var reported = new List<SoakSnapshot>();
        SoakResult r = SoakRun.Run(Quick, reported.Add);

        // The two minutes cover what the 8 hours do: sessions with both silent behaviors and a device change, tracks
        // with new and same-album palettes, digital silence and no-packet gaps, and a light that fades out and idles.
        SoakProgramSummary p = r.Program;
        Assert.Equal(120, p.Seconds, 6);
        Assert.InRange(p.Sessions, 2, 3);
        Assert.InRange(p.Tracks, 5, 9);
        Assert.InRange(p.SameAlbumTracks, 1, p.Tracks - 1);
        Assert.True(p.SilenceSeconds > 4.5, $"digital silence {p.SilenceSeconds:0.0} s");
        Assert.True(p.NoPacketSeconds > 4.5, $"no packets {p.NoPacketSeconds:0.0} s");
        Assert.True(r.SilentFrames > 0 && r.HiddenFrames > 0 && r.StaticFrames > 0, $"silent {r.SilentFrames}, hidden {r.HiddenFrames}, static {r.StaticFrames}");

        // Every frame was measured, and the music really drove the light.
        Assert.InRange(r.Frames, 120 * 60 - 200, 120 * 60 + 200);
        Assert.True(r.Analyzer.MeanNs > 0 && r.LightEngine.MeanNs > 0 && r.Palette.MeanNs > 0);
        Assert.True(r.Frame.MeanNs >= r.Analyzer.MeanNs);
        Assert.InRange(r.Beats, (long)(0.8 * p.Kicks), (long)(1.2 * p.Kicks));
        Assert.True(r.WarmupTours >= 1);

        // The verdict: nothing allocated in steady state, and managed memory flat.
        Assert.Equal(0, r.AllocatedBytes);
        Assert.Equal(0, r.AllocatingFrames);
        Assert.Null(r.FirstAllocationSeconds);
        Assert.True(r.ManagedGrowthBytes <= SoakResult.MaxManagedGrowthBytes, $"live managed heap grew {r.ManagedGrowthBytes:N0} B");
        Assert.True(r.Passed);

        Assert.Equal(r.Snapshots, reported);
        Assert.Equal(8, r.Snapshots.Count);                   // every 15 s; the last one at the end
        Assert.Equal(120, r.Snapshots[^1].SimulatedSeconds, 6);
        Assert.Equal(r.Frames, r.Snapshots[^1].Frames);
        Assert.All(r.Snapshots, s => Assert.Equal(0, s.AllocatedBytes));
        Assert.All(r.Snapshots, s => Assert.InRange(s.Interval.MeanNs, 1, 1e8));
    }

    [Fact]
    public void AnAllocatingFrameFailsTheSoakAndIsLocated()
    {
        // A light engine that allocates on every 100th update: each of those frames is counted, with its bytes.
        var kept = new AllocatingEngine.Keeper();
        SoakResult r = SoakRun.Run(Quick with
        {
            Hours = 0.25 / 60,
            WarmupSeconds = 0,
            WrapLightEngine = engine => new AllocatingEngine(engine, every: 100, retain: false, kept),
        });
        long expected = r.Frames / 100;
        Assert.True(expected >= 8);
        Assert.InRange(r.AllocatingFrames, expected, expected + 1);
        Assert.Equal(r.AllocatingFrames * AllocatingEngine.ArrayBytes, r.AllocatedBytes);
        Assert.NotNull(r.FirstAllocationSeconds);
        Assert.InRange(r.FirstAllocationSeconds!.Value, 0, 100 / 60.0 * 1.25);
        Assert.Contains(r.Snapshots, s => s.AllocatedBytes > 0);
        Assert.False(r.AllocationFree);
        Assert.True(r.ManagedMemoryFlat);                     // garbage only: the heap doesn't grow
        Assert.False(r.Passed);
    }

    [Fact]
    public void RetainedMemoryFailsTheSoak()
    {
        // A leak: every 5th update keeps its array. The 900 measured frames keep 180 arrays of 4 KB (about 740 KB), well
        // over the 256 KB allowed. (The warm-up tour keeps some too, but before the start sample.)
        var kept = new AllocatingEngine.Keeper();
        SoakResult r = SoakRun.Run(Quick with
        {
            Hours = 0.25 / 60,
            WarmupSeconds = 0,
            WrapLightEngine = engine => new AllocatingEngine(engine, every: 5, retain: true, kept),
        });
        Assert.True(r.ManagedGrowthBytes > SoakResult.MaxManagedGrowthBytes, $"grew {r.ManagedGrowthBytes:N0} B");
        Assert.False(r.ManagedMemoryFlat);
        Assert.False(r.Passed);
        GC.KeepAlive(kept);
    }

    [Fact]
    public void CommandLineRunsAShortSoakAndWritesTheJsonReport()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rimlight-soak-" + Guid.NewGuid().ToString("N"));
        string json = Path.Combine(directory, "report.json");
        try
        {
            var output = new StringWriter();
            int exit = BenchCli.Run(["soak", "--minutes", "0.5", "--fps", "30", "--warmup-seconds", "0", "--report-every", "0.25", "--json", json], output, TextWriter.Null);
            string text = output.ToString();
            Assert.True(exit == 0, text);
            Assert.Contains("0:00:30 simulated at 30 fps", text);
            Assert.Contains("| **whole frame** |", text);
            Assert.Contains("Verdict:** PASS", text);

            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(json));
            JsonElement root = report.RootElement;
            Assert.Equal("pass", root.GetProperty("verdict").GetString());
            Assert.Equal(30, root.GetProperty("options").GetProperty("fps").GetDouble());
            Assert.Equal(0.5 / 60, root.GetProperty("options").GetProperty("hours").GetDouble(), 9);
            Assert.InRange(root.GetProperty("frames").GetInt64(), 30 * 30 - 30, 30 * 30 + 30);
            Assert.Equal(0, root.GetProperty("allocations").GetProperty("bytes").GetInt64());
            Assert.True(root.GetProperty("costNs").GetProperty("frame").GetProperty("p99").GetDouble() > 0);
            Assert.True(root.GetProperty("memory").GetProperty("end").GetProperty("workingSetBytes").GetInt64() > 0);
            Assert.Equal(2, root.GetProperty("snapshots").GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    // Wraps the real engine and allocates a 4 KB array on every Nth update, optionally keeping it (a leak).
    private sealed class AllocatingEngine(ILightEngine inner, int every, bool retain, AllocatingEngine.Keeper kept) : ILightEngine
    {
        public const int ArrayBytes = 4096 + 24; // the array's elements plus its header on 64-bit
        private int updates;

        public bool IsStatic => inner.IsStatic;

        public LightState Update(float dtSeconds, in AudioFeatures audio, Palette palette, Settings settings, bool paused)
        {
            if (++updates % every == 0)
            {
                byte[] array = new byte[4096];
                if (retain) kept.Arrays.Add(array);
            }
            return inner.Update(dtSeconds, in audio, palette, settings, paused);
        }

        public sealed class Keeper
        {
            public List<byte[]> Arrays { get; } = new(capacity: 100_000); // sized up front: keeping allocates only the arrays
        }
    }
}

// C12: the synthetic listening program the soak plays. No Core calls, so these run in parallel with everything else.
public sealed class SoakProgramTests
{
    [Fact]
    public void TheDefaultProgramIsADeterministicListeningDay()
    {
        SoakProgram program = SoakProgram.Generate(8 * 3600, 48000, 1, SoakProgramShape.Default);
        SoakProgramSummary p = program.Summary;
        Assert.Equal(8L * 3600 * 48000, program.Length);
        Assert.Equal(8 * 3600, p.Seconds, 6);

        // Sessions of 40–80 minutes with 3–12 minute pauses, tracks of 2–5 minutes: a palette change every few minutes.
        Assert.InRange(p.Sessions, 6, 11);
        Assert.InRange(p.Tracks, 90, 190);
        double minutesPerPaletteChange = (p.MusicSeconds + p.SilenceSeconds) / 60 / p.PaletteChanges;
        Assert.InRange(minutesPerPaletteChange, 2, 5.5);
        Assert.InRange(p.SameAlbumTracks / (double)p.Tracks, 0.1, 0.4);
        Assert.InRange(p.MinBpm, 80, 100);
        Assert.InRange(p.MaxBpm, 150, 170);
        Assert.InRange(p.NoPacketSeconds / p.Seconds, 0.05, 0.3);  // mostly the pauses between sessions
        Assert.InRange(p.SilenceSeconds / p.Seconds, 0.001, 0.05);
        Assert.Equal(p.Seconds, p.MusicSeconds + p.SilenceSeconds + p.NoPacketSeconds, 6);

        // Sessions alternate between the two silent behaviors, and each starts with a track.
        var sessions = new List<SoakEvent>();
        foreach (SoakEvent e in program.Events)
            if (e.Settings is not null) sessions.Add(e);
        for (int i = 0; i < sessions.Count; i++)
            Assert.Equal(i % 2 == 0 ? SilentBehavior.IdleGlow : SilentBehavior.Hide, sessions[i].Settings!.WhenSilent);

        // Tempo changes: neighbouring tracks rarely share a tempo.
        var tempos = new List<double>();
        foreach (SoakSegment s in program.Segments)
            if (s.Kind == SoakSegmentKind.Music && (tempos.Count == 0 || tempos[^1] != s.Bpm)) tempos.Add(s.Bpm);
        Assert.True(tempos.Count > p.Tracks * 0.9, $"{tempos.Count} tempo changes for {p.Tracks} tracks");

        // Same seed, same program, down to the samples; another seed, another program.
        SoakProgram again = SoakProgram.Generate(8 * 3600, 48000, 1, SoakProgramShape.Default);
        Assert.Equal(program.Summary, again.Summary);
        Assert.Equal(Samples(program, 1_000_000_000, 4800), Samples(again, 1_000_000_000, 4800));
        Assert.NotEqual(p, SoakProgram.Generate(8 * 3600, 48000, 2, SoakProgramShape.Default).Summary);
    }

    [Fact]
    public void ReadDeliversMusicAndZerosButNothingWithoutPackets()
    {
        var builder = new SoakProgram.Builder(8000, 5);
        Palette palette = Palette.Default with { SourceTrackId = builder.NextTrackId() };
        builder.StartSession(SoakProgram.HideSettings);
        builder.AddTrack(120, -6, palette, false, 0.25, 0.25, 0.25, 0.25, fullMixEnd: true); // 8000 samples
        builder.AddGap(0.5, packets: true);   // 4000 zeros
        builder.AddGap(0.5, packets: false);  // 4000 samples that never arrive
        builder.AddTrack(90, 0, palette with { SourceTrackId = builder.NextTrackId() }, true, 0.25, 0.25, 0.25, 0.25, fullMixEnd: false);
        SoakProgram program = builder.Build(null);

        Assert.Equal(24000, program.Length);
        Assert.Equal(new[] { SoakSegmentKind.Music, SoakSegmentKind.Music, SoakSegmentKind.Music, SoakSegmentKind.Music, SoakSegmentKind.Silence,
            SoakSegmentKind.NoPackets, SoakSegmentKind.Music, SoakSegmentKind.Music, SoakSegmentKind.Music, SoakSegmentKind.Music },
            program.Segments.ToArray().Select(s => s.Kind));
        Assert.Equal(3, program.Events.Length);
        Assert.Equal(0, program.Events[0].Position);
        Assert.Same(SoakProgram.HideSettings, program.Events[0].Settings);
        Assert.Same(palette, program.Events[1].Palette);
        Assert.Equal(16000, program.Events[2].Position);
        Assert.True(program.Events[2].SameAlbum);

        // One pass in uneven pieces: music where there is music, zeros for silence, nothing for the no-packet gap.
        var delivered = new List<float>();
        float[] buffer = new float[1000];
        int cursor = 0;
        long from = 0, allocated = 0;
        int[] pieces = [999, 1, 777, 1000, 0, 333, 1000];
        for (int i = 0; from < program.Length; i++)
        {
            long to = Math.Min(program.Length, from + pieces[i % pieces.Length]);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int count = program.Read(from, to, buffer, ref cursor);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            delivered.AddRange(buffer.AsSpan(0, count).ToArray());
            from = to;
        }
        Assert.Equal(program.Length, from);
        Assert.Equal(0, allocated);
        Assert.Equal(20000, delivered.Count);
        Assert.Equal(Expected(program, 0, 8000), delivered.GetRange(0, 8000));
        Assert.All(delivered.GetRange(8000, 4000), x => Assert.Equal(0f, x));
        Assert.Equal(Expected(program, 8000, 8000), delivered.GetRange(12000, 8000));
        Assert.Contains(delivered.GetRange(0, 8000), x => x != 0);

        // The intro and the breakdown have no kicks; the bodies do.
        SoakSegment[] segments = program.Segments.ToArray();
        Assert.Equal(0, segments[0].Audio!.Options.KickLevel);
        Assert.True(segments[1].Audio!.Options.KickLevel > 0 && segments[1].Audio!.Options.BassLevel > 0);
        Assert.Equal(0, segments[2].Audio!.Options.KickLevel);
        Assert.Equal(0, segments[9].Audio!.Options.BassLevel); // the plain-mix ending
        Assert.Equal(Math.Pow(10, -6 / 20.0), segments[0].Audio!.Options.Gain, 12);
    }

    [Fact]
    public void BuildCutsTheLastSegment()
    {
        var builder = new SoakProgram.Builder(1000, 1);
        builder.AddGap(1, packets: true);
        builder.AddGap(1, packets: false);
        SoakProgram program = builder.Build(1.5);
        Assert.Equal(1500, program.Length);
        Assert.Equal(500, program.Segments[1].Length);
        Assert.Equal(1.0, program.Summary.SilenceSeconds, 9);
        Assert.Equal(0.5, program.Summary.NoPacketSeconds, 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(3));
    }

    [Fact]
    public void TheTourVisitsEveryPath()
    {
        SoakProgram tour = SoakProgram.Tour(48000, 1);
        Assert.InRange(tour.Seconds, 60, 120);
        var settings = new List<Settings>();
        int sameAlbum = 0, tracks = 0;
        foreach (SoakEvent e in tour.Events)
        {
            if (e.Settings is not null) settings.Add(e.Settings);
            if (e.Palette is not null) tracks++;
            if (e.SameAlbum) sameAlbum++;
        }
        Assert.Equal(new[] { SoakProgram.IdleGlowSettings, SoakProgram.HideSettings }, settings);
        Assert.True(tracks >= 5 && sameAlbum >= 1);

        // Gaps of both kinds longer than the 2 s silence hold plus the 1.5 s fade, so the light reaches Idle Glow,
        // hides and turns static; and a short one.
        double longest(SoakSegmentKind kind) => tour.Segments.ToArray().Where(s => s.Kind == kind).Max(s => s.Length) / 48000.0;
        Assert.True(longest(SoakSegmentKind.Silence) >= 3.5);
        Assert.True(longest(SoakSegmentKind.NoPackets) >= 3.5);
        Assert.Contains(tour.Segments.ToArray(), s => s.Kind == SoakSegmentKind.Silence && s.Length < 48000);
        Assert.Contains(tour.Segments.ToArray(), s => s.Kind == SoakSegmentKind.Music && s.Audio!.Options.KickLevel == 0);
    }

    private static float[] Samples(SoakProgram program, long from, int count)
    {
        float[] buffer = new float[count];
        int cursor = 0;
        int n = program.Read(from, from + count, buffer, ref cursor);
        return buffer[..n];
    }

    // The program's music between two delivered offsets, sample by sample from the segments' own tracks.
    private static List<float> Expected(SoakProgram program, long start, int count)
    {
        var result = new List<float>();
        foreach (SoakSegment s in program.Segments)
        {
            if (s.Kind != SoakSegmentKind.Music) continue;
            for (long i = 0; i < s.Length; i++) result.Add(s.Audio!.SampleAt(i));
        }
        return result.GetRange((int)start, count);
    }
}
