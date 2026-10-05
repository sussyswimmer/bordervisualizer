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

### Beat detection (spectral flux on the bass band)
1. `flux = Σ max(0, |X[k]| - |Xprev[k]|)` over bass bins (positive changes only).
2. Keep a ring of the last **1.0 s** of flux values. `threshold = mean + 1.5 * stddev`.
3. A beat fires when `flux > threshold` **and** `flux > minFlux` (to avoid noise during silence) **and** at least **180 ms** have passed since the last beat (max ≈333 BPM).
4. On a beat, `Beat = 1`. Otherwise, `Beat` decays exponentially with τ = **120 ms**.
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
- A beat counter and estimated BPM (median of recent beat intervals)
- `IsSilent`, the capture format, and the device name

This is how Maxwell will tune the feel. Make all the parameters above live-editable here (sliders) and add a "Copy params as JSON" button. Parameters live in an `AudioTuning` record in Core with the defaults above.

## 5. Tests (Rimlight.Tests)
- FFT: a pure sine at bin frequency produces a peak in the correct bin, and Parseval's identity holds within tolerance.
- Auto-gain: volume invariance (as described above).
- Beat detector: synthetic 120 BPM kick track (decaying 60 Hz bursts + noise) → detects 120 ± 2 BPM, with no beats on pure white noise at constant level and no beats on silence.
- Envelopes: step response reaches 63% at τ (±5%).
- Allocation test: run 1000 analysis frames under `GC.GetAllocatedBytesForCurrentThread()` and assert 0 bytes after warm-up.
