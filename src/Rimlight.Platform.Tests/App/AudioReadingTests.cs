using Rimlight.App.Overlay;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Platform.Tests;

// AudioReading (K7): the analyzer's features packed into one long, so Settings' meter reads a consistent set.
public sealed class AudioReadingTests
{
    [Theory]
    [InlineData(0f, 0f, 0f, true, 1)]
    [InlineData(1f, 1f, 1f, false, 32767)]
    [InlineData(0.5f, 0.25f, 0.75f, false, 1234)]
    [InlineData(0.123456f, 0.654321f, 0.999f, true, 2)]
    public void FeaturesRoundTripTo1Over65535(float level, float bass, float beat, bool silent, int sequence)
    {
        var features = new AudioFeatures(level, bass, beat, silent);
        AudioReading reading = AudioReading.Unpack(AudioReading.Pack(in features, sequence));
        Assert.Equal(sequence, reading.Sequence);
        Assert.Equal(silent, reading.Features.IsSilent);
        Assert.InRange(MathF.Abs(reading.Features.Level - level), 0, 0.5f / 65535 + 1e-7f);
        Assert.InRange(MathF.Abs(reading.Features.Bass - bass), 0, 0.5f / 65535 + 1e-7f);
        Assert.InRange(MathF.Abs(reading.Features.Beat - beat), 0, 0.5f / 65535 + 1e-7f);
    }

    [Fact]
    public void OutOfRangeAndNonFiniteValuesAreClamped()
    {
        var features = new AudioFeatures(2f, -1f, float.NaN, false);
        AudioReading reading = AudioReading.Unpack(AudioReading.Pack(in features, 5));
        Assert.Equal(1f, reading.Features.Level);
        Assert.Equal(0f, reading.Features.Bass);
        Assert.Equal(0f, reading.Features.Beat);
    }

    [Fact]
    public void ZeroIsNoFrameYetAndTheCounterNeverMakesZero()
    {
        Assert.Equal(AudioReading.None, AudioReading.Unpack(0));
        Assert.Equal(-1, AudioReading.None.Sequence);
        Assert.True(AudioReading.None.Features.IsSilent);

        // Silence with every feature at 0 still packs to non-zero, because the counter is never 0.
        var silence = new AudioFeatures(0, 0, 0, false);
        int sequence = 0;
        for (int i = 0; i < 70_000; i++)
        {
            sequence = AudioReading.Next(sequence);
            Assert.InRange(sequence, 1, 32767);
            Assert.NotEqual(0, AudioReading.Pack(in silence, sequence));
        }
        Assert.Equal(1, AudioReading.Next(32767));
        Assert.Equal(1, AudioReading.Next(0));
    }

    [Fact]
    public void AReaderOnAnotherThreadNeverSeesATornReading()
    {
        // The overlay thread publishes Level = Bass = Beat = the counter's share; the UI thread must always read a set
        // that belongs together.
        long packed = 0;
        bool done = false;
        var writer = new Thread(() =>
        {
            int sequence = 0;
            for (int i = 0; i < 2_000_000; i++)
            {
                sequence = AudioReading.Next(sequence);
                float value = sequence / 32767f;
                var features = new AudioFeatures(value, value, value, sequence % 2 == 0);
                Volatile.Write(ref packed, AudioReading.Pack(in features, sequence));
            }
            Volatile.Write(ref done, true);
        });
        writer.Start();
        long reads = 0, torn = 0;
        while (!Volatile.Read(ref done))
        {
            AudioReading reading = AudioReading.Unpack(Volatile.Read(ref packed));
            reads++;
            if (reading.Sequence < 0) continue;
            float expected = reading.Sequence / 32767f;
            if (MathF.Abs(reading.Features.Level - expected) > 1e-4f || reading.Features.Level != reading.Features.Bass
                || reading.Features.Bass != reading.Features.Beat || reading.Features.IsSilent != (reading.Sequence % 2 == 0))
                torn++;
        }
        writer.Join();
        Assert.Equal(0, torn);
        Assert.True(reads > 0);
    }
}
