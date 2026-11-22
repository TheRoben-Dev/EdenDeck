using Godot;

namespace EdenDeck.App.Ui;

/// <summary>Transient message at the bottom of the screen.</summary>
public partial class Toast : PanelContainer
{
    private readonly Label _label;
    private Tween? _tween;

    public Toast()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _label = UiTheme.Label("", wrap: true);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_label);
        Modulate = Colors.Transparent;
        SetAnchorsPreset(LayoutPreset.CenterBottom);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Begin;
        CustomMinimumSize = new Vector2(560, 0);
        OffsetBottom = -86;
    }

    public void Show(string text, Color? color = null)
    {
        _label.Text = text;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.PanelHi, 2, 12, color ?? UiTheme.Accent));
        _tween?.Kill();
        Modulate = Colors.White;
        _tween = CreateTween();
        _tween.TweenInterval(Math.Clamp(text.Length / 18.0, 2.5, 8));
        _tween.TweenProperty(this, "modulate", Colors.Transparent, 0.4);
    }
}
