using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using EdenDeck.Core.Backends;

namespace EdenDeck.Backends.Eden.Install;

/// <summary>
/// Eden's official release feeds (from Eden's CMakeModules/GenerateSCMRev.cmake and
/// src/common/net/net.cpp). GitHub-style release JSON.
/// </summary>
public static class EdenReleaseFeed
{
    public static readonly IReadOnlyDictionary<string, Uri> Feeds = new Dictionary<string, Uri>
    {
        ["stable"] = new("https://stable.eden-emu.dev/latest/release.json"),
        ["nightly"] = new("https://nightly.eden-emu.dev/latest/release.json"),
    };

    public sealed record Asset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

    public sealed record Release(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("assets")] List<Asset> Assets);

    public static Release Parse(string json) =>
        JsonSerializer.Deserialize<Release>(json) ?? throw new InvalidDataException("empty release JSON");

    /// <summary>
    /// Picks the Linux AppImage for this machine. Asset names are not verified yet (see
    /// docs/eden-integration.md §7), so selection is by suffix and architecture tokens.
    /// </summary>
    public static Asset? SelectAppImage(Release release, Architecture arch)
    {
        string[] mine = arch == Architecture.Arm64 ? ["aarch64", "arm64"] : ["x86_64", "amd64", "x64"];
        string[] other = arch == Architecture.Arm64 ? ["x86_64", "amd64", "x64"] : ["aarch64", "arm64"];
        var candidates = release.Assets
            .Where(a => a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
            .Where(a => !other.Any(t => a.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return candidates
            .OrderByDescending(a => mine.Any(t => a.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(a => a.Name.Contains("standard", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name.Contains("legacy", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name.Length)
            .FirstOrDefault();
    }

    public static AvailableRelease? ToAvailable(string channel, Release release, Architecture arch)
    {
        var asset = SelectAppImage(release, arch);
        if (asset == null)
        {
            return null;
        }
        string? sha = null;
        if (asset.Digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            sha = d["sha256:".Length..].ToLowerInvariant();
        }
        return new AvailableRelease(channel, release.TagName, asset.BrowserDownloadUrl, asset.Size, sha, release.PublishedAt);
    }
}
