# VK_LAYER_SCANLINE_overlay

Phase 1 prototype of the Scanline Vulkan layer. It's injected only into the Eden process
through environment variables. Eden is not modified.

**Current scope:** loader chaining, swapchain tracking, a `vkQueuePresentKHR` hook with
frame-time stats (log + JSON file), and the optional in-game **CRT filter**. No FSR, frame gen or
overlay yet.

## CRT filter

`src/crt_pass.hpp`, shaders in `shaders/` (compiled into `src/shaders.inc` by
`scripts/gen-shaders.sh`; the output is checked in and CI checks it is current). Right before each
present it copies the finished frame and draws it back through a port of the app's own CRT shader
(`src/EdenDeck.App/Scripts/Ui/Scanlines.cs`), with the same Subtle/Medium/Strong numbers. The submit
waits on the app's present semaphores and the present waits on ours.

* **Order with lsfg-vk:** implicit layers (lsfg-vk) sit above explicitly enabled ones, so lsfg-vk's
  generated frames are presented through this layer and get filtered too. When Scanline enables
  lsfg-vk explicitly, it must come before `VK_LAYER_SCANLINE_overlay` in `VK_INSTANCE_LAYERS`.
* **Graceful:** if the swapchain can't be read back (no TRANSFER_SRC usage), the present queue has no
  graphics, or anything fails to allocate, the filter turns itself off for that swapchain with one
  log line. If the layer doesn't load at all, the game simply runs unfiltered.
* **Cost while off:** none per frame beyond a `stat()` of the control file twice a second.

## Build

```bash
sudo apt install libvulkan-dev   # or: pacman -S vulkan-headers / dnf install vulkan-headers
cmake -S layer -B layer/build -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build layer/build
ctest --test-dir layer/build --output-on-failure
```

Produces `layer/build/libVkLayer_SCANLINE_overlay.so` and two manifests:

* `layer/build/VkLayer_SCANLINE_overlay.json` is **explicit**. Use it with `VK_ADD_LAYER_PATH` +
  `VK_INSTANCE_LAYERS`. This is what Scanline uses.
* `layer/build/implicit/VkLayer_SCANLINE_overlay.json` is **implicit**. When installed into
  `~/.local/share/vulkan/implicit_layer.d/` it only activates with `ENABLE_SCANLINE_LAYER=1`
  and never with `DISABLE_SCANLINE_LAYER=1`. This is the fallback if a loader ignores
  `VK_ADD_LAYER_PATH`. Edit `library_path` to an absolute path when installing.

## Environment variables

| Var | Effect |
|---|---|
| `VK_ADD_LAYER_PATH=<dir>` | where the loader finds the explicit manifest (loader 1.3.234+) |
| `VK_INSTANCE_LAYERS=VK_LAYER_SCANLINE_overlay` | enable the explicit layer |
| `SCANLINE_LAYER_LOG=<path>` | log file (default stderr) |
| `SCANLINE_LAYER_STATS=<path>` | JSON stats, rewritten atomically every report interval |
| `SCANLINE_LAYER_REPORT_MS=<ms>` | report interval, default 2000 |
| `SCANLINE_LAYER_NO_USAGE_PATCH=1` | don't add TRANSFER_SRC/DST to swapchain image usage |
| `SCANLINE_CRT=0..3` | CRT filter at start: off, Subtle, Medium, Strong |
| `SCANLINE_CRT_MOTION=0` | still picture: no wobble, rolling band or flicker (app's Animations off) |
| `SCANLINE_CRT_CONTROL=<path>` | file with `level=N` / `motion=0\|1` lines, followed while the game runs |
| `SCANLINE_LAYER_DUMP=<path.ppm>` | test hook: write the presented frame N as a PPM (`SCANLINE_LAYER_DUMP_FRAME`, default 60) |
| `DISABLE_SCANLINE_LAYER=1` | kill switch: pure pass-through (explicit) / not loaded (implicit) |

## What has been verified, and where

Verified in CI-like conditions (Ubuntu 24.04, Vulkan loader 1.3.275, Mesa 25.2 **llvmpipe**,
Xvfb, i.e. **X11 only, software rendering**) with `vkcube`:

| Check | Result |
|---|---|
| Explicit layer via `VK_ADD_LAYER_PATH` + `VK_INSTANCE_LAYERS` | loads, instance + device hooked |
| `vkCreateSwapchainKHR` hook, usage patch | 500×500 FIFO, usage 0x10 → 0x13, creation succeeds |
| `vkQueuePresentKHR` hook | all 300/300 presents counted, stats JSON written |
| `DISABLE_SCANLINE_LAYER=1` with explicit manifest | loads, no swapchain/present hooks |
| Implicit manifest: enable env / no env / enable+disable | active / inactive / inactive |
| Khronos validation layer above and below ours | no validation errors |
| Unit tests (`frame_stats_test`) | pass |

FPS numbers from Xvfb are meaningless (Xvfb has no vblank, so FIFO doesn't throttle).

**Not verified:** anything with Eden, any real GPU, Wayland, gamescope, Steam Deck, ROG Ally.

## Manual test procedure (real hardware)

Prereqs: the built layer, an Eden AppImage (Stable and Nightly), your own dumped game.
Repeat each step for **Stable and Nightly**.

### 1. Desktop X11 session

```bash
echo $XDG_SESSION_TYPE                     # expect x11
layer/scripts/run-eden-with-layer.sh ~/Applications/Eden.AppImage -f -g ~/Games/game.nsp
```

Pass criteria, from the log path the script prints:

* `vkCreateInstance ok: process=eden ...` (if `process=` is something else, note it: the
  AppImage may wrap the binary)
* `vkCreateDevice ok: gpu="AMD Custom GPU 0405 (RADV VANGOGH)"` (or your GPU), `VK_KHR_swapchain=yes`
* `vkCreateSwapchainKHR ok: <your resolution> ... usage=... (app asked ...)`
* `present:` lines every 2 s with fps close to the game's real frame rate (30 or 60),
  and `cat $XDG_RUNTIME_DIR/edendeck/stats.json` updating
* The game renders normally, with no new crashes or flicker. Compare with a run where
  `DISABLE_SCANLINE_LAYER=1` is set.

If no `[scanline-layer]` lines appear at all:

1. `VK_LOADER_DEBUG=layer` to see whether the loader found the manifest.
2. The AppImage may bundle an old `libvulkan.so.1` that ignores `VK_ADD_LAYER_PATH`. Check with
   `./Eden.AppImage --appimage-extract` then `find squashfs-root -name 'libvulkan*'`. If so,
   install the implicit manifest (above) and use `ENABLE_SCANLINE_LAYER=1`.
3. If Eden runs on OpenGL, the layer isn't involved. Set the renderer to Vulkan.

### 2. Desktop Wayland session (KDE Plasma / GNOME)

Same command. Note whether Eden runs native Wayland or XWayland (`xlsclients | grep -i eden`
lists it if XWayland). Record the swapchain present mode reported. Pass criteria are the same.

### 3. gamescope (nested, desktop)

```bash
gamescope -W 1280 -H 800 -r 60 -- layer/scripts/run-eden-with-layer.sh ~/Applications/Eden.AppImage -f -g ~/Games/game.nsp
```

The log should show a swapchain at gamescope's resolution, with the fps capped near 60.

### 4. Steam Deck / ROG Ally Game Mode (gamescope session)

1. Desktop Mode: copy the repo build to `~/edendeck/` (or build there with distrobox).
2. Steam → Add a Non-Steam Game → pick `run-eden-with-layer.sh`. Launch options:
   `~/Applications/Eden.AppImage -f -g /path/to/game.nsp`
3. Switch to Game Mode, launch it, play 1-2 minutes, exit.
4. Back in Desktop Mode, read the newest `/run/user/1000/edendeck/layer-*-gamescope.log`.
   The `session=` tag comes from `GAMESCOPE_WAYLAND_DISPLAY`, so if it says something else, note it.
5. Check that frame limiter changes in the Steam QAM show up in the logged fps, and that
   suspend/resume mid-game doesn't crash (look for a new `vkCreateSwapchainKHR` after resume).

### What to report back

For each (channel × session × device): the log file, Eden version string, GPU/driver line,
pass/fail on each criterion above, and anything visibly different with vs. without the layer.
