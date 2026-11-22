<div align="center">

# EdenDeck

**A controller-first frontend for the [Eden](https://eden-emu.dev) Nintendo Switch emulator on Linux.**

Built for SteamOS, Bazzite and other gaming distros. Made for handhelds like the Steam Deck and ROG Ally, and just as at home on a desktop or TV.

![Status](https://img.shields.io/badge/status-pre--alpha-orange)
![Platform](https://img.shields.io/badge/platform-Linux%20%7C%20SteamOS-1793d1)
![License](https://img.shields.io/badge/license-MIT-green)

</div>

> [!WARNING]
> **EdenDeck is in early development (pre-alpha).** Nothing here is usable yet. This README describes what is *planned*, and details may change as the work goes on. Watch the repo or check [Releases](https://github.com/TheRoben-Dev/EdenDeck/releases) for the first build.

---

## What is EdenDeck?

EdenDeck wraps the **unmodified upstream Eden emulator** in a fullscreen, gamepad-driven interface. You browse your library, tweak settings, manage mods, set up controllers and launch games without ever touching Eden's desktop Qt UI.

Eden stays a separate program. You can update it (Stable or Nightly) on its own schedule, and EdenDeck keeps working.

## Highlights (planned)

- 🎮 **Gamepad-only 10-foot UI** that reads well on an 800p handheld and a 4K TV
- 📦 **Eden manager** to install, update and roll back Eden Stable or Nightly
- 🔑 **Per-game key sets** so you can assign a different set of your own keys/firmware to games that need it
- 🗂️ **Game library** with per-game settings, mods, cheats and controller profiles
- 🕹️ **Controller mapping** written straight into Eden's own input profiles, with Steam Input handled properly
- 🚂 **Per-game "Add to Steam"** so each game launches from Game Mode with its own settings applied
- 🧩 **Mod and cheat manager** with conflict warnings and one-tap enable/disable
- ✨ **FSR upscaling and frame generation** through a custom Vulkan layer, plus optional **lsfg-vk** (Lossless Scaling) support
- 🎯 **Frame generation capped at 60 FPS**, because the goal is smooth 60 FPS Switch games and nothing more
- 🔄 **In-app self-updates** from this repo's GitHub Releases

---

## Features

### 1. Eden manager

- Finds an existing Eden install, or installs one for you from **Eden's official sources only**.
- **Stable / Nightly** channel picker, update checks, and the previous version kept around for **one-click rollback**.
- Checksums or signatures verified wherever Eden publishes them.
- **Guided import of your own** `prod.keys`, `title.keys` and firmware into the right Eden folders, with validation and clear error messages. EdenDeck never downloads or bundles keys, firmware or games.
- **Per-game key sets.** Import several sets of your own keys (and matching firmware), label them, and assign a specific set to any game. Handy when one set won't boot a newer or older title. EdenDeck puts the assigned set in place right before launch and restores your default afterwards, so Eden itself needs no changes.

### 2. Controller-first UI

- Fullscreen interface that works with **a gamepad alone**. Mouse, touch and keyboard work too, but they're secondary.
- SDL3 gamepad input with **predictable D-pad/stick focus** (no diagonal jumps), **shoulder buttons to switch tabs**, and an **on-screen keyboard** for every text field.
- **Button glyphs that match your controller**: Xbox, PlayStation, Nintendo or Steam Deck.
- **Fixed UI controls:** A confirms and B goes back in EdenDeck's menus, always (shown with your controller's glyphs). Button remaps you set up for games never change how the app itself is navigated.
- Controller-navigable file and folder pickers. No native GTK/Qt dialogs.
- Screens:
  - **Library grid:** scans your game folders and shows title, icon, title ID, version, update and DLC status.
  - **Game page:** launch, settings, key set, mods, cheats, controller profile, frame gen / upscaling, Add to Steam.
  - **Global settings**, **Eden manager**, **controller setup**.
- Modern dark theme by default, with short smooth animations. An optional **retro-terminal theme**.
- **Settings that matter, explained.** Only the Eden options worth changing (resolution scale, renderer, VSync, docked/handheld, shader cache, CPU accuracy and so on), each with a one-line explanation. Per-game overrides are written in Eden's own per-game config format.

### 3. Controllers and SteamOS integration

- **Mapping screen** that writes Eden input profiles for players 1–8, including motion/gyro and rumble.
- **Per-game profiles** and separate **handheld vs. docked** profiles.
- **Remaps apply to games only.** For example, you can swap ABXY so they match the Switch layout in-game while the EdenDeck menus keep A = confirm, B = back.
- **Steam Input aware:** detects Game Mode / Big Picture and maps against Steam's virtual gamepads instead of the physical controller, keeping player order stable across sleep and resume.
- **Gyro** on Steam Deck and ROG Ally through SDL / Steam Input, mapped to Eden motion.
- **Add to Steam:**
  - Add EdenDeck itself as a non-Steam shortcut, with artwork.
  - **Every game page has its own "Add to Steam" button.** It creates a per-game shortcut that launches that game straight from **SteamOS Game Mode** with all of its settings applied: frame generation, upscaling, controller profile and Eden overrides. No trip through the menus required.
  - Steam's `shortcuts.vdf` is only written while Steam is closed, or after a clear warning.
- A **recommended Steam Input template** for EdenDeck: menu navigation plus the quick-menu hotkey chord.
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

EdenDeck ships its own Vulkan layer, `VK_LAYER_EDENDECK_fsr`, built on the AMD FidelityFX SDK (FSR 3.1). It is injected **only into Eden, only at launch**, through environment variables. Eden itself is never patched.

**Being honest about the limits:** a layer like this only sees the final frame. It gets no game motion vectors, no reliable depth buffer and no HUD-free image. The options below are presented with that in mind.

**Upscaling.** You choose between:

| Mode | What it is |
| --- | --- |
| **Quality (Eden internal scaling)** | Eden renders at a higher resolution itself. Usually the best image. |
| **FSR spatial** | EASU + RCAS sharpening on the final frame. Cheap and stable. |
| **FSR 3 temporal (experimental)** | Temporal upscaling with estimated motion. Can look better or worse depending on the game. |

What ships as the default will be whatever actually looks best in testing.

**Frame generation.** Two engines, and you pick **one per game** (they're mutually exclusive):

- **FSR 3.1 frame generation** (built in), driven by optical flow.
- **lsfg-vk** (Lossless Scaling frame generation on Linux). This needs your own copy of [Lossless Scaling](https://store.steampowered.com/app/993090/Lossless_Scaling/) installed through Steam; EdenDeck detects it and exposes per-game options, and never ships or downloads Lossless Scaling files.

**The 60 FPS rule.** Frame generation exists here to get Switch games to a smooth 60 FPS, and **output is hard-capped at 60 FPS**:

- 30 FPS games get **2x** (30 → 60).
- Games that already run at 60 get **no frame generation**.
- No 3x/4x modes and no targets above 60.

**Frame pacing and quality work:**

- Duplicate-present detection so only real frames are interpolated, which matters for 30 FPS titles.
- Scene-cut and menu detection that pauses interpolation where it would smear.
- Per-game HUD masks and confidence thresholds to reduce ghosting on static UI.
- A clear latency note in the UI.

**Also:**

- Per-game toggles and presets, plus a **global kill switch**.
- A graceful fallback with a clear message when your GPU lacks a required Vulkan feature.
- An optional **performance overlay**: real vs. generated FPS, frame-time graph, active upscale mode.
- Targets **X11, Wayland and gamescope** (SteamOS Game Mode). AMD/RADV first (Steam Deck, ROG Ally / Z1 Extreme), then Intel and NVIDIA.

### 6. Updates and releases

- **In-app self-updates:** EdenDeck checks this repository's [GitHub Releases](https://github.com/TheRoben-Dev/EdenDeck/releases) and updates itself, with rollback if something goes wrong. Download it once, and future versions arrive inside the app.
- **Fully automated pipeline:** builds, tests, pull requests and releases all run through GitHub Actions.
- Packaged as **Flatpak** (primary, for SteamOS and Bazzite) and **AppImage**, with sandbox permissions set up for the Eden binary, your game folders and Steam's `shortcuts.vdf`.

---

## How it works

```
┌──────────────────────────┐      launch args, config files,      ┌─────────────────────┐
│        EdenDeck          │      mod/data folders, env vars      │   Eden (upstream,   │
│  controller-first UI     │ ───────────────────────────────────▶ │   unmodified)       │
│                          │                                      │   AppImage / pkg    │
│   └─ eden_adapter        │                                      └──────────┬──────────┘
│      (all Eden-specific  │                                                 │ Vulkan
│       knowledge lives    │                                      ┌──────────▼──────────┐
│       here, versioned)   │                                      │ VK_LAYER_EDENDECK_  │
└──────────────────────────┘                                      │ fsr  /  lsfg-vk     │
                                                                  └─────────────────────┘
```

**The core rule: EdenDeck does not fork Eden.** It talks to Eden only through command-line arguments, Eden's config files, its mod and data folders, environment variables and a Vulkan layer.

Everything Eden-specific (config keys, paths, CLI flags) lives in a single versioned module, `eden_adapter`. It detects the installed Eden version and tolerates config keys it doesn't know. When Eden changes, that one module is the only thing that needs updating, and it's covered by tests against sample configs from several Eden versions. Every path, key and flag is verified against the current Eden source and documented in [`docs/eden-integration.md`](docs/eden-integration.md).

## Tech stack

| Part | Choice |
| --- | --- |
| Frontend UI | **To be decided:** C# with Avalonia or Godot 4 (C#). The comparison covers gamepad focus handling, 800p/4K scaling, Wayland/gamescope behavior and Flatpak/AppImage packaging. |
| Gamepad input | SDL3 |
| Vulkan layer | C++20, CMake, AMD FidelityFX SDK (FSR 3.1) |
| Packaging | Flatpak (primary), AppImage |
| CI / releases | GitHub Actions |

---

## Roadmap

Development happens in phases, and each one ends with a review before the next starts. Every phase records its assumptions, risks and how it was tested on real hardware.

- [ ] **Phase 1 — Research spike**
  Verify Eden's CLI flags, config format and keys, data and mod paths, per-game config and disabled add-ons on current Stable and Nightly. Write [`docs/eden-integration.md`](docs/eden-integration.md). Prototype a minimal Vulkan layer that hooks present in Eden under X11, Wayland and gamescope. Settle the frontend tech stack.
- [ ] **Phase 2 — MVP**
  Eden manager (including per-game key sets), library scan, controller-navigable UI, launching games fullscreen, basic global settings. CI pipeline in place.
- [ ] **Phase 3 — Controllers and Steam**
  Controller mapping (games only; app nav stays fixed), Steam Input handling, gyro, Add to Steam for the app and per-game Game Mode shortcuts with settings applied.
- [ ] **Phase 4 — Mods and cheats**
  Mod and cheat manager, archive installer, conflict detection, version checks, backups, optional GameBanana browser.
- [ ] **Phase 5 — Upscaling and frame generation**
  FSR spatial upscaling, then FSR 3.1 frame generation (60 FPS cap), lsfg-vk integration, then the in-game overlay and quick menu.
- [ ] **Phase 6 — Packaging and updates**
  Flatpak and AppImage, in-app self-updates from GitHub Releases, rollback, user docs.

---

## Legal and project stance

- **No piracy features.** EdenDeck does not download, bundle or link to keys, firmware or games. You must dump these from hardware you own.
- **Eden is not modified.** If an upstream change is ever genuinely needed, it will be proposed to the Eden project as a pull request, not forked.
- **Third-party software stays yours.** Lossless Scaling is a paid Steam product you buy and install yourself. EdenDeck only detects it.
- EdenDeck is an independent project. It is not affiliated with or endorsed by the Eden team, Nintendo, Valve or AMD. "Nintendo Switch" is a trademark of Nintendo.

## License

[MIT](LICENSE) © 2026 TheRoben
