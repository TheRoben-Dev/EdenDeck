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

    /// <summary>Theme changed: pick up the new font and colours.</summary>
    public void Restyle() => _label.RemoveThemeColorOverride("font_color");

    public void Show(string text, Color? color = null)
    {
        _label.Text = text;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.PanelHi, 2, 12, color ?? UiTheme.Accent));
        _tween?.Kill();
        _tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        if (Anim.Enabled)
        {
            // Rise in from just below its spot.
            Modulate = Colors.Transparent;
            OffsetBottom = -66;
            OffsetTop = OffsetBottom - Size.Y;
            _tween.SetParallel();
            _tween.TweenProperty(this, "modulate", Colors.White, Anim.Normal);
            _tween.TweenProperty(this, "offset_bottom", -86f, Anim.Slow);
            _tween.TweenProperty(this, "offset_top", -86f - Size.Y, Anim.Slow);
            _tween.SetParallel(false);
        }
        else
        {
            Modulate = Colors.White;
        }
        _tween.TweenInterval(Math.Clamp(text.Length / 18.0, 2.5, 8));
        _tween.TweenProperty(this, "modulate", Colors.Transparent, 0.4);
    }
}
