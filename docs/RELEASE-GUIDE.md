# Release guide (for Maxwell)

The clicks and recordings that only you can do, in order: record the demo and screenshots, set up the repo page, then ship v1.0.0. Doc 08 is the spec behind each step. Everything else (README, banner, release workflow) is already in the repo.

## 1. Record the demo GIF

The GIF sits under the README's download button and at the top of the release notes. Aim for an 8-second loop, 960 px wide, under 8 MB.

1. **Set the stage.** Pick a clean wallpaper with some dark areas, so the glow stands out. Hide the desktop icons (right-click the desktop > **View** > uncheck **Show desktop icons**), and close or minimize every window.
2. **Start the demo.** Quit Rimlight from the tray first: only one copy runs at a time, and a second launch only opens the running copy's Settings. Then, in PowerShell:
   ```powershell
   & "$env:LOCALAPPDATA\Rimlight\current\Rimlight.exe" --demo
   ```
   From a clone of the repo, `dotnet run --project src/Rimlight.App -c Release -- --demo` does the same. Demo mode drives the glow with a synthetic beat and cycles through palettes, so you don't need music.
   - Keep **Hide from screen capture** off (**Settings > Behavior**; it's off by default), or the recording won't show the glow.
   - The default look records well. A slightly higher **Glow** or **Brightness** (**Settings > Appearance**) reads better once the GIF is shrunk to 960 px.
3. **Record with [ScreenToGif](https://www.screentogif.com)** (free). Use the screen recorder on the whole screen, since the glow lives at the edges, and set it to **30 fps**. Record 10 to 15 seconds, then stop to open the editor.
4. **Trim it to a seamless loop.** Delete the frames before and after an 8-second stretch. Start and end on the same moment of the beat (the demo beats twice a second, so 8 s is 16 beats). If the colors at the two ends differ, add a short fade back to the first frame (**Transitions** tab) so the loop doesn't jump.
5. **Export.**
   - **GIF:** resize to **960 px wide** (**Image** > **Resize**, keep the aspect ratio), then **File > Save as** a GIF. The Gifski encoder looks best on gradients if ScreenToGif offers it. It must be **under 8 MB**. If it's bigger, drop to 24 fps (**Edit** > **Reduce frame count**), shorten it, or lower the quality.
   - **MP4:** also save the full-size recording as an MP4 video (ScreenToGif downloads FFmpeg for this). Keep it under 10 MB, GitHub's video limit on a free plan.
6. **Get a hosted video link (it's sharper than the GIF).** On GitHub, open a new issue titled "Demo video", drag the MP4 into the comment box, and wait for the upload. GitHub inserts a link like `https://github.com/user-attachments/assets/…`. Submit the issue so the upload is kept (you can close it), and copy the link.
7. **Save the GIF as `assets/demo.gif`.**

## 2. Take the Settings screenshots

The README shows the dark one to visitors using GitHub's dark theme and the light one to everyone else.

1. Start Rimlight normally and play some music, or use `--demo` as above, so the preview glows.
2. Double-click the tray icon to open **Settings**. Stay on **Appearance**, where the live preview is at the top, and don't resize the window: both shots should be the same size.
3. Switch Windows to dark mode: **Settings > Personalization > Colors > Choose your mode > Dark** (on Windows 10: **Choose your default app mode > Dark**). Rimlight follows it; if its window doesn't change, close Settings and open it again.
4. Press **Win+Shift+S**, choose **Window** mode, and click the Rimlight Settings window. Open the snip from the notification and save it as **`assets/settings-dark.png`**.
5. Switch Windows to **Light** and repeat. Save it as **`assets/settings-light.png`**.
6. Use the same display scale for both. 125 % or 150 % looks crisp on high-DPI screens. Each file should be well under 1 MB.

## 3. Put them in the README

With `assets/demo.gif`, `assets/settings-dark.png` and `assets/settings-light.png` in place:

1. **`README.md`, demo:** un-comment the `<!-- Demo GIF … -->` block under the download button and delete the line "A demo of the glow in motion is coming soon." just below it.
2. **`README.md`, video:** in the `<!-- Demo video … -->` block, replace the placeholder link with the one from step 1.6, then un-comment it. It has to stay on a line of its own with a blank line above and below, or GitHub shows a plain link instead of a player.
3. **`README.md`, screenshots:** un-comment the `<!-- Settings screenshots … -->` block and delete the line "Screenshots of the Settings window are coming soon."
4. **`.github/release-notes-intro.md`:** un-comment the GIF at the top. It points at `assets/demo.gif` on `main` by absolute URL, because release notes don't resolve repo paths, so merge the GIF to `main` before you tag.
5. Commit, for example `docs(readme): add the demo GIF and Settings screenshots`, open a pull request, and check its rendered README before merging. Or hand the three files to Claude Code and ask it to do this step.

## 4. Set up the repo page

All on github.com/sussyswimmer/bordervisualizer, signed in as the owner.

**About** (the gear icon next to "About" on the repo's front page):

- **Description:**
  ```text
  Ambient edge lighting for Windows that syncs to your music — album-art colors, beat-reactive glow. Free & open source.
  ```
- **Website:**
  ```text
  https://github.com/sussyswimmer/bordervisualizer/releases/latest
  ```
- **Topics** (type each one and press Enter):
  ```text
  windows ambient-lighting music-visualizer wpf direct3d audio-visualizer desktop-app dotnet rgb open-source
  ```
- **Include in the home page:** check **Releases**. Uncheck **Packages** and **Deployments**; Rimlight publishes neither.

**Settings > General:**

- **Social preview:** **Edit > Upload an image…** and choose `assets/banner.png`. It is exactly the 1280×640 that GitHub asks for, and 172 KB, well under the 1 MB limit. Links to the repo on social sites and in chat apps then show the banner.
- **Features:** check **Discussions**, then click **Set up discussions** and post the suggested welcome. Afterwards un-comment the "Ask a question" link in `.github/ISSUE_TEMPLATE/config.yml`, so the new-issue page points questions there. You can also uncheck **Wikis** and **Projects** if you won't use them.
- **Pull Requests:**
  - Check **Allow squash merging** and set its default message to **Pull request title**. The Conventional Commit title of each pull request then becomes the commit on `main`.
  - Uncheck **Allow merge commits** and **Allow rebase merging**.
  - Check **Automatically delete head branches**.

**Settings > Advanced Security** (named **Code security** on some accounts):

- **Private vulnerability reporting:** **Enable**. `SECURITY.md` sends people to the "Report a vulnerability" button, which only exists with this on.

**Settings > Actions:** nothing to change. The release workflow asks for the write access it needs.

**Your profile:** on github.com/sussyswimmer, click **Customize your pins**, check **bordervisualizer**, and save.

## 5. Ship v1.0.0

**Before you tag:**

- [ ] The Velopack bootstrap (K9) is merged, and the release workflow's dry run on its pull request passed. Without it, the pack step stops and nothing is published.
- [ ] CI is green on `main`, and the README badge shows it.
- [ ] Sections 1 to 3 are done and merged: the GIF, the video link, the screenshots and the release-notes GIF.
- [ ] `CHANGELOG.md`: rename `## [0.1.0] - YYYY-MM-DD` to `## [1.0.0] - <today's date>`, move the `[Unreleased]` entries into it, and update the links at the bottom as its comment explains, with `1.0.0` instead of `0.1.0`.
- [ ] Optional: set `<Version>1.0.0</Version>` in `src/Directory.Build.props`. The tag sets the release's version either way; the file only sets local builds.

**Tag it.** On an up-to-date `main`:

```powershell
git switch main
git pull
git tag v1.0.0
git push origin v1.0.0
```

Push only the tag, and don't create the release on GitHub yourself: the workflow creates it, and fails if it already exists.

**What `release.yml` does** (Actions tab > **Release**): it builds and tests on Windows, packs x64 and ARM64 with Velopack (and signs them, once the signing secrets exist), and creates a draft release named "Rimlight 1.0.0". The draft holds `RimlightSetup.exe` and `RimlightSetup-arm64.exe`, then Velopack's upload adds `Rimlight-win-Portable.zip`, `Rimlight-win-arm64-Portable.zip`, the update packages and their `releases.*.json` feeds. Only then is the release published, so the README's download button never meets a half-uploaded release. The notes are `.github/release-notes-intro.md` followed by the pull requests merged since the last release.

**After it finishes:**

- [ ] The Release run is green, and **Releases** shows **v1.0.0** marked **Latest**, with the two installers and the two portable zips.
- [ ] The release notes start with the demo GIF and play it, then the short "what it is" and install text, then the generated list of changes. You can fix the notes afterwards with **Edit** on the release.
- [ ] The README's download links all work. In PowerShell:
  ```powershell
  foreach ($name in 'RimlightSetup.exe', 'RimlightSetup-arm64.exe', 'Rimlight-win-Portable.zip', 'Rimlight-win-arm64-Portable.zip') {
      curl.exe -sL -r 0-0 -o NUL -w "%{http_code}  $name`n" "https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/$name"
  }
  ```
  Each line should start with 206 (or 200). A 404 means that file is missing from the release.
- [ ] The README badges show the release (v1.0.0), a download count and CI passing. Before the first release they read "no releases found", which is expected.
- [ ] **Test on a clean account or a second PC.** Add a local user (**Settings > Accounts > Other users > Add account > I don't have this person's sign-in information > Add a user without a Microsoft account**) and sign in as them. Then:
  1. Open the README in a browser and click **Download for Windows**.
  2. Run the installer. SmartScreen's "Windows protected your PC" should offer **More info → Run anyway**, and there should be no admin prompt.
  3. The tray icon and the welcome window appear. Play music, and the edges glow.
  4. Rimlight is in the Start menu and on the desktop.
  5. Uninstall it from **Settings > Apps**. The app and its shortcuts go away, and `%APPDATA%\Rimlight` (settings and logs) stays, as the README's FAQ says.
- [ ] When you ship v1.0.1 later, check that a v1.0.0 install finds it (**Settings > About > Check for updates**) and is on the new version after a restart.
