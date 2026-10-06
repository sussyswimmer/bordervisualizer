using Rimlight.Core;

namespace Rimlight.App.Overlay;

/// <summary>
/// The analyzer's features from one overlay frame, for Settings' level meter and live preview. The render thread packs
/// them into one 64-bit value, so the UI reads a consistent set with one volatile read and nothing is allocated per
/// frame.
/// </summary>
/// <param name="Features">Level, Bass and Beat to 1/65535, and IsSilent.</param>
/// <param name="Sequence">Counts frames, 1 to 32767 and round again; the same value twice means no new frame came.</param>
internal readonly record struct AudioReading(AudioFeatures Features, int Sequence)
{
    private const float Scale = 65535f;

    /// <summary>No frame yet.</summary>
    public static AudioReading None { get; } = new(new AudioFeatures(0, 0, 0, true), -1);

    /// <summary>
    /// Packs features and a frame counter: 16 bits each for Level, Bass and Beat, 1 for IsSilent, 15 for the counter,
    /// which must not be 0 (0 is "no frame yet").
    /// </summary>
    public static long Pack(in AudioFeatures features, int sequence) =>
        (long)Quantize(features.Level)
        | (long)Quantize(features.Bass) << 16
        | (long)Quantize(features.Beat) << 32
        | (features.IsSilent ? 1L : 0L) << 48
        | (long)(sequence & 0x7FFF) << 49;

    /// <summary>The inverse of <see cref="Pack"/>; <see cref="None"/> for 0, the value before the first frame.</summary>
    public static AudioReading Unpack(long packed)
    {
        if (packed == 0) return None;
        var features = new AudioFeatures(
            (packed & 0xFFFF) / Scale,
            (packed >> 16 & 0xFFFF) / Scale,
            (packed >> 32 & 0xFFFF) / Scale,
            (packed >> 48 & 1) != 0);
        return new AudioReading(features, (int)(packed >> 49 & 0x7FFF));
    }

    /// <summary>The counter after <paramref name="sequence"/>: 1, 2, … 32767, then 1 again (never 0).</summary>
    public static int Next(int sequence) => sequence >= 0x7FFF ? 1 : sequence + 1;

    private static uint Quantize(float value) =>
        float.IsFinite(value) ? (uint)MathF.Round(Math.Clamp(value, 0f, 1f) * Scale) : 0;
}
