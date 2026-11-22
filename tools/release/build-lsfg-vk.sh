#!/usr/bin/env bash
# Builds the lsfg-vk Vulkan layer that ships inside Scanline, from a pinned upstream commit.
#
#   tools/release/build-lsfg-vk.sh <out-dir>
#
# lsfg-vk (https://github.com/PancakeTAS/lsfg-vk, GPL-3.0) runs Lossless Scaling's frame generation
# shaders, read at runtime from the user's own Lossless.dll, as a Vulkan layer. Scanline only uses the
# bundled copy when no lsfg-vk 2.x is installed, and enables it per launch as an explicit layer.
#
# <out-dir> gets:
#   liblsfg-vk-layer.so
#   VkLayer_LSFGVK_frame_generation.json   explicit manifest, library path relative to it
#   LICENSE.md                              upstream GPL-3.0 text
#   SOURCE.txt                              where the exact source is, and how it was built
#   lsfg-vk-<commit>-source.tar.gz          the complete corresponding source (published with releases)
#
# Needs: git, cmake, ninja, clang. Matches upstream's own CI (Vulkan-Headers vulkan-sdk-1.4.328).
set -euo pipefail
LSFG_REPO=https://github.com/PancakeTAS/lsfg-vk
LSFG_COMMIT=0213be9ff93121283ad46a27119f40ece0e6f301   # tag v2.0.0-dev
VKH_TAG=vulkan-sdk-1.4.328
OUT=$(realpath -m "$1")
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$OUT"

git -c advice.detachedHead=false clone -q --filter=blob:none "$LSFG_REPO" "$WORK/lsfg-vk"
git -C "$WORK/lsfg-vk" -c advice.detachedHead=false checkout -q "$LSFG_COMMIT"
git clone -q --depth 1 -b "$VKH_TAG" https://github.com/KhronosGroup/Vulkan-Headers "$WORK/vkh"

cmake -S "$WORK/lsfg-vk" -B "$WORK/build" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_CXX_COMPILER=clang++ \
  -DCMAKE_CXX_FLAGS="-isystem $WORK/vkh/include" \
  -DLSFGVK_BUILD_VK_LAYER=ON -DLSFGVK_BUILD_UI=OFF -DLSFGVK_BUILD_CLI=OFF \
  -DLSFGVK_LAYER_LIBRARY_PATH=./liblsfg-vk-layer.so
ninja -C "$WORK/build" lsfg-vk-layer

so=$(find "$WORK/build" -name liblsfg-vk-layer.so -type f | head -1)
install -m 755 "$so" "$OUT/liblsfg-vk-layer.so"
strip --strip-unneeded "$OUT/liblsfg-vk-layer.so"
# Upstream installs an implicit manifest; ours is the same layer, explicit (enabled only by Scanline).
python3 - "$WORK/build" "$OUT" <<'PY'
import json, pathlib, sys
src = next(pathlib.Path(sys.argv[1]).rglob("VkLayer_LSFGVK_frame_generation.json"))
m = json.loads(src.read_text())
m["layer"]["library_path"] = "./liblsfg-vk-layer.so"
m["layer"]["description"] += " (bundled with Scanline)"
(pathlib.Path(sys.argv[2]) / "VkLayer_LSFGVK_frame_generation.json").write_text(json.dumps(m, indent=2) + "\n")
PY
install -m 644 "$WORK/lsfg-vk/LICENSE.md" "$OUT/LICENSE.md"
git -C "$WORK/lsfg-vk" archive --format=tar.gz --prefix="lsfg-vk-$LSFG_COMMIT/" -o "$OUT/lsfg-vk-$LSFG_COMMIT-source.tar.gz" "$LSFG_COMMIT"
cat > "$OUT/SOURCE.txt" <<TXT
lsfg-vk, bundled with Scanline. Licensed under the GNU GPL version 3 (LICENSE.md).

Upstream:  $LSFG_REPO
Commit:    $LSFG_COMMIT (tag v2.0.0-dev), unmodified
Source:    lsfg-vk-$LSFG_COMMIT-source.tar.gz, attached to every Scanline release on
           https://github.com/TheRoben-Dev/Scanline/releases
Built:     cmake -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_CXX_COMPILER=clang++
           -DLSFGVK_BUILD_VK_LAYER=ON -DLSFGVK_BUILD_UI=OFF -DLSFGVK_BUILD_CLI=OFF
           against Vulkan-Headers $VKH_TAG; manifest made explicit (library path ./liblsfg-vk-layer.so).

Scanline uses this copy only when no lsfg-vk 2.x is installed on the system, and only for
Eden launches with frame generation turned on. It needs your own Lossless.dll (Lossless Scaling
on Steam); nothing from Lossless Scaling is included here.
TXT
ls -la "$OUT"
