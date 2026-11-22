using EdenDeck.App.Ui;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// Which collections one game is in. A fixed set of everyday shelves (no typing needed on a
/// controller), plus any other name already in use. The library's "Show" row filters by them.
/// </summary>
public partial class CollectionsPage : Screen
{
    /// <summary>Ready-made shelves offered for every game.</summary>
    public static readonly string[] Suggested = ["Playing now", "Up next", "Finished", "Multiplayer", "Party", "With the kids", "Comfort games"];

    private readonly LibraryGame _game;
    private readonly Action? _closed;
    private Control? _first;

    public CollectionsPage(LibraryGame game, Action? closed = null) => (_game, _closed) = (game, closed);

    public override bool OnBack()
    {
        App.Pop();
        _closed?.Invoke();
        return true;
    }

    protected override void Build()
    {
        var col = Column(10);
        AddChild(CardFrame(Scroller(col)));
        col.AddChild(UiTheme.Label("Collections", UiTheme.TitleSize - 6));
        col.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.FontSize, UiTheme.Dim));
        col.AddChild(UiTheme.Label("Put this game on any shelves you like. Pick one under Show in the library to see just those games.",
            UiTheme.SmallSize + 2, UiTheme.Text, wrap: true));
        var items = new List<Control>();
        var profile = Host.Profiles.Get(_game.GameId);
        var mine = profile.Collections ?? [];
        foreach (var name in Suggested.Concat(Host.Profiles.CollectionNames).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var n = name;
            var t = Toggle.Create(n, mine.Contains(n, StringComparer.OrdinalIgnoreCase), on => App.Guard(() =>
            {
                var p = Host.Profiles.Get(_game.GameId);
                Host.Profiles.Set(_game.GameId, p with { Collections = GameProfile.WithCollection(p.Collections, n, on) });
            }), $"Puts this game on the {n} shelf.");
            col.AddChild(t);
            items.Add(t);
        }
        var back = new ActionButton("Done", () => OnBack());
        col.AddChild(back);
        items.Add(back);
        Focus.Column(items);
        Anim.Stagger(items, 0.02);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
