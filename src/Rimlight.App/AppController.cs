using System.Diagnostics;
using System.IO;
using Rimlight.App.Overlay;
using Rimlight.Core;
using Rimlight.Platform.Audio;
using Rimlight.Platform.Overlay;

namespace Rimlight.App;

/// <summary>
/// The composition root: owns the settings, the tray icon, loopback capture and the glow overlay, wires them together
/// and tears them down in order. Created and used on the UI thread. Later tasks add the media session (K4), system
/// watchers (K5), shell services (K6), the settings window (K7), the visualizer (K8) and updates (K9).
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly Action shutdown;
    private SettingsService? settings;
    private TrayIconHost? tray;
    private LoopbackCapture? capture;
    private OverlayHost? overlays;

    public AppController(Action shutdown) => this.shutdown = shutdown;

    public SettingsService SettingsService => settings ?? throw new InvalidOperationException("Not started.");

    /// <summary>The glow overlay, or null if it could not start.</summary>
    public OverlayHost? Overlays => overlays;

    public void Start()
    {
        // %APPDATA%\Rimlight (doc 02 "Settings storage"). The store is in memory until C7.
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);
        settings = new SettingsService(CoreFactory.CreateSettingsStore(directory));
        tray = new TrayIconHost(this);

        // System audio only, via WASAPI loopback on the default output device; the microphone is never opened.
        // The glow opens the stream only while music sync needs it (MusicGlowSource), so Off and Idle Glow capture
        // nothing.
        try
        {
            Settings initial = settings.Current;
            capture = new LoopbackCapture();
            overlays = new OverlayHost(new MusicGlowSource(initial, capture), initial);
            overlays.Start();
            capture.Start();
        }
        catch (Exception exception)
        {
            // The tray keeps working without the glow; the failure is visible in the debugger output.
            Trace.WriteLine($"[App] The overlay could not start: {exception}");
            overlays?.Dispose();
            overlays = null;
            capture?.Dispose();
            capture = null;
        }

        // Every change reaches the overlay thread as a new snapshot, applied on its next frame.
        settings.Changed += snapshot => overlays?.ApplySettings(snapshot);
    }

    public void Quit() => shutdown();

    public void Dispose()
    {
        overlays?.Dispose(); // stops the render thread, the ring's only reader, before the capture goes away
        overlays = null;
        capture?.Dispose();
        capture = null;
        // Removes the icon now instead of leaving a ghost in the tray until the mouse passes over it.
        tray?.Dispose();
        tray = null;
        settings?.Dispose(); // saves a pending change
        settings = null;
    }
}
