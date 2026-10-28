using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Rimlight.Platform.Overlay;
using Xunit;

namespace Rimlight.Platform.Tests;

// GlowRaster (K7, reworked for the soft edge and then the flat shoulder): the Settings preview draws the overlay's edge
// profile, GlowProfile, at the card's scale (1280 DIPs across 352 pixels).
public sealed class GlowRasterTests
{
    private const int Width = 352;
    private const int Height = 198;
    private const float Scale = Width / GlowRaster.ScreenWidthDip;

    [Theory]
    [InlineData(6f, 0.45f, 0.72f, 0f)]  // the defaults, Idle Glow's level
    [InlineData(6f, 0.45f, 0.8f, 1f)]   // a full beat at the default Brightness
    [InlineData(0f, 0.45f, 0.72f, 0f)]  // the narrowest shoulder
    [InlineData(40f, 0.2f, 1f, 1f)]     // a wide shoulder, a narrow glow, the gain rolled off
    [InlineData(20f, 1f, 0.8f, 0.5f)]   // a wide wash
    [InlineData(1f, 0f, 0.55f, 0f)]     // a hairline
    [InlineData(0f, 0f, 0.72f, 1f)]     // a hairline on the beat: the shader's half-pixel spread floor
    public void ThePreviewDrawsTheOverlaysProfile(float thicknessDip, float glow, float intensity, float pulse)
    {
        var raster = new GlowRaster(Width, Height);
        raster.SetGradient(White());
        var state = new LightState(default, default, 0.6f, intensity, glow, thicknessDip, 0, pulse, 1);
        Assert.True(raster.Render(state, 0));

        int row = Height / 2;
        // The shader's shoulder (at least one DIP) and spread floor (half a pixel), both widened by the beat, and its
        // gain (rolled off once, then scaling the profile).
        float spread = GlowProfile.PulsedSpread(GlowProfile.SpreadFor(glow, Height, Scale), pulse);
        float shoulder = GlowProfile.PulsedShoulder(GlowProfile.ShoulderFor(thicknessDip * Scale, Scale), pulse);
        float gain = GlowProfile.Gain(intensity, pulse);
        int previous = 255;
        for (int x = 0; x < 80; x++)
        {
            float expected = GlowProfile.Glow(x + 0.5f, shoulder, spread) * gain * 255;
            int alpha = (int)((uint)raster.Pixels[row * Width + x] >> 24);
            Assert.InRange(alpha - expected, -1, 1);
            Assert.True(alpha <= previous, $"pixel {x} is brighter than the one outside it");
            previous = alpha;
        }
    }

    [Fact]
    public void ThePreviewSkipsOnlyPixelsUnderHalfACodeValue()
    {
        // Render skips pixels past the depth where the light falls under half a code value (the shader's early-out);
        // along a row through the middle, the last lit pixel and the first dark one sit on either side of it.
        foreach (float thicknessDip in new[] { 0f, 6, 40 })
            foreach (float glow in new[] { 0f, 0.1f, 0.45f })
            {
                var raster = new GlowRaster(Width, Height);
                raster.SetGradient(White());
                var state = new LightState(default, default, 0.6f, 0.72f, glow, thicknessDip, 0, 0.3f, 1);
                raster.Render(state, 0);
                float spread = GlowProfile.PulsedSpread(GlowProfile.SpreadFor(glow, Height, Scale), 0.3f);
                float shoulder = GlowProfile.PulsedShoulder(GlowProfile.ShoulderFor(thicknessDip * Scale, Scale), 0.3f);
                float gain = GlowProfile.Gain(0.72f, 0.3f);
                int row = Height / 2;
                for (int x = 0; x < Height / 2; x++)
                {
                    float alpha = GlowProfile.Glow(x + 0.5f, shoulder, spread) * gain;
                    int drawn = (int)((uint)raster.Pixels[row * Width + x] >> 24);
                    if (alpha >= 0.6f / 255)
                        Assert.True(drawn > 0, $"thickness {thicknessDip}, glow {glow}: pixel {x} ({alpha * 255:0.00}) was skipped");
                    else if (alpha < 0.4f / 255)
                        Assert.Equal(0, drawn);
                }
            }
    }

    [Theory]
    [InlineData(6f, 0.45f, 0.72f, 0f)]  // the defaults, as a line
    [InlineData(6f, 0.45f, 0.8f, 1f)]   // a full beat: brighter, not wider
    [InlineData(0f, 0.45f, 0.72f, 0f)]  // the narrowest line, under a pixel at this scale
    [InlineData(40f, 0.1f, 1f, 1f)]     // a wide line on a narrow halo
    [InlineData(20f, 0f, 0.55f, 0.5f)]  // a hairline halo
    public void ThePreviewDrawsTheLineStyle(float thicknessDip, float glow, float intensity, float pulse)
    {
        // Maxwell's request, drawn as the overlay draws it (GlowProfile.LineAndHalo): a line the Thickness wide along
        // the edge, in front of a halo with the narrowest shoulder. Pixels are skipped only under half a code value.
        var raster = new GlowRaster(Width, Height);
        raster.SetGradient(White());
        var state = new LightState(default, default, 0.6f, intensity, glow, thicknessDip, 0, pulse, 1);
        Assert.True(raster.Render(state, 0, EdgeStyle.Line));
        Assert.False(raster.Render(state, 0, EdgeStyle.Line)); // the same frame

        int row = Height / 2;
        float spread = GlowProfile.PulsedSpread(GlowProfile.SpreadFor(glow, Height, Scale), pulse);
        float shoulder = GlowProfile.PulsedShoulder(GlowProfile.ShoulderFor(0, Scale), pulse);
        float line = GlowProfile.LineFor(thicknessDip * Scale, Scale);
        float gain = GlowProfile.Gain(intensity, pulse);
        int previous = 255;
        for (int x = 0; x < Height / 2; x++)
        {
            float alpha = GlowProfile.LineAndHalo(x + 0.5f, line, x + 0.5f, shoulder, spread) * gain;
            int drawn = (int)((uint)raster.Pixels[row * Width + x] >> 24);
            Assert.InRange(drawn - alpha * 255, -1, 1);
            Assert.True(drawn <= previous, $"pixel {x} is brighter than the one outside it");
            if (alpha >= 0.6f / 255)
                Assert.True(drawn > 0, $"pixel {x} ({alpha * 255:0.00}) was skipped");
            else if (alpha < 0.4f / 255)
                Assert.Equal(0, drawn);
            previous = drawn;
        }

        // Back to the soft glow: a new frame, the glow's own.
        Assert.True(raster.Render(state, 0, EdgeStyle.Glow));
        float glowShoulder = GlowProfile.PulsedShoulder(GlowProfile.ShoulderFor(thicknessDip * Scale, Scale), pulse);
        int edge = (int)((uint)raster.Pixels[row * Width] >> 24);
        Assert.InRange(edge - GlowProfile.Glow(0.5f, glowShoulder, spread) * gain * 255, -1, 1);
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

        // Frames that move the beat and the colours, and a thickness and an edge style that move (the shape is
        // rebuilt).
        static void Draw(GlowRaster raster, in LightState state, int i) =>
            raster.Render(state with { Pulse = i % 10 / 10f, Phase = i / 60f, CoreThicknessDip = 6 + i % 2 }, 8,
                i % 3 == 0 ? EdgeStyle.Line : EdgeStyle.Glow);
    }

    private static float[] White()
    {
        float[] texels = new float[256];
        Array.Fill(texels, 1f);
        return texels;
    }
}
