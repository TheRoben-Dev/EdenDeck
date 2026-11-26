using EdenDeck.App.Ui;
using EdenDeck.Backends.Duckstation;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// One PS1 game's patches (widescreen, 60 fps…) and cheats from DuckStation's community database,
/// and the player's own cheats (.cht). The switches are DuckStation's own, in the game's settings
/// file, so DuckStation's Game Properties show the same thing. Needs the game's serial.
/// </summary>
public partial class DuckstationPatchesPage : Screen
{
    private readonly LibraryGame _game;
    private readonly Action? _closed;
    private Control? _first;

    public DuckstationPatchesPage(LibraryGame game, Action? closed = null) => (_game, _closed) = (game, closed);

    public override bool OnBack()
    {
        App.Pop();
        _closed?.Invoke();
        return true;
    }

    private static DuckstationBackend Duckstation => Host.Duckstation;

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
        var cheats = Duckstation.Cheats;
        void Toggles(IEnumerable<DuckstationCheats.Code> codes, bool patch, string fallbackHelp, string onDetail)
        {
            foreach (var c in codes)
            {
                var code = c;
                var auto = patch && Try(() => cheats.IsAutomatic(serial, code), false, "patch settings", col);
                var help = string.Join(" ", new[]
                {
                    c.Description, c.Author is { Length: > 0 } a ? $"By {a}." : null,
                    auto ? "On by itself while widescreen rendering is on with a 16:9 picture." : null,
                }.Where(x => !string.IsNullOrEmpty(x)));
                var on = Try(() => patch ? cheats.IsPatchOn(serial, code) : cheats.IsCheatOn(serial, code.Name), false, patch ? "patch settings" : "cheat settings", col);
                Add(Toggle.Create(c.Name, on, v =>
                {
                    App.Guard(() =>
                    {
                        if (patch)
                        {
                            cheats.SetPatch(serial, code, v);
                        }
                        else
                        {
                            cheats.SetCheat(serial, code.Name, v);
                        }
                    });
                    Rebuild();  // shows the switch as it really is, also when it couldn't change
                }, help.Length > 0 ? help : fallbackHelp, onDetail, "Off."));
            }
        }

        if (string.IsNullOrEmpty(serial))
        {
            col.AddChild(UiTheme.Label("Start this game once first. DuckStation names patches and cheats after the disc's serial, which Scanline learns from DuckStation the first time it runs this file.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        else
        {
            var looked = cheats.HasLists(serial);
            col.AddChild(Section("Patches"));
            var patches = Try(() => cheats.Patches(serial), [], "patches", col);
            if (!looked && patches.Count == 0)
            {
                col.AddChild(UiTheme.Label("DuckStation's community database has patches like widescreen and 60 fps modes for many games. Look them up to see what's there for this game.",
                    UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            else if (patches.Count == 0)
            {
                col.AddChild(UiTheme.Label("DuckStation's database has no patches for this game.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            Toggles(patches, patch: true, "A community patch for this game.", "Applied when the game starts.");
            if (patches.Any(p => p.Name.Contains("fps", StringComparison.OrdinalIgnoreCase)))
            {
                col.AddChild(UiTheme.Label("With a 60 fps patch on, set this game's own frame rate to 60 on its card so frame generation stays off.",
                    UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            Add(new ActionButton(looked ? "Look again for patches and cheats" : "Look up patches and cheats", Fetch,
                "Reads this game's entries in DuckStation's community database (the same codes DuckStation carries). Nothing is fetched until you press this."));

            col.AddChild(Section("Cheats"));
            var db = Try(() => cheats.DatabaseCheats(serial), [], "cheats", col);
            var own = Try(() => cheats.Cheats(serial), [], "cheats", col);
            var all = own.Concat(db).DistinctBy(c => c.Name).ToList();
            if (all.Count == 0)
            {
                col.AddChild(UiTheme.Label(looked
                    ? $"No cheats in DuckStation's database for this game. You can add a DuckStation cheat file (.cht) you have for {serial}."
                    : $"Look up the game above for the database's cheats, or add a DuckStation cheat file (.cht) you have for {serial}.",
                    UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            Toggles(all, patch: false, "A cheat for this game.", "On from the next start.");
            if (all.Count > 0)
            {
                col.AddChild(UiTheme.Label("Cheats can crash games or break saves. Switch them on only for the play you want them in.",
                    UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            Add(new ActionButton(own.Count == 0 ? "Add a cheat file" : "Replace your cheat file", () =>
                App.Push(new FilePicker("Choose a .cht cheat file", pickFolder: false, path => App.Guard(() =>
                {
                    var n = cheats.ImportCheats(serial, path);
                    App.Success($"{n} cheat{(n == 1 ? "" : "s")} added. Switch on the ones you want.");
                    Rebuild();
                }), null, ".cht")), "Copies your own .cht file into DuckStation's cheats folder for this game."));
            col.AddChild(UiTheme.Label(DuckstationCheats.Credit, UiTheme.SmallSize, UiTheme.Dim, wrap: true));
        }

        Add(new ActionButton("Back", () => OnBack()));
        Focus.Column(items);
        Anim.Stagger(items, 0.02);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();

    private void Fetch() =>
        App.Busy("Looking up patches and cheats…", (_, ct) => Duckstation.Cheats.FetchAsync(_game.Base.TitleId, ct), found =>
        {
            App.Success(found ? "Patches and cheats found." : "Nothing for this game in the database yet.");
            Rebuild();
        });
}
