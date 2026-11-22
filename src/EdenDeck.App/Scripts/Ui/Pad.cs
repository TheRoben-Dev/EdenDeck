using Godot;

namespace EdenDeck.App.Ui;

public enum PadFamily
{
    Keyboard,
    Xbox,
    PlayStation,
    Nintendo,
}

/// <summary>
/// App-side controller handling. The app always uses the button *labelled* A to confirm and B to
/// go back, on every controller, whatever remaps the user sets up for games (those live in the
/// emulator's own input config and never touch this). Joypad buttons are translated to actions
/// here instead of through the InputMap so the Nintendo A/B swap can be done per device, and so
/// held directions repeat at a controlled rate.
/// </summary>
public static class Pad
{
    public const string Accept = "ui_accept";
    public const string Back = "ui_cancel";
    public const string TabPrev = "tab_prev";
    public const string TabNext = "tab_next";
    /// <summary>North face button (Y on Xbox): context action on the current screen.</summary>
    public const string Secondary = "secondary";
    /// <summary>West face button (X on Xbox): second context action.</summary>
    public const string Tertiary = "tertiary";

    private static readonly string[] Directions = ["ui_up", "ui_down", "ui_left", "ui_right"];
    private const float RepeatDelay = 0.38f;
    private const float RepeatRate = 0.075f;
    private const float StickDeadzone = 0.55f;

    public static PadFamily LastFamily { get; private set; } = PadFamily.Keyboard;
    public static event Action? FamilyChanged;

    private static string? _held;
    private static float _heldFor;
    private static float _sinceRepeat;

    public static void Setup()
    {
        // Keyboard keeps Godot's defaults (arrows, Enter, Esc). Drop the default joypad events so
        // they don't fire twice alongside our own translation.
        foreach (var action in Directions.Concat([Accept, Back, "ui_select", "ui_focus_next", "ui_focus_prev"]))
        {
            foreach (var e in InputMap.ActionGetEvents(action))
            {
                if (e is InputEventJoypadButton or InputEventJoypadMotion)
                {
                    InputMap.ActionEraseEvent(action, e);
                }
            }
        }
        AddKeyAction(TabPrev, Key.Q, Key.Pageup);
        AddKeyAction(TabNext, Key.E, Key.Pagedown);
        AddKeyAction(Secondary, Key.R);
        AddKeyAction(Tertiary, Key.F);
    }

    private static void AddKeyAction(string action, params Key[] keys)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }
        foreach (var k in keys)
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
        }
    }

    public static PadFamily FamilyOf(int device)
    {
        var name = Input.GetJoyName(device).ToLowerInvariant();
        if (name.Contains("nintendo") || name.Contains("pro controller") || name.Contains("joy-con") || name.Contains("switch"))
        {
            return PadFamily.Nintendo;
        }
        if (name.Contains("playstation") || name.Contains("dualsense") || name.Contains("dualshock") || name.Contains("ps4") || name.Contains("ps5") || name.Contains("sony"))
        {
            return PadFamily.PlayStation;
        }
        return PadFamily.Xbox;  // Xbox, Steam Deck, ROG Ally, Steam Virtual Gamepad, generic XInput
    }

    /// <summary>
    /// Translates a raw input event. Returns true when the event was consumed (the caller should
    /// mark it handled); the translated action is injected with Input.ParseInputEvent.
    /// </summary>
    public static bool Translate(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true }:
                SetFamily(PadFamily.Keyboard);
                return false;
            case InputEventJoypadButton jb:
                SetFamily(FamilyOf(jb.Device));
                var action = ActionFor(jb.ButtonIndex, LastFamily);
                if (action == null)
                {
                    return true;
                }
                if (Directions.Contains(action))
                {
                    return true;  // d-pad is polled in Process() for repeat
                }
                Emit(action, jb.Pressed);
                return true;
            case InputEventJoypadMotion jm:
                if (Mathf.Abs(jm.AxisValue) > StickDeadzone)
                {
                    SetFamily(FamilyOf(jm.Device));
                }
                return true;  // sticks are polled in Process()
            default:
                return false;
        }
    }

    private static string? ActionFor(JoyButton b, PadFamily family)
    {
        // SDL reports buttons by position: A = south, B = east. On Nintendo pads the "A" label
        // is east, so swap to keep "the button labelled A confirms".
        var nintendo = family == PadFamily.Nintendo;
        return b switch
        {
            JoyButton.A => nintendo ? Back : Accept,
            JoyButton.B => nintendo ? Accept : Back,
            JoyButton.Y => nintendo ? Tertiary : Secondary,
            JoyButton.X => nintendo ? Secondary : Tertiary,
            JoyButton.LeftShoulder => TabPrev,
            JoyButton.RightShoulder => TabNext,
            JoyButton.DpadUp => "ui_up",
            JoyButton.DpadDown => "ui_down",
            JoyButton.DpadLeft => "ui_left",
            JoyButton.DpadRight => "ui_right",
            _ => null,
        };
    }

    /// <summary>Called every frame: d-pad and left stick with delayed auto-repeat.</summary>
    public static void Process(double delta)
    {
        var dir = CurrentDirection();
        if (dir != _held)
        {
            _held = dir;
            _heldFor = 0;
            _sinceRepeat = 0;
            if (dir != null)
            {
                Tap(dir);
            }
            return;
        }
        if (dir == null)
        {
            return;
        }
        _heldFor += (float)delta;
        if (_heldFor < RepeatDelay)
        {
            return;
        }
        _sinceRepeat += (float)delta;
        if (_sinceRepeat >= RepeatRate)
        {
            _sinceRepeat = 0;
            Tap(dir);
        }
    }

    private static string? CurrentDirection()
    {
        foreach (var dev in Input.GetConnectedJoypads())
        {
            if (Input.IsJoyButtonPressed(dev, JoyButton.DpadUp)) return "ui_up";
            if (Input.IsJoyButtonPressed(dev, JoyButton.DpadDown)) return "ui_down";
            if (Input.IsJoyButtonPressed(dev, JoyButton.DpadLeft)) return "ui_left";
            if (Input.IsJoyButtonPressed(dev, JoyButton.DpadRight)) return "ui_right";
            var x = Input.GetJoyAxis(dev, JoyAxis.LeftX);
            var y = Input.GetJoyAxis(dev, JoyAxis.LeftY);
            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) < StickDeadzone)
            {
                continue;
            }
            // Dominant axis only: no diagonal moves.
            return Mathf.Abs(x) > Mathf.Abs(y) ? (x < 0 ? "ui_left" : "ui_right") : (y < 0 ? "ui_up" : "ui_down");
        }
        return null;
    }

    private static void Tap(string action)
    {
        Emit(action, true);
        Emit(action, false);
    }

    private static void Emit(string action, bool pressed) =>
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });

    private static void SetFamily(PadFamily f)
    {
        if (f != LastFamily)
        {
            LastFamily = f;
            FamilyChanged?.Invoke();
        }
    }

    /// <summary>The label printed on the button for an action, for the current controller.</summary>
    public static string Glyph(string action) => (LastFamily, action) switch
    {
        (PadFamily.Keyboard, Accept) => "Enter",
        (PadFamily.Keyboard, Back) => "Esc",
        (PadFamily.Keyboard, TabPrev) => "Q",
        (PadFamily.Keyboard, TabNext) => "E",
        (PadFamily.Keyboard, Secondary) => "R",
        (PadFamily.Keyboard, Tertiary) => "F",
        (PadFamily.PlayStation, Accept) => "✕",
        (PadFamily.PlayStation, Back) => "○",
        (PadFamily.PlayStation, Secondary) => "△",
        (PadFamily.PlayStation, Tertiary) => "□",
        (PadFamily.PlayStation, TabPrev) => "L1",
        (PadFamily.PlayStation, TabNext) => "R1",
        (PadFamily.Nintendo, TabPrev) => "L",
        (PadFamily.Nintendo, TabNext) => "R",
        (_, Accept) => "A",
        (_, Back) => "B",
        (_, Secondary) => "Y",
        (_, Tertiary) => "X",
        (_, TabPrev) => "LB",
        (_, TabNext) => "RB",
        _ => "?",
    };
}
