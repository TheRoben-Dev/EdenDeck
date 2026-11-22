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

    // Direction state comes from the events themselves (not polling Input), so it works for any
    // device SDL reports and can be exercised by the smoke test with synthetic events.
    private static readonly List<string> _dpadHeld = new();
    private static readonly Dictionary<int, Vector2> _sticks = new();

    /// <summary>
    /// True on handheld PCs (Steam Deck, ROG Ally, Legion Go, MSI Claw). Used to show gamepad
    /// glyphs even when Steam's desktop layout turns the built-in controls into keyboard keys.
    /// </summary>
    public static bool IsHandheld { get; } = DetectHandheld();

    private static bool DetectHandheld()
    {
        try
        {
            string Read(string f) => File.Exists(f) ? File.ReadAllText(f).Trim() : "";
            var vendor = Read("/sys/class/dmi/id/board_vendor") + " " + Read("/sys/class/dmi/id/sys_vendor");
            var product = Read("/sys/class/dmi/id/product_name") + " " + Read("/sys/class/dmi/id/board_name");
            return vendor.Contains("Valve") || product.Contains("Jupiter") || product.Contains("Galileo")
                || product.Contains("ROG Ally") || product.Contains("RC71L") || product.Contains("RC72L")
                || product.Contains("Legion Go") || product.Contains("83E1") || product.Contains("Claw");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
        // Steam's Desktop Mode layout and handheld "lizard mode" send keys and mouse clicks
        // instead of gamepad buttons; accept the common ones.
        AddKeyAction(Back, Key.Backspace);
        InputMap.ActionAddEvent(Back, new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1 });
        InputMap.ActionAddEvent(TabPrev, new InputEventMouseButton { ButtonIndex = MouseButton.WheelLeft });
        InputMap.ActionAddEvent(TabNext, new InputEventMouseButton { ButtonIndex = MouseButton.WheelRight });
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
            case InputEventKey { Keycode: Key.Tab } tab:
                // Tab / Shift+Tab (and Ctrl+Tab) switch the top tabs; focus moves with arrows.
                if (tab.Pressed && !tab.Echo)
                {
                    SetFamily(PadFamily.Keyboard);
                    Emit(tab.ShiftPressed ? TabPrev : TabNext, true);
                    Emit(tab.ShiftPressed ? TabPrev : TabNext, false);
                }
                return true;
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
                    _dpadHeld.Remove(action);
                    if (jb.Pressed)
                    {
                        _dpadHeld.Add(action);  // most recent last
                        // Move on the press itself, so a tap shorter than a frame still counts
                        // (frames are slow in low-processor mode). Process() only repeats.
                        StartHold(action);
                    }
                    return true;
                }
                Emit(action, jb.Pressed);
                return true;
            case InputEventJoypadMotion jm:
                if (jm.Axis is JoyAxis.LeftX or JoyAxis.LeftY)
                {
                    var v = _sticks.GetValueOrDefault(jm.Device);
                    v = jm.Axis == JoyAxis.LeftX ? v with { X = jm.AxisValue } : v with { Y = jm.AxisValue };
                    _sticks[jm.Device] = v;
                }
                if (Mathf.Abs(jm.AxisValue) > StickDeadzone)
                {
                    SetFamily(FamilyOf(jm.Device));
                }
                return true;
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

    private static void StartHold(string dir)
    {
        _held = dir;
        _heldFor = 0;
        _sinceRepeat = 0;
        Tap(dir);
    }

    /// <summary>Called every frame: d-pad and left stick with delayed auto-repeat.</summary>
    public static void Process(double delta)
    {
        var dir = CurrentDirection();
        if (dir != _held)
        {
            if (dir == null)
            {
                _held = null;
            }
            else
            {
                StartHold(dir);
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
        if (_dpadHeld.Count > 0)
        {
            return _dpadHeld[^1];
        }
        foreach (var (x, y) in _sticks.Values.Select(v => (v.X, v.Y)))
        {
            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) < StickDeadzone)
            {
                continue;
            }
            // Dominant axis only: no diagonal moves.
            return Mathf.Abs(x) > Mathf.Abs(y) ? (x < 0 ? "ui_left" : "ui_right") : (y < 0 ? "ui_up" : "ui_down");
        }
        return null;
    }

    /// <summary>Forget held directions (on focus loss / game start) so nothing keeps repeating.</summary>
    public static void ReleaseAll()
    {
        _dpadHeld.Clear();
        _sticks.Clear();
        _held = null;
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

    /// <summary>The pad to give Eden as player 1: Steam's virtual gamepad first (Game Mode), else the first known one.</summary>
    public static EdenDeck.Backends.Eden.Config.PadInfo? ActivePad()
    {
        var pads = AllPads().Select(p => p with { Guid = SdlGuid(p) }).Where(p => p.Guid.Length == 32).ToList();
        return pads.FirstOrDefault(p => p.Vendor == 0x28de && p.Product == 0x11ff)
               ?? pads.FirstOrDefault(EdenDeck.Backends.Eden.Config.EdenControllerDefaults.HasKnownLayout);
    }

    public static List<EdenDeck.Backends.Eden.Config.PadInfo> AllPads() => Input.GetConnectedJoypads().Select(d =>
    {
        var info = Input.GetJoyInfo(d);
        int Id(string k) => info.TryGetValue(k, out var v) ? v.AsInt32() : 0;
        return new EdenDeck.Backends.Eden.Config.PadInfo(Input.GetJoyGuid(d), Input.GetJoyName(d), Id("vendor_id"), Id("product_id"));
    }).ToList();

    /// <summary>
    /// The GUID as SDL builds it for a USB pad, when Godot doesn't report one in that form:
    /// bus 0003, vendor and product little-endian, version 0001 (what the Steam Virtual Gamepad uses).
    /// </summary>
    private static string SdlGuid(EdenDeck.Backends.Eden.Config.PadInfo p)
    {
        if (p.Guid.Length == 32 && p.Guid.All(Uri.IsHexDigit) || p.Vendor == 0 || p.Product == 0)
        {
            return p.Guid;
        }
        static string Le(int v) => $"{v & 0xff:x2}{(v >> 8) & 0xff:x2}";
        return $"03000000{Le(p.Vendor)}0000{Le(p.Product)}000001000000";
    }

    /// <summary>One line per connected pad, for diagnostics ("name 28de:11ff guid").</summary>
    public static string DescribePads()
    {
        var pads = AllPads();
        return pads.Count == 0 ? "no controller detected" :
            string.Join("; ", pads.Select(p => $"{p.Name} [{p.Vendor:x4}:{p.Product:x4}, {(p.Guid.Length > 0 ? p.Guid : "no guid")}]"));
    }

    /// <summary>Which button set to draw: handhelds show Xbox glyphs even when Steam sends keys.</summary>
    public static PadFamily GlyphFamily => LastFamily == PadFamily.Keyboard && IsHandheld ? PadFamily.Xbox : LastFamily;

    /// <summary>The label printed on the button for an action, for the current controller.</summary>
    public static string Glyph(string action) => (GlyphFamily, action) switch
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
