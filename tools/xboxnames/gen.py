#!/usr/bin/env python3
"""Builds src/EdenDeck.Backends.Xemu/Data/xbox-names.tsv.gz: game names only (no disc data or
links), keyed by the serial printed on the disc ("MS-004", from the title id in the game's XBE),
from libretro-database's Redump metadata (CC BY-SA 4.0, https://github.com/libretro/libretro-database).

Usage: gen.py <libretro-database checkout> <output .tsv.gz>
"""
import gzip, os, re, sys

src, out = sys.argv[1], sys.argv[2]
names = {}
rank = lambda n: (any(t in n for t in ("(Beta", "(Proto", "(Demo", "(Sample", "(Kiosk", "(Trial")),
                  0 if "USA" in n else 1 if "Europe" in n else 2, n)
text = open(os.path.join(src, "metadat", "redump", "Microsoft - Xbox.dat"), encoding="utf-8").read()
for block in re.findall(r'^game \((.*?)^\)', text, re.S | re.M):
    name = re.search(r'^\s*name "(.*)"', block, re.M).group(1)
    m = re.search(r'^\s*serial "([^"]+)"', block, re.M)
    if not m:
        continue
    for s in m.group(1).split(","):
        mm = re.match(r"^([A-Z0-9]{2})-(\d{3})", s.strip().upper())
        if mm:
            names.setdefault(f"{mm.group(1)}-{mm.group(2)}", []).append(name)
lines = [f"s{k}\t{sorted(set(v), key=rank)[0]}" for k, v in sorted(names.items())]
os.makedirs(os.path.dirname(out), exist_ok=True)
with gzip.GzipFile(out, "wb", mtime=0) as g:
    g.write(("\n".join(lines) + "\n").encode("utf-8"))
print(len(names), "serials")
