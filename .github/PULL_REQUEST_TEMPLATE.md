<!--
Title: a Conventional Commit with the task ID if there is one, for example
`feat(overlay): K1 click-through glow overlays` or `fix(audio): keep beats at low volume`.
It becomes the squash commit on main and sets the labels for the release notes.
-->

## What and why

<!-- What does this change, and why? Link the issue it fixes ("Fixes #123"). -->

**Task:** <!-- The ID from docs/09-WORK-SPLIT.md (C11, K4, ...), or "none". -->

## How it was tested

<!-- Commands you ran and their results: test counts, ns/frame, allocations, before/after numbers where they matter. -->

## Manual test checklist

<!--
Needed when something visible changes: the glow, tray, Settings, installer or updates.
Write it for someone at a Windows PC: what to run, what they should see, and what counts as a bug.
Delete this section if nothing visible changed.
-->

## Checklist

- [ ] `dotnet build -c Release` finishes with zero warnings.
- [ ] `dotnet test` is green, and new logic has tests.
- [ ] No locks or allocations added to the audio callback or the per-frame path.
- [ ] `src/Rimlight.Core/Contracts/` is unchanged, or the change has an approved `CONTRACT CHANGE` in `docs/HANDOFF.md`.
- [ ] `docs/PROGRESS.md` is updated, and `docs/HANDOFF.md` entries this touches are resolved or added.
- [ ] `CHANGELOG.md` has a line under `[Unreleased]`, if users will notice the change.
- [ ] Visible change: the manual test checklist above is filled in.
- [ ] Still no microphone, no telemetry, and no network requests other than the update check.
