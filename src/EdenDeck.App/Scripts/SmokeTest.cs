using EdenDeck.App.Ui;
using Godot;

namespace EdenDeck.App;

/// <summary>
/// `EdenDeck -- --smoke &lt;dir&gt;`: drives the UI with scripted input, saves a screenshot after
/// each step and quits. Used by CI (under Xvfb) to catch startup and navigation regressions,
/// and to publish screenshots of each build. Exit code 1 if any step throws.
/// </summary>
public partial class SmokeTest : Node
{
    private readonly string _outDir;
    private readonly Queue<(string Name, Action Act, double Wait, string? Expect)> _steps = new();
    private double _wait = 1.0;
    private int _shot;
    private int _failures;

    public SmokeTest(string outDir)
    {
        _outDir = outDir;
        Directory.CreateDirectory(outDir);
        // The animated background costs a lot under software rendering (CI); it would skew the
        // hold-to-repeat timing. It is switched on for the theme screenshots further down.
        Step("library", () => SetAnimatedBackground(false));
        // Real joypad and keyboard events (not actions), through Pad's translation layer.
        Step("joy-dpad-right", () => Joy(JoyButton.DpadRight, true), wait: 0.25, expect: "Mountain Climb");
        Step("joy-dpad-release", () => Joy(JoyButton.DpadRight, false), expect: "Mountain Climb");
        Step("joy-stick-left", () => Stick(-1f), wait: 0.25, expect: "Castle Defenders");
        Step("joy-stick-center", () => Stick(0f), expect: "Castle Defenders");
        // Holding a direction auto-repeats after a short delay.
        Step("joy-dpad-hold", () => Joy(JoyButton.DpadRight, true), wait: 0.9, expect: "Sky Racers");
        Step("joy-dpad-hold-release", () => Joy(JoyButton.DpadRight, false), expect: "Sky Racers");
        Step("joy-dpad-down", () => { Joy(JoyButton.DpadDown, true); Joy(JoyButton.DpadDown, false); }, expect: "Zylo Drift");
        Step("joy-rb-tab", () => Tap(JoyButton.RightShoulder), expect: "Eden (Switch)");
        Step("joy-lb-tab", () => Tap(JoyButton.LeftShoulder), expect: "Castle Defenders");
        Step("key-tab", () => Key(Godot.Key.Tab), expect: "Eden (Switch)");
        // The Emulators tab has the same two panes as Settings.
        Step("eden-keys", () => Press("ui_down"), expect: "Keys and firmware");
        Step("eden-framegen", () =>
        {
            Press("ui_down");
            Press("ui_down");
        }, expect: "Frame generation");
        Step("eden-framegen-pane", () => Press(Pad.Accept), expect: "Check again");
        Step("eden-back", () => Press(Pad.Back), expect: "Frame generation");
        Step("key-shift-tab", () => Key(Godot.Key.Tab, shift: true), expect: "Castle Defenders");
        Step("joy-a-open", () => Tap(JoyButton.A), expect: "Play");
        Step("joy-b-back", () => Tap(JoyButton.B), expect: "Castle Defenders");
        // A finger tap on another tile (as gamescope delivers it: move there, press, release) opens it.
        Step("touch-tap-open", () => TouchTap(FindFocusable(GetTree().Root, "Sky Racers")), wait: 1.2, expect: "Play");
        Step("touch-tap-back", () => Tap(JoyButton.B), expect: "Sky Racers");
        Step("touch-refocus", () => FindFocusable(GetTree().Root, "Castle Defenders")?.GrabFocus(), expect: "Castle Defenders");
        Step("game-page", () => Press(Pad.Accept));
        Step("game-stepper", () =>
        {
            Press("ui_down");
            Press("ui_down");
            Press("ui_right");
        });
        Step("game-settings", () =>
        {
            for (var i = 0; i < 6; i++)
            {
                Press("ui_down");
            }
        });
        // Per-game CRT effect: same four levels as the app's, stepped and stepped back.
        Step("game-crt", () => FindFocusable(GetTree().Root, "Makes the game look like an old TV")?.GrabFocus(), expect: "CRT effect");
        Step("game-crt-step", () =>
        {
            Press("ui_right");
            Press("ui_left");
        }, expect: "CRT effect");
        // Performance presets: one press sets a group of Eden's per-game settings, and back.
        Step("game-preset", () => FindFocusable(GetTree().Root, "Presets set resolution")?.GrabFocus(), expect: "Presets set resolution");
        Step("game-preset-battery", () => Press("ui_right"), expect: "Longest battery life");
        Step("game-preset-global", () => Press("ui_left"), expect: "Presets set resolution");
        // Add to Steam: the button is there (CI has no Steam, so pressing it only explains why).
        Step("game-steam", () => FindFocusable(GetTree().Root, "Puts this game in your Steam library")?.GrabFocus(), expect: "Add to Steam");
        // Mods & cheats: the smoke home has two mods for this game that replace the same file.
        Step("game-mods", () => FindFocusable(GetTree().Root, "Install and switch mods")?.GrabFocus(), expect: "Mods & cheats  ·  2 mods");
        Step("mods-page", () => Press(Pad.Accept), expect: "LayeredFS");
        Step("mods-toggle", () => Press(Pad.Accept), expect: "LayeredFS");
        Step("mods-back", () => Press(Pad.Back), expect: "Mods & cheats  ·  1 of 2 mods on");
        // Saves: the smoke home has a save for this game; back it up from its page.
        Step("game-saves", () => FindFocusable(GetTree().Root, "Back up, restore or delete")?.GrabFocus(), expect: "Saves  ·  not backed up");
        Step("saves-page", () => Press(Pad.Accept), expect: "Back up now");
        Step("saves-backup", () => Press(Pad.Accept), expect: "Back up now");
        Step("saves-back", () => Press(Pad.Back), expect: "Saves  ·  backed up");
        // Favorites and collections: star the game, shelve it, and the library gets a Show filter.
        Step("game-fav", () => FindFocusable(GetTree().Root, "Favorites come first")?.GrabFocus(), expect: "Favorite");
        Step("game-fav-on", () => Press("ui_right"), expect: "Favorite");
        Step("game-shelves", () => FindFocusable(GetTree().Root, "Put this game on shelves")?.GrabFocus(), expect: "Collections");
        Step("shelves-page", () => Press(Pad.Accept), expect: "the Playing now shelf");
        Step("shelves-on", () => Press("ui_right"), expect: "the Playing now shelf");
        Step("shelves-back", () => Press(Pad.Back), expect: "Collections  ·  Playing now");
        Step("back-to-library", () => Press(Pad.Back), expect: "Castle Defenders");
        Step("library-show", () => FindFocusable(GetTree().Root, "Favorites and your collections")?.GrabFocus(), expect: "Favorites and your collections");
        Step("library-show-favorites", () => Press("ui_right"), expect: "Favorites and your collections");
        Step("library-show-all", () => FindFocusable(GetTree().Root, "Favorites and your collections")?.GrabFocus() , expect: "Favorites and your collections");
        Step("library-show-all-left", () => Press("ui_left"), expect: "Favorites and your collections");
        Step("library-refocus", () => FindFocusable(GetTree().Root, "Castle Defenders")?.GrabFocus(), expect: "Castle Defenders");
        // A Game Boy Advance game (mGBA): its system chip, looks presets, patches & cheats and CRT row.
        Step("gba-tile", () => FindFocusable(GetTree().Root, "Tiny Quest")?.GrabFocus(), expect: "Tiny Quest");
        Step("gba-page", () => Press(Pad.Accept), expect: "Play");
        Step("gba-look", () => FindFocusable(GetTree().Root, "Looks set scaling")?.GrabFocus(), expect: "Looks set scaling");
        Step("gba-look-pixel", () => Press("ui_right"), expect: "Whole-number scaling");
        Step("gba-look-global", () => Press("ui_left"), expect: "Looks set scaling");
        Step("gba-crt", () => FindFocusable(GetTree().Root, "Makes the game look like an old TV")?.GrabFocus(), expect: "CRT effect");
        Step("gba-mods", () => FindFocusable(GetTree().Root, "Apply a ROM patch")?.GrabFocus(), expect: "Patches & cheats");
        Step("gba-mods-page", () => Press(Pad.Accept), expect: "Install a patch");
        Step("gba-mods-back", () => Press(Pad.Back), expect: "Patches & cheats");
        Step("gba-saves", () => FindFocusable(GetTree().Root, "Back up, restore or delete")?.GrabFocus(), expect: "Saves");
        Step("gba-back", () => Press(Pad.Back), expect: "Tiny Quest");
        // A PS3 game (RPCS3): performance presets, game patches, frame generation and the CRT row.
        Step("ps3-tile", () => FindFocusable(GetTree().Root, "Velvet Run")?.GrabFocus(), expect: "Velvet Run");
        Step("ps3-page", () => Press(Pad.Accept), expect: "Play");
        Step("ps3-preset", () => FindFocusable(GetTree().Root, "Presets set resolution, upscaling and texture filtering")?.GrabFocus(), expect: "Presets set resolution, upscaling and texture filtering");
        Step("ps3-preset-battery", () => Press("ui_right"), expect: "Longest battery life");
        Step("ps3-preset-global", () => Press("ui_left"), expect: "Presets set resolution");
        Step("ps3-framegen", () => FindFocusable(GetTree().Root, "Doubles a 30 fps game")?.GrabFocus(), expect: "Doubles a 30 fps game");
        Step("ps3-crt", () => FindFocusable(GetTree().Root, "Makes the game look like an old TV")?.GrabFocus(), expect: "CRT effect");
        Step("ps3-patches", () => FindFocusable(GetTree().Root, "60 fps modes, higher resolutions")?.GrabFocus(), expect: "Game patches");
        Step("ps3-patches-page", () => Press(Pad.Accept), expect: "Get the patch list");
        Step("ps3-patches-back", () => Press(Pad.Back), expect: "Game patches");
        Step("ps3-back", () => Press(Pad.Back), expect: "Velvet Run");
        // A PS2 game (PCSX2): presets, frame generation, the CRT row, and patches & cheats.
        Step("ps2-tile", () => FindFocusable(GetTree().Root, "Zephyr Line")?.GrabFocus(), expect: "Zephyr Line");
        Step("ps2-page", () => Press(Pad.Accept), expect: "Play");
        Step("ps2-preset", () => FindFocusable(GetTree().Root, "Presets set resolution and texture filtering")?.GrabFocus(), expect: "Presets set resolution and texture filtering");
        Step("ps2-framegen", () => FindFocusable(GetTree().Root, "Doubles a 30 fps game")?.GrabFocus(), expect: "Doubles a 30 fps game");
        Step("ps2-crt", () => FindFocusable(GetTree().Root, "Makes the game look like an old TV")?.GrabFocus(), expect: "CRT effect");
        Step("ps2-patches", () => FindFocusable(GetTree().Root, "Widescreen, no-interlacing and 60 fps")?.GrabFocus(), expect: "Patches & cheats");
        Step("ps2-patches-page", () => Press(Pad.Accept), expect: "Look up patches");
        Step("ps2-patches-back", () => Press(Pad.Back), expect: "Patches & cheats");
        Step("ps2-back", () => Press(Pad.Back), expect: "Zephyr Line");
        // A PS1 game (DuckStation): presets, frame generation, the CRT row, and patches & cheats.
        Step("ps1-tile", () => FindFocusable(GetTree().Root, "Zircon Tide")?.GrabFocus(), expect: "Zircon Tide");
        Step("ps1-page", () => Press(Pad.Accept), expect: "Play");
        Step("ps1-preset", () => FindFocusable(GetTree().Root, "Presets set resolution and steady 3D")?.GrabFocus(), expect: "Presets set resolution and steady 3D");
        Step("ps1-framegen", () => FindFocusable(GetTree().Root, "Doubles a 30 fps game")?.GrabFocus(), expect: "Doubles a 30 fps game");
        Step("ps1-crt", () => FindFocusable(GetTree().Root, "Makes the game look like an old TV")?.GrabFocus(), expect: "CRT effect");
        Step("ps1-patches", () => FindFocusable(GetTree().Root, "Widescreen and 60 fps patches and cheats")?.GrabFocus(), expect: "Patches & cheats");
        Step("ps1-patches-page", () => Press(Pad.Accept), expect: "Look up patches and cheats");
        Step("ps1-patches-back", () => Press(Pad.Back), expect: "Patches & cheats");
        Step("ps1-back", () => Press(Pad.Back), expect: "Zircon Tide");
        // An Xbox game (xemu): presets, frame generation and the CRT row.
        Step("xbox-tile", () => FindFocusable(GetTree().Root, "Zylo Drift")?.GrabFocus(), expect: "Zylo Drift");
        Step("xbox-page", () => Press(Pad.Accept), expect: "Play");
        Step("xbox-preset", () => FindFocusable(GetTree().Root, "Presets set the resolution for this game")?.GrabFocus(), expect: "Presets set the resolution for this game");
        Step("xbox-framegen", () => FindFocusable(GetTree().Root, "Doubles a 30 fps game")?.GrabFocus(), expect: "Doubles a 30 fps game");
        Step("xbox-crt", () => FindFocusable(GetTree().Root, "Makes the game look like an old TV")?.GrabFocus(), expect: "CRT effect");
        Step("xbox-back", () => Press(Pad.Back), expect: "Zylo Drift");
        // A DS game (melonDS): presets and cheats.
        Step("nds-tile", () => FindFocusable(GetTree().Root, "Zyphra Tale")?.GrabFocus(), expect: "Zyphra Tale");
        Step("nds-page", () => Press(Pad.Accept), expect: "Play");
        Step("nds-framegen", () => FindFocusable(GetTree().Root, "Doubles a 30 fps game")?.GrabFocus(), expect: "Doubles a 30 fps game");
        Step("nds-crt", () => FindFocusable(GetTree().Root, "Makes the game look like an old TV")?.GrabFocus(), expect: "CRT effect");
        Step("nds-preset", () => FindFocusable(GetTree().Root, "Presets set the 3D renderer")?.GrabFocus(), expect: "Presets set the 3D renderer");
        Step("nds-cheats", () => FindFocusable(GetTree().Root, "Action Replay codes from libretro")?.GrabFocus(), expect: "Cheats");
        Step("nds-cheats-page", () => Press(Pad.Accept), expect: "Back");
        Step("nds-cheats-back", () => Press(Pad.Back), expect: "Cheats");
        Step("nds-back", () => Press(Pad.Back), expect: "Zyphra Tale");
        Step("library-refocus-2", () => FindFocusable(GetTree().Root, "Castle Defenders")?.GrabFocus(), expect: "Castle Defenders");
        Step("running", () => Press(Pad.Tertiary));  // quick play: fake Eden in CI
        // The fake Eden exits at once: Scanline reports the early exit instead of silently returning.
        Step("returned", () => { }, wait: 3.0, expect: "OK");
        Step("early-exit-ok", () => Press(Pad.Accept), expect: "Castle Defenders");
        Step("eden", () => Press(Pad.TabNext));
        Step("emulators-mgba", () => FindFocusable(GetTree().Root, "mGBA isn't installed yet")?.GrabFocus(), expect: "mGBA (Game Boy Advance)");
        Step("emulators-mgba-pane", () => Press(Pad.Accept), expect: "Install mGBA");
        Step("emulators-mgba-back", () => Press(Pad.Back), expect: "mGBA (Game Boy Advance)");
        Step("emulators-rpcs3", () => FindFocusable(GetTree().Root, "RPCS3 isn't installed yet")?.GrabFocus(), expect: "RPCS3 (PlayStation 3)");
        Step("emulators-rpcs3-pane", () => Press(Pad.Accept), expect: "Install RPCS3");
        Step("emulators-rpcs3-back", () => Press(Pad.Back), expect: "RPCS3 (PlayStation 3)");
        Step("emulators-pcsx2", () => FindFocusable(GetTree().Root, "PCSX2 isn't installed yet")?.GrabFocus(), expect: "PCSX2 (PlayStation 2)");
        Step("emulators-pcsx2-pane", () => Press(Pad.Accept), expect: "Install PCSX2");
        Step("emulators-pcsx2-back", () => Press(Pad.Back), expect: "PCSX2 (PlayStation 2)");
        Step("emulators-duckstation", () => FindFocusable(GetTree().Root, "DuckStation isn't installed yet")?.GrabFocus(), expect: "DuckStation (PlayStation)");
        Step("emulators-duckstation-pane", () => Press(Pad.Accept), expect: "Install DuckStation");
        Step("emulators-duckstation-back", () => Press(Pad.Back), expect: "DuckStation (PlayStation)");
        Step("emulators-xemu", () => FindFocusable(GetTree().Root, "xemu isn't installed yet")?.GrabFocus(), expect: "xemu (Xbox)");
        Step("emulators-xemu-pane", () => Press(Pad.Accept), expect: "Install xemu");
        Step("emulators-xemu-back", () => Press(Pad.Back), expect: "xemu (Xbox)");
        Step("emulators-melonds", () => FindFocusable(GetTree().Root, "melonDS isn't installed yet")?.GrabFocus(), expect: "melonDS (Nintendo DS)");
        Step("emulators-melonds-pane", () => Press(Pad.Accept), expect: "Install melonDS");
        Step("emulators-melonds-back", () => Press(Pad.Back), expect: "melonDS (Nintendo DS)");
        Step("settings", () => Press(Pad.TabNext), expect: "Controller");
        // Two panes: moving down the categories swaps the right pane in place.
        Step("settings-library", () =>
        {
            for (var i = 0; i < 4; i++)
            {
                Press("ui_down");
            }
        }, expect: "Library");
        // Inside a pane, left stays in the pane; only B goes back to the categories.
        Step("library-pane-left", () =>
        {
            Press(Pad.Accept);
            Press("ui_left");
        }, expect: "Games/Switch");
        Step("library-pane-back", () => Press(Pad.Back), expect: "Library");
        // Finger drag over the options pane scrolls it and must not press the row it started on.
        Step("touch-drag-scroll", () =>
        {
            Mouse(new Vector2(800, 600), true);
            for (var y = 580; y >= 260; y -= 40)
            {
                Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(800, y), GlobalPosition = new Vector2(800, y), ButtonMask = MouseButtonMask.Left, Relative = new Vector2(0, -40) });
            }
            Mouse(new Vector2(800, 260), false);
        }, expect: "Library");
        // A sideways swipe over a row is not a press either.
        Step("touch-swipe-sideways", () =>
        {
            Mouse(new Vector2(800, 300), true);
            for (var x = 812; x <= 900; x += 12)
            {
                Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(x, 302), GlobalPosition = new Vector2(x, 302), ButtonMask = MouseButtonMask.Left, Relative = new Vector2(12, 0) });
            }
            Mouse(new Vector2(900, 302), false);
        }, expect: "Library");
        Step("settings-graphics", () =>
        {
            FindFocusable(GetTree().Root, "Resolution, upscaling")?.GrabFocus();
            Press(Pad.Accept);  // into the pane
            for (var i = 0; i < 8; i++)
            {
                Press("ui_down");
            }
        });
        Step("settings-back-to-category", () => Press(Pad.Back), expect: "Graphics");
        // Button mapping for games: the pane shows the face-button layout and one row per Switch button.
        Step("buttons-category", () => FindFocusable(GetTree().Root, "Remap buttons for games")?.GrabFocus(), expect: "Buttons");
        Step("buttons-row", () => FindFocusable(GetTree().Root, "Switch positions: A is the right face button")?.GrabFocus(), expect: "Switch positions: A is the right face button");
        Step("buttons-zr", () => FindFocusable(GetTree().Root, "The controller button that presses Switch ZR")?.GrabFocus(), expect: "presses Switch ZR");
        // Themes switch in place: still on Settings, focus stays on the Theme row.
        Step("steam-category", () => FindFocusable(GetTree().Root, "Scanline and your games in your Steam library")?.GrabFocus(), expect: "Steam");
        Step("sound-category", () => FindFocusable(GetTree().Root, "Menu sounds and controller rumble")?.GrabFocus(), expect: "Sound & feel");
        Step("sound-pane", () => Press(Pad.Accept), expect: "Short sounds when you move");
        Step("sound-level", () => Press("ui_right"), expect: "Short sounds when you move");
        Step("sound-back", () => Press(Pad.Back), expect: "Sound & feel");
        // Storage: sizes, the shader cache clean-up, and a game's own page.
        Step("storage-category", () => FindFocusable(GetTree().Root, "What takes up space")?.GrabFocus(), expect: "Storage");
        Step("storage-pane", () => Press(Pad.Accept), expect: "Rebuild all shader caches");
        Step("storage-game", () => FindFocusable(GetTree().Root, "What this game takes up, and tidying it up")?.GrabFocus(), expect: "Castle Defenders");
        Step("storage-game-page", () => Press(Pad.Accept), expect: "Rebuild shader cache");
        Step("storage-clear", () => Press(Pad.Accept), expect: "Cancel");  // confirm opens on Cancel
        Step("storage-clear-yes", () => Press("ui_down"), expect: "Clear");
        Step("storage-cleared", () => Press(Pad.Accept), expect: "Done");
        Step("storage-game-back", () => Press(Pad.Back));
        Step("storage-back", () => Press(Pad.Back), expect: "Storage");
        Step("appearance", () => FindFocusable(GetTree().Root, "Theme, CRT")?.GrabFocus(), expect: "Appearance");
        Step("theme-focus", () =>
        {
            SetAnimatedBackground(true);
            FindFocusable(GetTree().Root, "Pick a theme, then choose Restart")?.GrabFocus();
        }, expect: "Pick a theme, then choose Restart");
        for (var i = 1; i <= UiTheme.Themes.Count; i++)
        {
            Step($"theme-{i}", () => Press("ui_right"), wait: 0.8, expect: "Pick a theme, then choose Restart");
        }
        // Themes apply by restarting: picking another one offers the restart, picking the current one doesn't.
        Step("theme-pick", () => Press("ui_right"), wait: 0.5, expect: "Pick a theme, then choose Restart");
        Step("theme-restart-offered", () => FindFocusable(GetTree().Root, "Saves the theme and restarts")?.GrabFocus(), expect: "Restart to apply");
        Step("theme-unpick", () =>
        {
            FindFocusable(GetTree().Root, "Pick a theme, then choose Restart")?.GrabFocus();
            Press("ui_left");
        }, wait: 0.5, expect: "Pick a theme, then choose Restart");
        // The 32-bit 3D theme: 3D carts over the same grid, a prop scene behind, era rendering.
        Step("poly-settings", () =>
        {
            // CI and release builds with the private model repo must actually carry the bought models.
            if (System.Environment.GetEnvironmentVariable("SCANLINE_EXPECT_MODELS") == "1" && !Ui.Poly.Models.Available)
            {
                throw new InvalidOperationException("the licensed 3D models are missing from this build");
            }
            SetTheme("polygon");
        }, wait: 1.5);
        // A tap on empty space bops the prop there (bottom-right slot) and touches no UI.
        Step("poly-tap-prop", () =>
        {
            Mouse(new Vector2(1216, 672), true);
            Mouse(new Vector2(1216, 672), false);
        }, wait: 0.45, expect: "Pick a theme, then choose Restart");
        Step("poly-library", () =>
        {
            Tap(JoyButton.LeftShoulder);
            Tap(JoyButton.LeftShoulder);
        }, wait: 1.5, expect: "Castle Defenders");
        Step("poly-focus-next", () => Press("ui_right"), wait: 0.8, expect: "Mountain Climb");
        Step("poly-open", () => Press(Pad.Accept), wait: 1.5, expect: "Play");
        Step("poly-back", () => Press(Pad.Back), wait: 1.0, expect: "Mountain Climb");
        Step("poly-restore", () =>
        {
            Tap(JoyButton.RightShoulder);
            Tap(JoyButton.RightShoulder);
            SetTheme("dark");
        });
        Step("poly-restore-appearance", () => FindFocusable(GetTree().Root, "Theme, CRT")?.GrabFocus(), expect: "Appearance");
        // Accent colour: pick one (applies live, focus stays on the row), then back to the theme's own.
        Step("accent-focus", () => FindFocusable(GetTree().Root, "The highlight colour for this theme")?.GrabFocus(), expect: "The highlight colour for this theme");
        Step("accent-pick", () => Press("ui_right"), wait: 0.8, expect: "The highlight colour for this theme");
        Step("accent-reset", () => Press("ui_left"), wait: 0.8, expect: "The highlight colour for this theme");
        Step("crt-strong", () =>
        {
            FindFocusable(GetTree().Root, "Makes the whole screen")?.GrabFocus();
            for (var i = 0; i < 3; i++)
            {
                Press("ui_right");
            }
        }, expect: "Makes the whole screen");
        Step("crt-off", () => Press("ui_right"), expect: "Makes the whole screen");
        // Every theme, after switching to it: a game card opens (A and a finger tap) and Play starts the game.
        foreach (var t in UiTheme.Themes)
        {
            var id = t.Id;
            Step($"open-{id}-switch", () =>
            {
                SetTheme(id);
                Tap(JoyButton.LeftShoulder);
                Tap(JoyButton.LeftShoulder);
            }, wait: 1.2);
            Step($"open-{id}-a", () =>
            {
                FindFocusable(GetTree().Root, "Mountain Climb")?.GrabFocus();
                Tap(JoyButton.A);
            }, wait: 1.2, expect: "Play");
            Step($"open-{id}-play", () => Press(Pad.Accept), wait: 3.0, expect: "OK");
            Step($"open-{id}-ok", () => Press(Pad.Accept), wait: 0.8, expect: "Play");
            Step($"open-{id}-back", () => Press(Pad.Back), wait: 0.8, expect: "Mountain Climb");
            Step($"open-{id}-tap", () => TouchTap(FindFocusable(GetTree().Root, "Sky Racers")), wait: 1.2, expect: "Play");
            Step($"open-{id}-tap-back", () =>
            {
                Press(Pad.Back);
                Tap(JoyButton.RightShoulder);
                Tap(JoyButton.RightShoulder);
            }, wait: 0.8);
        }
        // Live switches back and forth from the library itself (the 3D theme in both directions).
        foreach (var id in new[] { "dark", "polygon", "nightgrid", "polygon", "dark" })
        {
            Step($"reswitch-{id}", () =>
            {
                Tap(JoyButton.LeftShoulder);
                Tap(JoyButton.LeftShoulder);
                SetTheme(id);
            }, wait: 1.2);
            Step($"reswitch-{id}-open", () =>
            {
                FindFocusable(GetTree().Root, "Ocean Odyssey")?.GrabFocus();
                Tap(JoyButton.A);
            }, wait: 1.2, expect: "Play");
            Step($"reswitch-{id}-back", () =>
            {
                Tap(JoyButton.B);
                Tap(JoyButton.RightShoulder);
                Tap(JoyButton.RightShoulder);
            }, wait: 0.8);
        }
        Step("open-restore", () => SetTheme("dark"), wait: 1.0);
        Step("changelog", () =>
        {
            FindFocusable(GetTree().Root, "Version, changelog")?.GrabFocus();
            Press(Pad.Accept);  // into Updates: first row is Changelog
            Press(Pad.Accept);
        });
        Step("changelog-back", () => Press(Pad.Back), expect: "Changelog");
        Step("choice", () =>
        {
            FindFocusable(GetTree().Root, "Game folders")?.GrabFocus();
            Press(Pad.Accept);   // into Library: first row is the library folder
            Press(Pad.Accept);   // -> choice page
        });
        Step("file-picker", () =>
        {
            Press(Pad.Accept);   // Keep
            Press("ui_down");
            Press(Pad.Accept);   // Add folder -> picker
        });
    }

    private static void SetTheme(string id)
    {
        if (Main.Instance.Host is { } host)
        {
            host.SaveSettings(host.Settings with { Theme = id });
            Main.Instance.ApplyTheme(id);
        }
    }

    private static void SetAnimatedBackground(bool on)
    {
        if (Main.Instance.Host is { } host)
        {
            host.SaveSettings(host.Settings with { AnimatedBackground = on });
            Main.Instance.RefreshBackground();
        }
    }

    private void Step(string name, Action act, double wait = 1.0, string? expect = null) => _steps.Enqueue((name, act, wait, expect));

    private static Control? FindFocusable(Node root, string tooltip)
    {
        foreach (var c in root.GetChildren())
        {
            if (c is Control { FocusMode: Control.FocusModeEnum.All } ctl && ctl.TooltipText.StartsWith(tooltip, StringComparison.Ordinal) && ctl.IsVisibleInTree())
            {
                return ctl;
            }
            if (FindFocusable(c, tooltip) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static void Joy(JoyButton b, bool pressed) =>
        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = b, Pressed = pressed });

    private static void Mouse(Vector2 at, bool pressed) =>
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at });

    private static void TouchTap(Control? c)
    {
        if (c == null)
        {
            return;
        }
        var at = c.GetGlobalRect().GetCenter();
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Mouse(at, true);
        Mouse(at, false);
    }

    private static void Tap(JoyButton b)
    {
        Joy(b, true);
        Joy(b, false);
    }

    private static void Stick(float x) =>
        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = x });

    private static void Key(Key k, bool shift = false)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, ShiftPressed = shift, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, ShiftPressed = shift, Pressed = false });
    }

    private static void Press(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        Input.FlushBufferedEvents();
    }

    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait > 0)
        {
            return;
        }
        if (_steps.Count == 0)
        {
            GD.Print($"smoke: done, {_shot} screenshots, {_failures} failures");
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        var (name, act, wait, expect) = _steps.Dequeue();
        try
        {
            act();
        }
        catch (Exception e)
        {
            _failures++;
            GD.PushError($"smoke: step {name} failed: {e}");
        }
        // Give layout and focus a few frames to settle, then capture.
        _wait = wait;
        GetTree().CreateTimer(wait - 0.15).Timeout += () => Capture(name, expect);
    }

    private void Capture(string name, string? expect)
    {
        var focus = GetViewport().GuiGetFocusOwner();
        // Tiles draw their title with child labels, so fall back to the tooltip (which has the title).
        var text = focus is Button { Text: { Length: > 0 } t } ? t : focus?.TooltipText ?? "";
        if (expect != null && !text.Contains(expect))
        {
            _failures++;
            GD.PushError($"smoke: step {name} failed: expected focus on '{expect}', got '{text.Split('\n')[0]}'");
        }
        GD.Print($"smoke: {name}: focus={(focus == null ? "none" : focus.GetType().Name + " '" + text.Split('\n')[0] + "'")}");
        var img = GetViewport().GetTexture().GetImage();
        img?.SavePng(Path.Combine(_outDir, $"{++_shot:00}-{name}.png"));
    }
}
