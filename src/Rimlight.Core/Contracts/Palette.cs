namespace Rimlight.Core;

/// <summary>Immutable pair of linear RGB colors extracted from artwork.</summary>
/// <param name="Primary">Primary linear color.</param>
/// <param name="Secondary">Secondary linear color.</param>
/// <param name="SourceTrackId">Source track, or null for a fallback palette.</param>
public sealed record Palette(Rgb Primary, Rgb Secondary, string? SourceTrackId)
{
    /// <summary>The #7C5CFF / #22D3EE fallback, converted from sRGB to linear RGB.</summary>
    public static Palette Default { get; } = new(
        new Rgb(ToLinear(124), ToLinear(92), 1f),
        new Rgb(ToLinear(34), ToLinear(211), ToLinear(238)), null);

    private static float ToLinear(byte channel)
    {
        float value = channel / 255f;
        return value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }
}
