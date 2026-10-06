using Xunit;
using static Rimlight.IconGen.Tests.TestSvgs;

namespace Rimlight.IconGen.Tests;

public sealed class IconGenCliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = IconGenCli.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void HelpPrintsUsage()
    {
        var (code, output, _) = Run("--help");
        Assert.Equal(0, code);
        Assert.Contains("icon-gen ico <out.ico>", output);

        var (bare, _, error) = Run();
        Assert.Equal(2, bare);
        Assert.Contains("Usage:", error);
    }

    [Theory]
    [InlineData("bogus", "out.ico")]
    [InlineData("ico")]
    [InlineData("ico", "out.ico")]
    [InlineData("ico", "out.ico", "--svg")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "extra.ico")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--sizes", "16,16")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--sizes", "0")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--sizes", "257")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--sizes", "16,big")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--sizes", "-16")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--opacity", "1.5")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--opacity", "-0.5")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--opacity", "NaN")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--small-max", "24")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--size", "64")]
    [InlineData("ico", "out.ico", "--svg", "a.svg", "--frobnicate")]
    [InlineData("png", "out.png", "--svg", "a.svg")]
    [InlineData("png", "out.png", "--svg", "a.svg", "--size", "4097")]
    [InlineData("png", "out.png", "--svg", "a.svg", "--size", "64", "--sizes", "16")]
    [InlineData("png", "out.png", "--svg", "a.svg", "--size", "64", "--small-svg", "b.svg")]
    public void BadCommandLinesExitWith2(params string[] args)
    {
        var (code, output, error) = Run(args);
        Assert.Equal(2, code);
        Assert.Equal("", output);
        Assert.StartsWith("icon-gen: ", error);
    }

    [Fact]
    public void DefaultsMatchTheDocumentedOnes()
    {
        IconGenCli.Options options = IconGenCli.Parse(["ico", "x.ico", "--svg", "a.svg"]);
        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 256], options.Sizes);
        Assert.Equal(32, options.SmallMax);
        Assert.Equal(1f, options.Opacity);
        Assert.Null(options.SmallSvg);
    }

    [Fact]
    public void MissingOrInvalidSvgExitsWith1()
    {
        using var dir = new TempDir();
        var (missing, _, error) = Run("ico", dir.File("out.ico"), "--svg", dir.File("missing.svg"));
        Assert.Equal(1, missing);
        Assert.Contains("missing.svg", error);

        var (invalid, _, _) = Run("png", dir.File("out.png"), "--svg", dir.File("bad.svg", "<svg"), "--size", "16");
        Assert.Equal(1, invalid);
        Assert.False(File.Exists(dir.File("out.ico")));
        Assert.False(File.Exists(dir.File("out.png")));
    }

    [Fact]
    public void IcoCommandWritesTheRequestedImagesFromTheRightArt()
    {
        using var dir = new TempDir();
        string large = dir.File("large.svg", Solid("#0000FF"));
        string small = dir.File("small.svg", Solid("#FF0000"));
        string ico = Path.Combine(dir.Path, "nested", "out.ico");

        var (code, output, error) = Run(
            "ico", ico, "--svg", large, "--small-svg", small, "--small-max", "20", "--sizes", "48, 16,20,24", "--opacity", "0.5");

        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("16*, 20*, 24, 48 px", output);
        var (_, _, entries) = IcoReader.Parse(File.ReadAllBytes(ico));
        Assert.Equal([16, 20, 24, 48], entries.Select(e => e.Width));
        foreach (IcoReader.Entry e in entries)
        {
            var expected = e.Width <= 20 ? (0, 0, 255, 128) : (255, 0, 0, 128);
            Assert.Equal(expected, Pixel(IcoReader.DecodeBgra(e), e.Width, e.Width / 2, e.Height / 2));
        }
    }

    [Fact]
    public void PngCommandWritesOneSquarePng()
    {
        using var dir = new TempDir();
        string png = dir.File("out.png");
        var (code, output, _) = Run("png", png, "--svg", dir.File("art.svg", LeftHalfRed), "--size", "40");

        Assert.Equal(0, code);
        Assert.Contains("40x40 px", output);
        byte[] pixels = IcoReader.DecodePng(File.ReadAllBytes(png), 40, 40);
        Assert.Equal((0, 0, 255, 255), Pixel(pixels, 40, 19, 39));
        Assert.Equal((0, 0, 0, 0), Pixel(pixels, 40, 20, 0));
    }
}
