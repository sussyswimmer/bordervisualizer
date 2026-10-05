using System.Windows;

namespace Rimlight.App;

/// <summary>Minimal tray-only application for the K0 scaffold.</summary>
public partial class App : Application
{
    private TrayIconHost? tray;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        tray = new TrayIconHost(onQuit: () => Shutdown());
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        // Removes the icon now instead of leaving a ghost in the tray until the mouse passes over it.
        tray?.Dispose();
        base.OnExit(e);
    }
}
