# Core API reference

`Rimlight.Core` is the pure .NET 8 engine: audio analysis, palette extraction and blending, the light engine and settings storage. It has no Windows, UI or WinRT references. Platform and App use it through **`CoreFactory` and the frozen contracts in `src/Rimlight.Core/Contracts/`** and nothing else. Every other type is `internal`.

This reference describes Core as of C13. It is derived from the code, its XML docs and the tests that guard each statement. The `CoreFactory` remarks carry the same rules next to the code. Where the frozen contract XML is out of date, this document and those remarks are correct (§10).

## 1. The public surface

| Type | Kind | What it is |
|---|---|---|
| `CoreFactory` | static class | Creates every Core object. The only construction seam. |
| `IAudioAnalyzer` | interface | Turns loopback samples into `AudioFeatures` each frame (doc 03). |
| `AudioFeatures` | record struct | Level, Bass, Beat and IsSilent for one frame. |
| `AudioTuning` | record | Every doc 03 parameter, live-replaceable. |
| `AnalyzerDiagnostics` | class | The analyzer's spectrum, flux and threshold histories, tempo and beat count, for the debug visualizer. |
| `IPaletteExtractor` | interface | Album art (BGRA8) to a two-color `Palette`, or null for "no art" (doc 05 §2). |
| `IPaletteBlender` | interface | Crossfades palettes and fills the 64-texel perimeter gradient (doc 05 §3). |
| `Palette` | record | Primary and Secondary in linear RGB, plus the source track. `Palette.Default` is #7C5CFF / #22D3EE. |
| `Rgb` | record struct | One linear RGB color. |
| `ILightEngine` | interface | Maps audio, palette and settings to a `LightState`, with every fade (doc 07 Phase 3, doc 01 §2). |
| `LightState` | record struct | Everything the glow shader needs for one frame. |
| `Settings` | record | The doc 06 §1 settings model. |
| `ISettingsStore` | interface | Loads and saves `settings.json`. |
| `Presets` | static class | The five built-in looks. |
| `AnimationMode`, `SilentBehavior`, `ColorMode`, `MonitorSelection`, `BatteryBehavior` | enums | Settings choices. |
| `NowPlaying` | record | Media metadata. Platform's `NowPlayingService` fills it; Core never reads it. |
| `AppInfo` | static class | `AppInfo.Name` = `"Rimlight"`, the one place Core spells the app name. |

`CoreFactoryTests.ThePublicSurfaceIsTheContractsTheFactoryPresetsAndAppInfo` fails if a public type is added or removed without updating this table.

## 2. Conventions

### Units

| Quantity | Unit |
|---|---|
| `Rgb` channels, `Palette`, `LightState.ColorA/B`, gradient texels | Linear-light sRGB primaries, 0..1, straight (not premultiplied). |
| `Settings.PrimaryHex` / `SecondaryHex` | sRGB-encoded `#RRGGBB`. Converting to linear is Lane B's helper (`SrgbHex`). |
| `sampleRate` | Hz, the capture rate. |
| `dtSeconds`, every `…Seconds` | Seconds. Core reads no clock: time comes only from `dtSeconds`. |
| `AudioTuning` band edges | Hz. Levels are dBFS, gain rates dB/s, and `SilenceHoldMs` is milliseconds. |
| `AudioFeatures.Level`, `Bass` | 0..1. Bass is normalized by auto-gain; Level is judged against the song (§4.2). Neither depends on playback volume. |
| `AudioFeatures.Beat`, `LightState.Pulse` | 0..1 pulse: each beat's grade, then a decay (§4.2). |
| `AnalyzerDiagnostics.Spectrum`, flux, thresholds | Amplitude units: \|X[k]\| × 4 / WindowSize, so a full-scale sine centered on a bin reads about 1. |
| `EstimatedBpm` | Beats per minute, 0 while unknown. |
| `CoreThicknessDip`, `CornerRadiusDip` | DIPs (1/96 inch). |
| `LightState.Phase` | Cycles around the perimeter, in [0, 1). |
| `Ratio`, `Spread`, `Intensity`, `Visibility` | 0..1. |

### Threading

| Object | Who may call it |
|---|---|
| `IAudioAnalyzer` | Only the thread that owns the instance (the render thread), for every member: `Process`, `Reset`, the `Tuning` setter and reads of `Diagnostics`. A visualizer copies the diagnostics on the render thread and hands the copy over (H-004). |
| `IPaletteBlender` | Only its owning thread, `Current` included. |
| `ILightEngine` | Only its owning thread. |
| `IPaletteExtractor` | Any thread, also concurrently. Run it on the thread pool, never on the UI or render thread. |
| `ISettingsStore` | Any thread. Calls on one store are serialized internally. Never per frame. |
| `Presets.All`, `Apply` | Any thread. Pure functions. |
| `Settings`, `Palette`, `AudioTuning`, `AudioFeatures`, `LightState`, `Rgb`, `NowPlaying` | Immutable values. Share them freely and publish new ones by reference (doc 02). |

No member takes a lock on a per-frame path. **Instances are independent (H-017).** No analyzer, blender, extractor or engine keeps mutable static state: no caches, scratch buffers or random generators. Several instances can therefore run side by side, each on its own thread, with each giving exactly what it would give alone. Examples are one engine per monitor on the render thread, the settings preview's engine and blender on the UI thread, and a second analyzer. `InstanceIndependenceTests` checks this bit for bit, interleaved on one thread and on two threads at once, and scans every static field in Core's Audio, Color and Lighting code.

### Allocation

| Call | Allocates |
|---|---|
| `IAudioAnalyzer.Process`, `Reset` | Never, after construction. |
| `IAudioAnalyzer.Tuning` setter | Only when `WindowSize` changes: new buffers and a new `Diagnostics` instance. |
| `ILightEngine.Update` | Never. |
| `IPaletteBlender.Update`, `FillGradient` | Never. |
| `IPaletteBlender.SetTarget` | Never for a palette with every channel in 0..1. Otherwise it allocates one clamped copy. |
| `IPaletteBlender.Current` | Never while idle. During a fade it allocates one `Palette` on the first read after each `Update` or `SetTarget`. |
| `IPaletteExtractor.Extract` | The result plus about 500 B. The first call on each thread rents its scratch from `ArrayPool<T>.Shared`: about 85 KB at 64 × 64, about 5 MB at 512 × 512. |
| `ISettingsStore`, `Presets.Apply` | Yes. They are not per-frame calls. |

### Bad input

Per-frame calls sanitize bad frame data (dt, samples, colors, settings) instead of throwing. They throw only for arguments no correct caller passes: null, a sample rate of 0 or less, an invalid tuning, or a buffer of the wrong size.

| Input | Treatment |
|---|---|
| `dtSeconds` NaN, ±∞ or negative | Counts as 0. The analyzer caps dt at 1 h and the engine at 0.1 s (§6). |
| NaN or ±∞ samples, or samples beyond ±16 | Become 0, or are clamped to ±16. |
| Palette channels NaN or outside 0..1 | NaN becomes 0, and the rest are clamped. |
| `Settings` numbers out of range or non-finite, undefined enums | The engine clamps them, or uses the field's default. |
| `null` palette, settings or `trackId`, or `null` assigned to `Tuning` | `ArgumentNullException`. |
| `sampleRate` ≤ 0 | `ArgumentOutOfRangeException`. |
| Invalid `AudioTuning` | `ArgumentException` / `ArgumentOutOfRangeException` naming the property. The previous tuning is kept. |
| Wrong BGRA buffer size, gradient span under 256 floats | `ArgumentException`. |

## 3. CoreFactory

| Member | Returns |
|---|---|
| `CreateAnalyzer(AudioTuning? tuning = null)` | A new analyzer, with `new AudioTuning()` if null. Throws if `tuning` is invalid (§4.3). |
| `AnalysisSampleRate(int captureSampleRate)` | The rate the spectrum is computed at: the capture rate, divided by 2 from 75 kHz, by 4 from 150 kHz and by 8 from 300 kHz (88.2 kHz → 44.1 kHz, 96–384 kHz → 48 kHz). Throws for ≤ 0. |
| `CreatePaletteExtractor()` | A new extractor. It is stateless. |
| `CreatePaletteBlender(Palette initial)` | A new blender showing `initial`, with no fade running. |
| `CreateLightEngine()` | A new engine. It starts hidden and fades in over 300 ms. |
| `CreateSettingsStore(string directory)` | A store for `<directory>/settings.json`. Nothing is read or written until the first call. A relative path is resolved now, and a blank one throws. |

Each call returns a new instance of the real implementation. The K0 fakes were removed one by one as C2–C7 landed, and C13 confirmed that none remain (`CoreFactoryTests.EveryFactoryMethodReturnsTheRealImplementation`, `NoFakesAreLeftInCore`).

## 4. Audio: `IAudioAnalyzer`

### 4.1 Process

`AudioFeatures Process(ReadOnlySpan<float> newSamples, int sampleRate, float dtSeconds)` is called once per render frame with every mono sample that arrived since the last frame.

- **Any length works,** including empty. With no packets, call it with an empty span each frame. After `NoPacketTimeoutSeconds` (0.1 s) the gap counts as digital silence: zeros flow through the window, so `IsSilent` arrives 2.0 s after the last packet. Once the window is all zeros, the FFT is skipped (about 1–2 µs a frame).
- **Steps.** New audio is analyzed in equal steps of at most one 60 fps frame of audio (800 samples at 48 kHz). The newest samples always go last, so splitting adds no latency. Frames shorter than 1/240 s fold into the next beat step.
- **High rates.** Capture at 75 kHz or above is decimated, so the bass band keeps about 5 bins (§3).
- **Stalls.** More new audio than the window plus 16 steps in one call (about 0.3 s at 48 kHz) means the render loop stalled. Only the newest window plus four steps is analyzed. The flux statistics warm up again (0.25 s), and the tempo is kept.
- **Sample-rate change.** The first call at a new rate resets the analysis, like `Reset`.
- **Cost** (Linux test machine, informational): about 40 µs for a 60 fps frame at 48 kHz, with 0 bytes allocated. The song reference, beat grading and wave (§4.2) add a few hundred multiply-adds a step (mostly the loudness histogram's 240 bins), 1–2 µs; side by side on a style track the difference was within this machine's run-to-run noise (113–168 µs a frame with the light engine, against 104–146 µs before, in alternating runs).

### 4.2 AudioFeatures

| Field | Meaning |
|---|---|
| `Level` | 0..1. How loud this moment is within the song, moving like a slow wave without drums and with the groove with them (below), times `Sensitivity`, clamped. |
| `Bass` | 0..1. The normalized bass band through the Bass envelope. |
| `Beat` | The beat's grade, 0..1 (below), on the frame it fires (or on a later frame of its onset window, 35 ms of audio, if the grade grows), then decays by exp(−dt / `BeatDecaySeconds`) each frame. A kick in a loud passage reads exactly 1. |
| `IsSilent` | True once the raw RMS has stayed below `SilenceThresholdDb` for `SilenceHoldMs`. False again as soon as it rises above `SilenceExitThresholdDb`. |

**Character and dynamics (Maxwell's feedback, round 1).** Calm songs must move like a slow wave and never bounce on a whisper; rap and EDM keep bouncing on every kick; soft drums bounce softly; a bass guitar or a piano is not a drum; drums coming in make a song punchier; a chorus is brighter than a quiet verse. Three internal stages do this behind the frozen contract, with internal constants and fixed internal bands (no `AudioTuning` field or default changed, and live-tuning the band edges doesn't move them):

- **Song reference** (`SongLoudness`). The momentary loudness (power in the fixed 30–150 / 150–2000 / 2000–12000 Hz bands weighted 0.5 / 1 / 1.5, smoothed with τ = 0.25 s) goes into a histogram that forgets with τ = 10 s. Its 90th percentile is the song's loud parts. The **position** is 1 there and falls linearly to 0 at 24 dB below; it is 0 in near-silence (the auto-gain hold level, `SilenceThresholdDb` − 20 dB), and every window above that counts as music, so the chain stays scale-invariant at quiet playback. A verse 16 dB under the chorus sits at about a third for about 20 s, where doc 03's 4 s auto-gain had stretched it back to the full range within about 6 s. Until the memory fills, a prior says the loud parts are 4 dB above the loudest moment so far (4 s of virtual loudness that fades with the memory), so a quiet opening isn't judged as the top.
- **Songs.** 1 s of near-silence (the digital silence between songs, or no packets) clears the memory, so a song is judged against itself. A session reference follows the song reference (τ = 30 s, from about 16 s into a song) and survives the gap; the next song starts with it as a floor under its reference that falls 0.2 dB/s. Its quiet opening is then judged against the songs before it (the style tracks' folk band after itself: a mean pulse of 0.57 in its first verse, against 0.82 as the first song). A quieter master can't be told from a quiet opening, so it is brought up as gradually: rap after itself 10 dB down has its full bounce from 5 s and its brightness from about 25 s, 14 dB down its bounce over about 25 s and its brightness over about 40 s. The loudness normalization most streaming apps apply keeps songs within a few dB of each other. The first song after launch has no session.
- **Kick-likeness and punch** (`BeatCharacter`). A detected beat's prominence is the rise of a fixed 30–100 Hz kick band over its onset window (35 ms of audio, from the lowest level of the two steps before the beat) against the amplitude of the music above the bass band just before it (the larger of 0.25 s and 1.5 s averages), in dB; kick-likeness is smoothstep(4 dB, 9 dB) of it. Measured medians on the style tracks: 808s 13.5 dB, EDM kicks 19, acoustic kicks 9.6, a brush kit at the music's level 17, 12 dB under it 7, 20 dB under it 4; a bass guitar 6 dB under the music −3, a piano −9.5, a picked guitar −19, a voice and guitar −31. A drive follows the kick-likeness of the recent onsets (half the way toward a more kick-like one, a tenth of the way toward a less kick-like one; 3 s after the last kick-like onset it fades with τ = 2.5 s), and **punch** = smoothstep(0.3, 0.75, drive): 1 in a drum groove, a fraction with soft drums, 0 with none or with a bass guitar or a piano under the music. The first second of audio after near-silence only listens.
- **Beat grade.** The product of kick-likeness; the punch before this onset (so one kick-like onset in a wave gives no pulse, the next about 0.4, the third its full size); *accent*, the rise over 0.85 × the strongest recent kick's (halving every 1.5 s), capped at 1; *loud*, the position held over the dip between beats (falling 0.25 a second) through smoothstep(0.3, 0.6), so nothing 17 dB or more under the song's loud parts and full size within about 10 dB of them, but never under 0.3 × punch (a groove keeps that much bounce through a quiet passage or a volume drop); and `Sensitivity`, clamped to 1.

On the style tracks (`tools/audio-tools/StyleTracks.cs`, `MusicDynamicsTests`): every rap kick and 73–76 of 77 EDM kicks get a pulse of at least 0.7. The drumless ballads, the folk band with a bass guitar 6 dB under it and the piano ballad give no pulse at all (round 1: up to 12 big pulses per chorus on the last two). A brush kit 12 dB under the folk band gives at most 0.18 under its strummed choruses, apart from one full pulse where each chorus comes in (round 1: 12–14 pulses of 0.5 or more per chorus), and about 0.25 in the quiet verse; 20 dB under it, one pulse over 0.3 in the whole song, where the first chorus comes in. With the kit at the music's level, the whispered verse after the chorus gets about 0.3 per kick. Limits: the first verse of the first song after launch has nothing louder to be judged against, so a soft kit that stands out of a sparse verse still bounces there (0.82 on average); a bass guitar playing alone, or as loud as the rest of a drumless song, reads as the rhythm and pulses.

**Level.** The target is min(1, position / 0.8) × (1 − 0.6 × (1 − doc 03's Level target)), where doc 03's target is the weighted auto-gain bands (the contrast within the last few seconds). The knee keeps the loud parts at full height (within about 5 dB of the reference). The envelope's time constants are `LevelAttackSeconds` × 10^(1 − punch) and `LevelReleaseSeconds` × 5^(1 − punch): 0.3 s / 1.25 s with no drums, doc 03's 30 / 250 ms in a groove, and doc 03's in near-silence. On the style tracks the drumless ballad's brightness has 0.2–0.7 % of its variance above 3 Hz (doc 03's Level with full pulses gave 6.6–10.2 %), and a chorus is 1.58× as bright as the whispered verse after it (1.19×).

**Volume.** The whole chain is scale-invariant: the same song at −20 dB or −35 dB gives the same light from 2 s (doc 03 §5; within 0.001 of Intensity measured). A volume change in the middle of a song shifts every loudness at once, and within a second a song's own band balance moves by 2–7 dB, so it can't be told from the music getting quieter or louder: the light looks as before once the reference has turned over. A step of up to about 5 dB barely shows in the loud parts (rap 4 dB down: within 0.03 of Intensity). Larger steps are back within 0.1 of Intensity after up to 25 s (rap: 23.5 s after −20 dB, 6 s after +20 dB; ballad: 16.9 s after −6 dB, 13.6 s after +6 dB; a step up at the start of a quiet verse shows until the next chorus, up to 30 s), where doc 03's auto-gain took 0.7–15 s. Meanwhile a groove keeps its kicks: in the 10 s after the rap drops 10 dB they average a pulse of 0.73; after it drops 20 dB, 0.21 (the strongest-kick reference takes a few seconds to come down), then the groove's floor of 0.3.

**Beats.** Detection is doc 03's and unchanged: a beat fires (`BeatCount` rises) when the bass-band spectral flux exceeds the effective threshold, whatever its grade. That threshold is the largest of three values over the last `FluxHistorySeconds`:

- mean + k·σ, with k = `FluxThresholdMultiplier` / √Sensitivity;
- the median flux × (1 + 9 / √Sensitivity), a guard against steady noise;
- `MinFlux` × the window's RMS.

The detector also needs the history to be warm (a quarter of `FluxHistorySeconds` of real time), the window above the near-silence level (`SilenceThresholdDb` − 20 dB, −80 dBFS), and `BeatRefractorySeconds` since the last beat. Volume doesn't change which beats fire.

**Sensitivity is applied once, here (H-007).** It multiplies `Level` and each beat's grade, and lowers both beat criteria. The light engine never applies it again. Keep `analyzer.Tuning.Sensitivity` equal to `Settings.Sensitivity`: set it at creation and on each settings change.

### 4.3 AudioTuning

Defaults are doc 03's. Setting `IAudioAnalyzer.Tuning` validates the new value, and an invalid one throws and keeps the old one.

| Property | Default | Valid | Meaning |
|---|---|---|---|
| `WindowSize` | 2048 | Power of two, 1024–16384 | FFT window in samples. A change reallocates, resets the analysis and replaces `Diagnostics`. |
| `BassMinHz` / `BassMaxHz` | 30 / 150 Hz | ≥ 0, strictly increasing | Bass band (beats, `Bass`). A band narrower than one bin uses the bin below its center. The song reference and kick-likeness use fixed bands of their own (§4.2). |
| `MidMaxHz` / `HighMaxHz` | 2000 / 12000 Hz | Strictly increasing | Mid is [BassMax, MidMax), High is [MidMax, HighMax], for doc 03's Level target. |
| `MagnitudeEpsilon` | 1e-9 | > 0 | Added before converting to dB. |
| `PeakHalfLifeSeconds` | 4 s | > 0 | Auto-gain peak half-life. Auto-gain normalizes `Bass` and the local contrast inside `Level`; the song-level reference is internal (§4.2). |
| `FloorRiseDbPerSecond` | 1 dB/s | ≥ 0 | Auto-gain floor rise. The floor drops at once, and holds through near-silence. |
| `MinimumRangeDb` | 12 dB | > 0 | Smallest normalization range. |
| `LevelAttackSeconds` / `LevelReleaseSeconds` | 0.030 / 0.250 s | > 0 | Level envelope with drums; 10× and 5× these with none (§4.2). |
| `BassAttackSeconds` / `BassReleaseSeconds` | 0.015 / 0.180 s | > 0 | Bass envelope. |
| `LevelBassWeight` / `LevelMidWeight` / `LevelHighWeight` | 0.55 / 0.35 / 0.10 | Each 0..1 | Level target = clamp(Σ weight × band, 0, 1). They need not sum to 1. |
| `FluxHistorySeconds` | 1 s | Above 0, at most 4 s | Window for the flux statistics. |
| `FluxThresholdMultiplier` | 1.5 | ≥ 0 | k in mean + k·σ, before the Sensitivity scaling. |
| `MinFlux` | 0.01 | ≥ 0 | Relative to the level: a beat needs flux > `MinFlux` × the window RMS. |
| `BeatRefractorySeconds` | 0.180 s | ≥ 0 | Shortest time between beats. |
| `BeatDecaySeconds` | 0.120 s | > 0 | Time constant of `Beat`'s decay. |
| `Sensitivity` | 1 | 0.25–2 | Scales `Level` and each beat's grade, and relaxes the beat threshold (§4.2). |
| `NoPacketTimeoutSeconds` | 0.100 s | 0.03 s–1 h | Empty frames for this long count as digital silence. |
| `SilenceThresholdDb` | −60 dBFS | Finite | `IsSilent` entry level. The near-silence gate sits 20 dB below it. |
| `SilenceExitThresholdDb` | −55 dBFS | ≥ the entry level | `IsSilent` exit level (hysteresis). |
| `SilenceHoldMs` | 2000 ms | ≥ 0 | Time below the entry level before `IsSilent`. |

`AudioTuning` is a record: change it with `analyzer.Tuning = analyzer.Tuning with { … }` on the render thread. The debug visualizer's "Copy params as JSON" and `wav-analyze --tuning` share one format: a flat JSON object of these property names (H-015).

### 4.4 Diagnostics

`IAudioAnalyzer.Diagnostics` holds preallocated arrays that the analyzer rewrites in place during every `Process`. A `WindowSize` change replaces the instance, so read the property each frame and never cache it. Read it only on the render thread, copying what another thread needs.

| Member | Contents |
|---|---|
| `Spectrum` | WindowSize / 2 + 1 bins, from DC to Nyquist, in amplitude units. Bin k is at k × `CoreFactory.AnalysisSampleRate(rate)` / WindowSize Hz. |
| `FluxHistory` | 240 entries, exactly one per `Process` call (empty spans included), oldest first and 0 until filled. Each entry is the largest flux among the steps of that call's new samples, and 0 on a frame without new samples. A time axis must use the real call cadence. |
| `ThresholdHistory` | 240 entries in step with `FluxHistory`: the effective beat threshold after each call, in flux units. |
| `EstimatedBpm` | 60 / the mean of the last 16 intervals (each up to 2 s) between tempo beats that lie within ±25 % of their median. Where kicks play (a kick-like beat within the last 4 s, §4.2), only kick-like beats whose rise is at least 0.6 × the strongest recent kick's are tempo beats, timed when they fired: four-on-the-floor with a bass note on every off-beat reads 128, not 256 (before Maxwell's feedback round 1: 246–257). Where none play, every beat is a tempo beat, as in doc 03 (the tempo of a drumless song or a soft-drum mix is no more reliable than before). Missed and extra beats drop out, and a steady frame rate doesn't bias it (H-016: 126 BPM at a steady 60 fps reads 125.8–126.0, not 124.1). If no interval lies that close, which needs an even count split far apart (every other beat missed), it is 60 / the median. It is 0 until three intervals are in, and returns to 0 after 4 s without a tempo beat. |
| `BeatCount` | Beats since construction, `Reset` or a `WindowSize` change, every detected beat whatever its grade (a sung syllable the detector fired on counts, with a Beat of 0). |

`EstimatedBpm` and `BeatCount` have public setters only because the contract class is shared. Treat them as read-only.

### 4.5 Reset

`Reset()` clears everything except the current `IsSilent`: spectrum, histories, tempo, beat count, envelopes, auto-gain and the beat pulse. Call it on a device change. A device change while nothing plays therefore doesn't flash a hidden glow back on for 2 s. Audio above `SilenceExitThresholdDb` (−55 dBFS) clears `IsSilent` at once.

## 5. Color

### 5.1 IPaletteExtractor

`Palette? Extract(ReadOnlySpan<byte> bgra, int width, int height, string trackId)`

- **Input.** Tightly packed BGRA8 with straight alpha, exactly width × height × 4 bytes, with both sizes positive. Otherwise it throws `ArgumentException`. Pixels with alpha below 128 are skipped. An image with more than 512 × 512 pixels is sampled on a regular grid (every 2nd, 3rd, … pixel each way).
- **Method (doc 05 §2).** k-means with k = 5 in Oklab, seeded from a fixed seed. Clusters closer than 0.05 are merged, then each group is scored by share^0.6 × (0.25 + chroma) × a lightness fitness. Primary is the best score. Secondary is the best score at least 0.12 away from Primary, or else Primary turned +35° in hue and 0.08 lighter.
- **Output.** Both colors are glow-ified in OkLCh to lightness 0.55..0.85 and chroma of at least 0.12, then gamut-mapped into sRGB. Every channel is linear and in 0..1. A grayscale artwork color (chroma below 0.03) stays neutral and glows soft white. `SourceTrackId` is `trackId`.
- **Null means "no art"** (doc 05 §1). It is returned when no pixel has alpha of 128 or more, or when both palette sources are below chroma 0.03 *and* at least 60 % of the pixels lie within Oklab distance 0.03 of the largest cluster, as with a player's generic icon. Keep the previous palette, or the manual one, then.
- **Deterministic and stateless.** Calls may run concurrently. One call takes about 1.5 ms at 64 × 64, so pass the 64 × 64 thumbnail and keep one call in flight (latest track wins).

### 5.2 IPaletteBlender

**Crossfade (doc 05 §3).**
- `SetTarget(target, duration)` fades from the colors on screen at that moment to the target, over the whole duration. Primary fades to Primary and Secondary to Secondary, along straight lines in Oklab. Use 800 ms for new album colors and 200 ms for a manual edit.
- **Easing.** A fade from rest is smoothstep-eased. A fade that takes over while the colors are moving eases out only, t(2 − t), so a color drag that calls `SetTarget` on every edit trails the picker by about 0.1 s.
- **Special targets.** A target with the colors the running fade is already heading to (the next track of the same album) leaves that fade alone and only changes `Current`'s track. A zero or negative duration, or a target whose colors are already displayed, switches at once and leaves `IsAnimating` false.
- `IsAnimating` is true from `SetTarget` until the `Update` that reaches the duration. That `Update` lands exactly on the target's colors.
- `Update(dt)` ignores NaN, negative and zero steps. An infinite step finishes the fade.

**Current.** While idle it is the palette last passed to `SetTarget` (or `initial`), the same instance unless a channel had to be clamped. During a fade it is the blend, carrying the target's `SourceTrackId`. Read it off the per-frame path (H-004). The engine gets the palette last passed to `SetTarget`, and the gradient carries the crossfade.

**Gradient (H-008 item 1, binding).** `FillGradient(rgba64x4, ratio)` writes 64 RGBA texels into the first 256 floats of the span. Anything after them is left alone, and a shorter span throws.

| Rule | Value |
|---|---|
| Texel i | The color at perimeter coordinate u = (i + 0.5) / 64, *before* the shader's `frac(t + Phase)`. Sample it with WRAP addressing and linear filtering. |
| Layout | Primary covers u = 0..ratio (centered on ratio / 2) and Secondary the rest. |
| Boundaries | Both boundaries, u = ratio and the seam u = 0 ≡ 1 between texels 63 and 0, are smoothstep blends 0.08 wide, centered on the boundary and mixed in Oklab. The loop is seamless, and Primary's weight over the loop averages exactly `ratio`. |
| Away from the blends | The displayed colors exactly. |
| Format | Linear RGB clamped per channel to 0..1, as `saturate` would; alpha 1; not premultiplied. |
| `ratio` | Clamped to 0.1..0.9, so both colors keep a pure middle. NaN uses 0.6. |

**When to refill.** Refill after every `SetTarget`, after every `Update` that was animating *before* the call (so the last crossfade frame is uploaded, H-006), and when the ratio changes. One fill takes about 0.6 µs.

### 5.3 Palette and Rgb

`Palette(Rgb Primary, Rgb Secondary, string? SourceTrackId)` is immutable. `SourceTrackId` is null for a fallback or manual palette. `Palette.Default` is #7C5CFF / #22D3EE in linear RGB: (0.2016, 0.1070, 1.0) and (0.0160, 0.6514, 0.8550).

## 6. Lighting: `ILightEngine`

`LightState Update(float dtSeconds, in AudioFeatures audio, Palette palette, Settings settings, bool paused)` is called once per frame, with the palette last passed to `SetTarget`, the current settings snapshot and this engine's pause flag. The music's character reaches it only through `Level` and the graded `Beat` (§4.2). One update costs about 70 ns and allocates nothing.

### 6.1 LightState

| Field | Value |
|---|---|
| `ColorA`, `ColorB` | The palette's Primary and Secondary, clamped to 0..1. |
| `Ratio` | `PrimaryRatio`, clamped to 0.1..0.9. |
| `Intensity` | Music Sync: Brightness × (0.35 + 0.65 × Level). Idle Glow: Brightness × 0.9 × (1 + 0.1 sin(2π t / 6 s)), which breathes between 81 % and 99 % of Brightness. Between the two it crossfades (§6.2). **Excludes Visibility.** |
| `Spread` | `Glow`, 0..1. The renderer maps it to pixels. |
| `CoreThicknessDip` | `CoreThicknessDip`, clamped to 0..40. |
| `Phase` | In [0, 1). It drifts 0.015 cycles/s, and each beat adds 0.01 cycles × the height `Beat` rises to, as a push that decays with τ = 120 ms (the same total at any frame rate). A beat is `Beat` rising by more than 0.1, re-armed once it falls; a rise that continues over the next frames adds its share. Both scale with the Music Sync weight, so Idle Glow has no drift. |
| `Pulse` | `Beat` (the graded pulse), unsmoothed, times the Music Sync weight. |
| `Visibility` | 0..1. Carries every fade (§6.2). |

**Intensity and Visibility (H-008 item 2, binding).** The renderer multiplies alpha by both. Undefined enum values act as the defaults. Non-finite settings use the `Settings` defaults (Brightness 0.8, Glow 0.45, PrimaryRatio 0.6, CoreThicknessDip 6). The engine never reads `Settings.Sensitivity` (H-007).

### 6.2 Fades

All fades are linear progress shown through smoothstep.

| Fade | Trigger | Out | Back |
|---|---|---|---|
| Gate (Visibility) | `paused`, `Enabled = false`, `Animation = Off` | 300 ms | 300 ms |
| Hide (Visibility) | Music Sync, `WhenSilent = Hide`, `IsSilent` | 1.5 s | Within 150 ms |
| Music weight (Intensity, Pulse, drift) | Idle Glow mode, or Music Sync with `WhenSilent = IdleGlow` and `IsSilent` | 1.5 s to Idle Glow | Within 150 ms |
| Start | A new engine | — | 300 ms from Visibility 0 |

A light that fades out keeps the look it had when the fade started (Music Sync, Idle Glow or a blend), and a closing gate freezes the other two fades, so nothing flashes. While the light is hidden, the hidden fades jump to their targets, so it reappears in the right state. For example, Idle Glow comes back as Idle Glow, not as a Music Sync look that then crossfades.

### 6.3 Time

| `dtSeconds` | Counts as |
|---|---|
| NaN, ±∞, negative | 0 |
| Over 0.1 s (a stall, sleep) | 0.1 s, so nothing jumps; below 10 fps motion slows instead. |
| Each step on a new engine, or after a static frame, until the steps since then add up to 1/60 s | At most 1/60 s. `dt = 0` updates add nothing. A very short first step therefore can't let a long second one jump into the fade. |

### 6.4 IsStatic, and what the render loop can rely on (H-011)

`IsStatic` is true only while the light is hidden and stays hidden for the same inputs. That means the gate fade has finished (paused, disabled, Off), or in Music Sync with Hide, the silence fade has. It turns true on the second such `Update` in a row, so the frame that reaches Visibility 0 is still presented. It turns false on the very `Update` whose inputs can show the light, even with `dt = 0`. Those inputs are unpaused, enabled, a mode other than Off, `IsSilent` cleared, or `WhenSilent`/`Animation` no longer hiding silence. Palette, appearance fields, Level and Beat can't show the light, so they leave it true. Every visible state moves (Music Sync drifts, Idle Glow breathes), so `IsStatic` is never true while anything shows.

The K3 render loop builds on four guarantees. `RenderLoopContractTests` checks each one through `CoreFactory`, the way K3 calls the engine.

1. **Time alone changes nothing while static.** With the same inputs, any dt (0, 1/240, 0.1, 3600, NaN…) returns the first static state bit for bit. Calling a static engine at 10 Hz, at 60 fps or not at all changes nothing afterwards. Audio alone never wakes a gate-closed engine (Off, disabled, paused), so the loop may stop calling `Update` there until a settings or pause change. In Music Sync with Hide, keep calling it about 10 times a second with fresh audio. The first such poll that hears sound wakes the engine and is already above 0.5/255.
2. **Hidden is exactly `Visibility == 0`.** Every fade-out lands on +0, never on a tiny positive value, and `IsStatic` follows on the next update. Values between 0 and 0.5/255 occur only during a fade: in at most the last 40 ms of a fade-out (0–38 ms measured) and the first 7.7 ms of a fade-in. Visibility never rests there.
3. **Idle motion is slow.** Idle Glow changes at most about 0.094 × Brightness per second (0.0942 measured at Brightness 1; the test allows 0.1 × Brightness) in every field the pacer watches: Intensity, Visibility, Pulse, Spread, Ratio, thickness and phase. The silence → Idle Glow crossfade ends 1.5 s after the first `IsSilent` frame (1.500–1.510 s measured with ±20 % frame jitter), 0.25 s before K3 drops to 10 fps. From then on `Level` doesn't matter, `Pulse` is 0, and without new beats the phase is still.
4. **dt after a gap.** Given one ordinary frame's dt after a gap, the fade that brings the light back starts on that frame, from its beginning. With frames longer than 1/60 s, the first step counts 1/60 s, so a 300 ms fade takes 317 ms at 30 fps. A long frame right after a very short wake frame doesn't skip into the fade either (§6.3). This happens at K3's start: its first frame comes microseconds after its loop starts, and the GPU setup that frame runs makes the next dt up to 0.25 s. The engine used to count 0.1 s of that, so the glow popped in at about 26 %.
   - **A render loop must not slow down on a fade-in's first frame.** Above about 130 Hz, the first frame of a 300 ms gate fade-in is still under 0.5/255: 1.6e-3 at 144 Hz, 5.7e-4 at 240 Hz. The windows stay hidden for that frame, and a loop that then waits 100 ms shows the glow 100 ms late. K3's `FramePacer` did that with native pacing on every unpause, enable or Off → on, and the glow then appeared at about 29 %. It now drops to 10 Hz with nothing on screen only while `Visibility` is exactly 0.
   - **Result.** The windows show from the first frame on which the fade has run 7.7 ms (the second frame at 144 Hz), and no frame raises Visibility by more than one full-rate frame of the smoothstep (0.083 at 60 Hz, 0.035 at 144 Hz). A scratch replay of `FramePacer` and `OverlayHost`'s dt rule against the engine showed this at 60–360 Hz, at start and on unpause. `RenderLoopContractTests.AShortWakeStepDoesNotLetALongOneJump` guards the engine side.

### 6.5 More than one engine

`paused` is one flag per engine. A per-monitor pause (doc 04 §4) can work in two ways:

- **One engine, with a fade per overlay. This is what Platform does.** `OverlayHost` multiplies the engine's Visibility by each overlay's own 300 ms fade, and `FrameReport.Transitioning` holds the full rate while one runs. The engine is paused only once every overlay is paused and faded out.
- **One engine per overlay.** All are updated on the render thread with the same audio, palette and settings and that monitor's flag. Stop rendering only while every engine reports `IsStatic`.

The settings preview may own another engine and blender on the UI thread (H-017).

## 7. Settings

### 7.1 The Settings record

`Settings` is an immutable record. Instances read by `ISettingsStore.Load` and returned by `Presets.Apply` are validated. A snapshot built in code is not, but every Core consumer clamps what it reads. Record equality compares `CustomMonitorIds` by reference, so compare that field by content.

| Field | Default | Valid values (Load, Save, Presets) |
|---|---|---|
| `Version` | 1 | Always the current schema version (1). It names the file's schema. |
| `Enabled` | true | |
| `Animation` | MusicSync | MusicSync, IdleGlow, Off. |
| `WhenSilent` | IdleGlow | IdleGlow, Hide. |
| `ColorMode` | AlbumArt | AlbumArt, Manual. |
| `OverrideAlbumColor` | false | |
| `PrimaryHex` | `#7C5CFF` | `#RRGGBB` or `#RGB`, in either case, with surrounding whitespace ignored. Stored as upper-case `#RRGGBB`. Anything else (no `#`, alpha, names) becomes the default. |
| `SecondaryHex` | `#22D3EE` | Same rules. |
| `PrimaryRatio` | 0.60 | 0.1–0.9 |
| `CoreThicknessDip` | 6 | 0–40 |
| `Glow` | 0.45 | 0–1 |
| `Brightness` | 0.80 | 0.1–1 |
| `Sensitivity` | 1.0 | 0.25–2 |
| `CornerRadiusDip` | 0 | 0–40 |
| `CoverTaskbar` | true | |
| `Monitors` | All | All, PrimaryOnly, Custom. |
| `CustomMonitorIds` | empty | Opaque device IDs, compared ordinally. Null and empty entries are dropped. Duplicates, whitespace and unplugged monitors are kept (H-009). |
| `FpsCap` | 60 | 0 (native), 30, 60 or 120. Anything else becomes 60. |
| `PauseInFullscreen` | true | |
| `OnBattery` | Reduce | Normal, Reduce, Pause. |
| `HideFromScreenCapture` | false | |
| `LaunchAtStartup` | true | |
| `ToggleHotkey` | `Ctrl+Alt+L` | Opaque. Only null becomes the default, and `""` means no hotkey. Platform parses it (H-009). |
| `AutoUpdate` | true | |
| `FirstRunComplete` | false | |

Numbers outside their range are clamped to the nearest end. NaN and ±∞ become the default, and so do undefined enum values.

### 7.2 ISettingsStore

- **`Load()`** never throws and always returns valid settings.
  - A missing file gives the defaults.
  - A file in use by another process is retried (3 tries, 20 ms apart).
  - A file that can't be read or parsed is copied over `settings.bad.json` in the same directory, and the defaults are returned. That covers a file still locked, not JSON, not an object, invalid UTF-8, or over 1 MiB. The bad file stays in place until the next save replaces it.
  - Otherwise each field is read on its own, so a missing, mistyped or invalid field takes its default and the rest are kept.
  - Unknown fields are ignored.
- **Hand edits are accepted** within limits. Property and enum names match case-insensitively, and the last duplicate wins. Comments, trailing commas and a UTF-8 BOM are fine. Enums must be names, not numbers.
- **`Save(settings)`** validates and creates the directory. It then writes a uniquely named temporary file next to the target, flushes it to disk and swaps it in with `File.Replace` (or a move when there is no file yet). A crash or failure therefore leaves the previous file. If Windows' ReplaceFile failed half-way after removing the old file, it leaves the complete new one instead. Leftover temporary files from a crash are deleted after the next successful save.
  - It throws `IOException` or `UnauthorizedAccessException` when the file can't be written. Catch them and retry on the next change; App debounces saves by 500 ms.
- **Migration.** A file from an older schema is upgraded one version at a time before it is read. Version 1 is the first schema, so there are no steps yet. A missing, non-integer or below-1 version reads as 1. A file from a newer build is read as far as this build understands it, and isn't backed up. `Save` always writes the current version and the fields this build knows.
- **`Path`** is the full path of `settings.json`. The app passes `%APPDATA%\Rimlight` as the directory.
- **Concurrency.** Calls on one store are serialized. Separate stores never see a partial file. On Windows, a load during another process's save may briefly find no file (defaults) or a locked one (retried).

The format is camelCase JSON with enums as names, indented, UTF-8 without a BOM, and relaxed escaping. The defaults look like this:

```json
{
  "version": 1,
  "enabled": true,
  "animation": "MusicSync",
  "whenSilent": "IdleGlow",
  "colorMode": "AlbumArt",
  "overrideAlbumColor": false,
  "primaryHex": "#7C5CFF",
  "secondaryHex": "#22D3EE",
  "primaryRatio": 0.6,
  "coreThicknessDip": 6,
  "glow": 0.45,
  "brightness": 0.8,
  "sensitivity": 1,
  "cornerRadiusDip": 0,
  "coverTaskbar": true,
  "monitors": "All",
  "customMonitorIds": [],
  "fpsCap": 60,
  "pauseInFullscreen": true,
  "onBattery": "Reduce",
  "hideFromScreenCapture": false,
  "launchAtStartup": true,
  "toggleHotkey": "Ctrl+Alt+L",
  "autoUpdate": true,
  "firstRunComplete": false
}
```

### 7.3 Presets

`Presets.All` is a read-only list of `(string Name, Func<Settings, Settings> Apply)` in menu order. Each `Apply` returns new, validated settings and throws `ArgumentNullException` for null. Every preset sets the same six appearance fields, sets `OverrideAlbumColor = true` and keeps `ColorMode`, so the look shows even in Album Art mode, and turning Override off brings the album colors back (H-009). Corner radius and taskbar coverage are kept. Applying presets one after another gives the last one's look, except that Minimal's Idle Glow mode stays until the user changes the mode. For swatches, read the colors of `Apply(new Settings())`.

| Name | Primary | Secondary | Ratio | Core (DIP) | Glow | Brightness | Also |
|---|---|---|---|---|---|---|---|
| Aurora | `#14E3C5` | `#9D4DFF` | 0.60 | 6 | 0.60 | 0.85 | |
| Sunset | `#FF8A2A` | `#FF4F9A` | 0.55 | 6 | 0.50 | 0.85 | |
| Neon | `#FF2BD6` | `#00E5FF` | 0.50 | 12 | 0.35 | 1.00 | |
| Ember | `#FF3A1F` | `#FFB01F` | 0.65 | 4 | 0.80 | 0.60 | |
| Minimal | `#F4F1EA` | `#E8EDF5` | 0.50 | 1 | 0.15 | 0.55 | `Animation = IdleGlow` |

## 8. Other types

- **`NowPlaying(Title, Artist, SourceApp, IsPlaying, TrackId)`.** Platform's media service publishes it, and `TrackId` is what it passes to `Extract` as the palette's `SourceTrackId`. Core defines the record only.
- **`AppInfo.Name`** is the `"Rimlight"` constant. With `<AppName>` in `src/Directory.Build.props`, it is one of the two places a rename touches.

## 9. How to consume

This is how the overlay drives Core (`MusicGlowSource`), trimmed to the calls:

```csharp
// Tuning throws outside 0.25–2, and a Settings snapshot built in code isn't validated: clamp what you copy.
static float Sensitivity(Settings s) => float.IsFinite(s.Sensitivity) ? Math.Clamp(s.Sensitivity, 0.25f, 2f) : 1f;

// Render thread, once.
IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer(new AudioTuning { Sensitivity = Sensitivity(settings) });
IPaletteBlender blender = CoreFactory.CreatePaletteBlender(palette);
ILightEngine engine = CoreFactory.CreateLightEngine();
float[] gradient = new float[256];   // 64 RGBA texels
bool paletteChanged = false;
float filledRatio = float.NaN;       // nothing filled yet: the first frame fills the gradient

// Render thread, every frame: nothing here allocates.
AudioFeatures features = analyzer.Process(samples.AsSpan(0, count), sampleRate, dt); // an empty span when no packets came
bool wasAnimating = blender.IsAnimating;
blender.Update(dt);
LightState state = engine.Update(dt, features, palette, settings, paused); // palette: the one last given to SetTarget
if (paletteChanged || wasAnimating || state.Ratio != filledRatio)
{
    blender.FillGradient(gradient, state.Ratio);   // upload as a 64 × 1 texture, WRAP + linear
    paletteChanged = false;
    filledRatio = state.Ratio;
}
// Draw with state: alpha × Intensity × Visibility. Under 0.5/255, hide the window.
// Pace: blender.IsAnimating → full rate. engine.IsStatic → stop presenting: poll at 10 Hz in Music Sync,
// otherwise wait for an input. Idle Glow → 10 fps. Visibility above 0 but hidden is a fade: keep its rate (§6.4).

// Render thread, on a new palette (track change: 800 ms; manual edit: 200 ms).
blender.SetTarget(next, TimeSpan.FromMilliseconds(800));
palette = next;
paletteChanged = true;

// Render thread, on a Sensitivity change (never per frame), and on a capture device change.
analyzer.Tuning = analyzer.Tuning with { Sensitivity = Sensitivity(settings) };
analyzer.Reset();   // device change only

// Thread pool: one extraction in flight, the latest track wins. Publish by reference.
Palette? album = extractor.Extract(thumbnail64x64Bgra, 64, 64, nowPlaying.TrackId); // null: keep the previous palette
Volatile.Write(ref albumPalette, album);

// UI thread: load once at start; save debounced, off the UI thread.
ISettingsStore store = CoreFactory.CreateSettingsStore(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name));
Settings loaded = store.Load();
try { store.Save(loaded with { Glow = 0.6f }); }
catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* log; retry on the next change */ }
```

The settings preview (`GlowPreview`) is the same loop on the UI thread with its own `blender` and `engine`. It takes the overlay's latest `AudioFeatures` instead of an analyzer of its own.

## 10. Where this reference refines the contract XML

The contract files are frozen, so their XML can only change through an approved `CONTRACT CHANGE` (doc 09 §3). These summaries are out of date. The sections above are correct:

- `ISettingsStore`: "the real implementation arrives in C7". It has arrived (§7.2).
- `Settings`: "validation and persistence arrive in C7". Both exist (§7.1, §7.2).
- `AudioTuning.MinFlux`: "provisional K0 default, to be tuned in C2". The default stays 0.01, but it is relative to the window's RMS (§4.2).
- `AudioTuning.Sensitivity`: "inversely scales the flux multiplier". It scales by 1/√Sensitivity, and also relaxes the median guard (§4.2).
- `AudioTuning.FluxThresholdMultiplier`: "standard deviations above mean". The effective threshold is the largest of three criteria (§4.2).
- `LightState.Intensity`: "Brightness, 0..1". It excludes `Visibility` (H-008 item 2).
- `LightState.Ratio`: "0..1". The engine gives 0.1..0.9.
