using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Rimlight.AudioTools;
using Rimlight.Core;
using Rimlight.WavAnalyze;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Tools;

// Maxwell's feedback, round 2: the debug visualizer's "Record 2 min" saves what the app computed for every frame on his
// PC (numbers only, no audio) as a CSV; tools/audio-tools/RecordingCsv reads it back and "wav-analyze plot" draws it
// with the panels of a WAV analysis. The app side (the recorder and the writer) is tested in Rimlight.Platform.Tests,
// with a round trip through this reader. Here the files are written by hand, in the app's format.
public sealed class RecordingPlotTests(ITestOutputHelper output) : IDisposable
{
    private const string Comments = """
        # Rimlight analysis recording ("Record 2 min" in the debug visualizer): what Rimlight computed from the music, one row per overlay frame.
        # privacy: numbers only. No audio samples, no spectrum, nothing the sound could be rebuilt from.
        # app: Rimlight 0.1.0
        # started: 2026-10-18 15:30:12 local time
        # capture: 48000 Hz, 2 ch, 32-bit float, captured at 48000 Hz, analyzed at 48000 Hz; 0 samples dropped during the recording
        # settings: enabled=true animation=MusicSync whenSilent=IdleGlow brightness=0.8 glow=0.45 coreThicknessDip=6 cornerRadiusDip=0 primaryRatio=0.6 sensitivity=1 fpsCap=60 onBattery=Reduce pauseInFullscreen=true
        # tuning: {"BeatDecaySeconds":0.2,"Sensitivity":1}
        """;

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private readonly string directory = Directory.CreateTempSubdirectory("rimlight-plot-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void PlotDrawsARecordingWithTheBeatsWhereTheCountRose()
    {
        // 20 s of a 120 BPM full mix through the real analyzer at 60 fps with 10 ms packets, written as the app writes a
        // recording: the beat count carries on from a running analyzer (here 40), as it does on Maxwell's PC.
        var track = new SyntheticTrackOptions { Bpm = 120, Seconds = 20 }.WithFullMix();
        AnalysisResult run = OfflineAnalysis.Run(SyntheticTrack.Render(track), track.SampleRate, new FeedOptions { PacketSeconds = 0.01 });
        string csv = Path.Combine(directory, "analysis-20261018-153012.csv");
        File.WriteAllText(csv, RecordingText(run.Frames, firstBeatCount: 40));
        int expected = run.Frames.Skip(1).Sum(f => f.BeatsFired); // the first row has nothing to rise from

        (int code, string text, string error) = Cli("plot", csv, "--out", directory);
        output.WriteLine(text);

        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("analysis-20261018-153012.csv: recorded by Rimlight 0.1.0 at 2026-10-18 15:30:12 local time, 20.00 s, 1200 frames (60.0 a second)", text);
        Assert.Contains($"Beats: {expected}", text);
        Match tempo = Regex.Match(text, @"Tempo: (\d+\.\d) BPM \(from beat intervals\), analyzer (\d+\.\d) BPM");
        Assert.True(tempo.Success, text);
        Assert.InRange(double.Parse(tempo.Groups[1].Value, C), 118, 122);
        Assert.Contains("Silent: 0.0 % of 1200 frames", text);
        (int width, int height) = PngSize(Path.Combine(directory, "analysis-20261018-153012.png"));
        Assert.Equal(1600 + 76 + 28, width); // 80 px per second, like a WAV analysis
        Assert.InRange(height, 700, 1000);

        // --plot and --range, as for a WAV file.
        string zoomed = Path.Combine(directory, "zoom", "five.png");
        Assert.Equal(0, Cli("plot", csv, "--plot", zoomed, "--range", "5:10", "--width", "900").Code);
        Assert.Equal(900, PngSize(zoomed).Width);

        // The reader's view: the same beats, at the rows where the count rose, and the comments by key.
        Recording recording = RecordingCsv.Read(csv);
        AnalysisResult back = recording.ToAnalysis();
        Assert.Equal(expected, back.BeatTimes.Count);
        Assert.Equal(run.BeatTimes.Where(t => t > run.Frames[0].Time), back.BeatTimes.Select(t => Math.Round(t, 6)), new ToleranceComparer(1e-6));
        Assert.Equal(48000, back.SampleRate);
        Assert.Equal("Rimlight 0.1.0", recording.Comment("APP"));
        Assert.Null(recording.Comment("missing"));
        Assert.Equal("", recording.Comments[0].Key); // the title line has no key
        Assert.True(recording.HasLight);
    }

    [Fact]
    public void TheReaderFindsColumnsByNameAndFillsInWhatIsMissing()
    {
        // Columns in another order and case, a column this reader doesn't know, no dt, samples, bpm, light or
        // followsMusic, Windows line ends and a blank line.
        string text = "# app: Rimlight 0.2.0\r\nBEAT,time,extra,level,bass,isSilent,beatCount,flux,threshold\r\n"
            + "0.5,0.02,x,0.1,0.2,0,3,0.01,0.02\r\n\r\n1,0.05,y,0.3,0.4,1,4,1e-3,NaN\r\n";

        Recording recording = RecordingCsv.Read(new StringReader(text), "hand.csv");

        Assert.False(recording.HasLight);
        Assert.Equal(2, recording.Rows.Count);
        RecordingRow first = recording.Rows[0], second = recording.Rows[1];
        Assert.Equal(new AudioFeatures(0.1f, 0.2f, 0.5f, false), first.Features);
        Assert.Equal(new AudioFeatures(0.3f, 0.4f, 1, true), second.Features);
        Assert.Equal(0.02f, first.Dt, 6);  // from the times
        Assert.Equal(0.03f, second.Dt, 6);
        Assert.Equal((0, 0f, true), (first.Samples, first.Bpm, first.FollowsMusic));
        Assert.True(float.IsNaN(first.Intensity) && float.IsNaN(first.Phase));
        Assert.True(float.IsNaN(second.Threshold));
        Assert.Equal(0.001f, second.Flux);
        Assert.Equal(0.05, recording.Seconds, 9);
        Assert.Equal([0.05], recording.ToAnalysis().BeatTimes);
        Assert.Equal("Rimlight 0.2.0", recording.Comment("app"));

        // A fall of the count is the analyzer starting over (a device change): the beats since then count.
        Recording reset = RecordingCsv.Read(new StringReader(
            "time,level,bass,beat,isSilent,beatCount,flux,threshold\n0.1,0,0,0,0,9,0,0\n0.2,0,0,0,0,0,0,0\n0.3,0,0,0,0,1,0,0\n"));
        Assert.Equal([0.3], reset.ToAnalysis().BeatTimes);
    }

    [Fact]
    public void TheSampleRateIsTheOneTheSamplesCountIn()
    {
        // The samples column counts what the analyzer was handed, at the capture's rate; it decimates a 96 kHz capture to
        // 48 kHz inside. A run's sample rate is the rate of its counts, as for a WAV file, so 10 ms packets add up to the
        // recording's time at 96 kHz, not at 48.
        const string Rows = "time,dt,samples,level,bass,beat,isSilent,beatCount,flux,threshold\n"
            + "0.01,0.01,960,0,0,0,0,0,0,0\n0.02,0.01,960,0,0,0,0,0,0,0\n";
        AnalysisResult run = RecordingCsv.Read(new StringReader(
            "# capture: 96000 Hz, 2 ch, 32-bit float, captured at 96000 Hz, analyzed at 48000 Hz; 0 samples dropped during the recording\n" + Rows)).ToAnalysis();
        Assert.Equal(96000, run.SampleRate);
        Assert.Equal(run.AudioSeconds, run.Frames.Sum(f => f.Samples) / (double)run.SampleRate, 9);

        // Nothing captured when it started (the capture was still opening): the device captured at the end counted them.
        run = RecordingCsv.Read(new StringReader("# capture: none (no output device was captured); 0 samples dropped by the capture at the end\n"
            + "# captureAtEnd: 44100 Hz, 2 ch, 16-bit PCM, captured at 44100 Hz, analyzed at 44100 Hz\n" + Rows)).ToAnalysis();
        Assert.Equal(44100, run.SampleRate);

        Assert.Equal(0, RecordingCsv.Read(new StringReader(Rows)).ToAnalysis().SampleRate); // no comment: unknown
    }

    [Fact]
    public void ABadFileFailsWithAMessageThatSaysWhere()
    {
        string bad = Path.Combine(directory, "bad.csv");

        File.WriteAllText(bad, "# only comments\n");
        AssertFails(bad, "bad.csv has no header row");

        File.WriteAllText(bad, "time,level,bass,beat,isSilent,flux,threshold,bpm\n0.1,0,0,0,0,0,0,0\n"); // what wav-analyze writes
        AssertFails(bad, "bad.csv has no beatCount column, so it isn't a recording");

        File.WriteAllText(bad, "time,dt,level,bass,beat,isSilent,beatCount,flux\n");
        AssertFails(bad, "bad.csv has no threshold column");

        File.WriteAllText(bad, RecordingCsv.Header + "\n0.1,0.1,800,0.5,0.5,0.5,0,1,0.1,0.2,120,0.6,0.5,0.45,1,0.25\n");
        AssertFails(bad, "bad.csv line 2: expected 17 values, found 16.");

        File.WriteAllText(bad, RecordingCsv.Header + "\n0.1,0.1,800,0,5,0.5,0.5,0,1,0.1,0.2,120,0.6,0.5,0.45,1,0.25\n"); // a decimal comma
        AssertFails(bad, "bad.csv line 2: isSilent should be 0 or 1, not \"0.5\".");

        File.WriteAllText(bad, RecordingCsv.Header + "\n0.1,0.1,800,0.5,0.5,0.5,0,1.5,0.1,0.2,120,0.6,0.5,0.45,1,0.25,1\n");
        AssertFails(bad, "bad.csv line 2: beatCount should be a whole number, not \"1.5\".");

        File.WriteAllText(bad, RecordingCsv.Header + "\n");
        AssertFails(bad, "bad.csv has no frames.");

        (int code, _, string error) = Cli("plot", Path.Combine(directory, "missing.csv"));
        Assert.Equal(1, code);
        Assert.Contains("missing.csv", error);

        string good = Path.Combine(directory, "good.csv");
        File.WriteAllText(good, RecordingCsv.Header + "\n0.5,0.5,800,0.5,0.5,0.5,0,1,0.1,0.2,120,0.6,0.5,0.45,1,0.25,1\n1,0.5,800,0.5,0.5,0.5,0,1,0.1,0.2,120,0.6,0.5,0.45,1,0.25,1\n");
        (code, _, error) = Cli("plot", good, "--range", "5:10", "--out", directory);
        Assert.Equal(2, code);
        Assert.Contains("--range starts at 5 s but good.csv is only 1.00 s long.", error);
        Assert.Equal(2, Cli("plot").Code);
        Assert.Equal(2, Cli("plot", good, good).Code);
        Assert.Equal(2, Cli("plot", good, "--fps", "30").Code); // a WAV option, not a plot option
        Assert.False(File.Exists(Path.Combine(directory, "good.png")));
        Assert.Equal(0, Cli("plot", good, "--out", directory).Code);
        Assert.True(File.Exists(Path.Combine(directory, "good.png")));
    }

    [Fact]
    public void TheHelpNamesThePlotCommand()
    {
        (int code, string text, _) = Cli("--help");
        Assert.Equal(0, code);
        Assert.Contains("wav-analyze plot <recording.csv> [options]", text);
        Assert.Contains(@"%APPDATA%\Rimlight\logs\analysis-*.csv", text);
        Assert.Contains("pandas.read_csv(path, comment=\"#\")", text);
        // Its description lines up with the other commands' (column 44), on the line below its longer command.
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        int column = lines.Single(l => l.StartsWith("  wav-analyze selftest ", StringComparison.Ordinal)).IndexOf("Analyze", StringComparison.Ordinal);
        string plot = lines[Array.FindIndex(lines, l => l.StartsWith("  wav-analyze plot ", StringComparison.Ordinal)) + 1];
        Assert.Equal(44, column);
        Assert.Equal(column, plot.Length - plot.TrimStart().Length);
        Assert.StartsWith("Plot a recording", plot.TrimStart(), StringComparison.Ordinal);
    }

    // A recording in the app's format (AnalysisRecordingCsv): the header comments, the header row, then one row per
    // frame with the beat count rising by each frame's beats. The light columns are made up; the plot doesn't draw them.
    private static string RecordingText(IReadOnlyList<AnalysisFrame> frames, int firstBeatCount)
    {
        var text = new StringBuilder(Comments.Replace("\r\n", "\n", StringComparison.Ordinal)).Append('\n');
        double seconds = frames[^1].Time - frames[0].Time + (frames.Count > 1 ? frames[1].Time - frames[0].Time : 0);
        text.Append(C, $"# length: {seconds:0.000} s, {frames.Count} frames\n").Append(RecordingCsv.Header).Append('\n');
        double start = frames[0].Time - (frames.Count > 1 ? frames[1].Time - frames[0].Time : 0); // time counts from the recording's start
        int beats = firstBeatCount;
        double last = start;
        for (int i = 0; i < frames.Count; i++)
        {
            AnalysisFrame f = frames[i];
            if (i > 0) beats += f.BeatsFired;
            AudioFeatures a = f.Features;
            text.Append(C, $"{f.Time - start:F6},{f.Time - last:F6},{f.Samples},{a.Level:F4},{a.Bass:F4},{a.Beat:F4},{(a.IsSilent ? 1 : 0)},{beats},");
            text.Append(C, $"{f.Flux:G6},{f.Threshold:G6},{f.Bpm:F2},{0.8f * (0.35f + 0.65f * a.Level):F4},{a.Beat:F4},0.4500,1.0000,{i * 0.00025 % 1:F5},1\n");
            last = f.Time;
        }
        return text.ToString();
    }

    private void AssertFails(string path, string message)
    {
        (int code, _, string error) = Cli("plot", path, "--out", directory);
        Assert.Equal(1, code);
        Assert.Contains(message, error);
    }

    private static (int Width, int Height) PngSize(string path)
    {
        byte[] png = File.ReadAllBytes(path);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }

    private static (int Code, string Output, string Error) Cli(params string[] args)
    {
        var stdout = new StringWriter(C);
        var stderr = new StringWriter(C);
        int code = WavAnalyzeCli.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    // Beat times written with six decimals and read back.
    private sealed class ToleranceComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;

        public int GetHashCode(double obj) => 0;
    }
}
