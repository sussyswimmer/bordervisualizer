using Rimlight.Core;
using Rimlight.Core.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

public sealed class ReviewRegressionTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(2048)]
    public void QuietestQuarterRmsHandlesSmallAndDefaultWindows(int size)
    {
        var spectrum = new SpectrumAnalyzer(size);
        var samples = new float[size];
        Array.Fill(samples, 0.25f);
        spectrum.Process(samples);
        Assert.Equal(0.25f, spectrum.QuietestQuarterRms);
        spectrum.Process(new float[size]);
        Assert.Equal(0f, spectrum.QuietestQuarterRms);
        spectrum.Reset();
        Assert.Equal(0f, spectrum.QuietestQuarterRms);
    }

    [Fact]
    public void SmallestWindowHonorsNearSilenceHold()
    {
        var analyzer = new AudioFrontEnd(new AudioTuning { WindowSize = 2 });
        // At a 100 Hz sample rate, the non-DC bin belongs to the bass band.
        // Both frames are below the -80 dBFS hold threshold and must not seed auto-gain.
        Assert.Equal(default, analyzer.Process([1e-6f, 1e-6f], 100, 1f / 60));
        Assert.Equal(default, analyzer.Process([5e-5f, 5e-5f], 100, 1f / 60));
    }
}
