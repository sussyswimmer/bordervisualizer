namespace Rimlight.Core.Color;

/// <summary>A color in Oklab (Björn Ottosson, 2020): L is perceptual lightness 0..1, A and B are the opponent axes.</summary>
/// <param name="L">Lightness, 0 (black) to 1 (white).</param>
/// <param name="A">Green (−) to red (+).</param>
/// <param name="B">Blue (−) to yellow (+).</param>
/// <remarks>Conversions use Ottosson's published linear-sRGB matrices, so sRGB white maps to (1, 0, 0).</remarks>
internal readonly record struct Oklab(float L, float A, float B)
{
    /// <summary>Colorfulness: the distance from the neutral axis, √(A² + B²).</summary>
    public float Chroma => MathF.Sqrt(A * A + B * B);

    /// <summary>Converts linear sRGB to Oklab. Out-of-range input converts too (the cube root is odd).</summary>
    /// <param name="r">Linear red.</param>
    /// <param name="g">Linear green.</param>
    /// <param name="b">Linear blue.</param>
    /// <returns>The Oklab color.</returns>
    public static Oklab FromLinearSrgb(float r, float g, float b)
    {
        float l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        float m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        float s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
        return new Oklab(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    /// <summary>Converts linear sRGB to Oklab.</summary>
    /// <param name="color">Linear color.</param>
    /// <returns>The Oklab color.</returns>
    public static Oklab FromLinearSrgb(Rgb color) => FromLinearSrgb(color.R, color.G, color.B);

    /// <summary>Converts to linear sRGB. Colors outside the sRGB gamut give channels outside 0..1; nothing is clamped.</summary>
    /// <returns>The linear color.</returns>
    public Rgb ToLinearSrgb()
    {
        float l = L + 0.3963377774f * A + 0.2158037573f * B;
        float m = L - 0.1055613458f * A - 0.0638541728f * B;
        float s = L - 0.0894841775f * A - 1.2914855480f * B;
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;
        return new Rgb(
            4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
            -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
            -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
    }

    /// <summary>Converts to cylindrical OkLCh.</summary>
    /// <returns>The same color as lightness, chroma and hue.</returns>
    public OkLch ToLch()
    {
        float hue = MathF.Atan2(B, A) * (180f / MathF.PI);
        if (hue < 0) hue += 360f;
        return new OkLch(L, Chroma, hue >= 360f ? 0f : hue); // −1e-6° + 360 rounds to 360 in float
    }

    /// <summary>Linear interpolation in Oklab, the perceptual straight line between two colors: x + (y − x) × t.</summary>
    /// <param name="x">The color at t = 0.</param>
    /// <param name="y">The color at t = 1.</param>
    /// <param name="t">The position along the line; not clamped.</param>
    /// <returns>The interpolated color.</returns>
    public static Oklab Lerp(Oklab x, Oklab y, float t) =>
        new(x.L + (y.L - x.L) * t, x.A + (y.A - x.A) * t, x.B + (y.B - x.B) * t);

    /// <summary>Euclidean distance in Oklab, the perceptual color difference (about 0.02 is just noticeable).</summary>
    /// <param name="x">First color.</param>
    /// <param name="y">Second color.</param>
    /// <returns>The distance.</returns>
    public static float Distance(Oklab x, Oklab y) => MathF.Sqrt(DistanceSquared(x, y));

    /// <summary>Squared Euclidean distance in Oklab.</summary>
    /// <param name="x">First color.</param>
    /// <param name="y">Second color.</param>
    /// <returns>The squared distance.</returns>
    public static float DistanceSquared(Oklab x, Oklab y)
    {
        float dl = x.L - y.L, da = x.A - y.A, db = x.B - y.B;
        return dl * dl + da * da + db * db;
    }
}

/// <summary>Oklab in cylindrical form: lightness, chroma and hue.</summary>
/// <param name="L">Lightness, 0..1.</param>
/// <param name="C">Chroma, 0 for neutral colors; about 0.32 at most inside sRGB.</param>
/// <param name="H">Hue angle in degrees, 0..360 (red is about 29°, blue about 264°). Meaningless when C is 0.</param>
internal readonly record struct OkLch(float L, float C, float H)
{
    /// <summary>Converts to Oklab.</summary>
    /// <returns>The Oklab color.</returns>
    public Oklab ToOklab()
    {
        float radians = H * (MathF.PI / 180f);
        return new Oklab(L, C * MathF.Cos(radians), C * MathF.Sin(radians));
    }
}
