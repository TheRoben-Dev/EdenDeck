using Godot;

namespace EdenDeck.App.Ui;

/// <summary>Bottom bar: help for the focused control on the left, button hints on the right.</summary>
public partial class HintBar : HBoxContainer
{
    private readonly Label _help = UiTheme.Label("", UiTheme.SmallSize + 1, UiTheme.Dim);
    private readonly HBoxContainer _hints = new();

    public HintBar()
    {
        CustomMinimumSize = new Vector2(0, 40);
        _help.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _help.ClipText = true;
        _help.VerticalAlignment = VerticalAlignment.Center;
        AddChild(_help);
        _hints.AddThemeConstantOverride("separation", 22);
        AddChild(_hints);
    }

    public void SetHelp(string text) => _help.Text = text;

    public void SetHints(IEnumerable<(string Action, string Label)> hints)
    {
        foreach (var c in _hints.GetChildren())
        {
            c.QueueFree();
        }
        foreach (var (action, label) in hints)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var glyph = UiTheme.Label(Pad.Glyph(action), UiTheme.SmallSize, Colors.White);
            var badge = new PanelContainer();
            var box = UiTheme.Box(UiTheme.PanelHi, 1, 14, UiTheme.Dim.Darkened(0.3f));
            box.ContentMarginLeft = box.ContentMarginRight = 9;
            box.ContentMarginTop = box.ContentMarginBottom = 2;
            badge.AddThemeStyleboxOverride("panel", box);
            badge.AddChild(glyph);
            row.AddChild(badge);
            row.AddChild(UiTheme.Label(label, UiTheme.SmallSize + 1, UiTheme.Dim));
            _hints.AddChild(row);
        }
    }
}
