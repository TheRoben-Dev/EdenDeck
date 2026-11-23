using EdenDeck.App.Ui;
using EdenDeck.Backends.Mgba;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// One mGBA game's patches and cheats: pick which ROM patch (IPS/UPS/BPS, e.g. a translation) mGBA
/// applies at start, install or remove patches, and get cheats from the libretro cheat database
/// and switch each one on or off. The ROM file itself is never changed.
/// </summary>
public partial class MgbaModsPage : Screen
{
    private readonly LibraryGame _game;
    private readonly Action? _closed;
    private Control? _first;

    public MgbaModsPage(LibraryGame game, Action? closed = null) => (_game, _closed) = (game, closed);

    public override bool OnBack()
    {
        App.Pop();
        _closed?.Invoke();
        return true;
    }

    private string Tid => _game.Base.TitleId!;
    private static MgbaMods Mods => Host.Mgba.Mods;

    protected override void Build()
    {
        var col = Column(10);
        AddChild(CardFrame(Scroller(col)));
        col.AddChild(UiTheme.Label("Patches & cheats", UiTheme.TitleSize - 6));
        col.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.FontSize, UiTheme.Dim));
        var items = new List<Control>();
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }

        col.AddChild(Section("Patches"));
        var patches = Try(() => Mods.Patches(Tid), [], "patches", col);
        if (patches.Count == 0)
        {
            col.AddChild(UiTheme.Label("No patches yet. Install an .ips, .ups or .bps file (translations, fixes, hacks). mGBA applies it when the game starts; your ROM file stays untouched.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        else
        {
            var active = Try(() => Mods.ActivePatch(Tid), null, "patches", col);
            var step = new Stepper("Patch", patches.Select(p => (p, Path.GetFileName(p))).Prepend(("", "None (original game)")), active ?? "",
                "The patch mGBA applies when the game starts. Saves of a patched game can differ from the original's.");
            step.Changed += v =>
            {
                if (App.Guard(() => Mods.SetActivePatch(Tid, v == "" ? null : v)))
                {
                    Rebuild();
                }
            };
            Add(step);
        }
        Add(new ActionButton("Install a patch", () => App.Push(new FilePicker("Choose a patch (.ips, .ups, .bps)", pickFolder: false,
            InstallPatch, null, MgbaMods.PatchExtensions)), "Copies the patch in and makes it the one in use. The file you pick stays where it is."));
        if (patches.Count > 0)
        {
            Add(new ActionButton("Remove a patch", () => App.Push(new ChoicePage("Remove which patch?",
                patches.Select(p => (Path.GetFileName(p), (Action)(() => App.Confirm($"Delete \"{Path.GetFileName(p)}\"?", "Delete", () =>
                {
                    if (App.Guard(() => Mods.RemovePatch(Tid, p)))
                    {
                        App.Success($"Removed {Path.GetFileName(p)}");
                        Rebuild();
                    }
                })))).Append(("Cancel", () => { })).ToArray())), "Deletes a patch you no longer want."));
        }

        col.AddChild(Section("Cheats"));
        var cheats = Try(() => Mods.Cheats(Tid), [], "cheats", col);
        if (cheats.Count == 0)
        {
            col.AddChild(UiTheme.Label("No cheats yet.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        for (var i = 0; i < cheats.Count; i++)
        {
            var index = i;
            Add(Toggle.Create(cheats[i].Name, cheats[i].Enabled, on =>
            {
                var list = Mods.Cheats(Tid).ToList();
                if (index < list.Count && App.Guard(() => Mods.SaveCheats(Tid, list.Select((c, n) => n == index ? c with { Enabled = on } : c).ToList())))
                {
                    Rebuild();
                }
            }, "", "On when the game starts.", "Off."));
        }
        Add(new ActionButton(cheats.Count == 0 ? "Get cheats" : "Get cheats again", GetCheats,
            "Downloads this game's cheats from the libretro cheat database. They start switched off; pick the ones you want. Nothing is downloaded until you press this."));
        if (cheats.Count > 0)
        {
            Add(new ActionButton("Remove all cheats", () => App.Confirm("Remove this game's cheats?", "Remove", () =>
            {
                if (App.Guard(() => Mods.SaveCheats(Tid, [])))
                {
                    Rebuild();
                }
            }), "Deletes the cheat list for this game."));
        }
        col.AddChild(UiTheme.Label(MgbaCheatDatabase.Credit, UiTheme.SmallSize, UiTheme.Dim, wrap: true));

        Add(new ActionButton("Back", () => OnBack()));
        Focus.Column(items);
        Anim.Stagger(items, 0.02);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();

    private void InstallPatch(string path)
    {
        if (App.Guard(() => Mods.InstallPatch(Tid, path)))
        {
            App.Success($"Installed {Path.GetFileName(path)}");
            Rebuild();
        }
    }

    private void GetCheats()
    {
        var system = MgbaBackend.SystemOf(Tid);
        App.Busy("Looking for cheats…", async (_, ct) =>
        {
            try
            {
                return await Host.Mgba.CheatDatabase.Find(_game.Base, system, ct);
            }
            catch (System.Text.Json.JsonException e)
            {
                throw new InvalidDataException("The cheat database sent something Scanline can't read: " + e.Message, e);
            }
        }, sources =>
        {
            if (sources.Count == 0)
            {
                App.Error("The cheat database has no cheats for this game. It matches by the ROM's file name, so a renamed file may not be found.");
            }
            else if (sources.Count == 1)
            {
                Download(sources[0]);
            }
            else
            {
                App.Push(new ChoicePage("The database has cheats for several cheat devices. Which list?",
                    sources.Select(s => (s.Name, (Action)(() => Download(s)))).Append(("Cancel", () => { })).ToArray()));
            }
        });
    }

    private void Download(MgbaCheatDatabase.Source source)
    {
        App.Busy("Getting cheats…", async (_, ct) => await Host.Mgba.CheatDatabase.Download(source, ct), cheats =>
        {
            if (cheats.Count == 0)
            {
                App.Error("That cheat list is empty.");
                return;
            }
            // Keep the on/off choice of cheats the player already had.
            var on = Mods.Cheats(Tid).Where(c => c.Enabled).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            if (App.Guard(() => Mods.SaveCheats(Tid, cheats.Select(c => c with { Enabled = on.Contains(c.Name) }).ToList())))
            {
                App.Success($"{cheats.Count} cheats ready. Switch on the ones you want.");
                Rebuild();
            }
        });
    }
}
