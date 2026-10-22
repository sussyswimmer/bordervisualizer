using Rimlight.Core;
using Rimlight.Core.Color;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Color;

// Maxwell's feedback, twice: "Im playing a song that is white and black and it put light blue and dark blue", then on
// 1.0.0, with Spotify on Windows, that some covers are "still just light blue and dark blue". Reproduced on 35 real
// covers (docs/PROGRESS.md, "Black-and-white covers, again") and here on generated ones (BlackAndWhiteArt):
// - "No art" (doc 05 §1) was an image essentially one flat grey: 60 % flat at first, so a black cover with white text
//   was no art, then 98 %, so an all-black cover still was. No art shows the manual colors, #7C5CFF and #22D3EE: a
//   violet blue and a light blue. Now only an image with no opaque pixel is no art.
// - Glow-ify raised every color from chroma 0.03 on to 0.12, so a black-and-white photo with a slight cool cast
//   (chroma 0.04 here) glowed #3D73B6 and #588FD4, a dark and a light blue. Then casts up to 0.05 were kept as they
//   were, so the same photo glowed #627389 and #7D8FA5, and a real cover with a 0.02 cast a slate blue.
// Now a palette whose colors all stay at or below chroma 0.045 is a black-and-white cover and glows pure grey and
// white (Glow.ColorScale). Colorful covers glow as before.
public sealed class BlackAndWhiteArtTests(ITestOutputHelper output)
{
    // Pure grey: what's left of the chroma is float error.
    private const float PureGrey = 1e-3f;

    private static readonly Rgba8 Black = new(0, 0, 0);
    private static readonly Rgba8 White = new(255, 255, 255);

    // ---- the covers ----

    [Fact]
    public void BlackCoverWithWhiteTextGlowsSoftWhiteAndGrey()
    {
        // At first: no art, so #7C5CFF / #22D3EE. Now #BFBFBF / #717171.
        var palette = ExtractPureGrey(BlackAndWhiteArt.TextCover(Black, White));
        Assert.True(MathF.Max(Lch(palette.Primary).L, Lch(palette.Secondary).L) >= 0.75f, "no soft white");
    }

    [Fact]
    public void CoolBlackCoverWithCoolWhiteTextAndJpegNoiseGlowsPureGrey()
    {
        // #0B0E14 and #EEF2F8 (chroma 0.014 and 0.009) with ±3 grain per channel and ±2 per 8 × 8 block.
        // At first: no art. Then #A4A8AE / #6E7278, a blue-grey. Now pure grey.
        ExtractPureGrey(BlackAndWhiteArt.TextCover(BlackAndWhiteArt.CoolBlack, BlackAndWhiteArt.CoolWhite, channelNoise: 6, blockNoise: 4));
    }

    [Fact]
    public void CoolCastPhotoGlowsPureGreyNotBlue()
    {
        // At first: chroma 0.04 was raised to 0.12, so #3D73B6 / #588FD4. Then it was kept, so #627389 / #7D8FA5, a
        // slate blue. Now pure grey.
        var image = BlackAndWhiteArt.CoolPhoto(0.04f);
        var analysis = Analyze(image);
        foreach (var source in new[] { analysis.PrimarySource, analysis.SecondarySource })
            Assert.InRange(source.Chroma, 0.035f, 0.046f);
        Assert.True(analysis.ColorScale < 0.01f, $"color scale {analysis.ColorScale}");
        ExtractPureGrey(image);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.01f)]
    [InlineData(0.02f)] // the cast of the real cover that glowed a slate blue on 1.0.0
    [InlineData(0.03f)]
    [InlineData(0.035f)]
    public void CoolCastsUpTo0035GlowPureGrey(float cast) => ExtractPureGrey(BlackAndWhiteArt.CoolPhoto(cast));

    [Fact]
    public void WarmTonedPrintGlowsPureGrey()
    {
        // A black-and-white portrait printed warm (sepia-like, 0.04): real ones peak at 0.041–0.047, and glowed brown.
        ExtractPureGrey(BlackAndWhiteArt.TonedPhoto(0.04f, BlackAndWhiteArt.WarmHue));
    }

    [Fact]
    public void AllBlackCoverGlowsGreyNotTheManualColors()
    {
        // A real album's cover is a flat black square. It was no art (#7C5CFF / #22D3EE); now it glows #717171.
        var palette = ExtractPureGrey(ArtImage.Solid(BlackAndWhiteArt.Size, BlackAndWhiteArt.Size, Black));
        Assert.Equal(Glow.MinLightness, Lch(palette.Primary).L, 1e-3f);
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
    public void MutedColorPhotoKeepsItsColor()
    {
        // A faded warm photo (chroma 0.065: real muted covers peak at 0.053–0.061 and up) keeps its whole color.
        var image = BlackAndWhiteArt.TonedPhoto(0.065f, BlackAndWhiteArt.WarmHue);
        var analysis = Analyze(image);
        Assert.Equal(1f, analysis.ColorScale, 1e-3f);
        var palette = Extract(image);
        Assert.Equal(Glow.TargetChroma(analysis.PrimarySource.Chroma), Lch(palette.Primary).C, 2e-3f);
        Assert.True(OklabTests.HueDistance(Lch(palette.Primary).H, BlackAndWhiteArt.WarmHue) < 5, Lch(palette.Primary).ToString());
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

    // ---- blank images: grey, not the manual colors ----

    [Theory]
    [InlineData(0)]  // blank
    [InlineData(3)]  // a 3 × 3 speck: 16 pixels with its anti-aliased edge (0.4 %)
    [InlineData(6)]  // 6 × 6: 49 pixels (1.2 %)
    [InlineData(12)] // 12 × 12: 169 pixels (4.1 %)
    public void NearlyBlankImageGlowsGrey(int square)
    {
        // A flat grey tile with a white square from (29.5, 29.5), so its edge is anti-aliased. Up to 6 × 6 it was no
        // art, and the manual colors showed.
        ExtractPureGrey(ArtImage.Create(64, 64, (x, y) =>
        {
            float coverage = BlackAndWhiteArt.Coverage(x, y, (sx, sy) => sx >= 29.5f && sx < 29.5f + square && sy >= 29.5f && sy < 29.5f + square);
            byte v = (byte)MathF.Round(64 + (255 - 64) * coverage);
            return new Rgba8(v, v, v);
        }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(128)]
    [InlineData(240)]
    public void BlankImageWithJpegNoiseGlowsGrey(int grey)
    {
        // ±3 grain per channel and ±2 per 8 × 8 block on a blank grey or black: it was no art.
        ExtractPureGrey(ArtImage.Create(64, 64, (x, y) =>
        {
            float block = (Noise.Hash(x / 8, y / 8, 61) - 0.5f) * 4;
            byte Channel(int seed) => (byte)Math.Clamp(MathF.Round(grey + block + (Noise.Hash(x, y, seed) - 0.5f) * 6), 0, 255);
            return new Rgba8(Channel(62), Channel(63), Channel(64));
        }));
    }

    // ---- helpers ----

    private Palette ExtractPureGrey(ArtImage image)
    {
        var palette = Extract(image);
        foreach (var color in new[] { palette.Primary, palette.Secondary })
            Assert.True(Lch(color).C < PureGrey, $"not grey: {Lch(color)}");
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
