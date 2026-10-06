#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0' }
# Tests for the helpers in pack.ps1, and for the checks it makes before it starts work.
# Run: Invoke-Pester ./build
# Packing itself is checked by the dry run of .github/workflows/release.yml, which runs on pull
# requests that can break the packaging.

BeforeAll {
    . (Join-Path $PSScriptRoot 'pack.ps1')
}

Describe 'Resolve-PackVersion' {
    It 'uses the default when no version is given' {
        Resolve-PackVersion -Version '' -Default '0.1.0' | Should -Be '0.1.0'
        Resolve-PackVersion -Default '0.1.0' | Should -Be '0.1.0'
    }

    It 'accepts <Version>' -ForEach @(
        @{ Version = 'v1.0.0'; Expected = '1.0.0' }
        @{ Version = '1.0.0'; Expected = '1.0.0' }
        @{ Version = 'v10.20.30'; Expected = '10.20.30' }
        @{ Version = 'v1.3.0-beta.1'; Expected = '1.3.0-beta.1' }
        @{ Version = '2.0.0-rc.1.x-y'; Expected = '2.0.0-rc.1.x-y' }
        @{ Version = '1.0.0-0a'; Expected = '1.0.0-0a' }
        @{ Version = ' v1.2.3 '; Expected = '1.2.3' }
        @{ Version = 'V1.2.3'; Expected = '1.2.3' }
    ) {
        Resolve-PackVersion -Version $Version -Default '0.1.0' | Should -Be $Expected
    }

    It 'rejects <Version>' -ForEach @(
        @{ Version = 'v' }
        @{ Version = 'vNext' }
        @{ Version = 'v1' }
        @{ Version = '1.0' }
        @{ Version = '1.0.0.0' }
        @{ Version = '01.0.0' }
        @{ Version = '1.0.0-' }
        @{ Version = '1.0.0-beta..1' }
        @{ Version = '1.0.0-01' }
        @{ Version = '1.0.0+build.5' }
        @{ Version = 'vv1.0.0' }
        @{ Version = '1.0.0 beta' }
    ) {
        { Resolve-PackVersion -Version $Version -Default '0.1.0' } | Should -Throw '*not a SemVer 2 version*'
    }

    It 'rejects an invalid default' {
        { Resolve-PackVersion -Default '1.0' } | Should -Throw '*not a SemVer 2 version*'
    }
}

Describe 'Get-BuildProperty' {
    BeforeAll {
        $props = Join-Path $TestDrive 'Directory.Build.props'
        Set-Content -LiteralPath $props -Value @'
<Project>
  <PropertyGroup>
    <AppName> Glow </AppName>
    <Empty></Empty>
  </PropertyGroup>
  <PropertyGroup>
    <Version>2.3.4</Version>
    <AppName>Second</AppName>
  </PropertyGroup>
</Project>
'@
    }

    It 'reads the first value, trimmed, from any PropertyGroup' {
        Get-BuildProperty -Path $props -Name 'AppName' | Should -Be 'Glow'
        Get-BuildProperty -Path $props -Name 'Version' | Should -Be '2.3.4'
    }

    It 'throws for a missing or empty property' {
        { Get-BuildProperty -Path $props -Name 'Missing' } | Should -Throw '*No <Missing>*'
        { Get-BuildProperty -Path $props -Name 'Empty' } | Should -Throw '*No <Empty>*'
    }

    It 'finds an app name and a valid default version in src/Directory.Build.props' {
        $repoProps = Join-Path $PSScriptRoot '../src/Directory.Build.props'
        Get-BuildProperty -Path $repoProps -Name 'AppName' | Should -Not -BeNullOrEmpty
        $default = Get-BuildProperty -Path $repoProps -Name 'Version'
        Resolve-PackVersion -Default $default | Should -Be $default
    }
}

Describe 'Get-PackChannel and Get-InstallerAlias' {
    It 'names the channel and installer of <Runtime>' -ForEach @(
        @{ Runtime = 'win-x64'; Channel = 'win'; Installer = 'RimlightSetup.exe' }
        @{ Runtime = 'win-arm64'; Channel = 'win-arm64'; Installer = 'RimlightSetup-arm64.exe' }
    ) {
        Get-PackChannel -Runtime $Runtime | Should -Be $Channel
        Get-InstallerAlias -AppName 'Rimlight' -Runtime $Runtime | Should -Be $Installer
    }

    It 'rejects other runtimes' {
        { Get-PackChannel -Runtime 'win-x86' } | Should -Throw "*Unsupported runtime 'win-x86'*"
        { Get-InstallerAlias -AppName 'Rimlight' -Runtime 'linux-x64' } | Should -Throw "*Unsupported runtime 'linux-x64'*"
    }
}

Describe 'Get-VpkArgumentList' {
    # The Windows branch is what the release job runs, and Linux CI can't run it any other way.
    It 'runs vpk natively on Windows' {
        $list = Get-VpkArgumentList -Arguments @('pack', '--packId', 'Rimlight') -OnWindows $true
        $list.Count | Should -Be 4
        $list -join '|' | Should -Be 'vpk|pack|--packId|Rimlight'
    }

    It 'adds the [win] directive anywhere else' {
        $list = Get-VpkArgumentList -Arguments @('pack', '--packId', 'Rimlight') -OnWindows $false
        $list.Count | Should -Be 5
        $list -join '|' | Should -Be 'vpk|[win]|pack|--packId|Rimlight'
    }

    It 'keeps a single argument separate' {
        $list = Get-VpkArgumentList -Arguments 'pack' -OnWindows $true
        $list -join '|' | Should -Be 'vpk|pack'
    }
}

Describe 'pack.ps1 -Stage' {
    # The checks that run before the script deletes, restores or builds anything.
    BeforeAll {
        $script = Join-Path $PSScriptRoot 'pack.ps1'
        $appName = Get-BuildProperty -Path (Join-Path $PSScriptRoot '../src/Directory.Build.props') -Name 'AppName'
    }

    It 'refuses to sign in the Publish stage' {
        { & $script -Stage Publish -AzureTrustedSignFile 'signing.json' -OutputDir (Join-Path $TestDrive 'publish-only') } |
            Should -Throw '*happens in the Pack stage*'
    }

    It 'packs only after every runtime was published, and deletes nothing before that' {
        $out = Join-Path $TestDrive 'pack-only'
        $x64 = New-Item -ItemType Directory -Path (Join-Path $out 'publish/win-x64')
        Set-Content -LiteralPath (Join-Path $x64 "$appName.exe") -Value 'x64 build'
        $releases = New-Item -ItemType Directory -Path (Join-Path $out 'releases')
        Set-Content -LiteralPath (Join-Path $releases 'old.nupkg') -Value 'old package'

        { & $script -Stage Pack -OutputDir $out } | Should -Throw "*No published build at*win-arm64*$appName.exe*"
        Join-Path $releases 'old.nupkg' | Should -Exist
        Join-Path $x64 "$appName.exe" | Should -Exist
    }
}
