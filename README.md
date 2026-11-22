# EdenDeck

A controller-first, fullscreen frontend for the [Eden](https://eden-emu.dev) Switch emulator on
Linux (SteamOS, Bazzite and other gaming distros; Steam Deck, ROG Ally, desktop and TV).
EdenDeck wraps the **unmodified** upstream Eden binary, so Eden Stable or Nightly can be updated
independently.

> Status: phase 1 (research spike). No app yet.

EdenDeck never downloads, bundles or links to keys, firmware or games. You bring your own.

## Repository layout

| Path | What |
|---|---|
| `docs/eden-integration.md` | Verified Eden CLI flags, paths, config keys, per-game config, mods, disabled add-ons |
| `docs/tech-stack.md` | Frontend stack decision, self-update and release plan |
| `docs/phase1-report.md` | Phase 1 findings, assumptions, risks, verification status |
| `eden_adapter/schema/` | Eden settings and enum snapshots per Eden version (adapter ground truth) |
| `tools/extract_eden_settings.py` | Regenerates those snapshots from an Eden source checkout |
| `layer/` | `VK_LAYER_EDENDECK_fsr` Vulkan layer (C++20, CMake) and its hardware test procedure |
| `docs/backends.md` | Backend interface design and plan for future emulators |

## Roadmap

1. Research spike: Eden integration, stack decision, Vulkan layer prototype *(this PR)*
2. MVP: Eden manager, library scan, controller-first UI, fullscreen launch, global settings
3. Controller mapping, Steam Input handling, per-game "Add to Steam"
4. Mod and cheat manager
5. Graphics: Eden's built-in FSR/SGSR and frame gen (60 fps cap), in-game overlay and quick menu
6. Packaging (AppImage + Flatpak), in-app self-updates, rollback, docs

### Future backends

EdenDeck's core is built around a generic emulator-backend interface (see
`docs/backends.md`), with Eden as the first implementation. Planned later, each as its own
module: **PS3** (RPCS3), **PS2** (PCSX2), **PS1** (DuckStation), **Xbox** (xemu), **NDS**
(melonDS), **GBA** (mGBA). The same rules apply: unmodified upstream emulators, and the user's
own BIOS/firmware only.
