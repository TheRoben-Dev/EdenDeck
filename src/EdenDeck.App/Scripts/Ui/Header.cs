using Godot;

namespace EdenDeck.App.Ui;

/// <summary>Top bar: app name plus the root tabs (switched with the shoulder buttons).</summary>
public partial class Header : HBoxContainer
{
    private readonly List<Label> _tabs = new();
    private readonly List<string> _names = new();
    private readonly Label _prev = UiTheme.Label("", UiTheme.SmallSize, UiTheme.Dim);
    private readonly Label _next = UiTheme.Label("", UiTheme.SmallSize, UiTheme.Dim);
    private readonly HBoxContainer _tabRow = new();

    public Header(IEnumerable<string> tabs)
    {
        AddThemeConstantOverride("separation", 22);
        var name = UiTheme.Label("EDEN DECK", 22, UiTheme.Accent.Lightened(0.2f));
        AddChild(name);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(spacer);
        _tabRow.AddThemeConstantOverride("separation", 26);
        _tabRow.AddChild(_prev);
        foreach (var t in tabs)
        {
            var l = UiTheme.Label(t, 22);
            _tabs.Add(l);
            _names.Add(t);
            _tabRow.AddChild(l);
        }
        _tabRow.AddChild(_next);
        AddChild(_tabRow);
        var spacer2 = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(spacer2);
        AddChild(new Control { CustomMinimumSize = new Vector2(120, 0) });  // balances the name
    }

    public void SetTab(int index, bool tabsActive)
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].AddThemeColorOverride("font_color", i == index ? UiTheme.Text : UiTheme.Dim.Darkened(0.2f));
            _tabs[i].Text = i == index ? $"▸ {_names[i]}" : _names[i];
        }
        _tabRow.Modulate = tabsActive ? Colors.White : new Color(1, 1, 1, 0.45f);
        _prev.Text = Pad.Glyph(Pad.TabPrev);
        _next.Text = Pad.Glyph(Pad.TabNext);
    }
}
