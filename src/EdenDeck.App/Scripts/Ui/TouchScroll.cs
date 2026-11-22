using Godot;

namespace EdenDeck.App.Ui;

/// <summary>
/// Touchscreen behaviour for handhelds. Under gamescope/Steam the Deck's touchscreen usually
/// arrives as mouse events, so this works on those too:
/// - drag anywhere over a list scrolls it (with momentum), and a tap still presses;
/// - the cursor stays hidden unless a real mouse moves it (hover motion with no button held).
/// </summary>
public static class TouchScroll
{
    private const float Deadzone = 14;
    private static Vector2 _start;
    private static Vector2 _last;
    private static ScrollContainer? _target;
    private static bool _down;
    private static bool _dragging;
    private static float _velocity;
    private static ulong _lastMoveUs;
    private static float _hoverTravel;
    private static Control? _focusBeforePress;
    private static bool _injecting;

    /// <summary>Call from Main._Input before anything else. True = event consumed.</summary>
    public static bool Handle(Viewport vp, InputEvent e)
    {
        if (_injecting)
        {
            return false;  // our own synthetic release: let the GUI have it
        }
        switch (e)
        {
            case InputEventScreenTouch or InputEventScreenDrag:
                HideCursor();
                return false;  // Godot turns these into mouse events (emulate_mouse_from_touch)
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                return OnButton(vp, mb);
            case InputEventMouseMotion mm:
                return OnMotion(vp, mm);
            case InputEventJoypadButton or InputEventJoypadMotion or InputEventKey:
                HideCursor();
                _hoverTravel = 0;
                return false;
        }
        return false;
    }

    private static bool OnButton(Viewport vp, InputEventMouseButton mb)
    {
        if (mb.Pressed)
        {
            _down = true;
            _dragging = false;
            _velocity = 0;
            _start = _last = mb.Position;
            _target = ScrollerAt(vp, mb.Position);
            _focusBeforePress = vp.GuiGetFocusOwner();
            if (_hoverTravel < 40)
            {
                HideCursor();  // a press without hovering first: a finger, not a mouse
            }
            _hoverTravel = 0;
            return false;
        }
        _down = false;
        if (_dragging)
        {
            _dragging = false;
            if (_target != null && GodotObject.IsInstanceValid(_target))
            {
                var t = _target;
                // Re-enable after momentum has had a moment; the next d-pad move scrolls to focus again.
                t.GetTree().CreateTimer(1.2).Timeout += () =>
                {
                    if (GodotObject.IsInstanceValid(t))
                    {
                        t.FollowFocus = true;
                    }
                };
            }
            return true;  // swallow the release so the row under the finger doesn't fire
        }
        return false;
    }

    private static bool OnMotion(Viewport vp, InputEventMouseMotion mm)
    {
        if (!_down)
        {
            _hoverTravel += mm.Relative.Length();
            if (_hoverTravel > 40)
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;  // a real mouse/trackpad
            }
            return false;
        }
        if (_target == null || !GodotObject.IsInstanceValid(_target))
        {
            return false;
        }
        if (!_dragging && Math.Abs(mm.Position.Y - _start.Y) > Deadzone)
        {
            _dragging = true;
            // Cancel the press on whatever is under the finger: a release far away never fires it.
            _injecting = true;
            vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(-10000, -10000), GlobalPosition = new Vector2(-10000, -10000) });
            _injecting = false;
            _lastMoveUs = Time.GetTicksUsec();
            // The press also focused the row under the finger; a scroll shouldn't move focus.
            // Follow-focus is paused while dragging so restoring focus doesn't snap the list back.
            _target.FollowFocus = false;
            if (_focusBeforePress != null && GodotObject.IsInstanceValid(_focusBeforePress) && _focusBeforePress.IsVisibleInTree() &&
                vp.GuiGetFocusOwner() != _focusBeforePress)
            {
                _focusBeforePress.GrabFocus();
            }
        }
        if (!_dragging)
        {
            return false;
        }
        var dy = mm.Position.Y - _last.Y;
        _last = mm.Position;
        _target.ScrollVertical -= (int)Math.Round(dy);
        var now = Time.GetTicksUsec();
        var dt = Math.Max(1, now - _lastMoveUs) / 1_000_000f;
        _lastMoveUs = now;
        _velocity = Mathf.Clamp(Mathf.Lerp(_velocity, -dy / dt, 0.4f), -3000f, 3000f);
        return true;
    }

    /// <summary>Momentum after the finger lifts. Call every frame.</summary>
    public static void Process(double delta)
    {
        if (_down || Math.Abs(_velocity) < 20 || _target == null || !GodotObject.IsInstanceValid(_target))
        {
            _velocity = 0;
            return;
        }
        _target.ScrollVertical += (int)Math.Round(_velocity * delta);
        _velocity *= (float)Math.Pow(0.05, delta);  // ~95% of the speed gone after a second
    }

    private static void HideCursor()
    {
        if (Input.MouseMode != Input.MouseModeEnum.Hidden)
        {
            Input.MouseMode = Input.MouseModeEnum.Hidden;
        }
    }

    private static ScrollContainer? ScrollerAt(Viewport vp, Vector2 pos)
    {
        // The deepest visible scroller under the point.
        ScrollContainer? found = null;
        void Walk(Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is ScrollContainer s && s.IsVisibleInTree() && s.GetGlobalRect().HasPoint(pos) && !BlockedByFocusOff(s))
                {
                    found = s;
                }
                Walk(c);
            }
        }
        Walk(vp);
        return found;
    }

    // Pages behind an overlay card have focus turned off; don't scroll them.
    private static bool BlockedByFocusOff(ScrollContainer s) => s.HasMeta("ed_focus");
}
