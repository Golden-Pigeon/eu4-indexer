#!/usr/bin/env pwsh
# Cross-publish self-contained binaries (CLI + MCP server) for every supported
# OS/arch target and archive each one. .NET cross-publishes from any host, so
# this produces all targets regardless of the machine it runs on.
#
# Runs on Windows (Windows PowerShell 5.1 or PowerShell 7+) and, since it is
# plain pwsh, on macOS/Linux too. The bash equivalent is build-binaries.sh.
#
# Archives are named eu4indexer-<version>-<rid>; -Version defaults to the value
# in Eu4Indexer.Core/AppInfo.fs so it matches the release tag.
#
# Usage:
#   ./scripts/build-binaries.ps1                          # all six targets
#   ./scripts/build-binaries.ps1 linux-x64 osx-arm64      # only the listed RIDs
#   ./scripts/build-binaries.ps1 -Version 0.1.0           # override version label

[CmdletBinding()]
param(
    # -Version is named-only (because $Rids declares an explicit Position, every
    # parameter without a Position becomes named-only), so bare arguments are
    # always treated as RIDs rather than being captured by -Version.
    [Parameter()] [string] $Version,
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)] [string[]] $Rids
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$AllRids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
if (-not $Rids -or $Rids.Count -eq 0) { $Rids = $AllRids }

# Components published per target: archive name -> project file.
$Components = [ordered]@{
    cli = 'Eu4Indexer.Cli/Eu4Indexer.Cli.fsproj'
    mcp = 'Eu4Indexer.Mcp/Eu4Indexer.Mcp.csproj'
}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$DistDir = Join-Path $RepoRoot 'dist'

if (-not $Version) {
    # Default to the single source of truth in AppInfo.fs (let Version = "x.y.z").
    $appInfo = Join-Path $RepoRoot 'Eu4Indexer.Core/AppInfo.fs'
    $m = Select-String -Path $appInfo -Pattern 'Version\s*=\s*"([^"]+)"' | Select-Object -First 1
    if ($m) { $Version = $m.Matches[0].Groups[1].Value } else { $Version = Get-Date -Format 'yyyyMMdd' }
}

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
$builtUnixOnWindows = $false

foreach ($rid in $Rids) {
    if ($AllRids -notcontains $rid) {
        throw "unknown RID '$rid' (expected one of: $($AllRids -join ', '))"
    }

    $ridDir = Join-Path $DistDir $rid
    if (Test-Path $ridDir) { Remove-Item -Recurse -Force $ridDir }

    # Each component goes in its own subfolder (cli/, mcp/) of the per-RID dir,
    # so the two self-contained apps never overwrite each other's shared dlls.
    foreach ($name in $Components.Keys) {
        $project = Join-Path $RepoRoot $Components[$name]
        $outDir = Join-Path $ridDir $name
        Write-Host "==> publishing $name for $rid" -ForegroundColor Cyan

        # Self-contained (bundles the .NET runtime so no install is needed on the
        # target). Not single-file and not trimmed: F#/CWTools rely on reflection,
        # which trimming can break. The per-RID native SQLite library is restored
        # automatically by SQLitePCLRaw.
        dotnet publish $project -c Release -r $rid --self-contained true `
            -p:PublishSingleFile=false -p:PublishTrimmed=false -o $outDir
        if ($LASTEXITCODE -ne 0) { throw "publish failed for $name/$rid" }
    }

    # One archive per target, containing both cli/ and mcp/.
    $base = "eu4indexer-$Version-$rid"
    if ($rid -like 'win-*') {
        $archive = Join-Path $DistDir "$base.zip"
        if (Test-Path $archive) { Remove-Item -Force $archive }
        Compress-Archive -Path (Join-Path $ridDir '*') -DestinationPath $archive
    }
    else {
        $archive = Join-Path $DistDir "$base.tar.gz"
        if (Test-Path $archive) { Remove-Item -Force $archive }
        # tar ships with Windows 10+ (bsdtar) and with macOS/Linux.
        tar -czf $archive -C $ridDir .
        if ($LASTEXITCODE -ne 0) { throw "tar failed for $rid" }
        if ($IsWindows -or $env:OS -eq 'Windows_NT') { $builtUnixOnWindows = $true }
    }
    Write-Host "    -> $archive"
}

Write-Host "Done. Archives in $DistDir" -ForegroundColor Green
if ($builtUnixOnWindows) {
    Write-Warning ("Linux/macOS archives were built on Windows, which has no Unix " +
        "executable bit; recipients run 'chmod +x Eu4Indexer.Cli' (or Eu4Indexer.Mcp) once after extracting.")
}
