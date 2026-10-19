// The glow's per-pixel math, shared by the Metal shader (Resources/Shaders/Glow.metal includes it) and the Swift tests
// (which call it as C and compare it with the golden table that src/Rimlight.Native.Tests writes from GlowProfile).
// A line-by-line port of src/Rimlight.Platform/Overlay/Glow.hlsl steps 1-5 and GlowProfile.cs: keep them in sync.
// Valid both as Metal Shading Language and as C11. Keep this file ASCII-only.

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

// The Light constant buffer: GlowConstants.cs's 16 floats in the same order (64 bytes). rl_loop_plan_surface fills it.
typedef struct RlGlowConstants {
    float screenWidthPx;   // drawable size in pixels
    float screenHeightPx;
    float cornerRadiusPx;  // shape corner radius; 0 = square corners
    float shoulderPx;      // the soft shoulder at the edge: the Thickness setting, at least one point
    float spreadPx;        // glow reach (e-folding distance)
    float gain;            // Intensity x the beat's gain, rolled off under 1 (GlowProfile.Gain)
    float pulse;           // 0..1 beat kick
    float phase;           // 0..1 gradient rotation
    float visibility;      // 0..1 fades; multiplies alpha like gain
    float colorRadiusPx;   // corner radius of the colour path
    float glowRadiusPx;    // corner radius of the light's field
    float pad0;
    float meanRed;         // average of the gradient, linear RGB: the colour deep inside
    float meanGreen;
    float meanBlue;
    float pad1;
} RlGlowConstants;

GM_STATIC_ASSERT(sizeof(RlGlowConstants) == 64, "RlGlowConstants must match GlowConstants.SizeInBytes");

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

// Glow.hlsl step 2: the light's alpha at a pixel centre, before the colour (GlowProfile.Glow x Gain x Visibility).
GM_FN float gm_alpha(RlGlowConstants k, float x, float y) {
    float d = gm_depth(x, y, k.screenWidthPx, k.screenHeightPx, k.glowRadiusPx);
    float spread = GM_MAX(k.spreadPx * (1.0f + 0.35f * k.pulse), 0.5f);
    float shoulder = k.shoulderPx * (1.0f + 0.35f * k.pulse);
    float glow = 0.85f * GM_EXP(-(GM_SQRT(d * d + shoulder * shoulder) - shoulder) / spread);
    return glow * k.gain * k.visibility;
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
    const float halfPi = 1.5707963f;
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

#endif
