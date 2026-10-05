# 02 — Architecture

## Solution layout

```
Rimlight/
├─ Rimlight.sln
├─ CLAUDE.md
├─ README.md                      # written in the final phase (doc 08)
├─ LICENSE                        # MIT
├─ docs/                          # these spec files + PROGRESS.md
├─ assets/                        # icon sources, README banner, GIFs, screenshots
├─ build/                         # Velopack pack script, icon generation script
├─ .github/                       # workflows, issue templates (doc 08)
└─ src/
   ├─ Directory.Build.props       # AppName, version, nullable, TreatWarningsAsErrors, LangVersion latest
   ├─ Rimlight.Core/              # net8.0 — NO Windows/UI references
   │  ├─ AppInfo.cs
   │  ├─ Audio/                   # Fft, BandAnalyzer, BeatDetector, Envelope, AudioFeatures
   │  ├─ Color/                   # Oklab, PaletteExtractor, PaletteBlender
   │  ├─ Lighting/                # LightState, LightEngine (features + palette + settings → LightState)
   │  └─ Settings/                # Settings model, defaults, JSON (de)serialization, migration
   ├─ Rimlight.Platform/          # net8.0-windows10.0.19041.0 — Win32/WinRT/D3D code
   │  ├─ Audio/LoopbackCapture.cs
   │  ├─ Media/NowPlayingService.cs
   │  ├─ Overlay/                 # OverlayWindow, OverlayManager, D3D renderer, Glow.hlsl
   │  ├─ System/                  # MonitorWatcher, PowerWatcher, FullscreenDetector, HotkeyService, StartupRegistration
   │  └─ NativeMethods.txt        # CsWin32 API list
   ├─ Rimlight.App/               # WPF exe — composition root, tray, settings UI, Velopack bootstrap
   └─ Rimlight.Tests/             # xUnit, references Core only
```

## Threads and data flow

```
[WASAPI loopback thread]            [Media events (WinRT)]          [UI thread (WPF dispatcher)]
  float frames ─┐                      track changed ─┐                 settings edits ─┐
                ▼                                     ▼                                 ▼
        lock-free SPSC ring buffer         PaletteExtractor (threadpool)      immutable Settings snapshot
                │                                     │                                 │
                ▼                                     ▼                                 ▼
             [Render thread: one dedicated thread, paced by DwmFlush / frame timer]
              1. drain ring → FFT → BandAnalyzer → BeatDetector → AudioFeatures
              2. LightEngine.Update(dt, features, palette, settings) → LightState
              3. for each OverlayWindow: write constant buffer, draw, Present
```

Rules:
- **Audio callback** (NAudio `DataAvailable`): convert to mono float and copy into the ring buffer. Nothing else. No locks, no allocation, no logging.
- **Render thread** owns all D3D objects and all DSP state. One thread renders all monitors, so DSP runs once per frame, not once per monitor.
- **Palette extraction** runs on the threadpool and publishes the result with `Volatile.Write` of an immutable `Palette` record.
- **Settings** are an immutable record. The UI publishes a new snapshot with `Interlocked.Exchange`, and the render thread reads it once per frame.
- Hold all cross-thread state in immutable records or the ring buffer. No `lock` in the per-frame path.

## Frame pacing
- Target the refresh rate of the primary monitor, capped by setting (default cap 60 fps; options 30/60/120/Native).
- Pace with `DwmFlush()` when uncapped at native rate. Otherwise use a high-resolution waitable timer (`CreateWaitableTimerExW` with `CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`).
- **Idle optimization:** when the target state is fully static (Off, Hide, or Idle Glow at constant brightness), stop presenting. In Idle Glow, render at 10 fps (breathing is slow). When there is nothing to show at all, block the render thread on an event until something changes. CPU must be ~0%.

## Key types (Core)

```csharp
public readonly record struct AudioFeatures(
    float Level,        // 0..1 smoothed loudness (attack/release enveloped)
    float Bass,         // 0..1 smoothed low-band energy
    float Beat,         // 0..1 decaying pulse, jumps to 1 on detected beat
    bool  IsSilent);    // true after SilenceHoldMs of near-silence

public sealed record Palette(Rgb Primary, Rgb Secondary, string? SourceTrackId);

public readonly record struct LightState(
    Rgb ColorA, Rgb ColorB, float Ratio,     // gradient colors + 60/40 split
    float Intensity,                          // 0..1 final brightness multiplier
    float Spread,                             // 0..1 glow reach
    float CoreThicknessDip,                   // solid line thickness in DIPs
    float Phase,                              // 0..1 gradient rotation around the perimeter
    float Pulse);                             // 0..1 beat pulse, used for a brief width/brightness kick
```

`LightEngine` is a pure function plus internal smoothing state. All the "feel" lives here and in the audio engine, and it's fully unit-testable.

## Lifetime events (Platform → App)
- `MonitorWatcher`: `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, and `WM_SETTINGCHANGE` (work area) → `OverlayManager.Rebuild()` debounced by 300 ms.
- `PowerWatcher`: `WM_POWERBROADCAST` (suspend/resume, AC/DC via `PBT_APMPOWERSTATUSCHANGE`), `WTSRegisterSessionNotification` (lock/unlock), and `RegisterPowerSettingNotification(GUID_CONSOLE_DISPLAY_STATE)` (display off).
- `LoopbackCapture`: subscribe to `IMMNotificationClient.OnDefaultDeviceChanged` and restart capture on the new default render device. Retry with backoff (0.5 s → 5 s) on failure.
- **GPU device loss** (`DXGI_ERROR_DEVICE_REMOVED/RESET` on Present): tear down and recreate the device and all swap chains, then continue.
- Create a hidden message-only window (`HWND_MESSAGE`) in Platform to receive these messages.

## Logging
- `Microsoft.Extensions.Logging` with a small rolling file sink in `%LOCALAPPDATA%\Rimlight\logs\` (max 5 files × 1 MB).
- Never log in the audio callback or the per-frame path, except rate-limited errors.
- Add a tray item "Open logs folder" to help with bug reports.

## Settings storage
- `%APPDATA%\Rimlight\settings.json`. Include a `version` field for migrations. Write atomically (write temp file, then `File.Replace`).
