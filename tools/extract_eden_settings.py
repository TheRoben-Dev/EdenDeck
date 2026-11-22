#!/usr/bin/env python3
"""Extract Eden's settings schema (INI section, key, per-game switchable, type, default)
and enum index tables from an Eden source checkout.

Usage: extract_eden_settings.py <eden-src-root> <label> <out.json>

The output is a snapshot the eden_adapter uses to know which keys exist in which Eden
version, which INI section they live in, and how enum values are encoded (Eden writes
enums to the INI as their integer index, so index tables must be versioned).

This parses C++ with regexes on purpose (no build needed). It is good enough for the
declaration style in src/common/settings.h; re-check the output when Eden refactors it.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

# Category -> INI section, from Settings::TranslateCategory (src/common/settings.cpp).
# Spaces in section names are written as %20 by Config::AdjustKey.
SECTION = {
    "Android": "Android", "Audio": "Audio", "Core": "Core",
    "Cpu": "Cpu", "CpuDebug": "Cpu", "CpuUnsafe": "Cpu", "Overlay": "Overlay",
    "Renderer": "Renderer", "RendererAdvanced": "Renderer", "RendererHacks": "Renderer",
    "RendererDebug": "Renderer", "RendererExtensions": "Renderer",
    "System": "System", "SystemAudio": "System", "DataStorage": "Data%20Storage",
    "Debugging": "Debugging", "DebuggingGraphics": "Debugging", "GpuDriver": "GpuDriver",
    "LibraryApplet": "LibraryApplet", "Miscellaneous": "Miscellaneous", "Network": "Network",
    "WebService": "WebService", "AddOns": "DisabledAddOns", "Controls": "Controls",
    "Ui": "UI", "UiGeneral": "UI", "UiAudio": "UiAudio", "UiLayout": "UILayout",
    "UiGameList": "UIGameList",
}

DECL = re.compile(
    r'\b(Switchable)?(?:Ranged)?Setting<([^;{]*?)>\s+(\w+)\s*\{([^;]*?)"([A-Za-z0-9_ ]+)"\s*,\s*Category::(\w+)',
    re.S)
ENUM = re.compile(r'ENUM\(\s*(\w+)\s*,([^)]*)\)', re.S)


def strip_comments(src: str) -> str:
    src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)
    return re.sub(r'//[^\n]*', '', src)


def main() -> int:
    root, label, out = Path(sys.argv[1]), sys.argv[2], Path(sys.argv[3])
    settings_h = strip_comments((root / "src/common/settings.h").read_text())
    enums_h = strip_comments((root / "src/common/settings_enums.h").read_text())
    try:
        commit = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
    except Exception:
        commit = None

    settings = []
    for m in DECL.finditer(settings_h):
        args = " ".join(m.group(4).split())
        args = re.sub(r'^linkage,\s*', '', args).rstrip(', ')
        category = m.group(6)
        settings.append({
            "key": m.group(5),
            "member": m.group(3),
            "category": category,
            "section": SECTION.get(category, category),
            "per_game": bool(m.group(1)),
            "type": " ".join(m.group(2).split()),
            "default_expr": args,  # may contain #ifdef'd platform defaults
        })
    settings.sort(key=lambda s: (s["section"], s["key"]))

    enums = {}
    for m in ENUM.finditer(enums_h):
        values = [v.strip() for v in m.group(2).split(",") if v.strip()]
        enums[m.group(1)] = values  # list index == value written to the INI

    out.write_text(json.dumps({
        "eden_label": label,
        "eden_commit": commit,
        "settings": settings,
        "enums": enums,
    }, indent=1) + "\n")
    print(f"{label}: {len(settings)} settings, {len(enums)} enums -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
