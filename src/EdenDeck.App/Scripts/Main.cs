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
    private static readonly string[] TabNames = ["Library", "Emulators", "Settings"];
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
            if (_gameRunning != value)
            {
                AppLog.Info(value ? "game running" : "game ended");
            }
            _gameRunning = value;
            if (_highlight != null)
            {
                _highlight.Suspended = value;
            }
            PauseBackground();
        }
    }

    private AnimatedBackground? _animBg;
    private Scanlines? _crt;

    /// <summary>Background motion stops while a game runs or the window is in the background.</summary>
    private void PauseBackground()
    {
        var paused = _gameRunning || !_appFocused;
        if (_animBg != null && IsInstanceValid(_animBg))
        {
            _animBg.Paused = paused;
        }
        if (_crt != null && IsInstanceValid(_crt))
        {
            _crt.Paused = paused;
        }
    }

    private bool _gameRunning;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Hidden;  // never drawn; clicks and touch still work
        Instance = this;
        Pad.Setup();
        Pad.FamilyChanged += UpdateChrome;
        // First start after the rename: ~/Applications/EdenDeck moves to ~/Applications/Scanline
        // (the old path stays as a link), then the app restarts from the new folder.
        if (!OS.GetCmdlineUserArgs().Contains("--smoke") && System.Environment.ProcessPath is { } exe &&
            InstallMigration.Run(Path.GetDirectoryName(exe)!) is { } moved)
        {
            try
            {
                AppUpdater.Relaunch(moved, OS.GetCmdlineUserArgs());
                GetTree().Quit();
                return;
            }
            catch (Exception re) when (re is System.ComponentModel.Win32Exception or IOException)
            {
                GD.PushWarning($"Restart after moving the install failed: {re.Message}");
            }
        }
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
        if (host != null)
        {
            AppLog.Open(Path.Combine(host.Paths.Data, "logs"));
        }
        if (startupError != null)
        {
            AppLog.Error("startup: " + startupError);
        }
        var startTheme = host?.Settings.Theme ?? "dark";
        AppLog.Info($"theme {startTheme}, window {DisplayServer.WindowGetSize()}, pads: {string.Join(", ", Input.GetConnectedJoypads().Select(Input.GetJoyName))}");
        UiTheme.Use(startTheme, host?.Settings.Accents.GetValueOrDefault(startTheme));
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
        // A tick when the player moves focus (not when a page puts focus somewhere itself).
        GetViewport().GuiFocusChanged += _ =>
        {
            if (Time.GetTicksMsec() - _navAtMs < 250)
            {
                Feedback.Play(Cue.Move);
            }
        };

        Host = host;
        if (host != null)
        {
            host.Accent = "#" + UiTheme.Accent.ToHtml(false);
        }
        _feedback = new Feedback(UiTheme.Name)
        {
            Volume = host?.Settings.SoundVolume ?? 50,
            Haptics = host?.Settings.Haptics ?? true,
        };
        AddChild(_feedback);
        if (startupError is { } e)
        {
            GD.PushError(e.ToString());
            var msg = UiTheme.Label($"Scanline could not start:\n\n{e.Message}", wrap: true);
            msg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _content.AddChild(msg);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--open=appearance"))
        {
            // Back from a restart that applied a theme: Settings → Appearance, on the theme row.
            _tab = 2;
            var settings = new SettingsScreen { StartIn = "Appearance" };
            _stack.Add(settings);
            _content.AddChild(settings);
            UpdateChrome();
        }
        else
        {
            SwitchTab(0);
        }

        var args = OS.GetCmdlineUserArgs();
        var smoke = Array.IndexOf(args, "--smoke");
        var art = Array.IndexOf(args, "--render-steam-art");
        var sounds = Array.IndexOf(args, "--render-sounds");
        if (sounds >= 0 && sounds + 1 < args.Length)
        {
            // Development tool: every theme's sounds as .wav files, to listen to without the app.
            Feedback.WriteAll(args[sounds + 1]);
            GetTree().Quit();
        }
        else if (art >= 0 && art + 1 < args.Length)
        {
            AddChild(new SteamArtRenderer(args[art + 1], art + 2 < args.Length ? args[art + 2] : "Scanline", art + 3 < args.Length ? args[art + 3] : null));
        }
        else if (smoke >= 0 && smoke + 1 < args.Length)
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
            ApplySteamShortcuts();
            RenderSteamArt();
        }
    }

    /// <summary>Writes any Add to Steam shortcuts still waiting, when Steam is closed (never fatal).</summary>
    private void ApplySteamShortcuts()
    {
        if (Host is not { } host || !host.Steam.Available || host.Steam.Pending().Count == 0)
        {
            return;
        }
        try
        {
            if (host.Steam.SteamRunning)
            {
                host.Steam.WriteWhenSteamRestarts();  // e.g. the last helper timed out
                return;
            }
            host.Steam.Apply();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            AppLog.Warn($"steam: couldn't write shortcuts at startup: {e.Message}");
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

    /// <summary>Background colour, accent glow, the theme's animated background and decoration (dot grid, LCD grid), and the CRT effect.</summary>
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
        _animBg = null;
        _crt = null;
        if (Host?.Settings.AnimatedBackground != false)
        {
            _animBg = new AnimatedBackground(UiTheme.Spec);
            _bgLayer.AddChild(_animBg);
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
            _crt = new Scanlines(Host?.Settings.Crt == true ? Host.Settings.CrtLevel : 0);
            _bgLayer.AddChild(_crt);
        }
        PauseBackground();
    }

    /// <summary>
    /// Used by the UI smoke test only: players change theme through Settings, which restarts the
    /// app (switching live left the library unable to open games on the Deck).
    /// Switches theme in place: same tab, same page stack, same focus and scroll position. The old
    /// frame is kept as a picture on top and faded out, so the change is a quick crossfade.
    /// </summary>
    public void ApplyTheme(string name)
    {
        AppLog.Info($"theme -> {name} (stack: {StackNames()})");
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
        UiTheme.Use(name, Host?.Settings.Accents.GetValueOrDefault(name));
        if (Host != null)
        {
            Host.Accent = "#" + UiTheme.Accent.ToHtml(false);
        }
        Theme = UiTheme.Build();
        BuildBackground();
        var header = NewHeader();
        header.SetBadge(Array.IndexOf(TabNames, "Emulators"), EdenUpdate != null);
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
        AppLog.Info($"theme applied, focus: {Describe(GetViewport().GuiGetFocusOwner())}");
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
    /// copies Scanline installed; a user's own AppImage has no comparable version). Shows a badge
    /// on Emulators → Eden and a banner on the Eden page. Throttled to once every 15 minutes.
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
        _header.SetBadge(Array.IndexOf(TabNames, "Emulators"), release != null);
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
        Push(new ChangelogPage($"What's new in Scanline {display}", news, whatsNew: true));
        return true;
    }

    /// <summary>Full release history (Settings → Updates).</summary>
    public void ShowChangelog() => Push(new ChangelogPage("Changelog", Changelog.Bundled));

    /// <summary>Asks GitHub for a newer Scanline. Silent: no message when up to date or offline.</summary>
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
                Success($"Scanline {current} is the latest version");
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
            Toast($"Scanline {next} is available. Re-run the installer to update this copy.");
            return;
        }
        var whatsNew = Changelog.ItemsFromReleaseNotes(release.Notes);
        var notes = whatsNew.Count > 0 ? "\n\nWhat's new:\n" + string.Join("\n", whatsNew.Take(8).Select(i => "• " + i)) : "";
        Push(new ChoicePage($"Scanline {next} is available (you have {current}).{notes}\n\nThe update takes a few seconds. Your games, keys and settings are not touched, and the current version is kept so you can roll back in Settings.",
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
    /// <summary>
    /// Restarts the app with the same arguments (plus <paramref name="extra"/>), e.g. to apply a
    /// theme. Godot starts the new instance as this one exits, so it works from source and exported.
    /// </summary>
    public void RestartApp(params string[] extra)
    {
        AppLog.Info($"restart ({string.Join(' ', extra)})");
        var user = OS.GetCmdlineUserArgs().Where(a => !a.StartsWith("--open=", StringComparison.Ordinal)).Concat(extra).ToList();
        var args = OS.GetCmdlineArgs().ToList();
        if (user.Count > 0)
        {
            args.Add("--");
            args.AddRange(user);
        }
        OS.SetRestartOnExit(true, args.ToArray());
        GetTree().Quit();
    }

    private void Restart(string exe)
    {
        try
        {
            AppUpdater.Relaunch(exe, OS.GetCmdlineUserArgs());
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Error("Updated. Please start Scanline again to finish.");
            return;
        }
        GetTree().Quit();
    }

    /// <summary>
    /// Quitting: release Godot objects that C# statics keep alive, while .NET is still running.
    /// Left for engine shutdown they can abort the process ("script_bindings is not empty").
    /// </summary>
    public override void _ExitTree()
    {
        Theme = null;
        EdenDeck.App.Ui.Poly.Ps1.ReleaseShared();
        EdenDeck.App.Ui.Poly.CartStage.ReleaseShared();
        UiTheme.ReleaseFonts();
        // Wrappers of finished tweens, timers, input events and images still waiting for the GC
        // would otherwise be released by the engine after .NET is gone.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
        {
            _appFocused = false;
            AppLog.Info("window lost focus");
            Pad.ReleaseAll();
            PauseBackground();
        }
        else if (what == NotificationApplicationFocusIn)
        {
            _appFocused = true;
            AppLog.Info("window focused");
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            PauseBackground();
        }
    }

    private ulong _navAtMs;
    private Feedback? _feedback;

    /// <summary>Applies the sound and rumble settings right away (Settings → Sound & feel).</summary>
    public void ApplyFeedback(int volume, bool haptics)
    {
        if (_feedback != null)
        {
            _feedback.Volume = volume;
            _feedback.Haptics = haptics;
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!GameRunning && e is InputEventAction or InputEventKey && e.IsPressed() && !e.IsEcho())
        {
            if (e.IsAction("ui_up") || e.IsAction("ui_down") || e.IsAction("ui_left") || e.IsAction("ui_right"))
            {
                _navAtMs = Time.GetTicksMsec();
            }
            else if (e.IsAction(Pad.Accept) && GetViewport().GuiGetFocusOwner() is BaseButton and not Stepper)
            {
                Feedback.Play(Cue.Confirm);
            }
        }
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                AppLog.Info($"pointer {(mb.Pressed ? "down" : "up")} at {mb.Position.Round()} over {Describe(GetViewport().GuiGetHoveredControl())}, focus {Describe(GetViewport().GuiGetFocusOwner())}");
                break;
            case InputEventJoypadButton { Pressed: true } jb when jb.ButtonIndex is JoyButton.A or JoyButton.B:
                AppLog.Info($"pad {jb.ButtonIndex} (game running {GameRunning}, app focused {_appFocused}), focus {Describe(GetViewport().GuiGetFocusOwner())}");
                break;
        }
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
            var handled = top.OnBack();
            if (!handled && _stack.Count > 1)
            {
                Pop();
                handled = true;
            }
            if (handled)
            {
                Feedback.Play(Cue.Back);
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

    private string StackNames() => string.Join(" > ", _stack.Select(x => x.GetType().Name));

    private static string Describe(Control? c) =>
        c == null ? "none" : $"{c.GetType().Name} '{(c is Button b ? b.Text : c.TooltipText).Split('\n')[0]}'";

    private void SwitchTab(int index)
    {
        AppLog.Info($"tab -> {TabNames[index]}");
        if (index != _tab)
        {
            Feedback.Play(Cue.Tab);
        }
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
        AppLog.Info($"push {screen.GetType().Name} over {StackNames()}");
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
        AppLog.Info($"pop ({StackNames()})");
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

    public void Error(string text)
    {
        AppLog.Warn("shown error: " + text);
        Feedback.Play(Cue.Error);
        _toast.Show(text, UiTheme.Bad);
    }

    public void Success(string text)
    {
        Feedback.Play(Cue.Success);
        _toast.Show(text, UiTheme.Good);
    }

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

    /// <summary>
    /// Adds (or refreshes) a Steam shortcut. With Steam closed it's written right away; with Steam
    /// running (always, in Game Mode) Steam has to restart to pick it up, so the player chooses
    /// between restarting now and collecting a few more first.
    /// </summary>
    public void AddToSteam(string key, EdenDeck.Core.Steam.SteamShortcut shortcut)
    {
        var steam = Host!.Steam;
        if (steam.Unavailable is { } why)
        {
            Error(why);
            return;
        }
        var written = false;
        if (!Guard(() => written = steam.Add(key, shortcut)))
        {
            return;
        }
        RenderSteamArt();
        if (written)
        {
            Toast($"Added {shortcut.AppName} to Steam.");
            return;
        }
        SteamRestartNotice($"{shortcut.AppName} is ready for Steam.");
    }

    private bool _renderingArt;

    /// <summary>
    /// Renders 32-bit 3D style artwork (cover, wide, hero, logo, icon) for every game Scanline has
    /// added to Steam that doesn't have it yet, one at a time in the background, and puts it in
    /// Steam's grid folder. Also catches up on games added from the command line.
    /// </summary>
    public async void RenderSteamArt()
    {
        if (_renderingArt || Host is not { } host || !host.Steam.Available)
        {
            return;
        }
        _renderingArt = true;
        try
        {
            foreach (var e in host.Steam.Wanted().Where(e => !e.Remove && e.Key != EdenDeck.Host.SteamService.AppKey).ToList())
            {
                if (host.Steam.ArtDirFor(e.Key) is { } dir && !SteamArt.IsCurrent(dir))
                {
                    while (GameRunning)
                    {
                        await ToSignal(GetTree().CreateTimer(5), SceneTreeTimer.SignalName.Timeout);
                    }
                    // The game's own square icon goes on the cartridge label (read off the main thread).
                    var game = host.FindGame(e.Key);
                    var icon = game == null ? null : await Task.Run(() => host.Backend(game.Base.BackendId).IconFor(game.Base));
                    await SteamArt.Render(this, e.Shortcut.AppName, dir, icon);
                    AppLog.Info($"steam: rendered artwork for {e.Key}");
                }
                host.Steam.InstallArt(e.Key);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"steam: artwork: {ex.Message}");
        }
        finally
        {
            _renderingArt = false;
        }
    }

    /// <summary>
    /// Tells the player Steam has to be restarted to show new shortcuts. Scanline doesn't restart
    /// anything itself; a background helper writes the shortcuts as soon as Steam closes.
    /// </summary>
    public void SteamRestartNotice(string message)
    {
        Guard(() => Host!.Steam.WriteWhenSteamRestarts());
        Push(new ChoicePage(
            message + " Restart Steam to see the change in your library" + (GameMode ? " (restarting the Deck works too)." : "."),
            ("OK", () => { })));
    }

    private static bool GameMode => EdenDeck.Host.SteamService.GameMode;

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
        var controllerNote = game.Base.BackendId == Host.Eden.Id ? EnsureController() : null;
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
        Feedback.Play(Cue.Launch);
        var notes = controllerNote != null ? plan.Notes.Prepend(controllerNote).ToList() : plan.Notes;
        RunEden(game.Base.Title, plan, notes, null, game);
    }

    /// <summary>Opens Eden's own window for anything Scanline doesn't cover; re-reads Eden's config after.</summary>
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
        RunEden("Eden settings", plan, ["Change anything in Eden's window, then close it to come back. Scanline reads Eden's settings again afterwards."],
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

    /// <summary>RPCS3's own window, e.g. to set up controllers. Scanline waits until it's closed.</summary>
    public void OpenRpcs3()
    {
        if (Host == null || GameRunning)
        {
            return;
        }
        LaunchPlan plan;
        try
        {
            plan = Host.Rpcs3.PlanUi();
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            Error(e.Message);
            return;
        }
        RunEden("RPCS3", plan, ["Change anything in RPCS3's window (controllers are under Pads), then close it to come back."], () =>
        {
            foreach (var screen in _stack)
            {
                screen.Rebuild();
            }
            Success("Back from RPCS3.");
        }, emulator: "RPCS3");
    }

    /// <summary>RPCS3 installs the player's own PS3UPDAT.PUP; Scanline waits until RPCS3 is closed.</summary>
    public void InstallPs3Firmware(string pup)
    {
        if (Host == null || GameRunning)
        {
            return;
        }
        LaunchPlan plan;
        try
        {
            plan = Host.Rpcs3.PlanFirmwareInstall(pup);
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            Error(e.Message);
            return;
        }
        RunEden("PS3 system software", plan, ["RPCS3 is installing your system software. When it says it's done, close RPCS3 to come back."], () =>
        {
            foreach (var screen in _stack)
            {
                screen.Rebuild();
            }
            if (Host.Rpcs3.FirmwareInstalled)
            {
                Success("PS3 system software installed.");
            }
            else
            {
                Error("RPCS3 didn't install the system software. Check that the file is a PS3UPDAT.PUP and try again.");
            }
        }, emulator: "RPCS3");
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
                ? $"controller: Eden still uses the keyboard for player 1. Scanline sees: {Pad.DescribePads()}. Map it once with Open Eden → Controls."
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            GD.PushWarning($"controller mapping skipped: {e.Message}");
            return null;
        }
    }

    private async void RunEden(string title, LaunchPlan plan, IReadOnlyList<string> notes, Action? after, LibraryGame? game = null, string? emulator = null)
    {
        var started = DateTime.UtcNow;
        var page = new RunningPage(title, notes, emulator ?? EmulatorName(game));
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
            // Closed from Scanline (button chord or the Close game button): not a failure.
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
            Error($"Could not start {emulator ?? EmulatorName(game)}: {e.Message}");
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
            Error($"{emulator ?? EmulatorName(game)} exited with code {code}.");
        }
        after?.Invoke();
    }

    /// <summary>"Eden", "mGBA"... for messages about a game's emulator.</summary>
    private string EmulatorName(LibraryGame? game) =>
        game == null ? "Eden" : Host?.Backends.FirstOrDefault(b => b.Id == game.Base.BackendId)?.DisplayName ?? "The emulator";

    private void ShowEdenExit(LibraryGame game, int code, double seconds, List<string> output)
    {
        var eden = game.Base.BackendId == Host!.Eden.Id;
        var name = EmulatorName(game);
        var tail = EdenLogTail(output, fileLog: eden);
        var how = code == 0 ? "closed" : code > 128 ? $"crashed (signal {code - 128})" : $"exited with code {code}";
        var msg = $"{name} {how} after {seconds:0} s.\n\n" + (tail.Length > 0 ? $"Last lines of {name}'s log:\n" + tail : $"{name} wrote nothing to its log.");
        var options = new List<(string, Action)> { ("OK", () => { }) };
        var profile = Host!.Profiles.Get(game.GameId);
        if (eden && profile.LoadAddOns && (game.Updates.Count > 0 || game.Dlc.Count > 0))
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
    private string EdenLogTail(List<string> output, bool fileLog = true)
    {
        IEnumerable<string> lines = [];
        try
        {
            if (!fileLog)
            {
                throw new IOException("no file log");
            }
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
