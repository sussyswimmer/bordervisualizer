using System.Buffers.Binary;
using SkiaSharp;
using Xunit;

namespace Rimlight.IconGen.Tests;

/// <summary>An independent .ico parser for the tests, written from the format description, not from IcoWriter.</summary>
internal static class IcoReader
{
    internal sealed record Entry(
        int Width,
        int Height,
        byte ColorCount,
        byte Reserved,
        ushort Planes,
        ushort BitCount,
        int Size,
        int Offset,
        byte[] Data)
    {
        public bool IsPng => Data.Length >= 8 && Data.AsSpan(0, 8).SequenceEqual(PngSignature);
    }

    public static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static (ushort Reserved, ushort Type, Entry[] Entries) Parse(byte[] file)
    {
        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0));
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(2));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4));
        var entries = new Entry[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> e = file.AsSpan(6 + 16 * i, 16);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[8..]);
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[12..]);
            entries[i] = new Entry(
                e[0] == 0 ? 256 : e[0],
                e[1] == 0 ? 256 : e[1],
                e[2],
                e[3],
                BinaryPrimitives.ReadUInt16LittleEndian(e[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(e[6..]),
                size,
                offset,
                file.AsSpan(offset, size).ToArray());
        }

        return (reserved, type, entries);
    }

    /// <summary>Decodes an entry to straight-alpha BGRA, rows top to bottom.</summary>
    public static byte[] DecodeBgra(Entry entry)
    {
        return entry.IsPng ? DecodePng(entry.Data, entry.Width, entry.Height) : DecodeDib(entry.Data, entry.Width, entry.Height);
    }

    /// <summary>Decodes a PNG to straight-alpha BGRA exactly as stored (no premultiplied round trip).</summary>
    public static byte[] DecodePng(byte[] png, int width, int height)
    {
        using var data = SKData.CreateCopy(png);
        using SKCodec codec = SKCodec.Create(data) ?? throw new InvalidDataException("Not a PNG.");
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
        Assert.Equal(width, codec.Info.Width);
        Assert.Equal(height, codec.Info.Height);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, bitmap.GetPixels()));
        return bitmap.GetPixelSpan().ToArray();
    }

    public static byte[] DecodeDib(byte[] dib, int width, int height)
    {
        Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(dib));
        Assert.Equal(width, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4)));
        Assert.Equal(height * 2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8)));
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14)));
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            dib.AsSpan(40 + (height - 1 - y) * width * 4, width * 4).CopyTo(pixels.AsSpan(y * width * 4));
        }

        return pixels;
    }

    /// <summary>True when the AND-mask bit of pixel (x, y), counted from the top, says transparent.</summary>
    public static bool MaskBit(byte[] dib, int width, int height, int x, int y)
    {
        int stride = (width + 31) / 32 * 4;
        int maskStart = 40 + width * height * 4;
        byte b = dib[maskStart + (height - 1 - y) * stride + x / 8];
        return (b & (0x80 >> (x % 8))) != 0;
    }
}
