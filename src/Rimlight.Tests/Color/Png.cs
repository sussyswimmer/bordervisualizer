using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Rimlight.Tests.Color;

// A tiny PNG codec for the sample-art fixtures: 8-bit RGBA, no interlace, all five row filters (an image tool that
// re-saves a fixture in the same format picks its own filters).
internal static class Png
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    // filterForRow picks each row's PNG filter (0 none, 1 Sub, 2 Up, 3 Average, 4 Paeth); the fixtures use 0.
    public static byte[] Encode(ArtImage image, Func<int, int>? filterForRow = null)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; // bit depth
        header[9] = 6; // color type: RGBA

        int stride = image.Width * 4;
        var raw = new byte[image.Height * (1 + stride)];
        var previous = new byte[stride];
        var row = new byte[stride];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                row[x * 4] = pixel.R;
                row[x * 4 + 1] = pixel.G;
                row[x * 4 + 2] = pixel.B;
                row[x * 4 + 3] = pixel.A;
            }
            int filter = filterForRow?.Invoke(y) ?? 0;
            int o = y * (1 + stride);
            raw[o] = (byte)filter;
            for (int i = 0; i < stride; i++)
                raw[o + 1 + i] = (byte)(row[i] - Predict(filter, i >= 4 ? row[i - 4] : 0, previous[i], i >= 4 ? previous[i - 4] : 0));
            (previous, row) = (row, previous);
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(raw);

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    public static ArtImage Decode(byte[] png)
    {
        if (!png.AsSpan(0, Signature.Length).SequenceEqual(Signature)) throw new InvalidDataException("Not a PNG file.");
        int width = 0, height = 0;
        using var idat = new MemoryStream();
        for (int o = Signature.Length; o < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(o));
            string type = Encoding.ASCII.GetString(png, o + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(o + 8, length);
            if (Crc(png.AsSpan(o + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(o + 8 + length)))
                throw new InvalidDataException($"Bad CRC in {type}.");
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if (data[8] != 8 || data[9] != 6 || data[12] != 0) throw new InvalidDataException("Only 8-bit RGBA, non-interlaced PNGs are supported.");
            }
            else if (type == "IDAT") idat.Write(data);
            else if (type == "IEND") break;
            o += 12 + length;
        }

        int stride = width * 4;
        var raw = new byte[height * (1 + stride)];
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress)) zlib.ReadExactly(raw);

        var image = new ArtImage(width, height);
        var previous = new byte[stride];
        var row = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (1 + stride)];
            raw.AsSpan(y * (1 + stride) + 1, stride).CopyTo(row);
            for (int i = 0; i < stride; i++)
            {
                int left = i >= 4 ? row[i - 4] : 0, up = previous[i], upLeft = i >= 4 ? previous[i - 4] : 0;
                row[i] = (byte)(row[i] + Predict(filter, left, up, upLeft));
            }
            for (int x = 0; x < width; x++) image[x, y] = new Rgba8(row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]);
            (previous, row) = (row, previous);
        }
        return image;
    }

    // The PNG row-filter predictors, from the unfiltered left, up and upper-left bytes.
    private static int Predict(int filter, int left, int up, int upLeft) => filter switch
    {
        0 => 0,
        1 => left,
        2 => up,
        3 => (left + up) / 2,
        4 => Paeth(left, up, upLeft),
        _ => throw new InvalidDataException($"Unknown row filter {filter}."),
    };

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        stream.Write(word);
        byte[] typeAndData = [.. Encoding.ASCII.GetBytes(type), .. data];
        stream.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc(typeAndData));
        stream.Write(word);
    }

    // CRC-32 (ISO 3309), as PNG requires.
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in bytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
