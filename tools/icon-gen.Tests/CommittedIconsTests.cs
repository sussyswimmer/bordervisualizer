using Xunit;

namespace Rimlight.IconGen.Tests;

/// <summary>
/// The icons in the repo are generated files (build/icons.sh). These tests fail when one is missing, malformed,
/// or no longer matches its SVG, so an edited SVG can't be merged without regenerating the icons.
/// </summary>
public sealed class CommittedIconsTests
{
    // Mirrors build/icons.sh and build/icons.ps1.
    private const string Ico = "src/Rimlight.App/Assets/Rimlight.ico";
    private const string DimIco = "src/Rimlight.App/Assets/Rimlight-dim.ico";
    private const string Svg = "assets/icon.svg";
    private const string SmallSvg = "assets/icon-small.svg";
    private const int SmallMax = 32;

    // Skia's rasterizer may round a little differently on another OS or CPU; any real change to the art moves
    // some pixel much further than this (on a 0..255 premultiplied scale).
    private const int Tolerance = 8;

    private static readonly string Root = FindRepoRoot();

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rimlight.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Rimlight.sln not found above " + AppContext.BaseDirectory);
    }

    private static byte[] Read(string relativePath) => File.ReadAllBytes(Path.Combine(Root, relativePath));

    [Theory]
    [InlineData(Ico)]
    [InlineData(DimIco)]
    public void AppIconsHaveEverySizeInTheClassicLayout(string path)
    {
        byte[] file = Read(path);
        var (reserved, type, entries) = IcoReader.Parse(file);

        Assert.Equal((0, 1), (reserved, type));
        Assert.Equal(IconBuilder.DefaultSizes, entries.Select(e => e.Width));
        Assert.All(entries, e =>
        {
            Assert.Equal(e.Width, e.Height);
            Assert.Equal((1, 32), (e.Planes, e.BitCount));
            Assert.Equal(e.Width == 256, e.IsPng);
            Assert.Equal(e.Width * e.Height * 4, IcoReader.DecodeBgra(e).Length);
        });
        Assert.Equal(file.Length, entries[^1].Offset + entries[^1].Size);
    }

    [Fact]
    public void DimIconIsTheAppIconAtHalfOpacity()
    {
        var full = IcoReader.Parse(Read(Ico)).Entries;
        var dim = IcoReader.Parse(Read(DimIco)).Entries;
        Assert.Equal(full.Length, dim.Length);
        for (int n = 0; n < full.Length; n++)
        {
            byte[] a = IcoReader.DecodeBgra(full[n]);
            byte[] b = IcoReader.DecodeBgra(dim[n]);
            for (int i = 0; i < a.Length; i += 4)
            {
                Assert.Equal((byte)MathF.Round(a[i + 3] * 0.5f, MidpointRounding.AwayFromZero), b[i + 3]);
                if (b[i + 3] != 0)
                {
                    Assert.Equal(a.AsSpan(i, 3).ToArray(), b.AsSpan(i, 3).ToArray());
                }
            }
        }
    }

    [Fact]
    public void IconsMatchAFreshRenderOfTheSvgs()
    {
        using SvgRasterizer art = SvgRasterizer.FromFile(Path.Combine(Root, Svg));
        using SvgRasterizer small = SvgRasterizer.FromFile(Path.Combine(Root, SmallSvg));

        foreach (var (path, opacity) in new[] { (Ico, 1f), (DimIco, 0.5f) })
        {
            foreach (IcoReader.Entry e in IcoReader.Parse(Read(path)).Entries)
            {
                SvgRasterizer source = e.Width <= SmallMax ? small : art;
                AssertClose(source.RenderBgra(e.Width, opacity), IcoReader.DecodeBgra(e), $"{path} at {e.Width} px");
            }
        }

        foreach (int size in new[] { 256, 512 })
        {
            string png = $"assets/icon-{size}.png";
            AssertClose(art.RenderBgra(size), IcoReader.DecodePng(Read(png), size, size), png);
        }
    }

    private static void AssertClose(byte[] expected, byte[] actual, string what)
    {
        Assert.Equal(expected.Length, actual.Length);
        int worst = 0;
        for (int i = 0; i < expected.Length; i += 4)
        {
            int ea = expected[i + 3], aa = actual[i + 3];
            worst = Math.Max(worst, Math.Abs(ea - aa));
            for (int c = 0; c < 3; c++)
            {
                // Compare premultiplied, so the color of an almost transparent pixel doesn't count for much.
                worst = Math.Max(worst, Math.Abs(expected[i + c] * ea - actual[i + c] * aa) / 255);
            }
        }

        Assert.True(worst <= Tolerance, $"{what} differs from a fresh render by up to {worst}/255; run build/icons.sh.");
    }
}
