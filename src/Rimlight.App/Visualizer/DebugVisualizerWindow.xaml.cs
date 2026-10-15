using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rimlight.App.Overlay;
using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Rimlight.Platform.Shell;
using Wpf.Ui.Appearance;
using ShellTheme = Rimlight.Platform.Shell.SystemTheme;

namespace Rimlight.App.Visualizer;

/// <summary>
/// The debug visualizer (<c>--debug-visualizer</c>, doc 03 §4), the tool for tuning how the glow feels: the spectrum
/// with the bands, scrolling Level, Bass and Beat, the bass flux against the beat threshold, beat flashes, the tempo
/// estimate, silence, the capture device and format, and a slider for every <see cref="AudioTuning"/> field with
/// "Copy params as JSON". It shows the overlay's own analyzer through an <see cref="AnalyzerProbe"/>, so what it shows
/// is what drives the glow. Runs (keeps the capture open, the analyzer at the full rate and its timer going) only
/// while it is on screen: not hidden, minimized or on another virtual desktop. Closing it really closes it. Always on
/// top, and follows Windows' light or dark theme. UI thread only.
/// </summary>
internal partial class DebugVisualizerWindow : Window
{
    private const double TickSeconds = 1.0 / 30;
    private const double StaleSeconds = 0.5; // no new frame from the overlay for this long: say why

    private readonly AppController app;
    private readonly AnalyzerProbe probe = new();
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private readonly TimeAxis axis = new();
    private readonly List<TuningRow> rows = [];
    private readonly PlotSurface spectrumSurface;
    private readonly PlotSurface featuresSurface;
    private readonly PlotSurface fluxSurface;
    private readonly TextBlock[] bandLabels;
    private readonly (TextBlock Label, double Hz)[] frequencyLabels;
    private SpectrumPlot? spectrum;
    private TimelinePlot? features;
    private TimelinePlot? flux;
    private IDisposable? audio;
    private CloakWatcher? cloak; // kept with the window: its callback must outlive the hook
    private bool running;
    private bool failed;
    private AnalyzerSnapshot? latest;  // the snapshot on screen; ours until the next TakeSnapshot
    private double lastSnapshotAt;
    private int lastBeatCount = -1;
    private AudioTuning? edited;       // what the sliders show; null until the analyzer's tuning arrives
    private AudioTuning? applied;      // the analyzer's tuning, as last reported
    private AudioTuning? unconfirmed;  // the last request sent, until its outcome arrives
    private AudioTuning? labelledBands; // the tuning the band labels were placed for
    private bool refreshing;
    private bool subscribed;

    /// <summary>Builds the window; <see cref="Present"/> shows it.</summary>
    /// <param name="app">The app: its overlay frame source, settings and audio requests.</param>
    public DebugVisualizerWindow(AppController app)
    {
        this.app = app;
        InitializeComponent();
        Title = $"{AppInfo.Name} debug visualizer";
        FitWorkArea();
        if (!UiTheme.IsLoaded)
        {
            // Without WPF-UI's theme brushes; the system's colors keep it readable.
            Background = SystemColors.WindowBrush;
            Foreground = SystemColors.WindowTextBrush;
        }

        spectrumSurface = new PlotSurface(SpectrumHost, SpectrumImage);
        featuresSurface = new PlotSurface(FeaturesHost, FeaturesImage);
        fluxSurface = new PlotSurface(FluxHost, FluxImage);
        bandLabels = [AddLabel("Bass", BandLabels, "PlotLabel"), AddLabel("Mid", BandLabels, "PlotLabel"), AddLabel("High", BandLabels, "PlotLabel")];
        frequencyLabels = [.. SpectrumPlot.GridHz.Select(hz => (AddLabel(hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}", FrequencyLabels, "AxisLabel"), hz))];
        SpectrumTitle.Text = "Spectrum (dB, 20 Hz to 20 kHz)";
        BuildRows();
        SetEnabled(false);
        ShowWaiting(0);

        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(TickSeconds) };
        timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => UpdateRunning();
        StateChanged += (_, _) => UpdateRunning();
        // The plots follow their hosts' sizes (in device pixels, so a DPI change counts too).
        SpectrumHost.SizeChanged += (_, _) => Redraw();
        FeaturesHost.SizeChanged += (_, _) => Redraw();
        FluxHost.SizeChanged += (_, _) => Redraw();
        DpiChanged += (_, _) => Redraw();
        Closed += (_, _) =>
        {
            Stop();
            cloak?.Dispose();
        };
    }

    /// <summary>Shows the window, or brings it to the front.</summary>
    public void Present()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint handle = new WindowInteropHelper(this).Handle;
        // The content follows the theme through WPF-UI's brushes once it is synced; the title bar is this window's own.
        // Nothing else syncs the theme in a --debug-visualizer session, which has no settings window.
        ApplyTitleBar();
        UiTheme.Follow(this, ApplyTitleBar);
        // A switch to another virtual desktop changes neither IsVisible nor the window state.
        cloak = new CloakWatcher(handle, () => Dispatcher.InvokeAsync(UpdateRunning));

        void ApplyTitleBar() => ShellTheme.UseDarkTitleBar(handle, UiTheme.Current == ApplicationTheme.Dark);
    }

    // Runs only while on screen: the capture request, the probe on the overlay thread and the timer.
    private void UpdateRunning()
    {
        bool shown = IsVisible && WindowState != WindowState.Minimized && cloak?.IsCloaked != true;
        if (shown == running) return;
        running = shown;
        if (shown)
        {
            // The same probe as before a minimize, so a change sent as the window went away (a focused value box
            // commits then) still gets its answer; only the history starts over. A copy filled before the minimize is
            // dropped, and the window keeps that buffer as it keeps every snapshot it takes.
            probe.Restart();
            if (probe.TakeSnapshot() is { } stale) latest = stale;
            audio = app.RequestAudio();
            app.AttachVisualizer(probe);
            app.SettingsService.Changed += OnSettingsChanged;
            subscribed = true;
            clock.Restart();
            lastSnapshotAt = 0;
            if (!failed) timer.Start();
            RefreshRows(); // Sensitivity may have changed in Settings meanwhile
        }
        else
        {
            Stop();
        }
    }

    // CenterScreen centers on the work area without keeping the window inside it, so a window taller than the screen
    // starts with its title bar above it (1366×768 at 125% leaves about 580 DIPs). The primary display's work area
    // bounds the size, as for Settings.
    private void FitWorkArea()
    {
        Rect area = SystemParameters.WorkArea;
        if (!(area.Width > 32 && area.Height > 32)) return;
        double width = area.Width - 16, height = area.Height - 16;
        MinWidth = Math.Min(MinWidth, width);
        MinHeight = Math.Min(MinHeight, height);
        Width = Math.Min(Width, width);
        Height = Math.Min(Height, height);
    }

    private void Stop()
    {
        running = false;
        timer.Stop();
        clock.Stop();
        app.AttachVisualizer(null);
        audio?.Dispose();
        audio = null;
        if (!subscribed) return;
        subscribed = false;
        app.SettingsService.Changed -= OnSettingsChanged;
    }

    private void Tick()
    {
        try
        {
            double now = clock.Elapsed.TotalSeconds;
            if (probe.TakeOutcome() is { } outcome) OnOutcome(outcome);
            if (probe.TakeSnapshot() is { } snapshot)
            {
                latest = snapshot;
                lastSnapshotAt = now;
                OnSnapshot(snapshot);
            }
            else if (now - lastSnapshotAt > StaleSeconds)
            {
                ShowWaiting(now);
            }
        }
        catch (Exception exception)
        {
            // A broken plot must not take the app down; the window stays as it is.
            Trace.WriteLine($"[Visualizer] Drawing failed and stopped: {exception}");
            failed = true;
            timer.Stop();
            MessageText.Text = "Drawing failed and stopped. The log has the details.";
        }
    }

    private void OnSnapshot(AnalyzerSnapshot snapshot)
    {
        applied = snapshot.Tuning;
        if (edited is null)
        {
            // The analyzer's tuning as it is now (it may have been tuned in an earlier session of this window).
            edited = snapshot.Tuning;
            RefreshRows();
            SetEnabled(true);
        }
        Draw(snapshot);
        ShowText(snapshot);
    }

    private void Redraw()
    {
        PlaceLabels(force: true);
        if (latest is not null && !failed) Draw(latest);
    }

    private void Draw(AnalyzerSnapshot snapshot)
    {
        if (spectrumSurface.Fit() || spectrum is null) spectrum = spectrumSurface.Raster is { } s ? new SpectrumPlot(s) : null;
        if (featuresSurface.Fit() || features is null) features = featuresSurface.Raster is { } f ? new TimelinePlot(f, flux: false) : null;
        if (fluxSurface.Fit() || flux is null) flux = fluxSurface.Raster is { } x ? new TimelinePlot(x, flux: true) : null;
        axis.Update(snapshot);
        if (spectrum is not null)
        {
            spectrum.Draw(snapshot, spectrumSurface.Thickness);
            spectrumSurface.Present();
        }
        if (features is not null)
        {
            features.Draw(snapshot, axis, featuresSurface.Thickness);
            featuresSurface.Present();
        }
        if (flux is not null)
        {
            flux.Draw(snapshot, axis, fluxSurface.Thickness);
            fluxSurface.Present();
        }
        PlaceLabels(force: false);
    }

    private void ShowText(AnalyzerSnapshot s)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        string source = s.Audio is { } captured ? $"{captured.DeviceName} · {captured.Format}"
            : s.Listening ? "No output device to capture yet (retrying)"
            : "Not capturing";
        SourceText.Text = s.Demo ? source + " · Demo mode: the glow follows a synthetic beat, not this audio" : source;

        int window = Math.Max(2, (s.SpectrumLength - 1) * 2);
        string rate = string.Format(culture, "Analysis at {0} Hz · window {1} ({2:0.0} Hz bins) · {3:0} analyzer frames/s",
            s.AnalysisSampleRate, window, s.AnalysisSampleRate / (double)window, axis.FramesPerSecond);
        if (s.Audio is { DroppedSamples: > 0 } dropping) rate += string.Format(culture, " · {0:N0} samples dropped", dropping.DroppedSamples);
        RateText.Text = rate;

        BpmText.Text = s.EstimatedBpm > 0 ? string.Format(culture, "{0:0.0} BPM", s.EstimatedBpm) : "– BPM";
        BeatsText.Text = string.Format(culture, "{0:N0} beats", s.BeatCount);
        AudioFeatures f = s.Features;
        FeaturesText.Text = string.Format(culture, "Level {0:0.00} · Bass {1:0.00} · Beat {2:0.00}", f.Level, f.Bass, f.Beat);
        SilentText.Text = f.IsSilent ? "Silent" : "Sound";
        // A beat between two snapshots may have decayed already; a new count still flashes the lamp in full.
        bool newBeat = lastBeatCount >= 0 && s.BeatCount > lastBeatCount;
        lastBeatCount = s.BeatCount;
        BeatLamp.Opacity = 0.15 + 0.85 * Math.Max(Math.Clamp(f.Beat, 0, 1), newBeat ? 1 : 0);

        FeaturesTitle.Text = string.Format(culture, "Last {0:0.0} s", axis.SpanSeconds);
        FluxTitle.Text = string.Format(culture, "Bass flux, full scale {0:0.###}", flux?.Scale ?? 0);
    }

    // Before the first frame, or when frames stopped coming.
    private void ShowWaiting(double now)
    {
        BeatLamp.Opacity = 0.15;
        if (app.Glow is null)
        {
            SourceText.Text = "The glow isn't running, so there is no analyzer to show. Open logs folder in the tray menu shows why.";
            RateText.Text = "";
        }
        else if (now > 0 || latest is not null)
        {
            SourceText.Text = "No frames from the overlay: no display shows the glow right now.";
            RateText.Text = "The plots continue when a display shows it again.";
        }
        else
        {
            SourceText.Text = "Waiting for the analyzer…";
            RateText.Text = "";
        }
    }

    // Band names over their bands, frequency labels under the spectrum. Cheap; redone on a resize or a band change.
    private void PlaceLabels(bool force)
    {
        AudioTuning? tuning = latest?.Tuning;
        if (!force && ReferenceEquals(tuning, labelledBands)) return;
        labelledBands = tuning;
        double width = SpectrumHost.ActualWidth;
        foreach ((TextBlock label, double hz) in frequencyLabels) Place(label, SpectrumPlot.XForHz(hz, width), 0);
        if (tuning is null) return;
        Place(bandLabels[0], Center(tuning.BassMinHz, tuning.BassMaxHz, width), 2);
        Place(bandLabels[1], Center(tuning.BassMaxHz, tuning.MidMaxHz, width), 2);
        Place(bandLabels[2], Center(tuning.MidMaxHz, tuning.HighMaxHz, width), 2);
    }

    private static double Center(float fromHz, float toHz, double width) =>
        (SpectrumPlot.XForHz(Math.Max(fromHz, SpectrumPlot.MinHz), width) + SpectrumPlot.XForHz(toHz, width)) / 2;

    private static void Place(TextBlock label, double centerX, double top)
    {
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, centerX - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, top);
    }

    private TextBlock AddLabel(string text, Canvas canvas, string style)
    {
        var label = new TextBlock { Text = text, Style = (Style)Resources[style] };
        canvas.Children.Add(label);
        return label;
    }

    // ---- Tuning

    private sealed record TuningRow(TuningParameter Parameter, Slider Slider, TextBox Box);

    private void BuildRows()
    {
        string? group = null;
        foreach (TuningParameter parameter in TuningParameters.All)
        {
            if (parameter.Group != group)
            {
                group = parameter.Group;
                TuningRows.Children.Add(new TextBlock
                {
                    Text = group,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, rows.Count == 0 ? 0 : 12, 0, 4),
                });
            }
            TuningRows.Children.Add(BuildRow(parameter));
        }

        // A field added to AudioTuning later has no slider, but is still copied: the JSON is written from the record.
        IReadOnlyList<string> missing = TuningParameters.Missing();
        if (missing.Count > 0)
        {
            Trace.WriteLine($"[Visualizer] AudioTuning fields without a slider: {string.Join(", ", missing)}.");
            TuningRows.Children.Add(new TextBlock
            {
                Text = $"No slider yet for: {string.Join(", ", missing)}. Paste JSON to change them.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }
    }

    private Grid BuildRow(TuningParameter parameter)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2), ToolTip = parameter.Description };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var label = new TextBlock { Text = parameter.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        double range = parameter.SliderMaximum - parameter.SliderMinimum;
        var slider = new Slider
        {
            Minimum = parameter.SliderMinimum,
            Maximum = parameter.SliderMaximum,
            SmallChange = parameter.Scale switch
            {
                SliderScale.PowerOfTwo => 1,
                SliderScale.Log => 0.01,
                _ => parameter.Step > 0 ? parameter.Step : range / 100,
            },
            LargeChange = parameter.Scale == SliderScale.PowerOfTwo ? 1 : range / 10,
            IsSnapToTickEnabled = parameter.Scale == SliderScale.PowerOfTwo,
            TickFrequency = parameter.Scale == SliderScale.PowerOfTwo ? 1 : range / 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
        };
        var box = new TextBox { VerticalAlignment = VerticalAlignment.Center, MinWidth = 64 };
        var unit = new TextBlock { Text = parameter.Unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        unit.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        AutomationProperties.SetName(slider, parameter.Label);
        AutomationProperties.SetName(box, parameter.Label + " value");
        AutomationProperties.SetHelpText(slider, parameter.Description);

        Grid.SetColumn(slider, 1);
        Grid.SetColumn(box, 2);
        Grid.SetColumn(unit, 3);
        grid.Children.Add(label);
        grid.Children.Add(slider);
        grid.Children.Add(box);
        grid.Children.Add(unit);

        var row = new TuningRow(parameter, slider, box);
        rows.Add(row);
        slider.ValueChanged += (_, e) => OnSliderChanged(row, e.NewValue);
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Commit(row);
            e.Handled = true;
        };
        box.LostKeyboardFocus += (_, _) => Commit(row);
        return grid;
    }

    private void SetEnabled(bool enabled)
    {
        foreach (TuningRow row in rows)
        {
            row.Slider.IsEnabled = enabled;
            row.Box.IsEnabled = enabled;
        }
        CopyButton.IsEnabled = enabled;
        PasteButton.IsEnabled = enabled;
        ResetButton.IsEnabled = enabled;
    }

    // Every row from `edited`, and Sensitivity from the settings, without counting as edits. A box being typed in
    // keeps its text unless `includeFocused`.
    private void RefreshRows(bool includeFocused = false)
    {
        foreach (TuningRow row in rows) ShowRow(row, updateSlider: true, updateBox: includeFocused || !row.Box.IsKeyboardFocusWithin);
    }

    // The tuning a row shows: `edited`, with the setting's Sensitivity.
    private AudioTuning Shown(TuningParameter p, AudioTuning tuning) => p.Name == TuningParameters.SensitivityName
        ? tuning with { Sensitivity = app.SettingsService.Current.Sensitivity }
        : tuning;

    private void ShowRow(TuningRow row, bool updateSlider, bool updateBox)
    {
        if (edited is null) return;
        TuningParameter p = row.Parameter;
        AudioTuning shown = Shown(p, edited);
        refreshing = true;
        try
        {
            if (updateSlider)
            {
                // A value no position stands for (the slider refuses ∞ and NaN) leaves the slider where it is.
                double position = p.ToSlider(p.Get(shown));
                if (double.IsFinite(position)) row.Slider.Value = position;
            }
            if (updateBox) row.Box.Text = p.Text(shown);
        }
        finally
        {
            refreshing = false;
        }
    }

    private void OnSliderChanged(TuningRow row, double position)
    {
        if (refreshing || edited is null) return;
        Apply(row, row.Parameter.FromSlider(position));
        ShowRow(row, updateSlider: false, updateBox: true);
    }

    // The value box: Enter or leaving it applies a typed value; anything else puts the current value back. Text that
    // is still what the row shows sends nothing, so passing through a box never changes a value.
    private void Commit(TuningRow row)
    {
        if (refreshing || edited is null) return;
        TuningParameter p = row.Parameter;
        string text = row.Box.Text;
        if (text != p.Text(Shown(p, edited)))
        {
            if (!TuningParameter.TryParse(text, out double value))
                MessageText.Text = $"\"{text}\" isn't a number; {p.Label} keeps its value.";
            else if (!TuningParameter.CanStore(p.Round(value)))
                MessageText.Text = $"{text.Trim()} is far too large; {p.Label} keeps its value.";
            else
                Apply(row, p.Round(value));
        }
        ShowRow(row, updateSlider: true, updateBox: true);
    }

    private void Apply(TuningRow row, double value)
    {
        TuningParameter p = row.Parameter;
        if (p.Name == TuningParameters.SensitivityName)
        {
            // The settings own Sensitivity (H-007); the overlay passes the new value to the analyzer.
            float sensitivity = (float)Math.Clamp(value, 0.25, 2);
            app.SettingsService.Update(s => s with { Sensitivity = sensitivity });
            return;
        }
        // Compared as stored (float), so leaving a box unchanged, or a slider step that rounds to the same value,
        // sends nothing.
        if (edited is null || (float)p.Get(edited) == (float)value) return;
        edited = p.With(edited, value);
        Send(edited);
    }

    private void Send(AudioTuning tuning)
    {
        unconfirmed = tuning;
        probe.RequestTuning(tuning);
        app.Overlays?.RequestFrame(); // a stopped loop applies it at once
    }

    // The overlay thread applied the request, or refused it (the analyzer validates every field).
    private void OnOutcome(TuningOutcome outcome)
    {
        applied = outcome.Applied;
        // The answer to an older request: the newest is still on its way, and its own answer follows.
        if (!ReferenceEquals(outcome.Requested, unconfirmed)) return;
        unconfirmed = null;
        if (outcome.Error is { } error)
        {
            // The rows go back to what the analyzer uses, so they match Copy, and the next change doesn't carry the
            // refused value along (it would be refused again). The box that was typed in too.
            edited = outcome.Applied;
            RefreshRows(includeFocused: true);
            MessageText.Text = $"Not applied: {error} The analyzer keeps its previous values, which the rows show again.";
        }
        else if (MessageText.Text.StartsWith("Not applied", StringComparison.Ordinal))
        {
            MessageText.Text = "";
        }
    }

    // Sensitivity changed (here, in Settings, or by a paste).
    private void OnSettingsChanged(Settings settings)
    {
        TuningRow? row = rows.Find(r => r.Parameter.Name == TuningParameters.SensitivityName);
        if (row is not null) ShowRow(row, updateSlider: !row.Slider.IsMouseCaptureWithin, updateBox: !row.Box.IsKeyboardFocusWithin);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        AudioTuning? tuning = applied ?? edited;
        if (tuning is null) return;
        try
        {
            Clipboard.SetText(TuningJson.Serialize(tuning));
            MessageText.Text = unconfirmed is not null
                ? "Copied the analyzer's current parameters as JSON. Your latest change hasn't reached it yet, so it isn't in them."
                : "Copied every parameter as JSON (what tools/wav-analyze --tuning reads).";
        }
        catch (Exception exception)
        {
            // Usually another app holding the clipboard (WPF has retried for a second); any HRESULT can surface here.
            MessageText.Text = $"The clipboard is busy ({exception.Message}). Try again.";
        }
    }

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        string? text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (Exception exception)
        {
            MessageText.Text = $"The clipboard is busy ({exception.Message}). Try again.";
            return;
        }
        if (!TuningJson.TryParse(text, out AudioTuning? pasted, out bool hasSensitivity, out string? error))
        {
            MessageText.Text = $"The clipboard doesn't hold tuning JSON: {error}";
            return;
        }
        edited = pasted!;
        // Sensitivity is a setting (H-007): a pasted Sensitivity key within its range changes it. Without the key the
        // setting stays as it is (Reset keeps it too), rather than dropping to the default a partial JSON implies.
        float sensitivity = edited.Sensitivity;
        bool takeSensitivity = hasSensitivity && sensitivity is >= 0.25f and <= 2f;
        if (takeSensitivity && sensitivity != app.SettingsService.Current.Sensitivity)
            app.SettingsService.Update(s => s with { Sensitivity = sensitivity });
        RefreshRows();
        Send(edited);
        MessageText.Text = takeSensitivity
            ? "Pasted, the Sensitivity setting too. Missing parameters were set to their defaults."
            : "Pasted. Missing parameters were set to their defaults; Sensitivity stays as the setting has it.";
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        // Sensitivity stays as the settings have it.
        edited = new AudioTuning();
        RefreshRows();
        Send(edited);
        MessageText.Text = "Back to the defaults (Sensitivity is the setting's).";
    }
}
