# Emulator backends (design note)

Scanline starts with Eden only, but later backends are planned: PS3 (RPCS3), PS2 (PCSX2),
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
headless `scanline launch <id>`, quick menu, overlay, stats) stay backend-agnostic: the
game id becomes `<backend>:<title-id>`.

Each backend gets the same treatment as Eden got in phase 1: verify CLI, config and paths
against its source, keep versioned schema snapshots, and add tests against sample configs
from several releases.

## Feasibility per backend (initial assessment, not yet verified against source)

| System | Emulator | Fit | Notes |
|---|---|---|---|
| PS3 | RPCS3 | Good | AppImage with official update feed, `--no-gui` launch, YAML config with per-game custom configs. User imports their own PS3 firmware (PUP). Games are folders or ISOs; DLC/updates install as PKG. Vulkan, so the overlay layer applies. |
| PS2 | PCSX2 | Good | AppImage/Flatpak, `-batch -fullscreen` launch, INI config with per-game settings. User BIOS required. Vulkan renderer available. |
| PS1 | DuckStation | Good | AppImage, CLI boot with fullscreen/batch flags, INI config with per-game settings. User BIOS required. Licence CC BY-NC-ND 4.0: official builds may be passed on unchanged, so Scanline only downloads the official AppImage and never alters it. |
| Xbox | xemu | Medium | AppImage/Flatpak, TOML config. Needs user-supplied MCPX boot ROM, flash BIOS and HDD image. Limited per-game config. OpenGL/Vulkan depending on version. |
| NDS | melonDS | Medium | AppImage/Flatpak, INI/TOML config. Optional user BIOS/firmware (has a built-in fallback). OpenGL, so our Vulkan overlay won't apply; use the emulator's OSD or a separate window. Dual-screen layout needs per-handheld presets. |
| GBA | mGBA | Good | AppImage/Flatpak, simple CLI and INI config. BIOS optional. Mostly OpenGL, same overlay caveat. |

Main cross-backend risks: emulators that are OpenGL-only (the Vulkan overlay doesn't apply
there), emulators without per-game config files (we'd generate a temporary config per
launch), and differing controller-profile formats (each backend owns its mapping writer,
and the app's own UI navigation stays fixed).

## PCSX2 notes (as built, 0.9.0)

- A PS2 game is known by its disc serial (from SYSTEM.CNF's `BOOT2` line), which is its title id.
  PCSX2 also names per-game settings (`gamesettings/<SERIAL>_<CRC>.ini`), cheats and patches after
  the boot ELF's CRC (its 32-bit words XORed). Scanline reads both from .iso, .cue/.bin, .cso and
  .zso. A .chd needs codecs .NET lacks (LZMA, FLAC, Zstandard), so a DVD .chd is named from its
  header's raw-data SHA-1 (the original .iso's SHA-1, matched against Redump), and its CRC is
  learned from the log PCSX2 writes (`-logfile`) the first time it runs.
- `.iso` is shared with RPCS3: the library tries every backend that takes an extension.
- A new PCSX2.ini must carry `[UI] SettingsVersion = 1`, or PCSX2 treats it as broken and resets it.

## DuckStation notes (as built, 0.10.0)

- DuckStation's licence is CC BY-NC-ND 4.0. Scanline downloads the official AppImage unchanged from
  the `latest` release on stenzek/duckstation and writes only the player's own settings, locally.
  That release keeps one tag, so the version shown is the build number named in the release, or the
  AppImage's build date.
- A PS1 game is known by its serial, which DuckStation derives from SYSTEM.CNF's `BOOT` line (the
  executable's name without dots, `_` as `-`). Per-game settings are `gamesettings/<SERIAL>.ini`, so
  no checksum is needed. A disc with `BOOT2` is a PS2 disc and is left to PCSX2. Scanline reads the
  serial from .iso, .cue/.bin and .pbp (PARAM.SFO's `DISC_ID`). A .chd is named by its file name when
  that's a Redump name, or claimed when it sits in a psx/ps1 folder; its serial is then learned from
  DuckStation's own log (`Game: <title> | <serial> | HASH-…`). DuckStation has no log-file flag, so
  Scanline switches `[Logging] LogToFile` on for the run and puts it back afterwards.
- Data root: `$XDG_CONFIG_HOME/duckstation` when set, else `~/.local/share/duckstation`.
  The setup wizard is skipped with `[Main] SetupWizardIncomplete = false`.
- Patches and cheats come from DuckStation's community database (duckstation/chtdb), which it also
  ships inside itself. They're enabled with `[Patches] Enable` / `[Cheats] Enable` (+ `EnableCheats`)
  in the game's own ini. A patch with `OverrideAspectRatio` is switched on by DuckStation itself while
  widescreen rendering is on with that picture shape.
- Memory cards are per game by default (`memcards/<Title>_1.mcd`); a shared card is backed up but
  never deleted.

## xemu notes (as built, 0.11.0)

- xemu is GPLv2. Scanline downloads the official AppImage unchanged from xemu-project/xemu's latest
  release (`xemu-<ver>-x86_64.AppImage`, never the `-dbg` build), or uses the `app.xemu.xemu` Flatpak.
- An Xbox game is known by the title id in its `default.xbe` certificate (8 hex digits, `4D530004`).
  Scanline reads it offline from the XDVDFS file system (`MICROSOFT*XBOX*MEDIA` at sector 32), on its
  own (XISO) or after the video partition of a full Redump dump (offsets 0x18300000, 0xFD90000,
  0x2080000). The disc serial (`MS-004`: the title id's two letters and game number) names it from
  Redump's list; otherwise the XBE's own name is used. Non-Xbox `.iso` files return null, so PS2/PS3
  readers and xemu don't claim each other's discs.
- Config: `xemu.toml` in SDL's pref path (`$XDG_DATA_HOME/xemu/xemu`, else `~/.local/share/xemu/xemu`;
  the Flatpak's is under `~/.var/app/app.xemu.xemu/data`). Scanline edits it line by line, keeping
  everything else. `general.show_welcome = false` is required: the welcome window stops autostart.
  `general.updates.check` is switched off (Scanline updates xemu). A `port1 = 'keyboard'` binding is
  cleared when a gamepad is connected, so xemu auto-binds the first gamepad to port 1.
- Launch: `xemu [-config_path run.toml] [-full-screen] -dvd_path <image>`. xemu has no per-game
  settings, so a game's own settings live in Scanline (`<data>/xemu/games/<TITLEID>.json`) and are
  laid over a copy of xemu.toml (`<xemu data>/scanline/run.toml`) used for that run only. xemu saves
  its config to `-config_path` on exit, so changes made in xemu's window during such a run aren't
  kept; the player's own xemu.toml is untouched.
- Frame generation and the CRT layer need the Vulkan renderer. When neither the game nor the global
  config names a renderer (xemu's default is OpenGL), Scanline puts `display.renderer = 'VULKAN'` in
  the run config; an explicit OpenGL gets a warning note.
- System files are the player's own: the MCPX boot ROM (exactly 512 bytes) and flash BIOS (256 KB or
  1 MB) are copied into `<xemu data>/scanline/`; the hard disk (qcow2, magic `QFI\xfb`) is used where
  it is. The EEPROM is generated by xemu.
- Saves live inside the hard disk image, shared by every game, so `SaveData` is null and the app
  backs up the whole disk instead (`<data>/saves/XBOX-HDD/`, last 3 kept). xemu has no patch or cheat
  system; the app says so on the Emulators page and each Xbox game's card.

## melonDS notes (as built, 0.12.0)

- melonDS is GPLv3. Scanline downloads the official AppImage unchanged from melonDS-emu/melonDS's
  latest release. The release ships it inside a zip (`melonDS-<ver>-appimage-<arch>.zip`); Scanline
  keeps only the AppImage. The Flatpak is `net.kuribo64.melonDS`.
- A DS game is a .nds file, known by its header's game code as `NTR-AMCE` (the DS product code, so it
  can't clash with a GBA game's four-letter code); homebrew without a real code gets `NTR-H` and the
  header's CRC-32. The header's CRC-16 (0x15E) must match, which keeps other files out. Names come
  from No-Intro by game code, else the file name. The banner's 32x32 icon (4bpp tiles + BGR555
  palette) is drawn into a PNG for the cartridge label and Steam art.
- Config: `melonDS.toml` in `$XDG_CONFIG_HOME/melonDS` (else `~/.config/melonDS`; the Flatpak's is
  under `~/.var/app/net.kuribo64.melonDS/config`), edited line by line with `TomlFile` (shared with
  xemu). Window and input settings live under `Instance0` / `Instance0.Window0`.
- Launch: `melonDS [-f] <rom>`. melonDS has no per-game settings or config-path flag, so a game with
  its own settings is started with `XDG_CONFIG_HOME=<melonDS dir>/scanline-run` (Flatpak:
  `--env=`), where a copy of melonDS.toml with the game's settings sits. melonDS writes that copy back
  on exit, so changes made in its window during such a run aren't kept.
- melonDS renders in software or OpenGL (and shows the picture through Qt), so lsfg-vk frame
  generation and Scanline's Vulkan layer can't reach it. The app says so on the Emulators page and
  each DS game's card, and warns in the launch notes when a profile asks for them.
- Controller: melonDS maps nothing for a gamepad by default. Scanline writes `Instance0.Joystick.*`
  (raw SDL indices; a hat direction is `0x100 | hat << 4 | dir`, an axis `0x10000 | axis << 24 |
  dir << 20`, both can share one value) like a DS, plus right stick click = swap screens and left
  stick click = microphone, and only while the table is empty or still Scanline's (stamp file).
- BIOS/firmware are optional (melonDS has FreeBIOS). All three own files (4 KB ARM9, 16 KB ARM7,
  128-512 KB firmware) are needed together before `Emu.ExternalBIOSEnable` is switched on.
- Saves: `<rom>.sav` and save states `<rom>.ml1`-`.ml8` next to the ROM (or the configured paths).
- Cheats: libretro-database's `cht/Nintendo - Nintendo DS/<No-Intro name>.cht` (file name bundled per
  game code, fetched on request), written into melonDS's own `<rom>.mch` under a "libretro database"
  category (old-style `CAT name` header that every melonDS version reads). `Instance0.EnableCheats` is
  switched on before a game with codes on.

