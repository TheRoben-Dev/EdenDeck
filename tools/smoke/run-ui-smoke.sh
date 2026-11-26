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
# The fixture's mod folder is a Deck path; keep it inside the fake home, with two mods for
# "Castle Defenders" that both replace one file (the mods page warns about it).
sed -i "s|^load_directory=.*|load_directory=$HOME/eden-mods/|" "$HOME/.config/eden/qt-config.ini"
# Same for the NAND, with a save for "Castle Defenders" (the saves page backs it up).
sed -i "s|^nand_directory=.*|nand_directory=$HOME/eden-nand/|" "$HOME/.config/eden/qt-config.ini"
mkdir -p "$HOME/eden-nand/user/save/0000000000000000/00000000000000000000000000000001/0100000000070000"
echo "slot 1" > "$HOME/eden-nand/user/save/0000000000000000/00000000000000000000000000000001/0100000000070000/save.dat"
for m in "Better Water" "HD Textures"; do
  mkdir -p "$HOME/eden-mods/0100000000070000/$m/romfs/Data"
  echo "$m" > "$HOME/eden-mods/0100000000070000/$m/romfs/Data/water.bin"
done
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

# A Game Boy Advance game for the mGBA backend: just a cartridge header (title, game code, 0x96).
mkdir -p "$HOME/Games/GBA"
python3 - "$HOME/Games/GBA/Tiny Quest (USA).gba" <<'PY'
import sys
b = bytearray(0x400)
b[0xA0:0xA9] = b"TINYQUEST"; b[0xAC:0xB0] = b"BTQE"; b[0xB0:0xB2] = b"01"; b[0xB2] = 0x96; b[0xBD] = 0x42
open(sys.argv[1], "wb").write(b)
PY
touch -d '10 minutes ago' "$HOME/Games/GBA/"*.gba  # settled, not a copy in progress
# A PS3 disc folder for the RPCS3 backend: PS3_GAME/PARAM.SFO, ICON0.PNG and an EBOOT.BIN.
mkdir -p "$HOME/Games/PS3/Velvet Run/PS3_GAME/USRDIR"
python3 - "$HOME/Games/PS3/Velvet Run/PS3_GAME/PARAM.SFO" <<'PY'
import struct, sys
vals = [("CATEGORY", "DG"), ("TITLE", "Velvet Run"), ("TITLE_ID", "BLUS39001"), ("APP_VER", "01.00")]
keys, data, index = b"", b"", b""
for k, v in vals:
    b = v.encode() + b"\0"; m = (len(b) + 3) // 4 * 4
    index += struct.pack("<HHIII", len(keys), 0x0204, len(b), m, len(data))
    keys += k.encode() + b"\0"; data += b + b"\0" * (m - len(b))
keys += b"\0" * (-len(keys) % 4)
ks = 20 + len(index)
open(sys.argv[1], "wb").write(b"\0PSF\1\1\0\0" + struct.pack("<III", ks, ks + len(keys), len(vals)) + index + keys + data)
PY
echo "SCE" > "$HOME/Games/PS3/Velvet Run/PS3_GAME/USRDIR/EBOOT.BIN"
cp "$ROOT/tools/smoke/icon.jpg" "$HOME/Games/PS3/Velvet Run/PS3_GAME/ICON0.PNG"
touch -d '10 minutes ago' "$HOME/Games/PS3/Velvet Run/PS3_GAME/PARAM.SFO"
# A PS2 disc image for the PCSX2 backend: a tiny ISO9660 with SYSTEM.CNF and a boot ELF.
mkdir -p "$HOME/Games/PS2"
python3 - "$HOME/Games/PS2/Zephyr Line (USA).iso" <<'PY'
import struct, sys
S = 2048
cnf = b"BOOT2 = cdrom0:\\SLUS_299.90;1\r\nVER = 1.00\r\nVMODE = NTSC\r\n"
elf = b"\x7fELF" + bytes(range(256)) * 8
def rec(extent, size, is_dir, name):
    n = len(name); l = 33 + n + (1 if n % 2 == 0 else 0)
    r = bytearray(l); r[0] = l
    r[2:10] = struct.pack("<I", extent) + struct.pack(">I", extent)
    r[10:18] = struct.pack("<I", size) + struct.pack(">I", size)
    r[25] = 2 if is_dir else 0; r[32] = n; r[33:33 + n] = name
    return bytes(r)
iso = bytearray(22 * S)
iso[16 * S] = 1; iso[16 * S + 1:16 * S + 7] = b"CD001\1"
iso[16 * S + 156:16 * S + 190] = rec(18, S, True, b"\0")
iso[17 * S] = 255; iso[17 * S + 1:17 * S + 6] = b"CD001"
root = rec(18, S, True, b"\0") + rec(18, S, True, b"\1") + rec(19, len(cnf), False, b"SYSTEM.CNF;1") + rec(20, len(elf), False, b"SLUS_299.90;1")
iso[18 * S:18 * S + len(root)] = root
iso[19 * S:19 * S + len(cnf)] = cnf
iso[20 * S:20 * S + len(elf)] = elf
open(sys.argv[1], "wb").write(iso)
PY
touch -d '10 minutes ago' "$HOME/Games/PS2/"*.iso
# A PS1 disc image for the DuckStation backend: SYSTEM.CNF with BOOT (not BOOT2) and its executable.
mkdir -p "$HOME/Games/PS1"
python3 - "$HOME/Games/PS1/Zircon Tide (USA).iso" <<'PY'
import struct, sys
S = 2048
cnf = b"BOOT = cdrom:\\SLUS_299.91;1\r\nTCB = 4\r\nEVENT = 10\r\nSTACK = 801FFFF0\r\n"
exe = b"PS-X EXE" + bytes(2040)
def rec(extent, size, is_dir, name):
    n = len(name); l = 33 + n + (1 if n % 2 == 0 else 0)
    r = bytearray(l); r[0] = l
    r[2:10] = struct.pack("<I", extent) + struct.pack(">I", extent)
    r[10:18] = struct.pack("<I", size) + struct.pack(">I", size)
    r[25] = 2 if is_dir else 0; r[32] = n; r[33:33 + n] = name
    return bytes(r)
iso = bytearray(22 * S)
iso[16 * S] = 1; iso[16 * S + 1:16 * S + 7] = b"CD001\1"
iso[16 * S + 156:16 * S + 190] = rec(18, S, True, b"\0")
iso[17 * S] = 255; iso[17 * S + 1:17 * S + 6] = b"CD001"
root = rec(18, S, True, b"\0") + rec(18, S, True, b"\1") + rec(19, len(cnf), False, b"SYSTEM.CNF;1") + rec(20, len(exe), False, b"SLUS_299.91;1")
iso[18 * S:18 * S + len(root)] = root
iso[19 * S:19 * S + len(cnf)] = cnf
iso[20 * S:20 * S + len(exe)] = exe
open(sys.argv[1], "wb").write(iso)
PY
touch -d '10 minutes ago' "$HOME/Games/PS1/"*.iso

CLI="$ROOT/src/EdenDeck.Cli/bin/Release/net8.0/scanline.dll"
[ -f "$CLI" ] || CLI="$ROOT/src/EdenDeck.Cli/bin/Debug/net8.0/scanline.dll"
dotnet "$CLI" folders add "$HOME/Games/Switch" >/dev/null
dotnet "$CLI" folders add "$HOME/Games/GBA" >/dev/null
dotnet "$CLI" folders add "$HOME/Games/PS3" >/dev/null
dotnet "$CLI" folders add "$HOME/Games/PS2" >/dev/null
dotnet "$CLI" folders add "$HOME/Games/PS1" >/dev/null
dotnet "$CLI" scan
# One game with an icon already read (normally from the game's control data with the player's
# keys): its cartridge shows it pixelated on the label.
# A shader cache for Castle Defenders, for the Storage pages.
mkdir -p "$XDG_CACHE_HOME/eden/shader/0100000000070000"
head -c 300000 /dev/zero > "$XDG_CACHE_HOME/eden/shader/0100000000070000/vulkan.bin"
mkdir -p "$XDG_DATA_HOME/scanline/icons"
cp "$ROOT/tools/smoke/icon.jpg" "$XDG_DATA_HOME/scanline/icons/0100000000010000.jpg"
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
