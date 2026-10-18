# Changelog

All notable changes to Rimlight are listed here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Edge glow.** A soft two-color glow along the edges of every monitor. It's click-through, stays on top, never takes focus, and doesn't show up in Alt+Tab or on the taskbar. It stays sharp on mixed-DPI setups, rebuilds itself when you plug in, remove or rearrange a monitor, and comes back on its own after a graphics driver reset. ([#8](https://github.com/sussyswimmer/bordervisualizer/pull/8))
- **Listens to what your PC plays.** Rimlight hears system audio through WASAPI loopback and never opens the microphone. When you switch between speakers and headphones, it follows the new output device. ([#9](https://github.com/sussyswimmer/bordervisualizer/pull/9))
- **Beat detection.** Rimlight tracks loudness and bass and detects kicks, with a tempo estimate. It adapts to your volume, so the glow moves the same whether the music is loud or quiet, and it notices when the music stops. ([#2](https://github.com/sussyswimmer/bordervisualizer/pull/2), [#5](https://github.com/sussyswimmer/bordervisualizer/pull/5), [#7](https://github.com/sussyswimmer/bordervisualizer/pull/7))
- **Music Sync and Idle Glow.** In Music Sync, brightness follows the loudness, every beat sends a pulse through the glow, and the colors drift slowly around the screen. Idle Glow breathes gently on a 6-second cycle. When the music stops, the glow fades to Idle Glow (or hides, if you choose) after 2 seconds, and it's back within 150 ms when the music returns. ([#11](https://github.com/sussyswimmer/bordervisualizer/pull/11))
- **Colors from album art.** Rimlight picks two vivid colors from the cover of whatever is playing in any app that shows up in Windows' media controls, tunes them so they glow well, and crossfades to them in under a second when the track changes. Covers with no real artwork, and apps that share none, get your own colors instead. ([#12](https://github.com/sussyswimmer/bordervisualizer/pull/12), [#18](https://github.com/sussyswimmer/bordervisualizer/pull/18), [#20](https://github.com/sussyswimmer/bordervisualizer/pull/20))
- **Pauses when it should.** A fullscreen game or video pauses the glow on its own monitor only, and the other monitors keep glowing. Locking the PC, sleep, the display turning off and a disconnected Remote Desktop session pause it everywhere. On battery it saves power by default, or pauses, as you choose. ([#19](https://github.com/sussyswimmer/bordervisualizer/pull/19))
- **Easy on your PC.** The glow draws nothing while the picture doesn't change and only 10 frames a second while it breathes in Idle Glow. On battery, it's capped at 30 frames a second at half resolution by default. ([#14](https://github.com/sussyswimmer/bordervisualizer/pull/14))
- **Tray icon, shortcut and startup.** Rimlight lives in the notification area, as a glowing rounded square that dims while the glow is off, and its tooltip names the track that's playing. Click it to turn the glow on or off, double-click it for Settings, and right-click it for the mode, colors, presets, updates and the logs folder (`%APPDATA%\Rimlight\logs`, which never holds audio or track titles). **Ctrl+Alt+L** turns the glow on and off from anywhere, Rimlight starts with Windows, and starting it again just opens Settings. On the first run a short welcome says where it went. ([#1](https://github.com/sussyswimmer/bordervisualizer/pull/1), [#14](https://github.com/sussyswimmer/bordervisualizer/pull/14), [#15](https://github.com/sussyswimmer/bordervisualizer/pull/15), [#22](https://github.com/sussyswimmer/bordervisualizer/pull/22))
- **Settings window.** Six pages, with a live preview of the glow at the top of Appearance, five presets (Aurora, Sunset, Neon, Ember and Minimal), color pickers, a sound level meter, your monitors by name, and a shortcut recorder. Every change applies at once, and the window follows Windows' light or dark mode. Your settings are saved in `%APPDATA%\Rimlight`, and a damaged settings file is set aside instead of stopping the app. ([#16](https://github.com/sussyswimmer/bordervisualizer/pull/16), [#24](https://github.com/sussyswimmer/bordervisualizer/pull/24))
- **Installer and automatic updates.** `RimlightSetup.exe` (x64) and `RimlightSetup-arm64.exe` install Rimlight for your user account with no admin prompt, and portable zips are there too. An installed copy checks GitHub Releases twice a day while **Settings > Behavior > Update automatically** is on, downloads new versions in the background and installs them when you choose **Restart to update** in the tray menu (or click "Update ready"), or the next time it starts. **Check for updates** works anytime. Uninstalling keeps your settings and logs in `%APPDATA%\Rimlight`. ([#21](https://github.com/sussyswimmer/bordervisualizer/pull/21))
- **Demo mode.** `Rimlight.exe --demo` plays a synthetic 120 BPM beat through four palettes, so you can see or record the glow without music. It uses your glow's shape but saves nothing of its own, and it also starts in a copy that is already running. Uncheck **Demo mode** in the tray, or turn the glow off, to end it.
- **For contributors:**
  - `wav-analyze` runs the beat detector on any WAV file and writes the beats, the tempo, a CSV and a plot. It can also generate synthetic test tracks, and there's an analyzer benchmark. All of it runs on Linux too. ([#13](https://github.com/sussyswimmer/bordervisualizer/pull/13))
  - Continuous integration builds and tests every pull request on Linux and Windows, labels it from its title, and sorts it into release-note sections. ([#10](https://github.com/sussyswimmer/bordervisualizer/pull/10))
  - A contributing guide, security policy, code of conduct, issue forms and a pull request template.
  - `--debug-visualizer` shows the live spectrum, beats and tempo, with a slider for every audio setting and "Copy params as JSON" for sharing a tuning.
  - A frame benchmark and an 8-hour soak run of the audio and light engine, with the results in `docs/PERF.md`.
  - Tests for the Windows app's own logic (frame pacing, the tray tooltip, shortcuts, presets, the demo, updates' schedule and more), which run on Linux too.
  - Smoke tests that open every window and Settings page with the Fluent styles loaded. They run on Windows, in CI and with `dotnet test src/Rimlight.App.Tests`.
  - The debug visualizer's **Record 2 min** saves what the analyzer and the light computed for every frame as a CSV in the logs folder (numbers only, never sound), and `wav-analyze plot` draws it like a WAV analysis.
  - `wav-analyze --engine` replays a song through the overlay's per-frame work. `tools/corpus/` rebuilds the real-music test set the dynamics are measured on; no audio is in the repository.

### Changed

- **Calm music moves like a wave.** Brightness follows how loud each moment is within the song, so a chorus is brighter than a quiet verse and a whispered line stays dim. Without drums the glow swells and eases with the phrases instead of following every syllable. Pulses follow the drums: rap and EDM bounce hard, a folk or rock kit under guitars and voice gives a soft lift, and a voice, a guitar or a piano in a song without drums doesn't pulse. Rain, ocean and other noise sounds don't pulse either. Sensitivity (Settings > Motion) now also sets how big the pulses are.
- **Kicks hit on time, and hard.** In rap and EDM the pulse peaks about 25 ms after each kick and makes the light about a third brighter, with a dip between kicks. A drop hits on its first kick.
- **The edge fades in, with no line.** There is no solid line and no thin bright rim at the screen edge: the light starts soft and flat at the edge and fades smoothly inward, and by default it reaches further in. **Thickness** now sets how wide the soft, bright part along the edges is, and it always blends into the glow.
- **Settings says why it can't open.** If the Settings window, one of its pages or the visualizer can't open, a message says what went wrong and offers to open the logs folder (`%APPDATA%\Rimlight\logs`). Before, the welcome window could show up in its place, or nothing at all.

## [0.1.0] - YYYY-MM-DD

_The first release, not published yet. When it's tagged, move the entries from [Unreleased] here and put the release date in this heading._

[Unreleased]: https://github.com/sussyswimmer/bordervisualizer/commits/main
<!--
After tagging v0.1.0, replace the line above with these two:
[Unreleased]: https://github.com/sussyswimmer/bordervisualizer/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/sussyswimmer/bordervisualizer/releases/tag/v0.1.0
-->
