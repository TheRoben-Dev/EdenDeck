using EdenDeck.App.Ui;
using EdenDeck.Backends.Eden;
using EdenDeck.Backends.Eden.Mods;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// One game's mods and cheats: switch mods on and off (Eden's own add-on switches), install from a
/// .zip or folder, remove, see which mods fight over the same files, and get cheats for the exact
/// build the game runs.
/// </summary>
public partial class ModsPage : Screen
{
    private readonly LibraryGame _game;
    private Control? _first;

    private readonly Action? _closed;

    /// <param name="closed">Called when the page closes (the game card refreshes its summary).</param>
    public ModsPage(LibraryGame game, Action? closed = null) => (_game, _closed) = (game, closed);

    public override bool OnBack()
    {
        App.Pop();
        _closed?.Invoke();
        return true;
    }

    private string Tid => _game.Base.TitleId!;

    protected override void Build()
    {
        var col = Column(10);
        AddChild(CardFrame(Scroller(col)));
        col.AddChild(UiTheme.Label("Mods & cheats", UiTheme.TitleSize - 6));
        col.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.FontSize, UiTheme.Dim));
        var items = new List<Control>();
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }

        col.AddChild(Section("Mods"));
        var mods = Try(() => Host.Eden.Mods(Tid), [], "mods", col);
        var shown = mods.Where(m => m.Name != ScanlineCheats.ModName).ToList();
        if (shown.Count == 0)
        {
            col.AddChild(UiTheme.Label("No mods yet. Install one from a .zip or a folder: anything with exefs, romfs or cheats folders inside.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        foreach (var m in shown)
        {
            var mod = m;
            Add(Toggle.Create(m.Name, m.Enabled, on =>
            {
                if (App.Guard(() => Host.Eden.SetModEnabled(Tid, mod.Name, on)))
                {
                    Rebuild();
                }
            }, $"{string.Join(", ", m.Kinds)} · {FormatSize(m.Bytes)}. Same switch as in Eden's add-ons list.",
                "Loaded when the game starts.", "Installed but not loaded."));
        }
        foreach (var (pair, files) in Try(() => EdenMods.Conflicts(mods), [], "mod conflicts", col)
                     .GroupBy(c => string.Join(" + ", c.Mods)).Select(g => (g.Key, g.ToList())))
        {
            var winner = files[0].Winner;
            var what = files.Count == 1 ? files[0].File : $"{files.Count} files";
            col.AddChild(UiTheme.Label($"!  {pair} both change {what}. {winner}'s version is used (Eden loads mods in name order).",
                UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
        }
        Add(new ActionButton("Install mod from a .zip", () => App.Push(new FilePicker("Choose a mod (.zip)", pickFolder: false,
            path => Install(path), null, ".zip")), "Finds the mod inside however the archive is laid out. Mods for another game are refused."));
        Add(new ActionButton("Install mod from a folder", () => App.Push(new FilePicker("Choose a mod folder", pickFolder: true,
            path => Install(path))), "Copies the mod; the folder you pick stays as it is."));
        if (shown.Count > 0)
        {
            Add(new ActionButton("Remove a mod", () => App.Push(new ChoicePage("Remove which mod? Its files are deleted from Eden's mod folder.",
                shown.Select(m => (m.Name, (Action)(() => App.Confirm($"Delete \"{m.Name}\"?", "Delete", () =>
                {
                    if (App.Guard(() => Host.Eden.RemoveMod(Tid, m.Name)))
                    {
                        App.Success($"Removed {m.Name}");
                        Rebuild();
                    }
                })))).Append(("Cancel", () => { })).ToArray())), "Deletes a mod you no longer want."));
        }

        col.AddChild(Section("Cheats"));
        var cheatMod = mods.FirstOrDefault(m => m.Name == ScanlineCheats.ModName);
        if (cheatMod != null)
        {
            Add(Toggle.Create("Your cheats", cheatMod.Enabled, on =>
            {
                if (App.Guard(() => Host.Eden.SetModEnabled(Tid, cheatMod.Name, on)))
                {
                    Rebuild();
                }
            }, "The cheats you picked here, as one add-on in Eden.", "On when the game starts.", "Kept, but not loaded."));
        }
        Add(new ActionButton(cheatMod == null ? "Get cheats" : "Change cheats", GetCheats,
            "Downloads this game's cheats from the community cheat database and lets you pick which ones to use. Nothing is downloaded until you press this."));
        if (!Try(() => ScanlineCheats.EnabledByDefault(Host.Eden.Paths.GlobalConfigFile), true, "cheat setting", col))
        {
            col.AddChild(UiTheme.Label("Eden's \"Enable cheats by default\" is off, so cheats start switched off in game.", UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
            Add(new ActionButton("Turn cheats on by default", () =>
            {
                if (App.Guard(() => ScanlineCheats.SetEnabledByDefault(Host.Eden.Paths.GlobalConfigFile, true)))
                {
                    Rebuild();
                }
            }, "Eden's own setting, for every game."));
        }

        var back = new ActionButton("Back", () => OnBack());
        Add(back);
        Focus.Column(items);
        Anim.Stagger(items, 0.02);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();

    private void Install(string path, bool replace = false)
    {
        try
        {
            var names = Host.Eden.InstallMod(Tid, path, replace);
            App.Success(names.Count == 1 ? $"Installed {names[0]}" : $"Installed {names.Count} mods");
            Rebuild();
        }
        catch (IOException e) when (!replace && e.Message.Contains("already installed", StringComparison.Ordinal))
        {
            App.Confirm(e.Message + " Replace it?", "Replace", () => Install(path, replace: true));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            App.Error(e.Message);
        }
    }

    private void GetCheats()
    {
        App.Busy("Getting cheats…", async (_, ct) =>
        {
            try
            {
                return await Host.Eden.Cheats.Fetch(Tid, ct);
            }
            catch (System.Text.Json.JsonException e)
            {
                throw new InvalidDataException("The cheat database sent something Scanline can't read: " + e.Message, e);
            }
        }, cheats =>
        {
            if (cheats == null || cheats.ByBuild.Count == 0)
            {
                App.Error("The cheat database has no cheats for this game.");
                return;
            }
            App.Push(new CheatsPage(_game, cheats, EdenBackend.GameVersion(_game, Host.Profiles.Get(_game.GameId).LoadAddOns), Rebuild));
        });
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1 << 30 ? $"{bytes / (double)(1 << 30):0.0} GB" : bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
