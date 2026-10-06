# Contributing to Rimlight

Thanks for helping out. A clear bug report with logs is as useful as a pull request, so every kind of contribution counts.

- **Something broken?** [Report a bug](https://github.com/sussyswimmer/bordervisualizer/issues/new?template=bug_report.yml).
- **An idea?** [Suggest a feature](https://github.com/sussyswimmer/bordervisualizer/issues/new?template=feature_request.yml).
- **A security problem?** Please don't describe it in an issue. Follow [SECURITY.md](SECURITY.md) instead.

Everyone who takes part agrees to the [Code of Conduct](CODE_OF_CONDUCT.md).

## Build from source

You need:

- Windows 10 version 2004 (build 19041) or later, or Windows 11, on x64 or ARM64.
- The [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). A newer SDK alongside it is fine.
- Git, and any editor: Visual Studio 2022, VS Code with the C# Dev Kit, or Rider.

In PowerShell:

```powershell
git clone https://github.com/sussyswimmer/bordervisualizer.git
cd bordervisualizer
dotnet build -c Release                                  # must finish with 0 warnings
dotnet test                                              # must be green
dotnet run --project src/Rimlight.App                    # starts Rimlight in the tray
dotnet run --project src/Rimlight.App -- --debug-visualizer
dotnet run --project src/Rimlight.App -- --demo
```

- **`--debug-visualizer`** opens a small window with the live spectrum, Level, Bass, Beat, the beat threshold, a beat counter and the estimated BPM. Sliders change every audio tuning parameter live, and **Copy params as JSON** copies them so you can share a tuning.
- **`--demo`** drives the glow with a synthetic 120 BPM beat and cycles through a few palettes, so you can check visuals without playing music.

Close any installed copy of Rimlight first: only one instance runs at a time, and a second launch just opens the running one's Settings.

### Without Windows

`Rimlight.Core` is plain .NET 8 with no Windows APIs, so the engine, its tests and the offline tools build and run on Linux and macOS too:

```bash
dotnet build Rimlight.Core.slnf -c Release
dotnet test Rimlight.Core.slnf -c Release
dotnet run --project tools/wav-analyze -- --help         # beats, BPM, CSV and a PNG plot for a WAV file
```

The full solution also builds on Linux (the Windows projects set `EnableWindowsTargeting`), but the app itself only runs on Windows.

## Project layout

```text
src/
  Directory.Build.props   App name, version, Nullable, TreatWarningsAsErrors
  Rimlight.Core/          Pure .NET 8, no Windows APIs: beat detection, colors, light engine, settings
    Contracts/            The interfaces between the two lanes (frozen, see below)
  Rimlight.Platform/      Windows code: overlay windows, Direct3D 11 + DirectComposition, WASAPI loopback,
                          media session, monitor and power watchers
  Rimlight.App/           The WPF tray app: startup, tray menu, Settings window, updates
  Rimlight.Tests/         xUnit tests for Core and the tools
  Rimlight.Bench/         Benchmarks and long soak runs
tools/                    Offline tools: WAV analyzer, synthetic test tracks, icon generator
build/                    Packaging and release scripts
assets/                   Icon and README art
docs/                     The spec (01 to 09), PROGRESS.md and HANDOFF.md
```

The spec in `docs/` is the source of truth. Start with [01-PRD](docs/01-PRD.md) for what the app does and [02-ARCHITECTURE](docs/02-ARCHITECTURE.md) for how it fits together.

## The two lanes

Rimlight is built in two lanes that meet only at the interfaces in `src/Rimlight.Core/Contracts/`. [docs/09-WORK-SPLIT.md](docs/09-WORK-SPLIT.md) has the full rules and task list.

| Lane | Owns | Checked by |
|---|---|---|
| **A: Engine & Pipeline** | `Rimlight.Core` (except `Contracts/`), `Rimlight.Tests`, `Rimlight.Bench`, `tools/`, `build/`, `.github/`, the community files | Unit tests, benchmarks, CI on Linux and Windows |
| **B: Windows & Experience** | `Rimlight.Platform`, `Rimlight.App`, `assets/`, `README.md` | Running the app on Windows, manual test checklists |

- **Contracts are frozen.** Platform and App use only `CoreFactory` and the interfaces in `Contracts/`, never Core internals. Changing a contract takes a `CONTRACT CHANGE` entry in [docs/HANDOFF.md](docs/HANDOFF.md) that the maintainer approves first.
- **Keep a pull request inside one lane.** If you need something from the other lane, add an entry to [docs/HANDOFF.md](docs/HANDOFF.md) (the format is in doc 09 §5) and work around it locally until it lands.
- Both lanes append to [docs/PROGRESS.md](docs/PROGRESS.md) and [docs/HANDOFF.md](docs/HANDOFF.md).

New here? You don't need to learn the lanes in depth. Keep each pull request to one area, and open an issue first if you want to change anything in `Contracts/`.

## Branches, commits and pull requests

- One change per branch and per pull request into `main`. Pull requests are squash-merged.
- Branch names: `claude/<task-id>-<slug>` or `codex/<task-id>-<slug>` for the planned tasks in doc 09 (for example `claude/k4-now-playing`), and a short descriptive name for anything else (`fix/tray-menu-keyboard`).
- Commit messages and pull request titles use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/): `type(scope): summary`, for example `feat(overlay): reassert topmost after wake` or `fix(audio): keep beats at low volume`.
  - Types: `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `build`, `ci`, `chore`.
  - Common scopes: `audio`, `overlay`, `color`, `lighting`, `settings`, `tray`, `app`, `core`, `bench`, `tools`.
  - The title's type and scope set the pull request's labels, which sort it into the release notes.
- Mention the task ID from doc 09 in the title or description when there is one (`C11`, `K4`).

## Before you open a pull request

- **Zero warnings.** `dotnet build -c Release` must finish with 0 warnings. `TreatWarningsAsErrors` is on, so a warning fails the build.
- **Tests are green.** `dotnet test` passes, and new logic comes with tests.
  - Use synthetic audio and generated images. Never commit copyrighted songs or album covers.
  - Core is deterministic: time arrives as `dtSeconds`, random number generators are seeded, and nothing reads the clock.
- **Nothing on the hot path allocates or locks.** The audio callback only copies samples into the ring buffer. The render thread owns Direct3D and all DSP state. `IAudioAnalyzer.Process`, `ILightEngine.Update` and `IPaletteBlender.Update`/`FillGradient` run every frame and must not allocate. Tests check this with `GC.GetAllocatedBytesForCurrentThread()`.
- **Write it down.** Log task work in [docs/PROGRESS.md](docs/PROGRESS.md), and add a line under `[Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for anything a user would notice.
- **Visible change? Add a manual test checklist** to the pull request: what to run, what you should see, and what counts as a bug.

The [pull request template](.github/PULL_REQUEST_TEMPLATE.md) has the same checklist.

## Product rules

These hold for every change, in code and in docs:

- Audio comes only from system output through WASAPI loopback. **Rimlight never opens the microphone.**
- No telemetry and no analytics. The only network request the app makes is the update check against this repository's GitHub Releases.
- Rimlight is an original product. Don't name other apps, compare Rimlight with them, or borrow their look or wording.
- No made-up numbers, testimonials or badges anywhere.
- The name "Rimlight" is defined only in `src/Directory.Build.props` (`AppName`) and `src/Rimlight.Core/AppInfo.cs`. Use those instead of typing the name into new code.

## Reporting a bug with logs

Logs make most bugs quick to find. Rimlight keeps them in `%LOCALAPPDATA%\Rimlight\logs` (up to five files of 1 MB each).

1. Right-click the Rimlight tray icon and choose **Open logs folder**. If Rimlight won't start, paste `%LOCALAPPDATA%\Rimlight\logs` into the File Explorer address bar instead.
2. Select every file in the folder, right-click, and zip them: **Compress to** on Windows 11, or **Send to > Compressed (zipped) folder** on Windows 10.
3. [Open a bug report](https://github.com/sussyswimmer/bordervisualizer/issues/new?template=bug_report.yml) and drag the zip into the **Logs** box.

Logs never contain audio, but they can include device, monitor and track names. Have a quick look before you upload them.

If the problem involves settings, attach `%APPDATA%\Rimlight\settings.json` too.

## License

Rimlight is [MIT licensed](LICENSE). By contributing, you agree that your contributions are licensed under the same terms.
