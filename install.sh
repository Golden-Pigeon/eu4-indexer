#!/usr/bin/env sh
# eu4-indexer installer (macOS / Linux).
#
#   curl -fsSL https://raw.githubusercontent.com/Golden-Pigeon/eu4-indexer/main/install.sh | sh
#
# Downloads the self-contained eu4indexer binary (no .NET install needed) plus
# the bundled skill into ~/.eu4indexer and puts `eu4indexer` on PATH. Then:
#   eu4indexer setup            # download the cwtools config rules
#   eu4indexer index            # build an index from your local EU4 install
#   eu4indexer install          # register the MCP server + skill with agents
#
# Options (env or flags):
#   EU4INDEXER_HOME / --location DIR   install dir (default: ~/.eu4indexer)
#   EU4INDEXER_VERSION / --version V   release tag (default: v0.2.0)
#   EU4INDEXER_DIST PATH               install from a local archive or directory
#                                      (offline/dev mode; skips the download)
set -eu

REPO="Golden-Pigeon/eu4-indexer"
VERSION="${EU4INDEXER_VERSION:-v0.2.0}"
INSTALL_DIR="${EU4INDEXER_HOME:-$HOME/.eu4indexer}"
DIST="${EU4INDEXER_DIST:-}"

while [ $# -gt 0 ]; do
  case "$1" in
    --location) INSTALL_DIR="$2"; shift 2 ;;
    --location=*) INSTALL_DIR="${1#*=}"; shift ;;
    --version) VERSION="$2"; shift 2 ;;
    --version=*) VERSION="${1#*=}"; shift ;;
    --dist) DIST="$2"; shift 2 ;;
    --dist=*) DIST="${1#*=}"; shift ;;
    *) echo "unknown option: $1" >&2; exit 1 ;;
  esac
done

# Resolve the .NET runtime identifier from the host.
os="$(uname -s)"
arch="$(uname -m)"
case "$os" in
  Darwin) rid_os="osx" ;;
  Linux) rid_os="linux" ;;
  *) echo "unsupported OS: $os (use install.ps1 on Windows)" >&2; exit 1 ;;
esac
case "$arch" in
  arm64|aarch64) rid_arch="arm64" ;;
  x86_64|amd64) rid_arch="x64" ;;
  *) echo "unsupported architecture: $arch" >&2; exit 1 ;;
esac
RID="$rid_os-$rid_arch"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

stage="$tmp/stage"
mkdir -p "$stage"

if [ -n "$DIST" ]; then
  echo "Installing eu4indexer ($RID) from local dist: $DIST"
  if [ -d "$DIST" ]; then
    cp -R "$DIST"/. "$stage"/
  else
    tar -xzf "$DIST" -C "$stage"
  fi
else
  ver_no_v="${VERSION#v}"
  url="https://github.com/$REPO/releases/download/$VERSION/eu4indexer-$ver_no_v-$RID.tar.gz"
  echo "Downloading eu4indexer $VERSION ($RID)"
  echo "  $url"
  curl -fsSL "$url" -o "$tmp/eu4indexer.tar.gz"
  tar -xzf "$tmp/eu4indexer.tar.gz" -C "$stage"
fi

# Stage may contain bin/ + skills/ directly, or nested one level (tar of a dir).
if [ ! -d "$stage/bin" ] && [ -d "$(find "$stage" -maxdepth 2 -type d -name bin | head -1)" ]; then
  inner="$(dirname "$(find "$stage" -maxdepth 2 -type d -name bin | head -1)")"
  stage="$inner"
fi

if [ ! -f "$stage/bin/eu4indexer" ]; then
  echo "error: bin/eu4indexer not found in the package" >&2
  exit 1
fi

echo "Installing to $INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
cp -R "$stage/bin" "$INSTALL_DIR/"
[ -d "$stage/skills" ] && cp -R "$stage/skills" "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/bin/eu4indexer"

# macOS Gatekeeper: clear the quarantine attribute on the downloaded binary so
# it runs without "cannot verify developer". (Unsigned build; see README.)
if [ "$os" = "Darwin" ]; then
  xattr -dr com.apple.quarantine "$INSTALL_DIR" 2>/dev/null || true
fi

# Put `eu4indexer` on PATH via ~/.local/bin (created if absent).
mkdir -p "$HOME/.local/bin"
ln -sf "$INSTALL_DIR/bin/eu4indexer" "$HOME/.local/bin/eu4indexer"

echo ""
echo "Installed eu4indexer to $INSTALL_DIR/bin/eu4indexer"
case ":$PATH:" in
  *":$HOME/.local/bin:"*) : ;;
  *) echo "NOTE: add ~/.local/bin to your PATH, e.g.:"
     echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.zshrc  # or ~/.bashrc" ;;
esac
echo ""
echo "Next:"
echo "  eu4indexer setup      # download cwtools config rules"
echo "  eu4indexer index      # build an index from your EU4 install"
echo "  eu4indexer install    # register MCP + skill with Claude Code / Codex"
