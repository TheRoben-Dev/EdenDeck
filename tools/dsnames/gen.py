#!/usr/bin/env python3
"""Builds src/EdenDeck.Backends.MelonDs/Data/ds-names.tsv.gz: game names only (no ROM data or links),
keyed by the cartridge's game code ("s<CODE>" -> name), plus the name of the game's cheat file in
libretro-database's cht folder where there is one ("h<CODE>" -> file name without .cht). From
libretro-database's No-Intro metadata and cheat list (CC BY-SA 4.0, https://github.com/libretro/libretro-database).

Usage: gen.py <libretro-database checkout> <output .tsv.gz>
"""
import gzip, os, re, sys

src, out = sys.argv[1], sys.argv[2]
rank = lambda n: (any(t in n for t in ("(Beta", "(Proto", "(Demo", "(Sample", "(Kiosk", "[BIOS]", "(Pirate", "(Unl", "(Debug")),
                  0 if "USA" in n else 1 if "World" in n or "Europe" in n else 2, n)
text = open(os.path.join(src, "dat", "Nintendo - Nintendo DS.dat"), encoding="utf-8").read()
serials = {}
for block in re.findall(r'^game \((.*?)^\)', text, re.S | re.M):
    m = re.search(r'^\s*name "(.*)"', block, re.M)
    s = re.search(r'^\s*serial "([A-Z0-9]{4})"', block, re.M)
    if m and s and "[BIOS]" not in m.group(1):
        serials.setdefault(s.group(1), []).append(m.group(1))
chtdir = os.path.join(src, "cht", "Nintendo - Nintendo DS")
chts = {f[:-4] for f in os.listdir(chtdir) if f.endswith(".cht")} if os.path.isdir(chtdir) else set()
lines, withcht = [], 0
for code, names in sorted(serials.items()):
    names = sorted(set(names), key=rank)
    lines.append(f"s{code}\t{names[0]}")
    has = [n for n in names if n in chts]
    if has:
        lines.append(f"h{code}\t{has[0]}")
        withcht += 1
os.makedirs(os.path.dirname(out), exist_ok=True)
with gzip.GzipFile(out, "wb", mtime=0) as g:
    g.write(("\n".join(lines) + "\n").encode("utf-8"))
print(len(serials), "codes,", withcht, "with cheats")
