using Rimlight.App.Overlay;
using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Platform.Tests;

// SrgbHex (K4: settings' hex ⇄ Core's linear RGB) and SrgbColor (K7: the color editor's hex and HSV).
public sealed class ColorTextTests
{
    [Fact]
    public void EveryEightBitColorRoundTripsThroughLinearRgb()
    {
        for (int r = 0; r < 256; r += 5)
            for (int g = 0; g < 256; g += 3)
                for (int b = 0; b < 256; b += 7)
                {
                    string hex = $"#{r:X2}{g:X2}{b:X2}";
                    Assert.True(SrgbHex.TryParse(hex, out Rgb linear));
                    Assert.Equal(hex, SrgbHex.Format(linear));
                }
        for (int c = 0; c < 256; c++)
        {
            string hex = $"#{c:X2}{c:X2}{c:X2}";
            Assert.True(SrgbHex.TryParse(hex, out Rgb linear));
            Assert.Equal(hex, SrgbHex.Format(linear));
        }
    }

    [Fact]
    public void SrgbHexDecodesWithTheSrgbCurve()
    {
        Assert.True(SrgbHex.TryParse("#FF8000", out Rgb rgb));
        Assert.Equal(1f, rgb.R);
        Assert.Equal(0.21586f, rgb.G, 4); // sRGB 128 is about 21.6 % linear
        Assert.Equal(0f, rgb.B);
        Assert.True(SrgbHex.TryParse(" 7c5cff ", out Rgb noHash)); // the settings store upper case, but any case is fine
        Assert.True(SrgbHex.TryParse("#7C5CFF", out Rgb withHash));
        Assert.Equal(withHash, noHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#7C5CF")]
    [InlineData("#7C5CFF00")]
    [InlineData("#GG0000")]
    [InlineData("violet")]
    public void SrgbHexRefusesWhatIsNotSixHexDigits(string? text) => Assert.False(SrgbHex.TryParse(text, out _));

    [Fact]
    public void SrgbHexFormatsOutOfRangeAndNonFiniteValuesSafely()
    {
        Assert.Equal("#FF0000", SrgbHex.Format(new Rgb(2, -1, float.NaN)));
        Assert.Equal("#000000", SrgbHex.Format(new Rgb(float.NegativeInfinity, 0, 0)));
    }

    [Theory]
    [InlineData("#7C5CFF", 0x7C, 0x5C, 0xFF)]
    [InlineData("7c5cff", 0x7C, 0x5C, 0xFF)]
    [InlineData(" #abc ", 0xAA, 0xBB, 0xCC)]
    [InlineData("FFF", 0xFF, 0xFF, 0xFF)]
    public void SrgbColorReadsSixAndThreeDigitHex(string text, int r, int g, int b)
    {
        Assert.True(SrgbColor.TryParse(text, out SrgbColor color));
        Assert.Equal(new SrgbColor((byte)r, (byte)g, (byte)b), color);
        Assert.Equal($"#{r:X2}{g:X2}{b:X2}", color.ToString()); // what the settings store
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#+12345")]
    [InlineData("#12 345")]
    [InlineData("rgb(1,2,3)")]
    public void SrgbColorRefusesEverythingElse(string? text) => Assert.False(SrgbColor.TryParse(text, out _));

    [Fact]
    public void HsvRoundTripsExactlyForEveryColorOnAGrid()
    {
        for (int r = 0; r < 256; r += 3)
            for (int g = 0; g < 256; g += 3)
                for (int b = 0; b < 256; b += 3)
                {
                    var color = new SrgbColor((byte)r, (byte)g, (byte)b);
                    (double h, double s, double v) = color.ToHsv();
                    Assert.Equal(color, SrgbColor.FromHsv(h, s, v));
                }
        Assert.Equal(new SrgbColor(255, 0, 0), SrgbColor.FromHsv(360, 1, 1)); // hue wraps
        Assert.Equal(new SrgbColor(255, 0, 0), SrgbColor.FromHsv(-360, 1, 1));
        Assert.Equal(new SrgbColor(0, 0, 0), SrgbColor.FromHsv(double.NaN, double.NaN, double.NaN));
    }
}
