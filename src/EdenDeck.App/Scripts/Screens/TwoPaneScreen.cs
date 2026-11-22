using EdenDeck.App.Ui;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// Two panes, console style: categories on the left, the selected category on the right.
/// Focusing a category shows its contents; A or right moves into them, B (or left on a plain row)
/// goes back to the category. Used by Settings and the Eden tab.
/// </summary>
public abstract partial class TwoPaneScreen : Screen
{
    private int _category;
    private readonly List<Button> _cats = new();
    private VBoxContainer? _pane;
    private ScrollContainer? _paneScroll;
    private readonly List<Control> _paneItems = new();
    private IReadOnlyList<(string Name, string Summary)> _list = [];

    /// <summary>Heading over the category list.</summary>
    protected abstract string Heading { get; }

    /// <summary>Category names and the one-line summary shown in the hint bar. Read on every build.</summary>
    protected abstract IReadOnlyList<(string Name, string Summary)> Categories { get; }

    /// <summary>Fills the right pane for <paramref name="name"/>; focusable rows go through <paramref name="add"/>.</summary>
    protected abstract void BuildCategory(string name, VBoxContainer col, Action<Control> add);

    /// <summary>Optional marker after a category name (like "•" for something that needs attention).</summary>
    protected virtual string? Badge(string name) => null;

    protected override void Build()
    {
        _cats.Clear();
        _list = Categories;
        _category = Math.Clamp(_category, 0, Math.Max(0, _list.Count - 1));
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 28);
        AddChild(row);

        var left = Column(6);
        left.SizeFlagsHorizontal = SizeFlags.Fill;
        left.CustomMinimumSize = new Vector2(280, 0);
        row.AddChild(left);
        left.AddChild(Section(Heading));
        for (var i = 0; i < _list.Count; i++)
        {
            var index = i;
            var name = _list[i].Name;
            var badge = Badge(name);
            var b = new ActionButton(badge != null ? $"{name}  {badge}" : name, EnterPane, _list[i].Summary);
            b.FocusEntered += () => SelectCategory(index);
            left.AddChild(b);
            _cats.Add(b);
        }

        _pane = Column(8);
        _paneScroll = Scroller(_pane);
        row.AddChild(_paneScroll);
        BuildPane();
    }

    private void SelectCategory(int index)
    {
        if (index == _category && _paneItems.Count > 0)
        {
            return;
        }
        _category = index;
        BuildPane();
        if (_paneScroll != null)
        {
            _paneScroll.ScrollVertical = 0;
        }
    }

    private void EnterPane()
    {
        if (_paneItems.Count > 0)
        {
            _paneItems[0].GrabFocus();
        }
    }

    public override bool OnBack()
    {
        var f = GetViewport()?.GuiGetFocusOwner();
        if (f != null && _pane != null && _pane.IsAncestorOf(f) && _category < _cats.Count)
        {
            _cats[_category].GrabFocus();
            return true;
        }
        return false;
    }

    private void BuildPane()
    {
        if (_pane == null || _list.Count == 0)
        {
            return;
        }
        foreach (var c in _pane.GetChildren())
        {
            _pane.RemoveChild(c);
            c.QueueFree();
        }
        _paneItems.Clear();
        var col = _pane;
        var items = _paneItems;
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }
        col.AddChild(UiTheme.Label(_list[_category].Name, UiTheme.TitleSize));
        BuildCategory(_list[_category].Name, col, Add);

        for (var i = 0; i < _cats.Count; i++)
        {
            _cats[i].AddThemeColorOverride("font_color", i == _category ? UiTheme.AccentText : UiTheme.Text);
        }
        var cat = _cats[_category];
        Focus.Column(_cats, right: items.FirstOrDefault());
        // Steppers and toggles use left/right for their value; B still goes back to the category.
        Focus.Column(items, left: cat);
        Anim.Stagger(items, 0.015);
    }

    public override void FocusDefault() => (_category < _cats.Count ? _cats[_category] : null)?.GrabFocus();
}
