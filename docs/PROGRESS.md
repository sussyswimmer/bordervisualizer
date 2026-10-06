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

### C1 completion log

- [x] C1 FFT, Hann window, band analyzer, auto-gain, envelopes — PR [#2](https://github.com/sussyswimmer/bordervisualizer/pull/2) — effort: M. Completes the C1 item in the initial checklist above.
- Added internal audio building blocks and a zero-allocation `AudioFrontEnd`; C2 owns beat/silence detection, diagnostics integration, live tuning and the `CoreFactory.CreateAnalyzer` switch. Frozen contracts and every Lane B file remain unchanged.
- Spec clarifications: periodic Hann (`N` denominator), unnormalized forward FFT and one-sided magnitudes including DC/Nyquist; bands are [30,150), [150,2000), [2000,12000] Hz, clipped to available bins. Auto-gain peak decays in linear amplitude, floor tracks dB; leading exact silence does not initialize the floor, and the first positive sample initializes it to that sample's dB value. Initial sample history is zero-padded on the left. Empty input retains history; no-packet timeout belongs to C2. A sample-rate change resets history and envelopes.
- Validation: `dotnet build Rimlight.Core.slnf -c Release` — 0 warnings/errors; `dotnet test Rimlight.Core.slnf -c Release --no-build --logger "console;verbosity=detailed"` — 33 passed, 0 failed/skipped. Tests were written before implementation and initially failed compilation because the new types did not exist.
- Accuracy: independent DFT maximum component error 5.548456e-7 (N=8/32); Hann FFT peaks at the exact tested bins (3/64/511, N=2048); Parseval relative error 6.203781e-8. For the same modulated three-tone signal at 0 and -20 dB after 2 s warmup, maximum Level/Bass difference is 3.576279e-7 at 48 kHz and 2.980232e-7 at 44.1 kHz. Envelope attack/release reaches the expected 63.212%/36.788% response at one time constant.
- Informational timing on this Linux cloud machine, .NET SDK 8.0.425 Release: 53,842.7 ns/frame and **0 bytes/frame**, 48 kHz, 800 samples/frame, N=2048, 2,000 warmup + 1,000 measured frames. This measures the C1 front end only, excluding signal generation; it is not a latency guarantee or the C3/C12 benchmark harness. No real-DSP baseline or BPM accuracy applies before C2.

- C1 review fixes (made by Claude Code on Maxwell's instruction, because Codex had not picked up the Lane B review of #2):
  - **Near-silence hold:** auto-gain freezes its floor while the quietest quarter of the analysis window is at or below `SilenceThresholdDb` − 20 dB (−80 dBFS), and on zero or non-finite band values. The peak keeps tracking and decaying as usual. Per-quarter gating catches the frames where a gap or the music only partly fills the window: Hann weighting collapses the band values there even though the whole-window RMS is high.
    - Gaps tested: digital-zero and −96 dBFS dither gaps starting and ending anywhere inside a hop, leading silence at any offset, and a quieter track after a 30 s pause.
    - Result: Level and Bass stay within 0.1 of an uninterrupted (or music-only) run from 2 s after resume; measured max deviation 0.000.
    - Quiet but real music above −80 dBFS keeps adapting as doc 03 describes.
  - **Non-finite input:** NaN/±Inf samples become 0 and finite samples are clamped to ±16 on entry to the ring, on all three copy paths. AutoGain and Envelope also ignore non-finite input, so one bad sample can't latch Level/Bass to NaN.
  - **Level weights** are independent live-tuning sliders (doc 03 §4). Lane B bounds each to 0..1 (doc 03 sets no range), so the sum-to-1 check is gone and the Level target is clamped to 0..1.
  - **Empty input** returns early instead of recomputing an identical spectrum (about 30 µs → about 0.5 µs per no-packet frame).
  - **Tests (63):** gap edges at varied offsets, leading silence, a quiet track after a pause, NaN/±Inf on every copy path, huge samples (spectrum finite, signal still tracked), the near-silence threshold and the quarter-RMS definition, irregular-feed allocations, and a hand-wired doc 03 reference pipeline with a dither gap (band, weight, tau and hold wiring). Mutation checks: the previous whole-window gate, a frozen peak, un-held mid/high gains and a missing clamp each fail at least two tests.
  - **Known limit, follows doc 03 as written:** a slow fade-out or fade-in, or a breakdown that empties one band (e.g. no bass for 15 s), still drops that band's floor by tens of dB. The next 20–40 s are then compressed toward 1 (Level ≈ 0.9 after an 8 s fade, against 0.46). Options for the sync point 2 tuning round: limit each band's floor relative to its peak, raise the floor faster when the value sits far above it, or hold a band while it is far below its own decayed peak.
  - **Left for C2:** the no-packet policy (stale window on empty spans), and bass resolution at 96/192 kHz with N = 2048.

- C1 review follow-up by Codex — PR #6 — effort: S. Independently verified Claude's PR #5 at `34d10d4` (63 passed, zero-warning Core Release build). Found that supported `WindowSize = 2` divided by a zero quarter length, producing NaN and disabling the near-silence hold. Use one-sample blocks for that smallest window; the default 2048-sample behavior stays unchanged. Two regression cases failed before the correction; all 68 tests pass afterward, with 0 warnings/errors. Tests also cover sizes 4/8/2048, zero input and reset.
- Follow-up measurements on .NET SDK 8.0.425 Release, 48 kHz, 800 samples/frame, N=2048, 2,000 warmup + 1,000 measured frames: **31,172.9 ns/frame, 0 bytes/frame**. Informational timing only (independent PR #5 sample: 29,976.6 ns/frame). FFT/reference and Parseval errors remain 5.548456e-7 and 6.203781e-8; cold-start Level/Bass volume-invariance error remains at most 3.576279e-7. These measurements do not establish 2-second convergence after a mid-stream OS-volume step; that and the fade/one-band limits above remain tuning concerns.

- [x] C2 spectral-flux beat detector, silence detection, `AnalyzerDiagnostics`, live `AudioTuning`, real `IAudioAnalyzer` + zero-alloc tests (made by Claude Code on Maxwell's instruction while Codex was idle; stacked on the C1 PR) — effort: L
  - **Results (procedural signals only; 164 tests in the suite):**
    - Kick tracks at 90/120/128/174 BPM are found within ±2 BPM (measured within 0.15) at 44.1 and 48 kHz with ±20 % frame jitter, and every expected beat is detected.
    - A kick track and a music-like mix (kick, bass line, hats, pad, vocal tones) keep ≥ 95 % of kicks (100 % measured) and no false beats at 8, 20, 30, 60, 144 and 240 fps, continuous or in 10/20/50 ms packets, and at Sensitivity 0.25–2. Tempo stays within ±2 BPM; 50 ms packets quantize beat times, and the worst estimate measured is 122.0 for 124 BPM.
    - 88.2–384 kHz capture keeps tempo within ±2 BPM and fires no beats on noise.
    - There are 0 beats on 60 s of white noise at three levels and across those feeds, and 0 on silence or missing packets.
    - Beats are volume-invariant: a music-like mix gives the same beats at 0, −20, −40 and −60 dB, and noise down to −95 dBFS fires none.
    - Cost on this Linux cloud machine (Release, informational): about 42 µs per frame at 48 kHz/60 fps, 70 µs at 30 fps, 110 µs at 192 kHz, and 1.2 µs per idle frame (no packets). 0 bytes per frame across irregular feeds, stall bursts, decimated rates and rate changes.
    - Every guard below has a mutation check: reverting it fails at least one test.
  - **Spec clarifications and deviations** (AGENTS.md standard 1):
    - **Flux units:** flux and `Diagnostics.Spectrum` are in amplitude units (|X|·4/N, so a full-scale sine centered on a bin reads ≈ 1).
    - **Step size (deviation from doc 03's "hop = whatever arrived"):** new samples are analyzed in equal steps of at most one 60 fps frame of audio (800 samples at 48 kHz). Flux grows with the hop, so at 30 fps or with 50 ms packets kicks stopped standing out. The newest samples are always the last step, so no latency is added, and frames up to that size behave exactly as doc 03 says. On displays faster than 240 Hz, frames shorter than 1/240 s fold into the next beat step (at most ~4 ms of extra beat latency there), so the 1024-entry flux history always covers 4 s (Codex review on #7).
    - **High sample rates:** capture at 75 kHz or more is decimated by 2/4/8 (anti-aliased, flat to 0.1 dB up to 10 kHz, aliases ≥ 50 dB down), so the bass band keeps its ~5 bins. `CoreFactory.AnalysisSampleRate(rate)` gives the rate that maps `Diagnostics.Spectrum` bins to Hz.
    - **Median onset guard** (`MedianOnsetRatio` = 10, internal): a beat also needs flux > 10 × the median flux of the history window. Without it, mean + 1.5σ fires several times a second on steady white noise, because the bass band spans only ~5 bins and its flux is heavy-tailed. `ThresholdHistory` plots the effective threshold, the larger of the two. It could become an `AudioTuning` field at the next approved contract change.
    - **Sensitivity (deviation):** it scales k and the guard's excess over 1 by 1/√Sensitivity rather than 1/Sensitivity, because 1/s gives k = 6 at 0.25, which turns beats off. Weak kicks under heavy noise are found at 5/29/73/98 % for Sensitivity 0.25/0.5/1/2. At Sensitivity 2, full-scale white noise gives 0–3 false beats a minute. Sensitivity also multiplies Level (clamped to 0..1). It is applied once, here, so C6 must not apply it again (H-007).
    - **Real time:** frames without new samples carry their time into the next flux entry and into silence timing, so warm-up (a quarter of `FluxHistorySeconds`), the history window and `SilenceHoldMs` are real seconds at any frame rate or packet size.
    - **No packets:** after `NoPacketTimeoutSeconds`, missing packets are fed as zeros, so the spectrum, auto-gain hold, flux history and `IsSilent` behave as for digital silence. `IsSilent` arrives 2.0 s after the last packet (the timeout counts toward the hold). Once the window is all zeros the FFT is skipped. Silence is measured in order, in 1/60 s chunks, so the trailing silence of a burst drained after a stall counts toward the hold (Codex review on #7).
    - **Render stalls:** more audio in one frame than the window plus ~0.27 s means the render loop stalled. Only the newest window plus four steps is analyzed. The refilled window only seeds the detector, and the flux statistics warm up again (0.25 s); the tempo is kept. An 8 fps render loop is not a stall (tested).
    - **Tempo:** `EstimatedBpm` is the median of the last 16 beat intervals up to 2 s, and returns to 0 after 4 s without a beat.
    - **Live tuning:** bins that enter the bass band only seed their history, so widening the band fires no beat. A `WindowSize` change reallocates inside the `Tuning` setter, resets, and replaces `Diagnostics`. A bass band narrower than one bin (narrow edges, small windows) uses the bin just below its center, never DC or the Mid band's first bin, so Bass and beats don't go silent. Invalid tuning throws and keeps the previous value: `WindowSize` must be a power of two from 1024 to 16384 (at 256, 30–150 Hz falls between 48 kHz bins; Codex review on #7), `Sensitivity` 0.25–2, `NoPacketTimeoutSeconds` ≥ 0.03 s, and `FluxHistorySeconds` at most 4 s. A non-finite or negative dt counts as 0, and dt is capped at 1 h.
    - **Reset:** clears everything except the current `IsSilent`, so a device change while nothing plays doesn't flash a hidden glow back on for 2 s. Audio above −55 dBFS clears it at once.
    - **Diagnostics:** one history entry per `Process` call (240 entries, oldest first, zero until filled). Each flux entry is the largest flux of that call's steps. These semantics are in the XML remarks on `CoreFactory.CreateAnalyzer` (H-007).
  - **`MinFlux` is relative to the level (spec clarification, Codex review on #7):** doc 03's minFlux exists "to avoid noise during silence", but as an absolute floor (the provisional 0.01) it dropped every beat at −40 dB, for example a player's own volume at about 10 %. A beat now needs flux > `MinFlux` × the window's RMS, and no beat fires while that RMS is at or below the near-silence level (`SilenceThresholdDb` − 20 dB, −80 dBFS, the same level as the auto-gain hold). The default 0.01 then rarely binds, because kicks score 0.3–2 on that scale; around 0.6 it trims weak onsets. `ThresholdHistory` includes it. No contract default had to change, so H-005 is not needed for C2.
  - **Factory and fakes:** `CoreFactory.CreateAnalyzer` now returns `AudioAnalyzer`, and `FakeAnalyzer` is deleted. `FakeLightEngine` now uses Brightness × (0.35 + 0.65 × Level), so the glow keeps its floor with the real analyzer until C6 (H-007).

## Lane B — Claude Code

- [x] K0 scaffold — by Codex — effort: M (validation details below)
- [x] K0 follow-up: Lane B fixes on Codex's K0 (tray Efficiency Mode off, Rimlight.exe, placeholder .ico, CsWin32 DPI check), HANDOFF H-003..H-005 — PR #3 — effort: S
- [x] K1 Overlay windows, D3D11 + DirectComposition, `Glow.hlsl`, multi-monitor/DPI, device-loss recovery, topmost re-assert — PR #8 — effort: L (notes below; Windows manual test pending)
- [x] K2 `LoopbackCapture` + lock-free ring buffer + device-change restart. Render thread drains the ring → `IAudioAnalyzer` (real since C2) — PR #9 — effort: M (notes below; Windows manual test pending)
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

## K1 notes

- **Structure (`src/Rimlight.Platform/Overlay/`):**
  - `OverlayHost` runs one overlay thread that owns every window and GPU object and pumps their messages. Nothing crosses threads except the settings snapshot (`Volatile`) and two request flags, so there are no locks.
  - The thread waits on a high-resolution waitable timer, a wake event and the message queue (`MsgWaitForMultipleObjectsEx`).
  - The app supplies each frame through `IOverlayFrameSource`: a `LightState` plus the 64-texel gradient, refilled only when the colors change. K1's `StaticGlowSource` (App) draws the default settings' manual colors. K3 replaces it with analyzer → light engine.
- **Windows (doc 04 §1):**
  - `WS_POPUP` with `WS_EX_NOREDIRECTIONBITMAP | LAYERED | TRANSPARENT | TOPMOST | TOOLWINDOW | NOACTIVATE`, `SetLayeredWindowAttributes(255)`, `HTTRANSPARENT` / `MA_NOACTIVATE`, `DWMWA_EXCLUDED_FROM_PEEK`, and `WDA_EXCLUDEFROMCAPTURE` when Hide from screen capture is on.
  - Each window is created directly at its monitor's physical rectangle, so the initial placement never sends `WM_DPICHANGED`. Later `WM_DPICHANGED` messages are ignored apart from triggering a rebuild.
  - Topmost is re-asserted on `EVENT_SYSTEM_FOREGROUND`, at most every 250 ms, with a trailing re-assert so the last change in a burst still counts.
- **Monitors:**
  - One overlay per selected monitor (All / PrimaryOnly / Custom by `DISPLAY_DEVICE.DeviceID`), at `rcMonitor` or, with Cover taskbar off, `rcWork`.
  - A hidden top-level helper window receives `WM_DISPLAYCHANGE` and `WM_SETTINGCHANGE(SPI_SETWORKAREA)` (message-only windows don't get broadcasts). Overlays receive `WM_DPICHANGED`.
  - Each of these schedules a rebuild 300 ms later. The rebuild matches overlays by GDI device name, so unchanged overlays don't flicker.
  - DIP sizes become pixels at each monitor's `GetDpiForMonitor` effective DPI.
- **GPU (doc 04 §2):**
  - One D3D11 device (hardware, else WARP) and one DirectComposition device are shared by every overlay.
  - Each overlay has a composition swap chain: B8G8R8A8_UNORM, premultiplied, FLIP_SEQUENTIAL, 2 buffers, maximum frame latency 1. Its latency object is polled without blocking, so a frame is skipped rather than queued when DWM or the display is behind.
  - `DXGI_ERROR_DEVICE_REMOVED/RESET` from Present, resize or creation releases everything. The device and surfaces are recreated on the next frame and retried every second while that fails; the windows stay.
  - Debug builds have a tray item, "Simulate GPU device loss (debug)", that runs the same path (doc 07 Phase 1).
  - `Glow.hlsl` is embedded and compiled at runtime (vs_4_0 / ps_4_0, feature level 10.0+). The gradient texture is R16G16B16A16_FLOAT, which every feature level ≥ 10.0 must be able to filter (32-bit float filtering is optional there).
- **Spec clarifications and deviations (doc 04 §3):**
  - **Rounded glow field (deviation):** the glow's distance field has corners of radius ≥ 2 × spread. With doc 04's square field, the glows from two edges meet in a visible 45° crease (a picture-frame bevel); rounded, the light pools softly into the corners. The solid core still follows `CornerRadiusDip`.
  - **Perimeter coordinate:** arc length along a rounded path (radius ≥ 6 × spread and ≥ 2 × the core), starting at the top-left corner and running clockwise (H-008). It is continuous through the corners, at most 0.0008 per pixel within 150 px of the edge. Its seam (the medial axis, ≥ that radius inside) is hidden: between 3 and 6 spreads deep the colour blends to the gradient's mean, so a wide glow that reaches the screen centre shows no seam.
  - **Glow → spread (PRD §1):** "0 means a hairline; 100 means about 35% of the shorter screen dimension". Spill is taken as three e-folding distances, with a squared slider so it feels even. The e-folding distance is max(1 DIP, Glow² × 0.35 / 3 × shorter side); the default 0.45 gives about 25 px at 1080p.
  - **Core at thickness 0** draws no line.
  - **sRGB:** the gradient is linear RGB (H-008). The 8-bit swap chain is read by DWM as sRGB-encoded, so the shader encodes before premultiplying.
  - **Dither** is ±0.5/255 interleaved gradient noise on alpha.
  - **Visibility** multiplies alpha like Intensity (H-008).
- **Left for later tasks:** FpsCap "native" (0) runs at 60 fps until K3 adds DwmFlush pacing. K3 also adds idle/static throttling, Half render scale and the battery cap. K5 adds fullscreen pause and power/lock watchers. Logging is `Trace` (rebuilds, GPU state) until logging lands. The tray tooltip, settings UI and live settings come in K6/K7.
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes.
  - `Glow.hlsl` compiles under DXC with warnings as errors (vs_6_0/ps_6_0, `-HV 2018`).
  - A NumPy port of the pixel shader renders the expected look and checks perimeter continuity. Every Vortice and CsWin32 call was checked against the 3.8.3 API surface and the generated P/Invoke sources.
  - An adversarial multi-agent review (Win32, D3D/DComp, shader, threading, monitors, spec/performance) runs on this PR; its upheld findings are fixed here before merge.
  - Nothing has run on Windows yet: Maxwell's manual checklist is in the PR.

## K2 notes

- **Structure:**
  - `LoopbackCapture` (`src/Rimlight.Platform/Audio/`) runs a supervisor on its own MTA thread; NAudio's WASAPI objects must not live on the WPF STA thread.
  - Each capture session opens the default render endpoint (`DataFlow.Render`, `Role.Multimedia`) in loopback. It converts every packet to mono float and writes it into an `AudioRingBuffer`, all on the audio thread.
  - `LoopbackCapture.Current` publishes the session's `CapturedAudio` (ring, sample rate, device name, format). A new instance after every restart signals a device change.
  - `MusicGlowSource` (App, replacing K1's static source) runs on the overlay thread each frame: it drains the ring, calls `analyzer.Process`, then `lightEngine.Update`, and returns the `LightState`. On a device change it calls `analyzer.Reset()`, which keeps `IsSilent`. Analyzer `Sensitivity` comes from settings (H-007).
- **Ring buffer:**
  - Single producer, single consumer; capacity is 2 s rounded up to a power of two (131,072 samples at 44.1/48 kHz).
  - Each side owns one counter and publishes it with a release store (`Volatile.Write`); the other side reads it with an acquire load. It is correct on x64 and ARM64, with no locks and no waits.
  - When the reader falls 2 s behind (a stalled render thread), the newest samples are dropped and counted (`DroppedSamples`) rather than overwriting unread ones.
  - A read into a smaller destination skips the oldest, so the newest audio always arrives.
- **Spec clarifications and deviations (doc 03 §1, §3):**
  - **Lower-latency loopback (deviation from using `WasapiLoopbackCapture` as is):** NAudio's class always uses a 100 ms buffer polled every ~50 ms, so packets can be ~60 ms old. That is outside doc 03 §3's 10–20 ms loopback budget. Our own polling loop on NAudio's `AudioClient`/`AudioCaptureClient` opens the loopback stream with a 40 ms buffer polled every ~20 ms. It replaces `WasapiCapture` because that class's capture thread calls `IAudioClient::Stop` in a `finally`, which throws on an invalidated device (unplug) on a thread with no handler and ends the process. Its `Dispose` also races its own thread. The loop also writes straight from the WASAPI buffer into the ring with no per-poll allocation. It polls rather than using event callbacks, because loopback events aren't reliable on every Windows 10 build. 40 ms keeps headroom over the 15.6 ms default timer granularity.
  - **Formats:** 32-bit float, or 16/24/32-bit PCM by container size (24-in-32 is read as 32-bit), detected from the format tag or the `WAVE_FORMAT_EXTENSIBLE` subtype. Channels are averaged to mono. Any other mix format fails the session, which is then retried with backoff.
  - **Restarts:** a default render device change (Multimedia role, settled 250 ms) restarts at once. A stopped capture (unplug, format change, exclusive takeover) or no device at all is retried after 0.5, 1, 2, 4, then 5 s, and the backoff resets after a 5 s healthy session. While there is no device, `Current` is null and the analyzer gets empty spans (silence after its timeout).
  - **Allocations:** our audio-callback code allocates nothing. NAudio itself allocates a small `WaveInEventArgs` per packet (about 50 a second), which the NAudio API doesn't let us avoid.
  - **Privacy:** only a loopback stream on a render endpoint is ever opened. No capture endpoint and no microphone is touched, and nothing is sent anywhere.
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes.
  - A scratch harness (not committed; Lane B has no test project) checks the ring's semantics and stress-tests it between two threads: 102 M samples, 0 out-of-sequence values, including while it was full and with small reads. It also checks the mono conversion is exact for float32, int16, int24 (sign extension), int32 and 5.1, and that convert + write + read allocates 0 bytes.
  - An adversarial multi-agent review (WASAPI/NAudio, concurrency, integration) runs on this PR; its upheld findings are fixed here before merge.

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
