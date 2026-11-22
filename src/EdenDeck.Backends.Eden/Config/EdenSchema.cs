using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdenDeck.Backends.Eden.Config;

public sealed record SchemaSetting
{
    [JsonPropertyName("key")] public required string Key { get; init; }
    [JsonPropertyName("section")] public required string Section { get; init; }
    [JsonPropertyName("category")] public string Category { get; init; } = "";
    [JsonPropertyName("per_game")] public bool PerGame { get; init; }
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("default_expr")] public string DefaultExpr { get; init; } = "";

    /// <summary>The C++ type name (first template argument), e.g. "GpuAccuracy", "bool", "u32".</summary>
    public string BaseType => Type.Split(',')[0].Trim();
}

/// <summary>
/// One Eden version's settings schema, generated from Eden source by
/// tools/extract_eden_settings.py and embedded from eden_adapter/schema/.
/// </summary>
public sealed class EdenSchema
{
    [JsonPropertyName("eden_label")] public string Label { get; init; } = "";
    [JsonPropertyName("eden_commit")] public string? Commit { get; init; }
    [JsonPropertyName("settings")] public List<SchemaSetting> Settings { get; init; } = new();
    [JsonPropertyName("enums")] public Dictionary<string, List<string>> Enums { get; init; } = new();

    public string Channel => Label.StartsWith("nightly", StringComparison.OrdinalIgnoreCase) ? "nightly" : "stable";

    public SchemaSetting? Find(string key) => Settings.FirstOrDefault(s => s.Key == key);

    public bool IsEnum(SchemaSetting s) => Enums.ContainsKey(s.BaseType);

    /// <summary>Enum value name -> index written to the INI, for this version. Null if unknown here.</summary>
    public int? EnumIndex(string enumType, string name)
    {
        if (!Enums.TryGetValue(enumType, out var values))
        {
            return null;
        }
        var i = values.FindIndex(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
        return i < 0 || name == "MaxEnum" ? null : i;
    }

    public string? EnumName(string enumType, int index) =>
        Enums.TryGetValue(enumType, out var values) && index >= 0 && index < values.Count && values[index] != "MaxEnum"
            ? values[index]
            : null;

    // ---- loading ----

    private static readonly Lazy<IReadOnlyList<EdenSchema>> _all = new(LoadEmbedded);

    public static IReadOnlyList<EdenSchema> All => _all.Value;

    public static EdenSchema Parse(string json) =>
        JsonSerializer.Deserialize<EdenSchema>(json) ?? throw new InvalidDataException("empty schema");

    private static IReadOnlyList<EdenSchema> LoadEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        var list = new List<EdenSchema>();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("schema/") && n.EndsWith(".json")))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            list.Add(Parse(r.ReadToEnd()));
        }
        return list;
    }

    /// <summary>
    /// Picks the snapshot for an installed Eden. Exact label/tag match first, then the newest
    /// snapshot of the same channel (the adapter then only writes keys that snapshot knows).
    /// </summary>
    public static EdenSchema Select(string channel, string? version)
    {
        var all = All;
        if (version != null)
        {
            var exact = all.FirstOrDefault(s => s.Label.Equals(version, StringComparison.OrdinalIgnoreCase) ||
                                                 s.Label.EndsWith(version.TrimStart('v'), StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }
        }
        var sameChannel = all.Where(s => s.Channel == channel).ToList();
        var pool = sameChannel.Count > 0 ? sameChannel : all.ToList();
        // Labels: "v0.2.1" / "nightly-<sha>": newest = last when ordered by label for stable;
        // for nightly there is one snapshot per refresh, keep file order (newest generated last).
        return channel == "stable"
            ? pool.OrderBy(s => ParseVersion(s.Label)).Last()
            : pool.Last();
    }

    private static Version ParseVersion(string label) =>
        System.Version.TryParse(label.TrimStart('v').Split('-')[0], out var v) ? v : new Version(0, 0);
}
