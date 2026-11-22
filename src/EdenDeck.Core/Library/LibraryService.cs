using EdenDeck.Core.Backends;

namespace EdenDeck.Core.Library;

/// <summary>Scans user folders with every registered backend and groups results per title.</summary>
public sealed class LibraryService
{
    private readonly IReadOnlyList<IEmulatorBackend> _backends;

    public LibraryService(IReadOnlyList<IEmulatorBackend> backends) => _backends = backends;

    public IReadOnlyList<LibraryGame> Scan(IEnumerable<LibraryFolder> folders, CancellationToken ct = default)
    {
        var entries = new List<GameEntry>();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder.Path))
            {
                continue;
            }
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
                foreach (var backend in _backends)
                {
                    if (!backend.FileExtensions.Contains(ext))
                    {
                        continue;
                    }
                    var entry = SafeRead(backend, file);
                    if (entry != null)
                    {
                        entries.Add(entry);
                        break;
                    }
                }
            }
        }
        return Group(entries);
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
        foreach (var b in list.Where(e => e.Kind is ContentKind.Base or ContentKind.Homebrew or ContentKind.Unknown))
        {
            var baseId = BaseTitleId(b.TitleId);
            var updates = list.Where(e => e.Kind == ContentKind.Update && e.BackendId == b.BackendId &&
                                          BaseTitleId(e.TitleId) == baseId && baseId.Length > 0).ToList();
            var dlc = list.Where(e => e.Kind == ContentKind.Dlc && e.BackendId == b.BackendId &&
                                      DlcBaseTitleId(e.TitleId) == baseId && baseId.Length > 0).ToList();
            games.Add(new LibraryGame(b, updates, dlc));
        }
        return games.OrderBy(g => g.Base.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

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
