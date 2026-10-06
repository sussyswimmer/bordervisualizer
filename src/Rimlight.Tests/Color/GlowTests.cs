using Rimlight.Core;
using Rimlight.Core.Color;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Color;

// C4: glow-ify (doc 05 §2 step 6) and gamut mapping.
public sealed class GlowTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0.10f, 0.55f)]
    [InlineData(0.30f, 0.55f)]
    [InlineData(0.55f, 0.55f)]
    [InlineData(0.70f, 0.70f)]
    [InlineData(0.85f, 0.85f)]
    [InlineData(0.95f, 0.85f)]
    [InlineData(1.00f, 0.85f)]
    public void LightnessIsClampedTo055Through085(float lightness, float expected)
    {
        var target = Glow.Target(new OkLch(lightness, 0.15f, 30f).ToOklab());
        Assert.Equal(expected, target.L, 1e-6f);
        // Gamut mapping keeps lightness: the emitted light has it too.
        Assert.Equal(expected, Oklab.FromLinearSrgb(Glow.ToLight(new OkLch(lightness, 0.15f, 30f).ToOklab())).L, 1e-4f);
    }

    [Theory]
    [InlineData(0.000f, 0.000f)]
    [InlineData(0.020f, 0.020f)]
    [InlineData(0.029f, 0.029f)] // just under NeutralChroma: grayscale, never given a hue
    [InlineData(0.031f, 0.120f)] // just over: raised to MinChroma
    [InlineData(0.050f, 0.120f)]
    [InlineData(0.120f, 0.120f)]
    [InlineData(0.180f, 0.180f)] // already vivid: kept
    public void ChromaIsRaisedToAtLeast012UnlessGrayscale(float chroma, float expected)
    {
        var source = new OkLch(0.7f, chroma, 30f);
        var target = Glow.Target(source.ToOklab());
        Assert.Equal(expected, target.C, 1e-5f);
        // All of these fit sRGB at L = 0.7, hue 30°, so the light keeps that chroma and the source hue.
        var light = Oklab.FromLinearSrgb(Glow.ToLight(source.ToOklab())).ToLch();
        Assert.Equal(expected, light.C, 2e-4f);
        if (expected > 0.01f) Assert.True(OklabTests.HueDistance(light.H, 30f) < 0.5f, $"hue {light.H}");
    }

    [Fact]
    public void ThresholdsAreTheDocumentedOnes()
    {
        Assert.Equal(0.03f, Glow.NeutralChroma);
        Assert.Equal(0.12f, Glow.MinChroma);
        Assert.Equal(0.55f, Glow.MinLightness);
        Assert.Equal(0.85f, Glow.MaxLightness);
    }

    [Fact]
    public void GamutMappingLandsOnTheSrgbBoundaryAtConstantLightnessAndHue()
    {
        // Chroma 0.4 is outside sRGB everywhere. The result must be in gamut, keep L and hue, and be the most
        // chroma that fits: a little more is out of gamut again.
        int checkedColors = 0;
        for (float lightness = 0.55f; lightness <= 0.851f; lightness += 0.05f)
        {
            for (float hue = 0; hue < 360; hue += 7.5f)
            {
                Rgb mapped = Glow.MapToSrgb(new OkLch(lightness, 0.4f, hue));
                Assert.True(Glow.InGamut(mapped), $"L {lightness} h {hue}: {mapped}");
                var lch = Oklab.FromLinearSrgb(mapped).ToLch();
                Assert.Equal(lightness, lch.L, 1e-4f);
                Assert.True(OklabTests.HueDistance(lch.H, hue) < 0.5f, $"L {lightness} h {hue}: hue {lch.H}");
                Assert.InRange(lch.C, 0.01f, 0.4f);
                Assert.False(Glow.InGamut(new OkLch(lightness, lch.C + 0.002f, hue).ToOklab().ToLinearSrgb()), $"L {lightness} h {hue}: not maximal at {lch.C}");
                checkedColors++;
            }
        }
        output.WriteLine($"{checkedColors} boundary colors checked");
    }

    [Fact]
    public void InGamutColorsAreUnchanged()
    {
        var color = new OkLch(0.7f, 0.1f, 200f);
        Assert.Equal(color.ToOklab().ToLinearSrgb(), Glow.MapToSrgb(color));
    }

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(255, 0, 255)]
    [InlineData(0, 255, 255)]
    [InlineData(255, 255, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    [InlineData(10, 0, 40)]
    public void ExtremeColorsGlowInsideTheGamut(byte r, byte g, byte b)
    {
        Rgb light = Glow.ToLight(new Rgba8(r, g, b).ToOklab());
        Assert.True(Glow.InGamut(light), light.ToString());
        Assert.InRange(Oklab.FromLinearSrgb(light).L, Glow.MinLightness - 1e-4f, Glow.MaxLightness + 1e-4f);
    }

    [Theory]
    [InlineData(1.5f, 0.2f, 40f)]
    [InlineData(-0.2f, 0.2f, 40f)]
    [InlineData(0.6f, 2f, 300f)]
    public void MappingNeverLeavesTheUnitCube(float lightness, float chroma, float hue)
    {
        Rgb mapped = Glow.MapToSrgb(new OkLch(lightness, chroma, hue));
        Assert.True(Glow.InGamut(mapped), mapped.ToString());
    }
}
