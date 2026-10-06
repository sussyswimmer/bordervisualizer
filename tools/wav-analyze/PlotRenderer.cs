using System.Globalization;
using Rimlight.AudioTools;
using SkiaSharp;

namespace Rimlight.WavAnalyze;

/// <summary>What to put in the plot.</summary>
internal sealed record PlotOptions
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    /// <summary>First plotted second; null = the start.</summary>
    public double? From { get; init; }
    /// <summary>Last plotted second; null = the end of the audio.</summary>
    public double? To { get; init; }
    /// <summary>Image width in pixels; null = 80 px per plotted second within the allowed range.</summary>
    public int? Width { get; init; }
}

/// <summary>
/// Draws an offline run as a PNG: four panels on one time axis (small multiples, one y-scale each): Level and Bass,
/// the Beat pulse, bass flux against the beat threshold with the detected beats marked, and the tempo estimate.
/// Sustained silence (IsSilent) is shaded in every panel.
/// </summary>
internal static class PlotRenderer
{
    public const int MinWidth = 900;
    public const int MaxWidth = 12000;
    private const int DefaultMinWidth = 1200;
    private const float PixelsPerSecond = 80;

    private const float Left = 76, Right = 28, PanelTitle = 30, PanelGap = 14, AxisBottom = 52;

    private static readonly SKColor Surface = SKColor.Parse("#fcfcfb");
    private static readonly SKColor Ink = SKColor.Parse("#0b0b0b");
    private static readonly SKColor InkSecondary = SKColor.Parse("#52514e");
    private static readonly SKColor Grid = SKColor.Parse("#e6e5e0");
    private static readonly SKColor Axis = SKColor.Parse("#b9b8b1");
    private static readonly SKColor Silence = SKColor.Parse("#e9e8e3");
    private static readonly SKColor LevelColor = SKColor.Parse("#2a78d6");
    private static readonly SKColor BassColor = SKColor.Parse("#eb6834");
    private static readonly SKColor BeatColor = SKColor.Parse("#4a3aa7");
    private static readonly SKColor FluxColor = SKColor.Parse("#1baf7a");
    private static readonly SKColor ThresholdColor = SKColor.Parse("#52514e");

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static (int Width, int Height) Save(string path, AnalysisResult result, PlotOptions options)
    {
        using SKData png = Render(result, options, out int width, out int height);
        using FileStream file = File.Create(path);
        png.SaveTo(file);
        return (width, height);
    }

    public static SKData Render(AnalysisResult result, PlotOptions options, out int width, out int height)
    {
        double end = result.AudioSeconds > 0 ? result.AudioSeconds : result.Frames.Count > 0 ? result.Frames[^1].Time : 1;
        double from = Math.Clamp(options.From ?? 0, 0, end);
        double to = Math.Clamp(options.To ?? end, from, end);
        if (to - from < 1e-3) to = from + 1e-3;
        width = options.Width ?? (int)Math.Clamp(Math.Round((to - from) * PixelsPerSecond + Left + Right), DefaultMinWidth, MaxWidth);

        using var fonts = new Fonts();
        using var ink = Fill(Ink);
        using var secondary = Fill(InkSecondary);
        float plotWidth = width - Left - Right;
        List<string> title = Wrap(options.Title, fonts.Title, width - Left - Right, 2);
        List<string> subtitle = Wrap(options.Subtitle, fonts.Small, width - Left - Right, 3);
        float subtitleTop = 30 + (title.Count - 1) * 22 + 22;
        float header = subtitleTop + (subtitle.Count - 1) * 18 + 24;

        List<AnalysisFrame> frames = Visible(result.Frames, from, to);
        var panels = BuildPanels(frames, result.BeatTimes, from, to);
        height = (int)Math.Ceiling(header + panels.Sum(p => PanelTitle + p.Height + PanelGap) + AxisBottom);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(Surface);

        for (int i = 0; i < title.Count; i++) canvas.DrawText(title[i], Left, 30 + i * 22, SKTextAlign.Left, fonts.Title, ink);
        for (int i = 0; i < subtitle.Count; i++) canvas.DrawText(subtitle[i], Left, subtitleTop + i * 18, SKTextAlign.Left, fonts.Small, secondary);

        float top = header;
        var x = new Func<double, float>(t => (float)(Left + (t - from) / (to - from) * plotWidth));
        (double step, Func<double, string> label, string axisTitle) = TimeTicks(to - from, plotWidth);
        for (int p = 0; p < panels.Count; p++)
        {
            Panel panel = panels[p];
            var rect = new SKRect(Left, top + PanelTitle, Left + plotWidth, top + PanelTitle + panel.Height);
            DrawPanel(canvas, fonts, ink, secondary, panel, rect, frames, x, from, to, step);
            top = rect.Bottom + PanelGap;
            if (p == panels.Count - 1)
            {
                for (double t = Math.Ceiling(from / step) * step; t <= to + 1e-9; t += step)
                    canvas.DrawText(label(t), x(t), rect.Bottom + 18, SKTextAlign.Center, fonts.Small, secondary);
                canvas.DrawText(axisTitle, Left + plotWidth / 2, rect.Bottom + 40, SKTextAlign.Center, fonts.Small, secondary);
            }
        }

        using SKImage image = surface.Snapshot();
        return image.Encode(SKEncodedImageFormat.Png, 100);
    }

    private sealed record Series(string Name, SKColor Color, Func<AnalysisFrame, float> Value, bool GapAtZero = false, float StrokeWidth = 1.6f);

    private sealed record Panel(string Title, string? Note, float Height, double YMin, double YMax, double YStep, string YFormat,
        IReadOnlyList<Series> Series, IReadOnlyList<double>? Beats, bool ShowSilenceKey);

    private static List<Panel> BuildPanels(List<AnalysisFrame> frames, IReadOnlyList<double> beatTimes, double from, double to)
    {
        var beats = beatTimes.Where(t => t >= from && t <= to).ToList();
        bool anySilent = frames.Any(f => f.Features.IsSilent);

        // Flux is spiky and the threshold starts high while the detector warms up: scale to the 99.5th percentile of
        // both, so typical kicks fill the panel, and clip the rare spikes.
        var flux = frames.SelectMany(f => new[] { (double)f.Flux, f.Threshold }).Where(double.IsFinite).OrderBy(v => v).ToList();
        double fluxMax = flux.Count == 0 ? 0 : flux[(int)Math.Min(flux.Count - 1, Math.Floor(flux.Count * 0.995))] * 1.15;
        if (!(fluxMax > 0)) fluxMax = 1;
        bool clipped = flux.Count > 0 && flux[^1] > fluxMax;
        double fluxStep = NiceStep(fluxMax / 3);

        var bpms = frames.Where(f => f.Bpm > 0).Select(f => (double)f.Bpm).ToList();
        double bpmMin = bpms.Count == 0 ? 0 : Math.Floor((bpms.Min() - 5) / 10) * 10;
        double bpmMax = bpms.Count == 0 ? 200 : Math.Ceiling((bpms.Max() + 5) / 10) * 10;
        bpmMin = Math.Max(0, bpmMin);

        return
        [
            new Panel("Level and Bass (0–1, what the light sees)", null, 150, 0, 1, 0.5, "0.0",
                [new Series("Level", LevelColor, f => f.Features.Level), new Series("Bass", BassColor, f => f.Features.Bass)], null, anySilent),
            new Panel($"Beat pulse: {beats.Count} beats", null, 110, 0, 1, 0.5, "0.0",
                [new Series("Beat", BeatColor, f => f.Features.Beat)], null, false),
            new Panel("Bass spectral flux and beat threshold",
                clipped ? $"a beat needs flux above the threshold; clipped above {fluxMax.ToString("G3", C)}" : "a beat needs flux above the threshold", 170, 0, fluxMax, fluxStep, "G3",
                [new Series("flux", FluxColor, f => f.Flux), new Series("threshold", ThresholdColor, f => f.Threshold)], beats, false),
            new Panel("Estimated tempo (BPM)", bpms.Count == 0 ? "no estimate yet" : null, 100, bpmMin, bpmMax, NiceStep((bpmMax - bpmMin) / 3), "0",
                [new Series("BPM", BeatColor, f => f.Bpm, GapAtZero: true)], null, false),
        ];
    }

    private static void DrawPanel(SKCanvas canvas, Fonts fonts, SKPaint ink, SKPaint secondary, Panel panel, SKRect rect, List<AnalysisFrame> frames,
        Func<double, float> x, double from, double to, double timeStep)
    {
        float Y(double v) => (float)(rect.Bottom - (v - panel.YMin) / (panel.YMax - panel.YMin) * rect.Height);

        // Title (left) and legend (right) share the row above the plot; the note is dropped if it would collide.
        float titleY = rect.Top - 10;
        canvas.DrawText(panel.Title, rect.Left, titleY, SKTextAlign.Left, fonts.Label, ink);
        float legendLeft = DrawLegend(canvas, fonts, secondary, panel, rect.Right, titleY);
        if (panel.Note is not null)
        {
            string note = $"  ({panel.Note})";
            float noteLeft = rect.Left + fonts.Label.MeasureText(panel.Title);
            if (noteLeft + fonts.Small.MeasureText(note) + 16 < legendLeft)
                canvas.DrawText(note, noteLeft, titleY, SKTextAlign.Left, fonts.Small, secondary);
        }

        canvas.Save();
        canvas.ClipRect(rect);

        // Sustained silence: a wash behind everything.
        using (var wash = Fill(Silence))
        {
            for (int i = 0; i < frames.Count; i++)
            {
                if (!frames[i].Features.IsSilent) continue;
                int j = i;
                while (j + 1 < frames.Count && frames[j + 1].Features.IsSilent) j++;
                double start = i > 0 ? frames[i - 1].Time : frames[i].Time;
                canvas.DrawRect(SKRect.Create(x(start), rect.Top, Math.Max(1, x(frames[j].Time) - x(start)), rect.Height), wash);
                i = j;
            }
        }

        using (var grid = Stroke(Grid, 1))
        {
            for (double t = Math.Ceiling(from / timeStep) * timeStep; t <= to + 1e-9; t += timeStep)
                canvas.DrawLine(Snap(x(t)), rect.Top, Snap(x(t)), rect.Bottom, grid);
            for (double v = panel.YMin; v <= panel.YMax + 1e-9; v += panel.YStep)
                canvas.DrawLine(rect.Left, Snap(Y(v)), rect.Right, Snap(Y(v)), grid);
        }

        foreach (Series series in panel.Series)
        {
            using var path = new SKPath();
            bool drawing = false;
            foreach (AnalysisFrame frame in frames)
            {
                float value = series.Value(frame);
                if (!float.IsFinite(value) || (series.GapAtZero && value <= 0))
                {
                    drawing = false;
                    continue;
                }
                float px = x(frame.Time), py = Math.Clamp(Y(value), rect.Top - 4, rect.Bottom + 4);
                if (drawing) path.LineTo(px, py);
                else path.MoveTo(px, py);
                drawing = true;
            }
            using var stroke = Stroke(series.Color, series.StrokeWidth);
            stroke.StrokeJoin = SKStrokeJoin.Round;
            stroke.StrokeCap = SKStrokeCap.Round;
            canvas.DrawPath(path, stroke);
        }

        if (panel.Beats is not null)
        {
            using var marker = Fill(BeatColor);
            foreach (double beat in panel.Beats)
            {
                using SKPath triangle = Triangle(x(beat), rect.Top + 1);
                canvas.DrawPath(triangle, marker);
            }
        }
        canvas.Restore();

        using (var axis = Stroke(Axis, 1)) canvas.DrawLine(rect.Left, Snap(rect.Bottom), rect.Right, Snap(rect.Bottom), axis);
        for (double v = panel.YMin; v <= panel.YMax + 1e-9; v += panel.YStep)
            canvas.DrawText(v.ToString(panel.YFormat, C), rect.Left - 8, Y(v) + 4, SKTextAlign.Right, fonts.Small, secondary);
    }

    // Draws the legend right-aligned and returns its left edge.
    private static float DrawLegend(SKCanvas canvas, Fonts fonts, SKPaint secondary, Panel panel, float right, float baseline)
    {
        var items = new List<(string Label, Action<float> Key)>();
        bool showSeries = panel.Series.Count > 1 || panel.Beats is not null;
        if (showSeries)
            foreach (Series series in panel.Series)
                items.Add((series.Name, kx =>
                {
                    using var stroke = Stroke(series.Color, 2);
                    stroke.StrokeCap = SKStrokeCap.Round;
                    canvas.DrawLine(kx, baseline - 4, kx + 18, baseline - 4, stroke);
                }));
        if (panel.Beats is not null)
            items.Add(("detected beat", kx =>
            {
                using var fill = Fill(BeatColor);
                using SKPath triangle = Triangle(kx + 9, baseline - 9);
                canvas.DrawPath(triangle, fill);
            }));
        if (panel.ShowSilenceKey)
            items.Add(("IsSilent", kx =>
            {
                using var fill = Fill(Silence);
                using var edge = Stroke(Axis, 1);
                canvas.DrawRect(SKRect.Create(kx, baseline - 10, 18, 10), fill);
                canvas.DrawRect(SKRect.Create(kx + 0.5f, baseline - 9.5f, 17, 9), edge);
            }));

        float cursor = right;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            float textWidth = fonts.Small.MeasureText(items[i].Label);
            cursor -= textWidth;
            canvas.DrawText(items[i].Label, cursor, baseline, SKTextAlign.Left, fonts.Small, secondary);
            cursor -= 24;
            items[i].Key(cursor);
            cursor -= 18;
        }
        return cursor;
    }

    // A downward triangle at least 8 px wide, its tip at (x, top + 7).
    private static SKPath Triangle(float x, float top)
    {
        var path = new SKPath();
        path.MoveTo(x - 4.5f, top);
        path.LineTo(x + 4.5f, top);
        path.LineTo(x, top + 7);
        path.Close();
        return path;
    }

    private static List<AnalysisFrame> Visible(IReadOnlyList<AnalysisFrame> frames, double from, double to)
    {
        // One frame either side, so lines reach the edges.
        var visible = new List<AnalysisFrame>();
        for (int i = 0; i < frames.Count; i++)
        {
            bool inside = frames[i].Time >= from && frames[i].Time <= to;
            bool edge = (i + 1 < frames.Count && frames[i + 1].Time >= from && frames[i].Time < from)
                || (i > 0 && frames[i - 1].Time <= to && frames[i].Time > to);
            if (inside || edge) visible.Add(frames[i]);
        }
        return visible;
    }

    // Tick spacing of at least ~90 px; labels as seconds, or m:ss past two minutes.
    private static (double Step, Func<double, string> Label, string AxisTitle) TimeTicks(double seconds, float plotWidth)
    {
        double[] steps = [0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600];
        double step = steps.FirstOrDefault(s => s / seconds * plotWidth >= 90, steps[^1]);
        if (step < 1) return (step, t => t.ToString("0.0", C), "time (s)");
        if (seconds > 120) return (step, t => $"{(long)Math.Round(t) / 60}:{(long)Math.Round(t) % 60:00}", "time (m:ss)");
        return (step, t => t.ToString("0", C), "time (s)");
    }

    private static double NiceStep(double rough)
    {
        if (!(rough > 0)) return 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double fraction = rough / magnitude;
        return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * magnitude;
    }

    private static List<string> Wrap(string text, SKFont font, float maxWidth, int maxLines)
    {
        var lines = new List<string>();
        string current = "";
        foreach (string word in text.Split(' '))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) <= maxWidth || current.Length == 0) current = candidate;
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        if (current.Length > 0) lines.Add(current);
        if (lines.Count > maxLines)
        {
            lines.RemoveRange(maxLines, lines.Count - maxLines);
            lines[^1] += " …";
        }
        return lines;
    }

    private static float Snap(float v) => MathF.Floor(v) + 0.5f;

    private static SKPaint Fill(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    private static SKPaint Stroke(SKColor color, float width) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };

    // Prefers the platform UI font. On Linux the SkiaSharp build without fontconfig scans /usr/share/fonts; with no
    // fonts installed text is simply not drawn and the plot is still written.
    private sealed class Fonts : IDisposable
    {
        private static readonly string[] Families = ["Segoe UI", "Inter", "DejaVu Sans", "Liberation Sans", "Arial", "Helvetica"];
        private readonly SKTypeface regular, bold;

        public Fonts()
        {
            regular = Match(SKFontStyle.Normal);
            bold = Match(SKFontStyle.Bold);
            Title = new SKFont(bold, 17) { Subpixel = true, Edging = SKFontEdging.Antialias };
            Label = new SKFont(bold, 13.5f) { Subpixel = true, Edging = SKFontEdging.Antialias };
            Small = new SKFont(regular, 12.5f) { Subpixel = true, Edging = SKFontEdging.Antialias };
        }

        public SKFont Title { get; }
        public SKFont Label { get; }
        public SKFont Small { get; }

        public void Dispose()
        {
            Title.Dispose();
            Label.Dispose();
            Small.Dispose();
            if (regular != SKTypeface.Default) regular.Dispose();
            if (bold != SKTypeface.Default) bold.Dispose();
        }

        private static SKTypeface Match(SKFontStyle style)
        {
            foreach (string family in Families)
            {
                SKTypeface? typeface = SKFontManager.Default.MatchFamily(family, style);
                if (typeface is not null && typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase)) return typeface;
                typeface?.Dispose();
            }
            return SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
        }
    }
}
