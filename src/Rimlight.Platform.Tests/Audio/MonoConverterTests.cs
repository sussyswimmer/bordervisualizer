using System.Buffers.Binary;
using Rimlight.Platform.Audio;
using Xunit;

namespace Rimlight.Platform.Tests;

// MonoConverter (K2): WASAPI's interleaved little-endian mix format to mono floats, averaging the channels (doc 03 §1).
public sealed class MonoConverterTests
{
    [Fact]
    public void Float32StereoIsAveragedExactly()
    {
        byte[] data = Floats(0.5f, -0.25f, 1f, 1f, -1f, 0f);
        float[] mono = new float[3];
        Assert.Equal(3, MonoConverter.Convert(data, new CaptureFormat(SampleEncoding.Float32, 2, 48000), mono));
        Assert.Equal(new[] { 0.125f, 1f, -0.5f }, mono);
    }

    [Fact]
    public void Int16IsScaledByItsFullRange()
    {
        byte[] data = new byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0), short.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), short.MinValue);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 16384);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(6), 16384);
        float[] mono = new float[2];
        Assert.Equal(2, MonoConverter.Convert(data, new CaptureFormat(SampleEncoding.Int16, 2, 44100), mono));
        Assert.Equal(-0.5f / 32768, mono[0]);   // (32767 − 32768) / 2 / 32768
        Assert.Equal(0.5f, mono[1]);
    }

    [Fact]
    public void Int24IsSignExtended()
    {
        // Three bytes per sample: 0x7FFFFF, 0x800000 (most negative), 0xFFFFFF (−1), 0x400000.
        byte[] data = [0xFF, 0xFF, 0x7F, 0x00, 0x00, 0x80, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x40];
        float[] mono = new float[4];
        Assert.Equal(4, MonoConverter.Convert(data, new CaptureFormat(SampleEncoding.Int24, 1, 48000), mono));
        Assert.Equal(8388607f / 8388608f, mono[0]);
        Assert.Equal(-1f, mono[1]);
        Assert.Equal(-1f / 8388608f, mono[2]);
        Assert.Equal(0.5f, mono[3]);
    }

    [Fact]
    public void Int32UsesItsWholeRange()
    {
        byte[] data = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), int.MinValue);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), int.MinValue);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 1 << 30);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), -(1 << 30));
        float[] mono = new float[2];
        Assert.Equal(2, MonoConverter.Convert(data, new CaptureFormat(SampleEncoding.Int32, 2, 48000), mono));
        Assert.Equal(-1f, mono[0]); // no overflow summing two most-negative samples
        Assert.Equal(0f, mono[1]);
    }

    [Fact]
    public void FivePointOneAveragesAllSixChannels()
    {
        byte[] data = Floats(0.6f, 0.6f, 0f, 0f, 0f, 0f, /* frame 2 */ 1f, 1f, 1f, 1f, 1f, 1f);
        float[] mono = new float[2];
        Assert.Equal(2, MonoConverter.Convert(data, new CaptureFormat(SampleEncoding.Float32, 6, 48000), mono));
        Assert.Equal(0.2f, mono[0], 6);
        Assert.Equal(1f, mono[1], 6);
    }

    [Fact]
    public void OnlyWholeFramesThatFitAreConverted()
    {
        var format = new CaptureFormat(SampleEncoding.Int16, 2, 48000);
        float[] mono = new float[10];
        Assert.Equal(2, MonoConverter.Convert(new byte[11], format, mono)); // 11 bytes: two 4-byte frames and a part
        Assert.Equal(3, MonoConverter.Convert(new byte[40], format, mono.AsSpan(0, 3))); // limited by the destination
        Assert.Equal(0, MonoConverter.Convert([], format, mono));
        Assert.Equal(0, MonoConverter.Convert(new byte[8], new CaptureFormat(SampleEncoding.Float32, 0, 48000), mono));
    }

    [Theory]
    [InlineData("Float32", 2, "48000 Hz, 2 ch, 32-bit float", 8)]
    [InlineData("Int16", 2, "48000 Hz, 2 ch, 16-bit PCM", 4)]
    [InlineData("Int24", 6, "48000 Hz, 6 ch, 24-bit PCM", 18)]
    [InlineData("Int32", 1, "48000 Hz, 1 ch, 32-bit PCM", 4)]
    public void TheFormatDescribesItself(string encoding, int channels, string text, int bytesPerFrame)
    {
        var format = new CaptureFormat(Enum.Parse<SampleEncoding>(encoding), channels, 48000);
        Assert.Equal(text, format.ToString());
        Assert.Equal(bytesPerFrame, format.BytesPerFrame);
    }

    [Fact]
    public void ConversionAllocatesNothing()
    {
        byte[] data = new byte[480 * 8];
        float[] mono = new float[480];
        var format = new CaptureFormat(SampleEncoding.Float32, 2, 48000);
        MonoConverter.Convert(data, format, mono);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) MonoConverter.Convert(data, format, mono);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static byte[] Floats(params float[] values)
    {
        byte[] bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }
}
