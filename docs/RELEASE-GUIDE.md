# Release guide (for Maxwell)

The clicks and recordings that only you can do, in order: record the demo and screenshots, set up the repo page, then ship v1.0.0 (and, later, turn on code signing). Doc 08 is the spec behind each step. Everything else (README, banner, release workflow) is already in the repo.

## 1. Record the demo GIF

The GIF sits under the README's download button and at the top of the release notes. Aim for an 8-second loop, 960 px wide, under 8 MB.

1. **Set the stage.** Pick a clean wallpaper with some dark areas, so the glow stands out. Hide the desktop icons (right-click the desktop > **View** > uncheck **Show desktop icons**), and close or minimize every window.
2. **Start the demo.** In PowerShell:
   ```powershell
   & "$env:LOCALAPPDATA\Rimlight\current\Rimlight.exe" --demo
   ```
   If Rimlight is already running, this starts the demo in that copy, since only one copy runs at a time. From a clone of the repo, `dotnet run --project src/Rimlight.App -c Release -- --demo` does the same with your build; quit any installed copy from the tray first, or the demo starts in that copy instead. Demo mode drives the glow with a synthetic 120 BPM beat and its own palettes, so you don't need music. To end it, uncheck **Demo mode** in the tray menu.
   - Keep **Hide from screen capture** off (**Settings > Behavior**; it's off by default), or the recording won't show the glow.
   - The default look records well. A slightly higher **Glow** or **Brightness** (**Settings > Appearance**) reads better once the GIF is shrunk to 960 px.
3. **Record with [ScreenToGif](https://www.screentogif.com)** (free). Use the screen recorder on the whole screen, since the glow lives at the edges, and set it to **30 fps**. Record at least 15 seconds, so step 4 has a palette change with 4 seconds on each side, then stop to open the editor.
4. **Trim it to an 8-second loop.** The demo beats twice a second and changes palette every 6 seconds, always on the first beat of a bar, and its four palettes repeat every 24 seconds. Any 8-second stretch therefore shows a palette change and ends on different colors from the ones it starts with.
   - Find a frame where the colors start to change, and keep 4 seconds on each side of it (delete the frames before and after). The change then sits in the middle, and the clip is 16 beats long, so its two ends fall on the same moment of the beat.
   - Always add a short fade from the last frame back to the first (**Transitions** tab), so the loop changes color smoothly instead of jumping.
   - Only the full 24-second cycle loops without a fade, and that is three times the file size.
5. **Export.**
   - **GIF:** resize to **960 px wide** (**Image** > **Resize**, keep the aspect ratio), then **File > Save as** a GIF. The Gifski encoder looks best on gradients if ScreenToGif offers it. It must be **under 8 MB**. If it's bigger, drop to 24 fps (**Edit** > **Reduce frame count**), shorten it, or lower the quality.
   - **MP4:** also save the full-size recording as an MP4 video (ScreenToGif downloads FFmpeg for this). Keep it under 10 MB, GitHub's video limit on a free plan.
6. **Get a hosted video link (it's sharper than the GIF).** On GitHub, open a new issue titled "Demo video", drag the MP4 into the comment box, and wait for the upload. GitHub inserts a link like `https://github.com/user-attachments/assets/…`. Submit the issue so the upload is kept (you can close it), and copy the link.
7. **Save the GIF as `assets/demo.gif`.**

## 2. Take the Settings screenshots

The README shows the dark one to visitors using GitHub's dark theme and the light one to everyone else.

1. Start Rimlight normally and play some music, so the live preview at the top of the **Appearance** page moves. Demo mode won't help here: it drives only the glow at the screen edges, and the preview always follows the music, in the album art's colors or your own.
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
- [ ] For Mac updates: the update key is set up (section 7, step 1). Without it the release still has the Mac download, but those copies never update.
- [ ] Optional: set `<Version>1.0.0</Version>` in `src/Directory.Build.props`. The tag sets the release's version either way; the file only sets local builds.

**Tag it.** On an up-to-date `main`:

```powershell
git switch main
git pull
git tag v1.0.0
git push origin v1.0.0
```

Push only the tag, and don't create the release on GitHub yourself: the workflow creates it, and fails if it already exists.

**What `release.yml` does** (Actions tab > **Release**): it builds and tests on Windows, packs x64 and ARM64 with Velopack (and signs them, once the signing secrets exist: section 6), and creates a draft release named "Rimlight 1.0.0" that holds `RimlightSetup.exe` and `RimlightSetup-arm64.exe`. Velopack's upload then adds everything it packed, for each architecture:

- its own copy of the installer, `Rimlight-win-Setup.exe` and `Rimlight-win-arm64-Setup.exe`: the same files as the two above, under Velopack's names;
- the portable zips, `Rimlight-win-Portable.zip` and `Rimlight-win-arm64-Portable.zip`;
- the update packages, `Rimlight-1.0.0-full.nupkg` and `Rimlight-1.0.0-win-arm64-full.nupkg`, and their feeds, `releases.win.json` and `releases.win-arm64.json`;
- a `RELEASES` file, which Velopack adds for compatibility with older updaters.

The README and the release notes link only the `RimlightSetup*.exe` names, and the downloads badge counts `RimlightSetup.exe`; Velopack's copies are harmless. Only when every file is uploaded is the release published, so the README's download button never meets a half-uploaded release. The notes are `.github/release-notes-intro.md` followed by the pull requests merged since the last release.

**After it finishes:**

- [ ] The Release run is green, and **Releases** shows **v1.0.0** marked **Latest**, with the 11 files listed above (four installers, two zips, two packages, two feeds and `RELEASES`), the Mac files (`RimlightSetup.dmg`, `Rimlight-mac.zip` and, with the update key, `appcast.xml`: section 7), and GitHub's two **Source code** archives.
- [ ] The release notes start with the demo GIF and play it, then the short "what it is" and install text, then the generated list of changes. You can fix the notes afterwards with **Edit** on the release.
- [ ] The README's download links all work. In PowerShell:
  ```powershell
  foreach ($name in 'RimlightSetup.exe', 'RimlightSetup-arm64.exe', 'Rimlight-win-Portable.zip', 'Rimlight-win-arm64-Portable.zip') {
      curl.exe -sL -r 0-0 -o NUL -w "%{http_code}  $name`n" "https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/$name"
  }
  ```
  Each line should start with 206 (or 200). A 404 means that file is missing from the release.
- [ ] The README badges show the release (v1.0.0), a download count and CI passing. Before the first release they read "no releases found", which is expected. The downloads badge counts `RimlightSetup.exe` across all releases, on purpose: GitHub's total for every file would mostly count update checks, because each installed copy downloads the `releases.*.json` feeds of the newest releases every time it checks. So the badge leaves out the ARM64 installer, the zips and Velopack's installer copies, and errs low.
- [ ] **Test on a clean account or a second PC.** Add a local user (**Settings > Accounts > Other users > Add account > I don't have this person's sign-in information > Add a user without a Microsoft account**) and sign in as them. Then:
  1. Open the README in a browser and click **Download for Windows**.
  2. Run the installer. SmartScreen's "Windows protected your PC" should offer **More info → Run anyway**, and there should be no admin prompt.
  3. The tray icon and the welcome window appear. Play music, and the edges glow.
  4. Rimlight is in the Start menu and on the desktop.
  5. Uninstall it from **Settings > Apps**. The app and its shortcuts go away, and `%APPDATA%\Rimlight` (settings and logs) stays, as the README's FAQ says.
- [ ] When you ship v1.0.1 later, check that a v1.0.0 install finds it (**Settings > About > Check for updates**) and is on the new version after a restart.

## 6. Later: turn on code signing

Releases are unsigned until you add the six `SIGNING_*` secrets for Azure Trusted Signing in **Settings > Secrets and variables > Actions**; the pack job's comment in `.github/workflows/release.yml` says what each one holds. From then on, every tagged release is signed. Once a signed release is out and a fresh download on a clean account no longer shows *Windows protected your PC*, take the warning out of the docs:

- [ ] `README.md`, **Install** step 2: delete the quoted line about *Windows protected your PC*.
- [ ] `.github/release-notes-intro.md`, the **Install** line: delete its last two sentences, from "Windows may say" to "**Run anyway**."
- [ ] This guide: drop the SmartScreen part of step 2 of the clean-account test in section 5.

## 7. The Mac app

The release workflow builds the Mac app next to the Windows one and attaches it to the same release (docs/10-MACOS.md §6). Two things only you can do: set up its update key once, and try it on a Mac.

1. **Set up the update key (once, before the first release with the Mac app).** Without it, every Mac copy is built with no updater.
   1. On your Mac, download `Sparkle-2.10.0.tar.xz` from [Sparkle's 2.10.0 release](https://github.com/sparkle-project/Sparkle/releases/tag/2.10.0) and double-click it to unpack it.
   2. In Terminal, run `cd ~/Downloads/Sparkle-2.10.0`, then `./bin/generate_keys`. It keeps the key in your login keychain and prints two lines. The public key is the text between `<string>` and `</string>`: 44 characters, ending in `=`. Copy only that.
   3. Run `./bin/generate_keys -x private-key.txt`.
   4. On GitHub, open **Settings > Secrets and variables > Actions**:
      - **Variables > New repository variable:** name `SPARKLE_PUBLIC_KEY`, with the public key as the value.
      - **Secrets > New repository secret:** name `SPARKLE_PRIVATE_KEY`, with the contents of `private-key.txt` as the value (in Terminal, `pbcopy < private-key.txt` copies them; then paste).
   5. Delete `private-key.txt`. The keychain copy is your backup: if the key is lost, installed Macs can't update again.

   The release checks that the two halves match before it ships the Mac files.
2. **After the release:** check the Mac links in Terminal:
   ```sh
   for name in RimlightSetup.dmg Rimlight-mac.zip appcast.xml; do
     curl -sL -r 0-0 -o /dev/null -w "%{http_code}  $name\n" "https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/$name"
   done
   ```
   Each line should start with 206 (or 200). `appcast.xml` is 404 if you skipped step 1. If `RimlightSetup.dmg` is 404, the Mac build failed: open the Release run in the Actions tab and look at the **Build and pack (macOS)** job.
3. **Try it on a Mac** (macOS 14.4 or later), ideally a user account that has never run it:
   1. Click **Download for Mac** in the README, open the DMG, and drag Rimlight to Applications.
   2. Open it. On macOS 15 or later, macOS refuses at first: click **Done**, then **Open Anyway** in **System Settings > Privacy & Security**, and confirm with **Open Anyway** and your password. The README's Install section says the same.
   3. The menu bar icon appears and Settings opens with a welcome message. Click **Start**, allow system audio when macOS asks, play music in the Music app, and the edges glow in the cover's colors (Music asks once whether Rimlight may read it).
   4. Bugs: no glow, a prompt for the **microphone** (it must never ask for one), the glow catching clicks, or a second icon after opening the app again.
   5. When the next version ships, check **Settings > About > Check for Updates…** finds it, and that the new version is running after you quit and reopen Rimlight.
4. **Optional, a Mac demo recording.** The demo runs on a Mac too: quit Rimlight, then run `/Applications/Rimlight.app/Contents/MacOS/Rimlight --demo` in Terminal. Press **⌘⇧5**, choose **Record Entire Screen**, and record 15 seconds; macOS saves a .mov on your Desktop. Keep **Hide the glow from screen recordings** off (**Settings > Behavior**). Copy the file to your PC, and trim and export it as in section 1 (ScreenToGif: **File > Load**). The README has no Mac slot yet, so hand the file to Claude Code if you want one.
