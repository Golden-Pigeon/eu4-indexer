# eu4-indexer installer (Windows, PowerShell 5.1+ / 7+).
#
#   irm https://raw.githubusercontent.com/Golden-Pigeon/eu4-indexer/main/install.ps1 | iex
#
# Downloads the self-contained eu4indexer binary (no .NET install needed) plus
# the bundled skill into %USERPROFILE%\.eu4indexer and adds it to the user PATH.
# Then: eu4indexer setup; eu4indexer index; eu4indexer install.
#
# Options (env or params):
#   $env:EU4INDEXER_HOME / -Location DIR    install dir (default: ~\.eu4indexer)
#   $env:EU4INDEXER_VERSION / -Version V    release tag to pin (default: latest)
#   $env:EU4INDEXER_DIST / -Dist PATH       install from a local archive or dir
[CmdletBinding()]
param(
    [string] $Location = $env:EU4INDEXER_HOME,
    [string] $Version = $env:EU4INDEXER_VERSION,
    [string] $Dist = $env:EU4INDEXER_DIST
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo = 'Golden-Pigeon/eu4-indexer'
# $Version stays empty when not pinned, meaning "install the latest release".
if (-not $Location) { $Location = Join-Path $HOME '.eu4indexer' }

$arch = $env:PROCESSOR_ARCHITECTURE
$ridArch = if ($arch -match 'ARM64') { 'arm64' } else { 'x64' }
$Rid = "win-$ridArch"

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("eu4indexer-install-" + [guid]::NewGuid())
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$stage = Join-Path $tmp 'stage'
New-Item -ItemType Directory -Force -Path $stage | Out-Null

try {
    if ($Dist) {
        Write-Host "Installing eu4indexer ($Rid) from local dist: $Dist"
        if (Test-Path $Dist -PathType Container) {
            Copy-Item -Recurse -Force (Join-Path $Dist '*') $stage
        }
        else {
            tar -xzf $Dist -C $stage
        }
    }
    else {
        if ($Version) {
            # Pinned release: the tag segment selects the version; same version-less
            # asset name as the latest path.
            $url = "https://github.com/$Repo/releases/download/$Version/eu4indexer-$Rid.zip"
            Write-Host "Downloading eu4indexer $Version ($Rid)"
        }
        else {
            # Default: GitHub's latest-release redirect to the version-less asset.
            $url = "https://github.com/$Repo/releases/latest/download/eu4indexer-$Rid.zip"
            Write-Host "Downloading eu4indexer latest ($Rid)"
        }
        Write-Host "  $url"
        $zip = Join-Path $tmp 'eu4indexer.zip'
        Invoke-WebRequest -Uri $url -OutFile $zip
        # Clear Mark-of-the-Web so the extracted exe is not SmartScreen-blocked.
        Unblock-File -Path $zip
        Expand-Archive -Path $zip -DestinationPath $stage -Force
    }

    # Locate bin/ (directly under stage or nested one level).
    $binDir = Get-ChildItem -Path $stage -Recurse -Directory -Filter 'bin' | Select-Object -First 1
    if (-not $binDir) { throw "bin/ not found in the package" }
    $root = $binDir.Parent.FullName

    if (-not (Test-Path (Join-Path $binDir.FullName 'eu4indexer.exe'))) {
        throw "bin\eu4indexer.exe not found in the package"
    }

    Write-Host "Installing to $Location"
    New-Item -ItemType Directory -Force -Path $Location | Out-Null
    Copy-Item -Recurse -Force (Join-Path $root 'bin') $Location
    if (Test-Path (Join-Path $root 'skills')) {
        Copy-Item -Recurse -Force (Join-Path $root 'skills') $Location
    }

    # Clear Mark-of-the-Web on every extracted file (defence in depth).
    Get-ChildItem -Path $Location -Recurse -File | ForEach-Object { Unblock-File $_.FullName -ErrorAction SilentlyContinue }

    # Add the bin dir to the user PATH if not already present.
    $binPath = Join-Path $Location 'bin'
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (($userPath -split ';') -notcontains $binPath) {
        [Environment]::SetEnvironmentVariable('Path', "$binPath;$userPath", 'User')
        Write-Host "Added $binPath to your user PATH (restart the terminal to pick it up)."
    }

    Write-Host ""
    Write-Host "Installed eu4indexer to $binPath\eu4indexer.exe"
    Write-Host ""
    Write-Host "Next:"
    Write-Host "  eu4indexer setup      # download cwtools config rules"
    Write-Host "  eu4indexer index      # build an index from your EU4 install"
    Write-Host "  eu4indexer install    # register MCP + skill with Claude Code / Codex"
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
