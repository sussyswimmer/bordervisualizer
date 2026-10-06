using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Rimlight.Platform.Media;

// Decodes a media session's thumbnail straight to a small BGRA8 image (doc 05 §1). The decoder scales while it
// decodes (Fant), so a large cover never exists at full size in our memory.
internal static class AlbumArtDecoder
{
    public const int MaxSide = 64;

    // The decoded art, or null for an image with no pixels. Throws when the stream can't be read or decoded (an
    // unknown format, broken data, the app went away) or `timeout` passes; the timeout bounds the whole decode, so a
    // hung media app can't stall the caller.
    public static async Task<AlbumArt?> DecodeAsync(IRandomAccessStreamReference thumbnail, string trackId, TimeSpan timeout, CancellationToken stopping)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        deadline.CancelAfter(timeout);
        CancellationToken token = deadline.Token;

        // AsTask(token) cancels the WinRT operation; WaitAsync(token) stops waiting even if the app never answers.
        using IRandomAccessStreamWithContentType stream = await thumbnail.OpenReadAsync().AsTask(token).WaitAsync(token).ConfigureAwait(false);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).WaitAsync(token).ConfigureAwait(false);
        if (!TryFit(decoder.PixelWidth, decoder.PixelHeight, MaxSide, out int width, out int height)) return null;

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)width,
            ScaledHeight = (uint)height,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        // Straight alpha: the extractor skips pixels whose alpha is below 128 and reads color unpremultiplied. EXIF
        // orientation can't change colors, and the cover's own color profile is ignored like the shader ignores it.
        PixelDataProvider provider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token).WaitAsync(token).ConfigureAwait(false);
        byte[]? pixels = Pack(provider.DetachPixelData(), width, height);
        return pixels is null ? null : new AlbumArt(pixels, width, height, trackId);
    }

    // The decoded size: the long side at most maxSide, the aspect ratio kept, never enlarged. False for an empty image.
    internal static bool TryFit(uint pixelWidth, uint pixelHeight, int maxSide, out int width, out int height)
    {
        width = height = 0;
        if (pixelWidth == 0 || pixelHeight == 0 || maxSide <= 0) return false;
        double scale = Math.Min(1.0, maxSide / (double)Math.Max(pixelWidth, pixelHeight));
        width = Math.Clamp((int)Math.Round(pixelWidth * scale), 1, maxSide);
        height = Math.Clamp((int)Math.Round(pixelHeight * scale), 1, maxSide);
        return true;
    }

    // Tightly packed rows, as the extractor requires: Bgra8 pixel data already is; a padded stride is repacked, and a
    // buffer too small for the image is rejected (null).
    internal static byte[]? Pack(byte[] data, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        int row = width * 4;
        int size = row * height;
        if (data.Length == size) return data;
        if (data.Length < size || data.Length % height != 0) return null;
        int stride = data.Length / height;
        var packed = new byte[size];
        for (int y = 0; y < height; y++) data.AsSpan(y * stride, row).CopyTo(packed.AsSpan(y * row));
        return packed;
    }
}
