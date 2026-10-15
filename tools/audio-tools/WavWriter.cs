using System.Buffers.Binary;
using System.Text;

namespace Rimlight.AudioTools;

/// <summary>Writes interleaved float samples as a RIFF/WAVE file in any format <see cref="WavReader"/> reads.</summary>
public static class WavWriter
{
    private const int BufferBytes = 1 << 16;

    /// <summary>Writes a WAV file.</summary>
    /// <param name="path">The file to create or overwrite.</param>
    /// <param name="interleaved">Samples, frame by frame; integer formats clamp to [-1, 1) and write NaN as 0.</param>
    /// <param name="sampleRate">Frames per second, in Hz.</param>
    /// <param name="channels">Channels per frame.</param>
    /// <param name="format">The sample encoding.</param>
    /// <param name="extensible">Write a WAVE_FORMAT_EXTENSIBLE header instead of a plain PCM or float one.</param>
    public static void Write(string path, ReadOnlySpan<float> interleaved, int sampleRate, int channels, WavSampleFormat format, bool extensible = false)
    {
        using FileStream stream = File.Create(path);
        Write(stream, interleaved, sampleRate, channels, format, extensible);
    }

    /// <summary>Writes a WAV stream.</summary>
    /// <param name="stream">The destination.</param>
    /// <param name="interleaved">Samples, frame by frame; integer formats clamp to [-1, 1) and write NaN as 0.</param>
    /// <param name="sampleRate">Frames per second, in Hz.</param>
    /// <param name="channels">Channels per frame.</param>
    /// <param name="format">The sample encoding.</param>
    /// <param name="extensible">Write a WAVE_FORMAT_EXTENSIBLE header instead of a plain PCM or float one.</param>
    public static void Write(Stream stream, ReadOnlySpan<float> interleaved, int sampleRate, int channels, WavSampleFormat format, bool extensible = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, ushort.MaxValue);
        if (interleaved.Length % channels != 0) throw new ArgumentException("The sample count must be a whole number of frames.", nameof(interleaved));

        int sampleBytes = WavFormats.BytesPerSample(format);
        long dataBytes = (long)interleaved.Length * sampleBytes;
        int formatBytes = extensible ? 40 : WavFormats.IsFloat(format) ? 18 : 16;
        long riffBytes = 4 + 8 + formatBytes + 8 + dataBytes + (dataBytes & 1);
        if (riffBytes > uint.MaxValue) throw new ArgumentException("Too much audio for a 4 GB WAV file.", nameof(interleaved));
        if ((long)channels * sampleBytes > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(channels));

        Span<byte> header = stackalloc byte[12 + 8 + 40 + 8];
        int h = 0;
        h += WriteAscii(header[h..], "RIFF");
        BinaryPrimitives.WriteUInt32LittleEndian(header[h..], (uint)riffBytes); h += 4;
        h += WriteAscii(header[h..], "WAVE");
        h += WriteAscii(header[h..], "fmt ");
        BinaryPrimitives.WriteUInt32LittleEndian(header[h..], (uint)formatBytes); h += 4;
        ushort tag = WavFormats.IsFloat(format) ? WavFormats.FormatIeeeFloat : WavFormats.FormatPcm;
        BinaryPrimitives.WriteUInt16LittleEndian(header[h..], extensible ? WavFormats.FormatExtensible : tag); h += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(header[h..], (ushort)channels); h += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(header[h..], (uint)sampleRate); h += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(header[h..], (uint)Math.Min(uint.MaxValue, (long)sampleRate * channels * sampleBytes)); h += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(header[h..], (ushort)(channels * sampleBytes)); h += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(header[h..], (ushort)(sampleBytes * 8)); h += 2;
        if (formatBytes > 16)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header[h..], (ushort)(formatBytes - 18)); h += 2;
        }
        if (extensible)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header[h..], (ushort)(sampleBytes * 8)); h += 2;
            BinaryPrimitives.WriteUInt32LittleEndian(header[h..], ChannelMask(channels)); h += 4;
            BinaryPrimitives.WriteUInt16LittleEndian(header[h..], tag); h += 2;
            WavFormats.SubFormatTail.CopyTo(header[h..]); h += WavFormats.SubFormatTail.Length;
        }
        h += WriteAscii(header[h..], "data");
        BinaryPrimitives.WriteUInt32LittleEndian(header[h..], (uint)dataBytes); h += 4;
        stream.Write(header[..h]);

        byte[] buffer = new byte[BufferBytes - BufferBytes % sampleBytes];
        int used = 0;
        foreach (float sample in interleaved)
        {
            EncodeSample(sample, format, buffer.AsSpan(used, sampleBytes));
            used += sampleBytes;
            if (used == buffer.Length)
            {
                stream.Write(buffer, 0, used);
                used = 0;
            }
        }
        if (used > 0) stream.Write(buffer, 0, used);
        if ((dataBytes & 1) != 0) stream.WriteByte(0);
    }

    /// <summary>Copies a mono signal into every channel of an interleaved buffer.</summary>
    /// <param name="mono">The mono samples.</param>
    /// <param name="channels">Channels per frame.</param>
    /// <returns>The interleaved samples.</returns>
    public static float[] Interleave(ReadOnlySpan<float> mono, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        float[] interleaved = new float[(long)mono.Length * channels];
        for (int i = 0; i < mono.Length; i++)
            for (int c = 0; c < channels; c++) interleaved[i * channels + c] = mono[i];
        return interleaved;
    }

    /// <summary>Encodes one sample as little-endian bytes.</summary>
    /// <param name="sample">The value; integer formats clamp to [-1, 1) and encode NaN as 0.</param>
    /// <param name="format">The sample encoding.</param>
    /// <param name="destination">At least <see cref="WavFormats.BytesPerSample"/> bytes.</param>
    public static void EncodeSample(float sample, WavSampleFormat format, Span<byte> destination)
    {
        double x = float.IsNaN(sample) ? 0 : sample;
        switch (format)
        {
            case WavSampleFormat.Pcm8:
                destination[0] = (byte)(Quantize(x, 128) + 128);
                break;
            case WavSampleFormat.Pcm16:
                BinaryPrimitives.WriteInt16LittleEndian(destination, (short)Quantize(x, 32768));
                break;
            case WavSampleFormat.Pcm24:
                int v = (int)Quantize(x, 8388608);
                destination[0] = (byte)v;
                destination[1] = (byte)(v >> 8);
                destination[2] = (byte)(v >> 16);
                break;
            case WavSampleFormat.Pcm32:
                BinaryPrimitives.WriteInt32LittleEndian(destination, (int)Quantize(x, 2147483648.0));
                break;
            case WavSampleFormat.Float32:
                BinaryPrimitives.WriteSingleLittleEndian(destination, sample);
                break;
            case WavSampleFormat.Float64:
                BinaryPrimitives.WriteDoubleLittleEndian(destination, sample);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    // Full scale is ±scale, so decoding (÷ scale) returns every representable value exactly.
    private static long Quantize(double x, double scale) => (long)Math.Clamp(Math.Round(x * scale), -scale, scale - 1);

    private static uint ChannelMask(int channels) => channels switch
    {
        1 => 0x4,     // front center
        2 => 0x3,     // front left, front right
        4 => 0x33,    // quad
        6 => 0x3F,    // 5.1
        8 => 0x63F,   // 7.1
        _ => 0,       // unspecified
    };

    private static int WriteAscii(Span<byte> destination, string text) => Encoding.ASCII.GetBytes(text, destination);
}
