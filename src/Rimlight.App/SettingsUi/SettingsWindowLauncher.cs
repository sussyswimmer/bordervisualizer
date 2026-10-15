using System.Diagnostics;
using System.Windows;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Opens the settings window for the first run, the tray (double-click, "Settings…"), a second start of the app and a
/// normal start, or the welcome window when it can't be built. The window is created on first use, so a start at
/// sign-in builds no UI, and is then kept (closing only hides it). UI thread only.
/// </summary>
internal sealed class SettingsWindowLauncher(AppController app) : IShowSettings
{
    private SettingsWindow? window;

    /// <inheritdoc />
    public void ShowSettings()
    {
        if (!UiTheme.IsLoaded)
        {
            // Without WPF-UI's styles the window would have no templates; the log says why they are missing.
            Trace.WriteLine("[Settings] The settings window can't open without its styles.");
            app.ShowWelcome();
            return;
        }
        if (window is null)
        {
            try
            {
                window = new SettingsWindow(app);
            }
            catch (Exception exception)
            {
                // Something is still shown, and the log says what went wrong.
                Trace.WriteLine($"[Settings] Creating the settings window failed: {exception}");
                app.ShowWelcome();
                return;
            }
            window.Closed += (_, _) => window = null;
            ClaimMainWindow();
        }
        window.Present();
    }

    /// <summary>
    /// Makes the settings window (or no window) the application's main window: WPF-UI's theme manager restyles the
    /// main window's backdrop on a theme change, and WPF hands that role to the first window it creates.
    /// </summary>
    public void ClaimMainWindow()
    {
        try
        {
            if (Application.Current is { } application) application.MainWindow = window;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Setting the main window failed: {exception.Message}");
        }
    }

    /// <summary>Closes the window for good (the app is exiting): its timers stop and it lets go of every event.</summary>
    public void Close()
    {
        SettingsWindow? closing = window;
        window = null;
        try
        {
            closing?.Close();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Closing the settings window failed: {exception.Message}");
        }
    }
}
