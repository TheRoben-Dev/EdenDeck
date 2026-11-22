# Scanline changelog

Written for players. The app shows this under Settings → Updates → Changelog, and shows
"What's new" once after each update. Each release's GitHub notes start with its section.

## 0.4.1
- New: **CRT effect for games.** Each game's card has a CRT effect setting with the same levels as the app's (Off, Subtle, Medium, Strong): scanlines, a wobbling picture, a rolling band, colour fringes, tube curve and dark edges, drawn over the game by Scanline's Vulkan layer. It's off by default, holds still when Animations is off, and also covers frames made by frame generation. If the layer can't run, the game just plays without it.

## 0.4.0
- **Eden Deck is now Scanline.** New name, same app. Your settings, key sets, installed Eden, game profiles and controller setups move over automatically on first start, and the app moves itself to `~/Applications/Scanline`. The old folder stays as a link, so Steam shortcuts you made before (including `edendeck launch …` ones) keep working. The launcher is now `scanline`; `edendeck` still works. The app log moves to `~/.local/share/scanline/logs/app.log`.
- Themes and accent colours now apply with a quick restart: pick one under Settings → Appearance and choose **Restart to apply**. The app saves it, restarts and comes back to the same page. Switching live could leave game cards unable to open until the next start.

## 0.3.5
- Fix: opening a game's card could silently do nothing. The card now always opens; if part of it can't be read you get a note on the card, and if the whole page fails you see the error and a Back button instead of nothing.
- The app now keeps its own log (`~/.local/share/edendeck/logs/app.log`): screens opened, theme switches, presses and errors. Send it along with bug reports.
- 32-bit 3D theme: the CD case and disc player no longer flicker when they open, and the 3D cartridge always hands over to the game card even if its spin is interrupted.
- Pick an accent colour for any theme under Settings → Appearance: gold, orange, red, pink, violet, blue, cyan or green. Pocket LCD offers whole screen colours instead (grey, ice blue, amber, rose) so it keeps its four shades. Each theme remembers its own, and it applies instantly.
- Entering a Settings or Eden category no longer makes the category list twitch at the end of its slide.
- Gets the updater ready for the app's new name (Scanline) in the next release, so updating from here just works.

## 0.3.4
- New theme: 32-bit 3D. Your games become real low-poly 3D cartridges with printed labels; the one you pick slides toward you, and opening it spins it into the game card. Rendered the way late-90s consoles did it: wobbly vertices, warped textures, dithered colour and fog.
- Era toys float around the edges (a TV, video tape, disc player, pet egg, flip phone, lava lamp and more, a different handful each day). They're always gently moving, and tapping one bops it or plays its own animation.

## 0.3.3
- Settings and Eden: when you enter a category, the category list slides aside and dims so you can see where you are. Left no longer jumps back out; press B.

## 0.3.2
- The Eden tab is reorganised into two panes like Settings: Emulator, Keys and firmware, Key sets, Frame generation and Advanced. A dot marks anything that needs attention.
- New Frame generation page: shows whether lsfg-vk and your Lossless.dll are found, and what's missing.

## 0.3.1
- Every theme has its own slow animated background: drifting lights, a pulsing grid, phosphor rain, pixel clouds, a scrolling 16-bit floor, an LCD shimmer or spinning low-poly shapes. It stops while a game runs, and you can turn it off under Settings → Appearance.
- The CRT effect looks like a real TV now: the picture wobbles and curves, a band rolls down the screen, colours fringe and the edges darken. Pick Subtle, Medium or Strong.
- Touch: swiping no longer presses the button you started on, touching a scrolling list just stops it, and the mouse cursor is never shown.

## 0.3.0
- Settings are reorganised into two panes, console style: Controller, Graphics, System, Library, Appearance, Updates and Advanced on the left, their options on the right. B goes back to the list.
- Games copied without a file extension (like "Metroid Dread" instead of "Metroid Dread.nsp") are detected, and the library offers a one-press "Fix name".
- Dumps that bundle a game with its update or DLC now show up as the game instead of being hidden.

## 0.2.9
- Changelogs in the app: the update prompt shows what's new, you see "What's new" once after updating, and every past version is under Settings → Updates → Changelog.

## 0.2.8
- Frame generation works now. Eden's own frame generation is only built for Android, so Eden Deck uses lsfg-vk (the same Lossless Scaling frame generation) instead. It doubles 30 fps games to 60 and never goes above 60. Your own lsfg-vk settings aren't changed.
- The Steam Deck's built-in controller is set up for Eden automatically. A mapping you made yourself in Eden is never overwritten.

## 0.2.7
- Eden Deck finds Lossless Scaling in your Steam libraries (SD card ones too) and uses your own Lossless.dll for frame generation.

## 0.2.6
- Close a game from anywhere: hold View + Start for 2 seconds, or use "Close game" on the Playing screen.
- Settings → Controller: pick Pro Controller, Handheld or Dual Joy-Cons for player 1, open Eden's button mapping, and save player 1 as a profile.
- Pick a controller profile per game.
- Rumble and motion controls options.

## 0.2.5
- Smoother, console-style motion everywhere, including list scrolling.
- Retro themes: 8-bit brick, 16-bit, Pocket LCD and CD era, plus a CRT effect.
- Switching themes applies instantly and keeps you where you are.
- Settings explain themselves: a short description opens under the one you're on.
- The Eden tab tells you when a new Eden version is available.

## 0.2.4
- Fixed games not starting when a library folder was your home folder.
- When Eden quits early you see why, with the option to try again without update and DLC.

## 0.2.3
- Controllers are set up for Eden automatically on first launch.
- "Open Eden" to reach Eden's own settings.
- Cartridge-style game tiles.
- Each game shows whether its update and DLC are found.

## 0.2.2
- Before a game starts, Eden Deck tells you if it needs newer keys than the ones you have.

## 0.2.1
- Game cards open as an overlay on the library.
- Touch scrolling.
- Cleaner focus highlight.

## 0.2.0
- In-app updates: new versions install from inside the app, and you can roll back.
- Animations throughout.
- Nightgrid theme.
- Duplicate games in the library are merged.

## 0.1.1
- One-line installer.
- Controller input fixes.

## 0.1.0
- First release: Eden install manager, game library, per-game settings and key sets, headless launcher for Steam shortcuts.
