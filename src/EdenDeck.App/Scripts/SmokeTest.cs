using EdenDeck.App.Ui;
using Godot;

namespace EdenDeck.App;

/// <summary>
/// `EdenDeck -- --smoke &lt;dir&gt;`: drives the UI with scripted input, saves a screenshot after
/// each step and quits. Used by CI (under Xvfb) to catch startup and navigation regressions,
/// and to publish screenshots of each build. Exit code 1 if any step throws.
/// </summary>
public partial class SmokeTest : Node
{
    private readonly string _outDir;
    private readonly Queue<(string Name, Action Act, double Wait, string? Expect)> _steps = new();
    private double _wait = 1.0;
    private int _shot;
    private int _failures;

    public SmokeTest(string outDir)
    {
        _outDir = outDir;
        Directory.CreateDirectory(outDir);
        Step("library", () => { });
        // Real joypad and keyboard events (not actions), through Pad's translation layer.
        Step("joy-dpad-right", () => Joy(JoyButton.DpadRight, true), wait: 0.25, expect: "Mountain Climb");
        Step("joy-dpad-release", () => Joy(JoyButton.DpadRight, false), expect: "Mountain Climb");
        Step("joy-stick-left", () => Stick(-1f), wait: 0.25, expect: "Castle Defenders");
        Step("joy-stick-center", () => Stick(0f), expect: "Castle Defenders");
        // Holding a direction auto-repeats after a short delay.
        Step("joy-dpad-hold", () => Joy(JoyButton.DpadRight, true), wait: 0.9, expect: "Sky Racers");
        Step("joy-dpad-hold-release", () => Joy(JoyButton.DpadRight, false), expect: "Sky Racers");
        Step("joy-dpad-down", () => { Joy(JoyButton.DpadDown, true); Joy(JoyButton.DpadDown, false); }, expect: "Starlight Saga");
        Step("joy-rb-tab", () => Tap(JoyButton.RightShoulder), expect: "Install or update");
        Step("joy-lb-tab", () => Tap(JoyButton.LeftShoulder), expect: "Castle Defenders");
        Step("key-tab", () => Key(Godot.Key.Tab), expect: "Install or update");
        Step("key-shift-tab", () => Key(Godot.Key.Tab, shift: true), expect: "Castle Defenders");
        Step("joy-a-open", () => Tap(JoyButton.A), expect: "Play");
        Step("joy-b-back", () => Tap(JoyButton.B), expect: "Castle Defenders");
        Step("game-page", () => Press(Pad.Accept));
        Step("game-stepper", () =>
        {
            Press("ui_down");
            Press("ui_down");
            Press("ui_right");
        });
        Step("game-settings", () =>
        {
            for (var i = 0; i < 6; i++)
            {
                Press("ui_down");
            }
        });
        Step("back-to-library", () => Press(Pad.Back));
        Step("running", () => Press(Pad.Tertiary));  // quick play: fake Eden in CI
        // The fake Eden exits at once: Eden Deck reports the early exit instead of silently returning.
        Step("returned", () => { }, wait: 3.0, expect: "OK");
        Step("early-exit-ok", () => Press(Pad.Accept), expect: "Castle Defenders");
        Step("eden", () => Press(Pad.TabNext));
        Step("settings", () => Press(Pad.TabNext));
        // Finger drag over the list scrolls it and must not press the row it started on.
        Step("touch-drag-scroll", () =>
        {
            Mouse(new Vector2(640, 600), true);
            for (var y = 580; y >= 260; y -= 40)
            {
                Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(640, y), GlobalPosition = new Vector2(640, y), ButtonMask = MouseButtonMask.Left, Relative = new Vector2(0, -40) });
            }
            Mouse(new Vector2(640, 260), false);
        }, expect: "Games/Switch");
        Step("settings-scrolled", () =>
        {
            for (var i = 0; i < 8; i++)
            {
                Press("ui_down");
            }
        });
        // Themes switch in place: still on Settings, focus stays on the Theme row.
        Step("theme-focus", () => FindFocusable(GetTree().Root, "Changes apply right away")?.GrabFocus(), expect: "Changes apply right away");
        for (var i = 1; i <= UiTheme.Themes.Count; i++)
        {
            Step($"theme-{i}", () => Press("ui_right"), wait: 0.8, expect: "Changes apply right away");
        }
        Step("choice", () =>
        {
            Press(Pad.TabNext);  // wraps to Library
            Press(Pad.TabPrev);  // Settings
            Press(Pad.Accept);   // first row: library folder -> choice page
        });
        Step("file-picker", () =>
        {
            Press(Pad.Accept);   // Keep
            Press("ui_down");
            Press(Pad.Accept);   // Add folder -> picker
        });
    }

    private void Step(string name, Action act, double wait = 1.0, string? expect = null) => _steps.Enqueue((name, act, wait, expect));

    private static Control? FindFocusable(Node root, string tooltip)
    {
        foreach (var c in root.GetChildren())
        {
            if (c is Control { FocusMode: Control.FocusModeEnum.All } ctl && ctl.TooltipText.StartsWith(tooltip, StringComparison.Ordinal) && ctl.IsVisibleInTree())
            {
                return ctl;
            }
            if (FindFocusable(c, tooltip) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static void Joy(JoyButton b, bool pressed) =>
        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = b, Pressed = pressed });

    private static void Mouse(Vector2 at, bool pressed) =>
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at });

    private static void Tap(JoyButton b)
    {
        Joy(b, true);
        Joy(b, false);
    }

    private static void Stick(float x) =>
        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = x });

    private static void Key(Key k, bool shift = false)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, ShiftPressed = shift, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, ShiftPressed = shift, Pressed = false });
    }

    private static void Press(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        Input.FlushBufferedEvents();
    }

    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait > 0)
        {
            return;
        }
        if (_steps.Count == 0)
        {
            GD.Print($"smoke: done, {_shot} screenshots, {_failures} failures");
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        var (name, act, wait, expect) = _steps.Dequeue();
        try
        {
            act();
        }
        catch (Exception e)
        {
            _failures++;
            GD.PushError($"smoke: step {name} failed: {e}");
        }
        // Give layout and focus a few frames to settle, then capture.
        _wait = wait;
        GetTree().CreateTimer(wait - 0.15).Timeout += () => Capture(name, expect);
    }

    private void Capture(string name, string? expect)
    {
        var focus = GetViewport().GuiGetFocusOwner();
        // Tiles draw their title with child labels, so fall back to the tooltip (which has the title).
        var text = focus is Button { Text: { Length: > 0 } t } ? t : focus?.TooltipText ?? "";
        if (expect != null && !text.Contains(expect))
        {
            _failures++;
            GD.PushError($"smoke: step {name} failed: expected focus on '{expect}', got '{text.Split('\n')[0]}'");
        }
        GD.Print($"smoke: {name}: focus={(focus == null ? "none" : focus.GetType().Name + " '" + text.Split('\n')[0] + "'")}");
        var img = GetViewport().GetTexture().GetImage();
        img?.SavePng(Path.Combine(_outDir, $"{++_shot:00}-{name}.png"));
    }
}
