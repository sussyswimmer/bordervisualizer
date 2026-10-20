namespace Rimlight.Core.Color;

/// <summary>
/// Doc 05 §2 step 6, "glow-ify": turns a color taken from artwork into a color that reads as light rather than
/// paint, then maps it into the sRGB gamut.
/// </summary>
/// <remarks>
/// <para>In OkLCh: a near-neutral color, with chroma up to <see cref="NeutralChroma"/>, keeps its chroma, so a grey,
/// or a black-and-white cover with the faint cast a JPEG or a scan often gives it, glows as a soft white or grey with
/// no invented hue. A clearly colored one, from <see cref="ColorfulChroma"/> on, is raised to at least
/// <see cref="MinChroma"/>. In between, the raise eases in (a smoothstep, see <see cref="TargetChroma"/>), so a slightly
/// stronger tint never jumps to a vivid color. Lightness is clamped to [<see cref="MinLightness"/>,
/// <see cref="MaxLightness"/>]. If the result is outside sRGB, chroma is reduced at constant lightness and hue
/// (bisection) until it fits, so every channel of the result is in 0..1. Pure functions on value types: thread-safe
/// and allocation-free.</para>
/// <para><i>Maxwell's feedback:</i> the raise used to start at chroma 0.03, so a black-and-white cover with a slight
/// cool cast (chroma 0.03–0.05) glowed light and dark blue.</para>
/// </remarks>
internal static class Glow
{
    /// <summary>Up to this chroma a color is near-neutral and keeps its chroma: a grey, or a black-and-white cover's
    /// faint cast, glows soft white or grey and is never given a hue. Oklab's just-noticeable difference is about 0.02,
    /// so this is a tint, not a color.</summary>
    public const float NeutralChroma = 0.05f;

    /// <summary>From this chroma on a color is clearly colored and is raised to at least <see cref="MinChroma"/>.
    /// Between <see cref="NeutralChroma"/> and here the raise eases in.</summary>
    public const float ColorfulChroma = 0.10f;

    /// <summary>Colored inputs are raised to at least this chroma (before gamut mapping).</summary>
    public const float MinChroma = 0.12f;

    /// <summary>Lowest output lightness: darker light would just look dim.</summary>
    public const float MinLightness = 0.55f;

    /// <summary>Highest output lightness: brighter colors wash out to white.</summary>
    public const float MaxLightness = 0.85f;

    // 24 halvings of a chroma ≤ 0.4 leave an error below 3e-8, far under one 8-bit step.
    private const int BisectionSteps = 24;

    /// <summary>Glow-ifies a color: the target lightness, chroma and hue before gamut mapping.</summary>
    /// <param name="color">An artwork color (a cluster centroid or a derived color).</param>
    /// <returns>The glow color in OkLCh, possibly outside sRGB.</returns>
    public static OkLch Target(Oklab color)
    {
        OkLch lch = color.ToLch();
        return new OkLch(Math.Clamp(lch.L, MinLightness, MaxLightness), TargetChroma(lch.C), lch.H);
    }

    /// <summary>The glow's chroma for an artwork color's chroma: unchanged up to <see cref="NeutralChroma"/>, at least
    /// <see cref="MinChroma"/> from <see cref="ColorfulChroma"/> on, and in between c + (<see cref="MinChroma"/> − c) ×
    /// smoothstep(<see cref="NeutralChroma"/>, <see cref="ColorfulChroma"/>, c). Continuous, smooth and
    /// non-decreasing: 0.06 gives 0.066, 0.075 gives 0.0975 and 0.09 gives 0.117.</summary>
    /// <param name="chroma">The artwork color's OkLCh chroma, 0 or more.</param>
    /// <returns>The glow's chroma before gamut mapping, at least <paramref name="chroma"/>.</returns>
    public static float TargetChroma(float chroma)
    {
        if (chroma >= MinChroma) return chroma;
        float t = Math.Clamp((chroma - NeutralChroma) / (ColorfulChroma - NeutralChroma), 0f, 1f);
        return chroma + (MinChroma - chroma) * (t * t * (3 - 2 * t));
    }

    /// <summary>Glow-ifies a color and maps it into sRGB.</summary>
    /// <param name="color">An artwork color.</param>
    /// <returns>Linear RGB with every channel in 0..1.</returns>
    public static Rgb ToLight(Oklab color) => MapToSrgb(Target(color));

    /// <summary>Maps an OkLCh color into the sRGB gamut by reducing its chroma, keeping lightness and hue.</summary>
    /// <param name="color">Any OkLCh color.</param>
    /// <returns>Linear RGB with every channel in 0..1: the in-gamut color with the most chroma up to the input's.</returns>
    public static Rgb MapToSrgb(OkLch color)
    {
        Rgb full = color.ToOklab().ToLinearSrgb();
        if (InGamut(full)) return full;
        float low = 0, high = color.C;
        for (int i = 0; i < BisectionSteps; i++)
        {
            float mid = 0.5f * (low + high);
            if (InGamut(new OkLch(color.L, mid, color.H).ToOklab().ToLinearSrgb())) low = mid;
            else high = mid;
        }
        // At chroma `low` the color is in gamut up to float error (or L itself is outside 0..1); clamp the residue.
        Rgb mapped = new OkLch(color.L, low, color.H).ToOklab().ToLinearSrgb();
        return new Rgb(Math.Clamp(mapped.R, 0f, 1f), Math.Clamp(mapped.G, 0f, 1f), Math.Clamp(mapped.B, 0f, 1f));
    }

    /// <summary>Whether every channel of a linear color is in 0..1.</summary>
    /// <param name="color">Linear color.</param>
    /// <returns>True inside the sRGB gamut.</returns>
    public static bool InGamut(Rgb color) =>
        color.R is >= 0f and <= 1f && color.G is >= 0f and <= 1f && color.B is >= 0f and <= 1f;
}
