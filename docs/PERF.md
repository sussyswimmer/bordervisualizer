# Performance and soak results

What the render thread's Core work costs per frame, and what 8 hours of it do to memory. This covers C12 (doc 09 §4), AGENTS.md's "Bench report" item and the Core side of doc 07 Phases 3 and 7. Everything here was measured with `Rimlight.Bench` on a Linux cloud VM, not on a Windows PC. The real app's CPU use has to be measured on Windows; the steps are in [Measuring the app on Windows](#measuring-the-app-on-windows).

## At a glance

| What | Result (Linux VM, 2026-10-15) | Target |
|---|---|---|
| One render-thread frame at 48 kHz / 60 fps with the audio in 10 ms packets, as the app gets it (one or two packets a frame): analyzer + palette blender + light engine | median 78 µs, mean 79–91 µs (median of three runs); the 8-hour soak agrees: median 76 µs, mean 88 µs | none set; informational |
| The same, as a share of one CPU core at 60 fps | **about 0.5 % of one core** (about 1.1 % at 192 kHz) | the whole app under 2 % of the machine (doc 07 Phase 3) |
| The best case, exactly one frame of audio (800 samples) per frame, so one analyzer step | median 40 µs, mean 52–56 µs: about 0.3 % of one core (0.9–1 % at 192 kHz) | informational |
| Analyzer / light engine / blender + gradient, mean per frame at 48 kHz with 10 ms packets | 79–90 µs / 0.14–0.15 µs / 0.1 µs (0.8 µs while crossfading) | AGENTS.md: report ns/frame |
| Allocations per frame | **0 B**: every benchmark row, and all 1,441,560 measured frames of the 8-hour soak. Applying a settings edit is counted on its own: each Sensitivity edit allocates one 120-byte `AudioTuning` on the render thread, in the app too (8 edits, 960 B in the 8-hour day) | 0 B/frame (AGENTS.md) |
| GCs in the 8-hour soak's measured part | 0 / 0 / 0 (gen 0/1/2) | none |
| Memory over 8 simulated hours | **flat**: live managed heap +3 KB; working set +536 KB after the first hour (+756 KB in all, from JIT tier-ups); the harness itself allocated 0 B between frames | flat (doc 07 Phase 7) |
| The real app's CPU on Windows | **not measured yet** | under 2 % playing at 60 fps on 1–2 monitors; about 0 % idle and in Off |

## Machine and software

- **Machine:** a cloud VM (Firecracker, kernel 6.18.44), 4 logical CPUs reported as "Intel(R) Xeon(R) Processor @ 2.80GHz" (AVX2 and AVX-512), 16 GB RAM. The host is shared: other jobs ran during these measurements (load average 1–3 on 4 CPUs), so timings move by tens of percent between runs.
- **Software:** Ubuntu 24.04.5 LTS x64, .NET SDK 8.0.426 (`dotnet --info`), runtime Microsoft.NETCore.App 8.0.32, Release build, default runtime settings (tiered compilation and dynamic PGO on; `Rimlight.Bench.csproj` sets `TieredPGO`).
- **Code:** branch `wip/c12-soak-perf-fix` (C12 with its review fixes), on top of the integration commit `8abd6f5` (Core from C1–C11, `CoreFactory` returning the real implementations).

## What is and isn't measured

**Measured.** `FramePipeline` in Bench runs what the app's render thread runs per frame in `MusicGlowSource.NextFrame` (`src/Rimlight.App/Overlay/MusicGlowSource.cs`), step for step: the settings and album-palette checks (with `SetTarget` on a new palette or settings change, and the analyzer retune on a Sensitivity change), `IAudioAnalyzer.Process` (and `Reset` when the capture opens or closes), `IPaletteBlender.Update`, `ILightEngine.Update`, `FillGradient` whenever the app would refill, and the frame-pacing decision. The app is Windows-only, so this glue is mirrored in Bench rather than shared; the Core objects come from `CoreFactory`, as in the app. `Rimlight.Platform.Tests` keeps the mirror honest: it runs `FramePipeline` and the app's real `MusicGlowSource` side by side on the same audio, settings and album palettes and requires the same light state, gradient and motion bit for bit, and it checks `PaceModel` against `FramePacer` decision by decision (K10). Timing uses `Stopwatch` between the stages, and allocations are counted with `GC.GetAllocatedBytesForCurrentThread()` around `NextFrame` alone, with the settings step counted apart (below). The soak's frame schedule follows the overlay's pacer (`PaceModel` mirrors `FramePacer`): the full rate for music, 10 Hz for Idle Glow and while hidden and listening, and no frames at all while Off, disabled or paused.

**Settings edits allocate, in the app too.** `MusicGlowSource.ApplySettings` retunes the analyzer with `analyzer.Tuning with { Sensitivity = … }` on the render thread. `AudioTuning` is a record class, so each Sensitivity edit allocates one 120-byte object there. `FramePipeline` does the same and reports those bytes apart from the frame's own (`SettingsBytes`): the 0 B/frame verdict judges every frame with that once-per-edit step left out, and the soak prints what the step allocated on its own line. A manual color edit also allocates in the app (`ManualPalette` builds a new `Palette`); no schedule here edits the manual colors, so that path is not exercised.

**Not measured.**
- Everything Windows does: the WASAPI capture thread and the copy out of the loopback ring, Direct3D 11 drawing and `Present`, DirectComposition and DWM, the GPU, WPF, the media session reader and the system watchers. The real app's CPU use is these plus the numbers here.
- The app's memory. The working set here is `Rimlight.Bench`'s (about 34 MB, the .NET runtime plus the harness); the app also carries WPF, D3D and WinRT.
- Windows' scheduler, timer resolution and power management. On Windows, `Stopwatch` ticks are 100 ns, so sub-microsecond percentiles come out quantized there; the means are still right.
- Each timed stage includes the clock reads around it: about one read (21–30 ns here) for the analyzer and the light engine, about three for the blender + gradient, and about six for the whole frame. Most of the blender's 90–105 ns while idle is clock reads.

## Frame benchmark

`frame` runs the music of C3's synthetic full mix (128 BPM) through `FramePipeline`, 2,000 warm-up frames (at least 0.5 s) and then 30,000 measured frames per row, with the audio delivered in one of two ways:
- **10 ms packets** (`frame --packet-ms 10`), as WASAPI loopback delivers it (doc 03 §1) and as the app's render thread gets it: each frame gets the whole packets that arrived since the last one. At 60 fps that is one packet, then two, then two (480, 960, 960 samples at 48 kHz). The analyzer handles two packets in two steps (C2 splits a frame's audio into equal steps of at most one 60 fps frame), so two frames in three cost about twice the best case. **These are the numbers to compare with the app.**
- **One step** (`frame`, the default): exactly one frame of audio per frame (800 samples at 48 kHz), which the analyzer handles in one step. This is the best case, and the cost of one analyzer step.

**steady**: the album colors settled during warm-up, so the blender has nothing to do and the gradient isn't refilled. **crossfade**: new album colors every 0.8 s with an 800 ms fade, so every frame crossfades and refills the gradient, the most a frame ever does. Every row of every run allocated 0 bytes and ran no GC.

The machine was busy, so each delivery ran three times in the same session. The first three tables give the median of the three runs for each number; the last shows each run.

With 10 ms packets (what the app gets):

| rate | samples/frame | scenario | mean | p50 | p99 | p99.9 | share of one core at 60 fps |
|---:|---:|---|---:|---:|---:|---:|---:|
| 48 kHz | 480 or 960 | steady | 79.0 µs | 78.1 µs | 197 µs | 327 µs | 0.47 % |
| 48 kHz | 480 or 960 | crossfade | 91.3 µs | 78.6 µs | 215 µs | 421 µs | 0.55 % |
| 44.1 kHz | 441 or 882 | steady | 100.5 µs | 78.6 µs | 225 µs | 312 µs | 0.60 % |
| 44.1 kHz | 441 or 882 | crossfade | 92.1 µs | 79.1 µs | 224 µs | 458 µs | 0.55 % |
| 192 kHz | 1,920 or 3,840 | steady | 179 µs | 173 µs | 382 µs | 707 µs | 1.07 % |
| 192 kHz | 1,920 or 3,840 | crossfade | 174 µs | 174 µs | 382 µs | 756 µs | 1.04 % |

With one step per frame (the best case):

| rate | samples/frame | scenario | mean | p50 | p99 | p99.9 | share of one core at 60 fps |
|---:|---:|---|---:|---:|---:|---:|---:|
| 48 kHz | 800 | steady | 55.8 µs | 40.1 µs | 126 µs | 257 µs | 0.34 % |
| 48 kHz | 800 | crossfade | 52.2 µs | 40.6 µs | 124 µs | 284 µs | 0.31 % |
| 44.1 kHz | 735 | steady | 57.7 µs | 42.6 µs | 127 µs | 237 µs | 0.35 % |
| 44.1 kHz | 735 | crossfade | 49.6 µs | 40.3 µs | 120 µs | 217 µs | 0.30 % |
| 192 kHz | 3,200 | steady | 144 µs | 120 µs | 278 µs | 723 µs | 0.87 % |
| 192 kHz | 3,200 | crossfade | 164 µs | 127 µs | 288 µs | 633 µs | 0.98 % |

Per stage, mean (median of the three runs; p99 in brackets):

| delivery | rate | scenario | analyzer | blender + gradient | light engine |
|---|---:|---|---:|---:|---:|
| 10 ms packets | 48 kHz | steady | 78.7 µs | 95 ns (151 ns) | 140 ns (508 ns) |
| 10 ms packets | 48 kHz | crossfade | 90.3 µs | 818 ns (2.6 µs) | 154 ns (526 ns) |
| 10 ms packets | 44.1 kHz | steady | 100 µs | 104 ns (174 ns) | 177 ns (730 ns) |
| 10 ms packets | 44.1 kHz | crossfade | 91.1 µs | 850 ns (3.2 µs) | 171 ns (638 ns) |
| 10 ms packets | 192 kHz | steady | 179 µs | 104 ns (260 ns) | 187 ns (890 ns) |
| 10 ms packets | 192 kHz | crossfade | 173 µs | 872 ns (4.0 µs) | 194 ns (858 ns) |
| one step | 48 kHz | steady | 55.5 µs | 92 ns (168 ns) | 140 ns (474 ns) |
| one step | 48 kHz | crossfade | 51.3 µs | 756 ns (1.8 µs) | 148 ns (424 ns) |
| one step | 44.1 kHz | steady | 57.4 µs | 99 ns (148 ns) | 147 ns (410 ns) |
| one step | 44.1 kHz | crossfade | 48.7 µs | 733 ns (1.6 µs) | 125 ns (358 ns) |
| one step | 192 kHz | steady | 144 µs | 103 ns (231 ns) | 162 ns (654 ns) |
| one step | 192 kHz | crossfade | 163 µs | 931 ns (4.1 µs) | 194 ns (710 ns) |

The three runs (whole-frame mean / p50 in µs):

| delivery, rate, scenario | run 1 | run 2 | run 3 |
|---|---|---|---|
| packets, 48 kHz steady | 77.9 / 78.1 | 88.9 / 78.1 | 79.0 / 78.6 |
| packets, 48 kHz crossfade | 102.6 / 80.6 | 76.5 / 78.1 | 91.3 / 78.6 |
| packets, 44.1 kHz steady | 129.9 / 143.9 | 100.5 / 78.6 | 81.9 / 77.6 |
| packets, 44.1 kHz crossfade | 92.1 / 79.1 | 81.9 / 78.1 | 101.0 / 80.6 |
| packets, 192 kHz steady | 179.0 / 172.5 | 179.5 / 172.5 | 175.3 / 171.5 |
| packets, 192 kHz crossfade | 188.8 / 173.6 | 173.5 / 172.5 | 173.9 / 173.6 |
| one step, 48 kHz steady | 57.9 / 40.1 | 49.8 / 40.1 | 55.8 / 40.6 |
| one step, 48 kHz crossfade | 47.7 / 40.3 | 60.1 / 41.3 | 52.2 / 40.6 |
| one step, 44.1 kHz steady | 62.7 / 43.6 | 57.7 / 40.1 | 52.2 / 42.6 |
| one step, 44.1 kHz crossfade | 56.1 / 40.6 | 45.2 / 40.1 | 49.6 / 40.3 |
| one step, 192 kHz steady | 155.0 / 121.1 | 144.0 / 120.1 | 144.3 / 120.1 |
| one step, 192 kHz crossfade | 170.1 / 149.0 | 154.1 / 126.2 | 164.2 / 126.7 |

Reading the numbers:
- The analyzer is practically the whole frame: the light engine and the blender cost well under a microsecond, and a crossfade adds less than 1 µs.
- **Packets cost about twice the best case.** Two frames in three carry two packets, which are two analyzer steps, so the median frame with 10 ms packets (78 µs at 48 kHz) is about twice the one-step median (40 µs), and the share of one core goes from about 0.3 % to about 0.5 %. The 8-hour soak below, which also delivers 10 ms packets (with jittered frame times), agrees: median 76 µs, mean 88 µs. Use the packet rows for the app; the one-step rows are the cost of one analyzer step.
- The medians are the steady cost; the means and the p99 carry the VM's noise (run 1's 44.1 kHz steady row with packets ran at about half speed throughout).
- At 192 kHz the analyzer first decimates the new samples by 4 (C2), so the FFT sees the same 800 samples a frame as at 48 kHz. Comparing like with like, one step costs about 80 µs more at the median (120 vs 40 µs) and about 90 µs more in the mean (144 vs 56 µs): the work on the full-rate samples before the FFT, mostly the decimator.
- The one-step rows match C2's and C3's analyzer-only numbers (about 41 µs a frame at 48 kHz / 60 fps).

## 8-hour soak

`soak` simulates a day of listening and runs it as fast as the CPU allows: 8 simulated hours took 6 minutes. The day (`SoakSchedule.Generate`, seed 1) has:
- tracks of 2.5–5.5 minutes at 86–174 BPM, three quarters full mixes, at −24 to 0 dB, each bringing new album colors with an 800 ms crossfade (a quarter keep the previous track's colors, as on an album; a tenth have no art, so the manual colors show);
- a gap before every track, half with packets of digital silence and half with no packets (as WASAPI loopback does while nothing plays): 60 % are under 2.5 s (too short for IsSilent), 30 % are 4–30 s and 10 % are 1–10 minutes, so Idle Glow, Hide and listening-only frames all run;
- about every 15 minutes a settings edit (WhenSilent toggled first, then Brightness, Sensitivity, which retunes the analyzer on the render thread as the app does, and PrimaryRatio, which refills the gradient), with the new `Settings` records built before the run, off the measured path, as the app's UI thread builds them;
- about every 100 minutes, 2–15 minutes away: a fullscreen pause or the hotkey (Enabled off), so the glow fades out and the render thread blocks;
- about every 5 minutes a render-thread stall of 30 ms to 1.5 s, after which the analyzer gets the whole backlog;
- frame times jittered by ±20 %, and audio in 10 ms packets.

An input (a settings edit, a pause, new album colors) brings the next frame forward from 10 fps or from blocked. At the full rate it waits for the scheduled frame, as the app's overlay loop does since K3's review; the soak has done the same since the stack merge, after the runs below. That moves only frames that inputs had brought forward: a 2-hour run (seed 1) measured 290,501 frames with the rule and 290,509 without, and 720 refills instead of 729, so the numbers below stand.

Before measuring, a scripted 80-second scene that takes every one of those paths runs on a throwaway pipeline, so every path is compiled and has run its static initializers; paths the scene takes only a few times are not tiered up yet (see the memory notes below). The soak's own first 5 simulated seconds are not measured either. Memory is read every simulated minute, without allocating (`/proc/self/statm` on Linux, `GetProcessMemoryInfo` through `Environment.WorkingSet` on Windows), along with the JIT's compiled-method count. The harness keeps its own bookkeeping out of what it measures: the progress snapshots are structs in an array sized before the run, the progress lines are formatted into a reused buffer, and the report counts what the run's thread allocated between frames (0 B here).

**Pass criteria** (printed with every run):
- **0 B/frame:** no measured frame allocates a single byte, applying a settings edit aside. That once-per-edit step is counted and printed on its own line (a Sensitivity edit allocates 120 bytes, in the app too; see [What is and isn't measured](#what-is-and-isnt-measured)).
- **Memory flat:** from the settle point (one simulated hour after warm-up) to the end, the working set grows by less than 1 MB, and from the end of warm-up to the end the live managed heap after a full GC grows by less than 64 KB. The working set grows as paths are tiered up, in steps that come whenever a path that runs only now and then reaches its call counts (here +220 KB in the first hour and +536 KB in the seven after it); the first hour varies most between runs (see below), so it isn't judged. A leak keeps growing without end. The report also prints the growth from the end of warm-up and the trend after the settle point.

The run's own Markdown summary (`--markdown`):

#### Soak: 8:00:00 simulated, seed 1

- Run: 48000 Hz, 60 fps ±20 % jitter, 10 ms packets; 8:00:00 simulated in 339 s of wall time; the first 0:00:05 not measured.
- Schedule: 105 tracks (7 without art, 27 with the previous track's colors), 104 gaps (43 with silent packets, 61 without packets; 45 of 4 s or more), 33 settings edits (Brightness 8, PrimaryRatio 10, Sensitivity 8, WhenSilent 7), 1 fullscreen pause, 4 hotkey offs, 93 stalls (6 over 0.25 s).
- Frames: 1,441,560 measured (298 in warm-up); motion full 1,402,793, slow 24,050, listening 14,704, still 13; time at the full rate 6:29:57, at 10 fps 1:04:32, blocked 0:25:26.
- Paths: 104 crossfades started, 3,656 gradient refills, 10 analyzer resets (capture opened or closed), 89 stalled frames, 47,875 beats; IsSilent in 3.0 %, the glow hidden in 1.0 % and the engine static in 1.0 % of the measured frames.

Cost per measured frame, ns (the analyzer and the light engine include about one clock read each, the blender + gradient about three and the whole frame about six, 21 ns per read here):

| stage | mean | p50 | p99 | p99.9 | max |
|---|---:|---:|---:|---:|---:|
| whole frame | 87,525 | 76,032 | 258,560 | 567,296 | 8,992,391 |
| analyzer | 87,175 | 76,032 | 258,560 | 567,296 | 8,990,739 |
| blender + gradient | 123 | 93 | 386 | 1,068 | 1,578,647 |
| light engine | 192 | 137 | 826 | 2,552 | 3,753,913 |

- CPU: **0.438 %** of one core on average over the measured time (sum of whole-frame costs / simulated time).
- Allocations: **0 B** in 1,441,560 measured frames (0 B/frame), applying settings edits aside; the harness between frames: 0 B.
- Settings edits: 41 applied by measured frames; applying them allocated 960 B, in 8 of them. That step runs once per edit, as in the app, whose MusicGlowSource.ApplySettings allocates there too: a Sensitivity change rebuilds the analyzer's AudioTuning record on the render thread.
- GCs during the measured part (gen 0/1/2): 0/0/0.

| memory | working set | managed heap |
|---|---:|---:|
| start (before the schedule was built) | 27.3 MB | 0.1 MB |
| end of warm-up, 0:00:05 (after a full GC) | 34.2 MB | 1.0 MB |
| settle point, 1:00:05 | 34.4 MB | 1.0 MB |
| end, 8:00:00 | 34.9 MB | 1.1 MB |
| maximum, measured part | 34.9 MB | 1.1 MB |
| live heap after a full GC: end of warm-up → end | | 1,062 KB → 1,065 KB |

| simulated time | working set | managed heap | methods compiled by the JIT |
|---|---:|---:|---:|
| 0:00:05 | 34,992 KB | 1,062 KB | 933 |
| 1:00:05 | 35,212 KB | 1,071 KB | 1,064 |
| 2:00:05 | 35,284 KB | 1,071 KB | 1,094 |
| 3:00:05 | 35,308 KB | 1,079 KB | 1,106 |
| 4:00:05 | 35,384 KB | 1,079 KB | 1,121 |
| 5:00:05 | 35,580 KB | 1,079 KB | 1,138 |
| 6:00:05 | 35,728 KB | 1,079 KB | 1,148 |
| 7:00:05 | 35,748 KB | 1,079 KB | 1,160 |
| 8:00:00 | 35,748 KB | 1,079 KB | 1,165 |

- Working set from the end of warm-up (0:00:05) to the end: +756 KB, one-time growth included; trend from 1:00:05 on: +93 KB per simulated hour (least squares over 428 readings). The JIT had compiled 933 methods by the end of warm-up and compiled 232 more during the measured part (tier-ups of paths that run rarely), 101 of them after 1:00:05.
- **PASS 0 B/frame: 0 bytes allocated in 1,441,560 measured frames after warm-up, applying settings edits aside.**
- **PASS memory flat: from 1:00:05 to the end the working set changed by +536 KB (must be under +1,024 KB), and from the end of warm-up to the end the live managed heap after a full GC changed by +3 KB (must be under +64 KB).**

### Reading the soak

- **Nothing allocates per frame and nothing is collected.** 1.44 million frames over every path allocated 0 bytes, and no GC of any generation ran in 8 hours. The 41 settings snapshots the measured frames applied (33 edits and 8 hotkey toggles) allocated 960 bytes in all, every byte from the 8 Sensitivity edits (120 bytes each, the `AudioTuning` record the app rebuilds on its render thread too). The harness allocated 0 bytes between frames. The live managed heap after a full GC ended 3 KB above where it started; with tiered compilation off (below) it ended 1 KB lower instead, so those few KB come with the JIT's tier-ups, not with the frames.
- **The working set grows only while the JIT compiles.** It rises in steps that line up with the compiled-method count: methods on rarely taken paths (a settings edit, an analyzer reset, a long stall) reach the call counts that trigger a tier-1 recompile (about 30 calls, twice over with dynamic PGO) hours apart, and each batch adds code and JIT memory. The checks behind this:
  - The same soak with tiered compilation off (`DOTNET_TieredCompilation=0`, a diagnostic only; the app keeps the default) compiled 10 methods after warm-up instead of 232, and its working set grew by **+24 KB in 8 hours** (+8 KB after the first hour), at the same 0 B/frame.
  - Seed 2, a different day (105 tracks, 31 edits of which 5 Sensitivity, 2 fullscreen pauses, 2 hotkey offs, 88 stalls), gave the same picture: 0 B in 1,500,742 frames, 600 B from its 5 Sensitivity edits, the harness 0 B, GCs 0/0/0, live heap +3 KB, working set +752 KB in all and +528 KB after the first hour, with 227 methods compiled after warm-up. Both pass.
  - A longer JIT warm-up doesn't remove the growth. Three 8-hour runs of the C12 code before its review fixes (the same frame path; only the harness's bookkeeping differed), built into a scratch console program and run three at a time on this VM after 0.5, 10 and 20 s of the warm-up scene, compiled 223, 189 and 137 methods after warm-up, and their working sets grew by +1,568, +1,468 and +1,376 KB from the end of warm-up, but by +540, +504 and +484 KB after the first hour. Growth since warm-up varies between runs and setups (+756 KB for the run above, which ran alone), while growth after the first hour stayed near 0.5 MB in every run: that is why the criterion starts at the settle point. Warming up until the compiled-method count stops changing doesn't work either: in a scratch run that soaked the warm-up scene 25 times back to back, the count after each run went +0, +3, +0, +0 and then +67 after the 16th.
  - The growth is bounded by the code there is to compile: a leak would keep the working set rising while the method count stays put, and the managed heap would grow with it.
- **Cost:** the mean frame (88 µs, median 76 µs) matches the benchmark's 10 ms packet rows (median 78 µs): most frames bring two packets, which the analyzer handles as two steps (C2); the ±20 % jitter spreads that out a little, and 10 Hz frames bring ten packets (six analyzer steps). Averaged over the day, Core's frames took 0.44 % of one core, idle time included.
- **The maxima** (up to 9 ms) are single frames delayed by the shared VM (the host preempting it); p99.9 is under 0.6 ms.

## Measuring the app on Windows

Doc 07 Phase 3 asks for the real app's CPU while playing music at 60 fps on 1–2 monitors (target **under 2 %**) and in Off (target **about 0 %**, and no `Present`). The CLAUDE.md definition of done adds **about 0 % when idle**. Only a Windows PC can measure this. Steps for Maxwell:

1. **Build and start a Release build** (a Debug build or a debugger costs more):
   ```powershell
   dotnet build -c Release
   dotnet run -c Release --project src/Rimlight.App
   ```
   or install `RimlightSetup.exe`. Close the Settings window: while it's open (Appearance page and preview) the app does more work.
2. **Install dotnet-counters** once: `dotnet tool install --global dotnet-counters`.
3. **Watch the counters** in a second terminal:
   ```powershell
   dotnet-counters monitor --name Rimlight --refresh-interval 5 --counters "System.Runtime[cpu-usage,working-set,alloc-rate,gen-0-gc-count]"
   ```
   `CPU Usage (%)` is the process's share of the whole machine (all logical processors), the same scale as Task Manager. It is computed from `GetProcessTimes` / `GetSystemTimes` (checked in the .NET 8 runtime). Task Manager's **Processes** tab (Rimlight is listed under Apps or Background processes) shows the same with one decimal place, plus a **GPU** column.
4. **Measure each case for 2 minutes, after 30 s to settle,** and write down the typical `cpu-usage` value and the Task Manager CPU and GPU columns:

   | Case | How | Target |
   |---|---|---|
   | Playing, 1 monitor | Music in Spotify or YouTube; tray → Mode → Music Sync; Settings → Displays → Frame rate limit: 60 frames a second | **under 2 %** |
   | Playing, 2 monitors | the same with the glow on both monitors | **under 2 %** |
   | Idle Glow | pause the music (When it's quiet: Switch to Idle Glow, the default); wait 5 s | **about 0 %** |
   | Hidden while quiet | Settings → Motion → When it's quiet: Hide the glow; pause the music; wait 5 s | **about 0 %** |
   | Off | tray → Mode → Off, or Ctrl+Alt+L (Glow on unchecked) | **about 0 %**, GPU 0 % |

5. **Off draws nothing:** a Debug build's tray menu has "Render test (debug)", whose status line should read 0 frames/s and 0 presents/s in Off (and 10 frames/s with 0 presents/s while hidden). In a Release build, Task Manager's GPU column for Rimlight should stay at 0 %.
6. **Allocations:** with Settings closed and music playing, `alloc-rate` should sit near 0 B/s and `gen-0-gc-count` should not climb. Core's frames allocate nothing (above; a Sensitivity edit allocates 120 bytes once); a steady allocation rate would come from Platform or App code.
7. **Report back** the CPU model, the number of logical processors, the monitors and their refresh rates, and the five numbers. On a 16-thread CPU, 2 % of the machine is about a third of one core; Core's share of that (about 0.5 % of one core at 48 kHz while playing, with 10 ms packets, from the packet rows above and the soak) shows as roughly 0.03 % of the machine.

Doc 07 Phase 7's 8-hour run of the app itself (working set at start and end, no handle leaks) is K11's: `dotnet-counters collect --name Rimlight --refresh-interval 60 --format csv --output rimlight-8h.csv --counters "System.Runtime[cpu-usage,working-set,gc-heap-size]"` records it, and Task Manager's Details tab can add a Handles column.

## Reproducing

From the repository root, on Linux or Windows:

```bash
dotnet build Rimlight.sln -c Release      # or Rimlight.Core.slnf: everything but the WPF app

# Frame benchmark (three runs of each for this report); --rates 48000,44100,192000 is the default
dotnet run -c Release --project src/Rimlight.Bench -- frame --frames 30000 --packet-ms 10   # 10 ms packets, as the app
dotnet run -c Release --project src/Rimlight.Bench -- frame --frames 30000                  # one step per frame

# 8-hour soak with hourly progress lines and the Markdown summary above (6 minutes here)
dotnet run -c Release --project src/Rimlight.Bench -- soak --report-every 60 --markdown

# Other days and lengths
dotnet run -c Release --project src/Rimlight.Bench -- soak --seed 2 --markdown
dotnet run -c Release --project src/Rimlight.Bench -- soak --duration 30m
```

For this report the built `Rimlight.Bench.dll` was run directly (`dotnet src/Rimlight.Bench/bin/Release/net8.0/Rimlight.Bench.dll …`), which is the same program. `--help` lists every option. `soak` exits with 1 if either verdict fails, `frame` and `analyzer` if any frame allocates. The schedule is deterministic for a seed; timings and the working set are not.
