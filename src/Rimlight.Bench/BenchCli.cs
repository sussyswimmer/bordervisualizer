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
            --warmup <n>         Unmeasured frames first (default 2000, and at least 0.5 s)
          frame [options]      One render-thread frame as the app runs it (analyzer, palette blender + gradient, light
                               engine): ns per stage, bytes/frame and share of one core at each sample rate, with the
                               colors settled and while every frame crossfades
            --rates <list>       Sample rates in Hz, comma-separated (default 48000,44100,192000)
            --fps <n>            Frames per second; one frame of audio per call (default 60)
            --frames <n>         Measured frames per row (default 10000)
            --warmup <n>         Unmeasured frames first (default 2000, and at least 0.5 s)
          soak [options]       Simulated hours of the app's render loop over a scripted day of listening (tracks with
                               new album colors, silent gaps, settings edits, pauses, stalls): frame cost per stage,
                               allocations, GCs and memory, with pass/fail lines for 0 B/frame and flat memory
            --duration <time>    Simulated time, e.g. 8h, 90m, 45s or 1h30m (default 8h)
            --minutes <n>        The same in minutes (instead of --duration)
            --rate <Hz> (48000)  --fps <n> (60)  --jitter <f> (0.2)  --packet-ms <ms> (10)  --seed <n> (1)
            --warmup-seconds <s> Simulated seconds run before measuring starts (default 5)
            --report-every <min> Simulated minutes between progress lines (default: ten lines)
            --markdown           Also print the summary as Markdown, for docs/PERF.md

        Exit codes: 0 success, 1 allocations found on the measured path (soak: or memory not flat),
        2 bad command line.

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
                "frame" => Frame(rest, output),
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

    private static int Frame(string[] args, TextWriter output)
    {
        var a = new CommandLine(args, ["rates", "fps", "frames", "warmup"], []);
        if (a.Positional.Count != 0) throw new UsageException($"Unexpected argument \"{a.Positional[0]}\".");
        int[] rates = Rates(a.Text("rates")) ?? FrameBenchmark.DefaultRates;
        var options = new FrameBenchmarkOptions
        {
            Fps = a.Number("fps", 60, 1, 1000),
            Frames = a.Integer("frames", 10000, 1, 10_000_000),
            WarmupFrames = a.Integer("warmup", 2000, 0, 10_000_000),
        };
        output.WriteLine(string.Create(C, $"Frame benchmark: the render thread's frame (FramePipeline, as MusicGlowSource.NextFrame), {options.Fps:0.##} fps, {options.WarmupFrames} warm-up frames (at least {options.MinWarmupSeconds:0.#} s) + {options.Frames} measured frames per row"));
        output.WriteLine(Machine());
        var results = new List<FrameBenchmarkResult>();
        foreach (int rate in rates)
        {
            foreach (string scenario in FrameBenchmark.Scenarios) results.Add(FrameBenchmark.Run(rate, scenario, options));
        }
        double clockRead = StageCosts.ClockReadNanoseconds();

        output.WriteLine();
        output.WriteLine("| rate | samples/frame | scenario | frame mean ns | p50 | p99 | p99.9 | max | bytes/frame | core % | GCs (0/1/2) | refills | beats |");
        output.WriteLine("|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|");
        foreach (FrameBenchmarkResult r in results)
        {
            LatencyHistogram f = r.Costs[Stage.Frame];
            output.WriteLine(string.Create(C,
                $"| {r.SampleRate} | {r.SamplesPerFrame} | {r.Scenario} | {f.Mean:N0} | {f.Percentile(0.5):N0} | {f.Percentile(0.99):N0} | {f.Percentile(0.999):N0} | {f.Max:N0} | {r.BytesPerFrame:0.##} | {f.Mean * options.Fps / 1e9 * 100:0.000} | {r.Gen0}/{r.Gen1}/{r.Gen2} | {r.Refills} | {r.Beats} |"));
        }
        output.WriteLine();
        output.WriteLine("| rate | scenario | analyzer mean ns | p99 | blender + gradient mean ns | p99 | light engine mean ns | p99 |");
        output.WriteLine("|---:|---|---:|---:|---:|---:|---:|---:|");
        foreach (FrameBenchmarkResult r in results)
        {
            LatencyHistogram an = r.Costs[Stage.Analyzer], bl = r.Costs[Stage.Blender], en = r.Costs[Stage.Engine];
            output.WriteLine(string.Create(C,
                $"| {r.SampleRate} | {r.Scenario} | {an.Mean:N0} | {an.Percentile(0.99):N0} | {bl.Mean:N0} | {bl.Percentile(0.99):N0} | {en.Mean:N0} | {en.Percentile(0.99):N0} |"));
        }
        output.WriteLine();
        foreach (string scenario in FrameBenchmark.Scenarios) output.WriteLine($"{scenario}: {FrameBenchmark.Describe(scenario)}.");
        output.WriteLine("core %: mean frame cost × fps, as a share of one CPU core. Timings vary between machines and runs; bytes/frame must be 0.");
        output.WriteLine(string.Create(C, $"Timing: {StageCosts.ClockReadsNote} ({clockRead:0} ns per read here)."));
        return results.TrueForAll(r => r.BytesPerFrame == 0) ? 0 : 1;
    }

    private static int Soak(string[] args, TextWriter output)
    {
        var a = new CommandLine(args, ["duration", "minutes", "rate", "fps", "jitter", "packet-ms", "seed", "warmup-seconds", "report-every"], ["markdown"]);
        if (a.Positional.Count != 0) throw new UsageException($"Unexpected argument \"{a.Positional[0]}\".");
        if (a.Has("duration") && a.Has("minutes")) throw new UsageException("Give --duration or --minutes, not both.");
        double minutes = a.Text("duration") is { } duration ? Duration(duration) / 60 : a.Number("minutes", 8 * 60, 0.05, 48 * 60);
        var options = new SoakOptions
        {
            Minutes = minutes,
            SampleRate = a.Integer("rate", 48000, 8000, 384000),
            Fps = a.Number("fps", 60, 1, 1000),
            Jitter = a.Number("jitter", 0.2, 0, 0.9),
            PacketSeconds = a.Number("packet-ms", 10, 0, 1000) / 1000,
            Seed = a.Seed("seed", 1),
            WarmupSeconds = a.Number("warmup-seconds", 5, 0, 3600),
            ReportEveryMinutes = a.Number("report-every", 0, 0, 48 * 60),
        };
        output.WriteLine(string.Create(C, $"Soak: {SoakReport.Clock(options.Minutes * 60)} simulated of the render thread's frames (FramePipeline) over a scripted day of listening (seed {options.Seed}), {options.SampleRate} Hz, {options.Fps:0.##} fps ±{options.Jitter * 100:0.#} %, {options.PacketSeconds * 1000:0.#} ms packets, first {options.WarmupSeconds:0.#} s not measured"));
        output.WriteLine(Machine());
        SoakReport report = SoakRun.Execute(options, s => output.WriteLine(SoakRun.Format(s)));
        report.WriteSummary(output);
        if (a.Has("markdown"))
        {
            output.WriteLine();
            report.WriteMarkdown(output);
        }
        return report.Passed ? 0 : 1;
    }

    /// <summary>Parses a simulated duration: numbers with h, m or s units, summed ("8h", "90m", "1h30m", "0.5h").</summary>
    /// <param name="text">The text.</param>
    /// <returns>The duration in seconds, from 3 s to 48 h.</returns>
    /// <exception cref="UsageException">The text is not such a duration.</exception>
    internal static double Duration(string text)
    {
        string problem = $"--duration expects a time like 8h, 90m, 45s or 1h30m, not \"{text}\".";
        double total = 0;
        int i = 0;
        while (i < text.Length)
        {
            int start = i;
            while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.')) i++;
            if (i == start || i == text.Length || !double.TryParse(text.AsSpan(start, i - start), NumberStyles.AllowDecimalPoint, C, out double value))
                throw new UsageException(problem);
            total += value * char.ToLowerInvariant(text[i]) switch
            {
                'h' => 3600,
                'm' => 60,
                's' => 1,
                _ => throw new UsageException(problem),
            };
            i++;
        }
        if (text.Length == 0) throw new UsageException(problem);
        if (total < 3 || total > 48 * 3600) throw new UsageException("--duration must be from 3s to 48h.");
        return total;
    }

    private static int[]? Rates(string? text)
    {
        if (text is null) return null;
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new UsageException("--rates needs at least one sample rate.");
        int[] rates = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, C, out rates[i]) || rates[i] is < 8000 or > 384000)
                throw new UsageException($"--rates expects sample rates from 8000 to 384000 Hz, comma-separated, not \"{text}\".");
        }
        return rates;
    }

    private static string Machine()
    {
        string build =
#if DEBUG
            "DEBUG build: timings are not representative";
#else
            "Release";
#endif
        string cpu = ProcessMemory.CpuModel() is { Length: > 0 } model ? $" ({model})" : "";
        return $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription.Trim()} {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical CPUs{cpu}, {build}";
    }
}
