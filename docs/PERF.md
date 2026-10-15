# Performance and soak results

What the render thread's Core work costs per frame, and what 8 hours of it do to memory. This covers C12 (doc 09 §4), AGENTS.md's "Bench report" item and the Core side of doc 07 Phases 3 and 7. Everything here was measured with `Rimlight.Bench` on a Linux cloud VM, not on a Windows PC. The real app's CPU use has to be measured on Windows; the steps are in [Measuring the app on Windows](#measuring-the-app-on-windows).

## At a glance

| What | Result (Linux VM, 2026-10-15) | Target |
|---|---|---|
| One render-thread frame at 48 kHz / 60 fps (800 samples): analyzer + palette blender + light engine | median 40 µs, mean 51–55 µs (median of three runs) | none set; informational |
| The same, as a share of one CPU core at 60 fps | about 0.3 % of one core (0.9 % at 192 kHz) | the whole app under 2 % of the machine (doc 07 Phase 3) |
| Analyzer / light engine / blender + gradient, mean per frame at 48 kHz | 51 µs / 0.13 µs / 0.09 µs (0.8 µs while crossfading) | AGENTS.md: report ns/frame |
| Allocations per frame | **0 B**: every benchmark row, and all 1,441,560 measured frames of the 8-hour soak | 0 B/frame (AGENTS.md) |
| GCs in the 8-hour soak's measured part | 0 / 0 / 0 (gen 0/1/2) | none |
| Memory over 8 simulated hours | **flat**: live managed heap +8 KB; working set +408 KB after the first hour (+776 KB in all, from JIT tier-ups) | flat (doc 07 Phase 7) |
| The real app's CPU on Windows | **not measured yet** | under 2 % playing at 60 fps on 1–2 monitors; about 0 % idle and in Off |

## Machine and software

- **Machine:** a cloud VM (Firecracker, kernel 6.18.44), 4 logical CPUs reported as "Intel(R) Xeon(R) Processor @ 2.80GHz" (AVX2 and AVX-512), 16 GB RAM. The host is shared: other jobs ran during these measurements (load average 1–3 on 4 CPUs), so timings move by tens of percent between runs.
- **Software:** Ubuntu 24.04.5 LTS x64, .NET SDK 8.0.426 (`dotnet --info`), runtime Microsoft.NETCore.App 8.0.32, Release build, default runtime settings (tiered compilation and dynamic PGO on; `Rimlight.Bench.csproj` sets `TieredPGO`).
- **Code:** branch `wip/c12-soak-perf`, on top of the integration commit `8abd6f5` (Core from C1–C11, `CoreFactory` returning the real implementations).

## What is and isn't measured

**Measured.** `FramePipeline` in Bench runs exactly what the app's render thread runs per frame in `MusicGlowSource.NextFrame` (`src/Rimlight.App/Overlay/MusicGlowSource.cs`): the settings and album-palette checks (with `SetTarget` on a new palette or settings change), `IAudioAnalyzer.Process` (and `Reset` when the capture opens or closes), `IPaletteBlender.Update`, `ILightEngine.Update`, `FillGradient` whenever the app would refill, and the frame-pacing decision. The app is Windows-only, so this glue is mirrored in Bench rather than shared; the Core objects come from `CoreFactory`, as in the app. Timing uses `Stopwatch` between the stages, and allocations are counted with `GC.GetAllocatedBytesForCurrentThread()` around `NextFrame` alone. The soak's frame schedule follows the overlay's pacer (`PaceModel` mirrors `FramePacer`): the full rate for music, 10 Hz for Idle Glow and while hidden and listening, and no frames at all while Off, disabled or paused.

**Not measured.**
- Everything Windows does: the WASAPI capture thread and the copy out of the loopback ring, Direct3D 11 drawing and `Present`, DirectComposition and DWM, the GPU, WPF, the media session reader and the system watchers. The real app's CPU use is these plus the numbers here.
- The app's memory. The working set here is `Rimlight.Bench`'s (about 34 MB, the .NET runtime plus the harness); the app also carries WPF, D3D and WinRT.
- Windows' scheduler, timer resolution and power management. On Windows, `Stopwatch` ticks are 100 ns, so sub-microsecond percentiles come out quantized there; the means are still right.
- Each timed stage includes the clock reads around it: about one read (21–30 ns here) for the analyzer and the light engine, about three for the blender + gradient, and about six for the whole frame. Most of the blender's 85–95 ns while idle is clock reads.

## Frame benchmark

`frame` runs the music of C3's synthetic full mix (128 BPM) through `FramePipeline` in exact frame-sized chunks, 2,000 warm-up frames (at least 0.5 s) and then 30,000 measured frames per row. **steady**: the album colors settled during warm-up, so the blender has nothing to do and the gradient isn't refilled. **crossfade**: new album colors every 0.8 s with an 800 ms fade, so every frame crossfades and refills the gradient, the most a frame ever does. Every row of every run allocated 0 bytes and ran no GC.

The machine was busy, so it ran three times. The first two tables give the median of the three runs for each number; the third shows each run.

| rate | samples/frame | scenario | mean | p50 | p99 | p99.9 | share of one core at 60 fps |
|---:|---:|---|---:|---:|---:|---:|---:|
| 48 kHz | 800 | steady | 50.9 µs | 40.3 µs | 127 µs | 290 µs | 0.31 % |
| 48 kHz | 800 | crossfade | 54.7 µs | 40.6 µs | 121 µs | 213 µs | 0.33 % |
| 44.1 kHz | 735 | steady | 55.3 µs | 40.1 µs | 123 µs | 247 µs | 0.33 % |
| 44.1 kHz | 735 | crossfade | 49.1 µs | 40.6 µs | 117 µs | 258 µs | 0.29 % |
| 192 kHz | 3,200 | steady | 147 µs | 122 µs | 282 µs | 760 µs | 0.88 % |
| 192 kHz | 3,200 | crossfade | 154 µs | 134 µs | 280 µs | 484 µs | 0.92 % |

Per stage, mean (median of the three runs; p99 in brackets):

| rate | scenario | analyzer | blender + gradient | light engine |
|---:|---|---:|---:|---:|
| 48 kHz | steady | 50.6 µs | 85 ns (145 ns) | 128 ns (400 ns) |
| 48 kHz | crossfade | 53.6 µs | 797 ns (1.9 µs) | 142 ns (344 ns) |
| 44.1 kHz | steady | 55.0 µs | 93 ns (145 ns) | 148 ns (424 ns) |
| 44.1 kHz | crossfade | 48.2 µs | 745 ns (1.8 µs) | 133 ns (446 ns) |
| 192 kHz | steady | 146 µs | 92 ns (164 ns) | 185 ns (750 ns) |
| 192 kHz | crossfade | 153 µs | 898 ns (4.1 µs) | 195 ns (802 ns) |

The three runs (whole-frame mean / p50 in µs):

| rate, scenario | run 1 | run 2 | run 3 |
|---|---|---|---|
| 48 kHz steady | 61.5 / 47.0 | 49.1 / 40.3 | 50.9 / 39.8 |
| 48 kHz crossfade | 54.7 / 41.1 | 46.7 / 40.3 | 56.1 / 40.6 |
| 44.1 kHz steady | 55.3 / 40.1 | 57.0 / 42.9 | 48.9 / 39.8 |
| 44.1 kHz crossfade | 49.1 / 40.6 | 81.4 / 81.2 | 42.1 / 40.1 |
| 192 kHz steady | 147 / 122 | 155 / 125 | 141 / 121 |
| 192 kHz crossfade | 150 / 126 | 156 / 136 | 154 / 134 |

Reading the numbers:
- The analyzer is practically the whole frame: the light engine and the blender cost well under a microsecond, and a crossfade adds less than 1 µs. The medians (40 µs at 48 kHz) are the steady cost; the means and the p99 carry the VM's noise (run 2's 44.1 kHz crossfade row ran at half speed throughout).
- At 192 kHz the analyzer first decimates the 3,200 new samples by 4 (C2). That is the extra ~80 µs: the decimator measured alone took 90–110 µs a call here (a scratch measurement, within this machine's noise). The FFT then sees 800 samples a frame, as at 48 kHz.
- These match C2's and C3's analyzer-only numbers (about 41 µs a frame at 48 kHz / 60 fps).

## 8-hour soak

`soak` simulates a day of listening and runs it as fast as the CPU allows: 8 simulated hours took 6 minutes. The day (`SoakSchedule.Generate`, seed 1) has:
- tracks of 2.5–5.5 minutes at 86–174 BPM, three quarters full mixes, at −24 to 0 dB, each bringing new album colors with an 800 ms crossfade (a quarter keep the previous track's colors, as on an album; a tenth have no art, so the manual colors show);
- a gap before every track, half with packets of digital silence and half with no packets (as WASAPI loopback does while nothing plays): 60 % are under 2.5 s (too short for IsSilent), 30 % are 4–30 s and 10 % are 1–10 minutes, so Idle Glow, Hide and listening-only frames all run;
- about every 15 minutes a settings edit (WhenSilent toggled first, then Brightness, Sensitivity, which retunes the analyzer, and PrimaryRatio, which refills the gradient), with the new `Settings` records built before the run, off the measured path;
- about every 100 minutes, 2–15 minutes away: a fullscreen pause or the hotkey (Enabled off), so the glow fades out and the render thread blocks;
- about every 5 minutes a render-thread stall of 30 ms to 1.5 s, after which the analyzer gets the whole backlog;
- frame times jittered by ±20 %, and audio in 10 ms packets.

Before measuring, a scripted 80-second scene that takes every one of those paths runs on a throwaway pipeline (JIT, tier-up and static initializers), and the soak's own first 5 simulated seconds are not measured. Memory is read every simulated minute, without allocating (`/proc/self/statm` on Linux, `GetProcessMemoryInfo` through `Environment.WorkingSet` on Windows), along with the JIT's compiled-method count.

**Pass criteria** (printed with every run):
- **0 B/frame:** no measured frame allocates a single byte.
- **Memory flat:** from the settle point (one simulated hour after warm-up) to the end, the working set grows by less than 1 MB, and from the end of warm-up to the end the live managed heap after a full GC grows by less than 64 KB. Before the settle point the working set grows once while each path is compiled and tiered up the first few times it runs; a leak keeps growing after it. The report also prints the growth from the end of warm-up and the trend after the settle point.

The run's own Markdown summary (`--markdown`):

#### Soak: 8:00:00 simulated, seed 1

- Run: 48000 Hz, 60 fps ±20 % jitter, 10 ms packets; 8:00:00 simulated in 355 s of wall time; the first 0:00:05 not measured.
- Schedule: 105 tracks (7 without art, 27 with the previous track's colors), 104 gaps (43 with silent packets, 61 without packets; 45 of 4 s or more), 33 settings edits (Brightness 8, PrimaryRatio 10, Sensitivity 8, WhenSilent 7), 1 fullscreen pause, 4 hotkey offs, 93 stalls (6 over 0.25 s).
- Frames: 1,441,560 measured (298 in warm-up); motion full 1,402,793, slow 24,050, listening 14,704, still 13; time at the full rate 6:29:57, at 10 fps 1:04:32, blocked 0:25:26.
- Paths: 104 crossfades started, 3,656 gradient refills, 10 analyzer resets (capture opened or closed), 89 stalled frames, 47,875 beats; IsSilent in 3.0 %, the glow hidden in 1.0 % and the engine static in 1.0 % of the measured frames.

Cost per measured frame, ns (the analyzer and the light engine include about one clock read each, the blender + gradient about three and the whole frame about six, 21 ns per read here):

| stage | mean | p50 | p99 | p99.9 | max |
|---|---:|---:|---:|---:|---:|
| whole frame | 91,678 | 76,544 | 281,600 | 616,448 | 32,896,267 |
| analyzer | 91,272 | 76,032 | 279,552 | 612,352 | 32,891,618 |
| blender + gradient | 133 | 93 | 546 | 1,220 | 992,207 |
| light engine | 236 | 150 | 1,108 | 2,952 | 4,204,014 |

- CPU: **0.459 %** of one core on average over the measured time (sum of whole-frame costs / simulated time).
- Allocations: **0 B** in 1,441,560 measured frames (0 B/frame).
- GCs during the measured part (gen 0/1/2): 0/0/0.

| memory | working set | managed heap |
|---|---:|---:|
| start (before the schedule was built) | 26.5 MB | 0.1 MB |
| end of warm-up, 0:00:05 (after a full GC) | 33.7 MB | 1.9 MB |
| settle point, 1:00:05 | 34.0 MB | 1.9 MB |
| end, 8:00:00 | 34.4 MB | 1.9 MB |
| maximum, measured part | 34.4 MB | 1.9 MB |
| live heap after a full GC: end of warm-up → end | | 1,975 KB → 1,983 KB |

| simulated time | working set | managed heap | methods compiled by the JIT |
|---|---:|---:|---:|
| 0:00:05 | 34,480 KB | 1,975 KB | 912 |
| 1:00:05 | 34,848 KB | 1,983 KB | 1,044 |
| 2:00:05 | 34,856 KB | 1,991 KB | 1,069 |
| 3:00:05 | 34,848 KB | 1,991 KB | 1,078 |
| 4:00:05 | 34,916 KB | 1,991 KB | 1,090 |
| 5:00:05 | 35,044 KB | 1,991 KB | 1,106 |
| 6:00:05 | 35,220 KB | 1,991 KB | 1,114 |
| 7:00:05 | 35,256 KB | 1,991 KB | 1,124 |
| 8:00:00 | 35,256 KB | 1,991 KB | 1,129 |

- Working set from the end of warm-up (0:00:05) to the end: +776 KB, one-time growth included; trend from 1:00:05 on: +80 KB per simulated hour (least squares over 428 readings). The JIT had compiled 912 methods by the end of warm-up and compiled 217 more during the measured part (tier-ups of paths that run rarely), 85 of them after 1:00:05.
- **PASS 0 B/frame: 0 bytes allocated in 1,441,560 measured frames after warm-up.**
- **PASS memory flat: from 1:00:05 to the end the working set changed by +408 KB (must be under +1,024 KB), and from the end of warm-up to the end the live managed heap after a full GC changed by +8 KB (must be under +64 KB).**

### Reading the soak

- **Nothing allocates and nothing is collected.** 1.44 million frames over every path allocated 0 bytes, and no GC of any generation ran in 8 hours. The live managed heap ended 8 KB above where it started; the frames allocated nothing, so that is the harness's own bookkeeping between frames (its progress snapshots and output).
- **The working set grows only while the JIT compiles.** It rises in steps that line up with the compiled-method count: methods on rarely taken paths (a settings edit, an analyzer reset, a long stall) reach the 30 calls that trigger a tier-1 recompile hours apart, and each batch adds code and JIT memory. Two checks back this up:
  - The same soak with tiered compilation off (`DOTNET_TieredCompilation=0`, a diagnostic only; the app keeps the default) compiled 10 methods after warm-up instead of 217, and its working set grew by **+20 KB in 8 hours** (+8 KB after the first hour), at the same 0 B/frame.
  - Seed 2, a different day (105 tracks, 31 edits, 2 fullscreen pauses, 2 hotkey offs, 88 stalls), gave the same picture: 0 B in 1,500,742 frames, GCs 0/0/0, live heap +0 KB, working set +780 KB in all and +424 KB after the first hour, with 212 methods compiled after warm-up. Both pass.
  - The growth is bounded by the code there is to compile: a leak would keep the working set rising while the method count stays put, and the managed heap would grow with it.
- **Cost:** the mean frame (92 µs) is above the benchmark's because 10 ms packets with ±20 % jitter often bring two packets (960 samples) in one frame, which the analyzer handles as two steps (C2), and 10 Hz frames bring six. Averaged over the day, Core's frames took 0.46 % of one core, idle time included.
- **The maxima** (up to 33 ms) are single frames delayed by the shared VM (the host preempting it); p99.9 is under 0.62 ms.

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
6. **Allocations:** with Settings closed and music playing, `alloc-rate` should sit near 0 B/s and `gen-0-gc-count` should not climb. Core's frames allocate nothing (above); a steady allocation rate would come from Platform or App code.
7. **Report back** the CPU model, the number of logical processors, the monitors and their refresh rates, and the five numbers. On a 16-thread CPU, 2 % of the machine is about a third of one core; Core's share of that (about 0.3 % of one core at 48 kHz) shows as roughly 0.02 % of the machine.

Doc 07 Phase 7's 8-hour run of the app itself (working set at start and end, no handle leaks) is K11's: `dotnet-counters collect --name Rimlight --refresh-interval 60 --format csv --output rimlight-8h.csv --counters "System.Runtime[cpu-usage,working-set,gc-heap-size]"` records it, and Task Manager's Details tab can add a Handles column.

## Reproducing

From the repository root, on Linux or Windows:

```bash
dotnet build Rimlight.sln -c Release      # on Linux, Rimlight.Core.slnf builds Core, the tests and Bench

# Frame benchmark (three runs for this report); --rates 48000,44100,192000 is the default
dotnet run -c Release --project src/Rimlight.Bench -- frame --frames 30000

# 8-hour soak with hourly progress lines and the Markdown summary above (6 minutes here)
dotnet run -c Release --project src/Rimlight.Bench -- soak --report-every 60 --markdown

# Other days and lengths
dotnet run -c Release --project src/Rimlight.Bench -- soak --seed 2 --markdown
dotnet run -c Release --project src/Rimlight.Bench -- soak --duration 30m
```

For this report the built `Rimlight.Bench.dll` was run directly (`dotnet src/Rimlight.Bench/bin/Release/net8.0/Rimlight.Bench.dll …`), which is the same program. `--help` lists every option. `soak` exits with 1 if either verdict fails, `frame` and `analyzer` if any frame allocates. The schedule is deterministic for a seed; timings and the working set are not.
