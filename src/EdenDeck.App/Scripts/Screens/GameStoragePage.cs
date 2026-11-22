using EdenDeck.App.Ui;
using EdenDeck.Backends.Eden.Storage;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// What one game takes up, part by part, with the tidy-ups that are safe: rebuild the shader cache
/// and remove update files older than the one Eden loads.
/// </summary>
public partial class GameStoragePage : Screen
{
    private readonly LibraryGame _game;
    private readonly Action? _closed;
    private Control? _first;

    public GameStoragePage(LibraryGame game, Action? closed = null) => (_game, _closed) = (game, closed);

    public override bool OnBack()
    {
        App.Pop();
        _closed?.Invoke();
        return true;
    }

    protected override void Build()
    {
        var col = Column(10);
        AddChild(CardFrame(Scroller(col)));
        col.AddChild(UiTheme.Label("Storage", UiTheme.TitleSize - 6));
        col.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.FontSize, UiTheme.Dim));
        var u = Try(() => Host.Eden.Usage(_game), null, "sizes", col);
        var items = new List<Control>();
        if (u != null)
        {
            col.AddChild(UiTheme.Label($"{StorageReport.Format(u.Total)} in all", UiTheme.FontSize + 2, UiTheme.Text));
            void Line(string what, long bytes, string? note = null)
            {
                if (bytes > 0)
                {
                    col.AddChild(UiTheme.Label($"{what}: {StorageReport.Format(bytes)}{(note == null ? "" : "  ·  " + note)}", UiTheme.SmallSize + 2, UiTheme.Dim, wrap: true));
                }
            }
            Line("Game", u.Base);
            Line(_game.Updates.Count == 1 ? "Update" : $"Updates ({_game.Updates.Count} files)", u.Updates, u.OlderUpdates > 0 ? $"{StorageReport.Format(u.OlderUpdates)} in older ones" : null);
            Line(_game.Dlc.Count == 1 ? "DLC" : $"DLC ({_game.Dlc.Count} files)", u.Dlc);
            Line("Shader cache", u.Shaders);
            Line("Mods", u.Mods);

            var tid = _game.Base.TitleId;
            if (u.Shaders > 0 && !string.IsNullOrEmpty(tid))
            {
                var b = new ActionButton($"Rebuild shader cache ({StorageReport.Format(u.Shaders)})", () => App.Confirm(
                    $"Clear the shader cache for {_game.Base.Title}? Eden builds it again while you play, so expect some stutter the first time each effect appears.",
                    "Clear", () =>
                    {
                        if (App.Guard(() => Host.Eden.ClearShaderCache(tid)))
                        {
                            App.Success("Shader cache cleared");
                            Rebuild();
                        }
                    }), "Fixes flicker or glitches after a driver or Eden update. Eden builds it again as you play.");
                col.AddChild(b);
                items.Add(b);
            }
            var older = u.OlderUpdateFiles;
            if (older.Count > 0)
            {
                var trash = EdenDeck.Host.Trash.Available;
                var b = new ActionButton($"Remove older updates ({StorageReport.Format(u.OlderUpdates)})", () => App.Confirm(
                    $"{(trash ? "Move" : "Delete")} {older.Count} older update file{(older.Count == 1 ? "" : "s")} for {_game.Base.Title}{(trash ? " to the trash" : " for good")}? Eden only loads the newest update ({_game.Updates[0].Version ?? Path.GetFileName(_game.Updates[0].Path)}), which stays.",
                    trash ? "Move to trash" : "Delete", () =>
                    {
                        if (App.Guard(() =>
                            {
                                EdenDeck.Host.Trash.Remove(older);
                                Host.Rescan();
                            }))
                        {
                            App.Success(trash ? "Older updates moved to the trash" : "Older updates deleted");
                            Rebuild();
                        }
                    }), "Eden only ever loads the newest update. The older files just take space.");
                col.AddChild(b);
                items.Add(b);
            }
        }
        var done = new ActionButton("Done", () => OnBack());
        col.AddChild(done);
        items.Add(done);
        Focus.Column(items);
        Anim.Stagger(items, 0.02);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
