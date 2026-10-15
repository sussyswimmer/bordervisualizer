#!/usr/bin/env bash
# Regenerates every icon artifact from the SVG art in assets/ (doc 06 §2, doc 09 C10):
#   src/Rimlight.App/Assets/Rimlight.ico      app, exe and tray icon (16-256 px)
#   src/Rimlight.App/Assets/Rimlight-dim.ico  the same at 50 % opacity, for the tray while the glow is off
#   assets/icon-256.png, assets/icon-512.png  for the README
# Sizes up to 32 px are drawn from assets/icon-small.svg, larger ones from assets/icon.svg.
# Run from anywhere: build/icons.sh. Needs the .NET 8 SDK. build/icons.ps1 does the same on Windows.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

project=tools/icon-gen/Rimlight.IconGen.csproj
dotnet build "$project" -c Release --nologo -v quiet

icon_gen() {
    dotnet run --project "$project" -c Release --no-build -- "$@"
}

icon_gen ico src/Rimlight.App/Assets/Rimlight.ico --svg assets/icon.svg --small-svg assets/icon-small.svg
icon_gen ico src/Rimlight.App/Assets/Rimlight-dim.ico --svg assets/icon.svg --small-svg assets/icon-small.svg --opacity 0.5
icon_gen png assets/icon-256.png --svg assets/icon.svg --size 256
icon_gen png assets/icon-512.png --svg assets/icon.svg --size 512
