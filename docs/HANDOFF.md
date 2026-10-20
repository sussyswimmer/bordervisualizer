# Handoff

## [OPEN] H-030 · from: claude-code (macOS) · to: maxwell · blocking: none (Q0 and Q2 answered)
**Need:** Six decisions for the Mac app (docs/10-MACOS.md §8): Q0 Swift over the shared C# Core, Q1 the minimum macOS, Q2 an Apple Developer account, Q3 universal or Apple silicon only, Q4 fetching Spotify's album art from Spotify's server, Q5 the private-API workaround for other players.
**Repro:** docs/10-MACOS.md §8 has each option and why.
**Proposed:** Recommended: Q0 yes, Q1 macOS 14.4, Q2 yes before the first public Mac release, Q3 universal, Q4 yes, Q5 no for v1. M0 waits for Q0. Q4 stays "no" until you say yes, and the build follows the other recommendations until you answer.
**Answered (2026-10-19):** "Just build it, no app developer": Q0 yes (Swift), Q2 no (ad-hoc signing). Q1, Q3 and Q5 follow the recommendation; Q4 stays no until Maxwell says yes. Still open: Q4.
---
## [OPEN] H-029 · from: claude-code (macOS) · to: codex, maxwell · blocking: none
**Need:** Three Lane A items for the Mac app (docs/10-MACOS.md):
1. **.NET 8 reaches end of support on 10 November 2026** (dotnet.microsoft.com/platform/support/policy/dotnet-core). Core, Tests, Bench, the tools and the Windows app all target net8.0. The Mac library targets net10.0 and references Core as it is, so the Mac doesn't need this, but Windows users would run an unpatched runtime.
2. **A `macos` job in `.github/workflows/ci.yml`.** Maxwell's brief for the Mac app asks Claude Code to add it, on pull requests and `workflow_dispatch`, leaving the `core` and `windows` jobs and `Rimlight.Core.slnf` unchanged. It lands in M0's PR. Please review it there.
3. **`InternalsVisibleTo Rimlight.Native.Tests` in `src/Rimlight.Bench/Rimlight.Bench.csproj`**, so a test can replay the same input through Bench's `FramePipeline` and the Mac pipeline and require identical light states (10-MACOS §1.4). Wanted by M2.
**Repro:** —
**Proposed:** (1) Maxwell decides when. Codex then retargets Core, Tests, Bench and the tools to net10.0, and the Linux job to the .NET 10 SDK, and Claude Code moves Platform and App. (2) Codex reviews M0's job. (3) Codex adds the line, or approves Claude Code adding it in M2's PR.
---
## [OPEN] H-028 · from: claude-code (feedback round 2) · to: maxwell · blocking: none
**Need:** Three product calls from round 2's dynamics (PROGRESS.md, "Feedback round 2"). Each is one internal constant, and the defaults below are what this branch ships.
1. **How big a folk or rock kit's pulse is.** In calm songs with a kit (Noah Kahan, Hozier) a kick tops out at half a rap pulse (`BeatCharacter.LightGain` 0.5). Its bounce is about a quarter of rap's (9–11 % against 36–38 % brighter on a kick), and the light doesn't dip between kicks. If the kit now feels too faint, raise it.
2. **The dip between kicks** in rap and EDM. Now the light dips between kicks, so a kick lifts the drawn light 36–38 %, but loud drum sections sit a little dimmer on average. The alternative keeps the average brightness: choruses look brighter (loud/quiet 1.23–1.42 instead of 1.11–1.25), and kicks lift the light 24–34 % (both measured on the first pass).
3. **The strong-kick threshold.** Now it is 4.5..10.5 dB of kick prominence, which keeps a bass guitar under a calm chorus from starting a groove. At 3..9 dB more trap 808 kicks are caught (on the first pass, the trap test track went from 63 % to 76 % of kicks), but a folk band's bass guitar can start a groove and pulse in a calm chorus.

**Repro:** `tools/corpus/` rebuilds the test bed. PROGRESS.md, "Feedback round 2", has the measurements behind each option.
**Proposed:** Maxwell answers after trying the round-2 checklist, ideally with a Record 2 min file for a song where the choice shows. Until then the defaults above stay.
---
## [OPEN] H-027 · from: claude-code (feedback round 2) · to: codex · blocking: none
**Need:** Round 2 changed Lane A paths again, detection included. Please review them and keep them in later Lane A work. Relative to 6ac553b:
- **`src/Rimlight.Core/Audio/`:**
  - `KickEnvelope` (new): a per-sample kick band (Butterworth low-pass at 100 Hz and high-pass at 30 Hz, power over 4 ms) and the music above 150 Hz (over 0.25 s). It reports the rise and the prominence in dB, and each call's mean band powers.
  - `BeatDetector`: in music (`OnsetContrast` ≥ 0.23) with a prominence of −4 dB or more, the median guard is ×5 instead of ×10. `BeatRefractorySeconds` is a hard minimum again. `BeatResult.Threshold` is now the threshold applied, and `BeatResult.Strict` says whether doc 03's own threshold passed.
  - `BeatCharacter` (rewritten): each beat is graded on the step it fires, with an FFT re-grade a frame later that can only raise it. It adds the groove's weight, its memory and resume, the mask after a full pulse, the fading floor, the sliding loudness hold and `RawBeat`.
  - `AudioAnalyzer`: Level is a two-pass envelope of the song position, its speed blended by Punch × Weight. It adds the engaged pump and `Wave`. `LevelKnee` and `ContrastDepth` are gone.
  - `SongLoudness.RangeDb` 24 → 18. `BandAnalyzer.UpperBins` is new. `AudioFrontEnd.LevelTarget` is a diagnostic that only wav-analyze reads.
- **`src/Rimlight.Bench/Rimlight.Bench.csproj`:** `InternalsVisibleTo wav-analyze`, for `FramePipeline`.
- **`tools/`:**
  - `wav-analyze --engine` (new, `EngineReplay.cs`) replays a WAV through `FramePipeline` and writes 30 columns per frame.
  - `wav-analyze plot` draws a Record 2 min CSV (`RecordingPlot.cs`, `audio-tools/RecordingCsv.cs`).
  - `BeatStats.Tempo` no longer throws when no interval is near the median.
  - `tools/corpus/` (new) holds the real-music test bed's track list and scripts, with no audio.
- **`src/Rimlight.Tests/`:** new `KickEnvelopeTests` and `BeatDetectorTests`, a rewritten `BeatCharacterTests`, `RecordingPlotTests`, and new `MusicDynamicsTests` and `AudioAnalyzerTests` cases. Every changed expectation is listed with its reason in PROGRESS.md ("Feedback round 2", Dynamics, "Tests changed on purpose").
- **Cost:** about 5–6 µs more per analysis step on the Linux VM. An idle step costs 3.4 µs (1.9 µs before the review fix, which runs the onset contrast on silent steps too). `docs/PERF.md` was not re-measured.

**Repro:** `git diff 6ac553b b368ec6 -- src/Rimlight.Core src/Rimlight.Tests src/Rimlight.Bench tools` and `git diff 15d918f 698786a -- tools src/Rimlight.Tests`.
**Proposed:** Codex reviews these on PR #32: no Contracts change, 0 B per frame (Core's allocation tests pass), the cost, and the tests that guard Maxwell's feedback (`MusicDynamicsTests`, `AudioAnalyzerTests.TheWaveIsSlowWithoutDrumsAndQuickInAGroove` and `NoBeatsOnPinkOrBrownNoise`). Re-measure PERF.md's analyzer rows on the bench machine.**Progress (black-and-white covers):** Maxwell saw a black-and-white cover glow light and dark blue. `Core/Color` changed again: `Glow.NeutralChroma` 0.03 → 0.05, with the raise to `MinChroma` easing in up to the new `ColorfulChroma` 0.10 (`Glow.TargetChroma`), and `PaletteExtractor.MinFlatShare` 0.6 → 0.98, so only a blank image is no art. Tests: `BlackAndWhiteArtTests` (new), with changed expectations in `GlowTests`, `PaletteExtractorTests` and `SampleArtTests`, each with its reason.
---
## [OPEN] H-026 · from: claude-code (feedback round 1) · to: codex · blocking: none
**Need:** Maxwell's first Windows run (PROGRESS.md, "Feedback round 1") needed Core changes, so round 1 changed Lane A paths. Please review them and keep them in later Lane A work. Relative to 5b387fc:
- **`src/Rimlight.Core/Audio/`:**
  - `SongLoudness` and `BeatCharacter` (new): the song reference, kick-likeness, punch and each beat's grade (CORE-API §4.2).
  - `AudioAnalyzer`: Level comes from the song position, with doc 03's target as the contrast, and its time constants blend by punch. Beat is the grade, and `BeatCharacter` picks the tempo beats.
  - `AudioFrontEnd.LevelTarget`. `BandAnalyzer.WeightedPower` and `KickBins` work on fixed internal bands; `Rms` now sums through `Squares`.
  - `BeatDetector`: `BeatResult` gains `KickLevel`, which detection doesn't use. Tempo intervals come only from `AddTempoBeat(secondsAgo)`, and the tempo goes stale 4 s after the last tempo beat. Detection is unchanged.
- **`src/Rimlight.Core/Lighting/LightEngine.cs`:** the phase push is 0.01 cycles × the height Beat rises to.
- **Docs:** the `CoreFactory.CreateAnalyzer` and `CreateLightEngine` remarks, `docs/CORE-API.md` (§2 units, §4.1–4.4, §6, §6.1) and doc 03's clarifications.
- **`src/Rimlight.Tests/`:**
  - `SyntheticAudio.Run` spots a beat by `BeatCount` rising, because Beat is now a grade. It takes `pulses:`, and `StrongPulseRate` checks the pulse size.
  - New: `MusicDynamicsTests`, `SongLoudnessTests`, `BeatCharacterTests` and `StyleTracksTests`, plus `AudioAnalyzerTests.OffBeatBassNotesAreBeatsButNotTempo` and `LightEngineTests.AGradedBeatPushesInProportionToItsHeight`.
  - Changed on purpose:
    - WavAnalyze: full pulses ≥ beats − 3. The first second only listens, and the drive needs two kicks.
    - `FollowsTheRealAnalyzerThroughMusicSilenceAndMusicAgain`: pushes follow the graded rule, with full pulses ≥ beats − 4.
    - Four `AudioAnalyzerTests` gained `StrongPulseRate` assertions.
- **`tools/audio-tools/StyleTracks.cs` (new):** procedural CalmBallad, Rap, Edm, BalladWithDrums, FolkBand and PianoBallad, with single stems and `Mix`. Stems are not clipped.
- **From the Settings change:**
  - `.github/labeler.yml`: lane-b gains `src/Rimlight.App.Tests/**`.
  - A comment in `.github/workflows/ci.yml`: the Windows job also runs `Rimlight.App.Tests`.
  - `CONTRIBUTING.md`: layout and lanes.

**Repro:** `git diff 5b387fc 3a2fd04 -- src/Rimlight.Core src/Rimlight.Tests tools .github CONTRIBUTING.md` (merges ee8f7a4 and af5d2a6).
**Proposed:** Codex reviews these on the round-1 PR (contract compliance, hot-path allocations, the test-helper change) and acknowledges them here. The beat tests now check detection (`BeatCount`) and pulse size (`StrongPulseRate`) separately; new beat tests should do the same.**Progress (feedback round 2):** "Detection is unchanged" held for round 1 only. Round 2 changed detection and rewrote `BeatCharacter`; see H-027.
---
## [OPEN] H-025 · from: claude-code (feedback round 1) · to: maxwell · type: CONTRACT CHANGE · blocking: none
**Need:** Round 1's dynamics (PROGRESS.md, "Feedback round 1", Dynamics) run on internal constants that aren't `AudioTuning` fields. The stages behind each pulse aren't in `AnalyzerDiagnostics` either. So the K8 visualizer can't show why a beat got its size: a "beat fired" tick with little Beat under it is a beat graded low, but it can't say why (not kick-like, no groove yet, or a quiet moment in the song). "Copy params as JSON" (H-015) can't move what Maxwell's listening is most likely to need tuned:
- **`SongLoudness`:**
  - song memory τ 10 s (`MemorySeconds`)
  - range 24 dB (`RangeDb`)
  - reference percentile 0.9
- **`BeatCharacter`:**
  - kick-likeness smoothstep from 4 to 9 dB of kick prominence (`KickFromDb`, `KickAtDb`). The first version's bass-share threshold is gone.
  - punch smoothstep from 0.3 to 0.75 of the drive (`PunchFrom`, `PunchAt`)
  - drive hold 3 s and fade τ 2.5 s (`DriveHoldSeconds`, `DriveReleaseSeconds`)
  - loud gate from 0.3 to 0.6 of the song position, with a floor of 0.3 × punch (`QuietFrom`, `LoudAt`, `LoudFloor`)
- **`AudioAnalyzer`:**
  - wave factors 10× on the attack and 5× on the release (`WaveAttackFactor`, `WaveReleaseFactor`)
  - contrast depth 0.6 (`ContrastDepth`)
  - Level knee 0.8 (`LevelKnee`)

**Repro:** the `internal const` fields in `src/Rimlight.Core/Audio/SongLoudness.cs`, `BeatCharacter.cs` and `AudioAnalyzer.cs`. For the same reason, the round-1 Windows checklist asks Maxwell for screen recordings rather than JSON.
**Proposed:** two independent parts; Maxwell approves either or both.
1. **Diagnostics only ("approved: diagnostics").**
   - `AnalyzerDiagnostics` gains four preallocated 240-entry histories, in step with `FluxHistory` (one entry per `Process` call): `GradeHistory` (the latest beat's grade), `KickLikenessHistory`, `PunchHistory` and `SongPositionHistory`.
   - Core fills them without allocating, and K8 copies them on the render thread (H-004) and plots them under Beat.
   - No behaviour or default changes.
2. **Tuning fields ("approved: tuning fields"),** only if Maxwell's listening shows the constants need live tuning.
   - The constants above become `AudioTuning` fields with today's values as defaults, for example `SongMemorySeconds`, `SongRangeDb`, `KickFromDb`/`KickAtDb`, `PunchHoldSeconds`/`PunchReleaseSeconds`, `WaveAttackFactor`/`WaveReleaseFactor` and `LevelContrastDepth`.
   - They are validated like the existing fields, get K8 sliders, and `wav-analyze --tuning` reads them.
   - Tuned values reach the defaults under whatever H-005 decides.

Until then the constants stay internal and change only in code, with `MusicDynamicsTests` as the guard.**Progress (feedback round 2):** round 2 replaced most of the constants listed above. The internal constants are now:
- **`SongLoudness`:** memory τ 10 s, range 18 dB, reference percentile 0.9.
- **`KickEnvelope`:** kick band 30–100 Hz, upper band above 150 Hz, envelope 4 ms, upper band 0.25 s.
- **`BeatDetector`:** the loose median guard ×5 at a prominence of −4 dB or more; `MusicContrast` 0.23 over 4 s.
- **`BeatCharacter`:**
  - evidence: −10..−5 dB of prominence, or 12..20 dB of rise;
  - the re-grade 3..9 dB / 14..22 dB, and an instant grade at 8..14 dB;
  - strong confirmation 4.5..10.5 dB;
  - the groove's rate over 3 s, smoothstep 1.1..1.8;
  - punch hold 3 s, release τ 2.5 s;
  - memory 30 s, release τ 4 s; resume at a rise of 18 dB or more with a prominence of 0 dB or more;
  - size 0.05..0.3 of the reference kick (half-life 4 s);
  - loud 0.25..0.55 of the song position, held 0.75 s;
  - floor 0.3 × punch × smoothstep(0.1, 0.3);
  - weight −7..0 dB (8 s, with a fast 1.5 s start); `LightGain` 0.5;
  - mask 0.3 s, needing a rise of 10..20 dB.
- **`AudioAnalyzer`:** groove factors 5× / 3.2× (0.15 s / 0.8 s); the wave a further 6.67× / 2× (1 s / 1.6 s); pump depth 0.4 with τ 0.3 s; engaged hold 1.5 s, release 0.5 s.
- **Gone:** `LevelKnee`, `ContrastDepth`, the 10× / 5× wave factors, and the punch curve (`PunchFrom` / `PunchAt`, replaced by the groove's rate).

Part 1's diagnostics would now also add `Weight`, `RawBeat`, `OnsetContrast` and the memory. Record 2 min (`--debug-visualizer`) and `wav-analyze --engine` cover part of the need meanwhile: a recording holds what the app computed on Maxwell's PC, and the replay CSV holds every internal stage for a WAV. The three decisions in H-028 are each one of these constants.
---
## [OPEN] H-024 · from: claude-code (K6) · to: maxwell, codex · blocking: none (K6 handles it; a default change would be a CONTRACT CHANGE)
**Need:** *(Filed as H-019 on the K6 branch; renumbered in the stack merge, because C12's entry holds H-019.)* A decision on the default glow shortcut. `Settings.ToggleHotkey` defaults to `Ctrl+Alt+L` (frozen `Contracts/Settings.cs:57`, PRD "6. Shell"). Windows treats Ctrl+Alt as AltGr, so `RegisterHotKey(Ctrl+Alt+L)` also fires on AltGr+L and swallows what it types in every app: ł on Polish (Programmers), Czech, Slovak, Hungarian, Croatian and Slovenian layouts, ø on US-International.
**Repro:** Since the K6 review, Platform checks every installed keyboard layout before registering (`GlobalHotkey.TypesCharacter`, via `ToUnicodeEx`). On such a layout the default is not registered (`HotkeyState.TypesCharacter`): the welcome and the Behavior page's inline warning say why, and the Behavior page's shortcut recorder refuses such combinations. Those users start without a shortcut until they pick one.
**Proposed:** Maxwell picks one:
1. Keep `Ctrl+Alt+L`. It works on US, UK and most Western layouts, and the affected users get an explanation instead of a lost letter.
2. Change only the default *value* to a combination that can't be AltGr, e.g. `Win+Alt+L`, after Maxwell checks on his PC that Windows doesn't already use it. That edits `Contracts/Settings.cs` and the copies in doc 06 §1 and the PRD, so it needs "approved" here first; Lane B needs no code change.
---
## [OPEN] H-023 · from: claude-code (C13) · to: maxwell · type: CONTRACT CHANGE (XML only) · blocking: none
**Need:** *(Filed as H-018 on the C13 branch; renumbered in the K10 docs pass, because K10 had taken H-018.)* Seven summaries in the frozen `src/Rimlight.Core/Contracts/` are out of date. The code and `docs/CORE-API.md` are right; only the XML text is stale:
- `ISettingsStore`: "the real implementation arrives in C7". It has arrived.
- `Settings`: "validation and persistence arrive in C7". Both exist.
- `AudioTuning.MinFlux`: "provisional K0 default, to be tuned in C2". The default stays 0.01, but it is relative to the window's RMS.
- `AudioTuning.Sensitivity`: "inversely scales the flux multiplier". It scales by 1/√Sensitivity and also relaxes the median guard.
- `AudioTuning.FluxThresholdMultiplier`: "standard deviations above mean". The effective threshold is the largest of three criteria.
- `LightState.Intensity`: "Brightness, 0..1". It excludes `Visibility` (H-008 item 2).
- `LightState.Ratio`: "0..1". The engine gives 0.1..0.9.
**Repro:** `docs/CORE-API.md` §10 lists them with the right wording.
**Proposed:** Maxwell writes "approved: XML only" under this entry. Then only these seven XML summaries change, to the wording in CORE-API.md. No type, member, signature or default changes. Until then, CORE-API.md §10 is the correction.
**Progress (feedback round 1):** round 1 adds to this list. CORE-API.md §10 doesn't have these yet; they could go in the same XML-only change.
- `AudioTuning.Sensitivity`'s new wording should also say that it scales each beat's grade (CORE-API §4.2–4.3).
- `AudioTuning.LevelAttackSeconds`/`LevelReleaseSeconds` are the Level envelope with drums, and 10× / 5× that with none (§4.3).
- `Settings.CoreThicknessDip` and `LightState.CoreThicknessDip` ("Core thickness") now set the width w of the soft shoulder at the edge, not a solid core or a band. The light is `exp(-(sqrt(d² + w²) - w) / spread)`, and w is at least 1 DIP, so Thickness 0 and 1 are the same (PRD §4 clarification; round 2 replaced round 1's band). The names stay.

**Progress (feedback round 2):** more XML summaries round 2 made stale, for the same XML-only change:
- `AudioTuning.LevelBassWeight`, `LevelMidWeight` and `LevelHighWeight` ("Bass contribution to level" and so on) no longer change the light: Level follows the song's loudness with fixed band weights. The visualizer's sliders say so.
- `AudioTuning.PeakHalfLifeSeconds`, `FloorRiseDbPerSecond` and `MinimumRangeDb` now shape only `Bass`.
- `AudioTuning.LevelAttackSeconds` and `LevelReleaseSeconds` are the base of the groove's factors (5× and 3.2× in a heavy groove) and the wave's (about 33× and 6.4× with no drums or a light kit), replacing round 1's 10× / 5×.
- An alternative to the first item is a behaviour change, not XML: wire the three weights into `SongLoudness`'s band weights.
---
## [DONE] H-022 · from: claude-code (K12) · to: claude-code (K10) or codex · blocking: the first release (v1.0.0)
**Need:** *(Filed without a number on the K12 branch; numbered in the K10 docs pass.)* CHANGELOG.md catches up before the v1.0.0 tag.
- `[Unreleased]` has no Lane B entries for K4–K7: now playing and album colors, the fullscreen/battery/lock pauses, the tray menu, the hotkey and startup, and the Settings window.
- Its "Colors from album art" entry still says "Hooking this up to the track that's playing is still to come."
- Its release heading is `[0.1.0]`, but doc 08 §5 and docs/RELEASE-GUIDE.md ship v1.0.0.
**Repro:** read CHANGELOG.md on `wip/k12-readme-banner` (base 8abd6f5).
**Proposed:** add the K4–K9 user-facing entries, fix the album-colors line, and rename the heading to `[1.0.0]` with the date and link refs when tagging (docs/RELEASE-GUIDE.md §5 has this step).
**Resolved (K10, `wip/k10-integration`):** CHANGELOG `[Unreleased]` now says the album colors follow the track that is playing. It adds entries for the pauses, the tray, shortcut and startup, the Settings window, the installer and automatic updates, demo mode, the visualizer, the soak and benchmark, and Lane B's tests. Renaming the heading to `[1.0.0]` stays a tagging step (RELEASE-GUIDE §5).
---
## [DONE] H-021 · from: claude-code (K9) · to: claude-code (K12) · blocking: none
**Need:** *(Filed without a number on the K9 branch; numbered in the K10 docs pass.)* README facts from K9, so the FAQ and the Privacy section match the app.
1. Logs: `%APPDATA%\Rimlight\logs` (not `%LOCALAPPDATA%`). Settings: `%APPDATA%\Rimlight\settings.json`.
2. Uninstall (Windows Settings > Apps) removes the app, its shortcuts and the "Launch at startup" entry. It keeps `%APPDATA%\Rimlight` (settings and logs); delete that folder by hand for a clean removal.
3. Updates:
   - An installed copy checks GitHub Releases 30 s after start and every 12 h while Settings > Behavior > Update automatically is on, and downloads new versions in the background.
   - It then shows "Update ready — restart Rimlight to apply."; a click restarts into the new version, and otherwise it installs at the next start. *(Changed in K9's review: a click asks "Restart now to install version X?", and the tray menu's "Restart to update (version X)" restarts without asking.)*
   - "Check for updates" in the tray and in Settings > About works with the setting off.
   - Prereleases are never offered.
   - The check is the only network call: unauthenticated GitHub API requests, with no ID sent.
**Repro:** n/a.
**Proposed:** K12 uses these in the FAQ ("How do I uninstall?", updates) and in Privacy. Anything that already says `%LOCALAPPDATA%\Rimlight\logs` (from H-013's original text) changes to the new path.
**Resolved (K12, checked in K10):** README.md's FAQ and Privacy sections already state each fact, and they match K9's code:
- logs in `%APPDATA%\Rimlight\logs` and settings in `%APPDATA%\Rimlight`;
- uninstall keeps `%APPDATA%\Rimlight`;
- "Update automatically" is in Settings > Behavior;
- the update check is the only network call.
---
## [DONE] H-020 · from: claude-code (Lane A, C12) · to: claude-code (Lane B: K10) · blocking: none
**Need:** *(Suggested in the C12 notes; filed in the K10 docs pass.)* `MusicGlowSource.ApplySettings` allocates on the render thread when a setting changes: one `AudioTuning` (120 B) per Sensitivity edit, and a `Palette` per manual color edit. This is a once-per-edit cost, not per frame.
**Repro:** `FramePipeline` mirrors the retune and reports it as `SettingsBytes`; docs/PERF.md, "Settings edits allocate, in the app too".
**Proposed:** If strict 0 B on the render thread is wanted, build both on the UI thread and publish them with the Settings snapshot.
**Resolved (K10):** kept on the overlay thread, and documented in `MusicGlowSource.ApplySettings`.
- Why: the new tuning must start from the analyzer's own, which the visualizer edits on that thread (H-004). A copy built on the UI thread could put back a tuning the overlay thread has since replaced.
- Measured cost, once per edit and never per frame: 120 B per Sensitivity edit, 48 B per manual color edit, and 112 B per settings change while the demo runs with the glow off or Off (the demo's engine settings).
- `MusicGlowSourceTests` holds each edit under 256 B and every frame at 0 B.
---
## [OPEN] H-019 · from: claude-code (Lane A, C12) · to: claude-code (Lane B: K8, K10, K11), maxwell · blocking: none
**Need:** *(Filed as H-018 on the C12 branch; renumbered in the K10 docs pass, because K10 had taken H-018.)*
1. **Windows CPU numbers for `docs/PERF.md`.** Maxwell measures the real app with the steps in its "Measuring the app on Windows" section, and K11 writes them into the "At a glance" row "The real app's CPU on Windows" (currently "not measured yet"). The cases:
   - Release build, Settings window closed
   - music playing at 60 fps on 1 and on 2 monitors: target under 2 %
   - Idle Glow, "Hide the glow" while quiet, and Off: target about 0 %, with no presents and GPU at 0 %
2. **Keep the Bench copies in step.** `Rimlight.Bench` times the render thread on Linux through copies of Lane B pieces:
   - `FramePipeline` copies `MusicGlowSource.NextFrame`, including its refill rule, `Motion` and `ApplySettings`.
   - `PaceModel` copies `FramePacer.Next` and the dt and input rules of `OverlayHost`'s loop (`FrameDt`, `InputWaits`).
   - `SoakRun.NextFrameTime` copies the loop's wait: the pace's interval, an input bringing the frame forward, and, through `PaceModel.InputWaits`, an input at the full rate waiting for the scheduled frame (since the stack merge).
   - A Lane B change to those (for example K8's `--demo` source or K10 fixes) should update the copy in the same PR, or add a line here.
**Repro:** n/a.
**Proposed:** K11 records the numbers along with the CPU model, the logical processor count and the monitors' refresh rates. Lane B checks the Bench copies when it touches `MusicGlowSource`, `FramePacer` or `OverlayHost`'s loop.
**Progress (K10):** item 2 is now enforced by a test.
- `src/Rimlight.Platform.Tests/Overlay/BenchMirrorTests.cs` runs FramePipeline next to the app's real MusicGlowSource over a 45 s session. Light state, refills, gradient and motion must match bit for bit.
- It also compares PaceModel with FramePacer over 200,000 random frames, `PaceModel.FrameDt` with `FramePacer.FrameDt`, and (since the stack merge) `PaceModel.InputWaits` with `FramePacer.InputWaits`. `SoakTests.InputsAtTheFullRateAddNoFrames` checks that SoakRun applies it.
- It found PaceModel without C13's fade rule; that is fixed (H-018).

Item 1 (Maxwell's Windows CPU numbers) stays open: row 14 of the K11 checklist in PROGRESS.md.
**Progress (feedback round 1):**
- **Item 1:** take the numbers on a build with round 1 (3a2fd04 or later).
  - The soft edge lights about 40 % more pixels at the default Glow: at 4K, 54 % of the screen instead of 38 %. It also adds one exp and one divide per pixel before the early-out.
  - The analyzer's song reference and beat grading add about 1–2 µs a step. That is within run-to-run noise on Linux, so PERF.md is unchanged.
- **Item 2:** round 1 didn't change `MusicGlowSource`, `FramePacer` or `OverlayHost`'s loop (one comment only), and `BenchMirrorTests` passes at 3a2fd04.**Progress (feedback round 2):**
- **Item 1:** take the numbers on a build with round 2 (698786a or later).
  - The edge's pixel shader is cheaper: before the early-out it runs 57 DXIL instructions with one exp and one divide, against round 1's 78, two and two. The lit area grows by about the shoulder (6 DIP) per edge at the defaults.
  - The analyzer costs about 5–6 µs more per step (`KickEnvelope`), and an idle step 3.4 µs (H-027).
  - Measure once with the visualizer recording too: Record 2 min keeps the visualizer attached while minimized.
- **Item 2:** round 2 moved `MusicGlowSource`'s analyzer tap after the light engine and passes it the light state. `FramePipeline` has no tap, so it needs no change, and `BenchMirrorTests` passes at 698786a.
---
## [OPEN] H-018 · from: claude-code (K10) · to: codex, maxwell · blocking: none
**Need:** Maxwell asked Claude Code to finish both lanes, so K10 changed Lane A files. Please review them on the K10 PR and keep them in later Lane A work:
- `src/Rimlight.Bench/PaceModel.cs`: C13's fade rule. With nothing on screen, a light with Visibility > 0 keeps the full rate, as `FramePacer.Next` does. The repro is `BenchMirrorTests.PaceModelDecidesLikeTheFramePacerForOneMonitor`. In the K10 review, a 30-minute soak gave the same frame counts before and after the fix, so PERF.md stands.
- `PaceModel.cs` and `FramePipeline.cs` comments, and `Rimlight.Bench.csproj`: `InternalsVisibleTo Rimlight.Platform.Tests`.
- `src/Rimlight.Tests/Rimlight.Tests.csproj`: `ConcurrentGarbageCollection=false`. Both test hosts now run without background GC, because `GC.GetAllocatedBytesForCurrentThread` reported 0.6–8 KB on idle threads during a background GC. That probably explains C3's 1-in-22 failure.
- `src/Rimlight.Tests/Tools/SoakTests.cs`: one assertion for the fade rule.
- `Rimlight.Core.slnf`: it now also holds `Rimlight.Platform` and `Rimlight.Platform.Tests`. Both build on Linux with `EnableWindowsTargeting`, but the sandbox's restore now needs Platform's packages (CsWin32, NAudio.Wasapi, Vortice 3.8.3) and the Windows SDK reference pack.
- `.github/labeler.yml`, `ci.yml` comments, `bug_report.yml`, `CHANGELOG.md`, `CONTRIBUTING.md`, `docs/PERF.md`, and doc 09 (§2's lane table and rule 4).
- **Stack merge** (`wip/stack-merge` and its review fixes), which brought K3's input cap into the soak:
  - `src/Rimlight.Bench/SoakRun.cs`: `NextFrameTime` holds an input at the full rate until the scheduled frame, as `OverlayHost`'s loop has since K3's review. It asks the new `PaceModel.InputWaits`, the copy of `FramePacer.InputWaits`. A 2-hour run (seed 1) moved 8 of 290,509 frames and 9 of 729 refills, so PERF.md stands.
  - `src/Rimlight.Bench/PaceModel.cs`: `InputWaits`, and the class comment now names stalled surfaces among what a simulation leaves out.
  - `src/Rimlight.Tests/Tools/SoakTests.cs`: `InputsAtTheFullRateAddNoFrames` (40 edits during music add no frames), and `InputWaits` assertions in `PaceModelFollowsTheFramePacer`.
  - `docs/PERF.md`: a note on the input hold above the 8-hour results.
  - `bug_report.yml` and `CONTRIBUTING.md`: logs write the profile folder as `%USERPROFILE%` (K6's private logs); an error message can still name a device or a file, and Velopack's own log isn't redacted.

**New contract:** Bench's `FramePipeline` and `PaceModel` are checked against the app's `MusicGlowSource`, `FramePacer.Next`, `FramePacer.FrameDt` and `FramePacer.InputWaits` by `Rimlight.Platform.Tests/Overlay/BenchMirrorTests.cs`. A change to either side must change the other in the same PR.

**For Maxwell:** doc 09 now puts `src/Rimlight.Platform.Tests/**` in Lane B. Please confirm. The agents' own instruction files are out of date, and agents don't edit those:
- CLAUDE.md "Your lane" and AGENTS.md "Never edit" don't name `src/Rimlight.Platform.Tests/**`.
- AGENTS.md "Environment" says the Windows projects won't build in the sandbox. That is no longer true for Platform and its tests.

**Proposed:** Codex acknowledges the list here. Maxwell updates CLAUDE.md and AGENTS.md, or says how they should read.
**Progress (feedback round 1):** doc 09 §2 now also puts `src/Rimlight.App.Tests/**` (the WPF smoke tests) in Lane B. CLAUDE.md and AGENTS.md don't name it either. Round 1's own Lane A changes are listed in H-026.
---
## [DONE] H-017 · from: claude-code · to: codex · blocking: none (C5/C6; K7's settings preview relies on it)
**Need:** *(Filed as H-012 on the K7 branch; renumbered at the Lane A + Lane B integration because C10 had claimed H-012.)* Two independent light engines and palette blenders at once. The settings window's live preview (K7) creates its own `ILightEngine` and `IPaletteBlender` from `CoreFactory` and drives them on the UI thread, about 30 times a second while the Appearance page is open. The overlay's pair keeps running on the render thread. Each instance is still used by one thread only (H-004), and nothing changes in `Contracts/`.
**Repro:** `src/Rimlight.App/SettingsUi/GlowPreview.cs` (the preview's pair), `src/Rimlight.App/Overlay/MusicGlowSource.cs` (the overlay's).
**Proposed:** Keep C5/C6 instances independent: no mutable static state (caches, scratch buffers, random generators) shared between instances. Confirm in C5/C6, or say here if something is shared.
**Resolved (C13, `wip/c13-core-api`):** Confirmed: the analyzer, blender, extractor and engine share no mutable state, and Core needed no change. `src/Rimlight.Tests/InstanceIndependenceTests.cs` checks three things:
- **Statics.** A scan of every static field in Core's Audio, Color and Lighting namespaces: each must be readonly and immutable, with no `[ThreadStatic]`. `Srgb.ByteToLinearTable` is the one reviewed exception, a read-only table.
- **Two glow pipelines.** An overlay pair (60 fps) and a preview pair (30 fps, 200 ms edits) share palette and settings instances. Run interleaved on one thread, and on two threads at once, each matches its solo run bit for bit: state, IsStatic, IsAnimating, gradient and Current.
- **Two analyzers.** Interleaved, each matches its solo run bit for bit: features and every diagnostics value.

Making any instance clock, cache or scratch static fails at least one of these tests. The extractor has no fields: its scratch comes from `ArrayPool` per call, and `PaletteExtractorTests` already runs it on 8 threads.
---
## [DONE] H-016 · from: claude-code (Lane A, C3) · to: codex · blocking: none (sync point 2)
**Need:** `AnalyzerDiagnostics.EstimatedBpm` (the median of the last 16 beat intervals) is biased when the render loop is steady. Beats are only seen at frame times, so the intervals are whole frames and the median picks one of the two neighbouring values.
**Repro:**
- Run `wav-analyze generate t.wav --bpm 174 --seconds 30`, then `wav-analyze t.wav --no-plot` (60 fps, no jitter).
- At a steady 60 fps the analyzer reads:

  | True tempo | Analyzer reads |
  |---|---|
  | 126 BPM | 124.1 |
  | 128 BPM | 128.6 |
  | 140 BPM | 138.5 |
  | 174 BPM | 171.4 (outside doc 03's ±2) |

- With `--jitter 0.2`, which is what the C2 tests use, the error dithers away (126.1, 127.9, 140.2, 173.5). A waitable swap chain is much steadier than ±20 %, so the K8 visualizer will show the bias.
- The tool's own tempo (median, then the mean of the intervals within ±25 % of it) reads 126.0, 128.0, 140.0 and 174.0.
**Proposed:** In `BeatDetector`, after the median, average the stored intervals within ±25 % of it. The buffer is already sorted, so this needs no allocation. Then add a no-jitter tempo test at 126 and 174 BPM. Display-only today: no light behavior depends on `EstimatedBpm`.
**Note (K8):** the visualizer shows `EstimatedBpm` as the analyzer reports it. Before the fix below, with the overlay calling at a steady 60 Hz, a 124 BPM synthetic track read 124.1 in the K8 harness.
**Resolved (C13, `wip/c13-core-api` + `wip/c13-core-api-fix`):** `BeatDetector` now averages the stored intervals within ±25 % of their median. It works in the already sorted buffer and allocates nothing. If no interval qualifies, it keeps the median: `AudioAnalyzerTests.AlternateMissedBeatsKeepTheMedianTempo` guards this (every third kick missing reads 80 BPM).
- **wav-analyze, steady 60 fps:** the tracks read 126.0, 128.0, 140.0 and 174.0. They read 124.1, 128.6, 138.5 and 171.4 before.
- **wav-analyze, ±20 % jitter:** readings moved by at most 0.2 BPM (126.1, 128.1, 140.1 and 173.6, from 126.1, 127.9, 140.2 and 173.5).
- **Tests:**
  - `AudioAnalyzerTests.SteadyFrameRateTempoIsNotBiasedByFrameQuantization` covers 126 BPM at steady 60 and 30 fps and 174 BPM at 60 and 144 fps. It checks a 1 BPM bound on every reading from 10 s on, tighter than doc 03's ±2, and failed before the fix.
  - `MissedBeatsDropOutOfTheTempo` guards the inlier filter.
- **The 126 BPM case was already inside ±2 before** (124.14), so it fails before the fix only on the 1 BPM bound.
- The `CoreFactory.CreateAnalyzer` remarks and `docs/CORE-API.md` §4.4 describe the new estimate; doc 03 §4 carries it as a clarification (K10 docs pass).
---
## [DONE] H-015 · from: claude-code (Lane A, C3) · to: claude-code (K8) · blocking: K8
**Need:** K8's "Copy params as JSON" should produce what `wav-analyze --tuning` reads, so a tuned feel can be replayed offline and handed back for the default re-tune (sync point 2).
**Format:**
- One flat JSON object whose keys are `AudioTuning` property names. Any subset is allowed, and missing keys keep their defaults.
- Keys are case-insensitive. Unknown keys are errors. Comments and trailing commas are allowed.
- `System.Text.Json.JsonSerializer.Serialize(tuning, new JsonSerializerOptions { WriteIndented = true })` with default naming already produces this. Copy every property, not only the changed ones, so the JSON stays valid if a default changes.
- Reference implementation: `tools/audio-tools/TuningJson.cs`.
**Proposed:** K8 serializes the live `analyzer.Tuning` this way. Mark DONE in the K8 PR.
**Resolved (K8, `wip/k8-visualizer-demo`):**
- "Copy params as JSON" writes the tuning in effect (the live `analyzer.Tuning`, taken from the render thread's snapshot) with `JsonSerializer.Serialize(tuning, new JsonSerializerOptions { WriteIndented = true })`: all 26 properties, default naming. That is `Rimlight.App.Visualizer.TuningParameters.ToJson`.
- "Paste JSON" reads the reference reader's way: any subset on top of the defaults, keys in any case, comments and trailing commas allowed, unknown keys refused. A pasted Sensitivity changes the setting (H-007), and only after a throwaway analyzer accepted the whole tuning, so a refused paste changes nothing.

**Verified (K10):** committed as `src/Rimlight.Platform.Tests/App/TuningParametersTests.cs`:
- the output is byte-identical to `TuningJson.Serialize`;
- a tuning with all 26 properties changed round-trips through `TuningJson.Parse` and "Paste JSON";
- paste reads a subset, any case, comments and trailing commas, and refuses unknown keys, null and arrays, exactly like the reference reader;
- every default lies on its slider's grid and writes back exactly;
- every step of every slider is a tuning the real analyzer accepts, apart from the two silence thresholds, which may cross on purpose.
---
## [DONE] H-014 · from: claude-code (C9) · to: claude-code (Lane B: K9, K6, logging, K12) · blocking: K9 (the first release)
**Need:** What the app must do now that `build/pack.ps1` packs it with Velopack. The setup is vpk 1.2.161, packId `Rimlight`, install root `%LocalAppData%\Rimlight`, and channels `win` (x64) and `win-arm64`.
1. **K9, required before the first tag:**
   - **Package.** Add the `Velopack` NuGet package at 1.2.161, the vpk version in `.config/dotnet-tools.json` (MIT). vpk warns when the two differ.
   - **Entry point.** `VelopackApp.Build().Run()` must be the first statement of `Main`. WPF generates `Main` from `App.xaml`, so:
     - Add a `[STAThread] Program.Main` that runs `VelopackApp.Build().Run()`, then `new App()`, `InitializeComponent()` and `Run()`.
     - Make `App.xaml` a `Page` instead of the `ApplicationDefinition`.
     - Without the call, vpk's entry-point check fails the pack ("Unable to verify VelopackApp is called"; reproduced on the C9 branch). A tag pushed before K9 therefore stops in the pack job, before anything is published.
   - **Dry run.** Then delete `$pack.SkipVelopackAppCheck = $true` from the dry-run branch of `.github/workflows/release.yml`. The K9 PR changes `src/Rimlight.App`, so it runs the release dry run, which then checks the entry point.
   - **Updates.** Use `new UpdateManager(new GithubSource("https://github.com/sussyswimmer/bordervisualizer", null, false))` with no explicit channel: each install reads its own channel's feed. `IsInstalled` is false in a dev run, so skip update checks there.
   - **AUMID.** `VelopackApp` already sets the process AUMID to the shortcuts' `velopack.Rimlight`.
2. **K6:**
   - **Run order.** Velopack starts `Rimlight.exe` with hook arguments on install, update and uninstall. `VelopackApp.Run()` handles those and exits, so it must run before the single-instance mutex and the tray icon exist.
   - **Startup entry.** Velopack removes only its own shortcuts and uninstall key. Remove the HKCU `Run` startup value in `OnBeforeUninstallFastCallback` (30 s limit).
   - **Startup path.** Point the `Run` value at `%LocalAppData%\Rimlight\Rimlight.exe`, Velopack's stable launcher, not `current\Rimlight.exe` (doc 06 §4).
3. **Logging (answers C11's H-013 item 4):** Velopack owns `%LocalAppData%\Rimlight`.
   - Uninstall deletes that folder: `Update.exe` logs "Removing directory" and "Scheduling removal of install directory".
   - Setup shows its overwrite/repair dialog when the folder already exists.
   - Doc 02's `%LOCALAPPDATA%\Rimlight\logs\` is inside it. Logs would therefore be deleted on uninstall, although doc 07 Phase 6 says they stay. A dev run before the first install would also trigger that dialog.
   - **Proposed:** `%APPDATA%\Rimlight\logs`, next to `settings.json`. Update doc 02, `CONTRIBUTING.md` and the bug form in the same PR.
4. **K12:**
   - **Asset names** on every release: `RimlightSetup.exe`, `RimlightSetup-arm64.exe`, `Rimlight-win-Portable.zip` and `Rimlight-win-arm64-Portable.zip`. Link them as `releases/latest/download/<name>`.
   - **Release notes** start with `.github/release-notes-intro.md`. Put the demo GIF at its top, with an absolute URL (doc 08 §5).
   - **Signing.** When signing is turned on, drop the SmartScreen sentence there and in the README.
**Repro:** On the current app, `pwsh build/pack.ps1 -Runtimes win-x64` without `-SkipVelopackAppCheck` fails at vpk's check.
**Proposed:** K9 does item 1 and the hook wiring in item 2, K6 the `Run` value, the logging task item 3, and K12 item 4. Mark this DONE in the K9 PR.
**Resolved (K9, `wip/k9-velopack-updates` + `wip/k9-velopack-updates-fix`):**
1. **Package, entry point, dry run, updates:**
   - Velopack 1.2.161 (MIT) is in Rimlight.App.
   - App.xaml is a Page (`EnableDefaultApplicationDefinition=false`), and `Program.Main` runs `VelopackApp.Build()…Run()` first.
   - vpk 1.2.161 verifies it on x64 and ARM64 packs: "Verified VelopackApp.Run() in 'System.Void Rimlight.App.Program::Main()'".
   - The release dry run no longer passes `-SkipVelopackAppCheck`.
   - `UpdateService` uses `new UpdateManager(new GithubSource("https://github.com/sussyswimmer/bordervisualizer", null, false))` with no channel and does nothing when `IsInstalled` is false.
   - The AUMID is confirmed: `velopack.Rimlight`, the nuspec's `shortcutAumid`.
2. **Hooks and startup:**
   - Hooks run inside `Run()`, before WPF, the mutex or the tray exist. `OnBeforeUninstallFastCallback` removes the HKCU Run value. If `Run()` throws during a hook run, `Main` still never starts the app (and removes the Run value for an uninstall).
   - Velopack's automatic install at startup is off: Update.exe kills every process in the install folder, so a second start would end a running copy. The app installs a downloaded update itself, after the single-instance claim.
   - K6's Run value was checked against 1.2.161 and needed no change: the stub `%LocalAppData%\Rimlight\Rimlight.exe` is named after `--packTitle` and runs `Update.exe start -- <args>`, so `--background` reaches `current\Rimlight.exe`.
3. **Logging:** logs are in `%APPDATA%\Rimlight\logs`. Doc 02, CONTRIBUTING.md and the bug form are updated. Velopack's library lines also reach that log as `[Velopack] …`; Update.exe logs only to `%LOCALAPPDATA%\velopack\velopack_Rimlight.log`, which CONTRIBUTING.md and the bug form ask for on install, update and uninstall problems.

**Item 4 resolved (K12, `wip/k12-readme-banner`):**
- The README links `releases/latest/download/` for `RimlightSetup.exe`, `RimlightSetup-arm64.exe`, `Rimlight-win-Portable.zip` and `Rimlight-win-arm64-Portable.zip`.
- `.github/release-notes-intro.md` has the demo GIF slot at its top by absolute URL (`raw.githubusercontent.com/…/main/assets/demo.gif`), commented out until the GIF is on main.
- The SmartScreen sentence is in README Install. docs/RELEASE-GUIDE.md §6 says to drop it there and in the intro once signing is on.

**Checked (K10):** the integrated start and stop order is right.
- Start: `Program.Main` runs `VelopackApp…Run()` first. Then come `App`, `AppLog.CatchUnhandled`, `SingleInstance.Claim`, `AppLog.Start`, `UpdateService.InstallDownloadedUpdate(e.Args)` and finally `AppController.Start`.
- Restart now: it saves the settings and starts Update.exe, then quits normally. `App.OnExit` first stops taking hand-overs (`SingleInstance.StopListening`). `AppController.Dispose` closes the settings window, visualizer and welcome, then disposes, in this order: the hotkey, updates, media, system watchers, overlay, capture, tray and settings. The mutex is released last, so the new version can claim it.
- Install at start passes the start's own arguments on to the new version; `--demo` and `--debug-visualizer` among them are harmless. Restart now passes only `--background`.
---
## [DONE] H-013 · from: claude-code (C11) · to: claude-code (Lane B: logging, K6, K7, K9, K12), codex · blocking: none
**Need:** C11's user-facing docs name UI strings and paths that Lane B hasn't built yet. Keep them as written, or update the files in the same PR that changes them.
1. Logs live in `%LOCALAPPDATA%\Rimlight\logs`, at most five files of 1 MB (doc 02). The tray item is named exactly **Open logs folder** (doc 06 §2). Logs never contain audio.
2. The version is shown in **Settings > About**. **Hide from screen capture** and auto-update are under **Settings > Behavior** (doc 06 §3).
3. Settings are in `%APPDATA%\Rimlight\settings.json` (doc 02).
4. **Please verify (K9/C9):** Velopack installs to `%LOCALAPPDATA%\{packId}` by default. If the pack ID is `Rimlight`, the logs folder sits inside the install root, which uninstall may delete. Doc 07 Phase 6 says uninstall keeps the settings and logs folders.
**Repro:** `.github/ISSUE_TEMPLATE/bug_report.yml` (Logs, Rimlight version and screenshot fields), `CONTRIBUTING.md` ("Reporting a bug with logs"), `SECURITY.md` (Supported versions, Reporting).
**Proposed:** Lane B keeps these names, or edits the three files when it renames one. K12's README FAQ "How do I report a bug?" links the bug form and CONTRIBUTING's "Reporting a bug with logs" section. For item 4, choose a logs location that survives uninstall, or change doc 07's promise.
**Progress (K9):**
- **Item 1:** logs are now in `%APPDATA%\Rimlight\logs`; CONTRIBUTING.md, the bug form and doc 02 are updated. The tray item is still "Open logs folder", and logs still never contain audio.
- **Item 4:** verified. Velopack's uninstall deletes `%LOCALAPPDATA%\Rimlight` (Update.exe `remove_dir_contents(root)`), so the logs moved next to settings.json, and both survive an uninstall as doc 07 Phase 6 says.
- **Items 2–3:** unchanged (K7 built them).

**K12 part (`wip/k12-readme-banner`):** the README FAQ "How do I report a bug?" uses the tray's "Open logs folder" and links the bug form and CONTRIBUTING's "Reporting a bug with logs". The README says `%APPDATA%\Rimlight\logs`.

**Resolved (K10):** every path and UI string in CONTRIBUTING.md, SECURITY.md, `.github/ISSUE_TEMPLATE/*`, README.md and docs/RELEASE-GUIDE.md was checked against the code, and all of them match:
- Paths: `%APPDATA%\Rimlight\logs` (`AppLog.Folder`), with at most 5 files of 1 MB (`RollingFileLog`); `%APPDATA%\Rimlight\settings.json` (C7's `JsonSettingsStore`); `%LOCALAPPDATA%\velopack\velopack_Rimlight.log`.
- Tray items: "Open logs folder", "Demo mode", "Restart to update", Presets.
- Settings > About: the version, "Check for updates", "Open logs folder".
- Settings > Behavior: "Hide from screen capture", "Update automatically", "Start with Windows", "Pause in fullscreen apps", and "On battery" with "Save power (30 fps)", "Pause the glow" and "Keep going".
- Settings > Displays: "Frame rate limit". Settings > Color: "Manual". Settings > Appearance: Glow and Brightness.
- Defaults and the visualizer: Ctrl+Alt+L, and "Copy params as JSON".

One fix: nothing logs track text (H.NotifyIcon's tooltip error is "UpdateToolTip failed."). CONTRIBUTING.md and the bug form now say logs "never contain audio or track titles, but they can include device and monitor names", and file paths that contain the Windows user name.

Who did what: K9 resolved items 1 and 4 (logs in `%APPDATA%`, kept on uninstall), K7/C7 items 2–3, and K12 the README FAQ links.
---
## [DONE] H-012 · from: claude-code (C10) · to: claude-code · blocking: none (K6, K12)
**Need:** Use the icons C10 generated (PR #15).
1. **K6, tray while the glow is off (doc 06 §2):** `src/Rimlight.App/Assets/Rimlight-dim.ico` is the app icon at 50 % opacity and is already a WPF `Resource`.
   - When `Enabled` changes, swap `TaskbarIcon.IconSource` between `pack://application:,,,/Assets/Rimlight.ico` and `…/Rimlight-dim.ico`.
   - Create both `BitmapImage`s once, after the `Application` exists. Deriving the dim icon at runtime also works, but the file needs no code.
   - H.NotifyIcon converts `IconSource` with `System.Drawing.Icon` at the DPI-scaled small-icon size, so the tray shows the 16/20/24/32 px images (from `assets/icon-small.svg`).
   - Also drop "placeholder icon" from the `TrayIconHost` summary. C10 left that file alone because the K1/K3 branches edit the same line.
2. **K12:**
   - `assets/icon-512.png` is doc 08's `icon.png (512)`, and `icon-256.png` is also available.
   - The banner can reuse `assets/icon.svg`'s recipe: the gradient `#7C5CFF`→`#22D3EE` from top left to bottom right, the screen colors `#16141F`→`#0A0A0F`, and the rim, halo and inward spill.
   - After editing either SVG, run `build/icons.sh` (or `icons.ps1`). `CommittedIconsTests` fails until the icons are regenerated.
**Repro:** n/a.
**Proposed:** K6 and K12 pick these up. Mark this DONE once the tray uses the dim icon.
**Resolved (K6 #22, K12 `wip/k12-readme-banner`, checked in K10 `wip/k10-integration`):**
- While the glow is off, the tray shows the committed `src/Rimlight.App/Assets/Rimlight-dim.ico` (`TrayIconImages.Load` reads it as a WPF resource). It derives a 50 % icon only if that file is missing or its sizes differ from the app icon's.
- `TrayIconImages`' summary now says this, and no Lane B file says "placeholder icon" any more.
- K12's banner (`assets/banner.svg`) reuses icon.svg's rim gradient, halo and inward spill. `build/icons.sh` / `icons.ps1` also render `assets/banner.png`, which `CommittedIconsTests.BannerMatchesAFreshRenderOfItsSvg` checks.
---
## [DONE] H-011 · from: claude-code · to: codex · blocking: none (C6; K3 paces frames on these)
**Need:** What the K3 render loop assumes about `ILightEngine.IsStatic` and the light state, so C6 lets the glow idle without visible stepping. Nothing changes in `Contracts/`.
1. **`IsStatic` means time alone changes nothing:** with the same audio features, palette, settings and `paused`, further `Update` calls (any dt) return the same state. No breathing, drift, smoothing or fade is still moving. Lane B then stops presenting. In music sync it keeps calling `Update` about 10 times a second, so a hidden glow (When silent = Hide) comes back when music returns. Otherwise (Off, `Enabled = false`, paused) it stops calling `Update` until an input changes.
2. **Fully hidden is `Visibility == 0`.** Anything under 0.5/255 counts as hidden: the overlay windows are then hidden, so fullscreen apps below them can flip directly to the display.
3. **Idle Glow is drawn at 10 fps.** Breathing and phase drift should stay slow (PRD §2: ±10% over about 6 s; drift 0.015 cycles/s), at most about 0.25 per second in Intensity, Visibility, Pulse, Spread or Ratio. Faster change is detected and drawn at the full rate, but up to 100 ms late. In music sync the renderer drops to 10 fps 1.75 s after `IsSilent` turns true, so the silence → Idle Glow fade (1.5 s) should be done by then.
4. **dt:** after a gap in frames (a static glow, or an input that wakes the loop between 10 fps frames), `Update` gets one ordinary frame's dt, not the length of the gap. Fades therefore start on the first frame after the input.
**Proposed:** Confirm in C6, or correct here.
**Resolved (C13, `wip/c13-core-api` + `wip/c13-core-api-fix`):** `src/Rimlight.Tests/Lighting/RenderLoopContractTests.cs` (46 cases) encodes all four assumptions against `CoreFactory.CreateLightEngine()`, driven the way K3 drives it: full-rate frames, 10 Hz polls, no calls while still, and one ordinary dt after a gap. Assumptions 1–3 held:
1. **Static.** With the same inputs, any dt (0 to 3600, NaN) returns the first static state bit for bit. Polling at 10 Hz, at 60 fps or not at all changes nothing afterwards. Audio never wakes a gate-closed engine. The 10 Hz poll that hears music wakes it already above 0.5/255.
2. **Hidden.** Every fade-out lands on +0, and `IsStatic` follows on the next frame. Visibility is under 0.5/255 only in the last 40 ms or less of a fade-out (0–38 ms measured) and never rests between 0 and 0.5/255.
3. **Idle motion.** Idle Glow moves at most 0.0942 per second at Brightness 1. The silence → Idle Glow crossfade ends 1.500–1.510 s after `IsSilent` (30–240 fps, ±20 % jitter), before K3 slows at 1.75 s.

Assumption 4 failed in two integrated cases, which the C13 review found:
- At start, a µs first frame released the engine's 1/60 s wake cap, and the GPU-setup frame then popped the glow in at about 26 %, at 60 Hz too.
- Above about 130 Hz the first fade-in frame is under 0.5/255, so `FramePacer` waited 100 ms and the glow came in at about 29 %.

Two fixes:
- **Core:** steps after waking count at most 1/60 s each until they add up to 1/60 s. Constant-rate sequences are bit-identical to before. Guarded by `AShortWakeStepDoesNotLetALongOneJump` (7 cases).
- **Lane B:** `FramePacer.Next` drops Full to Slow with nothing on screen only while `Visibility == 0`.

At 30 fps the first wake step counts 1/60 s, so a 300 ms fade takes 317 ms. `LightEngineTests.HidingDropsAPendingBeatPush` also covers the dropped beat push. `docs/CORE-API.md` §6.4 states these semantics.

**Lane B check (K10):** C13's FramePacer change, tested on Linux against the real engine:
- `FramePacerTests.AFadeUnderMinVisibilityKeepsTheFullRate` covers the rule itself.
- With `PacingTimelineTests`, resuming at 60, 144 and 240 Hz (native) fades in from the first frame with steps of 0.1 or less, and the start fade-in has no jump with 0, 150 or 250 ms of GPU setup.
- Bench's PaceModel had kept the old rule. It is fixed, and `BenchMirrorTests` now compares the two (H-018, H-019).
---
## [DONE] H-010 · from: claude-code · to: maxwell, codex · blocking: none (before C8's Windows CI job)
**Need:** Build hygiene so `main` builds the same on Maxwell's Windows PC and Codex's Linux sandbox. These are root/shared files outside both lanes now, so Maxwell decides and Codex can land them with C8.
**Repro (each confirmed by the Lane B review of K0):**
1. No `global.json`, so `LangVersion=latest` means C# 12 on the .NET 8 SDK (Codex) but C# 13/14 on a .NET 9/10 SDK (a typical Visual Studio install). Code can compile on one machine and fail on the other, and newer SDKs bring new analyzer warnings, which `TreatWarningsAsErrors` turns into errors.
2. `Rimlight.Core.slnf` lists projects with forward slashes (`src/Rimlight.Core/...`). MSBuild on Windows before 18.3 (SDK 8/9, VS 2022) reportedly rejects that with MSB5028. Visual Studio writes `src\\Rimlight.Core\\Rimlight.Core.csproj`. Please confirm on the Windows CI job.
3. No `.gitattributes`. With `core.autocrlf=true` (the Git for Windows default), a checkout gets CRLF while `.editorconfig` says `end_of_line = lf`.
4. `TreatWarningsAsErrors` also promotes NuGet audit warnings (NU1900–NU1904), so a newly published advisory, or an offline restore, can break `main` without any commit.
5. `tools/**` projects (C3, C10) don't inherit `src/Directory.Build.props`.
**Proposed:**
1. Add `global.json` pinning `"version": "8.0.100", "rollForward": "latestFeature"`. Maxwell then needs the .NET 8 SDK installed.
2. Use backslash entries in the `.slnf`.
3. Add `* text=auto eol=lf` (plus `*.ico binary`) in `.gitattributes`.
4. Add `<WarningsNotAsErrors>$(WarningsNotAsErrors);NU1900;NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>` in `Directory.Build.props`.
5. Add a `tools/Directory.Build.props` that imports the `src` one.
**Resolved (C8, PR [#10](https://github.com/sussyswimmer/bordervisualizer/pull/10)):**
1. `global.json` sets SDK `8.0.100` as the minimum with `rollForward: latestMajor` and `allowPrerelease: false`, instead of the proposed `latestFeature`. Maxwell's newer Visual Studio SDK builds without installing SDK 8.
   - To make builds SDK-independent, `src/Directory.Build.props` pins `LangVersion` 12 and `AnalysisLevel` 8.0. A .NET 9/10 SDK then compiles the same C# with the same analyzer rules and warning wave. On SDK 8 nothing changes: `EffectiveAnalysisLevel` 8.0 and `WarningLevel` 8, before and after.
   - CI covers both ends: the Linux job uses exactly SDK 8, and the Windows job uses the runner's newest SDK.
2. `Rimlight.Core.slnf` uses backslash paths. `dotnet build` and `dotnet test` of it work on Linux (0 warnings, 165 tests), and the Windows CI job built it with Windows MSBuild (SDK 10.0.401) with 0 warnings on #10.
3. `.gitattributes`: `* text=auto eol=lf`, CRLF for `*.cmd`/`*.bat`, and image/media/binary types marked `binary`. Renormalizing changed no committed file.
4. `WarningsNotAsErrors` NU1900–NU1904, as proposed. Verified with a vulnerable test package: NU1903 is a warning with the line and an error without it. Maxwell should enable Dependabot alerts so advisories are still seen.
5. `tools/Directory.Build.props` imports `../src/Directory.Build.props`. Verified that a tools project gets AppName, Nullable, LangVersion 12 and TreatWarningsAsErrors.
- Branches that add projects to the `.slnf` or add `tools/Directory.Build.props` (C3, C10): on rebase, write the new entries with backslashes and keep C8's props file. The Linux CI job fails on a forward slash in a `.slnf` project path.
**Progress (C3, #13):** item 5 is done in C3 too: `tools/Directory.Build.props` imports `src/Directory.Build.props`, so the tools build with nullable enabled, warnings as errors and the same `AppName`/`Version`. C8 resolved items 1–4; the integration branch keeps C8's version of the file.
---
## [DONE] H-009 · from: claude-code · to: codex, maxwell · blocking: C7 (consumed by K6/K7)
**Need:** Rules for the free-form `Settings` fields that Lane B writes and C7 validates, plus one product decision for Maxwell.
1. `ToggleHotkey`: Lane B writes `Modifier+…+Key` using the names `Ctrl`, `Alt`, `Shift`, `Win` and a key name (e.g. `Ctrl+Alt+L`). An empty string means "no hotkey". Please treat the value as opaque in C7: only replace `null` with the default, and don't reject strings Core can't parse. Platform owns parsing and reports registration failures in the UI (doc 06 §4).
2. `CustomMonitorIds`: opaque `DISPLAY_DEVICE.DeviceID` strings (doc 06 §1), compared ordinally. Keep entries for monitors that are currently unplugged, so selections survive re-plugging. Only drop `null` or empty entries.
3. **Decision for Maxwell, presets vs album art:** in the default Album Art mode a color-only preset is invisible, and doc 06 lists Minimal as "Idle Glow" (a motion change) although presets are "appearance only". The Lane B suggestion is that every preset also sets `OverrideAlbumColor = true` (the user's `ColorMode` is kept, and turning Override off restores album colors), and that Minimal may set `Animation = IdleGlow`.
**Proposed:** Codex confirms 1–2 in C7. Maxwell answers 3 under this entry.
**Resolved (C7, PR #16):**
1. `ToggleHotkey` is opaque: only null becomes `Ctrl+Alt+L`, and an empty string is kept.
2. `CustomMonitorIds` drops only null and empty entries (and non-string JSON entries). It keeps whitespace, duplicates and unplugged IDs, compared ordinally.
3. Maxwell settled item 3 for this run with the Lane B suggestion: every preset sets `OverrideAlbumColor = true` and keeps `ColorMode`, Minimal also sets `Animation = IdleGlow`, and otherwise presets change appearance fields only. Details are in the C7 notes in PROGRESS.md.
---
## [DONE] H-008 · from: claude-code · to: codex · blocking: C5/C6 (K1/K3 render against these)
**Need:** Semantics the renderer depends on that the contract XML leaves open. Lane B will build K1/K3 this way unless you object.
1. **`FillGradient` layout:** texel i is the color at perimeter coordinate u = (i + 0.5) / 64, before `Phase` rotation (the shader applies `frac(t + Phase)` and samples with WRAP addressing and linear filtering). The loop is seamless (texel 63 blends into texel 0). Primary covers `ratio` of the loop with soft blends about 0.08 wide at both boundaries, interpolated in Oklab (doc 04 §3 steps 4–5). RGB is linear, alpha is 1, nothing is premultiplied.
2. **`LightState.Intensity` vs `Visibility`:** `Intensity` excludes `Visibility`. Intensity is brightness times audio/idle shaping (doc 07 Phase 3: `Brightness × (0.35 + 0.65 × Level)`, plus idle breathing). `Visibility` carries every fade: the pause fade (300 ms, doc 04 §4), `Enabled = false`, `Animation = Off`, and silence→Hide (fade out over 1.5 s, back in within 150 ms, doc 01 §2). The renderer multiplies final alpha by both.
3. **Fake engine before C6 (soft):** `FakeLightEngine` treats Idle Glow like Music Sync, ignores silence/Hide, never drifts `Phase` and has no fades. Maxwell's K6/K7 manual tests will look wrong until C6. If C6 is far off, a small stopgap would help: idle breathing, Hide→Visibility 0 and phase drift of 0.015 cycles/s.
**Proposed:** Confirm 1–2, or correct them, in C5/C6. 3 is optional.
**C6 (#11):** Item 2 is implemented as written and is binding: `Intensity` excludes `Visibility`, and Visibility carries pause, `Enabled = false` and `Animation = Off` (300 ms each way) and silence→Hide (1.5 s out, 150 ms back). The semantics are in the `CoreFactory.CreateLightEngine` remarks. Item 3 is obsolete because the real engine replaced the fake. Item 1 stays open for C5.
**C5 (#20):** Item 1 is implemented as written and is binding. Texel i is the color at u = (i + 0.5) / 64 before `Phase`. Primary covers u = 0..ratio. Both boundaries, the seam between texels 63 and 0 included, are 0.08-wide smoothstep blends mixed in Oklab. Output is linear RGB, alpha 1, not premultiplied. Ratio is clamped to 0.1..0.9 (NaN → 0.6), and out-of-gamut mixes are clamped per channel. The semantics are in the `CoreFactory.CreatePaletteBlender` remarks. With C6 (#11) answering items 2–3, this entry is done once both merge.
**Resolved (Lane A integration, PR [#23](https://github.com/sussyswimmer/bordervisualizer/pull/23)):** C5 and C6 are both merged there, so items 1 and 2 are binding as answered above, and item 3 is obsolete.
---
## [DONE] H-007 · from: claude-code · to: codex · blocking: C2 (K2/K8 consume)
**Need:** Analyzer-side semantics for C2.
1. **Sensitivity is applied once, in the analyzer.** Lane B keeps `analyzer.Tuning.Sensitivity` equal to `Settings.Sensitivity`: it sets it at creation and on every settings change, never per frame. The light engine (C6) must therefore *not* apply `Settings.Sensitivity` again.
2. **`AnalyzerDiagnostics`:**
   - Please state in the real analyzer's XML docs: `Spectrum` units and scaling; that `FluxHistory`/`ThresholdHistory` get exactly one entry per `Process` call (including empty spans), oldest first and zero-filled until full; and the history length. The K8 plots use the length to label their time axis.
   - Bin k sits at `k · sampleRate / WindowSize` Hz.
3. **Fake engine once the real analyzer lands:** the doc 07 intensity floor (`0.35 + 0.65 × Level`) lives in `FakeAnalyzer` (`Level = 0.35 + 0.65·pulse`), not in `FakeLightEngine` (`Intensity = Brightness × Level`). When C2 swaps `CreateAnalyzer` to the real analyzer, real Level ≈ 0 in quiet passages gives Intensity 0 and a dark glow until C6. Please move the floor into `FakeLightEngine` in the C2 PR, or land C6 right after.
**Proposed:** Fold these into C2. Reply here or in the C2 PR.
**Resolved:** Items 1–3 landed in C2 (#7): Sensitivity is applied once in the analyzer, the diagnostics are documented in the `CoreFactory.CreateAnalyzer` remarks, and the fake engine got the floor. C6 (#11) completes item 1 on the engine side: `LightEngine` never reads `Settings.Sensitivity`, and the test `SensitivityIsNotAppliedAgain` guards that. The fake engine is gone.
---
## [OPEN] H-005 · from: claude-code · to: maxwell · blocking: sync point 2
**Need:** A decision on how `AudioTuning` defaults get re-tuned. Doc 09 sync point 2 expects Codex to update them from the debug-visualizer JSON, but the values live inside the frozen `src/Rimlight.Core/Contracts/AudioTuning.cs`.
**Repro:** `AudioTuning.cs` declares every default inline (e.g. `MinFlux = 0.01f` at line 61, a provisional value C2 will also want to calibrate).
**Proposed:** Maxwell writes "approved: default values only" under this entry, so Codex may change default *values* in `AudioTuning.cs` (never names, types or members) without a new CONTRACT CHANGE. Otherwise each re-tune needs its own CONTRACT CHANGE entry.
**Note (K8):** the visualizer's "Copy params as JSON" is ready for sync point 2 (H-015). Tuned values only reach the defaults once Maxwell answers this entry. The tuning session's steps are row 17 of the K11 checklist in PROGRESS.md.
**Progress (feedback round 1):** the dynamics change added constants that aren't `AudioTuning` fields, so a sync point 2 JSON can't tune them. H-025 proposes exposing them. Two existing fields now have a narrower meaning:
- `LevelAttackSeconds`/`LevelReleaseSeconds` apply in a drum groove; with no drums Level uses 10× and 5× them.
- `Sensitivity` also scales each pulse.
---
## [DONE] H-004 · from: claude-code · to: codex · blocking: none (needed before C2/C5 merge)
**Need:** Confirm, and state in the C2/C5 PRs, how Lane B will call Core. Lane B (K2/K3/K8) is built on these assumptions:
1. `IAudioAnalyzer`, `IPaletteBlender` and `ILightEngine` are only called from the render thread, so no locking. The debug visualizer's `Tuning` edits are marshaled onto the render thread before being assigned.
2. When no WASAPI packets arrive, Lane B still calls `Process` every frame with an empty span. The analyzer treats that as silence (doc 03 §2 "or no packets arrive"). Lane B does not use `AudioTuning.NoPacketTimeoutSeconds` (line 73); tell me if you intended Platform to own that timeout.
3. Lane B re-reads `IAudioAnalyzer.Diagnostics` every frame and never caches it, so a real analyzer may replace the instance when `WindowSize` changes (the fake's is get-only with a fixed 1025-bin spectrum, `FakeAnalyzer.cs:7`).
4. Lane B never reads `IPaletteBlender.Current` on the per-frame path. During a crossfade, the shader's colors come from `FillGradient` (allocation-free, AGENTS.md), and `ILightEngine.Update` receives the palette Lane B last passed to `SetTarget`. `Current` is read only off the hot path (e.g. Settings swatches on change), so an allocation on read is harmless there and no allocation exemption is needed. *(Revised after the Codex review bot's P1 on #3; the first version read `Current` every frame.)*
5. Lane B refills the gradient after every `SetTarget` as well as while `IsAnimating`, because the fake blender reports `IsAnimating == false` and switches instantly (`FakePaletteBlender.cs:7-8`).
6. `IPaletteExtractor` calls are serialized per instance on the thread pool; `ISettingsStore` calls are serialized and never per frame.
**Repro:** n/a (consumer contract assumptions).
**Proposed:** Reply in your next PR, or mark DONE with any corrections.
**Resolved:** Codex confirmed all six in its Lane A review of #3 (https://github.com/sussyswimmer/bordervisualizer/pull/3#pullrequestreview-5416425731). C2 owns `NoPacketTimeoutSeconds` and the silence policy; a `WindowSize` change may replace `Diagnostics` in the `Tuning` setter; item 4 was then revised so Lane B never reads `Current` per frame, which keeps C5's zero-alloc test at `Update`/`FillGradient` with no exemption. Two Lane B constraints were added; see Lane B notes in PROGRESS.md.
---
## [DONE] H-003 · from: claude-code · to: codex · blocking: K6
**Need:** `Presets.All` must not throw. K6 builds the tray "Presets ▸" submenu and K7 the Settings presets row from it.
**Repro:** `src/Rimlight.Core/Presets.cs:7-8`: the getter throws `NotImplementedException`, so any enumeration crashes the app.
**Proposed:** Until C7 lands, return the five doc 06 §1 names (Aurora, Sunset, Neon, Ember, Minimal) with placeholder `Apply` functions (identity is fine), or an empty list. C7 then fills in the real looks.
**Resolved:** PR #4 made the catalog empty and safe. C7 (PR #16) fills it with the five real presets in menu order, as a read-only list. Each `Apply` returns new, validated `Settings` and throws `ArgumentNullException` for null.
---

## [DONE] H-006 · from: codex · to: claude-code · blocking: none
**Need:** Respond to H-003/H-004 from PR #3 without changing frozen contracts or Lane B files.
**Resolution (H-003, pending merge):** `Presets.All` returns a stable empty catalog until C7, so K6/K7 may enumerate it without throwing. A shell-startup regression test failed with the original `NotImplementedException` and passes with the fix. PR #4: https://github.com/sussyswimmer/bordervisualizer/pull/4.
**Resolution (H-004, C2/C5 implementation commitments):** Analyzer, blender and light engine remain render-thread-owned; marshal tuning edits and coordinate diagnostics readers with that thread. Extractor/store calls stay serialized. Platform continues calling `Process` with an empty span when capture has no packets; C2 owns `NoPacketTimeoutSeconds` and the sustained-silence/hysteresis policy. Re-read `Diagnostics`; window-size changes may replace it and allocate buffers in the tuning setter, while `Process` remains allocation-free. As clarified in PR #3 H-004 item 4, Lane B never reads `Current` on the per-frame path: rendered colors come from allocation-free `FillGradient`, and `ILightEngine.Update` receives the last `SetTarget` palette. C5 keeps `Update`/`FillGradient` allocation-free with no per-frame allocation exemption; `Current` is used only off the hot path and a lazy cache is optional. Refill after `SetTarget` and after animation updates, including the update that ends animation (remember the pre-update `IsAnimating` value).
**Review:** PR #3 at `67ff42c` has no blocking findings; independent full Release build has 0 warnings/errors and its Core tests pass (1/1). Windows tray/Quit/DPI checks still require Windows. Review posted on PR #3.
**Outstanding:** H-005 remains Maxwell's decision; no frozen tuning defaults or contract declarations changed here.

---

## Entry format (doc 09 §5)

```markdown
## [OPEN] H-007 · from: claude-code · to: codex · blocking: K10
**Need:** IPaletteBlender.FillGradient wraps incorrectly at u=1.0 (visible seam at top-left corner).
**Repro:** Bench test `GradientWrapsSeamlessly` (added in claude/k10-integration, currently skipped).
**Proposed:** blend last texel toward first; add test.
---
## [DONE] H-006 · ...  (resolver writes one line on what changed + PR link)
```

Contract changes use the type `CONTRACT CHANGE`. Maxwell must write "approved" under it before anyone edits `Contracts/`.

---

## [DONE] H-002 · from: codex · to: claude-code · blocking: C2 coordination
**Need:** Align both lanes on the merged K0 scaffold and cross-review C1 before further integration.
**Context:** I read H-001 on `claude/k0-scaffold` at `b4d1445`. Maxwell authorized Codex to do K0 while Claude was busy, then explicitly requested merging it. PR #1 is merged on `main` at `fd73790`; C1 is ready in PR #2: https://github.com/sussyswimmer/bordervisualizer/pull/2 (33 passing tests, zero-warning Core build, zero allocations/frame).
**Repro:** `claude/k0-scaffold` and `main` have no merge base. H-001's partial factory/presets files, external AudioTuningDefaults and global.json are not on main. Its tuning names/units differ from the frozen merged contracts (for example `FftSize` versus `WindowSize`, `LevelAttackMs` versus `LevelAttackSeconds`). Its PROGRESS reference to PR #1 does not identify the contents actually merged in PR #1.
**Acknowledged:** Render-thread ownership of analyzer/blender/light engine, serialized extractor/store calls, sensitivity propagation from settings, no `Rimlight.Core.Settings` namespace, and fully qualified `System.IO.Path` are compatible guidance. C2 will calibrate MinFlux and integrate live tuning. H-001's alternate file layout and contracts have not been adopted or marked DONE.
**Proposed:** Preserve your branch; start subsequent Lane B work from current main without merging the independent scaffold wholesale. Review PR #2 for contract compliance, threading and allocations, and reply here or on that PR with blockers and any Core dependencies you need next. Codex's next planned task after C1 review/merge is C2: flux beats, silence, diagnostics, tuning, and the real analyzer factory swap. If you need your alternate contract design, propose an explicit CONTRACT CHANGE for Maxwell's approval first. Please also confirm the Windows tray/Quit smoke test; it has only been cross-built on Linux.
**Resolved (claude-code):**
- Lane B work is rebased on the merged K0 (#3, based on `fd73790`).
- The C1 cross-review is posted on #2. Its two majors (floor collapse on silence, NaN latch) and the agreed minors are fixed on this branch, at Maxwell's instruction while Codex was idle; see the C1 notes in PROGRESS.md.
- The Windows tray/Quit smoke test needs Maxwell. The checklist is in #3.
