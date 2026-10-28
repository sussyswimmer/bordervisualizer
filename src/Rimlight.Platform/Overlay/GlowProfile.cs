using Rimlight.Core;

namespace Rimlight.Platform.Overlay;

/// <summary>
/// The glow's edge profile: how bright the light is at a given distance in from the screen edge, before the light's
/// gain (<see cref="Gain"/>: Intensity and the beat) and Visibility (doc 04 §3 step 2, reworked twice after Maxwell's
/// runs: "a very obvious line or bar around the screen ... I want it to fade in more", then "a thin bright line right
/// at the screen edge, then the glow fades in behind it"); the bright line he later asked for as an option
/// (<see cref="EdgeStyle.Line"/>); and the way round a camera notch (<see cref="NotchDistance"/>). <c>Glow.hlsl</c>
/// and the Mac's <c>glow_math.h</c> compute the same per pixel (<see cref="Alpha"/>), and the Settings preview
/// (<c>GlowRaster</c>) calls these methods, so they all agree. Pure math: any thread, no allocation.
/// </summary>
/// <remarks>
/// <para>One light, flat at the edge and fading smoothly inward:
/// <c>GlowPeak × exp(-(sqrt(d² + w²) - w) / s)</c> (<see cref="Glow"/>), where d is the distance in from the edge,
/// w the <b>shoulder</b> (<see cref="ShoulderFor"/>, the Thickness setting) and s the <b>spread</b>
/// (<see cref="SpreadFor"/>, the Glow setting). The square root is a hyperbola: about d² / 2w near the edge, so the
/// light starts with zero slope, in a soft shoulder that bends into the fade over about w; and about d - w further in,
/// so the shoulder eases into the usual exponential fade, only w further in. It is smooth everywhere, monotonic, and
/// bends from flat to fading exactly once (one inflection), so there is no step, kink or ring for the eye to call a
/// line.</para>
/// <para>The part that looks even grows more slowly than w: the light stays within 3 % of the edge for about
/// sqrt(0.06 w s), 4 px at the defaults on a 1080p screen and 10 px at Thickness 40, and when w is much larger than s
/// (a large Thickness on a narrow Glow) the shoulder is a soft half-Gaussian band, sigma sqrt(w s). A knee sharp
/// enough to stay flat all the way to w would be a bar again.</para>
/// <para>Why not an exponential from the edge: <c>exp(-d / s)</c> is steepest at d = 0, and round 1's soft band
/// (<c>exp(-d / (Thickness / 2))</c>, screen-blended with the glow) was steeper still, about 5 % of the edge's
/// brightness per pixel at the defaults on a 1080p screen at 100 %. That packs the brightest light into the outermost
/// few pixels, right against the dark bezel: a thin bright line, with the glow fading in behind it, however smooth the
/// curve. The shoulder's slope is zero at the edge and never more than the glow's own (<c>GlowPeak / s</c>), so the
/// outermost pixels match their neighbours (within 3 % over the first 4 px at the defaults, against 13 % in round 1)
/// and the light simply fades in.</para>
/// <para>Thickness 0 still has a shoulder of one DIP (<see cref="MinShoulderDip"/>), so the slope at the edge stays
/// zero; it then fades almost exactly like the plain exponential glow, and Thickness 0 and 1 draw the same light. The
/// beat widens the shoulder and the spread by the same factor (<see cref="PulsedShoulder"/>,
/// <see cref="PulsedSpread"/>): the whole light swells inward on a beat, it never gains a rim.</para>
/// <para>Intensity × (1 + 0.25 Pulse) can pass 1 (Brightness above 80 % on a strong beat), and alpha can't. Doc 04
/// clamps each pixel, which flattens the brightest ones into a plateau ending in a kink; even a smooth per-pixel knee
/// squashes them into a shoulder where the fade steepens again, a brighter rim flashing on the beat. So the roll-off
/// is on the gain instead (<see cref="Gain"/>): the profile is only ever scaled, never reshaped, and everything above
/// holds at any brightness and beat. Gains up to 0.9 are unchanged; above that the light grows a little less on the
/// strongest beats.</para>
/// <para><b>The Line style</b> (<see cref="EdgeStyle.Line"/>). Maxwell, with a photo of a MacBook whose screen edge
/// has a crisp neon line hugging it, round the camera notch, with a dim glow inside: "Please I want it to look like
/// this or have an option to look like this and detect if theres a notch and go around it for the lighting". Round 2
/// took a thin bright rim out of the default look at his request, so this is an option and the soft glow stays the
/// default. The line (<see cref="Line"/>) is the Thickness wide (<see cref="LineFor"/>, at least
/// <see cref="MinLineDip"/>), as bright as the gain allows (<see cref="LinePeak"/>), with an anti-aliased inner edge
/// <see cref="LineSoftnessPx"/> either side of its width; its width never pulses, the beat brightens it through the
/// gain. It follows the screen's own shape, whose top corners may be rounder than its bottom ones
/// (<see cref="Depth(float, float, float, float, float, float)"/>). Inside it a halo (<see cref="Halo"/>): the glow
/// above with the narrowest shoulder, the Glow setting's spread and the same beat push, at <see cref="HaloPeak"/>. The
/// two combine like light through two filters, <c>1 - (1 - line)(1 - halo)</c> (<see cref="LineAndHalo"/>), so the
/// line never passes <see cref="LinePeak"/> and the halo carries on past it.</para>
/// <para><b>The notch</b> (both styles): the camera housing hanging from the top edge of a notched screen, a box from
/// above the screen down to the notch's height with rounded lower corners (<see cref="NotchDistance"/>). Every depth
/// becomes the smooth minimum of the depth in from the edge and the distance from the notch (<see cref="AroundNotch"/>,
/// <see cref="SmoothMin"/>), so the light runs down the notch's side, along its bottom and up again, and the inside
/// corners where the notch meets the top edge are rounded like the notch's own "ears". The colours stay on the outer
/// rectangle's path, so the notch's outline takes the top edge's colours. With no notch nothing changes.</para>
/// </remarks>
public static class GlowProfile
{
    /// <summary>The light's brightness at the very edge, before the gain: doc 04 §3's <c>glow * 0.85</c>.</summary>
    public const float GlowPeak = 0.85f;

    /// <summary>
    /// The Line style's line, before the gain: as bright as the light gets, so it reads as the edge in front of the
    /// halo.
    /// </summary>
    public const float LinePeak = 1f;

    /// <summary>
    /// The Line style's halo at the very edge, before the gain: dimmer than the glow (<see cref="GlowPeak"/>), so the
    /// line stands out in front of it, as in Maxwell's photo.
    /// </summary>
    public const float HaloPeak = 0.5f;

    /// <summary>The narrowest line, in DIPs: what Thickness 0 and 1 draw in the Line style.</summary>
    public const float MinLineDip = 1f;

    /// <summary>
    /// Half the width of the line's anti-aliased inner edge, in the surface's own pixels: the line fades out from its
    /// width minus this to its width plus this, so a one-pixel line covers its pixel and a wider one ends crisply,
    /// without stair steps in the corners.
    /// </summary>
    public const float LineSoftnessPx = 0.75f;

    /// <summary>
    /// The radius of the notch's lower corners as a share of its height (at most half its width): a 32-point notch
    /// rounds them by about 10 points, as the housing does. The same radius rounds the inside corners where the notch
    /// meets the top edge (<see cref="AroundNotch"/>).
    /// </summary>
    public const float NotchCornerShare = 0.3f;

    /// <summary>
    /// The top corners' radius, in DIPs (points), on a screen with a notch: the notched MacBook screens round their top
    /// corners by about 10 points. A larger Corner radius setting wins.
    /// </summary>
    public const float NotchedDisplayTopCornerDip = 10f;

    /// <summary>Where <see cref="SoftClip"/> starts to roll the gain off: gains up to this are unchanged.</summary>
    public const float KneeStart = 0.9f;

    /// <summary>
    /// How much further in a full beat pushes the light: doc 04 §3's <c>SpreadPx * (1 + 0.35 * Pulse)</c>, applied to
    /// the shoulder too.
    /// </summary>
    public const float BeatPush = 0.35f;

    /// <summary>
    /// The pulsed spread's floor in pixels. <see cref="SpreadFor"/> is at least one DIP, which is half a swap-chain
    /// pixel at half render scale: the floor only guards the division and must not widen that hairline halo.
    /// </summary>
    public const float MinSpreadPx = 0.5f;

    /// <summary>
    /// The narrowest shoulder, in DIPs: what Thickness 0 draws, and Thickness 1 too, so the two look the same. The
    /// slope at the edge is zero for any shoulder above 0; at one DIP the outermost pixel is within 0.3 % of the edge
    /// on the default glow at 100 %, and the light then fades almost exactly like the plain exponential glow. Half a
    /// DIP would tell Thickness 0 from 1, but at half render scale (a quarter pixel on a 100 % monitor) the default
    /// glow would then fall fastest between the outermost two pixels again: round 1's line.
    /// </summary>
    public const float MinShoulderDip = 1f;

    // How much a full beat brightens the light: doc 04 §3's Intensity * (1 + 0.25 * Pulse).
    private const float BeatGain = 0.25f;

    // PRD §1: "Glow: how far the light spills inward. 0 means a hairline; 100 means about 35% of the shorter screen
    // dimension." Spill is taken as three e-folding distances (the glow is down to 5% there). The slider is raised to
    // the power 1.5 so it feels even (K1 squared it: the default 0.45 then reached 25 px at 1080p, which read as a
    // tight rim; at 1.5 it is 38 px, a light that fades in rather than a frame). Both ends stay put.
    private const float FullSpill = 0.35f;
    private const float EFoldingsPerSpill = 3;
    private const double SliderPower = 1.5;

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
    /// The shoulder's width in pixels for a Thickness of <paramref name="thicknessPx"/> pixels: the Thickness itself,
    /// but at least <see cref="MinShoulderDip"/>.
    /// </summary>
    /// <param name="thicknessPx">The Thickness setting in the surface's pixels; 0 when not finite or negative.</param>
    /// <param name="pixelsPerDip">The surface's pixels per DIP (DPI scale × render scale).</param>
    /// <remarks>
    /// It converts at the render scale like every other measure, so at half scale the same light lands in half as
    /// many pixels and looks the same once the visual stretches it back.
    /// </remarks>
    public static float ShoulderFor(float thicknessPx, float pixelsPerDip) =>
        Math.Max(float.IsFinite(thicknessPx) ? Math.Max(thicknessPx, 0) : 0, MinShoulderDip * pixelsPerDip);

    /// <summary>
    /// The corner radius of the light's distance field: the user's corner radius, but at least two e-folding distances
    /// plus the shoulder, and at most <paramref name="maxPx"/>.
    /// </summary>
    /// <remarks>
    /// Inside a rounded box the iso-lines at depth d have corner radius (radius - d), and past the radius they meet in
    /// a 45-degree crease. The light reaches about the shoulder plus a few spreads in, so the radius follows both (K1
    /// rounded the glow's field to two spreads; round 1 rounded a band's field to its own two e-folding distances).
    /// Outside the arc, in the screen's very corner, the depth is 0: the flat top of the shoulder, which the arc now
    /// meets with zero slope instead of a kink.
    /// </remarks>
    public static float GlowRadius(float cornerPx, float spreadPx, float shoulderPx, float maxPx) =>
        Math.Min(Math.Max(cornerPx, 2 * spreadPx + shoulderPx), maxPx);

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

    /// <summary>
    /// The distance eased through the shoulder, <c>sqrt(d² + w²) - w</c>: about d² / 2w within the shoulder (flat at
    /// the edge) and about d - w beyond it.
    /// </summary>
    /// <param name="distancePx">Distance in from the edge, through the light's rounded field; 0 or more.</param>
    /// <param name="shoulderPx">The shoulder, after the beat's push; 0 gives the distance itself.</param>
    public static float EasedDistance(float distancePx, float shoulderPx) =>
        MathF.Sqrt(distancePx * distancePx + shoulderPx * shoulderPx) - shoulderPx;

    /// <summary>
    /// The light's brightness at <paramref name="distancePx"/> into its field, 0..<see cref="GlowPeak"/>: flat at the
    /// edge, then fading exponentially.
    /// </summary>
    /// <param name="distancePx">Distance in from the edge, through the light's rounded field.</param>
    /// <param name="shoulderPx">The shoulder, after the beat's push (<see cref="PulsedShoulder"/>).</param>
    /// <param name="spreadPx">The e-folding distance, after the beat's push (<see cref="PulsedSpread"/>); positive.</param>
    public static float Glow(float distancePx, float shoulderPx, float spreadPx) =>
        GlowPeak * MathF.Exp(-EasedDistance(distancePx, shoulderPx) / spreadPx);

    /// <summary>
    /// The glow's e-folding distance on a beat: <paramref name="spreadPx"/> (<see cref="SpreadFor"/>) pushed further
    /// in by <paramref name="pulse"/> (0..1), never under <see cref="MinSpreadPx"/>.
    /// </summary>
    public static float PulsedSpread(float spreadPx, float pulse) =>
        Math.Max(spreadPx * (1 + BeatPush * pulse), MinSpreadPx);

    /// <summary>
    /// The shoulder on a beat: <paramref name="shoulderPx"/> (<see cref="ShoulderFor"/>) widened by the same factor as
    /// the spread, so a beat swells the whole light inward and keeps its shape.
    /// </summary>
    public static float PulsedShoulder(float shoulderPx, float pulse) => shoulderPx * (1 + BeatPush * pulse);

    /// <summary>
    /// The light's overall gain, which scales the whole profile: Intensity × the beat's (1 + 0.25 Pulse), rolled off
    /// by <see cref="SoftClip"/>. It stays under 1, so alpha (profile × gain × Visibility) never needs clipping.
    /// </summary>
    /// <param name="intensity">The light state's Intensity, 0..1.</param>
    /// <param name="pulse">The light state's Pulse, 0..1.</param>
    public static float Gain(float intensity, float pulse) => SoftClip(intensity * (1 + BeatGain * pulse));

    /// <summary>
    /// The Line style's line width in pixels for a Thickness of <paramref name="thicknessPx"/> pixels: the Thickness
    /// itself, but at least <see cref="MinLineDip"/>.
    /// </summary>
    /// <param name="thicknessPx">The Thickness setting in the surface's pixels; 0 when not finite or negative.</param>
    /// <param name="pixelsPerDip">The surface's pixels per DIP (DPI scale × render scale).</param>
    public static float LineFor(float thicknessPx, float pixelsPerDip) =>
        Math.Max(float.IsFinite(thicknessPx) ? Math.Max(thicknessPx, 0) : 0, MinLineDip * pixelsPerDip);

    /// <summary>
    /// The Line style's line at <paramref name="depthPx"/> in from the screen's edge, 0..<see cref="LinePeak"/>:
    /// <see cref="LinePeak"/> across its width, then an anti-aliased edge from <paramref name="linePx"/> -
    /// <see cref="LineSoftnessPx"/> to <paramref name="linePx"/> + <see cref="LineSoftnessPx"/>
    /// (<c>1 - smoothstep</c>).
    /// </summary>
    /// <param name="depthPx">Distance in from the edge, through the screen's own shape; 0 or more.</param>
    /// <param name="linePx">The line's width (<see cref="LineFor"/>), which the beat doesn't change.</param>
    public static float Line(float depthPx, float linePx) =>
        LinePeak * (1 - SmoothStep(linePx - LineSoftnessPx, linePx + LineSoftnessPx, depthPx));

    /// <summary>
    /// The Line style's halo at <paramref name="distancePx"/> into the light's field, 0..<see cref="HaloPeak"/>: the
    /// glow's own shape (<see cref="Glow"/>) at <see cref="HaloPeak"/>.
    /// </summary>
    /// <param name="distancePx">Distance in from the edge, through the light's rounded field.</param>
    /// <param name="shoulderPx">The narrowest shoulder (<see cref="ShoulderFor"/> of 0), after the beat's push.</param>
    /// <param name="spreadPx">The e-folding distance, after the beat's push; positive.</param>
    public static float Halo(float distancePx, float shoulderPx, float spreadPx) =>
        HaloPeak * MathF.Exp(-EasedDistance(distancePx, shoulderPx) / spreadPx);

    /// <summary>
    /// The Line style's profile before the gain, 0..1: the line in front of the halo, combined like light through two
    /// filters, <c>1 - (1 - line)(1 - halo)</c>.
    /// </summary>
    /// <param name="lineDepthPx">Distance in from the edge through the screen's own shape, for
    /// <see cref="Line"/>.</param>
    /// <param name="linePx">The line's width (<see cref="LineFor"/>).</param>
    /// <param name="haloDistancePx">Distance in from the edge through the light's rounded field, for
    /// <see cref="Halo"/>.</param>
    /// <param name="shoulderPx">The halo's shoulder, after the beat's push.</param>
    /// <param name="spreadPx">The halo's e-folding distance, after the beat's push; positive.</param>
    public static float LineAndHalo(float lineDepthPx, float linePx, float haloDistancePx, float shoulderPx,
        float spreadPx) =>
        1 - (1 - Line(lineDepthPx, linePx)) * (1 - Halo(haloDistancePx, shoulderPx, spreadPx));

    /// <summary>
    /// <see cref="Depth(float, float, float, float, float)"/> through a shape whose top two corners have radius
    /// <paramref name="topRadius"/> and bottom two <paramref name="bottomRadius"/>: the radius of the half the pixel is
    /// in. Both at most half the shorter side, so the two halves meet without a seam.
    /// </summary>
    public static float Depth(float x, float y, float width, float height, float topRadius, float bottomRadius) =>
        Depth(x, y, width, height, y < 0.5f * height ? topRadius : bottomRadius);

    /// <summary>
    /// The radius of a notch's lower corners: <see cref="NotchCornerShare"/> of its height, at most half its width.
    /// </summary>
    public static float NotchRadius(float notchWidthPx, float notchHeightPx) =>
        Math.Min(NotchCornerShare * notchHeightPx, 0.5f * notchWidthPx);

    /// <summary>
    /// Distance from the pixel centre (<paramref name="x"/>, <paramref name="y"/>) to a notch hanging from the top edge
    /// between <paramref name="leftPx"/> and <paramref name="rightPx"/>, down to <paramref name="heightPx"/>, with
    /// lower corners of radius <paramref name="radiusPx"/>: 0 inside it, growing outward.
    /// </summary>
    /// <remarks>
    /// A rounded box centred on the top edge's point under the notch's middle, its half height the notch's height, so
    /// it reaches from above the screen down to the notch's bottom and only its lower corners show. The radius is at
    /// most half the box's width and its height.
    /// </remarks>
    public static float NotchDistance(float x, float y, float leftPx, float rightPx, float heightPx, float radiusPx)
    {
        float halfWidth = 0.5f * (rightPx - leftPx);
        float r = MathF.Min(radiusPx, MathF.Min(halfWidth, heightPx));
        float qx = MathF.Abs(x - 0.5f * (leftPx + rightPx)) - halfWidth + r;
        float qy = MathF.Abs(y) - heightPx + r;
        float outside = MathF.Sqrt(MathF.Max(qx, 0) * MathF.Max(qx, 0) + MathF.Max(qy, 0) * MathF.Max(qy, 0));
        return MathF.Max(outside + MathF.Min(MathF.Max(qx, qy), 0) - r, 0);
    }

    /// <summary>
    /// The smaller of <paramref name="a"/> and <paramref name="b"/>, smoothed where they are within
    /// <paramref name="k"/> of each other: <c>min(a, b) - h² k / 4</c> with <c>h = max(k - |a - b|, 0) / k</c>, so the
    /// crease where two distance fields meet becomes a round fillet about <paramref name="k"/> across. Continuous with
    /// a continuous slope; <paramref name="k"/> ≤ 0 is the plain minimum.
    /// </summary>
    public static float SmoothMin(float a, float b, float k)
    {
        if (!(k > 0)) return MathF.Min(a, b);
        float h = MathF.Max(k - MathF.Abs(a - b), 0) / k;
        return MathF.Min(a, b) - h * h * k / 4;
    }

    /// <summary>
    /// A depth in from the edge, taken round the notch as well: the smooth minimum (<see cref="SmoothMin"/>) of
    /// <paramref name="depthPx"/> and <paramref name="notchDistancePx"/> with <paramref name="notchRadiusPx"/>, at
    /// least 0. Inside the notch it is 0, beside and below it the distance from the notch, and the inside corners where
    /// the notch meets the top edge are rounded with the notch's own radius.
    /// </summary>
    public static float AroundNotch(float depthPx, float notchDistancePx, float notchRadiusPx) =>
        MathF.Max(SmoothMin(depthPx, notchDistancePx, notchRadiusPx), 0);

    /// <summary>
    /// The alpha at pixel centre (<paramref name="x"/>, <paramref name="y"/>) for the constants <paramref name="c"/>,
    /// before the dither: what <c>Glow.hlsl</c> and the Mac's <c>glow_math.h</c> compute, in either style and round a
    /// notch. The Glow style without a notch is <see cref="Glow"/> × gain × Visibility, exactly as before the Line
    /// style.
    /// </summary>
    internal static float Alpha(in GlowConstants c, float x, float y)
    {
        float depth = Depth(x, y, c.ScreenWidthPx, c.ScreenHeightPx, c.GlowRadiusPx);
        bool notched = c.NotchHeightPx > 0;
        float notch = 0;
        if (notched)
        {
            notch = NotchDistance(x, y, c.NotchLeftPx, c.NotchRightPx, c.NotchHeightPx, c.NotchRadiusPx);
            depth = AroundNotch(depth, notch, c.NotchRadiusPx);
        }
        float shoulder = PulsedShoulder(c.ShoulderPx, c.Pulse);
        float spread = PulsedSpread(c.SpreadPx, c.Pulse);
        if (!(c.LinePx > 0)) return Glow(depth, shoulder, spread) * c.Gain * c.Visibility;

        float lineDepth = Depth(x, y, c.ScreenWidthPx, c.ScreenHeightPx, c.TopCornerRadiusPx, c.CornerRadiusPx);
        if (notched) lineDepth = AroundNotch(lineDepth, notch, c.NotchRadiusPx);
        return LineAndHalo(lineDepth, c.LinePx, depth, shoulder, spread) * c.Gain * c.Visibility;
    }

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

    // HLSL's smoothstep: 0 up to edge0, 1 from edge1, t² (3 - 2t) between.
    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
