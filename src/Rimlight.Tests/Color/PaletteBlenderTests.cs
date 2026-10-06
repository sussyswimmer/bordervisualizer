using System.Diagnostics;
using Rimlight.Core;
using Rimlight.Core.Color;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Color;

// C5: the palette crossfade (doc 05 §3) and the 64-texel perimeter gradient (doc 04 §3 steps 4–5, H-008 item 1).
public sealed class PaletteBlenderTests(ITestOutputHelper output)
{
    private const int Texels = 64;
    private const int Floats = Texels * 4;
    private const float HalfBlend = 0.04f; // doc 04 §3: blends 0.08 wide, centered on each boundary
    private static readonly TimeSpan AlbumFade = TimeSpan.FromMilliseconds(800); // doc 05 §3
    private static readonly TimeSpan ManualFade = TimeSpan.FromMilliseconds(200);

    private static readonly float[] Ratios = [0.1f, 0.2f, 0.25f, 0.33f, 0.5f, 0.6f, 0.75f, 0.9f];

    // Three pairs whose Oklab mixes stay inside sRGB, so nothing is clamped and every texel can be checked exactly.
    private static readonly Palette Violet = Palette.Default; // #7C5CFF / #22D3EE
    private static readonly Palette BlueOrange = Hex("#1E64FF", "#FF8C1E", "blue-orange");
    private static readonly Palette PinkTeal = Hex("#E0306A", "#20C0A0", "pink-teal");
    private static readonly Palette[] Palettes = [Violet, BlueOrange, PinkTeal];

    // ---- gradient layout (H-008 item 1) ----

    [Fact]
    public void TexelsAreTheOklabLoopAtTheirCenters()
    {
        foreach (Palette palette in Palettes)
            foreach (float ratio in Ratios)
            {
                float[] gradient = Fill(new PaletteBlender(palette), ratio);
                for (int i = 0; i < Texels; i++)
                {
                    Rgb expected = Reference(palette, (i + 0.5f) / Texels, ratio);
                    Assert.Equal(expected.R, gradient[i * 4], 1e-5f);
                    Assert.Equal(expected.G, gradient[i * 4 + 1], 1e-5f);
                    Assert.Equal(expected.B, gradient[i * 4 + 2], 1e-5f);
                    Assert.Equal(1f, gradient[i * 4 + 3]); // alpha 1, nothing premultiplied
                }
            }
    }

    [Fact]
    public void ColorsArePureAwayFromTheBlendsAndBlendedWithin004OfEachBoundary()
    {
        foreach (Palette palette in Palettes)
            foreach (float ratio in Ratios)
            {
                float[] gradient = Fill(new PaletteBlender(palette), ratio);
                int blendedAtStart = 0, blendedAtRatio = 0;
                for (int i = 0; i < Texels; i++)
                {
                    float u = (i + 0.5f) / Texels;
                    float toStart = MathF.Min(u, 1 - u), toRatio = MathF.Abs(u - ratio);
                    Rgb pure = u < ratio ? palette.Primary : palette.Secondary;
                    bool isPure = gradient[i * 4] == pure.R && gradient[i * 4 + 1] == pure.G && gradient[i * 4 + 2] == pure.B;
                    if (MathF.Min(toStart, toRatio) >= HalfBlend)
                    {
                        // Exactly the input color, not a round trip through Oklab.
                        Assert.True(isPure, $"texel {i} (u {u}) at ratio {ratio} should be pure");
                    }
                    else
                    {
                        Assert.False(isPure, $"texel {i} (u {u}) at ratio {ratio} should be blended");
                        if (toStart < toRatio) blendedAtStart++;
                        else blendedAtRatio++;
                    }
                }
                // 0.08 of the loop is 5.12 texel spacings: 5 or 6 texel centers fall inside each blend.
                Assert.InRange(blendedAtStart, 5, 6);
                Assert.InRange(blendedAtRatio, 5, 6);
            }
    }

    [Fact]
    public void BlendsAre008WideAndHalfwayAtEachBoundary()
    {
        foreach (float ratio in Ratios)
        {
            foreach (float boundary in new[] { 0f, ratio })
            {
                // Half way exactly at the boundary, and symmetric about it: the boundary at u = 0 rises into Primary,
                // the one at u = ratio falls out of it.
                Assert.Equal(0.5f, PaletteBlender.PrimaryWeight(boundary, ratio), 1e-6f);
                float sign = boundary == 0 ? 1 : -1;
                for (float x = 0.001f; x < 0.06f; x += 0.001f)
                {
                    float after = PaletteBlender.PrimaryWeight(Wrap(boundary + x), ratio);
                    float before = PaletteBlender.PrimaryWeight(Wrap(boundary - x), ratio);
                    Assert.Equal(1f, after + before, 1e-6f);
                    float primarySide = sign > 0 ? after : before;
                    if (x < HalfBlend - 1e-4f) Assert.InRange(primarySide, 0.5f + 1e-6f, 1 - 1e-7f);
                    if (x > HalfBlend + 1e-4f) Assert.Equal(1f, primarySide);
                }
            }
        }
        Assert.Equal(0.08f, PaletteBlender.BlendWidth);
    }

    [Fact]
    public void PrimaryCoversRatioOfTheLoop()
    {
        double worstTexel = 0, worstSampled = 0;
        foreach (Palette palette in Palettes)
            foreach (float ratio in Ratios)
            {
                float[] gradient = Fill(new PaletteBlender(palette), ratio);
                // How much Primary each texel holds: its position on the Oklab segment from Secondary to Primary.
                double texelMean = 0;
                for (int i = 0; i < Texels; i++) texelMean += PrimaryShare(palette, TexelOklab(gradient, i));
                texelMean /= Texels;

                // The same, sampled the way the shader samples it (linear filtering, WRAP), finely.
                double sampledMean = 0;
                const int samples = 8192;
                for (int k = 0; k < samples; k++) sampledMean += PrimaryShare(palette, Oklab.FromLinearSrgb(Sample(gradient, (k + 0.5f) / samples)));
                sampledMean /= samples;

                Assert.Equal(ratio, texelMean, 2e-4);
                Assert.Equal(ratio, sampledMean, 1e-3);
                worstTexel = Math.Max(worstTexel, Math.Abs(texelMean - ratio));
                worstSampled = Math.Max(worstSampled, Math.Abs(sampledMean - ratio));
            }
        output.WriteLine($"Primary share off ratio by at most {worstTexel:E2} (texels), {worstSampled:E2} (shader-sampled)");
    }

    [Theory]
    [InlineData(0f, 0.1f)]
    [InlineData(0.05f, 0.1f)]
    [InlineData(-5f, 0.1f)]
    [InlineData(float.NegativeInfinity, 0.1f)]
    [InlineData(0.95f, 0.9f)]
    [InlineData(1f, 0.9f)]
    [InlineData(float.PositiveInfinity, 0.9f)]
    [InlineData(float.NaN, 0.6f)] // the Settings default
    [InlineData(0.45f, 0.45f)]
    public void RatioIsClampedTo01Through09(float ratio, float used)
    {
        Assert.Equal(Fill(new PaletteBlender(BlueOrange), used), Fill(new PaletteBlender(BlueOrange), ratio));
        Assert.Equal(used, PaletteBlender.ClampRatio(ratio));
    }

    [Fact]
    public void BothColorsReachTheirPureValueAtTheRatioLimits()
    {
        // At 0.1 and 0.9 the shorter arc is 0.1 long, more than one 0.08 blend, so its middle is still pure.
        foreach (float ratio in new[] { 0.1f, 0.9f, -1f, 2f })
        {
            float[] gradient = Fill(new PaletteBlender(BlueOrange), ratio);
            Assert.Contains(Enumerable.Range(0, Texels), i => IsColor(gradient, i, BlueOrange.Primary));
            Assert.Contains(Enumerable.Range(0, Texels), i => IsColor(gradient, i, BlueOrange.Secondary));
            Assert.Equal(1f, PaletteBlender.PrimaryWeight(Math.Clamp(ratio, 0.1f, 0.9f) / 2, Math.Clamp(ratio, 0.1f, 0.9f)));
        }
    }

    // ---- the seam at u = 0 ----

    [Fact]
    public void TheBoundaryAcrossTexel63And0MirrorsTheOneAtTheRatio()
    {
        // At ratio 0.5 both boundaries fall half way between texels (0 between 63 and 0, 0.5 between 31 and 32), and
        // the loop is symmetric about each arc's middle: texel k equals texel 31 − k, and texel 32 + k equals 63 − k.
        // Texels 0 and 63 sit at the seam, their mirrors at the ratio boundary, so a seam that is not blended like
        // an ordinary boundary breaks the symmetry.
        foreach (Palette palette in Palettes)
        {
            float[] gradient = Fill(new PaletteBlender(palette), 0.5f);
            for (int k = 0; k < 32; k++)
            {
                AssertTexelsEqual(gradient, k, 31 - k, 1e-6f);
                AssertTexelsEqual(gradient, 32 + k, 63 - k, 1e-6f);
            }
            // The step across the seam is the step across the ratio boundary.
            Assert.Equal(
                Oklab.Distance(TexelOklab(gradient, 31), TexelOklab(gradient, 32)),
                Oklab.Distance(TexelOklab(gradient, 63), TexelOklab(gradient, 0)), 1e-5f);
        }
    }

    [Fact]
    public void EveryNeighborStepIncludingTheSeamIsASmoothBlendStep()
    {
        // The steepest a 0.08-wide smoothstep gets is 1.5 / 0.08 per unit of u, so neighbors 1/64 apart differ by at
        // most 18.75 / 64 ≈ 0.29 of the Oklab distance between the two colors. A hard edge anywhere, the seam
        // included, would be the whole distance.
        foreach (Palette palette in Palettes)
            foreach (float ratio in Ratios)
            {
                float[] gradient = Fill(new PaletteBlender(palette), ratio);
                float span = Oklab.Distance(Oklab.FromLinearSrgb(palette.Primary), Oklab.FromLinearSrgb(palette.Secondary));
                float limit = 1.5f / PaletteBlender.BlendWidth / Texels * span + 1e-5f;
                for (int i = 0; i < Texels; i++)
                {
                    float step = Oklab.Distance(TexelOklab(gradient, i), TexelOklab(gradient, (i + 1) % Texels));
                    Assert.True(step <= limit, $"step {i}→{(i + 1) % Texels} is {step} (limit {limit}) at ratio {ratio}");
                }
                // And the seam step is the one the ideal loop has between u = 127/128 and u = 1/128.
                Assert.Equal(
                    Oklab.Distance(Oklab.FromLinearSrgb(Reference(palette, 127 / 128f, ratio)), Oklab.FromLinearSrgb(Reference(palette, 1 / 128f, ratio))),
                    Oklab.Distance(TexelOklab(gradient, 63), TexelOklab(gradient, 0)), 2e-5f);
            }
    }

    [Fact]
    public void SampledLikeTheShaderTheLoopIsContinuousEverywhere()
    {
        // WRAP addressing with linear filtering turns the 64 texels into a closed piecewise-linear loop. It must stay
        // close to the ideal seamless loop at every u, across u = 0 too. Linear interpolation of a 0.08-wide smoothstep
        // at 1/64 spacing errs by at most f'' h² / 8 ≈ 0.029 of the weight, and the curve the Oklab mix traces in linear
        // RGB adds a little (0.039 of a channel at worst here). A seam would be off by about half the color distance.
        float worst = 0, worstU = 0;
        foreach (Palette palette in Palettes)
            foreach (float ratio in Ratios)
            {
                float[] gradient = Fill(new PaletteBlender(palette), ratio);
                const int samples = 4096;
                Rgb previous = Sample(gradient, 1 - 0.5f / samples);
                for (int k = 0; k < samples; k++)
                {
                    float u = (k + 0.5f) / samples;
                    Rgb sampled = Sample(gradient, u);
                    float error = MaxChannelDifference(sampled, Reference(palette, u, ratio));
                    if (error > worst) (worst, worstU) = (error, u);
                    // Neighboring samples 1/4096 apart (the first pair straddles u = 0) never jump.
                    Assert.True(MaxChannelDifference(sampled, previous) < 0.01f, $"jump at u {u}, ratio {ratio}");
                    previous = sampled;
                }
            }
        output.WriteLine($"worst shader-sampled deviation from the ideal loop: {worst:F4} at u {worstU:F4}");
        Assert.True(worst < 0.05f, $"deviation {worst} at u {worstU}");
    }

    // ---- color space ----

    [Fact]
    public void BoundariesBlendInOklabNotRgb()
    {
        // Every texel lies on the straight Oklab segment between the two colors. A linear-RGB blend of blue and
        // orange bows off that segment by about 0.07 at its middle (checked here, so the test can tell them apart).
        Oklab primary = Oklab.FromLinearSrgb(BlueOrange.Primary), secondary = Oklab.FromLinearSrgb(BlueOrange.Secondary);
        Rgb rgbMid = new(
            (BlueOrange.Primary.R + BlueOrange.Secondary.R) / 2,
            (BlueOrange.Primary.G + BlueOrange.Secondary.G) / 2,
            (BlueOrange.Primary.B + BlueOrange.Secondary.B) / 2);
        Assert.True(OffSegment(Oklab.FromLinearSrgb(rgbMid), secondary, primary) > 0.05f);

        foreach (float ratio in Ratios)
        {
            float[] gradient = Fill(new PaletteBlender(BlueOrange), ratio);
            for (int i = 0; i < Texels; i++)
                Assert.True(OffSegment(TexelOklab(gradient, i), secondary, primary) < 2e-5f, $"texel {i} at ratio {ratio}");
        }
    }

    [Fact]
    public void BlendsOutsideSrgbAreClampedPerChannel()
    {
        // White and red mix outside sRGB in Oklab: half way is linear red ≈ 1.11. The texels are clamped to 0..1 per
        // channel, as the shader's saturate would, and the other channels are left alone.
        var palette = new Palette(new Rgb(1, 1, 1), new Rgb(1, 0, 0), null);
        float[] gradient = Fill(new PaletteBlender(palette), 0.5f);
        Assert.All(gradient, value => Assert.InRange(value, 0f, 1f));

        Oklab white = Oklab.FromLinearSrgb(palette.Primary), red = Oklab.FromLinearSrgb(palette.Secondary);
        int clamped = 0;
        for (int i = 0; i < Texels; i++)
        {
            float w = PaletteBlender.PrimaryWeight((i + 0.5f) / Texels, 0.5f);
            Rgb raw = Mix(red, white, w).ToLinearSrgb();
            if (w is 0 or 1 || raw.R <= 1) continue; // pure texels are the exact input colors
            clamped++;
            Assert.Equal(1f, gradient[i * 4]);
            Assert.Equal(raw.G, gradient[i * 4 + 1], 1e-6f);
            Assert.Equal(raw.B, gradient[i * 4 + 2], 1e-6f);
        }
        Assert.True(clamped >= 4, $"{clamped} texels needed clamping");
    }

    // ---- crossfade (doc 05 §3) ----

    [Fact]
    public void CrossfadeEasesInAndOutInOklab()
    {
        var blender = new PaletteBlender(Violet);
        blender.SetTarget(BlueOrange, AlbumFade);
        Assert.True(blender.IsAnimating);
        AssertBlend(blender.Current, Violet, BlueOrange, 0f);

        // Smoothstep: 3t² − 2t³ of the elapsed share. Slow out of the old colors, half way at half time, slow into the
        // new ones. A linear fade would be at 0.25 / 0.5 / 0.75.
        float[] expected = [0.04296875f, 0.15625f, 0.31640625f, 0.5f, 0.68359375f, 0.84375f, 0.95703125f];
        for (int step = 0; step < expected.Length; step++)
        {
            blender.Update(0.1f);
            Assert.True(blender.IsAnimating);
            AssertBlend(blender.Current, Violet, BlueOrange, expected[step]);

            // The gradient shows the same colors: its pure texels are Current's colors.
            float[] gradient = Fill(blender, 0.6f);
            Assert.True(IsColor(gradient, 19, blender.Current.Primary), "Primary's middle");  // u ≈ 0.30
            Assert.True(IsColor(gradient, 51, blender.Current.Secondary), "Secondary's middle"); // u ≈ 0.80
        }
        blender.Update(0.1f);
        Assert.False(blender.IsAnimating);
        Assert.Same(BlueOrange, blender.Current);
    }

    [Fact]
    public void EaseIsSmoothstep()
    {
        Assert.Equal(0f, PaletteBlender.Ease(0));
        Assert.Equal(1f, PaletteBlender.Ease(1));
        Assert.Equal(0.5f, PaletteBlender.Ease(0.5f));
        Assert.Equal(0f, PaletteBlender.Ease(-1));
        Assert.Equal(1f, PaletteBlender.Ease(2));
        float previous = 0;
        for (int i = 1; i <= 100; i++)
        {
            float t = i / 100f;
            float e = PaletteBlender.Ease(t);
            Assert.Equal(t * t * (3 - 2 * t), e, 1e-6f);
            Assert.Equal(1f, e + PaletteBlender.Ease(1 - t), 1e-6f);
            Assert.True(e > previous);
            previous = e;
        }
        // Zero speed at both ends: the first and last 1 % of the time move the colors 0.03 % of the way.
        Assert.True(PaletteBlender.Ease(0.01f) < 3e-4f);
        Assert.True(1 - PaletteBlender.Ease(0.99f) < 3e-4f);
    }

    [Theory]
    [InlineData(800, 60, 48)]
    [InlineData(800, 144, 116)]
    [InlineData(200, 60, 12)]
    [InlineData(200, 30, 6)]
    public void CrossfadeEndsWithTheFrameThatReachesItsDuration(int milliseconds, int fps, int frames)
    {
        var blender = new PaletteBlender(Violet);
        blender.SetTarget(PinkTeal, TimeSpan.FromMilliseconds(milliseconds));
        int count = 0;
        while (blender.IsAnimating && count < 10_000)
        {
            blender.Update(1f / fps);
            count++;
        }
        Assert.Equal(frames, count);
        Assert.Same(PinkTeal, blender.Current);
        // Settled exactly on the target, the same gradient a blender created with it fills.
        Assert.Equal(Fill(new PaletteBlender(PinkTeal), 0.6f), Fill(blender, 0.6f));
    }

    [Fact]
    public void FadeEndsOnTheUpdateThatLandsExactlyOnItsDuration()
    {
        var blender = new PaletteBlender(Violet);
        blender.SetTarget(BlueOrange, TimeSpan.FromMilliseconds(750));
        blender.Update(0.5f);
        Assert.True(blender.IsAnimating);
        blender.Update(0.25f); // 0.75 s exactly
        Assert.False(blender.IsAnimating);
        Assert.Same(BlueOrange, blender.Current);
    }

    [Fact]
    public void FrameRateDoesNotChangeTheFade()
    {
        // 0.4 s of an 800 ms fade in four cadences, one of them irregular: each lands half way.
        float[][] cadences =
        [
            Enumerable.Repeat(1f / 30, 12).ToArray(),
            Enumerable.Repeat(1f / 60, 24).ToArray(),
            Enumerable.Repeat(1f / 240, 96).ToArray(),
            [0.005f, 0.03f, 0.015f, 0.1f, 0.001f, 0.049f, 0.02f, 0.08f, 0.1f],
        ];
        foreach (float[] cadence in cadences)
        {
            var blender = new PaletteBlender(Violet);
            blender.SetTarget(BlueOrange, AlbumFade);
            foreach (float dt in cadence) blender.Update(dt);
            AssertBlend(blender.Current, Violet, BlueOrange, 0.5f, 1e-4f);
        }
    }

    [Fact]
    public void RetargetMidFadeStartsFromTheDisplayedColors()
    {
        foreach (Palette next in new[] { PinkTeal, Violet }) // somewhere new, and straight back
        {
            var blender = new PaletteBlender(Violet);
            blender.SetTarget(BlueOrange, AlbumFade);
            for (int i = 0; i < 18; i++) blender.Update(1f / 60); // 0.3 s in
            float[] before = Fill(blender, 0.6f);
            Palette shown = blender.Current;

            blender.SetTarget(next, AlbumFade);
            Assert.True(blender.IsAnimating);
            // No jump: the same colors right after the retarget.
            AssertGradientsClose(before, Fill(blender, 0.6f), 1e-5f);
            AssertBlend(blender.Current, shown, shown, 0f);

            // Then a smooth fade to the new target, from the colors it started at.
            float[] previous = Fill(blender, 0.6f);
            int frames = 0;
            while (blender.IsAnimating)
            {
                blender.Update(1f / 60);
                frames++;
                float[] now = Fill(blender, 0.6f);
                // Smoothstep's top speed is 1.5× the average: at most 1.5 × (1/60) / 0.8 of any channel's range per frame.
                AssertGradientsClose(previous, now, 0.032f);
                previous = now;
                if (blender.IsAnimating && frames == 24) AssertBlend(blender.Current, shown, next, 0.5f, 1e-4f);
            }
            Assert.Equal(48, frames); // the new fade gets its whole duration
            Assert.Same(next, blender.Current);
            Assert.Equal(Fill(new PaletteBlender(next), 0.6f), previous);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void ZeroOrNegativeDurationSwitchesAtOnce(int milliseconds)
    {
        var blender = new PaletteBlender(Violet);
        blender.SetTarget(BlueOrange, TimeSpan.FromMilliseconds(milliseconds));
        Assert.False(blender.IsAnimating);
        Assert.Same(BlueOrange, blender.Current);
        Assert.Equal(Fill(new PaletteBlender(BlueOrange), 0.6f), Fill(blender, 0.6f));

        // It also cuts a running fade short.
        blender.SetTarget(PinkTeal, AlbumFade);
        blender.Update(0.2f);
        blender.SetTarget(Violet, TimeSpan.FromMilliseconds(milliseconds));
        Assert.False(blender.IsAnimating);
        Assert.Same(Violet, blender.Current);
        Assert.Equal(Fill(new PaletteBlender(Violet), 0.6f), Fill(blender, 0.6f));
    }

    [Fact]
    public void UpdateIgnoresNaNNegativeAndZeroTime()
    {
        var blender = new PaletteBlender(Violet);
        blender.SetTarget(BlueOrange, AlbumFade);
        blender.Update(0.2f);
        float[] before = Fill(blender, 0.6f);
        foreach (float dt in new[] { float.NaN, -1f, -1e-6f, 0f, float.NegativeInfinity, float.MinValue })
        {
            blender.Update(dt);
            Assert.True(blender.IsAnimating);
            Assert.Equal(before, Fill(blender, 0.6f));
        }
        AssertBlend(blender.Current, Violet, BlueOrange, 0.15625f);

        // An infinite step simply finishes the fade.
        blender.Update(float.PositiveInfinity);
        Assert.False(blender.IsAnimating);
        Assert.Same(BlueOrange, blender.Current);

        // While idle, Update changes nothing.
        foreach (float dt in new[] { float.NaN, -1f, 0f, 1f, float.PositiveInfinity }) blender.Update(dt);
        Assert.Same(BlueOrange, blender.Current);
    }

    [Fact]
    public void TargetWithTheDisplayedColorsNeedsNoFade()
    {
        var blender = new PaletteBlender(Violet);
        var sameColors = new Palette(Violet.Primary, Violet.Secondary, "track-2");
        blender.SetTarget(sameColors, AlbumFade);
        Assert.False(blender.IsAnimating); // nothing would change on screen, so the renderer may idle
        Assert.Same(sameColors, blender.Current);
        Assert.Equal(Fill(new PaletteBlender(Violet), 0.6f), Fill(blender, 0.6f));
    }

    [Fact]
    public void LongAndTinyDurationsBehave()
    {
        var blender = new PaletteBlender(Violet);
        blender.SetTarget(BlueOrange, TimeSpan.MaxValue);
        blender.Update(3600);
        Assert.True(blender.IsAnimating);
        Assert.All(Fill(blender, 0.6f), value => Assert.True(float.IsFinite(value)));

        blender.SetTarget(PinkTeal, TimeSpan.FromTicks(1));
        Assert.True(blender.IsAnimating);
        blender.Update(1f / 60);
        Assert.False(blender.IsAnimating);
        Assert.Same(PinkTeal, blender.Current);
    }

    // ---- Current ----

    [Fact]
    public void CurrentIsTheTargetWhenIdleAndOneCachedBlendPerFadeStep()
    {
        var blender = new PaletteBlender(Violet);
        Assert.Same(Violet, blender.Current);

        var album = BlueOrange with { SourceTrackId = "app|artist|title" };
        blender.SetTarget(album, AlbumFade);
        blender.Update(0.3f);
        Palette first = blender.Current;
        Assert.Same(first, blender.Current); // read twice, built once
        Assert.Equal("app|artist|title", first.SourceTrackId);

        blender.Update(0.1f);
        Palette second = blender.Current;
        Assert.NotSame(first, second);
        Assert.NotEqual(first.Primary, second.Primary);

        // A retarget mid-fade starts from the same colors, but Current now names the new target's track.
        var next = PinkTeal with { SourceTrackId = "app|artist|next" };
        blender.SetTarget(next, AlbumFade);
        Palette third = blender.Current;
        Assert.Equal("app|artist|next", third.SourceTrackId);
        AssertBlend(third, second, second, 0f);

        // Reading it while idle never allocates.
        blender.Update(1f);
        Assert.Same(next, blender.Current);
        _ = blender.Current;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = blender.Current;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ---- allocation, determinism, validation ----

    [Fact]
    public void PerFrameCallsAllocateNothing()
    {
        var blender = new PaletteBlender(Violet);
        var gradient = new float[Floats];
        Palette[] targets = [BlueOrange, PinkTeal, Violet];
        int frame = 0;
        void Frame()
        {
            // A new palette every half second, fades of both lengths, retargets mid-fade, ratio changes.
            if (frame % 30 == 0) blender.SetTarget(targets[frame / 30 % 3], frame % 60 == 0 ? AlbumFade : ManualFade);
            bool animating = blender.IsAnimating;
            blender.Update(frame % 7 == 0 ? float.NaN : 1f / 60);
            if (animating || frame % 10 == 0) blender.FillGradient(gradient, 0.1f + frame % 9 * 0.1f);
            if (!blender.IsAnimating) _ = blender.Current;
            frame++;
        }
        for (int i = 0; i < 600; i++) Frame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 6000; i++) Frame();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        // Informational cost of one fill (shared Linux machine).
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 20_000; i++) blender.FillGradient(gradient, 0.6f);
        double idle = watch.Elapsed.TotalMilliseconds * 1e6 / 20_000;
        blender.SetTarget(BlueOrange, TimeSpan.FromHours(1));
        blender.Update(1);
        watch.Restart();
        for (int i = 0; i < 20_000; i++) blender.FillGradient(gradient, 0.6f);
        double fading = watch.Elapsed.TotalMilliseconds * 1e6 / 20_000;
        // A short test run may still be on tier-0 JIT code; DOTNET_TieredCompilation=0 shows the optimized cost.
        output.WriteLine($"FillGradient: {idle:F0} ns idle, {fading:F0} ns during a crossfade");
    }

    [Fact]
    public void SameCallsGiveIdenticalGradientsOnAnyThread()
    {
        static List<float[]> Run()
        {
            var blender = new PaletteBlender(Violet);
            var frames = new List<float[]>();
            float[] dts = [1f / 60, 0.007f, 0.031f, float.NaN, 1f / 144, 0.1f];
            for (int i = 0; i < 300; i++)
            {
                if (i % 37 == 0) blender.SetTarget(Palettes[i / 37 % 3], i % 2 == 0 ? AlbumFade : ManualFade);
                blender.Update(dts[i % dts.Length]);
                frames.Add(Fill(blender, 0.15f + i % 8 * 0.1f));
            }
            return frames;
        }

        List<float[]> first = Run();
        List<float[]> second = Run();
        List<float[]>? third = null;
        var thread = new Thread(() => third = Run());
        thread.Start();
        thread.Join();
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], second[i]);
            Assert.Equal(first[i], third![i]);
        }
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        Assert.Throws<ArgumentNullException>(() => new PaletteBlender(null!));
        var blender = new PaletteBlender(Violet);
        Assert.Throws<ArgumentNullException>(() => blender.SetTarget(null!, AlbumFade));

        var shortSpan = Assert.Throws<ArgumentException>(() => blender.FillGradient(new float[Floats - 1], 0.6f));
        Assert.Equal("rgba64x4", shortSpan.ParamName);
        Assert.Throws<ArgumentException>(() => blender.FillGradient(Span<float>.Empty, 0.6f));

        // A longer span: the first 256 floats are written, the rest is left alone.
        var longer = new float[Floats + 40];
        Array.Fill(longer, -7f);
        blender.FillGradient(longer, 0.6f);
        Assert.Equal(Fill(new PaletteBlender(Violet), 0.6f), longer[..Floats]);
        Assert.All(longer[Floats..], value => Assert.Equal(-7f, value));
    }

    [Fact]
    public void OutOfRangeAndNonFiniteColorsAreClamped()
    {
        var odd = new Palette(new Rgb(float.NaN, -1f, 2f), new Rgb(float.PositiveInfinity, float.NegativeInfinity, 0.5f), "odd");
        var fromStart = new PaletteBlender(odd);
        Assert.Equal(new Rgb(0, 0, 1), fromStart.Current.Primary);
        Assert.Equal(new Rgb(1, 0, 0.5f), fromStart.Current.Secondary);
        Assert.Equal("odd", fromStart.Current.SourceTrackId);
        Assert.All(Fill(fromStart, 0.6f), value => Assert.InRange(value, 0f, 1f));

        var blender = new PaletteBlender(Violet);
        blender.SetTarget(odd, AlbumFade);
        for (int i = 0; i < 48; i++)
        {
            blender.Update(1f / 60);
            Assert.All(Fill(blender, 0.6f), value => Assert.InRange(value, 0f, 1f));
        }
        Assert.False(blender.IsAnimating);
        Assert.Equal(new Rgb(0, 0, 1), blender.Current.Primary);
        Assert.Equal("odd", blender.Current.SourceTrackId);

        // A palette already in range is kept as it is: no copy.
        blender.SetTarget(BlueOrange, TimeSpan.Zero);
        Assert.Same(BlueOrange, blender.Current);
    }

    [Fact]
    public void FactoryReturnsTheRealBlender()
    {
        IPaletteBlender blender = CoreFactory.CreatePaletteBlender(BlueOrange);
        Assert.IsType<PaletteBlender>(blender);
        Assert.False(blender.IsAnimating);
        Assert.Same(BlueOrange, blender.Current);
    }

    // ---- helpers ----

    // Independent statement of H-008 item 1: Primary on the arc [0, ratio], Secondary on [ratio, 1]; at each boundary
    // (u = 0 ≡ 1 and u = ratio) a smoothstep 0.08 wide centered on it, mixed in Oklab.
    private static float ReferenceWeight(float u, float ratio)
    {
        bool inPrimary = u >= 0 && u <= ratio;
        float distance = MathF.Min(MathF.Min(MathF.Abs(u), MathF.Abs(1 - u)), MathF.Abs(u - ratio));
        float x = Math.Clamp(((inPrimary ? distance : -distance) + HalfBlend) / (2 * HalfBlend), 0, 1);
        return x * x * (3 - 2 * x);
    }

    private static Rgb Reference(Palette palette, float u, float ratio)
    {
        Oklab mixed = Mix(Oklab.FromLinearSrgb(palette.Secondary), Oklab.FromLinearSrgb(palette.Primary), ReferenceWeight(u, ratio));
        return Clamp01(mixed.ToLinearSrgb());
    }

    private static Rgb Clamp01(Rgb rgb) => new(Math.Clamp(rgb.R, 0, 1), Math.Clamp(rgb.G, 0, 1), Math.Clamp(rgb.B, 0, 1));

    private static Oklab Mix(Oklab from, Oklab to, float t) =>
        new(from.L + (to.L - from.L) * t, from.A + (to.A - from.A) * t, from.B + (to.B - from.B) * t);

    private static float Wrap(float u) => u - MathF.Floor(u);

    private static float[] Fill(IPaletteBlender blender, float ratio)
    {
        var gradient = new float[Floats];
        blender.FillGradient(gradient, ratio);
        return gradient;
    }

    private static Oklab TexelOklab(float[] gradient, int i) =>
        Oklab.FromLinearSrgb(gradient[i * 4], gradient[i * 4 + 1], gradient[i * 4 + 2]);

    private static bool IsColor(float[] gradient, int i, Rgb color) =>
        gradient[i * 4] == color.R && gradient[i * 4 + 1] == color.G && gradient[i * 4 + 2] == color.B;

    // The shader's lookup: WRAP addressing and linear filtering between the two nearest texel centers.
    private static Rgb Sample(float[] gradient, float u)
    {
        float x = u * Texels - 0.5f;
        int left = (int)MathF.Floor(x);
        float f = x - left;
        int a = (left % Texels + Texels) % Texels, b = (a + 1) % Texels;
        return new Rgb(
            gradient[a * 4] + (gradient[b * 4] - gradient[a * 4]) * f,
            gradient[a * 4 + 1] + (gradient[b * 4 + 1] - gradient[a * 4 + 1]) * f,
            gradient[a * 4 + 2] + (gradient[b * 4 + 2] - gradient[a * 4 + 2]) * f);
    }

    // Position of a color projected onto the Oklab segment Secondary → Primary: 0 at Secondary, 1 at Primary.
    private static double PrimaryShare(Palette palette, Oklab color)
    {
        Oklab s = Oklab.FromLinearSrgb(palette.Secondary), p = Oklab.FromLinearSrgb(palette.Primary);
        double dl = p.L - s.L, da = p.A - s.A, db = p.B - s.B;
        return ((color.L - s.L) * dl + (color.A - s.A) * da + (color.B - s.B) * db) / (dl * dl + da * da + db * db);
    }

    // Distance of a color from the Oklab line through two others.
    private static float OffSegment(Oklab color, Oklab from, Oklab to)
    {
        float dl = to.L - from.L, da = to.A - from.A, db = to.B - from.B;
        float t = ((color.L - from.L) * dl + (color.A - from.A) * da + (color.B - from.B) * db) / (dl * dl + da * da + db * db);
        return Oklab.Distance(color, new Oklab(from.L + dl * t, from.A + da * t, from.B + db * t));
    }

    private static float MaxChannelDifference(Rgb x, Rgb y) =>
        MathF.Max(MathF.Abs(x.R - y.R), MathF.Max(MathF.Abs(x.G - y.G), MathF.Abs(x.B - y.B)));

    // Each color of `actual` is the Oklab mix of `from` and `to` at `progress`, clamped into 0..1 per channel. (Two
    // colors on the same face of the sRGB cube, such as two with blue = 1, bow slightly outside it when mixed.)
    private static void AssertBlend(Palette actual, Palette from, Palette to, float progress, float tolerance = 1e-5f)
    {
        AssertRgb(ClampedMix(from.Primary, to.Primary, progress), actual.Primary, tolerance);
        AssertRgb(ClampedMix(from.Secondary, to.Secondary, progress), actual.Secondary, tolerance);
    }

    private static Rgb ClampedMix(Rgb from, Rgb to, float progress) =>
        Clamp01(Mix(Oklab.FromLinearSrgb(from), Oklab.FromLinearSrgb(to), progress).ToLinearSrgb());

    private static void AssertRgb(Rgb expected, Rgb actual, float tolerance) =>
        Assert.True(MaxChannelDifference(expected, actual) <= tolerance, $"expected {expected}, got {actual}");

    private static void AssertTexelsEqual(float[] gradient, int i, int j, float tolerance)
    {
        for (int c = 0; c < 3; c++)
            Assert.True(MathF.Abs(gradient[i * 4 + c] - gradient[j * 4 + c]) <= tolerance, $"texels {i} and {j} differ in channel {c}");
    }

    private static void AssertGradientsClose(float[] expected, float[] actual, float tolerance)
    {
        for (int i = 0; i < Floats; i++)
            Assert.True(MathF.Abs(expected[i] - actual[i]) <= tolerance, $"float {i}: {expected[i]} vs {actual[i]}");
    }

    private static Palette Hex(string primary, string secondary, string trackId) =>
        new(Linear(primary), Linear(secondary), trackId);

    private static Rgb Linear(string hex) => new(
        Srgb.ToLinear(Convert.ToByte(hex.Substring(1, 2), 16)),
        Srgb.ToLinear(Convert.ToByte(hex.Substring(3, 2), 16)),
        Srgb.ToLinear(Convert.ToByte(hex.Substring(5, 2), 16)));
}
