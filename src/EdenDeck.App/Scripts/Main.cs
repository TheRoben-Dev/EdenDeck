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
        Instance = this;
        Pad.Setup();
        Pad.FamilyChanged += UpdateChrome;
        Theme = UiTheme.Build();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (OS.GetCmdlineUserArgs().Contains("--windowed"))
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        }

        var bg = new ColorRect { Color = UiTheme.Bg };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

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
        root.AddChild(_header);
        _content = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(_content);
        _hints = new HintBar();
        root.AddChild(_hints);
        _toast = new Toast();
        AddChild(_toast);

        GetViewport().GuiFocusChanged += c => _hints.SetHelp(c.TooltipText);

        try
        {
            Host = new AppHost();
        }
        catch (Exception e)
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
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
        {
            _appFocused = false;
        }
        else if (what == NotificationApplicationFocusIn)
        {
            _appFocused = true;
        }
    }

    public override void _Input(InputEvent e)
    {
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
        _tab = index;
        foreach (var s in _stack)
        {
            s.QueueFree();
        }
        _stack.Clear();
        var screen = TabFactories[index]();
        _stack.Add(screen);
        _content.AddChild(screen);
        UpdateChrome();
    }

    public void Push(Screen screen)
    {
        if (_stack.Count > 0)
        {
            var top = _stack[^1];
            top.Remember();
            top.Visible = false;
        }
        _stack.Add(screen);
        _content.AddChild(screen);
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
        top.QueueFree();
        var prev = _stack[^1];
        prev.Visible = true;
        prev.Restore();
        UpdateChrome();
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
        catch (Exception e) when (IsUserFacing(e))
        {
            Error(e.Message);
            return;
        }
        Host.MarkPlayed(game);
        var page = new RunningPage(game.Base.Title, plan.Notes);
        Push(page);
        GameRunning = true;
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
