using Rimlight.Core;

namespace Rimlight.App.Visualizer;

/// <summary>
/// The plot colors (0xRRGGBB, sRGB) for one theme. The series keep the brand's hues (doc 08) in both themes, deeper on
/// a light background so they keep their contrast.
/// </summary>
internal readonly record struct PlotColors(
    uint Grid, uint Level, uint Bass, uint Beat, uint Flux, uint Threshold, uint SpectrumLow, uint SpectrumHigh)
{
    /// <summary>For WPF-UI's dark theme.</summary>
    public static PlotColors Dark { get; } = new(
        Grid: 0xFFFFFF, Level: 0xA996FF, Bass: 0x22D3EE, Beat: 0xFFB01F, Flux: 0xFF6FB0, Threshold: 0xFFD27A,
        SpectrumLow: 0x7C5CFF, SpectrumHigh: 0x22D3EE);

    /// <summary>For WPF-UI's light theme.</summary>
    public static PlotColors Light { get; } = new(
        Grid: 0x000000, Level: 0x5B3DF0, Bass: 0x0B8BA3, Beat: 0xB86E00, Flux: 0xC2185B, Threshold: 0xA15C00,
        SpectrumLow: 0x5B3DF0, SpectrumHigh: 0x0B8BA3);
}

/// <summary>
/// The spectrum (doc 03 §4): magnitude in dB over a log-frequency axis from 20 Hz to 20 kHz, with the Bass, Mid and
/// High bands of the tuning in effect shaded and their boundaries marked. Each pixel column shows the loudest bin it
/// covers, or the value between the two nearest bins where the bins are wider than a column (low frequencies). The
/// column-to-bin map is cached and rebuilt only when the width, the sample rate or the window size changes.
/// </summary>
internal sealed class SpectrumPlot
{
    public const float MinHz = 20;
    public const float MaxHz = 20000;
    public const float TopDb = 0;      // a full-scale sine centered on a bin (the analyzer's amplitude units)
    public const float BottomDb = -100;

    private int[] firstBin = [];
    private int[] lastBin = [];
    private float[] centerBin = [];
    private float[] xs = [];
    private float[] ys = [];
    private int cachedWidth, cachedRate, cachedWindow;
    private int[] background = [];
    private (int, int, float, float, float, float, PlotColors) backgroundKey;

    /// <summary>Where <paramref name="hz"/> falls on a plot <paramref name="width"/> pixels wide (may be outside it).</summary>
    public static float XOf(float hz, float width) =>
        MathF.Log(MathF.Max(hz, 1) / MinHz) / MathF.Log(MaxHz / MinHz) * width;

    /// <summary>Draws the snapshot's spectrum and band boundaries.</summary>
    public void Draw(PlotRaster raster, AnalyzerSnapshot snapshot, in PlotColors colors)
    {
        int width = raster.Width, height = raster.Height;
        AudioTuning tuning = snapshot.Tuning;
        DrawBackground(raster, tuning, colors);
        if (snapshot.SpectrumLength < 2 || snapshot.AnalysisSampleRate <= 0) return;

        Configure(width, snapshot.AnalysisSampleRate, snapshot.WindowSize);
        ReadOnlySpan<float> spectrum = snapshot.Spectrum.AsSpan(0, snapshot.SpectrumLength);
        for (int x = 0; x < width; x++)
        {
            float magnitude;
            if (firstBin[x] <= lastBin[x])
            {
                magnitude = 0;
                for (int k = firstBin[x]; k <= lastBin[x]; k++) magnitude = MathF.Max(magnitude, spectrum[k]);
            }
            else
            {
                // Bins wider than the column: interpolate between the two around its center.
                float position = Math.Clamp(centerBin[x], 0, spectrum.Length - 1);
                int k = Math.Min((int)position, spectrum.Length - 2);
                float t = position - k;
                magnitude = spectrum[k] + (spectrum[k + 1] - spectrum[k]) * t;
            }
            float db = 20 * MathF.Log10(MathF.Max(magnitude, 1e-7f));
            float y = Math.Clamp((TopDb - db) / (TopDb - BottomDb), 0, 1) * (height - 1);
            xs[x] = x + 0.5f;
            ys[x] = y;
            // The area under the curve, violet in the bass turning cyan in the treble (the brand gradient).
            raster.FillColumn(x, y, height, Mix(colors.SpectrumLow, colors.SpectrumHigh, x / (float)Math.Max(width - 1, 1)), 0.35f);
        }
        raster.Series(xs.AsSpan(0, width), ys.AsSpan(0, width), Mix(colors.SpectrumLow, colors.SpectrumHigh, 0.5f), 1, 1.5f);

        // Band boundaries on top.
        Boundary(raster, tuning.BassMinHz, colors.Bass);
        Boundary(raster, tuning.BassMaxHz, colors.Bass);
        Boundary(raster, tuning.MidMaxHz, colors.Level);
        Boundary(raster, tuning.HighMaxHz, colors.Beat);
    }

    // The grid and the shaded bands of the tuning in effect, in the colors of what each band drives. They change only
    // with the size, the band edges or the theme, so they are drawn once and copied in on every other frame.
    private void DrawBackground(PlotRaster raster, AudioTuning tuning, in PlotColors colors)
    {
        var key = (raster.Width, raster.Height, tuning.BassMinHz, tuning.BassMaxHz, tuning.MidMaxHz, tuning.HighMaxHz, colors);
        int pixels = raster.Pixels.Length;
        if (background.Length >= pixels && key == backgroundKey)
        {
            background.AsSpan(0, pixels).CopyTo(raster.Pixels);
            return;
        }
        raster.Clear();
        DrawGrid(raster, colors);
        Band(raster, tuning.BassMinHz, tuning.BassMaxHz, colors.Bass);
        Band(raster, tuning.BassMaxHz, tuning.MidMaxHz, colors.Level);
        Band(raster, tuning.MidMaxHz, tuning.HighMaxHz, colors.Beat);
        if (background.Length < pixels) background = new int[pixels];
        raster.Pixels.AsSpan().CopyTo(background);
        backgroundKey = key;
    }

    // Faint lines at every 20 dB and at each 1-2-5 frequency step, stronger at the decades.
    private static void DrawGrid(PlotRaster raster, in PlotColors colors)
    {
        for (float db = TopDb - 20; db > BottomDb; db -= 20)
            raster.HorizontalLine((TopDb - db) / (TopDb - BottomDb) * (raster.Height - 1), 0, raster.Width, colors.Grid, 0.08f);
        for (float decade = 10; decade <= MaxHz; decade *= 10)
        {
            for (int step = 1; step <= 9; step++)
            {
                float hz = decade * step;
                if (hz < MinHz || hz > MaxHz) continue;
                raster.VerticalLine(XOf(hz, raster.Width), 0, raster.Height, colors.Grid, step == 1 ? 0.16f : 0.05f);
            }
        }
    }

    private static void Band(PlotRaster raster, float fromHz, float toHz, uint rgb)
    {
        int x0 = Math.Max((int)MathF.Round(XOf(fromHz, raster.Width)), 0);
        int x1 = Math.Min((int)MathF.Round(XOf(toHz, raster.Width)), raster.Width);
        for (int x = x0; x < x1; x++) raster.FillColumn(x, 0, raster.Height, rgb, 0.07f);
    }

    private static void Boundary(PlotRaster raster, float hz, uint rgb)
    {
        float x = XOf(hz, raster.Width);
        if (x >= 0 && x <= raster.Width) raster.VerticalLine(x, 0, raster.Height, rgb, 0.8f);
    }

    // Which bins each column covers: bin k is at k × rate / window Hz (CoreFactory.CreateAnalyzer).
    private void Configure(int width, int rate, int window)
    {
        if (width == cachedWidth && rate == cachedRate && window == cachedWindow) return;
        cachedWidth = width;
        cachedRate = rate;
        cachedWindow = window;
        if (firstBin.Length < width)
        {
            firstBin = new int[width];
            lastBin = new int[width];
            centerBin = new float[width];
            xs = new float[width];
            ys = new float[width];
        }
        float binsPerHz = window / (float)rate;
        int maxBin = window / 2;
        float ratio = MathF.Log(MaxHz / MinHz);
        for (int x = 0; x < width; x++)
        {
            float from = MinHz * MathF.Exp(ratio * x / width);
            float to = MinHz * MathF.Exp(ratio * (x + 1) / width);
            firstBin[x] = Math.Min((int)MathF.Ceiling(from * binsPerHz), maxBin + 1);
            lastBin[x] = Math.Min((int)MathF.Ceiling(to * binsPerHz) - 1, maxBin);
            centerBin[x] = MinHz * MathF.Exp(ratio * (x + 0.5f) / width) * binsPerHz;
        }
    }

    internal static uint Mix(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Channel(int shift) => (uint)MathF.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }
}

/// <summary>
/// The scrolling plots (doc 03 §4): Level, Bass and Beat on 0..1, and raw flux with the beat threshold on one shared,
/// self-scaling axis. One point per analyzer call, placed by the time steps the analyzer actually got, so the time axis
/// is the real call cadence (not an assumed 60 Hz): the newest call at the right edge, the oldest of the history at the
/// left, with a faint line every second.
/// </summary>
internal sealed class HistoryPlot
{
    // The flux axis follows the largest value shown, and when that drops it relaxes by half every 2 s at 30 draws a
    // second, so a loud kick doesn't leave the plot flat for long.
    private const float RelaxPerDraw = 0.9885f;

    private float[] xs = [];
    private float[] ys = [];
    private float fluxScale;

    /// <summary>The time the plotted history covers, in seconds (0 before any data).</summary>
    public float SpanSeconds { get; private set; }

    /// <summary>The top of the flux axis, in the analyzer's amplitude units.</summary>
    public float FluxScale => fluxScale;

    /// <summary>Draws Level, Bass and Beat.</summary>
    public void DrawFeatures(PlotRaster raster, AnalyzerSnapshot snapshot, in PlotColors colors)
    {
        raster.Clear();
        int count = Layout(raster, snapshot, colors);
        for (int i = 1; i < 4; i++) raster.HorizontalLine(Y(i * 0.25f, 1, raster.Height), 0, raster.Width, colors.Grid, 0.08f);
        if (count == 0) return;
        Line(raster, snapshot.Bass, count, 1, colors.Bass, 1.5f);
        Line(raster, snapshot.Level, count, 1, colors.Level, 2f);
        Line(raster, snapshot.Beat, count, 1, colors.Beat, 1.5f);
    }

    /// <summary>Draws raw flux and the effective beat threshold on one axis, with a tick at the top for each beat.</summary>
    public void DrawFlux(PlotRaster raster, AnalyzerSnapshot snapshot, in PlotColors colors)
    {
        raster.Clear();
        int count = Layout(raster, snapshot, colors);
        if (count == 0) return;
        float top = 0;
        for (int i = 0; i < count; i++) top = MathF.Max(top, MathF.Max(Finite(snapshot.Flux[i]), Finite(snapshot.Threshold[i])));
        fluxScale = MathF.Max(MathF.Max(top * 1.1f, fluxScale * RelaxPerDraw), 1e-4f);
        for (int i = 1; i < 4; i++) raster.HorizontalLine(Y(i * 0.25f * fluxScale, fluxScale, raster.Height), 0, raster.Width, colors.Grid, 0.08f);
        Line(raster, snapshot.Threshold, count, fluxScale, colors.Threshold, 1.5f);
        Line(raster, snapshot.Flux, count, fluxScale, colors.Flux, 1.5f);
        // A tick for every call that fired a beat (BeatCount rose). Beat is the beat's grade, so a fired beat the analyzer
        // judged not kick-like shows a tick here with little or no Beat in the plot above.
        for (int i = 0; i < count; i++)
            if (snapshot.Fired[i] > 0) raster.VerticalLine(xs[i], 0, raster.Height * 0.12f, colors.Beat, 1);
    }

    // Places each history entry by its age (the sum of the newer time steps) and draws a line every second. Returns the
    // number of entries. Entries from before the visualizer opened have no time step; they get the average of the
    // known ones, so the axis keeps its scale while the history fills up.
    private int Layout(PlotRaster raster, AnalyzerSnapshot snapshot, in PlotColors colors)
    {
        int count = snapshot.HistoryLength;
        if (xs.Length < count)
        {
            xs = new float[count];
            ys = new float[count];
        }
        float known = 0;
        int steps = 0;
        for (int i = 1; i < count; i++)
        {
            float dt = snapshot.FrameSeconds[i];
            if (!(dt > 0)) continue;
            known += dt;
            steps++;
        }
        float fallback = steps > 0 ? known / steps : 0;
        float span = known + fallback * (count - 1 - steps);
        SpanSeconds = span;
        if (count == 0) return 0;
        float right = raster.Width - 1;
        if (!(span > 0))
        {
            for (int i = 0; i < count; i++) xs[i] = count > 1 ? right * i / (count - 1) : right;
            return count;
        }
        float pixelsPerSecond = right / span;
        float age = 0;
        for (int i = count - 1; i >= 0; i--)
        {
            xs[i] = right - age * pixelsPerSecond;
            float dt = snapshot.FrameSeconds[i];
            age += dt > 0 ? dt : fallback;
        }
        for (int second = 1; second < span; second++)
            raster.VerticalLine(right - second * pixelsPerSecond, 0, raster.Height, colors.Grid, 0.1f);
        return count;
    }

    private void Line(PlotRaster raster, float[] values, int count, float scale, uint rgb, float thickness)
    {
        for (int i = 0; i < count; i++) ys[i] = Y(Finite(values[i]), scale, raster.Height);
        raster.Series(xs.AsSpan(0, count), ys.AsSpan(0, count), rgb, 1, thickness);
    }

    // 0 at the bottom, `scale` at the top, with a pixel of room for the line at both ends.
    private static float Y(float value, float scale, int height) =>
        1 + (1 - Math.Clamp(value / scale, 0, 1)) * (height - 3);

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
}
