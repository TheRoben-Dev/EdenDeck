#!/usr/bin/env bash
# UI smoke test: fake $HOME with a small fake library, then drives the Godot app with scripted
# controller input (--smoke) under Xvfb and saves a screenshot per step.
#
#   tools/smoke/run-ui-smoke.sh <godot-mono-editor | exported Scanline.x86_64> <out-dir>
#
# The "games" are random bytes named like real dumps ("Title [TITLEID][v0].nsp"); the scanner
# falls back to the title id in the file name. No real game, key or firmware files are used.
set -euo pipefail
GODOT=$(realpath "$1")
OUT=$(realpath -m "$2")
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# Every XDG dir inside the fake home: CI runners set XDG_CONFIG_HOME, and a second run would
# otherwise see the first run's play history (and library order).
export HOME="$WORK/home" XDG_RUNTIME_DIR="$WORK/run"
export XDG_CONFIG_HOME="$HOME/.config" XDG_DATA_HOME="$HOME/.local/share" XDG_CACHE_HOME="$HOME/.cache"
mkdir -p "$HOME/Games/Switch" "$HOME/.config/eden" "$XDG_RUNTIME_DIR" "$OUT"
cp "$ROOT/tests/EdenDeck.Backends.Eden.Tests/Fixtures/nightly-467ab644/qt-config.ini" "$HOME/.config/eden/"
i=0
for t in "Mountain Climb Deluxe" "Sky Racers" "Pixel Farm Story" "Ocean Odyssey" "Robot Rumble" "Starlight Saga" "Castle Defenders"; do
  i=$((i + 1))
  head -c 4096 /dev/urandom > "$HOME/Games/Switch/$t [01000000000${i}0000][v0].nsp"
done
head -c 512 /dev/urandom > "$HOME/Games/Switch/Sky Racers [0100000000020800][v131072].nsp"
# A second copy of a game and a file still being copied must not add tiles.
cp "$HOME/Games/Switch/Sky Racers [0100000000020000][v0].nsp" "$HOME/Games/Switch/Sky Racers copy [0100000000020000][v0].nsp"
# A dump copied without its extension: shown as a "Fix name" row, not a tile.
{ printf 'PFS0\0\0\0\0\0\0\0\0\0\0\0\0'; head -c 2097152 /dev/zero; } > "$HOME/Games/Switch/Nameless Quest"
touch -d '10 minutes ago' "$HOME/Games/Switch/"*.nsp "$HOME/Games/Switch/Nameless Quest"
head -c 512 /dev/urandom > "$HOME/Games/Switch/Copying Now [0100000000090000][v0].nsp"

CLI="$ROOT/src/EdenDeck.Cli/bin/Release/net8.0/scanline.dll"
[ -f "$CLI" ] || CLI="$ROOT/src/EdenDeck.Cli/bin/Debug/net8.0/scanline.dll"
dotnet "$CLI" folders add "$HOME/Games/Switch" >/dev/null
dotnet "$CLI" scan
if [ -n "${SMOKE_THEME:-}" ]; then  # e.g. SMOKE_THEME=nightgrid for theme screenshots
  python3 - "$HOME/.config/scanline/settings.json" "$SMOKE_THEME" <<'PY'
import json, sys
p, theme = sys.argv[1], sys.argv[2]
s = json.load(open(p))
s["Theme"] = theme
json.dump(s, open(p, "w"))
PY
fi

# Fake Eden: records its arguments and exits after a moment, to exercise the real launch path.
FAKE="$WORK/Eden-Linux-v0.2.1-amd64.AppImage"
printf '#!/bin/sh\necho "$@" > "%s/eden-args.txt"\nsleep 1\n' "$OUT" > "$FAKE"
chmod +x "$FAKE"
dotnet "$CLI" eden use "$FAKE"

cd "$ROOT/src/EdenDeck.App"
PROJECT=(--path .)
[ "$(basename "$GODOT")" = "Scanline.x86_64" ] && PROJECT=()  # exported build: project is embedded
set +e
timeout 300 xvfb-run -a -s "-screen 0 1280x800x24" "$GODOT" "${PROJECT[@]}" --rendering-driver opengl3 \
  -- --windowed --smoke "$OUT" 2>&1 | tee "$OUT/godot.log"
code=${PIPESTATUS[0]}
set -e
cp "$HOME"/.local/share/*/logs/app.log "$OUT/app.log" 2>/dev/null || true
if grep -qE "Unhandled exception|System\.[A-Za-z.]*Exception|smoke: step .* failed" "$OUT/godot.log"; then
  echo "::error::exceptions during the UI smoke test (see godot.log)"
  exit 1
fi
if ! grep -q -- "-f -g $HOME/Games/Switch/" "$OUT/eden-args.txt" 2>/dev/null; then
  echo "::error::the app did not launch (fake) Eden with -f -g <game>"
  exit 1
fi
echo "eden args: $(cat "$OUT/eden-args.txt")"
shots=$(ls "$OUT"/*.png 2>/dev/null | wc -l)
echo "exit=$code screenshots=$shots"
[ "$code" -eq 0 ] && [ "$shots" -ge 8 ]
