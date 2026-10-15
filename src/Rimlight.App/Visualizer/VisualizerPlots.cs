using Rimlight.App.Overlay;
using Rimlight.Core;

namespace Rimlight.App.Visualizer;

/// <summary>The plots' colors: a dark scope in either app theme, with the brand's violet and cyan.</summary>
internal static class PlotColors
{
    public static readonly int Background = PlotRaster.Rgb(0x101016);
    public static readonly int Grid = PlotRaster.Rgb(0x262631);
    public static readonly int Level = PlotRaster.Rgb(0xA08CFF);
    public static readonly int Bass = PlotRaster.Rgb(0x22D3EE);
    public static readonly int Beat = PlotRaster.Rgb(0xFFB020);
    public static readonly int Flux = PlotRaster.Rgb(0xE8E8F0);
    public static readonly int Threshold = PlotRaster.Rgb(0xFF5C7A);
    public static readonly int Spectrum = PlotRaster.Rgb(0xE8E8F0);
    public static readonly int SpectrumFill = PlotRaster.Rgb(0x7C5CFF);
    public static readonly int BassBand = PlotRaster.Rgb(0x7C5CFF);
    public static readonly int MidBand = PlotRaster.Rgb(0x22D3EE);
    public static readonly int HighBand = PlotRaster.Rgb(0xFFB020);
}

/// <summary>
/// The spectrum (doc 03 §4): magnitudes in dB over a log frequency axis from 20 Hz to 20 kHz, with the Bass, Mid and
/// High bands of the current tuning shaded. UI thread; drawing allocates nothing.
/// </summary>
internal sealed class SpectrumPlot(PlotRaster raster)
{
    public const double MinHz = 20;
    public const double MaxHz = 20000;
    public const float MinDb = -100;
    public const float MaxDb = 0;
    private const float Floor = 1e-9f;

    private static readonly double[] Grid = [50, 100, 200, 500, 1000, 2000, 5000, 10000];

    /// <summary>Frequencies with a grid line (and a label under the plot).</summary>
    public static IReadOnlyList<double> GridHz => Grid;

    private readonly int[] background = new int[raster.Pixels.Length]; // the bands and grid, redrawn when the bands move
    private AudioTuning? backgroundTuning;

    public PlotRaster Raster { get; } = raster;

    /// <summary>Where <paramref name="hz"/> sits across a plot <paramref name="width"/> wide (any unit).</summary>
    public static double XForHz(double hz, double width) =>
        width * (Math.Log(Math.Max(hz, 1e-3)) - Math.Log(MinHz)) / (Math.Log(MaxHz) - Math.Log(MinHz));

    /// <summary>The frequency at <paramref name="x"/> across a plot <paramref name="width"/> wide.</summary>
    public static double HzForX(double x, double width) =>
        Math.Exp(Math.Log(MinHz) + x / Math.Max(width, 1) * (Math.Log(MaxHz) - Math.Log(MinHz)));

    /// <summary>Draws a snapshot's spectrum; <paramref name="thickness"/> is the line width in pixels.</summary>
    public void Draw(AnalyzerSnapshot snapshot, int thickness)
    {
        PlotRaster r = Raster;
        AudioTuning tuning = snapshot.Tuning;
        if (ReferenceEquals(tuning, backgroundTuning))
        {
            background.CopyTo(r.Pixels, 0);
        }
        else
        {
            r.Clear(PlotColors.Background);
            Band(tuning.BassMinHz, tuning.BassMaxHz, PlotColors.BassBand, 0.2f);
            Band(tuning.BassMaxHz, tuning.MidMaxHz, PlotColors.MidBand, 0.1f);
            Band(tuning.MidMaxHz, tuning.HighMaxHz, PlotColors.HighBand, 0.1f);
            for (float db = MaxDb - 20; db > MinDb; db -= 20) r.HLine((int)MathF.Round(YForDb(db)), PlotColors.Grid);
            foreach (double hz in Grid) r.VSpan((int)Math.Round(XForHz(hz, r.Width)), 0, r.Height - 1, PlotColors.Grid);
            r.Pixels.CopyTo(background, 0);
            backgroundTuning = tuning;
        }

        int bins = snapshot.SpectrumLength;
        if (bins < 2 || snapshot.AnalysisSampleRate <= 0) return;
        double binHz = snapshot.AnalysisSampleRate / (double)((bins - 1) * 2);
        float previousY = float.NaN;
        for (int x = 0; x < r.Width; x++)
        {
            float magnitude = Magnitude(snapshot.Spectrum, bins, HzForX(x, r.Width) / binHz, HzForX(x + 1, r.Width) / binHz);
            if (float.IsNaN(magnitude))
            {
                previousY = float.NaN;
                continue;
            }
            float y = YForDb(20 * MathF.Log10(magnitude + Floor));
            r.VSpan(x, (int)MathF.Round(y), r.Height - 1, PlotColors.SpectrumFill, 1, 0.35f);
            if (!float.IsNaN(previousY)) r.Line(x - 1, previousY, x, y, PlotColors.Spectrum, thickness);
            previousY = y;
        }
    }

    private float YForDb(float db) => (Raster.Height - 1) * (MaxDb - Math.Clamp(db, MinDb, MaxDb)) / (MaxDb - MinDb);

    // The column covering bins k0..k1 (fractional): the largest bin inside it, or between bins (low frequencies, where
    // one bin spans several columns) the magnitude interpolated at its center. NaN beyond Nyquist.
    private static float Magnitude(float[] spectrum, int bins, double k0, double k1)
    {
        double center = (k0 + k1) / 2;
        if (center > bins - 1) return float.NaN;
        int first = (int)Math.Ceiling(k0), last = Math.Min((int)Math.Floor(k1), bins - 1);
        if (last >= first)
        {
            float max = 0;
            for (int k = first; k <= last; k++) max = MathF.Max(max, Finite(spectrum[k]));
            return max;
        }
        int below = Math.Clamp((int)Math.Floor(center), 0, bins - 1), above = Math.Min(below + 1, bins - 1);
        float t = (float)(center - below);
        return Finite(spectrum[below]) + (Finite(spectrum[above]) - Finite(spectrum[below])) * t;
    }

    private static float Finite(float value) => float.IsFinite(value) && value > 0 ? value : 0;

    private void Band(float fromHz, float toHz, int color, float opacity)
    {
        int x0 = (int)Math.Round(XForHz(fromHz, Raster.Width)), x1 = (int)Math.Round(XForHz(toHz, Raster.Width));
        Raster.FillRect(x0, 0, x1, Raster.Height, color, opacity);
        Raster.VSpan(x0, 0, Raster.Height - 1, color, 1, 0.7f);
        Raster.VSpan(x1, 0, Raster.Height - 1, color, 1, 0.7f);
    }
}

/// <summary>
/// The time axis the scrolling plots share: each history entry's age, from the frame time of every <c>Process</c> call
/// (the analyzer runs at the overlay's frame rate, 10 Hz while nothing is on screen, so entries aren't evenly spaced).
/// UI thread; allocates nothing after construction.
/// </summary>
internal sealed class TimeAxis
{
    private readonly float[] ages = new float[AnalyzerProbe.HistoryLength];

    /// <summary>The first valid entry.</summary>
    public int First { get; private set; } = AnalyzerProbe.HistoryLength;

    /// <summary>Seconds the plots span (the age of the oldest valid entry, at least 1 s).</summary>
    public float SpanSeconds { get; private set; } = 1;

    /// <summary>Analyzer frames in the last second.</summary>
    public float FramesPerSecond { get; private set; }

    /// <summary>Recomputes the ages from a snapshot.</summary>
    public void Update(AnalyzerSnapshot snapshot)
    {
        int n = AnalyzerProbe.HistoryLength;
        First = n - Math.Clamp(snapshot.HistoryCount, 0, n);
        float age = 0, covered = 0;
        int intervals = 0;
        for (int i = n - 1; i >= First; i--)
        {
            ages[i] = age;
            float dt = snapshot.Dt[i]; // entry i came this long after entry i - 1 (unknown for the first one)
            if (i > First && age + dt <= 1.0001f)
            {
                intervals++;
                covered = age + dt;
            }
            age += dt;
        }
        SpanSeconds = MathF.Max(1, First < n ? ages[First] : 0);
        FramesPerSecond = covered > 0 ? intervals / covered : 0;
    }

    /// <summary>Entry <paramref name="index"/>'s x across a plot <paramref name="width"/> pixels wide (newest at the right edge).</summary>
    public float X(int index, int width) => (width - 1) * (1 - ages[index] / SpanSeconds);

    /// <summary>The x of a moment <paramref name="secondsAgo"/> before the newest entry.</summary>
    public float XAt(float secondsAgo, int width) => (width - 1) * (1 - secondsAgo / SpanSeconds);
}

/// <summary>
/// A scrolling plot (doc 03 §4): Level, Bass and Beat on 0..1, or the bass flux against the beat threshold on a scale
/// that follows the flux (rising at once, falling slowly). Beats are marked as amber columns: a rise of Beat by more
/// than 0.1, the same onset the light engine reacts to. UI thread; drawing allocates nothing.
/// </summary>
internal sealed class TimelinePlot(PlotRaster raster, bool flux)
{
    private const float OnsetRise = 0.1f;
    private const float ScaleFall = 0.97f; // per drawn frame: about 1 s for the scale to halve at 30 Hz

    public PlotRaster Raster { get; } = raster;

    /// <summary>The flux plot's full scale, in flux units.</summary>
    public float Scale { get; private set; } = 1e-3f;

    /// <summary>Draws the snapshot over the shared time axis.</summary>
    public void Draw(AnalyzerSnapshot s, TimeAxis axis, int thickness)
    {
        PlotRaster r = Raster;
        r.Clear(PlotColors.Background);
        for (int q = 1; q < 4; q++) r.HLine((int)MathF.Round((r.Height - 1) * q / 4f), PlotColors.Grid);
        float tick = axis.SpanSeconds > 12 ? 5 : 1;
        for (float t = tick; t < axis.SpanSeconds; t += tick) r.VSpan((int)MathF.Round(axis.XAt(t, r.Width)), 0, r.Height - 1, PlotColors.Grid);

        int first = axis.First, n = AnalyzerProbe.HistoryLength;
        if (first >= n) return;
        for (int i = Math.Max(first, 1); i < n; i++)
            if (s.Beat[i] > s.Beat[i - 1] + OnsetRise)
                r.VSpan((int)MathF.Round(axis.X(i, r.Width)), 0, r.Height - 1, PlotColors.Beat, Math.Max(2, thickness), 0.3f);

        if (flux)
        {
            float peak = 0;
            for (int i = first; i < n; i++) peak = MathF.Max(peak, MathF.Max(Finite(s.Flux[i]), Finite(s.Threshold[i])));
            Scale = MathF.Max(MathF.Max(peak * 1.15f, Scale * ScaleFall), 1e-4f);
            Series(s.Threshold, axis, PlotColors.Threshold, thickness, Scale);
            Series(s.Flux, axis, PlotColors.Flux, thickness, Scale);
        }
        else
        {
            Series(s.Bass, axis, PlotColors.Bass, thickness, 1);
            Series(s.Level, axis, PlotColors.Level, thickness, 1);
            Series(s.Beat, axis, PlotColors.Beat, thickness, 1);
        }
    }

    private void Series(float[] values, TimeAxis axis, int color, int thickness, float scale)
    {
        PlotRaster r = Raster;
        float top = thickness, bottom = r.Height - 1 - thickness; // full scale stays visible
        float previousX = 0, previousY = 0;
        for (int i = axis.First; i < AnalyzerProbe.HistoryLength; i++)
        {
            float x = axis.X(i, r.Width);
            float y = bottom - (bottom - top) * Math.Clamp(Finite(values[i]) / scale, 0, 1);
            if (i > axis.First) r.Line(previousX, previousY, x, y, color, thickness);
            previousX = x;
            previousY = y;
        }
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
}
