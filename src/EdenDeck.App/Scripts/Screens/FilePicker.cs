using EdenDeck.App.Ui;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// Controller-navigable file/folder browser (the OS dialog is unusable with a gamepad and in
/// Game Mode). Places row on top (home, SD cards, mounted drives), list below.
/// </summary>
public partial class FilePicker : Screen
{
    private readonly string _title;
    private readonly bool _pickFolder;
    private readonly string[] _extensions;
    private readonly Action<string> _onPick;
    private string _dir;
    private Control? _firstItem;

    /// <param name="extensions">Lower-case extensions with dot for file mode; empty = any file.</param>
    public FilePicker(string title, bool pickFolder, Action<string> onPick, string? startDir = null, params string[] extensions)
    {
        _title = title;
        _pickFolder = pickFolder;
        _onPick = onPick;
        _extensions = extensions;
        var home = System.Environment.GetEnvironmentVariable("HOME") ?? "/";
        _dir = startDir != null && Directory.Exists(startDir) ? startDir : home;
    }

    public override IEnumerable<(string Action, string Label)> Hints =>
        _pickFolder ? [(Pad.Secondary, "Use this folder"), (Pad.Tertiary, "Up")] : [(Pad.Tertiary, "Up")];

    protected override void Build()
    {
        var col = Column(12);
        AddChild(col);
        col.AddChild(UiTheme.Label(_title, 26));
        var path = UiTheme.Label(_dir, UiTheme.SmallSize + 1, UiTheme.Dim);
        path.ClipText = true;
        col.AddChild(path);

        var places = new HBoxContainer();
        places.AddThemeConstantOverride("separation", 10);
        col.AddChild(places);
        var placeButtons = new List<Control>();
        foreach (var (label, dir) in Places())
        {
            var b = new Button { Text = label, CustomMinimumSize = new Vector2(0, 44), TooltipText = dir };
            b.Pressed += () => Navigate(dir);
            places.AddChild(b);
            placeButtons.Add(b);
        }

        var list = Column(6);
        col.AddChild(Scroller(list));
        var items = new List<Control>();
        if (_pickFolder)
        {
            var use = new ActionButton("✓  Use this folder", () => Pick(_dir), _dir);
            list.AddChild(use);
            items.Add(use);
        }
        var parent = Directory.GetParent(_dir)?.FullName;
        if (parent != null)
        {
            var up = new ActionButton("↑  ..", () => Navigate(parent), parent);
            list.AddChild(up);
            items.Add(up);
        }
        try
        {
            foreach (var d in Directory.EnumerateDirectories(_dir).Where(d => !Path.GetFileName(d).StartsWith('.')).Order(StringComparer.OrdinalIgnoreCase))
            {
                var target = d;
                var b = new ActionButton("▸  " + Path.GetFileName(d), () => Navigate(target), d);
                list.AddChild(b);
                items.Add(b);
            }
            if (!_pickFolder)
            {
                foreach (var f in Directory.EnumerateFiles(_dir).Where(Matches).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var target = f;
                    var b = new ActionButton("    " + Path.GetFileName(f), () => Pick(target), f);
                    list.AddChild(b);
                    items.Add(b);
                }
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            list.AddChild(UiTheme.Label("Can't open this folder: " + e.Message, color: UiTheme.Warn, wrap: true));
        }
        if (items.Count == 0)
        {
            list.AddChild(UiTheme.Label(_pickFolder ? "No folders here." : "No matching files here.", color: UiTheme.Dim));
        }

        _firstItem = items.FirstOrDefault();
        Focus.Row(placeButtons, below: _firstItem);
        Focus.Column(items, above: placeButtons.FirstOrDefault());
    }

    private bool Matches(string f) =>
        _extensions.Length == 0 || _extensions.Contains(Path.GetExtension(f).ToLowerInvariant());

    private static IEnumerable<(string Label, string Dir)> Places()
    {
        var home = System.Environment.GetEnvironmentVariable("HOME") ?? "/";
        yield return ("Home", home);
        foreach (var sub in new[] { "Downloads", "Games", "Emulation" })
        {
            var d = Path.Combine(home, sub);
            if (Directory.Exists(d))
            {
                yield return (sub, d);
            }
        }
        // SD cards and USB drives: /run/media/<user>/<label> (SteamOS, Bazzite), /media, /mnt.
        foreach (var root in new[] { "/run/media", "/media", "/mnt" })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            IEnumerable<string> mounts;
            try
            {
                // /run/media/<user>/<label> on older layouts, /run/media/<label> on newer ones.
                var user = System.Environment.UserName;
                mounts = Directory.EnumerateDirectories(root)
                    .SelectMany(d => Path.GetFileName(d) == user ? Directory.EnumerateDirectories(d) : [d])
                    .ToList();
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            foreach (var m in mounts.Take(6))
            {
                yield return (Path.GetFileName(m), m);
            }
        }
    }

    private void Navigate(string dir)
    {
        _dir = dir;
        Rebuild();
        FocusDefault();
    }

    private void Pick(string path)
    {
        App.Pop();
        _onPick(path);
    }

    public override void OnAction(string action)
    {
        if (action == Pad.Secondary && _pickFolder)
        {
            Pick(_dir);
        }
        else if (action == Pad.Tertiary && Directory.GetParent(_dir)?.FullName is { } parent)
        {
            Navigate(parent);
        }
    }

    public override void FocusDefault() => _firstItem?.GrabFocus();
}
