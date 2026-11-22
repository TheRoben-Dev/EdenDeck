using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EdenDeck.Core.Backends;

namespace EdenDeck.Backends.Eden.Install;

/// <summary>
/// Installs Eden AppImages from Eden's official feeds only, keeps the previous version for
/// one-click rollback, or uses an Eden the user already has.
/// Layout: &lt;edendeck data&gt;/emulators/eden/&lt;channel&gt;/&lt;tag&gt;/Eden.AppImage + state.json
/// </summary>
public sealed partial class EdenInstallManager : IInstallManager
{
    private readonly string _root;
    private readonly HttpClient _http;
    private readonly Func<string, string?> _env;
    private State _state;

    private sealed record State(InstalledEmulator? Current, InstalledEmulator? Previous);

    public EdenInstallManager(string emulatorsDir, HttpClient? http = null, Func<string, string?>? env = null)
    {
        _root = Path.Combine(emulatorsDir, "eden");
        _http = http ?? CreateHttp();
        _env = env ?? Environment.GetEnvironmentVariable;
        _state = LoadState();
        if (_state.Current == null)
        {
            var external = DetectExternal().FirstOrDefault();
            if (external != null)
            {
                _state = new State(external, null);
            }
        }
    }

    public IReadOnlyList<string> Channels => ["stable", "nightly"];
    public InstalledEmulator? Current => _state.Current;
    public InstalledEmulator? Previous => _state.Previous;

    private string StateFile => Path.Combine(_root, "state.json");

    public IReadOnlyList<InstalledEmulator> DetectExternal()
    {
        var home = _env("HOME") ?? "";
        var found = new List<InstalledEmulator>();
        string[] dirs = [Path.Combine(home, "Applications"), Path.Combine(home, "AppImages"), Path.Combine(home, ".local/bin"), Path.Combine(home, "Downloads")];
        foreach (var dir in dirs.Where(Directory.Exists))
        {
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(f);
                if (name.Contains("eden", StringComparison.OrdinalIgnoreCase) &&
                    (name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) || name is "eden"))
                {
                    found.Add(FromFile(f, managed: false));
                }
            }
        }
        foreach (var bin in new[] { "/usr/bin/eden", "/usr/local/bin/eden" })
        {
            if (File.Exists(bin))
            {
                found.Add(FromFile(bin, managed: false));
            }
        }
        return found.OrderByDescending(e => File.GetLastWriteTimeUtc(e.ExecutablePath)).ToList();
    }

    public async Task<AvailableRelease?> CheckForUpdateAsync(string channel, CancellationToken ct)
    {
        if (!EdenReleaseFeed.Feeds.TryGetValue(channel, out var url))
        {
            throw new ArgumentException($"unknown channel {channel}");
        }
        var json = await _http.GetStringAsync(url, ct);
        var release = EdenReleaseFeed.Parse(json);
        var available = EdenReleaseFeed.ToAvailable(channel, release, RuntimeInformation.OSArchitecture);
        if (available == null)
        {
            return null;
        }
        var cur = _state.Current;
        return cur != null && cur.Channel == channel && cur.Version == available.Version ? null : available;
    }

    public async Task<InstalledEmulator> InstallAsync(AvailableRelease release, IProgress<double>? progress, CancellationToken ct)
    {
        if (!IsOfficial(release.DownloadUrl))
        {
            throw new InvalidOperationException($"Refusing to download Eden from a non-official host: {release.DownloadUrl}");
        }
        var dir = Path.Combine(_root, release.Channel, Sanitize(release.Version));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "Eden.AppImage");
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
                throw new InvalidDataException($"Checksum mismatch for Eden {release.Version}: expected {release.Sha256}, got {actual}");
            }
        }
        File.Move(partial, target, overwrite: true);
        File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        var installed = new InstalledEmulator(release.Channel, release.Version, target, ManagedByEdenDeck: true);
        SetCurrent(installed);
        return installed;
    }

    public InstalledEmulator Rollback()
    {
        var prev = _state.Previous ?? throw new InvalidOperationException("No previous Eden version to roll back to.");
        if (!File.Exists(prev.ExecutablePath))
        {
            throw new FileNotFoundException("The previous Eden version is missing.", prev.ExecutablePath);
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
        // Delete managed installs that are neither current nor previous.
        var keep = new[] { installed.ExecutablePath, keepPrevious?.ExecutablePath }.Where(p => p != null).ToHashSet();
        foreach (var channelDir in Directory.Exists(_root) ? Directory.EnumerateDirectories(_root) : [])
        {
            foreach (var versionDir in Directory.EnumerateDirectories(channelDir))
            {
                var exe = Path.Combine(versionDir, "Eden.AppImage");
                if (!keep.Contains(exe))
                {
                    Directory.Delete(versionDir, recursive: true);
                }
            }
        }
        _state = new State(installed, keepPrevious);
        SaveState();
    }

    private State LoadState()
    {
        try
        {
            if (File.Exists(StateFile))
            {
                var s = JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile));
                if (s != null)
                {
                    return s with
                    {
                        Current = s.Current is { } c && File.Exists(c.ExecutablePath) ? c : null,
                        Previous = s.Previous is { } p && File.Exists(p.ExecutablePath) ? p : null,
                    };
                }
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
        var name = Path.GetFileName(path);
        var channel = name.Contains("nightly", StringComparison.OrdinalIgnoreCase) ? "nightly" : "stable";
        var m = VersionRegex().Match(name);
        var version = m.Success ? m.Value : "unknown";
        return new InstalledEmulator(channel, version, path, managed);
    }

    internal static bool IsOfficial(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" &&
        (u.Host == "eden-emu.dev" || u.Host.EndsWith(".eden-emu.dev", StringComparison.OrdinalIgnoreCase) ||
         // Eden publishes release assets on GitHub as well; only its own organisations are accepted.
         (u.Host == "github.com" && (u.AbsolutePath.StartsWith("/eden-emulator/", StringComparison.OrdinalIgnoreCase) ||
                                     u.AbsolutePath.StartsWith("/eden-emu/", StringComparison.OrdinalIgnoreCase))));

    private static string Sanitize(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EdenDeck", "0.1"));
        return h;
    }

    [GeneratedRegex(@"v?\d+\.\d+\.\d+(?:-(?:rc|beta|alpha)\d*)?|\b[0-9a-f]{7,12}\b")]
    private static partial Regex VersionRegex();
}
