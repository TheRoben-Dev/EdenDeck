using EdenDeck.App.Ui;
using EdenDeck.Backends.Rpcs3;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// One PS3 game's patches (60 fps, resolution, fixes) from RPCS3's community patch list: download
/// or refresh the list on request, then switch each patch on or off. The switches are RPCS3's own
/// (patch_config.yml), so its patch manager shows the same thing.
/// </summary>
public partial class Rpcs3PatchesPage : Screen
{
    private readonly LibraryGame _game;
    private readonly Action? _closed;
    private Control? _first;

    public Rpcs3PatchesPage(LibraryGame game, Action? closed = null) => (_game, _closed) = (game, closed);

    public override bool OnBack()
    {
        App.Pop();
        _closed?.Invoke();
        return true;
    }

    private static Rpcs3Patches Patches => Host.Rpcs3.Patches;

    protected override void Build()
    {
        var col = Column(10);
        AddChild(CardFrame(Scroller(col)));
        col.AddChild(UiTheme.Label("Game patches", UiTheme.TitleSize - 6));
        col.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.FontSize, UiTheme.Dim));
        var items = new List<Control>();
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }

        var tid = _game.Base.TitleId;
        if (!Patches.HasPatchList)
        {
            col.AddChild(UiTheme.Label("RPCS3's community patches add things like 60 fps modes, higher resolutions and fixes. Get the patch list to see what's there for this game.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        else
        {
            var patches = Try(() => Patches.ForGame(tid), [], "patches", col);
            if (patches.Count == 0)
            {
                col.AddChild(UiTheme.Label("The patch list has nothing for this game yet.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            foreach (var p in patches)
            {
                var patch = p;
                var versions = string.Join(", ", p.Targets.Select(t => t.Version).Distinct().Order(StringComparer.Ordinal));
                var help = string.Join(" ", new[] { p.Notes, p.Author is { Length: > 0 } a ? $"By {a}." : null, $"For game version {versions}." }.Where(x => !string.IsNullOrEmpty(x)));
                Add(Toggle.Create(p.Name, Try(() => Patches.IsEnabled(patch), false, "patch settings", col), on =>
                {
                    if (App.Guard(() => Patches.SetEnabled(patch, on)))
                    {
                        Rebuild();
                    }
                }, help, "Applied when the game starts.", "Off."));
            }
            if (patches.Any(p => p.Name.Contains("fps", StringComparison.OrdinalIgnoreCase)))
            {
                col.AddChild(UiTheme.Label("With a 60 fps patch on, set this game's own frame rate to 60 on its card so frame generation stays off.",
                    UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
        }
        Add(new ActionButton(Patches.HasPatchList ? "Update the patch list" : "Get the patch list", Update,
            "Downloads RPCS3's newest community patch list, the same one RPCS3's patch manager uses. Nothing is downloaded until you press this."));
        col.AddChild(UiTheme.Label(Rpcs3Patches.Credit, UiTheme.SmallSize, UiTheme.Dim, wrap: true));

        Add(new ActionButton("Back", () => OnBack()));
        Focus.Column(items);
        Anim.Stagger(items, 0.02);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();

    private void Update()
    {
        App.Busy("Getting patches…", async (_, ct) =>
        {
            try
            {
                return await Patches.UpdateAsync(ct);
            }
            catch (System.Text.Json.JsonException e)
            {
                throw new InvalidDataException("RPCS3's patch service sent something Scanline can't read: " + e.Message, e);
            }
        }, updated =>
        {
            App.Success(updated ? "Patch list updated." : "The patch list is already up to date.");
            Rebuild();
        });
    }
}
