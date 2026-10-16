using System.Diagnostics;
using Rimlight.App.SettingsUi;

namespace Rimlight.App.Visualizer;

/// <summary>
/// Opens the debug visualizer (doc 03 §4) for <c>--debug-visualizer</c>, a later start with it, and the Debug build's
/// tray menu. The window is created on first use; closing it really closes it (it is a tool, not part of the app's
/// everyday UI), so nothing of it stays in memory or runs. UI thread only.
/// </summary>
internal sealed class VisualizerLauncher(AppController app, SettingsWindowLauncher settings)
{
    private VisualizerWindow? window;

    /// <summary>Shows the window, or brings it to the front.</summary>
    public void Show()
    {
        if (!UiTheme.IsLoaded)
        {
            // Without WPF-UI's styles the window would have no templates; the log says why they are missing.
            Trace.WriteLine("[Visualizer] The visualizer can't open without its styles.");
            return;
        }
        if (window is null)
        {
            try
            {
                window = new VisualizerWindow(app);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"[Visualizer] Creating the visualizer failed: {exception}");
                return;
            }
            window.Closed += (_, _) => window = null;
            // WPF makes the first window it creates the main window, which WPF-UI restyles on a theme change; that
            // stays the settings window's role (the visualizer follows the theme itself).
            settings.ClaimMainWindow();
        }
        window.Present();
    }

    /// <summary>Closes the window (the app is exiting).</summary>
    public void Close()
    {
        VisualizerWindow? closing = window;
        window = null;
        try
        {
            closing?.Close();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Visualizer] Closing the visualizer failed: {exception.Message}");
        }
    }
}
