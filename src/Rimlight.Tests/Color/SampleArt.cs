namespace Rimlight.Tests.Color;

// Doc 05 §4: six small album-art-like images, generated procedurally (no real covers), 64 × 64 like a decoded
// thumbnail. They are committed as PNGs under src/Rimlight.Tests/fixtures/ so a person can look at them;
// SampleArtTests checks the files still match these generators.
internal static class SampleArt
{
    public const int Size = 64;

    // The logo color of dark-cover-logo, for its hue check.
    public static readonly Rgba8 LogoColor = new(255, 40, 160);

    public static readonly string[] Names =
        ["sunset-gradient", "blue-orange-coast", "noir-photo", "dark-cover-logo", "neon-split", "generic-app-icon"];

    public static ArtImage Create(string name) => name switch
    {
        "sunset-gradient" => SunsetGradient(),
        "blue-orange-coast" => BlueOrangeCoast(),
        "noir-photo" => NoirPhoto(),
        "dark-cover-logo" => DarkCoverLogo(),
        "neon-split" => NeonSplit(),
        "generic-app-icon" => GenericAppIcon(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown sample art."),
    };

    // A violet-to-orange sky, a soft yellow sun and a dark hill silhouette.
    private static ArtImage SunsetGradient() => ArtImage.Create(Size, Size, (x, y) =>
    {
        float grain = (Noise.Hash(x, y, 11) - 0.5f) * 8;
        float hill = 50 + 5 * MathF.Sin(x * 0.15f) + 3 * MathF.Sin(x * 0.41f + 1);
        if (y > hill) return Shade(24, 12, 38, grain);
        float t = y / hill;
        (float r, float g, float b) sky = t < 0.5f
            ? Mix((48, 22, 96), (214, 64, 128), t / 0.5f)
            : Mix((214, 64, 128), (255, 150, 52), (t - 0.5f) / 0.5f);
        float sun = Coverage(MathF.Sqrt((x - 34) * (x - 34) + (y - 38) * (y - 38)), 9);
        var color = Mix(sky, (255, 214, 96), sun);
        return Shade(color.r, color.g, color.b, grain);
    });

    // 70 % blue (sky over sea) and 30 % rippled orange sand, with grain: doc 05 §4's blue/orange case.
    private static ArtImage BlueOrangeCoast() => ArtImage.Create(Size, Size, (x, y) =>
    {
        float grain = (Noise.Hash(x, y, 12) - 0.5f) * 8;
        if (y >= 45) return Shade(238, 138, 48, grain + 24 * (Noise.Value(x, y, 5, 13) - 0.5f));
        var water = Mix((58, 118, 226), (40, 98, 208), y / 44f);
        return Shade(water.r, water.g, water.b, grain);
    });

    // A grayscale "photo": layered value noise, a bright moon, a vignette. R = G = B everywhere.
    private static ArtImage NoirPhoto() => ArtImage.Create(Size, Size, (x, y) =>
    {
        float value = 40 + 170 * Noise.Fractal(x, y, 24, 21);
        value = value * (1 - 0.6f * Coverage(MathF.Sqrt((x - 20) * (x - 20) + (y - 44) * (y - 44)), 14));
        value += 140 * Coverage(MathF.Sqrt((x - 44) * (x - 44) + (y - 18) * (y - 18)), 8);
        float vignette = 1 - 0.5f * MathF.Pow(MathF.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)) / 45f, 2);
        byte grey = Byte(value * vignette + (Noise.Hash(x, y, 22) - 0.5f) * 6);
        return new Rgba8(grey, grey, grey);
    });

    // Doc 05 §4: a typical dark cover with a small bright logo (a pink ring, about 3 % of the pixels) and a thin
    // line of white text. The logo, not the near-black, must become Primary.
    private static ArtImage DarkCoverLogo() => ArtImage.Create(Size, Size, (x, y) =>
    {
        float dark = 6 + 16 * Noise.Fractal(x, y, 16, 31);
        (float r, float g, float b) color = (dark * 0.8f, dark * 0.85f, dark * 1.25f);
        float radius = MathF.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 28.5f) * (y - 28.5f));
        float ring = Coverage(radius, 9) * (1 - Coverage(radius, 6));
        color = Mix(color, (LogoColor.R, LogoColor.G, LogoColor.B), ring);
        if (y is 52 or 53 && x is >= 20 and < 44) color = (236, 236, 236);
        return Shade(color.r, color.g, color.b, 0);
    });

    // Extreme saturation: a diagonal split of pure magenta and pure cyan, each fading a little toward its corner.
    // Both need gamut mapping once glow-ified.
    private static ArtImage NeonSplit() => ArtImage.Create(Size, Size, (x, y) =>
    {
        float fade = 1 - 0.25f * MathF.Abs(x - y) / Size;
        return x + 4 * MathF.Sin(y * 0.3f) > y ? Shade(255 * fade, 0, 255 * fade, 0) : Shade(0, 255 * fade, 255 * fade, 0);
    });

    // The "no art" quirk (doc 05 §1): a player's generic icon, a flat dark-grey tile with a white note glyph.
    private static ArtImage GenericAppIcon() => ArtImage.Create(Size, Size, (x, y) =>
    {
        float head = Coverage(MathF.Sqrt((x - 26) * (x - 26) * 0.7f + (y - 44) * (y - 44)), 6);
        float stem = x is >= 30 and < 33 && y is >= 16 and < 44 ? 1 : 0;
        float flag = x is >= 33 and < 42 && y >= 16 && y < 20 + (x - 33) / 3 ? 1 : 0;
        float glyph = MathF.Max(head, MathF.Max(stem, flag));
        byte grey = Byte(64 + (240 - 64) * glyph);
        return new Rgba8(grey, grey, grey);
    });

    // 1 inside the radius, 0 outside, with a one-pixel anti-aliased edge.
    private static float Coverage(float distance, float radius) => Math.Clamp(radius + 0.5f - distance, 0f, 1f);

    private static (float r, float g, float b) Mix((float r, float g, float b) a, (float r, float g, float b) b, float t) =>
        (a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);

    private static Rgba8 Shade(float r, float g, float b, float grain) => new(Byte(r + grain), Byte(g + grain), Byte(b + grain));

    private static byte Byte(float value) => (byte)Math.Clamp(MathF.Round(value), 0, 255);

    // The fixtures folder in the source tree: the directory holding Rimlight.Tests.csproj, found from the test
    // output folder (bin/<configuration>/<framework>/).
    public static string FixturesDirectory
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Rimlight.Tests.csproj")))
                    return Path.Combine(directory.FullName, "fixtures");
            }
            throw new DirectoryNotFoundException("Rimlight.Tests.csproj not found above " + AppContext.BaseDirectory);
        }
    }
}
