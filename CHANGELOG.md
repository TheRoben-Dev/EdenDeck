# Scanline changelog

Written for players. The app shows this under Settings → Updates → Changelog, and shows
"What's new" once after each update. Each release's GitHub notes start with its section.

## 0.5.0
- New: **Mods & cheats** on every game card.
  - **Mods:** switch each mod on or off (the same switches as Eden's add-ons list), install one from a .zip or a folder (Scanline finds the mod inside however the archive is laid out, and refuses mods made for another game), or remove one. When two mods change the same file, Scanline tells you which one wins.
  - **Get cheats:** downloads this game's cheats from the community cheat database, only when you press it, for the exact version your game runs, and tells you which versions have cheats. Turn on the ones you want and save; they're on the next time you play. Credit goes to each cheat's author; nothing is bundled with Scanline.

## 0.4.9
- The library's cartridges now show **each game's own icon**, pixelated onto the label like the Steam art, in every theme (including the 3D carts) and on the game card. Icons come from your game files with your own keys and are cached; games without a readable icon keep their generated label.

## 0.4.8
- Adding games to Steam no longer offers to restart Steam or the Deck. Scanline just tells you to restart Steam to see them, whenever it suits you; the shortcuts are written the moment Steam closes (restarting the Deck works too). Settings → Steam shows how many are waiting.

## 0.4.7
- Steam artwork now puts **the game's own square icon** on the cartridge label, pixelated to match the 32-bit look. Scanline reads the icon from the game file with your own keys (prod.keys, plus title.keys or the game's own ticket where needed); nothing is downloaded. Games without a readable icon keep the generated label. Art for games you already added is drawn again with their icons the next time Scanline starts.

## 0.4.6
- **Steam artwork, made by Scanline.** Shortcuts now come with a cover, wide banner, hero, logo and icon in the 32-bit 3D theme's style: a 3D cartridge with the game's own label over the fogged grid floor, and the title in the theme's lettering. Scanline's own shortcut uses a set that ships with the app. Each game's set is drawn on your device when you add it (games you already added get theirs the next time Scanline starts). Nothing is downloaded, and art you pick yourself in Steam is never replaced.

## 0.4.5
- Fix: in Game Mode, adding shortcuts to Steam no longer shuts Steam down. That could leave the Deck stuck on Scanline with no way back. Scanline now offers **Restart the Deck now** instead and adds the shortcuts while the Deck shuts down, so they're in your library after the restart. On the desktop, Scanline still restarts just Steam.

## 0.4.4
- New: **button mapping for games** under Settings → Buttons. Pick **Switch positions** (A is the right face button, like on a Switch) or **Xbox letters** (A is the bottom button, as printed on a Steam Deck or Xbox pad), or point any Switch button at any button, trigger or d-pad direction. It's saved in Eden's own controller setup, so Eden shows the same mapping, and "Save player 1 as profile" on the Controller page keeps it for one game. Scanline's menus always keep A to confirm and B to go back. Works with the Steam Deck, Steam Input in Game Mode and Xbox-style pads; for others, map in Eden.

## 0.4.3
- New: **Add to Steam.** A game's card has an **Add to Steam** button, and Settings → Steam can add Scanline itself or every game at once. Games show up in Steam's library and Game Mode under the Scanline tag, and starting one there opens it straight in Eden with that game's settings, keys, CRT effect and frame generation. Steam only reads new shortcuts when it starts, so if it's running Scanline offers to restart it for you (or adds them the next time Scanline starts with Steam closed). Your own non-Steam shortcuts are left alone, and a backup of Steam's file is kept.
- From a terminal: `scanline steam add <game|--app|--all>`, `scanline steam remove`, `scanline steam list`.

## 0.4.2
- **lsfg-vk now comes with Scanline.** Frame generation works without installing anything besides your own Lossless Scaling from Steam. If you already have lsfg-vk 2.x installed, yours is used instead, and your lsfg-vk settings are never changed. Frames made by frame generation also get the game's CRT effect.
- The Eden tab's Frame generation page says when the included lsfg-vk is in use.

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
