using System.Runtime.InteropServices;
using Rimlight.Core;

namespace Rimlight.Platform.Overlay;

// Mirror of the Light cbuffer in Glow.hlsl: 16 floats in four 16-byte registers. Keep the two in sync.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct GlowConstants : IEquatable<GlowConstants>
{
    public const int SizeInBytes = 64;

    // PRD §1: "Glow: how far the light spills inward. 0 means a hairline; 100 means about 35% of the shorter screen
    // dimension." Spill is taken as three e-folding distances (the glow is down to 5% there), and the slider is
    // squared so it feels even: the default 0.45 reaches about 7% of the shorter side (25 px e-folding at 1080p).
    private const float FullSpill = 0.35f;
    private const float EFoldingsPerSpill = 3;

    public float ScreenWidthPx;
    public float ScreenHeightPx;
    public float CornerRadiusPx;
    public float CoreThicknessPx;

    public float SpreadPx;
    public float Intensity;
    public float Pulse;
    public float Phase;

    public float Visibility;
    public float ColorRadiusPx;
    public float GlowRadiusPx;
    public float Padding0;

    public float MeanRed;
    public float MeanGreen;
    public float MeanBlue;
    public float Padding1;

    // Glow (0..1) to the glow's e-folding distance in pixels on a surface whose shorter side is shortSidePx. At 0 a
    // hairline halo of one DIP.
    internal static float SpreadFor(float glow, int shortSidePx, float scale)
    {
        float g = Clamp01(glow);
        return Math.Max(scale, g * g * FullSpill / EFoldingsPerSpill * shortSidePx);
    }

    // Converts a frame's light state to pixels for one surface. scale = monitor DPI / 96; meanColor is the average of
    // the gradient texels (linear RGB).
    public static GlowConstants Create(in LightState state, int widthPx, int heightPx, float scale, float cornerRadiusDip, Rgb meanColor)
    {
        int shortSide = Math.Min(widthPx, heightPx);
        float minHalf = 0.5f * shortSide;
        float spreadPx = SpreadFor(state.Spread, shortSide, scale);
        float cornerPx = Math.Clamp(Finite(cornerRadiusDip) * scale, 0, minHalf);
        float corePx = Math.Max(0, Finite(state.CoreThicknessDip)) * scale;
        return new GlowConstants
        {
            ScreenWidthPx = widthPx,
            ScreenHeightPx = heightPx,
            CornerRadiusPx = cornerPx,
            CoreThicknessPx = corePx,
            SpreadPx = spreadPx,
            Intensity = Clamp01(state.Intensity),
            Pulse = Clamp01(state.Pulse),
            Phase = Fraction(state.Phase),
            Visibility = Clamp01(state.Visibility),
            // The colour path's corner radius keeps the perimeter coordinate continuous through the corners, outside
            // a thick core too; its seam lies deeper, where the shader has blended to the mean colour. It uses the
            // unpulsed spread, so colours don't shift with the beat.
            ColorRadiusPx = Math.Min(Math.Max(Math.Max(cornerPx, 6 * spreadPx), 2 * corePx), minHalf),
            // Rounded glow field: no 45-degree crease where two edges' glows meet (deviation from doc 04 §3).
            GlowRadiusPx = Math.Min(Math.Max(cornerPx, 2 * spreadPx), minHalf),
            MeanRed = Finite(meanColor.R),
            MeanGreen = Finite(meanColor.G),
            MeanBlue = Finite(meanColor.B),
        };
    }

    // Average of 64 RGBA texels (linear RGB): the colour the glow blends to deep inside.
    public static Rgb MeanOf(ReadOnlySpan<float> texels)
    {
        float r = 0, g = 0, b = 0;
        for (int i = 0; i < 64; i++)
        {
            r += texels[i * 4];
            g += texels[i * 4 + 1];
            b += texels[i * 4 + 2];
        }
        return new Rgb(r / 64, g / 64, b / 64);
    }

    public readonly bool Equals(GlowConstants other) =>
        ScreenWidthPx == other.ScreenWidthPx && ScreenHeightPx == other.ScreenHeightPx &&
        CornerRadiusPx == other.CornerRadiusPx && CoreThicknessPx == other.CoreThicknessPx &&
        SpreadPx == other.SpreadPx && Intensity == other.Intensity && Pulse == other.Pulse && Phase == other.Phase &&
        Visibility == other.Visibility && ColorRadiusPx == other.ColorRadiusPx && GlowRadiusPx == other.GlowRadiusPx &&
        MeanRed == other.MeanRed && MeanGreen == other.MeanGreen && MeanBlue == other.MeanBlue;

    public override readonly bool Equals(object? obj) => obj is GlowConstants other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(ScreenWidthPx, ScreenHeightPx, SpreadPx, Intensity, Pulse, Phase, Visibility);

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;

    private static float Clamp01(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static float Fraction(float value) => float.IsFinite(value) ? value - MathF.Floor(value) : 0;
}
