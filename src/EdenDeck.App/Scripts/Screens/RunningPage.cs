using EdenDeck.App.Ui;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>Shown while a game runs. Input is ignored until the emulator exits.</summary>
public partial class RunningPage : Screen
{
    private readonly string _title;
    private readonly IReadOnlyList<string> _notes;

    public RunningPage(string title, IReadOnlyList<string> notes)
    {
        _title = title;
        _notes = notes;
    }

    protected override void Build()
    {
        var center = new CenterContainer();
        AddChild(center);
        var col = Column(12);
        col.CustomMinimumSize = new Vector2(640, 0);
        center.AddChild(col);
        col.AddChild(UiTheme.Label("Playing", UiTheme.SmallSize, UiTheme.Dim));
        col.AddChild(UiTheme.Label(_title, UiTheme.TitleSize, wrap: true));
        foreach (var n in _notes)
        {
            col.AddChild(UiTheme.Label("• " + n, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        col.AddChild(UiTheme.Label("Close Eden to come back here.", UiTheme.SmallSize + 1, UiTheme.Dim));
    }

    public override void FocusDefault()
    {
    }

    public override bool OnBack() => true;
}
