using EdenDeck.App.Ui;
using EdenDeck.Host;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// A full-screen page. Root tabs (Library, Eden, Settings) and pushed pages (game page, file
/// picker, confirm) are all screens; Main keeps them on a stack and B pops.
/// Screens build their UI in code in Build(), after they are in the tree, so focus neighbours
/// can be wired with real node paths.
/// </summary>
public abstract partial class Screen : MarginContainer
{
    private Control? _lastFocus;

    protected static Main App => Main.Instance;
    protected static AppHost Host => Main.Instance.Host!;

    /// <summary>Context hints shown in the bottom bar (Accept/Back are added by Main).</summary>
    public virtual IEnumerable<(string Action, string Label)> Hints => [];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Rebuild();
    }

    /// <summary>Clears and rebuilds the page (after data changes), keeping focus near where it was.</summary>
    public void Rebuild()
    {
        var focusedIndex = FocusIndex();
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        Build();
        if (IsVisibleInTree())
        {
            if (!RefocusIndex(focusedIndex))
            {
                FocusDefault();
            }
        }
    }

    protected abstract void Build();

    /// <summary>Focuses the screen's primary control.</summary>
    public abstract void FocusDefault();

    /// <summary>Called when B is pressed while this screen is on top. True = handled here.</summary>
    public virtual bool OnBack() => false;

    /// <summary>Called for the Secondary / Tertiary context buttons.</summary>
    public virtual void OnAction(string action)
    {
    }

    public void Remember() => _lastFocus = GetViewport()?.GuiGetFocusOwner();

    public void Restore()
    {
        if (_lastFocus != null && IsInstanceValid(_lastFocus) && _lastFocus.IsVisibleInTree() && IsAncestorOf(_lastFocus))
        {
            _lastFocus.GrabFocus();
        }
        else
        {
            FocusDefault();
        }
    }

    private int FocusIndex()
    {
        var f = GetViewport()?.GuiGetFocusOwner();
        return f != null && IsAncestorOf(f) ? Focusables().IndexOf(f) : -1;
    }

    private bool RefocusIndex(int index)
    {
        var all = Focusables();
        if (index < 0 || all.Count == 0)
        {
            return false;
        }
        all[Math.Min(index, all.Count - 1)].GrabFocus();
        return true;
    }

    private List<Control> Focusables()
    {
        var list = new List<Control>();
        void Walk(Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is Control { FocusMode: FocusModeEnum.All } ctl && ctl.IsVisibleInTree())
                {
                    list.Add(ctl);
                }
                Walk(c);
            }
        }
        Walk(this);
        return list;
    }

    // ---- small builders shared by screens ----

    protected static VBoxContainer Column(int separation = 10)
    {
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", separation);
        return v;
    }

    /// <summary>Vertical scroller that keeps the focused control visible.</summary>
    protected static ScrollContainer Scroller(Control content)
    {
        var s = new ScrollContainer
        {
            FollowFocus = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        s.AddChild(content);
        return s;
    }

    protected static Label Section(string text)
    {
        var l = UiTheme.Label(text.ToUpperInvariant(), UiTheme.SmallSize, UiTheme.Dim);
        l.CustomMinimumSize = new Vector2(0, 34);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        return l;
    }
}
