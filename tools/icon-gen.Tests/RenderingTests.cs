using Xunit;
using static Rimlight.IconGen.Tests.TestSvgs;

namespace Rimlight.IconGen.Tests;

public sealed class RenderingTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(64)]
    public void ViewBoxFillsTheSquareOnATransparentBackground(int size)
    {
        using SvgRasterizer art = SvgRasterizer.FromSvg(LeftHalfRed);
        byte[] bgra = art.RenderBgra(size);

        Assert.Equal(size * size * 4, bgra.Length);
        int half = size / 2;
        for (int y = 0; y < size; y++)
        {
            Assert.Equal((0, 0, 255, 255), Pixel(bgra, size, 0, y));
            Assert.Equal((0, 0, 255, 255), Pixel(bgra, size, half - 1, y));
            Assert.Equal((0, 0, 0, 0), Pixel(bgra, size, half, y));
            Assert.Equal((0, 0, 0, 0), Pixel(bgra, size, size - 1, y));
        }
    }

    [Fact]
    public void NonSquareArtIsScaledUniformlyAndCentered()
    {
        using SvgRasterizer art = SvgRasterizer.FromSvg(WideBlue);
        byte[] bgra = art.RenderBgra(20);

        // 20×10 units in a 20 px square: rows 5..14 are blue, the bands above and below are empty.
        for (int x = 0; x < 20; x++)
        {
            Assert.Equal((0, 0, 0, 0), Pixel(bgra, 20, x, 4));
            Assert.Equal((255, 0, 0, 255), Pixel(bgra, 20, x, 5));
            Assert.Equal((255, 0, 0, 255), Pixel(bgra, 20, x, 14));
            Assert.Equal((0, 0, 0, 0), Pixel(bgra, 20, x, 15));
        }
    }

    [Fact]
    public void PixelsAreStraightAlpha()
    {
        using SvgRasterizer art = SvgRasterizer.FromSvg(HalfGreen);
        var (b, g, r, a) = Pixel(art.RenderBgra(8), 8, 4, 4);

        Assert.InRange(a, 127, 128);
        Assert.Equal((0, 255, 0), (b, g, r)); // premultiplied would be ~128
    }

    [Fact]
    public void OpacityScalesAlphaAndKeepsColors()
    {
        using SvgRasterizer art = SvgRasterizer.FromSvg(LeftHalfRed);
        byte[] full = art.RenderBgra(16);
        byte[] dim = art.RenderBgra(16, 0.5f);

        Assert.Equal((0, 0, 255, 128), Pixel(dim, 16, 0, 0));    // 255 × 0.5 rounds up
        Assert.Equal((0, 0, 0, 0), Pixel(dim, 16, 15, 0));
        for (int i = 0; i < full.Length; i += 4)
        {
            Assert.Equal((byte)MathF.Round(full[i + 3] * 0.5f, MidpointRounding.AwayFromZero), dim[i + 3]);
            Assert.Equal(full.AsSpan(i, 3).ToArray(), dim.AsSpan(i, 3).ToArray());
        }

        Assert.All(art.RenderBgra(16, 0f).Where((_, i) => i % 4 == 3), alpha => Assert.Equal(0, alpha));
    }

    [Fact]
    public void ApplyOpacityRoundsHalfAwayFromZero()
    {
        byte[] pixels = [9, 9, 9, 0, 9, 9, 9, 1, 9, 9, 9, 3, 9, 9, 9, 255];
        SvgRasterizer.ApplyOpacity(pixels, 0.5f);
        Assert.Equal(new byte[] { 9, 9, 9, 0, 9, 9, 9, 1, 9, 9, 9, 2, 9, 9, 9, 128 }, pixels);
    }

    [Fact]
    public void RejectsBadSizesAndOpacity()
    {
        using SvgRasterizer art = SvgRasterizer.FromSvg(LeftHalfRed);
        Assert.Throws<ArgumentOutOfRangeException>(() => art.RenderBgra(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => art.RenderBgra(4097));
        Assert.Throws<ArgumentOutOfRangeException>(() => art.RenderBgra(16, 1.01f));
        Assert.Throws<ArgumentOutOfRangeException>(() => art.RenderBgra(16, -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => art.RenderBgra(16, float.NaN));
    }

    [Theory]
    [InlineData("this is not xml")]
    [InlineData("<svg")]
    [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\" width=\"16\" height=\"16\"/>")]
    // No viewport: Svg.Skia would size the picture to its content, so the framing would follow the art.
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"10\" height=\"10\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\"><rect width=\"10\" height=\"10\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100%\" height=\"100%\"><rect width=\"10\" height=\"10\"/></svg>")]
    public void RejectsInputThatIsNotADrawableSvg(string markup)
    {
        Assert.Throws<InvalidDataException>(() => SvgRasterizer.FromSvg(markup));
    }

    [Theory]
    [InlineData("viewBox=\"0 0 20 10\"", 20, 10)]
    [InlineData("width=\"30\" height=\"15\"", 30, 15)]
    [InlineData("width=\"100%\" height=\"100%\" viewBox=\"0 0 20 10\"", 20, 10)]
    [InlineData("width=\"64\" height=\"64\" viewBox=\"0 0 32 32\"", 64, 64)]
    public void TheViewportComesFromViewBoxOrWidthAndHeight(string attributes, float width, float height)
    {
        using SvgRasterizer art = SvgRasterizer.FromSvg($"<svg xmlns=\"http://www.w3.org/2000/svg\" {attributes}/>");
        Assert.Equal((width, height), (art.Bounds.Width, art.Bounds.Height));
    }

    [Fact]
    public void MissingFileIsReported()
    {
        using var dir = new TempDir();
        Assert.Throws<FileNotFoundException>(() => SvgRasterizer.FromFile(dir.File("missing.svg")));
    }

    [Fact]
    public void PngRoundTripsStraightAlphaExactly()
    {
        const int w = 5, h = 3;
        byte[] bgra = new byte[w * h * 4];
        for (int i = 0; i < bgra.Length; i++)
        {
            bgra[i] = (byte)(i * 37 + 11);
        }

        for (int i = 3; i < bgra.Length; i += 4)
        {
            bgra[i] = (byte)Math.Max(1, (int)bgra[i]); // alpha 0 may legally drop its color
        }

        byte[] png = SvgRasterizer.EncodePng(bgra, w, h);
        Assert.True(png.AsSpan(0, 8).SequenceEqual(IcoReader.PngSignature));
        Assert.Equal(bgra, IcoReader.DecodePng(png, w, h));
        Assert.Throws<ArgumentException>(() => SvgRasterizer.EncodePng(bgra, w, h + 1));
    }

    [Fact]
    public void IcoHasEverySizeWithDibsBelow256AndAPngAt256()
    {
        using SvgRasterizer art = SvgRasterizer.FromSvg(LeftHalfRed);
        byte[] ico = IconBuilder.BuildIco(IconBuilder.DefaultSizes, _ => art);
        var (_, _, entries) = IcoReader.Parse(ico);

        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 256], entries.Select(e => e.Width));
        Assert.All(entries, e =>
        {
            Assert.Equal(e.Width >= 256, e.IsPng);
            byte[] pixels = IcoReader.DecodeBgra(e);
            Assert.Equal((0, 0, 255, 255), Pixel(pixels, e.Width, 0, e.Height - 1));
            Assert.Equal((0, 0, 0, 0), Pixel(pixels, e.Width, e.Width - 1, 0));
            if (!e.IsPng)
            {
                Assert.False(IcoReader.MaskBit(e.Data, e.Width, e.Height, 0, 0));
                Assert.True(IcoReader.MaskBit(e.Data, e.Width, e.Height, e.Width - 1, 0));
            }
        });
    }

    [Fact]
    public void EachSizeIsDrawnFromTheArtChosenForIt()
    {
        using SvgRasterizer small = SvgRasterizer.FromSvg(Solid("#FF0000"));
        using SvgRasterizer large = SvgRasterizer.FromSvg(Solid("#0000FF"));
        byte[] ico = IconBuilder.BuildIco([16, 32, 40, 256], size => size <= 32 ? small : large, 0.5f);

        foreach (IcoReader.Entry e in IcoReader.Parse(ico).Entries)
        {
            var expected = e.Width <= 32 ? (0, 0, 255, 128) : (255, 0, 0, 128);
            Assert.Equal(expected, Pixel(IcoReader.DecodeBgra(e), e.Width, e.Width / 2, e.Height / 2));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => IconBuilder.BuildIco([512], _ => large));
        Assert.Throws<ArgumentException>(() => IconBuilder.BuildIco([16, 16], _ => large));
    }

    [Fact]
    public void RenderingIsDeterministic()
    {
        using SvgRasterizer art = SvgRasterizer.FromSvg(HalfGreen);
        byte[] first = IconBuilder.BuildIco(IconBuilder.DefaultSizes, _ => art);
        Assert.Equal(first, IconBuilder.BuildIco(IconBuilder.DefaultSizes, _ => art));
    }
}
