using System.Globalization;
using EdenDeck.Backends.Eden.Library;

namespace EdenDeck.Backends.Eden.SystemFiles;

/// <summary>A game needs a newer master key than the prod.keys it would launch with.</summary>
public sealed class KeysTooOldException(string message, int needed, int have) : InvalidOperationException(message)
{
    public int Needed { get; } = needed;
    public int Have { get; } = have;
}

/// <summary>
/// Checks, before launch, that the user's prod.keys can decrypt a game. Each NCA header names a key
/// generation; decrypting it needs master_key_XX (or the derived key_area_key_application_XX /
/// titlekek_XX) for that generation. Newer games use newer generations than older key dumps have,
/// which otherwise ends in Eden's "key area key ... could not be found" dialog.
/// Only reads files the user already has; never fetches keys.
/// </summary>
public static class KeyRequirements
{
    // Master key index -> first system version that introduced it (switchbrew key generations).
    private static readonly string[] Firmware =
    [
        "1.0.0", "3.0.0", "3.0.1", "4.0.0", "5.0.0", "6.0.0", "6.2.0", "7.0.0", "8.1.0", "9.0.0", "9.1.0",
        "12.1.0", "13.0.0", "14.0.0", "15.0.0", "16.0.0", "17.0.0", "18.0.0", "19.0.0", "20.0.0", "21.0.0",
    ];

    public static string FirmwareFor(int masterKeyIndex) =>
        masterKeyIndex >= 0 && masterKeyIndex < Firmware.Length ? Firmware[masterKeyIndex] : $"newer than {Firmware[^1]}";

    /// <summary>Highest master key index the key file can serve, or -1 if none.</summary>
    public static int MaxMasterKey(IReadOnlyDictionary<string, byte[]> keys)
    {
        var max = -1;
        foreach (var (name, value) in keys)
        {
            if (value.All(b => b == 0))
            {
                continue;
            }
            foreach (var prefix in new[] { "master_key_", "key_area_key_application_", "titlekek_" })
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(name.AsSpan(prefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var i))
                {
                    max = Math.Max(max, i);
                }
            }
        }
        return max;
    }

    /// <summary>Highest master key index any NCA in the game file needs; null if it can't be read.</summary>
    public static int? Required(string path, byte[] headerKey)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".nro")
            {
                return null;
            }
            var entries = ext is ".xci" or ".xcz" ? SwitchFormats.ReadXciSecurePartition(fs) : SwitchFormats.ReadPfs0(fs, 0);
            int? max = null;
            foreach (var e in entries.Where(e => e.Size >= 0xC00 &&
                                                 (e.Name.EndsWith(".nca", StringComparison.OrdinalIgnoreCase) ||
                                                  e.Name.EndsWith(".ncz", StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    var h = NcaHeaderReader.Read(fs, e.Offset, headerKey);
                    max = Math.Max(max ?? 0, h.MasterKeyIndex);
                }
                catch (InvalidDataException)
                {
                }
            }
            return max;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Throws <see cref="KeysTooOldException"/> when the key file is too old for any of the files.</summary>
    public static void Check(string title, IEnumerable<string> gameFiles, string prodKeysPath, string keysLabel)
    {
        if (!File.Exists(prodKeysPath))
        {
            return;  // Eden reports missing keys itself; nothing to compare against
        }
        var keys = KeyFile.Parse(File.ReadAllText(prodKeysPath));
        if (!KeyFile.TryGetHeaderKey(keys, out var headerKey))
        {
            return;
        }
        var have = MaxMasterKey(keys);
        var needed = gameFiles.Select(f => Required(f, headerKey)).Where(n => n != null).Select(n => n!.Value).DefaultIfEmpty(-1).Max();
        if (needed > have)
        {
            throw new KeysTooOldException(
                $"{title} needs keys from firmware {FirmwareFor(needed)} or newer (master key {needed:x2}). " +
                $"{keysLabel} only go up to firmware {FirmwareFor(have)} (master key {Math.Max(have, 0):x2}).\n\n" +
                "Dump prod.keys from a console on a newer firmware, add them as a key set on the Eden tab, " +
                "then choose that set under Keys on this game's page.",
                needed, have);
        }
    }
}
