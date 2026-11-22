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
    private readonly Queue<(string Name, Action Act, double Wait)> _steps = new();
    private double _wait = 1.0;
    private int _shot;
    private int _failures;

    public SmokeTest(string outDir)
    {
        _outDir = outDir;
        Directory.CreateDirectory(outDir);
        Step("library", () => { });
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
        Step("returned", () => { }, wait: 3.0);
        Step("eden", () => Press(Pad.TabNext));
        Step("settings", () => Press(Pad.TabNext));
        Step("settings-scrolled", () =>
        {
            for (var i = 0; i < 8; i++)
            {
                Press("ui_down");
            }
        });
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

    private void Step(string name, Action act, double wait = 0.6) => _steps.Enqueue((name, act, wait));

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
        var (name, act, wait) = _steps.Dequeue();
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
        GetTree().CreateTimer(wait - 0.15).Timeout += () => Capture(name);
    }

    private void Capture(string name)
    {
        var focus = GetViewport().GuiGetFocusOwner();
        GD.Print($"smoke: {name}: focus={(focus == null ? "none" : focus.GetType().Name + " '" + (focus as Button)?.Text?.Split('\n')[0] + "'")}");
        var img = GetViewport().GetTexture().GetImage();
        img?.SavePng(Path.Combine(_outDir, $"{++_shot:00}-{name}.png"));
    }
}
