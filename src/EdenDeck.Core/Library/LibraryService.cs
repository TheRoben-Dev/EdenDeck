using EdenDeck.Core.Backends;

namespace EdenDeck.Core.Library;

/// <summary>Scans user folders with every registered backend and groups results per title.</summary>
public sealed class LibraryService
{
    private readonly IReadOnlyList<IEmulatorBackend> _backends;

    public LibraryService(IReadOnlyList<IEmulatorBackend> backends) => _backends = backends;

    /// <summary>Files skipped by the last scan because they were still being written (copy in progress).</summary>
    public int LastSkippedInProgress { get; private set; }

    /// <summary>Game files the last scan found under a name without the right extension (fixable by renaming).</summary>
    public IReadOnlyList<MisnamedFile> LastMisnamed { get; private set; } = [];

    /// <summary>Only files at least this big are sniffed for magic bytes (games are hundreds of MB and up).</summary>
    private const long MinSniffSize = 1 << 20;

    /// <summary>Files changed this recently are treated as still copying (SFTP, network shares, downloads).</summary>
    public TimeSpan InProgressWindow { get; init; } = TimeSpan.FromSeconds(20);

    private static readonly string[] PartialSuffixes = [".filepart", ".part", ".partial", ".tmp", ".crdownload", ".download", ".!sync", "~"];

    public IReadOnlyList<LibraryGame> Scan(IEnumerable<LibraryFolder> folders, CancellationToken ct = default)
    {
        var entries = new List<GameEntry>();
        // The same file can be reached twice: a folder added as both /home/x and /var/home/x
        // (Bazzite symlinks /home), nested library folders, or symlinked subfolders.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;
        var misnamed = new List<MisnamedFile>();
        var cutoff = DateTime.UtcNow - InProgressWindow;
        foreach (var folder in CanonicalFolders(folders))
        {
            var option = new EnumerationOptions
            {
                RecurseSubdirectories = folder.Recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                MaxRecursionDepth = 8,
            };
            foreach (var file in Directory.EnumerateFiles(folder.Path, "*", option))
            {
                ct.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (PartialSuffixes.Any(x => file.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }
                var backend = _backends.FirstOrDefault(b => b.FileExtensions.Contains(ext));
                if (backend == null)
                {
                    // Inside a PS3 game folder every big file is game data, never a misnamed dump.
                    if (file.Contains("/USRDIR/", StringComparison.Ordinal) || file.Contains("/PS3_GAME/", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (Sniff(file, cutoff) is { } fix && seen.Add(PathUtil.RealPath(file)))
                    {
                        misnamed.Add(fix);
                    }
                    continue;
                }
                if (!seen.Add(PathUtil.RealPath(file)))
                {
                    continue;
                }
                try
                {
                    if (File.GetLastWriteTimeUtc(file) > cutoff)
                    {
                        skipped++;
                        continue;
                    }
                }
                catch (IOException)
                {
                    continue;
                }
                var entry = SafeRead(backend, file);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
        }
        LastSkippedInProgress = skipped;
        LastMisnamed = misnamed;
        return Group(entries);
    }

    /// <summary>A big, settled file with no known extension: ask each backend what its bytes are.</summary>
    private MisnamedFile? Sniff(string file, DateTime cutoff)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Length < MinSniffSize || info.LastWriteTimeUtc > cutoff)
            {
                return null;
            }
            foreach (var b in _backends)
            {
                if (b.Library.DetectExtension(file) is { Length: > 1 } ext)
                {
                    return new MisnamedFile(file, file + ext, b.Id, ext);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>Renames a misnamed game file to its suggested name. Throws if that name is taken.</summary>
    public static string FixName(MisnamedFile f)
    {
        if (File.Exists(f.SuggestedPath) || Directory.Exists(f.SuggestedPath))
        {
            throw new IOException($"{Path.GetFileName(f.SuggestedPath)} already exists next to it.");
        }
        File.Move(f.Path, f.SuggestedPath);
        return f.SuggestedPath;
    }

    /// <summary>Resolves symlinks and drops folders that are missing, repeated or inside another recursive folder.</summary>
    internal static IReadOnlyList<LibraryFolder> CanonicalFolders(IEnumerable<LibraryFolder> folders)
    {
        var real = folders.Where(f => Directory.Exists(f.Path))
            .Select(f => f with { Path = PathUtil.RealPath(f.Path).TrimEnd('/') })
            .GroupBy(f => f.Path, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(f => f.Recursive).First())
            .ToList();
        return real.Where(f => !real.Any(o => o.Recursive && !ReferenceEquals(o, f) &&
                                              f.Path.StartsWith(o.Path + "/", StringComparison.Ordinal))).ToList();
    }

    private static GameEntry? SafeRead(IEmulatorBackend backend, string file)
    {
        try
        {
            return backend.Library.TryRead(file);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<LibraryGame> Group(IEnumerable<GameEntry> entries)
    {
        var list = entries.ToList();
        var games = new List<LibraryGame>();
        var bases = list.Where(e => e.Kind is ContentKind.Base or ContentKind.Homebrew or ContentKind.Unknown)
            // One tile per title: several copies of the same base game (e.g. an NSP and an XCI) collapse.
            .GroupBy(e => string.IsNullOrEmpty(e.TitleId) ? "path:" + e.Path : e.BackendId + ":" + e.TitleId)
            .Select(g => g.OrderBy(e => e.TitleIdSource == "nca-header" ? 0 : 1).ThenBy(e => e.Path, StringComparer.Ordinal).First());
        foreach (var b in bases)
        {
            var baseId = BaseTitleId(b.TitleId);
            var updates = list.Where(e => e.Kind == ContentKind.Update && e.BackendId == b.BackendId &&
                                          BaseTitleId(e.TitleId) == baseId && baseId.Length > 0)
                .DistinctBy(e => (e.TitleId, e.Version ?? Path.GetFileName(e.Path)))
                .OrderByDescending(e => VersionNumber(e.Version)).ToList();
            var dlc = list.Where(e => e.Kind == ContentKind.Dlc && e.BackendId == b.BackendId &&
                                      DlcBaseTitleId(e.TitleId) == baseId && baseId.Length > 0)
                .DistinctBy(e => e.TitleId).ToList();
            games.Add(new LibraryGame(b, updates, dlc));
        }
        return games.OrderBy(g => g.Base.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static long VersionNumber(string? v) => long.TryParse(v, out var n) ? n : -1;

    // Switch title id conventions: update = base | 0x800; DLC = (base + 0x1000) & ~0xFFF + n.
    // Non-Switch backends will provide their own grouping when they arrive.
    private static string BaseTitleId(string tid)
    {
        if (!TryParseTid(tid, out var v))
        {
            return "";
        }
        return (v & ~0xFFFUL).ToString("X16");
    }

    private static string DlcBaseTitleId(string tid)
    {
        if (!TryParseTid(tid, out var v))
        {
            return "";
        }
        return ((v & ~0xFFFUL) - 0x1000UL).ToString("X16");
    }

    private static bool TryParseTid(string tid, out ulong v) =>
        ulong.TryParse(tid, System.Globalization.NumberStyles.HexNumber, null, out v) && tid.Length == 16;
}

public sealed record LibraryFolder(string Path, bool Recursive = true);

/// <summary>A game file whose name lacks the extension Eden (and the scanner) need, e.g. "Metroid Dread" (an XCI).</summary>
public sealed record MisnamedFile(string Path, string SuggestedPath, string BackendId, string Extension)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string SuggestedName => System.IO.Path.GetFileName(SuggestedPath);
}
