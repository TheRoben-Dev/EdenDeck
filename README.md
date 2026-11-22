<div align="center">

# Scanline

**A controller-first frontend for the [Eden](https://eden-emu.dev) Nintendo Switch emulator on Linux.**

Formerly called Eden Deck. Built for SteamOS, Bazzite and other gaming distros. Made for handhelds like the Steam Deck and ROG Ally, and just as at home on a desktop or TV.

![Status](https://img.shields.io/badge/status-pre--alpha-orange)
![Platform](https://img.shields.io/badge/platform-Linux%20%7C%20SteamOS-1793d1)
![License](https://img.shields.io/badge/license-MIT-green)

</div>

> [!WARNING]
> **Scanline is in early development (pre-alpha).** Nothing here is usable yet. This README describes what is *planned*, and details may change as the work goes on. Watch the repo or check [Releases](https://github.com/TheRoben-Dev/Scanline/releases) for the first build.

---

## What is Scanline?

Scanline wraps the **unmodified upstream Eden emulator** in a fullscreen, gamepad-driven interface. You browse your library, tweak settings, manage mods, set up controllers and launch games without ever touching Eden's desktop Qt UI.

Eden stays a separate program. You can update it (Stable or Nightly) on its own schedule, and Scanline keeps working.

## Highlights (planned)

- 🎮 **Gamepad-only 10-foot UI** that reads well on an 800p handheld and a 4K TV
- 📦 **Eden manager** to install, update and roll back Eden Stable or Nightly
- 🔑 **Per-game key sets** so you can assign a different set of your own keys/firmware to games that need it
- 🗂️ **Game library** with per-game settings, mods, cheats and controller profiles
- 🕹️ **Controller mapping** written straight into Eden's own input profiles, with Steam Input handled properly
- 🚂 **Per-game "Add to Steam"** so each game launches from Game Mode with its own settings applied
- 🧩 **Mod and cheat manager** with conflict warnings and one-tap enable/disable
- ✨ **Upscaling and frame generation** using Eden's built-in FSR/SGSR, and Lossless Scaling frame gen (your own copy) through lsfg-vk, which comes included
- 📊 **In-game overlay and quick menu** through Scanline's own Vulkan layer
- 📺 **CRT effect for games**, per game, the same look as the app's own CRT effect (drawn by the layer, also over generated frames)
- 🎯 **Frame generation capped at 60 FPS**, because the goal is smooth 60 FPS Switch games and nothing more
- 🔄 **In-app self-updates** from this repo's GitHub Releases

---

## Features

### 1. Eden manager

- Finds an existing Eden install, or installs one for you from **Eden's official sources only**.
- **Stable / Nightly** channel picker, update checks, and the previous version kept around for **one-click rollback**.
- Checksums or signatures verified wherever Eden publishes them.
- **Guided import of your own** `prod.keys`, `title.keys` and firmware into the right Eden folders, with validation and clear error messages. Scanline never downloads or bundles keys, firmware or games.
- **Per-game key sets.** Import several sets of your own keys (and matching firmware), label them, and assign a specific set to any game. Handy when one set won't boot a newer or older title. Scanline puts the assigned set in place right before launch and restores your default afterwards, so Eden itself needs no changes.

### 2. Controller-first UI

- Fullscreen interface that works with **a gamepad alone**. Mouse, touch and keyboard work too, but they're secondary.
- SDL3 gamepad input with **predictable D-pad/stick focus** (no diagonal jumps), **shoulder buttons to switch tabs**, and an **on-screen keyboard** for every text field.
- **Button glyphs that match your controller**: Xbox, PlayStation, Nintendo or Steam Deck.
- **Fixed UI controls:** A confirms and B goes back in Scanline's menus, always (shown with your controller's glyphs). Button remaps you set up for games never change how the app itself is navigated.
- Controller-navigable file and folder pickers. No native GTK/Qt dialogs.
- Screens:
  - **Library grid:** scans your game folders and shows title, icon, title ID, version, update and DLC status.
  - **Game page:** launch, settings, key set, mods, cheats, controller profile, frame gen / upscaling, Add to Steam.
  - **Global settings**, **Eden manager**, **controller setup**.
- Modern dark theme by default, with short smooth animations. Optional themes: retro terminal, Nightgrid, 8-bit, 16-bit, Pocket LCD, CD era and **32-bit 3D** (real low-poly 3D cartridges with era rendering and interactive props).
- **Settings that matter, explained.** Only the Eden options worth changing (resolution scale, renderer, VSync, docked/handheld, shader cache, CPU accuracy and so on), each with a one-line explanation. Per-game overrides are written in Eden's own per-game config format.

### 3. Controllers and SteamOS integration

- **Mapping screen** that writes Eden input profiles for players 1–8, including motion/gyro and rumble.
- **Per-game profiles** and separate **handheld vs. docked** profiles.
- **Remaps apply to games only.** For example, you can swap ABXY so they match the Switch layout in-game while the Scanline menus keep A = confirm, B = back.
- **Steam Input aware:** detects Game Mode / Big Picture and maps against Steam's virtual gamepads instead of the physical controller, keeping player order stable across sleep and resume.
- **Gyro** on Steam Deck and ROG Ally through SDL / Steam Input, mapped to Eden motion.
- **Add to Steam:**
  - Add Scanline itself as a non-Steam shortcut, with artwork.
  - **Every game page has its own "Add to Steam" button.** It creates a per-game shortcut that launches that game straight from **SteamOS Game Mode** with all of its settings applied: frame generation, upscaling, controller profile and Eden overrides. No trip through the menus required.
  - Steam's `shortcuts.vdf` is only written while Steam is closed, or after a clear warning.
- A **recommended Steam Input template** for Scanline: menu navigation plus the quick-menu hotkey chord.
- **In-game quick menu:** hold a chord (for example View + Menu) to open an overlay where you can exit the game, save/load state where Eden supports it, toggle FSR or frame gen, change resolution scale and adjust the FPS limit. All of it works without modifying Eden.

### 4. Mod and cheat manager

- Manages mods in **Eden's per-title mod folders** (exefs, romfs, IPS, pchtxt, cheats).
- **Install from .zip / .7z / .rar:** detects the archive layout, fixes common wrong nesting, and asks for the title ID when it can't work it out.
- **Enable/disable** each mod using Eden's own disabled-add-ons setting, so files aren't shuffled around.
- **Load order view** and **conflict warnings** when two mods touch the same romfs path.
- Shows which **game version** each mod targets and warns when your installed update doesn't match.
- **Optional GameBanana browser** (off by default): browse and download mods for the selected game from GameBanana's public API. Only what you pick is downloaded, the source is always shown, and executables inside archives are never run.
- **Backup and restore** of mod sets and save data before anything risky.

### 5. Upscaling and frame generation

Phase 1 found that Eden already has the important pieces built in, and they work better
than a layer that only sees the finished frame: spatial **FSR 1** (both channels), **SGSR**
(Nightly), and **frame generation using Lossless Scaling's engine** (Nightly). These run
inside Eden's renderer, so they know the game's real resolution and can tell real frames from
repeats. Scanline drives them through Eden's own per-game settings.

**Upscaling.** You choose between:

| Mode | What it is |
| --- | --- |
| **Quality (Eden internal scaling)** | Eden renders at a higher resolution itself. Usually the best image. |
| **FSR (Eden built-in)** | EASU + RCAS on the game image, with adjustable sharpening. Cheap and stable. |
| **SGSR (Eden Nightly)** | Snapdragon Game Super Resolution, an alternative spatial upscaler. |

**Frame generation.** One engine per game:

- **Lossless Scaling frame gen through lsfg-vk**, on Eden Stable and Nightly (Eden's own frame gen only exists in its Android build). This needs your own copy of [Lossless Scaling](https://store.steampowered.com/app/993090/Lossless_Scaling/) from Steam: Scanline finds its `Lossless.dll` and never ships or downloads Lossless Scaling files. lsfg-vk itself (GPL-3.0) comes **included** with Scanline and is used when you don't have it installed; an lsfg-vk 2.x you installed yourself takes priority, and your own lsfg-vk config is never touched.
- **FSR 3.1 frame generation** in Scanline's own layer, as an experimental option for players without Lossless Scaling.

**The 60 FPS rule.** Frame generation exists here to get Switch games to a smooth 60 FPS, and **output is hard-capped at 60 FPS**:

- 30 FPS games get **2x** (30 → 60).
- Games that already run at 60 get **no frame generation**.
- No 3x/4x modes and no targets above 60.

**Scanline's Vulkan layer** (`VK_LAYER_SCANLINE_overlay`) is injected **only into Eden, only at launch**, through environment variables. Eden is never patched. Its main jobs:

- An optional **performance overlay**: real vs. generated FPS, frame-time graph, active upscale mode.
- The **in-game quick menu**.
- Per-game toggles and presets, plus a **global kill switch**.
- A graceful fallback with a clear message when something isn't supported.
- Targets **X11, Wayland and gamescope** (SteamOS Game Mode). AMD/RADV first (Steam Deck, ROG Ally / Z1 Extreme), then Intel and NVIDIA.

### 6. Updates and releases

- **In-app self-updates:** Scanline checks this repository's [GitHub Releases](https://github.com/TheRoben-Dev/Scanline/releases) and updates itself, with rollback if something goes wrong. Download it once, and future versions arrive inside the app.
- **Fully automated pipeline:** builds, tests, pull requests and releases all run through GitHub Actions.
- Packaged as **Flatpak** (primary, for SteamOS and Bazzite) and **AppImage**, with sandbox permissions set up for the Eden binary, your game folders and Steam's `shortcuts.vdf`.

---

## How it works

```
┌──────────────────────────┐      launch args, config files,      ┌─────────────────────┐
│        Scanline          │      mod/data folders, env vars      │   Eden (upstream,   │
│  controller-first UI     │ ───────────────────────────────────▶ │   unmodified)       │
│                          │                                      │   AppImage / pkg    │
│   └─ Eden backend        │                                      └──────────┬──────────┘
│      (all Eden-specific  │                                                 │ Vulkan
│       knowledge lives    │                                      ┌──────────▼──────────┐
│       here, versioned)   │                                      │ VK_LAYER_SCANLINE_  │
└──────────────────────────┘                                      │ overlay             │
                                                                  └─────────────────────┘
```

**The core rule: Scanline does not fork Eden.** It talks to Eden only through command-line arguments, Eden's config files, its mod and data folders, environment variables and a Vulkan layer.

Everything Eden-specific (config keys, paths, CLI flags) lives in a single versioned module, the Eden backend (`EdenDeck.Backends.Eden`, with version snapshots in `eden_adapter/schema/`). It detects the installed Eden version and tolerates config keys it doesn't know. When Eden changes, that one module is the only thing that needs updating, and it's covered by tests against sample configs from several Eden versions. Every path, key and flag is verified against the current Eden source and documented in [`docs/eden-integration.md`](docs/eden-integration.md).

## Tech stack

| Part | Choice |
| --- | --- |
| Frontend UI | **Godot 4 (C#/.NET)**, with all logic in Godot-free .NET libraries. Why: [`docs/tech-stack.md`](docs/tech-stack.md) |
| Gamepad input | SDL3 (through Godot's joypad input) |
| Vulkan layer | C++20, CMake (overlay, quick menu, optional FSR 3.1 frame gen) |
| Packaging | Flatpak (primary), AppImage |
| CI / releases | GitHub Actions |

---

## Roadmap

Development happens in phases. Each phase lands as a pull request that merges once CI passes. Every phase records its assumptions, risks and how it was tested on real hardware.

- [x] **Phase 1 — Research spike** ([report](docs/phase1-report.md))
  Verify Eden's CLI flags, config format and keys, data and mod paths, per-game config and disabled add-ons on current Stable and Nightly. Write [`docs/eden-integration.md`](docs/eden-integration.md). Prototype a minimal Vulkan layer that hooks present in Eden under X11, Wayland and gamescope. Settle the frontend tech stack.
- [x] **Phase 2 — MVP** ([report](docs/phase2-report.md))
  Eden manager (including per-game key sets), library scan, controller-navigable UI, launching games fullscreen, basic global settings. CI pipeline in place.
- [ ] **Phase 3 — Controllers and Steam**
  Controller mapping (games only; app nav stays fixed), Steam Input handling, gyro, Add to Steam for the app and per-game Game Mode shortcuts with settings applied.
- [ ] **Phase 4 — Mods and cheats**
  Mod and cheat manager, archive installer, conflict detection, version checks, backups, optional GameBanana browser.
- [ ] **Phase 5 — Upscaling and frame generation**
  Eden's built-in FSR/SGSR upscaling, Lossless Scaling frame generation through lsfg-vk (Eden's own frame gen is Android-only; 60 FPS cap), lsfg-vk bundled in the app (done in 0.4.2), then the in-game overlay and quick menu.
- [ ] **Phase 6 — Packaging and updates**
  Flatpak and AppImage, in-app self-updates from GitHub Releases, rollback, user docs.

### Backlog

Planned after the phases above, unless one fits naturally earlier:

- Save management (backups before launch, restore, export)
- Favorites and collections
- Per-game performance presets
- Shader cache tools (size per game, clear and rebuild)
- Storage view with actions (what uses space, clean up)
- Haptic and sound feedback in the app
- Automatic docked/handheld switch when a TV is connected

### Future backends

Scanline's core uses a generic emulator-backend interface ([`docs/backends.md`](docs/backends.md)), and Eden is the first implementation. More are planned later, each as its own module: **PS3** (RPCS3), **PS2** (PCSX2), **PS1** (DuckStation), **Xbox** (xemu), **NDS** (melonDS) and **GBA** (mGBA). The same rules apply: upstream emulators stay unmodified, and you use only your own BIOS and firmware.

## Repository layout

| Path | What |
| --- | --- |
| [`docs/eden-integration.md`](docs/eden-integration.md) | Verified Eden CLI flags, paths, config keys, per-game config, mods, disabled add-ons |
| [`docs/tech-stack.md`](docs/tech-stack.md) | Frontend stack decision, self-update and release plan |
| [`docs/backends.md`](docs/backends.md) | Emulator backend interface and future backends |
| [`docs/phase1-report.md`](docs/phase1-report.md) | Phase 1 findings, assumptions, risks, verification status |
| `eden_adapter/schema/` | Eden settings and enum snapshots per Eden version |
| `tools/extract_eden_settings.py` | Regenerates those snapshots from an Eden source checkout |
| [`docs/phase2-report.md`](docs/phase2-report.md) | Phase 2 (MVP) contents, verification status, known gaps |
| `src/EdenDeck.Core/` | Backend interface, library grouping, launch plans, per-game profiles, frame gen policy |
| `src/EdenDeck.Backends.Eden/` | Everything Eden-specific |
| `src/EdenDeck.Host/` | Composition root shared by the app and the CLI |
| `src/EdenDeck.Cli/` | `scanline`, the headless launcher used by Steam shortcuts |
| `src/EdenDeck.App/` | Godot 4 .NET app (UI built in code) |
| `tests/` | xUnit tests with sample Eden configs |
| `tools/smoke/` | Scripted UI smoke test with screenshots |
| `layer/` | `VK_LAYER_SCANLINE_overlay` Vulkan layer and its hardware test procedure |

## Install

On SteamOS, Bazzite or any x86_64 Linux, open a terminal in Desktop Mode and run:

```sh
curl -fsSL https://github.com/TheRoben-Dev/Scanline/releases/latest/download/install.sh | bash
```

This installs to `~/Applications/Scanline` (no root), adds a menu entry, and puts the `scanline` launcher on your PATH.

**Updates (0.2.0 and later) install from inside the app.** Scanline checks GitHub Releases at startup and offers *Update and restart*; you can also check from *Settings → Updates*. The download is checksum-verified, your games, keys and settings are never touched, and the previous version is kept so you can roll back from the same screen. The version you're running shows in the top-right corner. Re-running the install command also works.

**Use a controller by starting Scanline from Steam**, in both Desktop and Game Mode: in Steam, choose *Games → Add a Non-Steam Game to My Library*, then tick Scanline. If you start it outside Steam while Steam is running, Steam's desktop controller layout turns your pad into keyboard and mouse input. Scanline still understands the arrow keys, Enter, Esc, and Tab/Shift+Tab for switching tabs, but gamepad buttons won't work.

## Building

You need the .NET 8 SDK, and for the app, the Godot 4.7.2 .NET editor.

```sh
dotnet test EdenDeck.sln                      # backend, core and CLI tests
dotnet run --project src/EdenDeck.Cli -- help # headless launcher
cd src/EdenDeck.App && godot --path . -- --windowed   # run the app from source
```

CI builds the Linux export on every pull request and uploads it as the `Scanline-linux-x86_64` artifact, together with screenshots from the UI smoke test.

---

## Legal and project stance

- **No piracy features.** Scanline does not download, bundle or link to keys, firmware or games. You must dump these from hardware you own.
- **Eden is not modified.** If an upstream change is ever genuinely needed, it will be proposed to the Eden project as a pull request, not forked.
- **Third-party software stays yours.** Lossless Scaling is a paid Steam product you buy and install yourself. Scanline only detects it.
- Scanline is an independent project. It is not affiliated with or endorsed by the Eden team, Nintendo, Valve, AMD or Lossless Scaling's developer. "Nintendo Switch" is a trademark of Nintendo.

## License

[MIT](LICENSE) © 2026 TheRoben
