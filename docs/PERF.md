# Performance

What the Core costs per frame, and what an 8-hour run does to memory. The numbers come from `Rimlight.Bench` (doc 09 C3 and C12, doc 07 Phase 7). It runs the real analyzer, light engine and palette blender exactly as the app's render thread calls them, on synthetic audio, as fast as the CPU allows.

This covers the Core only. The rest of the app's CPU (WASAPI capture, the Direct3D frame, composition) and its handles and GPU memory over a long run are measured on Windows with the real app (K11, doc 07 Phases 3 and 7, HANDOFF H-018).

## Summary

8 simulated hours at 60 fps (1,727,743 frames), on the Linux machine described [below](#machine):

| | Result |
|---|---|
| Allocations | **0 B in all 1,727,743 frames** (0 B/frame), no garbage collections |
| Frame cost, Core total | mean 60.5 µs, p50 69.4 µs, p99 140.8 µs, max 186 ms (most likely the VM pausing the thread, see [Frame cost](#frame-cost)) |
| CPU | 0.36 % of one core (0.09 % of this 4-CPU machine, Task Manager's scale) |
| Live managed heap | 671 KB → 686 KB (+15 KB, a fixed amount that doesn't grow with the run's length) |
| Working set | 34.6 MB → 37.1 MB, a one-off step of about 1.2 MB plus the progress lines' garbage, not growth over time (see [Memory](#memory)) |
| Handles | 50 → 50 |
| Verdict | **PASS** (0 B allocated per frame in steady state, managed memory flat) |

**CPU budget.** Doc 07 Phase 3 allows the whole app under 2 % CPU while it plays music at 60 fps on one or two monitors, measured in Task Manager. Task Manager shows a share of all logical processors. The Core's 0.36 % of one core is therefore 0.36 % ÷ the number of logical processors: 0.09 % here, 0.045 % on an 8-thread PC. That is a few percent of the budget, and capture, drawing and composition get the rest. The soak also fills the color gradient every frame, which the app does only while colors change, so the real Core cost is slightly lower.

## Method

### What runs each frame

These are the calls of the app's render thread (doc 02 render-thread steps 1–2, `MusicGlowSource.NextFrame` in the app), in the same order and on one thread:

1. `IPaletteBlender.SetTarget(palette, 800 ms)` on the first frame of a new track (doc 05 §3's album crossfade).
2. `IAudioAnalyzer.Reset()` on the first frame of a new listening session, which starts on a new output device.
3. `IAudioAnalyzer.Process(samples, 48000, dt)` with the whole 10 ms capture packets that arrived by the end of the frame: one or two at 60 fps, and an empty span while no packets arrive.
4. `IPaletteBlender.Update(dt)`.
5. `ILightEngine.Update(dt, features, palette, settings, paused: false)`.
6. `IPaletteBlender.FillGradient(gradient, ratio)`, every frame. The app fills it only while it changes, so this is an upper bound.
7. `IPaletteBlender.IsAnimating` and `ILightEngine.IsStatic`, which the frame pacing reads to draw at the full rate, slowly or not at all.

Frames last 1/60 s ± 20 % (uniform jitter, seeded), so the analyzer and the engine see uneven `dt` and packet counts, like a real render loop. Generating the audio between frames stands in for draining the ring buffer and is not measured.

### The synthetic program

`soak` plays a deterministic listening day built from C3's streaming track generator. A track is generated sample by sample as it plays, so 8 hours of audio need no memory. The default run (seed 1):

| | |
|---|---|
| Sessions | 8 of 40–80 minutes. Silence fades to Idle Glow in one session and hides the light in the next (`Settings.WhenSilent`), alternating. Each session starts on a new output device (`Reset`) and, after the first, with a 3–12 minute pause in which no packets arrive. |
| Tracks | 121 of 2–5 minutes, at 81–169 BPM and −24 to 0 dB. Each has four sections: an intro of vocal tones and hats with no kick, a full-mix body, a breakdown of vocals and noise, and a body with or without bass and hats. |
| Palettes | A new album palette on every track, so 121 changes, one every 3.5 minutes of music. 32 tracks continue an album: the same colors under a new track ID. |
| Gaps between tracks | Alternately digital silence (packets of zeros) and no packets. Most last 0.3–3 s, shorter than the analyzer's 2 s silence hold. Every fifth lasts 4–20 s, so the light fades to Idle Glow or hides. |
| Totals | Music 88.0 %, digital silence 0.7 %, no packets 11.3 %. 48,165 kicks. |

### Measurement

- **Cost:** the stopwatch is read around each call group into constant-memory histograms (log-linear buckets, at most 0.8 % error), so 8 hours hold no more memory than 2 minutes. "Whole frame" is steps 1–7; the analyzer row is steps 2–3, the light engine row step 5, and the palette row steps 1, 4, 6 and 7.
- **Allocations:** `GC.GetAllocatedBytesForCurrentThread()` before and after each frame's calls. Every allocating frame is counted, with its bytes and the simulated time of the first one. The run also reports the whole process's allocations while measuring (`GC.GetTotalAllocatedBytes`), minus what the progress lines allocate.
- **Steady state:** before measuring, a 1.5-minute "tour" visits every path the program has. It covers both gap kinds, short and long, both silent behaviors with the light hidden and static, a device change, and new and same-album palettes. The tour repeats until 2 s of real time have passed (3 tours here), so tiered compilation has optimized every measured path and static initializers have run. The same analyzer, engine and blender then carry on into the measured program.
- **Memory:** sampled at the start and the end after a forced, compacting full collection: the live managed heap, the working set and the handle count. The heap and working set are also printed at every progress line.
- **Pass:** 0 bytes allocated in the measured frames, and live managed heap growth of at most 256 KB. With 0 bytes allocated on the render thread, nothing there can leak. The heap check catches growth anywhere else in the process. The records the run keeps for its progress lines are structs in a list sized before the start sample, so however many lines it prints, they don't count.

## Results: 8 simulated hours

```bash
dotnet run -c Release --project src/Rimlight.Bench -- soak --json soak-8h.json
```

It ran in 4 min 36 s of real time (104× real time), after a 2.6 s warm-up.

### Frame cost

| Per frame | Mean | p50 | p99 | p99.9 | Max |
|---|---:|---:|---:|---:|---:|
| **Whole frame** | 60.5 µs | 69.4 µs | 140.8 µs | 330.8 µs | 185898.1 µs |
| Analyzer (`Reset`, `Process`) | 59.7 µs | 68.4 µs | 138.8 µs | 322.6 µs | 185891.4 µs |
| Light engine (`Update`) | 0.20 µs | 0.15 µs | 0.68 µs | 3.19 µs | 2245.5 µs |
| Palette blender (`SetTarget`, `Update`, `FillGradient`) | 0.66 µs | 0.58 µs | 1.32 µs | 9.12 µs | 4200.1 µs |

- The analyzer is nearly all of the cost. It runs one analysis step (an FFT, the bands and the beat detector) per 10 ms packet. During music at 60 fps ± 20 %, two-thirds of the frames receive two packets and the rest one, so the median frame runs two steps and the average is 1.7. That is why these costs are higher than the `analyzer` benchmark's below, which feeds one 800-sample step per frame. A frame with no packets costs about 1.4 µs (the benchmark's `idle` scenario).
- At p99, a frame takes 0.84 % of the 16.7 ms frame time.
- The maximums are most likely the virtual machine pausing the thread, not work. The light engine's `Update` is a short piece of float math with no loops over data, yet its maximum is 2.2 ms against a p99.9 of 3.2 µs. Other jobs share this machine's 4 CPUs, and the maximums of the half-hour intervals vary between 2.6 and 186 ms with no trend. Three of the sixteen are over 15 ms (17, 53 and 186 ms).

### Over time

One line per half hour, as the run prints it. The cost columns are for that interval, in µs. The memory columns are read without a collection, so the progress lines' own garbage is included.

| Simulated | Frames | Mean | p50 | p99 | Max | Allocated | Heap | Working set | GCs | Beats |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|
| 0:30:00 | 107,971 | 70.5 | 71.9 | 151.0 | 4768.2 | 0 B | 0.7 MB | 35.8 MB | 0/0/0 | 3,214 |
| 1:00:00 | 215,973 | 59.9 | 67.8 | 136.7 | 3046.7 | 0 B | 0.7 MB | 36.0 MB | 0/0/0 | 5,984 |
| 1:30:00 | 323,983 | 61.0 | 69.4 | 135.7 | 9006.9 | 0 B | 0.7 MB | 36.0 MB | 0/0/0 | 9,060 |
| 2:00:00 | 431,981 | 49.9 | 59.0 | 130.8 | 2611.3 | 0 B | 0.7 MB | 36.5 MB | 0/0/0 | 11,567 |
| 2:30:00 | 539,969 | 63.7 | 70.9 | 150.0 | 5399.1 | 0 B | 0.7 MB | 36.5 MB | 0/0/0 | 14,761 |
| 3:00:00 | 647,936 | 66.1 | 70.9 | 143.9 | 13168.2 | 0 B | 0.7 MB | 36.6 MB | 0/0/0 | 18,272 |
| 3:30:00 | 755,914 | 53.0 | 62.8 | 133.6 | 9616.4 | 0 B | 0.7 MB | 36.6 MB | 0/0/0 | 20,631 |
| 4:00:00 | 863,821 | 69.5 | 71.4 | 148.0 | 17342.8 | 0 B | 0.7 MB | 36.9 MB | 0/0/0 | 24,051 |
| 4:30:00 | 971,784 | 54.8 | 64.6 | 144.9 | 4660.4 | 0 B | 0.7 MB | 36.9 MB | 0/0/0 | 26,814 |
| 5:00:00 | 1,079,790 | 66.7 | 70.4 | 140.8 | 4385.7 | 0 B | 0.7 MB | 36.9 MB | 0/0/0 | 30,394 |
| 5:30:00 | 1,187,756 | 67.3 | 71.4 | 138.8 | 52927.3 | 0 B | 0.7 MB | 36.9 MB | 0/0/0 | 33,961 |
| 6:00:00 | 1,295,748 | 56.2 | 64.9 | 139.8 | 4310.0 | 0 B | 0.7 MB | 36.9 MB | 0/0/0 | 36,447 |
| 6:30:00 | 1,403,753 | 61.2 | 70.4 | 143.9 | 4048.1 | 0 B | 0.7 MB | 36.9 MB | 0/0/0 | 39,807 |
| 7:00:00 | 1,511,734 | 58.8 | 69.9 | 126.2 | 5055.4 | 0 B | 0.8 MB | 36.9 MB | 0/0/0 | 42,897 |
| 7:30:00 | 1,619,734 | 59.5 | 66.8 | 136.7 | 185898.1 | 0 B | 0.8 MB | 37.0 MB | 0/0/0 | 45,625 |
| 8:00:00 | 1,727,743 | 50.4 | 53.6 | 136.7 | 5158.3 | 0 B | 0.8 MB | 37.1 MB | 0/0/0 | 48,469 |

Interval means differ with the program. The seven pauses between sessions (5–11 minutes with no packets, at 0:57, 1:52, 2:59, 4:17, 5:36, 6:28 and 7:26) fill their intervals with 1.4 µs frames, so those intervals have the lower means.

### Allocations

- 0 bytes in 0 of 1,727,743 frames, including the 121 `SetTarget` calls, the 8 `Reset` calls and every silent, hidden and static frame.
- The whole process allocated 6,256 bytes while measuring, apart from the progress lines. None of it was in the frames; an earlier run of the same program measured 64 bytes.
- There were no garbage collections, in any generation, during the run.

### Memory

- **Live managed heap:** 671 KB → 686 KB after a full collection (+15 KB). 1-hour and 4-hour runs with one progress line grew 13 KB and 9 KB, and a 10-minute run 15 KB, so the amount doesn't depend on the length of the run.
  - About 3 KB of it was the 16 progress records the run keeps for its report. The harness now allocates their storage before the start sample, so they no longer count. A 10-minute run now grows 12 KB with 16 lines, and 20 KB with 2,000 or 10,000 lines. Before the change, 2,000 lines added 333 KB and failed the check.
- **Working set:** 34.6 MB → 37.1 MB, in two parts. Neither grows with time.
  - A one-off step of about 1.2 MB after the start sample. With one progress line, a 1-hour run ended at +1.2 MB (34.1 → 35.3 MB) and a 4-hour run also at +1.2 MB (34.3 → 35.5 MB).
  - The progress lines. Formatting and printing a line allocates, and nothing reclaims it, because no collection runs during the whole run. A 1-hour run printing 120 lines grew 3.6 MB, against 1.2 MB with one line. The default run prints 16 lines, which accounts for the rest of its 2.5 MB.
- **Handles:** 50 → 50.

### Beats and light

- **Beats:** 48,469 for 48,165 kicks (100.6 %). The run counts beats only to show that the music drove the light. Beat accuracy (missed and extra beats, latency, tempo) is checked by C3's `wav-analyze selftest` and the analyzer tests.
- **Light states:** frames were silent 11.4 % of the time, hidden (Visibility 0) 6.1 % and static (`ILightEngine.IsStatic`) 6.1 %. The colors were crossfading in 4,229 frames (0.2 %): about 48 frames for each of the 89 tracks with new colors. The run counted 121 `SetTarget` and 8 `Reset` calls. So the fades and the idle path ran as well as the music.

## Analyzer benchmark

```bash
dotnet run -c Release --project src/Rimlight.Bench -- analyzer
```

This is `IAudioAnalyzer.Process` alone, with 800 samples per call (60 fps at 48 kHz, delivered continuously, not in packets), after 2,000 warm-up frames, for 10,000 measured frames per scenario. Times are in ns.

| Scenario | Mean | p50 | p99 | Max | Bytes/frame | Core % | GCs | Beats | Signal |
|---|---:|---:|---:|---:|---:|---:|---|---:|---|
| music | 36,908 | 34,688 | 66,816 | 2,112,998 | 0 | 0.22 | 0/0/0 | 359 | synthetic full mix, 128 BPM |
| kick | 41,513 | 35,712 | 88,320 | 270,172 | 0 | 0.25 | 0/0/0 | 333 | kicks + noise + vocal tones, 120 BPM |
| noise | 37,950 | 35,712 | 63,872 | 218,567 | 0 | 0.23 | 0/0/0 | 0 | white noise, −10 dBFS |
| silence | 41,087 | 34,944 | 88,832 | 2,102,904 | 0 | 0.25 | 0/0/0 | 0 | digital silence, packets still arriving |
| idle | 1,392 | 1,324 | 3,720 | 27,285 | 0 | 0.01 | 0/0/0 | 0 | no packets (empty spans), as while nothing plays |

## Machine

These numbers are informational. The machine is shared, and its timings differ from a desktop PC's. Allocations and memory don't depend on timing.

- A Linux cloud VM (KVM): Intel Xeon @ 2.10 GHz, 4 vCPUs (one thread per core), 15 GB RAM.
- Ubuntu 24.04.4 LTS, kernel 6.18.
- .NET 8.0.31 runtime (SDK 8.0.425), Release build, x64, tiered compilation with PGO (the Bench project's settings).
- Other build and test jobs shared the 4 CPUs during the run (load average about 1).

## Running it on Windows

The Bench is a plain .NET 8 console app, so it runs the same way on Windows. From the repository root in PowerShell:

```powershell
dotnet --list-runtimes    # needs a Microsoft.NETCore.App 8.0.x line (the .NET 8 SDK installs it)
dotnet run -c Release --project src\Rimlight.Bench -- soak --json soak-8h.json
```

- It takes about 5 minutes on one core and prints a line every half hour of simulated time. The summary at the end is Markdown, ready to paste here.
- **Pass:** the summary ends with `Verdict: PASS` and the exit code (`$LASTEXITCODE`) is 0. 1 means a frame allocated or managed memory grew; 2 means a bad command line or a `--json` file that can't be created.
- For steadier timings, plug a laptop in and close heavy apps. Use `-c Release`: a Debug build says so in the header line, and its timings mean nothing.
- Please send back `soak-8h.json` or the summary, and the CPU model, so a Windows section can be added here.

Options (`dotnet run -c Release --project src\Rimlight.Bench -- --help` lists them all):

| Option | Default | Meaning |
|---|---|---|
| `--hours <h>` | 8 | Simulated hours |
| `--minutes <m>` | | Simulated minutes instead, for a shorter run (e.g. `--minutes 10`, a few seconds) |
| `--fps <n>` | 60 | Render frames per second, e.g. `--fps 144` for a high-refresh monitor |
| `--shape <name>` | day | `day` is the listening day above. `compressed` packs the same structure into about a minute per session, so `--minutes 2 --shape compressed` covers every path in a few seconds; it is what the test suite runs |
| `--json <file>` | | Also write the full report as JSON: options, machine, program, costs, allocations, memory and every progress line. The file is created before the run, so a path that can't be written fails at once |
| `--seed <n>` | 1 | Another seed gives another program, with different tracks, tempos, gaps and palettes |
| `--jitter <f>`, `--packet-ms <ms>`, `--rate <Hz>` | 0.2, 10, 48000 | Frame-time jitter, capture packet length, sample rate |
| `--warmup-seconds <s>` | 2 | Real seconds of warm-up on the tour before measuring |
| `--report-every <min>` | 30 for 8 h | Simulated minutes between progress lines (16 lines per run by default, at most 10,000) |

`analyzer` measures `Process` alone per signal type (table above). Its options are `--scenario`, `--rate`, `--fps`, `--frames` and `--warmup`.

## In the test suite

The quick soak is the CI form of the 8-hour run. It uses the same harness on a compressed program (`--shape compressed`): 2 simulated minutes cover both silent behaviors, a hidden and static light, a device change, new and same-album palettes, and both gap kinds. It runs in every `dotnet test`, so CI checks it on Linux and Windows on every pull request. The tests are in `src/Rimlight.Tests/Tools/SoakTests.cs`.

- **`SoakInItsOwnProcessPassesAndWritesTheJsonReport`** runs `Rimlight.Bench soak --minutes 2 --shape compressed` as a child process, the way you would run it. It asserts exit code 0, `Verdict: PASS`, 0 bytes allocated in every measured frame and live managed heap growth of at most 256 KB, all read from its JSON report. It prints 2,500 progress lines, so the records kept for them can't pass for growth. It takes a few seconds.
- **`QuickSoakCoversEveryPathAndAllocatesNothing`** runs the same soak inside the test process. It asserts what the program covered (sessions, tracks, gaps, silent, hidden and static frames, beats against kicks, progress lines) and 0 bytes allocated. Allocations are counted per thread, so the test runner can't disturb them.
- **Why memory is only checked in a child process:** inside the test host, the runner's own threads allocate and keep memory while a test runs. One 0.3-second soak saw about 1 MB allocated and 350 KB kept by other threads, while its own frames allocated 0 bytes. A process-wide heap sample can't tell that apart from a leak.
- The soak tests run one at a time, after the parallel ones.
- Two more tests prove the checks can fail. A light engine that allocates on every 100th frame fails the run, and each of its frames is counted. One that keeps every fifth allocation fails the memory check.
- The command line has tests too: exit code 1 on a failing verdict, the 8-hour default, the 10,000-line limit, and a `--json` path that can't be written failing before the run. The quick soak also checks that each progress line's costs cover only its own interval.

## Updating this file

Run the soak again after changing anything on the render path (the analyzer, the light engine or the palette blender). Replace the results with the new summary, and keep the old numbers only if a comparison matters.
