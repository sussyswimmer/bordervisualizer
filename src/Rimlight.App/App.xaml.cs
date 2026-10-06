using System.Diagnostics;
using System.Windows;
using Rimlight.App.Overlay;
using Rimlight.Core;
using Rimlight.Platform.Overlay;

namespace Rimlight.App;

/// <summary>Tray application with the static glow overlay (K1).</summary>
public partial class App : Application
{
    private TrayIconHost? tray;
    private OverlayHost? overlays;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        tray = new TrayIconHost(onQuit: () => Shutdown(), onSimulateDeviceLoss: () => overlays?.SimulateDeviceLoss());

        // Settings persistence arrives with C7/K7; until then the overlay uses the defaults.
        var settings = new Settings();
        try
        {
            overlays = new OverlayHost(new StaticGlowSource(settings), settings);
            overlays.Start();
        }
        catch (Exception exception)
        {
            // The tray keeps working without the glow; the failure is visible in the debugger output.
            Trace.WriteLine($"[App] The overlay could not start: {exception}");
            overlays?.Dispose();
            overlays = null;
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        overlays?.Dispose();
        // Removes the icon now instead of leaving a ghost in the tray until the mouse passes over it.
        tray?.Dispose();
        base.OnExit(e);
    }
}
