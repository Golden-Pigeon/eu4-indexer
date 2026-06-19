#!/usr/bin/env bash
# Cross-publish the self-contained eu4indexer binary (a single merged CLI that
# also hosts the MCP server via `serve`) for every supported OS/arch target and
# archive each one with the bundled skill. .NET cross-publishes from any host, so
# this produces all targets regardless of the machine it runs on.
#
# Each archive contains:
#   bin/       the self-contained eu4indexer app (no .NET install needed)
#   skills/    the eu4-indexer agent skill (copied to agents by `install`)
#
# For macOS/Linux. On Windows use scripts/build-binaries.ps1 (this script also
# works under Git Bash / WSL if `dotnet`, `tar`, and `zip` are on PATH).
#
# Archives are named eu4indexer-<rid> (version-less): the release tag the assets
# are attached to is what selects the version, so the installer can resolve both
# the latest (releases/latest/download/...) and a pinned (releases/download/<tag>/...)
# install from the same stable asset name.
#
# Usage:
#   ./scripts/build-binaries.sh                          # all six targets
#   ./scripts/build-binaries.sh linux-x64 osx-arm64      # only the listed RIDs

set -euo pipefail

ALL_RIDS=(win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64)

# The merged CLI is the only published app; it bundles the MCP server library.
CLI_PROJECT="Eu4Indexer.Cli/Eu4Indexer.Cli.fsproj"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$REPO_ROOT/dist"

RIDS=("$@")
if [ ${#RIDS[@]} -eq 0 ]; then RIDS=("${ALL_RIDS[@]}"); fi

mkdir -p "$DIST_DIR"

is_known_rid() {
  local x
  for x in "${ALL_RIDS[@]}"; do [ "$x" = "$1" ] && return 0; done
  return 1
}

for rid in "${RIDS[@]}"; do
  if ! is_known_rid "$rid"; then
    echo "unknown RID '$rid' (expected one of: ${ALL_RIDS[*]})" >&2
    exit 1
  fi

  rid_dir="$DIST_DIR/$rid"
  rm -rf "$rid_dir"

  echo "==> publishing eu4indexer for $rid"

  # Self-contained (bundles the .NET runtime so no install is needed on the
  # target). Not single-file and not trimmed: F#/CWTools rely on reflection,
  # which trimming can break. The per-RID native SQLite library is restored
  # automatically by SQLitePCLRaw.
  dotnet publish "$REPO_ROOT/$CLI_PROJECT" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=false -p:PublishTrimmed=false -o "$rid_dir/bin"

  # Bundle the agent skill alongside the binary so `eu4indexer install` can copy it.
  cp -R "$REPO_ROOT/skills" "$rid_dir/skills"

  # One archive per target, containing bin/ and skills/.
  # Version-less name: the release tag the asset is attached to selects the
  # version, so the same name serves both the latest and pinned install paths.
  if [[ "$rid" == win-* ]]; then
    archive="$DIST_DIR/eu4indexer-$rid.zip"
    rm -f "$archive"
    if ! command -v zip >/dev/null 2>&1; then
      echo "error: 'zip' is required to package Windows targets; install it (e.g. apt install zip / brew install zip)" >&2
      exit 1
    fi
    ( cd "$rid_dir" && zip -qr "$archive" . )
  else
    archive="$DIST_DIR/eu4indexer-$rid.tar.gz"
    rm -f "$archive"
    # publish on a Unix host already marked the apphosts executable; tar keeps it.
    tar -czf "$archive" -C "$rid_dir" .
  fi
  echo "    -> $archive"
done

echo "Done. Archives in $DIST_DIR"
