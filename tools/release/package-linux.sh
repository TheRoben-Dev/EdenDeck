#!/usr/bin/env bash
# Assembles the portable Linux tarball from already-built parts.
#
#   tools/release/package-linux.sh <version> <godot-export-dir> <cli-binary> <layer-build-dir> <out-dir> [<lsfg-vk-dir>]
#
# Layout inside Scanline-<version>-linux-x86_64.tar.gz:
#   Scanline/Scanline.x86_64                     the app (Godot export, .NET bundled)
#   Scanline/data_EdenDeck.App_linuxbsd_x86_64/  its managed assemblies
#   Scanline/scanline                            headless launcher (Steam shortcuts)
#   Scanline/EdenDeck.x86_64, Scanline/edendeck  links to the above: the app's old name, so Steam
#                                                shortcuts made before the rename keep working
#   Scanline/vulkan/                             VK_LAYER_SCANLINE_overlay + manifest (relative path)
#   Scanline/vulkan/lsfg-vk/                     bundled lsfg-vk layer (GPL-3.0, from build-lsfg-vk.sh),
#                                                with its license and SOURCE.txt; its source tarball
#                                                goes next to the release files
#   Scanline/scanline.svg                        icon for the menu entry
#   Scanline/scanline.png                        icon for Steam shortcuts (Steam can't show SVG)
#   Scanline/README.txt
#
# EdenDeck-linux-x86_64.tar.gz is the same tree with the top folder named EdenDeck, for the
# in-app updater of versions before 0.3.5 (which only knows that name and layout).
set -euo pipefail
VERSION=$1 EXPORT=$2 CLI=$3 LAYER=$4 OUT=$(realpath -m "$5") LSFG=${6:-}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
APP="$STAGE/Scanline"
mkdir -p "$APP/vulkan" "$OUT"
cp -a "$EXPORT"/. "$APP/"
rm -f "$APP"/scanline "$APP"/edendeck "$APP"/*.pdb
test -x "$APP/Scanline.x86_64"
install -m 755 "$CLI" "$APP/scanline"
ln -s Scanline.x86_64 "$APP/EdenDeck.x86_64"
ln -s scanline "$APP/edendeck"
install -m 755 "$LAYER/libVkLayer_SCANLINE_overlay.so" "$APP/vulkan/"
install -m 644 "$LAYER/VkLayer_SCANLINE_overlay.json" "$APP/vulkan/"
install -m 644 "$ROOT/src/EdenDeck.App/icon.svg" "$APP/scanline.svg"
install -m 644 "$ROOT/tools/release/scanline.png" "$APP/scanline.png"
LSFG_SRC=()
if [ -n "$LSFG" ]; then
  mkdir -p "$APP/vulkan/lsfg-vk"
  install -m 755 "$LSFG/liblsfg-vk-layer.so" "$APP/vulkan/lsfg-vk/"
  install -m 644 "$LSFG/VkLayer_LSFGVK_frame_generation.json" "$LSFG/LICENSE.md" "$LSFG/SOURCE.txt" "$APP/vulkan/lsfg-vk/"
  for f in "$LSFG"/lsfg-vk-*-source.tar.gz; do
    cp "$f" "$OUT/"
    LSFG_SRC+=("$(basename "$f")")
  done
fi
cat > "$APP/README.txt" <<TXT
Scanline $VERSION (Linux x86_64)

Run:      ./Scanline.x86_64            (add -- --windowed to run in a window)
Launcher: ./scanline help              (used by per-game Steam shortcuts; ./edendeck still works)

vulkan/lsfg-vk/ holds lsfg-vk (GPL-3.0), used for frame generation when you don't have
lsfg-vk installed yourself. See vulkan/lsfg-vk/SOURCE.txt for its source.

Scanline never downloads keys, firmware or games. Use your own dumps.
Source and updates: https://github.com/TheRoben-Dev/Scanline
TXT
NAME="Scanline-$VERSION-linux-x86_64.tar.gz"
tar -C "$STAGE" --owner=0 --group=0 -czf "$OUT/$NAME" Scanline
# Unversioned copy so releases/latest/download/Scanline-linux-x86_64.tar.gz always works.
cp "$OUT/$NAME" "$OUT/Scanline-linux-x86_64.tar.gz"
# Old name and folder, for updaters from before the rename.
tar -C "$STAGE" --owner=0 --group=0 --transform 's,^Scanline,EdenDeck,S' -czf "$OUT/EdenDeck-linux-x86_64.tar.gz" Scanline
install -m 755 "$ROOT/tools/release/install.sh" "$OUT/install.sh"
(cd "$OUT" && sha256sum "$NAME" Scanline-linux-x86_64.tar.gz EdenDeck-linux-x86_64.tar.gz install.sh "${LSFG_SRC[@]}" > SHA256SUMS)
cat "$OUT/SHA256SUMS"
