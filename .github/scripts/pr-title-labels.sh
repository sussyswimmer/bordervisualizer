#!/usr/bin/env bash
# Prints the labels for a pull request title written as a Conventional Commit, one per line,
# sorted: the type's label (feat -> feature, fix, perf -> performance, docs, build, ci) and the
# area of each scope (audio, overlay, color, ui). Prints nothing for any other title.
# Used by .github/workflows/labeler.yml; the categories in .github/release.yml read the labels.
# Tests: bash .github/scripts/pr-title-labels.test.sh
set -euo pipefail

title="${1:-}"
title="${title,,}"
pattern='^([a-z]+)(\(([^)]*)\))?!?:'
[[ "$title" =~ $pattern ]] || exit 0
type="${BASH_REMATCH[1]}"
scopes="${BASH_REMATCH[3]}"

labels=()
case "$type" in
  feat) labels+=(feature) ;;
  fix) labels+=(fix) ;;
  perf) labels+=(performance) ;;
  docs) labels+=(docs) ;;
  build) labels+=(build) ;;
  ci) labels+=(ci) ;;
esac

# A scope may list several areas: feat(audio,overlay) or fix(tray/settings).
IFS=', /' read -r -a parts <<< "$scopes"
for scope in "${parts[@]}"; do
  case "$scope" in
    audio | capture | loopback | wasapi | analyzer | beat | beats | dsp | fft)
      labels+=(audio) ;;
    overlay | render | renderer | glow | shader | d3d | gpu | dpi | monitor | monitors)
      labels+=(overlay) ;;
    color | colour | palette | media | nowplaying | album-art)
      labels+=(color) ;;
    ui | app | tray | settings | shell | visualizer | hotkey | startup | first-run | update | updates)
      labels+=(ui) ;;
  esac
done

if ((${#labels[@]} > 0)); then
  printf '%s\n' "${labels[@]}" | sort -u
fi
