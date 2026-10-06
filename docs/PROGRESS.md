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

- C10 was done by Claude Code (Maxwell asked it to finish both lanes). The tool and the brand icon landed together because the app needed a real icon: `assets/icon.svg` (40 px and up) and `assets/icon-small.svg` (16–32 px, drawn on the 16/32 px grid) replace the placeholder `Rimlight.ico` at the same path. `Rimlight-dim.ico` (50 % opacity) is new, and `assets/icon-256.png`/`icon-512.png` are for the README. `build/icons.sh` or `build/icons.ps1` regenerates all four files in about 5 s. The output is byte-for-byte deterministic. H-012 hands the icons to K6/K12. The C3 branch also adds an entry numbered H-012; C10 claimed the number first, so C3's entry moves to the next free ID when the second of the two PRs merges.
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
