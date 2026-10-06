using Rimlight.Core.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Audio;

public sealed class FftTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    public void TransformMatchesIndependentDft(int size)
    {
        var random = new Random(42);
        float[] input = Enumerable.Range(0, size).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
        float[] real = new float[size], imaginary = new float[size];
        new Fft(size).Transform(input, real, imaginary);
        double maxError = 0;
        for (int k = 0; k < size; k++)
        {
            double expectedReal = 0, expectedImaginary = 0;
            for (int n = 0; n < size; n++)
            {
                double angle = -2 * Math.PI * k * n / size;
                expectedReal += input[n] * Math.Cos(angle);
                expectedImaginary += input[n] * Math.Sin(angle);
            }
            maxError = Math.Max(maxError, Math.Max(Math.Abs(real[k] - expectedReal), Math.Abs(imaginary[k] - expectedImaginary)));
            Assert.InRange(Math.Abs(real[k] - expectedReal), 0, 0.00002);
            Assert.InRange(Math.Abs(imaginary[k] - expectedImaginary), 0, 0.00002);
        }
        output.WriteLine($"N={size}: max complex-component DFT error = {maxError:E6}");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(64)]
    [InlineData(511)]
    public void HannSinePeaksAtCorrectBinAndHasExpectedAmplitude(int bin)
    {
        const int size = 2048;
        float[] input = Enumerable.Range(0, size).Select(i => MathF.Sin(2 * MathF.PI * bin * i / size)).ToArray();
        var analyzer = new SpectrumAnalyzer(size);
        analyzer.Process(input);
        float[] spectrum = analyzer.Magnitudes.ToArray();
        int peak = Array.IndexOf(spectrum, spectrum.Max());
        Assert.Equal(bin, peak);
        Assert.InRange(spectrum[bin], size / 4f - 0.1f, size / 4f + 0.1f);
        Assert.InRange(spectrum[bin - 1], size / 8f - 0.1f, size / 8f + 0.1f);
        Assert.InRange(spectrum[bin + 1], size / 8f - 0.1f, size / 8f + 0.1f);
    }

    [Fact]
    public void OneSidedWindowedSpectrumObeysParsevalIncludingDcAndNyquist()
    {
        const int size = 2048;
        var random = new Random(71);
        var input = new float[size];
        double timeEnergy = 0;
        for (int i = 0; i < size; i++)
        {
            input[i] = 0.3f + (i % 2 == 0 ? 0.2f : -0.2f) + (float)random.NextDouble() - 0.5f;
            double windowed = input[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size));
            timeEnergy += windowed * windowed;
        }
        var analyzer = new SpectrumAnalyzer(size);
        analyzer.Process(input);
        ReadOnlySpan<float> spectrum = analyzer.Magnitudes;
        double spectralEnergy = (double)spectrum[0] * spectrum[0] + (double)spectrum[^1] * spectrum[^1];
        for (int k = 1; k < size / 2; k++) spectralEnergy += 2d * spectrum[k] * spectrum[k];
        double relativeError = Math.Abs(spectralEnergy / size / timeEnergy - 1);
        Assert.InRange(relativeError, 0, 0.00001);
        output.WriteLine($"N={size}: Parseval relative error = {relativeError:E6}");
    }

    [Fact]
    public void HannIsPeriodicAndPrecomputed()
    {
        var window = new HannWindow(2048);
        Assert.Equal(0f, window.Coefficients[0]);
        Assert.Equal(1f, window.Coefficients[1024]);
        for (int i = 1; i < 2048; i++)
            Assert.InRange(MathF.Abs(window.Coefficients[i] - window.Coefficients[2048 - i]), 0, 0.000001f);
    }

    [Fact]
    public void IrregularChunksAndOversizedInputsKeepOnlyNewestWindow()
    {
        const int size = 2048;
        float[] input = Enumerable.Range(0, size * 3 + 123).Select(i => MathF.Sin(i * 0.071f)).ToArray();
        var expected = new SpectrumAnalyzer(size);
        expected.Process(input.AsSpan(input.Length - size));
        var chunked = new SpectrumAnalyzer(size);
        int offset = 0;
        foreach (int count in new[] { 1, 799, 2049, 3, input.Length - 2852 })
        {
            chunked.Process(input.AsSpan(offset, count));
            offset += count;
        }
        Assert.Equal(expected.Magnitudes.ToArray(), chunked.Magnitudes.ToArray());
        var oversized = new SpectrumAnalyzer(size);
        oversized.Process(input);
        Assert.Equal(expected.Magnitudes.ToArray(), oversized.Magnitudes.ToArray());
        oversized.Process([]);
        Assert.Equal(expected.Magnitudes.ToArray(), oversized.Magnitudes.ToArray());
    }

    [Fact]
    public void StartupPadsOnLeftAndResetClearsHistory()
    {
        var analyzer = new SpectrumAnalyzer(8);
        var padded = new SpectrumAnalyzer(8);
        analyzer.Process([1, 2, 3]);
        padded.Process([0, 0, 0, 0, 0, 1, 2, 3]);
        Assert.Equal(padded.Magnitudes.ToArray(), analyzer.Magnitudes.ToArray());
        analyzer.Reset();
        Assert.All(analyzer.Magnitudes.ToArray(), x => Assert.Equal(0f, x));
        analyzer.Process([]);
        Assert.All(analyzer.Magnitudes.ToArray(), x => Assert.Equal(0f, x));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    public void InvalidFftSizeIsRejected(int size) => Assert.Throws<ArgumentOutOfRangeException>(() => new Fft(size));
}
