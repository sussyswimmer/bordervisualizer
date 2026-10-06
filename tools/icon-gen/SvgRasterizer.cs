using System.Xml;
using SkiaSharp;
using Svg.Skia;

namespace Rimlight.IconGen;

/// <summary>
/// Renders one SVG document to square bitmaps. The document's viewport (its width and height, or its viewBox) is
/// scaled uniformly to fit the bitmap and centered, so a square SVG fills it exactly.
/// </summary>
internal sealed class SvgRasterizer : IDisposable
{
    private readonly SKSvg svg;
    private readonly SKPicture picture;

    private SvgRasterizer(SKSvg svg, SKPicture picture, string source)
    {
        this.svg = svg;
        this.picture = picture;
        SKRect bounds = picture.CullRect;
        if (!(bounds.Width > 0 && bounds.Height > 0) || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height))
        {
            svg.Dispose();
            throw new InvalidDataException($"{source} has no size; give the <svg> a viewBox or a width and height.");
        }

        Bounds = bounds;
    }

    /// <summary>The SVG viewport in user units.</summary>
    public SKRect Bounds { get; }

    /// <summary>Parses an SVG file.</summary>
    /// <exception cref="InvalidDataException">The file is not an SVG Svg.Skia can draw.</exception>
    public static SvgRasterizer FromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"SVG not found: {path}", path);
        }

        return FromSvg(File.ReadAllText(path), path);
    }

    /// <summary>Parses SVG markup.</summary>
    /// <exception cref="InvalidDataException">The markup is not an SVG Svg.Skia can draw.</exception>
    public static SvgRasterizer FromSvg(string markup, string source = "SVG")
    {
        RequireViewport(markup, source);
        var svg = new SKSvg();
        SKPicture? picture;
        try
        {
            picture = svg.FromSvg(markup);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            svg.Dispose();
            throw new InvalidDataException($"{source} is not a valid SVG: {e.Message}", e);
        }

        if (picture is null)
        {
            svg.Dispose();
            throw new InvalidDataException($"{source} is not a valid SVG.");
        }

        return new SvgRasterizer(svg, picture, source);
    }

    /// <summary>
    /// The root &lt;svg&gt; must say how big it is. Without a viewBox or an absolute width and height, Svg.Skia
    /// sizes the picture to whatever is drawn, so the framing would silently change with the art.
    /// </summary>
    private static void RequireViewport(string markup, string source)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
        using var reader = XmlReader.Create(new StringReader(markup), settings);
        try
        {
            reader.MoveToContent();
        }
        catch (XmlException e)
        {
            throw new InvalidDataException($"{source} is not a valid SVG: {e.Message}", e);
        }

        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "svg")
        {
            throw new InvalidDataException($"{source} is not an SVG: its root element is not <svg>.");
        }

        static bool Absolute(string? length) => !string.IsNullOrWhiteSpace(length) && !length.TrimEnd().EndsWith('%');
        if (string.IsNullOrWhiteSpace(reader.GetAttribute("viewBox"))
            && !(Absolute(reader.GetAttribute("width")) && Absolute(reader.GetAttribute("height"))))
        {
            throw new InvalidDataException($"{source} has no size; give the <svg> a viewBox or a width and height.");
        }
    }

    /// <summary>
    /// Renders a <paramref name="size"/>×<paramref name="size"/> bitmap on a transparent background.
    /// </summary>
    /// <param name="opacity">Multiplies every pixel's alpha, 0 to 1; 0.5 gives the tray's "glow off" icon.</param>
    /// <returns>Straight-alpha (not premultiplied) BGRA, rows top to bottom.</returns>
    public byte[] RenderBgra(int size, float opacity = 1f)
    {
        if (size is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be 1 to 4096 px.");
        }

        if (!(opacity >= 0f && opacity <= 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "Opacity must be 0 to 1.");
        }

        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException($"Skia could not create a {size}x{size} surface.");
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float scale = Math.Min(size / Bounds.Width, size / Bounds.Height);
        canvas.Translate((size - Bounds.Width * scale) / 2f, (size - Bounds.Height * scale) / 2f);
        canvas.Scale(scale);
        canvas.Translate(-Bounds.Left, -Bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        // Read back unpremultiplied: .ico DIBs and PNGs both store straight alpha.
        byte[] pixels = new byte[size * size * 4];
        var straight = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using (var bitmap = new SKBitmap(straight))
        {
            if (!surface.ReadPixels(straight, bitmap.GetPixels(), straight.RowBytes, 0, 0))
            {
                throw new InvalidOperationException($"Skia could not read back the {size}x{size} render.");
            }

            bitmap.GetPixelSpan().CopyTo(pixels);
        }

        if (opacity < 1f)
        {
            ApplyOpacity(pixels, opacity);
        }

        return pixels;
    }

    /// <summary>Multiplies the alpha of straight-alpha BGRA pixels by <paramref name="opacity"/>, rounding.</summary>
    public static void ApplyOpacity(Span<byte> bgra, float opacity)
    {
        for (int i = 3; i < bgra.Length; i += 4)
        {
            bgra[i] = (byte)MathF.Round(bgra[i] * opacity, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>Encodes straight-alpha BGRA pixels as a PNG file.</summary>
    public static byte[] EncodePng(byte[] bgra, int width, int height)
    {
        if (bgra.Length != width * height * 4)
        {
            throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height}.", nameof(bgra));
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using SKData pixels = SKData.CreateCopy(bgra);
        using SKImage image = SKImage.FromPixels(info, pixels, info.RowBytes)
            ?? throw new InvalidOperationException("Skia could not wrap the pixels.");
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Skia could not encode the PNG.");
        return png.ToArray();
    }

    public void Dispose() => svg.Dispose();
}
