#!/usr/bin/env bash
# Copies the bought 3D-theme models from the private TheRoben-Dev/scanline-assets repo into
# src/EdenDeck.App/Licensed/ (git-ignored), so the Godot export bakes them into the build.
# Their licence allows shipping them inside the app but not redistributing the files, which is
# why they live in a private repo and never in this one.
#
#   SCANLINE_ASSETS_TOKEN=<read token> tools/release/fetch-licensed-models.sh
#
# Without a token (forks, local builds) it does nothing and the app uses its generated props.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
if [ -z "${SCANLINE_ASSETS_TOKEN:-}" ]; then
  echo "::warning::SCANLINE_ASSETS_TOKEN is not set: building with the generated 3D props."
  exit 0
fi
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
git -c credential.helper= clone -q --depth 1 \
  "https://x-access-token:${SCANLINE_ASSETS_TOKEN}@github.com/TheRoben-Dev/scanline-assets.git" "$TMP/assets"
mkdir -p "$ROOT/src/EdenDeck.App/Licensed"
cp "$TMP"/assets/models/* "$ROOT/src/EdenDeck.App/Licensed/"
test -f "$ROOT/src/EdenDeck.App/Licensed/tv.glb"
echo "Licensed models: $(ls "$ROOT/src/EdenDeck.App/Licensed" | grep -c '\.glb$') props"
# Tell the UI smoke test later in the job to fail if the build doesn't carry them.
if [ -n "${GITHUB_ENV:-}" ]; then
  echo "SCANLINE_EXPECT_MODELS=1" >> "$GITHUB_ENV"
fi
