using Rimlight.Core;
using Rimlight.Core.Color;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Color;

// Maxwell's feedback: "Im playing a song that is white and black and it put light blue and dark blue." Two rules made
// it, reproduced here on generated covers (BlackAndWhiteArt):
// - "No art" (doc 05 §1) needed only 60 % of the image to be one flat color, so a black cover with white text (79 %
//   flat black) was no art and the manual colors showed: #7C5CFF and #22D3EE, a violet blue and a light blue.
// - Glow-ify raised every color from chroma 0.03 on to 0.12, so a black-and-white photo with a slight cool cast
//   (chroma 0.04 here) glowed #3D73B6 and #588FD4, a dark and a light blue.
// Now no art is a blank image (98 % one flat color), glow-ify keeps chroma up to 0.05, and all three covers glow
// neutral: both colors at chroma 0.05 or less, a soft white or a grey. Colorful covers glow as before.
public sealed class BlackAndWhiteArtTests(ITestOutputHelper output)
{
    // A neutral glow: both colors at or below this chroma (a tint at most; Oklab's just-noticeable difference is 0.02).
    private const float MaxNeutralChroma = 0.05f;

    private static readonly Rgba8 Black = new(0, 0, 0);
    private static readonly Rgba8 White = new(255, 255, 255);

    // ---- the three covers ----

    [Fact]
    public void BlackCoverWithWhiteTextGlowsSoftWhiteAndGrey()
    {
        // Before: no art, so #7C5CFF / #22D3EE. Now #BFBFBF / #717171.
        var image = BlackAndWhiteArt.TextCover(Black, White);
        var analysis = Analyze(image);
        Assert.InRange(analysis.FlatShare, 0.6f, 0.9f); // no art under the old 60 % rule, far from the new 98 %
        Assert.False(analysis.IsNoArt);

        var palette = ExtractNeutral(image);
        Assert.True(Lch(palette.Primary).C < 0.01f && Lch(palette.Secondary).C < 0.01f, "pure black and white is pure grey");
        Assert.True(MathF.Max(Lch(palette.Primary).L, Lch(palette.Secondary).L) >= 0.75f, "no soft white");
    }

    [Fact]
    public void CoolBlackCoverWithCoolWhiteTextAndJpegNoiseGlowsNeutral()
    {
        // #0B0E14 and #EEF2F8 (chroma 0.014 and 0.009) with ±3 grain per channel and ±2 per 8 × 8 block.
        // Before: no art, so #7C5CFF / #22D3EE. Now #A4A8AE / #6E7278.
        var image = BlackAndWhiteArt.TextCover(BlackAndWhiteArt.CoolBlack, BlackAndWhiteArt.CoolWhite, channelNoise: 6, blockNoise: 4);
        var analysis = Analyze(image);
        Assert.InRange(analysis.FlatShare, 0.6f, 0.9f);
        Assert.False(analysis.IsNoArt);
        ExtractNeutral(image);
    }

    [Fact]
    public void CoolCastPhotoGlowsNeutralNotBlue()
    {
        // Before: chroma 0.04 was raised to 0.12, so #3D73B6 / #588FD4. Now #627389 / #7D8FA5: the photo's own faint
        // cool tint, at the glow's lightness.
        var image = BlackAndWhiteArt.CoolPhoto(0.04f);
        var analysis = Analyze(image);
        foreach (var source in new[] { analysis.PrimarySource, analysis.SecondarySource })
            Assert.InRange(source.Chroma, 0.035f, 0.045f); // above the old 0.03 threshold, which raised it to 0.12

        var palette = ExtractNeutral(image);
        Assert.Equal(analysis.PrimarySource.Chroma, Lch(palette.Primary).C, 1e-3f); // kept, not raised
        Assert.Equal(analysis.SecondarySource.Chroma, Lch(palette.Secondary).C, 1e-3f);
        Assert.True(OklabTests.HueDistance(Lch(palette.Primary).H, BlackAndWhiteArt.CoolHue) < 5, $"primary {Lch(palette.Primary)}");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.02f)]
    [InlineData(0.03f)]
    [InlineData(0.035f)]
    [InlineData(0.045f)]
    [InlineData(0.05f)]
    public void CoolCastsUpTo005KeepTheirChroma(float cast)
    {
        // Faint casts up to Glow.NeutralChroma, as black-and-white covers show. Before, each one from 0.03 on glowed
        // blue (0.12).
        var palette = Extract(BlackAndWhiteArt.CoolPhoto(cast));
        foreach (var color in new[] { palette.Primary, palette.Secondary })
            Assert.True(Lch(color).C <= cast + 2e-3f, $"cast {cast}: {Lch(color)}");
    }

    // ---- what must not change ----

    [Theory]
    [InlineData(0.10f)]
    [InlineData(0.12f)]
    [InlineData(0.15f)]
    public void ClearlyColoredPhotoStillGlowsSaturated(float tone)
    {
        // The same photo toned clearly blue (chroma 0.10 and up): the full raise to at least 0.12, at its hue.
        var palette = Extract(BlackAndWhiteArt.CoolPhoto(tone));
        foreach (var color in new[] { palette.Primary, palette.Secondary })
        {
            var lch = Lch(color);
            Assert.True(lch.C >= Glow.MinChroma - 1e-3f, $"tone {tone}: {lch}");
            Assert.True(OklabTests.HueDistance(lch.H, BlackAndWhiteArt.CoolHue) < 2, $"tone {tone}: {lch}");
        }
    }

    [Fact]
    public void ColoredCoverWithWhiteTextStillGlowsSaturated()
    {
        // The text cover's layout on a deep red: #B6484E / #AD515D, as before. The blend of red and white along the
        // glyph edges (chroma 0.108) still gets the full raise.
        var red = new Rgba8(0x8B, 0x1E, 0x2B);
        var palette = Extract(BlackAndWhiteArt.TextCover(red, White));
        foreach (var color in new[] { palette.Primary, palette.Secondary })
        {
            var lch = Lch(color);
            Assert.True(lch.C >= Glow.MinChroma - 1e-3f, lch.ToString());
            Assert.True(OklabTests.HueDistance(lch.H, red.ToOklab().ToLch().H) < 10, lch.ToString());
        }
    }

    [Theory]
    [InlineData(0, true)]   // blank
    [InlineData(3, true)]   // a 3 × 3 speck: 16 pixels with its anti-aliased edge (0.4 %)
    [InlineData(6, true)]   // 6 × 6: 49 pixels (1.2 %)
    [InlineData(12, false)] // 12 × 12: 169 pixels (4.1 %), a mark, so art
    public void NearlyBlankImageIsStillNoArt(int square, bool noArt)
    {
        // A flat grey tile with a white square from (29.5, 29.5), so its edge is anti-aliased.
        var image = ArtImage.Create(64, 64, (x, y) =>
        {
            float coverage = BlackAndWhiteArt.Coverage(x, y, (sx, sy) => sx >= 29.5f && sx < 29.5f + square && sy >= 29.5f && sy < 29.5f + square);
            byte v = (byte)MathF.Round(64 + (255 - 64) * coverage);
            return new Rgba8(v, v, v);
        });
        var analysis = Analyze(image);
        output.WriteLine($"{square} × {square}: flat share {analysis.FlatShare:F4}");
        Assert.Equal(noArt, analysis.IsNoArt);
        Assert.Equal(noArt, CoreFactory.CreatePaletteExtractor().Extract(image.Bgra, 64, 64, "t") is null);
    }

    [Theory]
    [InlineData(16)] // about the darkest grey where this grain stays flat; darker, Oklab spreads it 2–3 times wider
    [InlineData(128)]
    [InlineData(240)]
    public void BlankImageWithJpegNoiseIsStillNoArt(int grey)
    {
        // ±3 grain per channel and ±2 per 8 × 8 block on a blank grey: still at least 98 % within 0.03 of one color.
        var image = ArtImage.Create(64, 64, (x, y) =>
        {
            float block = (Noise.Hash(x / 8, y / 8, 61) - 0.5f) * 4;
            byte Channel(int seed) => (byte)Math.Clamp(MathF.Round(grey + block + (Noise.Hash(x, y, seed) - 0.5f) * 6), 0, 255);
            return new Rgba8(Channel(62), Channel(63), Channel(64));
        });
        var analysis = Analyze(image);
        output.WriteLine($"#{grey:X2}{grey:X2}{grey:X2} with noise: flat share {analysis.FlatShare:F4}");
        Assert.True(analysis.IsNoArt);
    }

    // ---- helpers ----

    private Palette ExtractNeutral(ArtImage image)
    {
        var palette = Extract(image);
        foreach (var color in new[] { palette.Primary, palette.Secondary })
            Assert.True(Lch(color).C <= MaxNeutralChroma, $"not neutral: {Lch(color)}");
        return palette;
    }

    private Palette Extract(ArtImage image)
    {
        var palette = CoreFactory.CreatePaletteExtractor().Extract(image.Bgra, image.Width, image.Height, "track");
        Assert.NotNull(palette); // not "no art": the manual colors would show
        Assert.True(Glow.InGamut(palette.Primary) && Glow.InGamut(palette.Secondary));
        output.WriteLine($"#{Hex(palette.Primary)} {Lch(palette.Primary)} / #{Hex(palette.Secondary)} {Lch(palette.Secondary)}");
        return palette;
    }

    private static PaletteAnalysis Analyze(ArtImage image)
    {
        var analysis = PaletteExtractor.Analyze(image.Bgra, image.Width, image.Height);
        Assert.NotNull(analysis);
        return analysis;
    }

    private static OkLch Lch(Rgb color) => Oklab.FromLinearSrgb(color).ToLch();

    private static string Hex(Rgb color) => $"{Srgb.ToByte(color.R):X2}{Srgb.ToByte(color.G):X2}{Srgb.ToByte(color.B):X2}";
}
