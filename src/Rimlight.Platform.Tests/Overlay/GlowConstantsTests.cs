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
    [InlineData(1365, 767, 1f, 12f, 0.45f)] // odd sizes (a work area, a window Windows sized): buffers round up
    [InlineData(2559, 1401, 1.75f, 500f, 1f)]
    [InlineData(1, 3, 1f, 8f, 0.45f)]
    public void HalfScaleHalvesEveryPixelMeasure(int width, int height, float scale, float cornerDip, float glow)
    {
        // "Reduce on battery" renders into a swap chain half the window's size (OverlaySurface.BufferSize, rounded up),
        // converts DIPs at the surface's render scale, and the visual stretches it back over the window. (The solid
        // core's screen-pixel fade, K3's review, is gone with the core: GlowProfileTests checks that a thin band keeps
        // its light at half scale.)
        LightState state = State with { Spread = glow };
        int bufferWidth = OverlaySurface.BufferSize(width, half: true);
        int bufferHeight = OverlaySurface.BufferSize(height, half: true);
        float renderScale = OverlaySurface.RenderScaleFor(width, half: true);
        Assert.Equal(1f, OverlaySurface.RenderScaleFor(width, half: false));
        Assert.Equal(width, OverlaySurface.BufferSize(width, half: false));
        GlowConstants full = GlowConstants.Create(state, width, height, scale, 1, cornerDip, Mean);
        GlowConstants half = GlowConstants.Create(state, bufferWidth, bufferHeight, scale, renderScale, cornerDip, Mean);

        Assert.Equal((width + 1) / 2, half.ScreenWidthPx);
        Assert.Equal((height + 1) / 2, half.ScreenHeightPx);
        if (width % 2 == 0 && height % 2 == 0)
        {
            Assert.Equal(0.5f, renderScale);
            Assert.Equal(full.CornerRadiusPx / 2, half.CornerRadiusPx, 4);
            Assert.Equal(full.BandScalePx / 2, half.BandScalePx, 4);
            Assert.Equal(full.BandRadiusPx / 2, half.BandRadiusPx, 4);
            Assert.Equal(full.SpreadPx / 2, half.SpreadPx, 4);
            Assert.Equal(full.ColorRadiusPx / 2, half.ColorRadiusPx, 4);
            Assert.Equal(full.GlowRadiusPx / 2, half.GlowRadiusPx, 4);
        }
        // Stretched back over the window, every measure lands within a pixel of the full-scale one.
        float stretch = width / (float)bufferWidth;
        Assert.InRange(half.CornerRadiusPx * stretch - full.CornerRadiusPx, -1, 1);
        Assert.InRange(half.BandScalePx * stretch - full.BandScalePx, -1, 1);
        Assert.InRange(half.BandRadiusPx * stretch - full.BandRadiusPx, -1, 1);
        Assert.InRange(half.SpreadPx * stretch - full.SpreadPx, -1, 1);
        Assert.InRange(half.ColorRadiusPx * stretch - full.ColorRadiusPx, -1, 1);
        Assert.InRange(half.GlowRadiusPx * stretch - full.GlowRadiusPx, -1, 1);
        // Everything that isn't a length stays as it is.
        Assert.Equal(full.Gain, half.Gain);
        Assert.Equal(full.Pulse, half.Pulse);
        Assert.Equal(full.Phase, half.Phase);
        Assert.Equal(full.Visibility, half.Visibility);
        Assert.Equal(full.MeanRed, half.MeanRed);
        // The band's gain is its average over a pixel at each scale's own pixel size.
        Assert.Equal(GlowProfile.BandGain(2 * full.BandScalePx), full.BandGain);
        Assert.Equal(GlowProfile.BandGain(2 * half.BandScalePx), half.BandGain);
    }

    [Fact]
    public void ThicknessIsTheSoftBandsWidth()
    {
        // Thickness (DIPs) is the band's nominal width: its e-folding distance is half of it, in the surface's pixels,
        // and its field's corners are rounded to at least two e-folding distances (or the user's corner radius).
        GlowConstants c = GlowConstants.Create(State with { CoreThicknessDip = 6 }, 2880, 1620, 1.5f, 1, 0, Mean);
        Assert.Equal(4.5f, c.BandScalePx, 5);
        Assert.Equal(9f, c.BandRadiusPx, 5);
        Assert.Equal(GlowProfile.BandGain(9), c.BandGain);
        Assert.InRange(c.BandGain, 1, 1.01); // a band several pixels wide: the pixel average is its centre value
        Assert.Equal(20f * 1.5f, GlowConstants.Create(State, 2880, 1620, 1.5f, 1, 20, Mean).BandRadiusPx, 5);

        GlowConstants none = GlowConstants.Create(State with { CoreThicknessDip = 0 }, 1920, 1080, 1, 1, 0, Mean);
        Assert.Equal(0, none.BandGain); // Thickness 0 leaves only the glow
        Assert.True(none.BandScalePx > 0); // the shader still divides by it
    }

    [Fact]
    public void ValuesAreClampedAndTheLoopCoordinateWraps()
    {
        GlowConstants c = GlowConstants.Create(State with
        {
            Intensity = 3, Pulse = -1, Visibility = float.NaN, Phase = 1.25f, CoreThicknessDip = float.PositiveInfinity,
        }, 1920, 1080, 1, 1, float.NaN, new Rgb(float.NaN, 1, 0));
        Assert.Equal(GlowProfile.Gain(1, 0), c.Gain); // Intensity clamped to 1, Pulse to 0, then rolled off
        Assert.Equal(0, c.Pulse);
        Assert.Equal(0, c.Visibility);
        Assert.Equal(0.25f, c.Phase, 6);
        Assert.Equal(0, c.BandGain);
        Assert.True(c.BandScalePx > 0);
        Assert.Equal(0, c.CornerRadiusPx);
        Assert.Equal(0, c.MeanRed);
        Assert.Equal(-0.75f + 1, GlowConstants.Create(State with { Phase = -0.75f }, 100, 100, 1, 1, 0, Mean).Phase, 6);
    }

    [Fact]
    public void GlowMapsToSpreadAsThePrdSays()
    {
        // 0 is a hairline (one DIP); 1 spills about 35 % of the shorter side (three e-folding distances). In between
        // the slider is raised to the power 1.5 (soft edge, Maxwell's feedback round 1; K1 squared it, which put the
        // default at 25 px): the default 0.45 is about 38 px at 1080p, so the light fades in instead of hugging the
        // edge.
        Assert.Equal(1f, GlowProfile.SpreadFor(0, 1080, 1));
        Assert.Equal(2f, GlowProfile.SpreadFor(0, 2160, 2));
        Assert.Equal(0.35f * 1080 / 3, GlowProfile.SpreadFor(1, 1080, 1), 3);
        Assert.InRange(GlowProfile.SpreadFor(0.45f, 1080, 1), 37, 39);
        Assert.InRange(GlowProfile.SpreadFor(0.45f, 2160, 2), 75, 77); // a 4K screen at 200 %: the same share of it
        Assert.Equal(GlowProfile.SpreadFor(1, 1080, 1), GlowProfile.SpreadFor(7, 1080, 1));
        Assert.Equal(1f, GlowProfile.SpreadFor(float.NaN, 1080, 1));
        float previous = 0;
        for (float glow = 0; glow <= 1; glow += 0.01f)
        {
            float spread = GlowProfile.SpreadFor(glow, 1080, 1);
            Assert.True(spread >= previous, $"Glow {glow:0.00} reaches less far than the step before it");
            previous = spread;
        }
        Assert.Equal(GlowProfile.SpreadFor(0.45f, 1080, 1), GlowConstants.Create(State, 1920, 1080, 1, 1, 0, Mean).SpreadPx);
    }

    [Fact]
    public void LooksLikeAllowsHalfACodeValueAndATinyPhaseShift()
    {
        GlowConstants a = GlowConstants.Create(State, 1920, 1080, 1, 1, 0, Mean);
        GlowConstants b = a;
        b.Gain += 0.4f / 255;
        b.Phase += 0.0001f;
        Assert.True(a.LooksLike(b));
        b.Gain += 0.2f / 255;
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
        GlowConstants dimmerBand = a;
        dimmerBand.BandGain -= 0.001f; // so must the band
        Assert.False(a.LooksLike(dimmerBand));
        GlowConstants rounder = a;
        rounder.BandRadiusPx += 0.001f;
        Assert.False(a.LooksLike(rounder));
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
