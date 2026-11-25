using EdenDeck.App.Ui;
using EdenDeck.Backends.Pcsx2;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// One PS2 game's patches (widescreen, no-interlacing, 60 fps…) from PCSX2's community patches,
/// and the player's own cheats (.pnach). The switches are PCSX2's own, in the game's settings file,
/// so PCSX2's Game Properties show the same thing. Needs the game's CRC, which a .chd only gives
/// after its first start.
/// </summary>
public partial class Pcsx2PatchesPage : Screen
{
    private readonly LibraryGame _game;
    private readonly Action? _closed;
    private Control? _first;

    public Pcsx2PatchesPage(LibraryGame game, Action? closed = null) => (_game, _closed) = (game, closed);

    public override bool OnBack()
    {
        App.Pop();
        _closed?.Invoke();
        return true;
    }

    private static Pcsx2Backend Pcsx2 => Host.Pcsx2;

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

        var serial = _game.Base.TitleId;
        if (Pcsx2.CrcOf(_game.Base) is not { } crc)
        {
            col.AddChild(UiTheme.Label("Start this game once first. PCSX2 names patches and cheats after the disc's own checksum, which Scanline can only read from this kind of file after PCSX2 has run it.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        else
        {
            var patches = Pcsx2.Patches;
            col.AddChild(Section("Patches"));
            if (!patches.HasPatchList(serial, crc))
            {
                col.AddChild(UiTheme.Label("PCSX2's community patches add things like widescreen, no-interlacing and 60 fps modes. Look them up to see what's there for this game.",
                    UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            else
            {
                var list = Try(() => patches.Community(serial, crc), [], "patches", col);
                if (list.Count == 0)
                {
                    col.AddChild(UiTheme.Label("PCSX2's community patches have nothing for this version of the game.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
                }
                foreach (var p in list)
                {
                    var patch = p;
                    var help = string.Join(" ", new[] { p.Description, p.Author is { Length: > 0 } a ? $"By {a}." : null }.Where(x => !string.IsNullOrEmpty(x)));
                    Add(Toggle.Create(p.Name, Try(() => patches.IsPatchOn(serial, crc, patch.Name), false, "patch settings", col), on =>
                    {
                        if (App.Guard(() => patches.SetPatch(serial, crc, patch.Name, on)))
                        {
                            Rebuild();
                        }
                    }, help.Length > 0 ? help : "A community patch for this game.", "Applied when the game starts.", "Off."));
                }
                if (list.Any(p => p.Name.Contains("fps", StringComparison.OrdinalIgnoreCase)))
                {
                    col.AddChild(UiTheme.Label("With a 60 fps patch on, set this game's own frame rate to 60 on its card so frame generation stays off.",
                        UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
                }
            }
            Add(new ActionButton(patches.HasPatchList(serial, crc) ? "Look again for patches" : "Look up patches", () => Fetch(serial, crc),
                "Reads this game's entry in PCSX2's community patch collection (the same patches PCSX2 carries). Nothing is fetched until you press this."));
            col.AddChild(UiTheme.Label(Pcsx2Patches.Credit, UiTheme.SmallSize, UiTheme.Dim, wrap: true));

            col.AddChild(Section("Your cheats"));
            var cheats = Try(() => patches.Cheats(serial, crc), [], "cheats", col);
            if (cheats.Count == 0)
            {
                col.AddChild(UiTheme.Label($"Add a PCSX2 cheat file (.pnach) you have for this game. It should be for {serial}, checksum {crc:X8}.",
                    UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            foreach (var c in cheats)
            {
                var cheat = c;
                Add(Toggle.Create(c.Name, Try(() => patches.IsCheatOn(serial, crc, cheat.Name), false, "cheat settings", col), on =>
                {
                    if (App.Guard(() => patches.SetCheat(serial, crc, cheat.Name, on)))
                    {
                        Rebuild();
                    }
                }, c.Description ?? "A cheat from your file.", "On from the next start.", "Off."));
            }
            Add(new ActionButton(cheats.Count == 0 ? "Add a cheat file" : "Replace the cheat file", () =>
                App.Push(new FilePicker("Choose a .pnach cheat file", pickFolder: false, path => App.Guard(() =>
                {
                    var n = patches.ImportCheats(serial, crc, path);
                    App.Success($"{n} cheat{(n == 1 ? "" : "s")} added. Switch on the ones you want.");
                    Rebuild();
                }), null, ".pnach")), "Copies your own .pnach file into PCSX2's cheats folder for this game."));
        }

        Add(new ActionButton("Back", () => OnBack()));
        Focus.Column(items);
        Anim.Stagger(items, 0.02);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();

    private void Fetch(string serial, uint crc) =>
        App.Busy("Looking up patches…", (_, ct) => Pcsx2.Patches.FetchAsync(serial, crc, ct), found =>
        {
            App.Success(found ? "Patches found." : "No patches for this game yet.");
            Rebuild();
        });
}
