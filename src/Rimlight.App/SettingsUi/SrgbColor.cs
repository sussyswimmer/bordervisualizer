using System.Globalization;

namespace Rimlight.App.SettingsUi;

/// <summary>An sRGB color as the settings store it ("#RRGGBB"), and its hue, saturation and value. Pure code.</summary>
/// <param name="R">Red, 0..255.</param>
/// <param name="G">Green, 0..255.</param>
/// <param name="B">Blue, 0..255.</param>
internal readonly record struct SrgbColor(byte R, byte G, byte B)
{
    /// <summary>
    /// Parses "#RRGGBB", "RRGGBB", "#RGB" or "RGB" (any case, spaces around ignored). False for anything else, which
    /// the color editor shows as a typing error instead of applying.
    /// </summary>
    public static bool TryParse(string? text, out SrgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        ReadOnlySpan<char> digits = text.AsSpan().Trim();
        if (digits.StartsWith("#")) digits = digits[1..];
        if (digits.Length is not (3 or 6)) return false;
        foreach (char c in digits)
            if (!char.IsAsciiHexDigit(c)) return false;
        if (!uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value)) return false;
        color = digits.Length == 6
            ? new SrgbColor((byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : new SrgbColor((byte)((value >> 8 & 0xF) * 17), (byte)((value >> 4 & 0xF) * 17), (byte)((value & 0xF) * 17));
        return true;
    }

    /// <summary>
    /// A color from hue (degrees, any value; wraps), saturation and value (0..1, clamped), rounded to the nearest
    /// 8-bit sRGB code values.
    /// </summary>
    public static SrgbColor FromHsv(double hue, double saturation, double value)
    {
        double h = double.IsFinite(hue) ? (hue % 360 + 360) % 360 : 0;
        double s = Clamp01(saturation);
        double v = Clamp01(value);
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new SrgbColor(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    /// <summary>Hue in degrees 0..360 (0 for grays), saturation and value 0..1.</summary>
    public (double Hue, double Saturation, double Value) ToHsv()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double hue = 0;
        if (delta > 0)
        {
            if (max == r) hue = 60 * ((g - b) / delta % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
            if (hue < 0) hue += 360;
        }
        double saturation = max > 0 ? delta / max : 0;
        return (hue, saturation, max);
    }

    /// <summary>"#RRGGBB" in upper case, the form the settings store.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static byte ToByte(double unit) => (byte)Math.Clamp(Math.Round(unit * 255), 0, 255);
}
