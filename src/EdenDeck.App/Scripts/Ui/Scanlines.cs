using Godot;

namespace EdenDeck.App.Ui;

/// <summary>
/// CRT scanlines over the whole screen: part of the terminal theme, or the "CRT scanlines"
/// setting for any theme (stronger lines plus a soft vignette at the edges).
/// </summary>
public partial class Scanlines : Control
{
    private readonly bool _strong;

    public Scanlines(bool strong = false)
    {
        _strong = strong;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 100;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var line = new Color(0, 0, 0, _strong ? 0.22f : 0.16f);
        for (var y = 0f; y < Size.Y; y += 3)
        {
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), line, 1);
        }
        if (_strong)
        {
            // Darkened edges, like the curve of a tube.
            const int Steps = 14;
            for (var i = 0; i < Steps; i++)
            {
                var inset = i * 3f;
                var a = 0.05f * (1 - i / (float)Steps);
                DrawRect(new Rect2(inset, inset, Size.X - 2 * inset, Size.Y - 2 * inset), new Color(0, 0, 0, a), false, 3);
            }
        }
    }
}
