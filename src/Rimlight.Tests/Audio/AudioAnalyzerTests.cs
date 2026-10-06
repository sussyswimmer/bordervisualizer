using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Audio;

// C2: the real IAudioAnalyzer (doc 03 §2, §5). Signals are procedural (SyntheticAudio); no recorded music.
public sealed class AudioAnalyzerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(48000, 120f)]
    [InlineData(44100, 120f)]
    [InlineData(48000, 90f)]
    [InlineData(48000, 128f)]
    [InlineData(48000, 174f)]
    public void KickTrackTempoIsDetectedWithinTwoBpm(int sampleRate, float bpm)
    {
        // Doc 03 §5: decaying 60 Hz bursts + noise → tempo ± 2 BPM. Frames jitter ±20 % like a real render loop.
        var analyzer = new AudioAnalyzer();
        var beats = SyntheticAudio.Run(analyzer, SyntheticAudio.KickTrack(sampleRate, 20, bpm, 0.8f, 0.2f), sampleRate, jitter: 0.2f);
        int expected = (int)(18 * bpm / 60);
        output.WriteLine($"{sampleRate} Hz, {bpm} BPM: {beats.Count(t => t >= 2)}/{expected} beats, estimate {analyzer.Diagnostics.EstimatedBpm:F2}");
        Assert.InRange(analyzer.Diagnostics.EstimatedBpm, bpm - 2, bpm + 2);
        Assert.InRange(SyntheticAudio.BpmFromBeats(beats, 2), bpm - 2, bpm + 2);
        Assert.InRange(beats.Count(t => t >= 2), expected - 1, expected + 1);
    }

    [Fact]
    public void MusicLikeMixTracksTheKick()
    {
        var analyzer = new AudioAnalyzer();
        var beats = SyntheticAudio.Run(analyzer, SyntheticAudio.MusicLike(48000, 20, 124), 48000, jitter: 0.2f);
        Assert.InRange(analyzer.Diagnostics.EstimatedBpm, 122, 126);
        Assert.InRange(beats.Count(t => t >= 2), 36, 38);
    }

    [Theory]
    [InlineData(0.05f, 1)]
    [InlineData(0.3f, 2)]
    [InlineData(1.0f, 3)]
    public void NoBeatsOnConstantWhiteNoise(float amplitude, int seed)
    {
        // Doc 03 §5. Steady noise has no onsets; the median-onset guard keeps its flux tail from firing.
        var beats = SyntheticAudio.Run(new AudioAnalyzer(), SyntheticAudio.WhiteNoise(48000, 60, amplitude, seed), 48000, jitter: 0.2f, seed: seed);
        Assert.Empty(beats);
    }

    [Fact]
    public void NoBeatsOnSilenceOrMissingPackets()
    {
        var analyzer = new AudioAnalyzer();
        var zeros = new float[800];
        for (int i = 0; i < 1800; i++) Assert.Equal(0f, analyzer.Process(zeros, 48000, 1f / 60).Beat);
        for (int i = 0; i < 1800; i++) Assert.Equal(0f, analyzer.Process([], 48000, 1f / 60).Beat);
        Assert.Equal(0, analyzer.Diagnostics.BeatCount);
    }

    [Fact]
    public void BeatsAreVolumeInvariant()
    {
        float[] loud = SyntheticAudio.KickTrack(48000, 20, 120, 0.8f, 0.2f);
        float[] quiet = loud.Select(x => x * 0.1f).ToArray();
        var a = SyntheticAudio.Run(new AudioAnalyzer(), loud, 48000, jitter: 0.2f);
        var b = SyntheticAudio.Run(new AudioAnalyzer(), quiet, 48000, jitter: 0.2f);
        Assert.InRange(b.Count, a.Count - 1, a.Count + 1);
        Assert.InRange(SyntheticAudio.BpmFromBeats(b, 2), 118, 122);
    }

    [Fact]
    public void BeatJumpsToOneAndDecaysWithBeatDecaySeconds()
    {
        var analyzer = new AudioAnalyzer();
        float[] track = SyntheticAudio.KickTrack(48000, 6, 120, 0.8f, 0.05f);
        int position = 0;
        bool sawBeat = false;
        while (position + 800 <= track.Length)
        {
            AudioFeatures f = analyzer.Process(track.AsSpan(position, 800), 48000, 1f / 60);
            position += 800;
            if (f.Beat == 1f && position > 48000)
            {
                sawBeat = true;
                // One BeatDecaySeconds (120 ms) later the pulse is at 1/e ± 5 % (doc 03 §5 envelope tolerance).
                float after = f.Beat;
                for (int i = 0; i < 6 && position + 800 <= track.Length; i++, position += 800) after = analyzer.Process(track.AsSpan(position, 800), 48000, 0.02f).Beat;
                Assert.InRange(after, MathF.Exp(-1) * 0.95f, MathF.Exp(-1) * 1.05f);
                break;
            }
        }
        Assert.True(sawBeat);
    }

    [Fact]
    public void IsSilentAfterHoldTimeAndExitsWithHysteresis()
    {
        var analyzer = new AudioAnalyzer();
        float[] music = SyntheticAudio.KickTrack(48000, 3, 120, 0.8f, 0.2f);
        Feed(analyzer, music);
        var zeros = new float[800];
        float t = 0;
        while (!analyzer.Process(zeros, 48000, 1f / 60).IsSilent) t += 1f / 60;
        Assert.InRange(t, 1.95f, 2.05f); // SilenceHoldMs = 2000

        // -58 dBFS sits between the -60 entry and -55 exit thresholds: stays silent.
        var between = Sine(800, 0.0018f);
        for (int i = 0; i < 120; i++) Assert.True(analyzer.Process(between, 48000, 1f / 60).IsSilent);
        // -50 dBFS is above the exit threshold: not silent on the very next frame.
        Assert.False(analyzer.Process(Sine(800, 0.0045f), 48000, 1f / 60).IsSilent);

        // And from the non-silent state, -58 dBFS never enters silence.
        for (int i = 0; i < 300; i++) Assert.False(analyzer.Process(between, 48000, 1f / 60).IsSilent);
    }

    [Fact]
    public void MissingPacketsCountAsSilenceAfterTheTimeout()
    {
        var analyzer = new AudioAnalyzer();
        Feed(analyzer, SyntheticAudio.KickTrack(48000, 3, 120, 0.8f, 0.2f));
        float t = 0, previousLevel = 1;
        float peakDuringGap = 0;
        AudioFeatures f;
        do
        {
            f = analyzer.Process([], 48000, 1f / 60);
            t += 1f / 60;
            if (t > 0.2f) peakDuringGap = MathF.Max(peakDuringGap, f.Level - previousLevel);
            previousLevel = f.Level;
        }
        while (!f.IsSilent && t < 5);
        Assert.InRange(t, 2.05f, 2.2f); // NoPacketTimeoutSeconds (0.1) + SilenceHoldMs (2.0)
        Assert.True(f.Level < 0.05f && f.Bass < 0.05f, $"Level {f.Level}, Bass {f.Bass} should have decayed");
        Assert.True(peakDuringGap <= 0.001f, "Level must not rise while no packets arrive");
    }

    [Fact]
    public void ProcessAllocatesZeroBytesForEveryFeedPlatformSends()
    {
        IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer();
        float[] track = SyntheticAudio.KickTrack(48000, 8, 120, 0.8f, 0.2f);
        int[] sizes = [800, 735, 0, 0, 1, 5000, 2048, 800];
        int position = 0, frame = 0;
        void Step()
        {
            int size = sizes[frame % sizes.Length];
            if (position + size > track.Length) position = 0;
            int rate = frame % 400 < 200 ? 48000 : 44100;
            analyzer.Process(track.AsSpan(position, size), rate, 1f / 60);
            position += size;
            frame++;
        }
        for (int i = 0; i < 2000; i++) Step();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Step();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void DiagnosticsFollowTheDocumentedLayout()
    {
        var analyzer = new AudioAnalyzer();
        AnalyzerDiagnostics d = analyzer.Diagnostics;
        Assert.Equal(1025, d.Spectrum.Length);

        // A full-scale sine centered on bin 64 reads ≈ 1 in amplitude units.
        var sine = new float[2048];
        for (int i = 0; i < sine.Length; i++) sine[i] = MathF.Sin(2 * MathF.PI * 64 * i / 2048f);
        analyzer.Process(sine, 48000, 1f / 60);
        Assert.InRange(d.Spectrum[64], 0.99f, 1.01f);

        // One history entry per Process call (empty spans included), newest last.
        d.FluxHistory[^1] = -1;
        analyzer.Process([], 48000, 1f / 60);
        Assert.Equal(-1, d.FluxHistory[^2]);
        Assert.Equal(0, d.FluxHistory[^1]);

        analyzer.Reset();
        int beats = SyntheticAudio.Run(analyzer, SyntheticAudio.KickTrack(48000, 10, 120, 0.8f, 0.2f), 48000).Count;
        Assert.Equal(beats, d.BeatCount);
        Assert.InRange(d.EstimatedBpm, 118, 122);
        Assert.Same(d, analyzer.Diagnostics);
    }

    [Fact]
    public void TuningIsLiveValidatedAndWindowSizeReplacesDiagnostics()
    {
        var analyzer = new AudioAnalyzer();
        float[] track = SyntheticAudio.KickTrack(48000, 6, 120, 0.8f, 0.2f);
        analyzer.Tuning = analyzer.Tuning with { MinFlux = 1e6f };
        Assert.Empty(SyntheticAudio.Run(analyzer, track, 48000));

        AudioTuning previous = analyzer.Tuning;
        Assert.ThrowsAny<ArgumentException>(() => analyzer.Tuning = previous with { BeatDecaySeconds = 0 });
        Assert.ThrowsAny<ArgumentException>(() => analyzer.Tuning = previous with { WindowSize = 1000 });
        Assert.ThrowsAny<ArgumentException>(() => analyzer.Tuning = previous with { SilenceExitThresholdDb = -70 });
        Assert.Same(previous, analyzer.Tuning);

        AnalyzerDiagnostics before = analyzer.Diagnostics;
        analyzer.Tuning = previous with { WindowSize = 4096, MinFlux = 0.01f };
        Assert.NotSame(before, analyzer.Diagnostics);
        Assert.Equal(2049, analyzer.Diagnostics.Spectrum.Length);
        Assert.InRange(SyntheticAudio.Run(analyzer, track, 48000).Count, 8, 12);
    }

    [Fact]
    public void SensitivityScalesLevel()
    {
        float[] track = SyntheticAudio.KickTrack(48000, 6, 120, 0.8f, 0.2f);
        float Average(float sensitivity)
        {
            var analyzer = new AudioAnalyzer(new AudioTuning { Sensitivity = sensitivity });
            float sum = 0;
            int frames = 0;
            for (int p = 0; p + 800 <= track.Length; p += 800, frames++) sum += analyzer.Process(track.AsSpan(p, 800), 48000, 1f / 60).Level;
            return sum / frames;
        }
        float low = Average(0.5f), normal = Average(1), high = Average(2);
        Assert.True(low < normal && normal < high, $"{low} < {normal} < {high}");
        Assert.InRange(low, normal * 0.45f, normal * 0.55f);
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var analyzer = new AudioAnalyzer();
        SyntheticAudio.Run(analyzer, SyntheticAudio.KickTrack(48000, 6, 120, 0.8f, 0.2f), 48000);
        Feed(analyzer, new float[48000 * 3]);
        Assert.True(analyzer.Process(new float[800], 48000, 1f / 60).IsSilent);
        analyzer.Reset();
        AnalyzerDiagnostics d = analyzer.Diagnostics;
        Assert.Equal(0, d.BeatCount);
        Assert.Equal(0, d.EstimatedBpm);
        Assert.All(d.FluxHistory, x => Assert.Equal(0, x));
        Assert.All(d.Spectrum, x => Assert.Equal(0, x));
        Assert.Equal(default, analyzer.Process([], 48000, 1f / 60));
    }

    [Fact]
    public void BadFrameTimesNeitherThrowNorPoison()
    {
        var analyzer = new AudioAnalyzer();
        float[] track = SyntheticAudio.KickTrack(48000, 4, 120, 0.8f, 0.2f);
        for (int p = 0, i = 0; p + 800 <= track.Length; p += 800, i++)
        {
            float dt = (i % 7) switch { 3 => float.NaN, 4 => -1, 5 => float.PositiveInfinity, _ => 1f / 60 };
            AudioFeatures f = analyzer.Process(track.AsSpan(p, 800), 48000, dt);
            Assert.True(float.IsFinite(f.Level) && float.IsFinite(f.Bass) && float.IsFinite(f.Beat));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Process([], 0, 1f / 60));
    }

    [Fact]
    public void FactoryReturnsTheRealAnalyzer()
    {
        Assert.IsType<AudioAnalyzer>(CoreFactory.CreateAnalyzer());
        var tuning = new AudioTuning { Sensitivity = 1.5f };
        Assert.Same(tuning, CoreFactory.CreateAnalyzer(tuning).Tuning);
    }

    private static void Feed(IAudioAnalyzer analyzer, float[] samples)
    {
        for (int p = 0; p + 800 <= samples.Length; p += 800) analyzer.Process(samples.AsSpan(p, 800), 48000, 1f / 60);
    }

    private static float[] Sine(int length, float amplitude)
    {
        var samples = new float[length];
        // RMS of a sine is amplitude / √2: 0.0018 → -58 dBFS, 0.0045 → -50 dBFS.
        for (int i = 0; i < length; i++) samples[i] = amplitude * MathF.Sin(2 * MathF.PI * 440 * i / 48000f);
        return samples;
    }
}
