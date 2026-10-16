# 03 — Audio Engine

The audio engine decides how "musical" the light feels. Implement it exactly as described here first, then tune it with the debug visualizer.

## 1. Capture (Platform)
- `NAudio.Wave.WasapiLoopbackCapture` on the **default render device** (`MMDeviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)`).
- The shared-mode mix format is usually 32-bit float at 48 kHz, stereo or more. Handle 16/24/32-bit int as well. Convert to **mono float** by averaging channels.
- Copy samples into a lock-free single-producer/single-consumer ring buffer (`float[]`, capacity 2 s of audio, power-of-two size).
- **WASAPI loopback delivers no packets while nothing plays.** Treat a lack of data for more than 100 ms as silence rather than an error.
- Restart on default-device change and on `RecordingStopped` with an exception, using backoff.
- **Never** use `WasapiCapture` (that's the microphone).

## 2. Analysis (Core, runs on the render thread each frame)

Sample rate `fs` comes from the capture format. Do not hard-code 48000.

### FFT
- Window size **N = 2048**, Hann window, hop = whatever new samples arrived since the last frame (≈800 at 60 fps / 48 kHz). Always analyze the most recent 2048 samples.
- Write the FFT yourself in Core (iterative radix-2, precomputed twiddles and window, preallocated buffers) so Core has no dependencies. Use `System.Numerics.Vector<float>` where it's easy. **Zero allocations per frame.**
- Magnitude spectrum `|X[k]|` for k = 0..N/2.

### Bands
| Band | Range | Used for |
|---|---|---|
| Sub/Bass | 30–150 Hz | beat detection, `Bass` |
| Mid | 150–2000 Hz | contributes to `Level` |
| High | 2000–12000 Hz | contributes to `Level` (small weight) |

Band energy = RMS of magnitudes in the band's bins. Convert to dB: `20*log10(e + 1e-9)`.

### Adaptive normalization (auto-gain)
Loudness differs wildly between songs and volume settings. For each band, track a running **peak** that decays slowly:
- `peak = max(value, peak * decay)`, with `decay` set so the peak halves in **4 s**.
- `floor` = slow running minimum (rises at 1 dB/s, drops instantly).
- `norm = clamp((value_dB - floor) / max(peak_dB - floor, 12 dB), 0, 1)`.
This makes quiet acoustic tracks and loud EDM both use the full 0..1 range. **The OS volume slider must not change how bright the glow is.** Unit-test this: the same signal at −20 dB and at 0 dB must give a similar `norm` after a 2 s warm-up.

*Clarification (Maxwell's feedback, round 1): with a 4 s peak this also stretches a whispered verse over the full range about 6 s after the chorus, so a calm song's verse looked as bright as its chorus. Auto-gain stays as written and still normalizes `Bass` and the contrast within a groove, but `Level` is now judged against a song-level reference (the 90th percentile of the loudness over a 10 s memory) and blends between a slow wave and the envelope below by how drum-driven the music is. The same song at another volume still gives the same light; a volume change in the middle of a song takes about 25 s to settle instead of 2–15 s. `docs/CORE-API.md` §4.2 has the details.*

### Level and Bass envelopes
Apply an asymmetric one-pole envelope to each normalized value:
```
coef = 1 - exp(-dt / tau)
tau  = (target > current) ? attackTau : releaseTau
current += (target - current) * coef
```
| Signal | attackTau | releaseTau | Formula for target |
|---|---|---|---|
| Level | 30 ms | 250 ms | `0.55*bass + 0.35*mid + 0.10*high` |
| Bass | 15 ms | 180 ms | `bassNorm` |

*Clarification (Maxwell's feedback, round 1): these Level time constants apply with drums; with none, Level uses 10× the attack and 5× the release (0.3 s / 1.25 s), and its target is the song position cut by the weighted bands above (CORE-API §4.2).*

### Beat detection (spectral flux on the bass band)
1. `flux = Σ max(0, |X[k]| - |Xprev[k]|)` over bass bins (positive changes only).
2. Keep a ring of the last **1.0 s** of flux values. `threshold = mean + 1.5 * stddev`.
3. A beat fires when `flux > threshold` **and** `flux > minFlux` (to avoid noise during silence) **and** at least **180 ms** have passed since the last beat (max ≈333 BPM).
4. On a beat, `Beat = 1`. Otherwise, `Beat` decays exponentially with τ = **120 ms**. *Clarification (Maxwell's feedback, round 1): detection is unchanged, but `Beat` jumps to the beat's grade, 0..1: how kick-like the onset is (sub-bass rise against the whole mix, and against the rise of the harmonics above it), how it compares with the strongest recent onset, and how loud the moment is within the song. A kick in a loud passage is still exactly 1; a sung syllable or a plucked low string is 0 (CORE-API §4.2).*
5. `Sensitivity` (0.25–2×) scales the multiplier 1.5 inversely (higher sensitivity → lower threshold) and scales the final `Level` gain.

### Silence
- `IsSilent = true` when the raw RMS of the time-domain signal stays below −60 dBFS (or no packets arrive) for **2000 ms**. It becomes false as soon as RMS rises above −55 dBFS (hysteresis).

## 3. Latency budget
Sound must not feel ahead of or behind the light. Budget:
- Loopback buffer: about 10–20 ms
- Analysis window centered ~21 ms in the past (N/2 at 48 kHz)
- Render + Present + DWM compose: ~1–2 frames
Total target: **< 50 ms**. Use the newest samples every frame, and do not queue frames (swap chain with `MaximumFrameLatency = 1`, via `IDXGISwapChain2.SetMaximumFrameLatency` with a waitable object).

## 4. Debug visualizer (`--debug-visualizer`)
A small always-on-top WPF window showing live:
- The spectrum (log frequency axis) with the band boundaries marked
- Scrolling plots of `Level`, `Bass`, `Beat`, raw flux, and the beat threshold
- A beat counter and estimated BPM (median of recent beat intervals). *Clarification (C13, H-016): the estimate is the mean of the last 16 intervals that lie within ±25 % of their median, or the median when none does. A plain median is biased by whole-frame intervals at a steady frame rate (171.4 for 174 BPM at 60 fps); `docs/CORE-API.md` §4.4 has the details. Since Maxwell's feedback round 1, only accented beats count for the tempo (not one within 0.3 s of a beat at least 1.4× as strong), so an off-beat bass note doesn't double it.*
- `IsSilent`, the capture format, and the device name

This is how Maxwell will tune the feel. Make all the parameters above live-editable here (sliders) and add a "Copy params as JSON" button. Parameters live in an `AudioTuning` record in Core with the defaults above.

## 5. Tests (Rimlight.Tests)
- FFT: a pure sine at bin frequency produces a peak in the correct bin, and Parseval's identity holds within tolerance.
- Auto-gain: volume invariance (as described above).
- Beat detector: synthetic 120 BPM kick track (decaying 60 Hz bursts + noise) → detects 120 ± 2 BPM, with no beats on pure white noise at constant level and no beats on silence.
- Envelopes: step response reaches 63% at τ (±5%).
- Allocation test: run 1000 analysis frames under `GC.GetAllocatedBytesForCurrentThread()` and assert 0 bytes after warm-up.
