using EdenDeck.Backends.Eden.Config;
using EdenDeck.Host.Updates;
using EdenDeck.App.Ui;
using EdenDeck.Core.Library;
using EdenDeck.Core.Settings;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>Settings in two panes (see <see cref="TwoPaneScreen"/>).</summary>
public partial class SettingsScreen : TwoPaneScreen
{
    private static readonly (string Name, string Summary)[] CategoryList =
    [
        ("Controller", "Controller style, rumble, motion and button mapping."),
        ("Buttons", "Remap buttons for games. The app's own controls never change."),
        ("Graphics", "Resolution, upscaling, anti-aliasing and VSync for all games."),
        ("System", "Console mode and speed limit for all games."),
        ("Library", "Game folders and rescanning."),
        ("Appearance", "Theme, CRT effect and animations."),
        ("Steam", "Scanline and your games in your Steam library."),
        ("Updates", "Version, changelog and updates."),
        ("Advanced", "Overlay layer and expert Eden options."),
    ];

    /// <summary>Eden options shown under Advanced instead of their group.</summary>
    private static readonly HashSet<string> AdvancedOptions =
        ["backend", "gpu_accuracy", "use_disk_shader_cache", "use_asynchronous_shaders", "cpu_accuracy", "use_multi_core"];

    protected override string Heading => "Settings";

    protected override IReadOnlyList<(string Name, string Summary)> Categories => CategoryList;

    protected override void BuildCategory(string name, VBoxContainer col, Action<Control> Add)
    {
        switch (name)
        {
            case "Controller":
                BuildController(col, Add);
                break;
            case "Buttons":
                BuildButtons(col, Add);
                break;
            case "Graphics":
                BuildEdenOptions(col, Add, o => o.Group == "Graphics" && !AdvancedOptions.Contains(o.Id));
                break;
            case "System":
                BuildEdenOptions(col, Add, o => o.Group == "System" && !AdvancedOptions.Contains(o.Id));
                break;
            case "Library":
                BuildLibrary(col, Add);
                break;
            case "Appearance":
                BuildAppearance(col, Add);
                break;
            case "Steam":
                BuildSteam(col, Add);
                break;
            case "Updates":
                BuildUpdates(col, Add);
                break;
            case "Advanced":
                BuildAdvanced(col, Add);
                break;
        }
    }

    private void BuildEdenOptions(VBoxContainer col, Action<Control> Add, Func<OptionDefinition, bool> filter, bool showGroups = false, bool note = true)
    {
        if (note)
        {
            col.AddChild(UiTheme.Label("Games can override these on their own page. Changes are saved straight to Eden's config, so close Eden first.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        var settings = Host.Eden.Settings;
        var global = settings.ReadGlobal();
        string? group = null;
        foreach (var o in settings.Options.Where(filter))
        {
            if (showGroups && o.Group != group)
            {
                group = o.Group;
                col.AddChild(UiTheme.Label(group, UiTheme.SmallSize + 1, UiTheme.AccentText));
            }
            var opt = o;
            Add(OptionRows.Global(o, global.GetValueOrDefault(o.Id), v =>
                App.Guard(() => settings.WriteGlobal(new Dictionary<string, string?> { [opt.Id] = v }))));
        }
    }

    private void BuildController(VBoxContainer col, Action<Control> Add)
    {
        var styles = new (int Style, string Label, string Detail)[]
        {
            (ControllerStyle.ProController, "Pro Controller", "Games see one full controller. Works everywhere; Eden keeps your Console mode setting."),
            (ControllerStyle.Handheld, "Handheld", "Games see a Switch in handheld mode with Joy-Cons attached, like the Steam Deck. Eden also switches Console mode to Handheld."),
            (ControllerStyle.DualJoycons, "Dual Joy-Cons", "Two detached Joy-Cons used together. Only for games that ask for that style."),
        };
        var current = Host.Eden.PlayerStyle();
        var style = new Stepper("Player 1 controller", styles.Select(x => (x.Style.ToString(), x.Label)),
            styles.Any(x => x.Style == current) ? current.ToString() : ControllerStyle.ProController.ToString(),
            "Which Switch controller Eden pretends player 1 is. Your button mapping stays the same.")
        {
            Expands = true,
            DetailFor = v => styles.FirstOrDefault(x => x.Style.ToString() == v).Detail,
        };
        style.Changed += v => App.Guard(() =>
        {
            Host.Eden.SetPlayerStyle(int.Parse(v));
            Rebuild();  // Console mode row may change with it
        });
        Add(style);
        Add(new ActionButton("Map buttons in Eden", App.OpenEden,
            "Opens Eden's Controls page for button mapping, other players and anything not shown here."));
        var styleName = styles.FirstOrDefault(x => x.Style == current).Label ?? "Player 1";
        Add(new ActionButton($"Save player 1 as profile \"{styleName}\"", () => App.Guard(() =>
        {
            Host.Eden.SaveInputProfile(styleName);
            App.Success($"Saved profile \"{styleName}\". Pick it on a game's page to use it just for that game.");
        }), "Saves the current mapping and controller style as an Eden input profile you can pick per game."));
        BuildEdenOptions(col, Add, o => o.Group == "Controls", note: false);
    }

    private void BuildButtons(VBoxContainer col, Action<Control> Add)
    {
        col.AddChild(UiTheme.Label("Which button on your controller presses each Switch button in games. Menus here keep A to confirm and B to go back. Sticks, motion and other players are set in Eden.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var pad = Pad.ActivePad();
        if (pad == null)
        {
            col.AddChild(UiTheme.Label(Pad.AllPads().Count == 0
                    ? "Connect a controller to change the mapping."
                    : "Scanline doesn't know this controller's layout. Use Map buttons in Eden on the Controller page.",
                UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
        }
        Dictionary<string, string> map;
        try
        {
            map = Host.Eden.ButtonMap();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            col.AddChild(UiTheme.Label($"Couldn't read the button mapping: {e.Message}", UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
            return;
        }

        void Change(Action<PadInfo> act)
        {
            if (pad == null)
            {
                App.Error("Connect a controller Scanline knows (Steam Deck, Steam Input or an Xbox-style pad) to change the mapping.");
            }
            else
            {
                App.Guard(() => act(pad));
            }
            RefreshPane();
        }

        var preset = EdenButtonMap.PresetOf(map) ?? "custom";
        var layouts = new List<(string, string)>
        {
            ("position", "Switch positions"),
            ("label", "Xbox letters"),
        };
        if (preset == "custom")
        {
            layouts.Add(("custom", "Custom"));
        }
        var layout = new Stepper("Face buttons", layouts, preset,
            "Switch positions: A is the right face button, like on a Switch. Xbox letters: A is the bottom button, matching the letters on a Steam Deck or Xbox pad.")
        {
            Expands = true,
            DetailFor = v => v switch
            {
                "position" => "A right, B bottom, X top, Y left. Games' button prompts match where you press.",
                "label" => "A bottom, B right, X top, Y left. Press the letter the game shows.",
                _ => "Your own mapping. Pick a layout to start over from it.",
            },
        };
        layout.Changed += v =>
        {
            if (v != "custom")
            {
                Change(p => Host.Eden.ApplyButtonLayout(p, v == "label" ? EdenButtonMap.LabelLayout : EdenButtonMap.PositionLayout));
            }
        };
        Add(layout);

        foreach (var button in EdenButtonMap.Buttons)
        {
            var current = map.GetValueOrDefault(button.Key, EdenButtonMap.None);
            var choices = EdenButtonMap.Inputs.Select(i => (i.Id, i.Label)).ToList();
            if (current == EdenButtonMap.Other)
            {
                choices.Insert(0, (EdenButtonMap.Other, "Set in Eden"));
            }
            var key = button.Key;
            var row = new Stepper(button.Label, choices, current, $"The controller button that presses Switch {button.Label} in games.");
            row.Changed += v =>
            {
                if (v != EdenButtonMap.Other)
                {
                    Change(p => Host.Eden.SetButton(p, key, v));
                }
            };
            Add(row);
        }
    }

    private void BuildLibrary(VBoxContainer col, Action<Control> Add)
    {
        foreach (var f in Host.Settings.LibraryFolders)
        {
            var folder = f;
            Add(new ActionButton(folder.Path, () => App.Push(new ChoicePage(folder.Path,
                ("Keep", () => { }),
                ("Remove from library", () =>
                {
                    Host.SaveSettings(Host.Settings with { LibraryFolders = Host.Settings.LibraryFolders.Where(x => x.Path != folder.Path).ToList() });
                    Rescan();
                }))), "Your games are never moved or deleted; removing only stops scanning this folder."));
        }
        Add(new ActionButton("Add folder", () => App.Push(new FilePicker("Choose a game folder", pickFolder: true, dir =>
        {
            if (Host.Settings.LibraryFolders.All(x => x.Path != dir))
            {
                Host.SaveSettings(Host.Settings with { LibraryFolders = [.. Host.Settings.LibraryFolders, new LibraryFolder(dir)] });
            }
            Rescan();
        }))));
        Add(new ActionButton("Rescan library", Rescan));
    }

    private string? _pendingTheme;
    private string? _pendingBase;
    private Dictionary<string, string>? _pendingAccents;

    private void BuildAppearance(VBoxContainer col, Action<Control> Add)
    {
        // Theme and accent are picked here and applied by restarting the app: switching them live
        // left the library unable to open games until the next start.
        var active = UiTheme.Name;
        if (_pendingBase != active)
        {
            _pendingBase = active;
            _pendingTheme = null;
            _pendingAccents = null;
        }
        _pendingTheme ??= active;
        _pendingAccents ??= new Dictionary<string, string>(Host.Settings.Accents);
        var themeId = _pendingTheme;
        var theme = new Stepper("Theme", UiTheme.Themes.Select(t => (t.Id, t.Label + (t.Id == active ? "  (current)" : ""))), themeId,
            "Pick a theme, then choose Restart to apply. The app restarts and comes back here.")
        {
            Expands = true,
            DetailFor = v => UiTheme.Themes.FirstOrDefault(t => t.Id == v)?.Blurb,
        };
        theme.Changed += v =>
        {
            _pendingTheme = v;
            RefreshPane();  // the accent row's choices depend on the theme
        };
        Add(theme);
        var pocket = themeId == "pocket";
        var accent = new Stepper(pocket ? "Screen colour" : "Accent colour",
            [("", pocket ? "Classic green" : "Theme's own"), .. UiTheme.SwatchesFor(UiTheme.Themes.First(t => t.Id == themeId))],
            _pendingAccents.GetValueOrDefault(themeId) ?? "",
            pocket ? "Pocket LCD's four shades, in another screen colour. Saved for this theme." : "The highlight colour for this theme. Each theme remembers its own.")
        {
            Expands = true,
            DetailFor = v => v == ""
                ? (pocket ? "The original pea-green screen." : "The colour this theme was designed with.")
                : (pocket ? "All four shades change together, so it still looks like one screen." : "Focus, selected values and highlights use this colour."),
        };
        accent.Changed += v =>
        {
            if (v == "")
            {
                _pendingAccents.Remove(themeId);
            }
            else
            {
                _pendingAccents[themeId] = v;
            }
            RefreshPane();
        };
        Add(accent);
        var changed = themeId != active ||
                      (_pendingAccents.GetValueOrDefault(themeId) ?? "") != (Host.Settings.Accents.GetValueOrDefault(themeId) ?? "");
        if (changed)
        {
            var label = UiTheme.Themes.First(t => t.Id == themeId).Label;
            Add(new ActionButton($"↻  Restart to apply {label}", () =>
            {
                Host.SaveSettings(Host.Settings with { Theme = themeId, Accents = new Dictionary<string, string>(_pendingAccents) });
                App.RestartApp("--open=appearance");
            }, "Saves the theme and restarts the app. You come back to this page."));
        }
        Add(Toggle.Create("Animated background", Host.Settings.AnimatedBackground, on =>
        {
            Host.SaveSettings(Host.Settings with { AnimatedBackground = on });
            App.RefreshBackground();
        }, "A slow animation behind the menus that matches the theme. It stops while a game runs.",
            "Each theme has its own: drifting lights, a pulsing grid, phosphor rain, pixel clouds, a scrolling floor, an LCD shimmer or spinning shapes.",
            "A plain background. Uses a little less battery."));
        var crt = new Stepper("CRT effect", [("off", "Off"), ("1", "Subtle"), ("2", "Medium"), ("3", "Strong")],
            Host.Settings.Crt ? Host.Settings.CrtLevel.ToString() : "off",
            "Makes the whole screen look like an old TV. Works with every theme.")
        {
            Expands = true,
            DetailFor = v => v switch
            {
                "off" => "No effect. Retro terminal still has its own light scanlines.",
                "1" => "Faint scanlines, a slight wobble and a gentle tube curve.",
                "2" => "Scanlines, a wobbling picture, a rolling band, colour fringes and dark edges.",
                _ => "The full old-TV look. Text gets a little softer.",
            },
        };
        crt.Changed += v =>
        {
            Host.SaveSettings(v == "off" ? Host.Settings with { Crt = false } : Host.Settings with { Crt = true, CrtLevel = int.Parse(v) });
            App.RefreshBackground();
        };
        Add(crt);
        Add(Toggle.Create("Animations", !Host.Settings.ReduceMotion, on =>
        {
            Host.SaveSettings(Host.Settings with { ReduceMotion = !on });
            Anim.Enabled = on;
        }, "Sliding focus, smooth scrolling and page transitions.", null,
            "Everything changes instantly, nothing slides or fades."));
    }

    private void BuildSteam(VBoxContainer col, Action<Control> Add)
    {
        var steam = Host.Steam;
        col.AddChild(UiTheme.Label("Games you add show up in Steam's library and Game Mode. Starting one there opens it straight in Eden with its settings from Scanline. Use Add to Steam on a game's page for one game.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        if (steam.Unavailable is { } why)
        {
            col.AddChild(UiTheme.Label(why, UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
            return;
        }
        var managed = steam.Wanted().Count(e => !e.Remove);
        var pending = steam.Pending();
        col.AddChild(UiTheme.Label(
            pending.Count > 0
                ? $"{managed} shortcut(s) from Scanline, {pending.Count} waiting for Steam to restart."
                : $"{managed} shortcut(s) from Scanline, all in Steam.",
            UiTheme.SmallSize + 2, pending.Count > 0 ? UiTheme.Warn : UiTheme.Good, wrap: true));
        Add(new ActionButton(steam.Has("app") ? "Update the Scanline shortcut" : "Add Scanline to Steam",
            () => App.AddToSteam("app", steam.ForApp()),
            "A Steam shortcut for Scanline itself, so it starts in Game Mode with full controller support."));
        Add(new ActionButton($"Add all {Host.Library.Count} games to Steam", () =>
        {
            if (!App.Guard(() =>
            {
                foreach (var g in Host.Library)
                {
                    steam.Add(g.GameId, steam.ForGame(g));
                }
            }))
            {
                return;
            }
            App.RenderSteamArt();
            if (steam.Pending().Count > 0)
            {
                App.OfferSteamRestart($"{Host.Library.Count} games are ready for Steam. Steam picks up new shortcuts when it restarts.");
            }
            else
            {
                App.Toast($"Added {Host.Library.Count} games to Steam.");
                RefreshPane();
            }
        }, "One Steam shortcut per game in your library. Games already added are updated, not duplicated."));
        if (pending.Count > 0)
        {
            var gameMode = EdenDeck.Host.SteamService.GameMode;
            Add(new ActionButton(gameMode ? $"Restart the Deck to add {pending.Count}" : $"Restart Steam to add {pending.Count}",
                () => App.OfferSteamRestart("Steam picks up new shortcuts when it restarts."),
                gameMode ? "Restarts the Deck and writes the shortcuts while it shuts down." : "Closes Steam, writes the shortcuts and lets Steam start again."));
        }
    }

    private void BuildUpdates(VBoxContainer col, Action<Control> Add)
    {
        var updater = Host.Updater;
        col.AddChild(UiTheme.Label($"Scanline {AppUpdater.DisplayVersion(updater.CurrentVersion)}", UiTheme.FontSize + 6, UiTheme.Text));
        Add(new ActionButton("Changelog", App.ShowChangelog, "What changed in each version of Scanline."));
        if (updater.CanSelfUpdate)
        {
            Add(new ActionButton("Check for updates", () => App.CheckForAppUpdate(silent: false),
                "Looks for a newer Scanline on GitHub. Updates install in place and keep your games, keys and settings."));
            Add(Toggle.Create("Check at startup", Host.Settings.CheckForUpdates,
                on => Host.SaveSettings(Host.Settings with { CheckForUpdates = on }),
                "Asks when a new version is out. Never interrupts a game."));
            if (updater.CanRollback)
            {
                var prev = updater.PreviousVersion is { } pv ? $" ({pv})" : "";
                Add(new ActionButton($"Roll back to previous version{prev}", () => App.Confirm(
                    $"Go back to the previous Scanline{prev}? The app restarts.", "Roll back", App.RollbackApp)));
            }
        }
        else
        {
            col.AddChild(UiTheme.Label("This copy can't update itself (development build or package). Use the installer to update.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        col.AddChild(UiTheme.Label("Scanline never downloads keys, firmware or games. Use your own dumps.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
    }

    private void BuildAdvanced(VBoxContainer col, Action<Control> Add)
    {
        Add(Toggle.Create("Performance overlay layer", !Host.Settings.LayerKillSwitch,
            on => Host.SaveSettings(Host.Settings with { LayerKillSwitch = !on }),
            "Scanline's Vulkan layer (FPS and frame-time stats).",
            "Shows stats in game. It's tiny, but turn it off if a game crashes or looks wrong only with Scanline.",
            "No overlay and no stats; games run exactly as in plain Eden."));
        col.AddChild(Section("Expert Eden options"));
        BuildEdenOptions(col, Add, o => AdvancedOptions.Contains(o.Id), showGroups: true);
    }

    private void Rescan() =>
        App.Busy("Scanning library…", (_, ct) => Task.FromResult(Host.Rescan(ct)), games =>
        {
            LibraryToast(games.Count);
            Rebuild();
        });

    private void LibraryToast(int count)
    {
        var copying = Host.LibraryService.LastSkippedInProgress;
        App.Success(copying > 0 ? $"{count} games ({copying} still copying, rescan when done)" : $"{count} games");
    }

}
