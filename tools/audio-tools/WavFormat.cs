namespace Rimlight.AudioTools;

/// <summary>Sample encodings the WAV reader and writer handle.</summary>
public enum WavSampleFormat
{
    /// <summary>8-bit unsigned integer PCM.</summary>
    Pcm8,
    /// <summary>16-bit signed integer PCM.</summary>
    Pcm16,
    /// <summary>24-bit signed integer PCM, packed in 3 bytes.</summary>
    Pcm24,
    /// <summary>32-bit signed integer PCM.</summary>
    Pcm32,
    /// <summary>32-bit IEEE float.</summary>
    Float32,
    /// <summary>64-bit IEEE float.</summary>
    Float64,
}

/// <summary>Describes a WAV stream's encoding.</summary>
/// <param name="SampleRate">Frames per second, in Hz.</param>
/// <param name="Channels">Interleaved channels per frame.</param>
/// <param name="Format">Sample encoding (the container size; valid bits may be fewer).</param>
/// <param name="ValidBitsPerSample">Meaningful bits per sample (WAVE_FORMAT_EXTENSIBLE may store fewer than the container).</param>
/// <param name="IsExtensible">Whether the file uses WAVE_FORMAT_EXTENSIBLE.</param>
/// <param name="FrameCount">Number of complete frames decoded.</param>
public sealed record WavInfo(int SampleRate, int Channels, WavSampleFormat Format, int ValidBitsPerSample, bool IsExtensible, long FrameCount)
{
    /// <summary>Duration in seconds.</summary>
    public double Seconds => FrameCount / (double)SampleRate;

    /// <summary>Bytes per sample in the container.</summary>
    public int BytesPerSample => WavFormats.BytesPerSample(Format);

    /// <summary>A short human-readable description, e.g. "48000 Hz, 2 ch, PCM 16-bit".</summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        string encoding = Format switch
        {
            WavSampleFormat.Float32 => "float 32-bit",
            WavSampleFormat.Float64 => "float 64-bit",
            _ => ValidBitsPerSample != BytesPerSample * 8
                ? $"PCM {ValidBitsPerSample}-bit in {BytesPerSample * 8}-bit"
                : $"PCM {ValidBitsPerSample}-bit",
        };
        return $"{SampleRate} Hz, {Channels} ch, {encoding}{(IsExtensible ? " (extensible)" : "")}";
    }
}

/// <summary>Decoded WAV audio, downmixed to mono.</summary>
/// <param name="Info">The source encoding.</param>
/// <param name="Mono">Samples averaged across channels, nominally in [-1, 1].</param>
public sealed record WavAudio(WavInfo Info, float[] Mono);

/// <summary>Format constants shared by the reader and writer.</summary>
public static class WavFormats
{
    /// <summary>WAVE_FORMAT_PCM.</summary>
    public const ushort FormatPcm = 0x0001;
    /// <summary>WAVE_FORMAT_IEEE_FLOAT.</summary>
    public const ushort FormatIeeeFloat = 0x0003;
    /// <summary>WAVE_FORMAT_EXTENSIBLE.</summary>
    public const ushort FormatExtensible = 0xFFFE;

    // KSDATAFORMAT_SUBTYPE_* GUIDs are the format tag followed by this fixed tail.
    internal static ReadOnlySpan<byte> SubFormatTail => [0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

    /// <summary>Bytes per sample for a format.</summary>
    /// <param name="format">The sample format.</param>
    /// <returns>1, 2, 3, 4 or 8.</returns>
    public static int BytesPerSample(WavSampleFormat format) => format switch
    {
        WavSampleFormat.Pcm8 => 1,
        WavSampleFormat.Pcm16 => 2,
        WavSampleFormat.Pcm24 => 3,
        WavSampleFormat.Pcm32 => 4,
        WavSampleFormat.Float32 => 4,
        WavSampleFormat.Float64 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Whether a format stores IEEE floats.</summary>
    /// <param name="format">The sample format.</param>
    /// <returns>True for <see cref="WavSampleFormat.Float32"/> and <see cref="WavSampleFormat.Float64"/>.</returns>
    public static bool IsFloat(WavSampleFormat format) => format is WavSampleFormat.Float32 or WavSampleFormat.Float64;
}
