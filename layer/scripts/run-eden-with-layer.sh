#!/usr/bin/env bash
# Launch an unmodified Eden binary with VK_LAYER_EDENDECK_fsr enabled for that process only.
#
#   run-eden-with-layer.sh <path-to-eden-or-AppImage> [eden args...]
#
# Example:
#   ./run-eden-with-layer.sh ~/Applications/Eden.AppImage -f -g ~/Games/MyGame.nsp
#
# Env overrides: LAYER_DIR (dir containing VkLayer_EDENDECK_fsr.json, default ../build),
#                OUT_DIR (where log + stats go, default $XDG_RUNTIME_DIR/edendeck or /tmp/edendeck)
set -euo pipefail

if [[ $# -lt 1 ]]; then
    sed -n '2,12p' "$0"
    exit 2
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAYER_DIR="${LAYER_DIR:-$here/../build}"
LAYER_DIR="$(cd "$LAYER_DIR" && pwd)"
OUT_DIR="${OUT_DIR:-${XDG_RUNTIME_DIR:-/tmp}/edendeck}"
mkdir -p "$OUT_DIR"

if [[ ! -f "$LAYER_DIR/VkLayer_EDENDECK_fsr.json" ]]; then
    echo "error: $LAYER_DIR/VkLayer_EDENDECK_fsr.json not found; build the layer first" >&2
    exit 1
fi

session="${XDG_SESSION_TYPE:-unknown}"
[[ -n "${GAMESCOPE_WAYLAND_DISPLAY:-}" ]] && session="gamescope"
log="$OUT_DIR/layer-$(date +%Y%m%d-%H%M%S)-$session.log"

echo "session=$session  log=$log  stats=$OUT_DIR/stats.json"

export VK_ADD_LAYER_PATH="$LAYER_DIR${VK_ADD_LAYER_PATH:+:$VK_ADD_LAYER_PATH}"
export VK_INSTANCE_LAYERS="VK_LAYER_EDENDECK_fsr${VK_INSTANCE_LAYERS:+:$VK_INSTANCE_LAYERS}"
export EDENDECK_LAYER_LOG="$log"
export EDENDECK_LAYER_STATS="$OUT_DIR/stats.json"

exec "$@"
