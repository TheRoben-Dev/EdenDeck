using Godot;

namespace EdenDeck.App.Ui;

/// <summary>Toggle row ("Caption   On/Off") built on Stepper so it behaves the same.</summary>
public static class Toggle
{
    public static Stepper Create(string caption, bool value, Action<bool> changed, string help = "")
    {
        var s = new Stepper(caption, [("true", "On"), ("false", "Off")], value ? "true" : "false", help);
        s.Changed += v => changed(v == "true");
        return s;
    }
}
