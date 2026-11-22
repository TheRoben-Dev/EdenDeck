# Emulator backends (design note)

EdenDeck starts with Eden only, but later backends are planned: PS3 (RPCS3), PS2 (PCSX2),
PS1 (DuckStation), Xbox (xemu), NDS (melonDS) and GBA (mGBA). From phase 2 on, the code
treats Eden as the first implementation of a generic backend interface, so adding one
later is a new module and not a rewrite.

The rules that apply to Eden apply to every backend: the upstream emulator stays
unmodified and independently updatable; integration goes only through CLI, config files,
data dirs, env vars and our Vulkan layer; and BIOS/firmware/keys/games are never
downloaded, bundled or linked. Users import their own files.

## Interface (phase 2, `EdenDeck.Core`)

Each backend is one module (`EdenDeck.Backends.<Name>`) implementing `IEmulatorBackend`,
made of these parts:

| Capability | Responsibility | Eden implementation |
|---|---|---|
| `Identity` | id, display name, systems, file extensions | `eden`, Switch, `.nsp .xci .nca .nro` |
| `Install` | detect install, list channels, update check, download from official sources, verify, rollback | AppImage Stable/Nightly feeds |
| `VersionInfo` | detect installed version, pick the matching schema snapshot | `eden_adapter/schema/*.json` |
| `Library` | scan folders, read title/icon/ID/version metadata | NSP/XCI control NACP |
| `Launch` | build argv + env + working dir for a game; fullscreen; process lifetime | `eden -f -g <path>`, key-set dir farm |
| `Settings` | the curated option list (one-line help each); read/write global and per-game overrides in the emulator's own format | INI with `\use_global`, enums by index |
| `Input` | write controller profiles per game, handheld/docked, gyro/rumble | `config/input/*.ini` |
| `Mods` *(optional)* | mod/patch/cheat layout, enable/disable, conflicts | `load/<TID>/`, `[DisabledAddOns]` |
| `SystemFiles` *(optional)* | guided import + validation of user BIOS/firmware/keys | keys dir, NAND firmware |
| `Graphics` *(optional)* | which upscaling/frame-gen options exist natively, and how our layer applies | FSR/SGSR, Lossless frame gen |
| `SaveData` | where saves live, for backup/restore | NAND user saves |

The UI only talks to these capabilities. Optional parts that a backend doesn't
implement are hidden in the UI for that system. Our own features (Add to Steam, the
headless `edendeck launch <id>`, quick menu, overlay, stats) stay backend-agnostic: the
game id becomes `<backend>:<title-id>`.

Each backend gets the same treatment as Eden got in phase 1: verify CLI, config and paths
against its source, keep versioned schema snapshots, and add tests against sample configs
from several releases.

## Feasibility per backend (initial assessment, not yet verified against source)

| System | Emulator | Fit | Notes |
|---|---|---|---|
| PS3 | RPCS3 | Good | AppImage with official update feed, `--no-gui` launch, YAML config with per-game custom configs. User imports their own PS3 firmware (PUP). Games are folders or ISOs; DLC/updates install as PKG. Vulkan, so the overlay layer applies. |
| PS2 | PCSX2 | Good | AppImage/Flatpak, `-batch -fullscreen` launch, INI config with per-game settings. User BIOS required. Vulkan renderer available. |
| PS1 | DuckStation | Good | AppImage, CLI boot with fullscreen/batch flags, INI config with per-game settings. User BIOS required. Licence and distribution terms have changed over time, so check before integrating. |
| Xbox | xemu | Medium | AppImage/Flatpak, TOML config. Needs user-supplied MCPX boot ROM, flash BIOS and HDD image. Limited per-game config. OpenGL/Vulkan depending on version. |
| NDS | melonDS | Medium | AppImage/Flatpak, INI/TOML config. Optional user BIOS/firmware (has a built-in fallback). OpenGL, so our Vulkan overlay won't apply; use the emulator's OSD or a separate window. Dual-screen layout needs per-handheld presets. |
| GBA | mGBA | Good | AppImage/Flatpak, simple CLI and INI config. BIOS optional. Mostly OpenGL, same overlay caveat. |

Main cross-backend risks: emulators that are OpenGL-only (the Vulkan overlay doesn't apply
there), emulators without per-game config files (we'd generate a temporary config per
launch), and differing controller-profile formats (each backend owns its mapping writer,
and the app's own UI navigation stays fixed).
