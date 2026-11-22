using System.Globalization;
using EdenDeck.Core;
using EdenDeck.Core.Library;

namespace EdenDeck.Backends.Eden.Config;

public enum AddOnState
{
    /// <summary>Eden loads it (its folder is an external content dir and it isn't disabled).</summary>
    Active,
    /// <summary>Turned off in Eden's add-ons list for this game.</summary>
    Disabled,
    /// <summary>Found by Eden Deck but Eden won't see it (folder not in Eden's external content dirs).</summary>
    NotLoaded,
}

public sealed record AddOnStatus(string Label, AddOnState State);

/// <summary>
/// Updates and DLC without installing to NAND: Eden loads them straight from
/// [UI] Paths\external_content_dirs (scanned recursively; src/core/file_sys/registered_cache.cpp
/// ExternalContentProvider). Eden Deck adds the user's library folders there (append only) and
/// reports per game whether Eden will actually load what was found, including the user's own
/// [DisabledAddOns] choices ("Update", "Update@&lt;version&gt;", "DLC").
/// </summary>
public static class EdenAddOns
{
    private const string Section = "UI";
    private const string Array = "Paths\\external_content_dirs";

    public static IReadOnlyList<string> ExternalContentDirs(QtIni ini)
    {
        var size = int.TryParse(ini.Get(Section, Array + "\\size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        var list = new List<string>();
        for (var i = 1; i <= size; i++)
        {
            if (ini.Get(Section, $"{Array}\\{i}\\path") is { Length: > 0 } p)
            {
                list.Add(p);
            }
        }
        return list;
    }

    /// <summary>Appends any missing folders (never removes the user's own entries). True if the file changed.</summary>
    public static bool EnsureExternalContentDirs(string configFile, IEnumerable<string> folders)
    {
        var ini = QtIni.Load(configFile);
        var current = ExternalContentDirs(ini).ToList();
        var known = current.Select(Norm).ToHashSet(StringComparer.Ordinal);
        var added = false;
        foreach (var f in folders.Where(Directory.Exists))
        {
            if (known.Add(Norm(f)))
            {
                current.Add(f);
                added = true;
            }
        }
        if (!added)
        {
            return false;
        }
        ini.RemoveWhere(Section, k => k.StartsWith(Array + "\\", StringComparison.Ordinal));
        for (var i = 0; i < current.Count; i++)
        {
            ini.Set(Section, $"{Array}\\{i + 1}\\path", current[i]);
        }
        ini.Set(Section, Array + "\\size", current.Count.ToString(CultureInfo.InvariantCulture));
        ini.Save(configFile);
        return true;
    }

    /// <summary>What Eden will do with this game's update and DLC files.</summary>
    public static IReadOnlyList<AddOnStatus> Status(string configFile, LibraryGame game, IEnumerable<string> willAddFolders)
    {
        var result = new List<AddOnStatus>();
        if (game.Updates.Count == 0 && game.Dlc.Count == 0 || string.IsNullOrEmpty(game.Base.TitleId))
        {
            return result;
        }
        var ini = QtIni.Load(configFile);
        var dirs = ExternalContentDirs(ini).Concat(willAddFolders).Select(Norm).ToList();
        var disabled = DisabledAddOns.Read(ini).GetValueOrDefault(DisabledAddOns.ParseTitleId(game.Base.TitleId)) ?? [];
        bool Visible(GameEntry e) => dirs.Any(d => Norm(Path.GetDirectoryName(e.Path)!).StartsWith(d, StringComparison.Ordinal) ||
                                                   Norm(Path.GetDirectoryName(e.Path)! + "/") == d);

        if (game.Updates.Count > 0)
        {
            var u = game.Updates[0];  // newest
            var version = uint.TryParse(u.Version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (uint?)null;
            var label = version is { } ver ? $"Update {ver >> 16} (v{ver})" : "Update";
            var off = disabled.Contains("Update") || version is { } vv && disabled.Contains($"Update@{vv}");
            result.Add(new AddOnStatus(label, off ? AddOnState.Disabled : Visible(u) ? AddOnState.Active : AddOnState.NotLoaded));
        }
        if (game.Dlc.Count > 0)
        {
            var label = game.Dlc.Count == 1 ? "1 DLC" : $"{game.Dlc.Count} DLC";
            var off = disabled.Contains("DLC");
            result.Add(new AddOnStatus(label, off ? AddOnState.Disabled : game.Dlc.All(Visible) ? AddOnState.Active : AddOnState.NotLoaded));
        }
        return result;
    }

    private static string Norm(string p) => PathUtil.RealPath(p).TrimEnd('/') + "/";
}
