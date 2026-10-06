using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon.Interop;
using Icon = System.Drawing.Icon;

namespace Rimlight.App;

/// <summary>
/// The tray icon at the notification area's size, and its 50% opacity variant for when the glow is off (doc 06 §2).
/// The dimmed one is derived from the app icon at startup, so it matches whatever <c>Assets/Rimlight.ico</c> ships;
/// an <c>Assets/Rimlight-dim.ico</c> in the build replaces it.
/// </summary>
internal sealed class TrayIconImages : IDisposable
{
    private const string IconUri = "pack://application:,,,/Assets/Rimlight.ico";
    private const string DimmedIconUri = "pack://application:,,,/Assets/Rimlight-dim.ico";

    private TrayIconImages(Icon normal, Icon dimmed)
    {
        Normal = normal;
        Dimmed = dimmed;
    }

    /// <summary>The icon while the glow is on.</summary>
    public Icon Normal { get; }

    /// <summary>The icon while the glow is off.</summary>
    public Icon Dimmed { get; }

    /// <summary>
    /// Builds both icons at the size Windows draws notification icons at (the small-icon size at the system DPI).
    /// Never throws: without a usable app icon both are the generic application icon. Call on the UI thread.
    /// </summary>
    /// <returns>The icons; dispose them after the tray icon is gone.</returns>
    public static TrayIconImages Load()
    {
        int size = Math.Max(16, IconUtilities.GetRequiredCustomIconSize(largeIcon: false).Width);
        try
        {
            byte[] normal = Pixels(Read(IconUri) ?? throw new FileNotFoundException("The app icon is not in the build.", IconUri), size,
                out int width, out int height);
            byte[] dimmed;
            if (Read(DimmedIconUri) is { } own)
            {
                dimmed = Pixels(own, size, out int dimmedWidth, out int dimmedHeight);
                if (dimmedWidth != width || dimmedHeight != height) dimmed = HalfOpacity(normal);
            }
            else
            {
                dimmed = HalfOpacity(normal);
            }
            return new TrayIconImages(ToIcon(normal, width, height), ToIcon(dimmed, width, height));
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Tray] Building the tray icons failed, using the generic icon: {exception}");
            return new TrayIconImages(new Icon(System.Drawing.SystemIcons.Application, size, size),
                new Icon(System.Drawing.SystemIcons.Application, size, size));
        }
    }

    public void Dispose()
    {
        Normal.Dispose();
        Dimmed.Dispose();
    }

    // The resource's bytes, or null if the build doesn't contain it.
    private static byte[]? Read(string uri)
    {
        try
        {
            using Stream? stream = Application.GetResourceStream(new Uri(uri, UriKind.Absolute))?.Stream;
            if (stream is null) return null;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        catch (IOException)
        {
            return null; // "Cannot locate resource"
        }
    }

    // Straight-alpha BGRA pixels of the .ico's best frame for this size: the exact size if present, otherwise the
    // next larger frame scaled down (or the largest scaled up).
    private static byte[] Pixels(byte[] ico, int size, out int width, out int height)
    {
        using var stream = new MemoryStream(ico);
        BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapFrame best = decoder.Frames
            .OrderBy(frame => frame.PixelWidth == size ? 0 : frame.PixelWidth > size ? 1 : 2)
            .ThenBy(frame => frame.PixelWidth >= size ? frame.PixelWidth : -frame.PixelWidth)
            .ThenByDescending(frame => frame.Format.BitsPerPixel)
            .First();

        BitmapSource source = best;
        if (best.PixelWidth != size || best.PixelHeight != size)
        {
            // Scaled with premultiplied alpha, so transparent pixels don't bleed dark fringes into the edges.
            source = new TransformedBitmap(new FormatConvertedBitmap(best, PixelFormats.Pbgra32, null, 0),
                new ScaleTransform((double)size / best.PixelWidth, (double)size / best.PixelHeight));
        }
        source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        width = source.PixelWidth;
        height = source.PixelHeight;
        byte[] pixels = new byte[width * height * 4];
        source.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static byte[] HalfOpacity(byte[] pixels)
    {
        byte[] dimmed = (byte[])pixels.Clone();
        for (int i = 3; i < dimmed.Length; i += 4) dimmed[i] = (byte)((dimmed[i] + 1) >> 1); // straight alpha: only A changes
        return dimmed;
    }

    // A one-frame .ico holding a PNG (supported by Windows since Vista, and how H.NotifyIcon builds icons too), loaded
    // as an icon that owns its handle.
    private static Icon ToIcon(byte[] pixels, int width, int height)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var png = new MemoryStream();
        encoder.Save(png);
        byte[] image = png.ToArray();

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); // ICONDIR: reserved
            writer.Write((ushort)1); // type: icon
            writer.Write((ushort)1); // one image
            writer.Write((byte)(width >= 256 ? 0 : width)); // ICONDIRENTRY: 0 means 256
            writer.Write((byte)(height >= 256 ? 0 : height));
            writer.Write((byte)0); // no palette
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(image.Length);
            writer.Write(6 + 16); // the image follows the directory
            writer.Write(image);
        }
        ico.Position = 0;
        return new Icon(ico);
    }
}
