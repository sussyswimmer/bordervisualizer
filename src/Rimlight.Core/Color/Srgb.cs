namespace Rimlight.Core.Color;

/// <summary>The exact sRGB transfer functions (IEC 61966-2-1): encoded 0..1 ⇄ linear 0..1.</summary>
internal static class Srgb
{
    // Byte → linear lookup, built once and never written again, so it is safe to share between threads.
    private static readonly float[] ByteToLinearTable = BuildTable();

    /// <summary>Decodes an sRGB-encoded channel to linear light.</summary>
    /// <param name="encoded">Encoded value, 0..1.</param>
    /// <returns>Linear value, 0..1.</returns>
    public static float ToLinear(float encoded) =>
        encoded <= 0.04045f ? encoded / 12.92f : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);

    /// <summary>Encodes a linear channel with the sRGB curve.</summary>
    /// <param name="linear">Linear value, 0..1.</param>
    /// <returns>Encoded value, 0..1.</returns>
    public static float FromLinear(float linear) =>
        linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f;

    /// <summary>Decodes an 8-bit sRGB channel to linear light, from a table.</summary>
    /// <param name="encoded">Encoded value, 0..255.</param>
    /// <returns>Linear value, 0..1.</returns>
    public static float ToLinear(byte encoded) => ByteToLinearTable[encoded];

    /// <summary>Encodes a linear channel to the nearest 8-bit sRGB value, clamping out-of-range input.</summary>
    /// <param name="linear">Linear value.</param>
    /// <returns>Encoded value, 0..255.</returns>
    public static byte ToByte(float linear) =>
        (byte)MathF.Round(Math.Clamp(FromLinear(Math.Clamp(linear, 0f, 1f)), 0f, 1f) * 255f);

    private static float[] BuildTable()
    {
        var table = new float[256];
        for (int i = 0; i < table.Length; i++) table[i] = ToLinear(i / 255f);
        return table;
    }
}
