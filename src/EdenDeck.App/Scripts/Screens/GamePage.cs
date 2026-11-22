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

    private readonly Rect2? _origin;
    private PanelContainer? _card;
    private bool _opened;

    /// <param name="origin">Global rect of the tile it was opened from; the card grows out of it.</param>
    public GamePage(LibraryGame game, Rect2? origin = null)
    {
        _game = game;
        _origin = origin;
    }

    public override bool IsOverlay => true;

    private GameProfile Profile => Host.Profiles.Get(_game.GameId);

    private void SetProfile(Func<GameProfile, GameProfile> change) =>
        App.Guard(() => Host.Profiles.Set(_game.GameId, change(Profile)));

    protected override void Build()
    {
        // A large card over the (blurred) library. Clicks outside the card reach the backdrop.
        MouseFilter = MouseFilterEnum.Ignore;
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(center);
        var view = GetViewportRect().Size;
        _card = new PanelContainer
        {
            // Inset so the dimmed library shows all around: it reads as a card, not a page.
            CustomMinimumSize = new Vector2(Mathf.Min(1120, view.X * 0.88f), Mathf.Min(600, (view.Y - 56) * 0.84f)),
            MouseFilter = MouseFilterEnum.Stop,
        };
        var cardBox = UiTheme.Box(UiTheme.Panel, 1, 22, UiTheme.Line);
        cardBox.ShadowColor = new Color(0, 0, 0, 0.55f);
        cardBox.ShadowSize = 28;
        cardBox.ShadowOffset = new Vector2(0, 10);
        cardBox.ContentMarginLeft = cardBox.ContentMarginRight = 30;
        cardBox.ContentMarginTop = cardBox.ContentMarginBottom = 26;
        _card.AddThemeStyleboxOverride("panel", cardBox);
        center.AddChild(_card);
        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 36);
        _card.AddChild(split);

        // Left: what the game is.
        var info = Column(8);
        info.CustomMinimumSize = new Vector2(300, 0);
        info.ClipContents = true;
        info.SizeFlagsHorizontal = SizeFlags.Fill;
        split.AddChild(info);
        var cart = new Cartridge(_game.Base.Title, LibraryScreen.Monogram(_game.Base.Title), large: true)
        {
            CustomMinimumSize = new Vector2(220, 300),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        info.AddChild(cart);
        info.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.TitleSize - 8, wrap: true));
        var b = _game.Base;
        var facts = new List<string>
        {
            string.IsNullOrEmpty(b.TitleId) ? "Title ID unknown" : $"Title ID {b.TitleId}",
            Path.GetFileName(b.Path),
        };
        foreach (var f in facts)
        {
            var l = UiTheme.Label(f, UiTheme.SmallSize + 1, UiTheme.Dim);
            l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            info.AddChild(l);
        }
        // Whether Eden will actually load the update / DLC that was found (its own add-on switches included).
        foreach (var a in Host.Eden.AddOnStatus(_game, Host.Settings.LibraryFolders.Select(f => f.Path)))
        {
            var (mark, text, color) = a.State switch
            {
                EdenDeck.Backends.Eden.Config.AddOnState.Active => ("✓", $"{a.Label} · active", UiTheme.Good),
                EdenDeck.Backends.Eden.Config.AddOnState.Disabled => ("!", $"{a.Label} · turned off in Eden's add-ons", UiTheme.Warn),
                _ => ("!", $"{a.Label} · not loaded (folder isn't a library folder)", UiTheme.Warn),
            };
            var l = UiTheme.Label($"{mark}  {text}", UiTheme.SmallSize + 1, color, wrap: true);
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
        // Primary action stands out: filled with the accent colour.
        _play.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.Accent.Darkened(0.25f), 0, 12));
        _play.AddThemeStyleboxOverride("hover", UiTheme.Box(UiTheme.Accent.Darkened(0.15f), 0, 12));
        _play.AddThemeStyleboxOverride("pressed", UiTheme.Box(UiTheme.Accent.Darkened(0.45f), 0, 12));
        var playFocus = UiTheme.Focus();
        playFocus.BorderColor = UiTheme.Text;
        _play.AddThemeStyleboxOverride("focus", playFocus);
        if (UiTheme.Accent.Luminance > 0.5f)
        {
            _play.AddThemeColorOverride("font_color", UiTheme.Bg);
            _play.AddThemeColorOverride("font_focus_color", UiTheme.Bg);
            _play.AddThemeColorOverride("font_hover_color", UiTheme.Bg);
        }
        Add(_play);

        Add(new ActionButton("Open Eden", App.OpenEden,
            "Opens Eden's own window (controller mapping, anything not here yet). Close it to come back."));

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
        Anim.Stagger(items, 0.02);
        Anim.Stagger(info.GetChildren().OfType<Control>().ToList(), 0.04);
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

    public override void AnimateOpen()
    {
        if (_card == null || _opened)
        {
            return;
        }
        _opened = true;
        if (!Anim.Enabled)
        {
            return;
        }
        // Grow the card out of the tile it came from. Only scale and pivot are animated, so the
        // container keeps full control of the card's position.
        _card.Modulate = _card.Modulate with { A = 0f };
        GrowIn();
    }

    private async void GrowIn()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_card == null || !IsInstanceValid(_card))
        {
            return;
        }
        var rect = _card.GetGlobalRect();
        var start = 0.9f;
        _card.PivotOffset = rect.Size / 2;
        if (_origin is { } o && rect.Size.X > 0)
        {
            start = Mathf.Clamp(o.Size.X / rect.Size.X, 0.25f, 0.95f);
            // Pivot p stays fixed while scaling, so put it where the tile's centre maps.
            var pivot = (o.GetCenter() - rect.Position - start * (rect.Size / 2)) / (1 - start);
            _card.PivotOffset = pivot.Clamp(Vector2.Zero, rect.Size);
        }
        _card.Scale = new Vector2(start, start);
        _card.Modulate = _card.Modulate with { A = 0.25f };
        var t = _card.CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        t.TweenProperty(_card, "scale", Vector2.One, 0.26);
        t.TweenProperty(_card, "modulate:a", 1f, 0.18);
    }

    public override void AnimateClose(Action done)
    {
        if (_card == null || !Anim.Enabled || !IsInsideTree())
        {
            done();
            return;
        }
        MouseFilter = MouseFilterEnum.Ignore;
        _card.PivotOffset = _card.Size / 2;
        var t = _card.CreateTween().SetParallel().SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
        t.TweenProperty(_card, "scale", new Vector2(0.94f, 0.94f), Anim.Normal);
        t.TweenProperty(_card, "modulate:a", 0f, Anim.Normal);
        t.Chain().TweenCallback(Callable.From(done));
    }
}
