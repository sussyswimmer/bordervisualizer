# Progress

## Lane A — Codex

- [x] H-003/H-004 handoff follow-up: safe empty preset catalog until C7; confirm consumer assumptions and cross-review PR #3 — PR #4 — effort: S. Core Release build: 0 warnings/errors; 2 tests passed after reproducing the original startup exception. Implemented, pending merge.

- [ ] C1 FFT, Hann window, band analyzer, auto-gain, envelopes
- [ ] C2 Spectral-flux beat detector, silence detection, `AnalyzerDiagnostics`, `AudioTuning`, real `IAudioAnalyzer` + zero-alloc test
- [ ] C3 `Rimlight.Bench` console + `tools/wav-analyze`: reads a WAV, runs the analyzer offline, prints beat timestamps/BPM, and writes a CSV and a PNG plot (ScottPlot or SkiaSharp) of Level/Bass/Beat/flux/threshold. Include a synthetic-track generator (kicks at a given BPM + noise + vocals-ish sines). **This lets beat tuning happen without Windows.**
- [x] C4 Oklab/OkLCh, k-means palette extractor, glow-ify, gamut mapping, procedural test fixtures — PR [#12](https://github.com/sussyswimmer/bordervisualizer/pull/12) — effort: M (made by Claude Code on Maxwell's instruction while Codex is not working on Lane A)
- [x] C5 `PaletteBlender` + gradient LUT fill (zero-alloc) — PR [#20](https://github.com/sussyswimmer/bordervisualizer/pull/20) — effort: M (made by Claude Code on Maxwell's instruction while Codex is not working on Lane A)
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

### C4 notes

- **Structure:** `Rimlight.Core/Color/` (all internal): `Srgb` (exact transfer functions, byte table), `Oklab`/`OkLch` (Ottosson's matrices), `OklabKMeans` (k-means++ seeded by SplitMix64, Lloyd rounds that stop once no point moves), `Glow` (glow-ify and gamut mapping), `PaletteExtractor`. `CoreFactory.CreatePaletteExtractor` returns the real extractor and `FakePaletteExtractor` is deleted. Its XML remarks give Lane B (K4) the input rules, the null cases, thread-safety and cost. Oklab is ready for C5's blending: conversions and glow allocate nothing.
- **Pipeline:** BGRA8 of exactly width × height × 4 bytes (`ArgumentException` otherwise) → pixels with alpha ≥ 128 → Oklab → k = 5, at most 12 rounds → merge clusters closer than 0.05 → score `share^0.6 × (0.25 + chroma) × lightnessFitness` → Primary = best score; Secondary = best score at Oklab distance ≥ 0.12 from Primary, otherwise derived from Primary (+35° hue, +0.08 L) → glow-ify. Glow-ify raises chroma to ≥ 0.12 unless it is below 0.03, clamps L to [0.55, 0.85], and gamut-maps by bisecting chroma at constant L and hue. Output is linear RGB in [0, 1] (H-008).
- **"No art" (doc 05 §1):** null when Primary and Secondary are both below chroma 0.03 (before glow-ify) **and** at least 60 % of the used pixels lie within Oklab distance 0.03 of the largest k-means cluster's centroid (taken before merging). Null also when no pixel has alpha ≥ 128. Thresholds are public constants on `PaletteExtractor`, each tested on both sides.
- **Spec clarifications** (AGENTS.md standard 1):
  - Lightness fitness is 1 on [0.40, 0.80], with smoothstep ramps from 0.25 and to 0.92, and a floor of 0.05 outside them. With the floor, an all-dark or black-and-white image still ranks clusters by size.
  - Population is the cluster's share of the used pixels (same ranking as raw counts).
  - **Near clusters are merged before scoring (review fix):** k-means clusters whose centroids are closer than 0.05 in Oklab, directly or through a chain (single linkage), become one group with the summed population and the population-weighted centroid. Primary, Secondary and the 0.12 rule work on the groups. Without this, k = 5 on a two-color image gives the flat color one cluster and splits the other color four ways, so any texture in a 70 % majority (±3 sRGB grain, or an L ramp of 0.01) let a flat 30 % minority win Primary (four ~17 % clusters at 0.16 against 0.21). The flat-share "no art" measure still uses the largest raw cluster, so two near greys don't pull the flat color off-center. Merging costs at most 10 centroid-distance checks per call.
  - Grayscale clusters keep their own chroma (< 0.03) instead of being zeroed.
  - The derived Secondary comes from the raw Primary, before glow-ify (the doc's step order).
  - "Nearly grayscale" is judged on the chosen Primary and Secondary, as doc 05 §1 words it.
  - Alpha is straight. Images over 512 × 512 are grid-sampled to at most 512 × 512 points. A null `trackId` throws.
  - Fixtures live in `src/Rimlight.Tests/fixtures/` (doc 05 says `tests/fixtures/`).
  - Doc 05 step 7's sRGB hex stays with Lane B's own helper, because `Palette` is linear only.
- **Known limits, following doc 05 as written (for the tuning round):**
  - A very wide gradient can still split into clusters more than 0.05 apart, each scored on its own share. The merge covers texture and shading up to an L span of about 0.15 (tested), but a ramp twice as wide, cut 4 ways, leaves steps of about 0.075. Option: a larger merge distance, traded against merging distinct shades.
  - A dark cover with one bright logo gets a soft-grey Secondary (the near-black cluster, glow-ified to neutral L 0.55; the fixture gives `#F32799` / `#707179`). Option: prefer the derived Secondary when Primary is colorful and the best candidate is neutral.
- **Tests:** 132 new (16 of them from the review fixes), 296 in the suite. All images are procedural.
  - Six album-art-like 64×64 fixtures are committed as PNGs in `src/Rimlight.Tests/fixtures/`. A test-side PNG codec writes them, and a test fails if they drift from their generators (`RIMLIGHT_WRITE_FIXTURES=1` rewrites them).
  - Every doc 05 §4 case is covered, plus 1×1, transparent and partial alpha, gradients, saturated extremes, validation, determinism across 8 threads, and grid sampling (a 2× upscale gives the identical palette).
- **Verification:**
  - `dotnet build Rimlight.sln -c Release`: 0 warnings. `dotnet test`: 296 passed.
  - Oklab reference values within 1e-4; round-trip error 2.2e-6.
  - 42 mutants each fail at least one test. 33 cover the score, the distance rule, the glow-ify clamps, gamut mapping, the no-art rule, alpha, channel order, the Oklab matrix and k-means. 9 come from the review fixes: no merge, merge without chaining, an unweighted merged centroid, Secondary by population, Secondary as the first qualifying group, flat share measured against Primary, and a conversion-cache key missing R, G or B.
  - Cost (informational, shared Linux machine): 1.5 ms per 64×64 call, 78–91 ms at 512×512. Scratch comes from `ArrayPool<T>.Shared`, which keeps it per thread: the first call on a thread allocates about 84 KB at 64×64 (5.2 MB at 512×512), and a repeat call on the same thread allocates 520 B (review fix: the earlier "368 B per call" held only for the same thread).

### C5 notes

- **Structure:** `Rimlight.Core/Color/PaletteBlender.cs` (internal), built on C4's Oklab code, plus `Oklab.Lerp`. `CoreFactory.CreatePaletteBlender` returns it. Its XML remarks give Lane B (K3/K4) the whole contract: threading, allocation, crossfade, `Current` and the gradient layout. `FakePaletteBlender` is deleted. Render thread only, no locks.
- **Crossfade (doc 05 §3):**
  - `SetTarget` fades from the colors on screen to the target. Primary fades to Primary and Secondary to Secondary along straight Oklab lines. A fade from rest is eased with smoothstep (3t² − 2t³ of the elapsed share).
  - A retarget mid-fade starts from the blend reached so far (no jump) and gets the whole new duration. While the colors are moving it eases out only, t(2 − t), so they keep moving (review fix, see the clarification below).
  - A target with the colors the running fade is already heading to leaves that fade untouched and only changes `Current`'s `SourceTrackId` (review fix).
  - `IsAnimating` is true from `SetTarget` until the `Update` that reaches the duration; that `Update` settles exactly on the target's linear colors.
  - `Update` ignores NaN, negative and zero dt; an infinite dt finishes the fade.
- **Gradient (H-008 item 1, binding):**
  - Texel i is the color at u = (i + 0.5) / 64, before `frac(t + Phase)`. Primary covers u = 0..ratio, centered on ratio / 2.
  - Both boundaries, u = ratio and the seam between texels 63 and 0, are smoothstep blends 0.08 wide, centered on the boundary and mixed in Oklab. The weight is one periodic function of the circular distance to Primary's arc, so the seam is blended like the other boundary.
  - Linear RGB, alpha 1, not premultiplied. Texels outside the blends are the displayed colors exactly.
  - A span under 256 floats throws `ArgumentException`; floats after the first 256 are left alone.
- **Spec clarifications** (AGENTS.md standard 1):
  - **Ratio** is clamped to 0.1..0.9, the `Settings.PrimaryRatio` range. In that range each arc is longer than one blend, so both colors keep a pure middle. NaN uses the Settings default (0.6).
  - **Gamut:** an Oklab mix of two in-gamut colors can leave sRGB (white and saturated red: about 0.11 over in red; blue and green: about 0.08 under 0 in red; two colors with blue = 1: about 1e-4). Texels and `Current` are clamped per channel to 0..1, as the shader's `saturate` would. Chroma-reducing gamut mapping would cost tens of µs per fill.
  - **Input colors:** NaN channels become 0 and out-of-range channels are clamped. An in-range palette is kept by reference.
  - **Same colors:** a zero or negative duration switches at once. So does a target whose colors are already on screen, which leaves `IsAnimating` false, so the renderer can idle instead of drawing 800 ms of identical frames. A target with the colors a running fade is heading to (the next track of the same album, published under a new `SourceTrackId`) keeps that fade's clock and curve: before the review fix it restarted, stalled the colors and stretched an 800 ms fade to 1.2 s.
  - **Retarget easing (review fix):** doc 05 §3 wants manual edits "live but smooth" with a 200 ms fade, and K4 calls `SetTarget(…, 200 ms)` on every settings change of a color drag. Restarting smoothstep on each edit (zero speed at the start) made the glow a slow follower: about 2 % of the way per 60 Hz frame, 0.6–0.8 s behind the picker. Now a fade that takes over while the colors are moving uses the ease-out t(2 − t): it leaves at twice the average speed (no jump in position) and still arrives at rest. A fade from rest keeps smoothstep. "Moving" means a fade is running and has advanced, or is itself an ease-out, so two targets in one frame before any `Update` still ease in. Measured: the glow trails a 1 s drag by 88–100 ms at 60/144/240 fps with 30–120 edits/s, and settles within 200 ms of the last edit. The rescaled second half of smoothstep (start speed 1.5×) was the alternative: about 0.13 s behind, for a gentler start.
  - **`Current`:** while idle it is the last palette passed in, read without allocating. During a fade it is the blend, with the target's `SourceTrackId`: one cached `Palette` is allocated on the first read after each `Update`/`SetTarget`. Lane B keeps it off the per-frame path (H-004).
- **Tests:** 53 (39 + 14 from the review fixes), 349 in the suite.
  - **Layout:** every texel matches an independent statement of H-008 within 1e-5 (8 ratios × 3 palettes). Pure texels are bitwise the input colors, and each blend holds 5–6 texels.
  - **Seam:** at ratio 0.5, texel k equals texel 31 − k and texel 32 + k equals 63 − k. Every neighbor step, 63 → 0 included, is within the smoothstep slope bound. Sampled like the shader (WRAP, linear filtering, 4096 points), the loop stays within 0.039 of the ideal loop, with no jump at u = 0.
  - **Coverage:** Primary's share is off ratio by at most 5.2e-5 over the texels and 4.3e-4 when shader-sampled.
  - **Oklab, not RGB:** every texel lies on the Oklab segment between the two colors, within 2e-5.
  - **Fade timing:** smoothstep checkpoints hold, and the fade ends on frame 48 at 60 fps and frame 116 at 144 fps. Frame-rate independence and mid-fade retarget continuity are tested; a retarget follows t(2 − t) frame by frame.
  - **Review fixes:** a color drag (SetTarget every edit) is followed within 0.2 s at four frame/edit rates; the same colors mid-fade leave the fade bitwise unchanged; a one-swatch edit fades that swatch alone, from rest and mid-fade; a palette with only one bad color is clamped on that side; blue/green blends are clamped up to 0 like white/red ones down to 1.
  - Also covered: 0 bytes allocated over 6000 frames, and bitwise determinism across runs and threads.
- **Verification:**
  - `dotnet build Rimlight.sln -c Release`: 0 warnings. `dotnet test`: 349 passed.
  - 33 mutants each fail at least one test: no wrap at u = 0, blend widths 0.04 and 0.16, an uncentered blend, RGB spatial blend, RGB crossfade, linear easing, three retarget errors, three ratio-clamp errors, two dt-guard errors, `>` at the end of the fade, two stale-cache errors, a fade with same colors, an animated zero duration, two input-clamp errors, no gamut clamp, no span check, a mirrored arc, texels at i/64, alpha 0, swapped pure colors, and four allocations (in `FillGradient`, `Update`, `SetTarget` and idle `Current`).
  - Review fixes: 13 more mutants each fail at least one test: a plain smoothstep restart, ease-out on every retarget, ease-out lost for a second retarget in one frame, smoothstep as the ease-out, no same-colors keep, a keep that ignores a zero duration, a keep on either or only the Primary color, a keep with a stale `Current`, `&&` and Primary-only in the same-colors check, `||` in the input range check, and no lower gamut clamp.
  - Cost (shared Linux machine, optimized JIT): about 0.5–0.7 µs per fill, idle or mid-fade.

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
