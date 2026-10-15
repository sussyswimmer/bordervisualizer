using System.Buffers.Binary;
using Rimlight.Core;
using Rimlight.Platform.Audio;
using Xunit;

namespace Rimlight.Tests.Audio;

// Platform's MonoConverter (doc 03 §1), compiled into this project from its source. Windows plays a stereo stream on
// a 5.1 or 7.1 mix format through FL/FR only, with every other channel zero.
public sealed class MonoConverterTests
{
    private const int Rate = 48000;
    private const int FramesPerTick = Rate / 60;

    [Theory]
    [InlineData(nameof(SampleEncoding.Float32), 2)]
    [InlineData(nameof(SampleEncoding.Float32), 6)]
    [InlineData(nameof(SampleEncoding.Float32), 8)]
    [InlineData(nameof(SampleEncoding.Int16), 2)]
    [InlineData(nameof(SampleEncoding.Int16), 6)]
    [InlineData(nameof(SampleEncoding.Int16), 8)]
    [InlineData(nameof(SampleEncoding.Int24), 2)]
    [InlineData(nameof(SampleEncoding.Int24), 6)]
    [InlineData(nameof(SampleEncoding.Int24), 8)]
    [InlineData(nameof(SampleEncoding.Int32), 2)]
    [InlineData(nameof(SampleEncoding.Int32), 6)]
    [InlineData(nameof(SampleEncoding.Int32), 8)]
    public void StereoContentGivesTheSameMonoOnEveryChannelCount(string encodingName, int channels)
    {
        var encoding = Enum.Parse<SampleEncoding>(encodingName);
        var random = new Random(7);
        var left = new float[480];
        var right = new float[480];
        for (int i = 0; i < left.Length; i++)
        {
            left[i] = (float)(random.NextDouble() * 1.8 - 0.9);
            right[i] = (float)(random.NextDouble() * 1.8 - 0.9);
        }

        float[] stereo = ToMono(left, right, new CaptureFormat(encoding, 2, Rate));
        float[] surround = ToMono(left, right, new CaptureFormat(encoding, channels, Rate));

        Assert.Equal(stereo, surround);
        for (int i = 0; i < left.Length; i++) Assert.Equal((left[i] + right[i]) / 2, surround[i], 1e-4f);
    }

    [Fact]
    public void MonoEndpointPassesSamplesThrough()
    {
        float[] samples = [0.5f, -0.25f, 1f, -1f, 0.125f];
        var bytes = new byte[samples.Length * 4];
        for (int i = 0; i < samples.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), samples[i]);
        var mono = new float[samples.Length];
        Assert.Equal(samples.Length, MonoConverter.Convert(bytes, new CaptureFormat(SampleEncoding.Float32, 1, Rate), mono));
        Assert.Equal(samples, mono);
    }

    // Doc 03 §2's silence thresholds are absolute dBFS (enter below -60 for 2 s, exit above -55), so quiet stereo music
    // must read the same on a 7.1 headset or a 5.1 receiver as on stereo headphones.
    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(8)]
    public void QuietStereoMusicOnASurroundMixIsNotSilence(int channels)
    {
        IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer();
        var format = new CaptureFormat(SampleEncoding.Float32, channels, Rate);
        var packet = new byte[FramesPerTick * format.BytesPerFrame];
        var mono = new float[FramesPerTick];
        long position = 0;

        // Plays a 220 Hz tone at the given RMS on FL and FR, one 60 fps frame at a time, and counts the silent frames.
        (int SilentFrames, bool SilentAtEnd) Play(float seconds, float dbfs)
        {
            double amplitude = Math.Sqrt(2) * Math.Pow(10, dbfs / 20.0);
            int silentFrames = 0;
            bool silent = false;
            for (int frame = 0; frame < seconds * 60; frame++)
            {
                for (int i = 0; i < FramesPerTick; i++, position++)
                {
                    float x = (float)(amplitude * Math.Sin(2 * Math.PI * 220 * position / Rate));
                    BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(i * format.BytesPerFrame), x);
                    BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(i * format.BytesPerFrame + 4), x);
                }
                int converted = MonoConverter.Convert(packet, format, mono);
                silent = analyzer.Process(mono.AsSpan(0, converted), Rate, 1f / 60).IsSilent;
                if (silent) silentFrames++;
            }
            return (silentFrames, silent);
        }

        // A quiet passage at -53 dBFS never enters silence (averaging 6 or 8 channels read it as -62.5 or -65).
        Assert.Equal(0, Play(4, -53).SilentFrames);
        // A gap between tracks does.
        Assert.True(Play(3, float.NegativeInfinity).SilentAtEnd);
        // A quiet intro at -50 dBFS leaves it on the first frame (averaging read it as -59.5 or -62 and stayed silent).
        Assert.Equal(0, Play(1, -50).SilentFrames);
    }

    private static float[] ToMono(float[] left, float[] right, CaptureFormat format)
    {
        var bytes = new byte[left.Length * format.BytesPerFrame];
        for (int i = 0; i < left.Length; i++)
        {
            Write(bytes.AsSpan(i * format.BytesPerFrame), left[i], format.Encoding);
            Write(bytes.AsSpan(i * format.BytesPerFrame + format.BytesPerSample), right[i], format.Encoding);
        }
        var mono = new float[left.Length];
        Assert.Equal(left.Length, MonoConverter.Convert(bytes, format, mono));
        return mono;
    }

    private static void Write(Span<byte> destination, float sample, SampleEncoding encoding)
    {
        switch (encoding)
        {
            case SampleEncoding.Float32:
                BinaryPrimitives.WriteSingleLittleEndian(destination, sample);
                break;
            case SampleEncoding.Int16:
                BinaryPrimitives.WriteInt16LittleEndian(destination, (short)MathF.Round(sample * 32767));
                break;
            case SampleEncoding.Int24:
                int value = (int)MathF.Round(sample * 8388607);
                destination[0] = (byte)value;
                destination[1] = (byte)(value >> 8);
                destination[2] = (byte)(value >> 16);
                break;
            case SampleEncoding.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(destination, (int)Math.Round(sample * 2147483647.0));
                break;
        }
    }
}
