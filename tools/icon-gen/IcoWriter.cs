using System.Buffers.Binary;

namespace Rimlight.IconGen;

/// <summary>One image of an .ico file: its pixel size and its encoded data (a PNG file or a DIB).</summary>
/// <param name="Width">Width in pixels, 1 to 256.</param>
/// <param name="Height">Height in pixels, 1 to 256.</param>
/// <param name="Data">A complete PNG file, or a DIB from <see cref="IcoWriter.EncodeDib"/>.</param>
internal sealed record IcoImage(int Width, int Height, byte[] Data);

/// <summary>
/// Writes Windows .ico files. The layout is the classic one every Windows API, the resource compiler and the
/// shell read: sizes below <see cref="PngMinSize"/> are 32-bpp DIBs (straight alpha plus a 1-bpp AND mask), and
/// 256 px is a PNG, which Windows reads from Vista on.
/// </summary>
internal static class IcoWriter
{
    /// <summary>The largest image an .ico can describe; its directory stores 256 as 0.</summary>
    public const int MaxSize = 256;

    /// <summary>Images this size or larger are stored as PNG; smaller ones as DIBs.</summary>
    public const int PngMinSize = 256;

    /// <summary>ICONDIR header size in bytes.</summary>
    public const int HeaderSize = 6;

    /// <summary>ICONDIRENTRY size in bytes.</summary>
    public const int EntrySize = 16;

    /// <summary>BITMAPINFOHEADER size in bytes.</summary>
    public const int BitmapInfoHeaderSize = 40;

    /// <summary>
    /// Writes the images, ordered from smallest to largest, to <paramref name="output"/>.
    /// </summary>
    /// <exception cref="ArgumentException">No images, a size outside 1..256, two images of the same size, or
    /// empty data.</exception>
    public static void Write(Stream output, IReadOnlyList<IcoImage> images)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("An .ico needs at least one image.", nameof(images));
        }

        if (images.Count > ushort.MaxValue)
        {
            throw new ArgumentException($"An .ico holds at most {ushort.MaxValue} images.", nameof(images));
        }

        var seen = new HashSet<(int, int)>();
        foreach (IcoImage image in images)
        {
            ArgumentNullException.ThrowIfNull(image, nameof(images));
            if (image.Width is < 1 or > MaxSize || image.Height is < 1 or > MaxSize)
            {
                throw new ArgumentException(
                    $"Image size {image.Width}x{image.Height} is outside 1..{MaxSize}.", nameof(images));
            }

            if (image.Data is null || image.Data.Length == 0)
            {
                throw new ArgumentException($"The {image.Width}x{image.Height} image has no data.", nameof(images));
            }

            if (!seen.Add((image.Width, image.Height)))
            {
                throw new ArgumentException($"Two images are {image.Width}x{image.Height}.", nameof(images));
            }
        }

        IcoImage[] ordered = images.OrderBy(i => i.Width * i.Height).ThenBy(i => i.Width).ToArray();

        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(header[0..], 0); // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1); // type: 1 = icon
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)ordered.Length);
        output.Write(header);

        long offset = HeaderSize + (long)EntrySize * ordered.Length;
        Span<byte> entry = stackalloc byte[EntrySize];
        foreach (IcoImage image in ordered)
        {
            if (offset + image.Data.Length > uint.MaxValue)
            {
                throw new ArgumentException("The images are too large for one .ico.", nameof(images));
            }

            entry[0] = (byte)(image.Width == MaxSize ? 0 : image.Width);
            entry[1] = (byte)(image.Height == MaxSize ? 0 : image.Height);
            entry[2] = 0; // palette colors: none
            entry[3] = 0; // reserved
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);  // color planes
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32); // bits per pixel
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)image.Data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            output.Write(entry);
            offset += image.Data.Length;
        }

        foreach (IcoImage image in ordered)
        {
            output.Write(image.Data);
        }
    }

    /// <summary>Writes the images to a new byte array; see <see cref="Write(Stream, IReadOnlyList{IcoImage})"/>.</summary>
    public static byte[] Write(IReadOnlyList<IcoImage> images)
    {
        using var stream = new MemoryStream();
        Write(stream, images);
        return stream.ToArray();
    }

    /// <summary>
    /// Encodes a straight-alpha BGRA bitmap as an .ico DIB: a BITMAPINFOHEADER whose height counts both masks,
    /// the 32-bpp color rows bottom-up, then the 1-bpp AND mask bottom-up with rows padded to 4 bytes. The mask
    /// marks fully transparent pixels; Windows draws 32-bpp images with their alpha channel and ignores it.
    /// </summary>
    /// <param name="bgra">Rows top to bottom, 4 bytes per pixel in B, G, R, A order, not premultiplied.</param>
    public static byte[] EncodeDib(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width is < 1 or > MaxSize || height is < 1 or > MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"{width}x{height} is outside 1..{MaxSize}.");
        }

        int colorRow = width * 4;
        if (bgra.Length != colorRow * height)
        {
            throw new ArgumentException($"Expected {colorRow * height} bytes for {width}x{height}.", nameof(bgra));
        }

        int maskRow = AndMaskStride(width);
        int colorBytes = colorRow * height;
        int maskBytes = maskRow * height;
        byte[] dib = new byte[BitmapInfoHeaderSize + colorBytes + maskBytes];

        Span<byte> h = dib.AsSpan(0, BitmapInfoHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[0..], BitmapInfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(h[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(h[8..], height * 2); // color rows + mask rows
        BinaryPrimitives.WriteUInt16LittleEndian(h[12..], 1);  // planes
        BinaryPrimitives.WriteUInt16LittleEndian(h[14..], 32); // bits per pixel
        BinaryPrimitives.WriteUInt32LittleEndian(h[16..], 0);  // BI_RGB
        BinaryPrimitives.WriteUInt32LittleEndian(h[20..], (uint)(colorBytes + maskBytes));
        // Resolution and palette fields stay 0.

        Span<byte> color = dib.AsSpan(BitmapInfoHeaderSize, colorBytes);
        Span<byte> mask = dib.AsSpan(BitmapInfoHeaderSize + colorBytes, maskBytes);
        for (int y = 0; y < height; y++)
        {
            int dibRow = height - 1 - y; // DIBs are stored bottom-up
            ReadOnlySpan<byte> source = bgra.Slice(y * colorRow, colorRow);
            source.CopyTo(color.Slice(dibRow * colorRow, colorRow));

            Span<byte> maskLine = mask.Slice(dibRow * maskRow, maskRow);
            for (int x = 0; x < width; x++)
            {
                if (source[x * 4 + 3] == 0)
                {
                    maskLine[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return dib;
    }

    /// <summary>Bytes per AND-mask row: one bit per pixel, padded to a 4-byte boundary.</summary>
    public static int AndMaskStride(int width) => (width + 31) / 32 * 4;
}
