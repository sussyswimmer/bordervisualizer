using System.Globalization;
using System.Text;
using System.Text.Json;
using Rimlight.AudioTools;
using Rimlight.Core;

namespace Rimlight.WavAnalyze;

/// <summary>The wav-analyze command line. <see cref="Run"/> is the whole program, so tests can drive it.</summary>
internal static class WavAnalyzeCli
{
    internal const string Usage = """
        wav-analyze: run Rimlight's real audio analyzer over a WAV file, offline.

        Usage:
          wav-analyze <file.wav> [options]          Analyze a WAV file: print beat times and tempo, write CSV and PNG
          wav-analyze generate <out.wav> [options]  Write a synthetic test track (kicks, noise, vocal-ish tones)
          wav-analyze selftest                      Analyze generated tracks and check the beats are found
          wav-analyze tuning [--tuning <file>]      Print the effective AudioTuning as JSON, every property
          wav-analyze --help

        Analyze options:
          --out <dir>        Directory for <name>.csv and <name>.png (default: the current directory)
          --csv <file>       CSV path (overrides --out)
          --plot <file>      PNG path (overrides --out)
          --no-plot          Skip the PNG
          --fps <n>          Render frames per second (default 60)
          --jitter <f>       Frame-time jitter as a fraction: 0.2 = each frame 80-120 % of 1/fps (default 0)
          --packet-ms <ms>   Audio arrives in whole packets of this length, as WASAPI's 10 ms;
                             0 = continuously (default 0)
          --seed <n>         Jitter seed (default 1)
          --tuning <file>    AudioTuning overrides as JSON (see below)
          --range <a>:<b>    Plot only seconds a to b, e.g. 30:45 (the CSV always has every frame)
          --width <px>       Plot width (default 80 px per plotted second, 1200 to 12000)

        Output: one CSV row per render frame with the columns
          time,level,bass,beat,isSilent,flux,threshold,bpm
        time in seconds; level, bass, beat 0..1 (what the light sees); isSilent 0/1; flux and threshold in the
        analyzer's flux units (a beat needs flux above threshold); bpm is the analyzer's estimate, 0 while unknown.

        Tuning JSON: one object whose keys are AudioTuning property names. Give any subset; the rest keep their
        defaults. Keys are case-insensitive, unknown keys are errors, comments and trailing commas are allowed:
          { "Sensitivity": 1.25, "FluxThresholdMultiplier": 1.8, "MinFlux": 0.02 }
        The debug visualizer's "Copy params as JSON" writes this format; "wav-analyze tuning" prints every key.

        Generate options (levels are peak dBFS, or "off"):
          --bpm <n> (120)  --seconds <s> (30)  --rate <Hz> (48000)  --channels <n> (1)  --seed <n> (1)
          --format pcm8|pcm16|pcm24|pcm32|float32|float64 (pcm16)  --extensible (WAVE_FORMAT_EXTENSIBLE header)
          --kick-db (-3.1)  --noise-db (-34)  --vocal-db (-16.5)  --bass-db (off)  --hat-db (off)
          --full-mix         Also a bass line (-14 dB) and off-beat hi-hats (-24.4 dB), unless set above
          --gain-db <dB>     Overall gain, e.g. -40 for a quiet copy of the same track (default 0)
          --intro <s>  --outro <s>   Digital silence before and after the music (default 0)

        Exit codes: 0 success, 1 failed (bad file, invalid tuning, selftest failure), 2 bad command line.

        """;

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0)
            {
                error.Write(Usage);
                return 2;
            }
            return args[0] switch
            {
                "--help" or "-h" or "-?" or "/?" or "help" => Help(output),
                "generate" => Generate(args[1..], output),
                "selftest" => SelfTest(args[1..], output),
                "tuning" => PrintTuning(args[1..], output),
                "analyze" => Analyze(args[1..], output),
                _ => Analyze(args, output),
            };
        }
        catch (UsageException e)
        {
            error.WriteLine($"wav-analyze: {e.Message}");
            error.WriteLine("Run \"wav-analyze --help\" for usage.");
            return 2;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            error.WriteLine($"wav-analyze: {e.Message}");
            return 1;
        }
    }

    private static int Help(TextWriter output)
    {
        output.Write(Usage);
        return 0;
    }

    private static int Analyze(string[] args, TextWriter output)
    {
        var a = new CommandLine(args,
            ["out", "csv", "plot", "fps", "jitter", "packet-ms", "seed", "tuning", "range", "width"],
            ["no-plot"]);
        if (a.Positional.Count != 1) throw new UsageException(a.Positional.Count == 0 ? "Expected a WAV file." : "Expected one WAV file.");
        string path = a.Positional[0];
        var feed = new FeedOptions
        {
            Fps = a.Number("fps", 60, 1, 1000),
            Jitter = a.Number("jitter", 0, 0, 0.9),
            PacketSeconds = a.Number("packet-ms", 0, 0, 1000) / 1000,
            Seed = a.Seed("seed", 1),
        };
        (double From, double To)? range = ParseRange(a.Text("range"));
        int? width = a.Text("width") is null ? null : a.Integer("width", 0, PlotRenderer.MinWidth, PlotRenderer.MaxWidth);
        string? tuningPath = a.Text("tuning");
        AudioTuning? tuning = tuningPath is null ? null : TuningJson.Load(tuningPath);

        WavAudio audio = WavReader.ReadMono(path);
        if (audio.Mono.Length == 0) throw new InvalidDataException($"{path} has no audio frames.");
        if (range is { } r && r.From >= audio.Info.Seconds) // the plot would clamp to an empty sliver past the end
            throw new UsageException($"--range starts at {r.From.ToString("0.###", C)} s but {Path.GetFileName(path)} is only {audio.Info.Seconds.ToString("F2", C)} s long.");
        AnalysisResult result = OfflineAnalysis.Run(audio.Mono, audio.Info.SampleRate, feed, tuning);

        string name = Path.GetFileNameWithoutExtension(path);
        string outDir = a.Text("out") ?? ".";
        string csvPath = a.Text("csv") ?? Path.Combine(outDir, name + ".csv");
        string? plotPath = a.Has("no-plot") ? null : a.Text("plot") ?? Path.Combine(outDir, name + ".png");

        output.WriteLine($"{Path.GetFileName(path)}: {audio.Info}, {audio.Info.Seconds.ToString("F2", C)} s");
        output.WriteLine($"Feed: {DescribeFeed(feed)}; tuning: {DescribeTuning(tuning, tuningPath)}");
        output.WriteLine($"Beats: {result.BeatTimes.Count}");
        WriteBeatTimes(output, result.BeatTimes);
        output.WriteLine($"Tempo: {DescribeTempo(result)}");
        output.WriteLine($"Silent: {(result.SilentFraction() * 100).ToString("F1", C)} % of {result.Frames.Count} frames");

        EnsureDirectory(csvPath);
        AnalysisCsv.Write(csvPath, result.Frames);
        output.WriteLine($"CSV:  {csvPath} ({result.Frames.Count} rows)");
        if (plotPath is not null)
        {
            EnsureDirectory(plotPath);
            var plot = new PlotOptions
            {
                Title = $"{Path.GetFileName(path)}: {result.BeatTimes.Count} beats, {DescribeTempo(result)}",
                Subtitle = $"{audio.Info}, {audio.Info.Seconds.ToString("F2", C)} s · {DescribeFeed(feed)} · tuning: {DescribeTuning(tuning, tuningPath)}",
                From = range?.From,
                To = range?.To,
                Width = width,
            };
            (int w, int h) = PlotRenderer.Save(plotPath, result, plot);
            output.WriteLine($"Plot: {plotPath} ({w}x{h} px)");
        }
        return 0;
    }

    private static int Generate(string[] args, TextWriter output)
    {
        var a = new CommandLine(args,
            ["bpm", "seconds", "rate", "channels", "format", "kick-db", "noise-db", "vocal-db", "bass-db", "hat-db", "gain-db", "intro", "outro", "seed"],
            ["extensible", "full-mix"]);
        if (a.Positional.Count != 1) throw new UsageException("Expected one output .wav path.");
        string path = a.Positional[0];
        var defaults = new SyntheticTrackOptions();
        if (a.Has("full-mix")) defaults = defaults.WithFullMix();
        var options = new SyntheticTrackOptions
        {
            Bpm = a.Number("bpm", defaults.Bpm, 30, 400),
            Seconds = a.Number("seconds", defaults.Seconds, 0.1, 48 * 3600),
            SampleRate = a.Integer("rate", defaults.SampleRate, 1000, 768000),
            KickLevel = a.Level("kick-db", defaults.KickLevel),
            NoiseLevel = a.Level("noise-db", defaults.NoiseLevel),
            VocalLevel = a.Level("vocal-db", defaults.VocalLevel),
            BassLevel = a.Level("bass-db", defaults.BassLevel),
            HatLevel = a.Level("hat-db", defaults.HatLevel),
            Gain = a.Level("gain-db", 1),
            IntroSeconds = a.Number("intro", 0, 0, 3600),
            OutroSeconds = a.Number("outro", 0, 0, 3600),
            Seed = a.Seed("seed", defaults.Seed),
        };
        int channels = a.Integer("channels", 1, 1, 32);
        WavSampleFormat format = ParseFormat(a.Text("format") ?? "pcm16");

        float[] mono = SyntheticTrack.Render(options);
        float[] data = channels == 1 ? mono : WavWriter.Interleave(mono, channels);
        EnsureDirectory(path);
        WavWriter.Write(path, data, options.SampleRate, channels, format, a.Has("extensible") || channels > 2);

        double[] kicks = new SyntheticTrack(options).KickTimes();
        float peak = 0;
        foreach (float x in mono) peak = MathF.Max(peak, MathF.Abs(x));
        var info = new WavInfo(options.SampleRate, channels, format, WavFormats.BytesPerSample(format) * 8, a.Has("extensible") || channels > 2, mono.Length);
        output.WriteLine($"Wrote {path}: {info}, {info.Seconds.ToString("F2", C)} s");
        output.WriteLine($"{kicks.Length} kicks at {options.Bpm.ToString("0.##", C)} BPM" +
            (kicks.Length > 0 ? $", the first at {kicks[0].ToString("F3", C)} s" : "") +
            $"; peak {(20 * Math.Log10(Math.Max(peak, 1e-9))).ToString("F1", C)} dBFS" +
            (peak > 1 && !WavFormats.IsFloat(format) ? " (clipped; lower the levels or --gain-db)" : ""));
        return 0;
    }

    private static int PrintTuning(string[] args, TextWriter output)
    {
        var a = new CommandLine(args, ["tuning"], []);
        if (a.Positional.Count != 0) throw new UsageException("tuning takes no file argument; use --tuning <file>.");
        string? path = a.Text("tuning");
        AudioTuning tuning = path is null ? new AudioTuning() : TuningJson.Load(path);
        _ = CoreFactory.CreateAnalyzer(tuning); // validates, so out-of-range values fail here rather than later
        output.WriteLine(TuningJson.Serialize(tuning));
        return 0;
    }

    // Generated tracks with known kick times through the real analyzer: the offline equivalent of doc 03 §5,
    // at the feeds the render loop sees. Prints one line per case.
    private static int SelfTest(string[] args, TextWriter output)
    {
        var a = new CommandLine(args, [], []);
        if (a.Positional.Count != 0) throw new UsageException("selftest takes no arguments.");
        var cases = new (string Name, SyntheticTrackOptions Track, FeedOptions Feed)[]
        {
            ("120 BPM, 48 kHz, 60 fps ±20 %", new SyntheticTrackOptions { Bpm = 120 }, new FeedOptions { Jitter = 0.2 }),
            ("128 BPM full mix, 44.1 kHz, 144 fps, 10 ms packets", new SyntheticTrackOptions { Bpm = 128, SampleRate = 44100 }.WithFullMix(), new FeedOptions { Fps = 144, Jitter = 0.1, PacketSeconds = 0.01 }),
            ("120 BPM full mix at -40 dB, 30 fps", new SyntheticTrackOptions { Bpm = 120, Gain = 0.01 }.WithFullMix(), new FeedOptions { Fps = 30, Jitter = 0.2 }),
            ("174 BPM, 96 kHz, 60 fps, 20 ms packets", new SyntheticTrackOptions { Bpm = 174, SampleRate = 96000 }, new FeedOptions { Jitter = 0.2, PacketSeconds = 0.02 }),
            ("white noise only (no beats expected)", new SyntheticTrackOptions { KickLevel = 0, VocalLevel = 0, NoiseLevel = 0.3 }, new FeedOptions { Jitter = 0.2 }),
        };
        const double warmUp = 2, tolerance = 2;
        bool allPassed = true;
        foreach (var (name, track, feed) in cases)
        {
            var synthetic = new SyntheticTrack(track);
            float[] samples = SyntheticTrack.Render(track);
            AnalysisResult result = OfflineAnalysis.Run(samples, track.SampleRate, feed);
            BeatMatch match = BeatStats.Match(result.BeatTimes, synthetic.KickTimes(), warmUp, result.AudioSeconds);
            double tempo = BeatStats.Tempo(result.BeatTimes, warmUp);
            float analyzerBpm = result.LastAnalyzerBpm().Bpm;
            bool expectBeats = track.KickLevel > 0;
            bool passed = expectBeats
                ? match.HitRate >= 0.95 && match.FalseBeats == 0 && Math.Abs(tempo - track.Bpm) <= tolerance && Math.Abs(analyzerBpm - track.Bpm) <= tolerance
                : result.BeatTimes.Count == 0;
            allPassed &= passed;
            var line = new StringBuilder($"{(passed ? "PASS" : "FAIL")}  {name}: ");
            line.Append(expectBeats
                ? $"{match.Hits}/{match.Kicks} kicks found, {match.FalseBeats} false, tempo {tempo.ToString("F1", C)} (analyzer {analyzerBpm.ToString("F1", C)}), median latency {(match.MedianLatency * 1000).ToString("F0", C)} ms"
                : $"{result.BeatTimes.Count} beats");
            output.WriteLine(line.ToString());
        }
        output.WriteLine(allPassed ? "All self-tests passed." : "Some self-tests FAILED.");
        return allPassed ? 0 : 1;
    }

    private static (double From, double To)? ParseRange(string? text)
    {
        if (text is null) return null;
        string[] parts = text.Split(':');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, C, out double from)
            && double.TryParse(parts[1], NumberStyles.Float, C, out double to)
            && double.IsFinite(from) && double.IsFinite(to) && from >= 0 && to > from)
            return (from, to);
        throw new UsageException($"--range expects <from>:<to> in seconds with from < to, e.g. 30:45, not \"{text}\".");
    }

    private static WavSampleFormat ParseFormat(string text) => text.ToLowerInvariant() switch
    {
        "pcm8" => WavSampleFormat.Pcm8,
        "pcm16" => WavSampleFormat.Pcm16,
        "pcm24" => WavSampleFormat.Pcm24,
        "pcm32" => WavSampleFormat.Pcm32,
        "float32" or "float" => WavSampleFormat.Float32,
        "float64" => WavSampleFormat.Float64,
        _ => throw new UsageException($"--format must be pcm8, pcm16, pcm24, pcm32, float32 or float64, not \"{text}\"."),
    };

    internal static string DescribeFeed(FeedOptions feed)
    {
        var text = new StringBuilder($"{feed.Fps.ToString("0.##", C)} fps");
        if (feed.Jitter > 0) text.Append($" ±{(feed.Jitter * 100).ToString("0.#", C)} % (seed {feed.Seed})");
        text.Append(feed.PacketSeconds > 0 ? $", {(feed.PacketSeconds * 1000).ToString("0.#", C)} ms packets" : ", continuous");
        return text.ToString();
    }

    private static string DescribeTuning(AudioTuning? tuning, string? path)
    {
        if (tuning is null) return "defaults";
        IReadOnlyList<string> overrides = TuningJson.Overrides(tuning);
        string file = Path.GetFileName(path) ?? "";
        return overrides.Count == 0 ? $"{file} (same as defaults)" : $"{file} ({string.Join(", ", overrides)})";
    }

    private static string DescribeTempo(AnalysisResult result)
    {
        double tempo = BeatStats.Tempo(result.BeatTimes);
        if (tempo <= 0) return "tempo unknown";
        (float bpm, double time) = result.LastAnalyzerBpm();
        return $"{tempo.ToString("F1", C)} BPM (from beat intervals)" +
            (bpm > 0 ? $", analyzer {bpm.ToString("F1", C)} BPM at {time.ToString("F1", C)} s" : "");
    }

    private static void WriteBeatTimes(TextWriter output, IReadOnlyList<double> beats)
    {
        const int perLine = 10;
        for (int i = 0; i < beats.Count; i += perLine)
        {
            var line = new StringBuilder(" ");
            for (int j = i; j < Math.Min(beats.Count, i + perLine); j++) line.Append(beats[j].ToString("F3", C).PadLeft(9));
            output.WriteLine(line.ToString());
        }
    }

    private static void EnsureDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }
}
