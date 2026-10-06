using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rimlight.AudioTools;
using Rimlight.Core;
using Rimlight.WavAnalyze;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Tools;

// C3: tools/wav-analyze end to end (generate → WAV file → analyze → CSV and PNG), the offline feed, tuning JSON and
// beat statistics. Signals are procedural; no recorded music.
public sealed class WavAnalyzeTests(ITestOutputHelper output) : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("rimlight-c3-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void GeneratedOneTwentyBpmTrackIsFoundThroughAWavFile()
    {
        // Generate → 16-bit stereo WAV → read back → analyzer at 60 fps with ±20 % frame jitter.
        var options = new SyntheticTrackOptions { Bpm = 120, Seconds = 20 };
        string path = Path.Combine(directory, "track.wav");
        WavWriter.Write(path, WavWriter.Interleave(SyntheticTrack.Render(options), 2), options.SampleRate, 2, WavSampleFormat.Pcm16);
        WavAudio audio = WavReader.ReadMono(path);

        AnalysisResult result = OfflineAnalysis.Run(audio.Mono, audio.Info.SampleRate, new FeedOptions { Jitter = 0.2 });
        double[] kicks = new SyntheticTrack(options).KickTimes();
        BeatMatch match = BeatStats.Match(result.BeatTimes, kicks, fromSeconds: 2, toSeconds: result.AudioSeconds);
        double tempo = BeatStats.Tempo(result.BeatTimes, fromSeconds: 2);
        output.WriteLine($"{result.BeatTimes.Count} beats, {match.Hits}/{match.Kicks} kicks after 2 s, {match.FalseBeats} false, tempo {tempo:F2}, analyzer {result.LastAnalyzerBpm().Bpm:F2}, latency {match.MedianLatency * 1000:F1} ms");

        Assert.InRange(tempo, 118, 122);
        Assert.InRange(result.LastAnalyzerBpm().Bpm, 118, 122);
        Assert.Equal(36, match.Kicks);                       // kicks at 2.0 … 19.5 s
        Assert.Equal(match.Kicks, match.Hits);
        Assert.Equal(0, match.FalseBeats);
        Assert.InRange(result.BeatTimes.Count, 39, 40);      // the kick at 0 s may fall in the 0.25 s warm-up
        Assert.InRange(match.MedianLatency, 0, 0.05);
        Assert.InRange(result.Frames.Count, 20 * 60 - 30, 20 * 60 + 30); // jittered frames: 20 s at 60 fps on average
    }

    [Fact]
    public void CommandLineWritesCsvAndPngAndPrintsTheTempo()
    {
        string wav = Path.Combine(directory, "beat120.wav");
        (int generated, string generateText, _) = Cli("generate", wav, "--bpm", "120", "--seconds", "20", "--channels", "2", "--format", "pcm24");
        Assert.Equal(0, generated);
        Assert.Contains("40 kicks at 120 BPM", generateText);

        string outDir = Path.Combine(directory, "out");
        (int code, string text, string error) = Cli(wav, "--out", outDir, "--jitter", "0.2", "--packet-ms", "10");
        output.WriteLine(text);
        Assert.Equal(0, code);
        Assert.Equal("", error);

        Match tempo = Regex.Match(text, @"Tempo: (\d+\.\d) BPM \(from beat intervals\), analyzer (\d+\.\d) BPM");
        Assert.True(tempo.Success, text);
        Assert.InRange(double.Parse(tempo.Groups[1].Value, CultureInfo.InvariantCulture), 118, 122);
        Assert.InRange(double.Parse(tempo.Groups[2].Value, CultureInfo.InvariantCulture), 118, 122);
        int beats = int.Parse(Regex.Match(text, @"Beats: (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.InRange(beats, 39, 40);
        Assert.Contains("Feed: 60 fps ±20 % (seed 1), 10 ms packets; tuning: defaults", text);

        string[] rows = File.ReadAllLines(Path.Combine(outDir, "beat120.csv"));
        Assert.Equal("time,level,bass,beat,isSilent,flux,threshold,bpm", rows[0]);
        Assert.InRange(rows.Length - 1, 1190, 1210);
        string[][] cells = rows[1..].Select(r => r.Split(',')).ToArray();
        Assert.All(cells, c => Assert.Equal(8, c.Length));
        Assert.Equal(beats, cells.Count(c => c[3] == "1.0000"));      // the Beat pulse is exactly 1 on a beat frame
        Assert.All(cells, c => Assert.Equal("0", c[4]));               // music throughout: never silent
        Assert.InRange(double.Parse(cells[^1][7], CultureInfo.InvariantCulture), 118, 122);

        byte[] png = File.ReadAllBytes(Path.Combine(outDir, "beat120.png"));
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        Assert.Equal(1600 + 76 + 28, width);                         // 80 px per second
        Assert.InRange(height, 700, 1000);
    }

    [Fact]
    public void TuningOverridesAreAppliedAndReported()
    {
        string wav = Path.Combine(directory, "t.wav");
        Assert.Equal(0, Cli("generate", wav, "--seconds", "8", "--full-mix").Code);
        string json = Path.Combine(directory, "tuning.json");
        File.WriteAllText(json, """
            // pasted from the debug visualizer
            { "sensitivity": 2, "BeatRefractorySeconds": 0.4, }
            """);

        (int code, string text, _) = Cli(wav, "--out", directory, "--no-plot", "--tuning", json);

        Assert.Equal(0, code);
        Assert.Contains("tuning: tuning.json (BeatRefractorySeconds=0.4, Sensitivity=2)", text);
        Assert.False(File.Exists(Path.Combine(directory, "t.png")));
    }

    [Fact]
    public void BadInputFailsWithAMessage()
    {
        string wav = Path.Combine(directory, "x.wav");
        Assert.Equal(0, Cli("generate", wav, "--seconds", "1").Code);
        string json = Path.Combine(directory, "bad.json");

        File.WriteAllText(json, """{ "Sensitivty": 1.5 }""");
        (int code, _, string error) = Cli(wav, "--tuning", json, "--out", directory);
        Assert.Equal(1, code);
        Assert.Contains("Sensitivty", error);

        File.WriteAllText(json, """{ "WindowSize": 1000 }""");
        (code, _, error) = Cli(wav, "--tuning", json, "--out", directory);
        Assert.Equal(1, code);
        Assert.Contains("WindowSize", error);

        (code, _, error) = Cli(Path.Combine(directory, "missing.wav"));
        Assert.Equal(1, code);
        Assert.Contains("missing.wav", error);

        File.WriteAllText(Path.Combine(directory, "text.wav"), "not audio");
        Assert.Equal(1, Cli(Path.Combine(directory, "text.wav")).Code);

        Assert.Equal(2, Cli(wav, "--fsp", "30").Code);
        Assert.Equal(2, Cli(wav, "--fps", "0").Code);
        Assert.Equal(2, Cli(wav, "--range", "5:2").Code);
        (code, _, error) = Cli(wav, "--range", "30:45", "--out", directory); // x.wav is 1 s long
        Assert.Equal(2, code);
        Assert.Contains("--range starts at 30 s but x.wav is only 1.00 s long.", error);
        Assert.False(File.Exists(Path.Combine(directory, "x.png")));
        Assert.Equal(2, Cli().Code);
        Assert.Equal(0, Cli("--help").Code);
    }

    [Fact]
    public void TuningCommandPrintsJsonThatReadsBack()
    {
        (int code, string text, _) = Cli("tuning");
        Assert.Equal(0, code);
        Assert.Equal(new AudioTuning(), TuningJson.Parse(text));
        Assert.Contains("\"FluxThresholdMultiplier\": 1.5", text);
    }

    [Fact]
    public void SelfTestPasses()
    {
        (int code, string text, _) = Cli("selftest");
        output.WriteLine(text);
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", text);
    }

    [Fact]
    public void TuningJsonAcceptsAnySubsetAndRejectsTypos()
    {
        AudioTuning tuning = TuningJson.Parse("""{ "MinFlux": 0.02, "silenceholdms": 1500 }""");
        Assert.Equal(new AudioTuning { MinFlux = 0.02f, SilenceHoldMs = 1500 }, tuning);
        Assert.Equal(["MinFlux=0.02", "SilenceHoldMs=1500"], TuningJson.Overrides(tuning));
        Assert.Equal(tuning, TuningJson.Parse(TuningJson.Serialize(tuning)));
        Assert.Equal(new AudioTuning(), TuningJson.Parse("{}"));
        Assert.Throws<JsonException>(() => TuningJson.Parse("""{ "MinFlux": 0.02, "Bogus": 1 }"""));
        Assert.Throws<JsonException>(() => TuningJson.Parse("null"));
        Assert.Throws<JsonException>(() => TuningJson.Parse("""{ "WindowSize": "big" }"""));
    }

    [Fact]
    public void FeedIsDeterministicAndPacketsLeaveEmptyFrames()
    {
        float[] samples = SyntheticTrack.Render(new SyntheticTrackOptions { Seconds = 3 });
        var feed = new FeedOptions { Fps = 144, Jitter = 0.3, PacketSeconds = 0.02, Seed = 5 };
        AnalysisResult a = OfflineAnalysis.Run(samples, 48000, feed);
        AnalysisResult b = OfflineAnalysis.Run(samples, 48000, feed);
        Assert.Equal(a.Frames, b.Frames);
        Assert.NotEqual(a.Frames, OfflineAnalysis.Run(samples, 48000, feed with { Seed = 6 }).Frames);

        Assert.Contains(a.Frames, f => f.Samples == 0);
        Assert.All(a.Frames, f => Assert.True(f.Samples % 960 == 0, $"{f.Samples} is not whole 20 ms packets"));
        Assert.InRange(a.Frames.Sum(f => f.Samples), samples.Length - 960, samples.Length);
        double[] durations = a.Frames.Zip(a.Frames.Skip(1), (x, y) => y.Time - x.Time).ToArray();
        Assert.InRange(durations.Min(), 0.7 / 144 - 1e-9, 1.0 / 144);
        Assert.InRange(durations.Max(), 1.0 / 144, 1.3 / 144 + 1e-9);
    }

    [Fact]
    public void BeatStatsMatchKicksToBeats()
    {
        double[] kicks = [0, 0.5, 1, 1.5, 2, 2.5, 3];
        double[] beats = [0.02, 0.51, 0.75, 1.03, 2.04, 2.52, 3.2];
        BeatMatch match = BeatStats.Match(beats, kicks, fromSeconds: 0.4, toSeconds: 3.5);
        // Kicks 0.5…3 count (6); 1.5 is missed, 3 is detected too late; 0.75 and 3.2 are false; 0.02 is out of range.
        Assert.Equal(new BeatMatch(6, 4, 2, 0.025, 0.04), match with { MedianLatency = Math.Round(match.MedianLatency, 6), MaxLatency = Math.Round(match.MaxLatency, 6) });
        Assert.Equal(4 / 6.0, match.HitRate, 9);

        Assert.Equal(120, BeatStats.Tempo([0, 0.5, 1, 1.5, 2.01, 2.5]), 6);
        Assert.Equal(120, BeatStats.Tempo([0, 0.5, 1, 6, 6.5, 7]), 6);   // the 5 s pause is not tempo
        Assert.Equal(0, BeatStats.Tempo([1]));

        // Beats seen at steady 60 fps frame times: 126 BPM intervals are 28 or 29 frames. A plain median reads 124.1;
        // the mean of the intervals near it reads the true tempo. Missed and extra beats are left out of the mean.
        double[] frameQuantized = Enumerable.Range(0, 60).Select(k => Math.Ceiling(k * 60 / 126.0 * 60) / 60).ToArray();
        Assert.Equal(126, BeatStats.Tempo(frameQuantized), 0.1);
        double[] withErrors = frameQuantized.Where((_, k) => k != 20).Append(10.3).Order().ToArray();
        Assert.Equal(126, BeatStats.Tempo(withErrors), 0.1);
    }

    private static (int Code, string Output, string Error) Cli(params string[] args)
    {
        var stdout = new StringWriter(CultureInfo.InvariantCulture);
        var stderr = new StringWriter(CultureInfo.InvariantCulture);
        int code = WavAnalyzeCli.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }
}
