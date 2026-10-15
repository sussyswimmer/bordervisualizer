using System.Globalization;
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
            --warmup <n>         Unmeasured frames first (default 2000, and at least 0.5 s)
          soak [options]       The C12 soak: the analyzer, light engine and palette blender, exactly as the render
                               thread runs them, over hours of simulated time on a synthetic listening program
                               (tracks with tempo, level and palette changes, silence and no-packet gaps, pauses).
                               Reports allocations, frame cost (mean, p50, p99, p99.9, max) and memory at start
                               and end. CPU-bound, not real time: 8 hours take minutes.
            --hours <h>          Simulated hours (default 8)
            --minutes <m>        Simulated minutes instead, for shorter runs
            --fps <n>            Render frames per second (default 60)
            --json <file>        Also write the full report as JSON (the file is created before the run starts)
            --shape <name>       day: the listening day (default); compressed: the same structure at about a
                                 minute per session, so two minutes visit every path (what the test suite runs)
            --rate <Hz> (48000)  --jitter <f> (0.2)  --packet-ms <ms> (10)  --seed <n> (1)
            --warmup-seconds <s> Real seconds of warm-up on a short tour of every path before measuring (default 2)
            --report-every <min> Simulated minutes between progress lines (default: 16 lines; at most 10,000 lines)

        Exit codes: 0 success, 1 allocations on the measured path (or, for soak, managed memory growth), 2 bad command line
        (or, for soak, a --json file that can't be created).

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
        output.WriteLine(string.Create(C, $"Analyzer benchmark: {options.SampleRate} Hz, {options.Fps:0.##} fps ({frameSamples} samples per frame), {options.WarmupFrames} warm-up frames (at least {options.MinWarmupSeconds:0.#} s) + {options.Frames} measured frames per scenario"));
        output.WriteLine(MachineInfo.Describe());
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
        (SoakOptions options, string? json) = ParseSoak(args);
        // Created before the run, so a path that can't be written fails now (exit code 2), not after hours of soak.
        using FileStream? report = json is null ? null : CreateReport(json);
        SoakProgramShape shape = options.Shape;
        output.WriteLine(string.Create(C, $"Soak: {SoakRun.Clock(options.Seconds)} simulated{(shape == SoakProgramShape.Default ? "" : $" ({shape.Name} program)")} at {options.Fps:0.##} fps ±{options.Jitter * 100:0.#} %, {options.SampleRate} Hz in {options.PacketSeconds * 1000:0.#} ms packets; analyzer + light engine + palette blender (gradient filled every frame)"));
        output.WriteLine(MachineInfo.Describe());
        SoakResult result = SoakRun.Run(options, s => output.WriteLine(SoakRun.Format(s)), program =>
        {
            output.WriteLine(SoakRun.Describe(program.Summary, options));
            output.WriteLine();
        });
        output.WriteLine();
        output.Write(SoakRun.Summarize(result));
        if (report is not null)
        {
            SoakReport.Write(result, report);
            output.WriteLine($"JSON report: {report.Name}");
        }
        return ExitCode(result);
    }

    /// <summary>The soak's exit code: 0 when it passed, 1 when a frame allocated or managed memory grew.</summary>
    internal static int ExitCode(SoakResult result) => result.Passed ? 0 : 1;

    /// <summary>The soak command's options and its --json path (null without one).</summary>
    /// <exception cref="UsageException">The command line is wrong.</exception>
    internal static (SoakOptions Options, string? Json) ParseSoak(string[] args)
    {
        var a = new CommandLine(args, ["hours", "minutes", "rate", "fps", "jitter", "packet-ms", "seed", "warmup-seconds", "report-every", "json", "shape"], []);
        if (a.Positional.Count != 0) throw new UsageException($"Unexpected argument \"{a.Positional[0]}\".");
        if (a.Has("hours") && a.Has("minutes")) throw new UsageException("Give --hours or --minutes, not both.");
        string? json = a.Text("json");
        if (json is not null && json.Trim().Length == 0) throw new UsageException("--json needs a file name.");
        SoakProgramShape shape = a.Text("shape") switch
        {
            null or "day" => SoakProgramShape.Default,
            "compressed" => SoakProgramShape.Compressed,
            _ => throw new UsageException("--shape must be day or compressed."),
        };
        var options = new SoakOptions
        {
            Hours = a.Has("minutes") ? a.Number("minutes", 0, 0.05, 48 * 60) / 60 : a.Number("hours", 8, 0.001, 48),
            SampleRate = a.Integer("rate", 48000, 8000, 384000),
            Fps = a.Number("fps", 60, 1, 1000),
            Jitter = a.Number("jitter", 0.2, 0, 0.9),
            PacketSeconds = a.Number("packet-ms", 10, 0, 1000) / 1000,
            Seed = a.Seed("seed", 1),
            WarmupSeconds = a.Number("warmup-seconds", 2, 0, 3600),
            ReportEveryMinutes = a.Number("report-every", 0, 0, 48 * 60),
            Shape = shape,
        };
        if (options.ProgressLines > SoakOptions.MaxProgressLines)
            throw new UsageException(string.Create(C, $"--report-every {options.ReportEveryMinutes} would print {options.ProgressLines:N0} progress lines; the most is {SoakOptions.MaxProgressLines:N0}."));
        return (options, json);
    }

    private static FileStream CreateReport(string path)
    {
        try
        {
            return SoakReport.Create(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new UsageException($"Cannot write the JSON report to {path}: {e.Message}");
        }
    }
}
