using System.Diagnostics;
using System.Windows;
using Rimlight.App.Overlay;
using Rimlight.Core;
using Rimlight.Platform.Audio;
using Rimlight.Platform.Overlay;

namespace Rimlight.App;

/// <summary>Tray application: loopback audio (K2) drives the glow overlays (K1).</summary>
public partial class App : Application
{
    private TrayIconHost? tray;
    private LoopbackCapture? capture;
    private OverlayHost? overlays;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        tray = new TrayIconHost(onQuit: () => Shutdown(), onSimulateDeviceLoss: () => overlays?.SimulateDeviceLoss());

        // Settings persistence arrives with C7/K7; until then the overlay uses the defaults.
        var settings = new Settings();

        // System audio only, via WASAPI loopback on the default output device; the microphone is never opened.
        capture = new LoopbackCapture();
        capture.Start();
        try
        {
            overlays = new OverlayHost(new MusicGlowSource(settings, capture), settings);
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
        overlays?.Dispose(); // stops the render thread, the ring's only reader, before the capture goes away
        capture?.Dispose();
        // Removes the icon now instead of leaving a ghost in the tray until the mouse passes over it.
        tray?.Dispose();
        base.OnExit(e);
    }
}
