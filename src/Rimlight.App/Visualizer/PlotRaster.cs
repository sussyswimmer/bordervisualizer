namespace Rimlight.App.Visualizer;

/// <summary>
/// A small software canvas for the visualizer's plots: opaque 32-bit pixels (0xAARRGGBB as one int, which is
/// <c>Pbgra32</c> in memory) that a <c>WriteableBitmap</c> shows with one copy. Lines, spans and translucent fills,
/// clipped to the canvas. Drawing allocates nothing. Not thread-safe; the UI thread owns it.
/// </summary>
internal sealed class PlotRaster
{
    /// <summary>Creates a canvas; both sizes are at least 1.</summary>
    public PlotRaster(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Pixels = new int[Width * Height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Rows top to bottom, Width pixels each.</summary>
    public int[] Pixels { get; }

    /// <summary>An opaque color from its sRGB bytes.</summary>
    public static int Rgb(int rgb) => unchecked((int)0xFF000000) | (rgb & 0xFFFFFF);

    /// <summary>Fills the whole canvas.</summary>
    public void Clear(int color) => Array.Fill(Pixels, color);

    /// <summary>Fills [x0, x1) × [y0, y1), blended with <paramref name="opacity"/> (0..1) over what is there.</summary>
    public void FillRect(int x0, int y0, int x1, int y1, int color, float opacity = 1)
    {
        x0 = Math.Max(x0, 0);
        y0 = Math.Max(y0, 0);
        x1 = Math.Min(x1, Width);
        y1 = Math.Min(y1, Height);
        if (x0 >= x1 || y0 >= y1 || !(opacity > 0)) return;
        int alpha = (int)MathF.Round(Math.Clamp(opacity, 0, 1) * 256);
        for (int y = y0; y < y1; y++)
        {
            Span<int> row = Pixels.AsSpan(y * Width + x0, x1 - x0);
            if (alpha >= 256) row.Fill(color);
            else for (int i = 0; i < row.Length; i++) row[i] = Blend(row[i], color, alpha);
        }
    }

    /// <summary>A vertical span from <paramref name="y0"/> to <paramref name="y1"/> (inclusive, any order), <paramref name="thickness"/> wide.</summary>
    public void VSpan(int x, int y0, int y1, int color, int thickness = 1, float opacity = 1)
    {
        if (y0 > y1) (y0, y1) = (y1, y0);
        FillRect(x - (thickness - 1) / 2, y0, x - (thickness - 1) / 2 + thickness, y1 + 1, color, opacity);
    }

    /// <summary>A horizontal line across the canvas.</summary>
    public void HLine(int y, int color, float opacity = 1) => FillRect(0, y, Width, y + 1, color, opacity);

    /// <summary>A straight line between two points (pixel centers), <paramref name="thickness"/> pixels thick.</summary>
    public void Line(float x0, float y0, float x1, float y1, int color, int thickness = 1)
    {
        // Clipped to the canvas first (Liang–Barsky), so the walk below never leaves it by more than the thickness.
        float margin = thickness;
        if (!Clip(ref x0, ref y0, ref x1, ref y1, -margin, -margin, Width - 1 + margin, Height - 1 + margin)) return;
        int ax = (int)MathF.Round(x0), ay = (int)MathF.Round(y0), bx = (int)MathF.Round(x1), by = (int)MathF.Round(y1);
        int dx = Math.Abs(bx - ax), dy = -Math.Abs(by - ay);
        int sx = ax < bx ? 1 : -1, sy = ay < by ? 1 : -1;
        int error = dx + dy;
        int offset = (thickness - 1) / 2;
        while (true)
        {
            Square(ax - offset, ay - offset, thickness, color);
            if (ax == bx && ay == by) break;
            int e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                ax += sx;
            }
            if (e2 <= dx)
            {
                error += dx;
                ay += sy;
            }
        }
    }

    // Liang–Barsky: shortens the segment to the rectangle; false when it misses it (or isn't finite).
    private static bool Clip(ref float x0, ref float y0, ref float x1, ref float y1, float left, float top, float right, float bottom)
    {
        if (!float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1)) return false;
        // Absurd coordinates would lose the precision the clip needs; plots never come near this.
        const float Limit = 1e6f;
        x0 = Math.Clamp(x0, -Limit, Limit);
        y0 = Math.Clamp(y0, -Limit, Limit);
        x1 = Math.Clamp(x1, -Limit, Limit);
        y1 = Math.Clamp(y1, -Limit, Limit);
        float dx = x1 - x0, dy = y1 - y0, t0 = 0, t1 = 1;
        if (!Edge(-dx, x0 - left, ref t0, ref t1) || !Edge(dx, right - x0, ref t0, ref t1)
            || !Edge(-dy, y0 - top, ref t0, ref t1) || !Edge(dy, bottom - y0, ref t0, ref t1)) return false;
        float sx = x0, sy = y0;
        x0 = sx + t0 * dx;
        y0 = sy + t0 * dy;
        x1 = sx + t1 * dx;
        y1 = sy + t1 * dy;
        return true;
    }

    private static bool Edge(float p, float q, ref float t0, ref float t1)
    {
        if (p == 0) return q >= 0;
        float t = q / p;
        if (p < 0)
        {
            if (t > t1) return false;
            if (t > t0) t0 = t;
        }
        else
        {
            if (t < t0) return false;
            if (t < t1) t1 = t;
        }
        return true;
    }

    private void Square(int x, int y, int size, int color)
    {
        if (size <= 1)
        {
            if ((uint)x < (uint)Width && (uint)y < (uint)Height) Pixels[y * Width + x] = color;
            return;
        }
        FillRect(x, y, x + size, y + size, color);
    }

    // Straight-alpha source over an opaque destination, alpha in 0..256.
    private static int Blend(int destination, int source, int alpha)
    {
        int inverse = 256 - alpha;
        int r = (((source >> 16) & 0xFF) * alpha + ((destination >> 16) & 0xFF) * inverse) >> 8;
        int g = (((source >> 8) & 0xFF) * alpha + ((destination >> 8) & 0xFF) * inverse) >> 8;
        int b = ((source & 0xFF) * alpha + (destination & 0xFF) * inverse) >> 8;
        return unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
    }
}
