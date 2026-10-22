using Rimlight.Core.Color;

namespace Rimlight.Tests.Color;

// Black-and-white covers for Maxwell's feedback ("a song that is white and black ... put light blue and dark blue"),
// generated procedurally like SampleArt, 64 × 64 like a decoded thumbnail. BlackAndWhiteArtTests says what the
// extractor must make of them.
internal static class BlackAndWhiteArt
{
    public const int Size = 64;

    // A black that leans blue and a white that leans blue, as a black-and-white JPEG often decodes.
    public static readonly Rgba8 CoolBlack = new(0x0B, 0x0E, 0x14);
    public static readonly Rgba8 CoolWhite = new(0xEE, 0xF2, 0xF8);

    // The hue of a cool cast in OkLCh (a slate blue).
    public const float CoolHue = 255f;

    // A minimalist cover: a title ("ECHO", 3.5 px cells) and a credit line ("IN SILENCE", 1.25 px cells) in `ink`
    // on a flat `background`. The text is drawn 4 × 4 finer and box-filtered down, mixing the encoded sRGB values like
    // an ordinary downscale, so every glyph edge is anti-aliased. The ink covers about 14.5 % of the image.
    // `channelNoise` adds independent grain to each channel (± half of it) and `blockNoise` shifts each 8 × 8 block
    // a little (± half of it), like a re-encoded JPEG.
    public static ArtImage TextCover(Rgba8 background, Rgba8 ink, float channelNoise = 0, float blockNoise = 0) =>
        ArtImage.Create(Size, Size, (x, y) =>
        {
            float coverage = Coverage(x, y, (sx, sy) => InText("ECHO", 5.75f, 14.25f, 3.5f, sx, sy) || InText("IN SILENCE", 7.625f, 44.4f, 1.25f, sx, sy));
            float block = (Noise.Hash(x / 8, y / 8, 41) - 0.5f) * blockNoise;
            byte Channel(byte from, byte to, int seed) =>
                Byte(from + (to - from) * coverage + block + (Noise.Hash(x, y, seed) - 0.5f) * channelNoise);
            return new Rgba8(Channel(background.R, ink.R, 42), Channel(background.G, ink.G, 43), Channel(background.B, ink.B, 44));
        });

    // The hue of a warm, sepia-like tone in OkLCh.
    public const float WarmHue = 60f;

    // A black-and-white "photo" with a cool cast: TonedPhoto at CoolHue.
    public static ArtImage CoolPhoto(float castChroma) => TonedPhoto(castChroma, CoolHue);

    // A black-and-white "photo" with a tone: a dark-to-light gradient under layered noise, every pixel tinted to OkLCh
    // chroma `castChroma` at `hue`, with ±3 grain per channel like a JPEG. Lightness runs 0.12..0.92.
    public static ArtImage TonedPhoto(float castChroma, float hue) => ArtImage.Create(Size, Size, (x, y) =>
    {
        float lightness = 0.12f + 0.8f * Math.Clamp(0.55f * y / (Size - 1f) + 0.45f * Noise.Fractal(x, y, 20, 51), 0f, 1f);
        var tinted = Rgba8.FromOklab(new OkLch(lightness, castChroma, hue).ToOklab());
        int Grain(int seed) => (int)MathF.Round((Noise.Hash(x, y, seed) - 0.5f) * 6);
        return new Rgba8(Byte(tinted.R + Grain(52)), Byte(tinted.G + Grain(53)), Byte(tinted.B + Grain(54)));
    });

    // The share of pixel (x, y) inside a shape, from 4 × 4 samples.
    public static float Coverage(int x, int y, Func<float, float, bool> inside)
    {
        int hits = 0;
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                if (inside(x + (i + 0.5f) / 4, y + (j + 0.5f) / 4)) hits++;
        return hits / 16f;
    }

    // Whether the point (x, y), in pixels, is inked in `text` set from (left, top) with square cells `cell` pixels
    // wide: each letter is 3 × 5 cells, with an empty cell after it.
    public static bool InText(string text, float left, float top, float cell, float x, float y)
    {
        float column = (x - left) / cell, row = (y - top) / cell;
        if (column < 0 || row < 0 || row >= 5) return false;
        int letter = (int)(column / 4), cx = (int)column - letter * 4;
        if (letter >= text.Length || cx == 3 || text[letter] == ' ') return false;
        return Font[text[letter]][(int)row][cx] == '#';
    }

    // A 3 × 5 bitmap font with just the letters these covers use.
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['C'] = [".##", "#..", "#..", "#..", ".##"],
        ['E'] = ["###", "#..", "##.", "#..", "###"],
        ['H'] = ["#.#", "#.#", "###", "#.#", "#.#"],
        ['I'] = ["###", ".#.", ".#.", ".#.", "###"],
        ['L'] = ["#..", "#..", "#..", "#..", "###"],
        ['N'] = ["##.", "#.#", "#.#", "#.#", "#.#"],
        ['O'] = [".#.", "#.#", "#.#", "#.#", ".#."],
        ['S'] = [".##", "#..", ".#.", "..#", "##."],
    };

    private static byte Byte(float value) => (byte)Math.Clamp(MathF.Round(value), 0, 255);
}
