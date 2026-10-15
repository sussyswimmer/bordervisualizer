using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Rimlight.App.Logging;
using Rimlight.App.Overlay;
using Rimlight.App.SettingsUi;
using Rimlight.App.Updates;
using Rimlight.App.Visualizer;
using Rimlight.Core;
using Rimlight.Platform.Audio;
using Rimlight.Platform.Media;
using Rimlight.Platform.Overlay;
using Rimlight.Platform.Shell;

namespace Rimlight.App;

/// <summary>
/// The composition root: owns the settings, the global shortcut, the tray icon, the media session reader, the system
/// watchers, loopback capture, the glow overlay, the settings window and the update checks, wires them together and
/// tears them down in order. Created and used on the UI thread. Also opens the debug visualizer and runs demo mode (K8).
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly Action shutdown;
    private readonly SingleInstance? instance;
    private readonly StartupOptions options;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher; // the UI thread's
    private readonly SettingsWindowLauncher settingsWindow;
    private readonly UpdateService updates;
    private readonly VisualizerLauncher visualizer;
    private SettingsService? settings;
    private GlobalHotkey? hotkey;
    private TrayIconHost? tray;
    private NowPlayingService? media;
    private SystemPauseBridge? systemPauses;
    private LoopbackCapture? capture;
    private MusicGlowSource? glow;
    private OverlayHost? overlays;
    private WelcomeWindow? welcome;
    private int toolTipQueued;
    private int audioRequests;
    private bool hotkeySuspended;
    private bool startupApplied;
    private bool disposed;

    /// <summary>Creates the controller; <see cref="Start"/> starts everything.</summary>
    /// <param name="shutdown">Ends the app (the tray's Quit).</param>
    /// <param name="instance">This process's single-instance claim, whose commands it handles; null for none.</param>
    /// <param name="options">The command line: <c>--background</c> (at sign-in) opens nothing; <c>--debug-visualizer</c>
    /// and <c>--demo</c> open the visualizer or start the demo instead of Settings.</param>
    public AppController(Action shutdown, SingleInstance? instance, StartupOptions options)
    {
        this.shutdown = shutdown;
        this.instance = instance;
        this.options = options;
        settingsWindow = new SettingsWindowLauncher(this);
        visualizer = new VisualizerLauncher(this, settingsWindow);
        SettingsWindow = settingsWindow;
        updates = new UpdateService(this);
        Updates = updates;
    }

    public SettingsService SettingsService => settings ?? throw new InvalidOperationException("Not started.");

    /// <summary>The glow overlay, or null if it could not start.</summary>
    public OverlayHost? Overlays => overlays;

    /// <summary>The overlay's frame source, whose analyzer feeds Settings' level meter and preview; null without the overlay.</summary>
    public MusicGlowSource? Glow => glow;

    /// <summary>What's playing and its album colors and art (the settings window's "Now playing" row).</summary>
    public NowPlayingService? Media => media;

    /// <summary>Sleep, lock, display, battery and fullscreen state, as it reaches the overlay.</summary>
    public SystemPauseBridge? SystemPauses => systemPauses;

    /// <summary>The tray icon, also for notifications (K7's "still running in the tray", K9's "update ready").</summary>
    public TrayIconHost? Tray => tray;

    /// <summary>The glow shortcut from the settings and whether it works, for Settings' inline warning (doc 06 §4).</summary>
    public HotkeyStatus Hotkey { get; private set; } = HotkeyStatus.None;

    /// <summary>Raised on the UI thread when <see cref="Hotkey"/> changes.</summary>
    public event Action? HotkeyChanged;

    /// <summary>Whether the "Launch at startup" registry entry matches the setting (false: the registry refused).</summary>
    public bool StartupRegistered { get; private set; } = true;

    /// <summary>True when this run is the first one (the welcome was shown): Settings shows its one-time tray toast then.</summary>
    public bool IsFirstRunSession { get; private set; }

    /// <summary>True once the app is quitting or Windows is ending the session: windows close instead of hiding.</summary>
    public bool IsExiting { get; private set; }

    /// <summary>True while demo mode runs (<c>--demo</c>, doc 04 §5); the tray shows it as a checked item.</summary>
    public bool IsDemoActive { get; private set; }

    /// <summary>Opens Settings (the settings window, created on first use).</summary>
    public IShowSettings SettingsWindow { get; set; }

    /// <summary>"Check for updates": Velopack's check against GitHub Releases (<see cref="UpdateService"/>).</summary>
    public IUpdateCheck Updates { get; set; }

    public void Start()
    {
        // %APPDATA%\Rimlight\settings.json (doc 02 "Settings storage"): validated on load, a bad file set aside as
        // settings.bad.json, every save atomic (Core's store, C7).
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);
        settings = new SettingsService(CoreFactory.CreateSettingsStore(directory));
        Settings initial = settings.Current;

        // WPF-UI's Fluent styles and the system's light or dark theme for every window and the tray menu, with the
        // brand accent (doc 08). Before the tray icon, whose menu picks the styles up.
        UiTheme.Load();

        // The glow's system-wide shortcut (doc 06 §4), on a message-only window on this thread.
        try
        {
            hotkey = new GlobalHotkey();
            hotkey.Pressed += ToggleGlow;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[App] The shortcut window could not be created: {exception.Message}");
        }
        ApplyHotkey(initial.ToggleHotkey);

        tray = new TrayIconHost(this, initial);

        // What's playing and its album colors, from Windows' media sessions (doc 05 §1): read on this machine and never
        // sent anywhere. Its events arrive on the thread pool.
        media = new NowPlayingService(CoreFactory.CreatePaletteExtractor());
        media.NowPlayingChanged += OnNowPlayingChanged;
        media.AlbumArtChanged += OnAlbumArtChanged;

        // Sleep, lock, display off, battery and fullscreen apps (doc 02 "Lifetime events", doc 04 §4), watched on their
        // own thread from before the overlay starts, so its first frame already knows about a locked session or a
        // fullscreen app.
        systemPauses = new SystemPauseBridge(initial);

        // System audio only, via WASAPI loopback on the default output device; the microphone is never opened.
        // The glow opens the stream only while music sync needs it (MusicGlowSource), so Off and Idle Glow capture
        // nothing.
        try
        {
            capture = new LoopbackCapture();
            glow = new MusicGlowSource(initial, capture, media);
            overlays = new OverlayHost(glow, initial);
            systemPauses.Attach(overlays);
            overlays.Start();
            capture.Start();
        }
        catch (Exception exception)
        {
            // The tray keeps working without the glow; the failure is in the log.
            Trace.WriteLine($"[App] The overlay could not start: {exception}");
            systemPauses.Attach(null);
            overlays?.Dispose();
            overlays = null;
            glow = null;
            capture?.Dispose();
            capture = null;
        }

        // Every change reaches the overlay thread as a new snapshot, applied on its next frame.
        settings.Changed += OnSettingsChanged;
        media.Start();

        // Launch at startup (doc 06 §4): the Run entry follows the setting, which is on by default, so the first run
        // registers it. Checked at every start, which also repairs a stale path.
        ApplyStartup(initial.LaunchAtStartup);

        // Updates from GitHub Releases (doc 06 §5), the app's only network call: checks while "Update automatically" is
        // on, and only in an installed copy.
        updates.Start(settings);

        // A later start of the app hands over its request ("show-settings", "show-visualizer", "demo") instead of running
        // twice.
        if (instance is not null)
        {
            instance.CommandReceived += OnCommand;
            instance.StartListening();
        }

        switch (options.WindowAtStart(initial.FirstRunComplete))
        {
            case StartupWindow.Welcome:
                // First run: say where the app went and how to use it, once. Saved like any change, so the next
                // start skips it.
                IsFirstRunSession = true;
                ShowWelcome(offerSettings: true);
                settings.Update(s => s with { FirstRunComplete = true });
                break;
            case StartupWindow.Settings:
                ShowSettings(); // started by hand: show something (doc 06 §4)
                break;
        }

        // --demo and --debug-visualizer (doc 04 §5, doc 03 §4).
        if (options.Demo) SetDemo(true);
        if (options.ShowVisualizer) ShowVisualizer();
    }

    /// <summary>
    /// Turns the glow on or off ("Glow on", a click on the icon, the shortcut). The demo shows the glow whatever the
    /// setting says, so during it this turns off what is on screen: it ends the demo and leaves the glow off.
    /// </summary>
    public void ToggleGlow()
    {
        if (IsDemoActive)
        {
            // Flipping the setting alone would change nothing on screen until the demo ended (doc 04 §5).
            SetDemo(false);
            settings?.Update(s => s with { Enabled = false });
            return;
        }
        settings?.Update(s => s with { Enabled = !s.Enabled });
    }

    /// <summary>Opens Settings (the tray, a second start, the welcome window).</summary>
    public void ShowSettings()
    {
        if (disposed) return;
        try
        {
            SettingsWindow.ShowSettings();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[App] Opening Settings failed: {exception}");
        }
    }

    /// <summary>
    /// Opens the debug visualizer (doc 03 §4), or brings it to the front: <c>--debug-visualizer</c>, a later start with
    /// it, and the Debug build's tray menu.
    /// </summary>
    public void ShowVisualizer()
    {
        if (disposed) return;
        try
        {
            visualizer.Show();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[App] Opening the visualizer failed: {exception}");
        }
    }

    /// <summary>
    /// Starts or ends demo mode (doc 04 §5): the glow plays a synthetic 120 BPM beat through four palettes, for
    /// recordings, without saving anything. <c>--demo</c>, a later start with it, and the tray's "Demo mode".
    /// </summary>
    /// <param name="on">True to start it.</param>
    public void SetDemo(bool on)
    {
        if (disposed || on == IsDemoActive) return;
        if (glow is null)
        {
            Trace.WriteLine("[App] Demo mode needs the glow, which didn't start.");
            return;
        }
        IsDemoActive = on;
        glow.SetDemo(on);
        overlays?.RequestFrame(); // a glow that draws nothing wakes for it
        if (settings is not null) tray?.ApplySettings(settings.Current); // the icon isn't dimmed while the demo glows
        Trace.WriteLine(on ? "[App] Demo mode on." : "[App] Demo mode off.");
    }

    /// <summary>
    /// Lets the debug visualizer watch and tune the overlay's analyzer through <paramref name="feed"/> (doc 03 §4): the
    /// overlay thread then publishes snapshots and calls the analyzer at the full rate, and the capture stays open, in
    /// every mode. Dispose the result when the visualizer closes or hides; everything then returns to how it idles.
    /// UI thread.
    /// </summary>
    /// <param name="feed">The visualizer's feed.</param>
    /// <returns>The attachment, or null when the glow didn't start (nothing to watch).</returns>
    public IDisposable? ObserveAnalyzer(VisualizerFeed feed)
    {
        if (disposed || glow is not { } source) return null;
        IDisposable audio = RequestAudio();
        source.SetVisualizer(feed);
        overlays?.RequestFrame();
        return new AnalyzerObservation(this, source, audio);
    }

    /// <summary>"Check for updates" in the tray.</summary>
    public void CheckForUpdates()
    {
        if (disposed) return;
        try
        {
            Updates.CheckNow();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[App] Checking for updates failed: {exception}");
        }
    }

    /// <summary>"Restart to update" in the tray, shown while a downloaded update waits for a restart.</summary>
    public void RestartToUpdate()
    {
        if (disposed) return;
        try
        {
            Updates.RestartToUpdate();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[App] Restarting to update failed: {exception}");
        }
    }

    /// <summary>"Open logs folder" in the tray.</summary>
    public void OpenLogsFolder()
    {
        if (!AppLog.OpenFolder()) Tell($"The logs folder couldn't be opened. It is {AppLog.Folder}.");
    }

    public void Quit()
    {
        IsExiting = true;
        shutdown();
    }

    /// <summary>Windows is signing out or shutting down: saves pending settings now, and lets windows close.</summary>
    public void EndSession()
    {
        IsExiting = true;
        settings?.Flush();
    }

    /// <summary>
    /// Keeps the analyzer running while something shows the audio (Settings' level meter and preview, K8's
    /// visualizer), even in Idle Glow, Off or while paused. Dispose the result when done. UI thread.
    /// </summary>
    /// <returns>The request; disposing it more than once is harmless.</returns>
    public IDisposable RequestAudio()
    {
        if (++audioRequests == 1) SetAudioWanted(true);
        return new AudioRequest(this);
    }

    /// <summary>
    /// Releases the glow shortcut while Settings records a new one, so pressing the current one is seen as keys
    /// rather than toggling the glow. <see cref="ResumeHotkey"/> registers it again. UI thread.
    /// </summary>
    public void SuspendHotkey()
    {
        if (hotkeySuspended || hotkey is null) return;
        hotkeySuspended = true;
        try
        {
            hotkey.Register(null); // not published: the setting itself hasn't changed
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[App] Releasing the shortcut failed: {exception.Message}");
        }
    }

    /// <summary>Registers the shortcut from the settings again after <see cref="SuspendHotkey"/>. UI thread.</summary>
    public void ResumeHotkey()
    {
        if (!hotkeySuspended) return;
        hotkeySuspended = false;
        if (settings is not null) ApplyHotkey(settings.Current.ToggleHotkey);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        IsExiting = true;
        if (instance is not null) instance.CommandReceived -= OnCommand; // the app disposes the claim itself
        settingsWindow.Close(); // first: stops its timers and its audio request, and lets go of every event
        visualizer.Close();     // the same for the visualizer
        welcome?.Close();
        welcome = null;
        hotkey?.Dispose();
        hotkey = null;
        updates.Dispose(); // stops the checks and a download; never waits for the network
        media?.Dispose(); // first of the engine: no media event reaches the overlay or the tray after this
        media = null;
        systemPauses?.Dispose(); // then no system event either
        systemPauses = null;
        overlays?.Dispose(); // stops the render thread, the ring's only reader, before the capture goes away
        overlays = null;
        glow = null;
        capture?.Dispose();
        capture = null;
        // Removes the icon now instead of leaving a ghost in the tray until the mouse passes over it.
        tray?.Dispose();
        tray = null;
        settings?.Dispose(); // saves a pending change
        settings = null;
    }

    /// <summary>Shows the welcome window, or brings it to the front.</summary>
    /// <param name="offerSettings">Whether it offers to open Settings (not while it stands in for them).</param>
    internal void ShowWelcome(bool offerSettings)
    {
        if (disposed) return;
        try
        {
            if (welcome is null)
            {
                bool startsWithWindows = StartupRegistered && SettingsService.Current.LaunchAtStartup;
                welcome = new WelcomeWindow(Hotkey, startsWithWindows, offerSettings ? ShowSettings : null);
                welcome.Closed += (_, _) => welcome = null;
                // WPF makes the first window the app's main window, which WPF-UI restyles on a theme change. That
                // must be the settings window (or none), never the welcome.
                settingsWindow.ClaimMainWindow();
            }
            welcome.Present();
        }
        catch (Exception exception)
        {
            // The tray works without it.
            Trace.WriteLine($"[App] Showing the welcome window failed: {exception}");
            welcome = null;
        }
    }

    // UI thread, inside SettingsService.Update.
    private void OnSettingsChanged(Settings snapshot)
    {
        overlays?.ApplySettings(snapshot);
        systemPauses?.ApplySettings(snapshot);
        tray?.ApplySettings(snapshot);
        if (snapshot.ToggleHotkey != Hotkey.Text) ApplyHotkey(snapshot.ToggleHotkey);
        if (snapshot.LaunchAtStartup != startupApplied) ApplyStartup(snapshot.LaunchAtStartup);
    }

    // Launch at startup (doc 06 §4): adds, updates or removes the Run entry.
    private void ApplyStartup(bool enabled)
    {
        startupApplied = enabled;
        StartupRegistered = StartupRegistration.Apply(AppInfo.Name, enabled);
    }

    // Registers the shortcut from the settings (H-009: "Ctrl+Alt+L", empty for none) and publishes the outcome.
    private void ApplyHotkey(string? text)
    {
        hotkeySuspended = false; // a new shortcut ends a recording's suspension
        HotkeyStatus status;
        try
        {
            status = hotkey?.Register(text) ?? new HotkeyStatus(text ?? "", HotkeyState.Failed, null);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[App] Registering the shortcut failed: {exception.Message}");
            status = new HotkeyStatus(text ?? "", HotkeyState.Failed, null);
        }
        if (status == Hotkey) return;
        Hotkey = status;
        welcome?.SetHotkey(status);
        HotkeyChanged?.Invoke();
    }

    // Thread pool: a later start's request, one or more commands separated by spaces (StartupOptions.HandOverCommand).
    // Only known commands do anything.
    private void OnCommand(string line)
    {
        foreach (string command in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (command)
            {
                case SingleInstance.ShowSettingsCommand:
                    dispatcher.InvokeAsync(ShowSettings);
                    break;
                case SingleInstance.ShowVisualizerCommand:
                    dispatcher.InvokeAsync(ShowVisualizer);
                    break;
                case SingleInstance.DemoCommand:
                    dispatcher.InvokeAsync(() => SetDemo(true));
                    break;
                default:
                    Trace.WriteLine("[App] Ignored an unknown command from another start.");
                    break;
            }
        }
    }

    /// <summary>A short message: a notification from the tray icon, or a message box if the tray can't show one. UI thread.</summary>
    /// <param name="message">The text.</param>
    internal void Tell(string message)
    {
        if (tray?.Notify(AppInfo.Name, message, userInitiated: true) == true) return;
        MessageBox.Show(message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
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
            // An exception here would reach the dispatcher; a stale tooltip is harmless.
            Trace.WriteLine($"[App] Updating the tooltip failed: {exception}");
        }
    }

    // The overlay thread reads the flag on its next frame; a glow that draws no frames is woken for it.
    private void SetAudioWanted(bool wanted)
    {
        glow?.SetAudioWanted(wanted);
        overlays?.RequestFrame();
    }

    private sealed class AudioRequest(AppController app) : IDisposable
    {
        private bool released;

        public void Dispose()
        {
            if (released) return;
            released = true;
            if (--app.audioRequests == 0) app.SetAudioWanted(false);
        }
    }

    // Detaches the visualizer: the overlay thread stops publishing snapshots and goes back to its usual pace.
    private sealed class AnalyzerObservation(AppController app, MusicGlowSource source, IDisposable audio) : IDisposable
    {
        private bool released;

        public void Dispose()
        {
            if (released) return;
            released = true;
            source.SetVisualizer(null);
            audio.Dispose(); // the capture closes again unless Settings still shows the audio
            app.overlays?.RequestFrame(); // a frame at once, so the overlay picks its idle pace again
        }
    }

}
