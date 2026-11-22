using Godot;

namespace EdenDeck.App.Ui;

/// <summary>Hit tests for background things you can tap (3D props, the console buddy).</summary>
public static class UiHit
{
    /// <summary>True when the point is over something the UI handles (buttons, rows, panels, overlays).</summary>
    public static bool OverUi(Node self, Vector2 at)
    {
        var main = Main.Instance;
        bool Walk(Node n)
        {
            foreach (var child in n.GetChildren())
            {
                if (child is Control c)
                {
                    if (!c.Visible || c.IsAncestorOf(self) || c == self)
                    {
                        continue;
                    }
                    // Focusable controls, panels and full-screen catchers (overlay backdrops) count as UI;
                    // plain layout controls, containers and scrollers don't, so empty space inside a list still reaches the props.
                    var blocks = c.FocusMode != Control.FocusModeEnum.None || c is PanelContainer or Panel ||
                                 (c.MouseFilter == Control.MouseFilterEnum.Stop && c is not Container && c.GetType() != typeof(Control));
                    if (blocks && c.GetGlobalRect().HasPoint(at))
                    {
                        return true;
                    }
                }
                if (child is CanvasItem { Visible: false })
                {
                    continue;
                }
                if (Walk(child))
                {
                    return true;
                }
            }
            return false;
        }
        return main != null && Walk(main);
    }
}
