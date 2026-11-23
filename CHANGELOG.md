# Scanline changelog

Written for players. The app shows this under Settings → Updates → Changelog, and shows
"What's new" once after each update. Each release's GitHub notes start with its section.

## 0.7.3
- Fixed: **zipped Game Boy Advance games** (.zip) weren't found. They now show up like any other game and mGBA runs them straight from the .zip. Saves, save states, cheats and patches work the same.
- **Real game names** for GBA, Game Boy and Game Boy Color games, whatever the files are called: Scanline recognises games from a built-in name list (no internet needed). Unknown files get tidied-up names, with no region tags, underscores or release numbers.
- Fixed: **the controller didn't work in mGBA** on the Steam Deck. Scanline now sets up your controller in mGBA before each game: A on the right face button, B on the bottom, L/R on the bumpers, Select/Start on View/Menu, d-pad and left stick to move. If you've changed buttons in mGBA yourself, yours are kept.

## 0.7.2
- Fixed: **Install RPCS3** failed with "RPCS3's update service answered with an error (-3)". Scanline now asks RPCS3's update service in a way it accepts, so installing and updating RPCS3 works.

## 0.7.1
- Fixed: on the **Emulators** tab, long names like "mGBA (Game Boy Advance)" made the category list run into the right side in themes with wider lettering (like 32-bit 3D). The list now widens to fit and keeps its gap.
- Fixed: when an Eden update is available, the installed version line no longer sits right on top of the Update button.

## 0.7.0
- New: **PlayStation 3 games**, played in **RPCS3**. Add the folder with your PS3 game folders or .iso files and they show up in the library with a PS3 chip and the game's own icon on the cartridge. Games you installed inside RPCS3 are found by themselves. Under **Emulators → RPCS3** you can install the official RPCS3 AppImage (the same build RPCS3's own updater offers, checksum-verified), use the RPCS3 Flatpak or one you already have, update or roll back, and change RPCS3's settings for every game.
- **Your own PS3 system software:** pick your PS3UPDAT.PUP and RPCS3 installs it. Scanline never downloads it.
- Every PS3 game card gets:
  - **Frame generation** like Switch games: lsfg-vk doubles 30 fps games to 60, never above 60.
  - **CRT effect** with the same moving old-TV look as Switch games, through Scanline's in-game layer.
  - **Performance presets** (Battery saver, Balanced, TV quality, Sharp) and RPCS3's settings just for that game, saved as RPCS3's own custom config, so RPCS3 shows them too.
  - **Game patches:** 60 fps modes, higher resolutions and fixes from RPCS3's community patch list, downloaded only when you ask. Switch each one on or off; RPCS3's patch manager shows the same switches.
  - **Saves** (backup, restore, delete, and a backup before each play), **Add to Steam** with the game's icon on the cartridge art, favorites and collections, a cache rebuild button, and a PS3 section under Settings → Storage.
- Frame generation and the CRT effect need the RPCS3 AppImage; they can't reach into the Flatpak's sandbox, and the app says so. Controllers are set up once in RPCS3's own window (Emulators → RPCS3 → Open RPCS3).

## 0.6.0
- New: **Game Boy Advance, Game Boy and Game Boy Color games**, played in **mGBA**. Add a folder with your .gba, .gb or .gbc files and they show up in the library next to your Switch games, with a GBA, GBC or GB chip on the cartridge. The Eden tab is now **Emulators**, with a new **mGBA** section: install the official mGBA AppImage (or use the mGBA Flatpak or an mGBA you already have), update or roll back, set your own GBA BIOS (optional, mGBA has a built-in replacement) and change mGBA's settings.
- Every GBA game card gets the same things as Switch games where they make sense:
  - **Looks:** one press for **Pixel perfect**, **Original LCD** (with the handheld's screen ghosting, so flicker effects look solid), **Smooth** or **Full screen**, plus each setting on its own just for that game.
  - **Patches & cheats:** apply an .ips, .ups or .bps patch (translations, fixes, hacks) without touching your ROM, and get cheats from the libretro cheat database, only when you press it. Switch each cheat on or off. Credit goes to each cheat's author.
  - **Saves:** back up, restore and delete the save and save states, with a backup before each play like Switch games.
  - **CRT effect:** the same Subtle, Medium and Strong levels as for Switch games, drawn by mGBA itself (a still effect: no wobble or rolling band).
  - **Add to Steam**, favorites, collections, and a Game Boy section under Settings → Storage.
- Not in mGBA: frame generation (GBA games already run at 60), and Scanline's in-game menu and performance overlay, which only reach Vulkan emulators. Hold View + Start for 2 seconds to quit a game. Buttons use mGBA's own mapping; the game card says so.

## 0.5.5
- New: **performance presets** on every game card, under Eden settings for this game. One press sets resolution, upscaling, anti-aliasing, GPU accuracy and async shaders: **Battery saver**, **Balanced** (the Steam Deck sweet spot), **TV quality** or **Sharp** (desktop GPUs). Change any setting by hand and the row says so; pick Eden's settings to go back to your global ones.
- New: **console mode follows the screen.** Games start docked when a TV or monitor is connected and handheld on the Deck's own screen, picked each time a game starts. Your Eden setting is put back when the game closes. A console mode set on a game's card still wins, and you can turn this off under Settings → System.
- New: **Storage** under Settings. Free space on each drive, what games, shader caches, mods, saves and save backups take up, and every game by size. Each game's page (also on its card) can **rebuild the shader cache**, which fixes flicker after a driver or Eden update, and **remove older update files** Eden no longer loads (they go to the trash). There's also a button to rebuild every shader cache at once.

## 0.5.4
- New: **favorites and collections.** Star a game on its card and it comes first in the library. Put games on shelves like Playing now, Up next, Finished or Multiplayer from Collections on the card. Once you've starred or shelved something, a **Show** row at the top of the library lets you see just your favorites or one shelf.

## 0.5.3
- New: **Saves** on every game card. See how big your save is and when it last changed, back it up with one press, and go back to any backup. Restoring or deleting always backs up what it replaces first, so nothing is lost. By default Scanline also makes a backup right before each play and keeps the last 5 of those per game (turn it off on the Saves page). Manual backups are never deleted automatically.

## 0.5.2
- New: **menu sounds and controller rumble**, like a console. Moving, pressing, going back, switching tabs, changing a value, errors and starting a game each get a short sound and a light buzz. Every theme has its own set: soft blips for Dark and CD era, chiptune chirps for the pixel themes, a neon buzz for Nightgrid and an echoing chime for 32-bit 3D. All made by Scanline itself, nothing downloaded. Set the volume or turn rumble off under Settings → Sound & feel. Games keep their own sound and rumble.

## 0.5.1
- Game icons now **fill the cartridge label**, cropped evenly to its shape, instead of sitting between black bars. Same on the 3D carts and the Steam art (Steam art for games you already added is drawn again the next time Scanline starts).

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
