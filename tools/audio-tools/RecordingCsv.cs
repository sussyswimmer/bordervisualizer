using System.Globalization;
using System.Text.RegularExpressions;
using Rimlight.Core;

namespace Rimlight.AudioTools;

/// <summary>
/// One row of a recording from the debug visualizer's "Record 2 min" (<c>%APPDATA%\Rimlight\logs\analysis-*.csv</c>):
/// what the app computed for one overlay frame. Numbers only; a recording holds no audio.
/// </summary>
/// <param name="Time">Seconds since the recording started: the sum of the time steps so far, this frame's included.</param>
/// <param name="Dt">The frame's time step in seconds.</param>
/// <param name="Samples">
/// New mono samples the analyzer got that frame, at the capture's rate (0 between capture packets); the analyzer
/// decimates a 96 or 192 kHz capture itself.
/// </param>
/// <param name="Features">Level, Bass, Beat and IsSilent, as the analyzer returned them.</param>
/// <param name="BeatCount">The analyzer's beat count after the frame: a beat fired where it rises.</param>
/// <param name="Flux">The frame's bass flux, in the analyzer's flux units.</param>
/// <param name="Threshold">The beat threshold after the frame, in the same units.</param>
/// <param name="Bpm">The analyzer's tempo estimate; 0 while unknown.</param>
/// <param name="Intensity">The light state's Intensity; NaN when the file has no light columns.</param>
/// <param name="Pulse">The light state's Pulse; NaN without light columns.</param>
/// <param name="Spread">The light state's Spread; NaN without light columns.</param>
/// <param name="Visibility">The light state's Visibility; NaN without light columns.</param>
/// <param name="Phase">The light state's Phase; NaN without light columns.</param>
/// <param name="FollowsMusic">Whether the light engine got this audio (music sync, glow on, not paused, no demo).</param>
public readonly record struct RecordingRow(
    double Time,
    float Dt,
    int Samples,
    AudioFeatures Features,
    int BeatCount,
    float Flux,
    float Threshold,
    float Bpm,
    float Intensity,
    float Pulse,
    float Spread,
    float Visibility,
    float Phase,
    bool FollowsMusic);

/// <summary>A recording read back by <see cref="RecordingCsv"/>: its header comments and its rows.</summary>
/// <param name="Comments">Each <c># key: value</c> line in order; a comment without a key has the key "".</param>
/// <param name="Rows">One row per overlay frame, oldest first.</param>
/// <param name="HasLight">Whether the file has the light state's columns (intensity, pulse, spread, visibility, phase).</param>
public sealed record Recording(IReadOnlyList<KeyValuePair<string, string>> Comments, IReadOnlyList<RecordingRow> Rows, bool HasLight)
{
    /// <summary>The recorded time: the last row's <see cref="RecordingRow.Time"/>, or 0 without rows.</summary>
    public double Seconds => Rows.Count > 0 ? Rows[^1].Time : 0;

    /// <summary>The first comment with this key (as the app writes them: app, started, length, capture, settings, tuning…).</summary>
    /// <param name="key">The key, matched ignoring case.</param>
    /// <returns>Its value, or null when there is none.</returns>
    public string? Comment(string key)
    {
        foreach (KeyValuePair<string, string> comment in Comments)
            if (string.Equals(comment.Key, key, StringComparison.OrdinalIgnoreCase)) return comment.Value;
        return null;
    }

    /// <summary>
    /// The rows as an offline run, for the plot and the beat statistics: each beat at the time of the row where the
    /// beat count rose (a fall is the analyzer starting over, and counts the beats since). The sample rate is the one
    /// the samples column counts in, as for a WAV file's run: the capture rate the capture comment names (not the
    /// analysis rate after it, which is half or a quarter of a 96 or 192 kHz capture's), or captureAtEnd's when nothing
    /// was captured at the start, or 0. A captureAtEnd comment with another rate means the counts change rate partway.
    /// </summary>
    /// <returns>The frames and beat times.</returns>
    public AnalysisResult ToAnalysis()
    {
        var frames = new List<AnalysisFrame>(Rows.Count);
        var beats = new List<double>();
        for (int i = 0; i < Rows.Count; i++)
        {
            RecordingRow row = Rows[i];
            int previous = i > 0 ? Rows[i - 1].BeatCount : row.BeatCount; // the first row can't tell
            int fired = row.BeatCount >= previous ? row.BeatCount - previous : Math.Max(0, row.BeatCount);
            frames.Add(new AnalysisFrame(row.Time, row.Samples, row.Features, row.Flux, row.Threshold, row.Bpm, fired));
            for (int b = 0; b < fired; b++) beats.Add(row.Time);
        }
        int sampleRate = CaptureRate(Comment("capture")) ?? CaptureRate(Comment("captureAtEnd")) ?? 0;
        return new AnalysisResult(frames, beats, sampleRate, Seconds);
    }

    // "48000 Hz, 2 ch, 32-bit float, captured at 48000 Hz, analyzed at 48000 Hz; …" → 48000; null for "none (…)".
    private static int? CaptureRate(string? capture)
    {
        Match rate = Regex.Match(capture ?? "", @"captured at (\d+) Hz");
        return rate.Success && int.TryParse(rate.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hz) && hz > 0 ? hz : null;
    }
}

/// <summary>
/// Reads the CSV the debug visualizer's "Record 2 min" saves (the app's <c>AnalysisRecordingCsv</c>): header comments
/// (<c># key: value</c>), a header row, then one row per overlay frame, with invariant-culture numbers. Columns are
/// found by name, in any order and case, and columns this reader doesn't know are skipped, so a later app can add
/// some. <see cref="RequiredColumns"/> must be there; the others take a default when missing.
/// </summary>
public static class RecordingCsv
{
    /// <summary>The header row the app writes (a test keeps it equal to the app's).</summary>
    public const string Header =
        "time,dt,samples,level,bass,beat,isSilent,beatCount,flux,threshold,bpm,intensity,pulse,spread,visibility,phase,followsMusic";

    /// <summary>
    /// The columns a file must have: what the plot draws and where the beats are. A CSV that wav-analyze writes has no
    /// beatCount, so it is refused with a message that says so.
    /// </summary>
    public static IReadOnlyList<string> RequiredColumns { get; } = ["time", "level", "bass", "beat", "isSilent", "beatCount", "flux", "threshold"];

    private static readonly string[] LightColumns = ["intensity", "pulse", "spread", "visibility", "phase"];
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>Reads a recording from a file.</summary>
    /// <param name="path">The CSV file.</param>
    /// <returns>Its comments and rows.</returns>
    /// <exception cref="IOException">The file can't be read.</exception>
    /// <exception cref="InvalidDataException">It isn't a recording: no header row, a required column missing, or a
    /// value that isn't a number. The message names the file, the line and the column.</exception>
    public static Recording Read(string path)
    {
        using var reader = new StreamReader(path);
        return Read(reader, Path.GetFileName(path));
    }

    /// <summary>Reads a recording.</summary>
    /// <param name="reader">The CSV text.</param>
    /// <param name="name">What error messages call it, e.g. the file's name.</param>
    /// <returns>Its comments and rows.</returns>
    /// <exception cref="InvalidDataException">It isn't a recording (see <see cref="Read(string)"/>).</exception>
    public static Recording Read(TextReader reader, string name = "the CSV")
    {
        ArgumentNullException.ThrowIfNull(reader);
        var comments = new List<KeyValuePair<string, string>>();
        var rows = new List<RecordingRow>();
        Dictionary<string, int>? columns = null;
        int width = 0, number = 0;
        double lastTime = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            number++;
            if (line.StartsWith('#'))
            {
                comments.Add(ParseComment(line));
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] cells = line.Split(',');
            if (columns is null)
            {
                columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < cells.Length; i++) columns.TryAdd(cells[i].Trim(), i);
                foreach (string required in RequiredColumns)
                    if (!columns.ContainsKey(required))
                        throw new InvalidDataException(required == "beatCount" && !columns.ContainsKey("dt")
                            ? $"{name} has no beatCount column, so it isn't a recording from the debug visualizer's \"Record 2 min\" (a CSV wav-analyze wrote has none: run wav-analyze on the WAV itself to plot it)."
                            : $"{name} has no {required} column (line {number}: \"{Shorten(line)}\").");
                width = cells.Length;
                continue;
            }
            if (cells.Length < width)
                throw new InvalidDataException($"{name} line {number}: expected {width} values, found {cells.Length}.");

            var row = new Cells(cells, columns, name, number);
            double time = row.Double("time");
            rows.Add(new RecordingRow(
                time,
                row.Single("dt", (float)(time - lastTime)),
                row.Integer("samples", 0),
                new AudioFeatures(row.Single("level"), row.Single("bass"), row.Single("beat"), row.Flag("isSilent")),
                row.Integer("beatCount"),
                row.Single("flux"),
                row.Single("threshold"),
                row.Single("bpm", 0),
                row.Single("intensity", float.NaN),
                row.Single("pulse", float.NaN),
                row.Single("spread", float.NaN),
                row.Single("visibility", float.NaN),
                row.Single("phase", float.NaN),
                row.Flag("followsMusic", true)));
            lastTime = time;
        }
        if (columns is null) throw new InvalidDataException($"{name} has no header row: expected \"{Header}\".");
        bool hasLight = LightColumns.All(columns.ContainsKey);
        return new Recording(comments, rows, hasLight);
    }

    // "# key: value" → (key, value); a comment without "key: " keeps its whole text with the key "".
    private static KeyValuePair<string, string> ParseComment(string line)
    {
        string text = line[1..].Trim();
        int colon = text.IndexOf(": ", StringComparison.Ordinal);
        bool keyed = colon > 0 && !text.AsSpan(0, colon).ContainsAny(' ', '"', '(');
        return keyed ? new(text[..colon], text[(colon + 2)..]) : new("", text);
    }

    private static string Shorten(string line) => line.Length <= 80 ? line : line[..77] + "...";

    // One data row, read by column name.
    private readonly struct Cells(string[] cells, Dictionary<string, int> columns, string name, int number)
    {
        public double Double(string column) =>
            double.TryParse(Text(column), NumberStyles.Float, C, out double value) ? value : throw Bad(column, "a number");

        public float Single(string column, float? fallback = null)
        {
            if (!columns.ContainsKey(column) && fallback is { } missing) return missing;
            return float.TryParse(Text(column), NumberStyles.Float, C, out float value) ? value : throw Bad(column, "a number");
        }

        public int Integer(string column, int? fallback = null)
        {
            if (!columns.ContainsKey(column) && fallback is { } missing) return missing;
            return int.TryParse(Text(column), NumberStyles.Integer, C, out int value) ? value : throw Bad(column, "a whole number");
        }

        public bool Flag(string column, bool? fallback = null)
        {
            if (!columns.ContainsKey(column) && fallback is { } missing) return missing;
            return Text(column) switch
            {
                "1" or "true" or "True" => true,
                "0" or "false" or "False" => false,
                _ => throw Bad(column, "0 or 1"),
            };
        }

        private string Text(string column) => cells[columns[column]].Trim();

        private InvalidDataException Bad(string column, string expected) =>
            new($"{name} line {number}: {column} should be {expected}, not \"{Shorten(Text(column))}\".");
    }
}
