using System.Runtime.InteropServices;
using Rimlight.Core;

namespace Rimlight.Platform.Overlay;

// Mirror of the Light cbuffer in Glow.hlsl: 12 floats in three 16-byte registers. Keep the two in sync.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct GlowConstants : IEquatable<GlowConstants>
{
    public const int SizeInBytes = 48;

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
    public float Padding;

    // Glow (0..1) to glow reach in DIPs: a 2 DIP halo at 0, 150 DIP at 1, squared so the slider feels even.
    // The default Glow 0.45 gives 32 DIP. Doc 04 leaves this mapping to the renderer.
    internal static float SpreadDip(float glow)
    {
        float g = Clamp01(glow);
        return 2 + 148 * g * g;
    }

    // Converts a frame's light state to pixels for one surface. scale = monitor DPI / 96.
    public static GlowConstants Create(in LightState state, int widthPx, int heightPx, float scale, float cornerRadiusDip)
    {
        float minHalf = 0.5f * Math.Min(widthPx, heightPx);
        float spreadPx = SpreadDip(state.Spread) * scale;
        float cornerPx = Math.Clamp(Finite(cornerRadiusDip) * scale, 0, minHalf);
        return new GlowConstants
        {
            ScreenWidthPx = widthPx,
            ScreenHeightPx = heightPx,
            CornerRadiusPx = cornerPx,
            CoreThicknessPx = Math.Max(0, Finite(state.CoreThicknessDip)) * scale,
            SpreadPx = spreadPx,
            Intensity = Clamp01(state.Intensity),
            Pulse = Clamp01(state.Pulse),
            Phase = Fraction(state.Phase),
            Visibility = Clamp01(state.Visibility),
            // The colour path's corner radius keeps the perimeter coordinate continuous wherever the glow is visible
            // (its only seam is at least 6 spreads in, below 1/255). It uses the unpulsed spread, so colours don't
            // shift with the beat.
            ColorRadiusPx = Math.Min(Math.Max(cornerPx, 6 * spreadPx), minHalf),
            // Rounded glow field: no 45-degree crease where two edges' glows meet (deviation from doc 04 §3).
            GlowRadiusPx = Math.Min(Math.Max(cornerPx, 2 * spreadPx), minHalf),
        };
    }

    public readonly bool Equals(GlowConstants other) =>
        ScreenWidthPx == other.ScreenWidthPx && ScreenHeightPx == other.ScreenHeightPx &&
        CornerRadiusPx == other.CornerRadiusPx && CoreThicknessPx == other.CoreThicknessPx &&
        SpreadPx == other.SpreadPx && Intensity == other.Intensity && Pulse == other.Pulse && Phase == other.Phase &&
        Visibility == other.Visibility && ColorRadiusPx == other.ColorRadiusPx && GlowRadiusPx == other.GlowRadiusPx;

    public override readonly bool Equals(object? obj) => obj is GlowConstants other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(ScreenWidthPx, ScreenHeightPx, SpreadPx, Intensity, Pulse, Phase, Visibility);

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;

    private static float Clamp01(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static float Fraction(float value) => float.IsFinite(value) ? value - MathF.Floor(value) : 0;
}
