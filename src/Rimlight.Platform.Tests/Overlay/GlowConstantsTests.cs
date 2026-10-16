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
        // converts DIPs at the surface's render scale, and the visual stretches it back over the window. Only the
        // thin-core fade stays in screen pixels (K3's review).
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
            Assert.Equal(full.CoreThicknessPx / 2, half.CoreThicknessPx, 4);
            Assert.Equal(full.SpreadPx / 2, half.SpreadPx, 4);
            Assert.Equal(full.ColorRadiusPx / 2, half.ColorRadiusPx, 4);
            Assert.Equal(full.GlowRadiusPx / 2, half.GlowRadiusPx, 4);
        }
        // Stretched back over the window, every measure lands within a pixel of the full-scale one.
        float stretch = width / (float)bufferWidth;
        Assert.InRange(half.CornerRadiusPx * stretch - full.CornerRadiusPx, -1, 1);
        Assert.InRange(half.CoreThicknessPx * stretch - full.CoreThicknessPx, -1, 1);
        Assert.InRange(half.SpreadPx * stretch - full.SpreadPx, -1, 1);
        Assert.InRange(half.ColorRadiusPx * stretch - full.ColorRadiusPx, -1, 1);
        Assert.InRange(half.GlowRadiusPx * stretch - full.GlowRadiusPx, -1, 1);
        // Everything that isn't a length stays as it is.
        Assert.Equal(full.Intensity, half.Intensity);
        Assert.Equal(full.Pulse, half.Pulse);
        Assert.Equal(full.Phase, half.Phase);
        Assert.Equal(full.Visibility, half.Visibility);
        Assert.Equal(full.MeanRed, half.MeanRed);
        Assert.Equal(full.CoreFade, half.CoreFade);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void AThinCoreFadesByItsWidthInScreenPixels(float scale)
    {
        // CoreFade is what Glow.hlsl's saturate(CoreThicknessPx) was at full scale: a core under a screen pixel lights
        // the pixel by its share of it. At half scale a 1-DIP core is half a swap-chain pixel, stretched over two screen
        // pixels; fading by swap-chain pixels would halve its light, so it fades by screen pixels at either scale.
        for (float coreDip = 0; coreDip <= 6; coreDip += 0.125f)
        {
            LightState state = State with { CoreThicknessDip = coreDip };
            GlowConstants full = GlowConstants.Create(state, 1920, 1080, scale, 1, 8, Mean);
            GlowConstants half = GlowConstants.Create(state, 960, 540, scale, 0.5f, 8, Mean);
            Assert.Equal(Math.Clamp(full.CoreThicknessPx, 0, 1), full.CoreFade, 6);
            Assert.Equal(full.CoreFade, half.CoreFade);
            Assert.Equal(full.CoreThicknessPx / 2, half.CoreThicknessPx, 4);
        }
        Assert.Equal(0.5f, GlowConstants.Create(State with { CoreThicknessDip = 0.5f }, 960, 540, 1, 0.5f, 8, Mean).CoreFade);
        Assert.Equal(1f, GlowConstants.Create(State with { CoreThicknessDip = 0.5f }, 960, 540, 2, 0.5f, 8, Mean).CoreFade);
    }

    [Fact]
    public void ValuesAreClampedAndTheLoopCoordinateWraps()
    {
        GlowConstants c = GlowConstants.Create(State with
        {
            Intensity = 3, Pulse = -1, Visibility = float.NaN, Phase = 1.25f, CoreThicknessDip = float.PositiveInfinity,
        }, 1920, 1080, 1, 1, float.NaN, new Rgb(float.NaN, 1, 0));
        Assert.Equal(1, c.Intensity);
        Assert.Equal(0, c.Pulse);
        Assert.Equal(0, c.Visibility);
        Assert.Equal(0.25f, c.Phase, 6);
        Assert.Equal(0, c.CoreThicknessPx);
        Assert.Equal(0, c.CoreFade);
        Assert.Equal(0, c.CornerRadiusPx);
        Assert.Equal(0, c.MeanRed);
        Assert.Equal(-0.75f + 1, GlowConstants.Create(State with { Phase = -0.75f }, 100, 100, 1, 1, 0, Mean).Phase, 6);
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
        GlowConstants a = GlowConstants.Create(State, 1920, 1080, 1, 1, 0, Mean);
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
        GlowConstants thinner = a;
        thinner.CoreFade -= 0.001f; // so must the thin-core fade
        Assert.False(a.LooksLike(thinner));
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
