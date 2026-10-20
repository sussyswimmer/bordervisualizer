using System.Globalization;
using System.Text.Json;
using Rimlight.AudioTools;
using Rimlight.Core;

namespace Rimlight.WavAnalyze;

/// <summary>
/// <c>wav-analyze plot</c>: draws a recording from the debug visualizer's "Record 2 min" (a CSV in the app's logs
/// folder) the way a WAV analysis is drawn, with the same panels, so what the app computed from a real song on a real
/// PC sits next to offline runs. The recording holds numbers only, no audio; its light-state columns are left to
/// scripts (the plot shows Level and Beat, which drive Intensity and Pulse).
/// </summary>
internal static class RecordingPlot
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>Runs <c>plot &lt;recording.csv&gt; [--out dir] [--plot file] [--range a:b] [--width px]</c>.</summary>
    /// <param name="args">The arguments after "plot".</param>
    /// <param name="output">Where the summary goes.</param>
    /// <returns>0 when the plot was written.</returns>
    public static int Run(string[] args, TextWriter output)
    {
        var a = new CommandLine(args, ["out", "plot", "range", "width"], []);
        if (a.Positional.Count != 1) throw new UsageException(a.Positional.Count == 0 ? "Expected a recording CSV." : "Expected one recording CSV.");
        string path = a.Positional[0];
        (double From, double To)? range = WavAnalyzeCli.ParseRange(a.Text("range"));
        int? width = a.Text("width") is null ? null : a.Integer("width", 0, PlotRenderer.MinWidth, PlotRenderer.MaxWidth);

        Recording recording = RecordingCsv.Read(path);
        string file = Path.GetFileName(path);
        if (recording.Rows.Count == 0) throw new InvalidDataException($"{file} has no frames.");
        if (range is { } r && r.From >= recording.Seconds)
            throw new UsageException($"--range starts at {r.From.ToString("0.###", C)} s but {file} is only {recording.Seconds.ToString("F2", C)} s long.");
        AnalysisResult result = recording.ToAnalysis();

        string name = Path.GetFileNameWithoutExtension(path);
        string plotPath = a.Text("plot") ?? Path.Combine(a.Text("out") ?? ".", name + ".png");
        string tempo = DescribeTempo(result);
        output.WriteLine($"{file}: {DescribeRecording(recording)}");
        output.WriteLine($"Beats: {result.BeatTimes.Count}");
        output.WriteLine($"Tempo: {tempo}");
        output.WriteLine($"Silent: {(result.SilentFraction() * 100).ToString("F1", C)} % of {result.Frames.Count} frames");

        string? directory = Path.GetDirectoryName(Path.GetFullPath(plotPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var plot = new PlotOptions
        {
            Title = $"{file}: {result.BeatTimes.Count} beats, {tempo}",
            Subtitle = $"{DescribeRecording(recording)} · {recording.Comment("capture") ?? "capture unknown"} · tuning: {DescribeTuning(recording.Comment("tuning"))}"
                + (recording.Comment("settings") is { } settings ? $" · {settings}" : ""),
            From = range?.From,
            To = range?.To,
            Width = width,
        };
        (int w, int h) = PlotRenderer.Save(plotPath, result, plot);
        output.WriteLine($"Plot: {plotPath} ({w}x{h} px)");
        return 0;
    }

    // e.g. "recorded by Rimlight 0.1.0 at 2026-10-18 15:30:12 local time, 120.00 s, 7200 frames (60.0 a second)".
    private static string DescribeRecording(Recording recording)
    {
        string app = recording.Comment("app") is { } version ? $"recorded by {version}" : "recorded";
        string started = recording.Comment("started") is { } when ? $" at {when}" : "";
        double seconds = recording.Seconds;
        string rate = seconds > 0 ? $" ({(recording.Rows.Count / seconds).ToString("0.0", C)} a second)" : "";
        return $"{app}{started}, {seconds.ToString("F2", C)} s, {recording.Rows.Count} frames{rate}";
    }

    // The tempo from the beat times and the analyzer's last estimate, as the WAV analysis prints it.
    private static string DescribeTempo(AnalysisResult result)
    {
        double tempo = BeatStats.Tempo(result.BeatTimes);
        if (tempo <= 0) return "tempo unknown";
        (float bpm, double time) = result.LastAnalyzerBpm();
        return $"{tempo.ToString("F1", C)} BPM (from beat intervals)"
            + (bpm > 0 ? $", analyzer {bpm.ToString("F1", C)} BPM at {time.ToString("F1", C)} s" : "");
    }

    // The tuning comment is the analyzer's whole tuning as JSON (H-015's format): name only what differs from the defaults.
    private static string DescribeTuning(string? json)
    {
        if (json is null) return "unknown";
        try
        {
            IReadOnlyList<string> overrides = TuningJson.Overrides(TuningJson.Parse(json));
            return overrides.Count == 0 ? "defaults" : string.Join(", ", overrides);
        }
        catch (JsonException)
        {
            return "see the CSV's header";
        }
    }
}
