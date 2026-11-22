# Phase 2 report: MVP

Status: done. Nothing has been tested on real hardware or with a real Eden install yet. Each
claim below says how it was checked.

## What's in it

| Part | Where |
|---|---|
| Generic emulator backend interface (`IEmulatorBackend`, install, library, settings, launch, system files) | `src/EdenDeck.Core/Backends/` |
| Eden backend: paths, INI read/write, version-aware enum translation, per-game config, disabled add-ons, NCA/XCI/NSP title ID reader, release feed, install/rollback, key sets | `src/EdenDeck.Backends.Eden/` |
| Shared composition root (used by the UI and the CLI) | `src/EdenDeck.Host/` |
| Headless launcher `edendeck` (what per-game Steam shortcuts will call in phase 3) | `src/EdenDeck.Cli/` |
| Godot 4.7 .NET app: library grid, game page, Eden manager, settings, file picker | `src/EdenDeck.App/` |
| Tests: 58 xUnit tests (backend + core) against hand-written Stable and Nightly config fixtures | `tests/` |
| UI smoke test: scripted controller input, a screenshot per step, launches a fake Eden | `tools/smoke/run-ui-smoke.sh`, `--smoke` in the app |
| CI: .NET build/test, Godot import + smoke test + Linux export, artifact upload | `.github/workflows/ci.yml` |

### App behaviour

- **Controller first.** D-pad and left stick move focus (dominant axis only, delayed
  auto-repeat). Every screen wires its focus neighbours explicitly, so the grid never
  jumps diagonally. LB/RB switch the Library, Eden and Settings tabs. B goes back.
- **A confirms, B goes back, by label.** On Xbox-style pads (Deck, Ally, Steam Virtual
  Gamepad) that is south/east. On Nintendo pads the app swaps them, so the button printed
  "A" still confirms. Game remaps live in Eden's own input config and never affect the app.
  Hints at the bottom show the right glyphs for the controller in use (Xbox, PlayStation,
  Nintendo, keyboard).
- **No popups or text fields.** Settings are "‹ value ›" rows changed with left/right.
  Key set names come from the folder name, so no typing is needed. The file picker
  lists home, common game folders, SD cards and USB drives (`/run/media`, `/media`, `/mnt`).
- **Game page.** Play, which key set to use, frame gen (Off or Eden built-in, always 2x
  and capped at 60, none for native-60 titles), the game's native frame rate, and per-game
  overrides for the 13 curated Eden settings ("Global (value)" removes the override).
- **While a game runs** the app ignores controller input entirely, so presses meant for
  the game can't trigger anything behind it, and it returns to the same tile afterwards.
- 1280x800 base resolution scaled to the screen, `low_processor_mode` on, dark theme.

## Verified

| Claim | How |
|---|---|
| Config read/write in Eden's format, enum translation per version, per-game overrides, disabled add-ons | unit tests on Stable v0.2.1 and Nightly 467ab644 fixtures |
| Key set farm (symlinked portable `user/` with the set's keys and firmware, saves shared) | unit tests on a temp filesystem |
| Launch plan (`-f -g <path>`, cwd, env, frame gen keys written only on Nightly, cleanup on crash) | unit tests |
| App starts, every screen renders, focus moves as expected, B/LB/RB work | `--smoke` under Xvfb + llvmpipe, editor and exported build, screenshots reviewed |
| App launches Eden with `-f -g <game>` and comes back to the library | `--smoke` with a fake Eden script that records its arguments |
| Exported Linux build is self-contained (bundles .NET) | export run locally and in CI |
| Godot 4.7.2 editor and templates are the official files | SHA-512 checked against Godot's `SHA512-SUMS.txt` in CI |

## Not verified yet (needs a Deck/Ally or desktop with Eden)

- A real game booting through the app or `edendeck launch`, on Stable and Nightly.
- Gamescope focus handover when Eden starts and exits in Game Mode.
- SDL3's button order on real Nintendo controllers. The A/B swap assumes positional
  reporting, which is what SDL3 does by default.
- Eden's real release asset names for Linux (the feed parser picks the first `.AppImage`).
- Frame gen end to end (needs Nightly plus the user's own Lossless Scaling files).

## Known gaps (planned)

- Tiles show the title from the file name and a colour, not the game's icon or real name.
  Reading the NACP from the control NCA needs the RomFS layer and is next.
- No on-screen keyboard yet. Nothing in phase 2 needs typing.
- Flatpak Eden installs aren't detected. Only AppImages are.
- Add to Steam and per-game Game Mode shortcuts land in phase 3. The `edendeck launch`
  command they will call is already in this phase.
- lsfg-vk options come in phase 5.

## Risks

| Risk | Effect | Mitigation |
|---|---|---|
| Eden changes an enum order or moves a key | settings written wrong | translation by name, schema snapshots per version, CI drift report for Nightly |
| Eden is running while settings are written | Eden overwrites them on exit | writes are refused while an `eden` process exists, with a message |
| A key set's firmware differs from the system firmware | save data or system titles misbehave | the farm only swaps keys and `registered` firmware; saves and everything else stay shared |
| Godot drops joypad input when unfocused in some compositors | none for us | the app already ignores input while a game runs |
