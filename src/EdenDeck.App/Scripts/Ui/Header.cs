using Godot;

namespace EdenDeck.App.Ui;

/// <summary>Top bar: app name plus the root tabs (shoulder buttons, Tab, or click), with an animated underline.</summary>
public partial class Header : HBoxContainer
{
    private readonly List<Label> _tabs = new();
    private readonly List<ColorRect> _bars = new();
    private readonly ButtonGlyph _prev = new();
    private readonly ButtonGlyph _next = new();
    private readonly HBoxContainer _tabRow = new();
    private int _current = -1;

    /// <summary>Mouse/trackpad click on a tab (desktop use).</summary>
    public event Action<int>? TabClicked;

    public Header(IEnumerable<string> tabs)
    {
        AddThemeConstantOverride("separation", 22);
        var name = UiTheme.Label(UiTheme.Terminal ? "> EDEN_DECK_" : UiTheme.Nightgrid ? "EDEN·DECK" : "EDEN DECK", 22, UiTheme.Nightgrid ? UiTheme.Text : UiTheme.Accent.Lightened(0.2f));
        name.CustomMinimumSize = new Vector2(170, 0);
        AddChild(name);
        AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _tabRow.AddThemeConstantOverride("separation", 30);
        _tabRow.AddChild(_prev);
        foreach (var t in tabs)
        {
            var index = _tabs.Count;
            var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
            col.AddThemeConstantOverride("separation", 4);
            col.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                {
                    TabClicked?.Invoke(index);
                }
            };
            var l = UiTheme.Label(UiTheme.Mono ? t.ToUpperInvariant() : t, UiTheme.Nightgrid ? 19 : 22);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.MouseFilter = MouseFilterEnum.Ignore;
            var bar = new ColorRect { Color = UiTheme.Accent, CustomMinimumSize = new Vector2(0, 3), MouseFilter = MouseFilterEnum.Ignore };
            bar.Scale = new Vector2(0, 1);
            bar.Resized += () => bar.PivotOffset = bar.Size / 2;
            col.AddChild(l);
            col.AddChild(bar);
            _tabs.Add(l);
            _bars.Add(bar);
            _tabRow.AddChild(col);
        }
        _tabRow.AddChild(_next);
        AddChild(_tabRow);
        AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        // Right side balances the name and shows the running version.
        var version = UiTheme.Label("v" + EdenDeck.Host.Updates.AppUpdater.DisplayVersion(EdenDeck.Host.Updates.AppUpdater.CurrentAppVersion()),
            UiTheme.SmallSize, UiTheme.Dim);
        version.CustomMinimumSize = new Vector2(170, 0);
        version.HorizontalAlignment = HorizontalAlignment.Right;
        version.VerticalAlignment = VerticalAlignment.Center;
        AddChild(version);
    }

    public void SetTab(int index, bool tabsActive)
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            var on = i == index;
            _tabs[i].AddThemeColorOverride("font_color", on ? UiTheme.Text : UiTheme.Dim.Darkened(0.15f));
            var target = new Vector2(on ? 1 : 0, 1);
            if (Anim.Enabled && IsInsideTree() && index != _current)
            {
                CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic)
                    .TweenProperty(_bars[i], "scale", target, Anim.Normal);
            }
            else
            {
                _bars[i].Scale = target;
            }
        }
        _current = index;
        _tabRow.Modulate = tabsActive ? Colors.White : new Color(1, 1, 1, 0.45f);
        _prev.SetGlyph(Pad.Glyph(Pad.TabPrev));
        _next.SetGlyph(Pad.Glyph(Pad.TabNext));
    }
}
