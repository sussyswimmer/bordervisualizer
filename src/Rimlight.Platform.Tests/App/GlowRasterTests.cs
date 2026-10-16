using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// GlowRaster (K7, reworked for the soft edge): the Settings preview draws the overlay's edge profile, GlowProfile, at
// the card's scale (1280 DIPs across 352 pixels).
public sealed class GlowRasterTests
{
    private const int Width = 352;
    private const int Height = 198;
    private const float Scale = Width / GlowRaster.ScreenWidthDip;

    [Theory]
    [InlineData(6f, 0.45f, 0.72f, 0f)]  // the defaults, Idle Glow's level
    [InlineData(6f, 0.45f, 0.8f, 1f)]   // a full beat at the default Brightness
    [InlineData(0f, 0.45f, 0.72f, 0f)]  // glow only
    [InlineData(40f, 0.2f, 1f, 1f)]     // a wide band, a narrow glow, the gain rolled off
    [InlineData(1f, 0f, 0.55f, 0f)]     // a hairline
    [InlineData(0f, 0f, 0.72f, 1f)]     // a hairline glow alone, on the beat: the shader's half-pixel spread floor
    public void ThePreviewDrawsTheOverlaysProfile(float thicknessDip, float glow, float intensity, float pulse)
    {
        var raster = new GlowRaster(Width, Height);
        raster.SetGradient(White());
        var state = new LightState(default, default, 0.6f, intensity, glow, thicknessDip, 0, pulse, 1);
        Assert.True(raster.Render(state, 0));

        int row = Height / 2;
        // The shader's spread floor (half a pixel) and gain (rolled off once, then scaling the profile).
        float spread = GlowProfile.PulsedSpread(GlowProfile.SpreadFor(glow, Height, Scale), pulse);
        float gain = GlowProfile.Gain(intensity, pulse);
        float thicknessPx = thicknessDip * Scale;
        int previous = 255;
        for (int x = 0; x < 80; x++)
        {
            float d = x + 0.5f;
            float shape = GlowProfile.Combine(GlowProfile.Glow(d, spread),
                GlowProfile.Band(d, GlowProfile.BandScale(thicknessPx), GlowProfile.BandGain(thicknessPx)));
            float expected = shape * gain * 255;
            int alpha = (int)((uint)raster.Pixels[row * Width + x] >> 24);
            Assert.InRange(alpha - expected, -1, 1);
            Assert.True(alpha <= previous, $"pixel {x} is brighter than the one outside it");
            previous = alpha;
        }
    }

    [Fact]
    public void DrawingAFrameAllocatesNothing()
    {
        var raster = new GlowRaster(Width, Height);
        raster.SetGradient(White());
        var state = new LightState(default, default, 0.6f, 0.7f, 0.45f, 6, 0, 0, 1);
        for (int i = 1; i <= 60; i++) // warm up: the first calls run unoptimized code
            Draw(raster, state, i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i <= 60; i++)
            Draw(raster, state, i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        // Frames that move the beat and the colours, and a thickness that moves (the shape is rebuilt).
        static void Draw(GlowRaster raster, in LightState state, int i) =>
            raster.Render(state with { Pulse = i % 10 / 10f, Phase = i / 60f, CoreThicknessDip = 6 + i % 2 }, 8);
    }

    private static float[] White()
    {
        float[] texels = new float[256];
        Array.Fill(texels, 1f);
        return texels;
    }
}
