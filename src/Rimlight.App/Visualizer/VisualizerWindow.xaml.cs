using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Wpf.Ui.Appearance;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using ImageIcon = Wpf.Ui.Controls.ImageIcon;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace Rimlight.App.Visualizer;

/// <summary>
/// The debug visualizer (doc 03 §4, <c>--debug-visualizer</c>): a compact, always-on-top Fluent window that shows what
/// the overlay's analyzer hears, live, and makes every <see cref="AudioTuning"/> parameter editable, with "Copy params
/// as JSON" in H-015's format. It reads only the snapshots the overlay thread hands it (<see cref="VisualizerFeed"/>),
/// never the analyzer, and runs only while it is shown and not minimized: then the analyzer gets audio and the full
/// frame rate in every mode, and its plots are redrawn about 30 times a second into reused bitmaps. UI thread only.
/// </summary>
internal partial class VisualizerWindow : FluentWindow
{
    private const double FrameSeconds = 1.0 / 30;
    private const double StaleSeconds = 0.5;      // no snapshot for this long: the overlay isn't calling the analyzer
    private const int TicksPerTextUpdate = 3;     // numbers change 10 times a second, so they stay readable
    private const int WmThemeChanged = 0x031A;
    private const int WmSysColorChange = 0x0015;
    private const int WmSettingChange = 0x001A;

    // Frequency labels on the spectrum (doc 03 §4: a log axis from about 20 Hz to 20 kHz).
    private static readonly (float Hz, string Text)[] FrequencyLabels =
        [(20, "20"), (50, "50"), (100, "100"), (200, "200"), (500, "500"), (1000, "1k"), (2000, "2k"), (5000, "5k"), (10000, "10k"), (20000, "20k Hz")];

    private readonly AppController app;
    private readonly VisualizerFeed feed = new();
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private readonly SpectrumPlot spectrumPlot = new();
    private readonly HistoryPlot featurePlot = new();
    private readonly HistoryPlot fluxPlot = new();
    private readonly PlotSurface spectrumSurface;
    private readonly PlotSurface featureSurface;
    private readonly PlotSurface fluxSurface;
    private readonly List<ParameterRow> rows = [];
    private readonly TextBlock[] bandLabels = new TextBlock[3];
    private PlotColors colors = PlotColors.Dark;
    private IDisposable? observation;
    private AnalyzerSnapshot? latest;
    private AudioTuning draft = new(); // what the sliders show: the tuning in effect, or the user's edits on top of it
    private bool hasDraft;
    private int postedRequest;
    private bool refreshing;
    private bool subscribed;
    private bool themeQueued;
    private int ticks;
    private double lastSnapshotAt;
    private long shownSequence;
    private AudioTuning? labeledTuning; // the tuning the band labels were placed for
    private string shownStatus = "";
    private string shownTime = "";
    private string shownScale = "";
    private (string?, string?, int, int, long, bool) shownDevice;

    /// <summary>Builds the window; <see cref="Present"/> shows it.</summary>
    /// <param name="app">The app, for the analyzer feed and the Sensitivity setting.</param>
    public VisualizerWindow(AppController app)
    {
        this.app = app;
        InitializeComponent();
        Title = $"{AppInfo.Name} audio visualizer";
        WindowTitleBar.Title = Title;
        if (AboutPage.LoadIcon(32) is { } icon) WindowTitleBar.Icon = new ImageIcon { Source = icon, Width = 16, Height = 16 };
        FitToWorkArea();

        spectrumSurface = new PlotSurface(SpectrumImage, SpectrumHost);
        featureSurface = new PlotSurface(FeatureImage, FeatureHost);
        fluxSurface = new PlotSurface(FluxImage, FluxHost);
        BuildTuningRows();
        BuildLabels();

        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(FrameSeconds) };
        timer.Tick += (_, _) => Tick();
        SpectrumHost.SizeChanged += (_, _) => PlaceSpectrumLabels();

        Loaded += OnLoaded;
        IsVisibleChanged += (_, _) => UpdateActivity();
        StateChanged += (_, _) => UpdateActivity();
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += OnClosed;
    }

    /// <summary>Shows the window, or brings it to the front.</summary>
    public void Present()
    {
        UiTheme.Sync();
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // WPF's CenterScreen doesn't keep a window inside the work area: one taller than it (a 1080p laptop at 150 % leaves
    // about 670 DIPs) would open with its title bar above the screen. So the size is capped like the settings window's,
    // and the window sits at the work area's right edge instead of in the middle, where it would hide a Settings window
    // opened meanwhile (centered, and never above an always-on-top window). Like the settings window, this uses
    // SystemParameters.WorkArea: the primary display's.
    private void FitToWorkArea()
    {
        const double Gap = 12;
        Rect area = SystemParameters.WorkArea;
        if (!(area.Width > 0 && area.Height > 0)) return; // Windows picks the place
        double width = Math.Min(Width, area.Width * 0.95), height = Math.Min(Height, area.Height * 0.95);
        MinWidth = Math.Min(MinWidth, width); // WPF would grow it back to the minimum
        MinHeight = Math.Min(MinHeight, height);
        Width = width;
        Height = height;
        Left = area.Right - width - Math.Min(Gap, (area.Width - width) / 2);
        Top = area.Top + (area.Height - height) / 2;
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Top-level windows get theme broadcasts; the settings window may be closed, so this one follows them itself.
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProcedure);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme(UiTheme.Current);
        ApplicationThemeManager.Changed += OnThemeChanged;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ApplicationThemeManager.Changed -= OnThemeChanged; // a static event: it would keep the window alive
        Subscribe(false);
        StopObserving();
    }

    // Esc closes the window (nothing in it uses the key).
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        Close();
    }

    // Runs only while it can be seen: then the overlay thread feeds it and the capture stays open; otherwise everything
    // goes back to how it idles (doc 02).
    private void UpdateActivity()
    {
        bool shown = IsVisible && WindowState != WindowState.Minimized;
        Subscribe(shown);
        if (shown && observation is null)
        {
            observation = app.ObserveAnalyzer(feed);
            if (observation is null)
            {
                ShowStatus("The glow didn't start, so there is nothing to analyze. Open logs folder in the tray menu shows why.");
                return;
            }
            clock.Restart();
            lastSnapshotAt = 0;
            timer.Start();
            Tick();
        }
        else if (!shown && observation is not null)
        {
            StopObserving();
        }
    }

    // Detaches from the overlay thread: no more snapshots, and the analyzer and the capture go back to how they idle.
    private void StopObserving()
    {
        timer.Stop();
        clock.Stop();
        observation?.Dispose();
        observation = null;
    }

    private void Subscribe(bool on)
    {
        if (on == subscribed) return;
        subscribed = on;
        if (!on)
        {
            app.SettingsService.Changed -= OnSettingsChanged;
            return;
        }
        app.SettingsService.Changed += OnSettingsChanged;
        // A change made while the window was minimized went unseen; the Sensitivity row catches up with it.
        OnSettingsChanged(app.SettingsService.Current);
    }

    // Sensitivity is a setting (H-007): its slider follows the setting, wherever it was changed.
    private void OnSettingsChanged(Settings settings)
    {
        if (!hasDraft) return;
        draft = draft with { Sensitivity = settings.Sensitivity };
        RefreshRows(onlySensitivity: true);
    }

    private void Tick()
    {
        try
        {
            double now = clock.Elapsed.TotalSeconds;
            if (feed.TryTake(out AnalyzerSnapshot snapshot))
            {
                latest = snapshot;
                lastSnapshotAt = now;
                Draw(snapshot);
                if (ticks++ % TicksPerTextUpdate == 0) ShowNumbers(snapshot);
                SyncTuning(snapshot);
                ShowStatus("");
            }
            else if (now - lastSnapshotAt > StaleSeconds)
            {
                ShowStatus(latest is null
                    ? "Waiting for the analyzer…"
                    : "No new frames: no display shows the glow right now (Displays in Settings).");
            }
        }
        catch (Exception exception)
        {
            // A plot must never take the window down; the log has the details. A window that no longer draws mustn't
            // keep the analyzer at the full rate and the capture open either: it detaches until it is shown again
            // (restored or reopened), which tries again.
            Trace.WriteLine($"[Visualizer] Drawing failed and stopped: {exception}");
            StopObserving();
            ShowStatus("The visualizer stopped after an error. Open logs folder in the tray menu shows why.");
        }
    }

    private void Draw(AnalyzerSnapshot snapshot)
    {
        if (spectrumSurface.Begin() is { } spectrum)
        {
            spectrumPlot.Draw(spectrum, snapshot, colors);
            spectrumSurface.Present();
        }
        if (featureSurface.Begin() is { } features)
        {
            featurePlot.DrawFeatures(features, snapshot, colors);
            featureSurface.Present();
        }
        if (fluxSurface.Begin() is { } flux)
        {
            fluxPlot.DrawFlux(flux, snapshot, colors);
            fluxSurface.Present();
        }
        if (!ReferenceEquals(snapshot.Tuning, labeledTuning)) PlaceSpectrumLabels();
    }

    // The numbers, the device line and the axis captions; text is only replaced when it changes.
    private void ShowNumbers(AnalyzerSnapshot snapshot)
    {
        if (snapshot.Sequence == shownSequence) return;
        shownSequence = snapshot.Sequence;
        CultureInfo culture = CultureInfo.CurrentCulture;
        AudioFeatures features = snapshot.Features;
        SetText(BeatCountText, snapshot.BeatCount.ToString(culture));
        SetText(TempoText, snapshot.EstimatedBpm > 0 ? snapshot.EstimatedBpm.ToString("0.0", culture) : "–"); // the caption says BPM
        SetText(LevelText, features.Level.ToString("0.00", culture));
        SetText(BassText, features.Bass.ToString("0.00", culture));
        SetText(BeatText, features.Beat.ToString("0.00", culture));
        SetText(SilenceText, features.IsSilent ? "Silent" : "Sound");
        var device = (snapshot.DeviceName, snapshot.Format, snapshot.CaptureSampleRate, snapshot.AnalysisSampleRate, snapshot.DroppedSamples, snapshot.Demo);
        if (device != shownDevice)
        {
            shownDevice = device;
            DeviceText.Text = DescribeDevice(snapshot);
        }

        string time = string.Create(culture,
            $"Time axis: the last {featurePlot.SpanSeconds:0.0} s, {snapshot.HistoryLength} analyzer calls ({CallsPerSecond(snapshot):0} a second); a line every second.");
        if (time != shownTime) TimeText.Text = shownTime = time;
        string scale = string.Create(culture, $"flux and threshold, 0 to {fluxPlot.FluxScale:0.###}");
        if (scale != shownScale) FluxScaleText.Text = shownScale = scale;
    }

    // e.g. "Speakers (Realtek(R) Audio) · 48000 Hz, 2 ch, 32-bit float · analyzed at 48 kHz · 0 samples dropped".
    private static string DescribeDevice(AnalyzerSnapshot snapshot)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        string demo = snapshot.Demo ? "Demo mode: the glow plays a synthetic beat; this is the real audio. " : "";
        if (snapshot.CaptureSampleRate <= 0) return demo + "No output device is captured (none, or the capture is starting).";
        string rate = snapshot.AnalysisSampleRate != snapshot.CaptureSampleRate
            ? string.Create(culture, $"decimated to {snapshot.AnalysisSampleRate / 1000.0:0.#} kHz for analysis")
            : string.Create(culture, $"analyzed at {snapshot.AnalysisSampleRate / 1000.0:0.#} kHz");
        return string.Create(culture,
            $"{demo}{snapshot.DeviceName} · {snapshot.Format} · {rate} · {snapshot.DroppedSamples:N0} samples dropped");
    }

    private static double CallsPerSecond(AnalyzerSnapshot snapshot)
    {
        double seconds = 0;
        int calls = 0;
        for (int i = 0; i < snapshot.HistoryLength; i++)
        {
            if (!(snapshot.FrameSeconds[i] > 0)) continue;
            seconds += snapshot.FrameSeconds[i];
            calls++;
        }
        return seconds > 0 ? calls / seconds : 0;
    }

    // Keeps the sliders on the tuning in effect, and reports a refused one (doc 03 §4, H-004).
    private void SyncTuning(AnalyzerSnapshot snapshot)
    {
        if (!hasDraft)
        {
            hasDraft = true;
            TuningPanel.IsEnabled = ResetButton.IsEnabled = CopyButton.IsEnabled = PasteButton.IsEnabled = true;
            // Sensitivity from the setting (H-007): a change made just before this snapshot reaches the analyzer a
            // frame later.
            draft = snapshot.Tuning with { Sensitivity = app.SettingsService.Current.Sensitivity };
            RefreshRows(onlySensitivity: false);
            return;
        }
        if (snapshot.TuningRequest != postedRequest) return; // the newest edit is still on its way
        if (snapshot.TuningError is { } error)
        {
            SetMessage(TuningMessage, $"Not applied: {error} The analyzer keeps its previous values until the sliders make a valid set.");
            return;
        }
        SetMessage(TuningMessage, "");
        // Sensitivity follows the setting (OnSettingsChanged), which reaches the analyzer a frame later; the rest
        // follows the analyzer.
        AudioTuning effect = snapshot.Tuning.Sensitivity == draft.Sensitivity ? snapshot.Tuning : snapshot.Tuning with { Sensitivity = draft.Sensitivity };
        if (effect == draft) return;
        draft = effect;
        RefreshRows(onlySensitivity: false);
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        draft = new AudioTuning { Sensitivity = app.SettingsService.Current.Sensitivity };
        postedRequest = feed.PostTuning(draft);
        RefreshRows(onlySensitivity: false);
        SetMessage(TuningInfo, "Defaults requested. Sensitivity is a setting and kept its value.");
    }

    // H-015: the tuning in effect (the live analyzer.Tuning), every property, as wav-analyze --tuning reads it.
    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (latest is null) return;
        string json = TuningParameters.ToJson(latest.Tuning);
        try
        {
            Clipboard.SetText(json);
            SetMessage(TuningInfo, $"Copied the tuning in effect ({TuningParameters.All.Count} parameters) as JSON.");
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            // Another program holds the clipboard.
            SetMessage(TuningInfo, "The clipboard is busy. Try again.");
            Trace.WriteLine($"[Visualizer] Copying failed: {exception.Message}");
        }
    }

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            SetMessage(TuningInfo, "The clipboard is busy. Try again.");
            Trace.WriteLine($"[Visualizer] Pasting failed: {exception.Message}");
            return;
        }
        if (!TuningParameters.TryParse(text, out AudioTuning pasted, out bool namesSensitivity, out string error))
        {
            SetMessage(TuningMessage, $"The clipboard doesn't hold tuning JSON: {error}");
            return;
        }
        // Sensitivity is the setting's (H-007): JSON that names it changes the setting, within the setting's range.
        if (namesSensitivity && !(pasted.Sensitivity >= 0.25f && pasted.Sensitivity <= 2f))
        {
            SetMessage(TuningMessage, "Not applied: Sensitivity must be from 0.25 to 2.");
            return;
        }
        // The whole tuning must pass before anything changes, the Sensitivity setting included. The analyzer checks a
        // tuning the same way when it is built, so a throwaway one tells (a one-off allocation, on a click).
        try
        {
            CoreFactory.CreateAnalyzer(pasted);
        }
        catch (ArgumentException exception)
        {
            SetMessage(TuningMessage, $"Not applied: {exception.Message} Nothing was changed.");
            return;
        }
        float sensitivity = app.SettingsService.Current.Sensitivity;
        if (namesSensitivity)
        {
            sensitivity = pasted.Sensitivity;
            app.SettingsService.Update(s => s with { Sensitivity = sensitivity });
        }
        draft = pasted with { Sensitivity = sensitivity };
        postedRequest = feed.PostTuning(draft);
        RefreshRows(onlySensitivity: false);
        SetMessage(TuningMessage, "");
        SetMessage(TuningInfo, "Pasted. Parameters the JSON leaves out took their defaults.");
    }

    private void OnSliderChanged(ParameterRow row)
    {
        if (refreshing || !hasDraft) return;
        double value = row.Parameter.Snap(row.Slider.Value);
        if (row.Parameter.Name == TuningParameters.SensitivityName)
        {
            // A setting: the overlay applies it to the analyzer with the next settings snapshot (H-007).
            float sensitivity = (float)value;
            app.SettingsService.Update(s => s with { Sensitivity = sensitivity });
            return; // OnSettingsChanged updates the readout
        }
        draft = row.Parameter.Write(draft, value);
        postedRequest = feed.PostTuning(draft);
        row.ShowValue(draft);
        SetMessage(TuningInfo, "");
    }

    private void RefreshRows(bool onlySensitivity)
    {
        refreshing = true;
        try
        {
            foreach (ParameterRow row in rows)
                if (!onlySensitivity || row.Parameter.Name == TuningParameters.SensitivityName) row.Show(draft);
        }
        finally
        {
            refreshing = false;
        }
    }

    private void BuildTuningRows()
    {
        string? group = null;
        foreach (TuningParameter parameter in TuningParameters.All)
        {
            if (parameter.Group != group)
            {
                group = parameter.Group;
                TuningPanel.Children.Add(new TextBlock
                {
                    Text = group,
                    Style = (Style)FindResource("SectionHeader"),
                    Margin = new Thickness(0, TuningPanel.Children.Count == 0 ? 4 : 14, 0, 6),
                });
            }
            var row = new ParameterRow(parameter, (Style)FindResource("TuningRow"), (Style)FindResource("ValueText"));
            row.Slider.ValueChanged += (_, _) => OnSliderChanged(row);
            rows.Add(row);
            TuningPanel.Children.Add(row.Root);
        }
    }

    // The frequency, dB and band labels over the spectrum, and the plots' legends.
    private void BuildLabels()
    {
        foreach ((float hz, string text) in FrequencyLabels) SpectrumLabels.Children.Add(Label(text, hz));
        for (int db = -20; db >= -80; db -= 20) SpectrumLabels.Children.Add(Label(string.Create(CultureInfo.CurrentCulture, $"{db} dB"), db));
        string[] bands = ["Bass", "Mid", "High"];
        for (int i = 0; i < bands.Length; i++)
        {
            bandLabels[i] = new TextBlock { Text = bands[i], Style = (Style)FindResource("AxisLabel"), FontWeight = FontWeights.SemiBold };
            SpectrumLabels.Children.Add(bandLabels[i]);
        }
        BuildLegends();
    }

    private TextBlock Label(string text, object tag) => new() { Text = text, Style = (Style)FindResource("AxisLabel"), Tag = tag };

    private void BuildLegends()
    {
        SpectrumLegend.Children.Clear();
        FeatureLegend.Children.Clear();
        FluxLegend.Children.Clear();
        SpectrumLegend.Children.Add(LegendItem("Bass band", colors.Bass));
        SpectrumLegend.Children.Add(LegendItem("Mid band", colors.Level));
        SpectrumLegend.Children.Add(LegendItem("High band", colors.Beat));
        FeatureLegend.Children.Add(LegendItem("Level", colors.Level));
        FeatureLegend.Children.Add(LegendItem("Bass", colors.Bass));
        FeatureLegend.Children.Add(LegendItem("Beat", colors.Beat));
        FluxLegend.Children.Add(LegendItem("Flux", colors.Flux));
        FluxLegend.Children.Add(LegendItem("Threshold", colors.Threshold));
        FluxLegend.Children.Add(LegendItem("Beat fired", colors.Beat));
    }

    private UIElement LegendItem(string text, uint rgb)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        item.Children.Add(new Rectangle
        {
            Width = 10,
            Height = 10,
            RadiusX = 2,
            RadiusY = 2,
            Fill = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        item.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("AxisLabel"), VerticalAlignment = VerticalAlignment.Center });
        return item;
    }

    // Labels sit in DIPs over the bitmap: frequencies along the bottom, dB at the left, band names over their bands.
    private void PlaceSpectrumLabels()
    {
        double width = SpectrumHost.ActualWidth, height = SpectrumHost.ActualHeight;
        if (width <= 0 || height <= 0) return;
        foreach (UIElement child in SpectrumLabels.Children)
        {
            if (child is not TextBlock label) continue;
            if (label.Tag is float hz)
            {
                double x = SpectrumPlot.XOf(hz, (float)width);
                Canvas.SetLeft(label, Math.Clamp(x + 3, 0, Math.Max(width - 44, 0)));
                Canvas.SetTop(label, height - 16);
            }
            else if (label.Tag is int db)
            {
                Canvas.SetLeft(label, 3);
                Canvas.SetTop(label, (SpectrumPlot.TopDb - db) / (SpectrumPlot.TopDb - SpectrumPlot.BottomDb) * height - 15);
            }
        }
        AudioTuning tuning = latest?.Tuning ?? draft;
        labeledTuning = latest?.Tuning;
        PlaceBand(bandLabels[0], tuning.BassMinHz, tuning.BassMaxHz, width);
        PlaceBand(bandLabels[1], tuning.BassMaxHz, tuning.MidMaxHz, width);
        PlaceBand(bandLabels[2], tuning.MidMaxHz, tuning.HighMaxHz, width);
    }

    private static void PlaceBand(TextBlock label, float fromHz, float toHz, double width)
    {
        double from = SpectrumPlot.XOf(fromHz, (float)width), to = SpectrumPlot.XOf(toHz, (float)width);
        Canvas.SetLeft(label, Math.Clamp((from + to) / 2 - 14, 0, Math.Max(width - 32, 0)));
        Canvas.SetTop(label, 4);
    }

    private void OnThemeChanged(ApplicationTheme theme, Color accent) => ApplyTheme(theme);

    // The plot colors and the window backdrop follow the app's theme; this window isn't the main window, which WPF-UI
    // restyles by itself.
    private void ApplyTheme(ApplicationTheme theme)
    {
        colors = theme switch
        {
            ApplicationTheme.Light => PlotColors.Light,
            ApplicationTheme.HighContrast => HighContrastColors(),
            _ => PlotColors.Dark,
        };
        BuildLegends();
        try
        {
            WindowBackgroundManager.UpdateBackground(this, theme, WindowBackdropType.Mica);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Visualizer] Applying the window backdrop failed: {exception.Message}");
        }
        if (latest is not null) Draw(latest);
    }

    // High contrast: the system's own colors; the numbers above the plots carry the same information as text.
    private static PlotColors HighContrastColors()
    {
        static uint Rgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);
        uint text = Rgb(SystemColors.WindowTextColor), highlight = Rgb(SystemColors.HighlightColor), hot = Rgb(SystemColors.HotTrackColor);
        return new PlotColors(text, highlight, hot, text, highlight, Rgb(SystemColors.GrayTextColor), highlight, hot);
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case WmThemeChanged or WmSysColorChange:
                QueueThemeSync();
                break;
            case WmSettingChange when lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet":
                QueueThemeSync(); // "Choose your app mode"
                break;
        }
        return IntPtr.Zero;
    }

    private void QueueThemeSync()
    {
        if (themeQueued) return;
        themeQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            themeQueued = false;
            UiTheme.Sync();
        });
    }

    private void ShowStatus(string text)
    {
        if (text == shownStatus) return;
        shownStatus = text;
        PageEdits.SetMessage(StatusText, text);
    }

    private static void SetMessage(TextBlock block, string text) => PageEdits.SetMessage(block, text);

    private static void SetText(TextBlock block, string text)
    {
        if (block.Text != text) block.Text = text;
    }

    // One slider row: label, readout and slider, with its parameter.
    private sealed class ParameterRow
    {
        private readonly TextBlock readout;

        public ParameterRow(TuningParameter parameter, Style rowStyle, Style valueStyle)
        {
            Parameter = parameter;
            readout = new TextBlock { Style = valueStyle };
            var label = new TextBlock { Text = parameter.Label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, ToolTip = parameter.Help };
            var header = new DockPanel();
            DockPanel.SetDock(readout, Dock.Right);
            header.Children.Add(readout);
            header.Children.Add(label);
            Slider = new Slider
            {
                Minimum = parameter.Minimum,
                Maximum = parameter.Maximum,
                SmallChange = parameter.Step,
                LargeChange = parameter.Step * Math.Max(1, Math.Round((parameter.Maximum - parameter.Minimum) / parameter.Step / 10)),
                TickFrequency = parameter.Step,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 2, 0, 0),
                ToolTip = parameter.Help,
            };
            AutomationProperties.SetName(Slider, parameter.Label);
            AutomationProperties.SetHelpText(Slider, parameter.Help);
            var panel = new StackPanel();
            panel.Children.Add(header);
            panel.Children.Add(Slider);
            Root = new Border { Style = rowStyle, Child = panel };
        }

        public TuningParameter Parameter { get; }

        public Slider Slider { get; }

        public Border Root { get; }

        // The slider (clamped to its range) and the exact value, which may lie between steps or outside the range
        // after a paste.
        public void Show(AudioTuning tuning)
        {
            double value = Parameter.Read(tuning);
            double clamped = Math.Clamp(double.IsFinite(value) ? value : Parameter.Minimum, Parameter.Minimum, Parameter.Maximum);
            if (Math.Abs(Slider.Value - clamped) > 1e-9) Slider.Value = clamped;
            ShowValue(tuning);
        }

        public void ShowValue(AudioTuning tuning)
        {
            string text = Parameter.Format(Parameter.Read(tuning));
            if (readout.Text != text) readout.Text = text;
            AutomationProperties.SetItemStatus(Slider, text);
        }
    }

    // A plot's bitmap at the device-pixel size of the element it fills, so lines stay sharp at every display scale.
    // Reallocated only when that size changes.
    private sealed class PlotSurface(Image image, FrameworkElement host)
    {
        private WriteableBitmap? bitmap;
        private PlotRaster? raster;

        public PlotRaster? Begin()
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(host);
            int width = (int)Math.Round(host.ActualWidth * dpi.DpiScaleX);
            int height = (int)Math.Round(host.ActualHeight * dpi.DpiScaleY);
            if (width < 2 || height < 2) return null;
            if (raster is null || raster.Width != width || raster.Height != height)
            {
                raster = new PlotRaster(width, height);
                bitmap = new WriteableBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32, null);
                image.Source = bitmap;
            }
            return raster;
        }

        public void Present()
        {
            if (bitmap is null || raster is null) return;
            bitmap.WritePixels(new Int32Rect(0, 0, raster.Width, raster.Height), raster.Pixels, raster.Width * 4, 0);
        }
    }
}
