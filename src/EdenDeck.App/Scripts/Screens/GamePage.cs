using EdenDeck.Core.Backends;
using EdenDeck.Core.Settings;
using EdenDeck.Backends.Eden.Config;
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

    /// <summary>A favorite or collection changed: the library re-sorts when this page closes.</summary>
    private bool _shelved;

    public override bool OnBack()
    {
        if (!_shelved)
        {
            return false;
        }
        App.Pop();
        if (App.Top is LibraryScreen library)
        {
            library.Refresh(_game.GameId);
        }
        return true;
    }

    private GameProfile Profile => Host.Profiles.Get(_game.GameId);

    private void SetProfile(Func<GameProfile, GameProfile> change) =>
        App.Guard(() => Host.Profiles.Set(_game.GameId, change(Profile)));

    /// <summary>The game's own icon on the big cartridge, once it's read.</summary>
    private async void ShowIcon(Control cart)
    {
        var icon = await EdenDeck.App.Ui.GameIcons.Load(_game.Base);
        if (icon == null || !IsInstanceValid(cart))
        {
            return;
        }
        switch (cart)
        {
            case Cartridge c:
                c.SetIcon(icon);
                break;
            case EdenDeck.App.Ui.Poly.CartStage stage:
                stage.SetIcon(0, icon);
                break;
        }
    }

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
        Control cart = UiTheme.Poly3D
            ? new EdenDeck.App.Ui.Poly.CartStage(_game.Base.Title)
            : new Cartridge(_game.Base.Title, LibraryScreen.Monogram(_game.Base.Title), large: true);
        cart.CustomMinimumSize = new Vector2(220, 300);
        cart.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        info.AddChild(cart);
        ShowIcon(cart);
        info.AddChild(UiTheme.Label(_game.Base.Title, UiTheme.TitleSize - 8, wrap: true));
        var b = _game.Base;
        var isEden = b.BackendId == Host.Eden.Id;
        var backend = Host.Backend(b.BackendId);
        var facts = new List<string>
        {
            isEden ? (string.IsNullOrEmpty(b.TitleId) ? "Title ID unknown" : $"Title ID {b.TitleId}") : LibraryScreen.SystemName(b),
            Path.GetFileName(b.Path),
        };
        foreach (var f in facts)
        {
            var l = UiTheme.Label(f, UiTheme.SmallSize + 1, UiTheme.Dim);
            l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            info.AddChild(l);
        }
        // Whether Eden will actually load the update / DLC that was found (its own add-on switches included).
        foreach (var a in isEden ? Try(() => Host.Eden.AddOnStatus(_game, Profile.LoadAddOns).ToList(), [], "update/DLC status", info) : [])
        {
            var (mark, text, color) = a.State switch
            {
                EdenDeck.Backends.Eden.Config.AddOnState.Active => ("✓", $"{a.Label} · active", UiTheme.Good),
                EdenDeck.Backends.Eden.Config.AddOnState.Disabled => ("!", $"{a.Label} · turned off in Eden's add-ons", UiTheme.Warn),
                _ => ("·", $"{a.Label} · off for this game", UiTheme.Dim),
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
        // The shared focus ring glides onto Play and fades: Play's own filled style takes over.
        _play.SetMeta(FocusHighlight.OwnFocus, true);
        if (UiTheme.Accent.Luminance > 0.5f || UiTheme.Spec.Light)
        {
            _play.AddThemeColorOverride("font_color", UiTheme.Bg);
            _play.AddThemeColorOverride("font_focus_color", UiTheme.Bg);
            _play.AddThemeColorOverride("font_hover_color", UiTheme.Bg);
        }
        Add(_play);

        if (isEden)
        {
            Add(new ActionButton("Open Eden", App.OpenEden,
                "Opens Eden's own window (controller mapping, anything not here yet). Close it to come back."));
        }
        var inSteam = Try(() => Host.Steam.Has(_game.GameId), false, "Steam shortcuts", col);
        Add(new ActionButton(inSteam ? "Update Steam shortcut" : "Add to Steam", () =>
        {
            if (Host.Steam.Unavailable is { } why)
            {
                App.Error(why);
                return;
            }
            App.AddToSteam(_game.GameId, Host.Steam.ForGame(_game));
        }, $"Puts this game in your Steam library. Starting it there opens it straight in {backend.DisplayName} with everything on this page applied."));
        if (isEden && !string.IsNullOrEmpty(_game.Base.TitleId))
        {
            var mods = Try(() => Host.Eden.Mods(_game.Base.TitleId), [], "mods", col);
            var modCount = mods.Count(m => m.Name != EdenDeck.Backends.Eden.Mods.ScanlineCheats.ModName);
            var on = mods.Count(m => m.Enabled && m.Name != EdenDeck.Backends.Eden.Mods.ScanlineCheats.ModName);
            var cheats = mods.Any(m => m.Enabled && m.Name == EdenDeck.Backends.Eden.Mods.ScanlineCheats.ModName);
            var summary = modCount == 0 && !cheats ? "" : "  ·  " + string.Join(", ", new[]
            {
                modCount == 0 ? null : on == modCount ? $"{modCount} mod{(modCount == 1 ? "" : "s")}" : $"{on} of {modCount} mods on",
                cheats ? "cheats on" : null,
            }.Where(x => x != null));
            Add(new ActionButton("Mods & cheats" + summary, () => App.Push(new ModsPage(_game, Rebuild)),
                "Install and switch mods, and get cheats for the version you play."));
        }
        if (backend is EdenDeck.Backends.Mgba.MgbaBackend mgba && !string.IsNullOrEmpty(_game.Base.TitleId))
        {
            var tid = _game.Base.TitleId;
            var patch = Try(() => mgba.Mods.ActivePatch(tid), null, "patches", col);
            var cheatsOn = Try(() => mgba.Mods.Cheats(tid).Count(c => c.Enabled), 0, "cheats", col);
            var summary = string.Join(", ", new[] { patch == null ? null : "patched", cheatsOn == 0 ? null : $"{cheatsOn} cheat{(cheatsOn == 1 ? "" : "s")} on" }.Where(x => x != null));
            Add(new ActionButton("Patches & cheats" + (summary == "" ? "" : "  ·  " + summary), () => App.Push(new MgbaModsPage(_game, Rebuild)),
                "Apply a ROM patch such as a translation, and get cheats for this game."));
        }
        if (backend is EdenDeck.Backends.Rpcs3.Rpcs3Backend rpcs3 && !string.IsNullOrEmpty(_game.Base.TitleId))
        {
            var on = Try(() => rpcs3.Patches.EnabledCount(_game.Base.TitleId), 0, "patches", col);
            Add(new ActionButton("Game patches" + (on == 0 ? "" : $"  ·  {on} on"), () => App.Push(new Rpcs3PatchesPage(_game, Rebuild)),
                "60 fps modes, higher resolutions and fixes from RPCS3's community patch list."));
            var usage = Try(() => rpcs3.Usage(_game.Base), null, "sizes", col);
            if (usage is { Cache: > 0 })
            {
                Add(new ActionButton($"Rebuild RPCS3 cache ({EdenDeck.Backends.Eden.Storage.StorageReport.Format(usage.Cache)})", () => App.Confirm(
                    "Clear this game's RPCS3 cache? RPCS3 compiles it again on the next start, which takes a while. Useful after an RPCS3 or driver update.", "Clear", () =>
                    {
                        if (App.Guard(() => rpcs3.ClearCache(_game.Base)))
                        {
                            App.Success("Cache cleared");
                            Rebuild();
                        }
                    }), "Fixes glitches after an RPCS3 or graphics driver update."));
            }
        }
        if (backend.SaveData is { } saveData && !string.IsNullOrEmpty(b.TitleId))
        {
            var lastBackup = Try(() => saveData.Backups(b).FirstOrDefault(), null, "save backups", col);
            var hasSave = Try(() => saveData.Saves(b).Count > 0, false, "saves", col);
            Add(new ActionButton("Saves" + (!hasSave ? "" : lastBackup == null ? "  ·  not backed up" : $"  ·  backed up {lastBackup.CreatedUtc.ToLocalTime():d MMM}"),
                () => App.Push(new SavesPage(_game, Rebuild)), "Back up, restore or delete this game's save."));
        }

        if (isEden)
        {
            var usage = Try(() => Host.Eden.Usage(_game), null, "sizes", col);
            Add(new ActionButton("Storage" + (usage == null ? "" : "  ·  " + EdenDeck.Backends.Eden.Storage.StorageReport.Format(usage.Total)),
                () => App.Push(new GameStoragePage(_game, Rebuild)), "What this game takes up, and rebuilding its shader cache."));
        }
        var fav = Profile.Favorite;
        Add(Toggle.Create("★ Favorite", fav, on =>
            {
                SetProfile(x => x with { Favorite = on });
                _shelved = true;
            },
            "Favorites come first in the library and get their own filter.", "Listed first in the library.", "Not starred."));
        var shelves = Profile.Collections ?? [];
        Add(new ActionButton("Collections" + (shelves.Count == 0 ? "" : "  ·  " + string.Join(", ", shelves)),
            () => App.Push(new CollectionsPage(_game, () =>
            {
                _shelved = true;
                Rebuild();
            })), "Put this game on shelves like Playing now or Up next."));

        if (isEden)
        {
            BuildEdenLaunchSetup(col, Add);
        }
        else if (backend.Id == EdenDeck.Backends.Mgba.MgbaBackend.BackendId)
        {
            BuildMgbaLaunchSetup(col, Add);
        }
        else if (backend.Id == EdenDeck.Backends.Rpcs3.Rpcs3Backend.BackendId)
        {
            BuildRpcs3LaunchSetup(col, Add);
        }

        col.AddChild(Section($"{backend.DisplayName} settings for this game"));
        if (string.IsNullOrEmpty(b.TitleId))
        {
            col.AddChild(UiTheme.Label("Per-game settings need a title ID, which this file doesn't have.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        else
        {
            var settings = backend.Settings;
            var global = Try(() => settings.ReadGlobal(), new Dictionary<string, string>(), $"{backend.DisplayName}'s settings", col);
            var overrides = Try(() => settings.ReadPerGame(b.TitleId), new Dictionary<string, string>(), $"this game's {backend.DisplayName} settings", col);
            if (backend.Presets is { } presets)
            {
                Add(PresetRow(presets, settings, backend.DisplayName, b.TitleId, overrides));
            }
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

    /// <summary>Eden only: key set, controller profile, update/DLC, frame generation and the CRT layer.</summary>
    private void BuildEdenLaunchSetup(VBoxContainer col, Action<Control> Add)
    {
            col.AddChild(Section("Launch setup"));
            var p = Profile;
            var keySets = new List<(string, string)> { ("", "Default keys") };
            keySets.AddRange(Try(() => Host.Eden.KeySets.List().Select(k => (k.Id, k.Label + (k.HasFirmware ? " + firmware" : ""))).ToList(), [], "key sets", col));
            var keys = new Stepper("Keys", keySets, p.KeySet ?? "",
                "Use one of your own key sets (and its firmware) just for this game. Add sets on Emulators → Eden.")
            {
                Expands = true,
                DetailFor = v => v == "" ? "Uses the keys and firmware you imported on Emulators → Eden." : "Swapped in when this game starts and put back when it closes.",
            };
            keys.Changed += v => SetProfile(x => x with { KeySet = v == "" ? null : v });
            Add(keys);
            if (!string.IsNullOrEmpty(_game.Base.TitleId))
            {
                var profiles = new List<(string, string)> { ("", "Global setup") };
                profiles.AddRange(Try(() => Host.Eden.InputProfiles().Select(n => (n, n)).ToList(), [], "controller profiles", col));
                var tid = _game.Base.TitleId;
                var current = Try(() => Host.Eden.GameInputProfile(tid), null, "this game's controller profile", col) ?? "";
                if (profiles.All(x => x.Item1 != current))
                {
                    current = "";
                }
                var ctl = new Stepper("Controller", profiles, current,
                    "Which controller setup player 1 uses in this game. Save setups as profiles in Settings → Controller.")
                {
                    Expands = true,
                    DetailFor = v => v == "" ? "Same mapping and controller style as everywhere else." : $"Uses your \"{v}\" profile (its mapping and controller style) only in this game.",
                };
                ctl.Changed += v => App.Guard(() => Host.Eden.SetGameInputProfile(tid, v == "" ? null : v));
                Add(ctl);
            }
            if (_game.Updates.Count > 0 || _game.Dlc.Count > 0)
            {
                Add(Toggle.Create("Update and DLC", p.LoadAddOns, on =>
                {
                    SetProfile(x => x with { LoadAddOns = on });
                    Rebuild();
                }, "Loads this game's update and DLC files straight from your library. Nothing is installed or copied."));
            }

            AddFrameGenRows(col, Add);
            Add(CrtRow("Eden", moving: true));
            AddLayerNote(col);
    }

    /// <summary>Frame generation (lsfg-vk, 60 fps rule) for any Vulkan emulator, with what's missing for it.</summary>
    private void AddFrameGenRows(VBoxContainer col, Action<Control> Add)
    {
        var p = Profile;
        var fg = new Stepper("Frame generation", [(nameof(FrameGenMode.Off), "Off"), (nameof(FrameGenMode.Builtin), "Lossless (lsfg-vk)")],
            p.FrameGen == FrameGenMode.Builtin ? nameof(FrameGenMode.Builtin) : nameof(FrameGenMode.Off),
            "Doubles a 30 fps game to 60 fps with Lossless Scaling frame generation (lsfg-vk). Never goes above 60.")
        {
            Expands = true,
            DetailFor = v => v == nameof(FrameGenMode.Builtin)
                ? "Smoother motion, with slightly more input lag and occasional artifacts around fast-moving edges."
                : "The game runs at its own frame rate with no added lag.",
        };
        fg.Changed += v =>
        {
            SetProfile(x => x with { FrameGen = Enum.Parse<FrameGenMode>(v) });
            UpdateFrameGenNote();
        };
        Add(fg);
        var fps = new Stepper("Game's own frame rate", [("", "Unknown"), ("20", "20 fps"), ("30", "30 fps"), ("60", "60 fps")],
            p.NativeFps?.ToString() ?? "",
            "What the game normally runs at. Games that already run at 60 get no frame generation.")
        {
            Expands = true,
            DetailFor = v => v switch
            {
                "" => "Frame generation still runs, capped at 60. Set 60 for games that already run at 60.",
                "60" => "Already 60: frame generation is skipped for this game.",
                _ => $"Frame generation targets 60 fps from {v}.",
            },
        };
        fps.Changed += v =>
        {
            SetProfile(x => x with { NativeFps = v == "" ? null : int.Parse(v) });
            UpdateFrameGenNote();
        };
        Add(fps);
        _frameGenNote = UiTheme.Label("", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true);
        col.AddChild(_frameGenNote);
        UpdateFrameGenNote();
        var lsfg = Try(() => Host.Eden.FindLsfgVk(), null, "lsfg-vk status", col);
        if (lsfg == null)
        {
        }
        else if (lsfg.Kind != EdenDeck.Backends.Eden.SystemFiles.LsfgVk.Kind.V2)
        {
            col.AddChild(UiTheme.Label(lsfg.Kind == EdenDeck.Backends.Eden.SystemFiles.LsfgVk.Kind.Legacy
                ? "Frame generation needs lsfg-vk 2.x. The installed lsfg-vk is an older 1.x version."
                : "Frame generation needs lsfg-vk, which isn't installed yet.", UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
        }
        else if (Try(() => Host.Eden.LosslessDllPath(), "", "Lossless.dll status", col) == null)
        {
            col.AddChild(UiTheme.Label("Frame generation needs Lossless.dll from your own Lossless Scaling. Install Lossless Scaling in Steam and Scanline finds it.", UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
        }
    }

    /// <summary>Says when the Vulkan layer (CRT effect, quick menu) can't run.</summary>
    private void AddLayerNote(VBoxContainer col)
    {
        var layer = Host.DefaultLaunchOptions();
        if (layer.LayerKillSwitch || layer.LayerDirectory == null)
        {
            col.AddChild(UiTheme.Label(layer.LayerKillSwitch
                ? "The CRT effect is drawn by Scanline's in-game layer, which is switched off in Settings."
                : "The CRT effect is drawn by Scanline's in-game layer, which this install doesn't have.", UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
        }
    }

    private Stepper CrtRow(string emulator, bool moving)
    {
        var crt = new Stepper("CRT effect", [("0", "Off"), ("1", "Subtle"), ("2", "Medium"), ("3", "Strong")],
            Math.Clamp(Profile.Crt, 0, 3).ToString(),
            "Makes the game look like an old TV, the same as the app's CRT effect.")
        {
            Expands = true,
            DetailFor = v => (v, moving) switch
            {
                ("0", _) => $"No effect. The game looks the way {emulator} draws it.",
                ("1", true) => "Faint scanlines, a slight wobble and a gentle tube curve.",
                ("2", true) => "Scanlines, a wobbling picture, a rolling band, colour fringes and dark edges.",
                ("1", false) => "Faint scanlines on every pixel row and a gentle tube curve.",
                ("2", false) => "Scanlines, colour fringes, a tube curve and dark edges.",
                _ => "The full old-TV look. Fine detail gets a little softer.",
            },
        };
        crt.Changed += v => SetProfile(x => x with { Crt = int.Parse(v) });
        return crt;
    }

    /// <summary>RPCS3: frame generation and the CRT layer, as for Switch games (both need the AppImage, not the Flatpak).</summary>
    private void BuildRpcs3LaunchSetup(VBoxContainer col, Action<Control> Add)
    {
        col.AddChild(Section("Launch setup"));
        if (Host.Rpcs3.IsFlatpak)
        {
            col.AddChild(UiTheme.Label("You're using the RPCS3 Flatpak, which frame generation and the CRT effect can't reach. Install the RPCS3 AppImage under Emulators → RPCS3 to use them.",
                UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
        }
        AddFrameGenRows(col, Add);
        Add(CrtRow("RPCS3", moving: true));
        AddLayerNote(col);
        col.AddChild(UiTheme.Label("Scanline sets up your controller in RPCS3 before each game, like a PS3 pad: Cross on the bottom face button, Circle on the right, L2/R2 on the triggers, PS on View + Menu. If you set up pads in RPCS3 yourself (Open RPCS3, then Pads), yours are kept. Hold View + Start for 2 seconds to quit.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
    }

    /// <summary>mGBA: the CRT effect (as an mGBA shader) and what the in-game side can and can't do.</summary>
    private void BuildMgbaLaunchSetup(VBoxContainer col, Action<Control> Add)
    {
        col.AddChild(Section("Launch setup"));
        Add(CrtRow("mGBA", moving: false));
        var frame = new Stepper("Handheld frame", [("default", Host.Settings.HandheldFrame ? "Default (on)" : "Default (off)"), ("on", "On"), ("off", "Off")],
            Profile.Frame switch { true => "on", false => "off", _ => "default" },
            "A handheld-style shell and screen bezel in the side bars, in your accent colour. Decoration only.")
        {
            Expands = true,
            DetailFor = v => v switch
            {
                "on" => "Always framed for this game.",
                "off" => "Plain black bars for this game.",
                _ => "Follows Emulators → mGBA → Handheld frame.",
            },
        };
        frame.Changed += v => SetProfile(x => x with { Frame = v switch { "on" => true, "off" => false, _ => null } });
        Add(frame);
        col.AddChild(UiTheme.Label("In mGBA the CRT effect is a still picture effect: no wobble or rolling band, since mGBA's shaders have no clock.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        col.AddChild(UiTheme.Label("Buttons are set up like a GBA: A on the right face button, B on the bottom, L/R on the bumpers. Change them in mGBA's Settings → Controllers and Scanline keeps yours. Hold View + Start for 2 seconds to quit. Scanline's in-game menu and performance overlay only reach Vulkan emulators, so they aren't available in mGBA.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
    }

    /// <summary>One press for a whole set of the settings below; shows which set the game is on.</summary>
    private Control PresetRow(PresetSet set, ISettingsProvider settings, string emulator, string titleId, IReadOnlyDictionary<string, string> overrides)
    {
        var current = set.Current(overrides, settings.Options);
        var choices = new List<(string, string)>();
        if (current == PresetSet.Custom)
        {
            choices.Add((PresetSet.Custom, "Your own mix"));
        }
        choices.Add((PresetSet.Global, $"{emulator}'s settings"));
        choices.AddRange(set.All.Select(p => (p.Id, p.Label)));
        var row = new Stepper(set.Caption, choices, current,
            set.All.FirstOrDefault(p => p.Id == current)?.Detail
            ?? (current == PresetSet.Global ? set.Intro : "You've changed the settings below by hand. Pick a preset to replace them."));
        row.Changed += v =>
        {
            if (v == PresetSet.Custom)
            {
                return;
            }
            if (App.Guard(() => set.Apply(settings, titleId, v)))
            {
                Rebuild();
            }
        };
        return row;
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
        if (_card == null || !IsInstanceValid(_card) || _opened)
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
