namespace Rimlight.Core.Fakes;

internal sealed class FakePaletteExtractor : IPaletteExtractor
{
    public Palette? Extract(ReadOnlySpan<byte> bgra, int width, int height, string trackId)
    {
        if (width <= 0 || height <= 0) return null;
        long count = (long)width * height;
        if (count > bgra.Length / 4) throw new ArgumentException("Insufficient BGRA pixels.", nameof(bgra));
        double r = 0, g = 0, b = 0;
        for (int i = 0; i < (int)count; i++)
        {
            b += ToLinear(bgra[i * 4]);
            g += ToLinear(bgra[i * 4 + 1]);
            r += ToLinear(bgra[i * 4 + 2]);
        }
        Rgb average = new((float)(r / count), (float)(g / count), (float)(b / count));
        // A cyclic channel permutation rotates hue by 120 degrees.
        return new Palette(average, new Rgb(average.B, average.R, average.G), trackId);
    }

    private static float ToLinear(byte channel)
    {
        float value = channel / 255f;
        return value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }
}
