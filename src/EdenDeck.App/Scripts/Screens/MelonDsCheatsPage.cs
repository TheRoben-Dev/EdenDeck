using EdenDeck.App.Ui;
using EdenDeck.Backends.MelonDs;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// One DS game's cheats: codes from libretro-database's DS cheat list, and the player's own codes
/// from melonDS's cheat file for the game. The switches are melonDS's own (in that file), so its
/// cheat window shows the same thing.
/// </summary>
public partial class MelonDsCheatsPage : Screen
{
    private readonly LibraryGame _game;
    private readonly Action? _closed;
    private Control? _first;

    public MelonDsCheatsPage(LibraryGame game, Action? closed = null) => (_game, _closed) = (game, closed);

    public override bool OnBack()
    {
        App.Pop();
        _closed?.Invoke();
        return true;
    }

    private static MelonDsBackend MelonDs => Host.MelonDs;

    protected override void Build()
    {
        var col = Column(10);
        AddChild(CardFrame(Scroller(col)));
        col.AddChild(UiTheme.Label("Cheats", UiTheme.TitleSize - 6));
        col.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.FontSize, UiTheme.Dim));
        var items = new List<Control>();
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }

        var game = _game.Base;
        var cheats = MelonDs.Cheats;
        var code = MelonDsBackend.GameCode(game);
        var inFile = Try(() => cheats.FileCodes(game), [], "the cheat file", col);
        var db = code == null ? [] : Try(() => cheats.DatabaseCodes(code), [], "cheats", col);
        var looked = code != null && cheats.HasList(code);
        var listed = new HashSet<string>();

        void Row(string name, bool on, string help, MelonDsCheats.Code? fromDb)
        {
            listed.Add(name);
            Add(Toggle.Create(name, on, v =>
            {
                App.Guard(() => cheats.Set(game, name, v, fromDb));
                Rebuild();  // shows the switch as it really is, also when it couldn't change
            }, help, "On from the next start.", "Off."));
        }

        col.AddChild(Section("From the cheat database"));
        if (code == null || DsNames.CheatFile(code) == null)
        {
            col.AddChild(UiTheme.Label("The cheat database has no list for this game. Codes you add in melonDS's own cheat window show up below.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        else
        {
            if (!looked)
            {
                col.AddChild(UiTheme.Label("libretro's cheat database has Action Replay codes for this game. Look them up to see what's there.",
                    UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            foreach (var c in db)
            {
                var on = inFile.Any(x => x.Name == c.Name && x.On);
                Row(c.Name, on, "An Action Replay code from libretro's cheat database.", c);
            }
            Add(new ActionButton(looked ? "Look again for cheats" : "Look up cheats", Fetch,
                "Reads this game's list in libretro's cheat database (only the text list of codes). Nothing is fetched until you press this."));
        }

        var own = inFile.Where(c => !listed.Contains(c.Name)).ToList();
        if (own.Count > 0)
        {
            col.AddChild(Section("In melonDS's cheat file"));
            foreach (var c in own)
            {
                Row(c.Name, c.On, c.Category.Length > 0 ? $"A code in melonDS's cheat file ({c.Category})." : "A code in melonDS's cheat file.", null);
            }
        }
        col.AddChild(UiTheme.Label("Cheats can crash games or break saves. Switch them on only for the play you want them in. Add your own codes in melonDS's cheat window (Open melonDS under Emulators → melonDS).",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        col.AddChild(UiTheme.Label(MelonDsCheats.Credit, UiTheme.SmallSize, UiTheme.Dim, wrap: true));

        Add(new ActionButton("Back", () => OnBack()));
        Focus.Column(items);
        Anim.Stagger(items, 0.02);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();

    private void Fetch()
    {
        if (MelonDsBackend.GameCode(_game.Base) is not { } code)
        {
            return;
        }
        App.Busy("Looking up cheats…", (_, ct) => MelonDs.Cheats.FetchAsync(code, ct), found =>
        {
            App.Success(found ? "Cheats found." : "Nothing for this game in the database yet.");
            Rebuild();
        });
    }
}
