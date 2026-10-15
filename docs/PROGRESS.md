# Progress

## Lane A — Codex

- [x] H-003/H-004 handoff follow-up: safe empty preset catalog until C7; confirm consumer assumptions and cross-review PR #3 — PR #4 — effort: S. Core Release build: 0 warnings/errors; 2 tests passed after reproducing the original startup exception. Implemented, pending merge.

- [ ] C1 FFT, Hann window, band analyzer, auto-gain, envelopes
- [ ] C2 Spectral-flux beat detector, silence detection, `AnalyzerDiagnostics`, `AudioTuning`, real `IAudioAnalyzer` + zero-alloc test
- [x] C3 `Rimlight.Bench` console + `tools/wav-analyze`: reads a WAV, runs the analyzer offline, prints beat timestamps/BPM, and writes a CSV and a PNG plot (ScottPlot or SkiaSharp) of Level/Bass/Beat/flux/threshold. Include a synthetic-track generator (kicks at a given BPM + noise + vocals-ish sines). **This lets beat tuning happen without Windows.** — PR #13 — effort: L
- [x] C4 Oklab/OkLCh, k-means palette extractor, glow-ify, gamut mapping, procedural test fixtures — PR [#12](https://github.com/sussyswimmer/bordervisualizer/pull/12) — effort: M (made by Claude Code on Maxwell's instruction while Codex is not working on Lane A)
- [x] C5 `PaletteBlender` + gradient LUT fill (zero-alloc) — PR [#20](https://github.com/sussyswimmer/bordervisualizer/pull/20) — effort: M (made by Claude Code on Maxwell's instruction while Codex is not working on Lane A)
- [x] C6 Real `LightEngine`: intensity formula, pulse, phase drift, idle breathing, silence fade, Visibility, `IsStatic` — PR [#11](https://github.com/sussyswimmer/bordervisualizer/pull/11) — effort: M
- [x] C7 Settings validation/clamping, JSON store (atomic, backup on corruption), version migration scaffold, Presets — PR #16 — effort: M
- [x] C8 CI: `ci.yml` (Linux job: Core.slnf build+test; Windows job: full sln build+test), labeler, `.github/release.yml` — PR [#10](https://github.com/sussyswimmer/bordervisualizer/pull/10) — effort: M (made by Claude Code on Maxwell's instruction; notes below)
- [x] C9 Packaging: `build/pack.ps1` (Velopack, x64+ARM64, self-contained), `release.yml` on tag `v*` with `vpk upload github`, optional signing step gated on secrets — PR [#21](https://github.com/sussyswimmer/bordervisualizer/pull/21) — effort: M (made by Claude Code on Maxwell's instruction; notes below)
- [x] C10 `tools/icon-gen`: SVG → multi-size `.ico` + PNGs (consumes `assets/icon.svg` from Lane B) — PR #15 — effort: M
- [x] C11 Community health files + issue/PR templates + CHANGELOG — PR [#17](https://github.com/sussyswimmer/bordervisualizer/pull/17) — effort: S (made by Claude Code on Maxwell's instruction; notes below)
- [x] C12 Perf + soak harness in Bench: run analyzer + light engine for 8 simulated hours of synthetic audio, and report allocations, p99 frame cost, and memory — PR [#25](https://github.com/sussyswimmer/bordervisualizer/pull/25) — effort: L (made by Claude Code on Maxwell's instruction; notes below)
- [x] C13 Delete `Fakes/` and make `CoreFactory` return the real implementations. Final Core API docs (`docs/CORE-API.md`). — PR [#27](https://github.com/sussyswimmer/bordervisualizer/pull/27) — effort: M (made by Claude Code on Maxwell's instruction; also fixes H-016 and C4's known limit 2; notes below)

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
  - **Before K9.** Until K9, packing needs `-SkipVelopackAppCheck`. Without it vpk fails with "Unable to verify VelopackApp is called" (H-014).
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
  - **K9** removes the dry run's `-SkipVelopackAppCheck` (H-014).
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
    - 21 of 22 consecutive full-suite runs passed. The one failure, on the first run right after a build on this busy shared machine, could not be reproduced or identified in 21 more runs.
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
  - **Not built yet:** the logs path, the **Open logs folder** tray item and the version in Settings > About all come from docs 02 and 06. H-013 asks Lane B to keep them in sync.
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
- **`IsStatic`** is true only while the light stays hidden, from the frame after Visibility reaches 0. Every visible state moves (Music Sync drifts, Idle Glow breathes), so the "fully settled visible" case never occurs. Hide targets are recomputed each frame from `Enabled`, `Animation`, `WhenSilent`, `paused` and `IsSilent`, so no settings cache is needed. For K3: keep calling `Update` with fresh audio at a low rate while Music Sync hides silence, or the return of sound goes unnoticed. For K3/K5's per-monitor pause (doc 04 §4), `paused` is one flag per engine, so give each overlay its own engine on the render thread and stop rendering only while every engine is static (this is in the `CreateLightEngine` remarks).
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
  - A dark cover with one bright logo gets a soft-grey Secondary (the near-black cluster, glow-ified to neutral L 0.55; the fixture gives `#F32799` / `#707179`). Option: prefer the derived Secondary when Primary is colorful and the best candidate is neutral. **Resolved in C13 (#27):** `#F32799` / `#FF7465`.
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

- **Where things are:**
  - All in `src/Rimlight.Bench`. `SoakProgram.cs` is the synthetic listening program and the warm-up tour, built on C3's streaming `SyntheticTrack`. `SoakRun.cs` is the render-thread simulator, the measurement and the Markdown summary. `SoakReport.cs` writes `--json`, and `BenchCli.cs` has the options.
  - Tests: `src/Rimlight.Tests/Tools/SoakTests.cs`. Numbers, method and the Windows instructions: `docs/PERF.md`.
- **A frame:** the app's render-thread calls in `MusicGlowSource.NextFrame`'s order (K3).
  - `SetTarget` (800 ms) on a new track, then `Reset` on a new session (a new device), then `Process` with the whole 10 ms packets that arrived (an empty span while none do).
  - Then the blender's `Update`, the engine's `Update` and `FillGradient`, which runs every frame; the app fills only on change, so this is an upper bound.
  - Last, the frame pacing's `IsAnimating`/`IsStatic` reads.
  - Frames are 60 fps ± 20 % jitter at 48 kHz. Timing and allocation counting cover only these calls; streaming the program between frames stands in for draining the ring buffer.
- **Program:**
  - Sessions of 40–80 minutes alternate Idle Glow and Hide for silence. Each starts with a device change and, after the first, a 3–12 minute pause with no packets.
  - Tracks of 2–5 minutes run at 80–170 BPM and −24 to 0 dB, in four sections. A quarter continue an album (same colors, new track ID).
  - Gaps alternate between digital silence and no packets; every fifth is 4–20 s.
  - The 8-hour seed-1 run has 8 sessions, 121 tracks and 121 palette changes: music 88 %, silence 0.7 %, no packets 11.3 %.
  - `--shape compressed` packs the same structure into about a minute per session for the 2-minute quick soak.
- **Command line:**
  - `soak` defaults to 8 hours. Options: `--hours`/`--minutes`, `--fps` (60), `--json`, `--shape`, `--seed`, `--jitter`, `--packet-ms`, `--rate`, `--warmup-seconds` (2) and `--report-every` (16 lines).
  - Exit 1 when a measured frame allocates or the live managed heap grows over 256 KB.
  - This replaces C3's 1-minute default and its 5-simulated-second warm-up.
- **Results** (8 hours, seed 1, the shared Linux VM, 4 min 36 s of real time):
  - **0 B in all 1,727,743 frames**, no GCs.
  - Frame mean 60.5 µs, p50 69.4 µs, p99 140.8 µs. The max is a 186 ms VM stall. The analyzer is 99 % of the cost.
  - 0.36 % of one core at 60 fps.
  - Live heap +15 KB (the same in 10-minute, 1-hour and 4-hour runs). Handles 50 → 50.
  - Working set 34.6 → 37.1 MB, which doesn't grow with time. With one progress line it ended 1.2 MB up after both 1 hour and 4 hours; the rest comes from the progress lines' uncollected garbage (120 lines in 1 hour: +3.6 MB).
- **Spec clarifications:**
  - **Scope:** doc 07 Phase 7's soak runs the app in `--demo` mode (overlay, handles, GPU), which is K11's job on Windows (H-018). C12 is the Core part, in simulated time.
  - **CPU budget:** "< 2 % while playing" is doc 07 Phase 3 (and CLAUDE.md's definition of done), not doc 01. PERF.md converts the Core's share to Task Manager's all-CPU scale.
  - **Steady state:** everything after the warm-up tour, which repeats for at least 2 s of real time, so tiered compilation is done. Allocation must be 0 there in every frame, `SetTarget` and `Reset` frames included.
  - **Memory flat:** the live managed heap, after a forced compacting collection, grows at most 256 KB. The working set is reported but not gated: it also moves with the harness's own output. A leak on the render thread would show first as an allocation. What the harness keeps until the end (the progress records) is allocated before the start sample, so it doesn't count.
  - **Session order:** a session's settings and device change take effect before its pause, so a pause into a Hide session hides the light. With the pause first, the 2-minute program never hid the light for seed 1.
- **Test-suite form, and two flaky tests fixed:**
  - The quick soak (2 simulated minutes, compressed) runs twice:
    - In the test process, for coverage and 0 allocations, which are counted per thread.
    - As a child process through the CLI (`SoakInItsOwnProcessPassesAndWritesTheJsonReport`), for the whole verdict including memory.
  - **Why the child process:** inside the test host, the runner's threads allocated about 1 MB and kept about 350 KB during a 0.3 s in-process soak. The old in-process CLI test failed its memory check in 13 of 13 runs with `--filter Soak`, while its frames allocated 0 B.
  - `ReadDeliversMusicAndZerosButNothingWithoutPackets` failed whenever it ran first in its process: 264 B of first-time initialization landed in the measured read. It now reads once before the checked pass.
  - C3's single soak test is replaced by 10: 6 soak tests and 4 program tests.
- **Verification:**
  - `dotnet build Rimlight.sln -c Release` gives 0 warnings. `dotnet test Rimlight.sln -c Release` passed 6 runs in a row: 666 + 73 tests.
  - 15 mutants each fail at least one soak test: allocations in `SetTarget`, in `Reset`, in `Process` on empty spans, and in the engine while hidden or static; allocations counted around `Process` only; session settings or palette events ignored; the old session order; no-packet gaps delivering zeros; warm-up measurements kept; the memory check always passing; the verdict ignoring memory; no `FillGradient`; and `--shape compressed` running the day.
  - A discarded `new` in a hot method was removed by the tier-1 JIT, so the hot-path mutants keep the allocation alive (`GC.KeepAlive`).
- **Review fixes:**
  - **Progress records counted as growth.** They were records allocated after the start sample and kept to the end, about 166 B per line. `--minutes 10 --report-every 0.005` (2,000 lines) grew the heap 333 KB and failed a clean run. `SoakSnapshot`, `CostSummary` and `MemorySample` are now structs, and the snapshot list is sized for every line before the start sample. A 10-minute run now grows 12 KB with 16 lines and 20 KB with 2,000 or 10,000. `--report-every` allows at most 10,000 lines (exit 2 above that), which bounds that list.
  - **`--json`** is created before the run, so a path that can't be written exits 2 at once. Before, it threw after the whole run.
  - **Command-line contract tested:** `ParseSoak` and `ExitCode` are split out. Tests cover the 8-hour default, exit 1 on FAIL, the line limit and the bad path. The quick soak checks that each progress line's costs cover only its interval (they add up to the run's).
  - **Child-process timeout:** the helper read stdout synchronously, so its 5-minute timeout could never fire. It now reads both streams asynchronously, and a test checks that a 48-hour soak is killed after 5 s.
  - **HANDOFF number:** C12's entry is H-018, because the Lane B integration branch already uses H-017 (K7's two-engines entry, which is H-022 since K10).
  - PERF.md said two half-hour maximums were over 15 ms. The published run has three (17, 53 and 186 ms).
  - 9 more mutants each fail a test: the list allocated after the start sample, snapshots as classes, exit code always 0, a 0.5-hour default, no interval reset, no line limit in the CLI or in the run, no report file before the run, and a synchronous stdout read in the helper.
  - **Verification:** 0 warnings. Every soak, program and Bench test passed in every run (669 + 73 tests in all). In 17 full runs of `Rimlight.Tests`, 2 failed, each on a different allocation check outside C12. `PaletteExtractorTests.ExtractionCostIsModest` (C4) failed once; `LightEngineTests.UpdateDoesNotAllocate` (C6) failed once with 7,896 B on the test's thread.
  - **Those two flakes, ruled out so far:**
    - First-time initialization: both pass alone in a fresh process.
    - GCs started by other threads: a scratch probe saw 0 B in 7,441 checked loops over 809 GCs.
    - Tiered compilation, OSR and the tiering worker restarting: another probe saw nothing.
    - Instrumented copies passed 20 full runs, so the cause is still open. It is not in C12's code, which runs after the parallel tests.

### C13 notes

- **Fakes.** `src/Rimlight.Core/Fakes/` was already empty: C2, C4, C5, C6 and C7 each deleted their fake when the real implementation landed. The last mention was `CoreFactory`'s summary, which now says every method returns the real implementation.
  - `CoreFactoryTests` now checks three things:
    - every factory method returns the real type;
    - Core has no `Fake*` type and no `Fakes` namespace;
    - the factory's public methods and Core's exported types are pinned, so changing them fails the test as a reminder to update `docs/CORE-API.md`. The test also reads that page and fails when it doesn't name one of them in code (`Name`, `Name(…)` or `Name.Member`), so updating only the pinned lists doesn't pass. It checks names only, not that a section is complete.
  - **Not edited:**
    - `CLAUDE.md` line 28 and `AGENTS.md` lines 17 and 41 are agent instruction files and still describe fakes. Maxwell can drop those lines.
    - `START-HERE.md` (the K0 prompt) and doc 09 describe the plan, so their mentions stay.
    - Two comments on the K8 branch say "the fake light engine" (H-020).
- **`docs/CORE-API.md`** is the Core API as Lane B uses it, taken from the code and the `CoreFactory` remarks. Where a frozen contract summary is out of date, its §9 says so: `IAudioAnalyzer`, `IPaletteBlender` and `ILightEngine.Update` (threading), `ISettingsStore`, `Settings`, `AudioTuning.MinFlux`, `AudioTuning.Sensitivity` and `LightState.Intensity`.
- **H-016, tempo (spec clarification of doc 03 §4):** `EstimatedBpm` is the mean of the stored intervals (at most 16, each at most 2 s) within ±25 % of their median. Before, it was the median alone.
  - **Why:** a beat is only seen on a frame, so at a steady frame rate the intervals are whole frames and the median picked one of two values.
  - **Results.** Steady 60 fps, no jitter, every estimate once the history is full:

    | True tempo | Before | After |
    |---|---|---|
    | 126 BPM | 124.14 | 125.76–126.04 |
    | 128 BPM | 128.57 | 128.00 |
    | 140 BPM | 138.46 | 139.81–140.15 |
    | 174 BPM | 171.43 | 173.49–174.02 |

    90 and 120 BPM read exactly. 44.1 kHz gives the same.
  - **Repro.** `wav-analyze` on generated tracks now reads 126.0 / 128.0 / 139.8 / 174.0. The C2 test with the worst estimate moves from 122.0 to 123.8 for 124 BPM. In `selftest`, the analyzer reads 120.1 / 127.9 / 119.9 / 173.9; C3's table had 120.2 / 127.7 / 119.8 / 175.1.
  - **Fallback:** when no interval lies within the window (two equal clusters), the median itself is used. `wav-analyze`'s `BeatStats.Tempo` (C3), the rule's reference, now does the same: before, alternating 0.3 s / 0.6 s beats made it average an empty set and the CLI crash.
- **C4 known limit 2 (spec clarification of doc 05 §2 step 5):** a colorful Primary (chroma ≥ 0.03) never takes a neutral Secondary group (< 0.03). When the best group at least 0.12 from Primary is neutral, Secondary is the derived color (+35° hue, L + 0.08) instead.
  - A neutral Primary keeps a neutral Secondary, so grayscale art still glows soft white. The no-art rule is unchanged.
  - The next colorful group is deliberately not used instead. On the dark-cover fixture it is the logo's anti-aliased edge (H 346.6° against 353.4°), which would make a one-hue glow.
  - The fixture now gives `#F32799` / `#FF7465`.
  - `PopulationIsWeightedWithDiminishingReturns` (75 % grey, 25 % blue) now expects the derived Secondary instead of the grey.
- **Verification:**
  - `dotnet build Rimlight.sln -c Release`: 0 warnings.
  - `dotnet test Rimlight.sln -c Release`: 695 + 73 passed (669 + 73 before).
  - **New tests (26):**
    - Tempo (17): the steady-60-fps theory (8), missed and extra kicks (2), and the interval rule (7).
    - Palette (7): candidate chroma on both sides of 0.03 (4), Primary chroma on both sides (3). The dark-cover fixture test now also checks its Secondary.
    - Factory: 3 new tests replace the old `AllFactoryMethodsReturnObjects`.
  - **Mutants, all killed (15):**
    - Tempo (6): a plain median, a plain mean, no fallback, windows of 0.35/0.28/0.22/0.15, and the lower median for even counts. The first edge test was symmetric, so the 0.35 window survived until it was split into one-sided cases.
    - Palette (7): no rule, no Primary condition, candidate thresholds of 0.05/0.02, Primary thresholds of 0.05/0.02, and the next colorful group instead of the derived color.
    - Factory guards (2): a leftover `Fakes` type, and a wrapped engine.
  - Nothing new runs per frame: the tempo change reuses the sorted 16-float buffer, and C12's soak allocation checks pass.
  - **A flaky test found and fixed:** `OklabTests.ConversionsDoNotAllocate` (C4) failed in 1 of 3 full runs, with 1,048 B allocated.
    - **Cause:** its warm-up skipped `Srgb.ToLinear(byte)`, so Srgb's 256-float table was built inside the measured loop whenever this test was the first in its process to touch Srgb. Alone, it failed every time.
    - **Fix:** the warm-up now runs the measured expression. The table is built once per process, so the product behavior is unchanged.
    - **Check:** each of the 11 allocation-measuring tests then passed alone in a fresh process, twice. The fix was followed by 5 more full runs: all 695 + 73 passed.
  - **CI on #27:** `06c807b` passed on Linux (SDK 8) and Windows. The next run, on `86253af` (only docs and the Oklab test changed), failed one test on each:
    - **Linux, `SoakTests.RetainedMemoryFailsTheSoak` (C12):** "grew −331,784 B". This test checks that a leak fails the soak. Its leak (180 arrays of 4 KB, about 740 KB) was smaller than the test host's own heap swing: the sample is process-wide and also sees memory other tests release meanwhile, such as pooled scratch that the array pool trims after a gen-2 GC. C12 already moved the positive memory check to a child process for this reason.
      - **Fix (C12 branch, `baff8d5`, merged here):** the kept arrays are now 64 KB each (about 11.8 MB, 45 times the limit). A local fix with 256 KB arrays gave way to it in the merge, so the two branches carry the same file. With nothing retained the test still fails (checked here: 8,928 B growth). `TheVerdictNeedsZeroAllocationsAndFlatMemory` still covers the 256 KB limit itself.
    - **Windows, `PaletteExtractorTests.ExtractionCostIsModest` (C4), one of C12's two open flakes:** 2,091 B per call at 512 × 512, against a 1 KB limit.
      - Locally the counts are deterministic: 520 B per call at 64 × 64 and 528 B at 512 × 512. That run therefore had about 6.6 KB extra in the 64 × 64 loop (50 calls) and 7.8 KB extra in the 512 × 512 loop (5 calls). This is close to the 7,896 B C12 saw in `UpdateDoesNotAllocate`.
      - Array-pool trimming can't explain it: a trimmed buffer is re-allocated whole (82 KB or MBs).
      - **Two more causes ruled out by scratch probes:**
        - Crossing the tier-1 call-count threshold, including after the tiering worker has idled out, allocates nothing on the calling thread.
        - `GetAllocatedBytesForCurrentThread` stays exact (0 B drift) while other threads force 576,291 GCs and the measured thread allocates every iteration.
      - **Change:** the test now measures three batches, and at least two must stay under 1 KB per call. One outlier batch is tolerated, so a one-off cost passes, but a cost that recurs every few calls lands in two batches and fails.
        - `57c702d` first took the smallest of the three. Review found that this hides an intermittent allocation. Example: 4 KB on every 7th 512 × 512 call reads 1,352 / 528 / 1,352 B per call. The minimum (528 B) passed, while two batches over the limit now fail. A one-off 7.8 KB on one call reads 2,092 / 528 / 528, which matches the CI figure, and still passes.
        - The one-off cost itself is still unexplained, so this stays open. Tier-up on the calling thread is ruled out above; on-stack replacement and a first type load remain candidates.
  - **C12's two open flakes** (`ExtractionCostIsModest`, `UpdateDoesNotAllocate`) didn't recur in 9 local full runs.
- **Review fixes:**
  - **The surface test now reads `docs/CORE-API.md`.** Before, it compared reflection with two lists written into the test, so editing the lists passed with the page unchanged. Its note above overstated this.
  - **`BeatStats.Tempo` median fallback**, as in Core (see H-016 above). New check: `[0, 0.3, 0.9, 1.2, 1.8]` reads 60 / 0.45 s.
  - **`ExtractionCostIsModest`** tolerates one outlier batch instead of taking the minimum (see the CI flake above).
  - **CORE-API.md:**
    - §3 now gives the median guard as × (1 + 9/√`Sensitivity`), not 10 × relaxed. That is 7.36 × at Sensitivity 2 and 19 × at 0.25.
    - `NoPacketTimeoutSeconds` is 0.03 s to 3600 s. The analyzer's error message said "at least 0.03 s" for both ends; it now names both. `TuningBoundsAreValidated` checks 3600 and 3601.
    - The blender may be owned by one thread, as the light engine already was. A Settings preview's blender on its UI thread (K7's `GlowPreview`) is allowed, and `CoreFactory`'s blender remark says so too.
    - §9 adds `IAudioAnalyzer`'s "no allocations after construction" (a `WindowSize` change allocates) and the render-thread wording of `IPaletteBlender` and `ILightEngine.Update`.
  - **Mutants, all killed (6):** the CLI's tempo without the fallback; the page missing a method (`AnalysisSampleRate`) or a type (`SilentBehavior`); a new `CreateFoo` added to the pinned list but not to the page; no upper bound on `NoPacketTimeoutSeconds`; and 4 KB on every 7th 512 × 512 extraction, which the minimum-of-three version passed.
  - **Verification:** 0 warnings. 4 full runs of `dotnet test Rimlight.sln -c Release`: 695 + 73 passed each time.

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
- [x] K8 `--debug-visualizer` window (binds `AnalyzerDiagnostics` + live `AudioTuning` sliders + "Copy params as JSON") and `--demo` mode — PR #26 — effort: M (notes below; Windows manual test pending)
- [x] K9 Velopack bootstrap in App (`VelopackApp.Build().Run()`), update checks, "Update ready" toast — PR #28 — effort: M (notes below; Windows manual test pending)
- [x] K10 Integration pass once C13 is merged: run everything for real, fix seams, and file HANDOFF entries for Core issues — PR #29 — effort: M (notes below; Windows manual test pending)
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
  - With no overlay (no selected monitor present) the loop stops its frame timer and releases the GPU until a display or settings change brings one back.
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
  - A scratch harness (not committed; Lane B has no test project) checks the ring's semantics and stress-tests it between two threads: 102 M samples, 0 out-of-sequence values, including while it was full and with small reads. It also checks the mono conversion is exact for float32, int16, int24 (sign extension), int32 and 5.1, and that convert + write + read allocates 0 bytes.
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
  - `LoopbackCapture.SetActive` opens or closes the loopback stream on the capture thread, and is inactive until first asked. `MusicGlowSource` asks for audio only in Music Sync while the glow is enabled and not paused.
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
    - With nothing on screen, frames run at 10 Hz.
  - **Static:** each surface remembers what it last presented and skips Draw and Present while the new constants look the same: identical geometry, values within half an 8-bit code value, phase within 0.0002.
  - **Hidden:** an overlay whose visibility drops below 0.5/255 presents one transparent frame, then hides its window. DWM then composes nothing of ours, and fullscreen apps below can flip directly. The window is shown again, topmost, before its next visible frame waits for the swap chain (and before a pending scale switch), because DWM may hand back the frame the transparent present holds only once the window is shown. Windows are created hidden.
  - **Time:** a gap over 0.25 s, or a frame that an input brought forward, gets one full-rate frame of dt. There are no dt spikes, and fades start on the frame after the input.
  - **Capture** is closed whenever audio can't change the glow: Off, `Enabled = false`, Idle Glow, and paused. The analyzer then hears silence. With When silent = Hide, a static invisible glow keeps listening at 10 Hz.
  - **No overlay** (Custom monitors unplugged or none selected): the source gets one paused frame, so the capture closes and no stale audio piles up, and then no calls until an overlay exists again.
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
  - **Capture also closes in Idle Glow and while paused,** not only for Off and disabled. K7's live level meter and K8's visualizer will need a way to request audio while they are open.
  - **The `AppController`/`SettingsService` skeleton lands in K3** because K3 needs live settings. The store stays in memory until C7.
  - **What the loop assumes about C6** (`IsStatic` means time alone changes nothing; hidden means `Visibility == 0`; idle motion is slow; dt after gaps) is in HANDOFF H-011.
- **Left for later:**
  - K5 drives `SetPaused`, `SetOnBattery` (`WM_POWERBROADCAST`) and `SetPausedMonitors`.
  - K7/K8 need an "audio wanted" input on the source.
  - Swap chains stay allocated while Off. They could be released after a long Off if memory matters.
  - The fake engine has no fades, no Hide and no breathing, so those paths can't be seen on Windows until C6.
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes (165).
  - Signatures were checked against CsWin32 0.3.346's generated sources (`EnumDisplaySettings`/`DEVMODEW`, `GetSystemPowerStatus`, `MsgWaitForMultipleObjectsEx`, `SWP_HIDEWINDOW`), decompiled Vortice 3.8.3 (`IDCompositionVisual.SetTransform(Matrix3x2)`, `SetBitmapInterpolationMode`, `SetBorderMode`) and H.NotifyIcon 2.3.2 (the WPF `ContextMenu` opens with `IsOpen`, so `Opened` fires).
  - **Scratch harness 1 (not committed; Lane B has no test project)** links `FramePacer.cs` and `GlowConstants.cs`.
    - It checks 14 cap/refresh/battery combinations, every pace decision, the change-rate threshold, the `LooksLike` tolerances, and that half scale halves every pixel measure except `CoreFade`, which matches full scale (and the old `saturate(CoreThicknessPx)` there) for 0–6 DIP at 100–200 % DPI.
    - Pacer + constants allocate 0 bytes over 100,000 frames.
    - An 85 s timeline with a C6-like engine reaches these steady states:
      - music: 60 frames and presents/s
      - silence → Idle Glow: 10/10
      - Hide: 10 frames/s, 0 presents, hidden, listening
      - Off, global pause and single-monitor pause: 0/0, capture closed
      - Idle Glow: 10 fps
      - Reduce: 30 fps
    - Timings: music returns from Hide within 83 ms. Off and pause stop after 283 ms, and a monitor pause hides after its 300 ms fade. The largest dt is 100 ms.
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
  - **Scratch harness (not committed), 73 checks:**
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
    - The tooltip is re-applied on `TaskbarCreated`. `TrayIconHost` subscribes after `ForceCreate`, so its handler runs after H.NotifyIcon's own one re-adds the icon. K6's `ForceCreate` retry on `TaskbarCreated` must also run before it.
    - Check whether a single `&` shows in the tooltip.
  - **K7:**
    - The "Now playing" row reads `Current` and `Art` (compare `Art.TrackId` with `Current.TrackId` to hide stale art), with swatches via `SrgbHex.Format`.
    - Subscribe to the two events and marshal them to the dispatcher.
  - **C4/C5 (via K10):** with the fakes, colors are averaged, never "no art", and switch instantly.

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
  - **Scratch harness (not committed)** linking `FullscreenRules.cs` and `SystemState.cs`, 53 checks:
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
  - **K7:** the Behavior page's "Pause in fullscreen apps" toggle (already wired through `settings.Changed`). `SystemState` can feed diagnostics.
  - **K11:** sleep/hibernate, Modern Standby, RDP (bandwidth too), TDR and hotplug on real hardware. Also check whether `QUNS_NOT_PRESENT` ever shows up spuriously, for example in a remote session.
    - CPU during a fullscreen pause can't be read with Task Manager in front: activating it on that monitor ends the pause. Use Task Manager with "Always on top" and Alt+Tab back to the fullscreen app, or log `typeperf "\Process(Rimlight)\% Processor Time" -si 1`.
  - **C6 (via K10):** global pauses fade over 300 ms once the real engine lands (H-008); with the fake they are instant.

## K6 notes

- **Structure:**
  - `src/Rimlight.Platform/Shell/` (namespace `Rimlight.Platform.Shell`):
    - `HotkeyGesture`: pure parser and writer for H-009's `Modifier+…+Key` (105 key names; `TryCreate` is K7's recorder seam).
    - `GlobalHotkey`: `RegisterHotKey` + `MOD_NOREPEAT` on a message-only window on the UI thread. `Register` returns a `HotkeyStatus` (None, Registered, Invalid, InUse, Failed, TypesCharacter). `TypesCharacter(gesture)` is the AltGr check (below), public for K7's recorder.
    - `SingleInstance`: mutex `Local\Rimlight-{SID}` + a current-user pipe (`Rimlight-{SID}-{session}`). One server instance serves every start (`Disconnect`), reads at most 64 bytes with a 2 s timeout. A second start sends `show-settings` after `AllowSetForegroundWindow` for the running process; a `--background` start sends nothing. If nobody answers, the new start waits up to 5 s to take over.
    - `StartupRegistration`: HKCU `Run` value `Rimlight` = `"<launcher>" --background`. The launcher is Velopack's stub in the install root when installed (`sq.version` + `..\Update.exe`), else `Environment.ProcessPath`. `Remove()` is for K9's uninstall hook.
    - `SystemTheme`: light/dark app mode and the dark title bar.
  - App:
    - `AppController` owns the hotkey, the startup entry, the first-run flow and the hooks: `IShowSettings` (K7 sets `SettingsWindow`), `IUpdateCheck` (K9 sets `Updates`), `Hotkey` + `HotkeyChanged` (K7's inline warning), `StartupRegistered`, `IsFirstRunSession` (K7's one-time tray toast), `Tray.Notify(title, message, userInitiated, onClick)` (K7/K9).
    - `TrayIconHost`: doc 06's full menu, click/double-click commands, keyboard menu, dimmed icon, re-creation with the icon and tooltip re-applied. `TrayIconImages` builds both icons at `SM_CXSMICON`.
    - `WelcomeWindow`: first run, and the stand-in for Settings until K7.
    - `Logging/RollingFileLog` (a `TraceListener`) + `Logging/AppLog` (start line, unhandled exceptions, open folder).
    - `App.xaml.cs`: claim → log → controller; exit releases the mutex last.
- **Spec clarifications and deviations:**
  - **Logging** is a rolling-file `TraceListener` fed by the existing `Trace` calls, not `Microsoft.Extensions.Logging` (doc 02): no package, same folder and 5 × 1 MB. Lines are queued and written 500 ms later on the pool, the file is opened per batch with full sharing, a burst rolls over line by line, lines over 32 K characters are cut, and it never traces its own failures.
  - **Hotkey rules:** Ctrl, Alt or Win required, except F1–F24; F12 alone is reserved by Windows. Punctuation keys are named by their US position (`Plus`, `Minus`, `Backtick`, …).
  - **Enter/Space on the focused icon open the menu** (Windows can send `NIN_KEYSELECT` twice for Enter, which would toggle twice).
  - **The icon dims only for "Glow on" off,** not for Mode = Off.
  - **Colors ▸ From album art also turns Override album color off,** which every preset turns on (H-009), so it is the tray's way back from a preset. It is checked only while album colors show (`AlbumArt` and no Override); Manual is checked otherwise.
  - **A notification's click action belongs to that notification:** `Notify(…, onClick)` replaces the previous action, and a timeout or close clears it, so a click on an older or timed-out notification does nothing. The action runs guarded, because H.NotifyIcon raises the click inside its window procedure, out of the dispatcher's reach.
  - **`FirstRunComplete` is set when the welcome is shown.** A normal start (no `--background`) of a set-up app opens Settings; until K7 that is the welcome window.
  - **The startup entry is checked at every start** (repairs a stale path). A build that isn't installed never replaces an entry pointing to an installed copy. Task Manager's `StartupApproved` switch is not touched.
  - **Tray icon size:** both icons are built at `SM_CXSMICON` (system DPI); H.NotifyIcon's own conversion picks 16 px at every DPI.
  - **Tooltip `&`** becomes the fullwidth `＆` (K4 follow-up): the shell can take a lone `&` as an access-key marker.
  - **Unhandled UI exceptions** are logged and survived, so the tray stays usable.
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes (165).
  - Signatures checked against CsWin32 0.3.346's generated sources; H.NotifyIcon 2.3.2, .NET 8's Windows pipes and `System.Drawing.Icon`, and Velopack 1.2.161 (locator and vpk's stub naming) decompiled.
  - **Scratch harness (not committed), 435 checks:** the hotkey parser (every key round-trips), the rolling log (rotation, limits, concurrency, `Trace` integration, failures), the single instance across real processes (hand-over, background start, silent and junk clients, take-over after exit and after a kill, a start during the exit teardown), the tooltip. It found two problems, fixed before the PR: a large batch could grow one log file past 1 MB, and re-creating the pipe between clients could make a waiting start miss it. K4's harness still passes against the new tooltip.
  - Nothing has run on Windows yet: Maxwell's checklist is in #22.
- **Review fixes (#22):**
  - **Right-click no longer moves the menu to the taskbar corner.** H.NotifyIcon uses `NOTIFYICON_VERSION_4`, where the shell also sends `WM_CONTEXTMENU` after a mouse right-click; the keyboard handler opened the already open menu again at the notification area. It now does nothing while the menu is open.
  - **A failed re-add after an Explorer restart is retried.** H.NotifyIcon swallows the failure, and `Removed` isn't raised because the new Explorer refuses to delete an icon it doesn't know; our own `TaskbarCreated` handler, which runs after H.NotifyIcon's, starts the retry when the icon isn't back.
  - **Single instance:** the pipe listens right after the settings load, before the slow start-up (a later start waits only 3 s for it), and `OnExit` stops listening first (`SingleInstance.StopListening`), so a start during the exit teardown takes over instead of handing its request to a process that drops it.
  - **Shortcut window missing:** an empty `ToggleHotkey` still reports `None`, and the welcome names the setting's text when there is no parsed gesture.
- **Second review round (#22):**
  - **Ctrl+Alt shortcuts vs AltGr:** Windows treats Ctrl+Alt as AltGr, so `RegisterHotKey(Ctrl+Alt+L)` would also swallow AltGr+L everywhere (ł on Polish, Czech, Hungarian, Croatian and Slovenian layouts; ø on US-International). Before registering a Ctrl+Alt shortcut without Win, `GlobalHotkey` asks every layout in `GetKeyboardLayoutList` what `ToUnicodeEx` types for the key with Ctrl+Alt (plus Shift if the shortcut has it), with flag 0x4 so the thread's dead-key state is untouched. A printable character or a dead key means it is not registered: the status is `TypesCharacter`, the welcome explains it, the tray shows no shortcut. US/UK-only users keep Ctrl+Alt+L (those layouts type nothing with Ctrl+Alt). A layout added while the app runs counts from the next registration (next start or shortcut change). The default lives in the frozen contract: H-019 asks Maxwell whether to change it.
  - **Logs carry no account or device names:** the log is meant for public bug reports, so `RollingFileLog` writes the profile folder as `%USERPROFILE%` (case-insensitive, only when a line is logged), and the capture line logs the mix format instead of the output device's name (Bluetooth headphones are often named after their owner).
  - **Tray choices are checkable:** Mode, Colors and Presets items now set `IsCheckable`, because WPF's `MenuItemAutomationPeer` offers the Toggle pattern only then; without it Narrator never says which item is checked. A click flips the mark, but the menu closes and `Refresh` resets every mark when it opens.
  - **Another copy running:** a start by hand compares the running copy's exe (`GetNamedPipeServerProcessId` → `QueryFullProcessImageName` with limited query rights) with its own. If they differ (say, an older dev build that started at sign-in), it sends nothing and says where the app runs, instead of leaving silently while the old build answers. Same exe, an unknown path or a `--background` start behave as before.
  - **`dotnet Rimlight.dll` doesn't register dotnet.exe for sign-in:** the startup entry is written only when the process exe carries the entry assembly's name; otherwise the existing value is left as it is (and `StartupRegistered` is false).
  - **Log file name** comes from `AppInfo.Name` (lower case), so a rename doesn't leave `rimlight.log` behind.
  - Checked with the scratch harness (now 445 checks: redaction, and the other-exe, same-exe, unknown-exe and background cases across real processes) and CsWin32's generated signatures for `GetKeyboardLayoutList`, `MapVirtualKeyEx`, `ToUnicodeEx`, `OpenProcess_SafeHandle` and `QueryFullProcessImageName`. The AltGr check itself needs Windows (Maxwell's checklist in #22).
- **Merged with the K3–K5 review fixes:** the tray keeps the tooltip it wants in its own field and re-applies it on every (re)creation, which replaces K4's comparison with `TrayIcon.ToolTip`. Its `TaskbarCreated` handler also calls K4's `NowPlayingService.Refresh()` before the re-add check. `SettingsService` keeps K3's lock-free save flag next to K6's `disposed` guard.
- **Left for later:**
  - **K7:** set `SettingsWindow`; the Behavior page's Launch at startup and hotkey recorder (`HotkeyGesture.TryCreate` + `KeyInterop.VirtualKeyFromKey`, warning from `AppController.Hotkey`); when this merges forward, the inline warning needs a `HotkeyState.TypesCharacter` message and the recorder can refuse such a combination up front with `GlobalHotkey.TypesCharacter`; the one-time "still running in the tray" toast via `IsFirstRunSession` and `Tray.Notify`; the welcome then shows its "Open Settings" button; theme the tray menu.
  - **K9:** set `Updates`; "Update ready" via `Tray.Notify(…, onClick: restart)`, with the restart also offered somewhere else (a click from the notification center does nothing); call `StartupRegistration.Remove(AppInfo.Name)` from Velopack's uninstall hook; check that the stub passes `--background` through.
  - **K10:** with C7's store, the welcome shows once and `LaunchAtStartup`/`ToggleHotkey` persist.

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
    - Its own engine and blender come from `CoreFactory` (H-022), with `MusicGlowSource`'s color rules and 800/200 ms crossfades.
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
  - **The toast** shows once per first-run session (K6's decision). Until C7 every run is a first run.
  - **Presets row** is hidden while `Presets.All` is empty.
  - **With Windows animations off,** pages switch without the slide.
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes (165).
  - WPF-UI 4.3.0 was decompiled: `FluentWindow` (Mica needs `ExtendsContentIntoTitleBar`), the theme and accent managers, `UiApplication`'s binding on first use, `NavigationView` (the page provider; its frame swallows F5, so the recorder listens with `handledEventsToo`), `TextBlockMetadata`, `HyperlinkButton`, `TitleBar` close, and every theme key the pages use.
  - The WPF 8.0.31 runtime was decompiled: `Frame`'s inheritance boundary, `DoShutdown` closing windows before `OnExit`, and `MainWindow` accepting null.
  - The CCD signatures were checked against CsWin32 0.3.346's generated sources.
  - **Scratch harness (not committed), 87 checks:**
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
    - **No overlay closes the capture:** with no display chosen or connected, `OverlayHost` calls the new `IOverlayFrameSource.FramesStopped()` once (default: nothing). `MusicGlowSource` then closes the loopback stream, also while Settings wants audio, since no frame publishes a reading. The next frame reopens it.
    - **Preview staleness:** `GlowPreview.Start` restarts its clock, so the reading from before counts as stale until a newer one arrives. A reading that froze minutes ago no longer drives the preview after the page reopens.
    - **Recorder:** it also refuses keys apps use for typing, editing and moving (letters, digits, Tab, Space, Enter, Backspace, Insert, Delete, Home/End/PageUp/PageDown, arrows, punctuation) unless Win, or Ctrl and Alt together, is held. So Ctrl+C, Ctrl+Z, Ctrl+Tab, Ctrl+Shift+T and Alt+F show a hint instead of becoming a global shortcut. Only the recorder refuses them: `HotkeyGesture` (K6) is unchanged, so a shortcut already in the settings still works.
    - **High contrast:** `UiTheme.Sync` re-applies when Windows switches from one contrast theme to another. WPF-UI has a dictionary per contrast theme.
    - **Preview card:** it sits in a `Viewbox` (`DownOnly`), so it shrinks rather than being cut off at the minimum width or 225% text.
    - The checklist in #24 now says the fake blender switches colors instantly, Silence shows after about 3 s (2 s of silence plus the 1 s status hold), and Alt+Tab only ends recording.
    - Release and Debug have 0 warnings, `dotnet test` (165) passes, and the harness has 142 checks (new: the capture with no overlay, and 47 recorder cases).
- **Merged with K3's second no-overlay fix:** K3's host already gives the source one paused frame when the last overlay goes away (`SuspendSource`). The merge keeps that call and has `SuspendSource` call `FramesStopped()` right after it, once per stretch, in place of K7's own flag, so the capture also closes while Settings wants audio.
- **Fix after the merge with K6's second review round:** the Behavior page's inline warning names `HotkeyState.TypesCharacter` (the shortcut types an AltGr character on one of the user's layouts), and the recorder refuses such a combination up front with `GlobalHotkey.TypesCharacter`, asking for Win or a different one. Before, the page showed the shortcut with no warning while it didn't work.
- **Left for later:**
  - **K8:** use `AppController.RequestAudio()` and `MusicGlowSource.LatestAudio`.
  - **K9:** set `AppController.Updates`; About's button and the tray already call it.
  - **K10:**
    - The presets row with C7.
    - The preview with the real C5/C6 (H-022).
    - Check the WPF-UI tray menu on Windows.
    - Optionally drop the Debug tray items Settings now covers.
  - **K11:** measure CPU with Settings open on Appearance (target: about 3% or less) and with it hidden (idle).
  - **K12:** use the About privacy bullets verbatim in the README.

## K8 notes

- **Structure:**
  - `src/Rimlight.App/Visualizer/` (namespace `Rimlight.App.Visualizer`):
    - `DebugVisualizerWindow`: an always-on-top WPF window.
      - It shows the spectrum (dB over log frequency, with the bands shaded), scrolling Level/Bass/Beat, and flux against the threshold, with beats marked.
      - Also the beat lamp, BPM, beat count, IsSilent, the device and format, and the analysis rate and frame rate.
      - A slider and value box for each of the 26 `AudioTuning` fields, plus Copy params as JSON, Paste params and Reset to defaults.
      - It runs (audio request, probe, 30 Hz timer) only while it is shown and not minimized. One probe serves it for its whole life, so a change sent as it is minimized still gets its answer; a restore starts only the plots' history over.
    - `PlotRaster`, `SpectrumPlot`, `TimelinePlot` and `TimeAxis` draw into `int[]` with no Windows calls and no allocation per frame. `PlotSurface` copies them into a `WriteableBitmap` at device pixels.
    - `TuningParameters` is the row table, with a reflection check for fields without a row. `TuningJson` handles the JSON.
  - `src/Rimlight.App/Overlay/`:
    - `AnalyzerProbe` hands two preallocated `AnalyzerSnapshot` buffers between the UI and the render thread.
      - The UI lends one; the render thread fills it on its next frame and hands it back. Each buffer has one owner at a time, there are at most 30 copies a second, and nothing is allocated.
      - Tuning requests go through one `Volatile` slot (`AudioTuning`). The render thread applies them before `Process` and reports a `TuningOutcome`.
    - `DemoShow` is the synthetic 120 BPM groove and the four palettes.
    - `MusicGlowSource` gains `AttachProbe`, `SetDemo` and `IsDemo`.
  - `AppController`:
    - `ParseArguments` / `LaunchOptions` is the only command-line parser; `App.xaml.cs` uses it for the single-instance claim.
    - The pipe commands `demo` and `debug-visualizer` let a later start hand its switches over.
    - `ShowVisualizer`, `StartDemo`, `StopDemo`, `IsDemoRunning`, `AttachVisualizer`. On exit the visualizer closes right after Settings.
  - Tray: Stop demo while the demo plays. The Debug submenu adds the visualizer and a demo toggle.
- **Spec clarifications and deviations:**
  - **Thread safety (H-004):** the UI never touches `AnalyzerDiagnostics` or the analyzer. `Diagnostics` is read on the render thread at every fill, because a `WindowSize` change replaces it. Sensitivity in a tuning request is replaced by the setting's (H-007), and the Sensitivity row edits `Settings.Sensitivity`.
  - **Analysis rate while open:** motion is `Full`, so the analyzer runs on every frame. The overlay still drops to 10 Hz while nothing is on screen, so the time axis uses each `Process` call's real dt rather than an assumed 60 Hz (CoreFactory remarks).
  - **JSON (H-015, sync point 2):**
    - `JsonSerializer` with `WriteIndented` and default naming, every property, byte-for-byte what C3's `TuningJson` writes.
    - Paste reads like C3: keys in any case, comments and trailing commas allowed, unknown keys refused, missing keys at their defaults.
    - Tuning lasts for the session only; the defaults stay in Core (H-005).
  - **Additions:** Paste, Reset and typed values, for round trips with `wav-analyze`.
    - A refused value shows the analyzer's message, the analyzer keeps its previous tuning, and the rows go back to it, so they always match Copy. A band edge moves past its neighbour by moving the neighbour first.
    - A typed or pasted number too large for its field (1e39 is ∞ as a float) is refused before it is sent.
    - Log rows show three significant digits (what the slider stores), and a box whose text wasn't edited sends nothing when it loses focus.
    - Paste changes the Sensitivity setting only when the JSON has a Sensitivity key, so a partial JSON (C3's usage text: "Give any subset") keeps the setting, as Reset does.
  - **Demo:**
    - Groove: a kick each beat (the downbeat stronger), Beat τ 120 ms, Bass with a 10 ms attack and a 180 ms release, an off-beat hat and a 6 s swell in Level. It repeats exactly every 24 s, the gradient's turn included.
    - Palettes: violet/cyan (the brand), pink/amber, mint/blue and red/purple, through the blender with the 800 ms album crossfade.
    - It forces Music Sync for the engine, and `StartDemo` turns "Glow on" on, which is saved like any change of that setting. Nothing else of the demo is saved. Pauses still apply.
    - The capture is closed unless Settings or the visualizer wants audio. A `--demo` start sets demo mode before the overlay's first frame, so the loopback stream never opens.
    - The demo owns the gradient's turn with either engine (`DemoShow.PhaseAt`): C6's 0.01-turn kick on each beat (τ 120 ms) plus a drift of 0.52 turns per loop, so exactly one turn per 24 s. C6 by itself would make 0.84 turns per loop, which jumps where a recording wraps. It starts from the engine's phase, so turning the demo on doesn't jump.
    - A `--demo` or `--debug-visualizer` start opens no Settings and no welcome.
  - **Window size:** about 1000×720 rather than "small", so the sliders fit beside the plots. It never exceeds the primary display's work area (`CenterScreen` doesn't keep a taller window on screen; 1366×768 at 125% leaves about 580 DIPs).
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes (165).
  - Decompiled WPF 8.0.31 `Clipboard`: after its retries, any exception type can come through, so both buttons catch everything. WPF-UI 4.3.0's theme BAML has every brush key the window uses.
  - **Scratch harness (not committed), 114 checks:**
    - the command line, extracted verbatim from `AppController.cs`
    - the demo's tempo at 30/60/144 fps with jitter, its palettes and its periodicity
    - `MusicGlowSource` in demo mode: capture, motion, palettes, pulse, phase, pauses; 0 bytes over 100,000 frames
    - the probe: exact copies, one fill per lent buffer, tuning applied or refused, WindowSize changes; 0 bytes over 100,000 frames with 50,000 fills
    - 361,000 snapshots taken across threads, none torn
    - every tuning row against `AudioTuning`
    - the JSON against C3's reader and writer
    - plot geometry, 0 bytes per draw, about 1.1 ms per 30 Hz tick
  - Demo frames rendered with the Lane A integration branch's real C4–C7 through `GlowRaster`, and the plots for a 120 BPM kick track, were checked by eye.
  - Nothing has run on Windows yet: Maxwell's checklist is in #26.
- **Left for later:**
  - **Sync point 2:** Maxwell tunes with the visualizer and hands the JSON over (HANDOFF). H-005 decides how the defaults change.
  - **K10:** check the demo with C5's crossfades and C6's drift. Optionally drop the Debug tray demo toggle.
  - **K11:** the 8-hour `--demo` soak (H-018 item 2).
  - **K12:** record the README GIF with `--demo`.
    - Settings → Behavior → **Hide from screen capture must be off**, or the recording shows no glow (the log warns when the demo starts with it on).
    - The loop is 24 s and seamless by construction (beats, palettes, crossfades and the gradient's turn). No shorter loop is seamless: every 6 s brings a new palette. Doc 08's "about 8 s" then means either the full 24 s at a lower frame rate or size, or an 8 s cut that isn't seamless.
- **Review fixes (same PR):** the items above about refused values, typed and pasted numbers, the Sensitivity paste, Log row digits, the probe across a minimize, the window size, the demo's turn, the early demo flag and the screen-capture warning. The scratch harness grew to 133 checks (`PhaseAt` periodic, continuous and one turn per loop; the demo's turn periodic through `MusicGlowSource`; every slider position's box text reads back as the stored value; the JSON checks; a request sent while detached answered after re-attaching; `Restart`). With the Lane A integration branch's real C6, the demo's turn repeats every 24 s to 2e-7 turns and starts at the engine's phase.

## K9 notes

- **Structure:**
  - `src/Rimlight.App/Program.cs`: `[STAThread] Main` (the `StartupObject`) runs `VelopackApp.Build()…Run()` first, then `new App(updates)`, `InitializeComponent()` and `Run()`. `App.xaml` is a `Page`, so WPF generates no `Main`.
    - The hooks: auto-apply off, Velopack's warnings and errors to the log (`VelopackTraceLog`), the uninstall hook (`StartupRegistration.Remove`) and the restarted hook.
    - If `Run()` throws in a process started for one of Velopack's steps (first argument `--veloapp-…` or `--squirrel-…`), `Main` returns -1 instead of starting the app. Otherwise an uninstall step could register "Launch at startup" again.
  - `src/Rimlight.App/Updates/` (namespace `Rimlight.App.Updates`):
    - `UpdateBootstrap`: what `Main` saw (restarted version, start error), the `UpdateManager` (`GithubSource(repo, null, false)`, no explicit channel; null when not installed), and `ApplyPending`. `App.OnStartup` calls `ApplyPending` right after the single-instance claim.
    - `UpdateService` (UI thread):
      - the 30 s / 12 h background checks, 1 h after an Offline or RateLimited failure, none while AutoUpdate is off or an update is ready
      - the manual check, the background download and the "Update ready" balloon
      - `RestartToUpdate`: the updater, then the usual Quit
      - Velopack runs on the pool, and its results come back through the dispatcher
    - `UpdateStatus` / `UpdatePolicy`: the phases, the schedule, the failure classes and the texts. No Windows, WPF or Velopack calls.
  - App:
    - `AppController.Updates` (replaces K6's `IUpdateCheck` stand-in), `CheckForUpdates(fromSettings)`, `RestartToUpdate()`, and `Tell` is internal. The service is disposed right after the windows.
    - Tray: "Check for updates" becomes "Restart to update to X" while an update waits.
    - About: a status line and a Restart now button.
    - `AppLog.Folder` is now `%APPDATA%\Rimlight\logs`.
- **Spec clarifications and deviations:**
  - **Restart now** is `WaitExitThenApplyUpdates(…, restart: true, ["--background"])` plus the normal Quit. `ApplyUpdatesAndRestart` is the same call followed by `Environment.Exit`, which would skip saving settings, removing the tray icon and stopping the overlay. The updater waits up to 60 s for the process to exit, so the single-instance mutex and pipe are released before the new version starts. A start meanwhile still waits up to 5 s to take over (K6).
  - **Apply on next restart** is done by the app after the single-instance claim, with Velopack's own start-up auto-apply off. `Update.exe` stops every process running from the install folder before applying, so a second start with a pending update would otherwise kill the running copy.
    - The updater is silent for a `--background` start (sign-in).
    - It isn't retried in a start that Velopack itself restarted, which covers a failed install.
  - **A check or download that failed as Offline or RateLimited retries in 1 h**, not 12 h. Sign-in and wake often come before the network, and GitHub's unauthenticated limit (60 requests/h per IP) resets hourly. **Other failures wait the usual 12 h** (a release file whose SHA-256 doesn't match, a full disk, a missing asset). They would fail the same way again, and each retry downloads the whole package from zero: Velopack deletes the `.partial`, and after a failed download its cleanup also deletes the base package that deltas need. A check gives up after 2 min (Velopack's HTTP timeout is 30 min).
  - **A download gives up after 10 min without progress** (review fix). `HttpClient.Timeout` stops applying once the headers arrive, and nothing else times out a read of the body. So a connection left half-open by a sleep or a network change would keep the service in Downloading for the rest of the session: no background checks, and the manual check only answers "is downloading". The watchdog is a `CancellationTokenSource` linked to the quit token, re-armed by each progress report. Velopack reports every 2-3 %, and applying deltas runs up to 5 min with no report, hence 10 min. Cancelling closes the connection and releases Velopack's update lock. The stall counts as an Offline download failure, retried in 1 h.
  - **Failures:** offline, timeouts, 5xx, a stalled download and a connection lost mid-download count as Offline. The last one is `HttpIOException` for a premature end, or `IOException` with a `SocketException` inside for a reset; a disk error has no `SocketException` inside, so it stays Other. 403/429 count as RateLimited, the rest as Other. Background failures are logged once until something changes, and no one is told. The tray's manual check gets a notification. About's check shows only on the page. After a failed download Velopack also logs "Failed to extract new Update.exe" (`FileNotFoundException` for the package that never arrived). `VelopackTraceLog` drops that one line, because our own line already gives the real cause.
  - **About's status line has no `AutomationProperties.Name`** (review fix). A fixed name would be what screen readers read instead of the text, as on K7's other live messages.
  - **Logs** moved from doc 02's `%LOCALAPPDATA%\Rimlight\logs` to `%APPDATA%\Rimlight\logs`. Velopack installs into `%LOCALAPPDATA%\Rimlight`, replaces that folder on install (renaming it for rollback) and deletes it on uninstall, and doc 07 Phase 6 keeps the logs. This answers H-014 item 3 and H-013 item 4. H-021 asks Lane A to update `CONTRIBUTING.md`, the bug form and doc 02.
  - **After an update restart** the app shows "Rimlight updated — You're now running version X", respecting quiet time, because the `--background` restart opens no window.
  - **The test feed:** the user environment variable `RIMLIGHT_UPDATE_FEED` (a folder of packed releases, or an http URL) replaces GitHub, for testing updates without a release. It is logged at start.
  - **AutoUpdate off** stops background checks only. The manual check still checks and downloads, and a downloaded update still installs at the next start.
  - Velopack keeps its own log in `%LOCALAPPDATA%\velopack\velopack_Rimlight.log` (`velopack.log` for a build-folder run).
- **Verified here (Linux):**
  - Release and Debug builds have 0 warnings, and `dotnet test` passes (165).
  - vpk 1.2.161 cross-packed a self-contained win-x64 publish of this branch and logged "Verified VelopackApp.Run() in 'System.Int32 Rimlight.App.Program::Main()'". The IL entry point is `Program.Main` with `[STAThread]`, and `App.g.cs` has no `Main`.
  - Velopack 1.2.161 was decompiled: `VelopackApp`, `UpdateManager`, `UpdateExe`, `GithubSource`/`GitBase`, `HttpClientFileDownloader` and `WindowsVelopackLocator`. The strings of its `Update.exe`, `Setup.exe` and stub were checked for apply, stopping processes, the install folder and argument forwarding. WPF 8.0.31's `Application.Shutdown` posts the shutdown.
  - **Scratch harness (not committed), 407 checks:** the real update sources with real Velopack (`TestVelopackLocator`), against the packed 0.1.0 through a local feed and an HTTP feed.
    - check, download, the balloon, the restart arguments and Quit
    - install at the next start, silent or not, with the arguments passed on, and no second try
    - up to date, the schedule, AutoUpdate on and off
    - HTTP 403/429/500/404 and a refused connection, logged once
    - not installed versus Velopack failing
    - dispose mid-download and with a result already queued
    - asks during a check don't stack
    - the uninstall and restarted hooks
    - 8 of 9 mutations caught; the survivor only coalesces progress posts
    - review fixes (with the stall timeout compiled as 3 s):
      - a feed that sends 8 MB and then hangs: Failed (Offline, while downloading) no earlier than the timeout after the last progress, logged once as the stall, told to the tray, retried in 1 h, with nothing left in `packages`
      - after that, a new download in the same folder works, so the update lock was released
      - a slow download that keeps progressing (about 3 times the timeout) reaches Ready
      - a feed that resets mid-body: `HttpIOException`, Offline, logged once, retried in 1 h
      - 404 (Other) retries in 12 h
      - the classifier's new cases, and the extract line dropped while other Velopack errors stay
      - 3 more mutations were caught: no re-arm, no `HttpIOException`/`IOException` arm, and no stall mapping
  - Nothing has run on Windows yet: Maxwell's checklist is in #28. Until K10 brings C7's store, settings live in memory only. So every start, the restarts after an update included, shows the welcome window, and AutoUpdate is on again after a restart. The checklist expects both (review fix).
- **Left for later:**
  - **Lane A (H-021):** drop the dry-run `SkipVelopackAppCheck` in `release.yml`, and update the logs path in `CONTRIBUTING.md`, the bug form and doc 02.
  - **K10:** with C9 merged, run `build/pack.ps1` without `-SkipVelopackAppCheck`, then a real tagged test release: install the older one, publish the newer, and check that the update downloads and installs.
  - **K11:** updates across sleep and wake (the 12 h timer), offline at sign-in, ARM64, and the `win-arm64` channel. Decide whether `RIMLIGHT_UPDATE_FEED` stays for 1.0.
  - **K12:** the README FAQ "How do I uninstall?": settings and logs stay in `%APPDATA%\Rimlight`. H-014 item 4 (asset names, release notes, signing).
  - **Optional:** hold back the background download on metered connections (`NetworkInformation` connection cost), and show the release notes from `NotesMarkdown` on the About page.

### K10 notes

- **What it is:** `claude/k10-integration` is the K9 tip (all of Lane B) merged with the C13 tip (all of Lane A), plus the seam fixes below. Merging #29 stands for #8, #9, #14, #18, #19, #22, #24, #26, #28 (Lane B) and #23, #25, #27 (Lane A). The app now runs only against the real Core.
- **Conflicts:**
  - `Rimlight.App.csproj`: K9's `StartupObject`, `App.xaml` as a `Page` and the `Assets\*.ico` glob, with C10's comment on the generated icons. C10's `Rimlight-dim.ico` has the same eight frames as `Rimlight.ico`, so K6's `TrayIconImages` uses it as is.
  - `HANDOFF.md`: every entry kept. K7's H-012 (two engines) collided with C10's earlier H-012 and is now H-022; the K8/K9 resolutions of H-014 and H-015 are folded into the originals.
  - `PROGRESS.md` merged cleanly.
- **Seam fixes (one commit each):**
  - The release dry run packs with vpk's entry-point check (H-021 item 1).
  - `CONTRIBUTING.md`, the bug form and doc 02 name `%APPDATA%\Rimlight\logs` (H-021 items 2–3, H-013).
  - No comment assumes fakes any more (H-020 item 1).
  - The settings preview stays lit in silence when When silent = Hide (below).
  - A failed settings save stays pending (below).
  - A Core test that two engine/blender pairs on two threads don't affect each other (H-022).
- **Spec clarifications:**
  - **Preview in silence (doc 06 §3.1):** the real engine hides the glow 2 s after the sound stops when When silent = Hide; the fake never did. The preview shows the look, not its absence, so it draws silence as Idle Glow there, as it already did for Glow off and Motion Off, with the caption "When it's quiet, the glow hides. This preview breathes instead."
  - **Failed saves:** C7's `Save` throws `IOException`/`UnauthorizedAccessException`. The change stays pending without a retry timer (lowest CPU); the next change or the save at Quit writes it.
  - **Demo phase (H-020):** kept on purpose. `DemoShow.PhaseAt` makes exactly one turn per 24 s loop, so a recording loops seamlessly; the engine's 0.015 cycles/s would make 0.84.
  - **Doc 02 "Logging":** path corrected to `%APPDATA%\Rimlight\logs` (K9's move). Doc 02 still names `Microsoft.Extensions.Logging`; K6's `Trace` listener is the deviation recorded in the K6 notes.
- **Checked against the real Core, no change needed:**
  - H-011: C6 matches every K3 assumption.
  - Settings: the app loads and saves through C7's store at `%APPDATA%\Rimlight\settings.json`. Corrupt, truncated, non-object, future-version and wrong-type files all start cleanly (scratch harness).
  - The analyzer's tuning is only ever set to valid values, or through the visualizer's probe, which catches errors.
  - Extractor exceptions and null "no art" results keep the previous colors.
  - In-range palettes keep `SetTarget` allocation-free.
  - The presets' "chosen" check holds because the validator reuses a clean monitor list.
- **Earlier "left for K10" items:**
  - K4–K6: real album colors, the 300 ms pause fades and settings that persist (the welcome shows once) now apply. Checklist items 1, 4, 5, 8 and 9 in #29 test them on Windows.
  - K7: the presets row and the preview were checked with the real C5–C7 (preview fix above). The WPF-UI tray menu is checklist item 2. The tray's debug items stay, because they exist only in Debug builds.
  - K8: the demo's palettes crossfade through C5 over 800 ms, and the demo owns the gradient's turn (H-020). The Debug-build demo toggle stays.
  - K9: packing without `SkipVelopackAppCheck` now runs in CI's release dry run.
- **HANDOFF:** DONE: H-011, H-012, H-013, H-020, H-021, H-022. Open: H-005 and H-019 (Maxwell), H-018 (item 1 Maxwell, item 2 K11), H-020 item 2 (Maxwell's instruction files).
- **Verified here (Linux):**
  - `dotnet build Rimlight.sln -c Release`: 0 warnings.
  - `dotnet test`: 769 passed (696 + 73 icon-gen).
  - `wav-analyze` on a generated 128 BPM track reads 127.9/128.0 BPM.
  - `icon-gen --help`.
  - `Rimlight.Bench soak --hours 1`: PASS, 0 B/frame, 0.43 % of one core.
  - Scratch harness (not committed): the preview rule and the save retry against the real Core.
  - A static engine field fails the new two-thread test.
  - CI on #29 at `8ec6783`: all green. The Linux job and the Windows full solution passed (0 warnings, 769 tests). The release dry run packed win-x64 and win-arm64 with vpk's entry-point check, which logged "Verified VelopackApp.Run() in 'System.Int32 Rimlight.App.Program::Main()'" for both.
- **Review fixes (core-seam):**
  - **Analyzer time after a stall:** a frame that drains more than `OverlayHost.MaxFrameDtSeconds` (0.25 s) of audio now passes the time that audio covers to `IAudioAnalyzer.Process` and the visualizer. The engine, blender, demo and idle timer keep H-011 item 4's capped dt. Every other frame keeps its own dt, because taking the larger of the two on every frame read a 120 BPM track as 76–92 BPM at 144–240 fps. Scratch harness against the real Core: steady state unchanged at 30–240 fps; 0 of 200 stalls differ from the real dt (115 before); silence after a 1 s stall at 5.13 s (6.02 s before). Not covered: a stall in total silence (no packets), and a frame brought forward from 10 fps (under 0.1 s of audio). Windows re-test (K11): after a display change or GPU reset while music plays, the pulse comes back on the next kick.
- **Review fixes (audio-media):**
  - **Mono level on surround endpoints (doc 03 §1 clarification):** mono = channel sum / min(channels, 2), not the average of every channel (this replaces K2's "Channels are averaged to mono"), so the dBFS silence thresholds don't depend on the endpoint's channel count. Windows plays stereo on a 5.1/7.1 mix format through FL/FR only, and averaging put it 9.5 dB (6 ch) or 12 dB (8 ch) lower: -53 dBFS music went silent after 2 s, and a -50 dBFS intro after a gap stayed silent. Content on more channels now comes out up to 12 dB above an average, which auto-gain absorbs. New `MonoConverterTests` (the source is compiled into `Rimlight.Tests`) cover 2/6/8 channels in all four encodings and silence entry and exit through the real analyzer; the old divisor fails 10 of them. Windows re-test (K11): on a 7.1 headset or a 5.1 HDMI endpoint, a quiet passage or a quiet intro keeps the glow reacting, and the Motion page's meter moving instead of reading "Silence", as on stereo headphones.
- **Left for later:**
  - **Maxwell:** the manual checklist in #29. Then a tagged test release to check an update end to end (K9 notes): install the older release, publish a newer one, and confirm it downloads and installs. Tags and releases are his.
  - **K11:** sleep/wake, RDP, TDR, hotplug, device switching, and the 8-hour `--demo` soak (H-018 item 2).
  - **K12:** the README, banner and repo settings (H-012 item 2, H-014 item 4).
  - **Known edge:** a `settings.json` held by another program through all three read tries at start-up (about 40 ms) reads as defaults, and the welcome's save then replaces it (documented in C7's store).

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
