using Rimlight.Core;
using Rimlight.Platform.Overlay;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Draws the glow on the CPU for the Appearance page's preview: a port of <c>Glow.hlsl</c> (doc 04 §3) into a small
/// premultiplied BGRA buffer, as if the card were a 1280-DIP-wide screen. The edge profile, in either edge style (the
/// soft glow, or the bright line with its halo), the Glow mapping and the Flow movement's comets and ripples are
/// <see cref="GlowProfile"/>'s, the same the overlay uses. Everything that depends only on the shape is cached per
/// pixel and recomputed when the shape changes, so a frame costs one square root and one exp per lit pixel, and in the
/// Flow movement one more exp for the comets and one per live ripple. No allocation after construction. Pure code; any
/// one thread.
/// </summary>
internal sealed class GlowRaster
{
    /// <summary>The width, in DIPs, of the screen the preview stands for: thickness and corner radius scale by it.</summary>
    public const float ScreenWidthDip = 1280;

    // Colors around the perimeter, looked up rather than filtered: 1024 steps put them within about a pixel of the
    // shader's at this size.
    private const int LutSize = 1024;
    // The shape cache is rebuilt when the spread moves more than this fraction (an engine may vary it with the music;
    // only the corner rounding of the light and color fields depends on it, which nobody can see move by 10%).
    private const float SpreadCacheTolerance = 0.1f;

    private readonly float[] glowDepth;  // distance into the light's rounded field
    private readonly float[] lineDepth;  // distance in from the screen's own edge, for the Line style's line
    private readonly float[] perimeter;  // perimeter coordinate t in [0, 1): the colors' and the Flow movement's
    private readonly float[] meanBlend;  // how far the color (and the Flow movement) has blended to the mean, deep inside
    private readonly float[] lut = new float[LutSize * 3]; // the gradient around the perimeter, sRGB-encoded
    private float meanR, meanG, meanB; // the gradient's mean, sRGB-encoded
    private int gradientVersion;

    private float shapeCorner = float.NaN, shapeShoulder = float.NaN, shapeSpread = float.NaN;
    private Frame last; // what the pixels show now
    private bool drawn;

    /// <summary>Creates a raster of the given size in pixels (16:9 for a screen).</summary>
    public GlowRaster(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);
        Width = width;
        Height = height;
        Pixels = new int[width * height];
        glowDepth = new float[width * height];
        lineDepth = new float[width * height];
        perimeter = new float[width * height];
        meanBlend = new float[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Premultiplied BGRA (WPF's <c>Pbgra32</c>), one int per pixel, rows top to bottom.</summary>
    public int[] Pixels { get; }

    /// <summary>Takes 64 linear RGBA texels as <see cref="IPaletteBlender.FillGradient"/> writes them (H-008).</summary>
    public void SetGradient(ReadOnlySpan<float> texels)
    {
        if (texels.Length < 256) throw new ArgumentException("Expected 64 RGBA texels.", nameof(texels));
        float r = 0, g = 0, b = 0;
        for (int i = 0; i < 64; i++)
        {
            r += texels[i * 4];
            g += texels[i * 4 + 1];
            b += texels[i * 4 + 2];
        }
        meanR = Encode(r / 64);
        meanG = Encode(g / 64);
        meanB = Encode(b / 64);

        // Texel i is the color at u = (i + 0.5) / 64; between texels the shader filters linearly, wrapping around.
        for (int j = 0; j < LutSize; j++)
        {
            float x = (j + 0.5f) / LutSize * 64 - 0.5f;
            int i0 = (int)MathF.Floor(x);
            float f = x - i0;
            int a = (i0 % 64 + 64) % 64;
            int c = (a + 1) % 64;
            for (int k = 0; k < 3; k++)
                lut[j * 3 + k] = Encode(texels[a * 4 + k] * (1 - f) + texels[c * 4 + k] * f);
        }
        gradientVersion++;
    }

    /// <summary>
    /// Draws a frame. Returns false, leaving <see cref="Pixels"/> as they are, when it would look the same as the
    /// last one.
    /// </summary>
    /// <param name="state">The light state, from a light engine.</param>
    /// <param name="cornerRadiusDip">The settings' corner radius, which the overlay takes from the settings too.</param>
    /// <param name="style">The settings' edge style, as the overlay draws it (GlowConstants.Create): in the Line style
    /// the Thickness is the line's width and the halo inside it has the narrowest shoulder.</param>
    public bool Render(in LightState state, float cornerRadiusDip, EdgeStyle style = EdgeStyle.Glow)
    {
        float scale = Width / ScreenWidthDip; // pixels per DIP
        int shortSide = Math.Min(Width, Height);
        float minHalf = 0.5f * shortSide;
        float spread = GlowProfile.SpreadFor(state.Spread, shortSide, scale);
        float corner = Math.Clamp(Finite(cornerRadiusDip) * scale, 0, minHalf);
        float thickness = MathF.Max(0, Finite(state.CoreThicknessDip)) * scale;
        bool line = style == EdgeStyle.Line;
        float shoulder = GlowProfile.ShoulderFor(line ? 0 : thickness, scale);
        float lineWidth = line ? GlowProfile.LineFor(thickness, scale) : 0;
        float pulse = Clamp01(state.Pulse);
        // The Flow movement, cleaned as GlowConstants.Create cleans it for the shader. Without an amount it draws
        // nothing, so its comets and ripples are left out of the frame and can't make it redraw.
        float amount = Clamp01(state.Flow.Amount);
        bool flowing = amount > 0;

        var frame = new Frame(corner, shoulder, spread, lineWidth, Clamp01(state.Intensity), pulse,
            Fraction(state.Phase), Clamp01(state.Visibility), gradientVersion, amount,
            flowing ? Fraction(state.Flow.CometPhase) : 0,
            flowing ? Clean(state.Flow.Ripple0) : default, flowing ? Clean(state.Flow.Ripple1) : default,
            flowing ? Clean(state.Flow.Ripple2) : default, flowing ? Clean(state.Flow.Ripple3) : default);
        if (drawn && frame == last) return false;

        if (corner != shapeCorner || shoulder != shapeShoulder || !(MathF.Abs(spread - shapeSpread) <= SpreadCacheTolerance * shapeSpread))
            BuildShape(corner, shoulder, spread, minHalf);

        // Per frame: the light is flat at the edge and fades as exp(-(sqrt(depth^2 + shoulder^2) - shoulder) / spread),
        // and the beat widens the shoulder and the spread alike (doc 04 §3 step 2). The spread's floor is the shader's
        // (K7 floored it at a whole pixel, which at about a quarter pixel per DIP drew Glow 0's hairline up to about 40
        // code values brighter than the shader does at this scale). The gain scales the whole profile, as in the shader,
        // and rises with the Flow amount.
        float pulsedShoulder = GlowProfile.PulsedShoulder(shoulder, pulse);
        float pulsedSpread = GlowProfile.PulsedSpread(spread, pulse);
        float gain = GlowProfile.Gain(frame.Intensity, pulse, amount);
        float visibility = frame.Visibility;
        // Beyond this depth the light (or the Line style's halo) is under half a code value: peak gain visibility
        // e^(-eased / spread) < 1/510 where the eased distance sqrt(depth^2 + w^2) - w passes h = spread ln(strongest),
        // that is where depth^2 = h (h + 2w). The line itself ends at its width plus the softness. The Flow movement
        // only ever dims the light, so the bound holds with it.
        float peak = line ? GlowProfile.HaloPeak : GlowProfile.GlowPeak;
        float strongest = peak * gain * visibility * 510;
        float eased = strongest > 1 ? pulsedSpread * MathF.Log(strongest) : -1;
        float cutoff = eased > 0 ? MathF.Sqrt(eased * (eased + 2 * pulsedShoulder)) : -1;
        float lineEnd = lineWidth + GlowProfile.LineSoftnessPx;
        float phase = frame.Phase;

        int[] pixels = Pixels;
        for (int p = 0; p < pixels.Length; p++)
        {
            float depth = glowDepth[p];
            if (depth > cutoff && (!line || lineDepth[p] >= lineEnd))
            {
                pixels[p] = 0;
                continue;
            }
            float profile = line
                ? GlowProfile.LineAndHalo(lineDepth[p], lineWidth, depth, pulsedShoulder, pulsedSpread)
                : GlowProfile.Glow(depth, pulsedShoulder, pulsedSpread);
            float alpha = profile * gain * visibility;
            if (alpha < 0.5f / 255)
            {
                pixels[p] = 0;
                continue;
            }
            // The Flow movement (GlowProfile.Motion, as the shader computes it): the comets and the ripples along the
            // perimeter, blended to their mean deep inside where the colors blend to theirs, faded in by the amount.
            if (flowing)
            {
                alpha *= Motion(frame, perimeter[p], meanBlend[p]);
                if (alpha < 0.5f / 255)
                {
                    pixels[p] = 0;
                    continue;
                }
            }
            int index = ((int)((perimeter[p] + phase) * LutSize) & LutSize - 1) * 3;
            float blend = meanBlend[p];
            float red = lut[index] + (meanR - lut[index]) * blend;
            float green = lut[index + 1] + (meanG - lut[index + 1]) * blend;
            float blue = lut[index + 2] + (meanB - lut[index + 2]) * blend;
            float a255 = alpha * 255;
            pixels[p] = (int)(a255 + 0.5f) << 24 | (int)(red * a255 + 0.5f) << 16 | (int)(green * a255 + 0.5f) << 8 | (int)(blue * a255 + 0.5f);
        }
        last = frame;
        drawn = true;
        return true;
    }

    // The per-pixel quantities that depend only on the shape (doc 04 §3 steps 1-5, as in Glow.hlsl and GlowConstants).
    private void BuildShape(float corner, float shoulder, float spread, float minHalf)
    {
        shapeCorner = corner;
        shapeShoulder = shoulder;
        shapeSpread = spread;
        float colorRadius = MathF.Min(MathF.Max(MathF.Max(corner, 6 * spread), 2 * shoulder), minHalf);
        float glowRadius = GlowProfile.GlowRadius(corner, spread, shoulder, minHalf);
        for (int y = 0; y < Height; y++)
        {
            float py = y + 0.5f;
            for (int x = 0; x < Width; x++)
            {
                float px = x + 0.5f;
                int p = y * Width + x;
                glowDepth[p] = GlowProfile.Depth(px, py, Width, Height, glowRadius);
                // The screen's own shape, which the overlay's line follows (no notch on Windows: four corners alike).
                lineDepth[p] = GlowProfile.Depth(px, py, Width, Height, corner, corner);
                perimeter[p] = GlowProfile.PerimeterT(px, py, Width, Height, colorRadius);
                meanBlend[p] = SmoothStep(0.5f * colorRadius, colorRadius, GlowProfile.EdgeDepth(px, py, Width, Height));
            }
        }
    }

    // GlowProfile.Motion for a pixel at perimeter coordinate t whose color has blended `deep` of the way to the mean:
    // GlowProfile.Flow (the comets over the dimmer edge, each ripple on top), blended to FlowMean and faded in by the
    // amount, step for step as the reference and the shader compute it.
    private static float Motion(in Frame frame, float t, float deep)
    {
        float m = GlowProfile.FlowFloor + (1 - GlowProfile.FlowFloor) * GlowProfile.Comets(t, frame.CometPhase);
        m = 1 - (1 - m) * (1 - RippleAt(t, frame.Ripple0));
        m = 1 - (1 - m) * (1 - RippleAt(t, frame.Ripple1));
        m = 1 - (1 - m) * (1 - RippleAt(t, frame.Ripple2));
        m = 1 - (1 - m) * (1 - RippleAt(t, frame.Ripple3));
        m += (GlowProfile.FlowMean - m) * deep;
        return 1 + (m - 1) * frame.FlowAmount;
    }

    private static float RippleAt(float t, in Ripple ripple) =>
        GlowProfile.RippleAt(t, ripple.Origin, ripple.Radius, ripple.Strength);

    // A ripple as GlowConstants hands it to the shader (GlowRipple.From): its origin a fraction, its radius at least 0,
    // its strength 0..1; not finite is 0.
    private static Ripple Clean(Ripple ripple) => new(Fraction(ripple.Origin),
        float.IsFinite(ripple.Radius) ? MathF.Max(ripple.Radius, 0) : 0, Clamp01(ripple.Strength));

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3 - 2 * t);
    }

    // Linear to sRGB-encoded, as the shader does before premultiplying (DWM reads 8-bit surfaces as sRGB).
    private static float Encode(float linear)
    {
        float c = Clamp01(linear);
        return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;

    private static float Clamp01(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static float Fraction(float value) => float.IsFinite(value) ? value - MathF.Floor(value) : 0;

    // Everything a frame's pixels depend on; in the Flow movement where its comets and ripples are, so a moving comet
    // redraws.
    private readonly record struct Frame(float Corner, float Shoulder, float Spread, float LineWidth, float Intensity,
        float Pulse, float Phase, float Visibility, int GradientVersion, float FlowAmount, float CometPhase,
        Ripple Ripple0, Ripple Ripple1, Ripple Ripple2, Ripple Ripple3);
}
