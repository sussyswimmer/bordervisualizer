using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// GlowProfile (Maxwell's feedback: round 1, "a very obvious line or bar around the screen ... I want it to fade in
// more"; round 2, "a thin bright line right at the screen edge, then the glow fades in behind it"): the edge profile
// Glow.hlsl draws, which the Settings preview copies. Profiles are read the way the shader computes them, in the
// surface's own pixels (swap-chain pixels at half render scale), before the dither, and in 8-bit code values
// (alpha x 255). Perceived brightness is CIE L* of the glow blended over a background as DWM blends an 8-bit
// premultiplied surface in SDR (in sRGB-encoded space).
public sealed class GlowProfileTests
{
    private static readonly Rgb Mean = new(0.2f, 0.3f, 0.4f);
    private static readonly Settings Defaults = new();

    private static readonly float[] Thicknesses = [0, 0.5f, 1, 2, 3, 4, 6, 8, 12, 20, 30, 40];
    private static readonly float[] Glows = [0, 0.1f, 0.15f, 0.2f, 0.3f, 0.45f, 0.6f, 0.9f, 1];

    // Intensity and Pulse together. Up to a gain of 0.9 (Intensity x (1 + 0.25 Pulse)) the gain is used as it is;
    // above it GlowProfile.Gain rolls it off under 1, so the profile is scaled, never reshaped. Default Brightness 0.8
    // tops out at a gain of 0.8 without a beat and 1 on a full one; Brightness 100 % reaches 1.25.
    private static readonly (float Intensity, float Pulse)[] AnyGain =
        [(0.3f, 0), (0.5f, 0), (0.72f, 0), (0.72f, 0.25f), (0.6f, 0.5f), (0.8f, 0), (0.9f, 0), (0.8f, 1), (1, 0), (1, 0.5f), (1, 1)];

    // Backgrounds as sRGB codes: a dark wallpaper or dark-mode window, and a mid grey. Glow colours as sRGB codes:
    // white, an album orange and an album blue.
    private static readonly double[] Backgrounds = [24, 128];
    private static readonly (double R, double G, double B)[] Colors = [(255, 255, 255), (255, 140, 70), (40, 110, 230)];

    // Monitors at 100, 125, 150 and 200 % scaling, at full and at half render scale.
    public static TheoryData<int, int, float, bool> Surfaces() => new()
    {
        { 1920, 1080, 1f, false }, { 1920, 1080, 1f, true },
        { 2560, 1440, 1.25f, false }, { 2560, 1440, 1.25f, true },
        { 2560, 1440, 1.5f, false }, { 2560, 1440, 1.5f, true },
        { 1920, 1080, 1.5f, false }, { 1920, 1080, 1.5f, true },
        { 3840, 2160, 1.5f, false }, { 3840, 2160, 1.5f, true },
        { 3840, 2160, 2f, false }, { 3840, 2160, 2f, true },
    };

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void TheLightIsBrightestAtTheEdgeAndOnlyFades(int width, int height, float dpi, bool half)
    {
        // Every setting, every brightness and beat: no pixel is brighter than the one outside it, along an edge and
        // along the diagonal out of a corner (where the rounded field bends the light round).
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
    public void TheDefaultEdgeIsAFlatSoftShoulder(int width, int height, float dpi, bool half)
    {
        // Round 2's acceptance: at the default Thickness and Glow, the light within max(4 px, Thickness / 2) of the
        // edge (screen pixels) stays within 3 % of the edge pixel, in code values and in perceived brightness over a
        // dark and a mid background, at every brightness and beat; and it falls more slowly between the outermost two
        // pixels than between the next two, so the steepest fall is never at the edge. Round 1's band fell 13 % over
        // the first 4 px with its steepest fall at the edge: a thin bright line (see
        // TheseChecksCatchRoundOnesBandAndTheSolidCore). Measured worst: 2.7 % in code values and 2.2 % in L*, at
        // 1920 x 1080 and 150 %.
        float renderScale = OverlaySurface.RenderScaleFor(width, half);
        double reach = Math.Max(4, Defaults.CoreThicknessDip * dpi / 2) * renderScale; // in the surface's pixels
        foreach ((float intensity, float pulse) in AnyGain)
        {
            GlowConstants c = Surface(width, height, dpi, half, Defaults.CoreThicknessDip, Defaults.Glow, intensity, pulse);
            string what = Describe(width, height, dpi, half, Defaults.CoreThicknessDip, Defaults.Glow, intensity, pulse);
            double[] edge = EdgeCodes(c);
            Assert.True(edge[0] - edge[1] < edge[1] - edge[2], $"{what}: the light falls fastest at the edge");
            for (int k = 0; k + 0.5 <= reach; k++)
            {
                Assert.True(edge[k] >= 0.97 * edge[0], $"{what}: pixel {k} is {100 * (1 - edge[k] / edge[0]):0.0} % dimmer than the edge");
                foreach (double background in Backgrounds)
                    foreach ((double R, double G, double B) color in Colors)
                    {
                        double atEdge = Lightness(edge[0] / 255, color, background);
                        double here = Lightness(edge[k] / 255, color, background);
                        Assert.True(Math.Abs(here - atEdge) <= 0.03 * atEdge,
                            $"{what}: over {background}, pixel {k} looks {100 * (1 - here / atEdge):0.0} % dimmer than the edge");
                    }
            }
        }
    }

    [Fact]
    public void TheSlopeAtTheEdgeIsZeroAtEverySetting()
    {
        // The hyperbola sqrt(d^2 + w^2) - w is about d^2 / 2w at the edge, so the light starts flat for any shoulder
        // above 0, Thickness 0 (a shoulder of one DIP) included, at any DPI and render scale. Over the first hundredth
        // of a pixel it falls by under 5 % of what an exponential glow from the edge would (it is 0.01 / 2w of it:
        // at most 1 %, for the half-pixel shoulder of Thickness 0 at half scale on a 100 % monitor).
        const float Delta = 0.01f;
        foreach (float pixelsPerDip in new[] { 0.5f, 0.625f, 0.75f, 1f, 1.25f, 1.5f, 2f })
            foreach (float thickness in Thicknesses)
                foreach (float glow in Glows)
                    foreach (float pulse in new[] { 0f, 0.5f, 1f })
                    {
                        float shoulder = GlowProfile.PulsedShoulder(GlowProfile.ShoulderFor(thickness * pixelsPerDip, pixelsPerDip), pulse);
                        float spread = GlowProfile.PulsedSpread(GlowProfile.SpreadFor(glow, (int)(1080 * pixelsPerDip), pixelsPerDip), pulse);
                        double fall = GlowProfile.Glow(0, shoulder, spread) - GlowProfile.Glow(Delta, shoulder, spread);
                        double exponential = GlowProfile.GlowPeak * (1 - Math.Exp(-Delta / spread));
                        Assert.True(fall <= 0.05 * exponential,
                            $"thickness {thickness} at {pixelsPerDip} px/DIP, glow {glow}, pulse {pulse}: the edge falls {fall / exponential:0.000} as fast as an exponential");
                        Assert.Equal(GlowProfile.GlowPeak, GlowProfile.Glow(0, shoulder, spread), 6);
                    }
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void TheDefaultLookFadesWithoutALine(int width, int height, float dpi, bool half)
    {
        // At the default Thickness and Glow, the drop from one pixel to the next never grows by more than one code
        // value per screen pixel (at half scale a surface pixel is two screen pixels, so its growth is divided by
        // four, the square of the spacing), at any brightness and beat, the outermost pair included: no step anywhere.
        // Measured worst: 0.81 at 1920 x 1080 and 100 % at Brightness 100 %. The solid core's inner edge grew by about
        // 29 code values at these settings.
        float renderScale = OverlaySurface.RenderScaleFor(width, half);
        foreach ((float intensity, float pulse) in AnyGain)
        {
            GlowConstants c = Surface(width, height, dpi, half, Defaults.CoreThicknessDip, Defaults.Glow, intensity, pulse);
            double growth = LargestGrowthOfTheDrop(EdgeCodes(c)) * renderScale * renderScale;
            Assert.True(growth <= 1, $"{Describe(width, height, dpi, half, Defaults.CoreThicknessDip, Defaults.Glow, intensity, pulse)}: " +
                $"the drop between neighbours grows by {growth:0.00} code values per screen pixel");
        }
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void NoSettingDrawsALine(int width, int height, float dpi, bool half)
    {
        // At any Thickness, Glow, brightness and beat:
        // - the light never falls faster than the glow's own exponential at its steepest, GlowPeak x gain / spread per
        //   pixel: nothing steeper than the glow (a band, a core) packs light against the edge;
        // - the drop between neighbours grows by at most the shoulder's own curvature at the edge,
        //   GlowPeak x gain / (shoulder x spread): the fade eases in over the shoulder's width, never in one step;
        // - the drop rises to one peak and then only shrinks: one inflection, no second knee and no ring.
        // A narrow light (Glow near 0) is narrow by choice, so the bounds scale with it. Round 1's band fell twice as
        // fast as its glow at the defaults, and a shoulder with a hard knee grows its drop three times faster than
        // this allows (see TheseChecksCatchRoundOnesBandAndTheSolidCore).
        foreach (float thickness in Thicknesses)
            foreach (float glow in Glows)
                foreach ((float intensity, float pulse) in AnyGain)
                {
                    GlowConstants c = Surface(width, height, dpi, half, thickness, glow, intensity, pulse);
                    string what = Describe(width, height, dpi, half, thickness, glow, intensity, pulse);
                    double[] edge = EdgeCodes(c);
                    double peak = GlowProfile.GlowPeak * c.Gain * c.Visibility * 255;
                    double spread = GlowProfile.PulsedSpread(c.SpreadPx, c.Pulse);
                    double shoulder = GlowProfile.PulsedShoulder(c.ShoulderPx, c.Pulse);
                    double steepest = LargestDrop(edge);
                    Assert.True(steepest <= peak / spread * 1.001 + 1e-3,
                        $"{what}: the light falls {steepest:0.00} code values in a pixel, its glow at most {peak / spread:0.00}");
                    double growth = LargestGrowthOfTheDrop(edge);
                    Assert.True(growth <= peak / (shoulder * spread) * 1.01 + 1e-3,
                        $"{what}: the drop grows by {growth:0.00} code values, the shoulder's curvature allows {peak / (shoulder * spread):0.00}");
                    Assert.True(InflectionsOf(edge) <= 1, $"{what}: the fade bends from steepening to easing more than once");
                }
    }

    [Fact]
    public void TheseChecksCatchRoundOnesBandAndTheSolidCore()
    {
        // At the defaults on a 1080p screen at 100 % (Thickness 6, Glow 0.45, Intensity 0.72).
        GlowConstants c = Surface(1920, 1080, 1, false, Defaults.CoreThicknessDip, Defaults.Glow, 0.72f, 0);
        double spread = c.SpreadPx;
        double glowSlope = GlowProfile.GlowPeak * 0.72 * 255 / spread; // 4.1 code values per pixel
        double[] now = EdgeCodes(c);

        // Round 1 (6ac553b): a soft band exp(-d / (Thickness / 2)), with its pixel-average gain, screen-blended with
        // the exponential glow. It falls 13 % over the first 4 px, fastest at the very edge, and at its steepest twice
        // as fast as its glow: the thin bright line.
        double b = Defaults.CoreThicknessDip / 2.0;
        double bandGain = 2 * b * Math.Sinh(1 / (2 * b));
        double[] round1 = Profile(d =>
        {
            double glow = GlowProfile.GlowPeak * Math.Exp(-d / spread);
            double band = Math.Min(bandGain * Math.Exp(-d / b), 1);
            return 1 - (1 - glow) * (1 - band);
        });
        Assert.True(round1[3] < 0.9 * round1[0]);
        Assert.True(round1[0] - round1[1] > round1[1] - round1[2]);
        Assert.True(LargestDrop(round1) > 2 * glowSlope);
        // Round 1 at Thickness 0, the plain exponential glow: no band, but still steepest at the edge, 7.6 % down by
        // the fourth pixel.
        double[] exponential = Profile(d => GlowProfile.GlowPeak * Math.Exp(-d / spread));
        Assert.True(exponential[3] < 0.97 * exponential[0]);
        Assert.True(exponential[0] - exponential[1] > exponential[1] - exponential[2]);
        // Doc 04's solid core (K1), core = 1 - smoothstep(T - 1, T + 1, d), then max(core, glow): a bar whose inner
        // edge falls about 7 times faster than the glow.
        double[] solid = Profile(d =>
        {
            double t = Math.Clamp((d - (Defaults.CoreThicknessDip - 1)) / 2, 0, 1);
            return Math.Max(1 - t * t * (3 - 2 * t), GlowProfile.GlowPeak * Math.Exp(-d / spread));
        });
        Assert.True(LargestDrop(solid) > 5 * glowSlope);
        // A shoulder with a hard knee, exp(-max(d - w, 0) / spread): flat, then the full exponential slope at once.
        // Its drop grows by about 2 code values a pixel for two pixels, three times the shoulder's curvature.
        double w = c.ShoulderPx;
        double[] kneed = Profile(d => GlowProfile.GlowPeak * Math.Exp(-Math.Max(d - w, 0) / spread));
        Assert.True(LargestGrowthOfTheDrop(kneed) > 2.5 * GlowProfile.GlowPeak * 0.72 * 255 / (w * spread));

        // The shipped profile passes all of them.
        Assert.True(now[3] >= 0.97 * now[0]);
        Assert.True(now[0] - now[1] < now[1] - now[2]);
        Assert.True(LargestDrop(now) <= glowSlope);
        Assert.True(LargestGrowthOfTheDrop(now) <= GlowProfile.GlowPeak * 0.72 * 255 / (w * spread));

        static double[] Profile(Func<double, double> shape)
        {
            var codes = new double[400];
            for (int k = 0; k < codes.Length; k++)
                codes[k] = shape(k + 0.5) * 0.72 * 255;
            return codes;
        }
    }

    [Fact]
    public void ClippingEachPixelWouldDrawALineOnTheBeat()
    {
        // Brightness 100 % on a full beat (a raw gain of 1.25), Thickness 12 on Glow 0.1 at 1080p and 100 %: clipping
        // each pixel, even with SoftClip's smooth knee, squashes the brightest into a plateau whose fade then steepens
        // faster than the shoulder's curvature allows. Rolling off the gain instead keeps the profile's shape.
        GlowConstants c = Surface(1920, 1080, 1, false, 12, 0.1f, 1, 1);
        float shoulder = GlowProfile.PulsedShoulder(c.ShoulderPx, c.Pulse);
        float spread = GlowProfile.PulsedSpread(c.SpreadPx, c.Pulse);
        var clipped = new double[200];
        for (int k = 0; k < clipped.Length; k++)
            clipped[k] = GlowProfile.SoftClip(GlowProfile.Glow(k + 0.5f, shoulder, spread) * 1.25f) * 255;
        double allowed = GlowProfile.GlowPeak * c.Gain * 255 / (shoulder * spread);
        Assert.True(LargestGrowthOfTheDrop(clipped) > 1.2 * allowed);
        Assert.True(LargestGrowthOfTheDrop(EdgeCodes(c)) <= 1.01 * allowed);
    }

    [Fact]
    public void ThicknessIsTheWidthOfTheFlatShoulder()
    {
        // The shoulder is the Thickness in pixels, at least one DIP, so Thickness 0 still starts flat.
        Assert.Equal(9f, GlowProfile.ShoulderFor(6 * 1.5f, 1.5f));
        Assert.Equal(1.5f, GlowProfile.ShoulderFor(0, 1.5f));
        Assert.Equal(0.5f, GlowProfile.ShoulderFor(0, 0.5f)); // half scale on a 100 % monitor
        Assert.Equal(1f, GlowProfile.ShoulderFor(-3, 1));
        Assert.Equal(1f, GlowProfile.ShoulderFor(float.NaN, 1));
        Assert.Equal(1f, GlowProfile.ShoulderFor(float.PositiveInfinity, 1));

        // On the default glow, more Thickness keeps the light near the edge's brightness further in, and pushes the
        // whole fade in by about as much. At 1080p and 100 % the light stays within 3 % of the edge for 2, 4, 7 and
        // 10 px at Thickness 0, 6, 20 and 40, and within 10 % for 5, 8, 13 and 18 px. The flat part grows as the
        // square root of shoulder x spread (sqrt(0.06 w s) for 3 %): the hyperbola's knee is as soft as the shoulder is
        // wide, so even Thickness 40 eases into its fade instead of ending in a bar.
        float spread = GlowProfile.SpreadFor(Defaults.Glow, 1080, 1);
        double previousFlat = 0, previousReach = 0;
        foreach (float thickness in new[] { 0f, 2, 6, 12, 20, 30, 40 })
        {
            float shoulder = GlowProfile.ShoulderFor(thickness, 1);
            double flat = Where(0.97), reach = Where(0.05);
            Assert.True(flat >= previousFlat && reach > previousReach, $"thickness {thickness}");
            if (thickness >= 6)
                Assert.True(flat >= Math.Sqrt(0.05 * thickness * spread), $"thickness {thickness}: flat for only {flat} px");
            previousFlat = flat;
            previousReach = reach;

            // The first pixel past the edge where the light is under `fraction` of the edge's.
            double Where(double fraction)
            {
                int k = 0;
                while (GlowProfile.Glow(k + 0.5f, shoulder, spread) >= fraction * GlowProfile.GlowPeak) k++;
                return k;
            }
        }
        // Far in, the fade is the glow's, only the shoulder further in: within 2 % of exp(-(d - w) / spread) 300 px in.
        double farRatio = GlowProfile.Glow(300, 20, spread) / (GlowProfile.GlowPeak * Math.Exp(-(300 - 20) / spread));
        Assert.InRange(farRatio, 0.98, 1);
    }

    [Fact]
    public void TheLightKeepsItsShapeAtEveryScale()
    {
        // Every pixel measure converts at the render scale and the Glow mapping follows the short side, so at half
        // scale each surface pixel shows the full-scale light at its centre's screen position, and a 4K screen at
        // 200 % draws the light of a 1080p screen at 100 %, twice as large in pixels.
        foreach ((int width, int height, float dpi) in new[] { (1920, 1080, 1f), (2560, 1440, 1.25f), (3840, 2160, 1.5f), (3840, 2160, 2f) })
            foreach (float thickness in new[] { 0f, 6, 20, 40 })
                foreach (float glow in new[] { 0f, 0.1f, 0.45f, 1 })
                {
                    GlowConstants full = Surface(width, height, dpi, false, thickness, glow, 0.72f, 0.5f);
                    GlowConstants half = Surface(width, height, dpi, true, thickness, glow, 0.72f, 0.5f);
                    double[] halfCodes = EdgeCodes(half);
                    for (int k = 0; k < 200; k++)
                        Assert.Equal(Code(full, 2 * k + 1, 0.5f * full.ScreenHeightPx), halfCodes[k], 2);
                }
        foreach (float thickness in new[] { 0f, 6, 20, 40 })
            foreach (float glow in new[] { 0f, 0.1f, 0.45f, 1 })
            {
                double[] small = EdgeCodes(Surface(1920, 1080, 1, false, thickness, glow, 0.72f, 0));
                GlowConstants large = Surface(3840, 2160, 2, false, thickness, glow, 0.72f, 0);
                for (int k = 0; k < 200; k++)
                    Assert.Equal(small[k], Code(large, 2 * k + 1, 0.5f * large.ScreenHeightPx), 2);
            }
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
    public void TheBeatSwellsTheWholeLightInwardAndTheFloorOnlyGuardsTheDivision()
    {
        Assert.Equal(38f * 1.35f, GlowProfile.PulsedSpread(38, 1), 4);
        Assert.Equal(38f, GlowProfile.PulsedSpread(38, 0));
        // One DIP at half scale on a 100 % monitor is half a pixel: not widened.
        Assert.Equal(0.5f, GlowProfile.PulsedSpread(0.5f, 0));
        Assert.Equal(GlowProfile.MinSpreadPx, GlowProfile.PulsedSpread(0.275f, 0)); // the Settings preview's hairline
        // The shoulder widens by the same factor, so a full beat draws the same light 1.35 times as far in: the edge
        // stays as bright (before the gain) and flat, it never gains a rim.
        Assert.Equal(6f * 1.35f, GlowProfile.PulsedShoulder(6, 1), 4);
        Assert.Equal(6f, GlowProfile.PulsedShoulder(6, 0));
        for (float d = 0; d < 300; d += 7)
            Assert.Equal(GlowProfile.Glow(d, 6, 38), GlowProfile.Glow(d * 1.35f, GlowProfile.PulsedShoulder(6, 1),
                GlowProfile.PulsedSpread(38, 1)), 5);
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

    [Fact]
    public void TheFieldIsRoundedForTheWholeLight()
    {
        // At least two spreads plus the shoulder (where the light is down to about e^-2), at least the user's corner
        // radius, at most half the short side.
        Assert.Equal(2 * 38f + 6, GlowProfile.GlowRadius(0, 38, 6, 540));
        Assert.Equal(100f, GlowProfile.GlowRadius(100, 38, 6, 540));
        Assert.Equal(540f, GlowProfile.GlowRadius(0, 380, 40, 540));
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
        float distance = GlowProfile.Depth(x, y, c.ScreenWidthPx, c.ScreenHeightPx, c.GlowRadiusPx);
        float glow = GlowProfile.Glow(distance, GlowProfile.PulsedShoulder(c.ShoulderPx, c.Pulse),
            GlowProfile.PulsedSpread(c.SpreadPx, c.Pulse));
        return glow * c.Gain * c.Visibility * 255;
    }

    // CIE L* of the glow at this alpha (premultiplied, its colour as sRGB codes) over a grey background (an sRGB code),
    // blended in sRGB-encoded space as DWM blends 8-bit surfaces in SDR.
    private static double Lightness(double alpha, (double R, double G, double B) color, double background)
    {
        double y = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        return y > 216.0 / 24389 ? 116 * Math.Cbrt(y) - 16 : 24389.0 / 27 * y;

        double Linear(double channel)
        {
            double v = (channel * alpha + background * (1 - alpha)) / 255;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }

    // The largest drop from one pixel to the next.
    private static double LargestDrop(double[] codes)
    {
        double largest = 0;
        for (int k = 1; k < codes.Length; k++)
            largest = Math.Max(largest, codes[k - 1] - codes[k]);
        return largest;
    }

    // How much the drop from one pixel to the next ever grows from one pair to the next, the outermost pair
    // included: a shoulder eases its drop in, and a step shows as a jump the size of the step.
    private static double LargestGrowthOfTheDrop(double[] codes)
    {
        double largest = double.NegativeInfinity;
        for (int k = 2; k < codes.Length; k++)
            largest = Math.Max(largest, (codes[k - 1] - codes[k]) - (codes[k - 2] - codes[k - 1]));
        return largest;
    }

    // How many times the drop between neighbours turns from growing to shrinking or back (beyond float noise): one
    // for a shoulder easing into a fade, none for a plain exponential, two or more for a knee, a bar or a ring.
    private static int InflectionsOf(double[] codes)
    {
        int turns = 0, direction = 0;
        for (int k = 2; k < codes.Length; k++)
        {
            double change = (codes[k - 1] - codes[k]) - (codes[k - 2] - codes[k - 1]);
            if (Math.Abs(change) < 1e-3) continue;
            int sign = Math.Sign(change);
            if (direction != 0 && sign != direction) turns++;
            direction = sign;
        }
        return turns;
    }

    private static string Describe(int width, int height, float dpi, bool half, float thickness, float glow, float intensity,
        float pulse) =>
        $"{width}x{height} at {dpi * 100:0}%{(half ? " (half scale)" : "")}, thickness {thickness}, glow {glow}, " +
        $"intensity {intensity}, pulse {pulse}";
}
