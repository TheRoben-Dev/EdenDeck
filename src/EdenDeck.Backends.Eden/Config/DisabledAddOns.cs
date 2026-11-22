using System.Globalization;

namespace EdenDeck.Backends.Eden.Config;

/// <summary>
/// Eden's [DisabledAddOns] array in the global config (docs/eden-integration.md §6):
/// <code>
/// size=N
/// 1\title_id=&lt;decimal u64&gt;
/// 1\disabled\size=M
/// 1\disabled\1\d=&lt;add-on name&gt;
/// </code>
/// </summary>
public static class DisabledAddOns
{
    public const string Section = "DisabledAddOns";

    public static Dictionary<ulong, List<string>> Read(QtIni ini)
    {
        var result = new Dictionary<ulong, List<string>>();
        var size = Int(ini.Get(Section, "size"));
        for (var i = 1; i <= size; i++)
        {
            if (!ulong.TryParse(ini.Get(Section, $"{i}\\title_id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tid))
            {
                continue;
            }
            var names = new List<string>();
            var dsize = Int(ini.Get(Section, $"{i}\\disabled\\size"));
            for (var j = 1; j <= dsize; j++)
            {
                var d = ini.Get(Section, $"{i}\\disabled\\{j}\\d");
                if (!string.IsNullOrEmpty(d))
                {
                    names.Add(d);
                }
            }
            result[tid] = names;
        }
        return result;
    }

    public static void Write(QtIni ini, IReadOnlyDictionary<ulong, List<string>> data)
    {
        ini.RemoveWhere(Section, _ => true);
        var i = 0;
        foreach (var (tid, names) in data)
        {
            i++;
            ini.Set(Section, $"{i}\\title_id", tid.ToString(CultureInfo.InvariantCulture));
            for (var j = 0; j < names.Count; j++)
            {
                ini.Set(Section, $"{i}\\disabled\\{j + 1}\\d", names[j]);
            }
            ini.Set(Section, $"{i}\\disabled\\size", names.Count.ToString(CultureInfo.InvariantCulture));
        }
        ini.Set(Section, "size", i.ToString(CultureInfo.InvariantCulture));
    }

    public static ulong ParseTitleId(string hex) => ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static int Int(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Max(0, v) : 0;
}
