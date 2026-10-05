# 08 — GitHub Presentation

The goal: someone lands on the repo from a Reddit or X post and, within 5 seconds, sees what it does, sees that it looks good, and finds a big download button. Treat the README like a product landing page, not like developer docs.

**Ownership (see doc 09):** Claude Code owns the README, banner, icon art, screenshots/GIF slots, and repo description/topics. Codex owns the community health files, issue/PR templates, release-notes config, and the badges' underlying workflows. Claude Code does the final pass and makes sure everything fits together.

## 1. Visual assets (`assets/`)
| File | Spec |
|---|---|
| `assets/banner.svg` + `banner.png` | 1280×640. Near-black background (#0A0A0F). A rounded-rectangle "screen" outline in the center glows violet→cyan along its edges (the product, shown as a picture). The **Rimlight** wordmark is large and centered in a clean geometric sans (inline the font as paths so it renders everywhere). The tagline below it: *"Your screen, lit by your music."* Render the PNG from the SVG in `build/` and commit both. **The PNG doubles as the GitHub social preview image.** |
| `assets/icon.svg` / `icon.png` (512) | The same glowing rounded-square motif, readable at 16 px. |
| `assets/demo.gif` | **Recorded by Maxwell** (you can't see the screen). About 8 s loop, ≤ 8 MB, 960 px wide. Run `--demo` on a nice desktop. Put a placeholder note in the README until it exists, and give Maxwell recording steps (below). |
| `assets/settings-dark.png`, `settings-light.png` | Settings window screenshots, **taken by Maxwell**. Use a `<picture>` element so GitHub shows the one matching the viewer's theme. |

Recording instructions for Maxwell (put these in your final summary):
1. Pick a clean wallpaper and hide the desktop icons.
2. `Rimlight.exe --demo`
3. Record with ScreenToGif (free) at 30 fps, then trim to a seamless loop.
4. Export at 960 px wide, under 8 MB, and save as `assets/demo.gif`. Also upload an MP4 version to a GitHub issue comment to get a hosted video URL, and embed that as well (it's sharper).

## 2. README structure (in this order)

```markdown
<p align="center">
  <img src="assets/banner.png" alt="Rimlight — your screen, lit by your music" width="100%">
</p>

<p align="center">
  [badges: latest release · total downloads · CI · license MIT · Windows 10 | 11 · .NET 8]
</p>

<p align="center">
  <b>Ambient edge lighting for Windows that dances to whatever you're playing.</b><br>
  Colors from the album art. Motion from the beat. Free and open source.
</p>

<p align="center">
  <a href="https://github.com/<owner>/<repo>/releases/latest/download/RimlightSetup.exe">
    <img src="https://img.shields.io/badge/Download%20for%20Windows-7C5CFF?style=for-the-badge&logo=windows&logoColor=white" height="44">
  </a>
</p>

<p align="center"><img src="assets/demo.gif" width="85%"></p>
```

Then, in order:
1. **Features**: a 2-column HTML table, 6 cells. Each cell has a bold title and one line (Beat-synced, Album-art colors, Click-through & invisible to your workflow, Multi-monitor, Pauses for games & fullscreen, Tiny footprint). One emoji per title is OK. Don't use more than that.
2. **Screenshots**: the dark/light `<picture>` of Settings.
3. **Install**: 3 numbered steps. Include the SmartScreen note ("Windows may say *Windows protected your PC* because the app isn't code-signed yet. Click **More info → Run anyway**."). Add a portable zip link and a `winget` line marked "coming soon" only if it isn't done.
4. **How it works**: a 4-line plain-English explanation and a Mermaid flowchart: System audio → FFT & beat detection → Light engine ← Album art palette → GPU overlay.
5. **Privacy**: the exact bullets from doc 01. People care about this a lot for anything that "listens."
6. **FAQ** (`<details>` collapsibles): Does it use my microphone? · Will it slow down games? · Battery impact? · Why doesn't it show over my fullscreen game? · Which music apps work? · How do I uninstall? · How do I report a bug? (point to "Open logs folder")
7. **Build from source**: prerequisites and 3 commands.
8. **Roadmap**: checkboxes with the stretch goals from doc 07.
9. **Contributing**: one paragraph plus a link to CONTRIBUTING.md.
10. **Credits**: NAudio, Vortice.Windows, WPF-UI, H.NotifyIcon, Velopack, CsWin32 (linked).
11. **License**: MIT, © Maxwell Olander. Add a link to maxwellolander.com.

README rules:
- Every image has alt text.
- No fake numbers, fake stars, fake testimonials, or "trusted by" sections.
- Don't mention or compare against any other app.
- Replace `<owner>/<repo>` with the real values (ask Maxwell if the remote isn't set).
- Check that every link resolves and every badge renders once the repo is public.

## 3. Repo files (Codex)
- `CONTRIBUTING.md`: dev setup, the lane structure from doc 09, Conventional Commits, how to run tests.
- `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1), `SECURITY.md` (email for reports: maxwell.olander@gmail.com).
- `.github/ISSUE_TEMPLATE/bug_report.yml`: Windows version, Rimlight version, GPU, monitor setup, music app, attached log zip, steps to reproduce. `feature_request.yml`. `config.yml` that links Discussions for questions.
- `.github/pull_request_template.md`.
- `.github/release.yml`: release notes categories (✨ Features, 🐛 Fixes, ⚡ Performance, 📚 Docs, 🧹 Chores) driven by PR labels. Also add a labeler workflow mapping Conventional Commit prefixes to labels.
- `CHANGELOG.md`, generated or maintained from Conventional Commits.

## 4. Repo settings (Maxwell clicks these; write them out for him)
- **Description:** "Ambient edge lighting for Windows that syncs to your music — album-art colors, beat-reactive glow. Free & open source."
- **Topics:** `windows`, `ambient-lighting`, `music-visualizer`, `wpf`, `direct3d`, `audio-visualizer`, `desktop-app`, `dotnet`, `rgb`, `open-source`
- **Social preview:** upload `assets/banner.png`.
- **Website:** the latest release URL, or a landing page if one is made later.
- Enable Discussions and Releases. Pin the repo on his profile.

## 5. First release checklist
- [ ] Tag `v1.0.0`. The release workflow uploads `RimlightSetup.exe`, the portable zip, and Velopack feed files for x64 and ARM64.
- [ ] Release notes start with the demo GIF, then a short "What it is", then the auto-generated changes.
- [ ] Download the installer from the release page on a different PC or a fresh user account, install it, and confirm it works.
- [ ] README download button hits the real asset (no 404).
