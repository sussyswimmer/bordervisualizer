using Rimlight.App.Visualizer;
using Rimlight.Platform.Shell;
using Xunit;

namespace Rimlight.App.Tests;

// The app's other XAML windows: the first run's welcome (with and without its Settings button, for each state of the
// shortcut) and the debug visualizer (--debug-visualizer), which takes its card styles from the settings pages'
// PageStyles.xaml.
public sealed class OtherWindowTests : UiTest
{
    [WindowsTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheWelcomeOpens(bool offerSettings)
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+L", out HotkeyGesture gesture));
        HotkeyStatus[] states =
        [
            HotkeyStatus.None,
            new("Ctrl+Alt+L", HotkeyState.Registered, gesture),
            new("Ctrl+Alt+L", HotkeyState.InUse, gesture),
            new("Ctrl+Alt+L", HotkeyState.TypesCharacter, gesture),
            new("Nonsense", HotkeyState.Invalid, null),
        ];
        WelcomeWindow window = UiThread.Run(() =>
        {
            Action? openSettings = offerSettings ? () => { } : null;
            WelcomeWindow created = Track(new WelcomeWindow(states[1], startsWithWindows: true, openSettings));
            MoveOffScreen(created);
            created.Show();
            return created;
        });
        UiThread.WaitUntil(() => window.IsLoaded && window.ActualHeight > 0, "the welcome window to show");
        UiThread.Run(() =>
        {
            foreach (HotkeyStatus state in states) window.SetHotkey(state);
            Assert.Equal(offerSettings, window.SettingsButton.IsVisible);
        });
        AssertNoFailures();
    }

    [WindowsFact]
    public void TheVisualizerOpens()
    {
        VisualizerWindow window = UiThread.Run(() =>
        {
            VisualizerWindow created = Track(new VisualizerWindow(Controller));
            MoveOffScreen(created); // after its constructor, which places it at the work area's right edge
            created.Show();
            return created;
        });
        UiThread.WaitUntil(
            () => window.IsLoaded && window.TuningPanel.Children.Count > 0 && window.SpectrumHost.ActualWidth > 0,
            "the visualizer to show its plots and tuning");
        // Without the overlay (not started here, as when it fails to start) it says so instead of plotting.
        UiThread.WaitUntil(() => window.StatusText.IsVisible, "the visualizer to say the glow didn't start");
        // "Record 2 min" waits for the analyzer's first frame: with no overlay there is nothing to record.
        UiThread.Run(() =>
        {
            Assert.False(window.RecordButton.IsEnabled);
            Assert.False(window.StopButton.IsEnabled);
            Assert.False(window.OpenRecordingButton.IsVisible);
        });
        AssertNoFailures();
    }
}
