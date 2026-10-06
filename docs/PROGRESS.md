# Progress

## Lane A — Codex

- [x] H-003/H-004 handoff follow-up: safe empty preset catalog until C7; confirm consumer assumptions and cross-review PR #3 — PR #4 — effort: S. Core Release build: 0 warnings/errors; 2 tests passed after reproducing the original startup exception. Implemented, pending merge.

- [ ] C1 FFT, Hann window, band analyzer, auto-gain, envelopes
- [ ] C2 Spectral-flux beat detector, silence detection, `AnalyzerDiagnostics`, `AudioTuning`, real `IAudioAnalyzer` + zero-alloc test
- [ ] C3 `Rimlight.Bench` console + `tools/wav-analyze`: reads a WAV, runs the analyzer offline, prints beat timestamps/BPM, and writes a CSV and a PNG plot (ScottPlot or SkiaSharp) of Level/Bass/Beat/flux/threshold. Include a synthetic-track generator (kicks at a given BPM + noise + vocals-ish sines). **This lets beat tuning happen without Windows.**
- [ ] C4 Oklab/OkLCh, k-means palette extractor, glow-ify, gamut mapping, procedural test fixtures
- [ ] C5 `PaletteBlender` + gradient LUT fill (zero-alloc)
- [ ] C6 Real `LightEngine`: intensity formula, pulse, phase drift, idle breathing, silence fade, Visibility, `IsStatic`
- [ ] C7 Settings validation/clamping, JSON store (atomic, backup on corruption), version migration scaffold, Presets
- [ ] C8 CI: `ci.yml` (Linux job: Core.slnf build+test; Windows job: full sln build+test), labeler, `.github/release.yml`
- [ ] C9 Packaging: `build/pack.ps1` (Velopack, x64+ARM64, self-contained), `release.yml` on tag `v*` with `vpk upload github`, optional signing step gated on secrets
- [ ] C10 `tools/icon-gen`: SVG → multi-size `.ico` + PNGs (consumes `assets/icon.svg` from Lane B)
- [ ] C11 Community health files + issue/PR templates + CHANGELOG
- [ ] C12 Perf + soak harness in Bench: run analyzer + light engine for 8 simulated hours of synthetic audio, and report allocations, p99 frame cost, and memory
- [ ] C13 Delete `Fakes/` and make `CoreFactory` return the real implementations. Final Core API docs (`docs/CORE-API.md`).

## Lane B — Claude Code

- [x] K0 scaffold — by Codex — effort: M (validation details below)
- [x] K0 follow-up: Lane B fixes on Codex's K0 (tray Efficiency Mode off, Rimlight.exe, placeholder .ico, CsWin32 DPI check), HANDOFF H-003..H-005 — PR #3 — effort: S
- [ ] K1 Overlay windows, D3D11 + DirectComposition, `Glow.hlsl`, multi-monitor/DPI, device-loss recovery, topmost re-assert
- [ ] K2 `LoopbackCapture` + lock-free ring buffer + device-change restart. Render thread drains the ring → `IAudioAnalyzer` (fake until C2 lands)
- [ ] K3 Render loop: frame pacing, waitable swap chain, idle/static optimization driven by `ILightEngine.IsStatic`, battery fps cap, render scale
- [ ] K4 `NowPlayingService` (GSMTC), thumbnail decode to 64×64 BGRA → `IPaletteExtractor`, debounce/retry quirks, tray tooltip
- [ ] K5 System watchers: monitors, power/battery, lock, display-off, fullscreen detection, per-monitor pause
- [ ] K6 Tray icon + menu, single instance, hotkey, startup registration, first-run flow
- [ ] K7 Settings window (WPF-UI, Mica) with all 6 pages, live preview, presets row, monitor list with friendly names
- [ ] K8 `--debug-visualizer` window (binds `AnalyzerDiagnostics` + live `AudioTuning` sliders + "Copy params as JSON") and `--demo` mode
- [ ] K9 Velopack bootstrap in App (`VelopackApp.Build().Run()`), update checks, "Update ready" toast
- [ ] K10 Integration pass once C13 is merged: run everything for real, fix seams, and file HANDOFF entries for Core issues
- [ ] K11 Hardening on real Windows: sleep/wake, RDP, TDR, hotplug, device switching. Results go in PROGRESS.md.
- [ ] K12 `assets/icon.svg`, `assets/banner.svg/png`, README, repo settings text for Maxwell, recording instructions

## K0 notes

- Contracts are defined by doc 09 §3; source specs are unchanged. `AudioTuning` and `AnalyzerDiagnostics` are data containers, not DSP implementations. Doc 03 does not specify a numeric `minFlux`; K0 uses a provisional 0.01 pending C2 tuning.
- Palette-blender and settings-store fakes complete the five factory methods; they deliberately provide immediate palette changes and in-memory settings only. `Presets.All` remains a C7 placeholder.
- CI belongs to C8 under doc 09 and is not included. The solution filter includes Core + Tests; Bench arrives in C3.
- Windows tray startup and Quit require manual validation on Windows; Linux can only cross-build them.
- Verified with .NET SDK 8.0.425 on Linux: `dotnet build Rimlight.Core.slnf -c Release` and `dotnet build Rimlight.sln -c Release` both pass with 0 warnings / 0 errors. Both solution-filter and full-solution test runs pass: 1 passed, 0 failed, 0 skipped.
- H.NotifyIcon.Wpf is pinned to 2.3.2 (includes .NET 8 assets); 2.4.1 targets .NET 10. No DSP accuracy or performance figures apply to K0 fakes.
- Manual Windows check pending: run `dotnet run --project src/Rimlight.App -c Release`, verify the purple placeholder tray icon and tooltip, then choose “Quit Rimlight”; the icon and process should exit cleanly.

## Lane B notes

- K0 was implemented twice in parallel (Codex: PR #1, merged; Claude Code: unmerged). Maxwell chose to keep the merged one. The contracts in `src/Rimlight.Core/Contracts/` as merged in PR #1 are frozen; Lane B patched only its own lane on top.
- Tray: `ForceCreate(enablesEfficiencyMode: false)`. H.NotifyIcon's default (`ForceCreate()`) sets EcoQoS and `ProcessPriorityClass.Idle` for the whole process (`EfficiencyModeUtilities.SetEfficiencyMode`), which would starve the K3 render thread and K2 audio capture.
- The exe is `Rimlight.exe` (`AssemblyName = $(AppName)`), matching docs 06/07/08. The placeholder icon (`src/Rimlight.App/Assets/Rimlight.ico`, violet→cyan ring, 16–256 px) is both the tray icon and the exe icon until C10/K12 replace it.
- The tray tooltip appends a warning if the process is not Per-Monitor V2 DPI aware (CsWin32 check in `Rimlight.Platform/DpiAwareness.cs`), so Maxwell can see at a glance that the manifest took effect.
- Namespaces: Lane B never uses a `…System` namespace (doc 02's `Platform/System/` folder) because it would shadow `System`.
- Constraints agreed with Codex (H-004): **K8** must not read `AnalyzerDiagnostics` buffers from the UI thread while the render thread writes them. Copy a snapshot on the render thread and hand it to the visualizer. **K3** refills the gradient after every `SetTarget` and after every `Update` that was animating *before* the call, so the final crossfade frame is uploaded even though `IsAnimating` is then false. K3 never reads `IPaletteBlender.Current` per frame: shader colors come from the gradient, and `ILightEngine.Update` gets the palette last passed to `SetTarget`, so crossfades allocate nothing per frame (CLAUDE.md rule 4).
- Lane B review of the merged K0 (verified findings that are Lane B's to handle later; Core asks went to HANDOFF H-007..H-010):
  - **K6 (tray/shell):**
    - Take the single-instance mutex before any `TaskbarIcon` exists. A second instance otherwise reuses the same path-derived tray GUID and removes the first instance's icon.
    - Wrap `ForceCreate` in try/catch and retry on `TaskbarCreated`: at logon, `NIM_ADD` can fail before Explorer's tray is ready, which would crash startup.
    - Open the menu on `TrayKeyboardContextMenu` (Shift+F10 / Menu key) for keyboard access.
  - **K3:** render scale is runtime-only (Half when on battery with `BatteryBehavior.Reduce`); there is no Settings field for it.
  - **K1/K3 `--demo`:** Lane B drives its own synthetic beat and a debug phase override, because the fakes go away in C13 and `FakeLightEngine.Phase` is always 0.
  - **K4/K7:**
    - Lane B owns its sRGB hex ⇄ linear `Rgb` helper, because `Palette` exposes only linear RGB.
    - Compare settings changes field by field: record equality compares `CustomMonitorIds` by reference.
    - Run at most one `Extract` in flight (latest track wins).
    - Save on a debounced background task, catching `IOException`.
  - **K7:** the one-time "still running in the tray" toast is tied to the first-run session (`FirstRunComplete`), so it needs no new setting.
  - **Manual checklists while the fakes are active:** the fake beat flash peaks at 75–100% depending on frame timing, and the first flash after start isn't counted. That is a fake artifact, not a pacing bug.
