using System.Buffers.Binary;
using Xunit;

namespace Rimlight.IconGen.Tests;

public sealed class IcoWriterTests
{
    private static byte[] Payload(int length, byte seed)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(seed + i * 7);
        }

        return data;
    }

    [Fact]
    public void HeaderAndDirectoryFollowTheIcoLayout()
    {
        // Out of order on purpose: the writer orders entries from smallest to largest.
        var images = new[]
        {
            new IcoImage(256, 256, Payload(300, 1)),
            new IcoImage(16, 16, Payload(40, 2)),
            new IcoImage(48, 48, Payload(77, 3)),
        };

        byte[] file = IcoWriter.Write(images);
        var (reserved, type, entries) = IcoReader.Parse(file);

        Assert.Equal(0, reserved);
        Assert.Equal(1, type); // 1 = icon, 2 = cursor
        Assert.Equal([16, 48, 256], entries.Select(e => e.Width));
        Assert.All(entries, e =>
        {
            Assert.Equal(e.Width, e.Height);
            Assert.Equal(0, e.ColorCount);
            Assert.Equal(0, e.Reserved);
            Assert.Equal(1, e.Planes);
            Assert.Equal(32, e.BitCount);
        });

        // 256 is stored as 0 in the one-byte width and height fields.
        Assert.Equal(0, file[6 + 2 * 16]);
        Assert.Equal(0, file[6 + 2 * 16 + 1]);
        Assert.Equal(16, file[6]);

        // Image data follows the directory back to back, in directory order, and fills the file exactly.
        int expectedOffset = 6 + 16 * 3;
        foreach (IcoReader.Entry entry in entries)
        {
            Assert.Equal(expectedOffset, entry.Offset);
            expectedOffset += entry.Size;
        }

        Assert.Equal(file.Length, expectedOffset);
        Assert.Equal(images[1].Data, entries[0].Data);
        Assert.Equal(images[2].Data, entries[1].Data);
        Assert.Equal(images[0].Data, entries[2].Data);
    }

    [Fact]
    public void StreamAndArrayOverloadsWriteTheSameBytes()
    {
        var images = new[] { new IcoImage(32, 32, Payload(10, 9)), new IcoImage(20, 20, Payload(5, 4)) };
        using var stream = new MemoryStream();
        IcoWriter.Write(stream, images);
        Assert.Equal(IcoWriter.Write(images), stream.ToArray());
    }

    [Fact]
    public void RejectsInvalidImageSets()
    {
        Assert.Throws<ArgumentException>(() => IcoWriter.Write(Array.Empty<IcoImage>()));
        Assert.Throws<ArgumentException>(() => IcoWriter.Write([new IcoImage(0, 0, Payload(4, 0))]));
        Assert.Throws<ArgumentException>(() => IcoWriter.Write([new IcoImage(257, 257, Payload(4, 0))]));
        Assert.Throws<ArgumentException>(() => IcoWriter.Write([new IcoImage(16, 300, Payload(4, 0))]));
        Assert.Throws<ArgumentException>(() => IcoWriter.Write([new IcoImage(16, 16, [])]));
        Assert.Throws<ArgumentException>(() => IcoWriter.Write(
            [new IcoImage(16, 16, Payload(4, 0)), new IcoImage(16, 16, Payload(4, 1))]));
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(16, 4)]
    [InlineData(20, 4)]
    [InlineData(24, 4)]
    [InlineData(32, 4)]
    [InlineData(33, 8)]
    [InlineData(40, 8)]
    [InlineData(48, 8)]
    [InlineData(64, 8)]
    [InlineData(256, 32)]
    public void AndMaskRowsArePaddedToFourBytes(int width, int stride)
    {
        Assert.Equal(stride, IcoWriter.AndMaskStride(width));
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(20, 20)]
    [InlineData(24, 24)]
    [InlineData(33, 17)]
    public void DibHeaderDescribesColorAndMask(int width, int height)
    {
        byte[] dib = IcoWriter.EncodeDib(new byte[width * height * 4], width, height);

        int colorBytes = width * height * 4;
        int maskBytes = IcoWriter.AndMaskStride(width) * height;
        Assert.Equal(40 + colorBytes + maskBytes, dib.Length);
        Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(0)));    // biSize
        Assert.Equal(width, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4)));    // biWidth
        Assert.Equal(height * 2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8))); // color + mask rows
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(12)));      // biPlanes
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14)));     // biBitCount
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16)));    // BI_RGB
        Assert.Equal((uint)(colorBytes + maskBytes), BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(20)));
        Assert.All(dib.AsSpan(24, 16).ToArray(), b => Assert.Equal(0, b)); // resolution and palette fields
    }

    [Fact]
    public void DibStoresRowsBottomUpAndMasksOnlyFullyTransparentPixels()
    {
        const int w = 20, h = 3;
        byte[] bgra = new byte[w * h * 4];
        void Set(int x, int y, byte b, byte g, byte r, byte a)
        {
            int i = (y * w + x) * 4;
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (b, g, r, a);
        }

        Set(0, 0, 10, 20, 30, 255);  // top-left, opaque
        Set(19, 0, 40, 50, 60, 1);   // top-right, almost transparent: still drawn
        Set(9, 2, 70, 80, 90, 128);  // bottom row, half transparent
        // Everything else is fully transparent black.

        byte[] dib = IcoWriter.EncodeDib(bgra, w, h);

        // The first stored color row is the bottom row of the image.
        int bottomRow = 40;
        int topRow = 40 + (h - 1) * w * 4;
        Assert.Equal(new byte[] { 70, 80, 90, 128 }, dib.AsSpan(bottomRow + 9 * 4, 4).ToArray());
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, dib.AsSpan(topRow, 4).ToArray());
        Assert.Equal(new byte[] { 40, 50, 60, 1 }, dib.AsSpan(topRow + 19 * 4, 4).ToArray());
        Assert.Equal(bgra, IcoReader.DecodeDib(dib, w, h));

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool transparent = bgra[(y * w + x) * 4 + 3] == 0;
                Assert.Equal(transparent, IcoReader.MaskBit(dib, w, h, x, y));
            }
        }

        // Padding bits past the last pixel of each mask row stay 0 (20 px use 3 of the row's 4 bytes).
        int maskStart = 40 + w * h * 4;
        for (int row = 0; row < h; row++)
        {
            Assert.Equal(0, dib[maskStart + row * 4 + 3]);
            Assert.Equal(0, dib[maskStart + row * 4 + 2] & 0x0F);
        }
    }

    [Fact]
    public void DibRejectsWrongBufferOrSize()
    {
        Assert.Throws<ArgumentException>(() => IcoWriter.EncodeDib(new byte[15 * 16 * 4], 16, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => IcoWriter.EncodeDib([], 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => IcoWriter.EncodeDib(new byte[257 * 4], 257, 1));
    }
}
