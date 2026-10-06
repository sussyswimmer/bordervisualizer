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

    // Render loops from 30 to 240 fps, and packets from continuous to 50 ms (Bluetooth, some drivers). Frames can be
    // empty between packets. New audio is analyzed in steps of at most one 60 fps frame, so flux stays comparable.
    [Theory]
    [InlineData(30f, 0f, 0f)]
    [InlineData(30f, 0.2f, 0f)]
    [InlineData(20f, 0.1f, 0f)]
    [InlineData(144f, 0.1f, 0f)]
    [InlineData(240f, 0.1f, 0.01f)]
    [InlineData(60f, 0.2f, 0.02f)]
    [InlineData(60f, 0.2f, 0.05f)]
    [InlineData(30f, 0.2f, 0.05f)]
    public void KicksAreDetectedAtAnyFrameRateAndPacketSize(float fps, float jitter, float packetSeconds)
    {
        foreach (var (samples, bpm) in new[] { (SyntheticAudio.KickTrack(48000, 20, 120, 0.8f, 0.2f), 120f), (SyntheticAudio.MusicLike(48000, 20, 124), 124f) })
        {
            var analyzer = new AudioAnalyzer();
            var beats = SyntheticAudio.Run(analyzer, samples, 48000, fps, jitter, packetSeconds: packetSeconds);
            float hits = SyntheticAudio.HitRate(beats, bpm, 20, 2);
            output.WriteLine($"{fps} fps, {packetSeconds * 1000} ms packets, {bpm} BPM: {hits:P0} hit, {SyntheticAudio.FalseBeats(beats, bpm, 2)} false, estimate {analyzer.Diagnostics.EstimatedBpm:F1}");
            Assert.True(hits >= 0.95f, $"{hits:P0} of kicks detected");
            Assert.Equal(0, SyntheticAudio.FalseBeats(beats, bpm, 2));
            Assert.InRange(analyzer.Diagnostics.EstimatedBpm, bpm - 2, bpm + 2);
        }
    }

    [Theory]
    [InlineData(30f, 0f)]
    [InlineData(144f, 0f)]
    [InlineData(240f, 0.01f)]
    [InlineData(60f, 0.05f)]
    public void NoBeatsOnWhiteNoiseAtAnyFrameRateAndPacketSize(float fps, float packetSeconds)
    {
        var beats = SyntheticAudio.Run(new AudioAnalyzer(), SyntheticAudio.WhiteNoise(48000, 60, 1f, 3), 48000, fps, 0.1f, packetSeconds: packetSeconds);
        Assert.Empty(beats);
    }

    // At 96/192 kHz an undecimated 2048-point window leaves the bass band 1–3 bins wide, and noise flux then fires.
    [Theory]
    [InlineData(88200)]
    [InlineData(96000)]
    [InlineData(176400)]
    [InlineData(192000)]
    [InlineData(384000)]
    public void HighSampleRatesKeepTempoAndStayQuietOnNoise(int sampleRate)
    {
        var analyzer = new AudioAnalyzer();
        var beats = SyntheticAudio.Run(analyzer, SyntheticAudio.KickTrack(sampleRate, 20, 120, 0.8f, 0.2f), sampleRate, jitter: 0.2f);
        Assert.True(SyntheticAudio.HitRate(beats, 120, 20, 2) >= 0.95f);
        Assert.InRange(analyzer.Diagnostics.EstimatedBpm, 118, 122);
        Assert.Empty(SyntheticAudio.Run(new AudioAnalyzer(), SyntheticAudio.WhiteNoise(sampleRate, 30, 0.3f, 5), sampleRate, jitter: 0.2f));
        Assert.Empty(SyntheticAudio.Run(new AudioAnalyzer(), SyntheticAudio.WhiteNoise(sampleRate, 30, 1f, 6), sampleRate, 30));
    }

    [Theory]
    [InlineData(48000, 48000)]
    [InlineData(74999, 74999)]
    [InlineData(88200, 44100)]
    [InlineData(96000, 48000)]
    [InlineData(192000, 48000)]
    [InlineData(384000, 48000)]
    [InlineData(768000, 96000)]
    public void AnalysisSampleRateMapsDiagnosticsBins(int captureRate, int analysisRate)
    {
        Assert.Equal(analysisRate, CoreFactory.AnalysisSampleRate(captureRate));
        // A 1 kHz tone peaks at the bin AnalysisSampleRate predicts.
        var analyzer = new AudioAnalyzer();
        var tone = new float[captureRate / 4];
        for (int i = 0; i < tone.Length; i++) tone[i] = 0.5f * MathF.Sin(2 * MathF.PI * 1000 * i / captureRate);
        for (int p = 0; p < tone.Length; p += captureRate / 60) analyzer.Process(tone.AsSpan(p, Math.Min(captureRate / 60, tone.Length - p)), captureRate, 1f / 60);
        float[] spectrum = analyzer.Diagnostics.Spectrum;
        int peak = Array.IndexOf(spectrum, spectrum.Max());
        Assert.InRange(peak * (float)analysisRate / 2048, 1000 - analysisRate / 2048f, 1000 + analysisRate / 2048f);
    }

    [Theory]
    [InlineData(0.25f, 30f)]
    [InlineData(0.25f, 60f)]
    [InlineData(0.5f, 30f)]
    [InlineData(0.5f, 60f)]
    [InlineData(1f, 30f)]
    [InlineData(2f, 60f)]
    public void EverySensitivityStillDetectsTheKick(float sensitivity, float fps)
    {
        // Doc 03 scales k = 1.5 by 1/Sensitivity; 1/√Sensitivity keeps Sensitivity 0.25 usable (k = 3, not 6).
        var analyzer = new AudioAnalyzer(new AudioTuning { Sensitivity = sensitivity });
        var beats = SyntheticAudio.Run(analyzer, SyntheticAudio.MusicLike(48000, 20, 124), 48000, fps, 0.2f);
        Assert.True(SyntheticAudio.HitRate(beats, 124, 20, 2) >= 0.95f, $"{SyntheticAudio.HitRate(beats, 124, 20, 2):P0}");
        Assert.Equal(0, SyntheticAudio.FalseBeats(beats, 124, 2));
    }

    [Fact]
    public void HigherSensitivityDetectsWeakerKicks()
    {
        // Weak kicks under heavy noise sit near the median onset guard, so Sensitivity decides how many fire.
        // Measured: 5 % / 29 % / 73 % / 98 % at Sensitivity 0.25 / 0.5 / 1 / 2.
        float[] track = SyntheticAudio.KickTrack(48000, 30, 120, 0.2f, 0.5f, seed: 21);
        float[] rates = new[] { 0.25f, 0.5f, 1f, 2f }
            .Select(s => SyntheticAudio.HitRate(SyntheticAudio.Run(new AudioAnalyzer(new AudioTuning { Sensitivity = s }), track, 48000, jitter: 0.2f), 120, 30, 2))
            .ToArray();
        output.WriteLine(string.Join(" / ", rates.Select(r => r.ToString("P0"))));
        for (int i = 1; i < rates.Length; i++) Assert.True(rates[i] > rates[i - 1] + 0.15f);
        Assert.True(rates[0] <= 0.1f);
        Assert.True(rates[^1] >= 0.95f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-20f)]
    [InlineData(-40f)]
    [InlineData(-60f)]
    public void BeatsAreDetectedAtAnyPlaybackVolume(float db)
    {
        // An absolute MinFlux of 0.01 dropped every beat at −40 dB (a player's own volume at about 10 %). MinFlux is
        // relative to the window's RMS, so the same music gives the same beats at any level above near-silence.
        float gain = MathF.Pow(10, db / 20);
        float[] music = SyntheticAudio.MusicLike(48000, 20, 124).Select(x => x * gain).ToArray();
        var analyzer = new AudioAnalyzer();
        var beats = SyntheticAudio.Run(analyzer, music, 48000, jitter: 0.2f);
        Assert.True(SyntheticAudio.HitRate(beats, 124, 20, 2) >= 0.95f, $"{SyntheticAudio.HitRate(beats, 124, 20, 2):P0}");
        Assert.Equal(0, SyntheticAudio.FalseBeats(beats, 124, 2));
        Assert.InRange(analyzer.Diagnostics.EstimatedBpm, 122, 126);
    }

    [Fact]
    public void MinFluxIsRelativeToTheLevelAndNearSilenceFiresNothing()
    {
        // A high MinFlux trims the same weak onsets at 0 and −40 dB.
        float[] weak = SyntheticAudio.KickTrack(48000, 30, 120, 0.2f, 0.5f, seed: 21);
        float Rate(float minFlux, float gain) => SyntheticAudio.HitRate(
            SyntheticAudio.Run(new AudioAnalyzer(new AudioTuning { MinFlux = minFlux, Sensitivity = 2 }), weak.Select(x => x * gain).ToArray(), 48000, jitter: 0.2f), 120, 30, 2);
        Assert.True(Rate(0.01f, 1) >= 0.95f);
        float loud = Rate(0.6f, 1), quiet = Rate(0.6f, 0.01f);
        Assert.True(loud <= 0.5f, $"{loud:P0}");
        Assert.InRange(quiet, loud - 0.05f, loud + 0.05f);

        // At or below the near-silence level (−80 dBFS window RMS) no beat fires, not even from clear kicks.
        float[] faintKicks = SyntheticAudio.KickTrack(48000, 10, 120, 0.8f, 0.2f).Select(x => x * 3e-5f).ToArray();
        Assert.Empty(SyntheticAudio.Run(new AudioAnalyzer(), faintKicks, 48000));
        // Noise (hiss, dither) fires nothing at −70 or −95 dBFS.
        Assert.Empty(SyntheticAudio.Run(new AudioAnalyzer(), SyntheticAudio.WhiteNoise(48000, 60, 0.00055f, 7), 48000, jitter: 0.2f));
        Assert.Empty(SyntheticAudio.Run(new AudioAnalyzer(), SyntheticAudio.WhiteNoise(48000, 60, 0.00003f, 8), 48000, jitter: 0.2f));
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
        Assert.InRange(t, 1.95f, 2.05f); // SilenceHoldMs from the last packet; the 0.1 s timeout is part of it
        Assert.True(f.Level < 0.05f && f.Bass < 0.05f, $"Level {f.Level}, Bass {f.Bass} should have decayed");
        Assert.True(peakDuringGap <= 0.001f, "Level must not rise while no packets arrive");
    }

    [Fact]
    public void MusicAfterALongPacketGapIsDetectedAtOnce()
    {
        var analyzer = new AudioAnalyzer();
        SyntheticAudio.Run(analyzer, SyntheticAudio.KickTrack(48000, 4, 120, 0.8f, 0.2f), 48000);
        for (int i = 0; i < 600; i++) Assert.True(analyzer.Process([], 48000, 1f / 60).Level < 0.05f || i < 60);
        Assert.True(analyzer.Process([], 48000, 1f / 60).IsSilent);
        Assert.All(analyzer.Diagnostics.Spectrum, x => Assert.Equal(0, x));

        // The flux history saw the gap as zeros, not as the loud track's statistics from 10 s ago, so a much
        // quieter track's first kick is already an onset.
        float[] track = SyntheticAudio.KickTrack(48000, 4, 120, 0.12f, 0.03f, seed: 22);
        var beats = SyntheticAudio.Run(analyzer, track, 48000);
        Assert.InRange(beats[0], 0, 0.05f);
        Assert.True(SyntheticAudio.HitRate(beats, 120, 4, 0) >= 0.95f);
        Assert.False(analyzer.Process([], 48000, 1f / 60).IsSilent);
    }

    [Fact]
    public void ProcessAllocatesZeroBytesForEveryFeedPlatformSends()
    {
        IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer();
        float[] track = SyntheticAudio.KickTrack(48000, 8, 120, 0.8f, 0.2f);
        int[] sizes = [800, 735, 0, 0, 1, 5000, 2048, 800, 0, 0, 0, 0, 0, 0, 0, 0, 40000, 1600, 3];
        int position = 0, frame = 0;
        void Step()
        {
            int size = sizes[frame % sizes.Length];
            if (position + size > track.Length) position = 0;
            int rate = (frame / 200 % 4) switch { 0 => 48000, 1 => 44100, 2 => 96000, _ => 192000 };
            analyzer.Process(track.AsSpan(position, size), rate, 1f / 60);
            position += size;
            frame++;
        }
        for (int i = 0; i < 2000; i++) Step();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1600; i++) Step();
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
    public void ResetClearsEverythingButIsSilent()
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
        // A device change while nothing plays must not flash a hidden glow back on for 2 s.
        Assert.Equal(new AudioFeatures(0, 0, 0, true), analyzer.Process([], 48000, 1f / 60));
        Assert.False(analyzer.Process(Sine(800, 0.0045f), 48000, 1f / 60).IsSilent);
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

    [Theory]
    [InlineData(240f, 0.01f)]
    [InlineData(144f, 0.02f)]
    [InlineData(30f, 0f)]
    public void IsSilentTimingIncludesEmptyFramesBetweenPackets(float fps, float packetSeconds)
    {
        // With 10 ms packets at 240 fps most frames are empty; their time still counts toward SilenceHoldMs.
        var analyzer = new AudioAnalyzer();
        float[] signal = [.. SyntheticAudio.KickTrack(48000, 3, 120, 0.8f, 0.2f), .. new float[48000 * 4]];
        var silentAt = new List<float>();
        int packet = packetSeconds > 0 ? (int)(packetSeconds * 48000) : 1, position = 0;
        double time = 0;
        bool wasSilent = false;
        while (time < 6.5)
        {
            time += 1 / fps;
            int arrived = (int)Math.Min(signal.Length, (long)(time * 48000) / packet * packet);
            bool silent = analyzer.Process(signal.AsSpan(position, arrived - position), 48000, 1 / fps).IsSilent;
            position = arrived;
            if (silent && !wasSilent) silentAt.Add((float)time);
            wasSilent = silent;
        }
        Assert.Single(silentAt);
        Assert.InRange(silentAt[0], 5 - 0.05f, 5 + 0.05f + packetSeconds);
    }

    [Theory]
    [InlineData(60f, 0f)]
    [InlineData(240f, 0.01f)]
    [InlineData(144f, 0.02f)]
    public void FluxHistoryIsMeasuredInRealTime(float fps, float packetSeconds)
    {
        // Empty frames between packets count toward the history's duration: warm-up (a quarter of
        // FluxHistorySeconds) ends after 0.25 s of real time, so the kick at 0.5 s is already detected.
        var beats = SyntheticAudio.Run(new AudioAnalyzer(), SyntheticAudio.KickTrack(48000, 2, 120, 0.8f, 0.2f), 48000, fps, 0.1f, packetSeconds: packetSeconds);
        Assert.Contains(beats, t => t >= 0.5f && t < 0.6f);
    }

    [Fact]
    public void TempoIsForgottenAfterFourSecondsWithoutBeats()
    {
        var analyzer = new AudioAnalyzer();
        SyntheticAudio.Run(analyzer, SyntheticAudio.KickTrack(48000, 8, 120, 0.8f, 0.2f), 48000);
        Assert.InRange(analyzer.Diagnostics.EstimatedBpm, 118, 122);
        float[] noise = SyntheticAudio.WhiteNoise(48000, 3.2f, 0.2f, 9);
        SyntheticAudio.Run(analyzer, noise, 48000);
        Assert.InRange(analyzer.Diagnostics.EstimatedBpm, 118, 122);
        SyntheticAudio.Run(analyzer, SyntheticAudio.WhiteNoise(48000, 0.6f, 0.2f, 10), 48000);
        Assert.Equal(0, analyzer.Diagnostics.EstimatedBpm);
    }

    [Fact]
    public void WideningTheBassBandLiveFiresNoBeat()
    {
        // A steady 200 Hz tone sits outside the default bass band. When the band grows to include it, its bins only
        // seed their history: the tone's whole magnitude is not an onset.
        float[] signal = SyntheticAudio.WhiteNoise(48000, 8, 0.05f, 11);
        for (int i = 0; i < signal.Length; i++) signal[i] += 0.5f * MathF.Sin(2 * MathF.PI * 200 * i / 48000f);
        var analyzer = new AudioAnalyzer();
        Assert.Empty(SyntheticAudio.Run(analyzer, signal[..(48000 * 4)], 48000));
        analyzer.Tuning = analyzer.Tuning with { BassMaxHz = 300 };
        Assert.Empty(SyntheticAudio.Run(analyzer, signal[(48000 * 4)..], 48000));
        Assert.Equal(0, analyzer.Diagnostics.BeatCount);
    }

    [Fact]
    public void AStallBurstFiresNoStaleOnsetAndBeatsResume()
    {
        // After a 1 s render stall, Platform drains 1 s of audio in one frame. Only the newest window plus a few
        // steps is analyzed; the level jump inside the dropped audio is not an onset, and beats resume afterwards.
        var analyzer = new AudioAnalyzer();
        Assert.Empty(SyntheticAudio.Run(analyzer, SyntheticAudio.WhiteNoise(48000, 4, 0.05f, 12), 48000));
        analyzer.Process(SyntheticAudio.WhiteNoise(48000, 1, 1f, 13), 48000, 1);
        Assert.Equal(0, analyzer.Diagnostics.BeatCount);
        float[] loud = SyntheticAudio.KickTrack(48000, 9, 120, 0.8f, 0.6f, seed: 14);
        var beats = SyntheticAudio.Run(analyzer, loud, 48000, jitter: 0.2f);
        Assert.True(SyntheticAudio.HitRate(beats, 120, 9, 0.4f) >= 0.95f, $"{SyntheticAudio.HitRate(beats, 120, 9, 0.4f):P0}");
        Assert.Equal(0, SyntheticAudio.FalseBeats(beats, 120, 0));
    }

    [Fact]
    public void SlowFrameRatesAreNotStalls()
    {
        // At 8 fps every frame carries 6000 samples; that is still analyzed in full, so no beat is lost.
        var analyzer = new AudioAnalyzer();
        var beats = SyntheticAudio.Run(analyzer, SyntheticAudio.MusicLike(48000, 20, 124), 48000, 8, 0.1f);
        Assert.True(SyntheticAudio.HitRate(beats, 124, 20, 2, maxDelay: 0.16f) >= 0.95f);
        Assert.InRange(analyzer.Diagnostics.EstimatedBpm, 122, 126);
    }

    [Fact]
    public void AbsurdFrameTimesStayFinite()
    {
        var analyzer = new AudioAnalyzer();
        float[] track = SyntheticAudio.KickTrack(48000, 2, 120, 0.8f, 0.2f);
        Feed(analyzer, track);
        AudioFeatures f = analyzer.Process(track.AsSpan(0, 800), 48000, float.MaxValue);
        Assert.True(float.IsFinite(f.Level) && float.IsFinite(f.Bass) && float.IsFinite(f.Beat));
        // A huge gap without packets fills the window with zeros and counts as silence at once.
        f = analyzer.Process([], 48000, 1e30f);
        Assert.True(f.IsSilent);
        Assert.Equal(0, f.Level, 3);
        Assert.All(analyzer.Diagnostics.Spectrum, x => Assert.Equal(0, x));
        Assert.True(float.IsFinite(analyzer.Process([], 48000, 1e30f).Level));
    }

    [Fact]
    public void TuningBoundsAreValidated()
    {
        var analyzer = new AudioAnalyzer();
        AudioTuning t = analyzer.Tuning;
        foreach (AudioTuning bad in new[]
        {
            t with { Sensitivity = 0.2f }, t with { Sensitivity = 2.1f }, t with { Sensitivity = float.NaN },
            t with { WindowSize = 256 }, t with { WindowSize = 512 }, t with { WindowSize = 32768 }, t with { WindowSize = 3000 },
            t with { NoPacketTimeoutSeconds = 0.02f }, t with { NoPacketTimeoutSeconds = float.PositiveInfinity },
            t with { FluxHistorySeconds = 4.1f }, t with { FluxHistorySeconds = 0 },
        })
        {
            Assert.ThrowsAny<ArgumentException>(() => analyzer.Tuning = bad);
            Assert.ThrowsAny<ArgumentException>(() => new AudioAnalyzer(bad));
        }
        Assert.Same(t, analyzer.Tuning);
        foreach (AudioTuning good in new[]
        {
            t with { Sensitivity = 0.25f }, t with { Sensitivity = 2 }, t with { WindowSize = 1024 }, t with { WindowSize = 16384 },
            t with { NoPacketTimeoutSeconds = 0.03f }, t with { FluxHistorySeconds = 4 },
        })
        {
            analyzer.Tuning = good;
            Assert.Same(good, analyzer.Tuning);
        }
    }

    [Theory]
    [InlineData(240f, 0.01f)]
    [InlineData(360f, 0f)]
    [InlineData(480f, 0f)]
    [InlineData(1000f, 0f)]
    public void FluxHistoryCoversFourSecondsAtAnyRefreshRate(float fps, float packetSeconds)
    {
        // FluxHistorySeconds = 4 on a 240–1000 Hz display: the history ring must still span the full 4 s, so the
        // threshold doesn't silently shrink to a shorter window (beat steps are at least 1/240 s).
        var tuning = new AudioTuning { FluxHistorySeconds = 4 };
        var analyzer = new AudioAnalyzer(tuning);
        var beats = SyntheticAudio.Run(analyzer, SyntheticAudio.MusicLike(48000, 20, 124), 48000, fps, 0.1f, packetSeconds: packetSeconds);
        Assert.True(analyzer.FluxHistoryStoredSeconds >= 4, $"{analyzer.FluxHistoryStoredSeconds:F2} s stored");
        Assert.True(SyntheticAudio.HitRate(beats, 124, 20, 5) >= 0.95f);
        // Idle frames (no packets) at the same rate keep the full window too.
        for (int i = 0; i < 6 * fps; i++) analyzer.Process([], 48000, 1 / fps);
        Assert.True(analyzer.FluxHistoryStoredSeconds >= 4, $"{analyzer.FluxHistoryStoredSeconds:F2} s stored while idle");
        Assert.Empty(SyntheticAudio.Run(new AudioAnalyzer(tuning), SyntheticAudio.WhiteNoise(48000, 30, 1f, 14), 48000, fps, 0.1f, packetSeconds: packetSeconds));
    }

    [Theory]
    [InlineData(1024, 30f, 150f)]
    [InlineData(1024, 50f, 90f)]
    [InlineData(2048, 40f, 45f)]
    public void ABassBandNarrowerThanABinStillCarriesBassAndBeats(int windowSize, float bassMin, float bassMax)
    {
        // Small windows or narrow live-tuned edges can leave no bin inside the bass band. It then uses the bin just
        // below its center, so Bass and beats keep working instead of going silent.
        // The 1024-sample default band case is the smallest window the analyzer accepts.
        var analyzer = new AudioAnalyzer(new AudioTuning { WindowSize = windowSize, BassMinHz = bassMin, BassMaxHz = bassMax });
        var beats = SyntheticAudio.Run(analyzer, SyntheticAudio.KickTrack(48000, 20, 120, 0.8f, 0.2f), 48000, jitter: 0.2f);
        Assert.True(analyzer.Process(SyntheticAudio.KickTrack(48000, 0.1f, 120, 0.8f, 0.2f), 48000, 0.1f).Bass > 0);
        Assert.True(SyntheticAudio.HitRate(beats, 120, 20, 2) >= 0.95f);
        Assert.InRange(analyzer.Diagnostics.EstimatedBpm, 118, 122);
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
