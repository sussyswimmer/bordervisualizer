using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// GlowConstants (K1/K3): the light state in a surface's pixels, Glow.hlsl's constant buffer.
public sealed class GlowConstantsTests
{
    private static readonly LightState State = new(default, default, 0.6f, 0.8f, 0.45f, 6, 0.3f, 1.25f, 1);
    private static readonly Rgb Mean = new(0.2f, 0.3f, 0.4f);

    [Theory]
    [InlineData(1920, 1080, 1f, 0f, 0.45f)]
    [InlineData(2560, 1440, 1.25f, 12f, 0.45f)]
    [InlineData(3840, 2160, 2f, 40f, 1f)]
    [InlineData(1280, 800, 1.5f, 8f, 0f)]   // a hairline: the one-DIP minimum spread
    [InlineData(1366, 768, 1f, 500f, 0.9f)] // a corner radius past half the short side is clamped
    public void HalfScaleHalvesEveryPixelMeasure(int width, int height, float scale, float cornerDip, float glow)
    {
        // "Reduce on battery" renders into a swap chain half the window's size, and converts DIPs at half the scale.
        LightState state = State with { Spread = glow };
        GlowConstants full = GlowConstants.Create(state, width, height, scale, cornerDip, Mean);
        GlowConstants half = GlowConstants.Create(state, width / 2, height / 2, scale / 2, cornerDip, Mean);

        Assert.Equal(full.ScreenWidthPx / 2, half.ScreenWidthPx);
        Assert.Equal(full.ScreenHeightPx / 2, half.ScreenHeightPx);
        Assert.Equal(full.CornerRadiusPx / 2, half.CornerRadiusPx, 4);
        Assert.Equal(full.CoreThicknessPx / 2, half.CoreThicknessPx, 4);
        Assert.Equal(full.SpreadPx / 2, half.SpreadPx, 4);
        Assert.Equal(full.ColorRadiusPx / 2, half.ColorRadiusPx, 4);
        Assert.Equal(full.GlowRadiusPx / 2, half.GlowRadiusPx, 4);
        // Everything that isn't a length stays as it is.
        Assert.Equal(full.Intensity, half.Intensity);
        Assert.Equal(full.Pulse, half.Pulse);
        Assert.Equal(full.Phase, half.Phase);
        Assert.Equal(full.Visibility, half.Visibility);
        Assert.Equal(full.MeanRed, half.MeanRed);
    }

    [Fact]
    public void ValuesAreClampedAndTheLoopCoordinateWraps()
    {
        GlowConstants c = GlowConstants.Create(State with
        {
            Intensity = 3, Pulse = -1, Visibility = float.NaN, Phase = 1.25f, CoreThicknessDip = float.PositiveInfinity,
        }, 1920, 1080, 1, float.NaN, new Rgb(float.NaN, 1, 0));
        Assert.Equal(1, c.Intensity);
        Assert.Equal(0, c.Pulse);
        Assert.Equal(0, c.Visibility);
        Assert.Equal(0.25f, c.Phase, 6);
        Assert.Equal(0, c.CoreThicknessPx);
        Assert.Equal(0, c.CornerRadiusPx);
        Assert.Equal(0, c.MeanRed);
        Assert.Equal(-0.75f + 1, GlowConstants.Create(State with { Phase = -0.75f }, 100, 100, 1, 0, Mean).Phase, 6);
    }

    [Fact]
    public void GlowMapsToSpreadAsThePrdSays()
    {
        // 0 is a hairline (one DIP); 1 spills about 35 % of the shorter side (three e-folding distances); the default
        // 0.45 is about 25 px at 1080p.
        Assert.Equal(1f, GlowConstants.SpreadFor(0, 1080, 1));
        Assert.Equal(2f, GlowConstants.SpreadFor(0, 2160, 2));
        Assert.Equal(0.35f * 1080 / 3, GlowConstants.SpreadFor(1, 1080, 1), 3);
        Assert.InRange(GlowConstants.SpreadFor(0.45f, 1080, 1), 24, 26);
        Assert.Equal(GlowConstants.SpreadFor(1, 1080, 1), GlowConstants.SpreadFor(7, 1080, 1));
    }

    [Fact]
    public void LooksLikeAllowsHalfACodeValueAndATinyPhaseShift()
    {
        GlowConstants a = GlowConstants.Create(State, 1920, 1080, 1, 0, Mean);
        GlowConstants b = a;
        b.Intensity += 0.4f / 255;
        b.Phase += 0.0001f;
        Assert.True(a.LooksLike(b));
        b.Intensity += 0.2f / 255;
        Assert.False(a.LooksLike(b));

        GlowConstants wrap = a;
        wrap.Phase = 0.99995f;
        GlowConstants zero = a;
        zero.Phase = 0.00005f;
        Assert.True(wrap.LooksLike(zero)); // 0.9999 and 0 are neighbours on the loop

        GlowConstants moved = a;
        moved.SpreadPx += 0.001f; // geometry must match exactly
        Assert.False(a.LooksLike(moved));
        GlowConstants faded = a;
        faded.Visibility -= 1f / 255;
        Assert.False(a.LooksLike(faded));
    }

    [Fact]
    public void TheMeanColorIsTheAverageTexel()
    {
        float[] texels = new float[256];
        for (int i = 0; i < 64; i++)
        {
            texels[i * 4] = i < 32 ? 1 : 0;
            texels[i * 4 + 1] = 0.5f;
            texels[i * 4 + 2] = i / 63f;
            texels[i * 4 + 3] = 1;
        }
        Rgb mean = GlowConstants.MeanOf(texels);
        Assert.Equal(0.5f, mean.R, 5);
        Assert.Equal(0.5f, mean.G, 5);
        Assert.Equal(0.5f, mean.B, 5);
    }

    [Fact]
    public void TheLayoutMatchesTheShadersConstantBuffer()
    {
        Assert.Equal(GlowConstants.SizeInBytes, System.Runtime.InteropServices.Marshal.SizeOf<GlowConstants>());
        Assert.Equal(64, GlowConstants.SizeInBytes);
    }
}
