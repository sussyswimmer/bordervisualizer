// Rimlight edge glow (doc 04 section 3). One full-screen triangle per overlay; every pixel is computed from the
// constant buffer and the 64-texel palette gradient. Compiled at runtime as vs_4_0 / ps_4_0 (feature level 10.0+).
// The C# mirror of this constant buffer is GlowConstants.cs: keep the two in sync. The edge profile (step 2) is
// GlowProfile.cs, which the Settings preview runs on the CPU: keep that in sync too. Keep this file ASCII-only.

cbuffer Light : register(b0)
{
    float2 ScreenPx;        // swap chain size in pixels
    float  CornerRadiusPx;  // shape corner radius; 0 = square corners. The radii below are at least this
    float  BandScalePx;     // soft band's e-folding distance: half the Thickness setting
    float  SpreadPx;        // glow reach (e-folding distance)
    float  Gain;            // Intensity x the beat's 1 + 0.25 Pulse, rolled off under 1 (GlowProfile.Gain)
    float  Pulse;           // 0..1 beat kick
    float  Phase;           // 0..1 gradient rotation
    float  Visibility;      // 0..1 fades (pause, off, hide, per-monitor pause); multiplies alpha like Gain
    float  ColorRadiusPx;   // corner radius of the colour path (>= CornerRadiusPx), see PerimeterT
    float  GlowRadiusPx;    // corner radius of the glow field (>= CornerRadiusPx), see PSMain step 2
    float  BandGain;        // soft band's brightness factor; 0 = no band (Thickness 0), see PSMain step 2
    float3 MeanColor;       // average of the gradient, linear RGB; the colour deep inside, see PSMain step 4
    float  BandRadiusPx;    // corner radius of the band field (>= CornerRadiusPx), see PSMain step 2
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

    // 1. Distance to the edge: 0 at the edge, growing inward, through two rounded fields. With the square field of
    // doc 04 the two edges' light would meet in a visible 45-degree crease; rounded (radius >= 2 e-folding distances
    // of each light), it bends softly round the corners.
    float bandDistance = max(-SdRoundBox(p - halfSize, halfSize, BandRadiusPx), 0);
    float glowDistance = max(-SdRoundBox(p - halfSize, halfSize, GlowRadiusPx), 0);

    // 2. Shape (GlowProfile.cs): light, brightest at the edge and fading smoothly inward, with no line anywhere.
    // Doc 04's solid core stepped from 1 to about 0.6 at its inner edge, which read as a bar. Now a soft band
    // (Thickness, the pixel's average of exp(-d / b), so a band under a pixel keeps its light) and the exponential
    // glow (Glow; the beat pushes it further in) add like light on a screen, which never passes 1.
    float band = saturate(BandGain * exp(-bandDistance / BandScalePx));
    // SpreadPx is at least one DIP, which is half a swap-chain pixel at half render scale; the floor only guards the
    // division and must not widen that hairline halo.
    float spread = max(SpreadPx * (1 + 0.35 * Pulse), 0.5);
    float glow = 0.85 * exp(-glowDistance / spread);
    float shape = 1 - (1 - glow) * (1 - band);
    // Intensity and the beat's gain can pass 1 (Brightness above 80% on a strong beat); the CPU rolls the gain off
    // under 1 instead of clipping here, so the profile is only ever scaled: clipping (or a per-pixel knee) would
    // squash the brightest pixels into a brighter rim flashing on the beat (GlowProfile.Gain).
    float a = shape * Gain * Visibility;
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
