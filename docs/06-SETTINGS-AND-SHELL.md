# 06 — Settings, Tray & Shell

## 1. Settings model (Core/Settings)

```csharp
public sealed record Settings
{
    public int Version { get; init; } = 1;
    public bool Enabled { get; init; } = true;

    public AnimationMode Animation { get; init; } = AnimationMode.MusicSync; // MusicSync | IdleGlow | Off
    public SilentBehavior WhenSilent { get; init; } = SilentBehavior.IdleGlow; // IdleGlow | Hide

    public ColorMode ColorMode { get; init; } = ColorMode.AlbumArt;          // AlbumArt | Manual
    public bool OverrideAlbumColor { get; init; } = false;
    public string PrimaryHex { get; init; } = "#7C5CFF";
    public string SecondaryHex { get; init; } = "#22D3EE";
    public float PrimaryRatio { get; init; } = 0.60f;                          // 0.1..0.9

    public float CoreThicknessDip { get; init; } = 6f;                         // 0..40
    public float Glow { get; init; } = 0.45f;                                  // 0..1
    public float Brightness { get; init; } = 0.80f;                            // 0.1..1
    public float Sensitivity { get; init; } = 1.0f;                            // 0.25..2
    public float CornerRadiusDip { get; init; } = 0f;                          // 0..40
    public bool CoverTaskbar { get; init; } = true;

    public MonitorSelection Monitors { get; init; } = MonitorSelection.All;    // All | PrimaryOnly | Custom
    public IReadOnlyList<string> CustomMonitorIds { get; init; } = [];         // stable device IDs, not indexes

    public int FpsCap { get; init; } = 60;                                     // 30 | 60 | 120 | 0 = native
    public bool PauseInFullscreen { get; init; } = true;
    public BatteryBehavior OnBattery { get; init; } = BatteryBehavior.Reduce;  // Normal | Reduce | Pause
    public bool HideFromScreenCapture { get; init; } = false;

    public bool LaunchAtStartup { get; init; } = true;
    public string ToggleHotkey { get; init; } = "Ctrl+Alt+L";
    public bool AutoUpdate { get; init; } = true;
    public bool FirstRunComplete { get; init; } = false;
}
```

- Validate and clamp all values on load, and reset invalid fields to their defaults. Never crash on a bad settings file. Back it up as `settings.bad.json` and continue.
- Monitor IDs come from `DISPLAY_DEVICE.DeviceID` via `EnumDisplayDevices(..., EDD_GET_DEVICE_INTERFACE_NAME)` so selections survive reboots and re-plugging.
- A `Presets` list of 5 built-in looks, applied in one click from Settings: **Aurora** (teal/violet, glow 0.6), **Sunset** (orange/pink), **Neon** (magenta/cyan, thick core), **Ember** (red/amber, slow), **Minimal** (soft white hairline, Idle Glow). A preset changes the appearance fields only.

## 2. Tray icon (H.NotifyIcon.Wpf)
- The icon shows a rounded-square outline with a glowing edge. Generate it as SVG in `assets/icon.svg`, then render a multi-size `.ico` (16/20/24/32/48/64/256) via a script in `build/` (use Svg.Skia or Magick.NET in a tiny console tool).
- The icon dims (50% opacity variant) when the glow is disabled.
- Tooltip: `Rimlight — {Title} · {Artist}`, or `Rimlight — Waiting for music`.
- Left-click: toggle the glow on/off. Double-click: open Settings.
- **Right-click menu:**
  ```
  ● Glow on                (checkable; shows hotkey)
  ──────────
  Mode ▸        Music Sync / Idle Glow / Off
  Colors ▸      From album art / Manual
  Presets ▸     Aurora / Sunset / Neon / Ember / Minimal
  ──────────
  Settings…
  Check for updates
  Open logs folder
  ──────────
  Quit Rimlight
  ```

## 3. Settings window (WPF + WPF-UI)
- `FluentWindow` with Mica backdrop, following the system light/dark theme. Size about 760×560, with a left `NavigationView`:
  1. **Appearance**: live preview at the top (a 16:9 rounded card rendering a miniature of the glow with the current settings and palette; reuse `LightEngine` and draw with WPF `DrawingVisual`/`WriteableBitmap` at about 30 fps only while the window is visible). Then the Thickness, Glow, Brightness, and Corner radius sliders, and the Cover taskbar toggle. Then the Presets row as clickable color chips.
  2. **Color**: Album art / Manual segmented control, the Override toggle, two color pickers (hex input + hue/sat picker), and the ratio slider. Show "Now playing: Title · Artist" with a tiny thumbnail and the two extracted color swatches.
  3. **Motion**: Mode (Music Sync / Idle Glow / Off), When silent, Sensitivity, and a small live level meter so users can see that audio is detected.
  4. **Displays**: a per-monitor list with friendly names (`DisplayConfigGetDeviceInfo` target name, e.g. "DELL U2720Q") and a toggle per monitor, plus FPS cap.
  5. **Behavior**: Pause in fullscreen, On battery, Hide from screen capture, Launch at startup, Hotkey recorder, Auto-update.
  6. **About**: version, "Check for updates", GitHub link, license, and a short privacy statement (same text as the README).
- Every change applies **live**, with no Save button. Persist with a 500 ms debounce.
- Closing the window only hides it. The app keeps running in the tray. On first close, show a one-time toast: "Rimlight is still running in the tray."
- Accessibility: every control has an AutomationProperties.Name, it's fully keyboard navigable, and it works at 200% text scaling.

## 4. Startup, single instance, hotkey
- **Startup:** use the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value pointing to the Velopack stable launcher path (not the versioned folder), with the `--background` argument (start without showing Settings).
- **Single instance:** a named `Mutex` (`Local\Rimlight-{user SID}`) plus a named pipe. The second instance sends "show-settings" and exits.
- **Hotkey:** `RegisterHotKey` on the message-only window. If registration fails (taken by another app), show an inline warning in Settings and let the user pick another.

## 5. Updates (Velopack)
- At startup (after 30 s) and every 12 h: `UpdateManager` with `GithubSource(repoUrl, prerelease: false)`. Download in the background and apply on next restart. Show a tray balloon: "Update ready — restart Rimlight to apply." with a "Restart now" action.
- Setting AutoUpdate = false disables background checks. The manual "Check for updates" still works.
