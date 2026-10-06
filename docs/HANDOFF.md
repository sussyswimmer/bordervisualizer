# Handoff

## [OPEN] H-010 · from: claude-code · to: maxwell, codex · blocking: none (before C8's Windows CI job)
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
## [OPEN] H-008 · from: claude-code · to: codex · blocking: C5/C6 (K1/K3 render against these)
**Need:** Semantics the renderer depends on that the contract XML leaves open. Lane B will build K1/K3 this way unless you object.
1. **`FillGradient` layout:** texel i is the color at perimeter coordinate u = (i + 0.5) / 64, before `Phase` rotation (the shader applies `frac(t + Phase)` and samples with WRAP addressing and linear filtering). The loop is seamless (texel 63 blends into texel 0). Primary covers `ratio` of the loop with soft blends about 0.08 wide at both boundaries, interpolated in Oklab (doc 04 §3 steps 4–5). RGB is linear, alpha is 1, nothing is premultiplied.
2. **`LightState.Intensity` vs `Visibility`:** `Intensity` excludes `Visibility`. Intensity is brightness times audio/idle shaping (doc 07 Phase 3: `Brightness × (0.35 + 0.65 × Level)`, plus idle breathing). `Visibility` carries every fade: the pause fade (300 ms, doc 04 §4), `Enabled = false`, `Animation = Off`, and silence→Hide (fade out over 1.5 s, back in within 150 ms, doc 01 §2). The renderer multiplies final alpha by both.
3. **Fake engine before C6 (soft):** `FakeLightEngine` treats Idle Glow like Music Sync, ignores silence/Hide, never drifts `Phase` and has no fades. Maxwell's K6/K7 manual tests will look wrong until C6. If C6 is far off, a small stopgap would help: idle breathing, Hide→Visibility 0 and phase drift of 0.015 cycles/s.
**Proposed:** Confirm 1–2, or correct them, in C5/C6. 3 is optional.
---
## [OPEN] H-007 · from: claude-code · to: codex · blocking: C2 (K2/K8 consume)
**Need:** Analyzer-side semantics for C2.
1. **Sensitivity is applied once, in the analyzer.** Lane B keeps `analyzer.Tuning.Sensitivity` equal to `Settings.Sensitivity`: it sets it at creation and on every settings change, never per frame. The light engine (C6) must therefore *not* apply `Settings.Sensitivity` again.
2. **`AnalyzerDiagnostics`:**
   - Please state in the real analyzer's XML docs: `Spectrum` units and scaling; that `FluxHistory`/`ThresholdHistory` get exactly one entry per `Process` call (including empty spans), oldest first and zero-filled until full; and the history length. The K8 plots use the length to label their time axis.
   - Bin k sits at `k · sampleRate / WindowSize` Hz.
3. **Fake engine once the real analyzer lands:** the doc 07 intensity floor (`0.35 + 0.65 × Level`) lives in `FakeAnalyzer` (`Level = 0.35 + 0.65·pulse`), not in `FakeLightEngine` (`Intensity = Brightness × Level`). When C2 swaps `CreateAnalyzer` to the real analyzer, real Level ≈ 0 in quiet passages gives Intensity 0 and a dark glow until C6. Please move the floor into `FakeLightEngine` in the C2 PR, or land C6 right after.
**Proposed:** Fold these into C2. Reply here or in the C2 PR.
---
## [OPEN] H-005 · from: claude-code · to: maxwell · blocking: sync point 2
**Need:** A decision on how `AudioTuning` defaults get re-tuned. Doc 09 sync point 2 expects Codex to update them from the debug-visualizer JSON, but the values live inside the frozen `src/Rimlight.Core/Contracts/AudioTuning.cs`.
**Repro:** `AudioTuning.cs` declares every default inline (e.g. `MinFlux = 0.01f` at line 61, a provisional value C2 will also want to calibrate).
**Proposed:** Maxwell writes "approved: default values only" under this entry, so Codex may change default *values* in `AudioTuning.cs` (never names, types or members) without a new CONTRACT CHANGE. Otherwise each re-tune needs its own CONTRACT CHANGE entry.
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
