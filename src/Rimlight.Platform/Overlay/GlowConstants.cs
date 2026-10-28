using System.Runtime.InteropServices;
using Rimlight.Core;

namespace Rimlight.Platform.Overlay;

// Mirror of the Light cbuffer in Glow.hlsl (and of RlGlowConstants in the Mac's glow_math.h): 20 floats in five
// 16-byte registers. Keep them in sync.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct GlowConstants : IEquatable<GlowConstants>
{
    public const int SizeInBytes = 80;

    // Below this Visibility every pixel is transparent: alpha never exceeds Visibility, and the shader returns 0 for
    // alpha under half a code value. Overlays whose visibility falls below it are hidden.
    public const float MinVisibility = 0.5f / 255;

    // LooksLike tolerances. Brightness-like values within half an 8-bit code value draw the same pixels (and the
    // dither hides the rest); a phase shift of 0.0002 moves the colours by about a pixel along a 4K perimeter.
    private const float ValueTolerance = 0.5f / 255;
    private const float PhaseTolerance = 0.0002f;

    public float ScreenWidthPx;
    public float ScreenHeightPx;
    public float CornerRadiusPx; // the bottom corners' radius, and the top ones' without a notch
    public float ShoulderPx;   // the soft shoulder at the edge (GlowProfile.ShoulderFor): the thickness, >= 1 DIP;
                               // in the Line style the halo's, one DIP

    public float SpreadPx;
    public float Gain;         // Intensity x the beat's gain, rolled off under 1 (GlowProfile.Gain); scales the profile
    public float Pulse;
    public float Phase;

    public float Visibility;
    public float ColorRadiusPx;
    public float GlowRadiusPx; // corner radius of the light's distance field (GlowProfile.GlowRadius)
    public float LinePx;       // the Line style's line width (GlowProfile.LineFor); 0 selects the Glow style

    public float MeanRed;
    public float MeanGreen;
    public float MeanBlue;
    public float TopCornerRadiusPx; // the screen's top corners, for the line: rounder on a notched screen

    public float NotchLeftPx;  // the notch hanging from the top edge (GlowProfile.NotchDistance); all 0 without one
    public float NotchRightPx;
    public float NotchHeightPx; // 0: no notch
    public float NotchRadiusPx; // its lower corners and the fillets beside it (GlowProfile.NotchRadius)

    // Converts a frame's light state to pixels for one surface. widthPx/heightPx: the swap chain's size; dpiScale =
    // monitor DPI / 96; renderScale: swap-chain pixels per screen pixel (1, or about 0.5 at half scale); meanColor is
    // the average of the gradient texels (linear RGB); style: the settings' edge style (anything but Line is the
    // glow); notch: the camera notch in the swap chain's pixels (none on Windows).
    public static GlowConstants Create(in LightState state, int widthPx, int heightPx, float dpiScale, float renderScale,
        float cornerRadiusDip, Rgb meanColor, EdgeStyle style = EdgeStyle.Glow, NotchPx notch = default)
    {
        float scale = dpiScale * renderScale; // swap-chain pixels per DIP
        int shortSide = Math.Min(widthPx, heightPx);
        float minHalf = 0.5f * shortSide;
        float spreadPx = GlowProfile.SpreadFor(state.Spread, shortSide, scale);
        float cornerPx = Math.Clamp(Finite(cornerRadiusDip) * scale, 0, minHalf);
        // Thickness is the width of the soft shoulder at the edge (GlowProfile), or in the Line style the line's, whose
        // halo then has the narrowest shoulder. It converts at the render scale like every other measure, so at half
        // scale the same light lands in half as many pixels.
        float thicknessPx = Math.Max(0, Finite(state.CoreThicknessDip)) * scale;
        bool line = style == EdgeStyle.Line;
        float shoulderPx = GlowProfile.ShoulderFor(line ? 0 : thicknessPx, scale);

        // The notch, cut to the screen's width and at most half its short side tall. Anything else (empty, inverted,
        // off the screen, not finite) is no notch.
        float notchLeft = Math.Max(notch.LeftPx, 0);
        float notchRight = Math.Min(notch.RightPx, widthPx);
        float notchHeight = Math.Min(notch.HeightPx, minHalf);
        bool notched = float.IsFinite(notch.LeftPx) && float.IsFinite(notch.RightPx) &&
            float.IsFinite(notch.HeightPx) && notchRight > notchLeft && notchHeight > 0;
        return new GlowConstants
        {
            ScreenWidthPx = widthPx,
            ScreenHeightPx = heightPx,
            CornerRadiusPx = cornerPx,
            ShoulderPx = shoulderPx,
            SpreadPx = spreadPx,
            // The gain scales the whole profile, so the light keeps its shape at any brightness and beat; rolled off
            // under 1, it never needs clipping per pixel (GlowProfile.Gain).
            Gain = GlowProfile.Gain(Clamp01(state.Intensity), Clamp01(state.Pulse)),
            Pulse = Clamp01(state.Pulse),
            Phase = Fraction(state.Phase),
            Visibility = Clamp01(state.Visibility),
            // The colour path's corner radius keeps the perimeter coordinate continuous through the corners, outside
            // a wide shoulder too; its seam lies deeper, where the shader has blended to the mean colour. It uses the
            // unpulsed spread, so colours don't shift with the beat.
            ColorRadiusPx = Math.Min(Math.Max(Math.Max(cornerPx, 6 * spreadPx), 2 * shoulderPx), minHalf),
            // Rounded field: no 45-degree crease where two edges' light meets (deviation from doc 04 §3).
            GlowRadiusPx = GlowProfile.GlowRadius(cornerPx, spreadPx, shoulderPx, minHalf),
            LinePx = line ? GlowProfile.LineFor(thicknessPx, scale) : 0,
            MeanRed = Finite(meanColor.R),
            MeanGreen = Finite(meanColor.G),
            MeanBlue = Finite(meanColor.B),
            // A notched screen has rounded top corners (GlowProfile.NotchedDisplayTopCornerDip); the line follows them.
            TopCornerRadiusPx = notched
                ? Math.Min(Math.Max(cornerPx, GlowProfile.NotchedDisplayTopCornerDip * scale), minHalf)
                : cornerPx,
            NotchLeftPx = notched ? notchLeft : 0,
            NotchRightPx = notched ? notchRight : 0,
            NotchHeightPx = notched ? notchHeight : 0,
            NotchRadiusPx = notched ? GlowProfile.NotchRadius(notchRight - notchLeft, notchHeight) : 0,
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

    // True when both draw the same picture: the same geometry, and values that differ by less than the eye or the
    // 8-bit swap chain can tell. Smoothed values approach their targets asymptotically; without this tolerance a
    // static glow would keep presenting for many seconds. Compared against the last presented frame, so slow drift
    // still reaches the screen once it adds up.
    public readonly bool LooksLike(in GlowConstants other) =>
        ScreenWidthPx == other.ScreenWidthPx && ScreenHeightPx == other.ScreenHeightPx &&
        CornerRadiusPx == other.CornerRadiusPx && ShoulderPx == other.ShoulderPx && SpreadPx == other.SpreadPx &&
        ColorRadiusPx == other.ColorRadiusPx && GlowRadiusPx == other.GlowRadiusPx && SameShape(other) &&
        Near(Gain, other.Gain) && Near(Pulse, other.Pulse) && Near(Visibility, other.Visibility) &&
        Near(MeanRed, other.MeanRed) && Near(MeanGreen, other.MeanGreen) && Near(MeanBlue, other.MeanBlue) &&
        PhaseNear(Phase, other.Phase);

    public readonly bool Equals(GlowConstants other) =>
        ScreenWidthPx == other.ScreenWidthPx && ScreenHeightPx == other.ScreenHeightPx &&
        CornerRadiusPx == other.CornerRadiusPx && ShoulderPx == other.ShoulderPx &&
        SpreadPx == other.SpreadPx && Gain == other.Gain && Pulse == other.Pulse && Phase == other.Phase &&
        Visibility == other.Visibility && ColorRadiusPx == other.ColorRadiusPx && GlowRadiusPx == other.GlowRadiusPx &&
        MeanRed == other.MeanRed && MeanGreen == other.MeanGreen && MeanBlue == other.MeanBlue && SameShape(other);

    public override readonly bool Equals(object? obj) => obj is GlowConstants other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(ScreenWidthPx, ScreenHeightPx, SpreadPx, Gain, Pulse, Phase, Visibility);

    // The line, the top corners and the notch: geometry, compared exactly.
    private readonly bool SameShape(in GlowConstants other) =>
        LinePx == other.LinePx && TopCornerRadiusPx == other.TopCornerRadiusPx &&
        NotchLeftPx == other.NotchLeftPx && NotchRightPx == other.NotchRightPx &&
        NotchHeightPx == other.NotchHeightPx && NotchRadiusPx == other.NotchRadiusPx;

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;

    private static bool Near(float a, float b) => MathF.Abs(a - b) <= ValueTolerance;

    // Phase is a fraction of the loop, so 0.9999 and 0 are neighbours.
    private static bool PhaseNear(float a, float b)
    {
        float d = MathF.Abs(a - b);
        return MathF.Min(d, 1 - d) <= PhaseTolerance;
    }

    private static float Clamp01(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static float Fraction(float value) => float.IsFinite(value) ? value - MathF.Floor(value) : 0;
}

// A camera notch hanging from the top edge of the screen (a notched MacBook's), in the swap chain's pixels: from
// LeftPx to RightPx across, HeightPx down from the top. default is no notch. GlowConstants.Create checks it.
internal readonly record struct NotchPx(float LeftPx, float RightPx, float HeightPx);
