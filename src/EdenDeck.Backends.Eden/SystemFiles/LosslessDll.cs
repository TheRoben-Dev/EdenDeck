using System.Text.RegularExpressions;

namespace EdenDeck.Backends.Eden.SystemFiles;

/// <summary>
/// Finds the user's own Lossless Scaling install (Steam, run through Proton) and copies its
/// Lossless.dll into Eden's lossless/ dir, where Eden Nightly's built-in frame gen loads it
/// (src/video_core/frame_gen/lossless_dll.cpp: GetEdenPath(LosslessDir) / "Lossless.dll").
/// Never downloads anything: without the user's own copy, frame gen stays unavailable.
/// </summary>
public static partial class LosslessDll
{
    public const string FileName = "Lossless.dll";
    private const string CacheFileName = "lsfg_spirv.cache";
    private const string AppFolder = "Lossless Scaling";

    public sealed record Status(bool Installed, string? Source, string Message);

    /// <summary>Steam roots to look in (native, ~/.steam symlink, Flatpak).</summary>
    public static IEnumerable<string> SteamRoots(string home) =>
    [
        Path.Combine(home, ".local", "share", "Steam"),
        Path.Combine(home, ".steam", "steam"),
        Path.Combine(home, ".steam", "root"),
        Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
    ];

    /// <summary>Every Steam library folder listed in libraryfolders.vdf, plus the roots themselves.</summary>
    public static IEnumerable<string> SteamLibraries(string home)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in SteamRoots(home))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            if (seen.Add(Normalize(root)))
            {
                yield return root;
            }
            foreach (var vdf in new[] { Path.Combine(root, "steamapps", "libraryfolders.vdf"), Path.Combine(root, "config", "libraryfolders.vdf") })
            {
                if (!File.Exists(vdf))
                {
                    continue;
                }
                string text;
                try
                {
                    text = File.ReadAllText(vdf);
                }
                catch (IOException)
                {
                    continue;
                }
                foreach (Match m in PathEntry().Matches(text))
                {
                    var lib = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(lib) && seen.Add(Normalize(lib)))
                    {
                        yield return lib;
                    }
                }
            }
        }
    }

    /// <summary>The user's Lossless.dll in a Steam library, or null.</summary>
    public static string? FindInSteam(string home)
    {
        foreach (var lib in SteamLibraries(home))
        {
            var candidate = Path.Combine(lib, "steamapps", "common", AppFolder, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>Is a usable-looking Lossless.dll already in Eden's lossless/ dir?</summary>
    public static bool IsInstalled(string losslessDir) => LooksLikePe(Path.Combine(losslessDir, FileName));

    /// <summary>
    /// Makes sure Eden's lossless/ dir has the user's Lossless.dll, copying it from Steam when it is
    /// missing or differs from the Steam copy (e.g. Lossless Scaling updated). Removes Eden's shader
    /// cache when the DLL changes so Eden rebuilds it.
    /// </summary>
    public static Status Ensure(string losslessDir, string home)
    {
        var target = Path.Combine(losslessDir, FileName);
        var source = FindInSteam(home);
        if (source == null)
        {
            return IsInstalled(losslessDir)
                ? new(true, null, "Lossless.dll is in Eden's lossless folder")
                : new(false, null, $"Lossless.dll not found. Install Lossless Scaling in Steam, or copy your own Lossless.dll to {losslessDir}");
        }
        if (!LooksLikePe(source))
        {
            return new(IsInstalled(losslessDir), source, $"{source} doesn't look like a valid Lossless.dll");
        }
        try
        {
            if (!SameFile(source, target))
            {
                Directory.CreateDirectory(losslessDir);
                var tmp = target + ".tmp";
                File.Copy(source, tmp, overwrite: true);
                File.Move(tmp, target, overwrite: true);
                var cache = Path.Combine(losslessDir, CacheFileName);
                if (File.Exists(cache))
                {
                    File.Delete(cache);
                }
                return new(true, source, "Lossless.dll copied from your Steam install of Lossless Scaling");
            }
            return new(true, source, "Lossless.dll is ready (from your Steam install)");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(IsInstalled(losslessDir), source, $"Couldn't copy Lossless.dll: {e.Message}");
        }
    }

    private static bool SameFile(string a, string b)
    {
        if (!File.Exists(b))
        {
            return false;
        }
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length)
        {
            return false;
        }
        using var sa = fa.OpenRead();
        using var sb = fb.OpenRead();
        var ba = new byte[81920];
        var bb = new byte[81920];
        int n;
        while ((n = sa.Read(ba, 0, ba.Length)) > 0)
        {
            var m = sb.ReadAtLeast(bb, n, throwOnEndOfStream: false);
            if (m != n || !ba.AsSpan(0, n).SequenceEqual(bb.AsSpan(0, n)))
            {
                return false;
            }
        }
        return true;
    }

    private static bool LooksLikePe(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }
            using var f = File.OpenRead(path);
            Span<byte> mz = stackalloc byte[2];
            return f.Read(mz) == 2 && mz[0] == (byte)'M' && mz[1] == (byte)'Z';
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string Normalize(string p)
    {
        try
        {
            var info = new DirectoryInfo(p);
            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            return Path.TrimEndingDirectorySeparator((resolved?.FullName ?? info.FullName));
        }
        catch (IOException)
        {
            return p;
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntry();
}
