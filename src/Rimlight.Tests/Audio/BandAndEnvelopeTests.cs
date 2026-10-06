using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

public sealed class BandAndEnvelopeTests
{
    [Fact]
    public void BandEdgesAreNotDoubleCountedAndEnergyIsRms()
    {
        // Ten Hz per bin: ranges are [30,150), [150,2000), [2000,12000].
        var spectrum = new float[2049];
        spectrum[2] = 1000; // Below the bass range.
        spectrum[3] = 3;
        spectrum[14] = 4;
        spectrum[15] = 6;
        spectrum[199] = 8;
        spectrum[200] = 12;
        spectrum[1200] = 5;
        spectrum[1201] = 1000; // Above the high range.
        BandEnergies bands = BandAnalyzer.Analyze(spectrum, 4096, 40960, new AudioTuning());
        Assert.InRange(MathF.Abs(bands.Bass - MathF.Sqrt(25f / 12)), 0, 0.000001f);
        Assert.InRange(MathF.Abs(bands.Mid - MathF.Sqrt(100f / 185)), 0, 0.000001f);
        Assert.InRange(MathF.Abs(bands.High - MathF.Sqrt(169f / 1001)), 0, 0.000001f);
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    public void BandsUseActualSampleRateAndClampToNyquist(int sampleRate)
    {
        var spectrum = new float[1025];
        int bin = (int)MathF.Round(1000f * 2048 / sampleRate);
        spectrum[bin] = 1;
        BandEnergies bands = BandAnalyzer.Analyze(spectrum, 2048, sampleRate, new AudioTuning());
        Assert.Equal(0, bands.Bass);
        Assert.True(bands.Mid > 0);
        Assert.Equal(0, bands.High);
    }

    [Fact]
    public void EmptyBandsAtLowSampleRateReturnZero()
    {
        BandEnergies bands = BandAnalyzer.Analyze(Enumerable.Repeat(1f, 1025).ToArray(), 2048, 40, new AudioTuning());
        Assert.Equal(default, bands);
    }

    [Theory]
    [InlineData(41f, 49f, 4)]    // 10 Hz bins: no bin in [41, 49), so the bin below the 45 Hz center
    [InlineData(31f, 39f, 3)]
    [InlineData(1f, 9f, 1)]      // never DC
    public void BassBandNarrowerThanABinUsesTheBinBelowItsCenter(float low, float high, int bin)
    {
        var tuning = new AudioTuning { BassMinHz = low, BassMaxHz = high };
        Assert.Equal((bin, bin + 1), BandAnalyzer.BassBins(2049, 10, tuning));
        var spectrum = new float[2049];
        spectrum[bin] = 2;
        spectrum[bin + 1] = 100; // the Mid band's first bin is not shared
        Assert.Equal(2, BandAnalyzer.Analyze(spectrum, 4096, 40960, tuning).Bass);
        // Wide bands and bands above Nyquist are unchanged.
        Assert.Equal((3, 15), BandAnalyzer.BassBins(2049, 10, new AudioTuning()));
        Assert.Equal((1025, 1025), BandAnalyzer.BassBins(1025, 0.02f, new AudioTuning()));
    }

    [Fact]
    public void BandAboveNyquistUsesOnlyAvailableBinsInItsRms()
    {
        // At 8 kHz, high covers bins 512..1024 inclusive (513 bins).
        var spectrum = new float[1025];
        spectrum[1024] = 3;
        BandEnergies bands = BandAnalyzer.Analyze(spectrum, 2048, 8000, new AudioTuning());
        Assert.InRange(MathF.Abs(bands.High - 3 / MathF.Sqrt(513)), 0, 0.000001f);
    }

    [Fact]
    public void AutoGainResetMatchesFreshInstance()
    {
        var tuning = new AudioTuning();
        var gain = new AutoGain();
        gain.Update(10, 1, tuning);
        gain.Update(0.01f, 1, tuning);
        gain.Reset();
        var fresh = new AutoGain();
        foreach (float amplitude in new[] { 0f, 0.1f, 1f, 0.4f })
            Assert.Equal(fresh.Update(amplitude, 0.02f, tuning), gain.Update(amplitude, 0.02f, tuning));
    }

    [Theory]
    [InlineData(0.030f, 0.250f)]
    [InlineData(0.015f, 0.180f)]
    public void EnvelopeReachesOneTimeConstantInBothDirections(float attack, float release)
    {
        var envelope = new Envelope();
        for (int i = 0; i < 100; i++) envelope.Update(1, attack / 100, attack, release);
        Assert.InRange(envelope.Value, 0.63212f - 0.0001f, 0.63212f + 0.0001f);
        envelope.Reset(1);
        for (int i = 0; i < 100; i++) envelope.Update(0, release / 100, attack, release);
        Assert.InRange(envelope.Value, 0.367879f - 0.0001f, 0.367879f + 0.0001f);
        float previous = envelope.Value;
        envelope.Update(1, 0, attack, release);
        Assert.Equal(previous, envelope.Value);
    }

    [Fact]
    public void AutoGainHoldFreezesTheFloorWhileThePeakKeepsDecaying()
    {
        var gain = new AutoGain();
        var tuning = new AudioTuning();
        gain.Update(10f, 1f / 60, tuning);
        gain.Update(2f, 1f / 60, tuning);
        float floor = gain.FloorDb;
        float peak = gain.Peak;

        Assert.Equal(0f, gain.Update(0f, 4, tuning));                // digital zero: one half-life
        Assert.InRange(gain.Peak, peak * 0.5f * 0.9999f, peak * 0.5f * 1.0001f);
        Assert.Equal(0f, gain.Update(1e-6f, 4, tuning, hold: true)); // near-silence
        Assert.Equal(0f, gain.Update(float.NaN, 4, tuning));         // non-finite counts as 0
        Assert.InRange(gain.Peak, peak * 0.125f * 0.9999f, peak * 0.125f * 1.0001f);
        Assert.Equal(floor, gain.FloorDb);

        gain.Update(100f, 1f / 60, tuning, hold: true);              // a loud held frame still lifts the peak
        Assert.Equal(100f, gain.Peak);
        Assert.Equal(floor, gain.FloorDb);

        var fresh = new AutoGain();                                  // leading near-silence does not seed state
        fresh.Update(5f, 1, tuning, hold: true);
        Assert.Equal((0f, 0f), (fresh.Peak, fresh.FloorDb));
    }

    [Fact]
    public void EnvelopeIgnoresNonFiniteTargets()
    {
        var envelope = new Envelope();
        envelope.Update(1, 0.1f, 0.03f, 0.25f);
        float value = envelope.Value;
        Assert.Equal(value, envelope.Update(float.NaN, 0.1f, 0.03f, 0.25f));
        Assert.Equal(value, envelope.Update(float.PositiveInfinity, 0.1f, 0.03f, 0.25f));
    }

    [Fact]
    public void AutoGainPeakHalvesInFourSecondsAndFloorRisesOneDbPerSecond()
    {
        var gain = new AutoGain();
        var tuning = new AudioTuning();
        gain.Update(1f, 0, tuning);
        gain.Update(0.001f, 0, tuning);
        float floor = gain.FloorDb;
        gain.Update(0.1f, 4, tuning);
        Assert.InRange(gain.Peak, 0.49999f, 0.50001f);
        Assert.InRange(gain.FloorDb - floor, 3.9999f, 4.0001f);
        gain.Update(0.0001f, 0, tuning);
        Assert.InRange(gain.FloorDb, -80.001f, -79.999f);
    }

    [Fact]
    public void AutoGainUsesDbAndMinimumTwelveDbRange()
    {
        var gain = new AutoGain();
        var tuning = new AudioTuning();
        gain.Update(0.1f, 0, tuning);
        float norm = gain.Update(MathF.Pow(10f, -14f / 20), 0, tuning);
        Assert.InRange(norm, 0.49999f, 0.50001f);
        Assert.Equal(0f, new AutoGain().Update(0, 1, tuning));
    }

    [Fact]
    public void AutoGainIsVolumeInvariantAfterTwoSecondsAndNotTriviallyZero()
    {
        var quiet = new AutoGain();
        var loud = new AutoGain();
        var tuning = new AudioTuning();
        float largestOutput = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            float amplitude = 0.1f + 0.9f * (0.5f + 0.5f * MathF.Sin(frame * 0.15f));
            float expected = loud.Update(amplitude, 1f / 60, tuning);
            float actual = quiet.Update(amplitude * 0.1f, 1f / 60, tuning);
            if (frame >= 120)
            {
                Assert.InRange(MathF.Abs(expected - actual), 0, 0.00001f);
                largestOutput = MathF.Max(largestOutput, expected);
            }
        }
        Assert.True(largestOutput > 0.9f);
    }
}
