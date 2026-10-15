using Rimlight.Core;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Draws the glow on the CPU for the Appearance page's preview: a port of <c>Glow.hlsl</c> (doc 04 §3) into a small
/// premultiplied BGRA buffer, as if the card were a 1280-DIP-wide screen. Everything that depends only on the shape is
/// cached per pixel and recomputed when the shape changes, so a frame costs one exp per lit pixel. No allocation
/// after construction. Pure code; any one thread.
/// </summary>
internal sealed class GlowRaster
{
    /// <summary>The width, in DIPs, of the screen the preview stands for: thickness and corner radius scale by it.</summary>
    public const float ScreenWidthDip = 1280;

    // Same mapping as GlowConstants: Glow 0..1 spills up to 35% of the shorter side, as three e-folding distances.
    private const float FullSpill = 0.35f;
    private const float EFoldingsPerSpill = 3;
    private const float HalfPi = 1.5707963f;
    // Colors around the perimeter, looked up rather than filtered: 1024 steps put them within about a pixel of the
    // shader's at this size.
    private const int LutSize = 1024;
    // The shape cache is rebuilt when the spread moves more than this fraction (an engine may vary it with the music;
    // only the corner rounding of the glow and color fields depends on it, which nobody can see move by 10%).
    private const float SpreadCacheTolerance = 0.1f;

    private readonly float[] coreAlpha;  // the solid line's coverage
    private readonly float[] glowDepth;  // distance into the rounded glow field
    private readonly float[] perimeter;  // perimeter coordinate t in [0, 1)
    private readonly float[] meanBlend;  // how far the color has blended to the mean (deep inside)
    private readonly float[] lut = new float[LutSize * 3]; // the gradient around the perimeter, sRGB-encoded
    private float meanR, meanG, meanB; // the gradient's mean, sRGB-encoded
    private int gradientVersion;

    private float shapeCorner = float.NaN, shapeCore = float.NaN, shapeSpread = float.NaN;
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
        coreAlpha = new float[width * height];
        glowDepth = new float[width * height];
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
    public bool Render(in LightState state, float cornerRadiusDip)
    {
        float scale = Width / ScreenWidthDip; // pixels per DIP
        int shortSide = Math.Min(Width, Height);
        float minHalf = 0.5f * shortSide;
        float glow = Clamp01(state.Spread);
        float spread = MathF.Max(scale, glow * glow * FullSpill / EFoldingsPerSpill * shortSide);
        float corner = Math.Clamp(Finite(cornerRadiusDip) * scale, 0, minHalf);
        float core = MathF.Max(0, Finite(state.CoreThicknessDip)) * scale;
        float pulse = Clamp01(state.Pulse);

        var frame = new Frame(corner, core, spread, Clamp01(state.Intensity), pulse, Fraction(state.Phase),
            Clamp01(state.Visibility), gradientVersion);
        if (drawn && frame == last) return false;

        if (corner != shapeCorner || core != shapeCore || !(MathF.Abs(spread - shapeSpread) <= SpreadCacheTolerance * shapeSpread))
            BuildShape(corner, core, spread, minHalf);

        // Per frame: the glow falls off as exp(-depth / spread), and the beat pushes it further in (doc 04 §3 step 2).
        float pulsedSpread = MathF.Max(spread * (1 + 0.35f * pulse), 1);
        float gain = frame.Intensity * (1 + 0.25f * pulse);
        float visibility = frame.Visibility;
        // Beyond this depth the glow is under half a code value (0.85 e^(-depth/spread) gain visibility < 1/510), so
        // pixels outside the core are transparent there.
        float strongest = 0.85f * gain * visibility * 510;
        float cutoff = strongest > 1 ? pulsedSpread * MathF.Log(strongest) : -1;
        float phase = frame.Phase;

        int[] pixels = Pixels;
        for (int p = 0; p < pixels.Length; p++)
        {
            float coverage = coreAlpha[p];
            float depth = glowDepth[p];
            if (coverage <= 0 && depth > cutoff)
            {
                pixels[p] = 0;
                continue;
            }
            float shape = MathF.Max(coverage, MathF.Exp(-depth / pulsedSpread) * 0.85f);
            float alpha = MathF.Min(MathF.Min(shape, 1) * gain, 1) * visibility;
            if (alpha < 0.5f / 255)
            {
                pixels[p] = 0;
                continue;
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
    private void BuildShape(float corner, float core, float spread, float minHalf)
    {
        shapeCorner = corner;
        shapeCore = core;
        shapeSpread = spread;
        float colorRadius = MathF.Min(MathF.Max(MathF.Max(corner, 6 * spread), 2 * core), minHalf);
        float glowRadius = MathF.Min(MathF.Max(corner, 2 * spread), minHalf);
        float halfX = 0.5f * Width, halfY = 0.5f * Height;
        float coreReach = Clamp01(core); // no line at thickness 0
        for (int y = 0; y < Height; y++)
        {
            float py = y + 0.5f;
            for (int x = 0; x < Width; x++)
            {
                float px = x + 0.5f;
                int p = y * Width + x;
                float d = -SdRoundBox(px - halfX, py - halfY, halfX, halfY, corner);
                coreAlpha[p] = (1 - SmoothStep(core - 1, core + 1, d)) * coreReach;
                glowDepth[p] = MathF.Max(-SdRoundBox(px - halfX, py - halfY, halfX, halfY, glowRadius), 0);
                perimeter[p] = PerimeterT(px, py, Width, Height, colorRadius);
                float edgeDepth = MathF.Min(MathF.Min(px, py), MathF.Min(Width - px, Height - py));
                meanBlend[p] = SmoothStep(0.5f * colorRadius, colorRadius, edgeDepth);
            }
        }
    }

    // Signed distance to a rounded box centred at the origin: negative inside.
    private static float SdRoundBox(float x, float y, float halfX, float halfY, float radius)
    {
        float qx = MathF.Abs(x) - halfX + radius;
        float qy = MathF.Abs(y) - halfY + radius;
        float outside = MathF.Sqrt(MathF.Max(qx, 0) * MathF.Max(qx, 0) + MathF.Max(qy, 0) * MathF.Max(qy, 0));
        return outside + MathF.Min(MathF.Max(qx, qy), 0) - radius;
    }

    // Arc length clockwise from the top-left corner along a rounded rectangle of corner radius r (Glow.hlsl PerimeterT).
    private static float PerimeterT(float px, float py, float width, float height, float r)
    {
        float topLen = width - 2 * r;
        float sideLen = height - 2 * r;
        float arcLen = HalfPi * r;
        float total = 2 * (topLen + sideLen) + 4 * arcLen;
        bool left = px < r, right = px > width - r, top = py < r, bottom = py > height - r;
        float s;
        if (top && right)
            s = topLen + arcLen * QuarterAngle(px - (width - r), -(py - r)) / HalfPi;
        else if (bottom && right)
            s = topLen + arcLen + sideLen + arcLen * QuarterAngle(py - (height - r), px - (width - r)) / HalfPi;
        else if (bottom && left)
            s = 2 * topLen + 2 * arcLen + sideLen + arcLen * QuarterAngle(-(px - r), py - (height - r)) / HalfPi;
        else if (top && left)
            s = 2 * topLen + 3 * arcLen + 2 * sideLen + arcLen * QuarterAngle(-(py - r), -(px - r)) / HalfPi;
        else
        {
            float dTop = py, dRight = width - px, dBottom = height - py, dLeft = px;
            float nearest = MathF.Min(MathF.Min(dTop, dRight), MathF.Min(dBottom, dLeft));
            if (nearest == dTop) s = px - r;
            else if (nearest == dRight) s = topLen + arcLen + (py - r);
            else if (nearest == dBottom) s = topLen + 2 * arcLen + sideLen + (width - r - px);
            else s = 2 * topLen + 3 * arcLen + sideLen + (height - r - py);
        }
        return Fraction((s + 0.5f * arcLen) / total);
    }

    private static float QuarterAngle(float y, float x) => MathF.Atan2(y, MathF.Max(x, 1e-5f));

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

    // Everything a frame's pixels depend on.
    private readonly record struct Frame(float Corner, float Core, float Spread, float Intensity, float Pulse, float Phase,
        float Visibility, int GradientVersion);
}
