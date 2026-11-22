using System.Globalization;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Eden.Config;

public sealed class EdenRunningException : InvalidOperationException
{
    public EdenRunningException()
        : base("Eden is running. Its config can only be changed while it is closed, because Eden rewrites it on exit.") { }
}

/// <summary>
/// Reads/writes Eden's global (qt-config.ini) and per-game (custom/&lt;TITLEID&gt;.ini) settings in
/// Eden's own format (docs/eden-integration.md §4-5), translating enum names to the installed
/// version's indices.
/// </summary>
public sealed class EdenSettings : ISettingsProvider
{
    private readonly Func<EdenPaths> _paths;
    private readonly Func<EdenSchema> _schema;
    private readonly Func<bool> _edenRunning;

    public EdenSettings(Func<EdenPaths> paths, Func<EdenSchema> schema, Func<bool>? edenRunning = null)
    {
        _paths = paths;
        _schema = schema;
        _edenRunning = edenRunning ?? EdenProcess.IsRunning;
    }

    public IReadOnlyList<OptionDefinition> Options
    {
        get
        {
            var schema = _schema();
            var list = new List<OptionDefinition>();
            foreach (var o in EdenOptions.All)
            {
                var s = schema.Find(o.Id);
                if (s == null)
                {
                    continue;  // not in this Eden version
                }
                var opt = o with { PerGame = s.PerGame };
                if (o.Type == OptionType.Choice && schema.IsEnum(s))
                {
                    opt = opt with { Choices = o.Choices.Where(c => schema.EnumIndex(s.BaseType, c.Value) != null).ToList() };
                }
                list.Add(opt);
            }
            return list;
        }
    }

    public IReadOnlyDictionary<string, string> ReadGlobal()
    {
        var ini = QtIni.Load(_paths().GlobalConfigFile);
        var result = new Dictionary<string, string>();
        foreach (var o in Options)
        {
            var s = _schema().Find(o.Id)!;
            if (ini.Get(s.Section, s.Key + "\\default") is "false" && ini.Get(s.Section, s.Key) is { } raw &&
                Decode(s, raw) is { } v)
            {
                result[o.Id] = v;
            }
        }
        return result;
    }

    public void WriteGlobal(IReadOnlyDictionary<string, string?> values)
    {
        EnsureNotRunning();
        var path = _paths().GlobalConfigFile;
        var ini = QtIni.Load(path);
        foreach (var (id, value) in values)
        {
            var s = Resolve(id);
            if (value == null)
            {
                ini.Set(s.Section, s.Key + "\\default", "true");
            }
            else
            {
                ini.Set(s.Section, s.Key + "\\default", "false");
                ini.Set(s.Section, s.Key, Encode(s, value));
            }
        }
        ini.Save(path);
    }

    public IReadOnlyDictionary<string, string> ReadPerGame(string titleId)
    {
        var ini = QtIni.Load(_paths().PerGameConfigFile(titleId));
        var result = new Dictionary<string, string>();
        foreach (var o in Options.Where(o => o.PerGame))
        {
            var s = _schema().Find(o.Id)!;
            if (ini.Get(s.Section, s.Key + "\\use_global") is not "false")
            {
                continue;
            }
            // use_global=false + default=true means "forced to Eden's default".
            if (ini.Get(s.Section, s.Key + "\\default") is "true")
            {
                result[o.Id] = o.DefaultValue ?? "";
                continue;
            }
            if (ini.Get(s.Section, s.Key) is { } raw && Decode(s, raw) is { } v)
            {
                result[o.Id] = v;
            }
        }
        return result;
    }

    public void WritePerGame(string titleId, IReadOnlyDictionary<string, string?> values)
    {
        EnsureNotRunning();
        var path = _paths().PerGameConfigFile(titleId);
        var ini = QtIni.Load(path);
        foreach (var (id, value) in values)
        {
            var s = Resolve(id);
            if (!s.PerGame)
            {
                throw new ArgumentException($"'{id}' can't be set per game in this Eden version");
            }
            if (value == null)
            {
                ini.Set(s.Section, s.Key + "\\use_global", "true");
                ini.Remove(s.Section, s.Key + "\\default");
                ini.Remove(s.Section, s.Key);
            }
            else
            {
                ini.Set(s.Section, s.Key + "\\use_global", "false");
                ini.Set(s.Section, s.Key + "\\default", "false");
                ini.Set(s.Section, s.Key, Encode(s, value));
            }
        }
        ini.Save(path);
    }

    /// <summary>Writes raw Eden keys (not limited to the curated list), e.g. frame gen settings.</summary>
    public void WritePerGameRaw(string titleId, IReadOnlyDictionary<string, string?> values) => WritePerGame(titleId, values);

    private SchemaSetting Resolve(string key) =>
        _schema().Find(key) ?? throw new KeyNotFoundException($"Eden {_schema().Label} has no setting '{key}'");

    private void EnsureNotRunning()
    {
        if (_edenRunning())
        {
            throw new EdenRunningException();
        }
    }

    internal string Encode(SchemaSetting s, string value)
    {
        var schema = _schema();
        if (schema.IsEnum(s))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                throw new ArgumentException($"enum setting '{s.Key}' must be written by name, got '{value}'");
            }
            return schema.EnumIndex(s.BaseType, value)?.ToString(CultureInfo.InvariantCulture)
                   ?? throw new ArgumentException($"'{value}' is not a valid {s.BaseType} in Eden {schema.Label}");
        }
        if (s.BaseType == "bool")
        {
            return value is "true" or "false" ? value : throw new ArgumentException($"'{s.Key}' expects true/false");
        }
        return value;
    }

    internal string? Decode(SchemaSetting s, string raw)
    {
        var schema = _schema();
        if (schema.IsEnum(s))
        {
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? schema.EnumName(s.BaseType, i) : null;
        }
        return raw;
    }
}
