using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Rimlight.App.Logging;
using Rimlight.App.Overlay;
using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Rimlight.Platform.Audio;
using Rimlight.Platform.Media;
using Rimlight.Platform.Overlay;
using Rimlight.Platform.Shell;

namespace Rimlight.App;

/// <summary>
/// The composition root: owns the settings, the global shortcut, the tray icon, the media session reader, the system
/// watchers, loopback capture, the glow overlay and the settings window, wires them together and tears them down in
/// order. Created and used on the UI thread. Later tasks plug in the visualizer (K8) and updates (K9,
/// <see cref="Updates"/>).
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly Action shutdown;
    private readonly SingleInstance? instance;
    private readonly bool background;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher; // the UI thread's
    private readonly SettingsWindowLauncher settingsWindow;
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
    /// <param name="background">Started with <c>--background</c> (at sign-in): don't open Settings.</param>
    public AppController(Action shutdown, SingleInstance? instance, bool background)
    {
        this.shutdown = shutdown;
        this.instance = instance;
        this.background = background;
        settingsWindow = new SettingsWindowLauncher(this);
        SettingsWindow = settingsWindow;
        Updates = new NoUpdateCheck(this);
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

    /// <summary>Opens Settings (the settings window, created on first use).</summary>
    public IShowSettings SettingsWindow { get; set; }

    /// <summary>"Check for updates". K9 replaces the stand-in with the Velopack check.</summary>
    public IUpdateCheck Updates { get; set; }

    public void Start()
    {
        // %APPDATA%\Rimlight (doc 02 "Settings storage"). The store is in memory until C7.
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

        // A later start of the app hands over its request ("show-settings") instead of running twice.
        if (instance is not null)
        {
            instance.CommandReceived += OnCommand;
            instance.StartListening();
        }

        if (!initial.FirstRunComplete)
        {
            // First run: say where the app went and how to use it, once.
            IsFirstRunSession = true;
            ShowWelcome(offerSettings: true);
            settings.Update(s => s with { FirstRunComplete = true });
        }
        else if (!background)
        {
            ShowSettings(); // started by hand: show something (doc 06 §4)
        }
    }

    /// <summary>Turns the glow on or off ("Glow on", a click on the icon, the shortcut).</summary>
    public void ToggleGlow() => settings?.Update(s => s with { Enabled = !s.Enabled });

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
        welcome?.Close();
        welcome = null;
        hotkey?.Dispose();
        hotkey = null;
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

    // Thread pool: a later start's request. Only known commands do anything.
    private void OnCommand(string command)
    {
        if (command == SingleInstance.ShowSettingsCommand) dispatcher.InvokeAsync(ShowSettings);
        else Trace.WriteLine("[App] Ignored an unknown command from another start.");
    }

    // A short message: a notification from the tray icon, or a message box if the tray can't show one.
    private void Tell(string message)
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

    // Until K9: says which version runs.
    private sealed class NoUpdateCheck(AppController app) : IUpdateCheck
    {
        public void CheckNow() => app.Tell($"You're running {AppInfo.Name} {AppLog.Version}. Update checks start with the first release.");
    }
}
