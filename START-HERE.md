# START HERE (for Maxwell)

This folder is the full spec for **Rimlight**, built by two agents in parallel:

- **Claude Code**, Lane B ("Windows & Experience"): overlay, GPU rendering, audio capture, media/album art, tray, Settings UI, integration, README/banner. It reads `CLAUDE.md`.
- **Codex**, Lane A ("Engine & Pipeline"): the beat-detection DSP, color science, light engine, settings logic, tests, benchmarks, WAV tuning tool, CI, packaging, and releases. It reads `AGENTS.md`.

`docs/09-WORK-SPLIT.md` is the rulebook: who owns which folders, the frozen interfaces between them, the task lists, and the handoff format.

## Setup (once)
1. Create an empty GitHub repo (e.g. `rimlight`) and clone it on your Windows PC.
2. Copy everything in this folder into the repo root and commit: `docs: add build spec`.
3. Push. Connect the repo to Codex (chatgpt.com/codex → environment for this repo). Install the .NET 8 SDK in the Codex environment setup script: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 && export PATH="$HOME/.dotnet:$PATH"`.

## Kickoff prompts

**1. Claude Code first** (on your Windows PC, in the repo folder):
> Read CLAUDE.md and docs/09-WORK-SPLIT.md, then do task K0 (scaffold + frozen contracts + fakes). Open a PR, and tell me when contracts are frozen.

Merge that PR.

**2. Then Codex** (start several cloud tasks at once, since they're independent):
> Read AGENTS.md and docs/09-WORK-SPLIT.md. Do task C1.

Run C4, C7, C8, and C11 as separate parallel Codex tasks with the same prompt and a different task ID.

**3. Back in Claude Code:**
> Continue with K1. Give me the manual test checklist when it's done.

## Ongoing loop
- Each agent works through its own task list, one PR per task.
- **Cross-review:** paste each Codex PR link into Claude Code ("review this PR as the Lane B owner"), and each Claude Code PR into Codex ("review this PR as the Lane A owner"). Merge when it's clean.
- When an agent says it needs something from the other, it's in `docs/HANDOFF.md`. Tell the other agent: "Resolve open HANDOFF entries addressed to you."
- Watch `docs/PROGRESS.md`. If one lane is 3+ tasks ahead, give it a floating task from doc 09 §6.

## Your checkpoints
1. After K0: start Codex.
2. After C2 + K3: run `--debug-visualizer` with 3–5 songs of different genres. Send Claude Code the tuned params; it passes them to Codex.
3. After C6 + K7: feature-complete. Do the cross-reviews.
4. After C13 + K10: release candidate. Record the demo GIF and screenshots (Claude Code gives you the steps), then K12 polishes the README.
5. Tag `v1.0.0`. The release workflow builds the installer, and you set the repo description, topics, and social preview from Claude Code's instructions.
