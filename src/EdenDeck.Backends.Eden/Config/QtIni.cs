using System.Text;

namespace EdenDeck.Backends.Eden.Config;

/// <summary>
/// Minimal, order-preserving INI document compatible with what Eden writes (SimpleIni with
/// SetSpaces(false), QSettings-style keys such as <c>resolution_setup\default</c>).
/// Lines and keys we don't touch are kept byte-for-byte, so unknown keys survive.
/// </summary>
public sealed class QtIni
{
    // Eden's Config::special_characters: values containing any of these are double-quoted.
    private const string SpecialCharacters = "!#$%^&*|;'\",<>?`~=";

    private sealed class Line
    {
        public string Raw = "";
        public string? Section;   // set for [section] lines
        public string? Key;       // set for key=value lines
        public string? Value;
    }

    private readonly List<Line> _lines = new();

    public static QtIni Parse(string text)
    {
        var ini = new QtIni();
        string? section = null;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = new Line { Raw = raw };
            var t = raw.Trim();
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                section = t[1..^1];
                line.Section = section;
            }
            else if (section != null && t.Length > 0 && t[0] != ';' && t[0] != '#')
            {
                var eq = raw.IndexOf('=');
                if (eq > 0)
                {
                    line.Key = raw[..eq].Trim();
                    line.Value = raw[(eq + 1)..].Trim();
                    line.Section = section;
                }
            }
            ini._lines.Add(line);
        }
        // Drop the artificial empty line produced by a trailing newline.
        if (ini._lines.Count > 0 && ini._lines[^1].Raw.Length == 0 && ini._lines[^1].Key == null &&
            ini._lines[^1].Section == null)
        {
            ini._lines.RemoveAt(ini._lines.Count - 1);
        }
        return ini;
    }

    public static QtIni Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new QtIni();

    public IEnumerable<string> Sections => _lines.Where(l => l.Key == null && l.Section != null).Select(l => l.Section!);

    public bool HasSection(string section) => Sections.Contains(section);

    /// <summary>Raw value with QSettings quoting removed, or null if absent.</summary>
    public string? Get(string section, string key)
    {
        var l = Find(section, key);
        return l?.Value is { } v ? Unquote(v) : null;
    }

    public IEnumerable<KeyValuePair<string, string>> Entries(string section) =>
        _lines.Where(l => l.Key != null && l.Section == section).Select(l => new KeyValuePair<string, string>(l.Key!, Unquote(l.Value!)));

    public void Set(string section, string key, string value)
    {
        var encoded = Quote(value);
        var existing = Find(section, key);
        if (existing != null)
        {
            existing.Value = encoded;
            existing.Raw = $"{key}={encoded}";
            return;
        }
        var newLine = new Line { Raw = $"{key}={encoded}", Key = key, Value = encoded, Section = section };
        var headerIndex = _lines.FindIndex(l => l.Key == null && l.Section == section);
        if (headerIndex < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Raw.Trim().Length > 0)
            {
                _lines.Add(new Line { Raw = "" });
            }
            _lines.Add(new Line { Raw = $"[{section}]", Section = section });
            _lines.Add(newLine);
            return;
        }
        // Insert after the last key of this section (before trailing blank lines).
        var insertAt = headerIndex + 1;
        for (var i = headerIndex + 1; i < _lines.Count; i++)
        {
            if (_lines[i].Key == null && _lines[i].Section != null)
            {
                break;  // next section header
            }
            if (_lines[i].Key != null)
            {
                insertAt = i + 1;
            }
        }
        _lines.Insert(insertAt, newLine);
    }

    public bool Remove(string section, string key)
    {
        var l = Find(section, key);
        return l != null && _lines.Remove(l);
    }

    /// <summary>Removes every key in the section whose name starts with <paramref name="prefix"/>.</summary>
    public int RemoveWhere(string section, Func<string, bool> predicate) =>
        _lines.RemoveAll(l => l.Key != null && l.Section == section && predicate(l.Key));

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var l in _lines)
        {
            sb.Append(l.Raw).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Atomic write (temp file + rename) so a crash never leaves a half-written config.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".edendeck.tmp";
        File.WriteAllText(tmp, ToString(), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private Line? Find(string section, string key) =>
        _lines.FirstOrDefault(l => l.Key != null && l.Section == section && l.Key == key);

    internal static string Unquote(string v) => v.Replace("\"", "");

    internal static string Quote(string v) =>
        v.IndexOfAny(SpecialCharacters.ToCharArray()) >= 0 ? $"\"{v.Replace("\"", "")}\"" : v;
}
