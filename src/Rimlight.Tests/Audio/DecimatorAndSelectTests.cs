using Rimlight.Core.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

public sealed class DecimatorAndSelectTests
{
    [Theory]
    [InlineData(44100, 1)]
    [InlineData(48000, 1)]
    [InlineData(74999, 1)]
    [InlineData(75000, 2)]
    [InlineData(96000, 2)]
    [InlineData(149999, 2)]
    [InlineData(150000, 4)]
    [InlineData(192000, 4)]
    [InlineData(300000, 8)]
    [InlineData(384000, 8)]
    public void FactorKeepsTheAnalysisRateNearCdRates(int sampleRate, int factor) => Assert.Equal(factor, Decimator.FactorFor(sampleRate));

    [Theory]
    [InlineData(96000)]
    [InlineData(192000)]
    [InlineData(384000)]
    public void PassbandIsFlatAndAliasesAreRejected(int sampleRate)
    {
        int factor = Decimator.FactorFor(sampleRate), outputRate = sampleRate / factor;
        // In band: 60 Hz (kick) and 10 kHz (top of the High band) pass at unity within 0.1 dB.
        Assert.InRange(Gain(sampleRate, 60), 0.9886f, 1.0116f);
        Assert.InRange(Gain(sampleRate, 10000), 0.9886f, 1.0116f);
        // Content that would fold into 0–12 kHz after decimation is at least 50 dB down.
        foreach (float alias in new[] { outputRate - 6000f, outputRate + 1000f, outputRate * 1.5f })
            if (alias < sampleRate / 2f) Assert.True(Gain(sampleRate, alias) < 0.00316f, $"{alias} Hz leaks {Gain(sampleRate, alias)}");
    }

    [Fact]
    public void DcPassesAtUnityAndOutputCountFollowsTheFactor()
    {
        var decimator = new Decimator();
        decimator.Configure(192000);
        var input = new float[10_001];
        Array.Fill(input, 0.25f);
        var output = new float[input.Length];
        int first = decimator.Process(input.AsSpan(0, 3), output);
        int rest = decimator.Process(input.AsSpan(3), output);
        Assert.Equal(0, first);
        Assert.Equal(2500, rest);
        Assert.InRange(output[rest - 1], 0.2499f, 0.2501f);

        decimator.Configure(48000);
        Assert.Equal(5, decimator.Process(input.AsSpan(0, 5), output));
    }

    [Fact]
    public void SelectMatchesASortedReference()
    {
        var random = new Random(15);
        for (int trial = 0; trial < 300; trial++)
        {
            int length = random.Next(1, 200);
            // Many duplicates and runs of zeros, like a flux history during silence.
            float[] values = Enumerable.Range(0, length).Select(_ => random.Next(4) == 0 ? 0 : MathF.Round((float)random.NextDouble() * 10, 1)).ToArray();
            float[] sorted = values.OrderBy(x => x).ToArray();
            int k = random.Next(length);
            float[] work = (float[])values.Clone();
            Assert.Equal(sorted[k], BeatDetector.Select(work, k));
            for (int i = 0; i < k; i++) Assert.True(work[i] <= work[k]);
            for (int i = k + 1; i < length; i++) Assert.True(work[i] >= work[k]);
            Assert.Equal(sorted, work.OrderBy(x => x).ToArray());
        }
    }

    // Steady-state amplitude (√2 × RMS) of a decimated unit sine, measured after the filter has settled.
    private static float Gain(int sampleRate, float frequency)
    {
        var decimator = new Decimator();
        decimator.Configure(sampleRate);
        int length = sampleRate / 2;
        var input = new float[length];
        for (int i = 0; i < length; i++) input[i] = MathF.Sin((float)(2 * Math.PI * frequency * i / sampleRate));
        var output = new float[length];
        int count = decimator.Process(input, output);
        double squares = 0;
        for (int i = count / 2; i < count; i++) squares += output[i] * output[i];
        return (float)Math.Sqrt(2 * squares / (count - count / 2));
    }
}
