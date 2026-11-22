using EdenDeck.Backends.Eden.Config;
using EdenDeck.Host.Updates;
using EdenDeck.App.Ui;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>Library folders, Eden's global settings and Eden Deck's own options.</summary>
public partial class SettingsScreen : Screen
{
    private Control? _first;

    protected override void Build()
    {
        var col = Column(8);
        AddChild(Scroller(col));
        var items = new List<Control>();
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }

        col.AddChild(Section("Library folders"));
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

        col.AddChild(Section("Eden settings (all games)"));
        col.AddChild(UiTheme.Label("Games can override these on their own page. Changes are saved straight to Eden's config, so close Eden first.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var settings = Host.Eden.Settings;
        var global = settings.ReadGlobal();
        string? group = null;
        foreach (var o in settings.Options)
        {
            if (o.Group != group)
            {
                group = o.Group;
                col.AddChild(UiTheme.Label(group, UiTheme.SmallSize + 1, UiTheme.AccentText));
            }
            var opt = o;
            Add(OptionRows.Global(o, global.GetValueOrDefault(o.Id), v =>
                App.Guard(() => settings.WriteGlobal(new Dictionary<string, string?> { [opt.Id] = v }))));
        }

        col.AddChild(Section("Controller"));
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

        col.AddChild(Section("Appearance"));
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
        Add(Toggle.Create("CRT scanlines", Host.Settings.Crt, on =>
        {
            Host.SaveSettings(Host.Settings with { Crt = on });
            App.RefreshBackground();
        }, "Scanlines and darkened edges over the whole screen, like an old TV.",
            "Works with every theme. Retro terminal always has its own lighter scanlines.", null));
        Add(Toggle.Create("Animations", !Host.Settings.ReduceMotion, on =>
        {
            Host.SaveSettings(Host.Settings with { ReduceMotion = !on });
            Anim.Enabled = on;
        }, "Sliding focus, smooth scrolling and page transitions.", null,
            "Everything changes instantly, nothing slides or fades."));

        col.AddChild(Section("Eden Deck"));
        Add(Toggle.Create("Performance overlay layer", !Host.Settings.LayerKillSwitch,
            on => Host.SaveSettings(Host.Settings with { LayerKillSwitch = !on }),
            "Eden Deck's Vulkan layer (FPS and frame-time stats).",
            "Shows stats in game. It's tiny, but turn it off if a game crashes or looks wrong only with Eden Deck.",
            "No overlay and no stats; games run exactly as in plain Eden."));

        col.AddChild(Section("Updates"));
        var updater = Host.Updater;
        col.AddChild(UiTheme.Label($"Eden Deck {AppUpdater.DisplayVersion(updater.CurrentVersion)}", UiTheme.FontSize + 6, UiTheme.Text));
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

        Focus.Column(items);
        Anim.Stagger(items, 0.015);
        _first = items.FirstOrDefault();
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

    public override void FocusDefault() => _first?.GrabFocus();
}
