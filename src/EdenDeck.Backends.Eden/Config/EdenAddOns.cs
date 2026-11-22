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
    /// <summary>Found by Eden Deck but not loaded (turned off for this game in Eden Deck).</summary>
    NotLoaded,
}

public sealed record AddOnStatus(string Label, AddOnState State);

/// <summary>
/// Updates and DLC without installing to NAND: Eden loads them straight from
/// [UI] Paths\external_content_dirs (scanned recursively at startup; src/core/file_sys/
/// registered_cache.cpp ExternalContentProvider). Eden Deck stages links to the launched game's
/// files in one folder it lists there, and reports per game whether Eden will actually load
/// them, including the user's own [DisabledAddOns] choices ("Update", "Update@&lt;version&gt;", "DLC").
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

    private static void WriteDirs(QtIni ini, IReadOnlyList<string> dirs)
    {
        ini.RemoveWhere(Section, k => k.StartsWith(Array + "\\", StringComparison.Ordinal));
        for (var i = 0; i < dirs.Count; i++)
        {
            ini.Set(Section, $"{Array}\\{i + 1}\\path", dirs[i]);
        }
        ini.Set(Section, Array + "\\size", dirs.Count.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Before a launch: Eden scans every external content dir recursively and opens each NSP/XCI
    /// in it at startup, so pointing it at whole library folders (what 0.2.3 did) made Eden slow
    /// or crash with large libraries. Instead Eden Deck keeps one small staging folder holding
    /// links to just this game's newest update and its DLC, and lists only that folder.
    /// Entries equal to the library folders (added by 0.2.3) are removed; the user's own entries
    /// stay. Returns true if Eden's config changed.
    /// </summary>
    public static bool PrepareLaunch(string configFile, string stagingDir, LibraryGame game, bool enabled, IEnumerable<string> libraryFolders)
    {
        Directory.CreateDirectory(stagingDir);
        foreach (var f in Directory.EnumerateFileSystemEntries(stagingDir))
        {
            File.Delete(f);  // only links we made; never follows them
        }
        if (enabled)
        {
            foreach (var e in game.Updates.Take(1).Concat(game.Dlc))
            {
                var link = Path.Combine(stagingDir, Path.GetFileName(e.Path));
                if (!File.Exists(link) && File.Exists(e.Path))
                {
                    File.CreateSymbolicLink(link, e.Path);
                }
            }
        }

        var ini = QtIni.Load(configFile);
        var current = ExternalContentDirs(ini).ToList();
        // Library folders (0.2.3 added them) and the home folder are never scan targets.
        var library = libraryFolders.Select(Norm).ToHashSet(StringComparer.Ordinal);
        if (Environment.GetEnvironmentVariable("HOME") is { Length: > 1 } home)
        {
            library.Add(Norm(home));
        }
        var kept = current.Where(d => !library.Contains(Norm(d))).ToList();
        if (!kept.Any(d => Norm(d) == Norm(stagingDir)))
        {
            kept.Add(stagingDir);
        }
        if (kept.SequenceEqual(current))
        {
            return false;
        }
        WriteDirs(ini, kept);
        ini.Save(configFile);
        return true;
    }

    /// <summary>What Eden will do with this game's update and DLC files.</summary>
    public static IReadOnlyList<AddOnStatus> Status(string configFile, LibraryGame game, bool enabled, string stagingDir)
    {
        var result = new List<AddOnStatus>();
        if (game.Updates.Count == 0 && game.Dlc.Count == 0 || string.IsNullOrEmpty(game.Base.TitleId))
        {
            return result;
        }
        var ini = QtIni.Load(configFile);
        var userDirs = ExternalContentDirs(ini).Select(Norm).Where(d => d != Norm(stagingDir)).ToList();
        var disabled = DisabledAddOns.Read(ini).GetValueOrDefault(DisabledAddOns.ParseTitleId(game.Base.TitleId)) ?? [];
        // Loaded if Eden Deck stages it for this launch, or it already sits in one of the user's own content dirs.
        bool Visible(GameEntry e) => enabled || userDirs.Any(d => (Norm(Path.GetDirectoryName(e.Path)!)).StartsWith(d, StringComparison.Ordinal));

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
