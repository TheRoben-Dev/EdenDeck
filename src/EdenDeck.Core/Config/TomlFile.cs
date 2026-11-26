using System.Globalization;
using System.Text;

namespace EdenDeck.Core.Config;

/// <summary>
/// A TOML config as xemu and melonDS write it: tables (<c>[display.window]</c>) of <c>key = value</c> lines, with
/// strings in quotes. Keys are read and written by their dotted path ("display.window.vsync").
/// Lines Scanline doesn't touch, arrays of tables (<c>[[input.gamepad_mappings]]</c>) included, are
/// kept as they are.
/// </summary>
public sealed class TomlFile
{
    private sealed class Line
    {
        public string Raw = "";
        public string? Table;  // set on [table] lines
        public string? Key;    // set on key = value lines
        public string Owner = "";
        public bool InArray;   // inside an [[array]] table: never matched
    }

    private readonly List<Line> _lines = [];

    public static TomlFile Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new TomlFile();

    public static TomlFile Parse(string text)
    {
        var toml = new TomlFile();
        var table = "";
        var inArray = false;
        foreach (var raw in text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
        {
            var line = new Line { Raw = raw };
            var t = raw.Trim();
            if (t.StartsWith("[[", StringComparison.Ordinal))
            {
                inArray = true;
                table = t.Trim('[', ']').Trim();
            }
            else if (t.StartsWith('[') && t.EndsWith(']'))
            {
                inArray = false;
                table = t[1..^1].Trim();
                line.Table = table;
            }
            else if (t.Length > 0 && t[0] != '#' && t.IndexOf('=') is > 0 and var eq)
            {
                line.Key = t[..eq].Trim();
                line.Owner = table;
                line.InArray = inArray;
            }
            toml._lines.Add(line);
        }
        if (toml._lines.Count == 1 && toml._lines[0].Raw.Length == 0)
        {
            toml._lines.Clear();
        }
        return toml;
    }

    private static (string Table, string Key) Split(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot < 0 ? ("", path) : (path[..dot], path[(dot + 1)..]);
    }

    private Line? Find(string path)
    {
        var (table, key) = Split(path);
        return _lines.FirstOrDefault(l => l.Key == key && l.Owner == table && !l.InArray);
    }

    /// <summary>The value as text (a string without its quotes), or null when it isn't set.</summary>
    public string? Get(string path)
    {
        if (Find(path) is not { } line)
        {
            return null;
        }
        var v = line.Raw[(line.Raw.IndexOf('=') + 1)..].Trim();
        if (v.StartsWith('"') && v.LastIndexOf('"') > 0)
        {
            return Unescape(v[1..v.LastIndexOf('"')]);
        }
        if (v.StartsWith('\'') && v.LastIndexOf('\'') > 0)
        {
            return v[1..v.LastIndexOf('\'')];
        }
        var hash = v.IndexOf('#');
        return (hash >= 0 ? v[..hash] : v).Trim();
    }

    public bool GetBool(string path, bool fallback) =>
        Get(path) is { } v ? v.Equals("true", StringComparison.OrdinalIgnoreCase) : fallback;

    /// <summary>Sets a string value (quoted).</summary>
    public void SetString(string path, string value) => SetRaw(path, "\"" + Escape(value) + "\"");

    public void SetBool(string path, bool value) => SetRaw(path, value ? "true" : "false");

    public void SetInt(string path, long value) => SetRaw(path, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Sets a value written as given (a number, true/false, or an already quoted string).</summary>
    public void SetRaw(string path, string literal)
    {
        var (table, key) = Split(path);
        var text = $"{key} = {literal}";
        if (Find(path) is { } line)
        {
            line.Raw = text;
            return;
        }
        var add = new Line { Raw = text, Key = key, Owner = table };
        var header = _lines.FindIndex(l => l.Table == table);
        if (header < 0 && table.Length > 0)
        {
            if (_lines.Count > 0 && _lines[^1].Raw.Trim().Length > 0)
            {
                _lines.Add(new Line());
            }
            _lines.Add(new Line { Raw = $"[{table}]", Table = table });
            _lines.Add(add);
            return;
        }
        // After the table's last key line.
        var at = header + 1;
        for (var i = at; i < _lines.Count && _lines[i].Table == null && !_lines[i].Raw.TrimStart().StartsWith("[[", StringComparison.Ordinal); i++)
        {
            if (_lines[i].Key != null)
            {
                at = i + 1;
            }
        }
        _lines.Insert(at, add);
    }

    public void Remove(string path)
    {
        if (Find(path) is { } line)
        {
            _lines.Remove(line);
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var l in _lines)
        {
            sb.Append(l.Raw).Append('\n');
        }
        return sb.ToString();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToString());
        File.Move(tmp, path, overwrite: true);
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Unescape(string s)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }
}
