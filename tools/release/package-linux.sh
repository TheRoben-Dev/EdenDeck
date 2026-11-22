#!/usr/bin/env bash
# Assembles the portable Linux tarball from already-built parts.
#
#   tools/release/package-linux.sh <version> <godot-export-dir> <cli-binary> <layer-build-dir> <out-dir>
#
# Layout inside EdenDeck-<version>-linux-x86_64.tar.gz:
#   EdenDeck/EdenDeck.x86_64                     the app (Godot export, .NET bundled)
#   EdenDeck/data_EdenDeck.App_linuxbsd_x86_64/  its managed assemblies
#   EdenDeck/edendeck                            headless launcher (Steam shortcuts)
#   EdenDeck/vulkan/                             VK_LAYER_EDENDECK_fsr + manifest (relative path)
#   EdenDeck/README.txt
set -euo pipefail
VERSION=$1 EXPORT=$2 CLI=$3 LAYER=$4 OUT=$(realpath -m "$5")
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
APP="$STAGE/EdenDeck"
mkdir -p "$APP/vulkan" "$OUT"
cp -a "$EXPORT"/. "$APP/"
rm -f "$APP"/edendeck "$APP"/*.pdb
install -m 755 "$CLI" "$APP/edendeck"
install -m 755 "$LAYER/libVkLayer_EDENDECK_fsr.so" "$APP/vulkan/"
install -m 644 "$LAYER/VkLayer_EDENDECK_fsr.json" "$APP/vulkan/"
cat > "$APP/README.txt" <<TXT
Eden Deck $VERSION (Linux x86_64)

Run:      ./EdenDeck.x86_64            (add -- --windowed to run in a window)
Launcher: ./edendeck help              (used by per-game Steam shortcuts)

Eden Deck never downloads keys, firmware or games. Use your own dumps.
Source and updates: https://github.com/TheRoben-Dev/EdenDeck
TXT
NAME="EdenDeck-$VERSION-linux-x86_64.tar.gz"
tar -C "$STAGE" --owner=0 --group=0 -czf "$OUT/$NAME" EdenDeck
(cd "$OUT" && sha256sum "$NAME" > SHA256SUMS)
cat "$OUT/SHA256SUMS"
