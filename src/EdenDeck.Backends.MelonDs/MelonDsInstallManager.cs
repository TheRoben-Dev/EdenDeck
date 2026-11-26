using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EdenDeck.Core.Backends;

namespace EdenDeck.Backends.MelonDs;

/// <summary>
/// Finds melonDS (AppImage, Flatpak or a system package) or installs the official AppImage from
/// melonDS's own GitHub releases (the latest stable release). melonDS ships its Linux AppImage
/// inside a zip ("melonDS-1.1-appimage-x86_64.zip"); Scanline unpacks the AppImage and keeps only
/// that. AppImages come first, then system packages, then the Flatpak. A Flatpak install is reported with <see cref="FlatpakId"/> as its executable and
/// launched through <c>flatpak run</c>.
/// </summary>
public sealed partial class MelonDsInstallManager : IInstallManager
{
    public const string FlatpakId = "net.kuribo64.melonDS";
    public const string FlatpakExe = "flatpak:" + FlatpakId;
    public const string ReleaseFeed = "https://api.github.com/repos/melonDS-emu/melonDS/releases/latest";

    private readonly string _root;
    private readonly HttpClient _http;
    private readonly Func<string, string?> _env;
    private State _state;

    private sealed record State(InstalledEmulator? Current, InstalledEmulator? Previous);

    public MelonDsInstallManager(string emulatorsDir, HttpClient? http = null, Func<string, string?>? env = null)
    {
        _root = Path.Combine(emulatorsDir, "melonds");
        _http = http ?? CreateHttp();
        _env = env ?? Environment.GetEnvironmentVariable;
        _state = LoadState();
        if (_state.Current == null && DetectExternal().FirstOrDefault() is { } external)
        {
            _state = new State(external, null);
        }
    }

    public IReadOnlyList<string> Channels => ["stable"];
    public InstalledEmulator? Current => _state.Current;
    public InstalledEmulator? Previous => _state.Previous;

    private string StateFile => Path.Combine(_root, "state.json");

    private string Home => _env("HOME") ?? "";

    /// <summary>AppImages first (frame gen and the layer work there), then system packages, then the Flatpak.</summary>
    public IReadOnlyList<InstalledEmulator> DetectExternal()
    {
        var found = new List<InstalledEmulator>();
        string[] dirs = [Path.Combine(Home, "Applications"), Path.Combine(Home, "AppImages"), Path.Combine(Home, ".local/bin"), Path.Combine(Home, "Downloads")];
        foreach (var dir in dirs.Where(Directory.Exists))
        {
            foreach (var f in Directory.EnumerateFiles(dir).OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var name = Path.GetFileName(f);
                if (name.Contains("melonds", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(FromFile(f, managed: false));
                }
            }
        }
        foreach (var bin in new[] { "/usr/bin/melonDS", "/usr/local/bin/melonDS" })
        {
            if (File.Exists(bin))
            {
                found.Add(FromFile(bin, managed: false));
            }
        }
        if (FlatpakInstalled())
        {
            found.Add(new InstalledEmulator("flatpak", "Flatpak", FlatpakExe, false));
        }
        return found;
    }

    public bool FlatpakInstalled() =>
        Directory.Exists(Path.Combine("/var/lib/flatpak/app", FlatpakId)) ||
        Directory.Exists(Path.Combine(Home, ".local/share/flatpak/app", FlatpakId));

    public async Task<AvailableRelease?> CheckForUpdateAsync(string channel, CancellationToken ct)
    {
        var json = await _http.GetStringAsync(ReleaseFeed, ct);
        var available = ToAvailable(Parse(json), RuntimeInformation.OSArchitecture);
        if (available == null)
        {
            return null;
        }
        var cur = _state.Current;
        return cur is { ManagedByEdenDeck: true } && cur.Version == available.Version ? null : available;
    }

    public sealed record Asset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

    public sealed record Release(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("assets")] List<Asset> Assets);

    public static Release Parse(string json) => JsonSerializer.Deserialize<Release>(json) ?? throw new InvalidDataException("empty release JSON");

    /// <summary>
    /// The Linux AppImage for this machine: an .AppImage, or the zip around one, whose name says
    /// "appimage" and the architecture ("x86_64", "aarch64").
    /// </summary>
    public static AvailableRelease? ToAvailable(Release release, Architecture arch)
    {
        var tag = arch switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            _ => null,
        };
        if (tag == null)
        {
            return null;
        }
        var asset = release.Assets
            .Where(a => a.Name.Contains("appimage", StringComparison.OrdinalIgnoreCase) && a.Name.Contains(tag, StringComparison.OrdinalIgnoreCase) &&
                        (a.Name.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase) || a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a.Name.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
        if (asset == null)
        {
            return null;
        }
        var sha = asset.Digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? d[7..].ToLowerInvariant() : null;
        return new AvailableRelease("stable", release.TagName.TrimStart('v'), asset.BrowserDownloadUrl, asset.Size, sha, release.PublishedAt);
    }

    public async Task<InstalledEmulator> InstallAsync(AvailableRelease release, IProgress<double>? progress, CancellationToken ct)
    {
        if (!IsOfficial(release.DownloadUrl))
        {
            throw new InvalidOperationException($"Refusing to download melonDS from a non-official host: {release.DownloadUrl}");
        }
        var dir = Path.Combine(_root, Sanitize(release.Version));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "melonDS.AppImage");
        var zipped = release.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var partial = target + (zipped ? ".zip.part" : ".part");
        using (var resp = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? release.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(partial);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                sha.AppendData(buf, 0, n);
                done += n;
                if (total > 0)
                {
                    progress?.Report((double)done / total);
                }
            }
            var actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            if (release.Sha256 != null && actual != release.Sha256)
            {
                dst.Close();
                File.Delete(partial);
                throw new InvalidDataException($"Checksum mismatch for melonDS {release.Version}.");
            }
        }
        if (zipped)
        {
            Unzip(partial, target);
            File.Delete(partial);
        }
        else
        {
            File.Move(partial, target, overwrite: true);
        }
        File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        var installed = new InstalledEmulator("stable", release.Version, target, ManagedByEdenDeck: true);
        SetCurrent(installed);
        return installed;
    }

    public InstalledEmulator Rollback()
    {
        var prev = _state.Previous ?? throw new InvalidOperationException("No previous melonDS version to roll back to.");
        if (!Exists(prev))
        {
            throw new FileNotFoundException("The previous melonDS version is missing.", prev.ExecutablePath);
        }
        _state = new State(prev, _state.Current);
        SaveState();
        return prev;
    }

    public void UseExternal(InstalledEmulator emulator) => SetCurrent(emulator);

    private void SetCurrent(InstalledEmulator installed)
    {
        var old = _state.Current;
        var keepPrevious = old != null && old.ExecutablePath != installed.ExecutablePath ? old : _state.Previous;
        var keep = new[] { installed.ExecutablePath, keepPrevious?.ExecutablePath }.Where(p => p != null).ToHashSet();
        foreach (var versionDir in Directory.Exists(_root) ? Directory.EnumerateDirectories(_root) : [])
        {
            if (!keep.Contains(Path.Combine(versionDir, "melonDS.AppImage")))
            {
                Directory.Delete(versionDir, recursive: true);
            }
        }
        _state = new State(installed, keepPrevious);
        SaveState();
    }

    private bool Exists(InstalledEmulator e) => e.ExecutablePath == FlatpakExe ? FlatpakInstalled() : File.Exists(e.ExecutablePath);

    private State LoadState()
    {
        try
        {
            if (File.Exists(StateFile) && JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) is { } s)
            {
                return new State(s.Current is { } c && Exists(c) ? c : null, s.Previous is { } p && Exists(p) ? p : null);
            }
        }
        catch (JsonException)
        {
        }
        return new State(null, null);
    }

    private void SaveState()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(StateFile, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static InstalledEmulator FromFile(string path, bool managed)
    {
        var m = VersionRegex().Match(Path.GetFileName(path));
        return new InstalledEmulator("stable", m.Success ? m.Value : "unknown", path, managed);
    }

    internal static bool IsOfficial(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "github.com" &&
        u.AbsolutePath.StartsWith("/melonDS-emu/melonDS/releases/download/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Takes the AppImage out of the release zip.</summary>
    internal static void Unzip(string zip, string target)
    {
        using var archive = ZipFile.OpenRead(zip);
        var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("The melonDS download has no AppImage in it.");
        entry.ExtractToFile(target, overwrite: true);
    }

    private static string Sanitize(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Scanline", "1.0"));
        return h;
    }

    [GeneratedRegex(@"\d+\.\d+(?:\.\d+)?")]
    private static partial Regex VersionRegex();
}
