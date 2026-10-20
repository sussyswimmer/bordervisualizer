#!/bin/sh
# Replays every corpus track through a build's per-frame work (wav-analyze --engine).
# Usage: run_harness.sh <corpus dir> <out dir> [extra wav-analyze options, e.g. --tuning params.json]
# Build first: dotnet build tools/wav-analyze -c Release. To compare two builds, build each one into its own
# folder and set WAV_ANALYZE to its wav-analyze.dll.
set -e
corpus="$1"; out="$2"
[ -n "$corpus" ] && [ -n "$out" ] || { echo "usage: $0 <corpus dir> <out dir> [wav-analyze options]" >&2; exit 2; }
shift 2
here="$(cd "$(dirname "$0")" && pwd)"
dll="${WAV_ANALYZE:-$here/../wav-analyze/bin/Release/net8.0/wav-analyze.dll}"
[ -f "$dll" ] || { echo "$dll not found: run dotnet build tools/wav-analyze -c Release, or set WAV_ANALYZE" >&2; exit 2; }
mkdir -p "$out"
for wav in "$corpus"/*.wav; do
  id="$(basename "$wav" .wav)"
  dotnet "$dll" "$wav" --engine --no-plot --out "$out" "$@" > "$out/$id.log"
  grep -E "^Light:" "$out/$id.log" | sed "s#^#$id: #"
done
