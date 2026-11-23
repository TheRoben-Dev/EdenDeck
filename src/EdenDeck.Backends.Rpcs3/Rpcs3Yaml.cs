using System.Text;

namespace EdenDeck.Backends.Rpcs3;

/// <summary>
/// Edits RPCS3's YAML config files (config.yml and custom_configs/config_&lt;TITLEID&gt;.yml) in
/// place: nested maps indented by two spaces with plain scalar values, exactly how RPCS3 writes
/// them. Keys are paths like <c>Video/Resolution Scale</c> or <c>Video/Vulkan/Adapter</c>. Lines
/// Scanline doesn't touch are kept as they are, so RPCS3's own settings survive.
/// </summary>
public sealed class Rpcs3Yaml
{
    private readonly List<string> _lines;

    private Rpcs3Yaml(List<string> lines) => _lines = lines;

    public static Rpcs3Yaml Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1] == "")
        {
            lines.RemoveAt(lines.Count - 1);
        }
        return new Rpcs3Yaml(lines);
    }

    public static Rpcs3Yaml Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new Rpcs3Yaml([]);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToString());
        File.Move(tmp, path, overwrite: true);
    }

    public override string ToString() => _lines.Count == 0 ? "" : string.Join('\n', _lines) + "\n";

    public bool IsEmpty => _lines.All(l => string.IsNullOrWhiteSpace(l) || l.TrimStart().StartsWith('#'));

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private static (string Key, string Value)? Split(string line)
    {
        var t = line.Trim();
        if (t.Length == 0 || t.StartsWith('#') || t.StartsWith('-'))
        {
            return null;
        }
        // Keys never contain ": "; values may.
        var i = t.IndexOf(": ", StringComparison.Ordinal);
        if (i < 0)
        {
            return t.EndsWith(':') ? (t[..^1], "") : null;
        }
        return (t[..i], t[(i + 2)..]);
    }

    /// <summary>Index of the line holding <paramref name="path"/>'s last key, or -1.</summary>
    private int Find(IReadOnlyList<string> path, out int parentEnd, out int depthFound)
    {
        int start = 0, end = _lines.Count, indent = 0;
        parentEnd = end;
        depthFound = 0;
        for (var d = 0; d < path.Count; d++)
        {
            var hit = -1;
            for (var i = start; i < end; i++)
            {
                if (string.IsNullOrWhiteSpace(_lines[i]) || Indent(_lines[i]) != indent)
                {
                    continue;
                }
                if (Split(_lines[i]) is { } kv && kv.Key == path[d])
                {
                    hit = i;
                    break;
                }
            }
            if (hit < 0)
            {
                parentEnd = end;
                return -1;
            }
            depthFound = d + 1;
            if (d == path.Count - 1)
            {
                return hit;
            }
            // The child block: following lines indented deeper than this key.
            var blockEnd = hit + 1;
            while (blockEnd < end && (string.IsNullOrWhiteSpace(_lines[blockEnd]) || Indent(_lines[blockEnd]) > indent))
            {
                blockEnd++;
            }
            start = hit + 1;
            end = blockEnd;
            parentEnd = blockEnd;
            indent += 2;
        }
        return -1;
    }

    private static string[] Keys(string key) => key.Split('/');

    /// <summary>A top-level key taken whole (vfs.yml keys look like paths: "/dev_hdd0/").</summary>
    public string? GetRoot(string key)
    {
        foreach (var line in _lines)
        {
            if (Indent(line) == 0 && Split(line) is { } kv && kv.Key == key)
            {
                return kv.Value.Length == 0 ? null : Unquote(kv.Value);
            }
        }
        return null;
    }

    public string? Get(string key)
    {
        var i = Find(Keys(key), out _, out _);
        if (i < 0 || Split(_lines[i]) is not { } kv || kv.Value.Length == 0)
        {
            return null;
        }
        return Unquote(kv.Value);
    }

    public void Set(string key, string value)
    {
        var path = Keys(key);
        var i = Find(path, out var parentEnd, out var depth);
        if (i >= 0)
        {
            _lines[i] = new string(' ', (path.Length - 1) * 2) + path[^1] + ": " + Quote(value);
            return;
        }
        // Add the missing sections and the key at the end of the deepest existing block.
        var insert = depth == 0 ? _lines.Count : parentEnd;
        while (insert > 0 && insert <= _lines.Count && string.IsNullOrWhiteSpace(_lines[insert - 1]) && depth > 0)
        {
            insert--;
        }
        var add = new List<string>();
        for (var d = depth; d < path.Length - 1; d++)
        {
            add.Add(new string(' ', d * 2) + path[d] + ":");
        }
        add.Add(new string(' ', (path.Length - 1) * 2) + path[^1] + ": " + Quote(value));
        _lines.InsertRange(insert, add);
    }

    public void Remove(string key)
    {
        var path = Keys(key);
        var i = Find(path, out _, out _);
        if (i < 0)
        {
            return;
        }
        _lines.RemoveAt(i);
        // Drop sections left empty.
        for (var d = path.Length - 1; d > 0; d--)
        {
            var parent = string.Join('/', path.Take(d));
            var p = Find(Keys(parent), out _, out _);
            if (p < 0)
            {
                break;
            }
            var next = p + 1;
            while (next < _lines.Count && string.IsNullOrWhiteSpace(_lines[next]))
            {
                next++;
            }
            if (next < _lines.Count && Indent(_lines[next]) > Indent(_lines[p]))
            {
                break;
            }
            _lines.RemoveAt(p);
        }
    }

    private static string Unquote(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && (v[0] == '"' && v[^1] == '"' || v[0] == '\'' && v[^1] == '\''))
        {
            return v[1..^1].Replace("\\\"", "\"");
        }
        return v;
    }

    private static string Quote(string v)
    {
        if (v.Length == 0)
        {
            return "\"\"";
        }
        var plain = !v.Contains(": ", StringComparison.Ordinal) && !v.Contains(" #", StringComparison.Ordinal) &&
                    "\"'{[&*!|>%@`#".IndexOf(v[0]) < 0 && v.Trim() == v && !v.EndsWith(':');
        if (plain)
        {
            return v;
        }
        var sb = new StringBuilder("\"");
        foreach (var c in v)
        {
            sb.Append(c is '"' or '\\' ? "\\" + c : c.ToString());
        }
        return sb.Append('"').ToString();
    }
}
