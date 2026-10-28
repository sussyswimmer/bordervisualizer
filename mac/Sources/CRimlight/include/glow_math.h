// The glow's per-pixel math, shared by the Metal shader (Resources/Shaders/Glow.metal includes it) and the Swift tests
// (which call it as C and compare it with the golden table that src/Rimlight.Native.Tests writes from GlowProfile).
// A line-by-line port of src/Rimlight.Platform/Overlay/Glow.hlsl steps 1-5 and GlowProfile.cs (GlowProfile.Alpha per
// pixel): both edge styles (the soft glow, and the Line style's bright line with a halo inside it), the way round a
// camera notch, and the Flow movement's light travelling round the edge. Keep them in sync. Valid both as Metal Shading
// Language and as C11. Keep this file ASCII-only.

#ifndef RIMLIGHT_GLOW_MATH_H
#define RIMLIGHT_GLOW_MATH_H

#if defined(__METAL_VERSION__)
#define GM_FN static inline
#define GM_STATIC_ASSERT(c, m) static_assert(c, m)
#define GM_SQRT(x) sqrt(x)
#define GM_EXP(x) exp(x)
#define GM_ABS(x) fabs(x)
#define GM_MAX(a, b) fmax(a, b)
#define GM_MIN(a, b) fmin(a, b)
#define GM_ATAN2(y, x) atan2(y, x)
#define GM_FLOOR(x) floor(x)
#else
#include <math.h>
#define GM_FN static inline
#define GM_STATIC_ASSERT(c, m) _Static_assert(c, m)
#define GM_SQRT(x) sqrtf(x)
#define GM_EXP(x) expf(x)
#define GM_ABS(x) fabsf(x)
#define GM_MAX(a, b) fmaxf(a, b)
#define GM_MIN(a, b) fminf(a, b)
#define GM_ATAN2(y, x) atan2f(y, x)
#define GM_FLOOR(x) floorf(x)
#endif

// One beat's ripple in the constants (GlowRipple in GlowConstants.cs), one 16-byte register: where it started on the
// perimeter, how far its fronts have run and how bright it is now.
typedef struct RlGlowRipple {
    float origin;   // 0..1, a perimeter coordinate (gm_perimeter_t)
    float radius;   // how far each front has run from the origin, in perimeters, 0 or more
    float strength; // 0..1; 0 = no ripple
    float pad;
} RlGlowRipple;

GM_STATIC_ASSERT(sizeof(RlGlowRipple) == 16, "RlGlowRipple must match GlowRipple");

// The Light constant buffer: GlowConstants.cs's 40 floats in the same order (160 bytes, ten 16-byte registers; the last
// five the Flow movement's). rl_loop_plan_surface fills it.
typedef struct RlGlowConstants {
    float screenWidthPx;     // drawable size in pixels
    float screenHeightPx;
    float cornerRadiusPx;    // the shape's (bottom) corner radius; 0 = square corners
    float shoulderPx;        // the soft shoulder at the edge: the Thickness setting, at least one point (Line: one)
    float spreadPx;          // glow reach (e-folding distance)
    float gain;              // Intensity x the beat's and the Flow's gains, rolled off under 1 (GlowProfile.Gain)
    float pulse;             // 0..1 beat kick
    float phase;             // 0..1 gradient rotation
    float visibility;        // 0..1 fades; multiplies alpha like gain
    float colorRadiusPx;     // corner radius of the colour path
    float glowRadiusPx;      // corner radius of the light's field
    float linePx;            // the Line style's line width: the Thickness, at least one point; 0 = the Glow style
    float meanRed;           // average of the gradient, linear RGB: the colour deep inside
    float meanGreen;
    float meanBlue;
    float topCornerRadiusPx; // the screen's top corners, for the line: rounder on a notched screen
    float notchLeftPx;       // the camera notch hanging from the top edge, in pixels; all 0 without one
    float notchRightPx;
    float notchHeightPx;     // 0 = no notch
    float notchRadiusPx;     // the notch's lower corners, and the fillets where it meets the top edge
    float flowAmount;        // the Flow movement's share, 0..1 (gm_motion); 0 = the edge lit evenly
    float cometPhase;        // 0..1, where the comets are (gm_comets)
    float flowPad0;
    float flowPad1;
    RlGlowRipple ripples[4]; // the beats' ripples (gm_ripple_at); Swift sees a tuple of four
} RlGlowConstants;

GM_STATIC_ASSERT(sizeof(RlGlowConstants) == 160, "RlGlowConstants must match GlowConstants.SizeInBytes");

// GlowProfile.Depth: distance in from the edge through a field whose corners have radius r; 0 at the edge and outside
// the rounded corners, growing inward (Glow.hlsl step 1).
GM_FN float gm_depth(float x, float y, float width, float height, float radius) {
    float qx = GM_ABS(x - 0.5f * width) - 0.5f * width + radius;
    float qy = GM_ABS(y - 0.5f * height) - 0.5f * height + radius;
    float ox = GM_MAX(qx, 0.0f);
    float oy = GM_MAX(qy, 0.0f);
    float outside = GM_SQRT(ox * ox + oy * oy);
    return GM_MAX(-(outside + GM_MIN(GM_MAX(qx, qy), 0.0f) - radius), 0.0f);
}

// GlowProfile.Depth with per-corner radii: the top two corners have radius topRadius and the bottom two bottomRadius,
// the radius of the half the pixel is in. The Line style's line follows the screen's own shape through it.
GM_FN float gm_depth_corners(float x, float y, float width, float height, float topRadius, float bottomRadius) {
    return gm_depth(x, y, width, height, y < 0.5f * height ? topRadius : bottomRadius);
}

// HLSL's smoothstep (GlowProfile.SmoothStep): 0 up to edge0, 1 from edge1, t^2 (3 - 2t) between.
GM_FN float gm_smoothstep(float edge0, float edge1, float x) {
    float t = GM_MIN(GM_MAX((x - edge0) / (edge1 - edge0), 0.0f), 1.0f);
    return t * t * (3.0f - 2.0f * t);
}

// GlowProfile.Line: the Line style's line at depthPx in from the edge, 1 (LinePeak) across its width linePx, then an
// anti-aliased inner edge 0.75 px (LineSoftnessPx) either side of it. Its width never pulses.
GM_FN float gm_line(float depthPx, float linePx) {
    return 1.0f * (1.0f - gm_smoothstep(linePx - 0.75f, linePx + 0.75f, depthPx));
}

// GlowProfile.Halo: the Line style's halo, the glow's own shape at 0.5 (HaloPeak) instead of 0.85.
GM_FN float gm_halo(float distancePx, float shoulderPx, float spreadPx) {
    return 0.5f * GM_EXP(-(GM_SQRT(distancePx * distancePx + shoulderPx * shoulderPx) - shoulderPx) / spreadPx);
}

// GlowProfile.LineAndHalo: the line in front of the halo, combined like light through two filters.
GM_FN float gm_line_and_halo(float lineDepthPx, float linePx, float haloDistancePx, float shoulderPx,
                             float spreadPx) {
    return 1.0f - (1.0f - gm_line(lineDepthPx, linePx)) * (1.0f - gm_halo(haloDistancePx, shoulderPx, spreadPx));
}

// GlowProfile.NotchDistance: distance from (x, y) to a notch hanging from the top edge between leftPx and rightPx,
// down to heightPx, with lower corners of radius radiusPx; 0 inside it, growing outward. A rounded box centred on the
// top edge under the notch's middle, its half height the notch's height, so only its lower corners show.
GM_FN float gm_notch_distance(float x, float y, float leftPx, float rightPx, float heightPx, float radiusPx) {
    float halfWidth = 0.5f * (rightPx - leftPx);
    float r = GM_MIN(radiusPx, GM_MIN(halfWidth, heightPx));
    float qx = GM_ABS(x - 0.5f * (leftPx + rightPx)) - halfWidth + r;
    float qy = GM_ABS(y) - heightPx + r;
    float ox = GM_MAX(qx, 0.0f);
    float oy = GM_MAX(qy, 0.0f);
    float outside = GM_SQRT(ox * ox + oy * oy);
    return GM_MAX(outside + GM_MIN(GM_MAX(qx, qy), 0.0f) - r, 0.0f);
}

// GlowProfile.SmoothMin: min(a, b) - h^2 k / 4 with h = max(k - |a - b|, 0) / k, which rounds the crease where two
// distance fields meet into a fillet about k across; k <= 0 is the plain minimum.
GM_FN float gm_smooth_min(float a, float b, float k) {
    if (!(k > 0.0f))
        return GM_MIN(a, b);
    float h = GM_MAX(k - GM_ABS(a - b), 0.0f) / k;
    return GM_MIN(a, b) - h * h * k / 4.0f;
}

// GlowProfile.AroundNotch: a depth taken round the notch too, 0 inside it, the distance from it beside and below it,
// and the inside corners where it meets the top edge rounded like the notch's "ears".
GM_FN float gm_around_notch(float depthPx, float notchDistancePx, float notchRadiusPx) {
    return GM_MAX(gm_smooth_min(depthPx, notchDistancePx, notchRadiusPx), 0.0f);
}

GM_FN float gm_frac(float x) {
    return x - GM_FLOOR(x);
}

// Angle in [0, pi/2] for a point in a corner zone; the x guard keeps atan2(0, 0) finite on every GPU.
GM_FN float gm_quarter_angle(float y, float x) {
    return GM_ATAN2(y, GM_MAX(x, 1e-5f));
}

// Glow.hlsl's PerimeterT: arc length clockwise along a rounded rectangle with corner radius r, starting at the
// top-left corner (the middle of its arc), as a fraction of the perimeter in [0, 1).
GM_FN float gm_perimeter_t(float px, float py, float sx, float sy, float r) {
    // GlowProfile's MathF.PI / 2 to the bit. Glow.hlsl's 1.5707963 rounds to the float below it, which moves t by an
    // ulp: invisible in the colour, but the Flow movement's comet heads are steep enough in t for the golden table to
    // see it.
    const float halfPi = 1.57079637f;
    float topLen = sx - 2.0f * r;
    float sideLen = sy - 2.0f * r;
    float arcLen = halfPi * r;
    float total = 2.0f * (topLen + sideLen) + 4.0f * arcLen;

    int left = px < r;
    int right = px > sx - r;
    int top = py < r;
    int bottom = py > sy - r;

    float s;
    if (top && right) {
        float vx = px - (sx - r), vy = py - r;
        s = topLen + arcLen * gm_quarter_angle(vx, -vy) / halfPi;
    } else if (bottom && right) {
        float vx = px - (sx - r), vy = py - (sy - r);
        s = topLen + arcLen + sideLen + arcLen * gm_quarter_angle(vy, vx) / halfPi;
    } else if (bottom && left) {
        float vx = px - r, vy = py - (sy - r);
        s = 2.0f * topLen + 2.0f * arcLen + sideLen + arcLen * gm_quarter_angle(-vx, vy) / halfPi;
    } else if (top && left) {
        float vx = px - r, vy = py - r;
        s = 2.0f * topLen + 3.0f * arcLen + 2.0f * sideLen + arcLen * gm_quarter_angle(-vy, -vx) / halfPi;
    } else {
        float dTop = py;
        float dRight = sx - px;
        float dBottom = sy - py;
        float dLeft = px;
        float nearest = GM_MIN(GM_MIN(dTop, dRight), GM_MIN(dBottom, dLeft));
        if (nearest == dTop)
            s = px - r;
        else if (nearest == dRight)
            s = topLen + arcLen + (py - r);
        else if (nearest == dBottom)
            s = topLen + 2.0f * arcLen + sideLen + (sx - r - px);
        else
            s = 2.0f * topLen + 3.0f * arcLen + sideLen + (sy - r - py);
    }
    return gm_frac((s + 0.5f * arcLen) / total);
}

// Distance to the nearest screen edge, for the blend to the mean colour deep inside (Glow.hlsl steps 3-5).
GM_FN float gm_edge_depth(float px, float py, float sx, float sy) {
    return GM_MIN(GM_MIN(px, py), GM_MIN(sx - px, sy - py));
}

// Glow.hlsl steps 1-2, GlowProfile.Alpha without the Flow movement: the light's alpha at a pixel centre, before the
// colour and the motion. The Glow style (linePx 0) is GlowProfile.Glow x Gain x Visibility; the Line style the line in
// front of its halo, x Gain x Visibility. With a notch every depth goes round it; without one (and in the Glow style)
// nothing else is computed. The motion is at most 1, so this is an upper bound on gm_alpha: Glow.metal tests it first.
GM_FN float gm_profile_alpha(RlGlowConstants k, float x, float y) {
    float d = gm_depth(x, y, k.screenWidthPx, k.screenHeightPx, k.glowRadiusPx);
    int notched = k.notchHeightPx > 0.0f;
    float notch = 0.0f;
    if (notched) {
        notch = gm_notch_distance(x, y, k.notchLeftPx, k.notchRightPx, k.notchHeightPx, k.notchRadiusPx);
        d = gm_around_notch(d, notch, k.notchRadiusPx);
    }
    float spread = GM_MAX(k.spreadPx * (1.0f + 0.35f * k.pulse), 0.5f);
    float shoulder = k.shoulderPx * (1.0f + 0.35f * k.pulse);
    if (!(k.linePx > 0.0f)) {
        float glow = 0.85f * GM_EXP(-(GM_SQRT(d * d + shoulder * shoulder) - shoulder) / spread);
        return glow * k.gain * k.visibility;
    }
    float lineDepth = gm_depth_corners(x, y, k.screenWidthPx, k.screenHeightPx, k.topCornerRadiusPx,
                                       k.cornerRadiusPx);
    if (notched)
        lineDepth = gm_around_notch(lineDepth, notch, k.notchRadiusPx);
    return gm_line_and_halo(lineDepth, k.linePx, d, shoulder, spread) * k.gain * k.visibility;
}

// The Flow movement (GlowProfile.Comets, RippleAt, Flow and Motion; Maxwell's request, H-033). Positions are perimeter
// coordinates t (gm_perimeter_t, before the phase) and lengths are in perimeters, so the light travels round the edge
// with the colours. The constants are GlowProfile's: 2 comets (CometCount), a head of 0.01 (CometHead) and a tail of
// 0.12 (CometTail) over a floor of 0.5 (FlowFloor); a ripple's front of 0.015 (RippleFront) and trail of 0.06
// (RippleTrail); the light's mean round the edge, 0.62886227 (FlowMean).

// GlowProfile.Comets: the comets' light at t, 0..1, the brightest of two comets whose heads sit at i / 2 - cometPhase.
// Each is 1 at its head, falls ahead of it as a Gaussian and fades behind it over its tail; the larger exponent wins,
// so the light is continuous everywhere, the heads included.
GM_FN float gm_comets(float t, float cometPhase) {
    float brightest = -1e30f;
    for (int i = 0; i < 2; i++) {
        float head = (float)i / 2.0f - cometPhase;
        float behind = gm_frac(t - head) / 0.12f;
        float ahead = gm_frac(head - t) / 0.01f;
        brightest = GM_MAX(brightest, GM_MAX(-behind, -ahead * ahead));
    }
    return GM_EXP(brightest);
}

// To [-0.5, 0.5): the signed way round the perimeter, the shorter one (GlowProfile.Wrap).
GM_FN float gm_wrap(float x) {
    return x - GM_FLOOR(x + 0.5f);
}

// GlowProfile.RippleAt: a ripple's light at t, two fronts radius either way from origin, each falling ahead of it over
// 0.015 and fading behind it, back toward the origin, over 0.06, times strength. A strength of 0 (or less) is none.
GM_FN float gm_ripple_at(float t, float origin, float radius, float strength) {
    if (!(strength > 0.0f))
        return 0.0f;
    float x = GM_ABS(gm_wrap(t - origin)) - radius;
    float exponent = x >= 0.0f ? -(x / 0.015f) * (x / 0.015f) : x / 0.06f;
    return strength * GM_EXP(exponent);
}

// GlowProfile.Flow: the Flow movement's light at t, 0.5..1, before the blend deep inside: the comets over the dimmer
// edge, then each ripple on top, combined like light through two filters, so it never passes 1.
GM_FN float gm_flow(RlGlowConstants k, float t) {
    float m = 0.5f + (1.0f - 0.5f) * gm_comets(t, k.cometPhase);
    for (int i = 0; i < 4; i++) {
        RlGlowRipple r = k.ripples[i];
        m = 1.0f - (1.0f - m) * (1.0f - gm_ripple_at(t, r.origin, r.radius, r.strength));
    }
    return m;
}

// GlowProfile.Motion from the pixel's perimeter coordinate t and deep, the colour's own blend to its mean
// (smoothstep(colorRadiusPx / 2, colorRadiusPx, gm_edge_depth)): gm_flow blended to its mean by deep and faded in by
// the Flow amount, at most 1. Glow.metal computes t and deep once for the colour and the motion.
GM_FN float gm_motion_at(RlGlowConstants k, float t, float deep) {
    float m = gm_flow(k, t);
    m += (0.62886227f - m) * deep;
    return 1.0f + (m - 1.0f) * k.flowAmount;
}

// GlowProfile.Motion: what the Flow movement multiplies the alpha by at a pixel centre, 0..1; 1 without a Flow amount,
// and nothing else is computed then.
GM_FN float gm_motion(RlGlowConstants k, float x, float y) {
    if (!(k.flowAmount > 0.0f))
        return 1.0f;
    float t = gm_perimeter_t(x, y, k.screenWidthPx, k.screenHeightPx, k.colorRadiusPx);
    float deep = gm_smoothstep(0.5f * k.colorRadiusPx, k.colorRadiusPx,
                               gm_edge_depth(x, y, k.screenWidthPx, k.screenHeightPx));
    return gm_motion_at(k, t, deep);
}

// GlowProfile.Alpha: the light's alpha at a pixel centre, before the colour, in either edge style, round a notch and
// with the Flow movement, multiplied in GlowProfile's order (profile x Gain x Visibility x Motion). Without a Flow
// amount the motion is exactly 1 and this is gm_profile_alpha.
GM_FN float gm_alpha(RlGlowConstants k, float x, float y) {
    return gm_profile_alpha(k, x, y) * gm_motion(k, x, y);
}

#endif
