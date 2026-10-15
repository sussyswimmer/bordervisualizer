using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Rimlight.App.Overlay;
using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Rimlight.Platform;
using Rimlight.Platform.Media;
using Rimlight.Platform.Overlay;
using Rimlight.Platform.Shell;

namespace Rimlight.App;

/// <summary>
/// Owns the notification-area icon (doc 06 §2): the icon, dimmed to half opacity while the glow is off; the
/// now-playing tooltip; click to turn the glow on or off, double-click for Settings; and the right-click menu, which
/// the keyboard opens too. Re-creates the icon when it can't be added at sign-in or Explorer restarts, and shows
/// notifications for later tasks (K7's "still running in the tray", K9's "update ready"). Use it on the UI thread only.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private static readonly TimeSpan CreateRetryInterval = TimeSpan.FromSeconds(3);

    private readonly AppController app;
    private readonly TaskbarIcon icon;
    private readonly TrayIconImages images;
    private readonly DispatcherTimer createRetry;
    private readonly string toolTipSuffix;
    private readonly MenuItem glowOn;
    private readonly (MenuItem Item, AnimationMode Mode)[] modes;
    private readonly (MenuItem Item, ColorMode Mode)[] colorModes;
    private readonly (MenuItem Item, Func<Settings, Settings> Apply)[] presets;
#if DEBUG
    private readonly DebugMenu debug;
#endif
    private string toolTip; // what the tooltip should say, also while the shell refuses it
    private bool dimmed;
    private bool disposed;

    /// <summary>Adds the icon to the notification area (or keeps trying until the shell takes it).</summary>
    /// <param name="app">The app, for its settings and actions.</param>
    /// <param name="settings">The settings at start.</param>
    public TrayIconHost(AppController app, Settings settings)
    {
        this.app = app;
        // Visible check that the manifest took effect; without Per-Monitor V2 the overlay is misplaced on mixed-DPI setups.
        toolTipSuffix = DpiAwareness.IsPerMonitorV2() ? "" : " (warning: not Per-Monitor V2 DPI aware)";
        toolTip = TrayToolTip.Build(null, toolTipSuffix);
        dimmed = !settings.Enabled;
        images = TrayIconImages.Load();

        var menu = new ContextMenu();
        glowOn = new MenuItem { Header = "_Glow on", IsCheckable = true };
        glowOn.Click += (_, _) => app.ToggleGlow();
        menu.Items.Add(glowOn);
        menu.Items.Add(new Separator());

        var mode = new MenuItem { Header = "_Mode" };
        modes = [Choice(mode, "_Music Sync", AnimationMode.MusicSync), Choice(mode, "_Idle Glow", AnimationMode.IdleGlow), Choice(mode, "_Off", AnimationMode.Off)];
        menu.Items.Add(mode);

        var colors = new MenuItem { Header = "_Colors" };
        colorModes = [ColorChoice(colors, "From _album art", ColorMode.AlbumArt), ColorChoice(colors, "_Manual", ColorMode.Manual)];
        menu.Items.Add(colors);

        // The built-in looks. Presets.All is empty until C7 lands, and the item stays greyed out until then.
        var presetMenu = new MenuItem { Header = "_Presets" };
        presets = [.. Presets.All.Select(preset => PresetChoice(presetMenu, preset.Name, preset.Apply))];
        presetMenu.IsEnabled = presets.Length > 0;
        menu.Items.Add(presetMenu);
        menu.Items.Add(new Separator());

        // Bold: what a double-click on the icon does (the Windows convention for a tray menu's default item).
        var settingsItem = new MenuItem { Header = "_Settings…", FontWeight = FontWeights.Bold };
        settingsItem.Click += (_, _) => app.ShowSettings();
        menu.Items.Add(settingsItem);
        var updates = new MenuItem { Header = "Check for _updates" };
        updates.Click += (_, _) => app.CheckForUpdates();
        menu.Items.Add(updates);
        var logs = new MenuItem { Header = "Open _logs folder" };
        logs.Click += (_, _) => app.OpenLogsFolder();
        menu.Items.Add(logs);
        menu.Items.Add(new Separator());

#if DEBUG
        // Debug builds only: switches for the manual render-loop tests (K3) and the device-loss path (doc 07 Phase 1).
        debug = new DebugMenu(app, Update);
        menu.Items.Add(debug.Root);
        menu.Items.Add(new Separator());
#endif

        var quit = new MenuItem { Header = "_Quit " + AppInfo.Name };
        quit.Click += (_, _) => app.Quit();
        menu.Items.Add(quit);
        menu.Opened += (_, _) =>
        {
            // The menu follows Windows' light or dark mode, also when it changed while no window watched for it.
            UiTheme.Sync();
            Refresh();
        };

        createRetry = new DispatcherTimer(DispatcherPriority.Background) { Interval = CreateRetryInterval };
        createRetry.Tick += (_, _) => Create();

        icon = new TaskbarIcon
        {
            ContextMenu = menu,
            // A click runs after the double-click time has passed without a second click, so a double-click opens
            // Settings without also toggling the glow.
            LeftClickCommand = new RelayCommand(app.ToggleGlow),
            DoubleClickCommand = new RelayCommand(app.ShowSettings),
        };
        // Keyboard access: Shift+F10 or the menu key on the focused icon (Win+B, then the arrow keys), Enter or Space.
        icon.TrayKeyboardContextMenu += (_, _) => ShowMenuFromKeyboard();
        icon.TrayKeyboardKeySelect += (_, _) => ShowMenuFromKeyboard();
        icon.TrayBalloonTipClicked += (_, _) => NotificationClicked?.Invoke();
        // Each (re)creation, including H.NotifyIcon's own after Explorer restarts (TaskbarCreated), gets the current
        // icon and tooltip: a change the shell refused while Explorer was gone would otherwise be lost. If that
        // re-creation fails, the retry timer takes over.
        icon.TrayIcon.Created += (_, _) => OnCreated();
        icon.TrayIcon.Removed += (_, _) =>
        {
            if (!disposed) createRetry.Start();
        };
        icon.TrayIcon.UpdateToolTip(toolTip); // kept until the icon exists
        icon.UpdateIcon(dimmed ? images.Dimmed : images.Normal);
        Create();
    }

    /// <summary>Raised on the UI thread when the user clicks a notification from <see cref="Notify"/>.</summary>
    public event Action? NotificationClicked;

    /// <summary>Removes the icon now instead of leaving a ghost in the tray until the mouse passes over it.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        createRetry.Stop();
        icon.Dispose();
        images.Dispose(); // after the shell has let go of the icon handles
    }

    /// <summary>Shows the playing track in the tooltip ("Rimlight — Title · Artist"); otherwise "Waiting for music".</summary>
    /// <param name="track">The current track, or null without a media session.</param>
    public void SetNowPlaying(NowPlaying? track)
    {
        string text = TrayToolTip.Build(track, toolTipSuffix);
        if (text == toolTip) return;
        toolTip = text;
        ApplyToolTip();
    }

    /// <summary>Follows a settings change: the icon dims while the glow is off.</summary>
    /// <param name="settings">The new settings.</param>
    public void ApplySettings(Settings settings)
    {
        if (disposed || dimmed == !settings.Enabled) return;
        dimmed = !settings.Enabled;
        ApplyIcon();
    }

    /// <summary>
    /// Shows a notification from the tray icon. Windows holds back notifications the user didn't ask for during quiet
    /// hours and Do Not Disturb.
    /// </summary>
    /// <param name="title">The first line.</param>
    /// <param name="message">The text.</param>
    /// <param name="userInitiated">True when it answers something the user just did, so it isn't held back.</param>
    /// <returns>False if the shell refused it (no icon yet, Explorer restarting).</returns>
    public bool Notify(string title, string message, bool userInitiated = false)
    {
        if (disposed) return false;
        try
        {
            icon.ShowNotification(title, message, NotificationIcon.None, respectQuietTime: !userInitiated);
            return true;
        }
        catch (InvalidOperationException exception)
        {
            Trace.WriteLine($"[Tray] Showing a notification failed: {exception.Message}");
            return false;
        }
    }

    private void Create()
    {
        if (disposed) return;
        if (icon.TrayIcon.IsCreated)
        {
            createRetry.Stop();
            return;
        }
        try
        {
            // Efficiency Mode stays off: H.NotifyIcon's default puts the whole process on EcoQoS and the Idle priority
            // class, which would starve the render thread and audio capture.
            icon.ForceCreate(enablesEfficiencyMode: false);
            createRetry.Stop();
        }
        catch (Exception exception)
        {
            // At sign-in the notification area may not be ready yet; Explorer's TaskbarCreated or the retry adds it.
            if (!createRetry.IsEnabled) Trace.WriteLine($"[Tray] Adding the icon failed, retrying every 3 s: {exception.Message}");
            createRetry.Start();
        }
    }

    private void OnCreated()
    {
        if (disposed) return;
        createRetry.Stop();
        ApplyIcon();
        ApplyToolTip();
    }

    private void ApplyIcon()
    {
        // False when the shell refuses (Explorer restarting); OnCreated sets it again once the icon is back.
        if (!icon.UpdateIcon(dimmed ? images.Dimmed : images.Normal)) Trace.WriteLine("[Tray] The shell refused the icon change.");
    }

    private void ApplyToolTip()
    {
        try
        {
            icon.TrayIcon.UpdateToolTip(toolTip);
        }
        catch (InvalidOperationException exception)
        {
            // Shell_NotifyIcon refused (Explorer restarting); OnCreated sets it again once the icon is back.
            Trace.WriteLine($"[Tray] Updating the tooltip failed: {exception.Message}");
        }
    }

    // The menu at the notification area, for keyboard users (the mouse opens it at the pointer).
    private void ShowMenuFromKeyboard()
    {
        try
        {
            icon.ShowContextMenu(TaskbarIcon.GetPopupTrayPosition());
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Tray] Opening the menu from the keyboard failed: {exception.Message}");
        }
    }

    private (MenuItem, AnimationMode) Choice(MenuItem parent, string header, AnimationMode mode)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => Update(s => s with { Animation = mode });
        parent.Items.Add(item);
        return (item, mode);
    }

    private (MenuItem, ColorMode) ColorChoice(MenuItem parent, string header, ColorMode mode)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => Update(s => s with { ColorMode = mode });
        parent.Items.Add(item);
        return (item, mode);
    }

    private (MenuItem, Func<Settings, Settings>) PresetChoice(MenuItem parent, string name, Func<Settings, Settings> apply)
    {
        var item = new MenuItem { Header = name.Replace("_", "__", StringComparison.Ordinal) }; // "_" would be an access key
        item.Click += (_, _) =>
        {
            try
            {
                Update(apply);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"[Tray] Applying the preset {name} failed: {exception}");
            }
        };
        parent.Items.Add(item);
        return (item, apply);
    }

    private void Update(Func<Settings, Settings> change) => app.SettingsService.Update(change);

    // Check marks and the shortcut follow the current state each time the menu opens.
    private void Refresh()
    {
        Settings current = app.SettingsService.Current;
        glowOn.IsChecked = current.Enabled;
        HotkeyStatus hotkey = app.Hotkey;
        glowOn.InputGestureText = hotkey.State == HotkeyState.Registered ? hotkey.Gesture?.ToString() ?? "" : "";
        foreach ((MenuItem item, AnimationMode mode) in modes) item.IsChecked = current.Animation == mode;
        foreach ((MenuItem item, ColorMode mode) in colorModes) item.IsChecked = current.ColorMode == mode;
        foreach ((MenuItem item, Func<Settings, Settings> apply) in presets) item.IsChecked = IsShowing(apply, current);
#if DEBUG
        debug.Refresh(current);
#endif
    }

    // A preset counts as chosen while applying it again would change nothing.
    private static bool IsShowing(Func<Settings, Settings> apply, Settings current)
    {
        try
        {
            return apply(current) == current;
        }
        catch (Exception)
        {
            return false;
        }
    }

#if DEBUG
    // "Render test (debug)": the render loop's live status, what the media session reader and the system watchers
    // see, settings without a settings window yet (K7: When silent, Override album color, FPS cap, On battery, Pause in
    // fullscreen apps), and simulated system states on top of the real ones: battery, a global pause and a per-monitor
    // pause.
    private sealed class DebugMenu
    {
        private readonly AppController app;
        private readonly Action<Func<Settings, Settings>> update;
        private readonly MenuItem status = new() { IsEnabled = false };
        private readonly MenuItem media = new() { IsEnabled = false };
        private readonly MenuItem system = new() { IsEnabled = false };
        private readonly MenuItem hideWhenSilent = new() { Header = "When silent: Hide" };
        private readonly MenuItem overrideAlbum = new() { Header = "Override album color" };
        private readonly (MenuItem Item, int Cap)[] caps;
        private readonly (MenuItem Item, BatteryBehavior Behavior)[] batteryBehaviors;
        private readonly MenuItem pauseInFullscreen = new() { Header = "Pause in fullscreen apps" };
        private readonly MenuItem onBattery = new() { Header = "Simulate running on battery" };
        private readonly MenuItem pauseAll = new() { Header = "Pause everywhere (like a locked session)" };
        private readonly MenuItem pausePrimary = new() { Header = "Pause the primary monitor (like a fullscreen video)" };

        public DebugMenu(AppController app, Action<Func<Settings, Settings>> update)
        {
            this.app = app;
            this.update = update;
            Root = new MenuItem { Header = "Render test (debug)" };
            Root.Items.Add(status);
            Root.Items.Add(media);
            Root.Items.Add(system);
            Root.Items.Add(new Separator());

            hideWhenSilent.Click += (_, _) => update(s => s with
            {
                WhenSilent = s.WhenSilent == SilentBehavior.Hide ? SilentBehavior.IdleGlow : SilentBehavior.Hide,
            });
            Root.Items.Add(hideWhenSilent);

            overrideAlbum.Click += (_, _) => update(s => s with { OverrideAlbumColor = !s.OverrideAlbumColor });
            Root.Items.Add(overrideAlbum);

            var fps = new MenuItem { Header = "FPS cap" };
            caps = [Cap(fps, "30", 30), Cap(fps, "60", 60), Cap(fps, "120", 120), Cap(fps, "Native (display refresh)", 0)];
            Root.Items.Add(fps);

            var battery = new MenuItem { Header = "On battery" };
            batteryBehaviors = [Battery(battery, "Normal", BatteryBehavior.Normal), Battery(battery, "Reduce (30 fps, half scale)", BatteryBehavior.Reduce),
                Battery(battery, "Pause", BatteryBehavior.Pause)];
            Root.Items.Add(battery);

            pauseInFullscreen.Click += (_, _) => update(s => s with { PauseInFullscreen = !s.PauseInFullscreen });
            Root.Items.Add(pauseInFullscreen);
            Root.Items.Add(new Separator());

            // Simulated on top of what the system watchers report: the glow pauses if either says so.
            onBattery.Click += (_, _) => Simulate(s => s with { OnBattery = !s.OnBattery });
            Root.Items.Add(onBattery);

            pauseAll.Click += (_, _) => Simulate(s => s with { PausedEverywhere = !s.PausedEverywhere });
            Root.Items.Add(pauseAll);

            pausePrimary.Click += (_, _) => Simulate(s => s with
            {
                PausedMonitor = s.PausedMonitor is null ? DisplayMonitors.PrimaryDeviceName() : null,
            });
            Root.Items.Add(pausePrimary);
            Root.Items.Add(new Separator());

            var deviceLoss = new MenuItem { Header = "Simulate GPU device loss" };
            deviceLoss.Click += (_, _) => app.Overlays?.SimulateDeviceLoss();
            Root.Items.Add(deviceLoss);
        }

        public MenuItem Root { get; }

        public void Refresh(Settings current)
        {
            status.Header = app.Overlays is { } overlays ? Describe(overlays.Status) : "The overlay is not running";
            media.Header = Describe(app.Media);
            system.Header = app.SystemPauses is { } pauses ? $"System: {pauses.State}" : "System: not watched";
            hideWhenSilent.IsChecked = current.WhenSilent == SilentBehavior.Hide;
            overrideAlbum.IsChecked = current.OverrideAlbumColor;
            foreach ((MenuItem item, int cap) in caps) item.IsChecked = current.FpsCap == cap;
            foreach ((MenuItem item, BatteryBehavior behavior) in batteryBehaviors) item.IsChecked = current.OnBattery == behavior;
            pauseInFullscreen.IsChecked = current.PauseInFullscreen;
            SimulatedSystem simulated = app.SystemPauses?.Simulated ?? default;
            onBattery.IsChecked = simulated.OnBattery;
            pauseAll.IsChecked = simulated.PausedEverywhere;
            pausePrimary.IsChecked = simulated.PausedMonitor is not null;
        }

        private void Simulate(Func<SimulatedSystem, SimulatedSystem> change)
        {
            if (app.SystemPauses is { } pauses) pauses.Simulated = change(pauses.Simulated);
        }

        // e.g. "Full rate (vsync): 60 frames/s, 60 presents/s, 1 of 1 overlay shown"
        private static string Describe(RenderStatus status) => string.Format(CultureInfo.InvariantCulture,
            "{0}: {1:0} frames/s, {2:0} presents/s, {3} of {4} overlay(s) shown{5}",
            status.Pace, status.FramesPerSecond, status.PresentsPerSecond, status.VisibleOverlays, status.Overlays,
            status.HalfScale ? ", half scale" : "");

        // e.g. "Media: Spotify.exe, playing, art 64×64, album colors #E0503C / #3C78E0". The title stays out of it; the
        // tooltip shows it.
        private static string Describe(NowPlayingService? media)
        {
            if (media is null) return "Media: not running";
            if (!media.IsAvailable) return "Media: unavailable (manual colors only)";
            if (media.Current is not { } track) return "Media: no session (manual colors)";
            string art = media.Art is { } image ? $"art {image.Width}×{image.Height}" : "no art";
            string colors = media.AlbumPalette is { } palette
                ? $"album colors {SrgbHex.Format(palette.Primary)} / {SrgbHex.Format(palette.Secondary)}"
                : "no album colors (manual colors)";
            // App IDs contain underscores, which a menu header would take as access keys.
            string app = (track.SourceApp ?? "unknown app").Replace("_", "__", StringComparison.Ordinal);
            return $"Media: {app}, {(track.IsPlaying ? "playing" : "not playing")}, {art}, {colors}";
        }

        private (MenuItem, int) Cap(MenuItem parent, string header, int cap)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => update(s => s with { FpsCap = cap });
            parent.Items.Add(item);
            return (item, cap);
        }

        private (MenuItem, BatteryBehavior) Battery(MenuItem parent, string header, BatteryBehavior behavior)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => update(s => s with { OnBattery = behavior });
            parent.Items.Add(item);
            return (item, behavior);
        }
    }
#endif
}
