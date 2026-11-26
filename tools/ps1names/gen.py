#!/usr/bin/env python3
"""Builds src/EdenDeck.Backends.Duckstation/Data/ps1-names.tsv.gz: game names only (no disc data
or links), keyed by disc serial ("s<SERIAL>" -> Redump name), plus each Redump name in lower case
("n<name>" -> serial, so a .chd named after its Redump entry can be identified without reading it),
from libretro-database's Redump metadata (CC BY-SA 4.0, https://github.com/libretro/libretro-database).

Usage: gen.py <libretro-database checkout> <output .tsv.gz>
"""
import gzip, os, re, sys

src, out = sys.argv[1], sys.argv[2]
names = {}
by_name = {}
rank = lambda n: (any(t in n for t in ("(Beta", "(Proto", "(Demo", "(Sample", "(Kiosk", "(Taikenban", "(Trial")),
                  0 if "USA" in n else 1 if "Europe" in n else 2, n)
text = open(os.path.join(src, "metadat", "redump", "Sony - PlayStation.dat"), encoding="utf-8").read()
for block in re.findall(r'^game \((.*?)^\)', text, re.S | re.M):
    name = re.search(r'^\s*name "(.*)"', block, re.M).group(1)
    m = re.search(r'^\s*serial "([^"]+)"', block, re.M)
    if not m:
        continue
    # "SLUS-00067", "SLUS-00067GHA" (a Greatest Hits pressing), "SCUS-94163, SCUS-94164"
    serials = []
    for s in re.split(r"[,/]", m.group(1)):
        s = s.strip().upper()
        mm = re.match(r"^([A-Z]{4})[- ]?(\d{5})", s)
        if mm:
            serials.append(f"{mm.group(1)}-{mm.group(2)}")
    if not serials:
        continue
    for s in serials:
        names.setdefault(s, []).append(name)
    by_name.setdefault(name.lower(), serials[0])
lines = [f"s{k}\t{sorted(set(v), key=rank)[0]}" for k, v in sorted(names.items())]
lines += [f"n{k}\t{v}" for k, v in sorted(by_name.items())]
os.makedirs(os.path.dirname(out), exist_ok=True)
with gzip.GzipFile(out, "wb", mtime=0) as g:
    g.write(("\n".join(lines) + "\n").encode("utf-8"))
print(len(names), "serials,", len(by_name), "names")
