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
            row.AddChild(new ButtonGlyph(Pad.Glyph(action)));
            var text = UiTheme.Label(UiTheme.Mono ? label.ToUpperInvariant() : label, UiTheme.SmallSize + 1, UiTheme.Dim);
            text.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(text);
            _hints.AddChild(row);
        }
    }
}
