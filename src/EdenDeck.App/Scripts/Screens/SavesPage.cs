using System.Globalization;
using EdenDeck.App.Ui;
using EdenDeck.Core.Saves;
using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// One game's saves: what's there, back up now, restore or delete a backup, and delete the save
/// itself. Every restore and delete first backs up what it replaces, so nothing is ever lost.
/// </summary>
public partial class SavesPage : Screen
{
    private readonly LibraryGame _game;
    private readonly Action? _closed;
    private Control? _first;

    public SavesPage(LibraryGame game, Action? closed = null) => (_game, _closed) = (game, closed);

    private ISaveData Data => Host.Backend(_game.Base.BackendId).SaveData!;

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
        col.AddChild(UiTheme.Label("Saves", UiTheme.TitleSize - 6));
        col.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.FontSize, UiTheme.Dim));
        var items = new List<Control>();
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }

        var saves = Try(() => Data.Saves(_game.Base), [], "saves", col);
        col.AddChild(UiTheme.Label(saves.Count == 0
                ? "No save yet. It appears after you play and save in the game."
                : $"Save: {FormatSize(saves.Sum(s => s.Bytes))}, last changed {When(saves.Max(s => s.LastWriteUtc))}{(saves.Count > 1 ? $" ({saves.Count} profiles)" : "")}.",
            UiTheme.SmallSize + 2, saves.Count == 0 ? UiTheme.Dim : UiTheme.Text, wrap: true));
        if (_game.Base.BackendId == EdenDeck.Backends.Pcsx2.Pcsx2Backend.BackendId)
        {
            col.AddChild(UiTheme.Label("PS2 games save to memory cards that every PS2 game shares, as on a real PS2. A backup holds the whole card plus this game's save states, so restoring one puts back the whole card. Deleting removes only this game's save states.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        if (saves.Count > 0)
        {
            Add(new ActionButton("Back up now", () =>
            {
                if (App.Guard(() => Data.Backup(_game.Base)))
                {
                    App.Success("Save backed up");
                    Rebuild();
                }
            }, "Keeps a copy you can go back to. Manual backups are never deleted automatically."));
        }
        Add(Toggle.Create("Back up before each play", Host.Settings.BackupSavesOnLaunch, on =>
                Host.SaveSettings(Host.Settings with { BackupSavesOnLaunch = on }),
            $"For every game: a copy right before it starts. The last {SaveZips.KeepAutomatic} per game are kept.",
            "Your saves are copied each time a game starts.", "Only the backups you make yourself."));

        var backups = Try(() => Data.Backups(_game.Base), [], "backups", col);
        col.AddChild(Section(backups.Count == 0 ? "No backups yet" : $"Backups ({backups.Count})"));
        foreach (var b in backups)
        {
            var backup = b;
            Add(new ActionButton($"{When(b.CreatedUtc)}  ·  {FormatSize(b.Bytes)}{(b.Automatic ? "  ·  before play" : "")}", () => Choose(backup),
                "Restore this backup or delete it."));
        }
        if (saves.Count > 0)
        {
            col.AddChild(Section("Danger zone"));
            Add(new ActionButton("Delete save", () => App.Confirm(
                $"Delete the save for {_game.Base.Title}? A backup is made first, so you can restore it from this page.", "Delete", () =>
                {
                    if (App.Guard(() => Data.Delete(_game.Base)))
                    {
                        App.Success("Save deleted (a backup was kept)");
                        Rebuild();
                    }
                }), "Starts the game fresh. A backup is kept."));
        }
        col.AddChild(UiTheme.Label("Backups are kept in " + Data.BackupDir(_game.Base), UiTheme.SmallSize, UiTheme.Dim, wrap: true));
        Add(new ActionButton("Back", () => OnBack()));
        Focus.Column(items);
        Anim.Stagger(items, 0.02);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();

    private void Choose(SaveBackup b) => App.Push(new ChoicePage($"Backup from {When(b.CreatedUtc)} ({FormatSize(b.Bytes)}).",
        ("Restore", () => App.Confirm("Replace the current save with this backup? The current save is backed up first.", "Restore", () =>
        {
            if (App.Guard(() => Data.Restore(_game.Base, b.Path)))
            {
                App.Success("Backup restored");
                Rebuild();
            }
        })),
        ("Delete backup", () =>
        {
            if (App.Guard(() => File.Delete(b.Path)))
            {
                App.Toast("Backup deleted");
                Rebuild();
            }
        }),
        ("Cancel", () => { })));

    private static string When(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var ago = DateTime.Now - local;
        var rel = ago.TotalMinutes < 1 ? "just now"
            : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes} min ago"
            : ago.TotalDays < 1 ? $"{(int)ago.TotalHours} h ago"
            : ago.TotalDays < 7 ? $"{(int)ago.TotalDays} days ago" : null;
        var date = local.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        return rel == null ? date : $"{date} ({rel})";
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
