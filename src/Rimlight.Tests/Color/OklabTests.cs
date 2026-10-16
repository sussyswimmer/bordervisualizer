using Rimlight.Core;
using Rimlight.Core.Color;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Color;

// C4: sRGB transfer functions and Oklab/OkLCh conversions.
public sealed class OklabTests(ITestOutputHelper output)
{
    // Reference values from Ottosson's matrices in double precision (they match the CSS Color 4 sample values).
    [Theory]
    [InlineData(1f, 1f, 1f, 1.000000f, 0.000000f, 0.000000f)]
    [InlineData(1f, 0f, 0f, 0.627955f, 0.224863f, 0.125846f)]
    [InlineData(0f, 1f, 0f, 0.866440f, -0.233888f, 0.179498f)]
    [InlineData(0f, 0f, 1f, 0.452014f, -0.032457f, -0.311528f)]
    [InlineData(0f, 0f, 0f, 0f, 0f, 0f)]
    public void PrimariesAndWhiteMatchReferenceOklab(float r, float g, float b, float l, float a, float bb)
    {
        var lab = Oklab.FromLinearSrgb(r, g, b);
        Assert.Equal(l, lab.L, 1e-4f);
        Assert.Equal(a, lab.A, 1e-4f);
        Assert.Equal(bb, lab.B, 1e-4f);
    }

    [Theory]
    [InlineData(255, 0, 0, 29.2339f, 0.2577f)]
    [InlineData(0, 0, 255, 264.0520f, 0.3132f)]
    [InlineData(255, 0, 255, 328.3634f, 0.3225f)]
    [InlineData(0, 255, 255, 194.7689f, 0.1546f)]
    public void HueAndChromaOfSaturatedColors(byte r, byte g, byte b, float hue, float chroma)
    {
        var lch = new Rgba8(r, g, b).ToOklab().ToLch();
        Assert.Equal(hue, lch.H, 1e-2f);
        Assert.Equal(chroma, lch.C, 1e-4f);
    }

    [Fact]
    public void LinearSrgbRoundTripsThroughOklab()
    {
        float worst = 0;
        for (int r = 0; r <= 16; r++)
            for (int g = 0; g <= 16; g++)
                for (int b = 0; b <= 16; b++)
                {
                    var rgb = new Rgb(r / 16f, g / 16f, b / 16f);
                    var back = Oklab.FromLinearSrgb(rgb).ToLinearSrgb();
                    worst = MathF.Max(worst, MathF.Max(MathF.Abs(back.R - rgb.R), MathF.Max(MathF.Abs(back.G - rgb.G), MathF.Abs(back.B - rgb.B))));
                }
        output.WriteLine($"worst linear round-trip error {worst:E2}");
        Assert.True(worst < 1e-5f, $"round-trip error {worst}");
    }

    [Fact]
    public void OkLchRoundTripsAndHueStaysInRange()
    {
        for (int i = 0; i < 360; i++)
        {
            var lab = new Oklab(0.6f, 0.2f * MathF.Cos(i * MathF.PI / 180), 0.2f * MathF.Sin(i * MathF.PI / 180));
            var lch = lab.ToLch();
            Assert.InRange(lch.H, 0f, 359.9999f);
            Assert.Equal(0.2f, lch.C, 1e-6f);
            Assert.True(HueDistance(lch.H, i) < 1e-3f, $"hue {lch.H} for {i}");
            var back = lch.ToOklab();
            Assert.Equal(lab.A, back.A, 1e-6f);
            Assert.Equal(lab.B, back.B, 1e-6f);
        }
        // A hue a hair below 0° rounds to 360 in float after wrapping; it must come back as 0.
        Assert.Equal(0f, new Oklab(0.5f, 0.1f, -1e-12f).ToLch().H);
    }

    [Fact]
    public void GreysAreNeutralAndLightnessIsMonotonic()
    {
        float previous = -1;
        for (int v = 0; v <= 255; v++)
        {
            var lab = new Rgba8((byte)v, (byte)v, (byte)v).ToOklab();
            Assert.True(lab.Chroma < 1e-6f, $"grey {v} has chroma {lab.Chroma}");
            Assert.True(lab.L > previous, $"L not increasing at {v}");
            previous = lab.L;
        }
    }

    [Fact]
    public void SrgbTransferMatchesTheStandard()
    {
        Assert.Equal(0f, Srgb.ToLinear(0f));
        Assert.Equal(1f, Srgb.ToLinear(1f), 1e-7f);
        Assert.Equal(0.21404114f, Srgb.ToLinear(0.5f), 1e-7f);        // ((0.5 + 0.055) / 1.055)^2.4
        Assert.Equal(0.04045f / 12.92f, Srgb.ToLinear(0.04045f), 1e-9f); // the linear segment
        Assert.Equal(0.5f, Srgb.FromLinear(0.21404114f), 1e-6f);
        Assert.Equal(0.002f * 12.92f, Srgb.FromLinear(0.002f), 1e-9f);         // the linear segment
        // The two pieces meet: across 1e-7 of input the output moves by about slope × 1e-7, with no jump.
        Assert.Equal(Srgb.ToLinear(0.04045f), Srgb.ToLinear(0.0404501f), 3e-8f);   // slope 1/12.92
        Assert.Equal(Srgb.FromLinear(0.0031308f), Srgb.FromLinear(0.0031309f), 2e-6f); // slope 12.92

        float worst = 0;
        for (int i = 0; i <= 1000; i++)
        {
            float x = i / 1000f;
            worst = MathF.Max(worst, MathF.Abs(Srgb.FromLinear(Srgb.ToLinear(x)) - x));
        }
        Assert.True(worst < 1e-6f, $"transfer round-trip error {worst}");
    }

    [Fact]
    public void ByteTableMatchesTheFormulaAndRoundTrips()
    {
        for (int v = 0; v <= 255; v++)
        {
            Assert.Equal(Srgb.ToLinear(v / 255f), Srgb.ToLinear((byte)v));
            Assert.Equal((byte)v, Srgb.ToByte(Srgb.ToLinear((byte)v)));
        }
        Assert.Equal(0, Srgb.ToByte(-0.5f));
        Assert.Equal(255, Srgb.ToByte(1.5f));
        // Palette.Default (contract) uses the same curve.
        Assert.Equal(Palette.Default.Primary.R, Srgb.ToLinear((byte)124), 1e-6f);
    }

    [Fact]
    public void ConversionsDoNotAllocate()
    {
        // C5 blends in Oklab every frame of a crossfade; these must stay allocation-free.
        float sink = 0;
        for (int i = 0; i < 100; i++) sink += Oklab.FromLinearSrgb(0.2f, 0.4f, 0.6f).ToLch().ToOklab().ToLinearSrgb().R;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            var lab = Oklab.FromLinearSrgb(i / 10_000f, 0.4f, 0.6f);
            sink += lab.ToLch().ToOklab().ToLinearSrgb().G + Srgb.ToLinear((byte)i) + Srgb.FromLinear(lab.L) + Glow.ToLight(lab).B;
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(float.IsFinite(sink));
    }

    internal static float HueDistance(float a, float b) => MathF.Abs((a - b + 540f) % 360f - 180f);
}
