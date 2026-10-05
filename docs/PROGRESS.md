# Progress

## Lane A — Codex

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

### C1 completion log

- [x] C1 FFT, Hann window, band analyzer, auto-gain, envelopes — PR pending — effort: M. Completes the C1 item in the initial checklist above.
- Added internal audio building blocks and a zero-allocation `AudioFrontEnd`; C2 owns beat/silence detection, diagnostics integration, live tuning and the `CoreFactory.CreateAnalyzer` switch. Frozen contracts and every Lane B file remain unchanged.
- Spec clarifications: periodic Hann (`N` denominator), unnormalized forward FFT and one-sided magnitudes including DC/Nyquist; bands are [30,150), [150,2000), [2000,12000] Hz, clipped to available bins. Auto-gain peak decays in linear amplitude, floor tracks dB; leading exact silence does not initialize the floor, and the first positive sample initializes it to that sample's dB value. Initial sample history is zero-padded on the left. Empty input retains history; no-packet timeout belongs to C2. A sample-rate change resets history and envelopes.
- Validation: `dotnet build Rimlight.Core.slnf -c Release` — 0 warnings/errors; `dotnet test Rimlight.Core.slnf -c Release --no-build --logger "console;verbosity=detailed"` — 33 passed, 0 failed/skipped. Tests were written before implementation and initially failed compilation because the new types did not exist.
- Accuracy: independent DFT maximum component error 5.548456e-7 (N=8/32); Hann FFT peaks at the exact tested bins (3/64/511, N=2048); Parseval relative error 6.203781e-8. For the same modulated three-tone signal at 0 and -20 dB after 2 s warmup, maximum Level/Bass difference is 3.576279e-7 at 48 kHz and 2.980232e-7 at 44.1 kHz. Envelope attack/release reaches the expected 63.212%/36.788% response at one time constant.
- Informational timing on this Linux cloud machine, .NET SDK 8.0.425 Release: 53,842.7 ns/frame and **0 bytes/frame**, 48 kHz, 800 samples/frame, N=2048, 2,000 warmup + 1,000 measured frames. This measures the C1 front end only, excluding signal generation; it is not a latency guarantee or the C3/C12 benchmark harness. No real-DSP baseline or BPM accuracy applies before C2.

## Lane B — Claude Code

- [x] K0 scaffold — by Codex — effort: M (validation details below)
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
