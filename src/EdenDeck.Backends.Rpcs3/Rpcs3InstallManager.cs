using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EdenDeck.Core.Backends;

namespace EdenDeck.Backends.Rpcs3;

/// <summary>
/// Finds RPCS3 (AppImage, Flatpak or a system package) or installs the official AppImage, using
/// RPCS3's own update service (the one its built-in updater asks), which points at RPCS3's GitHub
/// builds and gives a SHA-256. AppImages come first: frame generation and Scanline's Vulkan layer
/// can't reach into the Flatpak sandbox. A Flatpak install is reported with <see cref="FlatpakId"/>
/// as its executable and launched through <c>flatpak run</c>.
/// </summary>
public sealed partial class Rpcs3InstallManager : IInstallManager
{
    public const string FlatpakId = "net.rpcs3.RPCS3";
    public const string FlatpakExe = "flatpak:" + FlatpakId;
    public const string ReleaseFeed = "https://update.rpcs3.net/?api=v3&c=0&os_type=linux&os_arch={0}&os_version=0.0.0";

    private readonly string _root;
    private readonly HttpClient _http;
    private readonly Func<string, string?> _env;
    private State _state;

    private sealed record State(InstalledEmulator? Current, InstalledEmulator? Previous);

    public Rpcs3InstallManager(string emulatorsDir, HttpClient? http = null, Func<string, string?>? env = null)
    {
        _root = Path.Combine(emulatorsDir, "rpcs3");
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
                if (name.Contains("rpcs3", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(FromFile(f, managed: false));
                }
            }
        }
        foreach (var bin in new[] { "/usr/bin/rpcs3", "/usr/local/bin/rpcs3" })
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

    public static string? ArchName(Architecture arch) => arch switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => null };

    public async Task<AvailableRelease?> CheckForUpdateAsync(string channel, CancellationToken ct)
    {
        if (ArchName(RuntimeInformation.OSArchitecture) is not { } arch)
        {
            return null;
        }
        var json = await _http.GetStringAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, ReleaseFeed, arch), ct);
        var available = ToAvailable(json);
        if (available == null)
        {
            return null;
        }
        var cur = _state.Current;
        return cur is { ManagedByEdenDeck: true } && cur.Version == available.Version ? null : available;
    }

    /// <summary>
    /// Reads RPCS3's update answer: <c>{"return_code":0|-1, "latest_build":{"version", "datetime",
    /// "linux":{"download","size","checksum"}}}</c> (-1 only means "your build isn't known", which is
    /// the case for Scanline's request, and still carries the latest build).
    /// </summary>
    public static AvailableRelease? ToAvailable(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var code = root.TryGetProperty("return_code", out var rc) && rc.TryGetInt32(out var c) ? c : -255;
        if (code < -1)
        {
            throw new InvalidDataException(code == -2 ? "RPCS3's update service is down for maintenance. Try again later." : $"RPCS3's update service answered with an error ({code}).");
        }
        if (!root.TryGetProperty("latest_build", out var latest) || !latest.TryGetProperty("linux", out var linux) ||
            linux.ValueKind != JsonValueKind.Object || !linux.TryGetProperty("download", out var dl) || dl.GetString() is not { Length: > 0 } url)
        {
            return null;
        }
        var version = latest.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown";
        var size = linux.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s) ? s : 0;
        var sha = linux.TryGetProperty("checksum", out var cs) ? cs.GetString()?.ToLowerInvariant() : null;
        DateTimeOffset? published = latest.TryGetProperty("datetime", out var dt) && DateTimeOffset.TryParse(dt.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var when) ? when : null;
        return new AvailableRelease("stable", version, url, size, string.IsNullOrEmpty(sha) ? null : sha, published);
    }

    public async Task<InstalledEmulator> InstallAsync(AvailableRelease release, IProgress<double>? progress, CancellationToken ct)
    {
        if (!IsOfficial(release.DownloadUrl))
        {
            throw new InvalidOperationException($"Refusing to download RPCS3 from a non-official host: {release.DownloadUrl}");
        }
        var dir = Path.Combine(_root, Sanitize(release.Version));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "rpcs3.AppImage");
        var partial = target + ".part";
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
                throw new InvalidDataException($"Checksum mismatch for RPCS3 {release.Version}.");
            }
        }
        File.Move(partial, target, overwrite: true);
        File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        var installed = new InstalledEmulator("stable", release.Version, target, ManagedByEdenDeck: true);
        SetCurrent(installed);
        return installed;
    }

    public InstalledEmulator Rollback()
    {
        var prev = _state.Previous ?? throw new InvalidOperationException("No previous RPCS3 version to roll back to.");
        if (!Exists(prev))
        {
            throw new FileNotFoundException("The previous RPCS3 version is missing.", prev.ExecutablePath);
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
            if (!keep.Contains(Path.Combine(versionDir, "rpcs3.AppImage")))
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
        u.AbsolutePath.StartsWith("/RPCS3/rpcs3-binaries-linux", StringComparison.Ordinal);

    private static string Sanitize(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Scanline", "1.0"));
        return h;
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+(?:-\d+)?")]
    private static partial Regex VersionRegex();
}
