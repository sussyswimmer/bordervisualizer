namespace Rimlight.IconGen;

/// <summary>Renders SVG art into .ico and PNG files.</summary>
internal static class IconBuilder
{
    /// <summary>
    /// The .ico sizes from doc 06 §2 (16/20/24/32/48/64/256), plus 40: Windows draws 32 px icons at 40 px at
    /// 125 % display scale, and would otherwise shrink the 48 px image.
    /// </summary>
    public static IReadOnlyList<int> DefaultSizes { get; } = [16, 20, 24, 32, 40, 48, 64, 256];

    /// <summary>
    /// Renders every size and packs them into an .ico (see <see cref="IcoWriter"/> for the layout).
    /// </summary>
    /// <param name="sizes">Square image sizes, 1 to 256, each at most once.</param>
    /// <param name="artFor">The art to draw at a given size (for example a simplified SVG for the small sizes).</param>
    /// <param name="opacity">Alpha multiplier for every image, 0 to 1.</param>
    public static byte[] BuildIco(IReadOnlyList<int> sizes, Func<int, SvgRasterizer> artFor, float opacity = 1f)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(artFor);
        var images = new List<IcoImage>(sizes.Count);
        foreach (int size in sizes)
        {
            if (size is < 1 or > IcoWriter.MaxSize)
            {
                throw new ArgumentOutOfRangeException(nameof(sizes), size, $"An .ico image must be 1 to {IcoWriter.MaxSize} px.");
            }

            byte[] bgra = artFor(size).RenderBgra(size, opacity);
            byte[] data = size >= IcoWriter.PngMinSize
                ? SvgRasterizer.EncodePng(bgra, size, size)
                : IcoWriter.EncodeDib(bgra, size, size);
            images.Add(new IcoImage(size, size, data));
        }

        return IcoWriter.Write(images);
    }

    /// <summary>Renders one square PNG.</summary>
    public static byte[] BuildPng(SvgRasterizer art, int size, float opacity = 1f)
    {
        ArgumentNullException.ThrowIfNull(art);
        return SvgRasterizer.EncodePng(art.RenderBgra(size, opacity), size, size);
    }
}
