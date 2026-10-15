using System.Runtime.InteropServices;
using System.Text.Json;

namespace Rimlight.Bench;

// The soak's --json report: the options, the machine, the program, every result and the progress snapshots, in one
// object with camelCase names. Durations are in seconds, costs in nanoseconds, memory in bytes.
internal static class SoakReport
{
    public const int FormatVersion = 1;

    public static void Write(SoakResult r, string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using FileStream stream = File.Create(path);
        Write(r, stream);
    }

    public static void Write(SoakResult r, Stream stream)
    {
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        w.WriteStartObject();
        w.WriteNumber("formatVersion", FormatVersion);
        w.WriteString("verdict", r.Passed ? "pass" : "fail");

        w.WriteStartObject("machine");
        w.WriteString("runtime", RuntimeInformation.FrameworkDescription);
        w.WriteString("os", RuntimeInformation.OSDescription.Trim());
        w.WriteString("architecture", RuntimeInformation.ProcessArchitecture.ToString());
        w.WriteString("cpu", MachineInfo.CpuName());
        w.WriteNumber("logicalCpus", Environment.ProcessorCount);
        w.WriteString("build", MachineInfo.Build);
        w.WriteEndObject();

        SoakOptions o = r.Options;
        w.WriteStartObject("options");
        w.WriteNumber("hours", o.Hours);
        w.WriteNumber("sampleRate", o.SampleRate);
        w.WriteNumber("fps", o.Fps);
        w.WriteNumber("jitter", o.Jitter);
        w.WriteNumber("packetSeconds", o.PacketSeconds);
        w.WriteNumber("seed", o.Seed);
        w.WriteNumber("warmupSeconds", o.WarmupSeconds);
        w.WriteEndObject();

        SoakProgramSummary p = r.Program;
        w.WriteStartObject("program");
        w.WriteNumber("seconds", p.Seconds);
        w.WriteNumber("sessions", p.Sessions);
        w.WriteNumber("tracks", p.Tracks);
        w.WriteNumber("paletteChanges", p.PaletteChanges);
        w.WriteNumber("sameAlbumTracks", p.SameAlbumTracks);
        w.WriteNumber("minBpm", p.MinBpm);
        w.WriteNumber("maxBpm", p.MaxBpm);
        w.WriteNumber("musicSeconds", p.MusicSeconds);
        w.WriteNumber("silenceSeconds", p.SilenceSeconds);
        w.WriteNumber("noPacketSeconds", p.NoPacketSeconds);
        w.WriteNumber("kicks", p.Kicks);
        w.WriteEndObject();

        w.WriteStartObject("warmup");
        w.WriteNumber("tours", r.WarmupTours);
        w.WriteNumber("simulatedSeconds", r.WarmupSimulatedSeconds);
        w.WriteNumber("wallSeconds", r.WarmupWallSeconds);
        w.WriteEndObject();

        w.WriteNumber("wallSeconds", r.WallSeconds);
        w.WriteNumber("frames", r.Frames);
        w.WriteStartObject("costNs");
        Cost(w, "frame", r.Frame);
        Cost(w, "analyzer", r.Analyzer);
        Cost(w, "lightEngine", r.LightEngine);
        Cost(w, "palette", r.Palette);
        w.WriteEndObject();
        w.WriteNumber("corePercent", r.CorePercent);

        w.WriteStartObject("allocations");
        w.WriteNumber("bytes", r.AllocatedBytes);
        w.WriteNumber("bytesPerFrame", r.BytesPerFrame);
        w.WriteNumber("allocatingFrames", r.AllocatingFrames);
        if (r.FirstAllocationSeconds is double first) w.WriteNumber("firstAllocationSeconds", first);
        else w.WriteNull("firstAllocationSeconds");
        w.WriteNumber("processBytesExcludingReports", r.ProcessAllocatedBytes);
        w.WriteNumber("gen0Collections", r.Gen0);
        w.WriteNumber("gen1Collections", r.Gen1);
        w.WriteNumber("gen2Collections", r.Gen2);
        w.WriteEndObject();

        w.WriteStartObject("memory");
        Memory(w, "start", r.Start);
        Memory(w, "end", r.End);
        w.WriteNumber("managedGrowthBytes", r.ManagedGrowthBytes);
        w.WriteNumber("maxManagedGrowthBytes", SoakResult.MaxManagedGrowthBytes);
        w.WriteNumber("peakWorkingSetBytes", r.PeakWorkingSetBytes);
        w.WriteEndObject();

        w.WriteStartObject("light");
        w.WriteNumber("beats", r.Beats);
        w.WriteNumber("silentFrames", r.SilentFrames);
        w.WriteNumber("hiddenFrames", r.HiddenFrames);
        w.WriteNumber("staticFrames", r.StaticFrames);
        w.WriteEndObject();

        w.WriteStartArray("snapshots");
        foreach (SoakSnapshot s in r.Snapshots)
        {
            w.WriteStartObject();
            w.WriteNumber("simulatedSeconds", s.SimulatedSeconds);
            w.WriteNumber("frames", s.Frames);
            Cost(w, "intervalCostNs", s.Interval);
            w.WriteNumber("allocatedBytes", s.AllocatedBytes);
            w.WriteNumber("allocatingFrames", s.AllocatingFrames);
            w.WriteNumber("heapBytes", s.HeapBytes);
            w.WriteNumber("workingSetBytes", s.WorkingSetBytes);
            w.WriteNumber("gen0Collections", s.Gen0);
            w.WriteNumber("gen1Collections", s.Gen1);
            w.WriteNumber("gen2Collections", s.Gen2);
            w.WriteNumber("beats", s.Beats);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void Cost(Utf8JsonWriter w, string name, CostSummary s)
    {
        w.WriteStartObject(name);
        w.WriteNumber("mean", Math.Round(s.MeanNs, 1));
        w.WriteNumber("p50", Math.Round(s.P50Ns, 1));
        w.WriteNumber("p99", Math.Round(s.P99Ns, 1));
        w.WriteNumber("p999", Math.Round(s.P999Ns, 1));
        w.WriteNumber("max", s.MaxNs);
        w.WriteEndObject();
    }

    private static void Memory(Utf8JsonWriter w, string name, MemorySample m)
    {
        w.WriteStartObject(name);
        w.WriteNumber("liveManagedBytes", m.LiveManagedBytes);
        w.WriteNumber("workingSetBytes", m.WorkingSetBytes);
        w.WriteNumber("handles", m.Handles);
        w.WriteEndObject();
    }
}

internal static class MachineInfo
{
    public const string Build =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    public static string Describe() =>
        $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription.Trim()} {RuntimeInformation.ProcessArchitecture}, " +
        $"{CpuName()}, {Environment.ProcessorCount} logical CPUs, {(Build == "Debug" ? "DEBUG build: timings are not representative" : Build)}";

    // The processor's marketing name where the OS offers it without extra packages, otherwise "unknown CPU".
    public static string CpuName()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                foreach (string line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (line.StartsWith("model name", StringComparison.Ordinal) && line.IndexOf(':') is int colon and > 0)
                        return line[(colon + 1)..].Trim();
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key?.GetValue("ProcessorNameString") is string name && name.Trim().Length > 0) return name.Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
        return "unknown CPU";
    }
}
