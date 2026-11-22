#!/usr/bin/env python3
"""Checks frames dumped by the layer (SCANLINE_LAYER_DUMP): off.ppm unfiltered, then any number of
filtered frames. A filtered frame must have black, curved-away corners and the scanline pattern
(row-to-row ripple from every third row being darker) that the unfiltered frame doesn't have."""
import sys
from PIL import Image


def stats(path):
    im = Image.open(path).convert("L")
    w, h = im.size
    # Background strip near the left edge, middle rows: flat in vkcube, so only the filter varies it.
    cols = range(w // 10, w // 10 + 20)
    rows = [sum(im.getpixel((x, y)) for x in cols) / len(cols) for y in range(h // 2 - 30, h // 2 + 30)]
    # Row-to-row change: ~0 on a flat background, large with scanlines.
    ripple = sum(abs(a - b) for a, b in zip(rows, rows[1:])) / (len(rows) - 1)
    return im.getpixel((1, 1)), ripple


off_corner, off_lines = stats(sys.argv[1])
print(f"off: corner={off_corner} row ripple={off_lines:.1f}")
assert off_corner > 20 and off_lines < 0.5, "unfiltered frame looks filtered"
for path in sys.argv[2:]:
    corner, lines = stats(path)
    print(f"{path}: corner={corner} row ripple={lines:.1f}")
    assert corner < 5, "corners should be black (tube curvature + vignette)"
    assert lines > 3.0, "no scanline pattern"
print("crt dumps ok")
