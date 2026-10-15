using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rimlight.App.Visualizer;

/// <summary>
/// Shows a <see cref="PlotRaster"/> in an <see cref="Image"/> at the host's size in device pixels, so lines stay sharp
/// at any display scaling. The bitmap is replaced only when the host's size or DPI changes. UI thread only.
/// </summary>
internal sealed class PlotSurface(FrameworkElement host, Image image)
{
    private WriteableBitmap? bitmap;

    /// <summary>The canvas to draw on; null until the host has a size.</summary>
    public PlotRaster? Raster { get; private set; }

    /// <summary>Device pixels per DIP.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>Lines about 1.5 DIPs wide, in whole pixels.</summary>
    public int Thickness => Math.Max(1, (int)Math.Round(1.5 * Scale));

    /// <summary>Matches the canvas to the host. True when it made a new one, which then needs a full redraw.</summary>
    public bool Fit()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(host);
        int width = (int)Math.Round(host.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Round(host.ActualHeight * dpi.DpiScaleY);
        if (width < 2 || height < 2) return false;
        if (bitmap is not null && bitmap.PixelWidth == width && bitmap.PixelHeight == height && Scale == dpi.DpiScaleX) return false;
        // At 96 × scale DPI the bitmap's size in DIPs is the host's, so the image shows it one pixel per pixel.
        bitmap = new WriteableBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32, null);
        Raster = new PlotRaster(width, height);
        Scale = dpi.DpiScaleX;
        image.Source = bitmap;
        return true;
    }

    /// <summary>Copies the canvas to the screen.</summary>
    public void Present()
    {
        if (bitmap is null || Raster is null) return;
        bitmap.WritePixels(new Int32Rect(0, 0, Raster.Width, Raster.Height), Raster.Pixels, Raster.Width * 4, 0);
    }
}
