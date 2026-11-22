using EdenDeck.App.Ui;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>Game grid, most recently played first.</summary>
public partial class LibraryScreen : Screen
{
    private const int Columns = 6;
    private Control? _first;

    public override IEnumerable<(string Action, string Label)> Hints =>
        Host.Library.Count > 0 ? [(Pad.Tertiary, "Play"), (Pad.Secondary, "Rescan")] : [];

    protected override void Build()
    {
        if (Host.Settings.LibraryFolders.Count == 0 || Host.Library.Count == 0)
        {
            BuildEmpty();
            return;
        }

        var col = Column(14);
        AddChild(col);
        var played = Host.Profiles.All;
        var games = Host.Library
            .OrderByDescending(g => played.TryGetValue(g.GameId, out var p) ? p.LastPlayed : null)
            .ThenBy(g => g.Base.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        col.AddChild(UiTheme.Label($"{games.Count} games", UiTheme.SmallSize + 1, UiTheme.Dim));

        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 18);
        var pad = new MarginContainer();  // room for the focus glow at the edges
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            pad.AddThemeConstantOverride($"margin_{side}", 8);
        }
        pad.AddChild(grid);
        col.AddChild(Scroller(pad));

        var tiles = new List<Control>();
        foreach (var g in games)
        {
            var tile = Tile(g);
            grid.AddChild(tile);
            tiles.Add(tile);
        }
        Focus.Grid(tiles, Columns);
        Anim.Stagger(tiles);
        _first = tiles.FirstOrDefault();
    }

    private Button Tile(LibraryGame g)
    {
        var extras = new List<string>();
        if (g.Updates.Count > 0)
        {
            extras.Add("UPDATE");
        }
        if (g.Dlc.Count > 0)
        {
            extras.Add($"{g.Dlc.Count} DLC");
        }
        var b = new Button
        {
            CustomMinimumSize = new Vector2(168, 246),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = $"{g.Base.Title}  ·  {g.GameId}",
        };
        // The button is only the hit area; the cartridge draws everything (focus included).
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
        {
            b.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }
        var cart = new Cartridge(g.Base.Title, Monogram(g.Base.Title), extras.Count > 0 ? string.Join("  ·  ", extras) : null);
        cart.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        b.AddChild(cart);
        b.FocusEntered += () => cart.SetFocused(true);
        b.FocusExited += () => cart.SetFocused(false);
        b.MouseEntered += () => { if (!b.HasFocus()) { b.GrabFocus(); } };
        Anim.PressFlash(b);
        b.Pressed += () => App.Push(new GamePage(g, b.GetGlobalRect()));
        b.SetMeta("game", g.GameId);
        return b;
    }

    /// <summary>Up to two initials from the title ("Mountain Climb Deluxe" -> "MC").</summary>
    public static string Monogram(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]) && w.Length > 1 || char.IsDigit(w[0]))
            .ToList();
        var letters = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        return letters.Length > 0 ? letters : title.Length > 0 ? title[..1].ToUpperInvariant() : "?";
    }

    private void BuildEmpty()
    {
        var center = new CenterContainer();
        AddChild(center);
        var col = Column(16);
        col.CustomMinimumSize = new Vector2(620, 0);
        center.AddChild(col);
        var noFolders = Host.Settings.LibraryFolders.Count == 0;
        col.AddChild(UiTheme.Label(noFolders ? "Add your games" : "No games found", 30));
        col.AddChild(UiTheme.Label(
            noFolders
                ? "Pick the folder that holds your own game dumps (.nsp, .xci, .nsz, .xcz). Eden Deck never downloads games."
                : "None of your library folders contain Switch games yet. Add another folder, or rescan after copying games over.",
            UiTheme.SmallSize + 2, UiTheme.Dim, wrap: true));
        var add = new ActionButton("Add library folder", AddFolder);
        col.AddChild(add);
        var items = new List<Control> { add };
        if (!noFolders)
        {
            var rescan = new ActionButton("Rescan", Rescan);
            col.AddChild(rescan);
            items.Add(rescan);
        }
        Focus.Column(items);
        _first = add;
    }

    private void AddFolder() =>
        App.Push(new FilePicker("Choose a game folder", pickFolder: true, dir =>
        {
            if (Host.Settings.LibraryFolders.All(f => f.Path != dir))
            {
                Host.SaveSettings(Host.Settings with { LibraryFolders = [.. Host.Settings.LibraryFolders, new LibraryFolder(dir)] });
            }
            Rescan();
        }));

    public void Rescan() =>
        App.Busy("Scanning library…", (_, ct) => Task.FromResult(Host.Rescan(ct)), games =>
        {
            var copying = Host.LibraryService.LastSkippedInProgress;
            App.Success(copying > 0 ? $"{games.Count} games ({copying} still copying, rescan when done)" : $"{games.Count} games");
            Rebuild();
            App.UpdateChrome();
        });

    public override void OnAction(string action)
    {
        if (action == Pad.Secondary)
        {
            Rescan();
        }
        else if (action == Pad.Tertiary && GetViewport().GuiGetFocusOwner() is { } f && f.HasMeta("game"))
        {
            // Quick play from the grid, skipping the game page.
            if (Host.FindGame(f.GetMeta("game").AsString()) is { } g)
            {
                App.Launch(g);
            }
        }
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
