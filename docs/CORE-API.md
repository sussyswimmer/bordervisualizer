# Core API

This is how Platform and App (Lane B) use `Rimlight.Core`. Core is pure .NET 8 with no Windows APIs. Lane B sees only:

- `CoreFactory`, the one construction seam;
- the contract types in `src/Rimlight.Core/Contracts/`;
- `Presets` and `AppInfo`.

Everything else in Core is `internal`. Every factory method returns the real implementation: the Step 0 fakes are gone (C13). `CoreFactoryTests` fails if a fake type comes back or the public surface changes, as a reminder to update this page.

The XML remarks on `CoreFactory` are the binding text, and this page follows them. Where a frozen contract's own XML summary disagrees (§9), this page and the factory remarks win.

## 1. At a glance

| Factory method | Returns | Call from | Allocates |
|---|---|---|---|
| `CreateAnalyzer(AudioTuning? tuning = null)` | `IAudioAnalyzer` | The render thread, every member | At construction and on a `WindowSize` change only. `Process` never allocates. |
| `AnalysisSampleRate(int captureSampleRate)` | `int` (Hz); a rate ≤ 0 throws | Any thread | No (pure) |
| `CreatePaletteExtractor()` | `IPaletteExtractor` | Any thread, concurrently; use the thread pool | Pooled scratch per thread, then about 500 B per call |
| `CreatePaletteBlender(Palette initial)` | `IPaletteBlender` | One thread per instance (the render thread; a Settings preview may own its own), every member | `Update`, `FillGradient` and an in-range `SetTarget`: never. `Current` during a fade: one `Palette` per step. |
| `CreateLightEngine()` | `ILightEngine` | One thread per instance (the render thread, one per monitor; a Settings preview may own its own) | `Update` never |
| `CreateSettingsStore(string directory)` | `ISettingsStore` | Any thread, serialized internally; never per frame | Yes (file I/O) |

None of the render-thread objects take locks. The extractor holds no state. The store locks per instance.

Cost on the shared Linux test machine (Release), for scale only:

| Call | Cost |
|---|---|
| `Process`, 48 kHz at 60 fps | About 40 µs |
| `Process`, empty span after the no-packet timeout | About 1–2 µs |
| `FillGradient` | About 0.6 µs |
| `ILightEngine.Update` | About 75 ns |
| `Extract`, 64 × 64 | About 1.5 ms |

In C12's 8-hour soak, the analyzer, blender and light engine together used 0.36 % of one core at 60 fps and allocated 0 B per frame.

## 2. Public types

| Type | Meaning |
|---|---|
| `Rgb(R, G, B)` | Linear RGB, 0..1 per channel. Core never hands out sRGB. Lane B owns its own hex ⇄ linear helper. |
| `Palette(Primary, Secondary, SourceTrackId)` | Two linear colors. `SourceTrackId` is null for a fallback or manual palette. `Palette.Default` is `#7C5CFF` / `#22D3EE` converted to linear. |
| `AudioFeatures(Level, Bass, Beat, IsSilent)` | One frame of analysis (§3). |
| `LightState(ColorA, ColorB, Ratio, Intensity, Spread, CoreThicknessDip, Phase, Pulse, Visibility)` | One frame of render state (§6). |
| `NowPlaying(Title, Artist, SourceApp, IsPlaying, TrackId)` | A Lane B data type (K4). No Core member reads it. |
| `AudioTuning` | Immutable analyzer parameters with doc 03 defaults. Assigning one replaces the whole set (§3). |
| `AnalyzerDiagnostics` | The analyzer's preallocated display buffers (§3). |
| `Settings` and the enums `AnimationMode`, `SilentBehavior`, `ColorMode`, `MonitorSelection`, `BatteryBehavior` | Doc 06 §1 settings (§7). |
| `IAudioAnalyzer`, `IPaletteExtractor`, `IPaletteBlender`, `ILightEngine`, `ISettingsStore` | The five services. |
| `Presets.All` | The five looks (§7). |
| `AppInfo.Name` | `"Rimlight"`. |

## 3. Audio analyzer (`CreateAnalyzer`)

**Construction.** `tuning` defaults to `new AudioTuning()`. Invalid tuning throws `ArgumentException` (usually `ArgumentOutOfRangeException`) naming the property. The rules:

| Field | Rule |
|---|---|
| `WindowSize` | A power of two, 1024–16384 |
| `Sensitivity` | 0.25–2 |
| `NoPacketTimeoutSeconds` | 0.03 s to 3600 s |
| `FluxHistorySeconds` | Over 0 and at most 4 s |
| `BassMinHz` < `BassMaxHz` < `MidMaxHz` < `HighMaxHz` | Strictly increasing; `BassMinHz` at least 0 |
| `Level*Weight` | 0–1 each, independent; the sum isn't checked |
| Attack and release times, `PeakHalfLifeSeconds`, `MinimumRangeDb`, `MagnitudeEpsilon`, `BeatDecaySeconds` | Positive |
| `FloorRiseDbPerSecond`, `FluxThresholdMultiplier`, `MinFlux`, `BeatRefractorySeconds`, `SilenceHoldMs` | At least 0 |
| `SilenceExitThresholdDb` | At least `SilenceThresholdDb` |
| Every float | Finite |

**`Process(newSamples, sampleRate, dtSeconds)`**

- **Arguments.** `newSamples` is mono and may be any length, including empty while no packets arrive. Call it every frame anyway (H-004). `sampleRate` ≤ 0 throws. A NaN, infinite or negative `dtSeconds` counts as 0, and a step is capped at 1 h. A new `sampleRate` resets the analysis.
- **Capture rate.** Capture at 75 kHz or more is decimated by 2, 4 or 8. `AnalysisSampleRate(rate)` gives the rate the spectrum is computed at.
- **Steps.** New audio is analyzed in equal steps of at most 1/60 s, newest last, so no latency is added. On displays faster than 240 Hz, frames shorter than 1/240 s fold into the next beat step.
- **Render stalls.** If one frame brings more than the window plus about 0.27 s of audio, only the newest window plus four steps are analyzed. The flux statistics then warm up again (0.25 s), and the tempo is kept.
- **No packets.** After `NoPacketTimeoutSeconds` (0.1 s) without packets, the gap is fed as digital silence. With the defaults, `IsSilent` turns true 2.0 s after the last packet, because the timeout counts toward the hold.

**Outputs.** Every value is finite.

- **`Level`** is the smoothed, auto-gained mix of the bass, mid and high bands (weights 0.55/0.35/0.10 by default) × `Sensitivity`, clamped to 0–1.
- **`Bass`** is the smoothed, auto-gained bass band, 0–1.
- **`Beat`** is 1 on the frame a beat fires, then decays with `exp(−dt / BeatDecaySeconds)` (120 ms).
- **`IsSilent`** turns true after the RMS stays below `SilenceThresholdDb` (−60 dBFS) for `SilenceHoldMs` (2 s), and false as soon as it rises above `SilenceExitThresholdDb` (−55 dBFS).
- **Beats** come from spectral flux in the bass band. A beat needs flux above all three of:
  - the mean + `FluxThresholdMultiplier` × σ over `FluxHistorySeconds`, with the multiplier scaled by 1/√`Sensitivity`;
  - the median flux × (1 + 9/√`Sensitivity`), so 10 × at the default `Sensitivity` of 1;
  - `MinFlux` × the window's RMS, so playback volume doesn't matter.

  No beat fires until a quarter of `FluxHistorySeconds` of flux history exists (after creation, a reset or a stall), within `BeatRefractorySeconds` of the last beat, or while the window RMS is at or below −80 dBFS.

**Sensitivity (H-007, binding).** Sensitivity is applied once, here: it scales `Level` and relaxes the beat threshold. Lane B keeps `analyzer.Tuning.Sensitivity` equal to `Settings.Sensitivity`. It sets it at creation and on each settings change, never per frame. The light engine never applies it again.

**`Tuning`.** The setter validates as above. Invalid tuning throws and keeps the previous value. A `WindowSize` change allocates, resets the analysis and replaces `Diagnostics`; other changes apply on the next `Process`. Marshal UI edits onto the render thread before assigning (H-004).

**`Reset()`** is for a device change. It clears everything except the current `IsSilent`, so a hidden glow doesn't flash on. Audio above −55 dBFS clears `IsSilent` at once.

**`Diagnostics`.** The analyzer writes these buffers in place on the render thread. Re-read the property every frame, because a `WindowSize` change replaces the instance. The debug visualizer must copy a snapshot on the render thread, never read the arrays from the UI thread (H-004).

| Member | Meaning |
|---|---|
| `Spectrum` | `WindowSize / 2 + 1` bins, DC to Nyquist, in amplitude units: \|X[k]\| × 4 / `WindowSize`, so a full-scale sine centered on a bin reads about 1. Bin k is at k × `AnalysisSampleRate(rate)` / `WindowSize` Hz. |
| `FluxHistory`, `ThresholdHistory` | 240 entries each, exactly one per `Process` call (empty spans included), oldest first, zero until filled. The flux entry is the largest flux of that call's steps, and the threshold entry is the effective threshold in the same units. Label a time axis with the real call cadence, not an assumed 60 Hz. |
| `EstimatedBpm` | The mean of the last 16 beat intervals (each at most 2 s) that lie within ±25 % of their median (H-016). Missed and extra beats drop out, and frame-quantized intervals average to the true tempo: at a steady 60 fps it reads 173.5–174.0 for 174 BPM. It stays 0 until three intervals are known, and returns to 0 after 4 s without a beat. Display only: no light behavior depends on it. |
| `BeatCount` | Beats since creation or the last `Reset`. |

## 4. Palette extractor (`CreatePaletteExtractor`)

**Input.**
- Tightly packed BGRA8 with straight alpha, both sizes positive, exactly width × height × 4 bytes. Anything else throws `ArgumentException`, and a null `trackId` throws `ArgumentNullException`.
- Pixels with alpha below 128 are skipped. Images over 512 × 512 are sampled on a grid. Pass the thumbnail at 64 × 64.

**Output.**
- Both colors are linear RGB in 0..1, glow-ified in OkLCh: lightness 0.55–0.85, chroma raised to at least 0.12 and lowered only where sRGB can't hold it.
- An artwork color below chroma 0.03 is grayscale and stays neutral, so it glows soft white.
- `SourceTrackId` is `trackId`.
- **Primary** is the best-scoring color in the art.
- **Secondary** is the best-scoring color at least 0.12 from Primary in Oklab. When there is none, Secondary is Primary turned +35° in hue and 0.08 lighter.
- **The same derived Secondary is used when Primary is colorful and that best color is neutral** (C13). An example is a dark cover with one bright logo, whose near-black background would otherwise glow grey. A grayscale Primary keeps a grayscale Secondary.

**Null ("no art", doc 05 §1).** `Extract` returns null in two cases:
1. No pixel has alpha 128 or more.
2. The palette is nearly grayscale (Primary and Secondary below chroma 0.03 before glow-ify) **and** the image is mostly one flat color: at least 60 % of the used pixels lie within Oklab distance 0.03 of the largest cluster's centroid. This is a player's generic app icon.

A grayscale photo or gradient isn't flat, so it still returns a soft-white palette. A flat colorful image isn't grayscale, so it still returns its color. On null, keep the previous palette, or the manual one.

**Threading and cost.** The extractor is deterministic and holds no state, so calls may run on any thread, even concurrently. Run it on the thread pool, never on the UI or render thread. Lane B also keeps at most one call in flight (latest track wins), which the extractor itself doesn't require.
- One 64 × 64 call takes about 1.5 ms; 512 × 512 takes under 0.1 s.
- Scratch comes from the shared array pool, per thread. The first call on a thread allocates about 85 KB at 64 × 64 (about 5 MB at 512 × 512), and later calls on that thread about 500 B.

## 5. Palette blender (`CreatePaletteBlender`)

**Threading and allocation.**
- Every member, `Current` included, belongs to the one thread that owns the instance: the render thread, or a Settings preview's UI thread for its own blender.
- `Update` and `FillGradient` never allocate, read no clock and are deterministic. `SetTarget` allocates nothing for a palette whose channels are in 0..1.
- A null palette throws. NaN channels count as 0, and out-of-range channels are clamped.

**Timing (doc 05 §3).** Lane B passes 800 ms for a new track and 200 ms for a manual edit.
- `SetTarget(target, duration)` fades from the colors on screen to the target over the whole duration: Primary to Primary and Secondary to Secondary, along straight Oklab lines.
- A fade from rest is smoothstep-eased (3t² − 2t³).
- A new target while the colors are moving carries on from where they are, without a jump. It eases out only, t(2 − t), so a color drag that calls `SetTarget(…, 200 ms)` on every edit trails the picker by about 0.1 s.
- A target with the colors the running fade is already heading to (the next track of the same album) keeps that fade's clock and only changes `Current`'s `SourceTrackId`.
- A zero or negative duration, or a target whose colors are already shown, switches at once and leaves `IsAnimating` false.
- `IsAnimating` is true from `SetTarget` until the `Update` that reaches the duration. That `Update` lands exactly on the target's colors.
- `Update` ignores NaN, negative and zero steps; an infinite step finishes the fade.

**`Current`.** While idle, it is the palette last passed in (the same instance unless a channel was clamped), and reading it allocates nothing. During a fade it is the blend, with the target's `SourceTrackId`, and the first read after each `Update` or `SetTarget` allocates one `Palette`. Keep it off the per-frame path.

**Gradient (H-008 item 1, binding).** `FillGradient(span, ratio)` writes 64 RGBA texels into the first 256 floats. Fewer floats throw `ArgumentException`, and floats after the first 256 are left alone.
- Texel i is the color at perimeter coordinate u = (i + 0.5) / 64, before the shader's `frac(t + Phase)`. The shader samples it with WRAP addressing and linear filtering.
- Primary covers u = 0..ratio and Secondary the rest.
- Both boundaries are smoothstep blends 0.08 wide, centered on them and mixed in Oklab: u = ratio, and the seam between texels 63 and 0. The loop is therefore seamless, and Primary's weight over the loop averages exactly `ratio`.
- Away from the blends, the texels are the displayed colors exactly.
- Output is linear RGB in 0..1 (out-of-gamut mixes are clamped per channel), alpha 1, not premultiplied.
- `ratio` is clamped to 0.1–0.9, and NaN uses 0.6.

**Lane B's frame loop (H-004, H-006).**
- Refill the gradient after every `SetTarget`, and after every `Update` that was animating *before* the call, so the final crossfade frame is uploaded.
- Pass `ILightEngine.Update` the palette last given to `SetTarget`; the gradient carries the crossfade.
- Never read `Current` per frame.

## 6. Light engine (`CreateLightEngine`)

**Threading and inputs.**
- Each instance belongs to one thread: the render thread, which may own one engine per monitor. A Settings preview may own another.
- `Update` never allocates, reads no clock and is deterministic.
- `dtSeconds` that is negative or non-finite counts as 0, and a step over 0.1 s counts as 0.1 s. The first step that moves time, on a new engine or after a static frame, counts at most 1/60 s, so a slow start or a wake from idle doesn't jump.
- Out-of-range or non-finite audio, colors and settings are clamped, or replaced by 0 or the `Settings` default. An undefined `Animation` acts as Music Sync, and an undefined `WhenSilent` as Idle Glow.
- A null palette or settings throws.

**Intensity and Visibility (H-008 item 2, binding).** `Intensity` excludes `Visibility`, and the renderer multiplies alpha by both.

| Mode | Intensity |
|---|---|
| Music Sync | Brightness × (0.35 + 0.65 × Level) |
| Idle Glow | Brightness × 0.9 × (1 + 0.1 × sin(2π t / 6 s)), so 81–99 % of Brightness |

`Visibility` carries every fade. Each fade is smoothstep-eased:

| Cause | Out | Back in |
|---|---|---|
| Pause, `Enabled = false`, `Animation = Off` | 300 ms | 300 ms |
| `IsSilent` in Music Sync with `WhenSilent = Hide` | 1.5 s, keeping the look it starts with | 150 ms |
| A new engine | — | 300 ms from Visibility 0 |

With `WhenSilent = IdleGlow`, `IsSilent` instead crossfades Intensity, Pulse and the drift to Idle Glow over 1.5 s and back within 150 ms, and Visibility stays 1. Switching between Music Sync and Idle Glow uses that same crossfade. `Level` already carries Sensitivity (H-007).

**Other fields.**
- `ColorA` and `ColorB` are the palette's Primary and Secondary.
- `Ratio` is `PrimaryRatio` clamped to 0.1–0.9.
- `Spread` is `Glow` (0–1; the renderer maps it to pixels).
- `CoreThicknessDip` is clamped to 0–40.
- `Pulse` is `Beat`, unsmoothed, × the Music Sync weight.
- `Phase` is in [0, 1). It drifts 0.015 cycles/s. A beat (`Beat` rising by more than 0.1; it re-arms once `Beat` falls) adds 0.01 cycles as a push that decays with τ = 120 ms.
- Idle Glow has no drift and no pulse.

**`IsStatic`.** It is true only while the light is hidden and stays hidden for the same inputs:
- the 300 ms gate fade has finished (paused, `Enabled = false` or `Animation = Off`), or
- in Music Sync with `WhenSilent = Hide`, the 1.5 s silence fade has finished.

It turns true on the second such `Update` in a row, so the frame that reaches Visibility 0 is still presented. While it is true, nothing moves with time: `Update` returns the same state for the same inputs whatever `dtSeconds` is, so the renderer may stop presenting and stop calling.

It turns false on the very `Update` whose inputs can show the light again, even with dt = 0. Those inputs are: unpaused, `Enabled`, `Animation` not Off, `IsSilent` cleared, or `WhenSilent`/`Animation` no longer hiding silence. Appearance changes (palette, Glow, Brightness, Level, Beat) leave it true. Every visible state moves, so `IsStatic` is never true while anything shows.

**Renderer guidance (doc 02, doc 04 §4).**
- While Music Sync hides silence, keep feeding the analyzer and calling `Update` at a low rate, or the return of sound goes unnoticed.
- In Off, disabled or paused, only a settings or pause change can wake the engine.
- Idle Glow only breathes (6 s), so 10 fps is enough there.
- `paused` is one flag per engine. For per-monitor pause, give each overlay its own engine with the same audio, palette and settings and that monitor's flag. Stop rendering only while every engine reports `IsStatic`.

## 7. Settings store, validation and presets

**`CreateSettingsStore(directory)`.**
- A null, empty or whitespace `directory` throws `ArgumentException`. A relative path is resolved now.
- `Path` is `<directory>/settings.json`.
- Nothing touches the disk before the first call. Calls on one store are serialized internally and may come from any thread; never call them per frame.

**`Load()`** never throws and always returns valid settings.
- A missing file gives defaults, without creating the directory.
- A file that another program holds is retried (3 tries, 20 ms apart).
- A file that still can't be read, isn't a JSON object, isn't valid UTF-8 or is over 1 MiB is copied over `settings.bad.json`, and defaults are returned.
- Otherwise each field is read on its own: a missing, mistyped or invalid field takes its default and keeps the rest.
- An older file is migrated. A newer one is read as far as this build understands it.

**`Save(settings)`.**
- It validates, creates the directory, writes a temporary file, flushes it to disk and swaps it in atomically. A crash leaves the previous file, or the complete new one if Windows' replace failed half-way.
- It always writes the current schema version (1).
- It throws `IOException` or `UnauthorizedAccessException` when the file can't be written. Lane B catches these on its debounced save and retries on the next change.

**Validation** (on load, on save, and in every preset):

| Field | Rule |
|---|---|
| `PrimaryRatio` | Clamped to 0.1–0.9 |
| `CoreThicknessDip`, `CornerRadiusDip` | Clamped to 0–40 |
| `Glow` | Clamped to 0–1 |
| `Brightness` | Clamped to 0.1–1 |
| `Sensitivity` | Clamped to 0.25–2 |
| `FpsCap` | 0 (native), 30, 60 or 120; anything else becomes 60 |
| Enums | An undefined value becomes the default |
| `PrimaryHex`, `SecondaryHex` | `#RRGGBB` or `#RGB` in either case, whitespace trimmed, stored as upper-case `#RRGGBB`. Anything else (no `#`, alpha, names) becomes the default. |
| `ToggleHotkey` | Opaque (H-009): only null becomes `Ctrl+Alt+L`. An empty string means no hotkey, and Platform parses the rest. |
| `CustomMonitorIds` | Opaque device IDs compared ordinally (H-009). Null and empty entries are dropped; duplicates, whitespace and unplugged monitors are kept. A null list becomes empty. |
| `Version` | Always the current schema version |

A NaN or ±∞ number becomes the default, and other numbers are clamped. Booleans are taken as they are.

**File format.** camelCase JSON with enums as names, for example `"animation": "MusicSync"`. It is indented UTF-8 without a BOM, and `+` and `&` are not escaped. Hand edits may use any letter case, comments, trailing commas and a BOM.

**Equality.** `Settings` is a record, but `CustomMonitorIds` compares by reference, so compare settings changes field by field.

**Presets** (`Presets.All`, in menu order). Each `Apply` returns new, validated `Settings` and throws `ArgumentNullException` for null. Every preset sets the six appearance fields below and `OverrideAlbumColor = true`, so the look shows in Album Art mode too. Turning Override off brings the album colors back. It keeps `ColorMode`, and Minimal also sets `Animation = IdleGlow` (H-009 item 3, Maxwell's decision). Corner radius, taskbar coverage and everything else are kept. For swatches, read `Apply(new Settings())`.

| Preset | Primary | Secondary | Ratio | Core (DIP) | Glow | Brightness |
|---|---|---|---|---|---|---|
| Aurora | `#14E3C5` | `#9D4DFF` | 0.60 | 6 | 0.60 | 0.85 |
| Sunset | `#FF8A2A` | `#FF4F9A` | 0.55 | 6 | 0.50 | 0.85 |
| Neon | `#FF2BD6` | `#00E5FF` | 0.50 | 12 | 0.35 | 1.00 |
| Ember | `#FF3A1F` | `#FFB01F` | 0.65 | 4 | 0.80 | 0.60 |
| Minimal | `#F4F1EA` | `#E8EDF5` | 0.50 | 1 | 0.15 | 0.55 |

## 8. Lane B's frame, in order

This is the order C12's soak simulates and K3's `MusicGlowSource` uses, all on the render thread:

1. On a new track, `blender.SetTarget(palette, 800 ms)`. On a device change, `analyzer.Reset()`.
2. `analyzer.Process(drained samples or an empty span, rate, dt)`.
3. `blender.Update(dt)`, then `engine.Update(dt, features, lastTargetPalette, settings, paused)` for each monitor's engine.
4. `FillGradient` when step 1 set a target, or when the blender was animating before step 3.
5. Read `IsAnimating` and every engine's `IsStatic` to choose the frame pacing.

## 9. Contract XML that is out of date

`Contracts/` is frozen, so these Step 0 summaries stay as written. The behavior is as this page describes:

- `IAudioAnalyzer` ("no allocations after construction"): `Process` never allocates, but assigning a `Tuning` with a different `WindowSize` allocates and replaces `Diagnostics` (§3).
- `IPaletteBlender` ("Render-thread palette transitions") and `ILightEngine.Update` ("on the render thread"): each instance belongs to one thread. That is the render thread, except for a Settings preview's own blender and engine on its UI thread (§5, §6).
- `ISettingsStore` ("the real implementation arrives in C7") and `Settings` ("validation and persistence arrive in C7"): both are in place.
- `AudioTuning.MinFlux` ("provisional K0 default"): it is relative to the window's RMS (§3). The 0.01 default stays (H-005 is still open for re-tuning defaults).
- `AudioTuning.Sensitivity` ("inversely scales the flux multiplier"): it scales the multiplier by 1/√Sensitivity, not 1/Sensitivity, so 0.25 doesn't turn beats off.
- `LightState.Intensity` ("Brightness, 0..1"): it is Brightness shaped by audio or breathing, without `Visibility` (§6).
