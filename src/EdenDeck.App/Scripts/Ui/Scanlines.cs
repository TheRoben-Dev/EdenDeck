using Godot;

namespace EdenDeck.App.Ui;

/// <summary>Faint CRT scanlines over the whole screen, for the terminal theme only.</summary>
public partial class Scanlines : Control
{
    public Scanlines()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 100;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var line = new Color(0, 0, 0, 0.16f);
        for (var y = 0f; y < Size.Y; y += 3)
        {
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), line, 1);
        }
    }
}
