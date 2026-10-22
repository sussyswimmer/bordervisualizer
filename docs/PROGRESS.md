# Progress

## Lane A — Codex

- [x] H-003/H-004 handoff follow-up: safe empty preset catalog until C7; confirm consumer assumptions and cross-review PR #3 — PR #4 — effort: S. Core Release build: 0 warnings/errors; 2 tests passed after reproducing the original startup exception. Implemented, pending merge.

- [x] C1 FFT, Hann window, band analyzer, auto-gain, envelopes — PR [#2](https://github.com/sussyswimmer/bordervisualizer/pull/2) — effort: M (logged in the C1 completion log below; this line was left unchecked when C1 merged)
- [x] C2 Spectral-flux beat detector, silence detection, `AnalyzerDiagnostics`, `AudioTuning`, real `IAudioAnalyzer` + zero-alloc test — PR [#7](https://github.com/sussyswimmer/bordervisualizer/pull/7) — effort: L (made by Claude Code on Maxwell's instruction; logged after the C1 completion log below; this line was left unchecked when C2 merged)
- [x] C3 `Rimlight.Bench` console + `tools/wav-analyze`: reads a WAV, runs the analyzer offline, prints beat timestamps/BPM, and writes a CSV and a PNG plot (ScottPlot or SkiaSharp) of Level/Bass/Beat/flux/threshold. Include a synthetic-track generator (kicks at a given BPM + noise + vocals-ish sines). **This lets beat tuning happen without Windows.** — PR #13 — effort: L
- [x] C4 Oklab/OkLCh, k-means palette extractor, glow-ify, gamut mapping, procedural test fixtures — PR [#12](https://github.com/sussyswimmer/bordervisualizer/pull/12) — effort: M (made by Claude Code on Maxwell's instruction while Codex is not working on Lane A)
- [x] C5 `PaletteBlender` + gradient LUT fill (zero-alloc) — PR [#20](https://github.com/sussyswimmer/bordervisualizer/pull/20) — effort: M (made by Claude Code on Maxwell's instruction while Codex is not working on Lane A)
- [x] C6 Real `LightEngine`: intensity formula, pulse, phase drift, idle breathing, silence fade, Visibility, `IsStatic` — PR [#11](https://github.com/sussyswimmer/bordervisualizer/pull/11) — effort: M
- [x] C7 Settings validation/clamping, JSON store (atomic, backup on corruption), version migration scaffold, Presets — PR #16 — effort: M
- [x] C8 CI: `ci.yml` (Linux job: Core.slnf build+test; Windows job: full sln build+test), labeler, `.github/release.yml` — PR [#10](https://github.com/sussyswimmer/bordervisualizer/pull/10) — effort: M (made by Claude Code on Maxwell's instruction; notes below)
- [x] C9 Packaging: `build/pack.ps1` (Velopack, x64+ARM64, self-contained), `release.yml` on tag `v*` with `vpk upload github`, optional signing step gated on secrets — PR [#21](https://github.com/sussyswimmer/bordervisualizer/pull/21) — effort: M (made by Claude Code on Maxwell's instruction; notes below)
- [x] C10 `tools/icon-gen`: SVG → multi-size `.ico` + PNGs (consumes `assets/icon.svg` from Lane B) — PR #15 — effort: M
- [x] C11 Community health files + issue/PR templates + CHANGELOG — PR [#17](https://github.com/sussyswimmer/bordervisualizer/pull/17) — effort: S (made by Claude Code on Maxwell's instruction; notes below)
- [x] C12 Perf + soak harness in Bench: run analyzer + light engine for 8 simulated hours of synthetic audio, and report allocations, p99 frame cost, and memory — this branch (claude/confident-mccarthy-gt8tif) — effort: L (made by Claude Code on Maxwell's instruction; notes below)
- [x] C13 Delete `Fakes/` and make `CoreFactory` return the real implementations. Final Core API docs (`docs/CORE-API.md`). — this branch (claude/confident-mccarthy-gt8tif) — effort: M (made by Claude Code on Maxwell's instruction; notes below). Also resolves H-011, H-016 and H-017.

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

### C8 notes

- **CI (`.github/workflows/ci.yml`)** runs on pushes to `main`, pull requests and manual runs, with `contents: read`. Two jobs:
  - **"Core (Linux, .NET 8 SDK)"** builds and tests `Rimlight.Core.slnf`. It installs SDK 8.0.x into an empty `DOTNET_INSTALL_DIR`, so the job really uses SDK 8. The runner images ship newer SDKs, and `global.json` would roll forward to them.
  - **"Full solution (Windows)"** builds `Rimlight.sln` and then the `.slnf` (the slnf build checks its backslash paths), then tests. It deliberately runs on the newest SDK the runner has, like a current Visual Studio install.
  - Between them the jobs cover both ends of the SDK range `global.json` allows. If a future image breaks the Windows job, the same `DOTNET_INSTALL_DIR` line pins it to SDK 8.
  - The NuGet cache uses `actions/cache`, keyed on OS + `**/*.csproj` + `**/Directory.*.props`. `setup-dotnet`'s built-in cache expects lock files, which the repo doesn't have.
  - A new push to a PR cancels that PR's older run. Runs on `main` and manual runs each get their own concurrency group (keyed on the run ID), so none is ever cancelled and every merged commit gets a result. A shared group would not be enough: `cancel-in-progress: false` spares the running run, but GitHub still cancels a pending run when a newer one queues (review on #10).
  - The Linux job also fails if a `Rimlight.Core.slnf` project path contains a forward slash. The Windows job's newer MSBuild accepts them, so nothing else would catch a regression of H-010 item 2.
- **Labels (`.github/workflows/labeler.yml`, `.github/labeler.yml`):**
  - `actions/labeler@v5` on `pull_request_target`. The workflow has no permissions; the job has `contents: read`, `pull-requests: write` and `issues: write`. The last is only for creating a label the first time it is applied: the repo has only GitHub's default labels, and without it every label this PR introduces would fail with 403 (labeler README, "Recommended Permissions"; review on #10). The job checks out only the base branch's `.github` folder and never runs PR code.
  - Path labels as specified: `lane-a`, `lane-b`, `docs`, `ci`. Lane A also covers the community files doc 09 §2 assigns to it.
  - **Deviation:** `docs` ignores `docs/PROGRESS.md` and `docs/HANDOFF.md`. Almost every PR appends to them, so the label would be on every PR and would pull refactor/test PRs into the Docs release section.
  - Doc 08 §3 also asks for labels from Conventional Commit prefixes, and labeler v5 only matches paths and branches. A second step therefore maps the PR title through `.github/scripts/pr-title-labels.sh`:
    - type: `feat`→`feature`, `fix`, `perf`→`performance`, `docs`, `build`, `ci`
    - scope: `audio`, `overlay`, `color`, `ui`, with a few synonyms such as `capture`, `render`, `palette`, `tray`, `settings`
    - The title reaches the script through an environment variable.
    - The rules have 30 cases in `pr-title-labels.test.sh`, which the Linux CI job runs.
    - Title labels are only added, never removed. The step runs on every event, pushes included: a newer run replaces a pending one in the per-PR concurrency group, so a push right after a retitle would otherwise drop the new title's labels (review on #10).
  - The labeler first runs on the PR after #10 merges, because `pull_request_target` uses `main`'s copy of the workflow. Each of the 12 labels (`lane-a`, `lane-b`, `docs`, `ci`, `feature`, `fix`, `performance`, `build`, `audio`, `overlay`, `color`, `ui`) is created in grey the first time it is applied. To give them colors, create them beforehand under Issues → Labels; the workflow uses existing labels as they are.
- **Release notes (`.github/release.yml`):**
  - Sections: the task's categories, plus doc 08's Performance, in doc 08's emoji style. "Other changes" is the catch-all and stands in for doc 08's "Chores".
  - Excluded: the `ignore-for-release` label and Dependabot.
  - GitHub lists a PR under the first section that matches. Features and Fixes come first, and the area sections collect each area's refactors, tests and chores.
- **H-010** is done (see HANDOFF). `AnalysisLevel` 8.0 changes nothing on SDK 8: `EffectiveAnalysisLevel` is 8.0 and `WarningLevel` 8 both before and after. On newer SDKs it holds them there, together with `LangVersion` 12.
- **Branches rebasing onto C8 (C3, C10):** add `.slnf` entries with backslashes (`src\\Rimlight.Bench\\Rimlight.Bench.csproj`, `tools\\icon-gen\\...`) and keep C8's `tools/Directory.Build.props` (it imports via `$(MSBuildThisFileDirectory)`). The Linux CI job rejects forward slashes.
- **Validation (Linux, SDK 8.0.425):**
  - Core `.slnf` (backslash paths) and full `.sln` Release builds: 0 warnings, 0 errors.
  - `dotnet test` via both: 165 passed, 0 failed, 0 skipped.
  - The four YAML files parse. actionlint 1.7.12 with shellcheck 0.11.0 reports 0 problems.
  - The labeler globs were checked with minimatch 9 against 15 sample changed-file lists. The title-label tests pass 30/30, and changing one mapping fails 2.
  - The `.slnf` slash check passes on this branch's filter and fails, listing all five paths, on C3's forward-slash filter.
- **First CI run on #10** ([run 37440448837](https://github.com/sussyswimmer/bordervisualizer/actions/runs/37440448837)):
  - Linux, SDK 8.0.425: `.slnf` build with 0 warnings; 165 tests passed; 42 s.
  - Windows, SDK 10.0.401: full `.sln` and the backslash `.slnf`, both with 0 warnings; 165 tests passed; 80 s.
  - **Follow-up:** both jobs warn that `checkout`, `cache` and `setup-dotnet` @v4 (and `labeler`@v5) target Node 20 and are forced onto Node 24. Newer majors exist. The task pinned v4/v5, so the bump is left for a later PR.
- **For Maxwell:**
  - Enable Dependabot alerts (Settings → Code security), because NuGet advisories now warn instead of failing the build.
  - Optional: protect `main`, requiring the two CI checks.
  - The README CI badge is `actions/workflows/ci.yml/badge.svg?branch=main`.

### C9 notes

- **`build/pack.ps1` (PowerShell 7)** runs three steps for each of win-x64 and win-arm64:
  1. `dotnet publish src/Rimlight.App` into `artifacts/publish/<rid>`: Release, self-contained, untrimmed.
  2. `vpk pack` into `artifacts/releases`. This makes a Setup.exe, a portable zip, the full `.nupkg`, and the `releases.<channel>.json` feed that installed copies read.
  3. The installer is copied into `artifacts/installers` as `RimlightSetup.exe` (x64) or `RimlightSetup-arm64.exe` (ARM64).
  - **Pack identity.** packId and packTitle come from `<AppName>` in `src/Directory.Build.props`, so a rename still happens in one place. Change the packId only before the first release. Installed copies live in `%LocalAppData%\<packId>`, and a new ID strands them without updates.
  - **Channels.** `win` for x64 (Velopack's default, which also gets the legacy `RELEASES` file) and `win-arm64`.
  - **Trimming is off.** WPF and the WinRT projections aren't trim-safe, and doc 07 Phase 6 allows turning it off.
  - **Shortcuts.** Velopack's default: Desktop and Start menu.
  - **Version.** `-Version` defaults to `<Version>` in the props file and accepts a tag name such as `v1.2.0`. It is checked as SemVer 2 without build metadata before anything is built.
  - **vpk.** vpk 1.2.161 (MIT) is pinned in `.config/dotnet-tools.json`, and the script restores it. On Linux and macOS it cross-packs with vpk's `[win]` directive. Signing (`-AzureTrustedSignFile`) needs Windows.
  - **Clean start.** Each run first deletes the output folders it writes. When it can't prompt, vpk stops if the same version is already in its output folder, and a clean start also means no stale file is uploaded.
  - **Before K9.** Until K9, packing needs `-SkipVelopackAppCheck`. Without it vpk fails with "Unable to verify VelopackApp is called" (H-014). *(K9 added the call; packs and dry runs no longer pass the flag.)*
  - **Stages.** `-Stage Publish` runs step 1 and `-Stage Pack` runs steps 2 and 3 on the builds already in `artifacts/publish`, so the release workflow can sign on a machine that ran no build code. The default, `All`, runs both. Each stage deletes only the folders it writes. `-Stage Pack` first checks that every runtime's `Rimlight.exe` is there, and signing in the Publish stage is refused.
  - The helpers have Pester tests (`build/pack.Tests.ps1`, 33 cases), which the Linux CI job runs. Two of them run the script's stage checks. They include the Windows branch of the vpk command line, which Linux can't otherwise run. #21's first Windows dry run caught a bug there: a one-element `@('vpk')` returned from an `if` expression unrolled to a string, and every pack argument was joined into one.
- **`.github/workflows/release.yml`** has three jobs. Credentials go only to jobs that run no build code: restoring and building runs code from NuGet packages (MSBuild targets, analyzers, source generators such as CsWin32).
  - **`build` (windows-latest, read-only token, no secrets).**
    - Uses exactly SDK 8, the same way as the Linux CI job.
    - Builds and tests `Rimlight.sln`, runs `pack.ps1 -Stage Publish` with the tag's version, and keeps `artifacts/publish` as the `builds` artifact (1 day).
  - **`pack` (windows-latest, read-only token, signing secrets when signing is on).**
    - Checks out the repo, sets up the same SDK, and downloads `builds`. It has no NuGet cache, which the build job's code could have written to.
    - Runs `pack.ps1 -Stage Pack`, whose only restore is vpk from the tool manifest. vpk reads `Rimlight.exe` without running it: its VelopackApp check inspects the IL of `Main` (checked in vpk 1.2.161's `CompatUtil`).
    - Keeps `artifacts/releases` and `artifacts/installers` as the `packages` artifact (7 days).
    - Both jobs read the version from one workflow-level `PACK_VERSION`, so they can't disagree.
  - **`publish` (ubuntu-latest, `contents: write`, tags only).** It runs no build code. In order, it:
    1. Writes the release notes: `.github/release-notes-intro.md`, then GitHub's generated notes (the `releases/generate-notes` API with C8's `.github/release.yml` sections).
    2. Creates a draft release titled "Rimlight <version>" with both installers (`gh release create --draft --verify-tag`).
    3. Runs `vpk upload github --merge` for `win-arm64`.
    4. Runs it for `win` with `--publish`.

    The release is public only once all files are attached, so `releases/latest/download/RimlightSetup.exe` never meets a half-uploaded release. A version with a hyphen (`v1.1.0-beta.1`) becomes a prerelease. Neither `releases/latest` nor the app's update check (`prerelease: false`) picks it up.
  - **Deviation: release notes.** vpk would use notes embedded in the package as the release body. Instead, `gh` creates the release with the notes and vpk merges into it. That keeps every write-token step in one job that runs no build code. The packages carry no notes.
  - **Deviation: dry runs.** Pull requests that change the packaging files, and manual runs, are dry runs: build, test, an unsigned pack with `-SkipVelopackAppCheck`, and the artifact. Nothing is published. They test the Windows pack before any tag exists.
    - The trigger paths are `build/**`, the tool manifest, the workflow, `global.json`, `src/Directory.Build.props` and all of `src/Rimlight.App/**`. vpk checks two things there that CI can't: that the entry point calls `VelopackApp.Build().Run()`, and that the icon loads. That costs about 3.5 minutes of Windows time on App PRs, which is free on a public repo.
  - **Signing (optional, Azure Trusted Signing).**
    - It runs only for a tag and only when all six secrets exist: `SIGNING_ENDPOINT`, `SIGNING_ACCOUNT`, `SIGNING_PROFILE`, `SIGNING_TENANT_ID`, `SIGNING_CLIENT_ID` and `SIGNING_CLIENT_SECRET`. The client must be an app registration with the "Trusted Signing Certificate Profile Signer" role.
    - The workflow writes the metadata JSON and passes `AZURE_*` credentials to the pack job's Pack step only. That job runs no build code, so a compromised build-time package can't read the client secret. vpk signs with its bundled signtool and Trusted Signing client, and skips files that are already signed (the .NET runtime).
    - Until signing is on, the SmartScreen sentence stays in the README and in the release intro.
- **For Maxwell:**
  - **Repo settings: nothing has to be enabled.** The publish job asks for `contents: write` itself, which works whatever the default under Settings → Actions → General → Workflow permissions is. That default applies only to workflows that don't set `permissions`. Switching that default to "read and write" would only widen every other workflow's token. If a release ever fails with 403 "Resource not accessible by integration", check that Actions are allowed to run on the repo.
  - **To cut a release:**
    1. Make sure K9 is merged and `main` is green.
    2. Run `git tag v1.0.0` on `main`, then `git push origin v1.0.0`. Push only the tag; don't create the release in the GitHub UI, or the publish job's `gh release create` fails.
    3. Bumping `<Version>` in `src/Directory.Build.props` is optional. The tag sets the release version; the props value only sets local and dry-run packs.
    4. The build and pack jobs take about 3.5 minutes together, and the publish job uploads about 650 MB.
  - **If a release run fails:**
    - If a job fails for a reason outside the code (a download, a runner, an expired signing secret that you have since replaced), re-run it from the run's page. The tag stays as it is. "Re-run failed jobs" uses the earlier jobs' artifacts, which last 1 day (`builds`) and 7 days (`packages`); after that, use "Re-run all jobs". If `publish` had already created the draft release, delete the draft first.
    - If `build` or `pack` fails because of the code, nothing was published. Merge the fix into `main`, then move the tag to it. Re-pushing the old tag alone would build the same broken commit again.
      ```
      git fetch origin
      git push --delete origin v1.0.0
      git tag -f v1.0.0 origin/main
      git push origin v1.0.0
      ```
      Tagging the fix as the next patch version (`v1.0.1`) works too.
- **How auto-update finds releases (K9 uses Velopack's `GithubSource`):**
  1. The app lists the 10 newest releases through the GitHub API. This is unauthenticated, at 60 requests an hour per IP, and the app checks every 12 h. Drafts are invisible and prereleases are skipped.
  2. From each release it reads its own channel's feed: `releases.win.json` for an x64 install, `releases.win-arm64.json` for ARM64. The channel is stored in the installed package.
  3. It downloads the highest version above the installed one, then applies it on restart.
  - There are no delta packages yet, so each update is the full package, about 75 MB.
- **Validation (Linux, SDK 8.0.425, vpk 1.2.161, pwsh 7.4.6):**
  - **Full pack.** `pwsh build/pack.ps1 -SkipVelopackAppCheck` took 46 s.
    - x64 (channel `win`): `Rimlight-win-Setup.exe` (= `RimlightSetup.exe`) 82.8 MB, `Rimlight-win-Portable.zip` 75.5 MB, `Rimlight-0.1.0-full.nupkg` 75.6 MB.
    - ARM64 (channel `win-arm64`): `Rimlight-win-arm64-Setup.exe` (= `RimlightSetup-arm64.exe`) 77.2 MB, `Rimlight-win-arm64-Portable.zip` 71.4 MB, `Rimlight-0.1.0-win-arm64-full.nupkg` 71.4 MB.
    - Also written: both feeds, `RELEASES`, and the two `assets.*.json` files.
    - The x64 publish folder is 186 MB.
  - **Package contents.**
    - The nuspec id, title, authors, channel and rid are right.
    - The exe, `Update.exe` and Setup.exe are x86-64 or AArch64 PE images as expected.
    - There are no `.pdb` files.
    - FileVersion is 0.1.0.0. A `v0.2.0-beta.1` pack gives package version `0.2.0-beta.1` and FileVersion 0.2.0.0.
  - **Two stages.** `-Stage Publish -Version v0.2.0-beta.1 -Runtimes win-x64` (7 s), then `-Stage Pack` with the same options (12 s), gives the same files as a one-step pack (`RimlightSetup.exe` 82.8 MB, package version `0.2.0-beta.1`) and leaves `artifacts/publish` in place.
  - **Failure paths.** A bad `-Version` fails before any folder is touched, with exit code 1. A pack without the skip fails at vpk's VelopackApp check, so a tag pushed before K9 publishes nothing.
  - **Upload options.** The `vpk upload github` options parse, and `VPK_TOKEN` satisfies `--token`. This was checked against a non-existent repo, so no release was touched.
  - **Lint.** actionlint 1.7.12 with shellcheck 0.11.0 reports 0 problems. All YAML and JSON files parse. PSScriptAnalyzer only flags `Write-Host`, which this console build script uses on purpose.
  - **Tests and build.** Pester: 33/33 (reverting the vpk fix fails 2; dropping the Publish-stage signing check, or deleting folders before the Pack-stage check, fails 1 each). Full `.sln` Release build: 0 warnings. `dotnet test`: 165 passed.
  - **Windows dry run on #21** ([run 37451291615](https://github.com/sussyswimmer/bordervisualizer/actions/runs/37451291615)):
    - SDK 8, windows-latest. Build, the 165 tests and the native vpk pack all pass. The pack job takes 2.5 min; the pack itself 35 s.
    - Output: `RimlightSetup.exe` 83.0 MB, `RimlightSetup-arm64.exe` 77.2 MB, `Rimlight-win-Portable.zip` 75.7 MB, `Rimlight-win-arm64-Portable.zip` 71.5 MB.
    - The `packages` artifact holds 14 files, 645 MB. The publish job was skipped, as it should be for a pull request.
    - The Linux CI job ran the Pester tests.
  - **Windows dry run of the split jobs on #21** ([run 37454715304](https://github.com/sussyswimmer/bordervisualizer/actions/runs/37454715304)):
    - `build` took 1 min 44 s, including the two publishes and a 154 MB `builds` artifact.
    - `pack` took 1 min 41 s. Its log shows the artifact download, `dotnet tool restore` and the two vpk packs (19 s and 17 s), and no publish or package restore.
    - The output is the same as before: `RimlightSetup.exe` 83.0 MB, `RimlightSetup-arm64.exe` 77.2 MB, and a `packages` artifact of 14 files, 645 MB. `publish` was skipped.
  - **Not run:** signing, and the publish job, which needs a real tag.
- **Follow-ups:**
  - **Delta updates.** Running `vpk download github --channel <c>` into `artifacts/releases` before packing (and not wiping that folder) would make vpk build deltas. Turning that on later doesn't break existing installs.
  - **K9** removes the dry run's `-SkipVelopackAppCheck` (H-014). *(Done in K9.)*
  - **Action versions.** Bump to newer action majors together with C8's Node 20 follow-up.

### C3 notes

- **Where things are:**
  - `tools/audio-tools` (`Rimlight.AudioTools`) is the code shared by the tools, Bench and the tests: WAV reader/writer, `SyntheticTrack`, `OfflineAnalysis` (the frame feed), `BeatStats`, `TuningJson`, CSV and the option parser.
  - `tools/wav-analyze` holds the CLI and the SkiaSharp plot. `src/Rimlight.Bench` is the console.
  - All three are in `Rimlight.sln` (a new `tools` folder) and `Rimlight.Core.slnf` (backslash paths, as C8 requires). The tests are in `src/Rimlight.Tests/Tools`.
  - `tools/Directory.Build.props` imports the `src` props (H-010 item 5).
  - The path is `tools/wav-analyze` (doc 09). AGENTS.md said `tools/WavAnalyze`, so its command now uses the real path. Its `--plot out.png` works.
- **wav-analyze, usage** (`--help` lists every option):
  ```bash
  dotnet run -c Release --project tools/wav-analyze -- song.wav [--out dir] [--fps 60] [--jitter 0.2] [--packet-ms 10] [--tuning tuned.json] [--range 30:45]
  dotnet run -c Release --project tools/wav-analyze -- generate track.wav --bpm 128 --seconds 60 [--full-mix] [--intro 3] [--gain-db -40] [--format pcm24] [--channels 2]
  dotnet run -c Release --project tools/wav-analyze -- selftest
  dotnet run -c Release --project tools/wav-analyze -- tuning [--tuning tuned.json]
  ```
  - **Analyze:** prints the beat times and the tempo, then writes `<name>.csv` and `<name>.png`.
    - The CSV has one row per render frame: `time,level,bass,beat,isSilent,flux,threshold,bpm`.
    - The PNG has four panels on one time axis: Level+Bass, the Beat pulse, flux against the threshold with the detected beats, and the tempo estimate. `IsSilent` is shaded.
    - `--range` is clamped to the audio, but a range that starts at or past the end exits with code 2, because the plot would be empty.
  - **Input:** 8/16/24/32-bit PCM, 32/64-bit float, `WAVE_FORMAT_EXTENSIBLE` and RF64, at any rate and channel count (averaged to mono).
- **Tuning JSON:** this is the format of K8's "Copy params as JSON" (H-015).
  - It is one flat object of `AudioTuning` property names, e.g. `{ "Sensitivity": 1.25, "FluxThresholdMultiplier": 1.8 }`. Any subset works, and the rest keep their defaults.
  - Keys are case-insensitive and unknown keys are errors. Comments and trailing commas are allowed.
  - The analyzer validates the values. An invalid one exits with code 1 and names the property.
  - `wav-analyze tuning` prints every key.
- **Feed:**
  - Each frame lasts 1/fps × (1 ± jitter·u), with u from the seed. The frame is passed every sample that has arrived by its end, or only whole packets with `--packet-ms`.
  - Beat times are frame times, i.e. when the light would flash. Diagnostics follow the `CreateAnalyzer` remarks: the last `FluxHistory`/`ThresholdHistory` entry after each call.
- **Tempo in the tool (spec clarification):**
  - The median of the beat intervals picks the typical interval. The tempo is then the mean of the intervals within ±25 % of it, so missed and extra beats drop out.
  - A plain median is biased by frame quantization (H-016).
- **Generator:**
  - A kick on every beat (150→50 Hz sweep, ~70 ms decay), white noise, and a vocal-ish melody. The melody is 220–440 Hz with 4 harmonics, 5.5 Hz vibrato and a 4 Hz swell, and its notes start half a beat after every second beat.
  - Options: a bass line and off-beat hats (`--full-mix`), levels in dBFS, overall gain, intro/outro silence, rate and length.
  - Each sample is a pure function of its index and the options, with SplitMix64 written out in code. So it streams in any chunk size, `Read` allocates nothing (C12 needs this), and equal options give identical WAV bytes on a given machine. There is no golden hash, because `Math.Sin` can differ in the last bit between CPUs.
  - `KickTimes()` is the ground truth for `BeatStats.Match`.
- **Plot:**
  - Library: SkiaSharp 3.119.4 plus `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.4 (MIT; that build needs no fontconfig). The 4.x line (June 2026 onward) was skipped as too new, and ScottPlot was skipped for its extra HarfBuzz dependency.
  - Fonts: Segoe UI on Windows, DejaVu or Liberation on Linux. With no fonts installed, the text is omitted and the PNG is still written.
  - Width is 80 px per plotted second, clamped to 1200–12000 px; `--range` and `--width` zoom.
  - The flux panel scales to the 99.5th percentile of flux and threshold, so the warm-up threshold spike doesn't flatten the kicks.
- **Bench:**
  - Commands: `dotnet run -c Release --project src/Rimlight.Bench -- [analyzer|soak] [options]`.
  - What is measured: only `Process` is timed and allocation-counted (`GC.GetAllocatedBytesForCurrentThread` around each call). The signal is pre-rendered, or streamed between frames in the soak.
  - Per-frame cost goes into a constant-memory log-linear histogram (≤ 0.8 % error).
  - `analyzer` exits with 1 if any scenario allocates.
  - `analyzer` warms up for 2,000 frames and at least 0.5 s, so the tiered JIT has installed optimized code before timing starts.
  - `soak` leaves the first 5 simulated seconds out of the stats. It is the structure C12 extends: C12 still has to add the light engine once C6 lands, the 8 h default and `docs/PERF.md`.
- **Results** (Release, .NET 8.0.31, a Linux x64 box shared with other jobs, so the timings are informational):
  - Analyzer at 48 kHz/60 fps, 2,000 warm-up + 10,000 frames, **0 bytes/frame and 0 GCs in every scenario**:

    | Scenario | Mean | p99 | Share of one core |
    |---|---|---|---|
    | Music | 41.0 µs | 79 µs | 0.25 % |
    | Kick | 32.8 µs | – | – |
    | Noise | 37.8 µs | – | – |
    | Silence | 38.6 µs | – | – |
    | Idle, no packets | 2.2 µs | – | – |

  - Soak, 2 simulated minutes after a 5 s warm-up, 10 ms packets, ±20 % jitter: 60 µs/frame mean, p99 167 µs, 0.0 B/frame, managed heap flat at 0.4 MB, no GCs. Frames carrying two 10 ms packets need two analysis steps, which is why the mean is above the benchmark's.
  - `selftest`, kick hits after the 2 s warm-up:

    | Case | Kicks found | False beats | Tempo | Analyzer |
    |---|---|---|---|---|
    | 120 BPM, 60 fps ±20 % | 56/56 | 0 | 120.0 | 120.2 |
    | 128 BPM full mix, 44.1 kHz, 144 fps, 10 ms packets | 59/59 | 0 | 128.0 | 127.7 |
    | 120 BPM full mix at −40 dB, 30 fps | 56/56 | 0 | 120.0 | 119.8 |
    | 174 BPM, 96 kHz, 20 ms packets | 81/81 | 0 | 174.1 | 175.1 |

    White noise alone gave 0 beats. The median kick-to-beat latency was 21–32 ms.
  - End-to-end test: generated 120 BPM, 20 s, written as a 16-bit stereo WAV and read back, at 60 fps ±20 %.
    - Tempo 120.0; the analyzer read 119.95 BPM.
    - 39 beats in total (the kick at 0 s falls inside the 0.25 s warm-up), and 36/36 kicks after 2 s with 0 false beats.
    - Median latency 21.5 ms.
  - ffmpeg-encoded s16, s24 6-ch, s32, f32 4-ch, f64 and u8 files all decode, and they give identical beats.
  - Tests: 45 new, 210 in the suite.
    - 21 of 22 consecutive full-suite runs passed. The one failure, on the first run right after a build on this busy shared machine, could not be reproduced or identified in 21 more runs. *(K10: probably the background-GC counting effect; both test hosts now run without background GC.)*
    - The new allocation tests now warm up for longer (100 reads; the soak skips its first seconds), so a tier-up during measurement can't count against them.

### C10 notes

- C10 was done by Claude Code (Maxwell asked it to finish both lanes). The tool and the brand icon landed together because the app needed a real icon: `assets/icon.svg` (40 px and up) and `assets/icon-small.svg` (16–32 px, drawn on the 16/32 px grid) replace the placeholder `Rimlight.ico` at the same path. `Rimlight-dim.ico` (50 % opacity) is new, and `assets/icon-256.png`/`icon-512.png` are for the README. `build/icons.sh` or `build/icons.ps1` regenerates all four files in about 5 s. The output is byte-for-byte deterministic. H-012 hands the icons to K6/K12. The C3 branch also adds an entry numbered H-012; C10 claimed the number first, so C3's entry moves to the next free ID when the second of the two PRs merges (it is H-016 since the Lane A integration).
- **Decisions (simplest robust option):**
  - **`.ico` layout:** 32-bpp DIBs below 256 px, PNG at 256. PNG-only entries also work from Vista on, but this layout is what every Windows API, the resource compiler and the shell read, and the placeholder already used it. The AND mask marks fully transparent pixels only.
  - **Sizes:** doc 06's list plus 40 px (32 px icons at 125 % scale; the placeholder had it too).
  - **`--opacity`:** multiplies straight alpha, rounding half away from zero, and leaves colors alone. 0.5 turns alpha 255 into 128.
  - **SVG size:** an SVG without a viewBox or an absolute width and height is rejected. Svg.Skia would otherwise size it to its content, so the framing would follow the art.
  - **Tests:** they live in their own project, `tools/icon-gen.Tests`, so Core's tests don't pull in Skia or Svg.Skia. Both projects are in `Rimlight.sln` and `Rimlight.Core.slnf` (pure .NET with Linux native assets, so they build and test in the sandbox). Every `.slnf` entry uses backslashes, as in C8: MSBuild on Windows compares them verbatim with the `.sln` paths, and a forward-slash entry fails with MSB5028 (H-010 item 2).
  - **`tools/Directory.Build.props`** imports `src/Directory.Build.props` (H-010 item 5). It is byte-identical to the C8 branch's version.
  - **`tools` solution folder:** it reuses the C3 branch's GUID, so merging both PRs gives one folder.
- **Packages:**
  - Pinned: Svg.Skia 5.1.1, SkiaSharp 3.119.4 (the same as C3) and SkiaSharp.NativeAssets.Linux.NoDependencies 3.119.4.
  - All are MIT except Svg.Skia's SVG parser, Svg.Custom (from SVG.NET), which is MS-PL. It runs at build time only and nothing from it ships.
  - 5.1.1 is the newest Svg.Skia on SkiaSharp 3.x; 5.2+ needs SkiaSharp 4.
- **Validation:**
  - **Build:** `dotnet build Rimlight.sln -c Release` gives 0 warnings and 0 errors. Tests pass on both the `.sln` and the `.slnf`: 73 icon-gen and 165 Core, 0 failed.
  - **Test coverage:** ICO header, directory and DIB layout, rendering, every command-line error path (an empty or blank output path exits 2), and the committed icons. Those must be complete, the dim icon must be exactly half alpha, and they must match a fresh render of the SVGs within 8/255, so an edited SVG fails CI until the icons are regenerated.
  - **Mutation checks:** each of these fails at least one test: top-down DIB rows, an alpha < 128 mask, an undoubled DIB height, and a halo-opacity change that wasn't regenerated.
  - **PIL:** both `.ico` files decode at all 8 sizes as RGBA.
  - **Built app:** `Rimlight.dll` carries every image as its Win32 icon, and both `.ico` files as WPF resources.
  - **Visual check:** previews were inspected on white, light and dark taskbar grays, black and blue, with 8× nearest-neighbor upscaling and simulated tray strips at 100–200 % scale.
- **Manual Windows check pending (Maxwell):** the tray and Explorer show the new mark, sharp at your scale, on light and dark taskbars. The checklist is in PR #15.

### C11 notes

Done by Claude Code on Maxwell's instruction (Codex is not working Lane A this run).
- **Files:** `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, `CHANGELOG.md`, `.github/ISSUE_TEMPLATE/{bug_report,feature_request,private_contact,config}.yml` and `.github/PULL_REQUEST_TEMPLATE.md`. Nothing under `src/` changed.
- **Decisions and deviations:**
  - **Security contact (deviation from doc 08 §3 and AGENTS.md):** both name Maxwell's personal email. `SECURITY.md` uses GitHub's private vulnerability reporting instead, as this run's instructions asked, so no personal address gets published.
    - The repo API reports private reporting as `enabled: false`, so Maxwell has to enable it: Settings > Code security > Private vulnerability reporting.
    - Until then, the fallback in both `SECURITY.md` and `CODE_OF_CONDUCT.md` is the **Ask for a private contact** issue form (`private_contact.yml`, review fix). Blank issues are off, so the fallback needs its own form. It has a fixed neutral title, one optional field, no labels, and a warning to leave out every detail. The maintainer answers with a private channel, for example a draft security advisory with the reporter added.
  - **Code of conduct:** Contributor Covenant 2.1, identical to the upstream file except the contact line. That line names the maintainer's profile and the private report form (title "Conduct report"), and invents no email.
  - **Issue forms:**
    - The bug form asks for what doc 08 §3 lists (Windows version, Rimlight version, GPU, monitor setup, music app, log zip, steps), plus the audio output device, scaling and how often it happens.
    - Logs are optional, because the app may not start.
    - The forms apply the default `bug` / `enhancement` labels, which match C8's release-note sections.
  - **`config.yml`:** blank issues are off, and the security policy is the only contact link. Doc 08 §3 also wants a Discussions link, but Discussions is off (`has_discussions: false`), so that link is commented out until it's enabled (doc 08 §4).
  - **PR template:** named `.github/PULL_REQUEST_TEMPLATE.md`, as the task asked. Doc 08 spells it in lowercase, and GitHub accepts both.
  - **Squash merge (review fix):** the repo allows all three merge methods, and its squash default keeps a one-commit PR's commit title. `CONTRIBUTING.md` and the PR template therefore state only doc 09 §2's squash-merge rule and no longer promise that the PR title becomes the commit on `main`. PR #17 asks Maxwell to make squash the only method, with the PR title as its default.
  - **`CHANGELOG.md`:**
    - `[Unreleased]` describes, in user terms, what is on `main` or in open PRs #8 to #15. Drop a line if its PR doesn't merge.
    - `[0.1.0] - YYYY-MM-DD` waits for the release date, and the tag links sit in a comment until the tag exists.
    - `Version` is 0.1.0, but doc 08 §5 says to tag v1.0.0, so rename the heading to whichever tag ships.
  - **Not built yet:** the logs path, the **Open logs folder** tray item and the version in Settings > About all come from docs 02 and 06. H-013 asks Lane B to keep them in sync. *(Built in K6, K7 and K9; H-013 is done.)*
- **Verification:**
  - All four YAML files load with `yaml.safe_load`.
  - They also validate against SchemaStore's `github-issue-forms` / `github-issue-config` schemas with 0 errors. As controls, six deliberately broken variants each fail, plus four for `private_contact.yml` (no name, bad id, unknown attribute, non-string title). The schema doesn't catch a form with only markdown, so the script checks that each form has a field.
  - A script checks that ids and labels are unique.
  - The code of conduct diffed against the upstream 2.1 text shows only the contact line.
  - Every relative link resolves. External links answer 200/301/302, apart from two kinds: the github.com profile/security pages that this sandbox's proxy blocks (403), and the two post-tag links in a comment.
  - `dotnet build Rimlight.sln -c Release` gives 0 warnings, and `dotnet test` passes 165 of 165.

### C7 notes

- Done by Claude Code on Maxwell's instruction (Codex is not working Lane A). PR [#16](https://github.com/sussyswimmer/bordervisualizer/pull/16).
- **Structure.** The code lives in `src/Rimlight.Core/Settings/`, all internal. The namespace is `Rimlight.Core.SettingsStorage`, because `Rimlight.Core.Settings` would collide with the `Settings` record (H-002).
  - `SettingsValidator`: ranges and the H-009 rules.
  - `SettingsJson`: the format, with a source-generated writer and a field-by-field reader.
  - `SettingsMigrator`: the step table; the current version is 1 and has no steps.
  - `JsonSettingsStore`: the store.
  - `Presets.cs` and `CoreFactory.CreateSettingsStore` now use the real code. `Fakes/FakeSettingsStore.cs` is deleted. No contract changed.
- **Spec clarifications (H-009 binding; Maxwell's run decisions for item 3):**
  - **Colors:** `#RRGGBB` or CSS `#RGB` in either case, surrounding whitespace ignored, stored as upper-case `#RRGGBB`. Alpha, missing `#` and named colors fall back to the default.
  - **Numbers and enums:** numbers are clamped and NaN/±∞ fall back to the default. A JSON number is always finite, so one past `float`'s range (`1e39`) is clamped too rather than reset. Whole numbers may be written `30.0` or `3e1` (`FpsCap`, `version`). `FpsCap` outside {0, 30, 60, 120} becomes 60. Undefined enums fall back to the default.
  - **Hotkey:** only null is replaced, and an empty string means no hotkey.
  - **Monitor IDs:** only null and empty entries are dropped (also non-strings in JSON). Whitespace, duplicates and unplugged IDs are kept, compared ordinally.
  - **File format:** camelCase JSON (`version`, `primaryHex`, …), enums as C# names (`"MusicSync"`), indented, UTF-8 without a BOM. Relaxed escaping, so `"Ctrl+Alt+L"` and the `&` in monitor IDs appear as typed instead of `\u002B`/`\u0026`.
  - **Hand edits:** case-insensitive property and enum names (last duplicate wins), comments, trailing commas and a BOM are accepted. Enums must be names, not numbers.
  - **Bad fields vs bad files:** each field falls back on its own, so a mistyped field never resets the rest. Only a file that can't be read or parsed is backed up: not JSON, not an object, invalid UTF-8, or over 1 MiB (a size cap I added to protect startup).
- **Versions:**
  - A missing, non-integer or below-1 version is read as 1. One past `int.MaxValue` counts as `int.MaxValue`, so a far-newer file is never run through the migration steps.
  - A newer file loads what this build knows and isn't backed up.
  - `Save` always writes the current version and the known fields, so fields only a newer build knows are dropped after a downgrade (rare, since Velopack updates only forward).
  - Adding a schema means bumping `CurrentVersion` and appending one step; the XML docs have an example.
- **Store:**
  - `Load` never throws and doesn't create the directory. An `IOException` while opening or reading (usually a sharing violation from another process) is retried up to 3 tries, 20 ms apart, before the file is treated as bad. A transient lock at startup would otherwise return defaults, and the next save would overwrite the real settings with them.
  - `Save` writes a uniquely named temp file next to the target, calls `Flush(flushToDisk: true)`, then `File.Replace` (or `File.Move` when there is no file yet).
  - On failure it rethrows `IOException`/`UnauthorizedAccessException` and the old file is kept. There is no retry; Lane B's debounced save already catches and logs these.
  - **Partial replace (review fix):** Windows' ReplaceFile can fail after it has already removed `settings.json` (`ERROR_UNABLE_TO_MOVE_REPLACEMENT`/`_2`), leaving the new contents only in the temp file. Save then moves the temp file into place, or writes the same bytes there if the temp file is still held. If both fail, the temp file is kept as the last copy instead of being deleted. Only a complete, flushed temp file is ever used this way. The documented promise is now "the previous file, or the complete new one".
  - After each successful save, leftover `settings.json.*.tmp` files (from a crash or power loss mid-save) are deleted, best effort.
  - Calls on one instance share a lock. Separate instances never see a partial file, but on Windows ReplaceFile isn't one rename, so a load there may briefly find no file (defaults) or a locked one (retried).
- **Presets:**
  - Each preset sets the same six appearance fields, sets `OverrideAlbumColor = true` and keeps `ColorMode`. Minimal also sets Idle Glow.
  - Corner radius and taskbar coverage are kept, because they fit the screen rather than the look.
  - Applying presets in any order gives the last one's look; only Minimal's Idle Glow lingers.
  - "Slow" Ember is dim (0.6), very soft (glow 0.8) and thin-cored (4 DIP): engine speed isn't a setting, and Sensitivity is motion, which presets don't touch.
  - K7 swatches: read the colors of `Apply(new Settings())`.
- **Verification:** `dotnet build Rimlight.sln -c Release` gives 0 warnings and 0 errors. `dotnet test` passes 348 of 348, 183 of them new; the settings tests take about 1 s.
  - Atomic write is checked by failing the temp stream after 40 bytes and after 0 bytes, and by a failure just before the swap. `settings.json` stays byte-identical and no temp file is left. A second store read 300 saves without ever seeing a partial file.
  - Concurrency: 8 threads × 15 saves, and an overlap probe measured at most 1 save at a time.
  - 31 mutations (validator, reader, store, migrator, presets; list in the PR) were each killed by at least one test. Review fixes: 12 more (no restore, move-only restore, restore that reports success after failing, partial temp eligible for restore, no leftover cleanup, cleanup that also deletes unrelated `*.tmp` files, no read retry, default encoder, no float clamp, `TryGetInt32` only for `fpsCap` and for `version`, no saturation), all killed.
  - The partial replace is simulated with the `BeforeCommit` seam: it deletes `settings.json` and throws. Variants also delete the temp file, or block the target path with a directory. The lock retry holds the file with `FileShare.None`, which .NET enforces on Linux too.
  - Nothing here runs per frame, so no allocation test applies.

### C6 notes

Done by Claude Code on Maxwell's instruction (Codex is not working Lane A this run).
- **Structure:** `Lighting/LightEngine.cs` is internal and owned by the render thread. It uses three linear fades shown through smoothstep:
  - `gate`: pause, `Enabled = false` and `Animation = Off`, 300 ms out and in.
  - `shown`: Music Sync with `WhenSilent = Hide` while `IsSilent`, out over 1.5 s, back within 150 ms.
  - `music`: the Music Sync weight against Idle Glow, 1.5 s toward Idle Glow and 150 ms back.

  Visibility = gate × shown. Intensity, Pulse and drift crossfade with the music weight. `CoreFactory.CreateLightEngine` returns it, and its XML remarks carry the binding H-008 semantics, the timings and the exact `IsStatic` rules. `FakeLightEngine` is deleted.
- **Spec clarifications** (details in the PR):
  - Idle Glow breathes ±10 % around 0.9 × Brightness (81–99 %) with a 6 s sine, and has no drift or pulse (doc 01 "steady glow"; doc 02's 10 fps fits).
  - The 0.01-cycle kick per beat is a push decaying with τ = 120 ms, integrated exactly so the total is 0.01 at any frame rate. A beat is `Beat` rising by more than 0.1, and detection re-arms when it falls.
  - Spread = Glow. Doc 01's "width follows loudness" comes from the shader's pulse widening and the exp falloff scaled by Intensity.
  - A Hide fade keeps the look it starts with (Music Sync, Idle Glow or a blend), as Off does, and never crossfades while it fades out. (Review fix: a Hide fade that started from Idle Glow used to drop to the quiet Music Sync level within 150 ms first.)
  - `Enabled = false` and Off use the 300 ms pause fade. Music Sync ⇄ Idle Glow switches use the silence crossfade.
  - A new engine starts at Visibility 0 and fades in over 300 ms. While invisible, the music weight and the Hide fade jump to their targets; while the gate closes, they freeze (no flash). While silence hides the light (`shown` = 0), the gate jumps to its target too, so nothing inside the engine moves while `IsStatic` is true. (Review fix: a gate closed and reopened behind a hidden light used to move one capped step per `Update`, so how often the renderer polled decided whether sound brought the light back in 150 ms or up to 283 ms.)
  - Steps that are non-finite or negative count as 0, and steps over 0.1 s count as 0.1 s. The first step that moves time, on a new engine or after a static frame, counts as at most 1/60 s; `dt = 0` updates don't use it up. (Review fix: a renderer that passed a 0.5 s setup time as the first `dt` used to open at Visibility 0.26.)
  - Undefined enum values act as the defaults. A null palette or settings throws.
- **`IsStatic`** is true only while the light stays hidden, from the frame after Visibility reaches 0. Every visible state moves (Music Sync drifts, Idle Glow breathes), so the "fully settled visible" case never occurs. Hide targets are recomputed each frame from `Enabled`, `Animation`, `WhenSilent`, `paused` and `IsSilent`, so no settings cache is needed. For K3: keep calling `Update` with fresh audio at a low rate while Music Sync hides silence, or the return of sound goes unnoticed. For K3/K5's per-monitor pause (doc 04 §4), `paused` is one flag per engine, so give each overlay its own engine on the render thread and stop rendering only while every engine is static (this is in the `CreateLightEngine` remarks). *(K3 uses one engine with a 300 ms fade per overlay instead; CORE-API.md §6.5 describes both.)*
- **Verification:** `dotnet build Rimlight.sln -c Release` gives 0 warnings and 0 errors. 245 tests pass (80 new).
  - Of the first 69, 67 were written first and failed against a stub; 2 were added later, during mutation checking.
  - The review round added 11 more. The 6 regression cases for the findings (gate polling, Hide look, first step), and the extended wake test, failed on the previous head. The 5 freeze cases cover existing behavior that the gate jump left untested, and they were mutation-checked instead.
  - Mutation checks: 29 of 29 caught (every constant, Sensitivity re-applied, arming, instant kick, the `IsStatic` frame delay, stall and wake caps, the gate freeze, frozen clocks, the invisible snap, music-weight gating, dt/settings/color sanitizing, and the phase-rounding guard). The review round added 10, all caught: no gate jump, the gate jump only while silence still hides the light, Hide crossfading the music weight, no first-step cap, `dt = 0` using up the cap, no gate freeze, no music-weight freeze, and three ways of breaking the hidden test.
  - Real-analyzer integration: 19 beats moved the phase 0.36500 cycles (0.175 drift + 19 × 0.01). The light was gone 1.5 s after `IsSilent` (2.0 s after the last packet) and back 150 ms after `IsSilent` cleared.
  - Cost on this Linux machine (Release, informational): about 70–76 ns per `Update`. `UpdateDoesNotAllocate` measures 0 B over 50,000 updates that cycle every mode.

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

### C12 notes

Done by Claude Code on Maxwell's instruction (both lanes this run). Branch `wip/c12-soak-perf`, review fixes on `wip/c12-soak-perf-fix`, merged in e28ee81. Results in `docs/PERF.md`.
- **Structure** (all in `src/Rimlight.Bench`, internal):
  - **`FramePipeline`** runs what the app's render thread runs in `MusicGlowSource.NextFrame`, step for step:
    - settings and album checks, with `SetTarget` on a change. It takes `Settings` records the way the app does, and retunes the analyzer the way `MusicGlowSource.ApplySettings` does: `analyzer.Tuning with { Sensitivity = … }`, on the render thread. That step's allocation is reported apart as `SettingsBytes`, and `SettingsApplied` says whether the frame applied a new snapshot.
    - `analyzer.Reset` (when the capture opens or closes) and `Process`
    - `blender.Update`, then `engine.Update`
    - `FillGradient` under the app's refill rule (H-006), then the motion decision
    - Stages are timed with `Stopwatch` reads between them. Allocations are counted with `GC.GetAllocatedBytesForCurrentThread` around the whole `NextFrame` call.
    - Manual color edits are not exercised: the manual palette is fixed and no schedule edits it.
  - **`PaceModel`** copies `FramePacer.Next` and the dt rule of `OverlayHost`'s loop. Rimlight.App and Rimlight.Platform are Windows-only, so both are copies. *(Since K10, `Rimlight.Platform.Tests/Overlay/BenchMirrorTests.cs` checks both copies against the app's code, and `PaceModel` has C13's fade rule; H-018, H-019.)*
  - **`SoakSchedule.Generate(seconds, rate, seed)`** builds a pure, SplitMix64-based day of listening:
    - tracks of 2.5–5.5 min at 86–174 BPM, 75 % full mixes, at −24 to 0 dB
    - each track brings new album colors with an 800 ms fade; 25 % keep the previous colors and 10 % have no art
    - a gap before every track: half silent packets, half no packets; 60 % under 2.5 s, 30 % 4–30 s, 10 % 1–10 min
    - about every 15 min a settings edit; the first toggles WhenSilent, then Brightness, Sensitivity or PrimaryRatio
    - about every 100 min, 2–15 min away: a fullscreen pause or the hotkey
    - about every 5 min a stall of 30 ms to 1.5 s
    - `Scene(rate)` is a scripted 80 s day that takes every path. It is used for JIT warm-up and in tests.
  - **`SoakRun`** simulates the capture (open while listening, 0.1 s open delay, 2 s ring, 10 ms packets), the media service (palette 0.5 s after each track starts, which counts as an input change) and the overlay loop (jitter, inputs that bring a frame forward, blocking while Still).
    - It records `StageCosts` (four constant-memory histograms) and a `MemoryLog`. Each reading holds the working set, `GC.GetTotalMemory(false)` and `JitInfo.GetCompiledMethodCount`, every simulated minute, without allocating.
    - It takes the live heap after a full GC at the end of warm-up and at the end.
    - `SoakSnapshot` is a readonly record struct, stored in an array sized by `ProgressLines` and touched before the baseline.
    - Progress lines go through `Format(SoakSnapshot, Span<char>)`, which uses `TryFormat` calls. `TryWrite`'s handler boxed 24 B per value-type hole on 8.0.32, so it is not used. The CLI writes each line with `WriteLine(ReadOnlySpan<char>)`, which allocates 0 B.
    - `MaxProgressLines` is 10,000. Above that, `SoakRun` throws and the CLI exits with 2.
    - The report adds `SettingsEdits`, `AllocatingEdits`, `SettingsEditBytes` and `HarnessBytes`. `HarnessBytes` is what the run's thread allocated between measured frames, and it is 0.
  - **`MemoryLog`** clears its arrays up front. When it is full, it overwrites the last slot, so the last reading is always the end.
  - **`SoakReport`** holds the statistics, the verdicts, the plain-text summary and the Markdown summary.
  - **`ProcessMemory`** reads the working set without allocating (`/proc/self/statm` into a stackalloc buffer with pread on Linux, so it is thread-safe; `Environment.WorkingSet` on Windows) and reads the CPU model.
  - **`FrameBenchmark`**: 2,000 warm-up frames (at least 0.5 s), then N measured frames. "steady" settles the colors; "crossfade" changes them every 0.8 s, so every frame fades and refills. `--packet-ms` delivers whole packets on the packet clock: 480, 960, 960 samples at 48 kHz, so 1.67 analyzer steps per frame. The default is one 800-sample step per frame.
- **Commands:**
  ```bash
  dotnet run -c Release --project src/Rimlight.Bench -- frame [--rates 48000,44100,192000] [--frames 10000] [--packet-ms 10]
  dotnet run -c Release --project src/Rimlight.Bench -- soak [--duration 8h] [--seed 1] [--report-every 60] [--markdown]
  ```
  `soak` exits 1 if either verdict fails; `frame` and `analyzer` exit 1 if anything allocates.
- **Verdicts:**
  - **0 B/frame** judges every measured frame, with the settings step left out. That step gets its own line: each Sensitivity edit allocates one 120 B `AudioTuning`, in the app too (H-020).
  - **Memory flat:** the working set must grow less than 1 MB from the settle point (1 h after warm-up, or an eighth of a shorter run) to the end, and the live heap after a full GC less than 64 KB from the end of warm-up to the end.
  - **Why the settle point:** the working set grows only while the JIT tiers up rarely run methods. With `DOTNET_TieredCompilation=0` the same soak grew 24 KB in 8 h and compiled 10 methods after warm-up. Warm-ups of 0.5, 10 and 20 s grew +1.4–1.6 MB from the end of warm-up, but only +0.5 MB from the settle point. A warm-up that runs until the compiled-method count stops changing stops too early: the deltas went +0, +3, +0, +0, then +67.
- **Spec clarifications and deviations:**
  - **Stage order follows the app:** `FillGradient` runs after `engine.Update`, because the refill needs the frame's Ratio. The "blender + gradient" stage is the settings and palette checks with `SetTarget` and the retune, plus `Update` and `FillGradient`. The 0 B/frame check covers the whole `NextFrame`, glue included.
  - **Built off the measured path:** Settings records and palettes are built before the run. `SetTarget` and `Reset` run inside the measured frame, as in the app, and allocate nothing; the Sensitivity retune runs there too and allocates its 120 B, counted apart.
  - **What the pace model leaves out:** per-monitor fades and unfinished presents (the app keeps the full rate for those); one monitor; Settings' "audio wanted" is off.
  - **The harness controls its own memory:** it builds every SyntheticTrack and touches its buffer before the baseline. Its first 8 h run without this showed +1.8 MB, almost all from the buffer being faulted in by the first long stall.
  - **Defaults:** soak now defaults to 8 h instead of 1 min, and `--minutes` still works. The first track stays C3's 124 BPM full mix, so the C3 soak test passes unchanged.
  - **Timings** in PERF.md are the medians of three runs, because the VM was shared (load average 1–3).
- **Results** (Release, .NET 8.0.32, 4-CPU Xeon VM, informational; medians of three runs × 30,000 frames, same session):
  - **10 ms packets (what the app gets):**
    - 48 kHz: p50 78 µs, mean 79 µs steady / 91 µs crossfade, so 0.47 / 0.55 % of one core.
    - 44.1 kHz: p50 79 µs, mean 92–100 µs, about 0.55–0.60 %.
    - 192 kHz: p50 173 µs, mean 174–179 µs, about 1.05 %.
  - **One step per frame (800 samples, best case):**
    - 48 kHz: p50 40 µs, mean 52–56 µs, about 0.31–0.34 %.
    - 192 kHz: p50 120–127 µs, mean 144–164 µs, about 0.9–1.0 %.
  - **Stages:** light engine 0.14–0.19 µs; blender + gradient 0.1 µs, or 0.8 µs while crossfading. Every row allocated 0 B and ran no GC.
  - **8 h soak, seed 1** (339 s wall):
    - 1,441,560 frames. Per-frame mean / p50 / p99 / p99.9: 87.5 / 76.0 / 259 / 567 µs.
    - CPU: 0.438 % of one core averaged over the day.
    - Allocations: 0 B per frame. Applying the 41 settings snapshots allocated 960 B, all from the 8 Sensitivity edits. Harness 0 B. GCs 0/0/0.
    - Memory: live heap +3 KB. Working set 34.2 → 34.9 MB: +756 KB in all, +536 KB after 1:00:05, trend +93 KB/h, with 232 methods compiled after warm-up. PASS / PASS.
  - **Seed 2:** 0 B in 1,500,742 frames; 600 B from 5 Sensitivity edits; harness 0 B; GCs 0/0/0. Live heap +3 KB. Working set +752 KB in all, +528 KB after 1 h. PASS / PASS.
  - **Tiered compilation off (diagnostic):** working set +24 KB in 8 h (+8 KB after 1 h), 10 methods compiled after warm-up.
  - **`--report-every 0.05` over 1 h:** FAIL before the review fix (live heap +129 KB from the harness's own progress lines, exit 1); PASS after (live heap +3 KB, working set +100 KB after the settle point, 1,199 lines, harness 0 B).
  - The soak's mean is above the one-step benchmark's because two jittered 10 ms packets often land in one frame (two analysis steps, C2), and 10 Hz frames carry ten packets (six steps).
- **Verified here (Linux):**
  - Release and Debug builds: 0 warnings, 0 errors.
  - `dotnet test Rimlight.sln -c Release`: Rimlight.Tests 681 passed at the time (24 new), IconGen.Tests 73. The Tools tests also pass in Debug and in 3 more Release repeats.
  - Checked by decompiling with ilspycmd: `Environment.WorkingSet` (Windows uses `GetProcessMemoryInfo` into a struct; Linux parses `/proc/<pid>/status` into strings, hence the statm reader); the `cpu-usage` counter (process time over `GetSystemTimes`, so a share of the whole machine); `Registry.GetValue` (Windows-only, so calls are guarded); `TextWriter.SyncTextWriter.WriteLine(ReadOnlySpan<char>)`; `MemoryExtensions.TryWriteInterpolatedStringHandler`; `RandomAccess.Read(SafeFileHandle, Span<byte>, long)`; `File.OpenHandle`; `JitInfo`.
  - Measured in scratch probes: 120 B per `AudioTuning with`, the 24 B boxing in `TryWrite`, and 0 B for span writes to Console.Out redirected to a file and to a pipe.
- **Render-thread allocation on a settings edit** (suggested HANDOFF to Lane B): filed as H-020, and answered by K10: it stays on the overlay thread, documented in `MusicGlowSource.ApplySettings`.
- **Left for later:**
  - Maxwell's Windows CPU numbers for PERF.md's "At a glance" table (H-019 item 1; the K11 checklist below).
  - K11's 8 h run of the real app (with `dotnet-counters collect`; the K11 checklist below).
  - The decimator is about two thirds of the 192 kHz frame. That is still about 1 % of a core, but it could be optimized if high-rate devices matter.

### C13 notes

Done by Claude Code on Maxwell's instruction (both lanes this run). Branch `wip/c13-core-api`, review fixes on `wip/c13-core-api-fix` (an engine wake-cap fix and a one-condition Lane B `FramePacer` change), merged in e099943. Resolves H-011, H-016 and H-017.
- **Structure:**
  - **Fakes.** `Fakes/` was already gone at the integration base: each Lane A task deleted its own fake (C2, C4, C5, C6 and C7). Nothing references `Rimlight.Core.Fakes` in code or docs, and `CoreFactory` returns only real types.
    - `CoreFactory`'s summary no longer says fakes remain. Three Audio comments now name classes instead of the tasks that built them.
    - Fakes are still mentioned as task history in doc 09 §3–4, `START-HERE.md`, `AGENTS.md` and `CLAUDE.md`. The last two are agent instructions and were not edited.
  - **`docs/CORE-API.md` (new)** is the Core reference for Platform and App. It covers:
    - the public surface, units, threading (H-004, H-017), allocation and bad input;
    - `CoreFactory`;
    - the analyzer: tuning ranges, diagnostics, and the tempo estimate;
    - the extractor;
    - the blender: crossfade, `Current`, and the H-008 gradient layout;
    - the engine: Intensity vs Visibility, fades, dt, `IsStatic` and the four H-011 guarantees with measured values;
    - settings: validation table, store, `settings.bad.json`, migration and file format;
    - presets, a consumption example, and the stale contract XML it supersedes (§10).
  - **H-016:** `BeatDetector.AddInterval` takes the median of the sorted 16-interval buffer, then averages the intervals within ±25 % of it.
    - If no interval qualifies, it keeps the median. That needs an even count split far apart, as when every third kick is missing (`AudioAnalyzerTests.AlternateMissedBeatsKeepTheMedianTempo`: 80 BPM).
    - It reuses the existing sorted span, so it allocates nothing.
  - **H-011:** `Lighting/RenderLoopContractTests.cs` (46 cases) drives the factory's `ILightEngine` the way K3 does: full-rate frames, 10 Hz polls, no calls while still, and one ordinary dt after a gap.
    - The engine met assumptions 1–3 on its own.
    - Assumption 4 broke once K3's pacing was included, which review caught. Two cases:
      - **Unpause above about 130 Hz with native pacing.** The first frame of a 300 ms fade-in is under 0.5/255 (1.6e-3 at 144 Hz). `FramePacer` then dropped to 10 Hz, and the next frame counted 0.1 s: dark for 100 ms, then about 29 %.
      - **App start.** K3's first frame comes microseconds after its loop starts, and its GPU setup makes the next dt up to 0.25 s. A µs step released the engine's 1/60 s wake cap, so the glow popped in at about 26 %, at 60 Hz too.
    - Fixed on both sides:
      - **Core:** after a new engine or a static frame, each step counts at most 1/60 s until the steps since waking add up to 1/60 s. Constant-rate sequences are bit-identical to before. Guarded by `AShortWakeStepDoesNotLetALongOneJump` (7 cases).
      - **Lane B:** `FramePacer.Next` demotes Full to Slow with nothing on screen only while `Visibility` is exactly 0.
  - **H-017:** `InstanceIndependenceTests.cs` (4 tests). No Core change.
  - **Other tests:**
    - `CoreFactoryTests` has 3 new tests: the real types, no fakes, and the exact public surface.
    - `LightEngineTests.HidingDropsAPendingBeatPush` covers a line no test killed (see the mutation checks below).
- **Spec clarifications and deviations:**
  - **Tempo:** doc 03 §4 said "median of recent beat intervals".
    - `EstimatedBpm` is now 60 over the mean of the last 16 intervals within ±25 % of their median, as H-016 proposed and as `wav-analyze` already computes its own tempo. Doc 03 §4 now says so, as a clarification (K10 docs pass).
    - Missed and extra beats still drop out.
    - It is display-only: nothing in the light depends on it.
  - **H-016's 126 BPM case was already inside doc 03's ±2 before the fix.** At a steady 60 fps it read 124.14; 174 BPM read 171.43, which is outside.
    - The new steady-rate tests check a 1 BPM bound on every reading from 10 s on, tighter than doc 03's ±2. A mean of 16 intervals is off by at most one step in 16, about 0.5 BPM at 174 BPM and 60 fps.
    - The 126 BPM cases fail before the fix only on the 1 BPM bound.
  - **H-011 details:**
    - With frames longer than 1/60 s, the first step after static counts 1/60 s, so at 30 fps a 300 ms wake fade takes 317 ms.
    - Above about 130 Hz the first frame of a 300 ms gate fade-in is under 0.5/255 (1.6e-3 at 144 Hz, 5.7e-4 at 240 Hz).
      - `FramePacer` now keeps the full rate while `Visibility > 0`.
      - The windows show from the first frame on which the fade has run 7.7 ms: the second frame at 144 Hz, the third at 360 Hz.
    - While static, the returned Intensity and Pulse still follow Level and Beat, with Visibility 0. K3 paces Listening frames at 10 Hz regardless, so nothing is drawn.
  - **H-017, shared statics:**
    - The extractor has no fields. Its scratch comes from `ArrayPool<T>.Shared` per call, which is thread-safe.
    - The only static fields in Core's Audio, Color and Lighting are all immutable:
      - `Srgb.ByteToLinearTable`, built by the type initializer and only read;
      - `PaletteBlender.DefaultRatio`, a float;
      - `LightEngine.Defaults`, an immutable `Settings`.
  - **Stale contract XML** (frozen; filed as the XML-only CONTRACT CHANGE H-023, listed in CORE-API.md §10):

    | Member | Contract XML says | Actual |
    |---|---|---|
    | `ISettingsStore` | the real implementation "arrives in C7" | It exists. |
    | `Settings` | "validation and persistence arrive in C7" | Both exist. |
    | `AudioTuning.MinFlux` | "provisional K0 default, to be tuned in C2" | It is relative to the window's RMS. |
    | `AudioTuning.Sensitivity` | inversely scales the flux multiplier | It scales by 1/√s and also relaxes the median guard. |
    | `AudioTuning.FluxThresholdMultiplier` | standard deviations above the mean | It is one of three threshold criteria. |
    | `LightState.Intensity` | "Brightness, 0..1" | It excludes Visibility. |
    | `LightState.Ratio` | 0..1 | The engine gives 0.1..0.9. |
- **Verified here (Linux, SDK 8.0.426):**
  - **Build and tests:**
    - `dotnet build Rimlight.sln` in Release and Debug, `--no-incremental`: 0 warnings, 0 errors.
    - `dotnet test Rimlight.sln -c Release`: Rimlight.Tests 717 passed at the time (657 at the base, 60 new) and icon-gen 73. `Rimlight.Core.slnf` gives the same counts.
  - **H-016, tests first:** the four new steady-rate cases failed before the fix and pass after. Readings from 10 s on:

    | Case | Before | After |
    |---|---|---|
    | 126 BPM, 60 fps | 124.14 | 125.76–126.04 |
    | 126 BPM, 30 fps | 124.14 | 125.76–126.04 |
    | 174 BPM, 60 fps | 171.43 | 173.49–174.02 |
    | 174 BPM, 144 fps | 172.80 | 173.89–174.11 |

  - **H-016, wav-analyze:** `generate --bpm N --seconds 30`, then analyze with `--no-plot` at 48 kHz and 60 fps. The analyzer's reading at 30 s:

    | Track | Steady, before | Steady, after | ±20 % jitter, before | ±20 % jitter, after |
    |---|---|---|---|---|
    | 126 BPM | 124.1 | 126.0 | 126.1 | 126.1 |
    | 128 BPM | 128.6 | 128.0 | 127.9 | 128.1 |
    | 140 BPM | 138.5 | 139.8 | 140.2 | 140.1 |
    | 174 BPM | 171.4 | 174.0 | 173.5 | 173.6 |

    - The tool's own tempo (beat intervals) was 126.0, 128.0, 140.0 and 174.0 (173.9 with jitter), before and after.
    - From 10 s on, the steady readings were fixed at 124.14, 128.57, 138.46 and 171.43 before. After, they ranged 125.76–126.04, 127.86–128.00, 139.81–140.15 and 173.49–174.02.
    - A scratch scan covered the test kick track at 90–180 BPM, steady 30, 50, 60, 75, 90, 120, 144, 165 and 240 fps, at 48 and 44.1 kHz. The worst error went from 2.6 BPM (174 BPM at 30/60 fps; 176.5 at 50 fps) to under 0.5 BPM everywhere.
    - `wav-analyze selftest`, analyzer column: 120.2/127.7/119.8/175.1 before, 120.1/127.9/119.9/173.9 after.
  - **H-011, measured:**
    - Fastest Idle Glow change: 0.0942 per second at Brightness 1 (10 and 60 fps), 0.0094 at Brightness 0.1.
    - The silence → Idle Glow crossfade finished 1.500–1.510 s after the first silent frame, at 30–240 fps with ±20 % jitter.
    - Before landing on +0, Visibility was under 0.5/255 for 0–7 ms of a gate fade and 33–38 ms of a Hide fade.
  - **H-011 integration:** a scratch replay of `FramePacer.cs` (compiled from Platform) and `OverlayHost`'s dt rule against the real engine. *(K10 committed these cases as `PacingTimelineTests` and `FramePacerTests.AFadeUnderMinVisibilityKeepsTheFullRate`.)*

    | Case | Before | After |
    |---|---|---|
    | Unpause, native 144 Hz | 1.6e-3 hidden, Slow; then +100 ms at 0.291 | 1.6e-3 hidden, Full; then 6.9 ms later 6.2e-3 shown |
    | Unpause, native 240 Hz | then +100 ms at 0.278 | shown on the 2nd frame |
    | Unpause, native 360 Hz | then +100 ms at 0.272 | shown on the 3rd frame |
    | Start, 50 µs first frame, GPU setup 0–250 ms, 60 or 144 Hz | 2nd frame at 0.259 | 2nd frame at 0.0089–0.0090 |

    - After the fixes, the largest frame-to-frame step is 0.083 at 60 Hz and 0.035 at 144 Hz: one frame of the smoothstep.
    - With the engine fix alone, unpause at 144 Hz is dark for 100 ms, then 0.0176.
    - With the pacer fix alone, start with 150 ms of GPU setup at 60 Hz is still 0.26.
  - **Mutation checks** (scratch; each one reverted):
    - **H-016:**
      - The plain median fails the 4 steady-rate cases.
      - A mean of every interval fails `MissedBeatsDropOutOfTheTempo`.
      - `sum / inliers` without the median fallback gives 0 BPM. Only `AlternateMissedBeatsKeepTheMedianTempo` catches it.
    - **H-011:** 10 engine mutants; the new file killed 9:
      - no wake cap;
      - static on the zero frame;
      - breathing while hidden;
      - deeper breathing;
      - a 1.8 s silence fade;
      - an unweighted pulse;
      - no gate jump behind silence;
      - audio opening the gate;
      - a 1e-3 visibility floor.

      The 10th, a beat push that survives hiding, survived every Lighting test. `HidingDropsAPendingBeatPush` now kills it. The old "first moving step only" wake cap fails all 7 `AShortWakeStepDoesNotLetALongOneJump` cases (0.26–0.78 instead of 0.009–0.053).
    - **H-017:**
      - Making the engine's clocks, the blender's fade clock or `Current` cache, or the analyzer's beat static fails both the interleaving tests and the scan.
      - A static `BeatDetector` sort scratch fails the scan only. It is rewritten on every call, so it is harmless on one thread; catching it is what the scan is for.
  - **Not run on Windows:** Platform's `FramePacer` changed by one condition. App is unchanged. Visible effects:
    - the 300 ms fade-in at app start and on unpause, enable or Off → on with native pacing above 130 Hz (item 8 of the K10 Windows checklist);
    - the BPM readout in K8's visualizer.
- **Left for later:**
  - If Maxwell approves H-023, the XML-only CONTRACT CHANGE for the stale summaries above.
  - Two Lane B comments that still said C7 hadn't landed (`TrayIconHost.cs`, `AppController.cs`). *(Fixed in K10.)*
  - Spec text: doc 03 §4 said "median of recent beat intervals", and doc 02's `LightState` sample had no `Visibility`. *(Both carry clarifications since the K10 docs pass.)*

## Lane B — Claude Code

- [x] K0 scaffold — by Codex — effort: M (validation details below)
- [x] K0 follow-up: Lane B fixes on Codex's K0 (tray Efficiency Mode off, Rimlight.exe, placeholder .ico, CsWin32 DPI check), HANDOFF H-003..H-005 — PR #3 — effort: S
- [x] K1 Overlay windows, D3D11 + DirectComposition, `Glow.hlsl`, multi-monitor/DPI, device-loss recovery, topmost re-assert — PR #8 — effort: L (notes below; Windows manual test pending)
- [x] K2 `LoopbackCapture` + lock-free ring buffer + device-change restart. Render thread drains the ring → `IAudioAnalyzer` (real since C2) — PR #9 — effort: M (notes below; Windows manual test pending)
- [x] K3 Render loop: frame pacing, waitable swap chain, idle/static optimization driven by `ILightEngine.IsStatic`, battery fps cap, render scale — PR #14 — effort: L (notes below; Windows manual test pending)
- [x] K4 `NowPlayingService` (GSMTC), thumbnail decode to 64×64 BGRA → `IPaletteExtractor`, debounce/retry quirks, tray tooltip — PR #18 — effort: M (notes below; Windows manual test pending)
- [x] K5 System watchers: monitors, power/battery, lock, display-off, fullscreen detection, per-monitor pause — PR #19 — effort: M (notes below; Windows manual test pending)
- [x] K6 Tray icon + menu, single instance, hotkey, startup registration, first-run flow — PR #22 — effort: M (notes below; Windows manual test pending)
- [x] K7 Settings window (WPF-UI, Mica) with all 6 pages, live preview, presets row, monitor list with friendly names — PR #24 — effort: L (notes below; Windows manual test pending)
- [x] K8 `--debug-visualizer` window (binds `AnalyzerDiagnostics` + live `AudioTuning` sliders + "Copy params as JSON") and `--demo` mode — this branch (claude/confident-mccarthy-gt8tif) — effort: L (notes below; Windows manual test pending)
- [x] K9 Velopack bootstrap in App (`VelopackApp.Build().Run()`), update checks, "Update ready" toast — this branch (claude/confident-mccarthy-gt8tif) — effort: M (notes below; Windows manual test pending)
- [x] K10 Integration pass once C13 is merged: run everything for real, fix seams, and file HANDOFF entries for Core issues — `wip/k10-integration` + `wip/k10-fix`, on top of claude/confident-mccarthy-gt8tif — effort: L (notes below; Windows manual test pending)
- [ ] K11 Hardening on real Windows: sleep/wake, RDP, TDR, hotplug, device switching. Results go in PROGRESS.md. (needs Maxwell's Windows PC; checklist below)
- [x] K12 `assets/icon.svg`, `assets/banner.svg/png`, README, repo settings text for Maxwell, recording instructions — this branch (claude/confident-mccarthy-gt8tif) — effort: M (`assets/icon.svg` came with C10; notes below; Windows manual test pending: the recordings and the repo settings)
- [x] Feedback round 1: dynamics, soft edge, Settings failure message + WPF smoke tests — branch claude/confident-mccarthy-gt8tif — effort: L (Windows confirmation pending)
- [x] Feedback round 2: real-music corpus (`tools/corpus/`), dynamics retuned on it, an edge with no rim, Record 2 min — branch claude/confident-mccarthy-gt8tif — effort: L (Windows confirmation pending)
- [x] README cut to a short landing page at Maxwell's request ("no one wants to read this much"): an animated header with a wave bar (`assets/header.svg`, `footer.svg`), an 8 s preview and four looks rendered by the real beat engine through a port of `Glow.hlsl` (`tools/readme-art/`), and the FAQ collapsed. Replaces doc 08's longer layout. — effort: S
- [x] Black-and-white covers glow soft white and grey instead of the manual colors or an invented blue (Maxwell: "a song that is white and black ... put light blue and dark blue"). Near-neutral colors keep their chroma up to 0.05, and only a blank image counts as no art. Muted covers (chroma 0.05–0.10) now glow paler. `wip/bw-album-colors` 05a621f, HANDOFF H-027. — effort: S
- [x] Black-and-white covers, again: on 1.0.0 with Spotify on Windows, some covers were "still just light blue and dark blue". An all-black cover was still "no art" (the manual #7C5CFF / #22D3EE), and a kept cast of chroma 0.02 glowed slate blue. Now only an image with no opaque pixel is no art, and a palette whose colors are both at or below chroma 0.045 glows pure grey and white (`Glow.ColorScale`). Measured on 35 real covers; notes under "Black-and-white covers, again". Released as 1.0.1. — effort: S

## macOS (Claude Code)

The native Mac app (docs/10-MACOS.md): Swift/AppKit over Core compiled with NativeAOT. Milestones M0–M8 are in 10-MACOS §7.

- [x] Design: docs/10-MACOS.md, researched against Apple's and Microsoft's documentation on 2026-10-19; HANDOFF H-029 (Lane A items) and H-030 (Maxwell's decisions) — branch `claude/mac-m0-design` — effort: M
- [x] M0 skeleton + macOS CI job: `src/Rimlight.Native` (C ABI: ABI version, name, version, settings load and save), its tests, the Swift package (`mac/`), `mac/build.sh` (universal, ad-hoc signed, zipped), the `macos` job in ci.yml — branch `claude/mac-m0-skeleton` — effort: M (notes below; Mac manual test pending)
- [x] M1 glow on every display with the demo beat: the overlay loop (`MacOverlayLoop`, `MacGlowSource`) in C# over the shared FramePacer, GlowConstants and OverlayRules; `Glow.metal` (a port of `Glow.hlsl`) over `glow_math.h`; the render thread with display-link pacing; one panel per screen — branch `claude/mac-m1-glow` — effort: L (notes below; Mac manual test pending)
- [x] M2 system audio → Core → glow: a Core Audio process tap (C IO proc into a C ring), drained by the render thread into the shared frame source; device changes, health checks, the "can't hear" warning; the Mac's frame source proven equal to the Windows one — branch `claude/mac-m2-audio` — effort: L (notes below; Mac manual test pending)
- [x] M3 menu bar, Settings, saved settings: the menu bar menu (doc 06 §2), the Settings window's six pages with a live preview drawn by the overlay's shader, presets, the level meter, live edits saved through Core's store 500 ms after the last, the first-run welcome — branch `claude/mac-m3-settings` — effort: L (notes below; Mac manual test pending)
- [x] M4 album-art colors: Music and Spotify through their distributed notifications; Music's cover by Apple event (only while Music runs), decoded to 64 × 64 BGRA and handed to Core's extractor; doc 05 §1's debounce and retry; the Windows tooltip text; the Color page's Now playing — branch `claude/mac-m4-album` — effort: M (notes below; Mac manual test pending)
- [x] M5 system watchers: sleep and wake, displays asleep, lock, fast user switching, battery and Low Power Mode (Reduce at half scale and 30 fps, or Pause), the per-display full-screen pause, the Dock's room re-read, the tap rebuilt on wake — branch `claude/mac-m5-watchers` — effort: M (notes below; Mac manual test pending)
- [x] M6 hotkey, login item, single instance: the global shortcut through Carbon (no Accessibility), parsed and formatted by the Windows `HotkeyGesture`; a recorder on the Behavior page; launch at login through SMAppService; one copy at a time (a lock file; a second launch opens Settings or starts the demo in the first) — branch `claude/mac-m6-shell` — effort: M (notes below; Mac manual test pending)
- [x] M7 packaging and updates: `RimlightSetup.dmg` and the update zip from `build.sh` (ad hoc, per Q2); Sparkle 2 with an EdDSA-signed appcast as a release asset; "Check for Updates…" and AutoUpdate; Mac jobs in `release.yml` (H-031) — branch `claude/mac-m7-release` — effort: M (notes below; needs the update key from Maxwell, and a Mac manual test)
- [x] M8 README and docs: a Download for Mac button and macOS badge, a Mac (preview) install with the Gatekeeper and audio-permission steps, Mac lines in How it works, Privacy, FAQ (Open Anyway, uninstall, logs), build from source and the roadmap; Sparkle in Credits; RELEASE-GUIDE §7 (update key, link checks, a Mac test, a Mac recording) — branch `claude/mac-m8-docs` — effort: S (notes below)

### Fix: Mac album colours, after Maxwell's test

- **What he saw:** a black-and-white song glowed light and dark blue. Those are the default manual colours (#7C5CFF / #22D3EE), so no cover was used.
  - Core's fix from PR #32 (05a621f) is merged into the stack, so in Music a black-and-white cover now glows soft white and grey, as on Windows. The Mac calls the same extractor through `rl_album_art`.
  - Maxwell was playing from **Spotify**, which gives the Mac only a web address for its cover. He said yes to fetching it (Q4), so the Mac now asks Spotify for `artwork url` by Apple event (Automation, "control Spotify") and downloads it from Spotify's image hosts only: HTTPS, an ephemeral session, a 4 MB cap, a retry after 0.75 s, then the manual colours. Browsers still give no cover (they would need a private API, Q5: no).
- **Music:**
  - The cover is read with `raw data` and, for streamed tracks that lack it, `data` as a fallback.
  - Every failure is now logged with its reason (Automation refused, no cover, not decodable, blank), so `log show` tells which one it was.
- **README:** the short README from PR #32, with a Download for Mac button and one Mac install line (Open Anyway).

### M8 notes

- **The Mac is a "preview" in the README:** CI builds and tests it on every change, but no one has run it on a real Mac yet (every milestone's Mac manual test is still pending). Doc 08 says no claims that aren't true, so the button's subtitle and the Install heading say so, and the section asks for bug reports.
- **Links:**
  - The Mac links (`RimlightSetup.dmg`) use the same `releases/latest/download/` form as Windows, so like those they work from the first release on.
  - The shields.io badges were fetched: they render, with the Apple logo.
  - The FAQ's log command uses the bundle ID the app logs under.
- **Facts checked against the code:**
  - Album colours come only from Music (Q4 keeps Spotify off).
  - The update toggle is on the About page.
  - ⌃⌥L is the default shortcut.
  - Settings live in `~/Library/Application Support/Rimlight`.
  - Logs go to the unified log. `AppPaths`' comment said `~/Library/Logs`, which nothing writes, so the comment was corrected.
- **Not changed:** the banner and its tagline, which say nothing about Windows. The downloads badge still counts `RimlightSetup.exe` only.

### M7 notes

- **Downloads:** `mac/build.sh` now also makes `artifacts/mac/RimlightSetup.dmg`: a compressed HFS+ image holding the app and an Applications shortcut, checked with `hdiutil verify`. The zip stays as Sparkle's update archive. `RIMLIGHT_VERSION` sets the version from the tag, as `pack.ps1 -Version` does on Windows.
- **Updates (`Updater.swift`):**
  - `SPUStandardUpdaterController`, started once the settings are applied.
  - `automaticallyChecksForUpdates` and `automaticallyDownloadsUpdates` follow AutoUpdate, and `sendsSystemProfile` is off.
  - The feed is `releases/latest/download/appcast.xml`. **Check for Updates…** is in the menu and on the About page.
  - Without a public key in Info.plist there is no updater, no menu item, and the About page says the copy makes no network requests.
- **Release workflow (Lane A file, H-031):**
  - `mac-build` (macOS, no secrets) builds, tests and keeps the DMG and zip.
  - `mac-appcast` (tag only, key set up) checks Sparkle's archive SHA-256, signs and writes `appcast.xml`.
  - `publish` attaches the Mac files to the draft before it is published. A release without the key still ships the DMG.
- **Why Sparkle works without a Developer ID:** Sparkle requires either a valid EdDSA signature or a matching code signature, not both (`SUUpdateValidator.m`, 2.x). An ad-hoc copy is checked by EdDSA alone. Sparkle's framework keeps its own signature, which is ad hoc too: its helpers have no Team ID, so its installer asks nothing of the app's signature, and the app has no hardened runtime, so library validation doesn't apply.
- **Unverified until a real release:** an install from the DMG (Gatekeeper's Open Anyway), and an update from one tagged release to the next. Each update is a new ad-hoc identity, so macOS may ask for the system audio and Automation permissions again (§6, point 2).
- **Key safety:** `appcast.sh` checks the new signature against `SPARKLE_PUBLIC_KEY` with CryptoKit (`mac/tools/verify-signature.swift`), and checks that the app in the zip carries that key. A key pair that doesn't match fails the release's Mac part rather than shipping copies that can never update. In that case the Mac files are left out of the release.
- **The Windows release never waits on the Mac.** If the Mac build or appcast fails, the release goes out without the Mac files. The previous release's `appcast.xml` is carried forward, so `releases/latest/download/appcast.xml` keeps answering installed copies.
- **Installs on quit:** a menu bar app that starts at login rarely quits, so a downloaded update may wait until the next restart or log out. That matches Windows, which applies on restart. Sparkle's gentle reminders are on, so a scheduled update's alert isn't lost behind other apps.
- **Download counts:** GitHub counts each `appcast.xml` download, so that number roughly counts update checks. Nothing about the Mac is sent.
- **Pinning:** `mac/Package.resolved` pins Sparkle's commit, and its manifest pins the framework zip's checksum.
- **Review:** an independent review checked the work against Sparkle 2.10.0's source. Fixed after it: the key pair check, the release not waiting on the Mac, the appcast carried forward, the updater re-applied only when AutoUpdate changes, a quiet `sign_update` failure, and the signing wording.
- **Tests:** `appcast.sh` with a stand-in signer gives valid XML; Swift `UpdaterTests` cover when a copy can update.

### M6 notes

- **Shortcut (doc 06 §4):**
  - The setting keeps the Windows format, "Ctrl+Alt+L". `HotkeyGesture.cs` is compiled into the native library (`rl_hotkey_parse`, `rl_hotkey_format`), so both apps read and write it the same way. "Win" stands for ⌘ on the Mac.
  - Swift maps Windows virtual-key codes to Carbon key codes (US positions) and registers with `RegisterEventHotKey`, which needs no Accessibility or Input Monitoring permission.
  - On top of the Windows rules, the Mac asks for ⌃ or ⌘ (or a function key), since macOS 15 refuses Option- and Shift-only shortcuts.
  - It also refuses ⌘Q, ⌘W, ⌘H, ⌘M, ⌘Tab and ⌘Space, which a global shortcut would take from every app (the Mac's version of Windows' `IsSystemKey`).
  - Insert maps to the Help key and Oem102 to § (ISO). Pause, PrintScreen, ScrollLock and F21–F24 have no Mac key.
  - The Behavior page tells the two failures apart: "Another app uses this shortcut" or "This shortcut doesn't work on a Mac" (for example Alt+L from a Windows file).
  - **Glow On** in the menu shows the shortcut, only while it is registered.
- **Recorder:** Behavior › "Glow on and off" with Record, Cancel (or ⎋) and Remove. It takes the next key pressed in the window.
  - While it records, the current shortcut is let go, so it can be pressed into the recorder.
  - It stops when the page goes away or the window loses focus, so its key monitor can't keep swallowing keys.
- **Open at login:**
  - `SMAppService.mainApp`, registered only while not yet registered. If the user turns it off in System Settings › Login Items, the app doesn't register again over their choice; the Behavior page shows the approval note instead.
  - When macOS wants approval, the Behavior page says so, with a button to Login Items.
  - On by default, as on Windows.
- **One copy:**
  - An exclusive `flock` on `~/Library/Application Support/Rimlight/instance.lock`, held for the app's life.
  - A second launch posts a distributed notification (`<bundle id>.show-settings`, or `.demo` with `--demo`) and quits, and the first copy opens Settings or starts the demo.
  - Anyone can post those notifications, but all they can do is open Settings or start the demo.
  - Opening the app from Finder or Launchpad while it runs starts no second process. macOS sends the running copy a reopen event, which opens Settings too.
- **Tests:**
  - C#: 38, including the shortcut text through the C ABI.
  - Swift: ⌃⌥L, a pressed key becoming "Shift+Win+K", Option-only refused, macOS's own ⌘ keys refused, Insert and Oem102, and the key code map both ways.
- **Review:** an independent review found that the recorder's monitor could outlive the page, and that a reopen from Finder did nothing. Both are fixed, along with the login item re-registering over the user's choice, the wrong warning, and the menu showing an unregistered shortcut.

### M5 notes

- **What pauses the glow everywhere** (`SystemWatcher`, main thread):
  - sleep, `NSWorkspace.willSleep`/`didWake`;
  - the displays sleeping, `screensDidSleep`/`Wake`;
  - another user switching in, `sessionDidResignActive`/`BecomeActive`;
  - the lock, the distributed `com.apple.screenIsLocked`/`Unlocked`, undocumented and used by Chromium.
  - Any of these pauses it, through `rl_loop_set_paused`. The light engine fades out, the tap closes, and frames stop once the glow is static, as on Windows.
- **Wake:** the overlays are rebuilt, and an open tap is rebuilt on the new devices.
- **Battery:** `IOPSNotificationCreateRunLoopSource` + `IOPSGetProvidingPowerSourceType`. Low Power Mode (`NSProcessInfoPowerStateDidChange`) always reduces and never pauses.
  - "Reduce" halves the drawables (the layer stretches them back) and passes the halved pixels per point. FramePacer caps at 30 fps from the same flag.
  - "Pause" pauses the glow (OverlayRules).
- **Full screen (doc 04 §4):**
  - Checked when an app comes to the front and every 2 s, with a 0.5 s timer tolerance so the wakes coalesce.
  - A layer-0 window of the frontmost app whose bounds exactly cover a display pauses that display's glow (`rl_loop_set_surface_paused`, a 300 ms fade, keeping the window).
  - This catches macOS full-screen Spaces, borderless games and full-screen video. Window bounds and owners need no permission.
  - The same tick re-reads the layout, so the glow follows the Dock when it moves or auto-hides with "Cover the menu bar and Dock" off.
- **Behavior page:** Pause in full-screen apps and videos, and On battery (Keep going / Save power / Pause).
- **First macOS run:** the tests called the static geometry helpers, which inherited the class's main-actor isolation. They are now `nonisolated` (pure functions).
- **Independent review**, two blockers fixed:
  - **Notch.** A full-screen window on a notched MacBook starts below the camera housing, so it never matched the screen exactly. A window filling the display below `safeAreaInsets.top` now counts.
  - **Low Power Mode always reduces** (30 fps at half scale) and never pauses, as 10-MACOS §5 says. Before, it followed the On battery setting. `rl_loop_set_display` takes it separately (ABI 7); see `LowPowerModeReducesWhateverTheBatterySettingAndNeverPauses`.
  - **Also fixed:**
    - a power change that keeps the render scale no longer makes new windows, so the glow doesn't blink;
    - the 2 s layout check waits for a pending display rebuild;
    - wake rebuilds once the displays settle;
    - a missed display-wake can't leave the glow paused;
    - the window list isn't read while the glow is paused;
    - the full-screen setting applies at once.
  - **The full-screen pause is sticky, as on Windows.** For each display, the frontmost app window that reaches it decides, whichever app is in front, so the glow stays paused over a video while you work on another display.
- **Tests:**
  - Swift: the window-list coordinate conversion for screens above and beside the main one, and a window below the notch.
  - C#: Low Power Mode. The loop tests already cover the per-screen fade, the pause and Reduce.
- **M1's checklist changes:** with "Pause in fullscreen apps" on (the default), the glow now fades out on a screen in a full-screen app. Turn it off to check that the overlay still shows above full-screen apps.

### M4 notes

- **Track changes:** the distributed notifications `com.apple.Music.playerInfo` and `com.spotify.client.PlaybackStateChanged`. They need no permission, and they don't launch a player.
- **Which track:** a playing one; Music's if both play. A player that quits drops out, and its colours clear.
- **The cover:**
  - Music's is read with an Apple event (`raw data of artwork 1 of current track`), only on a track change and only while Music runs.
  - That asks once for Automation ("control Music"; `NSAppleEventsUsageDescription`). A refusal (-1743) shows a note and a button to Automation on the Color page.
  - Image I/O decodes it to at most 64 × 64 BGRA, and `rl_album_art` hands it to Core's extractor on the media queue. The palette reaches the glow by reference, as on Windows.
- **Doc 05 §1's rules:**
  - a 250 ms debounce;
  - a missing cover or "no art" keeps the previous colours and is read once more after 750 ms, then the manual colours show;
  - the track ID is `app|artist|title`.
- **Spotify:** the track shows (tooltip, Color page), but its colours are the manual ones. Its cover is a URL on Spotify's server, and fetching it would be a second network call, which isn't allowed (Q4 stays no). Other players, browsers included, have no public now-playing API on the Mac (10-MACOS §4.1).
- **Tooltip:** `TrayToolTip.cs` is compiled into the native library (`rl_tooltip`), so the Mac shows exactly the Windows text: "Rimlight — Title · Artist" or "Rimlight — Waiting for music".
- **Tests:**
  - C#: 32, including a cover becoming the palette (blue primary from a 70/30 blue and orange cover), a flat grey icon being "no art", and the tooltip.
  - Swift: decoding a cover to 64 px BGRA, and linear colours shown as sRGB.
- **Independent review**, two blockers fixed:
  - **Apple events could stall the media queue** for up to 2 minutes (AppleScript's default timeout), or until the Automation prompt was answered. They now run on a queue of their own with a 3 s timeout, so notifications and quits never wait behind them.
  - **A window where an Apple event could launch Music.** Both scripts first ask `application id "com.apple.Music" is running`, which never launches it.
  - **Also fixed:**
    - covers are read only while album colours are in use, so a user on manual colours is never asked about Automation;
    - a refused cover is read again when Settings opens, in case Automation was allowed meanwhile;
    - the Automation warning clears when Music isn't the source;
    - a cover read during a skip is dropped unless its track name matches;
    - the swatches show only in album mode.
- **Unverified until Maxwell's run:**
  - the Music notification's keys on macOS 26 and 27 (`Name`, `Artist`, `Player State`);
  - `raw data` of the artwork;
  - the Automation prompt's wording.

### M3 notes

- **Menu bar menu (doc 06 §2):** Glow On, Mode, Colors, Presets, Demo Mode, Settings… (⌘,) and Quit.
  - It is missing "Check for updates" (M7) and "Open logs folder": the Mac logs to the unified log (`os.Logger`, see Console.app), which has no folder.
  - The icon dims while the glow is off.
  - The tooltip is the name only until M4 brings the track.
- **Settings window:** SwiftUI, with the six pages of doc 06 §3.
  - Every change applies live through `rl_loop_apply_settings` and is saved 500 ms after the last one through Core's store. The window then shows exactly what the file holds.
  - **Appearance:** the live preview, Thickness, Glow, Brightness, Corner radius, Cover the menu bar and Dock, and the presets as swatches.
  - **Color:** album art or manual, Override, two colour pickers and the ratio. A footnote says album colours come later (M4).
  - **Motion:** mode, When it's quiet, Sensitivity, the level meter, and the "can't hear" warning.
  - **Displays:** all, main or chosen displays (by UUID), and the frame rate limit.
  - **Behavior:** Hide from screen recordings (`sharingType`). Pause in full screen, battery, launch at login and the hotkey arrive with M5 and M6, and aren't shown before they work.
  - **About:** the version, source and licence, the Mac's privacy statement, and "Show the Settings File in Finder".
- **Preview:**
  - Core's `MacPreview` mirrors the Windows `GlowPreview`: its own engine and blender, the latest audio, and the glow "as it looks when on", treated as a 1280-point screen.
  - Its constants are drawn by the overlay's own Metal shader into a small layer, so it can't drift from the overlay. Windows needed a CPU port.
  - It runs at 30 fps only while the window is visible (occlusion state), and keeps the audio open while it runs, as the meter does (`rl_loop_set_audio_wanted`, MusicGlowSource.SetAudioWanted).
- **First run (PRD §7, 10-MACOS §2.1):**
  - Settings opens with a welcome banner that says why macOS will ask for System Audio Recording.
  - The audio tap stays closed until **Start** is clicked or the window is closed, so the prompt comes after the explanation.
- **First macOS run:** one compile error. `$model.settings.animation` resolved to SwiftUI's `Binding.animation(_:)`, not the field. `SettingsModel.binding(\.animation)` fixes it.
- **Independent review**, three blockers fixed:
  - **The preview stopped for good** when it first appeared before layout, because a 0×0 surface returned an error that stopped its timer. It now waits for layout and stops only on an internal error. It also follows a change of backing scale.
  - **The meter didn't wake a stopped loop**, so it stayed at zero with the glow Off. Settings wanting the audio is now an input, like demo mode (`TheMeterWakesAStoppedLoop`).
  - **Closing or minimizing the window on the Motion page left the meter, the tap and the loop running**, because SwiftUI doesn't fire `onDisappear` there. The meter is now its own `MeterModel`. It runs only while the page is shown and the window is visible (occlusion state), and publishes only changed values, so nothing else redraws at 30 Hz.
  - **Also fixed:**
    - a reload after save now reaches the overlays;
    - the preview restarts its audio clock when shown again (`rl_preview_restart`, as GlowPreview.Start does);
    - a new test, `TheMeterNeverChangesTheGlow`;
    - stricter hex parsing, and a colour with no sRGB form keeps the old value;
    - Sensitivity shows two decimals;
    - one preview Metal pipeline instead of one per page switch;
    - the first run finishes only once.
- **Tests:**
  - C#: 29, including presets through the C ABI, the meter keeping the capture open and waking a stopped loop, the meter never changing the glow, and the preview's scale and fade-in.
  - Swift: the settings round trip, sRGB hex colours and presets.
  - The Mac frame source still matches the Windows one bit for bit, since the meter is off in that script.

### M2 notes

- **Capture (10-MACOS §2.1):**
  - A global stereo process tap that excludes Rimlight's own process, read through a private aggregate device whose main sub-device is the default output.
  - The IO proc is C (`TapIO.c`): it only mixes to mono into a lock-free SPSC ring (`AudioRing.c`, a port of `AudioRingBuffer.cs`) and timestamps the last callback and the last non-zero sample. No Swift or .NET runs on the real-time thread.
  - The render thread drains the ring each frame into a preallocated buffer.
  - The tap opens only while the glow listens (music sync, on, not paused, not in demo), as the Windows loopback does, so nothing is captured otherwise.
- **Permission:** `NSAudioCaptureUsageDescription` is in the Info.plist. macOS asks on the tap's first start. The microphone is never opened or asked for.
- **Resilience:**
  - The tap rebuilds when the default output device changes, and when the IO proc stops for 2 s.
  - A failed open retries with 0.5 → 5 s backoff.
  - If other processes play sound (`kAudioProcessPropertyIsRunningOutput`) but the tap hears only exact zeros for 10 s, the menu shows "Rimlight can't hear your Mac. Allow it…", which opens Privacy & Security. The tap also rebuilds at most once a minute, the mitigation for the macOS 26.5 zero-buffer bug.
- **Same frames as Windows, proven:**
  - `MacGlowSourceMirrorTests` runs a 45 s script (album changes, settings edits, silence, no packets, Off, pause) with synthetic 120 BPM music through `MacGlowSource` and Bench's `FramePipeline`. The light state, refill, gradient and motion match bit for bit on every frame, with more than 100 pulsing frames.
  - Bench's file is compiled into the test as a link, so there's no Lane A edit, and H-029 item 3 isn't needed any more. Platform's `BenchMirrorTests` already holds `FramePipeline` equal to `MusicGlowSource`.
  - To match Windows exactly, the analyzer now resets whenever the capture opens, closes or restarts (a capture generation in `rl_loop_begin_frame`, ABI 3), not on a sample-rate change.
- **Verified on Linux:**
  - The ring's C tests: wraparound, full ring, skipping the oldest samples, interleaved and planar mono.
  - `TapIO.c` compiled with `-Wnullability-completeness -Werror` against stub headers.
  - C#: 24 tests.
  - The IO proc's nullability matches `AudioDeviceIOProc`'s, so Swift passes the C function as one.
- **First macOS run:** two Swift compile errors. `CATapDescription` takes `[AudioObjectID]`, not `[NSNumber]` (the first review had it wrong too), and `.unmuted` needed its type once that was fixed. Both are fixed.
- **Independent review**, three blockers fixed:
  - **Microphone safety.** The aggregate listed the output device as a sub-device, and an aggregate exposes its sub-devices' inputs, so a USB headset could have had its mic opened. Now the output device joins only when it has no input streams; otherwise the aggregate holds the tap alone. The tap refuses to start unless the aggregate shows exactly one input stream, the tap's.
  - **Health checks after opening.** They now measure from the open (`rlc_tap_reset_times`), with a 3 s grace period, so a fresh tap isn't flagged or restarted during the permission prompt.
  - **No restart loop.** A tap that stops delivering goes through the backoff, which resets only once callbacks arrive.
  - **Also fixed:**
    - planar buffers use the shortest buffer's length;
    - the rate is the aggregate's nominal rate, and a rate change restarts the tap;
    - device-change bursts are debounced (0.2 s);
    - the health timer restarts with each open;
    - the mirror test now restarts the capture while capturing;
    - `Self.address`, the timebase is set up at creation, and the sample buffer is freed in `deinit`.
  - **Deviation from 10-MACOS §2.1:** the "can't hear" warning is a menu bar item until Settings exists (M3), where it moves to Motion.
- **Unverified until Maxwell's run:**
  - **no orange microphone dot in the menu bar**, with built-in speakers, AirPods and a USB headset;
  - that a tap-only aggregate (headsets) delivers audio;
  - the exact wording of the prompt;
  - that "Allow it…" lands on the right pane (`Privacy_ScreenCapture`, the pane that holds "System Audio Recording Only");
  - the latency, which should be under 50 ms per doc 03 §3.

### M1 notes

- **Shared with Windows, compiled as is:**
  - `GlowProfile.cs`, `GlowConstants.cs`, `FramePacer.cs`, `OverlayRules.cs`, `IOverlayFrameSource.cs`, `SrgbHex.cs` and `DemoBeat.cs`.
  - The one Windows file changed is `DemoBeat.cs`: `PaletteFade` is now written out as 800 ms rather than read from `MusicGlowSource.AlbumFade`, which the Mac doesn't have. It's the same value, and `DemoBeatTests` now checks the two are equal.
  - The Mac's frame source `MacGlowSource` mirrors `MusicGlowSource`, without the Windows capture, media service and visualizer.
  - The Mac's `MacOverlayLoop` mirrors OverlayHost's `RenderFrame`: the per-screen pause fades, the transparent frame before a window hides, a surface that has presented nothing for a second counting as stalled, and the static-frame skip (`LooksLike`). The pacing is FramePacer's.
- **The shader:** `Glow.metal` is a port of `Glow.hlsl`. Its math lives in `glow_math.h`, which is both MSL and C.
- **Verified:**
  - `glow_math.h` compiled as C on Linux matches the C# golden table (`GlowGoldenTests` writes `mac/Tests/RimlightKitTests/Fixtures/glow-golden.json` from GlowProfile and GlowConstants) to 3e-8.
  - On the macOS runner, the Swift tests check the constants through the AOT library, the per-pixel alpha through `glow_math.h`, and **the Metal shader on the runner's GPU** (it has one): alpha within 1.5 code values of the profile.
  - C#: 23 tests, including a zero-allocation frame on three screens, and the struct sizes against the header's static asserts.
  - A C program drove the loop through the AOT library on Linux.
- **Pacing:**
  - The full rate runs on the primary screen's `CADisplayLink` (`NSScreen.displayLink`), on the render thread's run loop, at the cap's rate.
  - The slow rate and a 0.1 s fallback (Windows' `VsyncFallbackSeconds`, for a primary screen that stops refreshing) use one re-aimed timer.
  - Blocked means no link and no timer: the thread sleeps until an input.
- **Review (independent), fixed before the PR:**
  - New windows after a display change were never shown on screens whose size hadn't changed: every surface now starts over.
  - Demo mode didn't wake a stopped loop: it is now an input.
  - The gradient texture could be rewritten while a frame still read it: it is now a ring of three.
  - A window could be hidden before its transparent frame was on screen: the hide now waits for the presented handler, and a later show cancels it.
  - A stuck in-flight count: it resets after 1 s.
  - The display link had no fallback timer.
  - Render scale: the render thread will pass halved surfaces in M5, so the constants take no render scale of their own.
  - Sendable annotations.
  - Shader math in `.safe` mode on macOS 15+.
  - Per-screen pause is now its own call (`rl_loop_set_surface_paused`) and keeps the window.
- **Known limits until later milestones:** no audio (M2: the glow shows Idle Glow, or the demo), the settings only through Glow On and the file (M3), no watchers (M5: battery, sleep, full-screen pause), and no single instance (M6: quit before starting `--demo` from Terminal).

### M0 notes

- **Answers:** Maxwell said "Just build it, no app developer": Swift (Q0) and no Apple Developer account (Q2). The app is signed ad hoc: no hardened runtime yet, since that needs a Developer ID to be useful.
- **Verified on Linux:**
  - `dotnet test src/Rimlight.Native.Tests -c Release`: 11 passed.
  - A NativeAOT publish for linux-x64: no trimming or AOT warnings, a 2.7 MB library exporting the five `rl_*` functions.
  - A C program compiled against `rimlight_core.h` with `-Wall -Wextra -Werror` called every export through the published library.
  - The Swift code, the bundle and the Mac publish can only be checked on the macOS job.
- **Bug found by the C smoke test, fixed before the PR:** saving settings JSON that left a field out turned it off. `{"glow":0.7}` saved `"enabled": false`, because the JSON source generator sets every init-only property of a record and gives a missing one its type's default. The transport now lays the given fields over the serialized defaults first (`FieldsLeftOutKeepTheirDefaults`).
- **Choices** (simplest, per CLAUDE.md):
  - Settings cross the ABI as JSON in the file's own format, not a struct (10-MACOS §1.3, changed from the design's sketch). Core's JSON and validator are internal, and a struct can't carry the monitor-ID list simply.
  - The Swift package links libRimlightCore at build time. `build.sh` builds it first into `mac/.build-core`.
  - The bundle ID is `io.github.sussyswimmer.` followed by the lower-case `<AppName>`.
- **CI:** the `macos` job runs on pull requests and `workflow_dispatch` only (not on pushes to main), on `macos-26`.
- **First macOS run (workflow_dispatch, run 39193560386):** green on the first try, with all three jobs passing. The universal build had zero warnings, the 6 Swift tests passed against the AOT library, and the zipped app is 3.0 MB.
- **Independent review:** no blockers. Fixed:
  - `Utf8Text.Read` now uses `CreateReadOnlySpanFromNullTerminated`, which can't read past a page.
  - Each export has its own try/catch: the shared delegate allocated a closure per call, which M1's per-frame calls must not do.
  - `build.sh` fails if the development rpath survives in the bundled app.
  - `CFBundleVersion` drops any prerelease suffix.
  - "Open Settings Folder" creates the folder first.
  - The header lists every status.
  - The intermediate publish folders moved out of `artifacts/`.
  - Kept: `rl_header_abi_version` (it is the C target's one source file), and the 512 px icon until M7 adds a 1024 px one.

## Fix: a settings save that fails is tried again

`SettingsFlowTests.ChangesAreSavedHalfASecondAfterTheLast` failed twice on Windows CI (runs 39194057907 and 39198227918, both on Mac PRs that touch no Windows code), and passed on the same commits otherwise.

- **Cause:** `SettingsService.Flush` logged a failed save and dropped it, so the change waited for the next edit. On Windows a new file is often held for a moment by another process, an antivirus scan for one. The user's last change could then be lost until they changed something else, and the test waited for a file that never came.
- **Fix:** a failed save keeps the change pending and is tried again after 1 s, up to 5 times. After that the next change tries again, as CORE-API §7.2 asks. Quitting still saves at once.
- **New test:** `ASaveThatFailsIsTriedAgain`, with a store whose first two saves fail.
- **Effort:** S.

## K0 notes

- Contracts are defined by doc 09 §3; source specs are unchanged. `AudioTuning` and `AnalyzerDiagnostics` are data containers, not DSP implementations. Doc 03 does not specify a numeric `minFlux`; K0 uses a provisional 0.01 pending C2 tuning.
- Palette-blender and settings-store fakes complete the five factory methods; they deliberately provide immediate palette changes and in-memory settings only. `Presets.All` remains a C7 placeholder. *(All fakes are gone since C7, and C7 filled the preset catalog.)*
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
  - With no overlay (no selected monitor present) the loop stops its frame timer and releases the GPU until a display or settings change brings one back. *(Changed in K10: no overlay counts as paused. The frame source keeps running, so the capture closes and Settings' meter and the visualizer keep working; no GPU device is held.)*
  - DIP sizes become pixels at each monitor's `GetDpiForMonitor` effective DPI.
- **GPU (doc 04 §2):**
  - One D3D11 device (hardware, else WARP) and one DirectComposition device are shared by every overlay.
  - Each overlay has a composition swap chain: B8G8R8A8_UNORM, premultiplied, FLIP_SEQUENTIAL, 2 buffers, maximum frame latency 1. Its latency object is polled without blocking, so a frame is skipped rather than queued when DWM or the display is behind.
  - `DXGI_ERROR_DEVICE_REMOVED/RESET` from Present, resize or creation releases everything. The device and surfaces are recreated on the next frame and retried every second while that fails; the windows stay.
  - Debug builds have a tray item, "Simulate GPU device loss (debug)", that runs the same path (doc 07 Phase 1). *(Since K3: "Render test (debug)" > "Simulate GPU device loss".)*
  - `Glow.hlsl` is embedded and compiled at runtime (vs_4_0 / ps_4_0, feature level 10.0+). The gradient texture is R16G16B16A16_FLOAT, which every feature level ≥ 10.0 must be able to filter (32-bit float filtering is optional there).
- **Spec clarifications and deviations (doc 04 §3):**
  - **Rounded glow field (deviation):** the glow's distance field has corners of radius ≥ 2 × spread. With doc 04's square field, the glows from two edges meet in a visible 45° crease (a picture-frame bevel); rounded, the light pools softly into the corners. The solid core still follows `CornerRadiusDip`.
  - **Perimeter coordinate:** arc length along a rounded path (radius ≥ 6 × spread and ≥ 2 × the core), starting at the top-left corner and running clockwise (H-008). It is continuous through the corners, at most 0.0008 per pixel within 150 px of the edge. Its seam (the medial axis, ≥ that radius inside) is hidden: between 3 and 6 spreads deep the colour blends to the gradient's mean, so a wide glow that reaches the screen centre shows no seam.
  - **Glow → spread (PRD §1):** "0 means a hairline; 100 means about 35% of the shorter screen dimension". Spill is taken as three e-folding distances, with a squared slider so it feels even. The e-folding distance is max(1 DIP, Glow² × 0.35 / 3 × shorter side); the default 0.45 gives about 25 px at 1080p.
  - **Core at thickness 0** draws no line.
  - **sRGB:** the gradient is linear RGB (H-008). The 8-bit swap chain is read by DWM as sRGB-encoded, so the shader encodes before premultiplying.
  - **Dither** is ±0.5/255 interleaved gradient noise, added to all four premultiplied channels so every colour channel gets the full half LSB. Pixels whose alpha is below half a code value return early ("cost almost nothing").
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
  - **Lower-latency loopback (deviation from using `WasapiLoopbackCapture` as is):** NAudio's class always uses a 100 ms buffer polled every ~50 ms (really ~62 ms at the default 15.6 ms timer tick), so packets can be ~60 ms old. That is outside doc 03 §3's 10–20 ms loopback budget. Our own capture loop on NAudio's `AudioClient`/`AudioCaptureClient` opens the loopback stream event-driven (supported from Windows 10 1703; the minimum is 2004), so each ~10 ms engine packet is read as it is produced. The 200 ms buffer is only overflow headroom; a 100 ms wait timeout covers silence (loopback signals nothing then), a stop, and a driver that never signals. The thread runs above normal priority. The loop replaces `WasapiCapture` because that class's capture thread calls `IAudioClient::Stop` in a `finally`, which throws on an invalidated device (unplug) on a thread with no handler and ends the process. Its `Dispose` also races its own thread. Packets go straight from the WASAPI buffer into the ring.
  - **Formats:** 32-bit float, or 16/24/32-bit PCM by container size (24-in-32 is read as 32-bit), detected from the format tag or the `WAVE_FORMAT_EXTENSIBLE` subtype. Channels are averaged to mono. Any other mix format fails the session, which is then retried with backoff.
  - **Restarts:** a default render device change (Multimedia role, settled 250 ms) restarts at once. A stopped capture (unplug, format change, exclusive takeover) or no device at all is retried after 0.5, 1, 2, 4, then 5 s, and the backoff resets after a 5 s healthy session. While there is no device, `Current` is null and the analyzer gets empty spans (silence after its timeout).
  - **Allocations:** none per packet. Packets are converted straight from the WASAPI buffer into the ring through a mono scratch array allocated once per session.
  - **Privacy:** only a loopback stream on a render endpoint is ever opened. No capture endpoint and no microphone is touched, and nothing is sent anywhere.
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes.
  - A scratch harness (not committed; Lane B has no test project) checks the ring's semantics and stress-tests it between two threads: 102 M samples, 0 out-of-sequence values, including while it was full and with small reads. It also checks the mono conversion is exact for float32, int16, int24 (sign extension), int32 and 5.1, and that convert + write + read allocates 0 bytes. *(K10 committed these checks as `AudioRingBufferTests` and `MonoConverterTests`. They found that a zero-channel format threw instead of returning 0, a latent bug now fixed.)*
  - An adversarial multi-agent review (WASAPI/NAudio, concurrency, integration) runs on this PR; its upheld findings are fixed here before merge.

## K3 notes

- **Structure:**
  - `FramePacer` (`src/Rimlight.Platform/Overlay/`, no Windows calls) decides how the overlay thread waits after each frame: full rate, 10 fps, or blocked. `OverlayHost` waits accordingly in `MsgWaitForMultipleObjectsEx`, so it keeps pumping messages in every mode.
  - `IOverlayFrameSource.NextFrame(dt, settings, paused, gradient)` gets the settings snapshot and the pause state every frame. It returns `OverlayFrame(state, gradientChanged, FrameMotion)`:
    - `Full`: music, or a transition.
    - `Slow`: Idle Glow breathing.
    - `Listening`: static, but music could change it.
    - `Still`: static until an input changes.
  - Inputs for K5 are thread-safe single-field writes followed by a wake: `SetPaused`, `SetOnBattery` (starts from `GetSystemPowerStatus`), and `SetPausedMonitors` (GDI device names; `DisplayMonitors.PrimaryDeviceName()`). `OverlayHost.Status` (`RenderStatus`) reports pace, frames/s, presents/s, half scale and shown overlays.
  - `LoopbackCapture.SetActive` opens or closes the loopback stream on the capture thread, and is inactive until first asked. `MusicGlowSource` asks for audio only in Music Sync while the glow is enabled and not paused. *(K7 and K8 added listeners: Settings' meter and preview, and the visualizer. The demo keeps the capture closed.)*
  - **App:**
    - `AppController` is the composition root; `App.xaml.cs` only creates and disposes it.
    - `SettingsService` holds the immutable snapshot. `Update` runs on the UI thread and raises `Changed`. Saves go through `ISettingsStore` with a 500 ms debounce, serialized, and are flushed on exit.
    - `Update` never waits on a save: the pending flag is lock-free, and only `Flush` takes the save lock, around the store call.
    - The tray has "Glow on" and "Mode", both checkable so screen readers hear their state. Debug builds add "Render test (debug)": the live status plus stand-ins for K5/K7 (When silent, FPS cap, On battery, simulated battery, pause everywhere, pause primary, device loss). The status names the wait the loop actually took (vsync or timer).
- **Pacing (doc 02):**
  - The full rate is FpsCap (0 = the primary monitor's refresh, from `EnumDisplaySettings`). It never exceeds that refresh, and is at most 30 fps under "Reduce on battery".
  - Source motion picks the pace:
    - `Full`: full rate.
    - `Slow`: 10 fps.
    - `Listening`: 10 Hz frames that present nothing new.
    - `Still`: no frames; the thread waits only on the wake event and messages, with no timer.
  - Host overrides:
    - An unfinished present or a per-monitor fade runs at the full rate until it is done.
    - A surface that has taken no frame for 1 s (display off, DWM stalled) no longer holds the full rate. Its frame is retried at the source's pace, but at least at 10 fps, so the loop never stops while it still has a frame to show. One that should hide is hidden without its transparent frame, so a display that goes dark during a fade-out still lets the loop stop. Shown again, it may show its last frame for a refresh, as it would have shown it when the display came back.
    - An input draws a frame at once only from 10 fps or stopped. At the full rate the next scheduled frame draws it, so a slider drag stays within the cap (30 fps on battery).
    - `Slow` runs at the full rate for 1 s after an input change, and for 0.5 s after a gradient refill or a state change faster than 0.25/s (idle breathing is about 0.1/s).
    - With nothing on screen, frames run at 10 Hz. *(Changed in C13: only while the light is fully hidden, at Visibility 0, so a fade-in keeps the full rate. K8: not while the visualizer observes. K10: no overlay at all counts as paused.)*
  - **Static:** each surface remembers what it last presented and skips Draw and Present while the new constants look the same: identical geometry, values within half an 8-bit code value, phase within 0.0002.
  - **Hidden:** an overlay whose visibility drops below 0.5/255 presents one transparent frame, then hides its window. DWM then composes nothing of ours, and fullscreen apps below can flip directly. The window is shown again, topmost, before its next visible frame waits for the swap chain (and before a pending scale switch), because DWM may hand back the frame the transparent present holds only once the window is shown. Windows are created hidden.
  - **Time:** a gap over 0.25 s, or a frame that an input brought forward, gets one full-rate frame of dt. There are no dt spikes, and fades start on the frame after the input.
  - **Capture** is closed whenever audio can't change the glow: Off, `Enabled = false`, Idle Glow, and paused. The analyzer then hears silence. With When silent = Hide, a static invisible glow keeps listening at 10 Hz. *(Unless Settings or the visualizer asks for audio, since K7 and K8.)*
  - **No overlay** (Custom monitors unplugged or none selected): the source gets one paused frame, so the capture closes and no stale audio piles up, and then no calls until an overlay exists again. *(Replaced in K10, kept by the stack merge: no overlay counts as paused, and the source keeps getting frames as its motion asks. The capture closes as for any pause, while Settings' meter and the visualizer still get their frames, and no GPU device is held. The K7 review's `IOverlayFrameSource.FramesStopped`, which closed the capture even while Settings showed the audio, is gone.)*
  - **Palette crossfades** run in the blender, outside the engine, so they count as motion even while `IsStatic` is true: the source reports `Full` until the fade ends.
  - **Device check:** a surface that hasn't been able to take a frame for 1 s gets a device-health check, at most once a second. K1 counted 60 skipped frames, which takes 6 s at the 10 Hz vsync fallback.
- **Battery and render scale (PRD §5, doc 04 §2):**
  - "Reduce" caps the rate at 30 fps and renders at half scale.
    - Each swap chain is half the window's size, rounded up. Its DirectComposition visual is stretched back by `SetTransform` (an exact window/buffer ratio), with linear interpolation and hard borders so the edge-hugging core stays solid.
    - Constants are computed in swap-chain pixels (DPI scale × render scale), so every pixel measure halves. Only the thin-core fade (`CoreFade`, in the former padding slot) stays in screen pixels, so a 1-DIP core keeps its brightness and its light at half scale. The shader's spread floor is 0.5 px, which is one DIP at half scale.
    - A surface switches scale only on a frame where it can present at once (its frame-latency object is signaled). It then resizes, rebinds the pipeline (a resize clears the context state), draws, presents and commits the new transform back to back. A surface that can't present yet keeps its old scale, with matching constants, and retries at the full rate. Hidden overlays switch once shown again: the window is shown first, still transparent, so a battery change while the glow is hidden can't keep it from coming back.
    - Swap-chain presents are not part of the DirectComposition transaction, so the switch is not strictly atomic. A refresh composed between the present and the commit, or before the GPU finishes that frame, could still show one mismatched frame. A fully atomic switch would need a second swap chain (or `SetSourceSize` + `SetMatrixTransform`), which can't be tested here.
  - "Pause" counts as paused.
- **Per-monitor pause (doc 04 §4):** each overlay fades its own visibility multiplier over 300 ms. Once every overlay is paused and fully faded, the engine also gets `paused`, so it goes static and the loop stops.
- **Spec clarifications and deviations:**
  - **Native pacing waits on the frame-latency object, not `DwmFlush` (doc 02).**
    - When the full rate equals the refresh (native, or a cap at least 97% of it, so 60 on 59.94 Hz qualifies), the thread waits on the overlay swap chain's frame-latency object inside `MsgWaitForMultipleObjectsEx`. With a maximum latency of 1, it is signaled when DWM takes the previous frame.
    - `DwmFlush` would block for up to a frame without pumping messages. It also follows DWM's clock, not our surface.
    - A successful wait takes the frame, so the surface remembers it holds it (`HoldsFrame`) and presents without waiting again; the host never waits on a frame it holds.
    - A 100 ms timer covers a display that stops refreshing.
    - Vsync is used only on a surface whose monitor has the pacing refresh rate, so a faster secondary can't exceed the cap. With an unknown refresh rate only Native uses vsync.
    - Lower caps use K1's waitable timer. Cap 60 on a 60 Hz display is now vsync-paced, which removes the timer's drift against the refresh.
  - **A cap above the refresh runs at the refresh** (120 on 60 Hz → 60).
  - **Capture also closes in Idle Glow and while paused,** not only for Off and disabled. K7's live level meter and K8's visualizer will need a way to request audio while they are open. *(Done: `AppController.RequestAudio()` and `MusicGlowSource.SetAudioWanted` in K7, used by K8 too.)*
  - **The `AppController`/`SettingsService` skeleton lands in K3** because K3 needs live settings. The store stays in memory until C7. *(C7's `JsonSettingsStore` since the integration.)*
  - **What the loop assumes about C6** (`IsStatic` means time alone changes nothing; hidden means `Visibility == 0`; idle motion is slow; dt after gaps) is in HANDOFF H-011.
- **Left for later:**
  - K5 drives `SetPaused`, `SetOnBattery` (`WM_POWERBROADCAST`) and `SetPausedMonitors`. *(Done in K5.)*
  - K7/K8 need an "audio wanted" input on the source. *(Done in K7.)*
  - Swap chains stay allocated while Off. They could be released after a long Off if memory matters.
  - The fake engine has no fades, no Hide and no breathing, so those paths can't be seen on Windows until C6. *(Obsolete: the real engine since C6.)*
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes (165).
  - Signatures were checked against CsWin32 0.3.346's generated sources (`EnumDisplaySettings`/`DEVMODEW`, `GetSystemPowerStatus`, `MsgWaitForMultipleObjectsEx`, `SWP_HIDEWINDOW`), decompiled Vortice 3.8.3 (`IDCompositionVisual.SetTransform(Matrix3x2)`, `SetBitmapInterpolationMode`, `SetBorderMode`) and H.NotifyIcon 2.3.2 (the WPF `ContextMenu` opens with `IsOpen`, so `Opened` fires).
  - **Scratch harness 1 (not committed; Lane B has no test project)** links `FramePacer.cs` and `GlowConstants.cs`. *(K10 committed these checks as `FramePacerTests`, `GlowConstantsTests` and `PacingTimelineTests`, with the real engine.)*
    - It checks 14 cap/refresh/battery combinations, every pace decision, the change-rate threshold, the `LooksLike` tolerances, and that half scale halves every pixel measure except `CoreFade`, which matches full scale (and the old `saturate(CoreThicknessPx)` there) for 0–6 DIP at 100–200 % DPI.
    - Pacer + constants allocate 0 bytes over 100,000 frames.
    - An 85 s timeline with a C6-like engine reaches these steady states:
      - music: 60 frames and presents/s
      - silence → Idle Glow: 10/10
      - Hide: 10 frames/s, 0 presents, hidden, listening
      - Off, global pause and single-monitor pause: 0/0, capture closed
      - Idle Glow: 10 fps
      - Reduce: 30 fps
    - Timings: music returns from Hide within 83 ms. Off and pause stop after 283 ms, and a monitor pause hides after its 300 ms fade. The largest dt is 100 ms. *(With the real engine, K10: 53 ms, and 383–400 ms.)*
  - **Scratch harness 2** runs the real `MusicGlowSource` (real analyzer, fake engine) on Linux:
    - motion is correct in every mode
    - gradient refills are exact
    - 0 bytes per frame in music sync, Idle Glow, Off and paused
    - `SettingsService` debounces and flushes saves
  - Three bugs found by walking through scenarios were fixed before the PR:
    - the half-scale resize cleared the bound pipeline
    - fades jumped up to 100 ms ahead when an input arrived between 10 fps frames
    - vsync on a faster secondary monitor could exceed the cap
  - Review fixes (two adversarial reviews, Windows-runtime and spec): render-scale switch gated on a frame that can present; one paused source call when the last overlay goes away; a time-based device check; screen-pixel `CoreFade`; crossfades count as motion; a lock-free save flag; checkable tray items; the status shows the wait actually taken.
  - Second review round:
    - A hidden overlay is shown before the scale-switch gate. Before, a battery change while hidden could keep the glow from returning, at 30 fps of retries.
    - Stalled surfaces drop to 10 fps or hide, so a display-off fade-out no longer spins at the full rate all night.
    - Inputs at the full rate wait for the next frame. A drag had run frames at the input rate and presents at the refresh, past the battery cap.
    - Harness 1 now models the frame-latency object, assuming DWM returns it only to a shown window on a lit display. It replays each scenario against the old and the new logic:
      - resume after a battery change while hidden: never → 17 ms
      - display off then pause: 30 frames/s all night → stopped 0.57 s after the pause
      - Idle Glow with the display off: 30 → 10 fps
      - a 200-inputs/s drag on battery: 200 frames/s and 60 presents/s → 30/30
  - Nothing has run on Windows yet: Maxwell's checklist, with CPU measurement steps, is in #14.

## K4 notes

- **Structure:**
  - `NowPlayingService` (`src/Rimlight.Platform/Media/`) reads Windows' media sessions on the thread pool.
    - `RequestAsync` runs once. If it throws, `IsAvailable` is false and the glow keeps the manual colors for the session.
    - Manager events (`CurrentSessionChanged`, `SessionsChanged`) and every session's `MediaPropertiesChanged` and `PlaybackInfoChanged` only schedule a refresh. `RefreshScheduler` debounces them: 250 ms after the last event, and at least once a second under a stream of events. Retry and reconnect deadlines (`RequestBy`) are kept as given; the 1 s cap applies to event bursts only. Without any event a refresh still runs every 30 s. One refresh runs at a time.
    - A refresh picks the playing session (Windows' current one if several play, or if none plays) and reads its properties. The track ID is `app|artist|title`.
    - The thumbnail is read for a new track and again after every `MediaPropertiesChanged` (or session/list change), never after a play/pause alone. Those events set a flag that the next refresh claims. A picture that is already shown is not extracted again; any other picture is decoded and passed to `IPaletteExtractor`. So a session that first shows the previous track's cover or a placeholder gets the real colors when the real cover arrives.
  - Published, immutable and by reference: `Current` (`NowPlaying`), `AlbumPalette` (null = manual colors), `Art` (≤ 64×64 straight-alpha BGRA8, for K7's "Now playing" row). `NowPlayingChanged` and `AlbumArtChanged` fire on the pool only on real changes.
  - `AlbumArtDecoder`: `BitmapDecoder` with a `BitmapTransform` (Fant, ≤ 64 px on the long side, aspect kept, never enlarged), `Bgra8`/`Straight`, EXIF and color profile ignored. Rows are repacked to exactly width × height × 4 bytes, which the real extractor (C4) requires.
  - `MusicGlowSource` reads `AlbumPalette` once per frame.
    - Album colors are used in Album Art mode without Override; the manual colors otherwise, and whenever there is no album palette.
    - `SetTarget` gets 800 ms for album changes and album/manual switches, 200 ms for manual edits. It reports `Full` motion while the blender animates.
  - `OverlayHost.RequestFrame()` wakes a static glow for a new palette.
  - `TrayToolTip` builds the tooltip, and `AppController` marshals it to the UI thread (coalesced) and disposes the service first.
    - `SetNowPlaying` compares with `TrayIcon.ToolTip`, the text the shell last accepted. After a refused update the `ToolTipText` property already holds the new text, so the icon is then updated directly.
    - On `TaskbarCreated` (Explorer restarted; H.NotifyIcon has already re-added the icon with its last accepted text) the tray re-applies the current tooltip and calls `NowPlayingService.Refresh()`.
  - The tray menu gains doc 06's "Colors ▸". Debug builds add a media status line and an "Override album color" toggle.
    - "From album art" also clears `OverrideAlbumColor`, because H-009 presets set it, so the item always brings the album colors back. The check marks show the colors actually in use: with Override on, "Manual" is checked even in Album Art mode.
- **Spec clarifications and deviations (doc 05 §1, §3; doc 06 §2):**
  - **`BitmapAlphaMode.Straight`** (doc 05 says "premultiplied-ignore"): the extractor reads straight color and skips alpha < 128, and `Ignore` leaves the alpha byte undefined.
  - **The tooltip names the track only while it plays**; paused reads "Waiting for music". Doc 05 exposes `IsPlaying` for the tooltip.
  - **Tooltip text:**
    - It is cut to 127 UTF-16 units (NOTIFYICONDATA.szTip) between whole text elements, with "…". The title gives way before the artist: the artist gets whatever room the title leaves, and never less than half.
    - Control characters become spaces.
    - A failed `Shell_NotifyIcon` update is caught (H.NotifyIcon throws).
  - **No art → manual colors** (doc 05 also allows keeping the previous palette). A missing thumbnail, one that won't decode and a generic app icon (the extractor's "no art") are treated alike. The previous colors stay, the track is read once more 750 ms after it first had no cover, and only then do the manual colors show. Tracks therefore never flash the manual colors when the cover arrives late.
  - **200 ms only for a manual edit while manual colors show**; every switch between album and manual colors uses 800 ms.
  - **Robustness additions:**
    - All sessions are subscribed, so a session that starts playing is noticed even if Windows keeps another one current.
    - The debounce waits at most 1 s.
    - Properties and decodes are bounded at 5 s.
    - A read that fails without a lost manager (a busy app timing out, a COM error from a live session) is tried once more after 750 ms, because a live session may already have sent its last event for a track change. If that retry fails too, the app is left to its events and the 30 s refresh rather than polled; a read that succeeds re-arms the retry.
    - A session manager whose process died (e.g. an Explorer restart) is dropped and requested again every 5 s. A dead manager raises no events, so it is noticed in three ways: any WinRT call in a refresh failing with an RPC "disconnected" error while the manager itself no longer answers (a closing session alone doesn't count), the 30 s idle refresh, and `Refresh()`, which the tray calls on `TaskbarCreated`.
  - `NowPlaying.SourceApp` is the raw `SourceAppUserModelId`. Track text is never logged.
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes (165).
  - Every WinRT, CsWinRT, H.NotifyIcon and WPF call was checked against the decompiled assemblies:
    - the SDK projection 10.0.19041.57
    - `AsTask(token)` only cancels the operation, hence `WaitAsync`
    - `EventSource` add/remove semantics
    - `TrayIcon.UpdateToolTip` throws, and `SetTo` truncates at 127
    - `Dispatcher.InvokeAsync` after shutdown
  - The real C4 extractor accepts the decoded layout and rejects padded rows.
  - **Scratch harness (not committed), 73 checks:** *(K10 committed the tooltip, `SrgbHex` and palette-selection checks as `TrayToolTipTests`, `ColorTextTests` and `MusicGlowSourceTests`.)*
    - session choice, decode sizing and repacking, and tooltip fitting over 20,000 random titles with emoji, ZWJ and combining marks
    - the `SrgbHex` round trip
    - scheduler timings (debounce, burst, endless stream, retry, 4-thread hammering)
    - the unavailable path on Linux
    - `MusicGlowSource`'s full palette-selection matrix with a recording blender, including 0 bytes per frame
  - The harness found one scheduler bug, fixed before the PR: a compare-and-swap claim could spin forever under a stream of events.
  - **Review fixes** (an adversarial review with a Windows-runtime lens and a spec lens). A second scratch harness with 27 checks links `NowPlayingService` with a stand-in decoder and extractor and drives the art logic through reflection. It covers:
    - the stale previous cover replaced by the real one
    - a generic icon followed by the cover 400 ms later, going straight from one track's colors to the next
    - an icon or missing thumbnail that stays: manual colors after 750 ms
    - no thumbnail read on play/pause, and no extraction of a picture already shown
    - a decode failure
    - the scheduler: `RequestBy(+5 s)` fires after 5 s (it fired after 1 s before the fix), a burst is still capped at 1 s while a far deadline is pending, the idle refresh, and cancellation.
    - The first harness's 73 checks still pass.
  - **Second review round** (same two lenses), three fixes:
    - The tooltip capped the artist at half the room even with a short title. It now gets what the title leaves, at least half.
    - `ShowNowPlaying` clears its coalescing flag with `Interlocked.Exchange` rather than a volatile store. On x64 a plain store can pass the following read of `Current` (store buffer), and a track published in that window would never reach the tooltip.
    - The one early read retry above. Before this, after a failed read the previous track could stay for up to 30 s.
    - The harnesses gained 13 checks: 6 for the tooltip, including 20,000 ASCII pairs that are uncut when they fit and otherwise fill the room with the artist keeping at least half; 7 for the retry, covering one retry at 750 ms, none after it fails again, re-arming, an earlier deadline kept and the loop woken on time. All 113 checks pass.
  - Nothing has run on Windows yet: Maxwell's checklist is in #18.
- **Left for later:**
  - **K6:**
    - Re-apply the tooltip after `TaskbarCreated` (Explorer restart). A tooltip update refused by the shell is otherwise retried only on the next track. *(Done in K6: the tray keeps the tooltip it wants and re-applies it on every (re)creation; its own `TaskbarCreated` handler runs after H.NotifyIcon's re-add, retries a failed one and calls `NowPlayingService.Refresh()`.)*
    - Check whether a single `&` shows in the tooltip. *(K6: it becomes the fullwidth "＆".)*
  - **K7:**
    - The "Now playing" row reads `Current` and `Art` (compare `Art.TrackId` with `Current.TrackId` to hide stale art), with swatches via `SrgbHex.Format`. *(Done in K7.)*
    - Subscribe to the two events and marshal them to the dispatcher. *(Done in K7.)*
  - **C4/C5 (via K10):** with the fakes, colors are averaged, never "no art", and switch instantly. *(Obsolete: the real extractor and blender since C4/C5; K10 checked the crossfades against them.)*

## K5 notes

- **Structure (`src/Rimlight.Platform/Watchers/`):**
  - `SystemWatcher` runs its own thread with a hidden top-level window and a `GetMessage` loop. `WM_POWERBROADCAST` and `WM_DISPLAYCHANGE` reach top-level windows only.
    - It publishes an immutable `SystemState` (locked, disconnected, suspended, display off, battery, remote session, fullscreen everywhere, fullscreen monitors) on every real change.
    - It raises `StateChanged` and `Resumed` (wake, unlock, reconnect, display back on) on its own thread.
    - `Start` waits up to 2 s for the first state. `Dispose` unregisters everything on the watcher thread, then destroys the window.
  - `PowerWatcher`:
    - `PBT_APMSUSPEND` / `PBT_APMRESUMEAUTOMATIC` / `PBT_APMRESUMESUSPEND`.
    - `PBT_APMPOWERSTATUSCHANGE` → `GetSystemPowerStatus`.
    - `RegisterPowerSettingNotification(GUID_CONSOLE_DISPLAY_STATE)`: 0 is off, dimmed is on.
  - `SessionWatcher`:
    - `WTSRegisterSessionNotification(NOTIFY_FOR_THIS_SESSION)` for lock/unlock and console/remote connect/disconnect.
    - The state at start comes from `WTSSessionInfoEx`, so a start while locked starts paused.
    - Registration is retried every 5 s while the session service is still starting.
  - `FullscreenDetector` gathers facts from Windows, and `FullscreenRules` (pure, no Windows calls) decides.
    - It re-evaluates on `EVENT_SYSTEM_FOREGROUND`, every 2 s, on `WM_DISPLAYCHANGE`, and right after a wake or unlock.
    - It runs only while `PauseInFullscreen` is on.
  - `OverlayHost.CheckDevices()` (new, thread-safe) asks the GPU device on the next frame and schedules K1's debounced monitor rebuild, which also catches a replaced adapter.
  - Paused monitors are now applied before settings, so overlays created at start begin paused on a fullscreen monitor.
  - **App:** `SystemPauseBridge` maps the state onto `SetPaused` / `SetOnBattery` / `SetPausedMonitors` (sent only on change) and `Resumed` onto `CheckDevices`. One lock orders the watcher and UI threads; it is never taken per frame.
    - The Debug menu's simulated battery and pauses (K3) are combined with the real state.
    - The Debug menu adds a `System:` status line and a "Pause in fullscreen apps" toggle.
- **Effects:**
  - Lock, a disconnected session, sleep, display off, a screen saver (`QUNS_NOT_PRESENT`) and presentation mode pause everywhere. That goes through the engine's `paused`: no frames, and the capture is closed.
  - Fullscreen apps pause per monitor through K3's 300 ms monitor fade.
  - Battery feeds K3's "On battery".
- **Spec clarifications and deviations:**
  - **Folder `Watchers/`, not doc 02's `System/`:** a `…System` namespace would shadow `System`. K6's hotkey and startup classes can go to `Platform/Shell/`.
  - **Own thread and top-level window, not doc 02's message-only window:** message-only windows get no broadcasts. Keeping it off the overlay thread means shell queries and session RPCs never touch frame timing.
  - **Fullscreen pauses only its monitor (doc 07 Phase 5 over doc 04's literal `QUNS_BUSY` → pause):**
    - Doc 04's rule: the foreground window's `GetWindowRect` equals its monitor's `rcMonitor`. Maximized windows never match, because of their resize borders.
    - With `QUNS_BUSY` or `QUNS_RUNNING_D3D_FULL_SCREEN`, a non-maximized window that covers its monitor also counts. An exclusive-mode window fills its output, so a real game passes.
    - Windows' state alone never pauses a window that doesn't cover its monitor. D3D is still reported for a moment after you switch out of an exclusive-mode game, so Alt+Tab or a click on another monitor must not pause that monitor. D3D pauses everywhere only when `GetForegroundWindow` returns no window at all; an excluded foreground (desktop, taskbar) keeps only the sticky entries.
  - **Sticky (addition):** a fullscreen window keeps its monitor paused while the focus is on another monitor. This lasts until it leaves fullscreen, moves, is minimized, hidden, cloaked or closed, or another window is activated on that monitor.
    - "Another window" includes File Explorer and Control Panel (`CabinetWClass`) and the shell's dialogs such as Run or Properties (`#32770`), although they run in the shell's process, and our own normal windows (settings, visualizer).
    - Only the desktop, the taskbar, Alt+Tab, Task View and our tray menu keep the pause. Shell windows are allow-listed by class, so an unknown shell surface keeps the pause. Our tray menu is a WPF popup (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, checked in PresentationFramework 8.0) that H.NotifyIcon makes the foreground window, so our tool and no-activate windows never end a pause.
  - **Private messages:** the watcher window is top-level, so it also gets every `HWND_BROADCAST`, including other apps' `WM_APP` messages. Its stop message works only after `Dispose` has set the stopping flag; the fullscreen-setting message only re-reads its flag.
  - **Never a fullscreen app:** every window of the shell's process (desktop, taskbar, Alt+Tab, Task View; F11 File Explorer as a side effect), `Progman`/`WorkerW`, the desktop window, and every window of our process (the overlays, the future settings window). Some of these still end a sticky pause (above).
  - **Detection runs whenever `PauseInFullscreen` is on**, even while the glow is off, so turning the glow on during a fullscreen app never flashes it. The cost is a few user32 calls every 2 s.
  - **Display off** applies only while the session is on the physical console (`WTSGetActiveConsoleSessionId`).
  - **Modern Standby** sends no suspend, and display off covers it. The display coming back also clears a missed suspend.
  - **RDP (decided):** the glow keeps working in a connected remote session and pauses while disconnected; fast user switching pauses it too. A reconnect re-checks the GPU and monitors. RDP bandwidth is K11's to measure.
  - **Do Not Disturb / Focus assist** has no documented API; only what `SHQueryUserNotificationState` reports pauses.
  - **F11-style fullscreen** (no foreground change) is found by the 2 s poll, per doc 04's cadence.
  - **Only the foreground window is found:** at start, and when detection is turned back on, a fullscreen window on another monitor counts only after it has had the focus once. Turning detection on from the tray menu finds nothing until the video is clicked, because the menu takes the foreground.
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes (165).
  - Every new Win32 call, struct and constant was checked against CsWin32 0.3.346's generated sources. That includes `GUID_CONSOLE_DISPLAY_STATE`'s value, `POWERBROADCAST_SETTING`'s inline data, `WTSINFOEX_LEVEL1_W`, `QUERY_USER_NOTIFICATION_STATE`, and `WTS_CURRENT_SERVER_HANDLE` living on `HANDLE`.
  - `GetWindowLong` (`GetWindowLongW`, `int`, `WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE` = -20) is checked against CsWin32's generated sources too.
  - **Scratch harness (not committed)** linking `FullscreenRules.cs` and `SystemState.cs`, 53 checks: *(K10 committed them as `FullscreenRulesTests`; the stack merge added the review's sticky-exit and D3D cases.)*
    - exact and loose cover, and maximized windows never pausing (also a loose entry maximized later)
    - `QUNS_BUSY` never pausing everywhere
    - D3D: a covering window pauses its monitor, a small or maximized one doesn't, an excluded foreground pauses nothing, and no foreground window at all pauses everywhere
    - Alt+Tab out of an exclusive game while D3D is still reported: a small or maximized window on the other monitor, or a click on the desktop, keeps only the game's monitor paused
    - screen saver and presentation mode
    - every sticky keep and drop case
    - File Explorer or our settings window: ends the sticky pause on its own monitor only (also under D3D), and never pauses itself, even when it covers a monitor with `QUNS_BUSY` or D3D reported
    - `SystemState` equality and `PausesEverywhere`
  - Nothing has run on Windows yet: Maxwell's checklist is in #19.
- **Left for later:**
  - **K7:** the Behavior page's "Pause in fullscreen apps" toggle (already wired through `settings.Changed`). `SystemState` can feed diagnostics. *(Done in K7.)*
  - **K11:** sleep/hibernate, Modern Standby, RDP (bandwidth too), TDR and hotplug on real hardware. Also check whether `QUNS_NOT_PRESENT` ever shows up spuriously, for example in a remote session. *(Rows 1–8 of the K11 checklist below.)*
    - CPU during a fullscreen pause can't be read with Task Manager in front: activating it on that monitor ends the pause. Use Task Manager with "Always on top" and Alt+Tab back to the fullscreen app, or log `typeperf "\Process(Rimlight)\% Processor Time" -si 1`.
  - **C6 (via K10):** global pauses fade over 300 ms once the real engine lands (H-008); with the fake they are instant. *(Done: the real engine fades them; K10 measured the stop at 383–400 ms.)*

## K6 notes

- **Structure:**
  - `src/Rimlight.Platform/Shell/` (namespace `Rimlight.Platform.Shell`):
    - `HotkeyGesture`: pure parser and writer for H-009's `Modifier+…+Key` (105 key names; `TryCreate` is K7's recorder seam).
    - `GlobalHotkey`: `RegisterHotKey` + `MOD_NOREPEAT` on a message-only window on the UI thread. `Register` returns a `HotkeyStatus` (None, Registered, Invalid, InUse, Failed, TypesCharacter). `TypesCharacter(gesture)` is the AltGr check (below), public for K7's recorder.
    - `SingleInstance`: mutex `Local\Rimlight-{SID}` + a current-user pipe (`Rimlight-{SID}-{session}`). One server instance serves every start (`Disconnect`), reads at most 64 bytes with a 2 s timeout. A second start sends `show-settings` after `AllowSetForegroundWindow` for the running process; a `--background` start sends nothing. If nobody answers, the new start waits up to 5 s to take over.
    - `StartupRegistration`: HKCU `Run` value `Rimlight` = `"<launcher>" --background`. The launcher is Velopack's stub in the install root when installed (`sq.version` + `..\Update.exe`), else `Environment.ProcessPath`. `Remove()` is for K9's uninstall hook.
    - `SystemTheme`: light/dark app mode and the dark title bar.
  - App:
    - `AppController` owns the hotkey, the startup entry, the first-run flow and the hooks: `IShowSettings` (K7 sets `SettingsWindow`), `IUpdateCheck` (K9 sets `Updates`), `Hotkey` + `HotkeyChanged` (K7's inline warning), `StartupRegistered`, `IsFirstRunSession` (K7's one-time tray toast), `Tray.Notify` + `NotificationClicked` (K7/K9) *(changed in K9 and the K6 review: `Tray.Notify(…, clicked:)` with no event, plus the tray's "Restart to update (version X)" item)*.
    - `TrayIconHost`: doc 06's full menu, click/double-click commands, keyboard menu, dimmed icon, re-creation with the icon and tooltip re-applied. `TrayIconImages` builds both icons at `SM_CXSMICON`.
    - `WelcomeWindow`: first run, and the stand-in for Settings until K7. *(Since K7: the first run, or the fallback when Settings can't be built.)*
    - `Logging/RollingFileLog` (a `TraceListener`) + `Logging/AppLog` (start line, unhandled exceptions, open folder). *(The folder is `%APPDATA%\Rimlight\logs` since K9.)*
    - `App.xaml.cs`: claim → log → controller; exit releases the mutex last. *(K9 put `Program.Main` with Velopack's startup code in front, and the install of a downloaded update after the log; K10 made exit stop taking hand-overs first.)*
- **Spec clarifications and deviations:**
  - **Logging** is a rolling-file `TraceListener` fed by the existing `Trace` calls, not `Microsoft.Extensions.Logging` (doc 02): no package, same folder and 5 × 1 MB *(the folder moved to `%APPDATA%\Rimlight\logs` in K9, H-014)*. Lines are queued and written 500 ms later on the pool, the file is opened per batch with full sharing, a burst rolls over line by line, lines over 32 K characters are cut, and it never traces its own failures.
  - **Hotkey rules:** Ctrl, Alt or Win required, except F1–F24; F12 alone is reserved by Windows. Punctuation keys are named by their US position (`Plus`, `Minus`, `Backtick`, …).
  - **Enter/Space on the focused icon open the menu** (Windows can send `NIN_KEYSELECT` twice for Enter, which would toggle twice).
  - **The icon dims only for "Glow on" off,** not for Mode = Off.
  - **Colors ▸ From album art also turns Override album color off,** which every preset turns on (H-009), so it is the tray's way back from a preset. It is checked only while album colors show (`AlbumArt` and no Override); Manual is checked otherwise.
  - **A notification's click action:** `Notify(…, clicked:)` replaces the previous notification's action. It runs on the dispatcher after H.NotifyIcon's window procedure returns (the click arrives inside it, out of reach of the dispatcher's exception handler, and the action may quit the app), guarded by a try/catch. *(The K6 review also cleared it on a timeout or close, so a click on an older or timed-out notification did nothing. K9's review, kept by the stack merge, keeps it until the next notification instead: a toast that Do Not Disturb sends straight to the notification list could otherwise never be clicked. That is safe because "Update ready" asks before restarting, and the tray menu offers "Restart to update" too.)*
  - **`FirstRunComplete` is set when the welcome is shown.** A normal start (no `--background`) of a set-up app opens Settings; until K7 that is the welcome window. *(Since K8, a `--demo` or `--debug-visualizer` start doesn't open it either.)*
  - **The startup entry is checked at every start** (repairs a stale path). A build that isn't installed never replaces an entry pointing to an installed copy. Task Manager's `StartupApproved` switch is not touched.
  - **Tray icon size:** both icons are built at `SM_CXSMICON` (system DPI); H.NotifyIcon's own conversion picks 16 px at every DPI.
  - **Tooltip `&`** becomes the fullwidth `＆` (K4 follow-up): the shell can take a lone `&` as an access-key marker.
  - **Unhandled UI exceptions** are logged and survived, so the tray stays usable.
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes (165).
  - Signatures checked against CsWin32 0.3.346's generated sources; H.NotifyIcon 2.3.2, .NET 8's Windows pipes and `System.Drawing.Icon`, and Velopack 1.2.161 (locator and vpk's stub naming) decompiled.
  - **Scratch harness (not committed), 435 checks:** the hotkey parser (every key round-trips), the rolling log (rotation, limits, concurrency, `Trace` integration, failures), the single instance across real processes (hand-over, background start, silent and junk clients, take-over after exit and after a kill, a start during the exit teardown), the tooltip. It found two problems, fixed before the PR: a large batch could grow one log file past 1 MB, and re-creating the pipe between clients could make a waiting start miss it. K4's harness still passes against the new tooltip. *(K10 committed the parser, log, single-instance and tooltip checks as `HotkeyGestureTests`, `RollingFileLogTests`, `SingleInstanceTests` and `TrayToolTipTests`.)*
  - Nothing has run on Windows yet: Maxwell's checklist is in #22.
- **Review fixes (#22):**
  - **Right-click no longer moves the menu to the taskbar corner.** H.NotifyIcon uses `NOTIFYICON_VERSION_4`, where the shell also sends `WM_CONTEXTMENU` after a mouse right-click; the keyboard handler opened the already open menu again at the notification area. It now does nothing while the menu is open.
  - **A failed re-add after an Explorer restart is retried.** H.NotifyIcon swallows the failure, and `Removed` isn't raised because the new Explorer refuses to delete an icon it doesn't know; our own `TaskbarCreated` handler, which runs after H.NotifyIcon's, starts the retry when the icon isn't back.
  - **Single instance:** the pipe listens right after the settings load, before the slow start-up (a later start waits only 3 s for it), and `OnExit` stops listening first (`SingleInstance.StopListening`), so a start during the exit teardown takes over instead of handing its request to a process that drops it.
  - **Shortcut window missing:** an empty `ToggleHotkey` still reports `None`, and the welcome names the setting's text when there is no parsed gesture.
- **Second review round (#22):**
  - **Ctrl+Alt shortcuts vs AltGr:** Windows treats Ctrl+Alt as AltGr, so `RegisterHotKey(Ctrl+Alt+L)` would also swallow AltGr+L everywhere (ł on Polish, Czech, Hungarian, Croatian and Slovenian layouts; ø on US-International). Before registering a Ctrl+Alt shortcut without Win, `GlobalHotkey` asks every layout in `GetKeyboardLayoutList` what `ToUnicodeEx` types for the key with Ctrl+Alt (plus Shift if the shortcut has it), with flag 0x4 so the thread's dead-key state is untouched. A printable character or a dead key means it is not registered: the status is `TypesCharacter`, the welcome explains it, the tray shows no shortcut. US/UK-only users keep Ctrl+Alt+L (those layouts type nothing with Ctrl+Alt). A layout added while the app runs counts from the next registration (next start or shortcut change). The default lives in the frozen contract: H-024 asks Maxwell whether to change it *(filed as H-019 on this branch; renumbered in the stack merge)*.
  - **Logs carry no account or device names:** the log is meant for public bug reports, so `RollingFileLog` writes the profile folder as `%USERPROFILE%` (case-insensitive, only when a line is logged), and the capture line logs the mix format instead of the output device's name (Bluetooth headphones are often named after their owner).
  - **Tray choices are checkable:** Mode, Colors and Presets items now set `IsCheckable`, because WPF's `MenuItemAutomationPeer` offers the Toggle pattern only then; without it Narrator never says which item is checked. A click flips the mark, but the menu closes and `Refresh` resets every mark when it opens.
  - **Another copy running:** a start by hand compares the running copy's exe (`GetNamedPipeServerProcessId` → `QueryFullProcessImageName` with limited query rights) with its own. If they differ (say, an older dev build that started at sign-in), it sends nothing and says where the app runs, instead of leaving silently while the old build answers. Same exe, an unknown path or a `--background` start behave as before.
  - **`dotnet Rimlight.dll` doesn't register dotnet.exe for sign-in:** the startup entry is written only when the process exe carries the entry assembly's name; otherwise the existing value is left as it is (and `StartupRegistered` is false).
  - **Log file name** comes from `AppInfo.Name` (lower case), so a rename doesn't leave `rimlight.log` behind.
  - Checked with the scratch harness (now 445 checks: redaction, and the other-exe, same-exe, unknown-exe and background cases across real processes) and CsWin32's generated signatures for `GetKeyboardLayoutList`, `MapVirtualKeyEx`, `ToUnicodeEx`, `OpenProcess_SafeHandle` and `QueryFullProcessImageName`. The AltGr check itself needs Windows (Maxwell's checklist in #22).
- **Merged with the K3–K5 review fixes:** the tray keeps the tooltip it wants in its own field and re-applies it on every (re)creation, which replaces K4's comparison with `TrayIcon.ToolTip`. Its `TaskbarCreated` handler also calls K4's `NowPlayingService.Refresh()` before the re-add check. `SettingsService` keeps K3's lock-free save flag next to K6's `disposed` guard.
- **Left for later:**
  - **K7:** set `SettingsWindow`; the Behavior page's Launch at startup and hotkey recorder (`HotkeyGesture.TryCreate` + `KeyInterop.VirtualKeyFromKey`, warning from `AppController.Hotkey`); the inline warning's `HotkeyState.TypesCharacter` message, and the recorder refusing such a combination up front with `GlobalHotkey.TypesCharacter`; the one-time "still running in the tray" toast via `IsFirstRunSession` and `Tray.Notify`; the welcome then shows its "Open Settings" button; theme the tray menu. *(Done in K7; the AltGr parts in its review.)*
  - **K9:** set `Updates`; "Update ready" via `Tray.Notify` + `NotificationClicked`; call `StartupRegistration.Remove(AppInfo.Name)` from Velopack's uninstall hook; check that the stub passes `--background` through. *(Done in K9, with `Notify(…, clicked:)` instead of the event and the tray's "Restart to update" item; the stub passes arguments on.)*
  - **K10:** with C7's store, the welcome shows once and `LaunchAtStartup`/`ToggleHotkey` persist. *(Checked in K10: `SettingsFlowTests`.)*

## K7 notes

- **Structure:**
  - `src/Rimlight.App/SettingsUi/` (namespace `Rimlight.App.SettingsUi`; a `…Settings` namespace would shadow the Core record):
    - `SettingsWindow`: a WPF-UI `FluentWindow` with Mica, the title bar, and a `NavigationView` over six `ISettingsPage` user controls, handed to it by an `INavigationViewPageProvider`.
      - Close hides the window (`AppController.IsExiting` lets Quit and session end really close it), with the one-time tray toast.
      - Only the selected page is active, and only while the window is shown and not minimized. Outside events, timers and the audio request stop otherwise.
    - `SettingsWindowLauncher` is K6's `IShowSettings`. It creates the window on first use, makes it the app's main window, and falls back to the welcome if it can't be built.
    - The pages:
      - Appearance: `GlowPreview` drawn by `GlowRaster`, the shape sliders, Cover taskbar, and preset chips.
      - Color: `GlowColorEditor` ×2 and `SrgbColor`, plus the now-playing row.
      - Motion: mode, when quiet, sensitivity, and the level meter.
      - Displays: `MonitorChoice` and the frame rate limit.
      - Behavior: the toggles, battery, and the shortcut recorder.
      - About.
    - Shared: `PageEdits` (live edits, guarded while a page shows the settings) and `PageStyles.xaml`.
    - `UiTheme` loads WPF-UI app-wide, follows Windows' app mode and high contrast, and applies the brand accent.
  - Platform:
    - `DisplayMonitors.Describe()` returns the public `MonitorInfo`, with Windows' monitor names from the CCD API (`MonitorNames`).
    - `HotkeyGesture` refuses Windows' window and task keys.
  - App:
    - `MusicGlowSource.SetAudioWanted` and `LatestAudio` (`AudioReading`: one packed `long`, written with `Volatile`).
    - `AppController`: `RequestAudio()` (counted, for K8 too), `SuspendHotkey()` / `ResumeHotkey()`, `Glow`, `IsExiting`, and `EndSession()` (formerly `Flush()`).
    - The tray re-syncs the theme when its menu opens.
- **Spec clarifications and deviations:**
  - **WPF-UI 4.3.0** (newest, MIT, net8 assets) is loaded in code at start, not in `App.xaml`. A second start that only hands over doesn't pay for it, and the tray menu gets Fluent light/dark styles too (K6 follow-up). A `--background` start pays a one-time load.
  - **Theme:** it follows `AppsUseLightTheme` (as the welcome does) and `SystemParameters.HighContrast`, not WPF-UI's guess from the `.theme` file name. The accent is the brand #7C5CFF, not the system accent.
  - **Pages are user controls with their own scroll viewer.** WPF's `Frame` is an inheritance boundary (`SkipToAppNow`), so each page sets its foreground and font and merges `PageStyles.xaml`. WPF-UI styles controls implicitly, so no page sets `Style` on a WPF-UI control.
  - **Preview:**
    - `GlowRaster` ports `Glow.hlsl` into a 352×198 `Pbgra32` bitmap at 30 fps. It caches the shape per pixel, so a frame costs about one exp per lit pixel.
    - The card stands for a 1280-DIP-wide screen: thickness and corner radius scale by it, and the glow by the card's shorter side, as on screen.
    - Its own engine and blender come from `CoreFactory` (H-017; filed as H-012 on the K7 branch), with `MusicGlowSource`'s color rules and 800/200 ms crossfades.
    - Its features come from the overlay's analyzer, and count as silence after 0.5 s without a new frame.
    - It shows the glow as it looks when on: "Glow on" off, or Off mode, shows Idle Glow with a caption.
  - **Audio wanted:** the meter, and the preview in music sync, keep the capture open and the analyzer at 10 Hz or more. The overlay's engine still hears silence outside music sync, so Settings never changes the glow.
  - **Color picker:** a hex box plus hue and saturation sliders at the color's own brightness, instead of a 2-D field, so the keyboard and screen readers work with standard controls. Six-digit hex applies as it is typed; three digits apply on Enter or focus loss.
  - **Displays:**
    - All / Main display / Choose, plus a toggle per monitor. A toggle flipped under All or Main switches to Choose, starting from what is lit now.
    - Choose restores the previous custom list if it lights an attached monitor. Unplugged IDs are always kept (H-009).
    - Names are Windows' EDID names, "Built-in display", or "Display N" (numbered left to right).
  - **Shortcut recorder:**
    - The current shortcut is released while recording.
    - Esc cancels, Tab and Shift+Tab leave, and Alt+F4 still closes.
    - Alt+F4, Alt(+Shift)+Tab, Alt(+Shift)+Esc, Alt+Space, Ctrl+Esc and Ctrl+Shift+Esc are refused for every shortcut (`HotkeyGesture.IsAllowed`): `RegisterHotKey` would take them from every app.
  - **200% text:** WPF ignores Windows' Text size. The content gets a `LayoutTransform` of `UISettings.TextScaleFactor` (up to 225%), and the first opening enlarges the window within the work area.
  - **The toast** shows once per first-run session (K6's decision). Until C7 every run is a first run. *(History: with C7's store only the first run is one; K10 checked it.)*
  - **Presets row** is hidden while `Presets.All` is empty. *(History: C7 fills the catalog with five presets, so the row shows. K10's `PresetChoice.IsShowing` decides which chip and tray item are checked.)*
  - **With Windows animations off,** pages switch without the slide.
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes (165).
  - WPF-UI 4.3.0 was decompiled: `FluentWindow` (Mica needs `ExtendsContentIntoTitleBar`), the theme and accent managers, `UiApplication`'s binding on first use, `NavigationView` (the page provider; its frame swallows F5, so the recorder listens with `handledEventsToo`), `TextBlockMetadata`, `HyperlinkButton`, `TitleBar` close, and every theme key the pages use.
  - The WPF 8.0.31 runtime was decompiled: `Frame`'s inheritance boundary, `DoShutdown` closing windows before `OnExit`, and `MainWindow` accepting null.
  - The CCD signatures were checked against CsWin32 0.3.346's generated sources.
  - **Scratch harness (not committed), 87 checks:** *(K10 committed the `SrgbColor`, `MonitorChoice`, refused-key, `AudioReading` and `MusicGlowSource` checks in `Rimlight.Platform.Tests`.)*
    - `SrgbColor`: HSV round trips exact on 85³ colors.
    - Every `MonitorChoice` H-009 case.
    - The refused system keys.
    - `AudioReading`: round trip, the counter never 0, and 580 M concurrent reads without tearing.
    - `MusicGlowSource` with audio wanted: capture, pacing and an engine that hears silence; 0 bytes over 100,000 frames.
    - `GlowRaster` against a direct shader port:
      - alpha exact, colors within 1 code value
      - no lit pixel dropped
      - 0 bytes per frame
      - 0.5 ms a frame at the default look, 0.9 ms at 100% glow
  - Rendered preview frames were checked by eye.
  - Nothing has run on Windows yet: Maxwell's checklist is in #24.
- **After the PR:**
  - K6's review fixes (#22) are merged into this branch, so #24 merges cleanly again. In `AppController.Start` the pipe listens first (K6), then WPF-UI loads (K7).
  - The Behavior page names the setting's text when the shortcut has no parsed gesture (no shortcut window), as K6's welcome does. A shortcut in use or refused shows by name only, above its warning.
  - Rebuilt (Release and Debug, 0 warnings), `dotnet test` (165) and the 87-check harness pass again.
  - Review fixes:
    - **Settings counts as hidden while nobody can see the screen:** the session is locked or disconnected, the machine is going to sleep, or the display is off (K5's `SystemState`). `SystemPauseBridge.StateChanged`, marshaled to the UI thread, stops the page timers and releases the audio request, so K5's pause closes the capture again. A fullscreen app doesn't count, because the window may be on another monitor.
    - **No overlay closes the capture:** with no display chosen or connected, `OverlayHost` calls the new `IOverlayFrameSource.FramesStopped()` once (default: nothing). `MusicGlowSource` then closes the loopback stream, also while Settings wants audio, since no frame publishes a reading. The next frame reopens it. *(Replaced in the stack merge by K10's rule: no overlay counts as paused and frames go on as the source's motion asks, so the capture closes like any pause unless Settings or the visualizer shows the audio, whose meter and plots then keep their frames. `FramesStopped` is gone.)*
    - **Preview staleness:** `GlowPreview.Start` restarts its clock, so the reading from before counts as stale until a newer one arrives. A reading that froze minutes ago no longer drives the preview after the page reopens.
    - **Recorder:** it also refuses keys apps use for typing, editing and moving (letters, digits, Tab, Space, Enter, Backspace, Insert, Delete, Home/End/PageUp/PageDown, arrows, punctuation) unless Win, or Ctrl and Alt together, is held. So Ctrl+C, Ctrl+Z, Ctrl+Tab, Ctrl+Shift+T and Alt+F show a hint instead of becoming a global shortcut. Only the recorder refuses them: `HotkeyGesture` (K6) is unchanged, so a shortcut already in the settings still works.
    - **High contrast:** `UiTheme.Sync` re-applies when Windows switches from one contrast theme to another. WPF-UI has a dictionary per contrast theme.
    - **Preview card:** it sits in a `Viewbox` (`DownOnly`), so it shrinks rather than being cut off at the minimum width or 225% text.
    - The checklist in #24 now says the fake blender switches colors instantly, Silence shows after about 3 s (2 s of silence plus the 1 s status hold), and Alt+Tab only ends recording.
    - Release and Debug have 0 warnings, `dotnet test` (165) passes, and the harness has 142 checks (new: the capture with no overlay, and 47 recorder cases).
- **Merged with K3's second no-overlay fix:** K3's host already gives the source one paused frame when the last overlay goes away (`SuspendSource`). The merge keeps that call and has `SuspendSource` call `FramesStopped()` right after it, once per stretch, in place of K7's own flag, so the capture also closes while Settings wants audio. *(Both gone since the stack merge; see the K10 notes.)*
- **Fix after the merge with K6's second review round:** the Behavior page's inline warning names `HotkeyState.TypesCharacter` (the shortcut types an AltGr character on one of the user's layouts), and the recorder refuses such a combination up front with `GlobalHotkey.TypesCharacter`, asking for Win or a different one. Before, the page showed the shortcut with no warning while it didn't work.
- **Left for later:**
  - **K8:** use `AppController.RequestAudio()` and `MusicGlowSource.LatestAudio`. *(Done in K8.)*
  - **K9:** set `AppController.Updates`; About's button and the tray already call it. *(Done in K9.)*
  - **K10:**
    - The presets row with C7. *(Checked in K10.)*
    - The preview with the real C5/C6 (H-017). *(H-017 resolved in C13.)*
    - Check the WPF-UI tray menu on Windows. *(Item 6 of the K10 Windows checklist.)*
    - Optionally drop the Debug tray items Settings now covers. *(Not done; they are in Debug builds only.)*
  - **K11:** measure CPU with Settings open on Appearance (target: about 3% or less) and with it hidden (idle). *(Row 15 of the K11 checklist.)*
  - **K12:** use the About privacy bullets verbatim in the README. *(Done in K12: character-identical.)*

## K8 notes

Branch `wip/k8-visualizer-demo`, review fixes on `wip/k8-visualizer-demo-fix`, merged in 4f350a0.
- **Structure:**
  - `src/Rimlight.App/Visualizer/` (namespace `Rimlight.App.Visualizer`). It is not `…Diagnostics`, which would shadow `System.Diagnostics` for partly qualified names, as with `…System`.
    - `VisualizerFeed`: one per open window. Two channels:
      - Render thread → UI: a lock-free `TripleBuffer<AnalyzerSnapshot>`. Each side owns one buffer and they swap the middle slot with `Interlocked.Exchange`, so the newest wins.
      - UI → render thread: a latest-wins `TuningRequest` slot (`Volatile.Write` / `Interlocked.Exchange`).
      - The outcome of the requests (`HandledRequest`, `TuningError`, written by the overlay thread only) lives on the feed, so a window that is minimized and restored keeps its one feed and still matches the replies to its requests.
      - The three snapshots are preallocated on the UI thread for the largest window: 8193 bins × 240 entries, about 115 KB.
    - `AnalyzerTap`: owned by `MusicGlowSource` and used on the render thread only while a feed is attached.
      - Applies the newest request before `Process` (H-004). The request's Sensitivity is replaced by the setting's (H-007).
      - Catches the setter's validation exception and reports it through the feed. The analyzer keeps its previous tuning.
      - Records Level, Bass, Beat and the dt of every `Process` call next to the analyzer's flux and threshold histories.
      - Publishes about 60 snapshots a second. The remainder carries over, so 144 Hz also gives 59.9 a second.
      - Every attachment starts with empty plots, also when the same window comes back after a minimize: while detached, the analyzer keeps adding flux entries, and old Level/Beat/dt entries would sit next to newer flux. `MusicGlowSource` calls `tap.Detach()` on every frame without a visualizer, so the tap holds no feed once the window closes. The rings are cleared in place: 0 bytes over 1000 re-attaches.
      - Arrays are sized from the current `Diagnostics` and only grow, which would allocate on that frame; in practice never, given the preallocation.
    - `VisualizerWindow` + `VisualizerLauncher`:
      - A WPF-UI `FluentWindow` with Mica and `Topmost`. It follows the theme itself, because it isn't the main window: the launcher gives that role back to Settings.
      - 960×660 DIP, capped at 95 % of `SystemParameters.WorkArea` like Settings, with the minimum size lowered to match. `WindowStartupLocation=Manual` at the work area's right edge, vertically centered. WPF's CenterScreen doesn't clamp (decompiled `Window.CalculateCenterScreenPosition`, 8.0.32), so a bigger centered window opened with its title bar off screen on 1080p at 150 %. At 100 % a centered Settings window shows 368 of 760 DIPs beside it; at 150 % only 48.
      - Runs only while shown and not minimized (RequestAudio + feed). A drawing error detaches (`StopObserving`) until the window is shown again, so the analyzer isn't kept at the full rate and the capture isn't held open. Esc and the close button really close it.
      - The Sensitivity row catches up with the setting when the window is shown again.
      - Stats row: the caption is "Tempo (BPM)" and the value is just the number; stat values are 20 px, so they fit down to the minimum width.
    - `PlotRaster` and `SpectrumPlot` / `HistoryPlot` (`VisualizerPlots.cs`): pure software plots into reused `WriteableBitmap`s at device-pixel size, with integer blending and a cached spectrum background. `TuningParameters`: the sliders and the H-015 JSON.
  - `Overlay/DemoBeat.cs` and `MusicGlowSource.SetDemo` / `SetVisualizer`.
  - `StartupOptions` (App root). `App.xaml.cs` only parses into it. *(K10 added `WindowAtStart`.)*
  - `AppController`: `ShowVisualizer()`, `SetDemo()` / `IsDemoActive`, `ObserveAnalyzer(feed)`. Hand-over lines are split on spaces.
  - Platform:
    - `FrameMotion.Observed`: `FramePacer` gives it the full rate, also when nothing is visible.
    - `SingleInstance.ShowVisualizerCommand` / `DemoCommand`; `MaxCommandBytes` is now public.
  - Tray:
    - A checked "Demo mode" item, visible only while the demo runs, in every build.
    - The Debug submenu "Render test (debug)" adds "Audio visualizer…" and "Demo mode (synthetic 120 BPM beat)".
    - Release has no visualizer item (doc 07 Phase 7 removes debug-only UI but keeps `--debug-visualizer`). A later `Rimlight.exe --debug-visualizer` opens it in the running copy.
- **Spec clarifications and deviations (doc 03 §4, doc 04 §5):**
  - **Cadence:** while the visualizer is shown the overlay calls the analyzer at the full rate (FPS cap, display refresh or the battery cap) in every mode, also with nothing on screen (`Observed`). When it closes or is minimized, the K3 pacing comes back.
  - **Time axis:** each history entry is placed by the dt the analyzer actually got. The caption shows the span, the number of calls and the calls a second: 4.0 s and 60 a second at 60 Hz, 8.0 s at 30 fps. Entries from before the window opened get the average dt, so the axis doesn't zoom while the history fills.
  - **Plots:**
    - The spectrum is in dB: 0 dB is a full-scale sine centered on a bin (the analyzer's amplitude units), down to −100 dB.
    - Each column shows its loudest bin, interpolated where bins are wider than a column. Bins map to Hz with `AnalysisSampleRate`, so decimated captures are correct.
    - The Bass, Mid and High bands of the tuning in effect are shaded and their edges marked.
    - The flux axis follows the largest value shown and relaxes by half every 2 s when the values drop. Beat ticks mark calls where Beat == 1.
  - **Slider ranges:** inside the analyzer's validation, WindowSize as exponents 2^10–2^14.
    - Band edges don't overlap: BassMin 0–60, BassMax 80–400, MidMax 500–6000, HighMax 6500–20000 Hz.
    - The two silence thresholds (−90 to −20 dB) overlap on purpose. A refused tuning leaves the sliders where the user put them, with an inline "Not applied: …" message until a valid set is reached; the analyzer keeps its previous tuning meanwhile.
    - The epsilon slider is on a log scale (1e-12 to 1e-3). Short times are shown in ms.
  - **Sensitivity:** the visualizer's slider edits `Settings.Sensitivity` through `SettingsService.Update`, so it is saved like any setting.
    - "Reset to defaults" leaves it alone.
    - "Paste JSON" changes it only when the JSON names it, and only after a throwaway `CoreFactory.CreateAnalyzer` accepted the whole pasted tuning. A refused paste changes and saves nothing ("Not applied: … Nothing was changed.").
    - A posted tuning can never revert it (H-007).
  - **Tuning lifetime:** visualizer edits live in the analyzer until the app quits. They are not saved; reopening the window shows the tuning in effect. "Copy params as JSON" copies the tuning in effect (`analyzer.Tuning`), not a refused draft.
  - **Text size:** the window is not scaled by Windows' text size, unlike Settings: a layout transform would blur the plot bitmaps. It's a debug tool.
  - **Demo look:**
    - Level, Bass and Beat are computed from the demo's own time (deterministic, no allocation, no Random), shaped like the analyzer's output:
      - Beat: decays with τ = 120 ms, accents 1.0/0.72/0.86/0.72 so the bar's downbeat is strongest.
      - Bass: a kick envelope with a 15 ms rise and 180 ms release, plus an off-beat bass note.
      - Level: the doc 03 mix with a 12 s pad swell and smoothed off-beat hats. Sharp 16th-note hats made the glow flicker at 8 Hz in the first draft.
    - Palette order: brand → sunset → aurora → ember, so warm and cool alternate. They are the presets' colors, crossfaded over 800 ms like album changes. 6 s is 3 bars, so every change starts on a downbeat.
  - **Demo rules:**
    - The user's shape, monitors, taskbar, FPS cap and pauses apply. The engine sees a cached copy of the settings with Enabled and MusicSync, rebuilt only when the settings change and never saved.
    - The capture stays closed unless Settings or the visualizer asks for audio.
    - Album and manual palette changes, and Mode and other settings edits, made during the demo are saved and apply when it ends (800 ms).
    - The glow toggle (shortcut, click on the icon, "Glow on") ends the demo and leaves the glow off. While the demo runs, the tray icon is undimmed and "Glow on" is checked.
    - The Settings preview keeps showing the real glow, not the demo.
  - **Command line:**
    - `--demo` and `--debug-visualizer` start the app normally plus the demo or visualizer, and don't open Settings.
    - A second start hands over "show-visualizer", "demo" or both ("show-visualizer demo", 21 of 64 bytes); unknown commands are ignored.
    - `--background` alone still hands over nothing.
- **Verified here (Linux):**
  - Release and Debug builds from a clean tree have 0 warnings and 0 errors. `dotnet test` passed at the time: Rimlight.Tests 657, IconGen 73.
  - APIs checked against the decompiled assemblies:
    - WPF-UI 4.3.0: `ApplicationThemeManager.Changed` (`ThemeChangedEvent(ApplicationTheme, Color)`) and `WindowBackgroundManager.UpdateBackground(Window, ApplicationTheme, WindowBackdropType)`.
    - WPF 8.0.32: a relative `ResourceDictionary.Source` resolves against the XAML's base URI (`BindUriHelper.GetResolvedUri`), `Clipboard` failures surface as `ExternalException` / `COMException`, and CenterScreen placement doesn't clamp.
    - Reference docs: `VisualTreeHelper.GetDpi`, `AutomationProperties.SetItemStatus`.
  - **Scratch harness (not committed):** real analyzer, light engine and blender; the real `MusicGlowSource`, `CapturedAudio`, ring buffer and `FramePacer`; a stand-in capture and media service; a 124 BPM synthetic full-mix track. Results after the review fixes:
    - **Allocations:** 0 bytes over 100,000 frames each in 8 modes: music sync, music sync + visualizer, Off + visualizer, Idle Glow + visualizer, paused + visualizer, demo, demo with glow off and mode Off, and demo + visualizer. The demo modes include many palette crossfades.
    - **Hand-off:** a 4 s two-thread stress of the triple buffer: 8.9 M publishes, 0 torn, 0 out of order. The real hand-off (render thread at 240 Hz, UI thread checking that each snapshot's features equal its newest history entry and don't change while held): 15,929 snapshots, 0 torn.
    - **Re-attach:** after a minimize, 0 stale entries, and a refused request's id and error survive. The closed feed is collected (WeakReference).
    - **Tuning:**
      - A valid tuning applies, and the request's Sensitivity never overrides the setting.
      - BassMax above MidMax and WindowSize 3000 are refused and the previous tuning kept.
      - WindowSize 4096 replaces Diagnostics; the snapshot follows with 2049 bins.
      - Only the newest of several posts applies.
      - A Sensitivity change keeps the visualizer's edits.
      - Snapshots: 59.9 a second at 144 Hz, one a frame at 30 Hz.
      - Every analyzer refusal surfaces as ArgumentException, from `CreateAnalyzer` and the live setter alike (the paste check).
    - **JSON:** byte-identical to `TuningJson.Serialize` and to H-015's exact call. Round-trips through `TuningJson.Parse` with the defaults and with a tuning that changes all 26 properties; subsets, case, comments and trailing commas are read, and unknown keys, null and arrays are refused.
    - **Sliders:** one per `AudioTuning` property (26); defaults lie on the grid and write back exactly; every step of every slider on its own passes the analyzer's validation, except the two deliberately overlapping silence sliders.
    - **Demo:**
      - Onsets exactly every 0.500 s: 97 in 48 s, which is 120 BPM. The downbeat peaks at 1.0 and the other beats at 0.77 on average.
      - Level ranges 0.31–0.78 and is never flat. It is deterministic.
      - Palettes change at 6/12/18/24 s, cycling 0→1→2→3→0, on downbeats; each primary is shown exactly 0.9 s after its change.
      - Through the engine, pulses come every 0.5 s.
      - Glow off with mode Off still shows the demo, with the user's shape. The capture never opens. Pause hides the glow and goes `Still`. Leaving the demo restores the album colors and the user's mode.
    - **Pacing:** `Observed` runs at the full rate with or without a visible overlay; the other motions are unchanged. StartupOptions parsing and hand-over lines are covered too.
    - **Plots:** about 0.7/0.2/0.17 ms per redraw at 1000 px wide, 0 bytes once sized.
    - **Placement:** the arithmetic over 7 work areas always keeps the window inside.
  - *(K10 committed the core of these checks in `Rimlight.Platform.Tests`: `VisualizerHandOffTests`, `TuningParametersTests`, `DemoBeatTests`, `MusicGlowSourceTests` and `StartupOptionsTests`.)*
  - Demo frames rendered through `GlowRaster` (640×360 and 1280×720 over a dim desktop) and visualizer plots in dark and light were checked by eye. The palettes read clearly, crossfades pass through Oklab mixes, the downbeat visibly pushes the glow further in, and the intensity has a kick and an off-beat groove with no 8 Hz ripple.
  - Nothing has run on Windows yet: items 12 and 13 of the K10 Windows checklist below.
- **Left for later:**
  - **Windows (K10 checklist items 12–13):** the window's XAML at runtime (styles merged from `../SettingsUi/PageStyles.xaml`, `BasedOn` the `Row` style), Mica and the theme switch, the placement on Maxwell's displays, the clipboard, the tray items and the single-instance hand-over, and the tray toggle during `--demo`. `SystemParameters.WorkArea` assumes the system DPI equals the primary display's (true unless the scale changed since sign-in); if not, the window lands off by that ratio, but its title bar stays on screen up to about 2×.
  - K9's `Program.Main` still passes `StartupEventArgs.Args` through *(checked in K9: WPF fills `e.Args` from `Environment.GetCommandLineArgs()`, independently of Main)*.
  - **K11:** CPU with the visualizer open (analyzer at the full rate, plus about 1 ms of plot drawing 30 times a second) and after closing it; the 8-hour soak in `--demo` (doc 07 Phase 7).
  - **Sync point 2:** tune with the visualizer, then pass the JSON on via H-005, which needs Maxwell's decision. *(C13 fixed H-016's steady-frame-rate BPM bias.)*
  - **K12:** record the GIF with `--demo` (docs/RELEASE-GUIDE.md §1).

## K9 notes

Branch `wip/k9-velopack-updates`, review fixes on `wip/k9-velopack-updates-fix`, merged in 6779286.
- **Structure:**
  - `src/Rimlight.App/Program.cs`: `[STAThread] Main` runs `VelopackApp.Build().SetAutoApplyOnStartup(false).OnBeforeUninstallFastCallback(...).OnRestarted(...).SetLogger(new TraceVelopackLogger()).Run()` first, then `new App()`, `InitializeComponent()` and `Run()`. The csproj sets `EnableDefaultApplicationDefinition=false`, so App.xaml is picked up as a Page and WPF generates no Main.
    - If `VelopackApp.Run()` throws during a hook run (argv[1] is one of the 8 hook arguments `Run()` handles, followed by a version), Main never starts the app: for `--veloapp-uninstall` it removes the Run value, then exits with −1.
  - `src/Rimlight.App/Updates/` (namespace `Rimlight.App.Updates`):
    - `UpdateService` (`IUpdateCheck`, set as `AppController.Updates`): automatic checks, download, "Update ready", Restart now, the user's check, the install at start (`InstallDownloadedUpdate`) and "Updated to version X".
    - `UpdateSchedule`: pure timing.
    - `UpdateMessages`: every user-facing string and the failure → message mapping.
    - `TraceVelopackLogger` sends Velopack's Information-and-above lines to the app log as `[Velopack] …`.
  - `IUpdateCheck.ReadyVersion` and `IUpdateCheck.RestartToUpdate()`; `AppController.RestartToUpdate()`. The tray menu item "Restart to update (version X)", after "Check for updates", is shown only while an update is downloaded and waiting.
  - `TrayIconHost.Notify(..., Action? clicked)` keeps the click action of the notification shown last until another notification replaces it, and ignores the shell's show and close messages. It replaces K6's unused `NotificationClicked` event.
  - `App.OnStartup`: after the claim and `AppLog.Start`, `UpdateService.InstallDownloadedUpdate(e.Args)`; when it returns true, the app shuts down.
  - `AppController`: creates the service in its constructor, calls `Start(settings)` after the startup entry, and disposes it after the hotkey. `Tell` is internal now.
  - `AppLog.Folder` is `%APPDATA%\Rimlight\logs`.
  - `release.yml`: dry runs no longer pass `-SkipVelopackAppCheck`.
- **Spec clarifications and deviations:**
  - **Velopack's automatic install at startup is off; the app installs a downloaded update itself, after the single-instance claim.**
    - Why: `Update.exe apply` kills every process running from the install folder (`force_stop_package`). With Velopack's default (install inside `VelopackApp.Run()`, before the mutex), starting the app again while it runs with a downloaded update would kill the running copy before it saves or removes its tray icon.
    - After the claim, a second start only hands over "show-settings".
    - The start's own arguments are passed on. A `--background` start installs silently, so no Velopack window appears at sign-in.
    - It never installs in a process Velopack just restarted (`OnRestarted`), so a failed install can't loop.
  - **Restart now** (a Yes to the notification's question, or the tray menu's "Restart to update"):
    - It saves the settings, calls `WaitExitThenApplyUpdates(update, silent: false, restart: true, ["--background"])`, then the normal Quit. `ApplyUpdatesAndRestart` isn't used: it calls `Environment.Exit(0)` at once, which skips OnExit (no settings save, a ghost tray icon, an abandoned mutex).
    - Update.exe waits up to 60 s for the PID. The new version comes back in the tray and says "Updated to version X." The same toast follows an install at start.
    - After the updater starts, `ready` is cleared, so a second Yes or menu click before the posted quit runs starts no second updater.
  - **"Update ready" notification:** tray notifications have no buttons.
    - The text is "Update ready — restart Rimlight to apply.\nClick here, or choose Restart to update in the tray menu, to install version X."
    - A click asks "Restart now to install version X?" (Yes/No). The shell doesn't say which notification was clicked, so a click on an older toast still in the list (K7's "still running in the tray") can only ask, never restart. A second click while the question is open does nothing.
    - Clicks from Windows' notification list count too, if Windows delivers them.
    - The tray menu's "Restart to update" works without any notification (an expired toast, Do Not Disturb, Focus Assist) and restarts without asking.
  - **Schedule:**
    - The first check is 30 s after start, or after "Update automatically" is turned on. After that, checks are 12 h after the previous check, of any kind (the user's checks count too), and 1 h after a failed check or a failed download (offline at sign-in, rate limit).
    - Turning the setting off and on doesn't ask GitHub again within the 12 h.
    - Waits are cut into pieces of at most 30 min. On Windows, `Task.Delay` uses `QueryUnbiasedInterruptTime`, which doesn't count sleep (decompiled from CoreLib 8.0.32). The schedule's clock, `Environment.TickCount64`, does count it.
  - **Download stall timeout:** a download is cancelled if it reports no progress for 10 min (a `CancellationTokenSource` linked to the exit token, `CancelAfter` moved forward on every progress report).
    - Why: HttpClient's 30 min timeout ends with the headers and nothing sends keep-alives, so a connection that died mid-body (sleep, a Wi-Fi change) would leave the service busy for the session.
    - Velopack reports about every 3 %, so this gives up only below about 4 KB/s. Its delta patching takes at most 5 min.
    - Logged as "stalled". The user's check says "Version X couldn't be downloaded. Check the internet connection and try again." Retried after 1 h.
  - **"Update automatically" off** stops the schedule; a check or download already running finishes. A downloaded update still installs at the next start, and the user's own check downloads too.
  - **Messages:**
    - Automatic checks are silent unless an update is ready. "Update ready" is shown once per version per session and respects quiet hours. If the tray refuses it, it is only logged and tried again at the next check.
    - The user's check answers every step:
      - "You're up to date (version X)."
      - "Downloading version X…"
      - the ready notification
      - a short failure:
        - 403/429: "GitHub is limiting update checks…"
        - other statuses: "GitHub answered with error N"
        - no connection or timeout: "Couldn't reach GitHub…"
        - anything else: the logs folder.
    - The fallback is a message box, as in `Tell`, and a Yes/No box for "Update ready".
  - **Check timeout of 1 min.** `CheckForUpdatesAsync` takes no token, and Velopack's HttpClient timeout is 30 min. A timed-out request is left on the pool and its result is dropped. The download takes the service's token and is cancelled on exit.
  - **Dev run:**
    - It shows "Updates work only in an installed copy of Rimlight. This one is version X, run from a build." and sends no request.
    - Velopack's locator still writes its own log to `%LOCALAPPDATA%\velopack\velopack.log`; an installed copy writes `velopack_Rimlight.log`.
    - Nothing creates `%LOCALAPPDATA%\Rimlight` any more.
  - **Logs:**
    - They are in `%APPDATA%\Rimlight\logs`, next to settings.json, as decided in H-014 item 3. Velopack's library lines reach them too, as `[Velopack] …`.
    - Velopack's uninstall deletes the install root (`remove_dir_contents(root)` in Update.exe), so settings and logs now survive it, as doc 07 Phase 6 says.
    - Update.exe logs installs, applies and uninstalls only to `%LOCALAPPDATA%\velopack\velopack_Rimlight.log` (`default_file_windows` in Velopack's lib-rust logging.rs at 92d6a1c). That file survives an uninstall. CONTRIBUTING.md and the bug form ask for it for install, update and uninstall problems.
    - `%APPDATA%` roams in a domain with roaming profiles.
  - **AUMID:** in an installed copy, VelopackApp sets `velopack.Rimlight`, the nuspec's `shortcutAumid`, which is also on Velopack's shortcuts. A dev run sets none.
  - **Uninstall:** Update.exe first ends the running app, then runs `--veloapp-uninstall`. Ours removes the HKCU Run value. Update.exe gives the hook 60 s; the library's doc says 30 s. It then removes the shortcuts, the root folder's contents and the uninstall entry.
  - **Velopack's launcher passes arguments on.**
    - The chain is `%LocalAppData%\Rimlight\Rimlight.exe` → `Update.exe start -- <args>` → `current\Rimlight.exe <args>`. So `--background` from the Run value reaches the app, which answers K6's open question.
    - The launcher is named after `--packTitle`, which pack.ps1 sets to AppName, the exe's name too.
    - StartupRegistration's layout check (`current\sq.version`, `..\Update.exe`) matches `WindowsVelopackLocator`. Nothing needed fixing.
  - **Privacy:** GithubSource sends unauthenticated GETs with User-Agent `Velopack/1.2.161`. Velopack's staged-rollout ID (`packages\.betaId`) is created locally but never sent by GithubSource (`GitBase.GetReleaseFeed` ignores it).
  - **No local-feed override.** The app reads only `GithubSource(RepositoryUrl)`. An environment variable or flag that redirects updates would let something other than this repository's releases update the app, which SECURITY.md lists as in scope. So the download and Restart now path is tested with real releases.
- **Verified here (Linux):**
  - Builds and tests: `dotnet build Rimlight.sln -c Release --no-incremental` and `-c Debug --no-incremental`: 0 warnings, 0 errors, also at the intermediate review commits. `dotnet test -c Release` passed at the time: Rimlight.Tests 657, IconGen.Tests 73.
  - **vpk 1.2.161** on self-contained win-x64 and win-arm64 publishes of this branch, with pack.ps1's arguments:
    - It prints "Verified VelopackApp.Run() in 'System.Void Rimlight.App.Program::Main()'", with no library/vpk version warning, also after `SetLogger` was added.
    - Its check (`CompatUtil.TrySearchAssemblyForVelopackApp`) looks for a call to `Velopack.VelopackApp::Run` in the managed entry point's own IL, which is why the call stays in Main.
    - Package: nuspec id Rimlight, channel win / win-arm64, mainExe Rimlight.exe, shortcutAumid velopack.Rimlight.
    - Portable zip: `Update.exe`, `Rimlight.exe` (the stub), `current\Rimlight.exe` + `sq.version`.
  - **WPF 8.0.32 runtime** (Microsoft.WindowsDesktop.App.Runtime.win-x64) decompiled: `StartupEventArgs.Args` is `Environment.GetCommandLineArgs()` without argv[0]. It doesn't depend on Main, so `e.Args` is still filled.
  - **Velopack 1.2.161 members used (decompiled from lib/net8.0):**
    - `public static VelopackApp VelopackApp.Build()`
    - `public VelopackApp SetAutoApplyOnStartup(bool autoApply)`: default on. When on, `Run()` applies a newer local full package via `UpdateExe.Apply(..., restart: true, args)` and exits, unless `VELOPACK_RESTART` was set.
    - `[SupportedOSPlatform("windows")] public VelopackApp OnBeforeUninstallFastCallback(VelopackHook hook)`: runs on `--veloapp-uninstall <version>`, then `Environment.Exit(0)`, or `Exit(-1)` if the hook throws.
    - `public VelopackApp OnRestarted(VelopackHook hook)`: invoked at the end of `Run()` when `VELOPACK_RESTART` was set. `Run()` clears the variable.
    - `public VelopackApp SetLogger(IVelopackLogger logger)`: passed into the locator's `CombinedVelopackLogger`, kept for the process's life, ignored when a custom locator is set. `IVelopackLogger.Log(VelopackLogLevel, string?, Exception?)`; `VelopackLogLevel { Trace, Debug, Information, Warning, Error, Critical }`.
    - `public void Run()`:
      - reads its arguments from `Environment.GetCommandLineArgs().Skip(1)`
      - on Windows, when installed, sets `SetCurrentProcessExplicitAppUserModelID(manifest.ShortcutAumid ?? "velopack." + id)`
      - dispatches `--veloapp-install/updated/obsolete/uninstall` and `--squirrel-*`, then exits
      - deletes old local packages
    - `public delegate void VelopackHook(SemanticVersion version)`
    - `public UpdateManager(IUpdateSource source, UpdateOptions? options = null, IVelopackLocator? locator = null)`: uses `VelopackLocator.Current`, which throws `InvalidOperationException` before `Run()`. The channel is `options?.ExplicitChannel ?? Locator.Channel`.
    - `public GithubSource(string repoUrl, string? accessToken, bool prerelease, IFileDownloader? downloader = null)`: lists the 10 newest releases, filters out prereleases, then reads each release's `releases.{channel}.json` from its `browser_download_url`.
    - `public virtual bool IsInstalled` (`Locator.CurrentlyInstalledVersion != null`)
    - `public virtual SemanticVersion? CurrentVersion`
    - `public virtual Task<UpdateInfo?> CheckForUpdatesAsync()`: no token, `NotInstalledException` when not installed, null when nothing is newer.
    - `public virtual Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)`:
      - takes a file lock
      - returns at once if the package is already there
      - downloads to `.partial`, checks size and SHA256, then renames; Velopack always gives the downloader its own rounding progress callback, and the body is read with `ReadAsync(..., cancelToken)`
      - on Windows, extracts the new Update.exe unless cancelled
      - deletes the other packages
    - `public virtual VelopackAsset? UpdatePendingRestart`: the newest local full package above CurrentVersion.
    - `public void WaitExitThenApplyUpdates(VelopackAsset? toApply, bool silent = false, bool restart = true, string[]? restartArgs = null)` → `UpdateExe.Apply` runs `Update.exe [--silent] apply [--package <nupkg>] [--waitPid <pid>] [--norestart] --rootDir <root> --packageDir <packages> [-- <restartArgs>]`.
    - `UpdateInfo.TargetFullRelease { get; }`, `VelopackAsset.Version { get; set; }`, `SemanticVersion.ToString()` (normalized, without metadata), and its `operator ==`/`!=`.
    - Not used: `ApplyUpdatesAndRestart(VelopackAsset?, string[]?)`, which is `WaitExitThenApplyUpdates` followed by `Environment.Exit(0)`.
  - **Velopack's native parts:** stub.rs, update.rs, `commands/start_windows_impl.rs`, `apply.rs`, `apply_windows_impl.rs`, `uninstall.rs`, `shared/util_windows.rs`, `lib-rust/bundle.rs` and `lib-rust/logging.rs` were read at the packages' source commit 92d6a1c, the same for Velopack and vpk. Strings in vpk's `stub_x64.exe` and `update_x64.exe` match that source.
  - **H.NotifyIcon 2.3.2 decompiled:**
    - `NIN_BALLOONSHOW` (1026) raises `TrayBalloonTipShown`; `NIN_BALLOONHIDE`/`NIN_BALLOONTIMEOUT` (1027/1028) raise `TrayBalloonTipClosed`; `NIN_BALLOONUSERCLICK` (1029) becomes `MouseEvent.BalloonToolTipClicked` (11) and raises `TrayBalloonTipClicked`.
    - All are raised inside the message window's window procedure, on the UI thread, and none says which notification it was.
    - `ShowNotification` throws `InvalidOperationException` when the icon isn't there or the shell refuses.
  - **Scratch harness (not committed), 126 checks after the review fixes, green in 3 runs:**
    - **`UpdateSchedule` with the real times:** 30 s, 12 h, 1 h, re-enabling, the user's checks counting.
    - **Velopack itself** with `TestVelopackLocator`, against a local fake GitHub API (GithubSource's GitHub Enterprise path) serving vpk-made 0.1.0 and 0.2.0 packages:
      - no releases → null, and a prerelease is skipped
      - 403, 429, 404, 500, connection refused and DNS failure map to the right messages
      - download, pending update, a second download that does nothing, old package cleaned up, cancelled download
      - Update.exe's exact command lines, recorded by a fake: Restart now `apply --package … --rootDir … --packageDir … -- --background`; at start `--silent apply … -- --background`, or without `--` when there are no arguments.
    - **The committed `UpdateService`,** compiled against stub AppController/Tray/Settings/MessageBox on a single-threaded dispatcher-like context, with times scaled (30 s → 300 ms, 12 h → 1.5 s, 1 h → 0.8 s, 1 min → 2 s, 30 min → 200 ms):
      - **Schedule:** first-check timing, interval, off/on, a quick toggle (one loop), dispose.
      - **The user's check:** up to date, rate limit, offline, timeout, joining a running automatic check.
      - **Update found automatically:** announced once and not downloaded again; the user's check while it is ready answers at once with no request.
      - **Click flow:** a click asks; No does nothing; a click while the question is open is ignored; Yes restarts; a second restart starts no second updater; the menu restarts without asking; `ReadyVersion` changes as expected.
      - **Restart now:** saves settings, starts the updater, quits. Message box fallback, Yes and No.
      - **Tray refusals:** an automatic announcement is only logged and shown at the next check.
      - **During a download:** Downloading… is said again with one ready toast. Exit mid-download cancels it: no package, no `.partial`, nothing said.
      - **Stall:** a stalled download (headers plus 64 KB, then silence) is given up, the user is told, no `.partial` file is left and the next check runs. An automatic stall or HTTP 500 is retried at the scaled 1 h. A slow but live download (25 chunks over about 3 s with a 400 ms timeout) is not cut off.
      - **Start of a run:** install at start; no install right after a Velopack restart; nothing to do. A restart after a failed install offers the update again. "Updated" toast.
      - **TraceVelopackLogger:** level filter; Velopack's own warning and information lines arrive through a locator logger.
      - Every Tell, Notify, MessageBox and Quit ran on the UI thread.
    - Mutation checks: without `schedule.Failed()` on a failed download 2 checks fail; without the progress reset 1; without the stall watchdog 6.
  - *(K10 committed the schedule and message rules as `UpdateRulesTests`.)*
  - **Not run:** Pester (pwsh isn't installed here; none of `build/pack.Tests.ps1`'s 33 cases touches `-SkipVelopackAppCheck` or the workflow, and pack.ps1 changed only in its help text) and actionlint (both YAML files parse with PyYAML).
  - **Needs Windows (items 14 and 15 of the K10 Windows checklist):** toasts and the click events, whether a click in Windows' notification list reaches the app, whether the question box comes to the front, the menu item in the real tray menu, Velopack's real install/apply/uninstall, and the stub on a real install. The hook-run guard needs a locator that throws, so it was only read.
- **Left for later:**
  - **K12:** the README FAQ and Privacy facts (H-021). *(Done in K12, checked in K10.)*
  - **Delta updates** (C9 follow-up): every update is still the full package of about 80 MB.
  - **Optional:** a "Restart to update" button in Settings > About while an update is ready (not in the spec; the tray menu item already gives a path that doesn't depend on notifications).

## K10 notes

Integration pass: Lane B's test project, a pacing check with the real Core, seams and stale text. Branch `wip/k10-integration`, review fixes on `wip/k10-fix`, this log on `wip/k10-docs`.
- **Structure:**
  - **`src/Rimlight.Platform.Tests` (new):**
    - Setup:
      - net8.0-windows10.0.19041.0, `EnableWindowsTargeting`, no `UseWPF`. xUnit 2.9.2, runner 2.8.2 and Test.Sdk 17.11.1, as in Rimlight.Tests.
      - It references Platform (new `InternalsVisibleTo`), AudioTools (synthetic tracks, `TuningJson`) and Bench (new `InternalsVisibleTo`).
      - It compiles in the 18 App files that use no WPF, WPF-UI or H.NotifyIcon type (`<Compile Include=… Link="App/…">`): AudioReading, DemoBeat, MusicGlowSource, SrgbHex, AnalyzerSnapshot, AnalyzerTap, TripleBuffer, TuningParameters, VisualizerFeed, MonitorChoice, PresetChoice, SrgbColor, UpdateMessages, UpdateSchedule, RollingFileLog, SettingsService, StartupOptions, TrayToolTip.
    - Where it runs: it is in `Rimlight.sln` (src folder) and in `Rimlight.Core.slnf` (backslash paths, Platform included), so CI's Linux job builds and runs it. The Windows job runs it through the full solution. The labeler puts it in `lane-b`.
    - Tests: 260 cases in 22 classes: FramePacer 38, HotkeyGesture 38, MusicGlowSource 23, SrgbHex/SrgbColor 20, StartupOptions 15, FullscreenRules + SystemState 15, AudioRingBuffer 15, GlowConstants 13, TuningParameters 11, PacingTimeline 11, MonoConverter 11, SettingsFlow 7, AudioReading 7, TrayToolTip 6, visualizer hand-off 5, update schedule and messages 5, MonitorChoice 5, DemoBeat 5, RollingFileLog 3, OverlayRules 3, BenchMirror 3, SingleInstance 1.
    - `Support/SerialTests` is a collection with parallelization off for the classes that wait on the thread pool (SingleInstance, RollingFileLog): while the parallel classes run, pool work (the pipe listener's `Task.Run`, the log's flush timer) waited more than a second on Linux. That failed the first hand-over in every full run, and let a double-write mutant of the log survive. Those classes now run on their own, after the others.
  - **Support code:**
    - `GlowRig` is the app's real `MusicGlowSource` with stand-ins for its two Windows threads:
      - the capture thread: it opens 50 ms after `SetActive(true)`, publishes a `CapturedAudio`, and writes 10 ms packets of a 124 BPM full mix, digital silence, or nothing;
      - the media reader: it publishes album palettes.
    - `OverlayLoopModel` models OverlayHost's overlay thread on a simulated clock. It calls the same rules OverlayHost calls (`FramePacer`, `GlowConstants`, `OverlayRules`, `OverlaySurface.RenderScaleFor`), so its own copy of the loop is down to the control flow: Loop, ApplyPendingInputs, and RenderFrame's per-overlay present/hide, plus OverlaySurface's record of the frame it presented last. `Rebuild(...)` models the outcome of a display change, zero overlays included.
    - It leaves out windows, the GPU and DirectComposition (every present completes), vsync (the timer runs at the same interval), and device loss.
  - **Platform:**
    - `Overlay/OverlayRules.cs` (new) holds the overlay loop's Windows-free rules: `MonitorFadeSeconds`, `Reduced`, `AdvanceFade`, `PausedEverywhere<T>` and the `IMonitorFade` interface.
    - `FramePacer` gained `FrameDt(elapsed, frameDue)` (the dt rule: a gap over 0.25 s counts as one full-rate frame; a frame an input brought forward gets the time since the last frame, at most one full-rate frame), `NextDeadline<T>(now, deadline, period)` (the cadence) and `MaxFrameDtSeconds`.
    - `OverlaySurface.BufferSize` is now internal, and `RenderScaleFor` is new.
    - `SingleInstance.StopListening()` is new, plus an internal `PipeName` test hook.
    - Internal test hooks `LoopbackCapture.IsActive` / `PublishForTesting(CapturedAudio?)` and `NowPlayingService.PublishForTesting(Palette?)`. The app never calls them.
    - `MonoConverter` checks the channel count before dividing by the frame size (below).
  - **App:**
    - `SettingsUi/PresetChoice.IsShowing`: the one rule for the tray's "Presets" checkmarks and the Appearance chips.
    - `StartupOptions.WindowAtStart` + `StartupWindow`: AppController's choice between the welcome, Settings and no window, now testable.
    - `App.OnExit` calls `instance.StopListening()` first.
  - **Docs:** CONTRIBUTING (layout, the lanes table, "Without Windows", what logs hold), the bug form, doc 02's tree and lifetime events, doc 09 §2 (Lane B owns the test project) and rule 4, ci.yml comments, PERF.md and Bench comments (which tests guard the copies), CHANGELOG (H-022), README (Velopack's log in a full cleanup), and HANDOFF H-018 (K10's Lane A edits, for Codex and Maxwell).
- **Spec clarifications and deviations:**
  - **No overlay counts as paused.** This covers no display chosen, or none of the chosen ones attached (Custom set to the external display, then undocked). The frame source keeps running, so the engine fades out, the capture closes and frames stop once the glow is static. Settings' meter still runs at 10 Hz and the visualizer at 60 Hz. No GPU device is made. The debug status reads "No overlay".
    - Before this fix the loop skipped the source entirely: a capture that was open stayed open, and the meter and the visualizer froze.
  - **An exiting copy stops taking hand-overs before its teardown.** A start during the teardown (Quit, then Rimlight again from the Start menu) used to be accepted and dropped, leaving no copy running. It now times out on the missing pipe after 3 s and takes over within Claim's 5 s wait. The mutex is still released last.
  - **ApplySettings allocations (C12's question, H-020): kept on the overlay thread, and documented in `MusicGlowSource.ApplySettings`.**
    - Why: the new tuning must start from the analyzer's own, which the visualizer edits on that thread (H-004). A copy built on the UI thread could put back a tuning the overlay thread has since replaced.
    - Measured cost, once per edit and never per frame: 120 B per Sensitivity edit, 48 B per manual color edit, and 112 B per settings change while the demo runs with the glow off or Off (the demo's engine settings). The tests check only a 256 B ceiling per edit, so an edit that allocated nothing would also pass.
  - **Stopping takes about 0.4 s with the real engine** (283 ms with K3's stand-in): the 300 ms fade, then the engine reports static from the frame after Visibility reaches 0 (H-011). That frame comes as a 10 Hz one. Nothing visible.
  - **Idle Glow presents 8–10 times a second, not 10.** Near the breathing's peaks a 10 fps frame is within half a code value of the last present, so the surface skips it (K3's static rule).
  - **A settings change and an album change in the same frame call SetTarget twice.** ApplySettings first targets the album it knew, then the album check targets the new one. The second call wins before Update runs (C5: colors already on screen switch at once). Left as is.
  - **The test hosts run without background GC** (`ConcurrentGarbageCollection=false` in Rimlight.Platform.Tests and Rimlight.Tests).
    - While a background GC runs, `GC.GetAllocatedBytesForCurrentThread` reported 0.6–8 KB on a thread that allocated nothing.
    - A loop of only Core calls (Process, Update, FillGradient) next to two busy test classes reproduced it in about half the runs, with tiered compilation on or off. It never happened in 6 runs with background GC off.
    - This probably explains C3's unidentified 1-in-22 failure. The app's own code allocates nothing per frame.
  - **Logs never hold track titles.** No code logs track text, and H.NotifyIcon's tooltip error is only "UpdateToolTip failed." (decompiled 2.3.2). CONTRIBUTING and the bug form now say logs can include device and monitor names, and file paths that contain the Windows user name, never track titles. *(Since the stack merge brought K6's private logs, they say the profile folder is written as `%USERPROFILE%` and only an error message may still name a device or a file; Velopack's own log isn't redacted.)*
- **Seams found and fixed:**
  - **Bench's PaceModel** still dropped a light fading in under MinVisibility to 10 Hz after C13 changed FramePacer (Visibility > 0 keeps the full rate). Fixed, with a new SoakTests case. A 30-minute soak gave the same frame counts before and after, so PERF.md stands.
    - `BenchMirrorTests` now compares PaceModel with FramePacer over 200,000 random frames, and `PaceModel.FrameDt` with `FramePacer.FrameDt`.
    - It also runs FramePipeline and MusicGlowSource side by side for a 45 s session (music, silence, Hide, Off, Idle Glow, album changes, pause, Sensitivity and ratio edits). Light state, refills, gradient and motion must match bit for bit.
  - **MonoConverter's zero-channel guard** came after the division by BytesPerFrame, so it threw DivideByZeroException instead of returning 0. Latent only: the capture session refuses such formats.
  - **No overlay, and the exit hand-over:** see the two clarifications above.
  - **Stale text fixed:**
    - TrayIconHost: "Presets.All is empty until C7".
    - AppController: "the store is in memory until C7".
    - IShowSettings and WelcomeWindow: "until K7".
    - TrayIconImages: the dim icon "derived" (H-012).
    - Doc 02: logging through `Microsoft.Extensions.Logging`.
    - CHANGELOG: "hooking this up to the track that's playing is still to come", and no entries for K4–K9 (H-022).
- **Verified here (Linux):**
  - **Builds and tests:**
    - `dotnet build Rimlight.sln -c Release` and `-c Debug`: 0 warnings, 0 errors.
    - `dotnet test Rimlight.sln -c Release`: Rimlight.Tests 741, Rimlight.Platform.Tests 260, Rimlight.IconGen.Tests 89, all passed.
    - CI's Linux steps on `Rimlight.Core.slnf` (backslash check, restore, build `--no-restore`, test `--no-build`): the same three counts, 0 warnings.
    - Platform.Tests takes 6–10 s in Release and about 24 s in Debug. It passed 4 full runs in a row after the review fixes; before them, 5 full runs in a row passed after the GC fix, plus 4 runs of the class combination that had failed.
  - **Pacing with the real Core,** one 1080p monitor at 60 Hz, defaults:

    | State | Frames/s | Presents/s | Capture | Windows shown |
    |---|---|---|---|---|
    | Music | 60 | 60 | open | 1 |
    | Silence → Idle Glow | 10 | 8.8 | open | 1 |
    | Hide while quiet | 10 | 0 | open (listening) | 0 |
    | Off, glow off, paused everywhere, only monitor paused | 0 | 0 | closed | 0 |
    | On battery, Save power | 30 | 30, half scale | open | 1 |
    | On battery, Pause the glow | 0 | 0 | closed | 0 |
    | Idle Glow mode | 10 | 9–10 | closed | 1 |
    | Off with Settings' meter open | 10 | 0 | open | 0 |
    | Off with the visualizer open | 60 | 0 | open | 0 |
    | Demo with the glow off | 60 | 60 | closed | 1 |
    | Two monitors, one paused | 60 | 60 | open | 1 of 2 |
    | No display chosen, music playing | 0 | 0 | closed | 0 |
    | No display, Settings' meter open | 10 | 0 | open | 0 |
    | No display, visualizer open | 60 | 0 | open | 0 |
    | Display chosen again | 60 | 60 | open | 1 |

    - Timings:
      - Music back from Hide is on screen 53 ms after it starts.
      - Every input wakes the loop at once, and its fade starts on that frame. Its dt is the time since the last frame, or 1/60 s between 10 Hz frames. *(Since the stack merge, an input at the full rate waits for the next scheduled frame instead, K3's review.)*
      - Off and pause stop after 383–400 ms. Frames stop 0.3–0.42 s after the last overlay goes, with Visibility 0.
      - A paused monitor's window hides after its 300 ms fade.
      - dt never exceeds 0.1 s.
    - Resuming at 60, 144 and 240 Hz (native) fades in from the first frame: no 100 ms gap, and steps of 0.1 or less. So does choosing a display again.
    - The start fade-in has no jump with 0, 150 or 250 ms of GPU setup in the first frame (C13's two cases).
    - Vsync tolerance: a 60 fps cap is paced on the display at 61 Hz (within 3 %), and on the timer at 62 Hz.
  - **MusicGlowSource:**
    - 0 B over 3,000 frames after warm-up in 17 modes: music, silence, no packets, Idle Glow, Off, glow off, paused, Hide while hidden, Off with Settings' audio, the visualizer (alone, with Off, with pause), the demo (alone, with the glow off and Off, with the visualizer), album crossfades, and 144 Hz on battery.
    - The capture opens only when something listens, and closes for Idle Glow, Off, the glow off, a pause, no overlay and the demo.
    - Album fades take 47–50 frames (800 ms), manual edits 11–14 (200 ms), and Override and Manual mode behave as specified.
    - The demo leaves the album colors as they were, and a new demo starts from its first downbeat.
    - The motion it reports moves Full → Slow (1.75 s after silence) → Listening → Still → Observed.
  - **Settings with C7's real store:**
    - The welcome shows once, and FirstRunComplete, LaunchAtStartup and ToggleHotkey (also "") persist.
    - Changes save 500 ms after the last one, and on quit at once.
    - A bad file loads defaults and is kept as `settings.bad.json`.
    - Each of the five presets is the only one shown as chosen after it is applied. It stays chosen after Sensitivity, FpsCap, corner and taskbar edits, a custom monitor list, and a save and load. A look edit clears it.
  - **The exit hand-over:** an exiting copy takes no more requests, a start meanwhile takes over, and the claim is released last (`SingleInstanceTests`).
  - **Mutation checks:** 33 mutants, all killed by the new tests:
    - the old FramePacer fade rule; Observed slowed down; a ring that overwrites, or reads the oldest instead of skipping;
    - no int24 sign extension; Alt+Tab allowed; an unscaled hairline; a tooltip limit of 128; a bare "&";
    - no refill on a crossfade's last frame; the capture open while paused; a 1.0 s idle settle; the visualizer's request overriding Sensitivity;
    - the welcome skipped at sign-in; a reference-only preset check; flat demo accents; a slider range past the analyzer's; a 12 h retry; no flush on dispose;
    - PaceModel's old rule; an allocation every 600 frames;
    - the old zero-overlay rule; vsync tolerance 1.0; no `demoBeat.Restart()`; a 100 ms monitor fade; dt as the raw gap; the cadence always restarting from now; Reduced never true;
    - StopListening doing nothing; Dispose skipping the mutex after StopListening; half-scale buffers rounding down; the log writing a batch twice.
- **Left for later:**
  - **Windows:** the K10 checklist below, and the K11 checklist after it (with the Windows CPU numbers for PERF.md, H-019 item 1).
  - **StopListening waits at most 1 s for the listener.** If the pool were saturated at exit, the pipe could outlive that wait by a moment. This is unlikely in the app, and is noted rather than fixed.
  - **OverlayLoopModel** still mirrors OverlayHost's control flow by hand: Loop, ApplyPendingInputs and RenderFrame's per-overlay logic. Keep the two in step, as with Bench's copies (H-019).
  - **At the v1.0.0 tag:** rename CHANGELOG's `[0.1.0]` heading (RELEASE-GUIDE §5).
  - **Waiting for Maxwell:** H-005 (tuning defaults), H-018 (Lane B owns `Rimlight.Platform.Tests`; CLAUDE.md and AGENTS.md lane lists), H-023 (C13's XML-only CONTRACT CHANGE) and H-024 (keep Ctrl+Alt+L, which AltGr layouts can't use, or change the default). H-018 also waits for Codex's acknowledgement.
- **Stack merge (`wip/stack-merge`, effort: M):** K10 started from the K7 tip at 1f00f02, which lacked the review fixes on the K3–K6 tips and the second review round cascaded since. `claude/k7-settings-window` at faa4f2f is now merged. That brings in the render-scale gate, stalled surfaces, the input-frame cap, the media read retry, sticky fullscreen exits, AltGr-safe shortcuts, private logs, checkable tray choices, and K7's idle-cost, preview, recorder and high-contrast fixes. Where the two sides disagreed:
  - **No overlay:** K10's rule stays. The source keeps getting frames, so the capture closes as for any pause, and Settings' meter and the visualizer keep working. K3/K7's `SuspendSource` and `IOverlayFrameSource.FramesStopped` are dropped; their `TakeGradient` helper stays.
  - **Notification clicks:** K9's rule stays. An action lasts until the next notification and runs after the window procedure returns. K6's clearing on close is dropped, because a toast in the notification list must stay clickable.
  - **Both kept:** `FramePacer` has C13's fade rule and K8's Observed, then K3's `Stalled` rule. `OverlayHost` has K10's shared rules and no GPU device without an overlay, plus K3's loop fixes. Start-up listens early (K6) for K8's commands. `Claim(…, out runningElsewhere)` takes K8's `HandOverCommand`. The tray has both sides' items.
  - **Tests:** Platform.Tests went from 260 to 314 (313 at the merge, one more in its review). Expectations changed only where the merge changed behaviour on purpose: an input at the full rate now waits, and D3D pauses only a window that covers its monitor. Three signatures changed too: `GlowConstants.Create`, `RollingFileLog` and `Claim`. New tests cover the stall rule, `CoreFade`, the drag cap, the hidden scale switch, sticky exits and D3D, log redaction, the tooltip, `RefreshScheduler`, the recorder's refusals and the AltGr shapes. The refusals moved out of BehaviorPage into `ShortcutChoice` so they can be tested.
  - **Mirrors:** `OverlayLoopModel` has the cap and the scale gate. `PaceModel` needs no rule change, because a simulation has no stalls. `SoakRun` holds inputs the way the app does; a 2-hour run moves 8 of 290,509 frames, so PERF.md stands. Since the review, the cap is one rule, `FramePacer.InputWaits`, which `OverlayHost` and `OverlayLoopModel` call; `SoakRun` calls its copy `PaceModel.InputWaits`. `BenchMirrorTests` compares the two, and `SoakTests.InputsAtTheFullRateAddNoFrames` checks that the soak applies it (40 edits during music add no frames; without the hold, 18).
  - **HANDOFF:** the K6 branch's H-019 is now H-024. H-018 lists the merge's Lane A edits for review (SoakRun, PaceModel, SoakTests, PERF.md, the bug form and CONTRIBUTING), and H-019 names `SoakRun.NextFrameTime` as a third Bench copy. The K10 checklist gained rows for the merged fixes (sticky exits, refused shortcuts, AltGr, another copy, the menu, redacted logs). K11 rows 9 and 10 expect the redacted capture line, `Capturing the default output device (…)`, and read the device from the visualizer.
  - **Review fixes:** besides the mirrors, HANDOFF and checklist items above, Settings clears its queued system-state flag with `Interlocked.Exchange`, the fence the merge brought for the tooltip's flag (`AppController.ShowNowPlaying`). A plain store could pass the read of the state, so a lock or unlock published in between could leave Settings' preview and meter running or frozen until the next window event.
  - **Verified here (Linux):** Release and Debug build with 0 warnings and 0 errors. `dotnet test` passes: Rimlight.Tests 742, Rimlight.Platform.Tests 314, Rimlight.IconGen.Tests 89.

### K10 Windows checklist

**Rimlight, end to end on Windows (K10).** About an hour. Note the Windows build, x64 or ARM64, and each monitor's refresh rate.

1. **Install.**
   - Do: if an earlier dev build left `%LOCALAPPDATA%\Rimlight`, delete it (Setup would show its repair dialog). Run RimlightSetup.exe, either from the release or from `pwsh build/pack.ps1 -Runtimes win-x64` (it lands in `artifacts\installers`).
   - Expect: no admin prompt (SmartScreen: More info → Run anyway). The app starts, the tray shows the glowing rounded-square icon, and the "Rimlight is running" welcome opens.
   - Bug: a crash, no icon, or an admin prompt.
2. **First run.**
   - Do: close the welcome, Quit from the tray, then start Rimlight from the Start menu.
   - Expect: Settings opens and the welcome doesn't. `%APPDATA%\Rimlight\settings.json` contains `"firstRunComplete": true`.
   - Bug: the welcome shows again.
3. **Music.**
   - Do: play music.
   - Expect:
     - The edges glow and pulse on kicks within about a second.
     - The tray tooltip reads "Rimlight — Title · Artist"; an "&" in a title shows as "＆".
     - Stop the music: the glow fades to slow breathing after about 2 s.
     - Set Settings > Motion > "When it's quiet" to "Hide the glow": stop the music and the glow fades out; play again and it is back within about 0.2 s.
4. **Album colors.**
   - Do: play from an app that shows the cover in Windows' media controls.
   - Expect: the glow takes the cover's colors and crossfades in about 1 s on a track change. A track with no cover, or tray > Colors > Manual, gives your own colors.
5. **Settings and presets.**
   - Do: double-click the tray icon.
   - Expect:
     - Settings opens with Mica in your light or dark theme. The Appearance preview moves with the music, and the sliders apply live.
     - Click the Aurora chip: it gets the accent border, and tray > Presets shows Aurora checked.
     - Change Sensitivity: Aurora stays checked. Move the Glow slider: nothing is checked.
     - Quit and restart: every change is kept.
     - Closing Settings only hides it (the first session shows "Rimlight is still running in the tray").
6. **Tray.**
   - Expect:
     - A click turns the glow on or off; the icon dims to half opacity while off, and "Glow on" is unchecked.
     - Mode > Off fades the glow out.
     - "Open logs folder" opens `%APPDATA%\Rimlight\logs`, which holds `rimlight.log`. Its paths show `%USERPROFILE%`, not your user name.
     - A right-click opens the menu at the pointer, and it stays there. After a preset, Colors > From album art brings the album colors back.
7. **Fullscreen, lock and battery.**
   - Expect:
     - A fullscreen video on one monitor fades only that monitor out within about 0.3 s, and it comes back when the video leaves fullscreen. A maximized window doesn't count.
     - With the video fullscreen on one monitor, click the other monitor: it stays paused. Then click File Explorer or Settings on its monitor and the glow comes back there. A click on the desktop or the taskbar keeps it paused.
     - Win+L stops the glow; it fades back in after you unlock.
     - On a laptop, unplugging keeps the glow at 30 fps. Behavior > On battery > "Pause the glow" stops it while unplugged.
8. **A 120 Hz or faster monitor, if you have one.**
   - Do: set Displays > Frame rate limit to "Match the display", pause with Win+L, then unlock. Also Quit and restart.
   - Expect: the fade-in starts at once, both after the unlock and at start.
   - Bug: a moment of nothing, then a jump to about a third of the brightness; or a pop-in at start.
9. **No display chosen** (a Debug run).
   - Do: quit the installed copy, then run `dotnet run --project src/Rimlight.App` with music playing. In Settings > Displays, untick every display.
   - Expect: the glow disappears at once. Within about 0.5 s, the tray's "Render test (debug)" status reads `No overlay: 0 frames/s, 0 presents/s, 0 of 0 overlay(s) shown`, and Task Manager shows Rimlight at about 0 % CPU.
   - Then, with every display still unticked, open Settings > Motion: the level meter follows the music and the status shows about 10 frames/s. The log gets a new `[LoopbackCapture] Capturing the default output device (…)` line, which proves the capture had closed. Open the tray's "Audio visualizer…": its plots move, at about 60 frames/s. Close both windows, and the status goes back to 0 frames/s.
   - Then tick a display: the glow fades in over about 0.3 s, with no pop.
   - Laptop variant: set Displays to only the external monitor and undock. Expect the same as the first step. Dock again, and the glow comes back.
   - Bug: frames/s stays above 0, or the CPU stays where it was while the music played.
10. **Shortcut.**
    - Expect:
      - Ctrl+Alt+L toggles the glow from any app.
      - Record Ctrl+Shift+F9 in Behavior and it works; Alt+Tab is refused.
      - Record Ctrl+C or Alt+F: refused with "Apps use that for typing and editing…", and the old shortcut still works.
      - A shortcut another app already holds shows the inline warning.
      - If you can add a Polish (Programmers) keyboard layout: restart Rimlight. The welcome or Behavior page says Ctrl+Alt+L "types a character", AltGr+L still types ł in Notepad, and recording Ctrl+Alt+L is refused. Remove the layout afterwards (H-024).
11. **Second start, sign-in, and quit then start.**
    - Expect:
      - Starting Rimlight again while it runs brings its Settings to the front, with no second tray icon.
      - Sign out and back in: Rimlight starts in the tray with no window.
      - Choose Quit Rimlight in the tray and, within a second, start Rimlight from the Start menu: within about 8 s Rimlight runs again, the tray icon is back and Settings opens.
      - With the installed copy running, start a dev build (`dotnet run --project src/Rimlight.App`): a message says Rimlight is already running from another folder and names the installed exe.
    - Bug: no tray icon 10 s after that quick restart (the old behavior: the start was swallowed and no Rimlight ran).
12. **Visualizer.**
    - Do: run `& "$env:LOCALAPPDATA\Rimlight\current\Rimlight.exe" --debug-visualizer` while it runs.
    - Expect:
      - The window opens docked on the right and fully on screen, also at 150 % scale.
      - The spectrum and flux plots move with the music. With a track of known tempo, the BPM is within about 0.5 after 10 s.
      - "Copy params as JSON" puts JSON on the clipboard.
      - Pasting `{ "WindowSize": 512 }` says "Not applied" and nothing changes.
      - After you close the window, CPU drops back to idle.
13. **Demo.**
    - Do: run the same exe with `--demo`, with no music playing.
    - Expect:
      - A 120 BPM pulse, with new colors every 6 s. The tray shows "Demo mode" checked, and the icon isn't dimmed even if the glow was off.
      - Ctrl+Alt+L ends the demo and turns the glow off.
14. **Update** (needs two releases, for example v1.0.0 and then v1.0.1).
    - Do: with the older version installed, use About > Check for updates.
    - Expect:
      - "Downloading version…", then the "Update ready…" notification.
      - Click it → "Restart now?" → Yes: Rimlight comes back in the tray and says "Updated to version X". About shows the new version and every setting is kept.
      - The tray item "Restart to update (version X)" works too, without a question.
      - Let a later "Update ready" toast time out and click it in Windows' notification list: the same question, or nothing (note which). With Do Not Disturb on, the tray item still works.
      - While an update is ready, start Rimlight again from the Start menu: Settings comes to the front in the running copy, which must not quit or restart. Then Quit and start it: it installs at start and says "Updated to version X".
15. **Uninstall.**
    - Do: Settings > Apps > Rimlight > Uninstall.
    - Expect: the app and its shortcuts are gone, and `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` has no Rimlight value. `%APPDATA%\Rimlight` (settings and logs) is still there, and so is Velopack's own `%LOCALAPPDATA%\velopack\velopack_Rimlight.log`.
16. **CPU, for K11 and PERF.md.**
    - Do: watch Rimlight.exe in Task Manager > Details with Settings closed.
    - Expect: under 2 % with music; about 0 % in Idle Glow, with "Hide the glow", and in Off.

## K11 checklist

Hardening on real Windows (doc 07 Phase 7 and the CLAUDE.md definition of done). Nothing here runs on Linux. Maxwell runs each row and fills in the Result column; K11 then writes the results here as "K11 results" and the CPU numbers into `docs/PERF.md` (H-019 item 1). Run the K10 Windows checklist first: this one assumes the basics work.

- **Setup:**
  - An installed Release build: `RimlightSetup.exe` from a release, or `pwsh build/pack.ps1 -Runtimes win-x64` (it lands in `artifacts\installers`). Row 8 (a) needs a Debug run: quit the installed copy, then `dotnet run --project src/Rimlight.App`.
  - Music playing in an app that shows in Windows' media controls, Settings closed, default settings unless a row says otherwise.
  - Keep the log open: tray > Open logs folder > `rimlight.log`. The rows name the lines to look for.
  - Write down once: the Windows version and build, x64 or ARM64, the CPU model and number of logical processors, the GPU and driver version, and each monitor's resolution, refresh rate and scale.
- **A bug in any row:** a crash or an exception in the log, a second tray icon, a glow left on screen where it shouldn't be (on the wrong monitor, the wrong size, or after a pause), a glow that never comes back, or CPU that stays up afterwards.

| # | Scenario | Do | Expect | Result |
|---|---|---|---|---|
| 1 | Sleep and wake | While music plays, Start > Power > Sleep. Wake the PC after a minute and sign in. | The glow is gone while asleep and fades back in within about 1 s of unlocking; the music drives it again. Log: `[PowerWatcher] Suspending.`, then `[PowerWatcher] Resumed.` | |
| 2 | Hibernate | `shutdown /h` (turn hibernation on once with `powercfg /hibernate on` in an admin prompt). Start the PC and sign in. | As row 1. | |
| 3 | Modern Standby | Only if `powercfg /a` lists "Standby (S0 Low Power Idle)". Press the power button (or close the lid) for a minute, then wake and sign in. | The glow pauses when the display goes off (`[SystemWatcher] …, display off, …`) and fades back after the unlock. No suspend line is expected: Modern Standby sends none. | |
| 4 | Remote Desktop | From another PC, connect to this one with Remote Desktop (this locks the console), play music inside the session for a minute, then disconnect, and sign in at the PC again. | Connected: the glow runs inside the remote session (`[SystemWatcher] …, remote session, …`), with no pause while nothing is fullscreen (no "fullscreen everywhere"). Disconnected: paused. Back at the console: the glow returns on the real monitors within a few seconds. Note the RDP bandwidth with the glow on and with Mode > Off (the client's connection info, or the host's network send rate in Task Manager > Performance). | |
| 5 | Switch user | Start > your account > Switch user, sign in as another user, then switch back. | Paused while the other user is active; back after you return. | |
| 6 | Monitor off and on | Turn one monitor off with its power button for 10 s, then on. Then set Settings > System > Power > "Turn off my screen" to 1 minute and wait. | Power button: if Windows drops the monitor, the overlays rebuild without it and again with it (`[OverlayHost] N overlay(s) on M monitor(s), primary at X Hz.`); if it doesn't, nothing changes. Screen timeout: the glow pauses everywhere and comes back with the screen. | |
| 7 | Hotplug and rearrange | Unplug a monitor, plug it back in, drag the monitors into a new arrangement in Settings > System > Display, then change one monitor's resolution and refresh rate. | About 1 s after each change, every attached monitor that Settings > Displays selects glows at its own edges, with no glow on a stale rectangle and no flicker on monitors that didn't change. Log: one `[OverlayHost] … overlay(s) …` line per change. | |
| 8 | GPU reset (TDR) | (a) Debug run: tray > Render test (debug) > Simulate GPU device loss. (b) A real reset: `dxcap -forcetdr` in an admin prompt (part of Graphics Tools, an optional Windows feature), or update or reinstall the graphics driver while Rimlight runs. | The glow disappears for a moment and comes back within about 1–2 s (setup is retried every second while the driver is away). Log: `[OverlayHost] GPU device lost (…); recreating.`, then `[OverlayHost] GPU ready (hardware).` If it reads WARP, hardware should take over again within 30 s. | |
| 9 | Default audio device switch | While music plays, switch the default output between speakers and headphones in the taskbar's volume flyout, several times. | The glow keeps following the music within about 1 s of each switch. Log: a new `[LoopbackCapture] Capturing the default output device (…)` line after each switch. The log doesn't name the device (K6's private logs), so check which one is captured in the tray's "Audio visualizer…": its device line changes to the new device after each switch. | |
| 10 | Audio device unplugged | Unplug the USB or Bluetooth headphones that are the default output while music plays; plug them back in. | Windows' next default device takes over and the glow follows it. Log: `[LoopbackCapture] Capturing the default output device (…)` after the unplug and again after the re-plug if Windows switches back, sometimes after `[LoopbackCapture] Capture stopped: …` (when the stream fails before Windows names the new default). The visualizer's device line, as in row 9, shows which device took over. With no output device at all, the glow goes quiet, and it comes back once a device appears. | |
| 11 | Explorer restart | Task Manager > Processes > Windows Explorer > Restart. | The tray icon comes back within a few seconds, with its tooltip and its dimmed or normal look, and its menu works. The glow keeps running throughout and stays above the new taskbar. | |
| 12 | Fullscreen game | Start a game in exclusive fullscreen (or borderless fullscreen) on one monitor; Alt+Tab out and back. | That monitor's glow fades out within about 0.3 s and the others keep glowing; it comes back while the game isn't in front. | |
| 13 | 8-hour demo soak | Set Settings > System > Power to never sleep and never turn off the screen (a screen that turns off pauses the glow). Run `& "$env:LOCALAPPDATA\Rimlight\current\Rimlight.exe" --demo` with Settings closed. In Task Manager > Details, add the Handles, GDI objects, USER objects and Threads columns. Write down Memory (active private working set), Handles, GDI objects, USER objects and Threads after 5 minutes and after 8 hours. Optionally record with `dotnet-counters collect --name Rimlight --refresh-interval 60 --format csv --output rimlight-8h.csv --counters "System.Runtime[cpu-usage,working-set,gc-heap-size]"`. | The glow still pulses at 120 BPM after 8 h, at the same CPU. The working set ends within about 2 MB of the 5-minute reading (Bench's soak grew about 0.5 MB in its last 7 h, from JIT tier-ups); handles, GDI and USER objects and threads end within a few of where they started. | |
| 14 | CPU, for PERF.md | Follow docs/PERF.md, "Measuring the app on Windows": Release build, Settings closed, each case for 2 minutes after 30 s to settle, `dotnet-counters monitor` or Task Manager. | Music at 60 fps on 1 monitor: under 2 %. Music on 2 monitors: under 2 %. Idle Glow: about 0 %. "Hide the glow" while quiet: about 0 %. Off: about 0 %, GPU 0 %. `alloc-rate` near 0 B/s with music. | |
| 15 | CPU with windows open | With music playing: open Settings on Appearance; close it; open the visualizer (`--debug-visualizer`); close it. | Settings on Appearance: about 3 % or less. The visualizer: a few percent (write it down). After each closes: back to the row 14 numbers. | |
| 16 | Release build has no debug UI | Right-click the tray icon of the installed copy. | No "Render test (debug)" item. `--debug-visualizer` still opens the visualizer (doc 07 Phase 7 keeps it). | |
| 17 | Five genres (sync point 2) | With the visualizer open, play a four-on-the-floor house track (124–128 BPM), a boom-bap hip-hop track (85–95), a drum and bass track (170–175), a quiet acoustic ballad (about 70) and a loud, compressed pop or EDM track. After each, "Copy params as JSON" and save the text with the genre and tempo. | Beats tick on every kick of the house track, Tempo within ±2 BPM (within about 0.5 on a steady track since C13), no beats in breakdowns without a kick, Level not pinned at 1 on the loud track. Slider changes that felt better go into the saved JSON; it reaches the defaults through H-005 once Maxwell decides it. | |

- **Recording the results:** send the filled table back. The row 14 numbers go into PERF.md's "At a glance" row "The real app's CPU on Windows", with the CPU model, the number of logical processors and the monitors' refresh rates; anything that failed becomes a fix in K11.

## K12 notes

Branch `wip/k12-readme-banner`, review fixes on `wip/k12-readme-banner-fix`, merged in dd2c3b7. `assets/icon.svg` was already done in C10.
- **Structure:**
  - **`assets/banner.svg` (1280×640, doc 08 §1):**
    - The near-black #0A0A0F background holds a centered rounded "screen" (940×440, radius 44) whose rim glows #7C5CFF → #22D3EE from top left to bottom right.
    - The layers come from `assets/icon.svg`'s recipe, at banner scale (H-012):
      - a wide faint bloom and a tighter halo outside
      - the screen fill, #15131E → #0C0B12
      - the inward spill, clipped to the screen and drawn twice: a far one (blur 48) and a near one (blur 10), so the light fades off the edge roughly like `Glow.hlsl`'s falloff
      - a 6 px rim and a soft bright core (blur 1.5)
    - Five `feGaussianBlur` filters in all. Each filter region covers the whole banner (`userSpaceOnUse`), so no blur is clipped.
    - The wordmark is Inter Display SemiBold, 150 px, −0.02 em tracking, on baseline 338. The tagline is Inter Regular, 34 px, on baseline 414. Both are shaped with HarfBuzz (kerning on), centered on x = 640 by their ink, and stored as one path each, one contour per line. There is no `<text>` and no font dependency.
    - The SVG comment names Inter 4.0 and its SIL OFL 1.1 license. The OFL allows embedding the outlines in a document.
  - **`assets/banner.png`:** 172 KB, opaque, rendered by `build/icons.sh` / `icons.ps1` with `icon-gen png --width 1280 --height 640`. It is also the social preview.
  - **`tools/icon-gen`:**
    - `png` takes `--width` + `--height` as an alternative to `--size`. Giving both, or only one of width/height, is a usage error (exit 2).
    - `SvgRasterizer.RenderBgra(width, height, opacity)` scales uniformly to fit and centers (letterbox or pillarbox). It has no default opacity, so `RenderBgra(16, 0.5f)` still means (size, opacity).
    - `RenderBgra(size, opacity)` and `BuildPng(art, size, opacity)` delegate to the rectangular versions. `SvgRasterizer.MaxSize` = 4096.
    - `IconGenCli.Options` replaces `Size` with `Width`/`Height`.
    - Every existing icon regenerates byte-identically.
  - **`tools/icon-gen.Tests`:** +16 tests, 89 in total (see Verified).
  - **`README.md`:** doc 08 §2 in order: banner, badges, pitch, the "Download for Windows" button with the ARM64 and portable links, the demo GIF slot, features, screenshots, install, how it works (Mermaid), privacy, FAQ, build from source, roadmap, contributing, credits, license.
  - **`docs/RELEASE-GUIDE.md`:** what only Maxwell can do:
    - §1–3: the GIF/MP4 recording and the two screenshots, and un-commenting the README and release-notes blocks
    - §4: repo settings with exact text
    - §5: the v1.0.0 checklist, with the 11 release files
    - §6: turning on code signing, then dropping the SmartScreen sentences
  - **`.github/release-notes-intro.md`:** the demo GIF sits at the top by absolute URL (raw.githubusercontent.com, main), in an HTML comment until the GIF exists.
- **Spec clarifications and deviations:**
  - **Download button logo:** doc 08's `logo=windows` no longer renders. shields.io's icon set dropped the Windows and Microsoft logos; checked against img.shields.io, where `windows`, `windows11` and `microsoft` give no logo and `dotnet` and `githubactions` do.
    - The button carries a self-drawn download arrow as a base64 data URI, white, 24×24.
    - Text, color (#7C5CFF), `for-the-badge` style and height 44 are as in doc 08. It has no trademark issue.
  - **Downloads badge (deviation from doc 08's "total downloads"):** `/github/downloads/<repo>/total` would mostly count update checks.
    - K9's updater uses Velopack's `GithubSource` without a token. On every check, `GitBase.GetReleaseFeed` downloads `releases.<channel>.json` from each of the 10 newest releases through `browser_download_url`, and each of those downloads adds to that asset's download count (decompiled Velopack 1.2.161). Update nupkgs land in the same total.
    - The badge counts `RimlightSetup.exe` across all releases instead, through shields.io's specific-asset route `/github/downloads/<owner>/<repo>/<asset>` (checked live against a repo that has releases). It reads "downloads | N [RimlightSetup.exe]".
    - It undercounts: the ARM64 installer, the zips and vpk's `Rimlight-win*-Setup.exe` copies are not counted. A README comment above the badge row says why.
    - The color is #0891B2, not the brand #22D3EE, because shields puts white text on it and white on #22D3EE is about 1.8:1.
  - **Badges:** all six are shields.io. Release uses `sort=semver&display_name=tag`. License, Windows and .NET are static badges, which don't depend on the API.
  - **Roadmap wording:** doc 07's stretch goals without other products' names, per doc 08's "don't mention any other app":
    - Hue Entertainment → "Smart room lights that follow the music"
    - Teams/Zoom → "pausing the glow during a video call"
    - "ambilight style" → "takes its colors from the edges of what's on screen"
  - **FAQ facts and their sources:**
    - Battery: "Save power (30 fps)" = 30 fps at half resolution; also "Pause the glow" and "Keep going" (K3, Behavior page).
    - Fullscreen: per-monitor pause with the overlay hidden; maximized windows don't count; lock, sleep and display off pause everywhere (K3/K5).
    - Games: the glow draws at the Frame rate limit while music drives it (60 by default, never above the display's rate), about 10 fps in Idle Glow, and not at all when still; the limit can go down to 30 in Settings > Displays.
    - Music apps: motion works with any audio (loopback); colors need the app in Windows' media controls, otherwise the manual colors (K4).
    - Uninstall: Settings > Apps; `%APPDATA%\Rimlight` stays (doc 07 Phase 6, H-014, H-021).
    - Bug reports: "Open logs folder", the bug form, and CONTRIBUTING#reporting-a-bug-with-logs (H-013).
    - No performance numbers anywhere: K11 hasn't measured any.
  - **Logs path:** the README says `%APPDATA%\Rimlight\logs`, as K9 decided (H-014 item 3).
  - **Placeholders:** the demo GIF, the video link and the Settings `<picture>` are ready in HTML comments, each with a one-line italic placeholder, so nothing renders broken.
  - **Build from source, 3 commands:** `git clone`, `cd`, `dotnet run --project src/Rimlight.App`. The build and test commands, `--demo` and `--debug-visualizer` are mentioned in one line, with a link to CONTRIBUTING.
  - **Generator not committed:** the text-to-path step (HarfBuzzSharp + SkiaSharp, about 60 lines) is a one-off design step and stayed in scratch. The outlines are baked into the SVG, and its comment records everything needed to redo them after a rename. An `icon-gen` command would need HarfBuzz shaping with kerning to reproduce them exactly, which is out of proportion for a rename step.
  - **First release:** doc 08 says v1.0.0, but CHANGELOG's placeholder is `[0.1.0]`. The guide tells Maxwell to rename it to `[1.0.0]` when tagging (H-022).
  - **Release guide, matched to the code after review (K8, vpk 1.2.161):**
    - **Demo loop:** `DemoBeat` changes palette every 6 s on a downbeat, cycling through 4 palettes every 24 s, so any 8 s clip ends on other colors than it starts with. The guide centres the clip on a palette change (16 beats, with a downbeat at both ends), always adds the fade back, and asks for at least 15 s of recording.
    - **Starting the demo:** `--demo` hands "demo" to a running copy. Only a run from source needs the installed copy quit first. Unchecking "Demo mode" in the tray ends the demo.
    - **Screenshots:** the Settings preview ignores demo mode, because it reads `MusicGlowSource.LatestAudio`, which comes from the real analyzer. The screenshot step therefore asks for music.
    - **Release contents:** a release holds 11 files: `RimlightSetup.exe` and `RimlightSetup-arm64.exe`; vpk's identical `Rimlight-win-Setup.exe` and `Rimlight-win-arm64-Setup.exe`; both portable zips; `Rimlight-1.0.0-full.nupkg` and `Rimlight-1.0.0-win-arm64-full.nupkg`; `releases.win.json`, `releases.win-arm64.json` and `RELEASES`. `vpk upload github` uploads every asset in `assets.<channel>.json`, plus `RELEASES` for the default `win` channel.
- **Verified here (Linux):**
  - **Build and tests:** `dotnet build Rimlight.sln -c Release` and `-c Debug`, clean builds after the fixes: 0 warnings, 0 errors. `dotnet test Rimlight.sln -c Release` at the time: 657 Core + 89 icon-gen, all green. One run made while the shared disk was 98 % full reported 1 Core failure whose name wasn't captured; two full reruns and four Core-only reruns passed. *(Probably the background-GC counting effect K10 found; both test hosts now run without background GC.)*
  - **Banner, by eye:**
    - at 1×, at 4× gain (no visible banding in the blurred spill), in a 3× corner zoom, and as 320 px and 160 px thumbnails (the wordmark is still legible at 160 px)
    - in headless Chromium 156 (Playwright): matches the Skia render, blur included
    - in CairoSVG, which ignores `feGaussianBlur`: geometry, gradients, clip and text match
  - **Banner, regeneration:** `build/icons.sh` regenerates the four icon files byte-identically.
  - **Mutation check:** halo opacity 0.7 → 0.6 without regenerating fails `BannerMatchesAFreshRenderOfItsSvg` (9/255 against a tolerance of 8).
  - **New tests:**
    - command line: `--width` alone, `--height` alone, `--size` together with `--width`/`--height`, out-of-range or non-numeric values, and `--width` on `ico` all exit 2
    - parsing: `--size` gives (n, n), and width/height parse in either order
    - a 40×20 PNG from 16×16 art is pillarboxed to columns 10..19
    - rendering: exact fit of 20×10 art in 40×20, a 32×16 pillarbox, the square render equals the rectangular one (`RenderBgra` and `BuildPng`), bad widths and heights rejected
    - committed files: the banner matches a fresh render, is 1280×640, opaque and under 1 MB
  - **README text, by script:**
    - the privacy bullets are character-identical to doc 01 and to `AboutPage.xaml.cs`
    - the SmartScreen note and the pitch are verbatim from doc 08
    - the repo description and topics in the guide are verbatim from doc 08 §4
  - **Velopack 1.2.161 and vpk 1.2.161,** decompiled with ilspycmd for the badge and the release list: `GithubSource`, `GitBase<T>.GetReleaseFeed`, `GitReleaseUploadCommandRunner.RunCoreAsync`, `BuildAssets`, `DefaultName`.
  - **README links and badges:**
    - Every relative link and anchor resolves (`#privacy`, `CONTRIBUTING.md#reporting-a-bug-with-logs`, `CONTRIBUTING.md#build-from-source`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, `LICENSE`, `assets/banner.png`).
    - Rendered with markdown-it, github-markdown-css 5.5.1 and Mermaid 10.9.1 in Chromium, and with pandoc gfm: the tables, `<details>`, task lists and the flowchart all render, the flowchart parses, and the comments stay hidden.
    - All badge URLs answer 200 from shields.io. Release and downloads read "no releases found", and CI reads "no status", because main is still at 8e3aa42 (before `ci.yml`) and nothing is released yet. The custom logo renders.
  - **External links:**
    - the 4 `releases/latest/download/…` links return 404 until v1.0.0, as expected; `/releases` and `/releases/latest` return 200
    - the issue templates exist on the integration branch (API 200), not yet on main
  - **Not verifiable here:**
    - Third-party GitHub repo pages are blocked by the sandbox proxy (403). Their URLs come from each package's nuspec `<repository>`, and all six packages are MIT per their nuspecs.
    - maxwellolander.com was unreachable through the proxy (502).
    - `build/icons.ps1` was not run: there is no PowerShell here. Its one added line mirrors the existing `Invoke-IconGen` calls.
    - GitHub's real rendering: the local preview approximates it.
- **Left for later:**
  - **Maxwell (docs/RELEASE-GUIDE.md):**
    - record `assets/demo.gif`, the MP4 link and `settings-dark.png` / `settings-light.png`, then un-comment the three README blocks and the release-notes GIF
    - repo settings: About, social preview, Discussions (then un-comment "Ask a question" in `.github/ISSUE_TEMPLATE/config.yml`), squash-only merging, private vulnerability reporting, pin
  - **After the integration merges into main:** recheck the badges (CI passing) and the community-file links.
  - **When signing is on:** follow RELEASE-GUIDE §6.
  - **When a winget manifest exists:** replace "coming soon".
  - **If the app is renamed:** re-outline the wordmark as the SVG comment describes, then run `build/icons.sh`.
  - **Optional (Lane A):** have release.yml delete vpk's duplicate `Rimlight-win*-Setup.exe` assets before it publishes, so the Releases page shows one installer per architecture. Not done: nothing needs it, and it touches the release path.

## Feedback round 1 (Maxwell's first Windows run)

Maxwell ran the app on Windows and reported, verbatim:

> the border doesn't really sync with the type of music so its still super sharp even when listening to a calm song like noah kahan or hozier for rap I am ok with it bouncing harder but those songs it should be more of a wave. Also the bounces weren't really proportionate with what was happening in the song it would be a super big bounce when the person was whispering. Thirdly there is a very obvious line or bar around the screen I can see the glow I want it to fade in more also theres no settings to change anything

- **Three changes, merged on `claude/confident-mccarthy-gt8tif` in 3a2fd04:**
  - Dynamics (wave vs bounce, the bounce on a whisper): ee8f7a4.
  - Soft edge (the line): 5420417.
  - Settings window (no settings): af5d2a6.
  - Dynamics and soft edge each had two reviews and a fix round before merging.
- **Merged state, checked in the docs pass:** `dotnet build Rimlight.sln -c Release` gives 0 warnings. `dotnet test` passes Rimlight.Tests 815, Rimlight.Platform.Tests 368 and Rimlight.IconGen.Tests 89. Rimlight.App.Tests isn't a test project off Windows; on Windows CI it passes 23 of 23 (see Settings window, Verified).
- **Not changed:** no file under `Contracts/`, and no `AudioTuning` or `Settings` default. Nothing has run on Windows yet; the checklist is at the end of this section.

### Dynamics

Branches `wip/dynamics` (b7e6817, 06bc2fd, 1aad6da) and `wip/dynamics-fix` (2a660d6, e74255d), on 5b387fc. Effort: L.

- **Problem, measured at 5b387fc.** Setup: the real analyzer and light engine through `CoreFactory`, at 60 fps with 10 ms packets, default settings, on the new procedural style tracks.
  - **Auto-gain flattening (doc 03 §2).** The 4 s peak half-life, +1 dB/s floor and 12 dB minimum range stretched a whispered verse 15 dB under the chorus back over the full range about 6 s after the chorus. In the mid band the range was 19.7 dB at 51 s and back at 12.0 dB at 57 s. The chorus was only 1.19× as bright as the verse after it.
  - **Voice and guitar fired beats.** In a drumless ballad the 30–150 Hz detector fired 1.1–1.8 times a second, and each stem fired on its own: guitar 1.3–1.9/s, a whispered baritone 1.4/s.
  - **Every beat was a full bounce.** `Beat` jumped to exactly 1 on each, giving 16–37 full pulses per ballad section.
  - **Level followed syllables.** Its 30 ms attack tracked syllables and plucks: 6.6–10.2 % of the shown brightness's variance lay above 3 Hz. "Shown" is I × (1 + 0.25·Pulse).
- **Structure.** Core internals behind the frozen contracts, with internal constants and fixed internal bands. CORE-API §4.2 has the full description.
  - **`SongLoudness` (new), the song reference:**
    - **Momentary loudness:** power in fixed 30–150 / 150–2000 / 2000–12000 Hz bands, weighted 0.5 / 1 / 1.5 and smoothed with τ = 0.25 s.
    - **Reference:** a 0.5 dB histogram that forgets with τ = 10 s. Its 90th percentile is the song's loud parts.
    - **Position:** 1 at the reference, falling linearly to 0 at 24 dB below it. It is 0 only in near-silence (−80 dBFS, the auto-gain hold level), so quiet playback stays scale-invariant.
    - **Prior:** until the memory fills, 4 s of virtual loudness at 4 dB above the loudest moment so far.
    - **Gaps:** 1 s of near-silence, or of no packets, clears the memory and re-arms the prior. A session reference (τ = 30 s, from about 16 s into a song) survives the gap. It then floors the next song's reference and falls 0.2 dB/s.
    - Subnormal floats are flushed to 0.
  - **`BeatCharacter` (new), each beat's grade and the music's punch:**
    - **Kick prominence:** the rise of a fixed 30–100 Hz kick band over the beat's 35 ms onset window (audio time). It is measured in dB against the amplitude of the music above the bass band just before the beat, the larger of a 0.25 s and a 1.5 s average. Kick-likeness = smoothstep(4 dB, 9 dB) of it.
    - **Punch:** a drive follows the kick-likeness of the recent onsets. It moves half the way toward a more kick-like onset and a tenth of the way toward a less kick-like one. It holds for 3 s after the last kick-like onset, then fades with τ = 2.5 s. Punch = smoothstep(0.3, 0.75, drive).
    - **Grade** = kick-likeness × punch (as before this onset) × accent × loud × Sensitivity, clamped to 1.
      - Accent: the rise over 0.85 × the strongest recent kick's rise, which halves every 1.5 s.
      - Loud: the song position held over the dip between beats, through smoothstep(0.3, 0.6), never under 0.3 × punch.
      - One kick-like onset in a wave gives no pulse, the next about 0.4, the third full size. The first second after near-silence only listens.
    - **Beat** is the grade on the frame it rises, then decays with `BeatDecaySeconds` as before.
  - **`AudioAnalyzer`:**
    - Level target = min(1, position / 0.8) × (1 − 0.6 × (1 − doc 03's Level target)).
    - Time constants: `LevelAttackSeconds` × 10^(1 − punch) and `LevelReleaseSeconds` × 5^(1 − punch). That is 0.3 s / 1.25 s with no drums, and doc 03's 30 / 250 ms in a groove and in near-silence.
    - Tempo: where kicks play (a kick-like onset within 4 s), only kick-like beats at least 0.6× as strong as the strongest recent kick count, timed when they fired (`BeatDetector.AddTempoBeat`). Elsewhere every beat counts, as before.
    - Detection and `BeatCount` are unchanged.
  - **`BandAnalyzer`:** `WeightedPower` and `KickBins` use fixed bands, so live-tuning the bass band in the visualizer doesn't change the grading. `BeatResult` gains `KickLevel`.
  - **`LightEngine`:** the phase push is 0.01 cycles × the height `Beat` rises to (it was 0.01 per beat).
  - **Visualizer (App):**
    - `AnalyzerTap` records whether `BeatCount` rose on each call (`AnalyzerSnapshot.Fired`), and the "beat fired" ticks use it (before, they needed Beat ≥ 0.999).
    - The slider descriptions for the Bass band, Level attack/release, Sensitivity and Beat decay say what each now does.
  - **`tools/audio-tools/StyleTracks.cs` (new):**
    - Six procedural songs: CalmBallad, Rap, Edm, BalladWithDrums, FolkBand and PianoBallad.
    - Each section is mixed to a target RMS, with kick ground truth.
    - Single stems (`StyleParts`) and `Mix`. Stems are not clipped; a full render is.
- **Spec clarifications and deviations:**
  - The dynamics merge put clarifications in doc 03 (§2 auto-gain, the Level envelope, beat step 4, and the §4 tempo) and in CORE-API (§2, §4.1–4.4, §6, §6.1):
    - Level is judged against a 10 s song reference. Auto-gain stays as written for `Bass` and the contrast within a groove.
    - Level's time constants are 10× / 5× with no drums.
    - `Beat` is a 0..1 grade, not always 1. A kick in a loud groove still reads exactly 1.
    - Where kicks play, the tempo counts only kick-like beats.
  - **Added in this docs pass, doc 03:**
    - §2 step 5: Sensitivity also scales each beat's grade (0.5 gives pulses about half as big).
    - §4: the new constants are internal and have no sliders yet (H-025).
  - **Volume (doc 03 §2):**
    - The same song at any steady volume gives the same light: within 0.0002 of Intensity down to −35 dB.
    - A volume change in the middle of a song can't be told from the music getting quieter or louder. Steps up to about 5 dB barely show in the loud parts (the knee at 0.8). Larger ones settle in up to about 25 s, or 30 s when a step up lands at the start of a quiet verse. Doc 03's auto-gain took 0.7–15 s.
  - **Not edited:** doc 01 §2 ("a pulse fires on each beat"; the pulse is now graded, and voice, guitar, piano and bass guitar onsets without drums give none) and doc 07 Phase 3 ("a small kick of 0.01 on each beat"; it is now 0.01 × the beat's height). CORE-API §4.2 and §6.1 carry both.
  - **Rejected: telling a volume step from the music getting quieter** (a histogram shift on a detected gain step). On the style tracks a song's own band balance moves 2–7 dB within 0.8 s. The prototypes missed real steps and flagged section changes. The knee and the groove floor (0.3 × punch) were done instead.
- **Verified here (Linux).** Style tracks at 60 fps with 10 ms packets, Brightness 0.8, against 5b387fc:

  | Measure | 5b387fc | Now |
  |---|---|---|
  | Calm ballad pulses per section | 16–37, all 1.0 | 0 |
  | Calm ballad brightness variance above 3 Hz | 6.6–10.2 % | 0.2–0.7 % |
  | Chorus vs the whispered verse after it | 1.19× | 1.58× |
  | Folk band + bass guitar 6 dB under: chorus pulses ≥ 0.5 | 19–23 | 0 |
  | Same + brush kit 12 dB under: chorus pulses ≥ 0.5 | 19–20 | 0 (max 0.18), plus 1 full pulse at each chorus entrance |
  | Same mix, chorus brightness variance above 3 Hz | 13–18 % | 2.3–3.8 % |
  | Piano ballad pulses per section | 6–7 at 1.0 | 0 |
  | Kit at the music's level, whispered verse 2 | 14 at 1.0 | mean 0.30, max 0.35 |
  | Rap kicks with a pulse ≥ 0.7 | 61/61 | 61/61 (58/61 at 44.1 kHz) |
  | EDM kicks with a pulse ≥ 0.7 | 74/77 | 73–76/77 |
  | EDM tempo | 246 BPM | 127.8–128.2 at 44.1 and 48 kHz, seeds 1, 5 and 6 |
  | EDM breakdown pulses | 18 at 1.0 | 0 |
  | Folk band after itself, verse 1 mean pulse | 1.0 | 0.57 (0.82 as the first song) |
  | Rap 10 dB quieter after a gap, mean pulse 5–10 s | 1.0 | 0.95 |
  | Rap 14 dB quieter after a gap, mean pulse 10–20 s | 1.0 | 0.52 (trade-off) |
  | Rap kicks in the 10 s after −10 dB mid-song, mean pulse | 0.94 | 0.73 |
  | Volume step, last brightness difference > 0.1: rap −20 / +20 dB | 5.2 / 15.3 s | 23.5 / 6.0 s |
  | The same, ballad −6 / +6 dB | 0.7 / 1.5 s | 16.9 / 13.6 s |

  - **Kick prominence medians:**

    | Source | dB |
    |---|---|
    | 808s | 13.5 (12.2 at 44.1 kHz) |
    | EDM kicks | 19 |
    | Acoustic kicks | 9.6 |
    | Brush kit at the music's level | 17 |
    | Brush kit 6 / 12 / 20 dB under | 12 / 7 / 4 |
    | Bass guitar 6 dB under | −3 |
    | Piano | −9.5 |
    | Picked guitar | −19 |
    | Ballad voice and guitar | −31 |

  - **Cost:**
    - About 1–2 µs a step by operation count.
    - Side by side, 113–168 µs a frame (analyzer and engine, style track) against 104–146 µs before, in alternating runs: within this machine's noise, so PERF.md is unchanged.
    - 0 bytes per frame. `AStyleTrackAllocatesNothingPerFrame` and the existing analyzer and engine allocation tests pass.
  - **Mutations:** of 17, the 16 that still apply each fail at least one test.
  - **Builds:** Release and Debug have 0 warnings on both branches.
  - **New tests:**
    - `MusicDynamicsTests`, `SongLoudnessTests`, `BeatCharacterTests` and `StyleTracksTests` (with `MixPutsEachStemAtItsLevel`).
    - `AudioAnalyzerTests.OffBeatBassNotesAreBeatsButNotTempo` and `LightEngineTests.AGradedBeatPushesInProportionToItsHeight`.
    - `VisualizerHandOffTests.TheTapMarksEveryCallThatFiredABeat`.
  - **Tests changed on purpose:**
    - `SyntheticAudio.Run` spots a beat by `BeatCount` rising, because `Beat` is now a grade. It takes `pulses:`, and `StrongPulseRate` puts pulse-size assertions back in four `AudioAnalyzerTests`.
    - WavAnalyze: full pulses ≥ beats − 3.
    - `FollowsTheRealAnalyzerThroughMusicSilenceAndMusicAgain`: pushes follow the graded rule, with full pulses ≥ beats − 4.
    - `SoftDrumsBounceSoftly`: the chorus bound is 0.25 (measured 0.18).
- **Left for later:**
  - **Windows listening:** the checklist below. Everything was measured on procedural tracks, so real recordings may need threshold tuning.
  - **Known limits:**
    - The first song after launch has nothing to be judged against, so a soft kit that stands out of a sparse first verse still bounces (0.82).
    - Each chorus entrance gives one full pulse.
    - A bass guitar playing alone, or as loud as the rest of a drumless song, pulses.
    - A quieter master after a gap dims for up to about 40 s. Most streaming apps normalize loudness, which keeps songs within a few dB of each other.
    - The tempo of soft-drum or drumless folk is unreliable, as before (for example 38 or 167 BPM).
    - Detection is unchanged, so its misses are too: 3 of 77 EDM kicks next to off-beat bass notes and 5 of 23 acoustic kicks.
  - **H-025:** the new constants can't be tuned live, and the visualizer can't show grade, kick-likeness, punch or position.
  - **H-026:** Codex's review of the Lane A paths.

### Soft edge

Branches `wip/soft-edge` (756fe4f) and `wip/soft-edge-fix` (7ca790c), on 5b387fc. Effort: M.

- **Problem, measured at 5b387fc.** `Glow.hlsl` drew a solid core, `core = 1 − smoothstep(T − 1, T + 1, d)`, then took `max(core, 0.85·glow)`. At the defaults (6 DIP, Glow 0.45, 1080p, Intensity 0.72) the alpha codes ran 184, 184, 184, 184, 184, 155, 121, 116… That is a flat 5 px, then drops of 29 and 34 code values: the bar.
- **Structure:**
  - **`GlowProfile` (Platform, new)** holds the profile math. `Glow.hlsl` step 2 mirrors it, and the Settings preview (`GlowRaster`) calls it.
  - **Band:**
    - The e-folding distance is b = max(T / 2, 1/8 px), where T is Thickness in surface pixels (DIP × DPI scale × render scale).
    - The gain is G = 2b·sinh(1 / 2b), the pixel average of exp(−x / b). It is 0 at Thickness 0, and below b = 1/8 px it is scaled by (T / 2) ÷ (1/8).
    - `band = saturate(G·exp(−d_band / b))`, through its own rounded field with radius min(max(corner, 2b), short side / 2).
  - **Glow** (K1's shape):
    - `glow = 0.85·exp(−d_glow / max(spread·(1 + 0.35·Pulse), 0.5))`, through K1's rounded field with radius max(corner, 2·spread).
    - Spread = max(1 DIP, Glow^1.5 × 0.35 / 3 × short side); it was Glow². The default 0.45 reaches 38 px at 1080p, against 25 before.
  - **Combine:** `shape = 1 − (1 − glow)(1 − band)`, a screen blend. It never passes 1, and Thickness 0 is exactly the old glow.
  - **Gain:**
    - `Gain = SoftClip(Intensity × (1 + 0.25·Pulse))`, computed once a frame on the CPU.
    - SoftClip(x) = min(x, 0.9) + 0.1·o / (1 + o), with o = max(x − 0.9, 0) / 0.1. That is 0.95 at 1 and 0.978 at 1.25, the largest gain.
    - The shader draws `alpha = shape × Gain × Visibility` and returns early when that is under 0.5/255. The profile is only ever scaled.
  - **cbuffer:** still 16 floats, 64 bytes. `BandScalePx`, `BandGain` and `BandRadiusPx` take the slots of `CoreThicknessPx`, `CoreFade` and a pad. `Gain` takes `Intensity`'s. `CornerRadiusPx` stays, and every field radius is at least it.
  - **Appearance page:** Thickness now reads "How wide the brightest light along the edges is, before it fades into the glow. 0 leaves only the glow."
  - **Settings preview:** it floors the pulsed spread at the shader's 0.5 px (`GlowProfile.PulsedSpread`; K7 used 1 px).
  - **Unchanged:** the colour path, the mean-colour blend, the sRGB encode and the dither.
  - **Candidates:** four profiles were rendered at 1080p (Thickness 0/6/20/40 × Glow 0.2/0.45/0.9, old and new spread):
    - `0.85·glow + 0.15·band`: Thickness barely did anything.
    - `0.70·glow + 0.30·band`: a hairline came back, and the glow-only look was 18 % dimmer.
    - The screen blend: chosen.
    - A Gaussian band: read as a blurred stroke with a mitre in each corner.
  - **The first draft rolled off each pixel.** On a full beat at Brightness 100 % that squashed the brightest pixels into a shoulder: up to 16.8 code values of extra drop inside the edge and 49 at the edge, a rim flashing on the beat. The review fix rolls off the gain instead.
- **Spec clarifications and deviations** (the PRD and doc 04 clarifications were added in this docs pass):
  - **PRD §4:** Thickness is the width of a soft bright band at the edge that blends into the glow, not a solid line.
  - **Doc 04 §3 step 2:**
    - The core smoothstep is gone; band and glow combine by screen blend.
    - The per-pixel clamp is replaced by a roll-off of the scalar gain on the CPU.
    - Two rounded distance fields replace the square one (K1 had already rounded the glow's).
  - **Glow → spread** uses the slider to the power 1.5 instead of K1's 2. The PRD endpoints are unchanged.
  - **K3's screen-pixel CoreFade** is replaced by the band's pixel-average gain, which keeps a band's light at any render scale. Every measure now converts at the render scale.
  - **Brightness 100 %:** the gain peaks at 0.95 without a beat (instead of 1) and at 0.978 on a full beat (instead of 1.25). The beat still pushes the glow 35 % further in.
  - **Lit area:** the default glow lights about 40 % more pixels. At 4K defaults that is 54 % of the screen instead of 38 %.
- **Verified here (Linux):**
  - **Shader:** `Glow.hlsl` compiles under DXC 1.8.2407 (`-T ps_6_0` / `vs_6_0`, `-HV 2018`, warnings as errors) and is ASCII-only. There is no SM4 compiler here, so vs_4_0/ps_4_0 was not compiled; the shader uses only SM4-legal intrinsics.
  - **Cost:** DXC ps_6_0 -O3, up to and including the early-out branch:

    | Shader | Instructions | exp | Divides |
    |---|---|---|---|
    | 5b387fc | 81 | 1 | 1 |
    | Now | 78 | 2 | 2 |

    That is one more exp and one more divide per pixel, both for the band. After the branch: 256 → 255.
  - **Cross-checks:**
    - A NumPy port matches `GlowProfile` to four decimals, and a scratch build of the real `GlowRaster` renders the same look.
    - A reviewer's independent port of both shaders confirmed the bar is gone. At the defaults the drop between pixels only shrinks (9.7, 8.9, 8.1, 7.3…), and the edge pixel is 199 against 204 before, at Intensity 0.8.
  - **Tests:**
    - `GlowProfileTests`:
      - The profile is monotone and brightest at the edge for every Thickness, Glow, Intensity and Pulse. That holds at 100/125/150/200 % DPI and at full and half scale, along the edge and the corner diagonal.
      - `NoSettingDrawsABar`, at every gain: inside the outermost pixel pair the drop never grows by more than 1.1 code values (worst 1.02); at that pair, by at most 6 (worst 5.86).
      - `TheDefaultLookFadesWithoutALine`, `TheFadeNeverEasesOffFasterThanItsLights` and the gain roll-off.
      - The old solid core fails these checks.
    - `GlowRasterTests`: the preview is within 1 code value of `GlowProfile` and monotone, including Glow 0 on the beat, and allocates 0 bytes per frame.
    - `GlowShaderTests`: the cbuffer names and offsets match `GlowConstants`, and step 2's formulas match `GlowProfile`'s constants.
    - Changed on purpose:
      - `GlowMapsToSpreadAsThePrdSays`: 37–39 px, from 24–26.
      - `AThinCoreFadesByItsWidthInScreenPixels` is replaced by `AThinBandKeepsItsLightAtAnyScale` and `ThicknessIsTheSoftBandsWidth`.
      - `HalfScaleHalvesEveryPixelMeasure`, `ValuesAreClamped` and `LooksLike` read the band fields and `Gain`. `Gain(1, 0)` is 0.95.
  - **Mutations:**
    - A `max()` combine passed all 84 Glow tests in the draft; it now fails 12.
    - Swapping `BandGain`/`BandRadiusPx` in the shader, or `SpreadPx`/`Gain` in the struct, fails `GlowShaderTests`.
  - **Platform.Tests:** 367 on the branch, all passing.
- **Left for later:**
  - **Windows:** nothing has run on a real screen yet.
  - **Corners (pre-existing K1):** outside each rounded field there is a plateau ending in an arc kink, plus a diagonal crease.
    - At the defaults the glow arc's slope changes by 4.4 code values per diagonal pixel at its 76 px radius; the old core stepped by 27.5.
    - A smooth clamp of both fields would remove it, but that changes K1's corners and needs its own renders.
  - **Preview colour:** the preview's mean-colour blend runs in sRGB space (K7), up to 4 code values off at Glow 1.
  - **At high Glow** the top-left corner (the gradient's seam blend) shows a lighter colour wedge. The colour logic is unchanged.
  - **Band divide:** storing the band's reciprocal would save it.
  - **K11 (H-019 item 1):** the larger lit area and the extra exp and divide belong in the GPU/CPU measurement.

### Settings window

Branch `wip/settings-window` (ff5db1c, 7c16c3a), on 5b387fc. Effort: M.

- **Root cause.** No defect was found. The static audit below came back clean. What it did prove is that 5b387fc hid every way Settings can fail:
  - **(P1)** If WPF-UI didn't load, or building the window threw, the launcher showed the welcome window without its Settings button. Nothing said anything went wrong.
  - **(P2)** If `Present()` threw, the only trace was "[App] Opening Settings failed" in the log. Nothing was shown.
  - **(P3)** If a page threw in `SetActive` or `Refresh`, the content area stayed empty: WPF-UI 4.3.0 raises `Navigated` before `UpdateContent`. A `Refresh` that throws also kept that page's edits from ever applying.
  - **Likely:** one of P1–P3 fired on Maxwell's PC; his log says which.
  - **Possible:**
    - Settings opened, and he meant that no setting changes the feel or the edge. The "very obvious line" was the default 6 px solid core; the soft edge above replaces it with a soft band.
    - The window opened behind other windows (Windows' foreground lock).
    - A single click, which toggles the glow, instead of a double-click.
  - **Static audit:**
    - WPF-UI 4.3.0 and the app's 11 BAML files were decompiled.
    - The 14 WPF-UI resource keys the app uses exist, and the theme brushes are in all six theme dictionaries.
    - The pack URIs resolve, and the StaticResource keys and Style TargetTypes match.
    - Every WPF-UI member and enum value used exists.
    - The sliders' `ValueChanged` during `InitializeComponent` only reaches `PageEdits.Update`, which ignores edits before the first refresh.
- **Structure:**
  - **`SettingsUi/WindowFailure` (new):** a plain `MessageBox`, which needs no WPF-UI styles. It reads "Rimlight couldn't open its settings window. What went wrong: …", followed by:
    - the outer and innermost exception, up to 600 characters
    - the log as `%APPDATA%\Rimlight\logs\rimlight.log` (never the expanded path), and the `[Settings]` or `[Visualizer]` and `[UiTheme]` lines to look at
    - an offer to open the logs folder (Yes)

    It is posted with `BeginInvoke`, one box at a time. `Quiet` is for the tests.
  - **The settings and visualizer launchers** show it when the styles are missing, or when building or showing the window fails. The full exception is still logged. A window that failed to show is kept and shown again on the next try.
  - **`SettingsWindow`** runs each page's `SetActive`, `Refresh` and `ShowHotkeyStatus` through `RunPage`. The first failure per page is logged in full and reported; later ones get one line. A navigation failure is reported too.
  - **`UiTheme`** logs as "[UiTheme]" (it was "[Ui]") and writes "[UiTheme] Loaded the Fluent styles (… theme)." The launcher writes "[Settings] Created the settings window." once.
  - **The welcome window** is only the first-run welcome now. `ShowWelcome()` is private and always offers Settings.
  - **Test seams:**
    - `AboutPage.LoadIcon` uses the assembly-qualified pack URI, so it also resolves in the test host.
    - `AppController.Unstarted(SettingsService)` is an internal seam.
    - The App project gains `InternalsVisibleTo Rimlight.App.Tests`.
  - **`src/Rimlight.App.Tests` (new), 23 WPF smoke tests:**
    - **Host:** one STA thread with a plain WPF `Application` (OnExplicitShutdown) that runs `UiTheme.Load`. The app's `App` can't be used: its constructor would start the tray, the capture and the overlay.
    - **Theme (5):** the styles load, every theme key the source uses exists in Light, Dark and HighContrast, and the icon loads.
    - **Pages (8):**
      - Each of the six is built, refreshed, started, stopped and laid out at 572×520 and 372×360.
      - The GlowColorEditor.
      - Edits apply only after the first refresh.
    - **Settings window (4):**
      - Opened through the tray path; the X hides it, and reopening brings back the same window.
      - Every page in all three themes.
    - **Other windows (3):** the welcome with and without its button, and the visualizer.
    - **Failure message (3).**
    - **How they fail:**
      - On a dispatcher exception.
      - On any `[Settings]`, `[UiTheme]`, `[Visualizer]` or `[App]` line containing "fail", "can't", "unhandled", "exception" or "not applied".
      - Operations time out after 30 s and waits after 15 s.
    - **Where they run:**
      - In Rimlight.sln, which CI's "Full solution (Windows)" job tests. They are not in Rimlight.Core.slnf.
      - Off Windows the project builds with `IsTestProject=false`, because there is no WPF test host there.
    - **Docs:** CONTRIBUTING (layout, lanes), the doc 02 layout, the doc 09 ownership map, the lane-b labeler and a ci.yml comment.
- **Spec clarifications and deviations:**
  - A Settings failure now opens an error box instead of the welcome window. This replaces a K6/K7 behaviour.
  - The log area "[Ui]" is now "[UiTheme]".
  - No contract, default value or existing test changed.
- **Verified here (Linux, SDK 8.0.426):**
  - Release and Debug builds have 0 warnings, and ff5db1c builds on its own.
  - `dotnet test Rimlight.sln -c Release` passes.
  - Rimlight.App.Tests can't run here: off Windows it isn't a test project, and forcing it aborted the run with "Microsoft.WindowsDesktop.App not found".
  - **Windows CI, 2026-10-17:** a manual run of CI on 3a2fd04 ([run 38992796791](https://github.com/sussyswimmer/bordervisualizer/actions/runs/38992796791)) ran Rimlight.App.Tests in the "Full solution (Windows)" job: **23 passed, 0 skipped**. With the Fluent styles loaded as the app loads them, the settings window, all six pages, the colour editors, the welcome window and the visualizer window build and lay out without a logged failure on windows-latest. So the window itself can be built on Windows; what Maxwell saw still needs his rimlight.log or a description (a hidden tray icon in Windows 11's overflow, a build from the other #25–#30 stack, or something specific to his PC).
- **Left for later:**
  - **The cause on Maxwell's PC** is still unknown. It needs his rimlight.log lines (checklist item 1) or the first Windows CI run.
  - **The first CI run** may hit environment issues:
    - WPF windows on windows-latest
    - `UISettings.TextScaleFactor` on Windows Server ("[Settings] Reading the text size failed" would fail the tray-path test)
    - DWM, Mica and the dark title bar on Server 2022/2025
  - **Harmless lines:** if CI logs a harmless, environment-only line, loosen the failure rule on purpose and say why.
  - **The error box has no owner window.** If Windows refuses it foreground rights it can open behind other windows; check this on Maxwell's PC.
  - **High contrast:** the smoke run uses whichever palette WPF-UI picks on a machine that isn't in high contrast (HCWhite). Real high contrast still needs a manual check.
  - **If "no settings" meant no setting for the feel:** the motion now follows the music without one, and the edge is soft. A new `Settings` field would be a CONTRACT CHANGE, and live tuning of the dynamics is H-025.

### Windows checklist (round 1)

*Round 2 replaces items 2–8; see "Windows checklist (round 2)" under Feedback round 2.*

**Setup.** Quit the installed Rimlight, then on `claude/confident-mccarthy-gt8tif` run `dotnet run --project src/Rimlight.App -c Release -- --debug-visualizer` (the glow plus the audio visualizer, for item 8). Default settings (Music Sync, Thickness 6, Glow 45 %, Brightness 80 %), a dark wallpaper, system volume about 50 %. Give each song about 30 s: the first second only listens, and the light learns a song's loud parts over its first 15–25 s.

1. **Settings opens.**
   - Do: double-click the tray icon (a single click turns the glow on or off), or right-click > Settings…. Click each page, close with X and reopen, then switch Windows between dark and light mode.
   - Expect: "Rimlight Settings" with Appearance, Color, Motion, Displays, Behavior and About, each showing its controls; X only hides it, the same page comes back, and it follows the theme. rimlight.log (tray > Open logs folder) has "[UiTheme] Loaded the Fluent styles (… theme)." at start and "[Settings] Created the settings window." at the first open.
   - Bug: the small "Rimlight is running" window, an empty right side, nothing at all, or an error box.
   - If it fails: the box reads "Rimlight couldn't open its settings window. What went wrong: …". Click Yes (it opens the logs folder) and send the box's text with every rimlight.log line that starts with [Settings], [UiTheme] or [App] and contains "failed", "can't" or "Unhandled", plus the first "[App] Rimlight … started: Windows 10.0.NNNNN" line (or the whole file). The 5b387fc build you ran first writes "[Ui]" instead of "[UiTheme]"; its lines help too. If nothing appears and nothing like that is logged, look behind other windows and say what you saw.
2. **The edge.**
   - Expect: brightest at the very screen edge, fading smoothly inward: about a third as bright 40 px in and nearly gone by about 150 px on a 1080p screen (twice that on 4K). It bends round the corners, and over a white window it tints smoothly.
   - Bug: any line, bar or brighter stripe along an edge, an inner outline, or a band that stays flat and then drops. A quarter-circle arc or a 45° crease in a corner is known (pre-existing); report it if you see it at normal viewing distance.
3. **Thickness and Glow** (Settings > Appearance). Thickness no longer draws a solid line: it sets the width of a soft bright band at the edge.
   - Do: Thickness 0, 20 and 40, each at Glow 10 % and 45 %; drag Glow from 0 to 100; apply the Minimal preset.
   - Expect: Thickness 0 leaves only the glow, and more widens the bright part at the edge as a soft gradient (a wide band on a narrow glow may look like a soft shoulder). Glow goes smoothly from a soft hairline to a wide wash. Minimal is a thin soft edge, not a crisp 1 px line (intended). The preview card matches the screen; changes apply live and survive a restart.
   - Bug: a hard inner edge, a line where the band meets the glow, jumps while dragging, or a preview that clearly differs from the screen.
4. **Calm songs** (Noah Kahan or Hozier, for example "Stick Season" or "Cherry Wine").
   - Expect: the glow swells and eases with the phrases, with no bounce on the voice, strums, piano or bass guitar. The chorus is clearly brighter; the next quiet verse eases down over 2–3 s and stays dimmer. Where a full kit comes in, bouncing on its kicks is fine, and it calms down within about 5–8 s after the drums stop.
   - Bug: a sharp bounce on a sung word, a strum or a piano chord, a big bounce on a whisper, jittery brightness in a quiet verse, or a chorus no brighter than the verse.
   - Known limits: in the first song after launch a soft kit in a sparse first verse can still bounce, and a chorus coming in gives one bigger pulse.
5. **Rap and EDM.**
   - Expect: after the first 2 s, a strong, tight bounce on every 808 or kick. An EDM breakdown has no pulses, only a slow wave, and the first kicks of the drop hit hard at once. At Brightness 100 % the light swells inward on each beat and the outermost pixels get only slightly brighter.
   - Bug: clearly audible kicks with no bounce, a bounce that lags the kick, bounces on syllables between kicks or on pads in a breakdown, or a brighter rim flashing along the edges on the beat.
6. **Volume and song changes.**
   - Expect: a few notches of volume barely show, and the same song at any steady volume looks the same. A big change (50 % → 10 %, or back) shows for up to about 25 s (30 s if it lands at the start of a quiet verse) while kicks keep bouncing at least softly. A quiet song after a loud one starts dimmer and recovers over 25–40 s.
   - Bug: no recovery after 40 s, or a flash.
7. **Sensitivity** (Settings > Motion): at 0.5 the bounces are about half as big; at 2 they are full and the light is brighter.
8. **Visualizer.**
   - Expect: a "beat fired" tick on every bass onset; in a calm song, ticks with little or no Beat above them are syllables and plucks judged not to be kicks. In rap each tick has Beat near 1, and the BPM is within ±2 of the song's (EDM about 128, not 256). Moving the Bass band sliders (say to 20–200 or 30–250 Hz) doesn't make a calm song bounce or stop rap bouncing on every 808.
9. **Half scale and mixed DPI,** if you have them. On battery with "Save power" the look is the same at half resolution, with no rim on the beat, and Thickness 1 is still faintly visible. A 100 % monitor next to a 150 % or 200 % one shows the glow proportionally the same on both.
10. **WPF smoke tests.** Quit Rimlight (the build can't overwrite a running copy's files), then run `dotnet test src/Rimlight.App.Tests -c Release`: it opens every window and settings page. Expect 23 passed; windows flash briefly and one takes focus. A failure's message lists the logged failure lines. CI's "Full solution (Windows)" job runs the same tests.

**Reporting a dynamics issue.** The new constants are internal, so "Copy params as JSON" can't move them (H-025). Send a screen recording with sound (the Snipping Tool's Record, or OBS) that shows the screen edges and ideally the visualizer; the song, artist and timestamp; the system volume and whether it was the first song after launch; and what you expected: a wave, a soft pulse or a hard bounce.

## Feedback round 2 (Maxwell's second Windows run)

Maxwell ran round 1 (6ac553b, this branch) on Windows, after quitting the old copy. He said it "still feels wrong" on all four points, and answered the follow-up questions:
- Calm songs (Noah Kahan, Hozier) are still sharp.
- The bounces still aren't proportional to the song.
- Rap and EDM: "Bounces too small", "Late / off the beat" and "Misses kicks".
- The edge: a "Thin bright rim", a thin bright line right at the screen edge with the glow fading in behind it.

Round 1 had been tuned on procedural tracks only. Round 2 built a test bed of 15 real Creative Commons recordings, with reference annotations that share no code with Rimlight (`tools/corpus/`). It measured each complaint there and retuned on it.

- **Merged on `claude/confident-mccarthy-gt8tif`:**
  - Dynamics, with the replay harness `wav-analyze --engine`: b368ec6 (`wip/r2-corpus` a767bca, `wip/r2-dyn`, `wip/r2-dyn-fix`).
  - The edge: 15d918f (`wip/r2-rim`, `wip/r2-rim-fix`).
  - Record 2 min: 698786a (`wip/r2-diag`, `wip/r2-diag-fix`).
  - Reviews: the dynamics and the edge each had two, Record 2 min one. Each part then had a fix round before merging.
  - `tools/corpus/` came with the docs pass: the track list and the fetch, annotation, replay and metrics scripts, with no audio.
- **Merged state, checked in the docs pass:**
  - `dotnet build Rimlight.sln`, Release and Debug from a clean tree: 0 warnings.
  - `dotnet test Rimlight.sln -c Release` passes Rimlight.Tests 873, Rimlight.Platform.Tests 413 and Rimlight.IconGen.Tests 89. Rimlight.App.Tests runs on Windows CI.
  - Replaying the whole corpus on the merged build gives exactly the reviewed dynamics branch's numbers, on every class measure.
- **Not changed:** no file under `Contracts/`, and no `AudioTuning` or `Settings` default. Nothing has run on Windows yet; the checklist is at the end of this section.

**Where it stands.** Measured on the corpus with `tools/corpus/metrics.py`, in frame times without display latency.
- **Hit:** a pulse of 0.5 or more within −20..+80 ms of an annotated kick.
- **Bounce depth:** how much brighter the drawn light gets on a kick.

| Measure | Before round 1 (5b387fc) | Round 1 (6ac553b) | Round 2 |
|---|---|---|---|
| **Calm, no drums:** pulses ≥ 0.2 a minute | 52 | 0 | 0 |
| **Calm, no drums:** p95 brightness slope, per second | 1.40 | 0.16 | 0.07 |
| **Calm, no drums:** brightness against the 3 s loudness, r | 0.24 | 0.57 | 0.78 |
| **Calm with a folk or rock kit:** bounce depth | 34 % | 12 % | 9 % |
| **Calm with a folk or rock kit:** brightness against loudness, r | 0.02 | 0.32 | 0.77 |
| **Rap / EDM:** bounce depth | 28 / 26 % | 18 / 18 % | 36 / 38 % |
| **Rap / EDM:** kick to pulse peak, median (p90), ms | 27 (41) / 28 (40) | 46 (59) / 53 (66) | 24 (37) / 28 (39) |
| **Rap / EDM:** hit rate | 72 / 69 % | 55 / 57 % | 74 / 79 % |
| **Rap / EDM:** false pulses ≥ 0.3 a minute | 17.0 / 22.9 | 2.8 / 7.3 | 15.5 / 14.5 |
| **Edge** at the defaults, 1080p: fall over the outermost 4 px (in L*) | a solid line | 13.4 % (10.6 %) | 2.4 % (1.8 %) |

**Still short of the brief.** Details are under Dynamics, "Known limits".
- Rap and EDM hit 74 % and 79 % of kicks, against a 90 % target. Most misses are kicks the detector never fires on, and a song's first groove needs two or three strong kicks.
- Rap and EDM have about 15 false pulses a minute. About half land on other onsets (snares, syllables), and most of the rest on 808 and bass hits that the annotation doesn't count as kicks.
- Pulse size follows the moment (the groove's weight and the loudness), not each kick's own strength.
- In rap, EDM and calm songs with drums, loud parts are only 1.11–1.25× as bright as quiet ones, against a 1.4 target (pop reaches 1.40).

**Decisions for Maxwell** (HANDOFF H-028): how big a folk or rock kit's pulse is, the dip between kicks, and the strong-kick threshold.

### Real-music corpus and replay harness

Branch `wip/r2-corpus` (a767bca), on 6ac553b. Effort: L. Merged with the dynamics (b368ec6). Feedback round 2: Maxwell tested 6ac553b on Windows and said every point still feels wrong. Round 1 was tuned on procedural tracks only, so this round builds a real-music test bed and measures his complaints on it.

#### Corpus

15 Creative Commons tracks, described in `tools/corpus/manifest.json` (id, title, artist, source URL, download URL, licence URL, class, duration, notes). No audio, and nothing derived from it, is in the repository: `tools/corpus/fetch.py` downloads and decodes the tracks into a folder outside it (added in the docs pass, with the scripts below and a README).

- **Sources:** archive.org netlabel and community items, starfrosch "Most Wanted", and ccMixter (its downloads need a Referer header).
- **Licences:** confirmed from each source's own API (archive.org metadata `licenseurl`, ccMixter `license_url`). They are CC BY 3.0 and 4.0, BY-NC 3.0, BY-NC-SA 3.0 and BY-NC-ND 3.0, all used only for private offline analysis.
- **Format:** 48 kHz mono 16-bit WAV (channels averaged, soxr resampling), the first 240 s.
- **Selection:** about 70 candidates were screened with low-band HPSS contact sheets (0-300 Hz percussive and harmonic parts over time).

| Class | Count | Tracks |
|---|---|---|
| Calm | 4 | David Rovics "Sugihara" (live, voice and strummed guitar); Derrick Hart "Rest Well"; Josh Woodward "Memorized" and "Only Whispering" |
| Calm-then-drums | 2 | Derek Clegg "On My Own" (kick from 55 s); Josh Woodward "Private Hurricane" (kit from 108 s, full band from 150 s) |
| Rap | 4 | Robbero "Ballin" (trap, 808 sections); Donnie Ozone "Clean"; starfrosch feat. Kcentric; starfrosch feat. Kojo Flint (808) |
| EDM | 2 | Awkward_tom "Breakers Delight" (breakdown 48-66 s, drop 66 s, build and drop at about 158 s); ScOmBer "POWER UP" (breakdown and drop) |
| Pop and rock | 2 | Josh Woodward "Let It In"; Derek Clegg "The River" |
| Piano | 1 | Fabrizio Paterlini "Viandanze" |

#### Oracle: `tools/corpus/oracle.py`, one `<id>.oracle.json` per track

Independent of Rimlight's code: librosa 1.0.0, scipy and pyloudnorm, pinned in `tools/corpus/requirements.txt`.

- **Kicks:**
  - **Detection:** HPSS (margin 3) on an 11.025 kHz STFT with a 2.9 ms hop. The percussive part is summed over 30-130 Hz and its log-level flux is peak-picked.
  - **Every class:** a candidate is kept when it rises at least 6 dB in the percussive part and at least 1.5 dB in the whole band (so the percussive flicker of a held 808 doesn't count). It must also be within 20 dB of the band's 95th percentile, and its low-passed envelope peak within 12 dB of the strongest kick within 3 s.
  - **Calm, calm-then-drums and piano** add three tests: a low share of the percussive rise, a 30-65 Hz sub-band test, and a decay test (a kick dies away within 250 ms, a low guitar or piano note rings on).
  - **Pop** adds the share and sub tests, against bass guitar notes and snare bodies.
  - **Timing:** in the time domain on the 48 kHz signal (zero-phase low-pass and Hilbert envelope); the onset is where the envelope first rises 20 % of the way to its peak. STFT frames are centred, so the frame time alone would lead the attack by up to half a window.
  - The module docstring gives every threshold and the measurements behind it.
- **Beats and tempo:** librosa `beat_track` at its defaults.
- **Loudness:** BS.1770 K-weighted, momentary (400 ms) and short-term (3 s) LUFS every 50 ms, and integrated loudness.
- **Broadband onsets:** all other onsets, labelled percussive or tonal.
- **Sections:** quiet, mid or loud, against the track's 95th-percentile short-term loudness (loud within 4 dB, quiet 10 dB or more under).

**Validation.** Every criterion came from inspecting spectrograms. `plots/oracle_check_*.png` covers one or more tracks per class: 0-300 Hz windows with the kicks marked, plus the inter-kick-interval histogram. I looked at each one.
- **Rap and EDM:** every mark sits on a visible kick or 808 attack. The histograms are crisp: trap kicks at 1, 2 and 3 beats, the EDM kick on every beat.
- **Calm-then-drums:** the kit's kicks are marked from where they enter.
- **Calm:** 0, 0, 1 and 10 kicks. Sugihara's 10 are sporadic low thumps in a live solo set. Earlier versions marked a detuned guitar's 60 Hz notes, which the decay test removed.
- **Piano:** no kicks.
- **Against librosa's tempo (`oracle_validation.md`):**
  - For 7 of the 10 drum tracks, 82-98 % of inter-kick intervals lie on whole or half beats.
  - The other three: rap-clean 57 % (librosa's tempo is wrong; the track is about 68 BPM), pop-letitin 60 % (triplet feel), rap-kojo 69 % (808 pattern).
  - 87-94 % of kicks lie within 70 ms of a beat or half-beat (rap-clean 45 %).
- **Known limits:**
  - Pop and calm-then-drums are marked for precision over recall (letitin: about two thirds of its kicks).
  - A ringing low hit in hurricane's calm part is left unmarked.
  - So false-pulse counts in those classes are upper bounds.

#### Harness: `wav-analyze --engine` (committed)

`dotnet run -c Release --project tools/wav-analyze -- song.wav --engine [--fps 60] [--packet-ms 10] [--jitter 0.1] [--brightness 0.8] [--sensitivity 1] [--tuning t.json]`

- **What it runs:** Bench's `FramePipeline`, the copy of `MusicGlowSource.NextFrame` that `BenchMirrorTests` keeps honest. That is the analyzer, the palette blender and gradient, and the light engine, all through `CoreFactory`. Audio arrives in WASAPI-like 10 ms packets at 60 fps with ±10 % frame jitter.
- **What it writes:** `song.engine.csv`, one row per frame:
  - time, samples, level, bass, beat, isSilent, beatCount, fired, flux, threshold and bpm;
  - intensity, pulse, spread, visibility and phase (the LightState);
  - position, loudnessDb, referenceDb, punch, drive, kickLike, grade and levelTarget.
- **Internal values** are read by reflection, one bound delegate each and no boxing, so the same tool replays builds from before or after a change to Core's internals; a value a build doesn't have is left empty. No Contracts change. Bench gives `wav-analyze` InternalsVisibleTo for `FramePipeline`.
- **Also:** it prints a one-line summary of the light, and still writes the analyzer's PNG plot.
- **Test:** `WavAnalyzeTests.EngineModeReplaysTheRenderThreadAndWritesTheLight` checks that:
  - the analyzer columns equal an analyzer-only run on the same feed;
  - Intensity = Brightness × (0.35 + 0.65 Level) and Pulse = Beat;
  - samples come in whole 10 ms packets;
  - every internal column is filled, so a rename in Core can't silently empty them.
- **Docs:** CONTRIBUTING explains how to replay a real song, and `.gitignore` now keeps `*.wav`, `*.mp3`, `*.flac`, `*.ogg`, `*.m4a` and `*.engine.csv` out of the repository.
- **Baselines:** the harness was copied into detached worktrees at 5b387fc and 6ac553b (Bench and the tools are identical at both) and run with `tools/corpus/run_harness.sh`. Both worktrees were removed afterwards.

#### Baseline: `tools/corpus/metrics.py`

Frame times only; on Windows add 1-2 frames.
- **Hit:** a pulse of 0.5 or more peaking within [-20, +80] ms of an oracle kick.
- **Bounce:** how much brighter the drawn light, SoftClip(Intensity × (1 + 0.25·Pulse)), gets on a kick.

| Class | Build | Detected | Hit | Peak latency, median / p90 | Bounce | False pulses ≥ 0.3 per min | Pulses ≥ 0.2 per min | Variance above 3 Hz (Intensity / drawn) | r(Intensity, loudness) |
|---|---|---|---|---|---|---|---|---|---|
| Calm | 5b387fc | – | – | – | – | 51.7 | 52.3 | 4.4 % / 10.4 % | 0.24 |
| Calm | 6ac553b | – | – | – | – | 0.0 | 0.0 | 0.2 % / 0.2 % | 0.57 |
| Calm-then-drums | 5b387fc | 94 % | 94 % | 27 / 35 ms | 34 % | 50.9 | 66.2 | 3.0 % / 12.1 % | 0.02 |
| Calm-then-drums | 6ac553b | 94 % | 32 % | 39 / 45 ms | 12 % | 0.4 | 6.7 | 1.0 % / 4.6 % | 0.32 |
| Rap | 5b387fc | 72 % | 72 % | 27 / 41 ms | 28 % | 17.0 | 72.0 | 7.2 % / 17.4 % | 0.26 |
| Rap | 6ac553b | 72 % | 55 % | 46 / 59 ms | 18 % | 2.8 | 48.8 | 4.8 % / 16.9 % | 0.49 |
| EDM | 5b387fc | 69 % | 69 % | 28 / 40 ms | 26 % | 22.9 | 105.8 | 10.4 % / 25.8 % | 0.41 |
| EDM | 6ac553b | 69 % | 57 % | 53 / 66 ms | 18 % | 7.3 | 79.1 | 6.9 % / 23.5 % | 0.57 |
| Pop | 5b387fc | 96 % | 96 % | 28 / 38 ms | 30 % | 42.7 | 98.4 | 5.3 % / 16.5 % | 0.41 |
| Pop | 6ac553b | 96 % | 38 % | 45 / 53 ms | 12 % | 4.2 | 31.6 | 2.6 % / 10.7 % | 0.65 |
| Piano | 5b387fc | – | – | – | – | 46.1 | 46.1 | 1.8 % / 5.9 % | 0.28 |
| Piano | 6ac553b | – | – | – | – | 0.0 | 0.0 | 0.1 % / 0.1 % | 0.63 |

The beat fires 21-29 ms after the kick in both builds. The per-track tables and a table of why each missed kick is missed stayed in the session's scratch files; `metrics.py` regenerates them.

#### Diagnosis, by complaint

1. **Calm songs sharp: not reproduced as motion.**
   - At 6ac553b drumless calm music has no pulses, 0.1-0.3 % of its variance above 3 Hz, and p95 |dI/dt| of 0.11-0.20 per second.
   - The light is nearly static and bright instead of a wave: Intensity's 10th to 90th percentile is 0.57-0.74 against a maximum of 0.80. Causes: Level is pinned at full within about 5 dB of the song's loud parts (LevelKnee 0.8, AudioAnalyzer.cs:255), and there is a floor of 0.35 × Brightness (LightEngine.cs:155).
   - So "sharp" is most likely the hard, constant glow, the edge line (5), and a folk kit bouncing at full size (2).
2. **Not proportional: reproduced.**
   - **Pulse size runs against loudness:** r = -0.11 to -0.47 on 7 of the 8 drum tracks with enough hits (edm-breakers +0.32). Kick-likeness (BeatCharacter.KickLike, :242-246) measures a kick against the rest of the mix.
   - **hurricane:** its sparse kick section bounces at full size, and Level also steps on every kick because punch switches it to the 30 ms attack. Its loud full-band ending doesn't bounce at all.
   - **pop-letitin:** a full rock kit is graded "no drums" (kick-likeness and punch 0 on 91-95 % of its kicks, hit rate 2 %).
   - **Re-entries start silent:** after any break of 4 s or more the first 2-4 kicks give 0. EDM drops go 0, 0.30, 0.73, 1. Cause: the grade uses the punch from before the onset (BeatCharacter.cs:114, 216; :54-59, :231).
3. **Rap and EDM: reproduced on all three counts.**
   - **Missed, detection (both builds):** 11-38 % of kicks stay below the threshold, reaching 60-90 % of it. The threshold there is about twice mean + 1.5σ, so the median guard sets it (BeatDetector.MedianOnsetRatio = 10, :24, :109-110). Another 3-14 % fall inside the 180 ms refractory time; that default is a frozen contract value, so a fix must be an internal exception.
   - **Missed, grading (round 1):** kicks over an 808 grade near 0 (kojo: median kick-likeness 0.23, grade 0.08; the rise is measured at BeatCharacter.cs:120-126).
   - **Small:** a bounce is 18 % brighter, 28 % before round 1. A full pulse is capped near +25 %: Intensity is 0.70-0.76 in a groove, the beat multiplies by 1 + 0.25·Pulse, and the soft clip starts at 0.9 (GlowProfile.cs:44-53, 174-185).
   - **Late:** the pulse peaks 46-53 ms after the kick, 27-28 ms before round 1. The beat itself fires at 21-29 ms, near the floor. The rest is the grade growing over the 35 ms onset window (BeatCharacter :171-179, :212-223). On screen that is about 65-85 ms.
   - **False pulses:** fixed by round 1 (17-52 a minute down to 0-7).
4. **A big bounce on a whisper:** at 6ac553b no pulse of 0.5 or more lands in a quiet section. What remains is the inverse proportionality in 2.
5. **Edge line: reproduced from the profile math** (`rim_profile.py`, rendering, outside the harness).
   - At the defaults (1080p, Thickness 6, Glow 0.45), the outermost pixels read 179, 171, 163, 155, 149, 143, 138, against 154, 150, 146, 142, 139, 135, 132 with Thickness 0.
   - So the band adds 25 code values within 6 px and falls 8.7 per pixel there, against 3.5-4 for the glow alone. The glow alone is also brightest and steepest exactly at the edge.
   - `CoreThicknessDip` 6 is a frozen default, so the renderer must reinterpret it internally.

**Plots (looked at):**
- `plots/track_{calm-memorized,rap-ballin,rap-kojo,edm-breakers,drums-hurricane,pop-letitin}.png`: loudness and sections, Intensity, drawn light and Pulse for both builds, oracle kicks, a whole-track view and a 12-20 s zoom.
- `plots/oracle_check_*.png`.

**Caveats:**
- Netlabel and ccMixter recordings: the rap is closer to underground hip-hop than to chart trap.
- MP3 sources.
- The frame-time model doesn't include Windows display latency.
- The bounce measure leaves out the 35 % spread push.

### Dynamics

#### First pass [L]

*The review fix below supersedes parts of this pass. Its re-trigger and supersede exceptions inside the refractory time are gone, the groove's weight now keeps folk and rock kits to a soft lift, and the fix's tables replace the ones here.*

Commits f44d35d and 1515662 on `wip/r2-dyn` (branched from a767bca; wip/r2-corpus was already merged). Measured with `wav-analyze --engine`: Bench's FramePipeline at 60 fps ±10 % with 10 ms packets, default settings, Brightness 0.8. Times are frame times; display latency is not included.

Held-out tracks are marked *. Tuned on: calm-memorized, calm-whispering, drums-hurricane, rap-ballin, rap-kojo, edm-breakers and pop-letitin. Caveat: some held-out tracks were looked at during early feature exploration and in one sweep of the calm loudness range. No constant was picked on them.

**Root causes, found in 6ac553b's Core:**
- **Calm light static.**
  - Level was min(1, position / LevelKnee 0.8) × (1 − 0.6 × (1 − auto-gain)) (AudioAnalyzer.cs:27, :255, :258), with RangeDb 24 (SongLoudness.cs:54).
  - The result: a calm song's Intensity stayed within 0.57–0.74 for 80 % of the song.
- **Late pulses.**
  - The grade grew over a 35 ms onset window of FFT frames (BeatCharacter.cs:52, :200, :216), so pulses peaked 46–53 ms after the kick, against 21–29 ms for the beat itself.
- **Missed and zero pulses.**
  - The median guard ×10 (BeatDetector.cs:24, :110) left 11–38 % of rap and EDM kicks under the threshold.
  - The 180 ms refractory time (:117) blocked 3–14 %.
  - Kick-likeness was measured against the current mix, so full choruses graded smaller.
  - The punch was taken from before the onset, so the first kicks after a break got 0, 0.3, 0.73, then 1.
- **Weak bounce.** Nothing dipped between kicks, so a full pulse lifted the drawn light only 12–18 %.

**Design (internal, behind the frozen contracts):**
- **KickEnvelope (new).** A time-domain kick band (Butterworth LP 100 Hz + HP 30 Hz, power over 4 ms) and the music above 150 Hz (0.25 s). It reports rise and prominence in dB.
- **BeatDetector.**
  - With prominence ≥ −4 dB, the median guard is 5× instead of 10×.
  - Inside the refractory time a beat may re-fire for a double kick (≥ 90 ms after, fresh attack, ≥ 3 dB of evidence).
  - Or it may fire for a kick right after a weaker onset (≥ 50 ms after, 1.3× the flux after a dip).
- **BeatCharacter (rewritten).**
  - Grade at the beat = Sensitivity × punch × evidence × size × loud.
    - Evidence: smoothstep −10..−5 dB of prominence, or 12..20 dB of rise, whichever is larger.
    - Size: amplitude against recent kicks, smoothstep 0.05..0.3, with a half-life of 4 s.
    - Loud: song position held 0.75 s, smoothstep 0.25..0.55, floor 0.3 × punch.
  - A frame later the FFT can raise the grade (prominence 3..9 dB, rise 14..22 dB).
  - An unmistakable kick (8..14 dB) grades itself without a groove.
  - The punch comes from strong confirmations (smoothstep 4.5..10.5 dB, count fading with τ = 3 s, groove smoothstep 1.1..1.8). Weak confirmations keep a groove going at a drum part's rate, up to its memory.
  - A beat that re-fires within 0.18 s counts once.
  - The punch holds 3 s, then releases with τ = 2.5 s.
  - The first second after near-silence is never graded.
- **AudioAnalyzer.**
  - Level is a two-stage envelope of the song position: 1 s / 1.6 s with no drums, 0.15 s / 0.8 s in a groove.
  - The pump is Level × (1 − 0.4 × punch × (1 − pump)), with τ = 0.3 s.
- **SongLoudness.** Range 18 dB.

**Per class (5b387fc / 6ac553b / now):**

| Class | Build | Kicks | Detected | Hit rate | Latency med / p90 ms | Pulse on hits mean / p10 | Bounce depth | False ≥0.3 /min (on nothing) | Pulses ≥0.2 /min | I var >3 Hz | p95 dI/dt | r(I, loudness) | Loud / low |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| calm | 5b387fc | 11 | 82 % | 82 % | 31 / 51 | 1.00 / 1.00 | 28 % | 51.7 (16.5) | 52.3 | 4.4 % | 1.06 | 0.24 | 1.24 |
| calm | 6ac553b | 11 | 82 % | 0 % | – | – | 1 % | 0.0 | 0.0 | 0.2 % | 0.16 | 0.57 | 1.46 |
| calm | now | 11 | 82 % | 0 % | – | – | 0 % | 0.0 | 0.0 | 0.0 % | 0.07 | 0.78 | 1.63 |
| calm-then-drums | 5b387fc | 114 | 94 % | 94 % | 27 / 35 | 1.00 / 1.00 | 34 % | 50.9 (23.6) | 66.2 | 3.0 % | 0.75 | 0.02 | 0.93 |
| calm-then-drums | 6ac553b | 114 | 94 % | 32 % | 39 / 45 | 0.87 / 0.73 | 12 % | 0.4 (0.0) | 6.7 | 1.0 % | 0.20 | 0.32 | 1.01 |
| calm-then-drums | now | 114 | 98 % | 78 % | 27 / 36 | 0.98 / 0.98 | 41 % | 9.0 (4.4) | 24.4 | 5.3 % | 0.40 | 0.43 | 1.13 |
| rap | 5b387fc | 1108 | 72 % | 72 % | 27 / 41 | 1.00 / 1.00 | 28 % | 17.0 (8.7) | 72.0 | 7.2 % | 1.04 | 0.26 | 1.00 |
| rap | 6ac553b | 1108 | 72 % | 55 % | 46 / 59 | 0.97 / 0.94 | 18 % | 2.8 (1.1) | 48.8 | 4.8 % | 0.53 | 0.49 | 1.05 |
| rap | now | 1108 | 88 % | 78 % | 25 / 37 | 0.98 / 0.93 | 38 % | 30.7 (18.0) | 94.9 | 14.0 % | 1.05 | 0.43 | 1.10 |
| edm | 5b387fc | 945 | 69 % | 69 % | 28 / 40 | 1.00 / 1.00 | 26 % | 22.9 (11.4) | 105.8 | 10.4 % | 1.20 | 0.41 | 1.16 |
| edm | 6ac553b | 945 | 69 % | 57 % | 53 / 66 | 0.95 / 0.80 | 18 % | 7.3 (2.1) | 79.1 | 6.9 % | 0.65 | 0.57 | 1.21 |
| edm | now | 945 | 89 % | 84 % | 28 / 40 | 1.00 / 1.00 | 37 % | 30.1 (16.7) | 131.6 | 23.1 % | 1.92 | 0.42 | 1.24 |
| pop | 5b387fc | 461 | 96 % | 96 % | 28 / 38 | 1.00 / 1.00 | 30 % | 42.7 (13.7) | 98.4 | 5.3 % | 1.07 | 0.41 | 1.20 |
| pop | 6ac553b | 461 | 96 % | 38 % | 45 / 53 | 0.88 / 0.68 | 12 % | 4.2 (0.3) | 31.6 | 2.6 % | 0.38 | 0.65 | 1.28 |
| pop | now | 461 | 97 % | 91 % | 28 / 37 | 0.97 / 0.92 | 44 % | 32.4 (11.1) | 88.4 | 13.5 % | 0.64 | 0.46 | 1.27 |
| piano | 5b387fc | 0 | – | – | – | – | – | 46.1 | 46.1 | 1.8 % | 0.77 | 0.28 | 1.09 |
| piano | 6ac553b | 0 | – | – | – | – | – | 0.0 | 0.0 | 0.1 % | 0.19 | 0.63 | 1.18 |
| piano | now | 0 | – | – | – | – | – | 0.0 | 0.0 | 0.0 % | 0.07 | 0.69 | 1.32 |

(5b387fc pulsed on every onset, calm songs included. That is why its hit rates are high and its false pulses and calm pulses are high too.)

**Kicks per track (5b387fc / 6ac553b / now):**

| Track | Build | Kicks | Detected | Hit rate | Latency med / p90 ms | Pulse on hits mean / p10 | Bounce depth | False ≥0.3 /min (on onset / on nothing) |
|---|---|---|---|---|---|---|---|---|
| drums-onmyown* | 5b387fc | 53 | 96 % | 96 % | 27 / 34 | 1.00 / 1.00 | 30 % | 45.9 (26.8 / 19.2) |
| drums-onmyown* | 6ac553b | 53 | 96 % | 11 % | 42 / 46 | 0.77 / 0.55 | 5 % | 0.0 |
| drums-onmyown* | now | 53 | 100 % | 81 % | 27 / 35 | 0.98 / 0.96 | 43 % | 7.3 (3.3 / 4.0) |
| drums-hurricane | 5b387fc | 61 | 92 % | 92 % | 28 / 36 | 1.00 / 1.00 | 38 % | 54.7 (27.7 / 27.0) |
| drums-hurricane | 6ac553b | 61 | 92 % | 51 % | 37 / 44 | 0.96 / 0.89 | 19 % | 0.8 (0.8 / 0.0) |
| drums-hurricane | now | 61 | 97 % | 75 % | 27 / 36 | 0.99 / 1.00 | 40 % | 10.3 (5.5 / 4.8) |
| rap-ballin | 5b387fc | 274 | 78 % | 78 % | 21 / 38 | 1.00 / 1.00 | 28 % | 8.1 (4.0 / 4.0) |
| rap-ballin | 6ac553b | 274 | 78 % | 64 % | 46 / 56 | 0.98 / 0.97 | 21 % | 2.3 (0.9 / 1.4) |
| rap-ballin | now | 274 | 89 % | 82 % | 14 / 30 | 0.99 / 1.00 | 44 % | 9.3 (5.2 / 4.0) |
| rap-clean* | 5b387fc | 279 | 54 % | 54 % | 29 / 43 | 1.00 / 1.00 | 27 % | 10.6 (5.5 / 5.0) |
| rap-clean* | 6ac553b | 279 | 54 % | 41 % | 47 / 60 | 0.97 / 0.92 | 16 % | 2.8 (1.5 / 1.3) |
| rap-clean* | now | 279 | 76 % | 61 % | 26 / 39 | 0.99 / 1.00 | 29 % | 14.1 (3.3 / 10.8) |
| rap-kcentric* | 5b387fc | 356 | 83 % | 83 % | 29 / 40 | 1.00 / 1.00 | 28 % | 26.3 (5.9 / 20.5) |
| rap-kcentric* | 6ac553b | 356 | 83 % | 78 % | 45 / 58 | 0.99 / 1.00 | 22 % | 5.2 (3.6 / 1.6) |
| rap-kcentric* | now | 356 | 97 % | 97 % | 29 / 41 | 1.00 / 1.00 | 42 % | 30.3 (5.2 / 25.0) |
| rap-kojo | 5b387fc | 199 | 68 % | 68 % | 29 / 43 | 1.00 / 1.00 | 28 % | 24.1 (17.0 / 7.1) |
| rap-kojo | 6ac553b | 199 | 68 % | 23 % | 48 / 62 | 0.92 / 0.83 | 10 % | 1.3 (1.0 / 0.3) |
| rap-kojo | now | 199 | 86 % | 63 % | 27 / 41 | 0.92 / 0.64 | 32 % | 66.6 (34.6 / 32.0) |
| edm-breakers | 5b387fc | 496 | 79 % | 79 % | 28 / 43 | 1.00 / 1.00 | 27 % | 21.0 (13.7 / 7.3) |
| edm-breakers | 6ac553b | 496 | 79 % | 68 % | 52 / 66 | 0.95 / 0.78 | 20 % | 5.1 (4.8 / 0.3) |
| edm-breakers | now | 496 | 94 % | 90 % | 28 / 40 | 1.00 / 1.00 | 38 % | 21.3 (13.2 / 8.1) |
| edm-powerup* | 5b387fc | 449 | 59 % | 59 % | 27 / 36 | 1.00 / 1.00 | 26 % | 24.7 (9.3 / 15.4) |
| edm-powerup* | 6ac553b | 449 | 59 % | 44 % | 53 / 66 | 0.96 / 0.82 | 17 % | 9.6 (5.5 / 4.0) |
| edm-powerup* | now | 449 | 84 % | 76 % | 28 / 41 | 1.00 / 1.00 | 36 % | 38.8 (13.6 / 25.2) |
| pop-letitin | 5b387fc | 202 | 91 % | 91 % | 29 / 41 | 1.00 / 1.00 | 26 % | 67.3 (41.8 / 25.5) |
| pop-letitin | 6ac553b | 202 | 91 % | 2 % | 47 / 56 | 0.87 / 0.68 | 2 % | 2.8 (2.8 / 0.0) |
| pop-letitin | now | 202 | 93 % | 82 % | 29 / 41 | 0.96 / 0.83 | 38 % | 56.2 (35.5 / 20.7) |
| pop-river* | 5b387fc | 259 | 100 % | 100 % | 27 / 34 | 1.00 / 1.00 | 33 % | 18.2 (16.1 / 2.0) |
| pop-river* | 6ac553b | 259 | 100 % | 66 % | 44 / 51 | 0.89 / 0.69 | 21 % | 5.5 (5.0 / 0.5) |
| pop-river* | now | 259 | 100 % | 98 % | 27 / 35 | 0.99 / 1.00 | 50 % | 8.6 (7.1 / 1.5) |

**Wave, smoothness and proportionality per track (5b387fc / 6ac553b / now):**

| Track | Pulses ≥0.2 /min | I var >3 Hz | p95 dI/dt /s | r(I, loudness) | Loud / low |
|---|---|---|---|---|---|
| calm-sugihara* | 54.2 / 0.0 / 0.0 | 3.8 / 0.2 / 0.0 % | 1.16 / 0.16 / 0.04 | −0.13 / 0.06 / 0.52 | – (mid) |
| calm-restwell* | 17.3 / 0.0 / 0.0 | 6.9 / 0.3 / 0.0 % | 0.81 / 0.11 / 0.03 | 0.62 / 0.90 / 0.89 | 1.47 / 1.94 / 2.21 |
| calm-memorized | 85.5 / 0.0 / 0.0 | 2.8 / 0.1 / 0.0 % | 0.96 / 0.18 / 0.10 | 0.03 / 0.55 / 0.87 | 1.00 / 1.11 / 1.28 |
| calm-whispering | 48.2 / 0.0 / 0.0 | 4.2 / 0.1 / 0.0 % | 1.30 / 0.20 / 0.09 | 0.45 / 0.76 / 0.82 | 1.27 / 1.31 / 1.41 |
| drums-onmyown* | 62.8 / 3.6 / 22.5 | 2.8 / 0.4 / 4.1 % | 0.61 / 0.18 / 0.39 | −0.05 / 0.21 / 0.35 | 0.83 / 0.92 / 1.03 |
| drums-hurricane | 68.8 / 9.1 / 26.0 | 3.2 / 1.5 / 6.6 % | 0.88 / 0.23 / 0.41 | 0.10 / 0.42 / 0.51 | 1.03 / 1.11 / 1.23 |
| rap-ballin | 70.0 / 58.7 / 78.9 | 6.9 / 4.5 / 15.6 % | 0.96 / 0.54 / 0.64 | 0.29 / 0.55 / 0.45 | 0.99 / 1.02 / 1.09 |
| rap-clean* | 48.2 / 33.5 / 58.5 | 4.0 / 2.0 / 9.0 % | 1.06 / 0.53 / 0.59 | 0.11 / 0.36 / 0.48 | 0.90 / 1.00 / 1.12 |
| rap-kcentric* | 122.3 / 97.9 / 142.8 | 7.2 / 6.0 / 20.0 % | 1.08 / 0.60 / 2.34 | 0.28 / 0.48 / 0.37 | – (mid) |
| rap-kojo | 58.7 / 17.3 / 108.2 | 10.8 / 6.5 / 11.2 % | 1.07 / 0.45 / 0.63 | 0.37 / 0.59 / 0.42 | 1.12 / 1.13 / 1.09 |
| edm-breakers | 120.6 / 94.0 / 136.6 | 11.3 / 6.9 / 22.2 % | 1.29 / 0.73 / 2.06 | 0.35 / 0.51 / 0.46 | 1.23 / 1.32 / 1.35 |
| edm-powerup* | 91.0 / 64.3 / 126.6 | 9.4 / 6.9 / 23.9 % | 1.11 / 0.57 / 1.79 | 0.48 / 0.63 / 0.39 | 1.09 / 1.10 / 1.13 (mid) |
| pop-letitin | 113.7 / 5.5 / 103.1 | 4.1 / 0.3 / 11.2 % | 1.10 / 0.23 / 0.66 | 0.43 / 0.65 / 0.42 | 1.13 / 1.16 / 1.15 |
| pop-river* | 83.2 / 57.7 / 73.6 | 6.4 / 4.9 / 15.8 % | 1.05 / 0.53 / 0.62 | 0.40 / 0.65 / 0.51 | 1.28 / 1.40 / 1.38 |
| piano-viandanze* | 46.1 / 0.0 / 0.0 | 1.8 / 0.1 / 0.0 % | 0.77 / 0.19 / 0.07 | 0.28 / 0.63 / 0.69 | 1.09 / 1.18 / 1.32 |

(The HF share and dI/dt rise on drum tracks because the light now dips between kicks. That is intended for drum music only.)

**Tuning set against held-out tracks:**
- **Hit rates.** Tuning set: hurricane 75, ballin 82, kojo 63, breakers 90, letitin 82 %. Held out: onmyown 81, clean 61, kcentric 97, powerup 76, river 98 %.
- **Calm r(I, loudness).** Tuning set: memorized 0.87, whispering 0.82. Held out: restwell 0.89, sugihara 0.52, piano 0.69.
- No sign of overfitting. The weakest tracks, clean and kojo, are on opposite sides of the split.

**Alternatives measured and rejected:**
- **A pump that keeps the average brightness.** Loud/low ratio: rap 1.23, EDM 1.37, pop 1.42. But the bounce depth fell to 24–34 %.
- **The same pump, deeper (0.55).** Bounce depth 28–39 % and ratios 1.19–1.39, but the verse would bounce harder than the hook.
- **Kick-likeness 3..9 dB.** Rap 80 %, kojo 76 %, but the bass guitar under the folk chorus started a groove (punch 0.78, 19 pulses).
- **A longer loudness hold, 0.5 → 0.75 s.** Neutral on the corpus; kept.

**Tests changed on purpose (reason in each comment):**
- **SongLoudnessTests:** expectations are written in terms of RangeDb.
- **MusicDynamicsTests:**
  - Measure uses a Hann window for the "above 3 Hz" share (the rectangular DFT read slow ramps as high frequencies).
  - SoftDrumsBounceSoftly became ASoftKitIsTheBeatAndBouncesWithTheMoment. No measure of the kick separates that kit from real rap and EDM kicks: their kick-band peaks against the song are −6.6, −7.3 and −6.5 dB.
  - ASmallVolumeNudgeLeavesTheLoudPartsAsBright became ASmallVolumeNudgeKeepsTheBounceAndSettles (the knee is gone).
  - AWhisperedVerse… stops 0.25 s before the chorus pickup. Its bound is now 0.55 for the swells, and the verse kicks are checked at the 0.3 floor.
  - The −10 dB volume step's minimum mean pulse went from 0.6 to 0.5 (measured 0.60).
  - New: InAGrooveThePulseLandsOnTheFrameItsBeatFires (61 of 61) and AGroovePumpsTheLightBetweenKicks (55 % on average, 41 % at least).
- **WavAnalyzeTests:** counts pulse peaks of 0.95 or more instead of exactly 1.0000 (measured 38 of 39 beats at 0.96 or more, 28 exactly 1).
- **BeatCharacterTests:** rewritten for the new design (25 → 29 tests).
- **New files:** KickEnvelopeTests (6) and BeatDetectorTests (8).

**Cost** (Bench on this machine, same machine for both):
- Analyzer one-step mean: 40–45 µs → 46–50 µs (0.27 → 0.30 % of a core).
- `frame --packet-ms 10`: 74 → 77 µs (0.44 → 0.47 %).
- Still 0 B per frame.

**Simplest-option choices, noted here per CLAUDE.md:**
- The loud hold covers 80 BPM.
- A beat that re-fires within the refractory time counts once.
- The first second after digital silence is never graded, even with a punch held over from the last song.

#### Review fix [L]

Branch `wip/r2-dyn-fix`, on `wip/r2-dyn` (1515662):
- cd3e2ad `fix(audio)`
- f5562f9 `fix(visualizer)`
- 078016e `docs(audio)`

It answers round 2's review: 1 blocker, 6 majors, 7 minors and 1 nit. The round-2 tables above use r2-dyn as "now"; the tables here supersede them.

Measured as before:
- `wav-analyze --engine` (Bench's FramePipeline), 60 fps ±10 %, 10 ms packets.
- Default settings, Brightness 0.8.
- Frame times, without display latency.
- The same 15 recordings; held-out tracks are marked *. No audio or corpus file is in the repository.

**Changes.** All are Core internals behind the frozen contracts; nothing under `Contracts/` changed.
- **Noise (majors 2 and 10).**
  - BeatDetector's looser median guard is ×5 instead of ×10 when KickEnvelope's prominence is ≥ −4 dB. It now also needs music: `OnsetContrast` ≥ `MusicContrast` 0.23.
  - `OnsetContrast` is the coefficient of variation of the relative spectral flux from 150 Hz to 12 kHz over 4 s. Noises read at most 0.19; the corpus's music reads 0.23 or more 93–100 % of the time.
  - BeatCharacter lets a beat confirm a groove only if it passed doc 03's own threshold (`BeatResult.Strict`) or came in music. It grades nothing instantly outside music.
- **Beat spacing (majors 11 and 4).**
  - Round 2's re-trigger and supersede exceptions are gone. `BeatRefractorySeconds` is a hard minimum again, as its XML doc, the slider and CORE-API say.
  - `BeatCharacter.EventSeconds` (0.18 s) only matters with a spacing tuned under it, where a flam counts once toward the groove's rate.
- **Light grooves (the blocker).**
  - `BeatCharacter.Weight` = smoothstep(−7 dB, 0 dB) of the kick band's mean power (KickEnvelope, 30–100 Hz) over the music above 150 Hz.
  - Rap and EDM grooves read +2 to +5 dB; folk and rock kits read −8 to −12 dB.
  - It starts as a 1.5 s average from zero. Once the punch reaches 0.5 it switches to an 8 s average that moves only while the punch is ≥ 0.5, so a breakdown keeps it. 1 s of near-silence clears it.
  - The grade is × (`LightGain` 0.5 + 0.5 × Weight). The pump depth and Level's groove speed scale by Punch × Weight.
- **Drops and re-entries (major 5).**
  - The groove's memory holds 30 s (`MemoryHoldSeconds`), then fades with τ 4 s.
  - It comes back on the first trusted kick confirmation, or on a kick in music with ≥ 18 dB of rise and ≥ 0 dB of prominence. That can happen as the kick fires or at the FFT re-grade a frame later (`Resume`).
  - The pump's depth is × engaged². Engaged is the groove's own recent pulse at Sensitivity 1, held 1.5 s and released over 0.5 s. So the light never dims before a groove's first pulse.
- **Off-beat flashes (major 4).** For `MaskSeconds` (0.3 s) after a pulse ≥ 0.5 at Sensitivity 1, a beat needs a fresh attack to pulse: × smoothstep(10, 20 dB) of KickEnvelope's rise.
- **Loudness (major 3, minor 7).**
  - The groove's floor (`LoudFloor` 0.3 × punch) fades with the held position, × smoothstep(0.1, 0.3). It is whole 12.6 dB under the loud parts and gone at 16.2 dB.
  - The held position is now the largest over the last 0.75 s (15 blocks of 50 ms).
  - Round 2's hold renewed only on a new maximum. In a steady groove a kick a hair under the held one let it run out, so every other kick was graded from the dip before it.
- **Sensitivity.** The pump, the engagement and the mask read `RawBeat`, the grade at Sensitivity 1. Sensitivity now scales Level and the pulses exactly as doc 03 says, and no longer changes the pump.
- **Diagnostics (minor 13).**
  - `BeatResult.Threshold` is the threshold applied for the step, and `BeatResult.Strict` says whether doc 03's own threshold passed. ThresholdHistory and the plot use it.
  - `wav-analyze --engine` adds prominenceDb, riseDb, onsetContrast, weight, pumpDip and memory (30 columns).
- **Smaller.**
  - `BandAnalyzer.UpperBins` is new.
  - KickEnvelope reports each call's mean band powers.
  - `AudioFrontEnd.LevelTarget` is documented as a diagnostic; only wav-analyze reads it.
  - The weight's fast average starts from zero.
  - `BeatStats.Tempo` no longer throws when no interval is near the median (wav-analyze crashed on some noises).
- **App (minor 14, f5562f9).**
  - The Level attack and release tooltips give the code's factors: 5× and 3.2× in a heavy groove, about 33× and 6.4× with no drums or a light kit.
  - The three Level mix sliders say they don't change the light.
  - Peak half-life, floor rise and minimum range say they shape Bass only.
- **Docs (minor 9 and nit 15, 078016e).** CORE-API §4.2 and doc 03 now say:
  - 15 recordings, tuned on 7 and checked on 8, not blindly.
  - The onset contrast and the hard spacing.
  - The weight, memory, mask, fading floor, sliding hold and engaged pump.
  - Results per class.

**Per class.** Builds: 5b387fc (before round 1), 6ac553b (round 1), r2-dyn (1515662) and now (078016e).
- Hit: a pulse ≥ 0.5 within −20..+80 ms of an annotated kick.
- Bounce depth: how much brighter the drawn light, SoftClip(I × (1 + 0.25·Pulse)), gets on a kick.
- In brackets: false pulses that land on an annotated non-kick onset.

| Class | Build | Kicks | Detected | Hit rate | Latency med / p90 ms | Pulse on hits mean / p10 | Pulse on all kicks | Bounce depth | False ≥0.3 /min (on onsets) | Pulses ≥0.2 /min | Shown var. >3 Hz | p95 dShown/dt /s | r(I, loudness) | Loud / low |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| calm | 5b387fc | 11 | 82 % | 82 % | 31 / 51 | 1.00 / 1.00 | 0.82 | 28 % | 51.7 (35.2) | 52.3 | 10.4 % | 1.40 | 0.24 | 1.24 |
| calm | 6ac553b | 11 | 82 % | 0 % | – | – | 0.00 | 1 % | 0.0 | 0.0 | 0.2 % | 0.16 | 0.57 | 1.46 |
| calm | r2-dyn | 11 | 82 % | 0 % | – | – | 0.00 | 0 % | 0.0 | 0.0 | 0.0 % | 0.07 | 0.78 | 1.63 |
| calm | now | 11 | 82 % | 0 % | – | – | 0.00 | 0 % | 0.0 | 0.0 | 0.0 % | 0.07 | 0.78 | 1.63 |
| calm-then-drums | 5b387fc | 114 | 94 % | 94 % | 27 / 35 | 1.00 / 1.00 | 0.94 | 34 % | 50.9 (27.3) | 66.2 | 12.1 % | 1.28 | 0.02 | 0.93 |
| calm-then-drums | 6ac553b | 114 | 94 % | 32 % | 39 / 45 | 0.87 / 0.73 | 0.33 | 12 % | 0.4 (0.4) | 6.7 | 4.6 % | 0.28 | 0.32 | 1.01 |
| calm-then-drums | r2-dyn | 114 | 98 % | 78 % | 27 / 36 | 0.98 / 0.98 | 0.80 | 41 % | 9.0 (4.6) | 24.4 | 13.0 % | 0.91 | 0.43 | 1.13 |
| calm-then-drums | now | 114 | 94 % | 47 % | 27 / 46 | 0.50 / 0.50 | 0.37 | 9 % | 11.4 (5.1) | 24.9 | 2.9 % | 0.31 | 0.77 | 1.25 |
| rap | 5b387fc | 1108 | 72 % | 72 % | 27 / 41 | 1.00 / 1.00 | 0.72 | 28 % | 17.0 (8.4) | 72.0 | 17.4 % | 1.86 | 0.26 | 1.00 |
| rap | 6ac553b | 1108 | 72 % | 55 % | 46 / 59 | 0.97 / 0.94 | 0.56 | 18 % | 2.8 (1.7) | 48.8 | 16.9 % | 1.23 | 0.49 | 1.05 |
| rap | r2-dyn | 1108 | 88 % | 78 % | 25 / 37 | 0.98 / 0.93 | 0.78 | 38 % | 30.7 (12.7) | 94.9 | 22.7 % | 2.64 | 0.43 | 1.10 |
| rap | now | 1108 | 83 % | 74 % | 24 / 37 | 0.99 / 0.98 | 0.74 | 36 % | 15.5 (8.0) | 74.1 | 23.6 % | 2.10 | 0.40 | 1.11 |
| edm | 5b387fc | 945 | 69 % | 69 % | 28 / 40 | 1.00 / 1.00 | 0.69 | 26 % | 22.9 (11.5) | 105.8 | 25.8 % | 2.57 | 0.41 | 1.16 |
| edm | 6ac553b | 945 | 69 % | 57 % | 53 / 66 | 0.95 / 0.80 | 0.55 | 18 % | 7.3 (5.2) | 79.1 | 23.5 % | 1.69 | 0.57 | 1.21 |
| edm | r2-dyn | 945 | 89 % | 84 % | 28 / 40 | 1.00 / 1.00 | 0.84 | 37 % | 30.1 (13.4) | 131.6 | 32.3 % | 4.32 | 0.42 | 1.24 |
| edm | now | 945 | 86 % | 79 % | 28 / 39 | 1.00 / 1.00 | 0.79 | 38 % | 14.5 (5.6) | 110.0 | 32.4 % | 2.70 | 0.37 | 1.20 |
| pop | 5b387fc | 461 | 96 % | 96 % | 28 / 38 | 1.00 / 1.00 | 0.96 | 30 % | 42.7 (29.0) | 98.4 | 16.5 % | 2.23 | 0.41 | 1.20 |
| pop | 6ac553b | 461 | 96 % | 38 % | 45 / 53 | 0.88 / 0.68 | 0.38 | 12 % | 4.2 (3.9) | 31.6 | 10.7 % | 0.68 | 0.65 | 1.28 |
| pop | r2-dyn | 461 | 97 % | 91 % | 28 / 37 | 0.97 / 0.92 | 0.90 | 44 % | 32.4 (21.3) | 88.4 | 24.1 % | 1.83 | 0.46 | 1.27 |
| pop | now | 461 | 96 % | 84 % | 28 / 37 | 0.50 / 0.50 | 0.45 | 11 % | 27.2 (18.8) | 82.8 | 10.4 % | 0.68 | 0.81 | 1.40 |
| piano | 5b387fc | 0 | – | – | – | – | – | – | 46.1 (38.6) | 46.1 | 5.9 % | 1.13 | 0.28 | 1.09 |
| piano | 6ac553b | 0 | – | – | – | – | – | – | 0.0 | 0.0 | 0.1 % | 0.19 | 0.63 | 1.18 |
| piano | r2-dyn | 0 | – | – | – | – | – | – | 0.0 | 0.0 | 0.0 % | 0.07 | 0.69 | 1.32 |
| piano | now | 0 | – | – | – | – | – | – | 0.0 | 0.0 | 0.0 % | 0.07 | 0.69 | 1.32 |

Light kits (calm-then-drums and pop) now top out at `LightGain` 0.5 on purpose. The review's blocker counts pop-river, a folk-rock song, as one of them.
- Their hit rate (≥ 0.5) therefore counts only kicks at the light kit's full size, and it swings with tiny changes: 42–72 % for calm-then-drums across the robustness runs.
- Their real measures are pulse on all kicks (0.37–0.45) and bounce depth (9–11 %, at most half of rap's 36 %).
- This overrides the brief's pop target of a mean pulse ≥ 0.8 on hits.

**Brief's targets, now.**

| Target | Now | Met |
|---|---|---|
| Rap / EDM latency ≤ 30 ms median, ≤ 50 ms p90 | 24 / 28 ms median, 37 / 39 ms p90 | yes |
| Rap / EDM pulse on hits mean ≥ 0.8, p10 ≥ 0.5 | mean 0.99 / 1.00, p10 0.98 / 1.00 | yes |
| Rap / EDM hit rate ≥ 90 % | 74 / 79 % | no |
| False pulses: a few a minute | 15.5 / 14.5 a minute | no |
| Calm songs: no pulses, smooth, r(I, loudness) ≥ 0.6 | no pulses; p95 slope 0.07 per second (round 1: 0.16); r ≥ 0.6 on 4 of 5 (sugihara*, a live solo set, 0.52) | yes |
| Loud / low ≥ 1.4 | calm 1.63 and pop 1.40 (yes); calm-then-drums 1.25, EDM 1.20, rap 1.11 (no) | partly |
| Pulse size grows with the kick's loudness | only by section | no |
| No beats on noise or silence | none (one swelling brown noise: 3 a minute, as in round 1) | yes |
| Zero allocations per frame | 0 B | yes |

**Drum tracks, r2-dyn → now.** The calm and piano tracks are unchanged on every measure:
- No pulses on any of them.
- r(I, loudness): sugihara* 0.52, restwell* 0.89, memorized 0.87, whispering 0.82, viandanze* 0.69.

| Track | Kicks | Detected | Hit rate | Pulse on all kicks | Bounce depth | False ≥0.3 /min | p95 dShown/dt /s | r(I, loudness) | Loud / low |
|---|---|---|---|---|---|---|---|---|---|
| drums-onmyown* | 53 | 100 → 96 % | 81 → 13 % | 0.82 → 0.33 | 43 → 8 % | 7.3 → 4.6 | 0.90 → 0.22 | 0.35 → 0.78 | 1.03 → 1.17 |
| drums-hurricane | 61 | 97 → 92 % | 75 → 77 % | 0.78 → 0.41 | 40 → 10 % | 10.3 → 16.6 | 0.93 → 0.39 | 0.51 → 0.77 | 1.23 → 1.32 |
| rap-ballin | 274 | 89 → 87 % | 82 → 81 % | 0.83 → 0.81 | 44 → 43 % | 9.3 → 5.5 | 1.68 → 1.67 | 0.45 → 0.36 | 1.09 → 1.10 |
| rap-clean* | 279 | 76 → 69 % | 61 → 52 % | 0.61 → 0.52 | 29 → 28 % | 14.1 → 7.1 | 1.58 → 1.46 | 0.48 → 0.46 | 1.12 → 1.13 |
| rap-kcentric* | 356 | 97 → 94 % | 97 → 92 % | 0.97 → 0.92 | 42 → 41 % | 30.3 → 12.0 | 5.35 → 3.52 | 0.37 → 0.38 | – |
| rap-kojo | 199 | 86 → 78 % | 63 → 64 % | 0.64 → 0.63 | 32 → 31 % | 66.6 → 35.6 | 1.97 → 1.74 | 0.42 → 0.41 | 1.09 → 1.09 |
| edm-breakers | 496 | 94 → 90 % | 90 → 85 % | 0.90 → 0.85 | 38 → 39 % | 21.3 → 5.1 | 4.67 → 2.96 | 0.46 → 0.45 | 1.35 → 1.33 |
| edm-powerup* | 449 | 84 → 82 % | 76 → 72 % | 0.77 → 0.72 | 36 → 36 % | 38.8 → 23.9 | 3.97 → 2.44 | 0.39 → 0.28 | 1.13 → 1.06 |
| pop-letitin | 202 | 93 → 92 % | 82 → 70 % | 0.81 → 0.40 | 38 → 10 % | 56.2 → 46.4 | 1.92 → 0.69 | 0.42 → 0.70 | 1.15 → 1.28 |
| pop-river* | 259 | 100 → 100 % | 98 → 95 % | 0.97 → 0.49 | 50 → 12 % | 8.6 → 8.1 | 1.73 → 0.66 | 0.51 → 0.91 | 1.38 → 1.51 |

**Tuned against held-out.** Tuned on calm-memorized, calm-whispering, drums-hurricane, rap-ballin, rap-kojo, edm-breakers and pop-letitin.
- The check is not blind: every run printed both sets.
- This fix tried `RateSeconds` 4 and reverted it, because held-out drums-onmyown drove the choice.
- The weight's −7..0 dB range sits between the classes as measured on all 10 drum tracks.

| Group | Build | Kicks | Detected | Hit rate | False ≥0.3 /min | Bounce depth (track mean) |
|---|---|---|---|---|---|---|
| heavy (rap, EDM), tuned | 6ac553b | 969 | 77 % | 57 % | 2.9 | 17 % |
| heavy (rap, EDM), tuned | r2-dyn | 969 | 91 % | 82 % | 33.3 | 38 % |
| heavy (rap, EDM), tuned | now | 969 | 87 % | 80 % | 15.8 | 38 % |
| heavy (rap, EDM), held out | 6ac553b | 1084 | 65 % | 55 % | 5.9 | 18 % |
| heavy (rap, EDM), held out | r2-dyn | 1084 | 86 % | 79 % | 27.5 | 36 % |
| heavy (rap, EDM), held out | now | 1084 | 82 % | 73 % | 14.5 | 35 % |
| light kit (folk, rock, pop), tuned | 6ac553b | 263 | 91 % | 14 % | 1.8 | 10 % |
| light kit (folk, rock, pop), tuned | r2-dyn | 263 | 94 % | 81 % | 33.3 | 39 % |
| light kit (folk, rock, pop), tuned | now | 263 | 92 % | 71 % | 31.5 | 10 % |
| light kit (folk, rock, pop), held out | 6ac553b | 312 | 99 % | 57 % | 3.1 | 13 % |
| light kit (folk, rock, pop), held out | r2-dyn | 312 | 100 % | 95 % | 8.0 | 46 % |
| light kit (folk, rock, pop), held out | now | 312 | 99 % | 81 % | 6.6 | 10 % |

**The review's cases, r2-dyn → now:**
- **Noise.** 18 signals of 60 s each:
  - brown noise at two levels and with two corners;
  - high-passed brown noise at three levels;
  - an MP3-like brown noise and a red one;
  - ocean-like swells, and a swelling brown noise;
  - four pink noises, a 50 Hz hum, and white noise.

  r2-dyn fired 12–86 beats and up to 49 full pulses a minute on 16 of them. Now 17 give no beat and no pulse. The swelling brown noise fires 3 beats a minute, as in round 1, with no pulse ≥ 0.2; its punch reaches 1.0 once for 4.7 s.
- **Double flashes.**
  - Beats under 175 ms after the one before: 589 → 2 of 4941 (frame times).
  - Full pulses within 150 ms of another: 218 → 0 (round 1: 9).
- **Beat spacing tuned to 0.4 s** (edm-breakers, pop-letitin, rap-kojo): beats closer than 0.38 s went 74 / 105 / 137 → 0 (closest 0.395–0.399 s).
- **hurricane.**
  - Full-band ending (203–240 s): mean pulse 0.05 (punch 0.05) → 0.35 (punch 0.75).
  - Sparse kit section (110–150 s): 0.82 → 0.41.
  - Both are light-kit sized now.
- **Loud vs mid sections.** Loud sections now pulse at least as much as mid ones on every drum track.

  | Track | Mean pulse at kicks, mid / loud | Intensity after 20 s, loud / mid (r2-dyn) |
  |---|---|---|
  | onmyown* | 0.29 / 0.33 | 0.72 / 0.68 (0.64 / 0.65) |
  | hurricane | 0.38 / 0.47 | – |
  | letitin | 0.38 / 0.43 | 0.73 / 0.70 (0.64 / 0.63) |
  | river* | 0.40 / 0.49 | 0.75 / 0.77 (0.64 / 0.70) |
  | ballin | 0.20 / 0.92 | – |
  | clean* | 0.43 / 0.68 | – |
  | breakers | 0.61 / 0.94 | – |
  | powerup* | 0.03 / 0.76 | – |
- **The held-out EDM drop** (powerup*, 161.5 s).
  - The first kick goes from 0.01 to 1.00, and so do the next two.
  - The off-beats between them are masked (0.20 and 0.11).
  - The shown light stays ≥ 0.60; r2-dyn fell to 0.55.
  - Sudden dims across the corpus (a fall ≥ 0.10 within 50 ms with no pulse): 8 → 1. The one left is in edm-breakers at 3.3 s and was there in round 1 too.
- **rap-kojo's re-entries.**
  - At 100, 134, 167, 183 and 217 s: full from the first or second kick (r2-dyn: 0.00–0.42 for 3–4 kicks).
  - At 33 s: 0.09, 0.89, 0.89, 0.14.
  - The song's first groove at 17 s is under limits below.
- **kcentric, 169.94–170.62 s.** Three full re-fires on a held bass note became one 0.85 (170.39 s, 0.45 s after the kick, outside the mask) and two masked at 0.13.
- **Fades to −36 dB.**
  - kcentric's kicks at 160–170 / 170–180 s: 0.30 / 0.30 → 0.07 / 0.00.
  - powerup's at 210–220 / 220–230 s: 0.26 / 0.29 → 0.07 / 0.00.
- **A synthetic kick under calm-restwell** at −12 / −6 / −3 / 0 dB bounces 0.0 / 0.2 / 0.7 / 15.5 %. The review measured 38 % at 0 dB on r2-dyn. The punch still switches on between −3 and 0 dB (0.05 → 0.98), and the weight (0.38) keeps the bounce small.
- **Beats at or under the plotted threshold:** 22.9 % → 1.0 % (6ac553b: 0.9 %, because several analysis steps share a frame).
- **Style tracks.**
  - Rap: every kick ≥ 0.7 (61/61, mean 1.00). EDM: 73/77 (mean 0.97).
  - Ballad, folk band with bass guitar, and piano ballad: no pulse.
  - The folk band's kit at 0 / −6 / −12 / −20 dB: chorus pulses 0.89 / 0.67 / 0.54 / none (weight 0.91 / 0.49 / 0.14).
- **Robustness**, on an intermediate build within 1–2 points of the final. At 30 fps, 144 fps, with 50 ms packets and with jitter seed 2:
  - rap stays at 73–75 % hits and EDM at 78–79 %, with 13.0–15.8 false pulses a minute;
  - with 50 ms packets the latency is 43 / 66 ms, bounded by the packets.

**Alternatives measured and rejected in this fix:**
- **The review's size curve** (0.25..0.9 of the reference kick, minor 6).
  - Rap: pulse on hits p10 0.95 → 0.79, on all kicks 0.75 → 0.67.
  - EDM: p10 1.00 → 0.75, all kicks 0.81 → 0.71.
  - False pulses only went 23.3 → 23.0 and 27.9 → 27.4 a minute.
  - Real kicks vary 6–12 dB in the kick band within a groove, so the curve mostly shrank real kicks.
- **A rise-based strong confirmation** (major 3's proposal).
  - At the corpus's fired beats, the kick band rises ≥ 14 dB over its own level on 50 % of beats in drumless calm songs and piano (≥ 18 dB on 30 %). In hurricane's full-band ending the shares are 58 % and 24 %. So it would start grooves in calm songs.
  - Rise separates only with prominence (medians −10.4 / −1.6 / +4.0 dB for calm music, the ending, and rap and EDM kicks). So rise is used only to resume a remembered groove, together with prominence ≥ 0 dB.
- **Masking groove confirmations** as well as pulses: it cost real kicks, so it was reverted.
- **`RateSeconds` 4:** reverted (see above).
- **The review's adaptive prominence for noise** (≥ 6 dB over its 2 s median): its own port kept only 57–100 % of annotated kicks. The onset contrast was chosen instead.

**Tests changed on purpose in this fix.** Each comment says why.
- `MusicDynamicsTests`:
  - `ACalmBalladNeverBouncesAndMovesSlowly`: the share above 3 Hz ≤ 1.5 % → 0.2 %, and the p95 slope ≤ 0.35 → 0.15 per second (major 12; measured 0.00 % and 0.03–0.12 per second).
  - The ballad's drums entering: full pulses ≥ detected − 2 → pulses ≥ detected − 3, with peaks 0.3..LightGain + 0.05. The ballad kit is a light groove, and without the re-fire exceptions the detector finds 18 of 23 kicks, as before round 2.
  - `ASoftKitIsTheBeatAndBouncesWithTheMoment` (chorus pulses ≥ 0.9) → `ASoftKitUnderAFolkBandBouncesSoftly` (the blocker):
    - chorus mean 0.3..LightGain + 0.1 (measured 0.54);
    - bounce ≤ half the rap's (14 % vs 55 %);
    - a kit 20 dB under: no pulse over 0.3 (was: at most one).
  - Volume change: −10 dB is unchanged (≥ 0.5, measured 0.66). For −20 dB, "the next 10 s ≥ 0.15" → "0–10 s ≤ 0.15 and 30–40 s ≥ 0.8". This follows minor 7: a −20 dB step now looks like a fade. Measured per 10 s: 0.03, 0.00, 0.61, 1.00.
  - Comment-only updates where measurements moved: EDM kicks ≥ 0.7 on 72–77 of 77 (r2-dyn 75–76), and the whispered verse's floor 0.13 (was 0.30).
  - New: `AnOceanRecordingNeverBuildsAGroove` (−20 / −30 dBFS) and `TheDropAfterTheBreakdownHitsOnItsFirstKick`.
- `BeatCharacterTests`:
  - The loudness rows now fade the floor: position 0.25 → 0.253, 0.2 → 0.15, 0.1 and 0 → 0 (all were 0.3).
  - The whisper case: 0.3 → 0.15.
  - New: `TheGroovesWeightScalesItsPulse`, `TheWeightIsTheGroovesHeldThroughABreakdownAndClearedBetweenSongs`, `WithinMaskSecondsOfAFullPulseABeatNeedsAFreshAttack`, `OnNoiseOnlyBeatsThatPassedDoc03sThresholdBuildAGroove`, `AGrooveComesBackOnTheFirstKickAfterABreakdown` and `EveryKickOfASteadyGrooveIsGradedFromTheMomentBeforeItsDip`.
  - The helpers now feed band powers and the music flag.
- `BeatDetectorTests`:
  - `ADoubleKickFiresAgainInsideTheRefractoryTime` and `AKickRightAfterAWeakerOnsetReplacesIt` went with the exceptions (major 11); `NoTwoBeatsComeCloserThanTheBeatSpacing` replaces them.
  - `EvidenceOfAKickLowersTheMedianGuard` became `InMusicEvidenceOfAKickLowersTheMedianGuard`, plus `OnASteadyNoiseTheEvidenceLowersNothing`.
  - New: `OnsetContrastTellsMusicFromASteadyNoise` and `OnsetContrastIgnoresTheMusicStartingOrStopping`.
- `AudioAnalyzerTests`, new:
  - `NoBeatsOnPinkOrBrownNoise` (6 cases);
  - `TheTunedBeatSpacingHoldsOnMusic` (0.18 and 0.4 s);
  - `TheWaveIsSlowWithoutDrumsAndQuickInAGroove`, Level's step response. The review's mutations m02, m03 and m11 each fail it.
- `WavAnalyzeTests`: 24 → 30 columns, each checked as filled.
- `SyntheticAudio` gains `BrownNoise`, `PinkNoise` and `Scaled`.
- **Round 2's own changed expectations.** Its notes listed seven; the full list:
  - `SongLoudnessTests` written in terms of `RangeDb`.
  - `Measure()`'s Hann window.
  - `SoftDrumsBounceSoftly` → `ASoftKitIsTheBeatAndBouncesWithTheMoment` (now replaced again, above).
  - `ASmallVolumeNudgeLeavesTheLoudPartsAsBright` → `ASmallVolumeNudgeKeepsTheBounceAndSettles`.
  - `AWhisperedVerse…` stops 0.25 s before the pickup, with a 0.55 bound.
  - The −10 dB step's mean pulse 0.6 → 0.5.
  - WavAnalyze full pulses ≥ 0.95 instead of exactly 1.
  - `BeatCharacterTests` rewritten.
  - New: `KickEnvelopeTests`, `BeatDetectorTests`, `InAGrooveThePulseLandsOnTheFrameItsBeatFires` and `AGroovePumpsTheLightBetweenKicks`.

**Cost.** Bench, alternating runs on this shared machine:

| Measure | Now | r2-dyn |
|---|---|---|
| Frame (`--packet-ms 10`) | 83–128 µs (0.50–0.77 % of a core) | 78–128 µs (0.47–0.77 %) |
| Analyzer step | 48–77 µs | 53–70 µs |
| Idle analyzer step | 3.4 µs | 1.9 µs |
| Allocations per frame | 0 B | 0 B |

The idle step costs more because the onset contrast's upper-band loop also runs on silent steps. PERF.md is unchanged.

**Known limits:**
- **Rap and EDM hit rates are 74 % and 79 %** (target 90 %; r2-dyn 78 / 84 %).
  - Detection misses 17 % of rap kicks and 14 % of EDM kicks.
  - The hard spacing alone loses 86 (4.2 %) of the kicks r2-dyn fired on; 37 % of those come right after a beat that fired 20–60 ms early.
  - Detected kicks graded under 0.5: rap 99 (57 before a groove forms, 37 masked); EDM 69 (32 before a groove forms, 23 masked, 9 with weak evidence).
- **A song's first groove still needs two or three strong kicks.** rap-clean*'s opening never forms one. rap-kojo's builds at 20.9 s, so its first four kicks from 16.7 s get 0; r2-dyn built it at 18.4 s from a re-fired beat.
- **False pulses ≥ 0.3** are 15.5 a minute on rap and 14.5 on EDM, about half of them on annotated onsets.
  - pop's 27.2 are light-kit sized (≤ 0.5), and the pop oracle marks only about two thirds of the kicks.
  - hurricane went 10.3 → 16.6 because its full-band ending now pulses. That is 23 more where the oracle marks 1 kick, and the review measured those events as kick-like.
- **Pulse size follows the moment** (the groove's weight, the loudness, the mask), not each kick's strength. Per-track r(pulse, loudness) is −0.32..0.55.
- **Volume.** A −10 dB step mid-song reads 0.50 / 0.53 / 0.66 over 0–10 / 10–30 / 30–60 s (round 1: 0.56 / 0.65 / 0.74). A −20 dB step looks like a fade: no bounce for about 20 s.
- **The first song after launch** judges a quiet intro against itself: kcentric with its first 30 s at −15 dB pulses 0.88. The first 10 s of a fade (down to −12 dB) keep full kicks.
- **Swelling brown noise** brings the punch to 1.0 for 4.7 s once. There is no pulse and no dip, but Level runs at groove speed for those seconds.
- **powerup*'s build** (159.2–161.1 s) pulses 0.26–1.0 once the memory resumes, and the pump dips the light to about 0.55 between those pulses. It is probably a filtered build kick that the oracle doesn't mark.
- **The held-out check isn't blind,** and no fresh tracks were added.

Plots, per-track tables and scripts stayed in the session's scratch files. `tools/corpus/` reproduces the tables.

**HANDOFF:** H-027 (to Codex), and progress notes on H-026, H-025 and H-023, written in the docs pass.

### Edge with no rim

#### First pass [M]

Branch `wip/r2-rim` (c41bd73), on 6ac553b. Effort: M.

**Problem (round 1 at 6ac553b, measured).** At the defaults (Thickness 6, Glow 45 %, Intensity 0.72), round 1's soft band, exp(−d/(T/2)) screen-blended with the exponential glow, was steepest at the very edge. That squeezed the brightest light into the outermost pixels against the bezel: a thin bright line with the glow fading in behind it. Perceived brightness was measured as CIE L* of the glow composited the way DWM does in SDR: premultiplied 8-bit, blended in sRGB-encoded space, with the shader's dither. Backgrounds were dark (#181818) and mid (#808080). Glow colours were white, orange #FF8C46 and blue #286EE6.

| Defaults | Round 1 | Round 2 |
|---|---|---|
| Edge pixel, 1080p/100 % (codes) | 179.2 | 156.0 |
| Slope at d = 0, 1080p/100 % | −9.2 codes/px (−5.0 %/px) | 0 |
| Slope at d = 0, 1440p/150 % | −6.1 (−3.3 %/px) | 0 |
| Slope at d = 0, 4K/200 % | −4.6 (−2.5 %/px) | 0 |
| Codes lost over the first 4 px, 1080p/100 % | 13.4 % | 2.4 % |
| L* lost over the first max(4 px, T/2), white over dark, 1080p/100 % | 10.6 % | 1.9 % |
| Same, 1080p/150 % | 11.2 % | 2.1 % |
| Same, 1440p/150 % | 10.0 % | 1.6 % |
| Same, 4K/200 % | 9.1 % | 1.2 % |
| Same, mid background (white) | 4.2–5.2 % | 0.5–0.9 % |
| Steepest drop, 1080p/100 % | 8.7 codes/px, at the edge pair (2.1× the glow's 4.1) | 3.0 codes/px, about 10 px in |
| Growth of the drop per screen px, worst over every gain | negative (convex everywhere) | 0.81 |
| Plain exponential (round 1 at Thickness 0): first 4 px | 7.6 % codes, 5.9 % L*, slope −2.6 %/px | — |

**Formula (`GlowProfile`, mirrored by `Glow.hlsl` step 2 and `GlowRaster`):**
- `glow = 0.85 · exp(−(sqrt(d² + w²) − w) / s)`, then `a = glow · Gain · Visibility`. Gain, SoftClip, the early-out, the dither, the sRGB encode and the colour path are unchanged.
- `w = ShoulderFor(Thickness px, px/DIP) = max(Thickness px, 1 DIP) · (1 + 0.35·Pulse)`. The default 6 DIP is unchanged and now means the shoulder's width. Thickness 0 still gives a one-DIP shoulder, so its slope at the edge is still zero.
- `s = max(SpreadPx · (1 + 0.35·Pulse), 0.5)`, as before. Glow → spread is unchanged.
- Near the edge the hyperbola is about d²/2w (zero slope). Further in it is about d − w: the exponential glow, moved w further in.
  - Its slope is never more than 0.85/s.
  - At d = w its relative rate of fall has eased to 71 % of the exponential's.
  - It is C∞ and has exactly one inflection, so there is no step, kink or ring anywhere.
- The beat scales w and s by the same factor, so the whole light swells inward. The edge only gets brighter through Gain, and the shape never gains a rim.
- One rounded field replaces round 1's two (band and glow): `GlowRadius = clamp(max(corner, 2s + w), ≤ short side / 2)`.
  - Because f′(0) = 0, the corner plateau outside the arc now meets the arc smoothly. Round 1 noted an arc kink there as a pre-existing K1 issue; it is gone.
- cbuffer: `ShoulderPx` replaces `BandScalePx`, `BandGain` and `BandRadiusPx`, and `Pad0`/`Pad1` keep it at 64 bytes. OverlaySurface is unchanged.

**Candidates (same numbers at the defaults, renders below):**
- **Hyperbolic (chosen):** C∞, and the cheapest at one sqrt.
- **Quadratic-then-linear ("Huber"):** C1 only; the curvature jumps at d = w.
- **log-cosh:** costs an exp and a log.

All three keep the first 4 px within 2 % L* at the defaults. A shoulder flat all the way to w with a sharp knee was rejected. On a narrow glow it becomes a bar: the knee's drop growth is 3× the hyperbola's at the defaults, and that is exactly the round-0 complaint.

**Shader cost.** DXC 1.8 ps_6_0 −O3, up to and including the early-out branch: 57 instructions against 78, one exp against two, one divide against two, and two sqrts (unchanged). After the branch: 256 against 255. Both shaders compile with `-HV 2018 -WX` for ps_6_0 and vs_6_0, and the file is ASCII-only. No SM4 compiler is available here; sqrt is SM4-legal.

**Cross-checks:**
- A NumPy model matches C# `GlowProfile` within 0.00006 code values.
- The final PNGs are rendered from C# alpha dumps (`xcheck/`), and the preview PNG from the real `GlowRaster`.

**Tests (Platform.Tests 368 → 380):**
- **GlowProfileTests, rewritten:**
  - `TheDefaultEdgeIsAFlatSoftShoulder`: the round-2 acceptance. Codes and L* stay within 3 % over max(4 px, T/2) on dark and mid backgrounds in three colours, on 12 surfaces including half scale and 1440p/150 %, at every gain. The edge pair also falls more slowly than the next pair.
  - `TheSlopeAtTheEdgeIsZeroAtEverySetting`.
  - `TheDefaultLookFadesWithoutALine`: the drop grows by at most 1 code value per screen pixel, the outermost pair included.
  - `NoSettingDrawsALine`, for every setting:
    - no drop larger than the glow's steepest;
    - drop growth at most the shoulder's curvature 0.85·gain/(w·s);
    - at most one inflection.
  - `TheseChecksCatchRoundOnesBandAndTheSolidCore`: round 1's band, the plain exponential, the K1 solid core and a hard-kneed shoulder each fail.
  - `ClippingEachPixelWouldDrawALineOnTheBeat`.
  - `ThicknessIsTheWidthOfTheFlatShoulder`.
  - `TheLightKeepsItsShapeAtEveryScale`: half scale is exactly the full-scale light, and 4K/200 % equals 1080p/100 %.
  - `TheBeatSwellsTheWholeLightInward…`.
  - `TheFieldIsRoundedForTheWholeLight`.
- **Expectations changed on purpose:**
  - **`TheDefaultLookFadesWithoutALine`** used to require the drop never to grow (≤ 0.1). A shoulder must grow its drop from 0, so the bound is now 1 code value per screen pixel, outermost pair included.
  - **`NoSettingDrawsABar`** had absolute bounds (≤ 1.1 inside, ≤ 6 at the outermost pair). It is replaced by `NoSettingDrawsALine`'s three scale-relative bounds above.
  - **`TheFadeNeverEasesOffFasterThanItsLights`** was specific to exponentials. It is replaced by the one-inflection check.
  - **`ScreenBlendNeverPassesOne…`, `AThinBandKeepsItsLightAtAnyScale` and the old solid-core test** went with the band. They are replaced by `TheLightKeepsItsShapeAtEveryScale` and the round-1 test.
  - **GlowConstantsTests:** `ThicknessIsTheSoftBandsWidth` becomes `ThicknessIsTheShouldersWidth`. Half scale checks `ShoulderPx`. An infinite thickness gives the one-DIP shoulder. `LooksLike` checks the shoulder and the field radius.
  - **GlowShaderTests:** the step-2 strings are updated, and no "Band" may remain in the shader.
  - **GlowRasterTests:** expectations come from `Glow(d, w, s)`, with an added case (T20, G1, P0.5) and the new `ThePreviewSkipsOnlyPixelsUnderHalfACodeValue`.
- **Mutations (10), each failing at least one test:**
  - round 1's band in `Glow`
  - a plain exponential
  - a hard knee
  - max(Gaussian band, glow)
  - no one-DIP floor
  - a beat that doesn't widen the shoulder
  - a field radius without w
  - the raster ignoring the beat on the shoulder
  - the raster cutoff ignoring w
  - constants skipping `ShoulderFor`

**Deviations / spec clarifications:**
- Thickness now means the width of the flat soft shoulder. PRD §4 and doc 04 §3 step 2 are updated.
- The Appearance description now reads "How far the light stays even along the edges before it softly fades into the glow. 0 starts the fade right at the edge."
- The edge peak is now 0.85 × gain at every Thickness. Round 1's band lifted it to about 0.98. The default edge is 13 % dimmer (156 vs 179 codes at Intensity 0.72), the same as round 1 at Thickness 0.
- Past the shoulder the tail is the same exponential, w further in: at the defaults it is about 17 % brighter at a given depth. The lit area grows by about w per edge (early-out at about 224 px against 218 at 1080p).
- The even part grows like sqrt(w·s), not w. Within 3 % of the edge at 1080p/100 %:
  - 2 px at Thickness 0, 4 px at 6, 7 px at 20, 10 px at 40;
  - within 10 %: 5, 8, 13 and 18 px.

  The whole fade moves w further in. That is deliberate: with a small s, a knee sharp enough to stay flat all the way to w is a bar.
- No `Contracts/` file and no `Settings` default changed.

**PNGs and scripts** (round 1 against round 2 at 1080p/100, 1440p/150 and 4K/200 %, the perceived-brightness profile, the Settings preview) stayed in the session's scratch files.

**Verified here (Linux, SDK 8.0.426):**
- `dotnet build Rimlight.sln` with `--no-incremental`: Release and Debug both 0 warnings, 0 errors.
- `dotnet test Rimlight.sln -c Release`: Rimlight.Tests 815/815, Rimlight.Platform.Tests 380/380, Rimlight.IconGen.Tests 89/89.
- Rimlight.App.Tests isn't a test project off Windows. Its tests don't read the changed description.

#### Review fixes

Branch `wip/r2-rim-fix` (618a6bb), on `wip/r2-rim` (c41bd73). Effort: S. The pixels drawn are unchanged: only copy, docs, comments and tests changed. Glow.hlsl changed only in comments; its DXIL under DXC 1.8 ps_6_0 -O3 is identical to c41bd73's. ps_6_0 and vs_6_0 compile with -HV 2018 -WX, and the file is still ASCII-only.

- **Thickness copy (findings 1 and 5, minor).** The flat part of `exp(-(sqrt(d²+w²)-w)/s)` is about sqrt(0.06·w·s), not w. Measured at 1080p/100 %, Intensity 0.72. Each cell is the first pixel under 97 % and 90 % of the edge pixel, then the first pixel under half; "down at d = T" is the drop T px in:

  | Glow | Thickness 0 | Thickness 6 | Thickness 20 | Thickness 40 | Down at d = T, Thickness 40 |
  |---|---|---|---|---|---|
  | 10 % | 1/1, 4 | 1/2, 6 | 2/4, 11 | 3/6, 15 | 98 % |
  | 45 % | 2/5, 27 | 4/8, 32 | 7/13, 42 | 10/18, 53 | 35 % |
  | 100 % | 5/14, 88 | 8/18, 93 | 13/27, 105 | 18/35, 121 | 12 % |

  - **Settings now reads:** "How wide the soft, bright part along the edges is. More keeps the light bright further in; 0 starts the fade right at the edge."
  - **Docs:** PRD §4, doc 04 §3 step 2 (a new bullet), the GlowProfile remarks and the Glow.hlsl and GlowConstants comments say three things:
    - The shoulder bends over about w.
    - The even part grows like sqrt(0.06 w spread).
    - When w ≫ spread, the shoulder is a soft half-Gaussian band with sigma = sqrt(w·spread). At Thickness 40 and Glow 10 % that is 12.6 px.
  - **Rejected remap:** w = T²/(0.06 s) would be about 700 px at Thickness 40 on the default Glow. A sharp knee instead would be a bar.
- **Thickness 0 = Thickness 1 (finding 6, nit).** The docs now say they are the same one-DIP shoulder.
  - **Why the floor stays at one DIP:**
    - A half-DIP floor would make them differ by up to 3 codes on the default Glow and up to 26 at Glow 0.
    - At half render scale on the default Glow, it would make Thickness 0 fall fastest at the outermost pair again: 1.02× the next pair at 1080p/100 % half and 1.04× at 768p half. The one-DIP floor gives 0.95 and 0.97.
- **Low-res screens (finding 2, nit).** These are the default edge's numbers at 100 %, the worst over every gain of the tests. "Codes" is the drop over the first 4 px. "L*" is the worst over dark and mid backgrounds in white, orange and blue. "Growth" is the largest growth of the drop, in codes per screen pixel:

  | Screen | Codes | L* | Growth |
  |---|---|---|---|
  | 1920x1080 | 2.40 % | 1.90 % | 0.81 |
  | 1600x900 | 2.88 % | 2.28 % | 0.97 |
  | 1366x768 | 3.36 % | 2.67 % | 1.13 |
  | 1280x800 | 3.23 % | 2.56 % | 1.08 |
  | 1280x720 | 3.58 % | 2.84 % | 1.20 |
  | Any of these at half scale | ≤ 2.44 % | — | ≤ 0.88 |

  - The worst growth comes at Brightness 100 % with no beat; at the default 80 % it is about 1.0.
  - Zoomed 6x and contrast-stretched, the 720p and 768p edges are an even plateau with a smooth fade. Round 1 shows a bright stripe in the outermost column at every resolution.
  - `GlowProfileTests.Surfaces()` gains 1366x768 and 1280x720 at 100 %, full and half scale.
- **Corner crease (finding 3, nit): rejected as out of scope.** It is K1's and unchanged. Max second difference across the diagonal past the field radius, round 1 against round 2 (1080p, white on dark luma codes/px²):
  - idle: 0.50 against 0.50
  - beat: 0.83 against 0.85
  - Neon beat: 1.25 against 1.28
  - Glow 20 % beat: 2.84 against 2.86

  Round 2 cut the arc/plateau kink from 1.6–4.4 to 0.2–1.3. A 3·spread + w field would enlarge the bright corner plateau (34 → 50 px along the diagonal at the defaults) and needs its own renders. It stays in Left for later.
- **CHANGELOG:** the [Unreleased] edge bullet mentions the rim and the soft, bright part.
- **HANDOFF:** H-023's round-1 line about `CoreThicknessDip` now describes the shoulder (docs pass).
- **Tests (Platform.Tests 380 → 396).**
  - **Expectations changed on purpose:**
    - **`TheDefaultEdgeIsAFlatSoftShoulder`:** the code-value proxy bound is 3.7 % below 1080 px, 3 % elsewhere. Why: on 720p/768p at 100 % the default glow is narrower in pixels but the shoulder is still 6 px, so codes fall 3.4–3.6 %. The brief's acceptance, L* ≤ 3 %, is unchanged on every surface.
    - **`TheDefaultLookFadesWithoutALine`:** the growth bound is 1.25 below 1080 px, 1 elsewhere. Why: measured up to 1.20 at Brightness 100 %. It is the shoulder's smooth bend, within `NoSettingDrawsALine`'s curvature bound, which still applies to these surfaces.
    - **`ThicknessIsTheWidthOfTheFlatShoulder`** is renamed `ThicknessWidensTheShoulderAndPushesTheFadeIn`, with the new upper bound flat ≤ sqrt(0.07·w·s) + 1.
  - **New coverage:** 16 theory cases (4 surfaces × 4 theories).
  - **Mutation:** a hard knee, `exp(-max(d - w, 0)/s)`, fails the new upper bound ("thickness 6: flat for 7 px").
- **Verified (Linux, SDK 8.0.426):**
  - `dotnet build Rimlight.sln --no-incremental`: Release and Debug both 0 warnings, 0 errors (11 projects).
  - `dotnet test Rimlight.sln -c Release`: Rimlight.Tests 815/815, Rimlight.Platform.Tests 396/396, Rimlight.IconGen.Tests 89/89.
  - Rimlight.App.Tests isn't a test project off Windows. Its tests move ThicknessSlider but don't read the description.
- **PNGs** (720p, 768p and 1080p corner crops, a 6× edge zoom) and scripts stayed in the session's scratch files.
- **Left for later:**
  - The K1 corner crease.
  - Thickness 0–2 look almost the same at any floor, because the hyperbola's shoulder is sub-visible below about 2 DIP.

### Record 2 min (diagnostics recorder) [M]

Branch `wip/r2-diag` (e181ea6, 4acfa84, cd7a766), on 6ac553b. Effort: M.

- **Why.** Round 1 tuned the dynamics on procedural tracks only, and Maxwell's real songs still feel wrong on every point. This gives him one click that saves what the app computed from a real song on his PC, with no audio, so the next tuning round can work from his music.

#### Structure

**`Visualizer/AnalysisRecording` (App, new, no WPF).** One `RecordedFrame` per overlay frame:
- `time`: seconds since the click, the sum of dt including this frame, as wav-analyze counts frame time.
- `dt`, and `samples`: how many new mono samples the analyzer got (a count only).
- `Level`, `Bass`, `Beat`, `IsSilent`, `BeatCount`.
- `flux` and `threshold`: the newest history entries.
- `EstimatedBpm`.
- The light state the frame returned to the overlay: `Intensity`, `Pulse`, `Spread`, `Visibility`, `Phase`. These are the engine's values; an overlay paused on its own monitor fades them further there.
- `FollowsMusic`: music sync, glow on, not paused, no demo.

Buffer and endings:
- The UI thread allocates the whole buffer: 120 s × 240 fps = 28,800 frames at 72 B, about 2 MB.
- It ends with the frame that reaches 120 s (`TimeLimit`), or when the buffer is full (`BufferFull`, above 240 fps; Native on a 360 Hz display stops at 80 s), on `Stop()` (`Stopped`), or when the tap lets go (`Dropped`). It never wraps.

Threads:
- The overlay thread is the only writer. It fills `frames[count]`, then publishes `count + 1` with `Volatile.Write`, and never writes a published frame again.
- So the UI can read any published prefix at any time, and stopping needs no handshake. A frame being written lies past the count the UI read and is ignored.
- `End` is published after the last count.

**`VisualizerFeed.StartRecording` / `TakeRecording`.** A latest-wins slot, like the tuning request: `Volatile.Write` on the UI side, and on the overlay side `Interlocked.Exchange` behind a `Volatile.Read` check.

**`AnalyzerTap.Record`.** Takes a newly started recording, then appends every frame, not only snapshot frames. `Detach`, attaching a different feed, and a newer recording all drop the current one.

**`MusicGlowSource`, the only seam.** The tap's `Record` call moved from just after `analyzer.Process` to just after `engine.Update`. It now also gets `in state`, `count` and `listen`: one line removed, two added. Behaviour is unchanged, because the tap only reads the analyzer's diagnostics and the features.

**`Visualizer/AnalysisRecordingCsv` (App, new, no WPF).** Writes `%APPDATA%\Rimlight\logs\analysis-YYYYMMDD-HHMMSS.csv`:
- If the name is taken it adds `-2`, `-3`…; a half-written file is deleted.
- UTF-8 without a BOM, invariant culture.
- Header comments, one `# key: value` each:
  - A title line, then `privacy`.
  - `app`: version and 7-character commit, e.g. `Rimlight 0.1.0+6ac553b`.
  - `started` (local time), `length`, `ended`.
  - `frameRate`: measured, plus `fpsCap`.
  - `capture`: format, capture and analysis rates, and samples dropped during the recording.
  - `settings`: enabled, animation, whenSilent, brightness, glow, coreThicknessDip, cornerRadiusDip, primaryRatio, sensitivity, fpsCap, onBattery, pauseInFullscreen.
  - `tuning`: the analyzer's whole tuning as compact H-015 JSON; `wav-analyze --tuning` and `TuningJson.Parse` read it.
  - `demo`.
  - `captureAtEnd`, `settingsAtEnd`, `tuningAtEnd`, `demoAtEnd`: only when they changed during the recording.
  - `columns`.
- Header row: `time,dt,samples,level,bass,beat,isSilent,beatCount,flux,threshold,bpm,intensity,pulse,spread,visibility,phase,followsMusic`.
- Formats: time and dt F6; level, bass, beat and the light values F4; phase F5; flux and threshold G6; bpm F2.
- It never writes the device or track name, like the log.

**`VisualizerWindow`.** A "Recording" section above "Tuning": the privacy text, **Record 2 min**, **Stop**, **Open folder**, and a status line.
- **Record** is enabled once the first snapshot arrives, while the overlay feeds the window and nothing is recording or saving.
- Progress updates 10 times a second: "Recording: 0:42 of 2:00, 2,520 frames. Numbers only, no sound."
- **Saving.** When the overlay thread ends the recording, or on Stop, the file is written on the thread pool. The window then says "Saved 2:00 (7,200 frames) as %APPDATA%\Rimlight\logs\analysis-….csv. Send it with the song's name and where in the song you started." and shows **Open folder**.
- **Open folder** runs Explorer with `/select`, falling back to `AppLog.OpenFolder`.
- **Closing.** Closing the window (Esc, X or Quit) saves at once on the UI thread. A tray notification gives the path unless the app is exiting.
- **Minimized.** A recording that finishes while minimized also sends a tray notification. A recording with no frames saves nothing and says so.
- Log lines start with `[Visualizer]`: "Recording the analysis…", "Saved an analysis recording of 120.0 s (7200 frames) as %APPDATA%\…", and "Saving the analysis recording failed: …".

**`tools/audio-tools/RecordingCsv` (new).** Reads the file by column name, in any order and any case, skipping unknown columns.
- Required: time, level, bass, beat, isSilent, beatCount, flux, threshold.
- Defaults for the rest: dt from the times, light NaN, followsMusic true.
- `Comment(key)` returns a header value.
- `ToAnalysis()` returns an `AnalysisResult` with beats at the rows where beatCount rose; a fall is an analyzer reset (device change).
- Errors name the file, line and column. A wav-analyze CSV is refused because it has no beatCount.

**`wav-analyze plot <recording.csv> [--out] [--plot] [--range a:b] [--width px]` (`RecordingPlot.cs`, new).**
- Draws the same four panels as a WAV analysis through the unchanged `PlotRenderer`.
- The subtitle has the app, start, capture, tuning overrides and settings.
- `WavAnalyzeCli` only gains the verb, the usage lines, and `ParseRange` made internal.

**Reading it in Python.**
- `pandas.read_csv(path, comment="#")`.
- Or `np.genfromtxt([l for l in open(path) if not l.startswith("#")], delimiter=",", names=True)`. genfromtxt's `names=True` alone would take the first comment line as the names.

#### Spec clarifications and deviations

- **K8 "runs only while shown and not minimized".** A running recording keeps the visualizer attached while minimized: audio requested, Observed motion, timer running, plots not drawn. That way Maxwell can watch the glow. Afterwards a minimized window detaches as before.
- **"The UI thread writes a CSV".** The UI thread starts and reports the save. The formatting runs on the thread pool, so the plots don't stall (up to about 3.5 MB at 240 fps). The one exception is a closing window, which saves at once on the UI thread so a quitting app doesn't lose the recording.
- **Extra columns.** `samples` (count), `bpm` and `followsMusic` go beyond the task's list.
- **Privacy wording.** The README privacy bullet now reads "The sound is never recorded or saved. Only the audio visualizer for developers (`--debug-visualizer`) can save the numbers it computed, like loudness and beats, to a file on your PC, when you click **Record 2 min**." Clarifications were added to doc 01 privacy and doc 03 §4. CONTRIBUTING got the visualizer bullet, `wav-analyze plot`, and a request for a recording in "Reporting a bug with logs".
- **Unchanged.** No file under Contracts/, no Core file, no default and no Settings field.

#### Verified here (Linux, SDK 8.0.426)

- **Builds.** `dotnet build Rimlight.sln -c Release --no-incremental` and `-c Debug --no-incremental` give 0 warnings and 0 errors. e181ea6 on its own also builds and passes its tests.
- **`dotnet test Rimlight.sln -c Release`.**
  - Rimlight.Tests 819/819 (815 + 4).
  - Rimlight.Platform.Tests 383/383 (368 + 15).
  - Rimlight.IconGen.Tests 89/89.
  - Rimlight.App.Tests builds only off Windows; it gains three assertions in `TheVisualizerOpens` (Record and Stop disabled, Open folder hidden, without the overlay).
- **`AnalysisRecordingTests` (Platform.Tests, 14 new).**
  - Buffer order and the analyzer's clock.
  - A full buffer never wraps.
  - It ends with the frame that reaches 2 minutes: 7,200 frames at 60 fps.
  - Stop keeps what the recording has; a bad dt counts as 0.
  - `Append` allocates 0 B over 5,000 frames.
  - Two threads, 100,000 frames: no half-written frame is ever seen and the count never goes back.
  - **Real `MusicGlowSource` through GlowRig:**
    - The recorded light equals the `OverlayFrame.State` of all 600 frames exactly.
    - The sample counts equal what was delivered.
    - The last frame's features, flux, threshold, beat count and BPM equal the last snapshot's, and the beat-count rises equal the snapshot's 240 beat ticks.
    - 0 B per frame over a whole 2-minute recording (7,200 frames) and after it ends.
    - A newer recording or a detach drops the old one.
    - followsMusic is 0 while paused, in Idle Glow and in the demo, while the analysis goes on.
  - **Header:** every key; no device name; the AtEnd lines only on a change; the tuning JSON reads back.
  - **Round trip** through the tools' reader under a comma-decimal culture.
  - **Save:** the file name comes from the start time, `-2` on a collision, no BOM, and `DisplayPath`.
  - New theory case "visualizer, recording" in `MusicGlowSourceTests.AFrameAllocatesNothingInAnyMode`: 0 B over 3,000 frames, with all 4,500 frames recorded.
- **`RecordingPlotTests` (Rimlight.Tests, 4 new).**
  - Plotting a 20 s offline run written in the app's format: 39 beats from the beatCount rises, 80 px per second, `--range`, `--width` and `--plot`.
  - Reader: columns by name, defaults, and a reset.
  - Errors carry line numbers; a wav-analyze CSV and an empty file are refused.
  - The help text.
- **Sample.** GlowRig's 124 BPM loop ran through the real frame source for 2 minutes: 7,200 frames, 822,890 bytes. `wav-analyze plot` on it gives 248 beats and 124.0 BPM. Setting RIMLIGHT_SAMPLE_RECORDING to a folder makes that test keep its CSV.

#### Left for later

- **Windows.** Nothing ran on Windows: the window's layout and flows, Explorer `/select`, the tray notifications, and CPU while recording minimized.
- **No light panel.** `wav-analyze plot` has no panel for the light state, such as Intensity × Visibility × (1 + 0.25·Pulse). `PlotRenderer` was left alone because the parallel `--engine` replay work is likely to add light output; it should use the same column names.
- **Song alignment.** The CSV's time starts at the click, not at the start of the song. Ask for the song position, or cross-correlate the recorded flux with an offline analysis of the same song.

#### Review fixes

Branch `wip/r2-diag-fix` (29859c5, e2c84fd), on `wip/r2-diag` cd7a766. Effort: S. All six findings (2 minor, 4 nit) were verified and fixed. Two optional extras were declined (see below).

- **The guidance stays on screen.** The first progress update used to replace Record's guidance about 33 ms after it appeared. The guidance is now part of the progress line: "Recording: 0:42 of 2:00, 2,520 frames. Minimize this window to watch the glow meanwhile; Stop, or closing this window, ends the recording early and saves it. Numbers only, no sound."
- **The file says why it ended.** `RecordingEnd` gains `Closed` and `Error`, and `AnalysisRecording.Stop(reason)` takes `Stopped` (the Stop button), `Closed` or `Error`; the first reason stays. The overlay thread ends with that reason, also when it drops a recording after the stop. A recording that reached 2 minutes or filled its buffer keeps that reason. The header's ended line now reads one of: "when Stop was clicked", "when the visualizer's window closed (Esc, its close button or Rimlight quitting)", "when the visualizer stopped after an error (the log says which)".
- **Samples are counted at the capture rate.** `RecordingCsv.ToAnalysis().SampleRate` now comes from "captured at N Hz", falling back to captureAtEnd and then 0. Before, it took the analysis rate, which is half of a 96 kHz capture's and a quarter of a 192 kHz one's. The CSV's columns line and the docs now say the samples column counts at the capture rate.
- **The file says how to load it.** A new `# reading:` header line before `# columns:` says that `pandas.read_csv(path, comment="#")` reads the file. `numpy.genfromtxt(..., names=True)` needs the # lines dropped first, otherwise it takes the first comment line as the column names; this was reproduced on the sample. `wav-analyze --help` and the writer's XML docs say the same.
- **Tuning is locked while recording.** The sliders, Reset and Paste are disabled while a recording runs, because the rows don't mark when the tuning changed. Copy stays available, and a note under the tuning buttons explains the lock. *Deviation from doc 03 §4 ("live-editable"), recorded in its round-2 clarification.*
- **`wav-analyze --help`:** the plot command's description now starts at column 44, like the other commands'.
- **Test expectations changed on purpose:**
  - `# ended: stopped by hand` is now `# ended: when Stop was clicked`, because Stopped now means only the Stop button. Closing the window and an error get their own lines, asserted in the same test.
  - The header has 13 comment lines instead of 12 (the new `# reading:` line).
  - The existing assertions that SampleRate is 48000 still hold, because those tests capture at 48 kHz.
- **New tests:**
  - Platform.Tests: `ItEndsWithTheReasonTheVisualizerStoppedItFor`, `TheSamplesCountAtTheCaptureRate`.
  - Rimlight.Tests: `TheSampleRateIsTheOneTheSamplesCountIn`, and `TheHelpNamesThePlotCommand` now checks the column.
- **Declined:**
  - A `fired` column: it is just the rise of beatCount, one line in any script.
  - A per-frame change-counter column: it would need Settings passed into the overlay-thread code, and nothing marks the remaining changes better than the header's AtEnd lines do.
- **Sample:** regenerated (823,115 bytes, 7,200 frames, 248 beats, 124.0 BPM). Its data rows are byte-identical to cd7a766's sample. The header differs only in the new `# reading:` line and the capture-rate wording.
- **Not run on Windows:** the progress text, the lock and the ended lines after a real Stop, Esc or close.

### Windows checklist (round 2)

This replaces items 2–8 of round 1's checklist. Round 1's item 1 (Settings opens) and item 10 (WPF smoke tests) still apply. So does item 9 (half scale and mixed DPI), except that Thickness 1 now looks the same as 0.

**Setup.**
- Quit Rimlight (tray > Quit).
- Pull `claude/confident-mccarthy-gt8tif` and run `dotnet run --project src/Rimlight.App -c Release -- --debug-visualizer`. That gives the glow plus the audio visualizer, which item 10 needs.
- Use default settings: Music Sync, Thickness 6, Glow 45 %, Brightness 80 %, Sensitivity 1.
- Use a dark wallpaper and a system volume of about 50 %.
- Give each song 20–30 s: the light learns a song's loud parts over its first 15–25 s.

1. **The edge, at the defaults.** Look from normal distance, then up close. Then drag a white or light-grey window against the screen edge.
   - Expect: the light is soft and even right against the bezel, then fades smoothly inward. At 1080p it is about a third as bright about 40 px in, and twice that far on 4K. Compared with round 1 the very edge is a little dimmer and softer, and the glow reaches slightly further in.
   - Bug: a thin bright line in the outermost pixels, a stripe or inner outline anywhere, or a band that stays flat and then drops.
   - Known: a faint 45° crease across a corner (K1). Report it only if you see it at normal viewing distance.
2. **Thickness and Glow** (Settings > Appearance).
   - Do: Thickness 0, 20 and 40 at Glow 45 %; Thickness 20–40 at Glow 10 %; Glow 10, 45 and 100 % at Thickness 6.
   - Expect:
     - Thickness 0 and 1 look the same, with the fade starting right at the edge and no crisp line.
     - More Thickness widens the soft, bright part and moves the glow further in. It still fades: at 40 it is visibly dimmer 40 px in.
     - At Glow 10 % a large Thickness draws a soft band along the edges.
     - Glow goes smoothly from a narrow soft edge to a wide wash.
     - The Thickness text reads "How wide the soft, bright part along the edges is. More keeps the light bright further in; 0 starts the fade right at the edge."
     - The preview card matches the screen.
   - Bug: a hard inner edge or bar at any setting, a line where the bright part ends, or jumps while dragging.
3. **A calm song with a kit** (Noah Kahan "Stick Season" or "Dial Drunk", Hozier "Too Sweet" or "Take Me to Church").
   - Expect: the verses swell and ease like a slow wave. Once the kit plays, each kick gives a small, soft lift, about a quarter of a rap bounce, and the light doesn't dip between kicks. When the drums stop, the light settles back into the wave within about 5–8 s.
   - Bug: the kit bouncing as hard as rap, the glow pumping darker between kicks, two flashes on one kick, or flicker in a quiet verse.
   - If the kit now feels too faint, say so. It is one constant (H-028).
4. **A calm song without drums** (Hozier "Cherry Wine", or a piano piece).
   - Expect: no pulses at all. The glow follows the phrases, with swells taking 1–2 s, and a chorus is clearly brighter than a quiet verse.
   - Bug: any bounce on a voice, a strum, a piano chord or a bass note, or a jittery glow.
5. **Rap with 808s.**
   - Expect: within the first few kicks, a strong, tight bounce on every kick. The light dips between kicks, and the edge brightens only slightly. There is no second flash on the same hit. Hi-hats and syllables between kicks give no flash, or a small one.
   - Bug: a bounce that lags the kick, clearly audible kicks in a steady verse with no bounce, full-size flashes between kicks, two flashes on one kick, or a bright rim flashing along the edges on the beat.
   - Known: a song's very first groove can take a few seconds to start, longer on an 808-heavy intro. Some kicks still get no full bounce (about 1 in 4 on the rap test tracks).
6. **EDM with a breakdown and a drop.**
   - Expect: the breakdown calms to a wave with no pulses. The drop's first kick hits at full size, and the light never blinks darker as the drop lands.
   - Bug: the drop's first two or three kicks are small, the light blinks dark about a second into the drop, or the breakdown's pads pulse.
   - Known: kicks in the build-up right before the drop may already pulse.
7. **Brown noise, rain or ocean sounds** (a sleep or focus video).
   - Expect: no pulses. The glow drifts slowly with the sound.
   - Bug: any rhythmic pulsing or flashing.
8. **Fades and volume.**
   - Expect: in a fade-out, the bounces shrink and stop over its last seconds.
   - Expect: cutting the volume by about 10 dB mid-song (50 % → 15 %) dims the glow, the kicks keep bouncing, and the brightness comes back over about 30 s.
   - Expect: a bigger cut (below 10 %) reads like a fade. The bounce stops for about 20 s, then returns.
   - Bug: small pulses going on through a nearly silent fade, or no recovery after 40 s.
9. **Sensitivity** (Settings > Motion).
   - Expect: at 0.5 the bounces are about half as big and the glow is dimmer. At 2 the bounces are full and the glow is brighter. The dip between kicks is the same at every setting.
10. **The visualizer and Record 2 min.**
    - Expect: each "beat fired" tick's flux sits above the threshold line, which now shows the threshold actually used. With "Beat spacing" at 400 ms, no two ticks are closer than 0.4 s. The Level mix sliders say they no longer change the light.
    - Record: play a song and click **Record 2 min**.
      - The line under the buttons counts up: "Recording: 0:05 of 2:00, … frames. Minimize this window to watch the glow meanwhile; …".
      - The tuning sliders, Reset and Paste JSON are greyed out, and Copy still works.
      - The glow looks exactly as it does without recording.
    - Minimize the window and wait. After 2:00 a tray notification names `%APPDATA%\Rimlight\logs\analysis-YYYYMMDD-HHMMSS.csv`. Restore the window: **Open folder** selects the file in Explorer.
    - Stop after 10 s, and close with Esc while recording. Each saves at once. The file's `# ended:` line says "when Stop was clicked" or "when the visualizer's window closed …".
    - Open a file in Notepad. It has `#` lines (version, capture format, frame rate, settings, tuning), then numbers only: no song title, artist or device name.
    - Bug: Record never enables while music plays, no file after "Saved", the window freezes while saving, the glow changes while recording, or anything but numbers after the `#` lines.

**Reporting a dynamics or edge issue.** For each song that still feels wrong:
- Click **Record 2 min** at the part that feels most wrong, and send the CSV.
- Give the song, the artist, where in the song you clicked Record (m:ss), and the system volume. Say whether it was the first song after starting Rimlight.
- Say what looked wrong and when, and what you expected: a wave, a soft lift or a hard bounce. For example: "0:45–1:10: bounces late and too small".
- A screen recording with sound (the Snipping Tool's Record, or OBS) that shows the edges still helps, especially for the edge.
- Optional, but it replays exactly what you heard:
  1. Convert the song to a WAV outside the repository.
  2. Run `dotnet run -c Release --project tools/wav-analyze -- song.wav --engine`.
  3. Send the `.engine.csv` and the PNG. Never commit them or the WAV.

## Black-and-white covers, again (Maxwell on 1.0.0)

Maxwell, on the 1.0.0 release, Windows, Spotify: "can you fix the error where it doesn't properly read the cover art it is still just light blue and dark blue", for some songs, not all (colorful covers were right). Effort: S.

**Reproduced on real covers.** 35 album covers that are black and white, toned, or muted (and a few colorful ones as controls), from the Cover Art Archive at 250 px, about Spotify's thumbnail size. They were decoded like the Windows app does (fit in 64 × 64, area-averaged) and run through Core's extractor. They stay in the session's scratch folder: no cover is in the repository. On 1.0.0:
- **An all-black cover was "no art"**, so it showed the manual colors, #7C5CFF and #22D3EE: a violet blue and a light blue. That cover is a flat black square, 100 % one flat color, and no art was "98 % one flat grey".
- **Black-and-white covers with a faint cool cast glowed blue-grey.** Their casts peak at chroma 0.014–0.029, for example #819097 / #69737A (a light and a dark slate blue) for a cover whose cast is 0.020. 1.0.0 kept chroma up to 0.05 as it was. A 0.02 cast looks grey on the cover, but lifted to the glow's lightness (0.55–0.85) and spread around the screen as light, it reads as a clear tint.
- Toned prints of black-and-white photos (0.041–0.047) glowed brown or sage green.

**Fix (Core, behind the frozen contracts).**
- `PaletteExtractor`: no art is only an image with no pixel at alpha 128 or more. A blank or nearly blank image is art and glows its own grey or color. A player's blank placeholder can't be told from an all-black cover, and a grey glow is the smaller mistake. `MinFlatShare`, `FlatTolerance`, `NoArtMaxChroma` and the analysis' `FlatShare` / `IsNoArt` are gone.
- `Glow.ColorScale`: the palette is judged as a whole. When its more colorful source is at or below chroma 0.045 (`MonochromeChroma`), both glow colors get chroma 0, pure grey and white. From 0.06 (`TintedChroma`) they keep their colors exactly as in 1.0.0, and in between they fade in (a smoothstep). Per-color glow-ify (`TargetChroma`) is unchanged.
- Calibration on the 35 covers: black-and-white ones peak at 0.000–0.031, toned prints at 0.041–0.047 (the last keeps 5 % of its color), muted color photos at 0.053–0.061 (they keep 59 %, 92 % and 100 %), clearly colored covers at 0.08 and up. Every cover with a palette peak of 0.06 or more glows exactly the 1.0.0 hex.
- The Mac calls the same extractor (`rl_album_art`), so it gets both changes; its header comment and one log line follow.

**Tests.** `BlackAndWhiteArtTests` is rewritten around pure grey: the text covers, the cool-cast photo (it glowed #627389 / #7D8FA5 on 1.0.0), casts 0–0.035, a warm-toned print, an all-black cover, nearly blank and noisy blank images, plus a muted warm photo that keeps its whole color and the colored cases that must not change. `GlowTests` covers `ColorScale` (values, no jump, the scale multiplies chroma only), and `PaletteExtractorTests` replaces the no-art tests with blank images that glow grey. The Mac bridge test now expects a flat grey cover to glow grey and only a transparent one to be no art.

**Windows checklist (1.0.1).** After the update installs (or `dotnet run --project src/Rimlight.App -c Release`), play in Spotify:
- [ ] A black-and-white cover: the glow is white and grey, with no blue, green or brown tint. Settings > Color shows two grey swatches.
- [ ] An all-black cover (for example a black square): a grey glow, not purple and light blue.
- [ ] A colorful cover: the same colors as in 1.0.0.
- [ ] A faded, muted photo cover: a soft tint of its color, not grey.
- Bug: purple and light blue on any song that has a cover, or a visible tint on a black-and-white one.

## Lane B notes

- K0 was implemented twice in parallel (Codex: PR #1, merged; Claude Code: unmerged). Maxwell chose to keep the merged one. The contracts in `src/Rimlight.Core/Contracts/` as merged in PR #1 are frozen; Lane B patched only its own lane on top.
- Tray: `ForceCreate(enablesEfficiencyMode: false)`. H.NotifyIcon's default (`ForceCreate()`) sets EcoQoS and `ProcessPriorityClass.Idle` for the whole process (`EfficiencyModeUtilities.SetEfficiencyMode`), which would starve the K3 render thread and K2 audio capture.
- The exe is `Rimlight.exe` (`AssemblyName = $(AppName)`), matching docs 06/07/08. The placeholder icon (`src/Rimlight.App/Assets/Rimlight.ico`, violet→cyan ring, 16–256 px) is both the tray icon and the exe icon until C10/K12 replace it. *(C10's brand icon replaced it.)*
- The tray tooltip appends a warning if the process is not Per-Monitor V2 DPI aware (CsWin32 check in `Rimlight.Platform/DpiAwareness.cs`), so Maxwell can see at a glance that the manifest took effect.
- Namespaces: Lane B never uses a `…System` namespace (doc 02's `Platform/System/` folder) because it would shadow `System`.
- Constraints agreed with Codex (H-004): **K8** must not read `AnalyzerDiagnostics` buffers from the UI thread while the render thread writes them. Copy a snapshot on the render thread and hand it to the visualizer. **K3** refills the gradient after every `SetTarget` and after every `Update` that was animating *before* the call, so the final crossfade frame is uploaded even though `IsAnimating` is then false. K3 never reads `IPaletteBlender.Current` per frame: shader colors come from the gradient, and `ILightEngine.Update` gets the palette last passed to `SetTarget`, so crossfades allocate nothing per frame (CLAUDE.md rule 4).
- Lane B review of the merged K0 (verified findings that are Lane B's to handle later; Core asks went to HANDOFF H-007..H-010):
  - **K6 (tray/shell):**
    - Take the single-instance mutex before any `TaskbarIcon` exists. A second instance otherwise reuses the same path-derived tray GUID and removes the first instance's icon.
    - Wrap `ForceCreate` in try/catch and retry on `TaskbarCreated`: at logon, `NIM_ADD` can fail before Explorer's tray is ready, which would crash startup.
    - Open the menu on `TrayKeyboardContextMenu` (Shift+F10 / Menu key) for keyboard access.
  - **K3:** render scale is runtime-only (Half when on battery with `BatteryBehavior.Reduce`); there is no Settings field for it.
  - **K1/K3 `--demo`:** Lane B drives its own synthetic beat and a debug phase override, because the fakes go away in C13 and `FakeLightEngine.Phase` is always 0. *(Done in K8: `DemoBeat` feeds the real engine, which drifts the phase itself, so no phase override was needed.)*
  - **K4/K7:**
    - Lane B owns its sRGB hex ⇄ linear `Rgb` helper, because `Palette` exposes only linear RGB.
    - Compare settings changes field by field: record equality compares `CustomMonitorIds` by reference.
    - Run at most one `Extract` in flight (latest track wins).
    - Save on a debounced background task, catching `IOException`.
  - **K7:** the one-time "still running in the tray" toast is tied to the first-run session (`FirstRunComplete`), so it needs no new setting.
  - **Manual checklists while the fakes are active:** the fake beat flash peaks at 75–100% depending on frame timing, and the first flash after start isn't counted. That is a fake artifact, not a pacing bug. *(Obsolete since C2 and C6 replaced the fakes.)*
- **HANDOFF numbering:** the K7 branch filed its preview entry as H-012; C10 had claimed that number, so it is H-017 since the Lane A + Lane B integration. In the K10 docs pass, C12's entry and C13's CONTRACT CHANGE (both filed as H-018 on their branches) became H-019 and H-023, because K10 had taken H-018. C12's suggested entry is H-020, and the unnumbered entries from K9 and K12 are H-021 and H-022.
