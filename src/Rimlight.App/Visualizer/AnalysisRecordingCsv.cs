using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Rimlight.Core;

namespace Rimlight.App.Visualizer;

/// <summary>
/// What shapes a recording's numbers besides the music, at its start or its end: the settings, the analyzer's tuning,
/// the capture format and whether the demo played. Taken on the UI thread from the visualizer's newest snapshot. The
/// device's name is left out, as the log leaves it out: people attach these files to public reports.
/// </summary>
/// <param name="Settings">The settings in effect.</param>
/// <param name="Tuning">The analyzer's tuning in effect, the visualizer's live edits included.</param>
/// <param name="Format">The captured device's mix format, e.g. "48000 Hz, 2 ch, 32-bit float"; null while none is captured.</param>
/// <param name="CaptureSampleRate">The capture's sample rate in Hz; 0 while none is captured.</param>
/// <param name="AnalysisSampleRate">The rate the analyzer works at (<see cref="CoreFactory.AnalysisSampleRate"/>).</param>
/// <param name="DroppedSamples">Samples the capture has dropped so far.</param>
/// <param name="Demo">Whether the glow showed the demo's synthetic beat.</param>
internal sealed record RecordingSource(
    Settings Settings,
    AudioTuning Tuning,
    string? Format,
    int CaptureSampleRate,
    int AnalysisSampleRate,
    long DroppedSamples,
    bool Demo)
{
    /// <summary>From a snapshot the overlay thread handed over and the current settings.</summary>
    /// <param name="snapshot">The visualizer's newest snapshot.</param>
    /// <param name="settings">The settings now.</param>
    public static RecordingSource From(AnalyzerSnapshot snapshot, Settings settings) => new(
        settings, snapshot.Tuning, snapshot.Format, snapshot.CaptureSampleRate, snapshot.AnalysisSampleRate,
        snapshot.DroppedSamples, snapshot.Demo);
}

/// <summary>What a recording's file says about it besides its frames.</summary>
/// <param name="AppVersion">The app's version, e.g. "0.1.0".</param>
/// <param name="Started">When the recording started, in local time. It names the file too.</param>
/// <param name="AtStart">What shaped the numbers when it started.</param>
/// <param name="AtEnd">The same when it ended; the header says what changed meanwhile.</param>
/// <param name="End">Why it ended.</param>
internal sealed record RecordingInfo(string AppVersion, DateTime Started, RecordingSource AtStart, RecordingSource AtEnd, RecordingEnd End);

/// <summary>
/// Turns an <see cref="AnalysisRecording"/> into a CSV file in the logs folder, for a bug report or a tuning round:
/// header comments (lines starting with <c>#</c>) that say where the numbers came from, a header row, then one row
/// per overlay frame. Numbers use the invariant culture on every PC. <c>wav-analyze plot</c> plots the file like a WAV
/// analysis, and <c>tools/audio-tools/RecordingCsv.cs</c> reads it back. Runs on the UI thread or the thread pool,
/// never on the overlay thread.
/// </summary>
/// <remarks>
/// <para>
/// Privacy (doc 01): the file holds numbers only, as <see cref="AnalysisRecording"/> does: never audio samples, a
/// spectrum, or anything else the sound could be rebuilt from. Its header says so, and names neither the device nor
/// the track.
/// </para>
/// <para>
/// Scripts must skip the comment lines, and the file says how (<c># reading:</c>): <c>pandas.read_csv(path,
/// comment="#")</c> does; <c>numpy.genfromtxt(..., names=True)</c> needs them dropped first, or it takes the first one
/// for the header row. Their number varies (the <c>…AtEnd</c> lines), so a fixed number of lines to skip doesn't work.
/// </para>
/// </remarks>
internal static class AnalysisRecordingCsv
{
    /// <summary>The header row. <c>tools/audio-tools/RecordingCsv.cs</c> reads these names (a test keeps them equal).</summary>
    public const string Header =
        "time,dt,samples,level,bass,beat,isSilent,beatCount,flux,threshold,bpm,intensity,pulse,spread,visibility,phase,followsMusic";

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The file's name: <c>analysis-YYYYMMDD-HHMMSS.csv</c> at the recording's start.</summary>
    /// <param name="started">When the recording started, local time.</param>
    public static string FileName(DateTime started) => string.Create(C, $"analysis-{started:yyyyMMdd-HHmmss}.csv");

    /// <summary>
    /// The file as Windows names its folder, e.g. <c>%APPDATA%\Rimlight\logs\analysis-20261018-153012.csv</c>, for the
    /// visualizer and the log: a screenshot of it names no account, like <c>WindowFailure.LogFile</c>.
    /// </summary>
    /// <param name="path">A file saved in the logs folder.</param>
    public static string DisplayPath(string path) => $@"%APPDATA%\{AppInfo.Name}\logs\{Path.GetFileName(path)}";

    /// <summary>
    /// Writes a new file in <paramref name="directory"/> (created if needed) named after the start
    /// (<see cref="FileName"/>; <c>-2</c>, <c>-3</c>… when that name is taken), and returns its path. A file that
    /// can't be finished is deleted.
    /// </summary>
    /// <param name="directory">The logs folder.</param>
    /// <param name="info">Where the numbers came from.</param>
    /// <param name="frames">The frames to write.</param>
    /// <returns>The new file's full path.</returns>
    /// <exception cref="IOException">The folder or the file can't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder can't be written.</exception>
    public static string Save(string directory, RecordingInfo info, ReadOnlyMemory<RecordedFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(info);
        Directory.CreateDirectory(directory);
        string name = FileName(info.Started);
        for (int attempt = 1; ; attempt++)
        {
            string path = Path.Combine(directory, attempt == 1 ? name : string.Create(C, $"{Path.GetFileNameWithoutExtension(name)}-{attempt}.csv"));
            FileStream file;
            try
            {
                file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (attempt < 100 && File.Exists(path))
            {
                continue; // a recording that started in the same second
            }
            try
            {
                using var writer = new StreamWriter(file, Utf8);
                Write(writer, info, frames.Span);
            }
            catch
            {
                file.Dispose();
                TryDelete(path);
                throw;
            }
            return path;
        }
    }

    /// <summary>Writes the header comments, the header row and one row per frame.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="info">Where the numbers came from.</param>
    /// <param name="frames">The frames, oldest first.</param>
    public static void Write(TextWriter writer, RecordingInfo info, ReadOnlySpan<RecordedFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(info);
        double seconds = frames.Length > 0 ? frames[^1].Time : 0;
        RecordingSource start = info.AtStart, end = info.AtEnd;

        writer.WriteLine($"# {AppInfo.Name} analysis recording (\"Record 2 min\" in the debug visualizer): "
            + $"what {AppInfo.Name} computed from the music, one row per overlay frame.");
        writer.WriteLine("# privacy: numbers only. No audio samples, no spectrum, nothing the sound could be rebuilt from.");
        writer.WriteLine($"# app: {AppInfo.Name} {info.AppVersion}");
        writer.WriteLine(string.Create(C, $"# started: {info.Started:yyyy-MM-dd HH:mm:ss} local time"));
        writer.WriteLine(string.Create(C, $"# length: {seconds:0.000} s, {frames.Length} frames"));
        writer.WriteLine($"# ended: {Describe(info.End)}");
        writer.WriteLine(string.Create(C, $"# frameRate: {(seconds > 0 ? frames.Length / seconds : 0):0.00} frames a second, measured; "
            + $"fpsCap={start.Settings.FpsCap} (the setting: 0 is the display's refresh, and Reduce on battery caps the overlay at 30)"));
        writer.WriteLine($"# capture: {DescribeCapture(start)}; {DroppedDuring(start, end)}");
        writer.WriteLine($"# settings: {DescribeSettings(start.Settings)}");
        writer.WriteLine($"# tuning: {JsonSerializer.Serialize(start.Tuning)}");
        writer.WriteLine($"# demo: {(start.Demo ? "on, the light played the demo's synthetic beat" : "off")}");
        // What changed during the recording (a device change, a slider in Settings or the visualizer, the demo).
        if (DescribeCapture(end) != DescribeCapture(start)) writer.WriteLine($"# captureAtEnd: {DescribeCapture(end)}");
        if (DescribeSettings(end.Settings) != DescribeSettings(start.Settings)) writer.WriteLine($"# settingsAtEnd: {DescribeSettings(end.Settings)}");
        if (end.Tuning != start.Tuning) writer.WriteLine($"# tuningAtEnd: {JsonSerializer.Serialize(end.Tuning)}");
        if (end.Demo != start.Demo) writer.WriteLine($"# demoAtEnd: {(end.Demo ? "on" : "off")}");
        writer.WriteLine("# reading: the lines starting with # are comments: pandas.read_csv(path, comment=\"#\") skips them; "
            + "numpy.genfromtxt(..., names=True) needs them dropped first, or it takes the first one for the header row.");
        writer.WriteLine("# columns: time and dt in seconds (time is the sum of dt so far, the analyzer's clock); samples: new mono "
            + "samples the analyzer got, at the capture rate (a count); level, bass, beat: 0 to 1; isSilent: 0 or 1; beatCount: "
            + "beats since the analyzer's last reset (a beat fired where it rises); flux and threshold: bass flux and the beat "
            + "threshold, in the analyzer's units; bpm: 0 while unknown; intensity, pulse, spread, visibility, phase: the light "
            + "state the overlay got; followsMusic: 1 when the light engine got this audio (music sync, glow on, not paused, no demo).");
        writer.WriteLine(Header);

        var row = new StringBuilder(160);
        foreach (ref readonly RecordedFrame f in frames)
        {
            row.Clear();
            row.Append(C, $"{f.Time:F6},{f.Dt:F6},{f.Samples},{f.Features.Level:F4},{f.Features.Bass:F4},{f.Features.Beat:F4},");
            row.Append(C, $"{(f.Features.IsSilent ? 1 : 0)},{f.BeatCount},{f.Flux:G6},{f.Threshold:G6},{f.Bpm:F2},");
            row.Append(C, $"{f.Intensity:F4},{f.Pulse:F4},{f.Spread:F4},{f.Visibility:F4},{f.Phase:F5},{(f.FollowsMusic ? 1 : 0)}");
            writer.WriteLine(row);
        }
    }

    private static string Describe(RecordingEnd end) => end switch
    {
        RecordingEnd.TimeLimit => "after 2 minutes",
        RecordingEnd.BufferFull => "when its buffer was full (above 240 frames a second)",
        RecordingEnd.Stopped => "when Stop was clicked",
        RecordingEnd.Dropped => "when the visualizer let go of it",
        RecordingEnd.Closed => "when the visualizer's window closed (Esc, its close button or Rimlight quitting)",
        RecordingEnd.Error => "when the visualizer stopped after an error (the log says which)",
        _ => "while still recording",
    };

    // The format and the two rates; never the device's name.
    private static string DescribeCapture(RecordingSource source) => source.Format is null || source.CaptureSampleRate <= 0
        ? "none (no output device was captured)"
        : string.Create(C, $"{source.Format}, captured at {source.CaptureSampleRate} Hz, analyzed at {source.AnalysisSampleRate} Hz");

    // Within one capture the count only grows; a new capture (a device change) starts it over.
    private static string DroppedDuring(RecordingSource start, RecordingSource end) =>
        DescribeCapture(end) == DescribeCapture(start) && end.DroppedSamples >= start.DroppedSamples
            ? string.Create(C, $"{end.DroppedSamples - start.DroppedSamples} samples dropped during the recording")
            : string.Create(C, $"{end.DroppedSamples} samples dropped by the capture at the end");

    // The settings that shape the light and its timing, named as in settings.json. Colors, monitors and the shortcut
    // don't change what the analyzer or the light engine compute.
    private static string DescribeSettings(Settings s) => string.Create(C,
        $"enabled={Bool(s.Enabled)} animation={s.Animation} whenSilent={s.WhenSilent} movement={s.Movement} "
        + $"brightness={s.Brightness:0.###} "
        + $"glow={s.Glow:0.###} coreThicknessDip={s.CoreThicknessDip:0.###} cornerRadiusDip={s.CornerRadiusDip:0.###} "
        + $"edgeStyle={s.EdgeStyle} primaryRatio={s.PrimaryRatio:0.###} sensitivity={s.Sensitivity:0.###} "
        + $"fpsCap={s.FpsCap} onBattery={s.OnBattery} pauseInFullscreen={Bool(s.PauseInFullscreen)}");

    private static string Bool(bool value) => value ? "true" : "false";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left half-written; the caller reports the first failure.
        }
    }
}
