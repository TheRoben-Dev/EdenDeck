using EdenDeck.Core.Backends;
using EdenDeck.Host.Updates;
using EdenDeck.App.Screens;
using EdenDeck.App.Ui;
using EdenDeck.Backends.Eden.Config;
using EdenDeck.Backends.Eden.SystemFiles;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Host;
using Godot;

namespace EdenDeck.App;

/// <summary>
/// Root node: chrome (header, hint bar, toast), the screen stack and input routing.
/// All non-UI work goes through <see cref="AppHost"/>, the same code the headless CLI uses.
/// </summary>
public partial class Main : Control
{
    private static readonly string[] TabNames = ["Library", "Eden", "Settings"];
    private static readonly Func<Screen>[] TabFactories = [() => new LibraryScreen(), () => new EdenScreen(), () => new SettingsScreen()];

    private readonly List<Screen> _stack = new();
    private Control _content = null!;
    private Control _overlay = null!;
    private Backdrop _backdrop = null!;
    private Header _header = null!;
    private Control _bgLayer = null!;
    private VBoxContainer _root = null!;
    private FocusHighlight _highlight = null!;
    private HintBar _hints = null!;
    private Toast _toast = null!;
    private int _tab;
    private bool _appFocused = true;

    public static Main Instance { get; private set; } = null!;

    /// <summary>Null only if startup failed (shown on screen).</summary>
    public AppHost? Host { get; private set; }

    /// <summary>True while a game runs: the app ignores controller input so game presses don't leak in.</summary>
    public bool GameRunning
    {
        get => _gameRunning;
        private set
        {
            _gameRunning = value;
            if (_highlight != null)
            {
                _highlight.Suspended = value;
            }
        }
    }

    private bool _gameRunning;

    public override void _Ready()
    {
        if (Pad.IsHandheld)
        {
            Input.MouseMode = Input.MouseModeEnum.Hidden;  // shown again when a real mouse moves
        }
        Instance = this;
        Pad.Setup();
        Pad.FamilyChanged += UpdateChrome;
        AppHost? host = null;
        Exception? startupError = null;
        try
        {
            host = new AppHost();
        }
        catch (Exception ex)
        {
            startupError = ex;
        }
        UiTheme.Use(host?.Settings.Theme ?? "dark");
        Anim.Enabled = !(host?.Settings.ReduceMotion ?? false);
        Theme = UiTheme.Build();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (OS.GetCmdlineUserArgs().Contains("--windowed"))
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        }

        _bgLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _bgLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_bgLayer);
        BuildBackground();

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 40);
        }
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 16);
        margin.AddChild(root);
        _root = root;
        _header = NewHeader();
        root.AddChild(_header);
        _content = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(_content);
        _hints = new HintBar();
        root.AddChild(_hints);
        // Overlay layer (game cards) above header and content; the hint bar stays visible below it.
        _overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _overlay.OffsetBottom = -56;
        AddChild(_overlay);
        _backdrop = new Backdrop();
        _backdrop.Clicked += () =>
        {
            if (_stack.Count > 1 && _stack[^1].IsOverlay && !GameRunning)
            {
                Pop();
            }
        };
        _overlay.AddChild(_backdrop);
        _highlight = new FocusHighlight();
        AddChild(_highlight);
        _toast = new Toast();
        AddChild(_toast);

        // Rows with an expanding explanation show it in place; the hint bar would only repeat it.
        GetViewport().GuiFocusChanged += c => _hints.SetHelp(c.HasMeta(Stepper.ExpandsMeta) ? "" : c.TooltipText);

        Host = host;
        if (startupError is { } e)
        {
            GD.PushError(e.ToString());
            var msg = UiTheme.Label($"Eden Deck could not start:\n\n{e.Message}", wrap: true);
            msg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _content.AddChild(msg);
            return;
        }
        SwitchTab(0);

        var args = OS.GetCmdlineUserArgs();
        var smoke = Array.IndexOf(args, "--smoke");
        if (smoke >= 0 && smoke + 1 < args.Length)
        {
            AddChild(new SmokeTest(args[smoke + 1]));
        }
        else
        {
            if (!ShowWhatsNew() && Host is { Settings.CheckForUpdates: true, Updater.CanSelfUpdate: true })
            {
                CheckForAppUpdate(silent: true);
            }
            CheckForEdenUpdate();
        }
    }

    private Header NewHeader()
    {
        var h = new Header(TabNames);
        h.TabClicked += i =>
        {
            if (_stack.Count == 1 && !GameRunning)
            {
                SwitchTab(i);
            }
        };
        return h;
    }

    /// <summary>Background colour, accent glow and the theme's decoration (dot grid, scanlines, LCD grid).</summary>
    private void BuildBackground()
    {
        foreach (var c in _bgLayer.GetChildren())
        {
            c.QueueFree();
        }
        var bg = new ColorRect { Color = UiTheme.Bg, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _bgLayer.AddChild(bg);
        if (UiTheme.Spec.Glow > 0)
        {
            // Soft accent glow from the top-left corner so the background isn't flat.
            var glow = new TextureRect
            {
                Texture = new GradientTexture2D
                {
                    Gradient = new Gradient
                    {
                        Offsets = [0f, 1f],
                        Colors = [UiTheme.Accent with { A = UiTheme.Spec.Glow }, UiTheme.Accent with { A = 0f }],
                    },
                    Fill = GradientTexture2D.FillEnum.Radial,
                    FillFrom = new Vector2(0.1f, 0f),
                    FillTo = new Vector2(0.85f, 0.9f),
                    Width = 256,
                    Height = 256,
                },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            glow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _bgLayer.AddChild(glow);
        }
        switch (UiTheme.Spec.Decor)
        {
            case Decor.DotGrid:
                _bgLayer.AddChild(new DotGrid());
                break;
            case Decor.PixelGrid:
                _bgLayer.AddChild(new PixelGrid());
                break;
        }
        // Scanlines sit above everything (ZIndex), from the theme or the CRT setting.
        if (UiTheme.Spec.Decor == Decor.Scanlines || Host?.Settings.Crt == true)
        {
            _bgLayer.AddChild(new Scanlines(strong: UiTheme.Spec.Decor != Decor.Scanlines));
        }
    }

    /// <summary>
    /// Switches theme in place: same tab, same page stack, same focus and scroll position. The old
    /// frame is kept as a picture on top and faded out, so the change is a quick crossfade.
    /// </summary>
    public void ApplyTheme(string name)
    {
        TextureRect? snap = null;
        if (Anim.Enabled)
        {
            var img = GetViewport().GetTexture().GetImage();
            snap = new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(img),
                MouseFilter = MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ZIndex = 200,
            };
            snap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
        UiTheme.Use(name);
        Theme = UiTheme.Build();
        BuildBackground();
        var header = NewHeader();
        header.SetBadge(Array.IndexOf(TabNames, "Eden"), EdenUpdate != null);
        _root.AddChild(header);
        _root.MoveChild(header, _header.GetIndex());
        _header.QueueFree();
        _header = header;
        var hints = new HintBar();
        _root.AddChild(hints);
        _root.MoveChild(hints, _hints.GetIndex());
        _hints.QueueFree();
        _hints = hints;
        _toast.Restyle();
        _highlight.Restyle();
        for (var i = 0; i < _stack.Count; i++)
        {
            _stack[i].Rebuild();
            if (i < _stack.Count - 1 && _stack[i + 1].IsOverlay)
            {
                SetFocusable(_stack[i], false);
            }
        }
        UpdateChrome();
        if (GetViewport().GuiGetFocusOwner() is { } f)
        {
            _hints.SetHelp(f.HasMeta(Stepper.ExpandsMeta) ? "" : f.TooltipText);
        }
        if (snap != null)
        {
            AddChild(snap);
            var t = snap.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            t.TweenProperty(snap, "modulate:a", 0f, Anim.Slow + 0.08);
            t.TweenCallback(Callable.From(snap.QueueFree));
        }
    }

    /// <summary>Re-applies the background decoration (CRT setting changed).</summary>
    public void RefreshBackground() => BuildBackground();

    /// <summary>Newer Eden build on the installed channel, found by the last background check.</summary>
    public AvailableRelease? EdenUpdate { get; private set; }

    private DateTime _edenCheckedAt = DateTime.MinValue;
    private bool _edenChecking;

    /// <summary>
    /// Quietly asks Eden's official feed whether the installed channel has a newer build (only for
    /// copies Eden Deck installed; a user's own AppImage has no comparable version). Shows a badge
    /// on the Eden tab and a banner on the Eden page. Throttled to once every 15 minutes.
    /// </summary>
    public async void CheckForEdenUpdate(bool force = false)
    {
        if (Host?.Eden.Install.Current is not { ManagedByEdenDeck: true } cur || _edenChecking ||
            (!force && DateTime.UtcNow - _edenCheckedAt < TimeSpan.FromMinutes(15)))
        {
            return;
        }
        _edenChecking = true;
        try
        {
            var install = Host.Eden.Install;
            var found = await Task.Run(() => install.CheckForUpdateAsync(cur.Channel, CancellationToken.None));
            _edenCheckedAt = DateTime.UtcNow;
            SetEdenUpdate(found);
        }
        catch (Exception e)
        {
            // Offline or feed hiccup: keep whatever we knew. A background check never interrupts.
            GD.Print("Eden update check failed: " + e.Message);
        }
        finally
        {
            _edenChecking = false;
        }
    }

    /// <summary>Records the check result; the Eden page rebuilds to show or drop its banner.</summary>
    public void SetEdenUpdate(AvailableRelease? release)
    {
        var changed = release?.Version != EdenUpdate?.Version;
        EdenUpdate = release;
        _header.SetBadge(Array.IndexOf(TabNames, "Eden"), release != null);
        if (changed)
        {
            foreach (var s in _stack.OfType<EdenScreen>())
            {
                s.Rebuild();
            }
        }
    }

    /// <summary>
    /// After an update, shows the changes since the last version the user saw, once. A fresh install
    /// just records the version. Returns true if the page was shown.
    /// </summary>
    private bool ShowWhatsNew()
    {
        if (Host == null)
        {
            return false;
        }
        var current = Host.Updater.CurrentVersion;
        var display = AppUpdater.DisplayVersion(current);
        var seen = AppUpdater.ParseVersion(Host.Settings.LastSeenVersion);
        if (Host.Settings.LastSeenVersion != display)
        {
            Host.SaveSettings(Host.Settings with { LastSeenVersion = display });
        }
        if (seen == null)
        {
            return false;
        }
        var news = Changelog.Between(Changelog.Bundled, seen, current);
        if (news.Count == 0)
        {
            return false;
        }
        Push(new ChangelogPage($"What's new in Eden Deck {display}", news, whatsNew: true));
        return true;
    }

    /// <summary>Full release history (Settings → Updates).</summary>
    public void ShowChangelog() => Push(new ChangelogPage("Changelog", Changelog.Bundled));

    /// <summary>Asks GitHub for a newer Eden Deck. Silent: no message when up to date or offline.</summary>
    public async void CheckForAppUpdate(bool silent)
    {
        if (Host == null)
        {
            return;
        }
        var updater = Host.Updater;
        AppRelease? release;
        try
        {
            release = await Task.Run(() => updater.CheckAsync());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (!silent)
            {
                Error("Couldn't reach GitHub to check for updates.");
            }
            return;
        }
        var current = AppUpdater.DisplayVersion(updater.CurrentVersion);
        if (release == null)
        {
            if (!silent)
            {
                Success($"Eden Deck {current} is the latest version");
            }
            return;
        }
        // A startup check never interrupts a running game or a page the user is on.
        if (silent && (GameRunning || _stack.Count != 1))
        {
            return;
        }
        var next = AppUpdater.DisplayVersion(release.Version);
        if (!updater.CanSelfUpdate)
        {
            Toast($"Eden Deck {next} is available. Re-run the installer to update this copy.");
            return;
        }
        var whatsNew = Changelog.ItemsFromReleaseNotes(release.Notes);
        var notes = whatsNew.Count > 0 ? "\n\nWhat's new:\n" + string.Join("\n", whatsNew.Take(8).Select(i => "• " + i)) : "";
        Push(new ChoicePage($"Eden Deck {next} is available (you have {current}).{notes}\n\nThe update takes a few seconds. Your games, keys and settings are not touched, and the current version is kept so you can roll back in Settings.",
            ("Update and restart", () => InstallAppUpdate(release)),
            ("Later", () => { })));
    }

    private void InstallAppUpdate(AppRelease release)
    {
        if (Host == null || GameRunning)
        {
            return;
        }
        var updater = Host.Updater;
        Busy($"Updating to {AppUpdater.DisplayVersion(release.Version)}…", (p, ct) => updater.InstallAsync(release, p, ct), Restart);
    }

    public void RollbackApp()
    {
        if (Host == null || GameRunning)
        {
            return;
        }
        Guard(() => Restart(Host.Updater.Rollback()));
    }

    /// <summary>Starts the given app binary with the same arguments and quits this one.</summary>
    private void Restart(string exe)
    {
        try
        {
            AppUpdater.Relaunch(exe, OS.GetCmdlineUserArgs());
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Error("Updated. Please start Eden Deck again to finish.");
            return;
        }
        GetTree().Quit();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
        {
            _appFocused = false;
            Pad.ReleaseAll();
        }
        else if (what == NotificationApplicationFocusIn)
        {
            _appFocused = true;
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!GameRunning && TouchScroll.Handle(GetViewport(), e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        var joy = e is InputEventJoypadButton or InputEventJoypadMotion;
        if (joy && (GameRunning || !_appFocused))
        {
            // Godot receives joypad input even when unfocused; never react to presses meant for the game.
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Pad.Translate(e))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private CancellationTokenSource? _gameCts;
    private double _chordHeld;

    /// <summary>How long View+Start (Select+Start) must be held to close the running game.</summary>
    public const double CloseChordSeconds = 2.0;

    /// <summary>Closes the running game: SIGTERM to Eden (so it saves), then a kill after a few seconds.</summary>
    public void CloseGame()
    {
        if (_gameCts is { IsCancellationRequested: false } c)
        {
            c.Cancel();
        }
    }

    /// <summary>
    /// The close chord is read from the pad state directly: Godot still sees the controller
    /// while Eden has focus, and the chord works even though app input is ignored during a game.
    /// </summary>
    private void WatchCloseChord(double delta)
    {
        var held = Input.GetConnectedJoypads().Any(d => Input.IsJoyButtonPressed(d, JoyButton.Back) && Input.IsJoyButtonPressed(d, JoyButton.Start));
        _chordHeld = held ? _chordHeld + delta : 0;
        if (_chordHeld >= CloseChordSeconds)
        {
            _chordHeld = double.NegativeInfinity;  // once per hold
            CloseGame();
        }
    }

    public override void _Process(double delta)
    {
        if (GameRunning)
        {
            WatchCloseChord(delta);
        }
        if (!GameRunning && _appFocused)
        {
            TouchScroll.Process(delta);
            Pad.Process(delta);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_stack.Count == 0 || GameRunning)
        {
            return;
        }
        var top = _stack[^1];
        if (e.IsActionPressed(Pad.Back))
        {
            if (!top.OnBack() && _stack.Count > 1)
            {
                Pop();
            }
        }
        else if (e.IsActionPressed(Pad.TabPrev) && _stack.Count == 1)
        {
            SwitchTab((_tab + TabNames.Length - 1) % TabNames.Length);
        }
        else if (e.IsActionPressed(Pad.TabNext) && _stack.Count == 1)
        {
            SwitchTab((_tab + 1) % TabNames.Length);
        }
        else if (e.IsActionPressed(Pad.Secondary) || e.IsActionPressed(Pad.Tertiary))
        {
            top.OnAction(e.IsActionPressed(Pad.Secondary) ? Pad.Secondary : Pad.Tertiary);
        }
        else
        {
            return;
        }
        GetViewport().SetInputAsHandled();
    }

    // ---- navigation ----

    private void SwitchTab(int index)
    {
        var dir = index == _tab ? 0 : (index - _tab + TabNames.Length) % TabNames.Length == 1 ? 1 : -1;
        _tab = index;
        foreach (var s in _stack)
        {
            if (dir != 0 && s.Visible && !s.IsOverlay)
            {
                SetFocusable(s, false);
                Anim.Exit(s, -dir);
            }
            else
            {
                s.QueueFree();
            }
        }
        _stack.Clear();
        var screen = TabFactories[index]();
        _stack.Add(screen);
        _content.AddChild(screen);
        if (dir != 0)
        {
            Anim.Enter(screen, dir);
        }
        else
        {
            Anim.FadeIn(screen);
        }
        UpdateChrome();
    }

    private bool OverlayOpen => _stack.Any(x => x.IsOverlay);

    public void Push(Screen screen)
    {
        var top = _stack.Count > 0 ? _stack[^1] : null;
        top?.Remember();
        if (screen.IsOverlay)
        {
            // The page behind stays visible (dimmed and blurred) but can't take focus.
            if (top != null)
            {
                SetFocusable(top, false);
            }
            _stack.Add(screen);
            _overlay.AddChild(screen);
            _backdrop.FadeTo(true);
            screen.AnimateOpen();
        }
        else
        {
            if (top != null)
            {
                top.Visible = false;
            }
            _stack.Add(screen);
            // Pages opened from a card (pickers, confirmations) sit above the backdrop too.
            (OverlayOpen ? _overlay : _content).AddChild(screen);
            Anim.Enter(screen, 1);
        }
        UpdateChrome();
    }

    public void Pop()
    {
        if (_stack.Count <= 1)
        {
            return;
        }
        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        var prev = _stack[^1];
        if (top.IsOverlay)
        {
            top.AnimateClose(top.QueueFree);
            SetFocusable(prev, true);
            if (!OverlayOpen)
            {
                _backdrop.FadeTo(false);
            }
        }
        else
        {
            SetFocusable(top, false);
            Anim.Exit(top, 1);
            prev.Visible = true;
            Anim.Enter(prev, -1);
        }
        prev.Restore();
        UpdateChrome();
    }

    /// <summary>Turns keyboard/pad focus off (and back on) for everything in a page behind an overlay.</summary>
    private static void SetFocusable(Node root, bool on)
    {
        foreach (var n in root.GetChildren())
        {
            if (n is Control c)
            {
                if (!on && c.FocusMode != FocusModeEnum.None)
                {
                    c.SetMeta("ed_focus", (int)c.FocusMode);
                    c.FocusMode = FocusModeEnum.None;
                }
                else if (on && c.HasMeta("ed_focus"))
                {
                    c.FocusMode = (FocusModeEnum)c.GetMeta("ed_focus").AsInt32();
                    c.RemoveMeta("ed_focus");
                }
            }
            SetFocusable(n, on);
        }
    }

    /// <summary>Pops back to the given screen if it is still on the stack.</summary>
    public void PopTo(Screen screen)
    {
        while (_stack.Count > 1 && _stack[^1] != screen)
        {
            Pop();
        }
    }

    public Screen Top => _stack[^1];

    public void UpdateChrome()
    {
        _header.SetTab(_tab, _stack.Count == 1);
        if (_stack.Count == 0)
        {
            return;
        }
        var hints = new List<(string, string)>();
        hints.AddRange(_stack[^1].Hints);
        hints.Add((Pad.Accept, "Select"));
        if (_stack.Count > 1)
        {
            hints.Add((Pad.Back, "Back"));
        }
        _hints.SetHints(hints);
    }

    // ---- feedback ----

    public void Toast(string text) => _toast.Show(text);

    public void Error(string text) => _toast.Show(text, UiTheme.Bad);

    public void Success(string text) => _toast.Show(text, UiTheme.Good);

    /// <summary>Runs an action and shows expected failures (I/O, bad files, Eden running) as a toast.</summary>
    public bool Guard(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            Error(e.Message);
            return false;
        }
    }

    public static bool IsUserFacing(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
            or KeyImportException or EdenRunningException or InvalidDataException or HttpRequestException
            or KeyNotFoundException or TaskCanceledException;

    public void Confirm(string message, string yes, Action onYes) => Push(new ConfirmPage(message, yes, onYes));

    /// <summary>Runs background work behind a busy page. Progress is 0..1 or negative for indeterminate.</summary>
    public async void Busy<T>(string title, Func<IProgress<double>, CancellationToken, Task<T>> work, Action<T> done, bool cancellable = true)
    {
        using var cts = new CancellationTokenSource();
        var page = new BusyPage(title, cancellable ? cts : null);
        Push(page);
        var progress = new Progress<double>(page.SetProgress);  // created on the main thread: reports marshal back
        try
        {
            var result = await Task.Run(() => work(progress, cts.Token), cts.Token);
            PopTo(page);
            Pop();
            done(result);
        }
        catch (Exception e) when (IsUserFacing(e) || e is OperationCanceledException)
        {
            PopTo(page);
            Pop();
            if (e is not OperationCanceledException)
            {
                Error(e.Message);
            }
        }
    }

    /// <summary>Applies the game's settings and runs it. The app goes idle until Eden exits.</summary>
    public void Launch(LibraryGame game)
    {
        if (Host == null || GameRunning)
        {
            return;
        }
        var controllerNote = EnsureController();
        LaunchPlan plan;
        try
        {
            plan = Host.PlanLaunch(game);
        }
        catch (EdenDeck.Backends.Eden.SystemFiles.KeysTooOldException e)
        {
            // Our own controller-friendly explanation instead of Eden's desktop error dialog.
            Push(new ChoicePage(e.Message, ("OK", () => { })));
            return;
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            Error(e.Message);
            return;
        }
        Host.MarkPlayed(game);
        var notes = controllerNote != null ? plan.Notes.Prepend(controllerNote).ToList() : plan.Notes;
        RunEden(game.Base.Title, plan, notes, null, game);
    }

    /// <summary>Opens Eden's own window for anything Eden Deck doesn't cover; re-reads Eden's config after.</summary>
    public void OpenEden()
    {
        if (Host == null || GameRunning)
        {
            return;
        }
        LaunchPlan plan;
        try
        {
            plan = Host.Eden.PlanEdenUi();
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            Error(e.Message);
            return;
        }
        RunEden("Eden settings", plan, ["Change anything in Eden's window, then close it to come back. Eden Deck reads Eden's settings again afterwards."],
            () =>
            {
                Host.Eden.ReloadPaths();
                foreach (var screen in _stack)
                {
                    screen.Rebuild();  // pages show values read from Eden's config
                }
                Success("Back from Eden. Settings reloaded.");
            });
    }

    /// <summary>Player 1 gets a controller mapping when Eden still has only the keyboard (Game Mode needs this).</summary>
    private string? EnsureController()
    {
        if (Host == null)
        {
            return null;
        }
        try
        {
            if (Pad.ActivePad() is { } pad && Host.Eden.EnsureControllerMapping(pad))
            {
                return $"controller: mapped {pad.Name} as player 1 (Eden had only the keyboard)";
            }
            // Still on the keyboard and nothing we can map: say what we see, so it can be mapped in Eden.
            return Host.Eden.ControllerNeedsMapping()
                ? $"controller: Eden still uses the keyboard for player 1. Eden Deck sees: {Pad.DescribePads()}. Map it once with Open Eden → Controls."
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            GD.PushWarning($"controller mapping skipped: {e.Message}");
            return null;
        }
    }

    private async void RunEden(string title, LaunchPlan plan, IReadOnlyList<string> notes, Action? after, LibraryGame? game = null)
    {
        var started = DateTime.UtcNow;
        var page = new RunningPage(title, notes);
        Push(page);
        GameRunning = true;
        Pad.ReleaseAll();
        var log = new List<string>();
        int code;
        using var cts = new CancellationTokenSource();
        _gameCts = cts;
        try
        {
            code = await Task.Run(() => new ProcessLauncher(l => { lock (log) log.Add(l); }).RunAsync(plan, cts.Token));
        }
        catch (OperationCanceledException)
        {
            // Closed from Eden Deck (button chord or the Close game button): not a failure.
            _gameCts = null;
            GameRunning = false;
            PopTo(page);
            Pop();
            GetWindow().GrabFocus();
            Toast($"Closed {title}.");
            after?.Invoke();
            return;
        }
        catch (Exception e) when (IsUserFacing(e) || e is System.ComponentModel.Win32Exception)
        {
            _gameCts = null;
            GameRunning = false;
            PopTo(page);
            Pop();
            Error($"Could not start Eden: {e.Message}");
            return;
        }
        _gameCts = null;
        GameRunning = false;
        PopTo(page);
        Pop();
        GetWindow().GrabFocus();
        var seconds = (DateTime.UtcNow - started).TotalSeconds;
        // A game that closes within a minute, or any non-zero exit, is almost always a failed start:
        // say so and show why, instead of silently dropping back to the card.
        if (game != null && (code != 0 || seconds < 60))
        {
            ShowEdenExit(game, code, seconds, log);
        }
        else if (code != 0)
        {
            Error($"Eden exited with code {code}.");
        }
        after?.Invoke();
    }

    private void ShowEdenExit(LibraryGame game, int code, double seconds, List<string> output)
    {
        var tail = EdenLogTail(output);
        var how = code == 0 ? "closed" : code > 128 ? $"crashed (signal {code - 128})" : $"exited with code {code}";
        var msg = $"Eden {how} after {seconds:0} s.\n\n" + (tail.Length > 0 ? "Last lines of Eden's log:\n" + tail : "Eden wrote nothing to its log.");
        var options = new List<(string, Action)> { ("OK", () => { }) };
        var profile = Host!.Profiles.Get(game.GameId);
        if (profile.LoadAddOns && (game.Updates.Count > 0 || game.Dlc.Count > 0))
        {
            options.Add(("Try without update and DLC", () =>
            {
                Host.Profiles.Set(game.GameId, profile with { LoadAddOns = false });
                foreach (var s in _stack)
                {
                    s.Rebuild();
                }
                Launch(game);
            }));
        }
        Push(new ChoicePage(msg, [.. options]));
    }

    /// <summary>The interesting end of Eden's log: errors first, else the last lines (file log, then stdout).</summary>
    private string EdenLogTail(List<string> output)
    {
        IEnumerable<string> lines = [];
        try
        {
            var dir = Path.Combine(Host!.Eden.Paths.DataDir, "log");
            var file = Directory.Exists(dir)
                ? new DirectoryInfo(dir).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (file != null && DateTime.UtcNow - file.LastWriteTimeUtc < TimeSpan.FromMinutes(5))
            {
                lines = File.ReadLines(file.FullName).TakeLast(400).ToList();
            }
        }
        catch (IOException)
        {
        }
        if (!lines.Any())
        {
            lock (output)
            {
                lines = output.TakeLast(400).ToList();
            }
        }
        var list = lines.ToList();
        var errors = list.Where(l => l.Contains("<Critical>") || l.Contains("<Error>")).TakeLast(6).ToList();
        var pick = errors.Count > 0 ? errors : list.TakeLast(6).ToList();
        // Drop the "[  12.345678] Core.Foo <Error> file.cpp:Func:123: " prefix noise down to the message.
        return string.Join("\n", pick.Select(l =>
        {
            var i = l.IndexOf(": ", l.IndexOf('>') + 1, StringComparison.Ordinal);
            var m = l.Contains('>') && i > 0 ? l[(i + 2)..] : l;
            return "• " + (m.Length > 160 ? m[..160] + "…" : m);
        }));
    }
}
