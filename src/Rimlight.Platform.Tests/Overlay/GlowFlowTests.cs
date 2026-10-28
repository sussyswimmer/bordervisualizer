using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// The Flow movement's reference math (GlowProfile.Comets, RippleAt, Flow and Motion; H-033), which Glow.hlsl, the Mac's
// glow_math.h and the Settings preview mirror. Maxwell: "I want the rimlight to be like the video I sent where it moves
// around and doesn't just bounce". The shader can't run here, so these pin what it must draw: a light that stays in
// range, varies smoothly round the edge (no seam, no step at a comet's head or a ripple's front), travels as the
// engine moves it, blends to its mean deep inside, and is exactly the old light without a Flow amount.
public sealed class GlowFlowTests
{
    private static readonly Rgb Mean = new(0.2f, 0.3f, 0.4f);

    // The steepest the light can change along the perimeter, per perimeter: a comet's head, exp(-(a / CometHead)^2),
    // is steepest at a = CometHead / sqrt 2, sqrt(2 / e) / CometHead; a ripple's front likewise with RippleFront (its
    // trail, 1 / RippleTrail, is gentler).
    private static readonly float HeadSlope = MathF.Sqrt(2 / MathF.E) / GlowProfile.CometHead;
    private static readonly float FrontSlope = MathF.Sqrt(2 / MathF.E) / GlowProfile.RippleFront;

    // Comet phases and ripple sets the engine might send: none, a flash at its origin, fronts running both ways, one
    // wrapping round t = 0, fronts that have run past the far side, and all four at once.
    private static readonly float[] CometPhases = [0, 0.1f, 0.3f, 0.5f, 0.77f, 0.9999f];

    public static TheoryData<int> RippleSets() => new() { 0, 1, 2, 3, 4 };

    private static (Ripple, Ripple, Ripple, Ripple) Ripples(int set) => set switch
    {
        0 => default,
        1 => (new Ripple(0.3f, 0, 1), default, default, default),
        2 => (new Ripple(0.6f, 0.08f, 0.7f), default, new Ripple(0.25f, 0.2f, 0.3f), default),
        3 => (new Ripple(0.99f, 0.03f, 0.9f), new Ripple(0.01f, 0.7f, 0.05f), default, default),
        _ => (new Ripple(0.1f, 0.02f, 1), new Ripple(0.4f, 0.15f, 0.6f), new Ripple(0.7f, 0.3f, 0.4f),
            new Ripple(0.9f, 0.45f, 0.2f)),
    };

    [Fact]
    public void TheCometsAreOneAtTheirHeadsAndInRangeEverywhere()
    {
        foreach (float phase in CometPhases)
        {
            for (int k = 0; k < GlowProfile.CometCount; k++)
            {
                float head = (float)k / GlowProfile.CometCount - phase;
                Near(1, GlowProfile.Comets(head - MathF.Floor(head), phase), 1e-3f);
            }
            float floor = MathF.Exp(-0.5f / GlowProfile.CometTail); // the tail reaches the next head at its dimmest
            for (float t = 0; t < 1; t += 1e-4f)
            {
                float comets = GlowProfile.Comets(t, phase);
                Assert.InRange(comets, floor * 0.999f, 1);
            }
        }
    }

    [Fact]
    public void TheCometsAreContinuousThroughTheirHeadsAndTheSeam()
    {
        // The light ahead of a head is a Gaussian and behind it an exponential; the larger exponent wins, so nothing
        // steps where the two meet, at the head, or where t wraps from 1 to 0.
        const float step = 1e-5f;
        foreach (float phase in CometPhases)
        {
            float previous = GlowProfile.Comets(0, phase);
            for (int i = 1; i <= 100_000; i++)
            {
                float comets = GlowProfile.Comets(i * step % 1, phase);
                Assert.True(MathF.Abs(comets - previous) <= HeadSlope * step + 1e-5f,
                    $"phase {phase}: the comets jump by {comets - previous} at t = {i * step}");
                previous = comets;
            }
            // Either side of each head.
            for (int k = 0; k < GlowProfile.CometCount; k++)
            {
                float head = (float)k / GlowProfile.CometCount - phase;
                head -= MathF.Floor(head);
                float before = GlowProfile.Comets(Fraction(head - 1e-6f), phase);
                float after = GlowProfile.Comets(Fraction(head + 1e-6f), phase);
                Assert.InRange(before, 0.999f, 1);
                Assert.InRange(after, 0.999f, 1);
            }
        }
    }

    [Fact]
    public void TheCometsTrailBehindTheirHeadsAndEndCrisplyAhead()
    {
        // The heads move toward smaller t as the phase grows (counterclockwise, the way the colours flow), so the tail
        // lies at larger t: a long exponential behind, a Gaussian over about CometHead ahead.
        float head = 0.5f - 0.3f; // comet 1 at phase 0.3
        Near(MathF.Exp(-0.06f / GlowProfile.CometTail), GlowProfile.Comets(head + 0.06f, 0.3f), 1e-4f);
        Near(MathF.Exp(-1), GlowProfile.Comets(head - GlowProfile.CometHead, 0.3f), 1e-4f);
        Assert.True(GlowProfile.Comets(head - 0.03f, 0.3f) < GlowProfile.Comets(head + 0.03f, 0.3f));
        // The next phase moves every head back by the same amount.
        for (float t = 0; t < 1; t += 0.001f)
            Near(GlowProfile.Comets(t, 0.3f), GlowProfile.Comets(Fraction(t - 0.05f), 0.35f), 1e-3f);
    }

    [Fact]
    public void ARippleIsItsStrengthAtItsFrontsAndContinuousEverywhere()
    {
        const float step = 1e-5f;
        foreach ((float origin, float radius, float strength) in new[]
        {
            (0.3f, 0f, 1f), (0.3f, 0.1f, 0.7f), (0.99f, 0.03f, 0.9f), (0.01f, 0.2f, 0.5f), (0.5f, 0.6f, 0.4f),
        })
        {
            // Both fronts, round the shorter way (one wraps past t = 0 or 1), are the ripple's strength, until they
            // have run past each other on the far side and only the trail is left.
            if (radius <= 0.5f)
            {
                Near(strength, GlowProfile.RippleAt(Fraction(origin + radius), origin, radius, strength), 1e-4f);
                Near(strength, GlowProfile.RippleAt(Fraction(origin - radius), origin, radius, strength), 1e-4f);
            }
            else
            {
                Assert.True(GlowProfile.RippleAt(Fraction(origin + 0.5f), origin, radius, strength) <
                    strength * MathF.Exp((0.5f - radius) / GlowProfile.RippleTrail) + 1e-6f);
            }
            float previous = GlowProfile.RippleAt(0, origin, radius, strength);
            for (int i = 1; i <= 100_000; i++)
            {
                float t = i * step % 1;
                float light = GlowProfile.RippleAt(t, origin, radius, strength);
                Assert.InRange(light, 0, strength);
                Assert.True(MathF.Abs(light - previous) <= strength * FrontSlope * step + 1e-5f,
                    $"ripple ({origin}, {radius}, {strength}) jumps by {light - previous} at t = {t}");
                // The same either way from the origin.
                Near(light, GlowProfile.RippleAt(Fraction(2 * origin - t), origin, radius, strength), 1e-3f);
                previous = light;
            }
        }
        // Ahead of a front a Gaussian over RippleFront; behind it, toward the origin, an exponential over RippleTrail.
        Near(MathF.Exp(-1), GlowProfile.RippleAt(0.5f + 0.1f + GlowProfile.RippleFront, 0.5f, 0.1f, 1), 1e-4f);
        Near(MathF.Exp(-1), GlowProfile.RippleAt(0.5f + 0.1f - GlowProfile.RippleTrail, 0.5f, 0.1f, 1), 1e-4f);
        // No strength, no ripple, whatever else it says.
        Assert.Equal(0, GlowProfile.RippleAt(0.3f, 0.3f, 0, 0));
        Assert.Equal(0, GlowProfile.RippleAt(0.3f, 0.3f, 0, -1));
        Assert.Equal(0, GlowProfile.RippleAt(0.3f, 0.3f, 0, float.NaN));
    }

    [Fact]
    public void FlowMeanIsTheCometsAverage()
    {
        // FlowMean is FlowFloor + (1 - FlowFloor) x CometCount x (the head's Gaussian half, CometHead sqrt(pi) / 2,
        // plus the tail, CometTail): what the shaders blend to deep inside.
        float formula = GlowProfile.FlowFloor + (1 - GlowProfile.FlowFloor) * GlowProfile.CometCount *
            (GlowProfile.CometHead * MathF.Sqrt(MathF.PI) / 2 + GlowProfile.CometTail);
        Near(GlowProfile.FlowMean, formula, 1e-6f);
        // Averaged round the perimeter it is within 0.3 % (each tail is cut short at the next head), so the blend to
        // it deep inside neither brightens nor dims the light there.
        GlowConstants c = Flowing(1920, 1080, 1, 1, 0.3f, 0);
        double sum = 0;
        const int samples = 200_000;
        for (int i = 0; i < samples; i++)
            sum += GlowProfile.Flow(c, (i + 0.5f) / samples);
        Assert.InRange(sum / samples - GlowProfile.FlowMean, -0.003, 0.003);
    }

    [Theory]
    [MemberData(nameof(RippleSets))]
    public void TheFlowLightStaysBetweenTheFloorAndOne(int set)
    {
        // The comets over the dimmer edge, the ripples on top like light through two filters: never under FlowFloor,
        // never over 1, and continuous round the loop.
        const float step = 1e-5f;
        (Ripple r0, Ripple r1, Ripple r2, Ripple r3) = Ripples(set);
        float strengths = r0.Strength + r1.Strength + r2.Strength + r3.Strength;
        foreach (float phase in CometPhases)
        {
            GlowConstants c = Flowing(1920, 1080, 1, 1, phase, set);
            float previous = GlowProfile.Flow(c, 0);
            for (int i = 1; i <= 100_000; i++)
            {
                float t = i * step % 1;
                float m = GlowProfile.Flow(c, t);
                Assert.InRange(m, GlowProfile.FlowFloor, 1);
                float bound = ((1 - GlowProfile.FlowFloor) * HeadSlope + strengths * FrontSlope) * step + 1e-5f;
                Assert.True(MathF.Abs(m - previous) <= bound, $"set {set}, phase {phase}: Flow jumps at t = {t}");
                previous = m;
            }
            // Where a ripple's front is, the light is full.
            if (r0.Strength == 1)
                Near(1, GlowProfile.Flow(c, Fraction(r0.Origin + r0.Radius)), 1e-4f);
        }
    }

    [Fact]
    public void WithoutAFlowAmountTheLightIsExactlyAsBefore()
    {
        // The Pulse movement and Idle Glow: Motion is 1 and nothing is computed, whatever comet phase and ripples
        // come with it, and Alpha is the profile x gain x Visibility it was before the Flow movement, bit for bit.
        foreach (EdgeStyle style in new[] { EdgeStyle.Glow, EdgeStyle.Line })
        {
            var state = new LightState(default, default, 0.6f, 0.8f, 0.45f, 6, 0.2f, 0.7f, 0.9f)
            {
                Flow = new FlowMotion(0, 0.42f, new Ripple(0.3f, 0.05f, 1), default, default, new Ripple(0.8f, 0, 0.5f)),
            };
            GlowConstants c = GlowConstants.Create(state, 1920, 1080, 1, 1, 8, Mean, style);
            GlowConstants plain = GlowConstants.Create(state with { Flow = default }, 1920, 1080, 1, 1, 8, Mean, style);
            Assert.Equal(plain.Gain, c.Gain);
            for (float x = 0.5f; x < 1920; x += 13)
                for (float y = 0.5f; y < 1080; y += 11)
                {
                    Assert.Equal(1, GlowProfile.Motion(c, x, y));
                    Assert.Equal(GlowProfile.Alpha(plain, x, y), GlowProfile.Alpha(c, x, y));
                }
        }
    }

    [Fact]
    public void DeepInsideTheMotionBlendsToItsMean()
    {
        // The perimeter coordinate has a seam deep inside; where the colours have blended to their mean (EdgeDepth
        // from ColorRadiusPx in), the motion is its mean too, the same everywhere, so the seam never shows. Within half
        // the colour radius of the edge it is the Flow light itself.
        foreach (int set in new[] { 0, 4 })
        {
            GlowConstants c = Flowing(1920, 1080, 1, 0.8f, 0.3f, set);
            float r = c.ColorRadiusPx;
            float mean = 1 + (GlowProfile.FlowMean - 1) * 0.8f;
            for (float x = 0.5f; x < 1920; x += 7)
                for (float y = 0.5f; y < 1080; y += 7)
                {
                    float edgeDepth = GlowProfile.EdgeDepth(x, y, 1920, 1080);
                    float motion = GlowProfile.Motion(c, x, y);
                    if (edgeDepth >= r)
                        Near(mean, motion, 1e-5f);
                    else if (edgeDepth <= 0.5f * r)
                    {
                        float t = GlowProfile.PerimeterT(x, y, 1920, 1080, r);
                        Near(1 + (GlowProfile.Flow(c, t) - 1) * 0.8f, motion, 1e-6f);
                    }
                }
        }
    }

    public static TheoryData<int, int, float, bool, EdgeStyle, bool> Surfaces() => new()
    {
        { 1920, 1080, 1f, false, EdgeStyle.Glow, false },
        { 2560, 1440, 1.5f, true, EdgeStyle.Glow, false },
        { 3840, 2160, 2f, false, EdgeStyle.Line, false },
        { 1366, 768, 1f, true, EdgeStyle.Line, false },
        { 3024, 1964, 2f, false, EdgeStyle.Line, true },   // a notched MacBook: the Mac draws the same math
        { 3024, 1964, 2f, false, EdgeStyle.Glow, true },
    };

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void AlphaWithTheFlowMovementStaysInRangeAndVariesSmoothlyRoundTheEdge(int width, int height, float dpi,
        bool half, EdgeStyle style, bool notched)
    {
        // Alpha at many points, for many comet phases, ripple sets and amounts: finite, 0..1, and between the light
        // without the motion (with the same raised gain) and FlowFloor's share of it. Along the edge, one pixel in, it
        // changes by no more than the comets' and ripples' slopes allow from one pixel to the next: round the
        // corners, across t's wrap at the top-left corner and past the notch, nothing steps.
        int w = OverlaySurface.BufferSize(width, half);
        int h = OverlaySurface.BufferSize(height, half);
        float renderScale = OverlaySurface.RenderScaleFor(width, half);
        NotchPx notch = notched ? new NotchPx(1262, 1762, 64) : default;
        foreach (float amount in new[] { 0.25f, 1f })
            foreach (float phase in new[] { 0f, 0.37f, 0.93f })
                for (int set = 0; set <= 4; set++)
                {
                    (Ripple r0, Ripple r1, Ripple r2, Ripple r3) = Ripples(set);
                    var state = new LightState(default, default, 0.6f, 0.8f, 0.45f, 6, 0.1f, 0.5f, 1)
                    {
                        Flow = new FlowMotion(amount, phase, r0, r1, r2, r3),
                    };
                    GlowConstants c = GlowConstants.Create(state, w, h, dpi, renderScale, 8, Mean, style, notch);
                    GlowConstants still = c;
                    still.FlowAmount = 0;
                    string what = $"{width}x{height} at {dpi}{(half ? " half" : "")}, {style}{(notched ? " notched" : "")}, " +
                        $"amount {amount}, phase {phase}, ripples {set}";

                    for (float x = 0.5f; x < w; x += 29)
                        for (float y = 0.5f; y < h; y += 17)
                        {
                            float alpha = GlowProfile.Alpha(c, x, y);
                            float without = GlowProfile.Alpha(still, x, y);
                            Assert.True(float.IsFinite(alpha) && alpha >= 0 && alpha <= 1, $"{what}: alpha {alpha} at ({x}, {y})");
                            Assert.True(alpha <= without + 1e-6f, $"{what}: the motion brightens ({x}, {y})");
                            Assert.True(alpha >= without * (1 - (1 - GlowProfile.FlowFloor) * amount) - 1e-6f,
                                $"{what}: the motion dims ({x}, {y}) below the floor");
                        }

                    // One pixel in, clockwise round the whole edge.
                    float strengths = r0.Strength + r1.Strength + r2.Strength + r3.Strength;
                    float perimeter = 2 * (w + h);
                    float slope = ((1 - GlowProfile.FlowFloor) * HeadSlope + strengths * FrontSlope) * amount;
                    float previous = float.NaN;
                    foreach ((float x, float y) in Around(w, h))
                    {
                        float motion = GlowProfile.Motion(c, x, y);
                        if (!float.IsNaN(previous))
                            Assert.True(MathF.Abs(motion - previous) <= 1.5f * slope / perimeter + 1e-5f,
                                $"{what}: the motion steps by {motion - previous} at ({x}, {y})");
                        previous = motion;
                    }
                }
    }

    [Fact]
    public void TheCometsTravelRoundTheEdgeAsTheirPhaseGrows()
    {
        // Along the top edge, one pixel in, the brightest pixel is a comet's head (comet 0's, at t = 0.15); a larger
        // phase moves it left (t falls as the phase grows) by the phase's share of the perimeter.
        GlowConstants first = Flowing(1920, 1080, 1, 1, 0.85f, 0);
        GlowConstants later = first;
        later.CometPhase = 0.87f;
        float perimeterPx = PerimeterPx(first);
        float x0 = BrightestOnTop(first);
        float x1 = BrightestOnTop(later);
        Assert.InRange(x0 - x1, 0.02f * perimeterPx - 2, 0.02f * perimeterPx + 2);
        // The head is the full light: there the motion is 1.
        float y = 0.5f;
        float profile = GlowProfile.Glow(GlowProfile.Depth(x0, y, 1920, 1080, first.GlowRadiusPx),
            GlowProfile.PulsedShoulder(first.ShoulderPx, first.Pulse), GlowProfile.PulsedSpread(first.SpreadPx, first.Pulse));
        Near(profile, GlowProfile.Alpha(first, x0, y) / first.Gain / first.Visibility, 1e-3f);
    }

    // The constants of a 1920 x 1080-like surface with the Flow movement at amount, phase and ripple set.
    private static GlowConstants Flowing(int width, int height, float dpi, float amount, float phase, int set)
    {
        (Ripple r0, Ripple r1, Ripple r2, Ripple r3) = Ripples(set);
        var state = new LightState(default, default, 0.6f, 0.8f, 0.45f, 6, 0, 0.3f, 1)
        {
            Flow = new FlowMotion(amount, phase, r0, r1, r2, r3),
        };
        return GlowConstants.Create(state, width, height, dpi, 1, 0, Mean);
    }

    // Pixel centres one pixel in from the edge, clockwise from the top-left corner.
    private static IEnumerable<(float X, float Y)> Around(int w, int h)
    {
        for (int x = 0; x < w; x++) yield return (x + 0.5f, 0.5f);
        for (int y = 1; y < h; y++) yield return (w - 0.5f, y + 0.5f);
        for (int x = w - 2; x >= 0; x--) yield return (x + 0.5f, h - 0.5f);
        for (int y = h - 2; y >= 0; y--) yield return (0.5f, y + 0.5f);
    }

    // The colour path's length in pixels (GlowProfile.PerimeterT's total).
    private static float PerimeterPx(in GlowConstants c)
    {
        float r = c.ColorRadiusPx;
        return 2 * (c.ScreenWidthPx - 2 * r + c.ScreenHeightPx - 2 * r) + 2 * MathF.PI * r;
    }

    private static float BrightestOnTop(in GlowConstants c)
    {
        // Between the corners' arcs, where the profile is the same at every x and only the motion varies.
        float best = float.NegativeInfinity, at = 0;
        for (float x = c.ColorRadiusPx + 0.5f; x < c.ScreenWidthPx - c.ColorRadiusPx; x++)
        {
            float alpha = GlowProfile.Alpha(c, x, 0.5f);
            if (alpha > best)
            {
                best = alpha;
                at = x;
            }
        }
        return at;
    }

    private static float Fraction(float x) => x - MathF.Floor(x);

    private static void Near(float expected, float actual, float tolerance) =>
        Assert.True(MathF.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual} (tolerance {tolerance})");
}
