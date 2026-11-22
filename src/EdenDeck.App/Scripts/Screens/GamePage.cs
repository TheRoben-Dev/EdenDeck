using EdenDeck.App.Ui;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// One game: play, which keys to use, frame gen (2x to 60 fps only) and per-game Eden settings.
/// Everything set here is applied identically when the game is started from a Steam shortcut.
/// </summary>
public partial class GamePage : Screen
{
    private readonly LibraryGame _game;
    private Button? _play;
    private Label? _frameGenNote;

    public GamePage(LibraryGame game) => _game = game;

    private GameProfile Profile => Host.Profiles.Get(_game.GameId);

    private void SetProfile(Func<GameProfile, GameProfile> change) =>
        App.Guard(() => Host.Profiles.Set(_game.GameId, change(Profile)));

    protected override void Build()
    {
        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 36);
        AddChild(split);

        // Left: what the game is.
        var info = Column(8);
        info.CustomMinimumSize = new Vector2(380, 0);
        info.SizeFlagsHorizontal = SizeFlags.Fill;
        split.AddChild(info);
        var art = new Panel { CustomMinimumSize = new Vector2(380, 214) };
        art.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.TileColor(_game.Base.Title), 0, 14));
        info.AddChild(art);
        info.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.TitleSize - 4, wrap: true));
        var b = _game.Base;
        var facts = new List<string>
        {
            string.IsNullOrEmpty(b.TitleId) ? "Title ID unknown" : $"Title ID {b.TitleId}",
            Path.GetFileName(b.Path),
        };
        if (_game.Updates.Count > 0)
        {
            facts.Add("Update: " + string.Join(", ", _game.Updates.Select(u => u.Version ?? Path.GetFileName(u.Path))));
        }
        if (_game.Dlc.Count > 0)
        {
            facts.Add($"{_game.Dlc.Count} DLC");
        }
        foreach (var f in facts)
        {
            var l = UiTheme.Label(f, UiTheme.SmallSize + 1, UiTheme.Dim);
            l.ClipText = true;
            info.AddChild(l);
        }

        // Right: controls.
        var col = Column(8);
        split.AddChild(Scroller(col));
        var items = new List<Control>();
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }

        _play = new ActionButton("▶  Play", () => App.Launch(_game), "Starts the game with everything below applied.");
        _play.AddThemeFontSizeOverride("font_size", 24);
        _play.CustomMinimumSize = new Vector2(0, 64);
        Add(_play);

        col.AddChild(Section("Launch setup"));
        var p = Profile;
        var keySets = new List<(string, string)> { ("", "Default keys") };
        keySets.AddRange(Host.Eden.KeySets.List().Select(k => (k.Id, k.Label + (k.HasFirmware ? " + firmware" : ""))));
        var keys = new Stepper("Keys", keySets, p.KeySet ?? "",
            "Use one of your own key sets (and its firmware) just for this game. Add sets on the Eden tab.");
        keys.Changed += v => SetProfile(x => x with { KeySet = v == "" ? null : v });
        Add(keys);

        var nightly = Host.Eden.Schema.Channel == "nightly";
        var fg = new Stepper("Frame generation", [(nameof(FrameGenMode.Off), "Off"), (nameof(FrameGenMode.Builtin), "Eden built-in")],
            p.FrameGen == FrameGenMode.Builtin ? nameof(FrameGenMode.Builtin) : nameof(FrameGenMode.Off),
            "Doubles a 30 fps game to 60 fps using Eden's built-in frame generation. Never goes above 60.");
        fg.Changed += v =>
        {
            SetProfile(x => x with { FrameGen = Enum.Parse<FrameGenMode>(v) });
            UpdateFrameGenNote();
        };
        Add(fg);
        var fps = new Stepper("Game's own frame rate", [("", "Unknown"), ("20", "20 fps"), ("30", "30 fps"), ("60", "60 fps")],
            p.NativeFps?.ToString() ?? "",
            "What the game normally runs at. Games that already run at 60 get no frame generation.");
        fps.Changed += v =>
        {
            SetProfile(x => x with { NativeFps = v == "" ? null : int.Parse(v) });
            UpdateFrameGenNote();
        };
        Add(fps);
        _frameGenNote = UiTheme.Label("", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true);
        col.AddChild(_frameGenNote);
        UpdateFrameGenNote();
        if (!nightly)
        {
            col.AddChild(UiTheme.Label("Frame generation needs Eden Nightly and your own Lossless Scaling files.", UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
        }

        col.AddChild(Section("Eden settings for this game"));
        if (string.IsNullOrEmpty(b.TitleId))
        {
            col.AddChild(UiTheme.Label("Per-game settings need a title ID, which this file doesn't have.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        else
        {
            var settings = Host.Eden.Settings;
            var global = settings.ReadGlobal();
            var overrides = settings.ReadPerGame(b.TitleId);
            foreach (var o in settings.Options.Where(o => o.PerGame))
            {
                var row = OptionRows.PerGame(o, overrides.GetValueOrDefault(o.Id), global.GetValueOrDefault(o.Id), v =>
                    App.Guard(() => settings.WritePerGame(b.TitleId, new Dictionary<string, string?> { [o.Id] = v })));
                Add(row);
            }
        }

        Focus.Column(items);
    }

    private void UpdateFrameGenNote()
    {
        if (_frameGenNote == null)
        {
            return;
        }
        var d = FrameGenPolicy.Decide(Profile.FrameGen, Profile.NativeFps);
        _frameGenNote.Text = d.Enabled ? $"Frame gen: {d.Reason}." : d.Reason == "off" ? "Frame gen is off." : $"No frame gen: {d.Reason}.";
        _frameGenNote.AddThemeColorOverride("font_color", d.Enabled ? UiTheme.Good : UiTheme.Dim);
    }

    public override void FocusDefault() => _play?.GrabFocus();
}
