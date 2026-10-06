<#
.SYNOPSIS
    Publishes Rimlight for Windows x64 and ARM64 and packs each build with Velopack.

.DESCRIPTION
    For each runtime in -Runtimes:

      1. dotnet publish src/Rimlight.App into <OutputDir>/publish/<runtime>: Release,
         self-contained, not trimmed.
      2. vpk pack into <OutputDir>/releases. This makes the installer
         (Rimlight-<channel>-Setup.exe), the portable zip (Rimlight-<channel>-Portable.zip), the
         full update package (.nupkg) and the update feed (releases.<channel>.json) that installed
         copies read. The channel is "win" for x64 and "win-arm64" for ARM64.
      3. Copies the installer into <OutputDir>/installers as RimlightSetup.exe (x64) or
         RimlightSetup-arm64.exe (ARM64). The README's download button links to the first name.

    The installer needs no admin rights: Velopack installs per user, into
    %LocalAppData%\Rimlight.

    Trimming is off. WPF and the WinRT projections are not trim-compatible, and a trimmed build can
    fail at run time with no build warning (doc 07 Phase 6 allows turning it off).

    vpk comes from the repo's tool manifest (.config/dotnet-tools.json), so every machine packs
    with the same version. On Linux and macOS the script cross-packs with vpk's [win] directive;
    code signing needs Windows.

    Every run starts by deleting <OutputDir>/publish, <OutputDir>/releases and
    <OutputDir>/installers, so no stale package can end up in a release.

.PARAMETER Version
    The package version: SemVer 2 without build metadata, such as 1.2.0, or 1.3.0-beta.1 for a
    prerelease. A leading "v" (or "V") is dropped, so a tag name works too. Default: <Version> in
    src/Directory.Build.props.

.PARAMETER Runtimes
    The builds to make: win-x64, win-arm64, or both (the default).

.PARAMETER OutputDir
    Where the publish, releases and installers folders go. Default: artifacts in the repo root.

.PARAMETER AzureTrustedSignFile
    Signs the executables and the installer with Azure Trusted Signing. The value is the path to
    the metadata JSON (Endpoint, CodeSigningAccountName, CertificateProfileName). The credentials
    come from the AZURE_TENANT_ID, AZURE_CLIENT_ID and AZURE_CLIENT_SECRET environment variables.
    Windows only. Without this parameter the build is unsigned.

.PARAMETER SkipVelopackAppCheck
    Packs even when Rimlight.exe doesn't call VelopackApp.Build().Run() (task K9). This is only for
    testing the packaging: such an installer can't run Velopack's install hooks or update itself.
    Tagged releases never use it.

.EXAMPLE
    ./build/pack.ps1

    Packs x64 and ARM64 at the version in src/Directory.Build.props.

.EXAMPLE
    ./build/pack.ps1 -Version v1.0.0 -Runtimes win-x64
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [string] $Version,

    [ValidateSet('win-x64', 'win-arm64')]
    [string[]] $Runtimes = @('win-x64', 'win-arm64'),

    [string] $OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'),

    [string] $AzureTrustedSignFile,

    [switch] $SkipVelopackAppCheck
)

# The publisher shown in Windows Settings > Apps.
$PackAuthors = 'Maxwell Olander'

function Get-BuildProperty {
    # The value of the first <$Name> in a PropertyGroup of an MSBuild file, e.g. AppName.
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $Name)
    $match = Select-Xml -LiteralPath $Path -XPath "/Project/PropertyGroup/$Name" | Select-Object -First 1
    if (-not $match -or [string]::IsNullOrWhiteSpace($match.Node.InnerText)) {
        throw "No <$Name> in $Path."
    }
    $match.Node.InnerText.Trim()
}

function Resolve-PackVersion {
    # $Version without a leading "v", or $Default when no version is given. Velopack orders
    # updates by SemVer 2. Build metadata (+...) is rejected: SemVer ignores it in comparisons, so
    # two builds that differ only there could never update to each other.
    param([string] $Version, [Parameter(Mandatory)] [string] $Default)
    $resolved = if ([string]::IsNullOrWhiteSpace($Version)) { $Default } else { $Version.Trim() -replace '^v', '' }
    $number = '(0|[1-9]\d*)'
    $identifier = '(0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)'
    if ($resolved -notmatch "^$number\.$number\.$number(-$identifier(\.$identifier)*)?$") {
        throw "Version '$resolved' is not a SemVer 2 version like 1.2.0 or 1.3.0-beta.1."
    }
    $resolved
}

function Get-PackChannel {
    # The Velopack channel of a runtime. An installed copy only reads its own channel's feed, so
    # x64 and ARM64 installs each get their own build. x64 uses Velopack's default "win".
    param([Parameter(Mandatory)] [string] $Runtime)
    switch ($Runtime) {
        'win-x64' { 'win' }
        'win-arm64' { 'win-arm64' }
        default { throw "Unsupported runtime '$Runtime'." }
    }
}

function Get-InstallerAlias {
    # The fixed download name of a runtime's installer. The README's download button links to
    # releases/latest/download/RimlightSetup.exe.
    param([Parameter(Mandatory)] [string] $AppName, [Parameter(Mandatory)] [string] $Runtime)
    switch ($Runtime) {
        'win-x64' { "${AppName}Setup.exe" }
        'win-arm64' { "${AppName}Setup-arm64.exe" }
        default { throw "Unsupported runtime '$Runtime'." }
    }
}

function Get-VpkArgumentList {
    # The dotnet arguments that run the manifest's vpk with $Arguments. vpk packs for Windows
    # natively on Windows; anywhere else it needs the [win] directive. (Built as a typed array: a
    # one-element @('vpk') returned from an if expression unrolls to a string, and string + array
    # then joins everything into one argument.)
    param([Parameter(Mandatory)] [string[]] $Arguments, [Parameter(Mandatory)] [bool] $OnWindows)
    [string[]] $list = @('vpk')
    if (-not $OnWindows) { $list += '[win]' }
    $list + $Arguments
}

function Invoke-Native {
    # Runs a command after echoing it, and throws if it fails. PowerShell before 7.3 doesn't stop
    # on a failing native command by itself.
    param([Parameter(Mandatory)] [string] $FilePath, [string[]] $ArgumentList = @())
    $shown = $ArgumentList | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }
    Write-Host "> $FilePath $($shown -join ' ')"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath $($ArgumentList[0])' failed with exit code $LASTEXITCODE."
    }
}

# Dot-sourcing the script (". ./build/pack.ps1", as build/pack.Tests.ps1 does) only defines the
# functions above.
if ($MyInvocation.InvocationName -eq '.') { return }

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$repoRoot = Split-Path -Parent $PSScriptRoot
$props = Join-Path $repoRoot 'src/Directory.Build.props'
$appName = Get-BuildProperty -Path $props -Name 'AppName'
$packVersion = Resolve-PackVersion -Version $Version -Default (Get-BuildProperty -Path $props -Name 'Version')
$project = Join-Path $repoRoot 'src/Rimlight.App/Rimlight.App.csproj'
$icon = Join-Path $repoRoot 'src/Rimlight.App/Assets/Rimlight.ico'

if ($AzureTrustedSignFile) {
    if (-not $IsWindows) { throw 'Code signing (-AzureTrustedSignFile) needs Windows.' }
    if (-not (Test-Path -LiteralPath $AzureTrustedSignFile -PathType Leaf)) {
        throw "The signing metadata file '$AzureTrustedSignFile' doesn't exist."
    }
    $AzureTrustedSignFile = (Resolve-Path -LiteralPath $AzureTrustedSignFile).Path
}

$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$publishRoot = Join-Path $OutputDir 'publish'
$releaseDir = Join-Path $OutputDir 'releases'
$installerDir = Join-Path $OutputDir 'installers'
foreach ($dir in $publishRoot, $releaseDir, $installerDir) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}
$null = New-Item -ItemType Directory -Path $releaseDir, $installerDir

# dotnet looks for the tool manifest from the current directory up.
Push-Location -LiteralPath $repoRoot
try {
    Invoke-Native dotnet @('tool', 'restore')

    foreach ($runtime in $Runtimes) {
        $channel = Get-PackChannel -Runtime $runtime
        $publishDir = Join-Path $publishRoot $runtime
        Write-Host "`n==> $appName $packVersion for $runtime (Velopack channel '$channel')"

        Invoke-Native dotnet @(
            'publish', $project,
            '--configuration', 'Release',
            '--runtime', $runtime,
            '--self-contained', 'true',
            '--output', $publishDir,
            '-p:PublishTrimmed=false',
            '-p:PublishSingleFile=false',
            "-p:Version=$packVersion")

        $packArgs = @(
            'pack',
            '--packId', $appName,
            '--packVersion', $packVersion,
            '--packDir', $publishDir,
            '--packTitle', $appName,
            '--packAuthors', $PackAuthors,
            '--mainExe', "$appName.exe",
            '--icon', $icon,
            '--channel', $channel,
            '--runtime', $runtime,
            '--outputDir', $releaseDir)
        if ($AzureTrustedSignFile) { $packArgs += '--azureTrustedSignFile', $AzureTrustedSignFile }
        if ($SkipVelopackAppCheck) { $packArgs += '--skipVeloAppCheck' }
        Invoke-Native dotnet (Get-VpkArgumentList -Arguments $packArgs -OnWindows $IsWindows)

        $setup = Join-Path $releaseDir "$appName-$channel-Setup.exe"
        if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw "vpk didn't create $setup." }
        Copy-Item -LiteralPath $setup -Destination (Join-Path $installerDir (Get-InstallerAlias -AppName $appName -Runtime $runtime))
    }
}
finally {
    Pop-Location
}

Write-Host "`n==> $appName $packVersion packages in $OutputDir"
Get-ChildItem -LiteralPath $releaseDir, $installerDir -File | ForEach-Object {
    Write-Host ('{0,9:N1} MB  {1}' -f ($_.Length / 1MB), [IO.Path]::GetRelativePath($OutputDir, $_.FullName))
}
