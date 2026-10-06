#!/usr/bin/env bash
# Tests for pr-title-labels.sh. Run: bash .github/scripts/pr-title-labels.test.sh
set -euo pipefail

script="$(dirname "${BASH_SOURCE[0]}")/pr-title-labels.sh"
failures=0

check() {
  local title="$1" want="$2" got
  got="$(bash "$script" "$title" | paste -sd ' ' -)"
  if [[ "$got" == "$want" ]]; then
    echo "ok    $title -> [$got]"
  else
    echo "FAIL  $title -> [$got], want [$want]"
    failures=$((failures + 1))
  fi
}

# Types
check 'feat: add presets row' 'feature'
check 'fix: keep tray out of Efficiency Mode' 'fix'
check 'perf: skip the FFT on silent frames' 'performance'
check 'docs: link H-003 resolution to PR 4' 'docs'
check 'build: pin the SDK' 'build'
check 'ci: add the Windows job' 'ci'
check 'refactor: split the renderer' ''
check 'test: cover the decimator' ''
check 'chore: bump version' ''

# Scopes map to areas
check 'feat(audio): C2 spectral-flux beats' 'audio feature'
check 'fix(overlay): reassert topmost after wake' 'fix overlay'
check 'feat(palette): k-means extractor' 'color feature'
check 'fix(app): keep tray out of Efficiency Mode, ship Rimlight.exe with icon (#3)' 'fix ui'
check 'refactor(capture): reuse the ring buffer' 'audio'
check 'perf(render): cache the constant buffer' 'overlay performance'
check 'feat(settings): validation and clamping' 'feature ui'
check 'fix(core): expose an empty preset catalog until C7 (#4)' 'fix'
check 'docs(handoff): add H-007..H-010' 'docs'

# Several scopes, breaking marker, case and spacing
check 'feat(audio,overlay): beat pulse width' 'audio feature overlay'
check 'fix(tray/settings): reopen the window' 'fix ui'
check 'feat(audio, color): shared clock' 'audio color feature'
check 'feat!: new settings format' 'feature'
check 'fix(overlay)!: drop the legacy path' 'fix overlay'
check 'Feat(Audio): Capitalized title' 'audio feature'
check 'fix:no space after the colon' 'fix'

# Not Conventional Commits: no labels
check 'Update README.md' ''
check 'Revert "feat(audio): C2 beats"' ''
check '[WIP] feat(audio): beats' ''
check 'feat audio: missing colon' ''
check '' ''

if ((failures > 0)); then
  echo "$failures failure(s)"
  exit 1
fi
echo "All PR title label checks passed."
