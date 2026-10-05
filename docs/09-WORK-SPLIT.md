# 09 — Work Split: Codex × Claude Code

Two AI agents build Rimlight in parallel. Each owns a **lane**, which is a set of folders it alone edits. They connect only through the **contracts** in §3. This doc overrides any "who does what" wording in docs 01–08.

## 1. Why the split is drawn here

| | **Lane A — Codex** ("Engine & Pipeline") | **Lane B — Claude Code** ("Windows & Experience") |
|---|---|---|
| Plays to | Well-specified, self-contained, test-verifiable tasks. Runs fine in a **Linux cloud sandbox** and can run several tasks in parallel. | Long-context integration across many files, Win32/D3D/WinRT plumbing that must run on Maxwell's real Windows PC, iterative debugging with a human tester, and product/UX writing. |
| Owns | Everything in `Rimlight.Core` (pure .NET 8, no Windows APIs), so it all builds and tests on Linux. | Everything that needs Windows to run: overlay, GPU, audio capture, media session, tray, UI. |
| Verifies by | Unit tests, benchmarks, an offline WAV analyzer, CI. | Running the app on Windows plus Maxwell's manual test checklists. |

Both lanes are sized to take roughly the same amount of agent work, about 45–55% each. If one lane runs ahead, it takes tasks from §6 (floating tasks). It never takes them from the other lane.

## 2. Ownership map (enforced)

```
LANE A — CODEX                          LANE B — CLAUDE CODE
src/Rimlight.Core/**    (except Contracts/ after freeze, see §3)
src/Rimlight.Tests/**                   src/Rimlight.Platform/**
src/Rimlight.Bench/**   (new)           src/Rimlight.App/**
tools/**                (new)           assets/**
build/**                                README.md
.github/**                              docs/08 visual items
CONTRIBUTING.md, SECURITY.md,           Phase 0 scaffold (one-time, see §4)
CODE_OF_CONDUCT.md, CHANGELOG.md

SHARED (append-only, both may edit):    docs/PROGRESS.md, docs/HANDOFF.md
FROZEN after Step 0:                    src/Rimlight.Core/Contracts/**
```

**Rules:**
1. **Never edit a file in the other lane.** If you need a change there, add an entry to `docs/HANDOFF.md` (format in §5) and work around it with a local stub until it lands.
2. Work on branches named `codex/<task-id>-<slug>` or `claude/<task-id>-<slug>`. Open PRs into `main`. Squash-merge. Use Conventional Commits.
3. **Cross-review:** before merging, the *other* agent reviews the PR (Maxwell pastes the PR link to it). The reviewer checks contract compliance, threading rules (doc 02), and allocations in hot paths. Reviews are short: blockers first, then nits.
4. `main` must always build. The Codex sandbox builds `Rimlight.Core.slnf` (Core + Tests + Bench). Windows CI builds the full `.sln`.

## 3. Contracts (frozen interface between lanes)

Claude Code writes these files in Step 0 with full signatures and XML docs. Method bodies `throw new NotImplementedException()` except where noted. After Step 0 is merged, **neither agent changes a contract** without a HANDOFF entry that Maxwell approves. Codex implements behind them, and Claude Code consumes them.

`src/Rimlight.Core/Contracts/`:

```csharp
namespace Rimlight.Core;

// ---- primitives (fully implemented in Step 0) ----
public readonly record struct Rgb(float R, float G, float B);      // linear 0..1
public sealed record Palette(Rgb Primary, Rgb Secondary, string? SourceTrackId)
{
    public static Palette Default { get; } = /* #7C5CFF / #22D3EE in linear */;
}
public readonly record struct AudioFeatures(float Level, float Bass, float Beat, bool IsSilent);
public readonly record struct LightState(
    Rgb ColorA, Rgb ColorB, float Ratio, float Intensity, float Spread,
    float CoreThicknessDip, float Phase, float Pulse, float Visibility); // Visibility 0..1 = fade in/out
public sealed record NowPlaying(string? Title, string? Artist, string? SourceApp, bool IsPlaying, string TrackId);

// ---- audio ----
public sealed record AudioTuning { /* every parameter from doc 03 with defaults */ }

public interface IAudioAnalyzer
{
    /// Called by the render thread once per frame with the newest samples (mono, any length).
    /// Must not allocate after construction.
    AudioFeatures Process(ReadOnlySpan<float> newSamples, int sampleRate, float dtSeconds);
    void Reset();                                  // on device change
    AudioTuning Tuning { get; set; }               // live-tunable (debug visualizer)
    AnalyzerDiagnostics Diagnostics { get; }       // spectrum, flux, threshold, BPM estimate (for visualizer)
}
public sealed class AnalyzerDiagnostics { /* preallocated float[] Spectrum, FluxHistory, ThresholdHistory; float EstimatedBpm; int BeatCount */ }

// ---- color ----
public interface IPaletteExtractor
{
    /// pixels: BGRA8, tightly packed. Returns null when the image is "no art" (doc 05 §1 quirk).
    Palette? Extract(ReadOnlySpan<byte> bgra, int width, int height, string trackId);
}
public interface IPaletteBlender
{
    void SetTarget(Palette target, TimeSpan duration);
    Palette Current { get; }
    bool IsAnimating { get; }
    void Update(float dtSeconds);
    /// Fills a 64-texel gradient (RGBA float, linear, premult NOT applied) for the shader. No allocation.
    void FillGradient(Span<float> rgba64x4, float ratio);
}

// ---- lighting ----
public interface ILightEngine
{
    LightState Update(float dtSeconds, in AudioFeatures audio, Palette palette, Settings settings, bool paused);
    bool IsStatic { get; }     // true when no visible change will happen until inputs change → renderer may idle
}

// ---- settings ----
// `Settings` record + enums exactly as doc 06 §1 (fully implemented in Step 0; Codex adds validation/migration/presets).
public interface ISettingsStore
{
    Settings Load();                 // never throws; backs up bad files (doc 06 §1)
    void Save(Settings settings);    // atomic write
    string Path { get; }
}
public static class Presets { public static IReadOnlyList<(string Name, Func<Settings, Settings> Apply)> All { get; } }

// ---- factory, the single seam Lane B uses ----
public static class CoreFactory
{
    public static IAudioAnalyzer CreateAnalyzer(AudioTuning? tuning = null);
    public static IPaletteExtractor CreatePaletteExtractor();
    public static IPaletteBlender CreatePaletteBlender(Palette initial);
    public static ILightEngine CreateLightEngine();
    public static ISettingsStore CreateSettingsStore(string directory);
}
```

**Fakes for Lane B to develop against before Lane A lands** (in `Rimlight.Core/Fakes/`, written by Claude Code in Step 0, deleted by Codex once real implementations exist):
- `FakeAnalyzer` returns a synthetic 120 BPM `AudioFeatures` regardless of input.
- `FakeLightEngine` maps features to state with simple linear math.
- `FakePaletteExtractor` returns the average color + a hue-rotated second color.
`CoreFactory` returns fakes until Codex swaps in real implementations, which needs no Lane B code change.

## 4. Task list and order

### Step 0 — Claude Code (small, do first, blocks everyone)
- **K0** Phase 0 scaffold (doc 07), the solution filter `Rimlight.Core.slnf`, all `Contracts/` files, `Fakes/`, and `CoreFactory` returning fakes. Create `docs/PROGRESS.md` with both lanes and `docs/HANDOFF.md`. Tray icon placeholder plus Quit. Merge to `main`. **Then tell Maxwell: "Contracts are frozen, start Codex."**

### Then both lanes run in parallel

**Lane A — Codex** (each item = one PR, each with tests)
| ID | Task | Spec |
|---|---|---|
| C1 | FFT, Hann window, band analyzer, auto-gain, envelopes | doc 03 §2 |
| C2 | Spectral-flux beat detector, silence detection, `AnalyzerDiagnostics`, `AudioTuning`, real `IAudioAnalyzer` + zero-alloc test | doc 03 §2, §5 |
| C3 | `Rimlight.Bench` console + `tools/wav-analyze`: reads a WAV, runs the analyzer offline, prints beat timestamps/BPM, and writes a CSV and a PNG plot (ScottPlot or SkiaSharp) of Level/Bass/Beat/flux/threshold. Include a synthetic-track generator (kicks at a given BPM + noise + vocals-ish sines). **This lets beat tuning happen without Windows.** | doc 03 |
| C4 | Oklab/OkLCh, k-means palette extractor, glow-ify, gamut mapping, procedural test fixtures | doc 05 §2, §4 |
| C5 | `PaletteBlender` + gradient LUT fill (zero-alloc) | doc 05 §3 |
| C6 | Real `LightEngine`: intensity formula, pulse, phase drift, idle breathing, silence fade, Visibility, `IsStatic` | doc 07 Phase 3, doc 01 §2 |
| C7 | Settings validation/clamping, JSON store (atomic, backup on corruption), version migration scaffold, Presets | doc 06 §1 |
| C8 | CI: `ci.yml` (Linux job: Core.slnf build+test; Windows job: full sln build+test), labeler, `.github/release.yml` | doc 08 §3 |
| C9 | Packaging: `build/pack.ps1` (Velopack, x64+ARM64, self-contained), `release.yml` on tag `v*` with `vpk upload github`, optional signing step gated on secrets | doc 07 Phase 6 |
| C10 | `tools/icon-gen`: SVG → multi-size `.ico` + PNGs (consumes `assets/icon.svg` from Lane B) | doc 06 §2 |
| C11 | Community health files + issue/PR templates + CHANGELOG | doc 08 §3 |
| C12 | Perf + soak harness in Bench: run analyzer + light engine for 8 simulated hours of synthetic audio, and report allocations, p99 frame cost, and memory | doc 07 Phase 7 |
| C13 | Delete `Fakes/` and make `CoreFactory` return the real implementations. Final Core API docs (`docs/CORE-API.md`). | — |

**Lane B — Claude Code**
| ID | Task | Spec |
|---|---|---|
| K1 | Overlay windows, D3D11 + DirectComposition, `Glow.hlsl`, multi-monitor/DPI, device-loss recovery, topmost re-assert | doc 04 §1–3, doc 07 Phase 1 |
| K2 | `LoopbackCapture` + lock-free ring buffer + device-change restart. Render thread drains the ring → `IAudioAnalyzer` (fake until C2 lands) | doc 03 §1, doc 02 |
| K3 | Render loop: frame pacing, waitable swap chain, idle/static optimization driven by `ILightEngine.IsStatic`, battery fps cap, render scale | doc 02, doc 04 §2 |
| K4 | `NowPlayingService` (GSMTC), thumbnail decode to 64×64 BGRA → `IPaletteExtractor`, debounce/retry quirks, tray tooltip | doc 05 §1 |
| K5 | System watchers: monitors, power/battery, lock, display-off, fullscreen detection, per-monitor pause | doc 02, doc 04 §4 |
| K6 | Tray icon + menu, single instance, hotkey, startup registration, first-run flow | doc 06 §2, §4 |
| K7 | Settings window (WPF-UI, Mica) with all 6 pages, live preview, presets row, monitor list with friendly names | doc 06 §3 |
| K8 | `--debug-visualizer` window (binds `AnalyzerDiagnostics` + live `AudioTuning` sliders + "Copy params as JSON") and `--demo` mode | doc 03 §4, doc 04 §5 |
| K9 | Velopack bootstrap in App (`VelopackApp.Build().Run()`), update checks, "Update ready" toast | doc 06 §5 |
| K10 | Integration pass once C13 is merged: run everything for real, fix seams, and file HANDOFF entries for Core issues | — |
| K11 | Hardening on real Windows: sleep/wake, RDP, TDR, hotplug, device switching. Results go in PROGRESS.md. | doc 07 Phase 7 |
| K12 | `assets/icon.svg`, `assets/banner.svg/png`, README, repo settings text for Maxwell, recording instructions | doc 08 |

### Rough order inside each lane
- Codex: C1 → C2 → C3 (in parallel with C4) → C5 → C6 → C7 → C8 → C9 → C10 → C11 → C12 → C13. C8 can go early. Codex can run independent tasks (C4, C7, C8, C11) as parallel cloud tasks.
- Claude Code: K0 → K1 → K2 → K3 → K4 → K5 → K6 → K7 → K8 → K9 → (wait for C13) → K10 → K11 → K12.

### Sync points (Maxwell's checkpoints)
1. **After K0:** start Codex.
2. **After C2 + K3:** first real beat-synced glow. Maxwell runs the debug visualizer with a few songs, and Claude Code passes the tuned JSON back to Codex via HANDOFF to update the `AudioTuning` defaults.
3. **After C6 + K7:** feature-complete. Both agents review each other's latest PRs.
4. **After C13 + K10:** release candidate. Codex runs C12 numbers, Claude Code runs K11, and then K12 ships the README.

## 5. HANDOFF.md format

```markdown
## [OPEN] H-007 · from: claude-code · to: codex · blocking: K10
**Need:** IPaletteBlender.FillGradient wraps incorrectly at u=1.0 (visible seam at top-left corner).
**Repro:** Bench test `GradientWrapsSeamlessly` (added in claude/k10-integration, currently skipped).
**Proposed:** blend last texel toward first; add test.
---
## [DONE] H-006 · ...  (resolver writes one line on what changed + PR link)
```
- Always read HANDOFF.md at the start of a session and resolve anything addressed to you before starting new work.
- Contract changes use the type `CONTRACT CHANGE`. Maxwell must write "approved" under it before anyone edits `Contracts/`.

## 6. Floating tasks (for whichever lane is ahead; claim them in PROGRESS.md first)
- **Codex-suited:** a `winget` manifest + submission PR draft. A Philips Hue Entertainment client in a new `Rimlight.Hue` project (pure .NET, DTLS via BouncyCastle) behind an `ILightSink` contract (needs a CONTRACT CHANGE). Property-based tests (FsCheck) for color math.
- **Claude-suited:** Windows Dynamic Lighting (`LampArray`) sink. Screen-color ambilight mode via `Windows.Graphics.Capture`. Microsoft Store packaging.

## 7. Keeping usage balanced
- Each agent logs a line per finished task in its section of `PROGRESS.md`: `- [x] C4 palette extractor — PR #12 — ~effort: M`. Use effort S/M/L.
- If one lane has finished 3+ more tasks than the other, Maxwell moves the next floating task to the lagging lane's *partner*, so the faster agent stays busy and the slower one isn't rushed.
