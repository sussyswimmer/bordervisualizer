using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Rimlight.App.Overlay;
using Rimlight.Core;
using Rimlight.Platform.Audio;
using Rimlight.Platform.Media;
using Rimlight.Platform.Overlay;

namespace Rimlight.App;

/// <summary>
/// The composition root: owns the settings, the tray icon, the media session reader, the system watchers, loopback
/// capture and the glow overlay, wires them together and tears them down in order. Created and used on the UI thread.
/// Later tasks add shell services (K6), the settings window (K7), the visualizer (K8) and updates (K9).
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly Action shutdown;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher; // the UI thread's
    private SettingsService? settings;
    private TrayIconHost? tray;
    private NowPlayingService? media;
    private SystemPauseBridge? systemPauses;
    private LoopbackCapture? capture;
    private OverlayHost? overlays;
    private int toolTipQueued;

    public AppController(Action shutdown) => this.shutdown = shutdown;

    public SettingsService SettingsService => settings ?? throw new InvalidOperationException("Not started.");

    /// <summary>The glow overlay, or null if it could not start.</summary>
    public OverlayHost? Overlays => overlays;

    /// <summary>What's playing and its album colors and art (the settings window's "Now playing" row).</summary>
    public NowPlayingService? Media => media;

    /// <summary>Sleep, lock, display, battery and fullscreen state, as it reaches the overlay.</summary>
    public SystemPauseBridge? SystemPauses => systemPauses;

    public void Start()
    {
        // %APPDATA%\Rimlight (doc 02 "Settings storage"). The store is in memory until C7.
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);
        settings = new SettingsService(CoreFactory.CreateSettingsStore(directory));
        tray = new TrayIconHost(this);

        // What's playing and its album colors, from Windows' media sessions (doc 05 §1): read on this machine and never
        // sent anywhere. Its events arrive on the thread pool.
        media = new NowPlayingService(CoreFactory.CreatePaletteExtractor());
        media.NowPlayingChanged += OnNowPlayingChanged;
        media.AlbumArtChanged += OnAlbumArtChanged;

        // Sleep, lock, display off, battery and fullscreen apps (doc 02 "Lifetime events", doc 04 §4), watched on their
        // own thread from before the overlay starts, so its first frame already knows about a locked session or a
        // fullscreen app.
        systemPauses = new SystemPauseBridge(settings.Current);

        // System audio only, via WASAPI loopback on the default output device; the microphone is never opened.
        // The glow opens the stream only while music sync needs it (MusicGlowSource), so Off and Idle Glow capture
        // nothing.
        try
        {
            Settings initial = settings.Current;
            capture = new LoopbackCapture();
            overlays = new OverlayHost(new MusicGlowSource(initial, capture, media), initial);
            systemPauses.Attach(overlays);
            overlays.Start();
            capture.Start();
        }
        catch (Exception exception)
        {
            // The tray keeps working without the glow; the failure is visible in the debugger output.
            Trace.WriteLine($"[App] The overlay could not start: {exception}");
            systemPauses.Attach(null);
            overlays?.Dispose();
            overlays = null;
            capture?.Dispose();
            capture = null;
        }

        // Every change reaches the overlay thread as a new snapshot, applied on its next frame.
        settings.Changed += snapshot =>
        {
            overlays?.ApplySettings(snapshot);
            systemPauses?.ApplySettings(snapshot);
        };
        media.Start();
    }

    public void Quit() => shutdown();

    public void Dispose()
    {
        media?.Dispose(); // first: no media event reaches the overlay or the tray after this
        media = null;
        systemPauses?.Dispose(); // then no system event either
        systemPauses = null;
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

    // Thread pool: new album colors. The render thread reads them on its next frame; a static glow draws no frames by
    // itself, so ask for one.
    private void OnAlbumArtChanged() => Volatile.Read(ref overlays)?.RequestFrame();

    // Thread pool: a new track or play state. The tooltip is a UI object, so it is updated on the UI thread; one queued
    // update shows the latest track however many changes arrive meanwhile.
    private void OnNowPlayingChanged()
    {
        if (Interlocked.Exchange(ref toolTipQueued, 1) == 0) dispatcher.InvokeAsync(ShowNowPlaying);
    }

    private void ShowNowPlaying()
    {
        Volatile.Write(ref toolTipQueued, 0);
        try
        {
            tray?.SetNowPlaying(media?.Current); // null after Dispose
        }
        catch (Exception exception)
        {
            // An exception here would reach the dispatcher and end the app; a stale tooltip is harmless.
            Trace.WriteLine($"[App] Updating the tooltip failed: {exception}");
        }
    }
}
