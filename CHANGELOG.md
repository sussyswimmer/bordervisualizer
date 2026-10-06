# Changelog

All notable changes to Rimlight are listed here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Edge glow.** A soft two-color glow along the edges of every monitor. It's click-through, stays on top, never takes focus, and doesn't show up in Alt+Tab or on the taskbar. It stays sharp on mixed-DPI setups, rebuilds itself when you plug in, remove or rearrange a monitor, and comes back on its own after a graphics driver reset. ([#8](https://github.com/sussyswimmer/bordervisualizer/pull/8))
- **Listens to what your PC plays.** Rimlight hears system audio through WASAPI loopback and never opens the microphone. When you switch between speakers and headphones, it follows the new output device. ([#9](https://github.com/sussyswimmer/bordervisualizer/pull/9))
- **Beat detection.** Rimlight tracks loudness and bass and detects kicks, with a tempo estimate. It adapts to your volume, so the glow moves the same whether the music is loud or quiet, and it notices when the music stops. ([#2](https://github.com/sussyswimmer/bordervisualizer/pull/2), [#5](https://github.com/sussyswimmer/bordervisualizer/pull/5), [#7](https://github.com/sussyswimmer/bordervisualizer/pull/7))
- **Music Sync and Idle Glow.** In Music Sync, brightness follows the loudness, every beat sends a pulse through the glow, and the colors drift slowly around the screen. Idle Glow breathes gently on a 6-second cycle. When the music stops, the glow fades to Idle Glow (or hides, if you choose) after 2 seconds, and it's back within 150 ms when the music returns. ([#11](https://github.com/sussyswimmer/bordervisualizer/pull/11))
- **Colors from album art.** Rimlight picks two vivid colors from a cover image and tunes them so they glow well. It recognizes covers with no real artwork. Hooking this up to the track that's playing is still to come. ([#12](https://github.com/sussyswimmer/bordervisualizer/pull/12))
- **Easy on your PC.** The glow draws nothing while the picture doesn't change and only 10 frames a second while it breathes in Idle Glow. On battery, it's capped at 30 frames a second at half resolution by default. ([#14](https://github.com/sussyswimmer/bordervisualizer/pull/14))
- **Tray icon.** Rimlight lives in the notification area. Right-click its icon, a glowing rounded square, to turn the glow on or off, change the mode, or quit. ([#1](https://github.com/sussyswimmer/bordervisualizer/pull/1), [#14](https://github.com/sussyswimmer/bordervisualizer/pull/14), [#15](https://github.com/sussyswimmer/bordervisualizer/pull/15))
- **For contributors:**
  - `wav-analyze` runs the beat detector on any WAV file and writes the beats, the tempo, a CSV and a plot. It can also generate synthetic test tracks, and there's an analyzer benchmark. All of it runs on Linux too. ([#13](https://github.com/sussyswimmer/bordervisualizer/pull/13))
  - Continuous integration builds and tests every pull request on Linux and Windows, labels it from its title, and sorts it into release-note sections. ([#10](https://github.com/sussyswimmer/bordervisualizer/pull/10))
  - A contributing guide, security policy, code of conduct, issue forms and a pull request template.

## [0.1.0] - YYYY-MM-DD

_The first release, not published yet. When it's tagged, move the entries from [Unreleased] here and put the release date in this heading._

[Unreleased]: https://github.com/sussyswimmer/bordervisualizer/commits/main
<!--
After tagging v0.1.0, replace the line above with these two:
[Unreleased]: https://github.com/sussyswimmer/bordervisualizer/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/sussyswimmer/bordervisualizer/releases/tag/v0.1.0
-->
