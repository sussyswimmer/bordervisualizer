using Rimlight.Core;
using Rimlight.Core.Color;
using Xunit;
using Xunit.Abstractions;

namespace Rimlight.Tests.Color;

// Doc 05 §4: the six procedural album-art-like fixtures, committed as PNGs so they can be inspected, and what the
// extractor must make of each. Set RIMLIGHT_WRITE_FIXTURES=1 to rewrite the PNGs after changing a generator.
public sealed class SampleArtTests(ITestOutputHelper output)
{
    public static TheoryData<string> Names => new(SampleArt.Names);

    [Theory]
    [MemberData(nameof(Names))]
    public void FixturePngMatchesItsGenerator(string name)
    {
        var generated = SampleArt.Create(name);
        string path = Path.Combine(SampleArt.FixturesDirectory, name + ".png");
        if (!File.Exists(path) || Environment.GetEnvironmentVariable("RIMLIGHT_WRITE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Png.Encode(generated));
        }
        var decoded = Png.Decode(File.ReadAllBytes(path));
        Assert.Equal(SampleArt.Size, decoded.Width);
        Assert.Equal(SampleArt.Size, decoded.Height);
        Assert.True(generated.Bgra.AsSpan().SequenceEqual(decoded.Bgra), $"{path} is stale: rerun with RIMLIGHT_WRITE_FIXTURES=1");
    }

    [Fact]
    public void PngCodecRoundTripsEveryRowFilter()
    {
        var image = ArtImage.Create(7, 10, (x, y) => new Rgba8((byte)(x * 37 + y), (byte)(y * 51), (byte)(x * y * 13), (byte)(255 - x * 20)));
        Assert.Equal(image.Bgra, Png.Decode(Png.Encode(image)).Bgra);
        Assert.Equal(image.Bgra, Png.Decode(Png.Encode(image, y => y % 5)).Bgra);
        // The PNG signature and the IHDR chunk CRC of a 1×1 RGBA image, checked against the spec's layout.
        byte[] png = Png.Encode(ArtImage.Solid(1, 1, new Rgba8(1, 2, 3)));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, png[..16]);
        Assert.Equal(new byte[] { 0x1F, 0x15, 0xC4, 0x89 }, png[29..33]);
    }

    [Fact]
    public void SunsetGivesTwoWarmDistinctColors()
    {
        var palette = Extract("sunset-gradient");
        var primary = Lch(palette.Primary);
        var secondary = Lch(palette.Secondary);
        Assert.True(primary.C >= 0.1f && secondary.C >= 0.1f, $"{primary} / {secondary}");
        Assert.True(IsWarm(primary.H), $"primary hue {primary.H}");
        Assert.True(OklabTests.HueDistance(primary.H, secondary.H) >= 15, $"{primary.H} vs {secondary.H}");
    }

    [Fact]
    public void CoastGivesBluePrimaryAndOrangeSecondary()
    {
        var palette = Extract("blue-orange-coast");
        Assert.InRange(Lch(palette.Primary).H, 245f, 275f);
        Assert.InRange(Lch(palette.Secondary).H, 40f, 75f);
    }

    [Fact]
    public void NoirPhotoGlowsSoftWhite()
    {
        var palette = Extract("noir-photo");
        Assert.True(Lch(palette.Primary).C < Glow.NeutralChroma);
        Assert.True(Lch(palette.Secondary).C < Glow.NeutralChroma);
    }

    [Fact]
    public void DarkCoverPicksItsLogo()
    {
        var palette = Extract("dark-cover-logo");
        Assert.True(OklabTests.HueDistance(Lch(palette.Primary).H, SampleArt.LogoColor.ToOklab().ToLch().H) < 10);
    }

    [Fact]
    public void NeonSplitGivesMagentaAndCyanInsideTheGamut()
    {
        var palette = Extract("neon-split");
        float magenta = new Rgba8(255, 0, 255).ToOklab().ToLch().H, cyan = new Rgba8(0, 255, 255).ToOklab().ToLch().H;
        float[] hues = [Lch(palette.Primary).H, Lch(palette.Secondary).H];
        Assert.Contains(hues, h => OklabTests.HueDistance(h, magenta) < 10);
        Assert.Contains(hues, h => OklabTests.HueDistance(h, cyan) < 10);
    }

    [Fact]
    public void GenericAppIconIsNoArt()
    {
        var image = Load("generic-app-icon");
        Assert.Null(CoreFactory.CreatePaletteExtractor().Extract(image.Bgra, image.Width, image.Height, "t"));
    }

    private Palette Extract(string name)
    {
        var image = Load(name);
        var palette = CoreFactory.CreatePaletteExtractor().Extract(image.Bgra, image.Width, image.Height, name);
        Assert.NotNull(palette);
        Assert.True(Glow.InGamut(palette.Primary) && Glow.InGamut(palette.Secondary));
        output.WriteLine($"{name}: primary {Lch(palette.Primary)} #{Hex(palette.Primary)}, secondary {Lch(palette.Secondary)} #{Hex(palette.Secondary)}");
        return palette;
    }

    // The committed PNG when present (what a person sees), else the generator.
    private static ArtImage Load(string name)
    {
        string path = Path.Combine(SampleArt.FixturesDirectory, name + ".png");
        return File.Exists(path) ? Png.Decode(File.ReadAllBytes(path)) : SampleArt.Create(name);
    }

    private static OkLch Lch(Rgb color) => Oklab.FromLinearSrgb(color).ToLch();

    private static string Hex(Rgb color) => $"{Srgb.ToByte(color.R):X2}{Srgb.ToByte(color.G):X2}{Srgb.ToByte(color.B):X2}";

    // Reds, oranges, yellows, pinks and magentas.
    private static bool IsWarm(float hue) => hue < 100 || hue > 300;
}
