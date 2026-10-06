# Regenerates every icon artifact from the SVG art in assets/ (doc 06 §2, doc 09 C10):
#   src/Rimlight.App/Assets/Rimlight.ico      app, exe and tray icon (16-256 px)
#   src/Rimlight.App/Assets/Rimlight-dim.ico  the same at 50 % opacity, for the tray while the glow is off
#   assets/icon-256.png, assets/icon-512.png  for the README
# Sizes up to 32 px are drawn from assets/icon-small.svg, larger ones from assets/icon.svg.
# Run from anywhere: pwsh build/icons.ps1 (Windows PowerShell 5.1 works too). Needs the .NET 8 SDK.
# build/icons.sh does the same on Linux and macOS.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Push-Location (Join-Path $PSScriptRoot '..')
try {
    $project = 'tools/icon-gen/Rimlight.IconGen.csproj'
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Building icon-gen failed ($LASTEXITCODE)." }

    function Invoke-IconGen {
        dotnet run --project $project -c Release --no-build -- @args
        if ($LASTEXITCODE -ne 0) { throw "icon-gen $($args -join ' ') failed ($LASTEXITCODE)." }
    }

    Invoke-IconGen ico src/Rimlight.App/Assets/Rimlight.ico --svg assets/icon.svg --small-svg assets/icon-small.svg
    Invoke-IconGen ico src/Rimlight.App/Assets/Rimlight-dim.ico --svg assets/icon.svg --small-svg assets/icon-small.svg --opacity 0.5
    Invoke-IconGen png assets/icon-256.png --svg assets/icon.svg --size 256
    Invoke-IconGen png assets/icon-512.png --svg assets/icon.svg --size 512
}
finally {
    Pop-Location
}
