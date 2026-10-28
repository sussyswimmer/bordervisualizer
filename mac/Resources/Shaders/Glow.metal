// Rimlight edge glow on the Mac (docs/10-MACOS.md section 3.3): a line-by-line port of
// src/Rimlight.Platform/Overlay/Glow.hlsl. One full-screen triangle per overlay; every pixel is computed from the
// constants (RlGlowConstants, GlowConstants.cs) and the 64-texel palette gradient, in either edge style (the soft glow,
// or the bright line with a halo inside it), round the camera notch of a notched MacBook screen and with the Flow
// movement's light travelling round the edge (gm_alpha). Compiled at runtime from source: build.sh (and
// ShaderSource.swift for tests) replaces the include line below with glow_math.h, since runtime compilation can't
// resolve includes. Keep this file ASCII-only.

#include <metal_stdlib>
using namespace metal;

#include "glow_math.h"

struct GlowVertex {
    float4 position [[position]];
};

// Full-screen triangle from the vertex id: (-1, 1), (3, 1), (-1, -3) in clip space. No vertex buffer.
vertex GlowVertex glow_vertex(uint id [[vertex_id]]) {
    float2 uv = float2(float((id << 1) & 2u), float(id & 2u));
    GlowVertex out;
    out.position = float4(uv.x * 2.0f - 1.0f, 1.0f - uv.y * 2.0f, 0.0f, 1.0f);
    return out;
}

static float3 linear_to_srgb(float3 c) {
    float3 low = c * 12.92f;
    float3 high = 1.055f * pow(abs(c), 1.0f / 2.4f) - 0.055f;
    return mix(high, low, step(c, float3(0.0031308f)));
}

// Interleaved gradient noise in [0, 1) (Jimenez 2014): breaks up 8-bit banding in the dark tail of the glow.
static float interleaved_gradient_noise(float2 pixel) {
    return fract(52.9829189f * fract(dot(pixel, float2(0.06711056f, 0.00583715f))));
}

fragment float4 glow_fragment(GlowVertex in [[stage_in]],
                              constant RlGlowConstants &k [[buffer(0)]],
                              texture2d<float> gradient [[texture(0)]]) {
    // 64 x 1, linear RGB, sampled with linear filtering and wrapping, so the loop is seamless (H-008).
    constexpr sampler gradientSampler(filter::linear, address::repeat);
    float2 p = in.position.xy; // pixel centre, origin at the top-left, as in Direct3D

    // 1-2. The light's alpha before the Flow movement (GlowProfile.Alpha): the soft glow, flat at the edge and fading
    // smoothly inward, or the Line style's bright line in front of a dimmer halo; round the notch when the screen has
    // one.
    RlGlowConstants constants = k;
    float a = gm_profile_alpha(constants, p.x, p.y);
    if (a < 0.5f / 255.0f)
        return float4(0.0f);

    // The perimeter coordinate and the blend to the means deep inside, computed once for the motion and the colour.
    // Both follow the outer rectangle, so the notch's outline takes the top edge's colours and light.
    float t = gm_perimeter_t(p.x, p.y, k.screenWidthPx, k.screenHeightPx, k.colorRadiusPx);
    float depth = gm_edge_depth(p.x, p.y, k.screenWidthPx, k.screenHeightPx);
    float deep = smoothstep(0.5f * k.colorRadiusPx, k.colorRadiusPx, depth);

    // The Flow movement (GlowProfile.Motion, Maxwell's request): comets chase round the edge and the beats' ripples run
    // along it, on the colours' own coordinate t, blending to their mean where the colours blend to theirs. The motion
    // is at most 1, so the test above was an upper bound; dimmed by it, the pixel may now fall under half a code value
    // too. With no Flow amount (the Pulse movement, Idle Glow) it is skipped.
    if (k.flowAmount > 0.0f) {
        a *= gm_motion_at(constants, t, deep);
        if (a < 0.5f / 255.0f)
            return float4(0.0f);
    }

    // 3-5. Colour around the perimeter from the Oklab-blended gradient, blended to the mean colour deep inside.
    float u = gm_frac(t + k.phase);
    float3 rgb = gradient.sample(gradientSampler, float2(u, 0.5f), level(0.0f)).rgb;
    rgb = mix(rgb, float3(k.meanRed, k.meanGreen, k.meanBlue), deep);

    // 7. Premultiplied output, then 6. the dither. The layer is 8-bit with an sRGB colour space, so the linear colour
    // is encoded here before it is multiplied by alpha, as on Windows.
    float noise = (interleaved_gradient_noise(p) - 0.5f) / 255.0f;
    return saturate(float4(linear_to_srgb(saturate(rgb)) * a, a) + noise);
}
