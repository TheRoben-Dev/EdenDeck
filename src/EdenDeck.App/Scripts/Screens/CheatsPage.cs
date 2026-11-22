using System.Globalization;
using EdenDeck.App.Ui;
using EdenDeck.Backends.Eden.Mods;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// Pick cheats from the database for one build of the game. Cheats only work on the build they
/// were made for, so the page opens on the build the game will run (its newest loaded update, or
/// the base game) and says which versions have cheats. Saving writes just the picked cheats (plus
/// any master code they depend on) as Scanline's cheat add-on.
/// </summary>
public partial class CheatsPage : Screen
{
    private readonly LibraryGame _game;
    private readonly GameCheats _cheats;
    private readonly uint _runningVersion;
    private readonly Action? _saved;
    private string? _build;
    private HashSet<string> _picked = new(StringComparer.Ordinal);
    private Control? _first;

    public CheatsPage(LibraryGame game, GameCheats cheats, uint runningVersion, Action? saved = null)
    {
        _saved = saved;
        _game = game;
        _cheats = cheats;
        _runningVersion = runningVersion;
        _build = cheats.BuildFor(runningVersion);
        LoadPicked();
    }

    private string Tid => _game.Base.TitleId!;

    private void LoadPicked()
    {
        _picked = _build == null
            ? new(StringComparer.Ordinal)
            : ScanlineCheats.Read(Host.Eden.Paths, Tid, _build).Where(c => !c.IsMaster).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static string Running(uint v) => v == 0 ? "the base game" : VersionLabel(v);

    private static string VersionLabel(uint v) => v == 0 ? "Base game" : $"Update {(v >> 16).ToString(CultureInfo.InvariantCulture)} (v{v.ToString(CultureInfo.InvariantCulture)})";

    protected override void Build()
    {
        var col = Column(8);
        AddChild(CardFrame(Scroller(col)));
        col.AddChild(UiTheme.Label("Cheats", UiTheme.TitleSize - 6));
        col.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.FontSize, UiTheme.Dim));
        var items = new List<Control>();
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }

        // Which build: the one the game will run, or another covered version if the player prefers.
        var covered = _cheats.CoveredVersions;
        var choices = covered.Select(v => (_cheats.BuildFor(v)!, VersionLabel(v))).GroupBy(x => x.Item1).Select(g => (g.Key, string.Join(", ", g.Select(x => x.Item2)))).ToList();
        foreach (var unknown in _cheats.ByBuild.Keys.Where(b => choices.All(c => c.Key != b)).Order(StringComparer.Ordinal))
        {
            choices.Add((unknown, $"Build {unknown[..8]}…"));
        }
        var runningBuild = _cheats.BuildFor(_runningVersion);
        col.AddChild(UiTheme.Label(
            runningBuild != null && _cheats.ByBuild.ContainsKey(runningBuild)
                ? $"Your game runs {Running(_runningVersion)}, and there are cheats for it."
                : $"Your game runs {Running(_runningVersion)}, which has no cheats in the database. Cheats only work on the version they were made for.",
            UiTheme.SmallSize + 1, runningBuild != null && _cheats.ByBuild.ContainsKey(runningBuild) ? UiTheme.Good : UiTheme.Warn, wrap: true));
        if (covered.Count > 0)
        {
            col.AddChild(UiTheme.Label("Versions with cheats: " + string.Join(", ", covered.Select(VersionLabel)), UiTheme.SmallSize, UiTheme.Dim, wrap: true));
        }
        if (_build == null || !_cheats.ByBuild.ContainsKey(_build))
        {
            _build = choices.FirstOrDefault().Key;
            LoadPicked();
        }
        if (choices.Count > 1)
        {
            var step = new Stepper("Version", choices, _build ?? "", "Cheats for another version only work once the game runs that version.")
            {
                Expands = true,
            };
            step.Changed += v =>
            {
                _build = v;
                LoadPicked();
                Rebuild();
            };
            Add(step);
        }

        var list = _cheats.For(_build);
        col.AddChild(Section($"{list.Count(c => !c.IsMaster)} cheats"));
        foreach (var c in list.Where(c => !c.IsMaster))
        {
            var name = c.Name;
            Add(Toggle.Create(name, _picked.Contains(name), on =>
            {
                if (on)
                {
                    _picked.Add(name);
                }
                else
                {
                    _picked.Remove(name);
                }
            }, "On when the game starts. Some cheats can crash a game or break a save; try one at a time."));
        }
        Add(new ActionButton("Save", Save, "Writes the cheats you turned on. You can change them here any time."));
        Add(new ActionButton("Back", () => App.Pop()));
        col.AddChild(UiTheme.Label(CheatDatabase.Credit, UiTheme.SmallSize, UiTheme.Dim, wrap: true));
        Focus.Column(items);
        Anim.Stagger(items, 0.02);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();

    private void Save()
    {
        if (_build == null)
        {
            return;
        }
        var all = _cheats.For(_build);
        var picked = all.Where(c => !c.IsMaster && _picked.Contains(c.Name)).ToList();
        // Master codes set up what the other cheats rely on: kept whenever any cheat is on.
        var save = picked.Count == 0 ? [] : all.Where(c => c.IsMaster).Concat(picked).ToList();
        if (!App.Guard(() =>
        {
            ScanlineCheats.Write(Host.Eden.Paths, Tid, _build, save);
            if (save.Count > 0)
            {
                Host.Eden.SetModEnabled(Tid, ScanlineCheats.ModName, true);
            }
        }))
        {
            return;
        }
        App.Pop();
        _saved?.Invoke();
        App.Success(picked.Count == 0 ? "Cheats off for this game" : $"{picked.Count} cheat{(picked.Count == 1 ? "" : "s")} on for next time you play");
    }
}
