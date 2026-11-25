using EdenDeck.Core.Profiles;
using EdenDeck.App.Ui;
using EdenDeck.App.Ui.Poly;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>Game grid, most recently played first.</summary>
public partial class LibraryScreen : Screen
{
    private const int Columns = 6;
    private Control? _first;
    private CartStage? _stage;

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
        GameProfile? P(LibraryGame g) => played.TryGetValue(g.GameId, out var p) ? p : null;
        var collections = Host.Profiles.CollectionNames;
        var anyFavorite = Host.Library.Any(g => P(g)?.Favorite == true);
        if (Filter == Favorites && !anyFavorite || Filter.Length > 0 && Filter != Favorites && !collections.Contains(Filter))
        {
            Filter = "";  // the last favorite or collection went away
        }
        // Favorites first, then most recently played, then by name.
        var games = Host.Library
            .Where(g => Filter == "" || Filter == Favorites ? (Filter == "" || P(g)?.Favorite == true) : P(g)?.Collections?.Contains(Filter) == true)
            .OrderByDescending(g => P(g)?.Favorite == true)
            .ThenByDescending(g => P(g)?.LastPlayed)
            .ThenBy(g => g.Base.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        col.AddChild(UiTheme.Label(games.Count == Host.Library.Count ? $"{games.Count} games" : $"{games.Count} of {Host.Library.Count} games",
            UiTheme.SmallSize + 1, UiTheme.Dim));
        var fixes = FixRows(col);
        if (anyFavorite || collections.Count > 0)
        {
            // Only once the player has starred a game or made a collection.
            var options = new List<(string, string)> { ("", "All games") };
            if (anyFavorite)
            {
                options.Add((Favorites, "★ Favorites"));
            }
            options.AddRange(collections.Select(c => (c, c)));
            var show = new Stepper("Show", options, Filter, "Favorites and your collections. Star games and add them to collections on their card.");
            show.Changed += v =>
            {
                Filter = v;
                Rebuild();
            };
            col.AddChild(show);
            fixes.Add(show);
        }

        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 18);
        var pad = new MarginContainer();  // room for the focus glow at the edges
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            pad.AddThemeConstantOverride($"margin_{side}", 8);
        }
        pad.AddChild(grid);
        var scroller = Scroller(pad);
        col.AddChild(scroller);

        var tiles = new List<Control>();
        _carts.Clear();
        foreach (var g in games)
        {
            var tile = Tile(g);
            grid.AddChild(tile);
            tiles.Add(tile);
        }
        Focus.Grid(tiles, Columns, above: fixes.LastOrDefault());
        if (fixes.Count > 0)
        {
            Focus.Column(fixes, below: tiles.FirstOrDefault());
        }
        Anim.Stagger(tiles);
        _first = tiles.FirstOrDefault();
        _stage = null;
        if (UiTheme.Poly3D)
        {
            // Same grid, same focus; the carts are drawn in 3D on top of the tiles.
            _stage = new CartStage(tiles.Select((t, i) => (t, games[i].Base.Title)).ToList(), scroller);
            AddChild(_stage);
        }
        LoadIcons(games, _stage);
    }

    private readonly List<Cartridge> _carts = new();

    private const string Favorites = "\u0001favorites";

    /// <summary>What the library shows: "" all, <see cref="Favorites"/>, or a collection name. Kept while the app runs.</summary>
    private static string Filter { get; set; } = "";

    /// <summary>Puts each game's own icon on its cartridge as it's read (cached ones appear at once).</summary>
    private async void LoadIcons(IReadOnlyList<LibraryGame> games, CartStage? stage)
    {
        var carts = _carts.ToList();
        for (var i = 0; i < games.Count && i < carts.Count; i++)
        {
            var icon = await GameIcons.Load(games[i].Base);
            if (!IsInstanceValid(this) || !IsInsideTree())
            {
                return;
            }
            if (icon == null)
            {
                continue;
            }
            if (IsInstanceValid(carts[i]))
            {
                carts[i].SetIcon(icon);
            }
            if (stage != null && IsInstanceValid(stage))
            {
                stage.SetIcon(i, icon);
            }
        }
    }

    /// <summary>One row per game file the scan found without its extension, with a rename action.</summary>
    private List<Control> FixRows(VBoxContainer col)
    {
        var rows = new List<Control>();
        foreach (var f in Host.Misnamed)
        {
            var file = f;
            var row = new ActionButton($"⚠  \"{f.Name}\" is a game without a file extension  ·  Fix name", () => App.Confirm(
                $"\"{file.Name}\" is a Switch game ({file.Extension.TrimStart('.').ToUpperInvariant()}), but its name has no extension, so Eden can't recognise it.\n\nRename it to \"{file.SuggestedName}\"? Nothing else about the file changes.",
                "Rename", () =>
                {
                    if (App.Guard(() => LibraryService.FixName(file)))
                    {
                        Rescan();
                    }
                }), $"Renames it to {f.SuggestedName} so it shows up in your library.");
            row.AddThemeColorOverride("font_color", UiTheme.Warn);
            col.AddChild(row);
            rows.Add(row);
        }
        return rows;
    }

    private Button Tile(LibraryGame g)
    {
        var extras = new List<string>();
        if (Host.Profiles.Get(g.GameId).Favorite)
        {
            extras.Add("★");
        }
        if (SystemTag(g.Base) is { } tag)
        {
            extras.Add(tag);
        }
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
        b.SetMeta(FocusHighlight.OwnFocus, true);
        var cart = new Cartridge(g.Base.Title, Monogram(g.Base.Title), extras.Count > 0 ? string.Join("  ·  ", extras) : null) { TitleOnly = UiTheme.Poly3D };
        cart.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        b.AddChild(cart);
        _carts.Add(cart);
        b.FocusEntered += () => cart.SetFocused(true);
        b.FocusExited += () => cart.SetFocused(false);
        b.MouseEntered += () => { if (!b.HasFocus()) { b.GrabFocus(); } };
        Anim.PressFlash(b);
        b.Pressed += () =>
        {
            EdenDeck.Host.AppLog.Info($"tile pressed: {g.Base.Title} (3D stage: {_stage != null})");
            void Open() => App.Push(new GamePage(g, b.GetGlobalRect()));
            if (_stage != null)
            {
                _stage.Launch(b, Open);
            }
            else
            {
                Open();
            }
        };
        b.SetMeta("game", g.GameId);
        return b;
    }

    /// <summary>Up to two initials from the title ("Mountain Climb Deluxe" -> "MC").</summary>
    /// <summary>"Game Boy Advance", "PlayStation 3"... for a game's card.</summary>
    public static string SystemName(GameEntry e) =>
        e.BackendId == EdenDeck.Backends.Mgba.MgbaBackend.BackendId ? EdenDeck.Backends.Mgba.MgbaBackend.SystemOf(e.TitleId) : Host.Backend(e.BackendId).SystemName;

    /// <summary>A short system chip on carts that aren't Switch games ("GBA", "GBC", "GB").</summary>
    public static string? SystemTag(GameEntry e) => e.BackendId == Host.Eden.Id ? null : SystemName(e) switch
    {
        "Game Boy Advance" => "GBA",
        "Game Boy Color" => "GBC",
        "Game Boy" => "GB",
        "PlayStation 3" => "PS3",
        "PlayStation 2" => "PS2",
        var other => other,
    };

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
                ? "Pick the folder that holds your own game dumps (.nsp, .xci, .nsz, .xcz). Scanline never downloads games."
                : "None of your library folders contain Switch games yet. Add another folder, or rescan after copying games over.",
            UiTheme.SmallSize + 2, UiTheme.Dim, wrap: true));
        var items = FixRows(col);
        var add = new ActionButton("Add library folder", AddFolder);
        col.AddChild(add);
        items.Add(add);
        if (!noFolders)
        {
            var rescan = new ActionButton("Rescan", Rescan);
            col.AddChild(rescan);
            items.Add(rescan);
        }
        Focus.Column(items);
        _first = items[0];
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

    /// <summary>Rebuilds (a favorite or collection changed the order) and keeps focus on the same game.</summary>
    public void Refresh(string gameId)
    {
        Rebuild();
        Focusables(this).FirstOrDefault(c => c.HasMeta("game") && c.GetMeta("game").AsString() == gameId)?.GrabFocus();
    }

    private static IEnumerable<Control> Focusables(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is Control ctl && ctl.FocusMode != FocusModeEnum.None)
            {
                yield return ctl;
            }
            foreach (var d in Focusables(c))
            {
                yield return d;
            }
        }
    }
}
