// Rimlight edge glow (doc 04 section 3). One full-screen triangle per overlay; every pixel is computed from the
// constant buffer and the 64-texel palette gradient. Compiled at runtime as vs_4_0 / ps_4_0 (feature level 10.0+).
// The C# mirror of this constant buffer is GlowConstants.cs: keep the two in sync. The edge profile (step 2), in both
// edge styles (the soft glow, and the Line style's bright line with a halo inside it) and round a camera notch, is
// GlowProfile.cs (GlowProfile.Alpha per pixel), which the Settings preview runs on the CPU and the Mac's glow_math.h
// mirrors: keep those in sync too. With LinePx and NotchHeightPx both 0 (the Glow style on Windows) the new work is
// branched over and the glow is drawn as before the Line style. Keep this file ASCII-only.

cbuffer Light : register(b0)
{
    float2 ScreenPx;          // swap chain size in pixels
    float  CornerRadiusPx;    // the shape's (bottom) corner radius; 0 = square. The field radii are at least this
    float  ShoulderPx;        // width of the soft shoulder at the edge: the Thickness, at least one DIP (Line: one DIP)
    float  SpreadPx;          // glow reach (e-folding distance)
    float  Gain;              // Intensity x the beat's 1 + 0.25 Pulse, rolled off under 1 (GlowProfile.Gain)
    float  Pulse;             // 0..1 beat kick
    float  Phase;             // 0..1 gradient rotation
    float  Visibility;        // 0..1 fades (pause, off, hide, per-monitor pause); multiplies alpha like Gain
    float  ColorRadiusPx;     // corner radius of the colour path (>= CornerRadiusPx), see PerimeterT
    float  GlowRadiusPx;      // corner radius of the light's field (>= CornerRadiusPx), see PSMain step 1
    float  LinePx;            // the Line style's line width: the Thickness, at least one DIP; 0 = the Glow style
    float3 MeanColor;         // average of the gradient, linear RGB; the colour deep inside, see PSMain step 4
    float  TopCornerRadiusPx; // the screen's top corners, for the line: rounder on a notched screen
    float  NotchLeftPx;       // the camera notch hanging from the top edge, in pixels; all 0 without one
    float  NotchRightPx;
    float  NotchHeightPx;     // 0 = no notch
    float  NotchRadiusPx;     // the notch's lower corners, and the fillets where it meets the top edge
};

// 64 x 1, linear RGB. Texel i is the colour at perimeter position u = (i + 0.5) / 64 before Phase is applied
// (H-008). Sampled with linear filtering and WRAP addressing, so the loop is seamless.
Texture2D<float4> Gradient : register(t0);
SamplerState GradientSampler : register(s0);

static const float HalfPi = 1.5707963;

// Full-screen triangle from SV_VertexID: (-1, 1), (3, 1), (-1, -3) in clip space. No vertex buffer.
float4 VSMain(uint id : SV_VertexID) : SV_Position
{
    float2 uv = float2((id << 1) & 2, id & 2);
    return float4(uv.x * 2 - 1, 1 - uv.y * 2, 0, 1);
}

// Signed distance to a rounded box centred at the origin: negative inside.
float SdRoundBox(float2 p, float2 halfSize, float radius)
{
    float2 q = abs(p) - halfSize + radius;
    return length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
}

// Distance from p to the camera notch (GlowProfile.NotchDistance): 0 inside it, growing outward. A rounded box
// centred on the top edge under the notch's middle, its half height the notch's height, so it reaches from above the
// screen down to the notch's bottom and only its lower corners show.
float NotchDistance(float2 p)
{
    float2 notchHalf = float2(0.5 * (NotchRightPx - NotchLeftPx), NotchHeightPx);
    float radius = min(NotchRadiusPx, min(notchHalf.x, notchHalf.y));
    return max(SdRoundBox(p - float2(0.5 * (NotchLeftPx + NotchRightPx), 0), notchHalf, radius), 0);
}

// Polynomial smooth minimum (GlowProfile.SmoothMin): min(a, b) - h^2 k / 4 with h = max(k - |a - b|, 0) / k, which
// rounds the crease where two distance fields meet into a fillet about k across; k <= 0 is the plain minimum.
float SmoothMin(float a, float b, float k)
{
    if (!(k > 0))
        return min(a, b);
    float h = max(k - abs(a - b), 0) / k;
    return min(a, b) - h * h * k / 4;
}

// A depth in from the edge, taken round the notch too (GlowProfile.AroundNotch): 0 inside the notch, the distance
// from it beside and below it, and the inside corners where it meets the top edge rounded like the notch's "ears".
float AroundNotch(float depth, float notchDistance)
{
    return max(SmoothMin(depth, notchDistance, NotchRadiusPx), 0);
}

// Angle in [0, pi/2] for a point in a corner zone; y and x are both >= 0 there. The x guard keeps atan2(0, 0)
// (the exact arc centre) finite on every GPU.
float QuarterAngle(float y, float x)
{
    return atan2(y, max(x, 1e-5));
}

// Perimeter coordinate in [0, 1): arc length clockwise along a rounded rectangle with corner radius r, starting at
// the top-left corner (the middle of its arc, which is the screen corner's diagonal; H-008). Inside the corner zones
// the position follows the arc, so it is continuous through the corners. Elsewhere it is the projection onto the
// nearest straight edge, which is discontinuous only on the medial axis at least r from every edge; PSMain blends
// the colour to the mean before that depth.
float PerimeterT(float2 p, float2 size, float r)
{
    float topLen = size.x - 2 * r;
    float sideLen = size.y - 2 * r;
    float arcLen = HalfPi * r;
    float total = 2 * (topLen + sideLen) + 4 * arcLen;

    bool left = p.x < r;
    bool right = p.x > size.x - r;
    bool top = p.y < r;
    bool bottom = p.y > size.y - r;

    float s;
    if (top && right)
    {
        float2 v = p - float2(size.x - r, r);
        s = topLen + arcLen * QuarterAngle(v.x, -v.y) / HalfPi;
    }
    else if (bottom && right)
    {
        float2 v = p - (size - r);
        s = topLen + arcLen + sideLen + arcLen * QuarterAngle(v.y, v.x) / HalfPi;
    }
    else if (bottom && left)
    {
        float2 v = p - float2(r, size.y - r);
        s = 2 * topLen + 2 * arcLen + sideLen + arcLen * QuarterAngle(-v.x, v.y) / HalfPi;
    }
    else if (top && left)
    {
        float2 v = p - float2(r, r);
        s = 2 * topLen + 3 * arcLen + 2 * sideLen + arcLen * QuarterAngle(-v.y, -v.x) / HalfPi;
    }
    else
    {
        float dTop = p.y;
        float dRight = size.x - p.x;
        float dBottom = size.y - p.y;
        float dLeft = p.x;
        float nearest = min(min(dTop, dRight), min(dBottom, dLeft));
        if (nearest == dTop)
            s = p.x - r;
        else if (nearest == dRight)
            s = topLen + arcLen + (p.y - r);
        else if (nearest == dBottom)
            s = topLen + 2 * arcLen + sideLen + (size.x - r - p.x);
        else
            s = 2 * topLen + 3 * arcLen + sideLen + (size.y - r - p.y);
    }
    // s = 0 is where the top edge's straight part begins; shift by half an arc so t = 0 is the top-left corner.
    return frac((s + 0.5 * arcLen) / total);
}

float3 LinearToSrgb(float3 c)
{
    float3 low = c * 12.92;
    float3 high = 1.055 * pow(abs(c), 1.0 / 2.4) - 0.055;
    return lerp(high, low, step(c, 0.0031308));
}

// Interleaved gradient noise in [0, 1) (Jimenez 2014): breaks up 8-bit banding in the dark tail of the glow.
float InterleavedGradientNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

float4 PSMain(float4 position : SV_Position) : SV_Target
{
    float2 p = position.xy; // pixel centre, origin at the top-left
    float2 halfSize = 0.5 * ScreenPx;

    // 1. Distance to the edge: 0 at the edge, growing inward, through a rounded field. With the square field of
    // doc 04 the two edges' light would meet in a visible 45-degree crease; rounded (radius >= the shoulder plus two
    // e-folding distances), it bends softly round the corners.
    float glowDistance = max(-SdRoundBox(p - halfSize, halfSize, GlowRadiusPx), 0);
    // Round a camera notch hanging from the top edge (both styles): the light runs down its side, along its bottom
    // and up again. Only the depth changes; the colour path below stays on the outer rectangle, so the notch's
    // outline takes the top edge's colours.
    float notchDistance = 0;
    [branch] if (NotchHeightPx > 0)
    {
        notchDistance = NotchDistance(p);
        glowDistance = AroundNotch(glowDistance, notchDistance);
    }

    // 2. Shape (GlowProfile.cs). The Glow style: one light, flat at the edge and fading smoothly inward, no line. Doc
    // 04's solid core stepped from 1 to about 0.6 at its inner edge (a bar), and round 1's exponentials were steepest
    // at the very edge, which packed the brightest light into the outermost pixels (a thin bright line). The distance
    // is eased through a hyperbola, sqrt(d^2 + w^2) - w: about d^2 / 2w near the edge, so the light starts with zero
    // slope (a soft shoulder that bends into the fade over about w = Thickness, and stays within 3% of the edge for
    // about sqrt(0.06 w spread)), and about d - w further in, the exponential glow. The beat widens the shoulder and
    // the spread alike, swelling the whole light inward.
    // SpreadPx is at least one DIP, which is half a swap-chain pixel at half render scale; the floor only guards the
    // division and must not widen that hairline halo.
    float spread = max(SpreadPx * (1 + 0.35 * Pulse), 0.5);
    float shoulder = ShoulderPx * (1 + 0.35 * Pulse);
    float glow = 0.85 * exp(-(sqrt(glowDistance * glowDistance + shoulder * shoulder) - shoulder) / spread);
    // The Line style (GlowProfile.LineAndHalo, Maxwell's request): a bright line along the very edge, LinePx wide
    // with a 1.5 px anti-aliased inner edge, in front of a dimmer halo, the glow above with the narrowest shoulder at
    // 0.5 instead of 0.85. The line follows the screen's own shape (the top corners' radius in the top half), and
    // the beat brightens it through the gain but never widens it. Line and halo combine like light through two
    // filters, so the line never passes 1 and the halo carries on past it.
    [branch] if (LinePx > 0)
    {
        float lineRadius = p.y < halfSize.y ? TopCornerRadiusPx : CornerRadiusPx;
        float lineDistance = max(-SdRoundBox(p - halfSize, halfSize, lineRadius), 0);
        [branch] if (NotchHeightPx > 0)
            lineDistance = AroundNotch(lineDistance, notchDistance);
        float lineCoverage = 1 - smoothstep(LinePx - 0.75, LinePx + 0.75, lineDistance);
        float halo = 0.5 * exp(-(sqrt(glowDistance * glowDistance + shoulder * shoulder) - shoulder) / spread);
        glow = 1 - (1 - lineCoverage) * (1 - halo);
    }
    // Intensity and the beat's gain can pass 1 (Brightness above 80% on a strong beat); the CPU rolls the gain off
    // under 1 instead of clipping here, so the profile is only ever scaled: clipping (or a per-pixel knee) would
    // squash the brightest pixels into a brighter rim flashing on the beat (GlowProfile.Gain).
    float a = glow * Gain * Visibility;
    // Far inside: below half a code value. The test is on the undithered alpha so whole waves skip together; the
    // only output lost is a sparse speckle of dithered 1/255 pixels.
    [branch] if (a < 0.5 / 255)
        return 0;

    // 3-5. Colour around the perimeter from the Oklab-blended gradient (built on the CPU when the palette changes).
    float u = frac(PerimeterT(p, ScreenPx, ColorRadiusPx) + Phase);
    float3 rgb = Gradient.SampleLevel(GradientSampler, float2(u, 0.5), 0).rgb;
    // The perimeter coordinate has a seam deep inside (its medial axis, at least ColorRadiusPx from every edge). The
    // colour blends to the gradient's mean before that depth, so both sides of the seam agree even when a wide glow
    // (ColorRadiusPx clamped to half the screen) still lights it.
    float depth = min(min(p.x, p.y), min(ScreenPx.x - p.x, ScreenPx.y - p.y));
    rgb = lerp(rgb, MeanColor, smoothstep(0.5 * ColorRadiusPx, ColorRadiusPx, depth));

    // 7. Premultiplied output, then 6. the dither. The swap chain is 8-bit UNORM, which DWM reads as sRGB-encoded,
    // so the linear gradient colour is encoded here before it is multiplied by alpha. The same noise goes on all
    // four channels, so every colour channel gets the full +-0.5 LSB, and colour stays <= alpha (valid premultiplied).
    float noise = (InterleavedGradientNoise(p) - 0.5) / 255;
    return saturate(float4(LinearToSrgb(saturate(rgb)) * a, a) + noise);
}
