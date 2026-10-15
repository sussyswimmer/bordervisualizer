<p align="center">
  <img src="assets/banner.png" alt="Rimlight — your screen, lit by your music" width="100%">
</p>

<!-- The downloads badge counts the x64 installer only. GitHub's total for every file would mostly count update checks:
     each installed copy downloads the update feed (releases.<channel>.json) of the newest releases on every check. -->
<p align="center">
  <a href="https://github.com/sussyswimmer/bordervisualizer/releases/latest"><img src="https://img.shields.io/github/v/release/sussyswimmer/bordervisualizer?sort=semver&amp;display_name=tag&amp;label=release&amp;color=7C5CFF" alt="Latest release"></a>
  <a href="https://github.com/sussyswimmer/bordervisualizer/releases"><img src="https://img.shields.io/github/downloads/sussyswimmer/bordervisualizer/RimlightSetup.exe?label=downloads&amp;color=0891B2" alt="Downloads of RimlightSetup.exe, all releases"></a>
  <a href="https://github.com/sussyswimmer/bordervisualizer/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/sussyswimmer/bordervisualizer/ci.yml?branch=main&amp;label=CI&amp;logo=githubactions&amp;logoColor=white" alt="CI status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&amp;logoColor=white" alt=".NET 8">
</p>

<p align="center">
  <b>Ambient edge lighting for Windows that dances to whatever you're playing.</b><br>
  Colors from the album art. Motion from the beat. Free and open source.
</p>

<p align="center">
  <a href="https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/RimlightSetup.exe">
    <img src="https://img.shields.io/badge/Download%20for%20Windows-7C5CFF?style=for-the-badge&amp;logo=data%3Aimage%2Fsvg%2Bxml%3Bbase64%2CPHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI%2BPHBhdGggZmlsbD0iI2ZmZiIgZD0iTTEwLjc1IDNoMi41djkuMmwzLjEtMy4xIDEuNzcgMS43N0wxMiAxNyA1Ljg4IDEwLjg3IDcuNjUgOS4xbDMuMSAzLjFWM3pNNCAxOWgxNnYyLjI1SDR6Ii8%2BPC9zdmc%2B" alt="Download for Windows" height="44">
  </a>
</p>

<p align="center">
  <sub>
    Windows 10 (version 2004 or later) and Windows 11, x64 ·
    <a href="https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/RimlightSetup-arm64.exe">ARM64 installer</a> ·
    portable zip for <a href="https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/Rimlight-win-Portable.zip">x64</a>
    or <a href="https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/Rimlight-win-arm64-Portable.zip">ARM64</a> ·
    <a href="https://github.com/sussyswimmer/bordervisualizer/releases">all releases</a>
  </sub>
</p>

<!-- Demo GIF: un-comment this block and delete the placeholder below it once assets/demo.gif exists
     (docs/RELEASE-GUIDE.md, "Record the demo GIF").
<p align="center">
  <img src="assets/demo.gif" alt="The Rimlight glow around the edges of a desktop, pulsing with the beat and changing color with the album art" width="85%">
</p>
-->
<p align="center"><i>A demo of the glow in motion is coming soon.</i></p>

<!-- Demo video: paste the video link from docs/RELEASE-GUIDE.md here, on a line of its own with a blank line
     above and below, and GitHub shows it as a player under the GIF.
https://github.com/user-attachments/assets/REPLACE-WITH-THE-VIDEO-ID
-->

## Features

<table>
  <tr>
    <td width="50%" valign="top">
      <b>🥁 Beat-synced</b><br>
      Brightness follows the loudness, and every kick sends a pulse through the glow.
    </td>
    <td width="50%" valign="top">
      <b>🎨 Album-art colors</b><br>
      Two colors from the cover of whatever's playing, crossfading when the song changes. Or pick your own.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <b>🖱️ Click-through &amp; invisible to your workflow</b><br>
      Clicks, typing and dragging go straight through. It never takes focus and stays out of Alt+Tab and the taskbar.
    </td>
    <td width="50%" valign="top">
      <b>🖥️ Multi-monitor</b><br>
      Every screen, sharp at any display scaling, rebuilt on its own when you plug a monitor in or out.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <b>🎮 Pauses for games &amp; fullscreen</b><br>
      A fullscreen game or video pauses the glow on that screen only. Locking the PC pauses it everywhere.
    </td>
    <td width="50%" valign="top">
      <b>🪶 Tiny footprint</b><br>
      Drawn on the GPU, it stops drawing when nothing changes and saves power on battery.
    </td>
  </tr>
</table>

## Screenshots

Settings follows your Windows light or dark theme, and every change applies live, with a preview of the glow at the top.

<!-- Settings screenshots: un-comment this block and delete the placeholder below it once
     assets/settings-dark.png and assets/settings-light.png exist (docs/RELEASE-GUIDE.md, "Take the Settings screenshots").
<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/settings-dark.png">
    <source media="(prefers-color-scheme: light)" srcset="assets/settings-light.png">
    <img src="assets/settings-light.png" alt="The Rimlight Settings window on its Appearance page: a live preview of the glow, sliders for thickness, glow, brightness and corner radius, and a row of preset looks" width="760">
  </picture>
</p>
-->
<p align="center"><i>Screenshots of the Settings window are coming soon.</i></p>

## Install

1. **Download [RimlightSetup.exe](https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/RimlightSetup.exe)**, or [RimlightSetup-arm64.exe](https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/RimlightSetup-arm64.exe) on a Windows on ARM PC.
2. **Run it.** Rimlight installs for your user account, with no admin prompt, and starts in the tray. If you don't see its icon, click **^** next to the clock.
   > Windows may say *Windows protected your PC* because the app isn't code-signed yet. Click **More info → Run anyway**.
3. **Play some music**, then look at the edges of your screen.

**Ctrl+Alt+L** turns the glow on and off. Double-click the tray icon for Settings, or right-click it for modes, colors and presets. Rimlight starts with Windows and keeps itself up to date; you can turn off both in **Settings > Behavior**.

**Portable:** prefer no installer? Unzip [Rimlight-win-Portable.zip](https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/Rimlight-win-Portable.zip) (or [the ARM64 one](https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/Rimlight-win-arm64-Portable.zip)) into any folder and run `Rimlight.exe`.

**winget:** coming soon.

## How it works

Rimlight listens to what your PC plays through WASAPI loopback, the same sound your speakers get, and never the microphone.<br>
An FFT splits that sound into bands, and a beat detector watches the bass for kicks while auto-gain keeps the glow lively at any volume.<br>
Meanwhile it reads the album art from Windows' media controls and picks two colors from the cover that glow well.<br>
The light engine turns the beat, the loudness and those colors into a glow, which Direct3D draws into click-through windows along every screen's edges.

```mermaid
flowchart LR
    audio["System audio"] --> fft["FFT & beat detection"]
    fft --> engine["Light engine"]
    art["Album art palette"] --> engine
    engine --> overlay["GPU overlay"]
```

## Privacy

Anything that listens should say exactly what it does with what it hears:

- Only system output audio is captured, via WASAPI loopback. The microphone is never opened.
- Audio is analyzed in memory and immediately discarded. Nothing is recorded or saved.
- Track info and artwork are read locally from Windows' media controls and never leave the device.
- No telemetry or analytics. The only network call is the GitHub update check.

You can turn off the update check in **Settings > Behavior > Update automatically**. The same promise is in **Settings > About**, and all the code is here to check.

## FAQ

<details>
<summary><b>Does it use my microphone?</b></summary>

No. Rimlight captures only system output audio, the sound your speakers or headphones play, through WASAPI loopback. It never opens the microphone or any other input. The sound is analyzed in memory and thrown away right away, and nothing is recorded or saved. See [Privacy](#privacy).

</details>

<details>
<summary><b>Will it slow down games?</b></summary>

It's built not to. The glow is drawn on the GPU and only redraws when it changes: 60 times a second while music drives it (the default **Frame rate limit**, never above your display's rate), about 10 times a second while it breathes in Idle Glow, and not at all while it holds still. When a game fills a screen, the glow on that screen pauses and its overlay window is hidden, so the game has that display to itself. If you play with the glow on, you can lower **Frame rate limit** to 30 in **Settings > Displays**.

</details>

<details>
<summary><b>Battery impact?</b></summary>

On battery, Rimlight saves power by default: **On battery** in **Settings > Behavior** is set to **Save power (30 fps)**, which caps the glow at 30 frames a second and draws it at half resolution. Choose **Pause the glow** to stop it on battery, or **Keep going** for the full frame rate. It also stops drawing while the PC is locked or asleep and while the display is off. The **Off** mode stops everything, audio capture included.

</details>

<details>
<summary><b>Why doesn't it show over my fullscreen game?</b></summary>

That's on purpose. With **Pause in fullscreen apps** on (in **Settings > Behavior**, and on by default), a game, video or presentation that fills a screen pauses the glow on that screen only. Your other monitors keep glowing, and the glow comes back when the app leaves fullscreen. A maximized window doesn't count; a video in fullscreen does.

Turn the setting off if you want the glow over fullscreen apps too. Some games use exclusive fullscreen, which keeps every other window off the screen; switch the game to borderless or windowed fullscreen to see the glow there.

</details>

<details>
<summary><b>Which music apps work?</b></summary>

For the motion, all of them: Rimlight reacts to whatever your PC plays, from any player, browser, game or video.

The album-art colors need the app to share what's playing with Windows' media controls (the song and cover that show up with the volume controls on Windows 10, or in Quick Settings on Windows 11). Most music players and browsers do. When an app doesn't, or a song has no cover, the glow uses your own colors from **Settings > Color**, where you can also choose **Manual** to always use them.

</details>

<details>
<summary><b>How do I uninstall?</b></summary>

Open Windows **Settings > Apps > Installed apps** (**Apps & features** on Windows 10), find **Rimlight**, and choose **Uninstall**. That removes the app and its shortcuts.

Your settings and logs stay in `%APPDATA%\Rimlight`, so a reinstall picks up where you left off. For a complete cleanup, delete that folder too: paste `%APPDATA%\Rimlight` into File Explorer's address bar to find it.

For a portable copy, turn off **Start with Windows** in **Settings > Behavior**, quit Rimlight from the tray, and delete its folder.

</details>

<details>
<summary><b>How do I report a bug?</b></summary>

Right-click the tray icon and choose **Open logs folder**, zip the files in it, and attach the zip to a [bug report](https://github.com/sussyswimmer/bordervisualizer/issues/new?template=bug_report.yml). If Rimlight won't start, paste `%APPDATA%\Rimlight\logs` into File Explorer's address bar instead. [Reporting a bug with logs](CONTRIBUTING.md#reporting-a-bug-with-logs) walks through every step.

Logs never contain audio, but have a quick look before you upload them. For a security problem, please follow [SECURITY.md](SECURITY.md) instead of opening an issue.

</details>

## Build from source

You need Windows 10 (version 2004 or later) or Windows 11, the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and Git. In PowerShell:

```powershell
git clone https://github.com/sussyswimmer/bordervisualizer.git
cd bordervisualizer
dotnet run --project src/Rimlight.App
```

Quit any installed copy first, since only one Rimlight runs at a time. `dotnet build -c Release` and `dotnet test` are what CI runs, and `-- --demo` (a synthetic beat, no music needed) and `-- --debug-visualizer` (the live audio analysis) are handy while you work. [CONTRIBUTING.md](CONTRIBUTING.md#build-from-source) has the details, including how to build and test the engine on Linux or macOS.

## Roadmap

- [ ] **RGB devices:** keyboards, mice and light strips that glow in sync, through Windows Dynamic Lighting.
- [ ] **Smart room lights** that follow the music.
- [ ] **Screen colors:** a mode that takes its colors from the edges of what's on screen instead of the album art.
- [ ] **Per-app rules,** such as pausing the glow during a video call.
- [ ] **winget and Microsoft Store** listings.

Have another idea? [Suggest a feature](https://github.com/sussyswimmer/bordervisualizer/issues/new?template=feature_request.yml).

## Contributing

Bug reports with logs, ideas and pull requests are all welcome. Rimlight is split into a portable .NET engine (beat detection, color, the light engine) and the Windows app around it (overlay, audio capture, tray and Settings), which meet at a small set of interfaces. [CONTRIBUTING.md](CONTRIBUTING.md) explains the layout, the build and tests, how commits and pull requests work, and how to send logs. Everyone who takes part agrees to the [Code of Conduct](CODE_OF_CONDUCT.md).

## Credits

Rimlight is built on these open-source projects, all MIT licensed:

- [NAudio](https://github.com/naudio/NAudio): WASAPI loopback capture
- [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows): Direct3D 11, DXGI and DirectComposition
- [WPF-UI](https://github.com/lepoco/wpfui): the Fluent look of the Settings window
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon): the tray icon
- [Velopack](https://github.com/velopack/velopack): the installer and updates
- [CsWin32](https://github.com/microsoft/CsWin32): the Win32 interop

The banner's lettering is [Inter](https://github.com/rsms/inter), under the SIL Open Font License 1.1.

## License

Rimlight is released under the [MIT License](LICENSE). © 2026 [Maxwell Olander](https://maxwellolander.com).
