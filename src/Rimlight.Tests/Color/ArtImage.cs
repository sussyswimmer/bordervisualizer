using Rimlight.Core.Color;

namespace Rimlight.Tests.Color;

// An 8-bit sRGB color with straight alpha, as a decoded thumbnail delivers it.
internal readonly record struct Rgba8(byte R, byte G, byte B, byte A = 255)
{
    // The nearest 8-bit sRGB color to an Oklab color (clamped into gamut channel by channel).
    public static Rgba8 FromOklab(Oklab color, byte alpha = 255)
    {
        var linear = color.ToLinearSrgb();
        return new Rgba8(Srgb.ToByte(linear.R), Srgb.ToByte(linear.G), Srgb.ToByte(linear.B), alpha);
    }

    public Oklab ToOklab() => Oklab.FromLinearSrgb(Srgb.ToLinear(R), Srgb.ToLinear(G), Srgb.ToLinear(B));

    public Rgba8 WithAlpha(byte alpha) => this with { A = alpha };
}

// A procedurally generated test image in the extractor's input format: tightly packed BGRA8.
internal sealed class ArtImage
{
    public ArtImage(int width, int height)
    {
        Width = width;
        Height = height;
        Bgra = new byte[width * height * 4];
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public static ArtImage Create(int width, int height, Func<int, int, Rgba8> pixel)
    {
        var image = new ArtImage(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) image[x, y] = pixel(x, y);
        return image;
    }

    public static ArtImage Solid(int width, int height, Rgba8 color) => Create(width, height, (_, _) => color);

    // Exact shares: pixel i (row-major) takes the first color whose cumulative share exceeds i / count. The colors
    // form horizontal bands, like regions of a cover.
    public static ArtImage Bands(int width, int height, params (Rgba8 Color, float Share)[] bands)
    {
        int total = width * height;
        return Create(width, height, (x, y) =>
        {
            float position = (y * width + x + 0.5f) / total, cumulative = 0;
            foreach (var (color, share) in bands)
            {
                cumulative += share;
                if (position < cumulative) return color;
            }
            return bands[^1].Color;
        });
    }

    public Rgba8 this[int x, int y]
    {
        get
        {
            int o = (y * Width + x) * 4;
            return new Rgba8(Bgra[o + 2], Bgra[o + 1], Bgra[o], Bgra[o + 3]);
        }
        set
        {
            int o = (y * Width + x) * 4;
            Bgra[o] = value.B;
            Bgra[o + 1] = value.G;
            Bgra[o + 2] = value.R;
            Bgra[o + 3] = value.A;
        }
    }

    // Nearest-neighbor upscale by an integer factor.
    public ArtImage Upscale(int factor) => Create(Width * factor, Height * factor, (x, y) => this[x / factor, y / factor]);
}

// Deterministic procedural noise: an integer hash, so the images never depend on System.Random's implementation.
internal static class Noise
{
    // Uniform in [0, 1) for a lattice point.
    public static float Hash(int x, int y, int seed)
    {
        uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return (h >> 8) / 16777216f;
    }

    // Smooth value noise in [0, 1): bilinear with a smoothstep fade between lattice points `cell` pixels apart.
    public static float Value(float x, float y, float cell, int seed)
    {
        float fx = x / cell, fy = y / cell;
        int ix = (int)MathF.Floor(fx), iy = (int)MathF.Floor(fy);
        float tx = Fade(fx - ix), ty = Fade(fy - iy);
        float top = Lerp(Hash(ix, iy, seed), Hash(ix + 1, iy, seed), tx);
        float bottom = Lerp(Hash(ix, iy + 1, seed), Hash(ix + 1, iy + 1, seed), tx);
        return Lerp(top, bottom, ty);
    }

    // Three octaves of value noise, normalized back to [0, 1).
    public static float Fractal(float x, float y, float cell, int seed) =>
        (Value(x, y, cell, seed) * 4 + Value(x, y, cell / 2, seed + 1) * 2 + Value(x, y, cell / 4, seed + 2)) / 7;

    private static float Fade(float t) => t * t * (3 - 2 * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
