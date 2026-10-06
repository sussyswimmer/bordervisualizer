using System.Diagnostics;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Rimlight.App.Overlay;
using Rimlight.Core;
using Rimlight.Platform;
using Rimlight.Platform.Media;
using Rimlight.Platform.Overlay;

namespace Rimlight.App;

/// <summary>
/// Owns the notification-area icon: placeholder icon, the now-playing tooltip, "Glow on", "Mode", "Colors" and Quit,
/// plus a test submenu in Debug builds. The full menu (doc 06 §2) arrives in K6. Use it on the UI thread only.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly AppController app;
    private readonly TaskbarIcon icon;
    private readonly string toolTipSuffix;
    private readonly MenuItem glowOn;
    private readonly (MenuItem Item, AnimationMode Mode)[] modes;
    private readonly (MenuItem Item, ColorMode Mode)[] colorModes;
#if DEBUG
    private readonly DebugMenu debug;
#endif

    public TrayIconHost(AppController app)
    {
        this.app = app;
        // Visible check that the manifest took effect; without Per-Monitor V2 the overlay is misplaced on mixed-DPI setups.
        toolTipSuffix = DpiAwareness.IsPerMonitorV2() ? "" : " (warning: not Per-Monitor V2 DPI aware)";
        var menu = new ContextMenu();

        glowOn = new MenuItem { Header = "Glow on" };
        glowOn.Click += (_, _) => Update(s => s with { Enabled = !s.Enabled });
        menu.Items.Add(glowOn);

        var mode = new MenuItem { Header = "Mode" };
        modes = [Choice(mode, "Music Sync", AnimationMode.MusicSync), Choice(mode, "Idle Glow", AnimationMode.IdleGlow), Choice(mode, "Off", AnimationMode.Off)];
        menu.Items.Add(mode);

        var colors = new MenuItem { Header = "Colors" };
        colorModes = [ColorChoice(colors, "From album art", ColorMode.AlbumArt), ColorChoice(colors, "Manual", ColorMode.Manual)];
        menu.Items.Add(colors);
        menu.Items.Add(new Separator());

#if DEBUG
        // Debug builds only: switches for the manual render-loop tests (K3) and the device-loss path (doc 07 Phase 1).
        debug = new DebugMenu(app, Update);
        menu.Items.Add(debug.Root);
        menu.Items.Add(new Separator());
#endif

        var quit = new MenuItem { Header = "Quit " + AppInfo.Name };
        quit.Click += (_, _) => app.Quit();
        menu.Items.Add(quit);
        menu.Opened += (_, _) => Refresh();

        icon = new TaskbarIcon
        {
            // Built here rather than in a static field: the pack: scheme only parses once the WPF Application exists.
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/Rimlight.ico", UriKind.Absolute)),
            ToolTipText = TrayToolTip.Build(null, toolTipSuffix),
            ContextMenu = menu,
        };

        // The icon lives outside any visual tree, so it must be created explicitly. Keep Efficiency Mode off:
        // H.NotifyIcon's default puts the whole process on EcoQoS and Idle priority class, which would starve
        // the render thread and audio capture.
        icon.ForceCreate(enablesEfficiencyMode: false);
        // Explorer restarted. Raised on this thread, after H.NotifyIcon has put the icon back.
        icon.TrayIcon.MessageWindow.TaskbarCreated += (_, _) => OnTaskbarCreated();
    }

    public void Dispose() => icon.Dispose();

    /// <summary>Shows the playing track in the tooltip ("Rimlight — Title · Artist"); otherwise "Waiting for music".</summary>
    public void SetNowPlaying(NowPlaying? track)
    {
        string text = TrayToolTip.Build(track, toolTipSuffix);
        // TrayIcon.ToolTip is the text the shell last accepted. After a refused update the property already holds the
        // new text, so setting it again would change nothing: the icon is updated directly then.
        if (text == icon.TrayIcon.ToolTip) return;
        try
        {
            if (text == icon.ToolTipText) icon.TrayIcon.UpdateToolTip(text);
            else icon.ToolTipText = text;
        }
        catch (InvalidOperationException exception)
        {
            // H.NotifyIcon throws when Shell_NotifyIcon refuses the change (Explorer restarting); TaskbarCreated or the
            // next track retries.
            Trace.WriteLine($"[Tray] Updating the tooltip failed: {exception.Message}");
        }
    }

    // The icon came back with the last tooltip the shell accepted; a newer one may have been refused meanwhile. The
    // media session manager may have gone away with Explorer, so it is asked again too.
    private void OnTaskbarCreated()
    {
        try
        {
            app.Media?.Refresh();
            SetNowPlaying(app.Media?.Current);
        }
        catch (Exception exception)
        {
            // This runs inside the icon's window procedure, where an exception would end the app.
            Trace.WriteLine($"[Tray] Restoring the tooltip failed: {exception}");
        }
    }

    private (MenuItem, AnimationMode) Choice(MenuItem parent, string header, AnimationMode mode)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => Update(s => s with { Animation = mode });
        parent.Items.Add(item);
        return (item, mode);
    }

    // "From album art" also turns Override album color off (every preset sets it, H-009), so it always brings the
    // album colors back.
    private (MenuItem, ColorMode) ColorChoice(MenuItem parent, string header, ColorMode mode)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => Update(s => mode == ColorMode.AlbumArt
            ? s with { ColorMode = mode, OverrideAlbumColor = false }
            : s with { ColorMode = mode });
        parent.Items.Add(item);
        return (item, mode);
    }

    private void Update(Func<Settings, Settings> change) => app.SettingsService.Update(change);

    // Check marks follow the current settings each time the menu opens.
    private void Refresh()
    {
        Settings current = app.SettingsService.Current;
        glowOn.IsChecked = current.Enabled;
        foreach ((MenuItem item, AnimationMode mode) in modes) item.IsChecked = current.Animation == mode;
        // The colors the glow uses: with Override album color, the manual ones in Album Art mode too.
        ColorMode colors = current.ColorMode == ColorMode.AlbumArt && !current.OverrideAlbumColor
            ? ColorMode.AlbumArt
            : ColorMode.Manual;
        foreach ((MenuItem item, ColorMode mode) in colorModes) item.IsChecked = colors == mode;
#if DEBUG
        debug.Refresh(current);
#endif
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
