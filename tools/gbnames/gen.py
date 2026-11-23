#!/usr/bin/env python3
"""Builds src/EdenDeck.Backends.Mgba/Data/gb-names.tsv.gz: game names only (no ROM data or links),
keyed by ROM CRC32 ("c<CRC>") and GBA game code ("s<CODE>"), from libretro-database's No-Intro
metadata (CC BY-SA 4.0, https://github.com/libretro/libretro-database).

Usage: gen.py <libretro-database checkout> <output .tsv.gz>
"""
import gzip, os, re, sys

src, out = sys.argv[1], sys.argv[2]
names = {}
serials = {}
rank = lambda n: (any(t in n for t in ("(Beta", "(Proto", "(Demo", "(Sample", "(Kiosk", "[BIOS]", "(Pirate", "(Unl")),
                  0 if "USA" in n else 1 if "World" in n or "Europe" in n else 2, n)
for f in ("Nintendo - Game Boy Advance.dat", "Nintendo - Game Boy Color.dat", "Nintendo - Game Boy.dat"):
    text = open(os.path.join(src, "metadat", "no-intro", f), encoding="utf-8").read()
    for block in re.findall(r'^game \((.*?)^\)', text, re.S | re.M):
        name = re.search(r'^\s*name "(.*)"', block, re.M).group(1)
        if "[BIOS]" in name:
            continue
        for crc in re.findall(r'\bcrc ([0-9A-Fa-f]{8})', block):
            names.setdefault(crc.upper(), name)
        m = re.search(r'^\s*serial "([A-Z0-9]{4})"', block, re.M)
        if m and "Advance" in f:
            serials.setdefault(m.group(1), []).append(name)
lines = [f"c{k}\t{v}" for k, v in sorted(names.items())]
lines += [f"s{k}\t{sorted(v, key=rank)[0]}" for k, v in sorted(serials.items())]
os.makedirs(os.path.dirname(out), exist_ok=True)
with gzip.GzipFile(out, "wb", mtime=0) as g:
    g.write(("\n".join(lines) + "\n").encode("utf-8"))
print(len(names), "crcs,", len(serials), "codes")
