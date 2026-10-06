using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Rimlight.Core;
using Rimlight.Platform;
using Rimlight.Platform.Overlay;

namespace Rimlight.App;

/// <summary>
/// Owns the notification-area icon: placeholder icon, tooltip, "Glow on", "Mode" and Quit, plus a test submenu in
/// Debug builds. The full menu (doc 06 §2) arrives in K6.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly AppController app;
    private readonly TaskbarIcon icon;
    private readonly MenuItem glowOn;
    private readonly (MenuItem Item, AnimationMode Mode)[] modes;
#if DEBUG
    private readonly DebugMenu debug;
#endif

    public TrayIconHost(AppController app)
    {
        this.app = app;
        var menu = new ContextMenu();

        glowOn = new MenuItem { Header = "Glow on" };
        glowOn.Click += (_, _) => Update(s => s with { Enabled = !s.Enabled });
        menu.Items.Add(glowOn);

        var mode = new MenuItem { Header = "Mode" };
        modes = [Choice(mode, "Music Sync", AnimationMode.MusicSync), Choice(mode, "Idle Glow", AnimationMode.IdleGlow), Choice(mode, "Off", AnimationMode.Off)];
        menu.Items.Add(mode);
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
            ToolTipText = BuildToolTip(),
            ContextMenu = menu,
        };

        // The icon lives outside any visual tree, so it must be created explicitly. Keep Efficiency Mode off:
        // H.NotifyIcon's default puts the whole process on EcoQoS and Idle priority class, which would starve
        // the render thread and audio capture.
        icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Dispose() => icon.Dispose();

    private (MenuItem, AnimationMode) Choice(MenuItem parent, string header, AnimationMode mode)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => Update(s => s with { Animation = mode });
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
#if DEBUG
        debug.Refresh(current);
#endif
    }

    private static string BuildToolTip()
    {
        string text = AppInfo.Name + " — Waiting for music";

        // Visible check that the manifest took effect; without Per-Monitor V2 the overlay is misplaced on mixed-DPI setups.
        return DpiAwareness.IsPerMonitorV2() ? text : text + " (warning: not Per-Monitor V2 DPI aware)";
    }

#if DEBUG
    // "Render test (debug)": the render loop's live status, settings without a settings window yet (K7), and stand-ins
    // for the system watchers (K5): battery, a global pause and a per-monitor pause.
    private sealed class DebugMenu
    {
        private readonly AppController app;
        private readonly Action<Func<Settings, Settings>> update;
        private readonly MenuItem status = new() { IsEnabled = false };
        private readonly MenuItem hideWhenSilent = new() { Header = "When silent: Hide" };
        private readonly (MenuItem Item, int Cap)[] caps;
        private readonly (MenuItem Item, BatteryBehavior Behavior)[] batteryBehaviors;
        private readonly MenuItem onBattery = new() { Header = "Simulate running on battery" };
        private readonly MenuItem pauseAll = new() { Header = "Pause everywhere (like a locked session)" };
        private readonly MenuItem pausePrimary = new() { Header = "Pause the primary monitor (like a fullscreen video)" };
        private bool simulatedBattery;
        private bool pausedAll;
        private bool pausedPrimary;

        public DebugMenu(AppController app, Action<Func<Settings, Settings>> update)
        {
            this.app = app;
            this.update = update;
            Root = new MenuItem { Header = "Render test (debug)" };
            Root.Items.Add(status);
            Root.Items.Add(new Separator());

            hideWhenSilent.Click += (_, _) => update(s => s with
            {
                WhenSilent = s.WhenSilent == SilentBehavior.Hide ? SilentBehavior.IdleGlow : SilentBehavior.Hide,
            });
            Root.Items.Add(hideWhenSilent);

            var fps = new MenuItem { Header = "FPS cap" };
            caps = [Cap(fps, "30", 30), Cap(fps, "60", 60), Cap(fps, "120", 120), Cap(fps, "Native (display refresh)", 0)];
            Root.Items.Add(fps);

            var battery = new MenuItem { Header = "On battery" };
            batteryBehaviors = [Battery(battery, "Normal", BatteryBehavior.Normal), Battery(battery, "Reduce (30 fps, half scale)", BatteryBehavior.Reduce),
                Battery(battery, "Pause", BatteryBehavior.Pause)];
            Root.Items.Add(battery);

            onBattery.Click += (_, _) =>
            {
                simulatedBattery = !simulatedBattery;
                app.Overlays?.SetOnBattery(simulatedBattery || PowerStatus.IsOnBattery());
            };
            Root.Items.Add(onBattery);

            pauseAll.Click += (_, _) =>
            {
                pausedAll = !pausedAll;
                app.Overlays?.SetPaused(pausedAll);
            };
            Root.Items.Add(pauseAll);

            pausePrimary.Click += (_, _) =>
            {
                pausedPrimary = !pausedPrimary;
                string? primary = pausedPrimary ? DisplayMonitors.PrimaryDeviceName() : null;
                app.Overlays?.SetPausedMonitors(primary is null ? [] : [primary]);
            };
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
            hideWhenSilent.IsChecked = current.WhenSilent == SilentBehavior.Hide;
            foreach ((MenuItem item, int cap) in caps) item.IsChecked = current.FpsCap == cap;
            foreach ((MenuItem item, BatteryBehavior behavior) in batteryBehaviors) item.IsChecked = current.OnBattery == behavior;
            onBattery.IsChecked = simulatedBattery;
            pauseAll.IsChecked = pausedAll;
            pausePrimary.IsChecked = pausedPrimary;
        }

        // e.g. "Full rate (vsync): 60 frames/s, 60 presents/s, 1 of 1 overlay shown"
        private static string Describe(RenderStatus status) => string.Format(CultureInfo.InvariantCulture,
            "{0}: {1:0} frames/s, {2:0} presents/s, {3} of {4} overlay(s) shown{5}",
            status.Pace, status.FramesPerSecond, status.PresentsPerSecond, status.VisibleOverlays, status.Overlays,
            status.HalfScale ? ", half scale" : "");

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
