#!/usr/bin/env python3
"""Builds src/EdenDeck.Backends.Pcsx2/Data/ps2-names.tsv.gz: game names only (no disc data or
links), keyed by disc serial ("s<SERIAL>"), plus the first 16 hex digits of each single-file DVD
image's SHA-1 ("h<SHA1>" -> serial, so a DVD .chd can be named from its header), from
libretro-database's Redump metadata (CC BY-SA 4.0, https://github.com/libretro/libretro-database).

Usage: gen.py <libretro-database checkout> <output .tsv.gz>
"""
import gzip, os, re, sys

src, out = sys.argv[1], sys.argv[2]
names = {}
hashes = {}
rank = lambda n: (any(t in n for t in ("(Beta", "(Proto", "(Demo", "(Sample", "(Kiosk", "(Taikenban", "(Trial")),
                  0 if "USA" in n else 1 if "Europe" in n else 2, n)
text = open(os.path.join(src, "metadat", "redump", "Sony - PlayStation 2.dat"), encoding="utf-8").read()
for block in re.findall(r'^game \((.*?)^\)', text, re.S | re.M):
    name = re.search(r'^\s*name "(.*)"', block, re.M).group(1)
    m = re.search(r'^\s*serial "([^"]+)"', block, re.M)
    if not m:
        continue
    # "SLUS-20946", "SLES-50330, SLES-50331", "SLPM-65002-0" (a second pressing listed apart)
    serials = []
    for s in m.group(1).split(","):
        s = s.strip().upper()
        s = re.sub(r"^([A-Z]{4}-\d{5})-\d+$", r"\1", s)
        if re.fullmatch(r"[A-Z]{4}-\d{5}", s):
            serials.append(s)
    if not serials:
        continue
    for s in serials:
        names.setdefault(s, []).append(name)
    roms = re.findall(r'rom \( name "[^"]*\.iso" size \d+ crc \w+ md5 \w+ sha1 ([0-9A-Fa-f]{40})', block)
    if len(roms) == 1:
        hashes.setdefault(roms[0][:16].lower(), serials[0])
lines = [f"s{k}\t{sorted(set(v), key=rank)[0]}" for k, v in sorted(names.items())]
lines += [f"h{k}\t{v}" for k, v in sorted(hashes.items())]
os.makedirs(os.path.dirname(out), exist_ok=True)
with gzip.GzipFile(out, "wb", mtime=0) as g:
    g.write(("\n".join(lines) + "\n").encode("utf-8"))
print(len(names), "serials,", len(hashes), "hashes")
