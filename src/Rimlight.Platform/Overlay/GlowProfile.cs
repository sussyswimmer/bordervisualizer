namespace Rimlight.Platform.Overlay;

/// <summary>
/// The glow's edge profile: how bright the light is at a given distance in from the screen edge, before the light's
/// gain (<see cref="Gain"/>: Intensity and the beat) and Visibility (doc 04 §3 step 2, reworked after Maxwell's first
/// run: "a very obvious line or bar around the screen ... I want it to fade in more"). <c>Glow.hlsl</c> computes the
/// same per pixel, and the Settings preview (<c>GlowRaster</c>) calls these methods, so the two agree. Pure math: any
/// thread, no allocation.
/// </summary>
/// <remarks>
/// <para>Doc 04 drew a solid core line (alpha 1 for the Thickness setting) and then
/// <c>max(core, 0.85 exp(-d / spread))</c>, so alpha fell from 1 to about 0.6 within two pixels at the core's inner
/// edge: a bar. Now two lights add, each brightest at the edge and fading exponentially inward:</para>
/// <list type="bullet">
/// <item>the <b>glow</b>, <see cref="GlowPeak"/> × exp(-d / spread), reaching as far as the Glow setting says
/// (<see cref="SpreadFor"/>);</item>
/// <item>a soft <b>band</b>, exp(-d / (Thickness / 2)): most of its light lies within the Thickness setting, it has
/// no edge of its own, and Thickness 0 leaves the glow alone.</item>
/// </list>
/// <para>They combine like light on a screen, <c>1 - (1 - glow)(1 - band)</c> (<see cref="Combine"/>), which never
/// passes 1: the edge peaks at 1 when there is a band, so the gain scales it as Intensity scaled the core.
/// Every term is smooth, so the profile has no step or kink anywhere: it falls monotonically from the edge and, at
/// the default Thickness, with a slope that only ever eases off (no inflection, which the eye reads as a line). A
/// band much wider than the glow eases in over a soft shoulder instead, which is what asking for it means.</para>
/// <para>Intensity × (1 + 0.25 Pulse) can pass 1 (Brightness above 80 % on a strong beat), and alpha can't. Doc 04
/// clamps each pixel, which flattens the brightest ones into a plateau ending in a kink; even a smooth per-pixel knee
/// squashes them into a shoulder where the fade steepens again (on a full beat at Brightness 100 %, up to 17 code
/// values of extra drop between neighbouring pixels), a brighter rim flashing on the beat. So the roll-off is on the
/// gain instead (<see cref="Gain"/>): the profile is only ever scaled, never reshaped, and everything above holds at
/// any brightness and beat. Gains up to 0.9 are unchanged; above that the light grows a little less on the
/// strongest beats, and the beat's push of the glow further in (<see cref="PulsedSpread"/>) is untouched.</para>
/// </remarks>
public static class GlowProfile
{
    /// <summary>The glow's brightness at the very edge, before the gain: doc 04 §3's <c>glow * 0.85</c>.</summary>
    public const float GlowPeak = 0.85f;

    /// <summary>Where <see cref="SoftClip"/> starts to roll the gain off: gains up to this are unchanged.</summary>
    public const float KneeStart = 0.9f;

    /// <summary>
    /// How much further in a full beat pushes the glow: doc 04 §3's <c>SpreadPx * (1 + 0.35 * Pulse)</c>.
    /// </summary>
    public const float BeatPush = 0.35f;

    /// <summary>
    /// The pulsed spread's floor in pixels. <see cref="SpreadFor"/> is at least one DIP, which is half a swap-chain
    /// pixel at half render scale: the floor only guards the division and must not widen that hairline halo.
    /// </summary>
    public const float MinSpreadPx = 0.5f;

    // How much a full beat brightens the light: doc 04 §3's Intensity * (1 + 0.25 * Pulse).
    private const float BeatGain = 0.25f;

    // PRD §1: "Glow: how far the light spills inward. 0 means a hairline; 100 means about 35% of the shorter screen
    // dimension." Spill is taken as three e-folding distances (the glow is down to 5% there). The slider is raised to
    // the power 1.5 so it feels even (K1 squared it: the default 0.45 then reached 25 px at 1080p, which with the core
    // gone read as a tight rim; at 1.5 it is 38 px, a light that fades in rather than a frame). Both ends stay put.
    private const float FullSpill = 0.35f;
    private const float EFoldingsPerSpill = 3;
    private const double SliderPower = 1.5;

    // The band's e-folding distance is half the Thickness setting: it is down to e^-2 (14%) at the nominal width.
    private const float BandScalePerThickness = 0.5f;

    // Below this e-folding distance (an eighth of a pixel) the band is drawn at this width with its light scaled down
    // to match, so BandGain stays finite; it is far too faint to see by then.
    private const float MinBandScalePx = 0.125f;

    /// <summary>
    /// Glow (0..1) to the glow's e-folding distance in pixels on a surface whose shorter side is
    /// <paramref name="shortSidePx"/>; at 0 a hairline halo of one DIP.
    /// </summary>
    /// <param name="glow">The Glow setting, 0..1; clamped, and 0 when not finite.</param>
    /// <param name="shortSidePx">The surface's shorter side in its own pixels.</param>
    /// <param name="pixelsPerDip">The surface's pixels per DIP (DPI scale × render scale).</param>
    public static float SpreadFor(float glow, int shortSidePx, float pixelsPerDip)
    {
        float g = float.IsFinite(glow) ? Math.Clamp(glow, 0, 1) : 0;
        float reach = (float)Math.Pow(g, SliderPower) * FullSpill / EFoldingsPerSpill * shortSidePx;
        return Math.Max(pixelsPerDip, reach);
    }

    /// <summary>
    /// The band's e-folding distance in pixels for a Thickness of <paramref name="thicknessPx"/> pixels.
    /// </summary>
    /// <remarks>
    /// Never below an eighth of a pixel, so the shader never divides by zero; see <see cref="BandGain"/>.
    /// </remarks>
    public static float BandScale(float thicknessPx) =>
        Math.Max(BandScalePerThickness * (float.IsFinite(thicknessPx) ? thicknessPx : 0), MinBandScalePx);

    /// <summary>
    /// The band's brightness factor for a Thickness of <paramref name="thicknessPx"/> pixels: 0 when there is no band.
    /// </summary>
    /// <remarks>
    /// A pixel shows the band's average over its own width, not its value at the centre: for an e-folding distance b
    /// the average of exp(-x / b) over a pixel is its centre value times 2b sinh(1 / 2b). That is about 1 for a band
    /// several pixels wide, and keeps a band thinner than a pixel from vanishing between samples: a 1-DIP band at
    /// 100 % lights the outermost pixel to 0.43 (a centre sample would give 0.37, and 0.14 at half scale). Summed
    /// over pixels, a band's light is always half its thickness, so at half render scale (pixels twice as wide) the
    /// same light lands in half as many pixels and looks the same once the visual stretches it back. That replaces
    /// the old core's screen-pixel fade (K3's CoreFade).
    /// </remarks>
    public static float BandGain(float thicknessPx)
    {
        if (!(thicknessPx > 0) || !float.IsFinite(thicknessPx)) return 0;
        double b = BandScalePerThickness * (double)thicknessPx;
        double drawn = Math.Max(b, MinBandScalePx);
        return (float)(2 * drawn * Math.Sinh(0.5 / drawn) * (b / drawn));
    }

    /// <summary>
    /// The corner radius of the band's distance field: the user's corner radius, but at least two of the band's
    /// e-folding distances (as the glow's field is at least two spreads), and at most <paramref name="maxPx"/>.
    /// </summary>
    /// <remarks>
    /// With square corners the band's iso-lines would meet in a 45-degree crease, a small mitre in every corner (the
    /// solid core hid it by being flat). Rounded, the light bends round the corner.
    /// </remarks>
    public static float BandRadius(float cornerPx, float bandScalePx, float maxPx) =>
        Math.Min(Math.Max(cornerPx, 2 * bandScalePx), maxPx);

    /// <summary>
    /// Distance in from the edge of a <paramref name="width"/> × <paramref name="height"/> surface, for the pixel
    /// centre (<paramref name="x"/>, <paramref name="y"/>), through a field whose corners have radius
    /// <paramref name="radius"/>: 0 at the edge and outside the rounded corners, growing inward (Glow.hlsl step 1).
    /// </summary>
    public static float Depth(float x, float y, float width, float height, float radius)
    {
        // Minus the signed distance to a rounded box centred on the surface (negative inside).
        float qx = MathF.Abs(x - 0.5f * width) - 0.5f * width + radius;
        float qy = MathF.Abs(y - 0.5f * height) - 0.5f * height + radius;
        float outside = MathF.Sqrt(MathF.Max(qx, 0) * MathF.Max(qx, 0) + MathF.Max(qy, 0) * MathF.Max(qy, 0));
        return MathF.Max(-(outside + MathF.Min(MathF.Max(qx, qy), 0) - radius), 0);
    }

    /// <summary>The glow's brightness at <paramref name="distancePx"/> into the glow's field.</summary>
    /// <param name="distancePx">Distance in from the edge, through the glow's rounded field.</param>
    /// <param name="spreadPx">The e-folding distance, after the beat's push; positive.</param>
    public static float Glow(float distancePx, float spreadPx) => GlowPeak * MathF.Exp(-distancePx / spreadPx);

    /// <summary>The band's brightness at <paramref name="distancePx"/> into the band's field, 0..1.</summary>
    /// <param name="distancePx">Distance in from the edge, through the band's rounded field.</param>
    /// <param name="bandScalePx">The band's e-folding distance (<see cref="BandScale"/>).</param>
    /// <param name="bandGain">The band's brightness factor (<see cref="BandGain"/>); 0 for no band.</param>
    /// <remarks>
    /// Clamped to 1: only the corner pixels outside the band's rounded field (distance 0) could pass it, where the
    /// pixel average no longer applies.
    /// </remarks>
    public static float Band(float distancePx, float bandScalePx, float bandGain) =>
        Math.Clamp(bandGain * MathF.Exp(-distancePx / bandScalePx), 0, 1);

    /// <summary>
    /// The glow's e-folding distance on a beat: <paramref name="spreadPx"/> (<see cref="SpreadFor"/>) pushed further
    /// in by <paramref name="pulse"/> (0..1), never under <see cref="MinSpreadPx"/>.
    /// </summary>
    public static float PulsedSpread(float spreadPx, float pulse) =>
        Math.Max(spreadPx * (1 + BeatPush * pulse), MinSpreadPx);

    /// <summary>
    /// Glow and band together, 0..1: they add like light on a screen and never pass 1. Smooth, and brighter whenever
    /// either light is (while the other is under 1): a <c>max</c> would hand the light from one to the other with a
    /// kink in the slope, which the eye sees as a line (a Mach band).
    /// </summary>
    public static float Combine(float glow, float band) => 1 - (1 - glow) * (1 - band);

    /// <summary>
    /// The light's overall gain, which scales the whole profile: Intensity × the beat's (1 + 0.25 Pulse), rolled off
    /// by <see cref="SoftClip"/>. It stays under 1, so alpha (profile × gain × Visibility) never needs clipping.
    /// </summary>
    /// <param name="intensity">The light state's Intensity, 0..1.</param>
    /// <param name="pulse">The light state's Pulse, 0..1.</param>
    public static float Gain(float intensity, float pulse) => SoftClip(intensity * (1 + BeatGain * pulse));

    /// <summary>
    /// A gain <paramref name="x"/> ≥ 0 rolled off below 1: <paramref name="x"/> itself up to <see cref="KneeStart"/>,
    /// then a smooth roll-off that approaches 1 (0.95 at 1, 0.978 at 1.25, the largest gain) with no kink where it
    /// starts, so a beat's brightening eases in and out over time instead of stopping dead.
    /// </summary>
    public static float SoftClip(float x)
    {
        float over = Math.Max(x - KneeStart, 0) / (1 - KneeStart);
        return Math.Min(x, KneeStart) + (1 - KneeStart) * over / (1 + over);
    }
}
