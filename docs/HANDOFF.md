# Handoff

## [OPEN] H-003 · from: claude-code · to: maxwell · blocking: sync point 2
**Need:** A decision on how `AudioTuning` defaults get re-tuned. Doc 09 sync point 2 expects Codex to update them from the debug-visualizer JSON, but the values live inside the frozen `src/Rimlight.Core/Contracts/AudioTuning.cs`.
**Repro:** `AudioTuning.cs` declares every default inline (e.g. `MinFlux = 0.01f` at line 61, a provisional value C2 will also want to calibrate).
**Proposed:** Maxwell writes "approved: default values only" under this entry, so Codex may change default *values* in `AudioTuning.cs` (never names, types or members) without a new CONTRACT CHANGE. Otherwise each re-tune needs its own CONTRACT CHANGE entry.
---
## [OPEN] H-002 · from: claude-code · to: codex · blocking: none (needed before C2/C5 merge)
**Need:** Confirm, and state in the C2/C5 PRs, how Lane B will call Core. Lane B (K2/K3/K8) is built on these assumptions:
1. `IAudioAnalyzer`, `IPaletteBlender` and `ILightEngine` are only called from the render thread, so no locking. The debug visualizer's `Tuning` edits are marshaled onto the render thread before being assigned.
2. When no WASAPI packets arrive, Lane B still calls `Process` every frame with an empty span. The analyzer treats that as silence (doc 03 §2 "or no packets arrive"). Lane B does not use `AudioTuning.NoPacketTimeoutSeconds` (line 73); tell me if you intended Platform to own that timeout.
3. Lane B re-reads `IAudioAnalyzer.Diagnostics` every frame and never caches it, so a real analyzer may replace the instance when `WindowSize` changes (the fake's is get-only with a fixed 1025-bin spectrum, `FakeAnalyzer.cs:7`).
4. `IPaletteBlender.Current` is an immutable `Palette` record, so a blended value must allocate during a crossfade. Lane B reads it once per frame. Please keep that to at most one allocation per `Update` while animating, zero when idle, and scope the zero-alloc test accordingly.
5. Lane B refills the gradient after every `SetTarget` as well as while `IsAnimating`, because the fake blender reports `IsAnimating == false` and switches instantly (`FakePaletteBlender.cs:7-8`).
6. `IPaletteExtractor` calls are serialized per instance on the thread pool; `ISettingsStore` calls are serialized and never per frame.
**Repro:** n/a (consumer contract assumptions).
**Proposed:** Reply in your next PR, or mark DONE with any corrections.
---
## [OPEN] H-001 · from: claude-code · to: codex · blocking: K6
**Need:** `Presets.All` must not throw. K6 builds the tray "Presets ▸" submenu and K7 the Settings presets row from it.
**Repro:** `src/Rimlight.Core/Presets.cs:7-8`: the getter throws `NotImplementedException`, so any enumeration crashes the app.
**Proposed:** Until C7 lands, return the five doc 06 §1 names (Aurora, Sunset, Neon, Ember, Minimal) with placeholder `Apply` functions (identity is fine), or an empty list. C7 then fills in the real looks.
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
