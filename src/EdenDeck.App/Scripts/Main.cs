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
    private HintBar _hints = null!;
    private Toast _toast = null!;
    private int _tab;
    private bool _appFocused = true;

    public static Main Instance { get; private set; } = null!;

    /// <summary>Null only if startup failed (shown on screen).</summary>
    public AppHost? Host { get; private set; }

    /// <summary>True while a game runs: the app ignores controller input so game presses don't leak in.</summary>
    public bool GameRunning { get; private set; }

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

        var bg = new ColorRect { Color = UiTheme.Bg };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        // Soft accent glow from the top-left corner so the background isn't flat.
        var glow = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient
                {
                    Offsets = [0f, 1f],
                    Colors = [UiTheme.Accent with { A = UiTheme.Terminal ? 0.07f : UiTheme.Nightgrid ? 0.06f : 0.13f }, UiTheme.Accent with { A = 0f }],
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
        AddChild(glow);
        if (UiTheme.Nightgrid)
        {
            AddChild(new DotGrid());
        }
        if (UiTheme.Terminal)
        {
            AddChild(new Scanlines());
        }

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
        _header = new Header(TabNames);
        _header.TabClicked += i =>
        {
            if (_stack.Count == 1 && !GameRunning)
            {
                SwitchTab(i);
            }
        };
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
        _toast = new Toast();
        AddChild(_toast);

        GetViewport().GuiFocusChanged += c => _hints.SetHelp(c.TooltipText);

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
        else if (Host is { Settings.CheckForUpdates: true, Updater.CanSelfUpdate: true })
        {
            CheckForAppUpdate(silent: true);
        }
    }

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
        Push(new ChoicePage($"Eden Deck {next} is available (you have {current}).\n\nThe update takes a few seconds. Your games, keys and settings are not touched, and the current version is kept so you can roll back in Settings.",
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

    public override void _Process(double delta)
    {
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
            s.QueueFree();
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

    /// <summary>Re-creates the whole UI (after a theme change).</summary>
    public void Reload() => GetTree().ReloadCurrentScene();

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
            top.QueueFree();
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
    public async void Launch(LibraryGame game)
    {
        if (Host == null || GameRunning)
        {
            return;
        }
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
        var page = new RunningPage(game.Base.Title, plan.Notes);
        Push(page);
        GameRunning = true;
        Pad.ReleaseAll();
        var log = new List<string>();
        int code;
        try
        {
            code = await Task.Run(() => new ProcessLauncher(l => { lock (log) log.Add(l); }).RunAsync(plan));
        }
        catch (Exception e) when (IsUserFacing(e) || e is System.ComponentModel.Win32Exception)
        {
            GameRunning = false;
            PopTo(page);
            Pop();
            Error($"Could not start Eden: {e.Message}");
            return;
        }
        GameRunning = false;
        PopTo(page);
        Pop();
        if (code != 0)
        {
            Error($"Eden exited with code {code}.");
        }
        GetWindow().GrabFocus();
    }
}
