# Security policy

Thanks for helping keep Rimlight and the people who run it safe.

## Supported versions

Security fixes go into the next release, and only the latest release is supported. Rimlight checks for updates on its own (unless you turned that off in Settings > Behavior), so a fix reaches you on the next update.

| Version | Supported |
|---|---|
| Latest release | Yes |
| Older releases | No. Please update to the latest release first. |
| Builds from `main` | Best effort. Reports are welcome. |

## Reporting a vulnerability

**Please don't report security problems in a public issue, discussion or pull request.** Report them privately through GitHub instead:

1. Open the repository's **Security** tab.
2. Click **Report a vulnerability**, or go straight to the [private report form](https://github.com/sussyswimmer/bordervisualizer/security/advisories/new).
3. Describe the problem: the Rimlight version (Settings > About), your Windows version and build (run `winver`), the steps or a proof of concept, and what an attacker could do with it.

Only you and the maintainers can see the report. If you don't see the **Report a vulnerability** button, or the form doesn't open, [ask for a private contact](https://github.com/sussyswimmer/bordervisualizer/issues/new?template=private_contact.yml) instead. That issue is public, so leave out every detail, and the maintainer will set up a private channel with you.

What happens next:

- The maintainer confirms the report and works with you on the details in the private report.
- Once a fix is ready, it ships in a new release, and a GitHub security advisory is published alongside it. You're credited in the advisory unless you'd rather not be.
- Please keep the details private until that release is out.

Rimlight has a single maintainer, so a reply can take a few days.

## Scope

Rimlight is a tray app that draws a glow around the edges of your screens and follows the music your PC is playing. By design:

- It captures only system output audio, through WASAPI loopback. It never opens the microphone.
- Audio is analyzed in memory and discarded immediately. Nothing is recorded or saved.
- Track info and album art are read locally from Windows' media controls and never leave the device.
- There's no telemetry or analytics. The only network request is the update check against this repository's GitHub Releases.

**In scope:**

- Anything that breaks one of the promises above, for example Rimlight opening a capture device other than loopback, or sending data anywhere other than the update check.
- Code execution or privilege escalation through Rimlight, its installer (`RimlightSetup.exe`) or its updater.
- The updater accepting a package that doesn't come from this repository's releases.
- Crafted track info or album art (any app can publish these to Windows' media controls) that crashes Rimlight in a way that looks exploitable, or that makes it run code.
- Another user account on the same PC controlling your copy of Rimlight, for example through its single-instance channel.
- The build and release pipeline in this repository: GitHub Actions workflows and packaging scripts, for example a pull request that can read secrets or change a published release.

**Out of scope:**

- Bugs in Windows, GPU drivers, .NET or the libraries Rimlight uses. Please report those to their maintainers. If Rimlight ships a version with a known vulnerability, that *is* in scope.
- Attacks that need administrator rights, or that already control your user account (for example, replacing Rimlight's files in your user profile).
- The *Windows protected your PC* (SmartScreen) warning when installing. Rimlight isn't code-signed yet; choose **More info**, then **Run anyway**.
- Crashes, visual glitches and high CPU use with no security impact. Those are regular bugs: please use the [bug report form](https://github.com/sussyswimmer/bordervisualizer/issues/new?template=bug_report.yml).
