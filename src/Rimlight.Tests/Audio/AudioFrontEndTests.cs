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
    public void ResetAndSampleRateChangeMatchFreshAnalyzer()
    {
        var analyzer = new AudioFrontEnd();
        var samples = new float[800];
        FillSignal(samples, 0, 48000);
        analyzer.Process(samples, 48000, 0.1f);
        analyzer.Reset();
        var fresh = new AudioFrontEnd();
        Assert.Equal(fresh.Process(samples, 48000, 0.1f), analyzer.Process(samples, 48000, 0.1f));
        fresh.Reset();
        Assert.Equal(fresh.Process(samples, 44100, 0.1f), analyzer.Process(samples, 44100, 0.1f));
        Assert.Equal(fresh.Spectrum.ToArray(), analyzer.Spectrum.ToArray());
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
        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Process([], 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Process([], 48000, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Process([], 48000, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Process([], 48000, float.PositiveInfinity));
        Assert.Equal(default, analyzer.Process([], 48000, 0));
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
            new() { LevelHighWeight = float.MaxValue }
        };
        foreach (AudioTuning tuning in invalid)
            Assert.ThrowsAny<ArgumentException>(() => new AudioFrontEnd(tuning));
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
