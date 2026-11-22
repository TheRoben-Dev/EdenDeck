using EdenDeck.Backends.Eden.Config;
using EdenDeck.Host.Updates;
using EdenDeck.App.Ui;
using EdenDeck.Core.Library;
using EdenDeck.Core.Settings;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>Settings in two panes: categories on the left, the selected category on the right.</summary>
public partial class SettingsScreen : Screen
{
    // Two panes, console style: categories on the left, the selected category's options on the
    // right. Focusing a category shows its options; A or right moves into them, B or left on a
    // plain row goes back to the category.
    private static readonly (string Name, string Summary)[] Categories =
    [
        ("Controller", "Controller style, rumble, motion and button mapping."),
        ("Graphics", "Resolution, upscaling, anti-aliasing and VSync for all games."),
        ("System", "Console mode and speed limit for all games."),
        ("Library", "Game folders and rescanning."),
        ("Appearance", "Theme, CRT effect and animations."),
        ("Updates", "Version, changelog and updates."),
        ("Advanced", "Overlay layer and expert Eden options."),
    ];

    /// <summary>Eden options shown under Advanced instead of their group.</summary>
    private static readonly HashSet<string> AdvancedOptions =
        ["backend", "gpu_accuracy", "use_disk_shader_cache", "use_asynchronous_shaders", "cpu_accuracy", "use_multi_core"];

    private int _category;
    private readonly List<Button> _cats = new();
    private VBoxContainer? _pane;
    private ScrollContainer? _paneScroll;
    private readonly List<Control> _paneItems = new();

    protected override void Build()
    {
        _cats.Clear();
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 28);
        AddChild(row);

        var left = Column(6);
        left.SizeFlagsHorizontal = SizeFlags.Fill;
        left.CustomMinimumSize = new Vector2(280, 0);
        row.AddChild(left);
        left.AddChild(Section("Settings"));
        for (var i = 0; i < Categories.Length; i++)
        {
            var index = i;
            var b = new ActionButton(Categories[i].Name, () => EnterPane(), Categories[i].Summary);
            b.FocusEntered += () => SelectCategory(index);
            left.AddChild(b);
            _cats.Add(b);
        }

        _pane = Column(8);
        _paneScroll = Scroller(_pane);
        row.AddChild(_paneScroll);
        BuildPane();
    }

    private void SelectCategory(int index)
    {
        if (index == _category && _paneItems.Count > 0)
        {
            return;
        }
        _category = index;
        BuildPane();
        if (_paneScroll != null)
        {
            _paneScroll.ScrollVertical = 0;
        }
    }

    private void EnterPane()
    {
        if (_paneItems.Count > 0)
        {
            _paneItems[0].GrabFocus();
        }
    }

    public override bool OnBack()
    {
        var f = GetViewport()?.GuiGetFocusOwner();
        if (f != null && _pane != null && _pane.IsAncestorOf(f) && _category < _cats.Count)
        {
            _cats[_category].GrabFocus();
            return true;
        }
        return false;
    }

    private void BuildPane()
    {
        if (_pane == null)
        {
            return;
        }
        foreach (var c in _pane.GetChildren())
        {
            _pane.RemoveChild(c);
            c.QueueFree();
        }
        _paneItems.Clear();
        var col = _pane;
        var items = _paneItems;
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }
        col.AddChild(UiTheme.Label(Categories[_category].Name, UiTheme.TitleSize));
        switch (Categories[_category].Name)
        {
            case "Controller":
                BuildController(col, Add);
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
            case "Updates":
                BuildUpdates(col, Add);
                break;
            case "Advanced":
                BuildAdvanced(col, Add);
                break;
        }

        for (var i = 0; i < _cats.Count; i++)
        {
            _cats[i].AddThemeColorOverride("font_color", i == _category ? UiTheme.AccentText : UiTheme.Text);
        }
        var cat = _cats[_category];
        Focus.Column(_cats, right: items.FirstOrDefault());
        Focus.Column(items, left: cat);
        // Steppers and toggles use left/right for their value; B still goes back to the category.
        Anim.Stagger(items, 0.015);
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

    private void BuildAppearance(VBoxContainer col, Action<Control> Add)
    {
        var theme = new Stepper("Theme", UiTheme.Themes.Select(t => (t.Id, t.Label)), UiTheme.Name,
            "Changes apply right away, so you can flip through them here.")
        {
            Expands = true,
            DetailFor = v => UiTheme.Themes.FirstOrDefault(t => t.Id == v)?.Blurb,
        };
        theme.Changed += v =>
        {
            Host.SaveSettings(Host.Settings with { Theme = v });
            App.ApplyTheme(v);
        };
        Add(theme);
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

    private void BuildUpdates(VBoxContainer col, Action<Control> Add)
    {
        var updater = Host.Updater;
        col.AddChild(UiTheme.Label($"Eden Deck {AppUpdater.DisplayVersion(updater.CurrentVersion)}", UiTheme.FontSize + 6, UiTheme.Text));
        Add(new ActionButton("Changelog", App.ShowChangelog, "What changed in each version of Eden Deck."));
        if (updater.CanSelfUpdate)
        {
            Add(new ActionButton("Check for updates", () => App.CheckForAppUpdate(silent: false),
                "Looks for a newer Eden Deck on GitHub. Updates install in place and keep your games, keys and settings."));
            Add(Toggle.Create("Check at startup", Host.Settings.CheckForUpdates,
                on => Host.SaveSettings(Host.Settings with { CheckForUpdates = on }),
                "Asks when a new version is out. Never interrupts a game."));
            if (updater.CanRollback)
            {
                var prev = updater.PreviousVersion is { } pv ? $" ({pv})" : "";
                Add(new ActionButton($"Roll back to previous version{prev}", () => App.Confirm(
                    $"Go back to the previous Eden Deck{prev}? The app restarts.", "Roll back", App.RollbackApp)));
            }
        }
        else
        {
            col.AddChild(UiTheme.Label("This copy can't update itself (development build or package). Use the installer to update.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        col.AddChild(UiTheme.Label("Eden Deck never downloads keys, firmware or games. Use your own dumps.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
    }

    private void BuildAdvanced(VBoxContainer col, Action<Control> Add)
    {
        Add(Toggle.Create("Performance overlay layer", !Host.Settings.LayerKillSwitch,
            on => Host.SaveSettings(Host.Settings with { LayerKillSwitch = !on }),
            "Eden Deck's Vulkan layer (FPS and frame-time stats).",
            "Shows stats in game. It's tiny, but turn it off if a game crashes or looks wrong only with Eden Deck.",
            "No overlay and no stats; games run exactly as in plain Eden."));
        col.AddChild(Section("Expert Eden options"));
        BuildEdenOptions(col, Add, o => AdvancedOptions.Contains(o.Id), showGroups: true);
    }

    public override void FocusDefault() => (_category < _cats.Count ? _cats[_category] : null)?.GrabFocus();

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
