#!/usr/bin/env pwsh
# Cross-publish the self-contained eu4indexer binary (a single merged CLI that
# also hosts the MCP server via `serve`) for every supported OS/arch target and
# archive each one with the bundled skill. .NET cross-publishes from any host, so
# this produces all targets regardless of the machine it runs on.
#
# Each archive contains bin/ (the self-contained app) and skills/ (the agent skill).
#
# Runs on Windows (Windows PowerShell 5.1 or PowerShell 7+) and, since it is
# plain pwsh, on macOS/Linux too. The bash equivalent is build-binaries.sh.
#
# Archives are named eu4indexer-<rid> (version-less): the release tag the assets
# are attached to is what selects the version, so the same stable name serves both
# the latest (releases/latest/download/...) and pinned (releases/download/<tag>/...)
# install paths.
#
# Usage:
#   ./scripts/build-binaries.ps1                          # all six targets
#   ./scripts/build-binaries.ps1 linux-x64 osx-arm64      # only the listed RIDs

[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)] [string[]] $Rids
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$AllRids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
if (-not $Rids -or $Rids.Count -eq 0) { $Rids = $AllRids }

# The merged CLI is the only published app; it bundles the MCP server library.
$CliProject = 'Eu4Indexer.Cli/Eu4Indexer.Cli.fsproj'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$DistDir = Join-Path $RepoRoot 'dist'

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
$builtUnixOnWindows = $false

foreach ($rid in $Rids) {
    if ($AllRids -notcontains $rid) {
        throw "unknown RID '$rid' (expected one of: $($AllRids -join ', '))"
    }

    $ridDir = Join-Path $DistDir $rid
    if (Test-Path $ridDir) { Remove-Item -Recurse -Force $ridDir }

    Write-Host "==> publishing eu4indexer for $rid" -ForegroundColor Cyan

    # Self-contained (bundles the .NET runtime so no install is needed on the
    # target). Not single-file and not trimmed: F#/CWTools rely on reflection,
    # which trimming can break. The per-RID native SQLite library is restored
    # automatically by SQLitePCLRaw.
    dotnet publish (Join-Path $RepoRoot $CliProject) -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -o (Join-Path $ridDir 'bin')
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }

    # Bundle the agent skill alongside the binary so `eu4indexer install` can copy it.
    Copy-Item -Recurse -Force (Join-Path $RepoRoot 'skills') (Join-Path $ridDir 'skills')

    # One archive per target, containing bin/ and skills/.
    # Version-less name: the release tag the asset is attached to selects the
    # version, so the same name serves both the latest and pinned install paths.
    if ($rid -like 'win-*') {
        $archive = Join-Path $DistDir "eu4indexer-$rid.zip"
        if (Test-Path $archive) { Remove-Item -Force $archive }
        Compress-Archive -Path (Join-Path $ridDir '*') -DestinationPath $archive
    }
    else {
        $archive = Join-Path $DistDir "eu4indexer-$rid.tar.gz"
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
        "executable bit; the install script runs 'chmod +x bin/eu4indexer' after extracting.")
}
