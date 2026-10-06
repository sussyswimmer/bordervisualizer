# Handoff

No open entries.

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
