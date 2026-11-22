using System.Globalization;
using System.Text.RegularExpressions;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Library;

namespace EdenDeck.Backends.Eden.Library;

/// <summary>
/// Identifies Switch game files. With the user's prod.keys (header_key) the title id comes
/// from the decrypted NCA header of the meta/program NCA. Without keys, or on failure, it
/// falls back to the "[0100XXXXXXXXXXXX]" filename convention.
///
/// Phase 2 limits: the display name comes from the filename and there is no icon yet;
/// reading the control NACP needs full NCA section decryption (planned).
/// </summary>
public sealed partial class EdenLibraryScanner : ILibraryScanner
{
    public static readonly IReadOnlyCollection<string> Extensions = [".nsp", ".nsz", ".xci", ".xcz", ".nro"];

    private readonly Func<byte[]?> _headerKey;

    public EdenLibraryScanner(Func<byte[]?> headerKey) => _headerKey = headerKey;

    public GameEntry? TryRead(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!Extensions.Contains(ext))
        {
            return null;
        }
        var fileName = Path.GetFileNameWithoutExtension(path);
        var title = CleanTitle(fileName);
        var version = VersionRegex().Match(fileName) is { Success: true } vm ? vm.Groups[1].Value : null;

        if (ext == ".nro")
        {
            return new GameEntry
            {
                BackendId = EdenBackend.BackendId, TitleId = "", Title = title, Path = path,
                Kind = ContentKind.Homebrew, TitleIdSource = "none",
            };
        }

        string? tid = null;
        var source = "unknown";
        var key = _headerKey();
        if (key != null)
        {
            try
            {
                tid = ReadTitleIdFromHeaders(path, ext, key);
                if (tid != null)
                {
                    source = "nca-header";
                }
            }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException or ArgumentException)
            {
                tid = null;
            }
        }
        if (tid == null && TitleIdRegex().Match(fileName) is { Success: true } m)
        {
            tid = m.Groups[1].Value.ToUpperInvariant();
            source = "filename";
        }
        if (tid == null)
        {
            return new GameEntry
            {
                BackendId = EdenBackend.BackendId, TitleId = "", Title = title, Path = path,
                Kind = ContentKind.Unknown, Version = version, TitleIdSource = "none",
            };
        }
        return new GameEntry
        {
            BackendId = EdenBackend.BackendId,
            TitleId = tid,
            Title = title,
            Path = path,
            Kind = KindFromTitleId(ulong.Parse(tid, NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
            Version = version,
            TitleIdSource = source,
        };
    }

    /// <summary>NSP/NSZ: "PFS0" at 0. XCI/XCZ: "HEAD" at 0x100. Compressed variants hold .ncz entries.</summary>
    public string? DetectExtension(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        var buf = new byte[0x104];
        if (fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false) < buf.Length)
        {
            return null;
        }
        bool Compressed(Func<IReadOnlyList<ContainerEntry>> read)
        {
            try
            {
                return read().Any(e => e.Name.EndsWith(".ncz", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException or ArgumentException)
            {
                return false;
            }
        }
        if (buf[0] == 'P' && buf[1] == 'F' && buf[2] == 'S' && buf[3] == '0')
        {
            return Compressed(() => SwitchFormats.ReadPfs0(fs, 0)) ? ".nsz" : ".nsp";
        }
        if (buf[0x100] == 'H' && buf[0x101] == 'E' && buf[0x102] == 'A' && buf[0x103] == 'D')
        {
            return Compressed(() => SwitchFormats.ReadXciSecurePartition(fs)) ? ".xcz" : ".xci";
        }
        return null;
    }

    private static string? ReadTitleIdFromHeaders(string path, string ext, byte[] key)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        var entries = ext is ".xci" or ".xcz" ? SwitchFormats.ReadXciSecurePartition(fs) : SwitchFormats.ReadPfs0(fs, 0);
        // Prefer the meta NCA (its program id is the content's title id: base, update or DLC).
        var ncas = entries.Where(e => e.Name.EndsWith(".nca", StringComparison.OrdinalIgnoreCase) ||
                                      e.Name.EndsWith(".ncz", StringComparison.OrdinalIgnoreCase))
                          .OrderByDescending(e => e.Name.Contains(".cnmt.", StringComparison.OrdinalIgnoreCase))
                          .ToList();
        ulong? programFallback = null;
        var metas = new List<ulong>();
        foreach (var e in ncas)
        {
            if (e.Size < 0xC00)
            {
                continue;
            }
            var h = NcaHeaderReader.Read(fs, e.Offset, key);
            if (h.ContentType == NcaContentType.Meta)
            {
                // A dump can bundle the base game with its update/DLC: the base decides the tile.
                metas.Add(h.ProgramId);
                continue;
            }
            if (h.ContentType == NcaContentType.Program)
            {
                programFallback ??= h.ProgramId;
            }
        }
        if (metas.Count > 0)
        {
            var pick = metas.Where(m => KindFromTitleId(m) == ContentKind.Base).DefaultIfEmpty(metas[0]).First();
            return KeyFile.FormatTitleId(pick);
        }
        return programFallback is { } p ? KeyFile.FormatTitleId(p) : null;
    }

    /// <summary>
    /// Switch title id conventions: updates are base|0x800; DLC ids are base+0x1000+n.
    /// Heuristic until the CNMT meta type is read (needs section decryption).
    /// </summary>
    internal static ContentKind KindFromTitleId(ulong tid)
    {
        if ((tid & 0xFFF) == 0x800)
        {
            return ContentKind.Update;
        }
        if ((tid & 0xFFF) != 0 || ((tid >> 12) & 0x1) == 1)
        {
            return ContentKind.Dlc;
        }
        return ContentKind.Base;
    }

    internal static string CleanTitle(string fileName)
    {
        var t = TagRegex().Replace(fileName, " ");
        t = t.Replace('_', ' ');
        t = SpaceRegex().Replace(t, " ").Trim(' ', '-', '.');
        return t.Length == 0 ? fileName : t;
    }

    [GeneratedRegex(@"\[([0-9A-Fa-f]{16})\]")]
    private static partial Regex TitleIdRegex();

    [GeneratedRegex(@"\[v(\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}
