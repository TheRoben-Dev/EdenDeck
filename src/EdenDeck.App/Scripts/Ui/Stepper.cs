using Godot;

namespace EdenDeck.App.Ui;

/// <summary>
/// Controller-friendly value picker: a row showing "Caption   ‹ Value ›". Left/right (or A)
/// changes the value in place, so no popups or text fields are needed with a gamepad.
/// The help line for the focused control is shown in the hint bar (stored in TooltipText).
/// </summary>
public partial class Stepper : Button
{
    private readonly Label _caption;
    private readonly Label _value;
    private readonly List<(string Value, string Label)> _options;
    private int _index;

    public bool Wrap { get; init; } = true;
    public event Action<string>? Changed;

    public Stepper(string caption, IEnumerable<(string Value, string Label)> options, string? current, string help = "")
    {
        _options = options.ToList();
        _index = Math.Max(0, _options.FindIndex(o => o.Value == current));
        TooltipText = help;
        CustomMinimumSize = new Vector2(0, 54);
        FocusMode = FocusModeEnum.All;

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 18;
        row.OffsetRight = -18;
        _caption = UiTheme.Label(caption);
        _caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _caption.VerticalAlignment = VerticalAlignment.Center;
        _caption.MouseFilter = MouseFilterEnum.Ignore;
        _value = UiTheme.Label("", color: UiTheme.Accent.Lightened(0.35f));
        _value.VerticalAlignment = VerticalAlignment.Center;
        _value.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_caption);
        row.AddChild(_value);
        AddChild(row);

        Pressed += () => Step(1);
        Anim.PressFlash(this);
        Refresh();
    }

    public string Value => _options.Count > 0 ? _options[_index].Value : "";

    public override void _GuiInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_left", allowEcho: true))
        {
            Step(-1);
            AcceptEvent();
        }
        else if (e.IsActionPressed("ui_right", allowEcho: true))
        {
            Step(1);
            AcceptEvent();
        }
    }

    private void Step(int d)
    {
        if (_options.Count < 2 || Disabled)
        {
            return;
        }
        var next = _index + d;
        next = Wrap ? (next + _options.Count) % _options.Count : Math.Clamp(next, 0, _options.Count - 1);
        if (next == _index)
        {
            return;
        }
        _index = next;
        Refresh();
        Bump(d);
        Changed?.Invoke(Value);
    }

    /// <summary>The value nudges in the direction it changed and settles.</summary>
    private void Bump(int d)
    {
        if (!Anim.Enabled)
        {
            return;
        }
        _value.PivotOffset = _value.Size / 2;
        _value.Scale = new Vector2(1.12f, 1.12f);
        _value.Modulate = Colors.White with { A = 0.4f };
        var t = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back).SetParallel();
        t.TweenProperty(_value, "scale", Vector2.One, Anim.Normal);
        t.TweenProperty(_value, "modulate:a", 1f, Anim.Fast);
    }

    public void SetValue(string value, bool notify = false)
    {
        var i = _options.FindIndex(o => o.Value == value);
        if (i >= 0 && i != _index)
        {
            _index = i;
            Refresh();
            if (notify)
            {
                Changed?.Invoke(Value);
            }
        }
    }

    private void Refresh()
    {
        var label = _options.Count > 0 ? _options[_index].Label : "-";
        var canLeft = _options.Count > 1 && (Wrap || _index > 0);
        var canRight = _options.Count > 1 && (Wrap || _index < _options.Count - 1);
        _value.Text = $"{(canLeft ? "‹" : " ")}  {label}  {(canRight ? "›" : " ")}";
    }
}
