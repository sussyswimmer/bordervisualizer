# 01 — Product Requirements

## Target user
Windows 10 (2004+) and Windows 11 users who listen to music while working or gaming and want their setup to feel alive. These include desk-setup and RGB enthusiasts, students, and streamers.

## Core features (v1.0)

### 1. Edge glow overlay
- A glow drawn along all four edges of each enabled monitor, fading inward.
- Fully **click-through**, **topmost**, **never focusable**, hidden from the taskbar and Alt-Tab.
- Correct on multi-monitor and mixed-DPI setups. Rebuilds automatically when monitors are added, removed, or rearranged.
- Optional rounded corners (`CornerRadius` setting) for screens with rounded corners.

### 2. Animation modes
| Mode | Behavior |
|---|---|
| **Music Sync** (default) | Brightness and width follow loudness. A pulse fires on each beat (kick/bass onset). The color gradient drifts slowly around the perimeter. |
| **Idle Glow** | A steady glow with a very slow "breathing" (about a 6 s sine, ±10% brightness). Ignores audio. |
| **Off** | Nothing rendered. The render loop is fully stopped. |

In **Music Sync**, when no audio is detected for 2 s, fade over 1.5 s to the `WhenSilent` behavior (`Idle Glow` or `Hide`, user setting). Fade back in within 150 ms when audio returns.

### 3. Color modes
| Mode | Behavior |
|---|---|
| **Album Art** (default) | Two colors extracted from the current track's thumbnail. Crossfade over 800 ms on track change. Fall back to the manual colors if there is no thumbnail. |
| **Manual** | User picks Primary + Secondary colors. |

- A **Primary/Secondary ratio** slider (default 60/40) controls how much of the perimeter each color occupies.
- **Override album color**: a toggle that forces Manual colors even when Album Art is selected.

### 4. Appearance controls
- **Thickness**: the solid core line, 0–40 px (DIPs), default 6.
- **Glow**: how far the light spills inward, 0–100%. 0 means a hairline; 100 means about 35% of the shorter screen dimension.
- **Brightness**: 10–100%, default 80.
- **Sensitivity**: how strongly audio drives the light, 0.25×–2×, default 1×.
- **Monitors**: All / Primary only / a checkbox per monitor.

### 5. Smart pausing
- Pause rendering when a **fullscreen exclusive / D3D fullscreen app** is running, or when presentation mode / Do Not Disturb is active (setting, default on).
- Pause when the **session is locked** or the **display is off**.
- **On battery**: cap at 30 fps (setting: "Reduce on battery", default on), or pause entirely (setting).
- Optional: **Hide from screen capture** (`WDA_EXCLUDEFROMCAPTURE`), default **off**. Default off matters because Maxwell needs to record demo GIFs.

### 6. Shell
- Tray icon with a quick menu (see doc 06).
- Settings window with a live preview and a modern Fluent/Mica look.
- Launch at startup (default on after first run).
- Global hotkey to toggle the glow on/off: **Ctrl+Alt+L** (configurable).
- Single instance. A second launch opens the Settings window of the running instance.
- Auto-updates via Velopack/GitHub Releases. The update check is the only network request the app ever makes.

### 7. First run
- On the first launch, show the Settings window with a small welcome banner: "Play some music, then look at the edges of your screen."
- No account and no license key. The app is free and open-source (MIT).

## Privacy (must be true and stated in the README)
- Only system output audio is captured, via WASAPI loopback. The microphone is never opened.
- Audio is analyzed in memory and immediately discarded. Nothing is recorded or saved.
- Track info and artwork are read locally from Windows' media controls and never leave the device.
- No telemetry or analytics. The only network call is the GitHub update check.

## Non-goals for v1
- macOS/Linux (there will be a separate native macOS app later).
- Lock-screen widgets. Windows doesn't allow third-party lock-screen drawing.
- Driving real RGB hardware or smart lights. This is listed as a stretch goal in doc 07.
- Following on-screen colors via screen capture. Also a stretch goal.
- Licensing or payments.
