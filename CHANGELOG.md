# Scanline changelog

Written for players. The app shows this under Settings → Updates → Changelog, and shows
"What's new" once after each update. Each release's GitHub notes start with its section.

## 0.13.3
- **A controller plugged into your Steam Deck's dock is player 1 now.** Steam lists the Deck's own controls first, so on a TV games could ignore the controller in your hands (BOTW was unplayable). Before each game Scanline now looks at what's connected and sets the emulator up itself: the controllers you plugged in or paired are player 1, 2, 3 and so on, in the order they connected, and the Deck's own controls are player 1 only when nothing else is connected. Unplug the controller and the Deck's controls take over again on the next launch.
- This works for every system: Switch (up to 8 players), PS3 (up to 7), PS2 and PS1 (2 players), GBA and DS (1 player). Steam shortcuts do the same. Your button choices from Settings → Buttons follow player 1 to whichever controller it is, and player 2 gets the same buttons. A controller you set up yourself in the emulator is still left alone.
- Switch: a game's own input profile follows player 1 too, and with the controller style set to Handheld, docked launches with a controller use a Pro Controller for that game (handheld style would keep it in handheld mode).
- Xbox: xemu can't be told which Steam controller to use, so it still takes the first one Steam lists. When that's the Deck's own controls, Scanline says so as the game starts, with the fix: in Steam's Quick Access menu (the … button), open the gear tab and choose Rearrange controller order.

## 0.13.2
- **Seven more hand-made props replace the old ones in the 3D theme.** Tap them: the treasure chest flips open with a glow, the ink ribbon wobbles, the top alphabet block tumbles off and hops back up, the walkie-talkie buzzes, the lantern's flame flickers as it swings, the cassette flips over, and the key above its glowing pedestal leaps and spins.

## 0.13.1
- **The 3D theme's props are easier to see.** They're brighter, bigger, sit further in from the screen edges, and the background renders sharper, so their details no longer turn to noise on a Steam Deck. The TV now shows its picture all the time, and a tap still runs the static and colour bars.
- The scene now uses only the new models. The old flat-coloured jewel case, memory card, pet egg, handheld, lava lamp, floppy and save crystal are gone.

## 0.13.0
- **The 32-bit 3D theme has new, hand-made models.** The TV, video tapes, console, phone, the claw machine (in the mascot's place) and the star crest are now detailed low-poly models with their own moving parts. Tap them: the TV warms up through static to its picture, the tapes flip, the console's lid pops open, the phone rings and dials, the claw grabs a plush (and drops it, of course), and the crest spins up.
- **Seven new props** in the rotation: a typewriter, a beige PC that reboots, a toy car that pops wheelies, a delivery drone that barrel-rolls, a desk lamp you can switch, a glowing arcade cabinet and a mirror ball. A different handful shows each day, as before.

## 0.12.1
- **Frame generation, the CRT effect and the in-game menu now work for DS games.** Turn frame generation or the CRT effect on in a DS game's Launch setup, and Scanline runs melonDS's OpenGL on Vulkan (through Mesa's Zink driver) for that game, so they can reach it. Frame generation only doubles DS games that run at 30 fps, as for every system. Needs the melonDS AppImage, not the Flatpak. If a game looks wrong or slow that way, turn both off to go back to plain OpenGL.

## 0.12.0
- New: **Nintendo DS games, through melonDS.** Install melonDS under **Emulators → melonDS (Nintendo DS)** (the official AppImage, straight from melonDS's own page), or Scanline finds one you already have. No BIOS is needed: melonDS has a free one built in. You can still add the BIOS and firmware from your own DS there; Scanline never downloads them.
- DS games show up from your library folders as .nds, with their real names from a built-in list (no internet needed), an **NDS** chip, and **the game's own icon** pixelated on the cartridge label and in its Steam art.
- Everything else works as for the other systems: performance presets (renderer, resolution and fast mode) and melonDS settings for all games or just one, including the **screen layout** (side by side fills a Steam Deck best), **save backups** (the save and save states), **Add to Steam**, sizes under Settings → Storage, and your controller set up like a DS before each game. Click the right stick to swap the screens and the left stick to blow into the microphone. Touch the bottom screen with the touchscreen.
- **Cheats** on each DS game's card: Action Replay codes from libretro's cheat database, plus your own codes from melonDS's cheat window. The switches are melonDS's own.
- Good to know: melonDS draws with OpenGL or in software, so **frame generation, the CRT effect and the in-game menu aren't available for DS games**. The app says so where it matters.

## 0.11.0
- New: **original Xbox games, through xemu.** Install xemu under **Emulators → xemu (Xbox)** (the official AppImage, straight from xemu's own page), or Scanline finds one you already have. Choose your own Xbox **MCPX boot ROM**, **flash BIOS** and **hard disk image** there too; Scanline never downloads them.
- Xbox games show up from your library folders as .iso or .xiso (XISO or full Redump dumps), with their real names from a built-in list (no internet needed) or the name inside the game. They get an **Xbox** chip on their cartridge.
- Everything else works as for the other systems: **frame generation** and the **CRT effect** (Scanline switches xemu to its Vulkan renderer for them when you haven't picked one), resolution presets and xemu settings for all games or just one, **Add to Steam**, sizes under Settings → Storage, and your controller on the Xbox's first port before each game. xemu's welcome window is skipped so games start straight away.
- Good to know: Xbox games save to the console's hard disk, so xemu keeps **every game's saves in one hard disk image**. Instead of per-game save backups, Scanline backs up the whole disk (from the game's card or the Emulators page, keeping the last 3). xemu has no patches or cheats of its own, so Xbox games have no Patches & cheats page.

## 0.10.0
- New: **PlayStation games, through DuckStation.** Install DuckStation under **Emulators → DuckStation (PlayStation)** (the official AppImage, straight from DuckStation's own page), or Scanline finds one you already have. Add the BIOS you dumped from your own PS1 there too (a PS2 BIOS works as well); Scanline never downloads it.
- PS1 games show up from your library folders as .cue/.bin, .chd, .iso or .pbp, with their real names from a built-in list (no internet needed). They get a **PS1** chip on their cartridge. A .chd is recognised by its Redump name or by sitting in a folder called psx or ps1.
- Everything else works as for the other systems: **frame generation** and the **CRT effect**, performance presets (with **steady 3D** that fixes the PS1's wobbling polygons) and DuckStation settings for all games or just one, **save backups**, **Add to Steam**, sizes under Settings → Storage, and your controller set up like a DualShock before each game (yours are kept if you map buttons in DuckStation).
- **Patches & cheats** on each PS1 game's card: widescreen and 60 fps patches and cheats from DuckStation's community database, plus your own .cht cheat files. The switches are DuckStation's own, so its Game Properties show the same thing.
- PS1 games get their own memory card each, as DuckStation sets up, so a save backup holds just that game's card and save states.

## 0.9.0
- New: **PlayStation 2 games, through PCSX2.** Install PCSX2 under **Emulators → PCSX2 (PlayStation 2)** (the official AppImage, checksum-verified), or Scanline finds the one you installed from Discover. Choose the BIOS you dumped from your own PS2 there too; Scanline never downloads it.
- PS2 games show up from your library folders as .iso, .chd, .cso, .zso or .cue, with their real names from a built-in list (no internet needed). They get a **PS2** chip on their cartridge.
- Everything else works as for the other systems: **frame generation** and the **CRT effect** (with the AppImage), performance presets and PCSX2 settings for all games or just one, **save backups**, **Add to Steam**, sizes under Settings → Storage, and your controller set up like a PS2 pad before each game (yours are kept if you map buttons in PCSX2).
- **Patches & cheats** on each PS2 game's card: switch on PCSX2's community widescreen, no-interlacing and 60 fps patches, and add your own .pnach cheat files. The switches are PCSX2's own, so its Game Properties show the same thing.
- Good to know: a game's own settings, patches and cheats need the disc's checksum, which Scanline can't read from a .chd by itself. **Start a .chd game once** and they unlock. PS2 games share their memory cards, as on a real PS2, so a save backup holds the whole card.

## 0.8.4
- Fixed: **PS3 games started from their Steam shortcut didn't get the controller set up**, and GBA games started there drew the handheld frame in the default colour instead of your accent. Steam shortcuts run without the app, so Scanline now remembers the controller and accent the app last saw and uses them there too.
- Fixed: closing a GBA game after a short play no longer shows the "closed after N seconds" error page. That page is for games that fail to start.

## 0.8.3
- Fixed: **games added to Steam showed Scanline's icon** instead of their own cartridge icon. Each game's shortcut now uses the icon drawn with its Steam art (existing shortcuts switch over the next time Scanline writes to Steam).
- Fixed: two games with the same name (say a Game Boy and a GBA "Tetris", or two regions of one game) **overwrote each other's Steam shortcut**. The second one now gets its game code after its name.
- Fixed: one damaged or unusual file in a library folder could stop the whole library scan. It's now skipped.

## 0.8.2
- Fixed: **PS3 games' Steam artwork** squashed the game's wide icon onto the cartridge label. It's now cropped to the middle like in the library, and existing artwork is redrawn.
- Fixed: one game's Steam artwork failing no longer stops the rest from being drawn.
- Fixed: the Steam settings text said every game opens in Eden. Each game opens in its own emulator.

## 0.8.1
- Fixed: **the controller didn't work in PS3 games**. With no pad setup, RPCS3 falls back to the keyboard. Scanline now sets up your controller in RPCS3 before each game, like a PS3 pad: Cross on the bottom face button, Circle on the right, L1/R1 on the bumpers, L2/R2 on the triggers, Select/Start on View/Menu, and PS on the guide button or View + Menu. If you've set up pads in RPCS3 yourself, yours are kept.

## 0.8.0
- New: **handheld frame** for GBA, Game Boy and Game Boy Color games. The black bars beside the picture become a handheld-style shell and screen bezel in your theme's accent colour, with its own look for Game Boy / Color. It's decoration only, so nothing in it is a button, and the game's pixels stay sharp. It works together with the CRT effect. It's on by default: switch it off under **Emulators → mGBA → Handheld frame**, or per game on its card.

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
