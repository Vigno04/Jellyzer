#!/usr/bin/env bash
set -euo pipefail

PLUGIN_NAME="Jellyzer"
DLL_NAME="Jellyfin.Plugin.Jellyzer.dll"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DLL_SRC="$SCRIPT_DIR/../Jellyfin.Plugin.Jellyzer/bin/Release/net9.0/$DLL_NAME"

# Verify the DLL was built
if [ ! -f "$DLL_SRC" ]; then
  echo "[ERROR] DLL not found: $DLL_SRC"
  echo "Run 'dotnet build --configuration Release' first."
  exit 1
fi

find_or_create_plugin_dir() {
  local base="$1"
  if [ ! -d "$base" ]; then
    return 1
  fi
  # Find existing plugin folder (newest first)
  local found
  found=$(ls -d "$base/${PLUGIN_NAME}"* 2>/dev/null | sort -rV | head -n 1)
  if [ -n "$found" ]; then
    echo "$found"
  else
    # First install – create versioned folder
    echo "$base/${PLUGIN_NAME}_1.0.0.0"
  fi
  return 0
}

DEST_DIR=""

if [ "$(uname)" = "Darwin" ]; then
  # macOS
  DEST_DIR=$(find_or_create_plugin_dir "$HOME/.local/share/jellyfin/plugins") || true
elif [ "$(uname -s)" = "Linux" ]; then
  # Linux – try system path first, then user path
  if [ -d "/var/lib/jellyfin/plugins" ]; then
    DEST_DIR=$(find_or_create_plugin_dir "/var/lib/jellyfin/plugins") || true
  else
    DEST_DIR=$(find_or_create_plugin_dir "$HOME/.local/share/jellyfin/plugins") || true
  fi
fi

if [ -z "$DEST_DIR" ]; then
  echo "[ERROR] Jellyfin plugin directory not found."
  echo "Expected one of:"
  echo "  /var/lib/jellyfin/plugins/           (Linux system install)"
  echo "  ~/.local/share/jellyfin/plugins/     (Linux/macOS user install)"
  exit 1
fi

echo "Installing to: $DEST_DIR"
mkdir -p "$DEST_DIR"
cp -f "$DLL_SRC" "$DEST_DIR/$DLL_NAME"

echo ""
echo "[OK] $DLL_NAME installed successfully."
echo "Restart Jellyfin to load the updated plugin."
