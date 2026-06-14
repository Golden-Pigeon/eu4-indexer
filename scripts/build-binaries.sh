#!/usr/bin/env bash
# Cross-publish self-contained binaries (CLI + MCP server) for every supported
# OS/arch target and archive each one. .NET cross-publishes from any host, so
# this produces all targets regardless of the machine it runs on.
#
# For macOS/Linux. On Windows use scripts/build-binaries.ps1 (this script also
# works under Git Bash / WSL if `dotnet`, `tar`, and `zip` are on PATH).
#
# Usage:
#   ./scripts/build-binaries.sh                      # all six targets
#   ./scripts/build-binaries.sh linux-x64 osx-arm64  # only the listed RIDs

set -euo pipefail

ALL_RIDS=(win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64)

# Components published per target, as "archive-name:project-file" pairs.
COMPONENTS=(
  "cli:Eu4Indexer.Cli/Eu4Indexer.Cli.fsproj"
  "mcp:Eu4Indexer.Mcp/Eu4Indexer.Mcp.csproj"
)

RIDS=("$@")
if [ ${#RIDS[@]} -eq 0 ]; then RIDS=("${ALL_RIDS[@]}"); fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$REPO_ROOT/dist"
VERSION="$(date +%Y%m%d)"

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

  # Each component goes in its own subfolder (cli/, mcp/) of the per-RID dir, so
  # the two self-contained apps never overwrite each other's shared dlls.
  for comp in "${COMPONENTS[@]}"; do
    name="${comp%%:*}"
    project="$REPO_ROOT/${comp#*:}"
    echo "==> publishing $name for $rid"

    # Self-contained (bundles the .NET runtime so no install is needed on the
    # target). Not single-file and not trimmed: F#/CWTools rely on reflection,
    # which trimming can break. The per-RID native SQLite library is restored
    # automatically by SQLitePCLRaw.
    dotnet publish "$project" -c Release -r "$rid" --self-contained true \
      -p:PublishSingleFile=false -p:PublishTrimmed=false -o "$rid_dir/$name"
  done

  # One archive per target, containing both cli/ and mcp/.
  base="eu4indexer-$VERSION-$rid"
  if [[ "$rid" == win-* ]]; then
    archive="$DIST_DIR/$base.zip"
    rm -f "$archive"
    if ! command -v zip >/dev/null 2>&1; then
      echo "error: 'zip' is required to package Windows targets; install it (e.g. apt install zip / brew install zip)" >&2
      exit 1
    fi
    ( cd "$rid_dir" && zip -qr "$archive" . )
  else
    archive="$DIST_DIR/$base.tar.gz"
    rm -f "$archive"
    # publish on a Unix host already marked the apphosts executable; tar keeps it.
    tar -czf "$archive" -C "$rid_dir" .
  fi
  echo "    -> $archive"
done

echo "Done. Archives in $DIST_DIR"
