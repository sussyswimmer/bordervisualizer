using System.Buffers.Binary;
using System.Text;

namespace Rimlight.AudioTools;

/// <summary>
/// Reads RIFF/WAVE files into mono float samples: 8/16/24/32-bit integer PCM and 32/64-bit IEEE float, plain or
/// WAVE_FORMAT_EXTENSIBLE, any channel count (averaged to mono, as capture does in doc 03 §1) and any sample rate.
/// RF64 files and streamed files with an unknown data size are read to the end of the stream.
/// </summary>
public static class WavReader
{
    private const int MaxFormatChunkBytes = 1 << 16;
    private const int BufferFrames = 4096;

    /// <summary>Reads a WAV file and downmixes it to mono.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The encoding and the mono samples.</returns>
    /// <exception cref="InvalidDataException">The file is not a WAV file this reader supports.</exception>
    public static WavAudio ReadMono(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return ReadMono(stream);
    }

    /// <summary>Reads a WAV stream and downmixes it to mono.</summary>
    /// <param name="stream">The stream, positioned at the RIFF header.</param>
    /// <returns>The encoding and the mono samples.</returns>
    /// <exception cref="InvalidDataException">The stream is not a WAV stream this reader supports.</exception>
    public static WavAudio ReadMono(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[12];
        if (!TryReadExactly(stream, header)) throw new InvalidDataException("Not a WAV file: shorter than a RIFF header.");
        string riff = Ascii(header[..4]);
        if (riff == "RIFX") throw new InvalidDataException("Big-endian (RIFX) WAV files are not supported.");
        if (riff is not ("RIFF" or "RF64") || Ascii(header[8..12]) != "WAVE")
            throw new InvalidDataException("Not a WAV file: missing the RIFF/WAVE header.");

        FormatChunk? format = null;
        long? ds64DataSize = null;
        Span<byte> chunkHeader = stackalloc byte[8];
        while (true)
        {
            if (!TryReadExactly(stream, chunkHeader))
                throw new InvalidDataException(format is null ? "Not a valid WAV file: no fmt chunk." : "Not a valid WAV file: no data chunk.");
            string id = Ascii(chunkHeader[..4]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            switch (id)
            {
                case "fmt ":
                    if (size > MaxFormatChunkBytes) throw new InvalidDataException($"Not a valid WAV file: fmt chunk of {size} bytes.");
                    format = ParseFormat(ReadChunk(stream, (int)size));
                    SkipBytes(stream, size & 1);
                    break;
                case "ds64":
                    if (size > MaxFormatChunkBytes) throw new InvalidDataException($"Not a valid RF64 file: ds64 chunk of {size} bytes.");
                    byte[] ds64 = ReadChunk(stream, (int)size);
                    if (ds64.Length >= 16) ds64DataSize = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(ds64.AsSpan(8, 8)), long.MaxValue);
                    SkipBytes(stream, size & 1);
                    break;
                case "data":
                    if (format is null) throw new InvalidDataException("Not a supported WAV file: the data chunk comes before the fmt chunk.");
                    long dataSize = size == uint.MaxValue ? ds64DataSize ?? -1 : size;
                    return Decode(stream, format, dataSize);
                default:
                    SkipBytes(stream, size + (size & 1L));
                    break;
            }
        }
    }

    private sealed record FormatChunk(int SampleRate, int Channels, int BlockAlign, WavSampleFormat Format, int ValidBits, bool Extensible);

    private static FormatChunk ParseFormat(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 16) throw new InvalidDataException("Not a valid WAV file: fmt chunk shorter than 16 bytes.");
        ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
        uint sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
        int blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(chunk[12..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
        int validBits = bits;
        bool extensible = tag == WavFormats.FormatExtensible;
        if (extensible)
        {
            int extraSize = chunk.Length >= 18 ? BinaryPrimitives.ReadUInt16LittleEndian(chunk[16..]) : 0;
            if (chunk.Length < 40 || extraSize < 22) throw new InvalidDataException("Not a valid WAV file: WAVE_FORMAT_EXTENSIBLE without its extension.");
            validBits = BinaryPrimitives.ReadUInt16LittleEndian(chunk[18..]);
            ReadOnlySpan<byte> subFormat = chunk.Slice(24, 16);
            if (!subFormat[2..].SequenceEqual(WavFormats.SubFormatTail))
                throw new InvalidDataException("Unsupported WAV encoding: unknown WAVE_FORMAT_EXTENSIBLE sub-format.");
            tag = BinaryPrimitives.ReadUInt16LittleEndian(subFormat);
            if (validBits == 0 || validBits > bits) validBits = bits;
        }

        if (channels == 0) throw new InvalidDataException("Not a valid WAV file: zero channels.");
        if (sampleRate == 0 || sampleRate > int.MaxValue) throw new InvalidDataException($"Not a valid WAV file: sample rate {sampleRate} Hz.");
        WavSampleFormat format = tag switch
        {
            WavFormats.FormatPcm => ((bits + 7) / 8) switch
            {
                1 => WavSampleFormat.Pcm8,
                2 => WavSampleFormat.Pcm16,
                3 => WavSampleFormat.Pcm24,
                4 => WavSampleFormat.Pcm32,
                _ => throw new InvalidDataException($"Unsupported WAV encoding: {bits}-bit PCM."),
            },
            WavFormats.FormatIeeeFloat => bits switch
            {
                32 => WavSampleFormat.Float32,
                64 => WavSampleFormat.Float64,
                _ => throw new InvalidDataException($"Unsupported WAV encoding: {bits}-bit float."),
            },
            _ => throw new InvalidDataException($"Unsupported WAV encoding (format tag 0x{tag:X4}); convert the file to PCM or float WAV first."),
        };
        int frameBytes = channels * WavFormats.BytesPerSample(format);
        if (blockAlign < frameBytes)
            throw new InvalidDataException($"Not a valid WAV file: block align {blockAlign} is smaller than {channels} × {WavFormats.BytesPerSample(format)} bytes.");
        return new FormatChunk((int)sampleRate, channels, blockAlign, format, validBits, extensible);
    }

    // dataSize < 0: unknown, read to the end of the stream. A data size beyond the end of the file (a truncated
    // recording) is read up to the last complete frame.
    private static WavAudio Decode(Stream stream, FormatChunk format, long dataSize)
    {
        int stride = format.BlockAlign;
        int sampleBytes = WavFormats.BytesPerSample(format.Format);
        long declaredFrames = dataSize >= 0 ? dataSize / stride : -1;
        if (stream.CanSeek)
        {
            long available = (stream.Length - stream.Position) / stride;
            declaredFrames = declaredFrames < 0 ? available : Math.Min(declaredFrames, available);
        }
        if (declaredFrames > Array.MaxLength) throw new InvalidDataException($"The WAV file is too long ({declaredFrames} frames) to analyze in one piece.");

        float[] mono = new float[declaredFrames >= 0 ? declaredFrames : 1 << 20];
        long remainingBytes = declaredFrames >= 0 ? declaredFrames * stride : long.MaxValue;
        byte[] buffer = new byte[stride * BufferFrames];
        float scale = 1f / format.Channels;
        int frames = 0, carried = 0;
        while (remainingBytes > 0)
        {
            int toRead = (int)Math.Min(buffer.Length - carried, remainingBytes);
            int read = stream.Read(buffer, carried, toRead);
            if (read == 0) break;
            remainingBytes -= read;
            int bytes = carried + read, complete = bytes / stride;
            if (frames + complete > mono.Length)
            {
                if (mono.Length >= Array.MaxLength) throw new InvalidDataException("The WAV file is too long to analyze in one piece.");
                Array.Resize(ref mono, (int)Math.Min(Array.MaxLength, Math.Max(2L * mono.Length, frames + complete)));
            }
            for (int f = 0; f < complete; f++)
            {
                ReadOnlySpan<byte> frame = buffer.AsSpan(f * stride, stride);
                float sum = 0;
                for (int c = 0; c < format.Channels; c++) sum += DecodeSample(frame.Slice(c * sampleBytes, sampleBytes), format.Format);
                mono[frames + f] = sum * scale;
            }
            frames += complete;
            carried = bytes - complete * stride;
            if (carried > 0) Buffer.BlockCopy(buffer, complete * stride, buffer, 0, carried);
        }
        if (frames != mono.Length) Array.Resize(ref mono, frames);
        return new WavAudio(new WavInfo(format.SampleRate, format.Channels, format.Format, format.ValidBits, format.Extensible, frames), mono);
    }

    /// <summary>Decodes one little-endian sample to a float nominally in [-1, 1].</summary>
    /// <param name="bytes">The sample's bytes.</param>
    /// <param name="format">The sample format.</param>
    /// <returns>The sample value.</returns>
    public static float DecodeSample(ReadOnlySpan<byte> bytes, WavSampleFormat format) => format switch
    {
        WavSampleFormat.Pcm8 => (bytes[0] - 128) / 128f,
        WavSampleFormat.Pcm16 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
        WavSampleFormat.Pcm24 => (bytes[0] | bytes[1] << 8 | (sbyte)bytes[2] << 16) / 8388608f,
        WavSampleFormat.Pcm32 => (float)(BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648.0),
        WavSampleFormat.Float32 => BinaryPrimitives.ReadSingleLittleEndian(bytes),
        WavSampleFormat.Float64 => (float)BinaryPrimitives.ReadDoubleLittleEndian(bytes),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static string Ascii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0) return false;
            total += read;
        }
        return true;
    }

    private static byte[] ReadChunk(Stream stream, int size)
    {
        byte[] chunk = new byte[size];
        if (!TryReadExactly(stream, chunk)) throw new InvalidDataException("Not a valid WAV file: a chunk runs past the end of the file.");
        return chunk;
    }

    private static void SkipBytes(Stream stream, long count)
    {
        if (count <= 0) return;
        if (stream.CanSeek)
        {
            stream.Seek(Math.Min(count, stream.Length - stream.Position), SeekOrigin.Current);
            return;
        }
        Span<byte> scratch = stackalloc byte[4096];
        while (count > 0)
        {
            int read = stream.Read(scratch[..(int)Math.Min(scratch.Length, count)]);
            if (read == 0) return;
            count -= read;
        }
    }
}
