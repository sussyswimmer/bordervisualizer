using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// GlowProfile (soft edge, Maxwell's feedback round 1: "a very obvious line or bar around the screen ... I want it to
// fade in more"): the edge profile Glow.hlsl draws, which the Settings preview copies. Profiles are read the way the
// shader computes them, in the surface's own pixels (swap-chain pixels at half render scale), before the dither, and
// in 8-bit code values (alpha x 255).
public sealed class GlowProfileTests
{
    private static readonly Rgb Mean = new(0.2f, 0.3f, 0.4f);
    private static readonly Settings Defaults = new();

    private static readonly float[] Thicknesses = [0, 0.5f, 1, 2, 3, 4, 6, 8, 12, 20, 30, 40];
    private static readonly float[] Glows = [0, 0.1f, 0.15f, 0.2f, 0.3f, 0.45f, 0.6f, 0.9f, 1];

    // Intensity and Pulse together. Up to a gain of 0.9 (Intensity x (1 + 0.25 Pulse)) the gain is used as it is;
    // above it GlowProfile.Gain rolls it off under 1, so the profile is scaled, never reshaped. Default Brightness 0.8
    // tops out at a gain of 0.8 without a beat and 1 on a full one; Brightness 100 % reaches 1.25.
    private static readonly (float Intensity, float Pulse)[] BelowTheKnee = [(0.3f, 0), (0.5f, 0), (0.72f, 0), (0.72f, 0.25f), (0.6f, 0.5f), (0.8f, 0), (0.9f, 0)];
    private static readonly (float Intensity, float Pulse)[] AnyGain = [.. BelowTheKnee, (0.8f, 1), (1, 0), (1, 0.5f), (1, 1)];

    // Monitors at 100, 125, 150 and 200 % scaling, at full and at half render scale.
    public static TheoryData<int, int, float, bool> Surfaces() => new()
    {
        { 1920, 1080, 1f, false }, { 1920, 1080, 1f, true },
        { 2560, 1440, 1.25f, false }, { 2560, 1440, 1.25f, true },
        { 1920, 1080, 1.5f, false }, { 1920, 1080, 1.5f, true },
        { 3840, 2160, 1.5f, false }, { 3840, 2160, 1.5f, true },
        { 3840, 2160, 2f, false }, { 3840, 2160, 2f, true },
    };

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void TheLightIsBrightestAtTheEdgeAndOnlyFades(int width, int height, float dpi, bool half)
    {
        // Every setting, every brightness and beat: no pixel is brighter than the one outside it, along an edge and
        // along the diagonal out of a corner (where the rounded fields bend the light round).
        foreach (float thickness in Thicknesses)
            foreach (float glow in Glows)
                foreach ((float intensity, float pulse) in AnyGain)
                {
                    GlowConstants c = Surface(width, height, dpi, half, thickness, glow, intensity, pulse);
                    string what = Describe(width, height, dpi, half, thickness, glow, intensity, pulse);
                    double[] edge = EdgeCodes(c);
                    Assert.True(edge[0] > 0, $"{what}: the edge is dark");
                    for (int k = 1; k < edge.Length; k++)
                        Assert.True(edge[k] <= edge[k - 1] + 1e-3, $"{what}: pixel {k} is brighter than pixel {k - 1}");
                    double[] diagonal = DiagonalCodes(c);
                    for (int k = 1; k < diagonal.Length; k++)
                        Assert.True(diagonal[k] <= diagonal[k - 1] + 1e-3, $"{what}: diagonal pixel {k} is brighter than {k - 1}");
                }
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void TheDefaultLookFadesWithoutALine(int width, int height, float dpi, bool half)
    {
        // At the default Thickness and Glow the light falls from the edge with a slope that only ever eases off: no
        // inflection, which is where the eye puts a line. That holds at every brightness and beat, because the gain
        // only scales the profile. The solid core's inner edge made the drop between neighbours jump by about 29 code
        // values at these settings (see TheOldSolidCoreFailsTheseChecks).
        foreach ((float intensity, float pulse) in AnyGain)
        {
            GlowConstants c = Surface(width, height, dpi, half, Defaults.CoreThicknessDip, Defaults.Glow, intensity, pulse);
            double growth = LargestGrowthOfTheDrop(EdgeCodes(c), from: 0);
            Assert.True(growth <= 0.1, $"{Describe(width, height, dpi, half, Defaults.CoreThicknessDip, Defaults.Glow, intensity, pulse)}: " +
                $"the drop between neighbours grows by {growth:0.00} code values");
        }
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void NoSettingDrawsABar(int width, int height, float dpi, bool half)
    {
        // At any Thickness, Glow, brightness and beat the drop from one pixel to the next never jumps, inside the
        // outermost pixel pair, by more than about a code value (a band much wider than its glow eases in over a soft
        // shoulder; the worst measured is 0.94 up to a gain of 0.9 and 1.02 at the largest gain: Thickness 30 on
        // Glow 0.2, 1080p at 150 % and half scale, a full beat at Brightness 100 %). The outermost pair may round over
        // by a few (5.9 at most, a wide band on a hairline glow, both near full at the very edge). The solid core's
        // inner edge jumped by 29 to 31; clipping each pixel instead of the gain drew a rim on the beat (see
        // TheseChecksCatchAKinkedBlendAndAClippedPixel).
        foreach (float thickness in Thicknesses)
            foreach (float glow in Glows)
                foreach ((float intensity, float pulse) in AnyGain)
                {
                    GlowConstants c = Surface(width, height, dpi, half, thickness, glow, intensity, pulse);
                    string what = Describe(width, height, dpi, half, thickness, glow, intensity, pulse);
                    double[] edge = EdgeCodes(c);
                    double inside = LargestGrowthOfTheDrop(edge, from: 2);
                    Assert.True(inside <= 1.1, $"{what}: the drop grows by {inside:0.00} code values inside the edge");
                    double outermost = LargestGrowthOfTheDrop(edge, from: 0);
                    Assert.True(outermost <= 6, $"{what}: the drop grows by {outermost:0.00} code values at the edge");
                }
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void TheFadeNeverEasesOffFasterThanItsLights(int width, int height, float dpi, bool half)
    {
        // The other direction of a kink: where a steep light hands over to a shallow one with a jump in the slope, the
        // drop between neighbours shrinks suddenly (a Mach band; max(glow, band) does that). The glow and the band are
        // exponentials, and their screen blend subtracts their product, which falls faster than either, so from one
        // pixel pair to the next the drop shrinks at most by the faster light's own factor, e^(-1 / its e-folding
        // distance). Along the edge both fields' distances are the pixel's, so the bound is exact.
        foreach (float thickness in Thicknesses)
            foreach (float glow in Glows)
                foreach ((float intensity, float pulse) in AnyGain)
                {
                    GlowConstants c = Surface(width, height, dpi, half, thickness, glow, intensity, pulse);
                    double shrink = LargestShrinkOfTheDrop(EdgeCodes(c), Fastest(c));
                    Assert.True(shrink <= 1e-3, $"{Describe(width, height, dpi, half, thickness, glow, intensity, pulse)}: " +
                        $"the drop shrinks by {shrink:0.000} code values more than its lights allow");
                }
    }

    [Fact]
    public void TheseChecksCatchAKinkedBlendAndAClippedPixel()
    {
        // max(glow, band) instead of the screen blend, at Thickness 20 on the default Glow (1080p at 100 %): the band
        // (b = 10 px) hands over to the glow (38 px) with the slope jumping from about 15 to 4 code values per pixel.
        GlowConstants c = Surface(1920, 1080, 1, false, 20, Defaults.Glow, 0.72f, 0);
        double[] kinked = Codes(c, (glow, band) => Math.Max(glow, band), clipEachPixelAt: 0);
        Assert.True(LargestShrinkOfTheDrop(kinked, Fastest(c)) > 5);
        Assert.True(LargestShrinkOfTheDrop(EdgeCodes(c), Fastest(c)) <= 1e-3);

        // Brightness 100 % on a full beat (gain 1.25), Thickness 12 on Glow 0.1: clipping each pixel, even with
        // SoftClip's smooth knee, squashes the brightest into a rim whose fade then steepens by about 17 code values.
        // Rolling off the gain instead keeps the profile's shape.
        c = Surface(1920, 1080, 1, false, 12, 0.1f, 1, 1);
        double[] clipped = Codes(c, GlowProfile.Combine, clipEachPixelAt: 1.25f);
        Assert.True(LargestGrowthOfTheDrop(clipped, from: 2) > 10);
        Assert.True(LargestGrowthOfTheDrop(EdgeCodes(c), from: 2) <= 1.1);
    }

    [Fact]
    public void TheOldSolidCoreFailsTheseChecks()
    {
        // Doc 04's step 2 as K1 shipped it, at the defaults on a 1080p screen at 100 %: the checks above catch it.
        GlowConstants c = Surface(1920, 1080, 1, false, Defaults.CoreThicknessDip, Defaults.Glow, 0.72f, 0);
        float core = Defaults.CoreThicknessDip;
        var old = new double[200];
        for (int k = 0; k < old.Length; k++)
        {
            float d = k + 0.5f;
            float t = Math.Clamp((d - (core - 1)) / 2, 0, 1);
            float solid = 1 - t * t * (3 - 2 * t);
            float glow = MathF.Exp(-d / c.SpreadPx) * 0.85f;
            old[k] = Math.Clamp(Math.Max(solid, glow), 0, 1) * 0.72f * 255;
        }
        Assert.True(LargestGrowthOfTheDrop(old, from: 2) > 20);
    }

    [Fact]
    public void AThinBandKeepsItsLightAtAnyScale()
    {
        // BandGain makes each pixel show the band's average over its width, so a band's light per unit of edge is half
        // its thickness (the integral of exp(-x / b) is b) however it falls on pixels: at full scale, at half render
        // scale (swap-chain pixels two screen pixels wide), and for bands much thinner than a pixel. The solid core
        // needed a separate fade in screen pixels for that (K3's CoreFade).
        foreach (float dpi in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f })
            for (float thicknessDip = 0; thicknessDip <= 6; thicknessDip += 0.125f)
            {
                double expected = 0.5 * thicknessDip * dpi; // screen pixels
                double full = BandLight(thicknessDip * dpi) * 1;
                double half = BandLight(thicknessDip * dpi * 0.5f) * 2;
                Assert.Equal(expected, full, 1e-4);
                Assert.Equal(expected, half, 1e-4);
            }
        // A 1-DIP band at 100 % lights the outermost pixel by its average there: 0.5 (1 - e^-2).
        Assert.Equal(0.5 * (1 - Math.Exp(-2)), GlowProfile.Band(0.5f, GlowProfile.BandScale(1), GlowProfile.BandGain(1)), 4);
        Assert.Equal(0, GlowProfile.BandGain(0));
        Assert.Equal(0, GlowProfile.BandGain(-1));
        Assert.Equal(0, GlowProfile.BandGain(float.NaN));
        Assert.Equal(0, GlowProfile.BandGain(float.PositiveInfinity));
        Assert.True(float.IsFinite(GlowProfile.BandGain(1e-6f)));
    }

    [Fact]
    public void TheGainRollsOffUnderOneAndOnlyScalesTheProfile()
    {
        // Up to the knee the gain is Intensity x (1 + 0.25 Pulse) itself; above it, it rolls off and never reaches 1,
        // so the profile (<= 1) times the gain never needs clipping. It still grows with both, so a beat always
        // brightens.
        Assert.Equal(0.72f, GlowProfile.Gain(0.72f, 0));
        Assert.Equal(0.9f, GlowProfile.Gain(0.72f, 1), 5);
        Assert.Equal(0.95f, GlowProfile.Gain(0.8f, 1), 4); // the default Brightness on a full beat
        Assert.InRange(GlowProfile.Gain(1, 1), 0.975f, 0.98f); // the largest gain
        for (float intensity = 0; intensity <= 1; intensity += 0.05f)
            for (float pulse = 0; pulse < 1; pulse += 0.05f)
            {
                float gain = GlowProfile.Gain(intensity, pulse);
                Assert.InRange(gain, 0, 0.98f);
                Assert.True(GlowProfile.Gain(intensity, pulse + 0.05f) >= gain);
                Assert.True(GlowProfile.Gain(intensity + 0.05f, pulse) > gain);
            }

        // The constants carry it: Brightness 100 % scales the default profile, it doesn't reshape it.
        GlowConstants bright = Surface(1920, 1080, 1, false, Defaults.CoreThicknessDip, Defaults.Glow, 1, 0);
        GlowConstants dim = Surface(1920, 1080, 1, false, Defaults.CoreThicknessDip, Defaults.Glow, 0.5f, 0);
        Assert.Equal(GlowProfile.Gain(1, 0), bright.Gain);
        double[] brightCodes = EdgeCodes(bright), dimCodes = EdgeCodes(dim);
        for (int k = 0; k < 100; k++)
            Assert.Equal(brightCodes[k] / bright.Gain, dimCodes[k] / dim.Gain, 1e-3);
    }

    [Fact]
    public void SoftClipLeavesEverythingBelowTheKneeAndNeverReachesOne()
    {
        for (float x = 0; x <= GlowProfile.KneeStart; x += 0.001f)
            Assert.Equal(x, GlowProfile.SoftClip(x));
        float previous = GlowProfile.SoftClip(GlowProfile.KneeStart);
        float previousSlope = 1;
        for (float x = GlowProfile.KneeStart + 0.001f; x <= 2; x += 0.001f)
        {
            float y = GlowProfile.SoftClip(x);
            float slope = (y - previous) / 0.001f;
            Assert.True(y > previous && y < 1, $"SoftClip({x}) = {y}");
            // C1 at the knee (the slope starts at 1), then only easing off.
            Assert.True(slope <= previousSlope + 0.01f, $"SoftClip's slope jumps at {x}");
            previous = y;
            previousSlope = slope;
        }
        Assert.Equal(0.95f, GlowProfile.SoftClip(1), 4);
    }

    [Fact]
    public void ScreenBlendNeverPassesOneAndAlwaysAddsLight()
    {
        // Strictly brighter as either light grows while the other is under 1: a piecewise blend such as max(), which
        // ignores the dimmer light and so kinks where the two cross, fails this.
        for (float glow = 0; glow <= GlowProfile.GlowPeak; glow += 0.05f)
            for (float band = 0; band <= 1; band += 0.05f)
            {
                float both = GlowProfile.Combine(glow, band);
                Assert.InRange(both, Math.Max(glow, band) - 1e-6f, 1);
                if (band < 0.99f)
                    Assert.True(GlowProfile.Combine(glow + 0.01f, band) > both, $"more glow than {glow} adds nothing ({band})");
                Assert.True(GlowProfile.Combine(glow, band + 0.01f) > both, $"more band than {band} adds nothing ({glow})");
            }
    }

    [Fact]
    public void TheBeatPushesTheGlowInAndTheFloorOnlyGuardsTheDivision()
    {
        Assert.Equal(38f * 1.35f, GlowProfile.PulsedSpread(38, 1), 4);
        Assert.Equal(38f, GlowProfile.PulsedSpread(38, 0));
        // One DIP at half scale on a 100 % monitor is half a pixel: not widened.
        Assert.Equal(0.5f, GlowProfile.PulsedSpread(0.5f, 0));
        Assert.Equal(GlowProfile.MinSpreadPx, GlowProfile.PulsedSpread(0.275f, 0)); // the Settings preview's hairline
    }

    [Fact]
    public void DepthIsTheDistanceInFromTheEdge()
    {
        // Square corners: the distance to the nearest edge. Rounded: outside the arc it is 0, the light's brightest.
        Assert.Equal(0.5f, GlowProfile.Depth(0.5f, 540, 1920, 1080, 0), 5);
        Assert.Equal(10.5f, GlowProfile.Depth(960, 1069.5f, 1920, 1080, 50), 4);
        Assert.Equal(0.5f, GlowProfile.Depth(0.5f, 0.5f, 1920, 1080, 0), 5);
        Assert.Equal(0, GlowProfile.Depth(0.5f, 0.5f, 1920, 1080, 50));
        Assert.Equal(540, GlowProfile.Depth(960, 540, 1920, 1080, 0), 3);
    }

    internal static GlowConstants Surface(int width, int height, float dpi, bool half, float thicknessDip, float glow,
        float intensity, float pulse)
    {
        int w = OverlaySurface.BufferSize(width, half);
        int h = OverlaySurface.BufferSize(height, half);
        var state = new LightState(default, default, 0.6f, intensity, glow, thicknessDip, 0, pulse, 1);
        return GlowConstants.Create(state, w, h, dpi, OverlaySurface.RenderScaleFor(width, half), 0, Mean);
    }

    // Alpha x 255 at the pixel centres along the middle of the left edge, out to half the shorter side, as Glow.hlsl
    // step 2 computes it (before the dither).
    internal static double[] EdgeCodes(in GlowConstants c)
    {
        int count = (int)(Math.Min(c.ScreenWidthPx, c.ScreenHeightPx) / 2);
        var codes = new double[count];
        for (int k = 0; k < count; k++)
            codes[k] = Code(c, k + 0.5f, 0.5f * c.ScreenHeightPx);
        return codes;
    }

    // The same along the diagonal from the top-left corner.
    private static double[] DiagonalCodes(in GlowConstants c)
    {
        int count = (int)(Math.Min(c.ScreenWidthPx, c.ScreenHeightPx) / 2);
        var codes = new double[count];
        for (int k = 0; k < count; k++)
            codes[k] = Code(c, k + 0.5f, k + 0.5f);
        return codes;
    }

    private static double Code(in GlowConstants c, float x, float y)
    {
        float bandDistance = GlowProfile.Depth(x, y, c.ScreenWidthPx, c.ScreenHeightPx, c.BandRadiusPx);
        float glowDistance = GlowProfile.Depth(x, y, c.ScreenWidthPx, c.ScreenHeightPx, c.GlowRadiusPx);
        float shape = GlowProfile.Combine(GlowProfile.Glow(glowDistance, GlowProfile.PulsedSpread(c.SpreadPx, c.Pulse)),
            GlowProfile.Band(bandDistance, c.BandScalePx, c.BandGain));
        return shape * c.Gain * c.Visibility * 255;
    }

    // EdgeCodes drawn another way, for the checks that must catch it: with another blend, and (when clipEachPixelAt
    // is not 0) with that raw gain clipped on each pixel by SoftClip instead of rolled off once (the first draft).
    private static double[] Codes(in GlowConstants c, Func<float, float, float> combine, float clipEachPixelAt)
    {
        int count = (int)(Math.Min(c.ScreenWidthPx, c.ScreenHeightPx) / 2);
        float spread = GlowProfile.PulsedSpread(c.SpreadPx, c.Pulse);
        var codes = new double[count];
        for (int k = 0; k < count; k++)
        {
            float d = k + 0.5f;
            float shape = combine(GlowProfile.Glow(d, spread), GlowProfile.Band(d, c.BandScalePx, c.BandGain));
            float alpha = clipEachPixelAt > 0 ? GlowProfile.SoftClip(shape * clipEachPixelAt) : shape * c.Gain;
            codes[k] = alpha * c.Visibility * 255;
        }
        return codes;
    }

    // How much the drop from one pixel to the next ever grows from one pair to the next, from pixel `from` inward:
    // never positive for a light whose slope only eases off; a step shows as a jump the size of the step.
    private static double LargestGrowthOfTheDrop(double[] codes, int from)
    {
        double largest = double.NegativeInfinity;
        for (int k = from + 2; k < codes.Length; k++)
        {
            double drop = codes[k - 1] - codes[k];
            double before = codes[k - 2] - codes[k - 1];
            largest = Math.Max(largest, drop - before);
        }
        return largest;
    }

    // How far the drop from one pixel to the next ever falls below `factor` times the drop before it: never positive
    // for a sum of exponentials that fall no faster than e^(-1 / pixel) by that factor; a convex kink shows as a sudden
    // shrink.
    private static double LargestShrinkOfTheDrop(double[] codes, double factor)
    {
        double largest = double.NegativeInfinity;
        for (int k = 2; k < codes.Length; k++)
        {
            double drop = codes[k - 1] - codes[k];
            double before = codes[k - 2] - codes[k - 1];
            largest = Math.Max(largest, factor * before - drop);
        }
        return largest;
    }

    // Per pixel, the faster of the two lights falls by this factor: e^(-1 / the shorter e-folding distance).
    private static double Fastest(in GlowConstants c)
    {
        double spread = GlowProfile.PulsedSpread(c.SpreadPx, c.Pulse);
        double fastest = c.BandGain > 0 ? Math.Min(spread, c.BandScalePx) : spread;
        return Math.Exp(-1 / fastest);
    }

    // The band's light per unit of edge in the surface's pixels: the sum over pixel centres of its pixel averages.
    private static double BandLight(float thicknessPx)
    {
        float scale = GlowProfile.BandScale(thicknessPx);
        float gain = GlowProfile.BandGain(thicknessPx);
        double sum = 0;
        for (int k = 0; k < 4000; k++)
            sum += GlowProfile.Band(k + 0.5f, scale, gain);
        return sum;
    }

    private static string Describe(int width, int height, float dpi, bool half, float thickness, float glow, float intensity,
        float pulse) =>
        $"{width}x{height} at {dpi * 100:0}%{(half ? " (half scale)" : "")}, thickness {thickness}, glow {glow}, " +
        $"intensity {intensity}, pulse {pulse}";
}
