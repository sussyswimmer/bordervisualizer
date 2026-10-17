using System.Diagnostics;
using System.Windows;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Opens the settings window for the tray (double-click, "Settings…"), a second start of the app, a normal start and
/// the welcome window. The window is created on first use, so a start at sign-in builds no UI, and is then kept
/// (closing only hides it). If it can't be built or shown, the user is told so, with where the log is
/// (<see cref="WindowFailure"/>), rather than getting another window that has no settings in it. UI thread only.
/// </summary>
internal sealed class SettingsWindowLauncher(AppController app) : IShowSettings
{
    private const string What = "its settings window";

    private SettingsWindow? window;

    /// <inheritdoc />
    public void ShowSettings()
    {
        if (!UiTheme.IsLoaded)
        {
            // Without WPF-UI's styles the window would have no templates; the [UiTheme] line before this says why.
            Trace.WriteLine("[Settings] The settings window can't open: WPF-UI's styles didn't load.");
            string reason = UiTheme.LoadFailure is { } failure ? " " + WindowFailure.Describe(failure) : "";
            WindowFailure.Show(What, "The Fluent styles (WPF-UI) didn't load." + reason);
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
                // The full exception names the XAML file and line, or the page whose code threw.
                Trace.WriteLine($"[Settings] Creating the settings window failed: {exception}");
                WindowFailure.Show(What, WindowFailure.Describe(exception));
                return;
            }
            window.Closed += (_, _) => window = null;
            ClaimMainWindow();
            Trace.WriteLine("[Settings] Created the settings window.");
        }
        try
        {
            window.Present();
        }
        catch (Exception exception)
        {
            // Kept: the next try shows it again (Present only shows and activates it).
            Trace.WriteLine($"[Settings] Showing the settings window failed: {exception}");
            WindowFailure.Show(What, WindowFailure.Describe(exception));
        }
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
