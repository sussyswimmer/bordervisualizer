using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Rimlight.App.Overlay;
using Rimlight.Core;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Motion (doc 06 §3.3): the mode, what happens when it's quiet, sensitivity, and a live sound level meter fed by the
/// overlay thread's analyzer (polled about 15 times a second, only while the page is on screen).
/// </summary>
internal partial class MotionPage : UserControl, ISettingsPage
{
    private const double MeterSeconds = 1.0 / 15;
    private const double StaleSeconds = 0.5;   // no new analyzer frame for this long: nothing is listening
    private const double StatusHoldSeconds = 1; // a status must hold this long before it is shown (and announced)

    private readonly AppController app;
    private readonly PageEdits edits;
    private readonly DispatcherTimer meter;
    private readonly Stopwatch clock = new();
    private int lastSequence = -2;
    private double lastSequenceAt;
    private MeterState shownState = MeterState.Unknown;
    private MeterState pendingState = MeterState.Unknown;
    private double pendingSince;

    public MotionPage(AppController app)
    {
        this.app = app;
        edits = new PageEdits(app);
        InitializeComponent();
        meter = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(MeterSeconds) };
        meter.Tick += (_, _) => UpdateMeter();
    }

    private enum MeterState { Unknown, Hearing, Silent, NotListening, Unavailable }

    public void Refresh(Settings settings) => edits.Refresh(() =>
    {
        MusicSyncChoice.IsChecked = settings.Animation == AnimationMode.MusicSync;
        IdleGlowChoice.IsChecked = settings.Animation == AnimationMode.IdleGlow;
        OffChoice.IsChecked = settings.Animation == AnimationMode.Off;
        WhenSilentBox.SelectedIndex = settings.WhenSilent == SilentBehavior.Hide ? 1 : 0;
        WhenSilentBox.IsEnabled = settings.Animation == AnimationMode.MusicSync;
        WhenSilentDescription.Text = settings.Animation == AnimationMode.MusicSync
            ? "After two seconds without sound, the glow fades to this."
            : "Only matters in Music Sync.";
        PageEdits.Set(SensitivitySlider, settings.Sensitivity);
        SensitivityValue.Text = string.Create(CultureInfo.CurrentCulture, $"{settings.Sensitivity:0.00}×");
    });

    public void SetActive(bool active)
    {
        if (active)
        {
            clock.Restart();
            lastSequence = -2;
            shownState = pendingState = MeterState.Unknown;
            PageEdits.SetMessage(MeterStatus, "");
            UpdateMeter();
            meter.Start();
        }
        else
        {
            meter.Stop();
            clock.Stop();
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        AnimationMode mode = ReferenceEquals(sender, OffChoice) ? AnimationMode.Off
            : ReferenceEquals(sender, IdleGlowChoice) ? AnimationMode.IdleGlow
            : AnimationMode.MusicSync;
        edits.Update(s => s with { Animation = mode });
    }

    private void OnWhenSilentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WhenSilentBox.SelectedIndex < 0) return;
        SilentBehavior behavior = WhenSilentBox.SelectedIndex == 1 ? SilentBehavior.Hide : SilentBehavior.IdleGlow;
        edits.Update(s => s with { WhenSilent = behavior });
    }

    private void OnSensitivityChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        edits.Update(s => s with { Sensitivity = (float)(Math.Round(e.NewValue * 20) / 20) });

    // The analyzer's latest features (published by the overlay thread every frame, at least 10 times a second while
    // this page asks for audio).
    private void UpdateMeter()
    {
        try
        {
            double now = clock.Elapsed.TotalSeconds;
            MeterState state;
            AudioFeatures features = default;
            if (app.Glow is not { } glow)
            {
                state = MeterState.Unavailable;
            }
            else
            {
                AudioReading reading = glow.LatestAudio;
                if (reading.Sequence != lastSequence)
                {
                    lastSequence = reading.Sequence;
                    lastSequenceAt = now;
                }
                bool fresh = reading.Sequence >= 0 && now - lastSequenceAt < StaleSeconds;
                features = fresh ? reading.Features : default;
                state = !fresh ? MeterState.NotListening : features.IsSilent ? MeterState.Silent : MeterState.Hearing;
            }

            LevelMeter.Value = Math.Clamp(features.Level, 0, 1) * 100;
            BeatDot.Opacity = 0.15 + 0.85 * Math.Clamp(features.Beat, 0, 1);
            ShowState(state, now);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] The level meter failed and stopped: {exception.Message}");
            meter.Stop();
        }
    }

    // The status line changes only once a state has held for a second, so pauses between songs don't chatter (it is
    // announced to screen readers), and a reading left from before the page opened never flashes up.
    private void ShowState(MeterState state, double now)
    {
        if (state != pendingState)
        {
            pendingState = state;
            pendingSince = now;
        }
        if (state == shownState || now - pendingSince < StatusHoldSeconds) return;
        shownState = state;
        PageEdits.SetMessage(MeterStatus, state switch
        {
            MeterState.Hearing => "Sound detected.",
            MeterState.Silent => "Silence. Play something and the bar moves.",
            MeterState.NotListening => "Not listening: no display shows the glow right now.",
            MeterState.Unavailable => "Sound can't be read because the glow didn't start. Open logs folder in the tray menu shows why.",
            _ => "",
        });
    }
}
