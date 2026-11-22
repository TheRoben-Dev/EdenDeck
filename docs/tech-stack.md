# Tech stack decision

## Decision

**Frontend: Godot 4 (.NET / C#)**, Godot 4.5 or newer, with all non-UI logic in plain .NET
class libraries that don't reference Godot:

```
src/
  EdenDeck.Core/         library scan, settings model, Eden manager, mod manager, self-update
  EdenDeck.EdenAdapter/  ALL Eden-specific knowledge (paths, keys, flags, version detection)
  EdenDeck.App/          Godot project: screens, focus, input, theming, animation
tests/
  EdenDeck.EdenAdapter.Tests/  xUnit, runs against eden_adapter/schema/*.json + sample INIs
  EdenDeck.Core.Tests/
layer/                   C++20/CMake Vulkan layer (separate build, packaged alongside)
```

`dotnet test` runs the adapter and core tests in CI without Godot, a GPU or a display.

## Comparison

| Criterion | Avalonia 11 (C#) | Godot 4.5+ (C#) |
|---|---|---|
| Gamepad input | None built in. Needs SDL3 via P/Invoke (e.g. SDL3-CS) plus our own input-to-focus bridge. | Built in. Godot 4.5 moved desktop joypad input to SDL3, with SDL's controller database, so we get SDL3 device handling without our own binding. We can still P/Invoke SDL3 directly for gyro and Steam-specific queries if Godot's API falls short. |
| Focus navigation | XY focus for keyboard arrows exists (11.1+), but it's built for desktop forms. Predictable no-diagonal grid focus would be custom work. | First-class: `focus_neighbor_*`, `focus_next/previous`, `grab_focus`, focus modes per control. Explicit neighbours give exactly the "no diagonal jumps" behaviour the spec asks for. |
| 800p handheld ↔ 4K TV | DPI scaling is good, but it follows the desktop scale factor. A forced 10-foot scale needs a LayoutTransform. | `content_scale_mode = canvas_items` with a base resolution (e.g. 1280×800) scales all UI uniformly. Vector/MSDF fonts stay sharp at 4K. |
| Wayland / gamescope | No first-party Wayland backend in stable 11.x; runs through XWayland. Fine under gamescope (which is XWayland), less ideal on desktop Wayland. | Native Wayland (4.3+) and X11. Under gamescope it uses X11. Fullscreen and window handover to Eden is simpler with a game-engine window. |
| Animation / look | Good for transitions; shader effects are limited. | Tweens, AnimationPlayer and canvas shaders make short, smooth animations and the optional retro-terminal theme (scanlines, glow) cheap. |
| Text-heavy UI (settings, mod lists) | Stronger: rich controls, virtualization, data binding. | Adequate. `Tree`, `ItemList`, `ScrollContainer` with custom focus. We build a few reusable list/row components. |
| On-screen keyboard | Custom either way. | Custom either way (Godot's virtual keyboard is mobile only). |
| Packaging | `dotnet publish` self-contained. Flatpak via the .NET SDK extension is well trodden. AppImage via appimagetool. | Godot .NET export bundles the .NET runtime. Flatpak via the Godot BaseApp or our own manifest using the .NET SDK extension. AppImage via appimagetool. Slightly more work than Avalonia. |
| Battery on Steam Deck | Low (retained-mode, renders on change). | Must enable `low_processor_mode` and use the Compatibility (GL) renderer for UI, which keeps idle cost low. We also pause rendering while Eden runs. |
| Tests | Headless UI testing exists. | UI tests are harder. Mitigated by keeping logic out of the Godot project. |

**Why Godot:** the core of this app is controller-first navigation, 10-foot scaling and
handheld/TV behaviour. Godot gives us gamepad input (SDL3-backed), explicit focus
neighbours and resolution-independent scaling out of the box. With Avalonia all three would
be custom work. Avalonia is better for dense forms, but our settings are deliberately short.
The architecture risk (UI tests) is handled by keeping the adapter and core logic in
Godot-free libraries.

**Headless direct launch:** per-game "Add to Steam" shortcuts must start a game straight
from Game Mode with all of that game's settings applied (Eden overrides, controller
profile, upscaling/frame gen, layer env). So `EdenDeck.Core` exposes a launch pipeline that
also runs without the UI: `edendeck launch <TITLEID>` (a small .NET console entry point
shipped in the same package) writes the per-game Eden config, prepares env vars and execs
Eden. The Steam shortcut points at that command, so the UI and Game Mode launches share one
code path and can't drift apart.

**Input separation (product rule):** the app's own navigation is fixed (A = confirm,
B = back, mapped by the detected controller's layout and glyphs) and lives only in
`EdenDeck.App`. Game button remaps are only written to Eden input profiles by
`EdenDeck.EdenAdapter` and never change how the app UI responds.

## Self-updates and release automation

Requirement: the user downloads EdenDeck from GitHub once. After that, updates arrive in the
app, and all CI, PR and release work is automated.

**Release pipeline (GitHub Actions):**

1. Every PR: build the layer, run its unit tests and the lavapipe/Xvfb smoke test, run
   `dotnet test`, build the Godot export (from phase 2 on).
2. Merge to `main`: release-please (or an equivalent conventional-commit bot) keeps a
   release PR open with the changelog. Merging that PR tags `vX.Y.Z`.
3. Tag build: produce `EdenDeck-x86_64.AppImage` (+ `.zsync`), the Flatpak bundle and repo,
   `SHA256SUMS` and a minisign signature. Publish a GitHub Release.

**Update channels:**

* **AppImage:** the app polls `api.github.com/repos/TheRoben-Dev/EdenDeck/releases/latest`,
  downloads the new AppImage, checks `SHA256SUMS` and its minisign signature (public key
  built into the app), swaps the file in place and keeps the previous one for rollback. The
  embedded zsync update info also lets AppImageUpdate-style tools update it.
* **Flatpak:** CI publishes a signed OSTree repo to GitHub Pages. The one-time download is a
  `.flatpakref` from the release page, which adds our remote. After that, the app uses the
  Flatpak portal's `UpdateMonitor` to detect and apply updates in-app, and Discover/GNOME
  Software can update it too.

## Packaging note for phase 6 (to confirm then)

Running the Eden AppImage from inside a Flatpak sandbox is the hard part: there's no FUSE in
the sandbox, and the Vulkan layer, gamescope and host GPU drivers need to line up. The current
plan is to start Eden on the host with `flatpak-spawn --host` (permission
`--talk-name=org.freedesktop.Flatpak`), passing our layer env vars, plus filesystem access to
`~/.local/share/Steam` for `shortcuts.vdf` and the user's game folders. That rules out Flathub
but works for our own self-hosted remote. The AppImage build doesn't have this problem.
