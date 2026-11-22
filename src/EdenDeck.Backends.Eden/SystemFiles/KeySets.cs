using System.Text.Json;
using EdenDeck.Backends.Eden.Library;

namespace EdenDeck.Backends.Eden.SystemFiles;

public sealed record KeySet(string Id, string Label, bool HasTitleKeys, bool HasFirmware, string Directory);

public sealed class KeyImportException(string message) : Exception(message);

/// <summary>
/// The user's own key sets (prod.keys/title.keys, optional firmware). EdenDeck never downloads,
/// bundles or links to keys or firmware; it only copies files the user points it at.
/// Layout: &lt;edendeck data&gt;/keysets/&lt;id&gt;/{prod.keys,title.keys,firmware/*.nca,keyset.json}
/// </summary>
public sealed class KeySetStore
{
    private readonly string _root;

    public KeySetStore(string root) => _root = root;

    public IReadOnlyList<KeySet> List()
    {
        if (!System.IO.Directory.Exists(_root))
        {
            return [];
        }
        return System.IO.Directory.EnumerateDirectories(_root)
            .Select(Load)
            .Where(k => k != null)
            .Select(k => k!)
            .OrderBy(k => k.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public KeySet? Get(string id) => List().FirstOrDefault(k => k.Id == id);

    /// <summary>Validates and copies the user's key files (and optional firmware folder) into a new set.</summary>
    public KeySet Import(string label, string prodKeysPath, string? titleKeysPath, string? firmwareFolder)
    {
        ValidateProdKeys(prodKeysPath);
        if (titleKeysPath != null && !File.Exists(titleKeysPath))
        {
            throw new KeyImportException($"title.keys not found: {titleKeysPath}");
        }
        var firmware = firmwareFolder != null ? FirmwareFiles(firmwareFolder) : [];
        if (firmwareFolder != null && firmware.Count == 0)
        {
            throw new KeyImportException("The firmware folder has no .nca files.");
        }

        var id = MakeId(label);
        var dir = Path.Combine(_root, id);
        System.IO.Directory.CreateDirectory(dir);
        File.Copy(prodKeysPath, Path.Combine(dir, "prod.keys"), overwrite: true);
        if (titleKeysPath != null)
        {
            File.Copy(titleKeysPath, Path.Combine(dir, "title.keys"), overwrite: true);
        }
        if (firmware.Count > 0)
        {
            var fw = Path.Combine(dir, "firmware");
            System.IO.Directory.CreateDirectory(fw);
            foreach (var f in firmware)
            {
                File.Copy(f, Path.Combine(fw, Path.GetFileName(f)), overwrite: true);
            }
        }
        File.WriteAllText(Path.Combine(dir, "keyset.json"), JsonSerializer.Serialize(new { label }));
        return Load(dir)!;
    }

    public void Delete(string id)
    {
        var dir = Path.Combine(_root, id);
        if (System.IO.Directory.Exists(dir) && Path.GetDirectoryName(Path.GetFullPath(dir)) == Path.GetFullPath(_root))
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    public static void ValidateProdKeys(string path)
    {
        if (!File.Exists(path))
        {
            throw new KeyImportException($"prod.keys not found: {path}");
        }
        var info = new FileInfo(path);
        if (info.Length > 1 << 20)
        {
            throw new KeyImportException("That file is too large to be a prod.keys file.");
        }
        var keys = KeyFile.Parse(File.ReadAllText(path));
        if (keys.Count == 0)
        {
            throw new KeyImportException("No keys found. Expected lines like 'header_key = ...'.");
        }
        if (!KeyFile.TryGetHeaderKey(keys, out _))
        {
            throw new KeyImportException("prod.keys has no valid header_key, so Eden can't read games with it.");
        }
    }

    public static IReadOnlyList<string> FirmwareFiles(string folder) =>
        System.IO.Directory.Exists(folder)
            ? System.IO.Directory.EnumerateFiles(folder, "*.nca", SearchOption.AllDirectories).ToList()
            : [];

    private KeySet? Load(string dir)
    {
        if (!File.Exists(Path.Combine(dir, "prod.keys")))
        {
            return null;
        }
        var id = Path.GetFileName(dir);
        var label = id;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "keyset.json")));
            label = doc.RootElement.GetProperty("label").GetString() ?? id;
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException)
        {
        }
        var fw = Path.Combine(dir, "firmware");
        return new KeySet(id, label, File.Exists(Path.Combine(dir, "title.keys")),
            System.IO.Directory.Exists(fw) && System.IO.Directory.EnumerateFiles(fw, "*.nca").Any(), dir);
    }

    private string MakeId(string label)
    {
        var slug = new string(label.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length == 0)
        {
            slug = "keyset";
        }
        var id = slug;
        for (var n = 2; System.IO.Directory.Exists(Path.Combine(_root, id)); n++)
        {
            id = $"{slug}-{n}";
        }
        return id;
    }
}
