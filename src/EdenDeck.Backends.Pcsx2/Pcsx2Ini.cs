namespace EdenDeck.Backends.Pcsx2;

/// <summary>
/// PCSX2's INI files (SimpleIni, as PCSX2 writes them: <c>Key = Value</c>, sections such as
/// <c>[EmuCore/GS]</c>, and keys that repeat for lists such as <c>[Cheats] Enable</c>). Lines
/// Scanline doesn't touch are kept as they are.
/// </summary>
public sealed class Pcsx2Ini
{
    private sealed class Line
    {
        public string Raw = "";
        public string? Section;  // set on [section] lines
        public string? Key;      // set on key = value lines
        public string Value = "";
        public string Owner = "";  // the section a key line is in
    }

    private readonly List<Line> _lines = [];

    public static Pcsx2Ini Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new Pcsx2Ini();

    public static Pcsx2Ini Parse(string text)
    {
        var ini = new Pcsx2Ini();
        var section = "";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1] == "")
        {
            lines.RemoveAt(lines.Count - 1);
        }
        foreach (var raw in lines)
        {
            var line = new Line { Raw = raw };
            var t = raw.Trim();
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                section = t[1..^1].Trim();
                line.Section = section;
            }
            else if (t.Length > 0 && t[0] is not ';' and not '#' && t.IndexOf('=') is > 0 and var eq)
            {
                line.Key = t[..eq].Trim();
                line.Value = t[(eq + 1)..].Trim();
                line.Owner = section;
            }
            ini._lines.Add(line);
        }
        return ini;
    }

    private IEnumerable<Line> Keys(string section, string key) =>
        _lines.Where(l => l.Key != null && l.Owner.Equals(section, StringComparison.OrdinalIgnoreCase) && l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public string? Get(string section, string key) => Keys(section, key).FirstOrDefault()?.Value;

    public IReadOnlyList<string> GetAll(string section, string key) => Keys(section, key).Select(l => l.Value).ToList();

    public bool IsEmpty => _lines.All(l => l.Key == null);

    public bool HasSection(string section) => _lines.Any(l => l.Section != null && l.Section.Equals(section, StringComparison.OrdinalIgnoreCase));

    public void Set(string section, string key, string value) => SetAll(section, key, [value]);

    /// <summary>Replaces every <paramref name="key"/> line in the section with one line per value.</summary>
    public void SetAll(string section, string key, IReadOnlyList<string> values)
    {
        var existing = Keys(section, key).ToList();
        var at = existing.Count > 0 ? _lines.IndexOf(existing[0]) : -1;
        foreach (var l in existing)
        {
            _lines.Remove(l);
        }
        if (values.Count == 0)
        {
            DropEmptySection(section);
            return;
        }
        if (at < 0)
        {
            var header = _lines.FindIndex(l => l.Section != null && l.Section.Equals(section, StringComparison.OrdinalIgnoreCase));
            if (header < 0)
            {
                if (_lines.Count > 0 && _lines[^1].Raw.Trim().Length > 0)
                {
                    _lines.Add(new Line());
                }
                _lines.Add(new Line { Raw = $"[{section}]", Section = section });
                at = _lines.Count;
            }
            else
            {
                // After the section's last key (before blank lines and the next section).
                at = header + 1;
                for (var i = header + 1; i < _lines.Count && _lines[i].Section == null; i++)
                {
                    if (_lines[i].Raw.Trim().Length > 0)
                    {
                        at = i + 1;
                    }
                }
            }
        }
        _lines.InsertRange(at, values.Select(v => new Line { Raw = $"{key} = {v}", Key = key, Value = v, Owner = section }));
    }

    public void Remove(string section, string key) => SetAll(section, key, []);

    private void DropEmptySection(string section)
    {
        var header = _lines.FindIndex(l => l.Section != null && l.Section.Equals(section, StringComparison.OrdinalIgnoreCase));
        if (header < 0)
        {
            return;
        }
        var end = header + 1;
        while (end < _lines.Count && _lines[end].Section == null)
        {
            if (_lines[end].Raw.Trim().Length > 0)
            {
                return;
            }
            end++;
        }
        _lines.RemoveRange(header, end - header);
    }

    public override string ToString() => string.Join('\n', _lines.Select(l => l.Raw)) + "\n";

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToString());
        File.Move(tmp, path, overwrite: true);
    }
}
