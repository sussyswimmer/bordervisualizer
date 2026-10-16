namespace Rimlight.App.Visualizer;

/// <summary>
/// A small software canvas for the visualizer's plots: premultiplied BGRA pixels (WPF's <c>Pbgra32</c>) on a
/// transparent background, so the window's own card shows through in light and dark themes. Lines are drawn column by
/// column with antialiased ends, which suits plots whose x only increases. Blending is integer math, so a full redraw
/// of a plot costs well under a millisecond. No allocation after construction. Pure code; any one thread.
/// </summary>
internal sealed class PlotRaster
{
    /// <summary>Creates a canvas of the given size in device pixels.</summary>
    public PlotRaster(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        Pixels = new int[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Premultiplied BGRA, one int per pixel, rows top to bottom.</summary>
    public int[] Pixels { get; }

    /// <summary>Makes every pixel transparent.</summary>
    public void Clear() => Array.Clear(Pixels);

    /// <summary>
    /// Covers part of column <paramref name="x"/> from <paramref name="top"/> to <paramref name="bottom"/> (pixel edges,
    /// fractions blend), with <paramref name="rgb"/> (0xRRGGBB, sRGB) at <paramref name="alpha"/>.
    /// </summary>
    public void FillColumn(int x, float top, float bottom, uint rgb, float alpha)
    {
        if ((uint)x >= (uint)Width || !(alpha > 0)) return;
        if (top > bottom) (top, bottom) = (bottom, top);
        top = Math.Clamp(top, 0, Height);
        bottom = Math.Clamp(bottom, 0, Height);
        if (!(bottom > top)) return;
        int first = (int)top;
        int last = Math.Min((int)MathF.Ceiling(bottom) - 1, Height - 1);
        if (first == last)
        {
            Blend(first * Width + x, rgb, Weight(alpha * (bottom - top)));
            return;
        }
        // Partly covered end pixels, and the fully covered ones between them at one weight.
        Blend(first * Width + x, rgb, Weight(alpha * (first + 1 - top)));
        Blend(last * Width + x, rgb, Weight(alpha * (bottom - last)));
        int weight = Weight(alpha);
        for (int y = first + 1; y < last; y++) Blend(y * Width + x, rgb, weight);
    }

    /// <summary>A horizontal line one pixel thick, centered on <paramref name="y"/>, from x0 to x1 (exclusive).</summary>
    public void HorizontalLine(float y, int x0, int x1, uint rgb, float alpha)
    {
        x0 = Math.Max(x0, 0);
        x1 = Math.Min(x1, Width);
        for (int x = x0; x < x1; x++) FillColumn(x, y - 0.5f, y + 0.5f, rgb, alpha);
    }

    /// <summary>A vertical line one pixel thick, centered on <paramref name="x"/>, split between the two columns it touches.</summary>
    public void VerticalLine(float x, float top, float bottom, uint rgb, float alpha)
    {
        float left = x - 0.5f;
        int column = (int)MathF.Floor(left);
        float share = left - column; // how much of the line falls in the next column
        FillColumn(column, top, bottom, rgb, alpha * (1 - share));
        FillColumn(column + 1, top, bottom, rgb, alpha * share);
    }

    /// <summary>
    /// A polyline through points whose x never decreases, <paramref name="thickness"/> pixels thick: each column it
    /// crosses is covered once, from the lowest to the highest y the line reaches there.
    /// </summary>
    public void Series(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys, uint rgb, float alpha, float thickness)
    {
        int n = Math.Min(xs.Length, ys.Length);
        if (n == 0) return;
        xs = xs[..n];
        ys = ys[..n];
        float half = MathF.Max(thickness, 0.5f) / 2;
        int firstColumn = Math.Max((int)MathF.Floor(xs[0]), 0);
        int lastColumn = Math.Min((int)MathF.Floor(xs[n - 1]), Width - 1);
        int segment = 0;
        for (int column = firstColumn; column <= lastColumn; column++)
        {
            float left = MathF.Max(column, xs[0]);
            float right = MathF.Min(column + 1, xs[n - 1]);
            while (segment < n - 2 && xs[segment + 1] < left) segment++;
            float low = YAt(xs, ys, segment, left), high = low;
            int i = segment + 1;
            for (; i < n && xs[i] < right; i++)
            {
                low = MathF.Min(low, ys[i]);
                high = MathF.Max(high, ys[i]);
            }
            float end = YAt(xs, ys, Math.Max(i - 1, segment), right);
            low = MathF.Min(low, end);
            high = MathF.Max(high, end);
            FillColumn(column, low - half, high + half, rgb, alpha);
        }
    }

    // y on the polyline at x, starting the search at segment `from` (x ascending).
    private static float YAt(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys, int from, float x)
    {
        int n = xs.Length;
        if (n == 1) return ys[0];
        int i = Math.Clamp(from, 0, n - 2);
        while (i < n - 2 && xs[i + 1] < x) i++;
        float x0 = xs[i], x1 = xs[i + 1];
        float t = x1 > x0 ? Math.Clamp((x - x0) / (x1 - x0), 0, 1) : 1;
        return ys[i] + (ys[i + 1] - ys[i]) * t;
    }

    // Coverage 0..1 as a weight 0..256.
    private static int Weight(float alpha) => (int)(Math.Clamp(alpha, 0, 1) * 256 + 0.5f);

    // Source-over in premultiplied BGRA: every channel becomes source × w + destination × (1 − w), w = weight / 256.
    private void Blend(int index, uint rgb, int weight)
    {
        if (weight <= 0) return;
        if (weight >= 256)
        {
            Pixels[index] = unchecked((int)(0xFF000000u | rgb));
            return;
        }
        uint source = 0xFF000000u | rgb;
        uint destination = unchecked((uint)Pixels[index]);
        uint keep = (uint)(256 - weight), take = (uint)weight;
        // Two channels at a time: alpha and green, then red and blue.
        uint ag = (((source >> 8) & 0x00FF00FFu) * take + ((destination >> 8) & 0x00FF00FFu) * keep) & 0xFF00FF00u;
        uint rb = (((source & 0x00FF00FFu) * take + (destination & 0x00FF00FFu) * keep) >> 8) & 0x00FF00FFu;
        Pixels[index] = unchecked((int)(ag | rb));
    }
}
