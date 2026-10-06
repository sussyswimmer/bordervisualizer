using System.Diagnostics;
using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Audio;

public sealed class AudioFrontEndTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void PipelineIsVolumeInvariantAfterWarmup(int sampleRate)
    {
        var loud = new AudioFrontEnd();
        var quiet = new AudioFrontEnd();
        var samples = new float[sampleRate / 60];
        var quietSamples = new float[samples.Length];
        float maxError = 0, maxLevel = 0;
        for (int frame = 0; frame < 360; frame++)
        {
            FillSignal(samples, frame * samples.Length, sampleRate);
            for (int i = 0; i < samples.Length; i++) quietSamples[i] = samples[i] * 0.1f;
            AmplitudeFeatures expected = loud.Process(samples, sampleRate, 1f / 60);
            AmplitudeFeatures actual = quiet.Process(quietSamples, sampleRate, 1f / 60);
            if (frame >= 120)
            {
                maxError = MathF.Max(maxError, MathF.Max(MathF.Abs(expected.Level - actual.Level), MathF.Abs(expected.Bass - actual.Bass)));
                maxLevel = MathF.Max(maxLevel, expected.Level);
            }
        }
        Assert.InRange(maxError, 0, 0.001f);
        Assert.True(maxLevel > 0.5f);
        output.WriteLine($"{sampleRate} Hz: max Level/Bass volume-invariance error = {maxError:E6}; max Level = {maxLevel:F6}");
    }

    [Fact]
    public void PipelineAllocatesZeroBytesOverThousandFrames()
    {
        var analyzer = new AudioFrontEnd();
        var samples = new float[800];
        FillSignal(samples, 0, 48000);
        for (int i = 0; i < 2000; i++) analyzer.Process(samples, 48000, 1f / 60);
        long start = Stopwatch.GetTimestamp();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) analyzer.Process(samples, 48000, 1f / 60);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        double nsPerFrame = Stopwatch.GetElapsedTime(start).TotalNanoseconds / 1000;
        Assert.Equal(0, bytes);
        output.WriteLine($"C1 front end, 48 kHz, 800 samples/frame, N=2048, 2000 warmup + 1000 measured frames: {nsPerFrame:F1} ns/frame, {bytes / 1000.0:F1} bytes/frame. Timing is informational, not a CI threshold.");
    }

    [Fact]
    public void IrregularFeedsAllocateZeroBytes()
    {
        // What Platform sends: empty spans (no packets), catch-up bursts after a stall, odd sizes, device rate changes.
        var analyzer = new AudioFrontEnd();
        var burst = new float[5000];
        FillSignal(burst, 0, 48000);
        int[] sizes = [0, 1, 441, 735, 800, 2048, 2049, 5000];
        for (int i = 0; i < 400; i++) analyzer.Process(burst.AsSpan(0, sizes[i % sizes.Length]), i % 50 < 25 ? 48000 : 44100, 1f / 60);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) analyzer.Process(burst.AsSpan(0, sizes[i % sizes.Length]), i % 50 < 25 ? 48000 : 44100, 1f / 60);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ResetAndSampleRateChangeMatchFreshAnalyzer()
    {
        var analyzer = new AudioFrontEnd();
        WarmUp(analyzer, 48000, 120);

        analyzer.Reset();
        var fresh = new AudioFrontEnd();
        AssertSameSequence(fresh, analyzer, 48000, 240);

        // A sample-rate change resets on its own: compare against an analyzer that only ever saw 44.1 kHz.
        var fresh44 = new AudioFrontEnd();
        AssertSameSequence(fresh44, analyzer, 44100, 240);
        Assert.Equal(fresh44.Spectrum.ToArray(), analyzer.Spectrum.ToArray());
    }

    [Fact]
    public void MatchesHandWiredDoc03PipelineWithAsymmetricTuning()
    {
        // Distinct weights and time constants make any mis-wiring (swapped bands, weights or tau) visible.
        // A -96 dBFS dither gap exercises the near-silence hold on all three gains (exact zeros would hold anyway).
        var tuning = new AudioTuning
        {
            LevelBassWeight = 0.7f, LevelMidWeight = 0.2f, LevelHighWeight = 0.05f,
            LevelAttackSeconds = 0.020f, LevelReleaseSeconds = 0.300f,
            BassAttackSeconds = 0.010f, BassReleaseSeconds = 0.150f,
        };
        var analyzer = new AudioFrontEnd(tuning);
        var spectrum = new SpectrumAnalyzer(tuning.WindowSize);
        AutoGain bassGain = new(), midGain = new(), highGain = new();
        Envelope level = new(), bass = new();
        float holdRms = MathF.Pow(10, -80f / 20); // SilenceThresholdDb - 20 dB
        var samples = new float[800];
        var dither = new Random(3);
        for (int frame = 0; frame < 720; frame++)
        {
            FillSignal(samples, frame * samples.Length, 48000);
            if (frame >= 300 && frame < 330)
                for (int i = 0; i < samples.Length; i++) samples[i] = (float)((dither.NextDouble() * 2 - 1) * 2.7e-5);
            spectrum.Process(samples);
            BandEnergies e = BandAnalyzer.Analyze(spectrum.Magnitudes, tuning.WindowSize, 48000, tuning);
            bool silent = spectrum.QuietestQuarterRms <= holdRms;
            float b = bassGain.Update(e.Bass, 1f / 60, tuning, silent);
            float m = midGain.Update(e.Mid, 1f / 60, tuning, silent);
            float h = highGain.Update(e.High, 1f / 60, tuning, silent);
            float target = Math.Clamp(0.7f * b + 0.2f * m + 0.05f * h, 0, 1);
            var expected = new AmplitudeFeatures(level.Update(target, 1f / 60, 0.020f, 0.300f), bass.Update(b, 1f / 60, 0.010f, 0.150f));
            Assert.Equal(expected, analyzer.Process(samples, 48000, 1f / 60));
        }
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(false, 5, 795)]
    [InlineData(false, 200, 600)]
    [InlineData(false, 548, 252)]
    [InlineData(false, 790, 10)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 20, 300)]
    [InlineData(true, 700, 50)]
    public void MidStreamSilenceDoesNotCollapseTheFloor(bool dithered, int stopOffset, int resumeOffset)
    {
        // 10 s of music, then 1 s of digital zeros or -96 dBFS dither, then music again. The gap starts and ends at
        // arbitrary points inside an 800-sample hop: the frames where the gap (or the music) only partly fills the
        // window are the ones that used to collapse the floor and pin Level/Bass near 1 for a minute or more.
        const int fs = 48000, hop = 800;
        int gapLength = fs + resumeOffset - stopOffset;
        var signal = new float[fs * 16];
        FillSignal(signal, 0, fs);
        var random = new Random(7);
        int gapStart = 600 * hop + stopOffset;
        for (int i = gapStart; i < gapStart + gapLength; i++) signal[i] = dithered ? (float)((random.NextDouble() * 2 - 1) * 2.7e-5) : 0;
        var reference = new float[signal.Length];
        FillSignal(reference, 0, fs);
        Array.Copy(reference, 0, reference, 0, gapStart); // uninterrupted music, same phase after the gap

        var analyzer = new AudioFrontEnd();
        var uninterrupted = new AudioFrontEnd();
        int resumeFrame = (gapStart + gapLength) / hop + 1;
        float maxLevelError = 0, maxBassError = 0;
        for (int frame = 0; (frame + 1) * hop <= signal.Length; frame++)
        {
            AmplitudeFeatures actual = analyzer.Process(signal.AsSpan(frame * hop, hop), fs, 1f / 60);
            AmplitudeFeatures expected = uninterrupted.Process(reference.AsSpan(frame * hop, hop), fs, 1f / 60);
            int after = frame - resumeFrame;
            if (after >= 120 && after < 360)
            {
                maxLevelError = MathF.Max(maxLevelError, MathF.Abs(actual.Level - expected.Level));
                maxBassError = MathF.Max(maxBassError, MathF.Abs(actual.Bass - expected.Bass));
            }
        }
        output.WriteLine($"dithered={dithered} stop+{stopOffset} resume+{resumeOffset}: max |dLevel| {maxLevelError:F3}, max |dBass| {maxBassError:F3} over 2-6 s after resume");
        Assert.InRange(maxLevelError, 0, 0.1f);
        Assert.InRange(maxBassError, 0, 0.1f);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 10)]
    [InlineData(false, 300)]
    [InlineData(true, 790)]
    public void LeadingSilenceDoesNotSeedTheFloor(bool dithered, int musicOffset)
    {
        // 2 s of zeros or dither at startup, then music starting anywhere inside a hop, against music alone.
        const int fs = 48000, hop = 800;
        int lead = 2 * fs + musicOffset;
        var signal = new float[lead + 8 * fs];
        var random = new Random(11);
        for (int i = 0; i < lead; i++) signal[i] = dithered ? (float)((random.NextDouble() * 2 - 1) * 2.7e-5) : 0;
        FillSignal(signal.AsSpan(lead), 0, fs);
        var analyzer = new AudioFrontEnd();
        int frame = 0;
        for (; (frame + 1) * hop <= lead; frame++) analyzer.Process(signal.AsSpan(frame * hop, hop), fs, 1f / 60);

        // From here, compare against a fresh analyzer fed the same music-only stream (offset by the partial hop).
        var musicOnly = new AudioFrontEnd();
        int musicStart = frame * hop;
        float maxError = 0;
        for (int f = 0; musicStart + (f + 1) * hop <= signal.Length; f++)
        {
            AmplitudeFeatures actual = analyzer.Process(signal.AsSpan(musicStart + f * hop, hop), fs, 1f / 60);
            int from = Math.Max(lead, musicStart + f * hop), to = musicStart + (f + 1) * hop;
            AmplitudeFeatures expected = musicOnly.Process(signal.AsSpan(from, to - from), fs, 1f / 60);
            if (f >= 120 && f < 360) maxError = MathF.Max(maxError, MathF.Max(MathF.Abs(actual.Level - expected.Level), MathF.Abs(actual.Bass - expected.Bass)));
        }
        output.WriteLine($"dithered={dithered} offset {musicOffset}: max |d| {maxError:F3}");
        Assert.InRange(maxError, 0, 0.1f);
    }

    [Fact]
    public void QuieterTrackAfterALongPauseIsNotDimmedByTheOldPeak()
    {
        // Loud track, 30 s of digital silence, then a track 24 dB quieter. The peak keeps decaying during the
        // pause (half-life 4 s), so the quiet track is normalized like it is when played on its own.
        const int fs = 48000, hop = 800;
        var loud = new float[fs * 30];
        FillSignal(loud, 0, fs);
        var quiet = new float[fs * 10];
        FillSignal(quiet, 0, fs);
        for (int i = 0; i < quiet.Length; i++) quiet[i] *= 0.063f;
        var afterPause = new AudioFrontEnd();
        var alone = new AudioFrontEnd();
        for (int p = 0; p + hop <= loud.Length; p += hop) afterPause.Process(loud.AsSpan(p, hop), fs, 1f / 60);
        var zeros = new float[hop];
        for (int i = 0; i < 30 * 60; i++) afterPause.Process(zeros, fs, 1f / 60);
        float maxError = 0;
        for (int f = 0; (f + 1) * hop <= quiet.Length; f++)
        {
            AmplitudeFeatures a = afterPause.Process(quiet.AsSpan(f * hop, hop), fs, 1f / 60);
            AmplitudeFeatures b = alone.Process(quiet.AsSpan(f * hop, hop), fs, 1f / 60);
            if (f >= 120) maxError = MathF.Max(maxError, MathF.Abs(a.Level - b.Level));
        }
        output.WriteLine($"quiet track after pause: max |dLevel| {maxError:F3} from 2 s on");
        Assert.InRange(maxError, 0, 0.1f);
    }

    [Theory]
    [InlineData(float.NaN, 800, 100)]
    [InlineData(float.NaN, 800, 400)]
    [InlineData(float.NaN, 4096, 3000)]
    [InlineData(float.PositiveInfinity, 800, 100)]
    [InlineData(float.PositiveInfinity, 800, 400)]
    [InlineData(float.NegativeInfinity, 4096, 3000)]
    public void NonFiniteSampleBehavesLikeZero(float bad, int chunk, int index)
    {
        // One NaN/Inf sample used to latch Level/Bass to NaN until Reset. It is now treated as a 0 sample, whichever
        // of the three ring-copy paths it takes (before the wrap, after the wrap, oversized catch-up burst).
        var analyzer = new AudioFrontEnd();
        var reference = new AudioFrontEnd();
        var samples = new float[chunk];
        for (int frame = 0; frame < 600; frame++)
        {
            FillSignal(samples, frame * chunk, 48000);
            var clean = samples.ToArray();
            if (frame == 120) { samples[index] = bad; clean[index] = 0; }
            AmplitudeFeatures actual = analyzer.Process(samples, 48000, 1f / 60);
            Assert.Equal(reference.Process(clean, 48000, 1f / 60), actual);
            Assert.All(analyzer.Spectrum.ToArray(), x => Assert.True(float.IsFinite(x)));
        }
    }

    [Theory]
    [InlineData(1e6f)]
    [InlineData(1e18f)]
    [InlineData(1e30f)]
    public void HugeFiniteSampleCannotOverflowTheSpectrum(float huge)
    {
        var analyzer = new AudioFrontEnd();
        var samples = new float[800];
        float maxLevel = 0, maxBass = 0;
        for (int frame = 0; frame < 900; frame++)
        {
            FillSignal(samples, frame * samples.Length, 48000);
            if (frame == 120) samples[400] = huge;
            AmplitudeFeatures result = analyzer.Process(samples, 48000, 1f / 60);
            Assert.All(analyzer.Spectrum.ToArray(), x => Assert.True(float.IsFinite(x)));
            if (frame >= 600) { maxLevel = MathF.Max(maxLevel, result.Level); maxBass = MathF.Max(maxBass, result.Bass); }
        }
        // The spike is clamped to ±16, so the signal is still tracked a few seconds later (not stuck at 0 or 1).
        Assert.InRange(maxLevel, 0.5f, 1);
        Assert.InRange(maxBass, 0.5f, 1);
    }

    [Fact]
    public void NearSilenceGateIsTwentyDbBelowTheSilenceThreshold()
    {
        // A steady tone never seeds auto-gain at -85 dBFS RMS (held as near-silence), but does at -75 dBFS.
        AmplitudeFeatures Run(float rmsDb)
        {
            var analyzer = new AudioFrontEnd();
            var samples = new float[800];
            float amplitude = MathF.Pow(10, rmsDb / 20) * MathF.Sqrt(2);
            AmplitudeFeatures last = default;
            for (int frame = 0; frame < 240; frame++)
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    float t = (frame * samples.Length + i) / 48000f;
                    samples[i] = amplitude * (1 + 0.5f * MathF.Sin(2 * MathF.PI * 2 * t)) / 1.5f * MathF.Sin(2 * MathF.PI * 80 * t);
                }
                last = analyzer.Process(samples, 48000, 1f / 60);
                if (rmsDb < -80) Assert.Equal(default, last);
            }
            return last;
        }
        Run(-85);
        AmplitudeFeatures audible = Run(-75);
        Assert.True(audible.Level > 0 || audible.Bass > 0);
    }

    [Fact]
    public void QuietestQuarterRmsIsTheTimeDomainRmsOfTheQuietestQuarter()
    {
        var spectrum = new SpectrumAnalyzer(2048);
        var sine = new float[2048];
        for (int i = 0; i < sine.Length; i++) sine[i] = 0.5f * MathF.Sin(2 * MathF.PI * 64 * i / 2048f);
        spectrum.Process(sine);
        Assert.InRange(spectrum.QuietestQuarterRms, 0.5f / MathF.Sqrt(2) * 0.99f, 0.5f / MathF.Sqrt(2) * 1.01f);
        spectrum.Process(new float[600]); // the newest 600 samples are zeros: the last quarter is silent
        Assert.Equal(0f, spectrum.QuietestQuarterRms);
    }

    [Fact]
    public void LevelWeightsAreFreeSliderValues()
    {
        // K8 edits each weight independently, so they need not sum to 1. Level stays within 0..1.
        foreach (var tuning in new[]
        {
            new AudioTuning { LevelBassWeight = 0.6f },
            new AudioTuning { LevelBassWeight = 1, LevelMidWeight = 1, LevelHighWeight = 1 },
            new AudioTuning { LevelBassWeight = 0, LevelMidWeight = 0, LevelHighWeight = 0 },
        })
        {
            var analyzer = new AudioFrontEnd(tuning);
            var samples = new float[800];
            for (int frame = 0; frame < 240; frame++)
            {
                FillSignal(samples, frame * samples.Length, 48000);
                Assert.InRange(analyzer.Process(samples, 48000, 1f / 60).Level, 0, 1);
            }
        }
    }

    [Fact]
    public void EmptyInputKeepsTheLatestSpectrum()
    {
        // Behaviour guard: an empty frame leaves the spectrum as it was. (The early-out itself only saves time.)
        var analyzer = new AudioFrontEnd();
        WarmUp(analyzer, 48000, 60);
        float[] before = analyzer.Spectrum.ToArray();
        analyzer.Process([], 48000, 1f / 60);
        Assert.Equal(before, analyzer.Spectrum.ToArray());
    }

    [Fact]
    public void SilenceHasZeroLevelBassAndSpectrum()
    {
        var analyzer = new AudioFrontEnd();
        for (int i = 0; i < 120; i++) Assert.Equal(default, analyzer.Process([], 48000, 1f / 60));
        Assert.All(analyzer.Spectrum.ToArray(), x => Assert.Equal(0, x));
    }

    [Fact]
    public void InvalidFrameArgumentsAreRejectedBeforeStateChanges()
    {
        var analyzer = new AudioFrontEnd();
        var samples = new float[800];
        FillSignal(samples, 0, 48000);
        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Process(samples, 0, 0));
        // Same rate as the comparison below, so no rate-change Reset could hide state a rejected call leaked.
        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Process(samples, 48000, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Process(samples, 48000, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Process(samples, 48000, float.PositiveInfinity));
        Assert.Equal(default, analyzer.Process([], 48000, 0));

        var fresh = new AudioFrontEnd();
        AssertSameSequence(fresh, analyzer, 48000, 120);
        Assert.Equal(fresh.Spectrum.ToArray(), analyzer.Spectrum.ToArray());
    }

    [Fact]
    public void InvalidTuningFailsAtConstruction()
    {
        var invalid = new AudioTuning[]
        {
            new() { WindowSize = 1000 },
            new() { PeakHalfLifeSeconds = 0 },
            new() { MagnitudeEpsilon = float.NaN },
            new() { FloorRiseDbPerSecond = -1 },
            new() { MinimumRangeDb = 0 },
            new() { LevelAttackSeconds = 0 },
            new() { LevelReleaseSeconds = float.PositiveInfinity },
            new() { BassAttackSeconds = -1 },
            new() { BassReleaseSeconds = 0 },
            new() { BassMinHz = 150 },
            new() { BassMaxHz = 2000 },
            new() { MidMaxHz = 12000 },
            new() { HighMaxHz = float.NaN },
            new() { LevelBassWeight = -0.5f },
            new() { LevelMidWeight = 2 },
            new() { LevelHighWeight = float.MaxValue },
            new() { SilenceThresholdDb = float.NaN },
            new() { SilenceThresholdDb = float.PositiveInfinity }
        };
        foreach (AudioTuning tuning in invalid)
            Assert.ThrowsAny<ArgumentException>(() => new AudioFrontEnd(tuning));
    }

    private static void WarmUp(AudioFrontEnd analyzer, int sampleRate, int frames)
    {
        var samples = new float[sampleRate / 60];
        for (int frame = 0; frame < frames; frame++)
        {
            FillSignal(samples, frame * samples.Length, sampleRate);
            analyzer.Process(samples, sampleRate, 1f / 60);
        }
    }

    private static void AssertSameSequence(AudioFrontEnd expected, AudioFrontEnd actual, int sampleRate, int frames)
    {
        var samples = new float[sampleRate / 60];
        bool sawSignal = false;
        for (int frame = 0; frame < frames; frame++)
        {
            FillSignal(samples, frame * samples.Length, sampleRate);
            AmplitudeFeatures e = expected.Process(samples, sampleRate, 1f / 60);
            Assert.Equal(e, actual.Process(samples, sampleRate, 1f / 60));
            sawSignal |= e.Level > 0.1f && e.Bass > 0.1f;
        }
        Assert.True(sawSignal, "the comparison must cover non-trivial Level/Bass values");
    }

    internal static void FillSignal(Span<float> samples, int start, int sampleRate)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (start + i) / (float)sampleRate;
            float amplitude = 0.1f + 0.9f * (0.5f + 0.5f * MathF.Sin(2 * MathF.PI * 2 * t));
            samples[i] = amplitude * (0.5f * MathF.Sin(2 * MathF.PI * 80 * t)
                + 0.3f * MathF.Sin(2 * MathF.PI * 700 * t) + 0.2f * MathF.Sin(2 * MathF.PI * 5000 * t));
        }
    }
}
