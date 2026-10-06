using System.Globalization;
using Rimlight.Core;

namespace Rimlight.App.Overlay;

/// <summary>
/// Settings store colors as sRGB hex ("#7C5CFF"); Core works in linear RGB (<see cref="Rgb"/>). Lane B owns this
/// conversion (the K0 review of Palette's API).
/// </summary>
internal static class SrgbHex
{
    /// <summary>Parses "#RRGGBB" or "RRGGBB" into linear RGB.</summary>
    public static bool TryParse(string? hex, out Rgb linear)
    {
        linear = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        ReadOnlySpan<char> digits = hex.AsSpan().Trim();
        if (digits.StartsWith("#")) digits = digits[1..];
        if (digits.Length != 6 || !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
            return false;
        linear = new Rgb(ToLinear((byte)(value >> 16)), ToLinear((byte)(value >> 8)), ToLinear((byte)value));
        return true;
    }

    private static float ToLinear(byte channel)
    {
        float c = channel / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}
