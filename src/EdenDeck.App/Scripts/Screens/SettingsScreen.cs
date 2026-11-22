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
                col.AddChild(UiTheme.Label(group, UiTheme.SmallSize + 1, UiTheme.Accent.Lightened(0.3f)));
            }
            var opt = o;
            Add(OptionRows.Global(o, global.GetValueOrDefault(o.Id), v =>
                App.Guard(() => settings.WriteGlobal(new Dictionary<string, string?> { [opt.Id] = v }))));
        }

        col.AddChild(Section("Appearance"));
        var theme = new Stepper("Theme", [("dark", "Dark"), ("terminal", "Retro terminal"), ("nightgrid", "Nightgrid")], Host.Settings.Theme,
            "Dark is the default. Retro terminal: phosphor green and scanlines. Nightgrid: void black, dot grid, magenta highlights.");
        theme.Changed += v =>
        {
            Host.SaveSettings(Host.Settings with { Theme = v });
            App.Reload();
        };
        Add(theme);
        Add(Toggle.Create("Animations", !Host.Settings.ReduceMotion, on =>
        {
            Host.SaveSettings(Host.Settings with { ReduceMotion = !on });
            Anim.Enabled = on;
        }, "Short transitions and focus effects. Turn off for a static UI."));

        col.AddChild(Section("Eden Deck"));
        Add(Toggle.Create("Performance overlay layer", !Host.Settings.LayerKillSwitch,
            on => Host.SaveSettings(Host.Settings with { LayerKillSwitch = !on }),
            "Eden Deck's Vulkan layer (FPS and frame-time stats). Turn off if a game misbehaves with it."));

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
