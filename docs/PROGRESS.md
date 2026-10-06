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
- [x] C8 CI: `ci.yml` (Linux job: Core.slnf build+test; Windows job: full sln build+test), labeler, `.github/release.yml` — PR [#10](https://github.com/sussyswimmer/bordervisualizer/pull/10) — effort: M (made by Claude Code on Maxwell's instruction; notes below)
- [x] C9 Packaging: `build/pack.ps1` (Velopack, x64+ARM64, self-contained), `release.yml` on tag `v*` with `vpk upload github`, optional signing step gated on secrets — PR [#21](https://github.com/sussyswimmer/bordervisualizer/pull/21) — effort: M (made by Claude Code on Maxwell's instruction; notes below)
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
  - **Clean start.** Each run first deletes its three output folders. When it can't prompt, vpk stops if the same version is already in its output folder, and a clean start also means no stale file is uploaded.
  - **Before K9.** Until K9, packing needs `-SkipVelopackAppCheck`. Without it vpk fails with "Unable to verify VelopackApp is called" (H-014).
  - The helpers have Pester tests (`build/pack.Tests.ps1`, 28 cases), which the Linux CI job runs.
- **`.github/workflows/release.yml`** has two jobs.
  - **`pack` (windows-latest, read-only token).**
    - Uses exactly SDK 8, the same way as the Linux CI job.
    - Builds and tests `Rimlight.sln`, runs `pack.ps1` with the tag's version, and keeps `artifacts/releases` and `artifacts/installers` as the `packages` artifact (7 days).
  - **`publish` (ubuntu-latest, `contents: write`, tags only).** It runs no build code. In order, it:
    1. Writes the release notes: `.github/release-notes-intro.md`, then GitHub's generated notes (the `releases/generate-notes` API with C8's `.github/release.yml` sections).
    2. Creates a draft release titled "Rimlight <version>" with both installers (`gh release create --draft --verify-tag`).
    3. Runs `vpk upload github --merge` for `win-arm64`.
    4. Runs it for `win` with `--publish`.

    The release is public only once all files are attached, so `releases/latest/download/RimlightSetup.exe` never meets a half-uploaded release. A version with a hyphen (`v1.1.0-beta.1`) becomes a prerelease. Neither `releases/latest` nor the app's update check (`prerelease: false`) picks it up.
  - **Deviation: release notes.** vpk would use notes embedded in the package as the release body. Instead, `gh` creates the release with the notes and vpk merges into it. That keeps every write-token step in one job that runs no build code. The packages carry no notes.
  - **Deviation: dry runs.** Pull requests that change the packaging files, and manual runs, are dry runs: build, test, an unsigned pack with `-SkipVelopackAppCheck`, and the artifact. Nothing is published. They test the Windows pack before any tag exists.
  - **Signing (optional, Azure Trusted Signing).**
    - It runs only for a tag and only when all six secrets exist: `SIGNING_ENDPOINT`, `SIGNING_ACCOUNT`, `SIGNING_PROFILE`, `SIGNING_TENANT_ID`, `SIGNING_CLIENT_ID` and `SIGNING_CLIENT_SECRET`. The client must be an app registration with the "Trusted Signing Certificate Profile Signer" role.
    - The workflow writes the metadata JSON and passes `AZURE_*` credentials to the pack step only. vpk signs with its bundled signtool and Trusted Signing client, and skips files that are already signed (the .NET runtime).
    - Until signing is on, the SmartScreen sentence stays in the README and in the release intro.
- **For Maxwell:**
  - **Repo settings: nothing has to be enabled.** The publish job asks for `contents: write` itself, which works whatever the default under Settings → Actions → General → Workflow permissions is. That default applies only to workflows that don't set `permissions`. Switching that default to "read and write" would only widen every other workflow's token. If a release ever fails with 403 "Resource not accessible by integration", check that Actions are allowed to run on the repo.
  - **To cut a release:**
    1. Make sure K9 is merged and `main` is green.
    2. Run `git tag v1.0.0` on `main`, then `git push origin v1.0.0`. Push only the tag; don't create the release in the GitHub UI, or the publish job's `gh release create` fails.
    3. Bumping `<Version>` in `src/Directory.Build.props` is optional. The tag sets the release version; the props value only sets local and dry-run packs.
    4. The run takes about 15 minutes.
  - **If a release run fails:**
    - If `publish` fails, delete the draft release (keep the tag) and use "Re-run failed jobs".
    - If `pack` fails, fix the problem, then delete and re-push the tag (`git push --delete origin v1.0.0`).
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
  - **Failure paths.** A bad `-Version` fails before any folder is touched, with exit code 1. A pack without the skip fails at vpk's VelopackApp check, so a tag pushed before K9 publishes nothing.
  - **Upload options.** The `vpk upload github` options parse, and `VPK_TOKEN` satisfies `--token`. This was checked against a non-existent repo, so no release was touched.
  - **Lint.** actionlint 1.7.12 with shellcheck 0.11.0 reports 0 problems. All YAML and JSON files parse. PSScriptAnalyzer only flags `Write-Host`, which this console build script uses on purpose.
  - **Tests and build.** Pester: 28/28. Full `.sln` Release build: 0 warnings. `dotnet test`: 165 passed.
  - **Not run here:** a native Windows pack (#21's dry run covers it), signing, and the publish job, which needs a real tag.
- **Follow-ups:**
  - **Delta updates.** Running `vpk download github --channel <c>` into `artifacts/releases` before packing (and not wiping that folder) would make vpk build deltas. Turning that on later doesn't break existing installs.
  - **K9** removes the dry run's `-SkipVelopackAppCheck` (H-014).
  - **Action versions.** Bump to newer action majors together with C8's Node 20 follow-up.

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
