using System.Globalization;
using System.Runtime.InteropServices;
using Rimlight.AudioTools;

namespace Rimlight.Bench;

internal static class BenchCli
{
    internal const string Usage = """
        Rimlight.Bench: cost of the Core hot paths. Measure a Release build:
          dotnet run -c Release --project src/Rimlight.Bench -- [command] [options]

        Commands:
          analyzer [options]   IAudioAnalyzer.Process per render frame: ns/frame (mean, p50, p99, max), bytes/frame
                               and share of one CPU core, for each signal scenario (the default command)
            --scenario <name>    music, kick, noise, silence, idle, or all (default all)
            --rate <Hz>          Sample rate (default 48000)
            --fps <n>            Frames per second; one frame of audio per call (default 60)
            --frames <n>         Measured frames per scenario (default 10000)
            --warmup <n>         Unmeasured frames first (default 2000)
          soak [options]       Simulated-time run over a long synthetic track, with progress lines
            --minutes <n>        Simulated minutes (default 1)
            --rate <Hz>  --fps <n> (60)  --jitter <f> (0.2)  --packet-ms <ms> (10)  --seed <n> (1)
            --report-every <min> Simulated minutes between progress lines (default: ten lines)

        Exit codes: 0 success, 1 allocations found on the measured path, 2 bad command line.

        """;

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            string command = args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal) ? "analyzer" : args[0];
            string[] rest = args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal) ? args : args[1..];
            if (args.Length > 0 && args[0] is "--help" or "-h" or "help" or "/?")
            {
                output.Write(Usage);
                return 0;
            }
            return command switch
            {
                "analyzer" => Analyzer(rest, output),
                "soak" => Soak(rest, output),
                _ => throw new UsageException($"Unknown command \"{command}\"."),
            };
        }
        catch (UsageException e)
        {
            error.WriteLine($"Rimlight.Bench: {e.Message}");
            error.WriteLine("Run with --help for usage.");
            return 2;
        }
    }

    private static int Analyzer(string[] args, TextWriter output)
    {
        var a = new CommandLine(args, ["scenario", "rate", "fps", "frames", "warmup"], []);
        if (a.Positional.Count != 0) throw new UsageException($"Unexpected argument \"{a.Positional[0]}\".");
        string scenario = a.Text("scenario") ?? "all";
        string[] scenarios = scenario == "all" ? AnalyzerBenchmark.Scenarios
            : AnalyzerBenchmark.Scenarios.Contains(scenario) ? [scenario]
            : throw new UsageException($"--scenario must be one of {string.Join(", ", AnalyzerBenchmark.Scenarios)} or all.");
        var options = new AnalyzerBenchmarkOptions
        {
            SampleRate = a.Integer("rate", 48000, 8000, 384000),
            Fps = a.Number("fps", 60, 1, 1000),
            Frames = a.Integer("frames", 10000, 1, 10_000_000),
            WarmupFrames = a.Integer("warmup", 2000, 0, 10_000_000),
        };
        int frameSamples = (int)Math.Round(options.SampleRate / options.Fps);
        output.WriteLine(string.Create(C, $"Analyzer benchmark: {options.SampleRate} Hz, {options.Fps:0.##} fps ({frameSamples} samples per frame), {options.WarmupFrames} warm-up + {options.Frames} measured frames per scenario"));
        output.WriteLine(Machine());
        output.WriteLine();
        output.WriteLine("| scenario | mean ns/frame | p50 | p99 | max | bytes/frame | core % | GCs (0/1/2) | beats | signal |");
        output.WriteLine("|---|---:|---:|---:|---:|---:|---:|---|---:|---|");
        bool allocationFree = true;
        foreach (string name in scenarios)
        {
            BenchmarkResult r = AnalyzerBenchmark.Run(name, options);
            allocationFree &= r.BytesPerFrame == 0;
            double corePercent = r.Cost.Mean * options.Fps / 1e9 * 100;
            output.WriteLine(string.Create(C,
                $"| {name} | {r.Cost.Mean:N0} | {r.Cost.Percentile(0.5):N0} | {r.Cost.Percentile(0.99):N0} | {r.Cost.Max:N0} | {r.BytesPerFrame:0.##} | {corePercent:0.00} | {r.Gen0}/{r.Gen1}/{r.Gen2} | {r.Beats} | {AnalyzerBenchmark.Describe(name)} |"));
        }
        output.WriteLine();
        output.WriteLine("core %: mean cost × fps, as a share of one CPU core. Timings vary between machines and runs; bytes/frame must be 0.");
        return allocationFree ? 0 : 1;
    }

    private static int Soak(string[] args, TextWriter output)
    {
        var a = new CommandLine(args, ["minutes", "rate", "fps", "jitter", "packet-ms", "seed", "report-every"], []);
        if (a.Positional.Count != 0) throw new UsageException($"Unexpected argument \"{a.Positional[0]}\".");
        var options = new SoakOptions
        {
            Minutes = a.Number("minutes", 1, 0.05, 48 * 60),
            SampleRate = a.Integer("rate", 48000, 8000, 384000),
            Fps = a.Number("fps", 60, 1, 1000),
            Jitter = a.Number("jitter", 0.2, 0, 0.9),
            PacketSeconds = a.Number("packet-ms", 10, 0, 1000) / 1000,
            Seed = a.Seed("seed", 1),
            ReportEveryMinutes = a.Number("report-every", 0, 0, 48 * 60),
        };
        output.WriteLine(string.Create(C, $"Soak: {options.Minutes:0.##} simulated minutes of synthetic music (124 BPM full mix), {options.SampleRate} Hz, {options.Fps:0.##} fps ±{options.Jitter * 100:0.#} %, {options.PacketSeconds * 1000:0.#} ms packets"));
        output.WriteLine(Machine());
        IReadOnlyList<SoakSnapshot> snapshots = SoakRun.Run(options, s => output.WriteLine(SoakRun.Format(s)));
        bool allocationFree = snapshots.Count > 0 && snapshots[^1].BytesPerFrame == 0;
        return allocationFree ? 0 : 1;
    }

    private static string Machine()
    {
        string build =
#if DEBUG
            "DEBUG build: timings are not representative";
#else
            "Release";
#endif
        return $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription.Trim()} {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical CPUs, {build}";
    }
}
