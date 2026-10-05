# 07 — Build Plan

> **Two agents build this.** This doc describes *what* each phase must achieve and how it is accepted. **Who** builds each part is defined in `docs/09-WORK-SPLIT.md` (Codex task IDs C1–C13, Claude Code task IDs K0–K12), and doc 09 wins where the two differ. For example, CI is task C8, not part of Phase 0. The phase acceptance criteria below still apply and are checked at doc 09's sync points.

Work through the phases in order. Each phase ends with: zero-warning Release build, all tests green, `PROGRESS.md` updated, a commit, and (for visual phases) a **Manual test checklist** for Maxwell in your summary. Don't start the next phase until the current phase's acceptance criteria are met or the gap is written down in `PROGRESS.md` with a reason.

---

## Phase 0 — Scaffold
- `git init`, `.gitignore` (VisualStudio template), `.editorconfig`, MIT `LICENSE` (copyright "Maxwell Olander").
- Solution + 4 projects from doc 02. `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`, `AppName=Rimlight`, `Version=0.1.0`.
- App manifest: PerMonitorV2 DPI awareness, `supportedOS` Win10/11, `asInvoker`.
- CsWin32 set up with `NativeMethods.txt`.
- Create `docs/PROGRESS.md` with every phase as a checklist.
- A GitHub Actions CI workflow (`windows-latest`): restore, build Release, test.

**Accept:** `dotnet build` and `dotnet test` pass. The app launches, shows a tray icon (placeholder icon is fine), and "Quit" exits cleanly.

## Phase 1 — Static glow overlay
- `OverlayWindow` + `OverlayManager` + D3D11/DirectComposition renderer + `Glow.hlsl` (doc 04), with hard-coded colors and settings.
- One overlay per monitor. Rebuild on display change. Mixed DPI is handled.
- Device-loss recovery path (test it by calling `D3D11Device.Release` in a debug-only tray item, or use `dxcap -forcetdr` if available).

**Accept:** A soft two-color glow appears on every monitor's edges. Clicking, scrolling, dragging, and typing through it all work. It's not in Alt-Tab or the taskbar. Plugging or unplugging a monitor rebuilds overlays within 1 s. CPU at a static 60 fps is under 1%. **Manual test checklist required.**

## Phase 2 — Audio engine + debug visualizer
- `LoopbackCapture`, the ring buffer, FFT, bands, auto-gain, envelopes, beat detector, and silence detection (doc 03).
- `--debug-visualizer` window with live-tunable params.
- All doc 03 tests.

**Accept:** The visualizer shows a responsive spectrum. On a 4-on-the-floor track the beat counter lands within ±2 BPM of the real tempo. Changing the OS volume doesn't change Level much after 2 s. Switching the default output device (speakers ↔ headphones) keeps it working. Zero allocations per analysis frame (test). **Manual test checklist required**, including 3 suggested test tracks of different genres for Maxwell to try.

## Phase 3 — Music-reactive light
- `LightEngine` maps AudioFeatures + Settings + Palette to `LightState`. `Intensity = Brightness × (0.35 + 0.65 × Level)`. Pulse comes from Beat. Phase drifts at 0.015 cycles/s, plus a small kick of 0.01 on each beat.
- Idle Glow breathing, silence fade, the Off state, and the idle-CPU optimization (doc 02 frame pacing).
- `--demo` mode.

**Accept:** The light visibly "hits" on kicks with no perceptible lag. It fades to idle 2 s after music pauses and comes back instantly. While playing music at 60 fps on 1–2 monitors, CPU is under 2% (measure with Task Manager or `dotnet-counters`, and report numbers). In Off mode the process uses about 0% CPU and doesn't Present. **Manual test checklist required.**

## Phase 4 — Album colors
- `NowPlayingService` (GSMTC), the palette extractor, the palette blender, and the gradient texture (doc 05). All doc 05 tests.

**Accept:** Switching songs in Spotify and on YouTube in a browser crossfades the glow to colors that clearly match the cover. The fallback works when there is no art. The tray tooltip shows the current track. **Manual test checklist required.**

## Phase 5 — Settings, tray, shell
- Settings model + persistence + validation, the full tray menu, the Settings window with live preview, presets, per-monitor selection, startup, single instance, hotkey, and first-run welcome (doc 06).
- Fullscreen/battery/lock/display-off pausing (doc 04 §4, doc 02).

**Accept:** Every setting applies live and survives a restart. A corrupt settings.json doesn't crash the app. The hotkey toggles the glow. Launching a second time opens Settings. A fullscreen game or fullscreen YouTube pauses the glow on that monitor only. Locking the PC stops rendering. **Manual test checklist required.**

## Phase 6 — Packaging & updates
- Velopack: `build/pack.ps1` publishes self-contained, trimmed-where-safe (WPF and WinRT don't trim well; disable trimming if it breaks anything and note it), for win-x64 and win-arm64. Produces `RimlightSetup.exe` plus a portable zip.
- Auto-update wired to GitHub Releases (doc 06 §5).
- `release.yml` workflow: on tag `v*`, build, pack, and upload the release assets with `vpk upload github`. Generate release notes from Conventional Commits.
- **Code signing:** write a placeholder step that only runs when the `SIGNING_*` secrets exist (for Azure Trusted Signing later). Document in the README that unsigned builds may show SmartScreen's "More info → Run anyway".

**Accept:** Installing from `RimlightSetup.exe` on a clean machine or user works with no admin prompt. Installing an older build and then publishing a newer test release causes the update to download and apply. Uninstalling from Windows Settings removes everything except the settings/logs folders. Document that in the README FAQ.

## Phase 7 — Hardening
- 8-hour soak in `--demo` mode: memory stays flat (report working set at start and end). No handle leaks.
- Sleep/wake, hibernate, RDP session connect/disconnect, monitor turned off and on, and GPU driver update (TDR) are all recovered from.
- Run the debug visualizer against at least 5 genres and finalize the `AudioTuning` defaults.
- Fix every TODO, and remove dead code and debug-only UI from Release builds (keep `--debug-visualizer`, it's useful).

**Accept:** A list of the scenarios tested and their results in `PROGRESS.md`.

## Phase 8 — GitHub presentation
Follow `docs/08-GITHUB-PRESENTATION.md` exactly. This phase matters as much as the code: it decides whether anyone downloads the app.

---

## Stretch goals (only after Phase 8, and only if Maxwell asks)
1. **Real RGB devices:** the Windows Dynamic Lighting API (`Windows.Devices.Lights.LampArray`) drives keyboards, mice, and light strips in sync with the glow.
2. **Philips Hue Entertainment** sync, so the room lights follow the music.
3. **Screen color mode:** sample the edge colors of the desktop with `Windows.Graphics.Capture` at low resolution (ambilight style).
4. **Per-app rules:** e.g. disable while Teams/Zoom is in a call.
5. **winget + Microsoft Store** listings.
