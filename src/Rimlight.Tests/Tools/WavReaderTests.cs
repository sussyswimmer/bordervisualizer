using System.Buffers.Binary;
using System.Text;
using Rimlight.AudioTools;
using Xunit;

namespace Rimlight.Tests.Tools;

// C3: tools/audio-tools WAV reader and writer. Byte-level fixtures are built by hand (WavBytes), independently of
// WavWriter, so a shared misunderstanding of the format can't make both sides agree.
public sealed class WavReaderTests
{
    [Fact]
    public void Pcm16StereoIsAveragedToMono()
    {
        byte[] data = WavBytes.Samples16([16384, -16384, 32767, 32767, -32768, 0, 100, 300]);
        byte[] file = WavBytes.File(WavBytes.Format(WavFormats.FormatPcm, channels: 2, sampleRate: 44100, bits: 16), data);

        WavAudio audio = WavReader.ReadMono(new MemoryStream(file));

        Assert.Equal(new WavInfo(44100, 2, WavSampleFormat.Pcm16, 16, false, 4), audio.Info);
        Assert.Equal([0f, 32767 / 32768f, -0.5f, 200 / 32768f], audio.Mono);
    }

    [Fact]
    public void Pcm24UsesTheSignOfTheTopByte()
    {
        byte[] data = [0xFF, 0xFF, 0x7F, 0x00, 0x00, 0x80, 0x01, 0x00, 0x00, 0xFF, 0xFF, 0xFF];
        byte[] file = WavBytes.File(WavBytes.Format(WavFormats.FormatPcm, channels: 1, sampleRate: 48000, bits: 24), data);

        WavAudio audio = WavReader.ReadMono(new MemoryStream(file));

        Assert.Equal(WavSampleFormat.Pcm24, audio.Info.Format);
        Assert.Equal([8388607 / 8388608f, -1f, 1 / 8388608f, -1 / 8388608f], audio.Mono);
    }

    [Fact]
    public void Pcm8IsUnsignedAroundOneTwentyEight()
    {
        byte[] file = WavBytes.File(WavBytes.Format(WavFormats.FormatPcm, channels: 1, sampleRate: 8000, bits: 8), [128, 255, 0, 64, 0]);

        WavAudio audio = WavReader.ReadMono(new MemoryStream(file));

        Assert.Equal(WavSampleFormat.Pcm8, audio.Info.Format);
        Assert.Equal(8000, audio.Info.SampleRate);
        Assert.Equal([0f, 127 / 128f, -1f, -0.5f, -1f], audio.Mono); // odd data size: the pad byte is not a sample
    }

    [Fact]
    public void Pcm32AndFloatFormatsDecode()
    {
        byte[] pcm32 = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(pcm32, int.MinValue);
        BinaryPrimitives.WriteInt32LittleEndian(pcm32.AsSpan(4), 1 << 30);
        Assert.Equal([-1f, 0.5f], WavReader.ReadMono(new MemoryStream(WavBytes.File(WavBytes.Format(WavFormats.FormatPcm, 1, 48000, 32), pcm32))).Mono);

        byte[] float32 = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(float32, 0.25f);
        BinaryPrimitives.WriteSingleLittleEndian(float32.AsSpan(4), -1.5f);
        WavAudio f32 = WavReader.ReadMono(new MemoryStream(WavBytes.File(WavBytes.Format(WavFormats.FormatIeeeFloat, 1, 48000, 32, cbSize: 0), float32)));
        Assert.Equal(WavSampleFormat.Float32, f32.Info.Format);
        Assert.Equal([0.25f, -1.5f], f32.Mono); // float WAVs may exceed full scale; the analyzer clamps

        byte[] float64 = new byte[16];
        BinaryPrimitives.WriteDoubleLittleEndian(float64, 0.125);
        BinaryPrimitives.WriteDoubleLittleEndian(float64.AsSpan(8), 0.375);
        WavAudio f64 = WavReader.ReadMono(new MemoryStream(WavBytes.File(WavBytes.Format(WavFormats.FormatIeeeFloat, 2, 96000, 64, cbSize: 0), float64)));
        Assert.Equal(WavSampleFormat.Float64, f64.Info.Format);
        Assert.Equal([0.25f], f64.Mono);
    }

    [Fact]
    public void ExtensibleFloatWithSixChannelsIsAveraged()
    {
        // WAVE_FORMAT_EXTENSIBLE, KSDATAFORMAT_SUBTYPE_IEEE_FLOAT, 5.1: one frame with channel c = c / 10.
        byte[] data = new byte[6 * 4];
        for (int c = 0; c < 6; c++) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(c * 4), c / 10f);
        byte[] file = WavBytes.File(WavBytes.Extensible(WavFormats.FormatIeeeFloat, channels: 6, sampleRate: 48000, containerBits: 32, validBits: 32, channelMask: 0x3F), data);

        WavAudio audio = WavReader.ReadMono(new MemoryStream(file));

        Assert.True(audio.Info.IsExtensible);
        Assert.Equal(WavSampleFormat.Float32, audio.Info.Format);
        Assert.Equal(6, audio.Info.Channels);
        Assert.Equal(0.25f, Assert.Single(audio.Mono), 6);
    }

    [Fact]
    public void ExtensibleTwentyFourBitsInAThirtyTwoBitContainerAreLeftJustified()
    {
        byte[] data = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(data, 0x400000 << 8);      // 0.5 in the top 24 bits
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), -0x200000 << 8);
        byte[] file = WavBytes.File(WavBytes.Extensible(WavFormats.FormatPcm, channels: 1, sampleRate: 192000, containerBits: 32, validBits: 24, channelMask: 0x4), data);

        WavAudio audio = WavReader.ReadMono(new MemoryStream(file));

        Assert.Equal(WavSampleFormat.Pcm32, audio.Info.Format);
        Assert.Equal(24, audio.Info.ValidBitsPerSample);
        Assert.Equal("192000 Hz, 1 ch, PCM 24-bit in 32-bit (extensible)", audio.Info.ToString());
        Assert.Equal([0.5f, -0.25f], audio.Mono);
    }

    [Fact]
    public void UnknownChunksAndOddPaddingAreSkipped()
    {
        byte[] fmt = WavBytes.Format(WavFormats.FormatPcm, 1, 22050, 16);
        byte[] file = WavBytes.Riff(
            WavBytes.Chunk("JUNK", [1, 2, 3]),        // odd size, padded
            WavBytes.Chunk("fmt ", fmt),
            WavBytes.Chunk("LIST", Encoding.ASCII.GetBytes("INFOISFT\u0005\0\0\0test\0")),
            WavBytes.Chunk("data", WavBytes.Samples16([8192, -8192])),
            WavBytes.Chunk("id3 ", [9, 9]));          // trailing chunks are ignored

        WavAudio audio = WavReader.ReadMono(new MemoryStream(file));

        Assert.Equal(22050, audio.Info.SampleRate);
        Assert.Equal([0.25f, -0.25f], audio.Mono);
    }

    [Fact]
    public void TruncatedDataReadsTheCompleteFrames()
    {
        byte[] file = WavBytes.File(WavBytes.Format(WavFormats.FormatPcm, 2, 48000, 16), WavBytes.Samples16([1000, 1000, 2000, 2000, 3000]));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(40), 4000); // the data chunk claims 4000 bytes

        WavAudio audio = WavReader.ReadMono(new MemoryStream(file));

        Assert.Equal(2, audio.Info.FrameCount);
        Assert.Equal([1000 / 32768f, 2000 / 32768f], audio.Mono);
    }

    [Fact]
    public void StreamedFileWithUnknownSizeIsReadToTheEnd()
    {
        int[] samples = Enumerable.Range(0, 5000).Select(i => i % 2000 - 1000).ToArray();
        byte[] file = WavBytes.File(WavBytes.Format(WavFormats.FormatPcm, 1, 48000, 16), WavBytes.Samples16(samples));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(40), uint.MaxValue);

        WavAudio audio = WavReader.ReadMono(new NonSeekableStream(file));

        Assert.Equal(samples.Select(s => s / 32768f), audio.Mono);
    }

    [Fact]
    public void Rf64TakesTheDataSizeFromItsDs64Chunk()
    {
        byte[] ds64 = new byte[28];
        BinaryPrimitives.WriteUInt64LittleEndian(ds64.AsSpan(8), 6); // data size: 3 samples of the 4 present
        byte[] file = WavBytes.Riff(
            WavBytes.Chunk("ds64", ds64),
            WavBytes.Chunk("fmt ", WavBytes.Format(WavFormats.FormatPcm, 1, 48000, 16)),
            WavBytes.Chunk("data", WavBytes.Samples16([4096, 8192, 16384, 1])));
        Encoding.ASCII.GetBytes("RF64", file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12 + 36 + 24 + 4), uint.MaxValue);

        Assert.Equal([0.125f, 0.25f, 0.5f], WavReader.ReadMono(new MemoryStream(file)).Mono);
    }

    [Theory]
    [InlineData(WavSampleFormat.Pcm8, 1, false)]
    [InlineData(WavSampleFormat.Pcm16, 1, false)]
    [InlineData(WavSampleFormat.Pcm16, 2, true)]
    [InlineData(WavSampleFormat.Pcm24, 2, false)]
    [InlineData(WavSampleFormat.Pcm24, 6, true)]
    [InlineData(WavSampleFormat.Pcm32, 2, false)]
    [InlineData(WavSampleFormat.Float32, 1, false)]
    [InlineData(WavSampleFormat.Float32, 8, true)]
    [InlineData(WavSampleFormat.Float64, 2, false)]
    public void WriterOutputReadsBackForEveryFormat(WavSampleFormat format, int channels, bool extensible)
    {
        // Channel c carries the signal plus 0.0125 × c, so the mono result is the signal plus the mean offset, within
        // one quantization step (or float rounding for the finer formats).
        const int frames = 2000;
        float[] interleaved = new float[frames * channels];
        float[] expected = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float x = 0.8f * MathF.Sin(i * 0.05f);
            for (int c = 0; c < channels; c++)
            {
                float v = x + 0.0125f * c;
                interleaved[i * channels + c] = v;
                expected[i] += v / channels;
            }
        }
        var stream = new MemoryStream();
        WavWriter.Write(stream, interleaved, 32000, channels, format, extensible);
        stream.Position = 0;

        WavAudio audio = WavReader.ReadMono(stream);

        Assert.Equal(new WavInfo(32000, channels, format, WavFormats.BytesPerSample(format) * 8, extensible, frames), audio.Info);
        double step = Math.Max(2e-6, format switch { WavSampleFormat.Pcm8 => 1 / 128.0, WavSampleFormat.Pcm16 => 1 / 32768.0, _ => 0 });
        for (int i = 0; i < frames; i++) Assert.True(Math.Abs(audio.Mono[i] - expected[i]) <= step, $"frame {i}: {audio.Mono[i]} vs {expected[i]}");
    }

    [Fact]
    public void WriterClampsIntegerFormatsAndWritesNaNAsZero()
    {
        var stream = new MemoryStream();
        WavWriter.Write(stream, [2f, -2f, float.NaN, 1f], 48000, 1, WavSampleFormat.Pcm16);
        stream.Position = 0;
        Assert.Equal([32767 / 32768f, -1f, 0f, 32767 / 32768f], WavReader.ReadMono(stream).Mono);
    }

    [Fact]
    public void UnsupportedFilesFailWithAClearMessage()
    {
        Assert.Contains("RIFF", Assert.Throws<InvalidDataException>(() => WavReader.ReadMono(new MemoryStream(Encoding.ASCII.GetBytes("ID3\u0004 not a wav file")))).Message);

        byte[] adpcm = WavBytes.File(WavBytes.Format(0x0002, 1, 48000, 4), [0, 0, 0, 0]);
        Assert.Contains("0x0002", Assert.Throws<InvalidDataException>(() => WavReader.ReadMono(new MemoryStream(adpcm))).Message);

        byte[] dataFirst = WavBytes.Riff(WavBytes.Chunk("data", [0, 0]), WavBytes.Chunk("fmt ", WavBytes.Format(WavFormats.FormatPcm, 1, 48000, 16)));
        Assert.Throws<InvalidDataException>(() => WavReader.ReadMono(new MemoryStream(dataFirst)));

        byte[] noData = WavBytes.Riff(WavBytes.Chunk("fmt ", WavBytes.Format(WavFormats.FormatPcm, 1, 48000, 16)));
        Assert.Contains("no data chunk", Assert.Throws<InvalidDataException>(() => WavReader.ReadMono(new MemoryStream(noData))).Message);
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        // Short reads, as pipes deliver them.
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 777));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

// Hand-assembled RIFF/WAVE bytes for the reader tests.
internal static class WavBytes
{
    public static byte[] Format(ushort tag, int channels, int sampleRate, int bits, int? cbSize = null)
    {
        var fmt = new byte[cbSize is null ? 16 : 18 + cbSize.Value];
        int bytes = (bits + 7) / 8;
        BinaryPrimitives.WriteUInt16LittleEndian(fmt, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(2), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(4), (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(8), (uint)(sampleRate * channels * bytes));
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(12), (ushort)(channels * bytes));
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(14), (ushort)bits);
        if (cbSize is not null) BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(16), (ushort)cbSize.Value);
        return fmt;
    }

    public static byte[] Extensible(ushort subFormat, int channels, int sampleRate, int containerBits, int validBits, uint channelMask)
    {
        byte[] fmt = Format(WavFormats.FormatExtensible, channels, sampleRate, containerBits, cbSize: 22);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(18), (ushort)validBits);
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(20), channelMask);
        // KSDATAFORMAT_SUBTYPE_PCM / _IEEE_FLOAT: {0000000X-0000-0010-8000-00AA00389B71}
        new Guid($"0000{subFormat:x4}-0000-0010-8000-00aa00389b71").TryWriteBytes(fmt.AsSpan(24));
        return fmt;
    }

    public static byte[] Samples16(int[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), checked((short)samples[i]));
        return bytes;
    }

    public static byte[] Chunk(string id, byte[] body)
    {
        var chunk = new byte[8 + body.Length + (body.Length & 1)];
        Encoding.ASCII.GetBytes(id, chunk);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)body.Length);
        body.CopyTo(chunk, 8);
        return chunk;
    }

    public static byte[] Riff(params byte[][] chunks)
    {
        byte[] body = chunks.SelectMany(c => c).ToArray();
        var file = new byte[12 + body.Length];
        Encoding.ASCII.GetBytes("RIFF", file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)(4 + body.Length));
        Encoding.ASCII.GetBytes("WAVE", file.AsSpan(8));
        body.CopyTo(file, 12);
        return file;
    }

    // fmt first, data second: the data chunk's size field is at byte 40 for a 16-byte fmt chunk.
    public static byte[] File(byte[] format, byte[] data) => Riff(Chunk("fmt ", format), Chunk("data", data));
}
