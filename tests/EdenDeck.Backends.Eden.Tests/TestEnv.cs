using EdenDeck.Backends.Eden;
using EdenDeck.Backends.Eden.Config;
using EdenDeck.Core;

namespace EdenDeck.Backends.Eden.Tests;

/// <summary>A throwaway $HOME with XDG dirs, optionally seeded with a fixture version.</summary>
public sealed class TestEnv : IDisposable
{
    public string Home { get; }
    public Dictionary<string, string?> Vars { get; } = new();

    public TestEnv(string? fixture = null)
    {
        Home = Path.Combine(Path.GetTempPath(), "edendeck-test-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Home);
        Vars["HOME"] = Home;
        Vars["XDG_RUNTIME_DIR"] = Path.Combine(Home, "run");
        if (fixture != null)
        {
            var cfg = Path.Combine(Home, ".config", "eden");
            Directory.CreateDirectory(Path.Combine(cfg, "custom"));
            var src = FixtureDir(fixture);
            File.Copy(Path.Combine(src, "qt-config.ini"), Path.Combine(cfg, "qt-config.ini"));
            foreach (var f in Directory.EnumerateFiles(src, "custom-*.ini"))
            {
                var tid = Path.GetFileNameWithoutExtension(f)["custom-".Length..];
                File.Copy(f, Path.Combine(cfg, "custom", tid + ".ini"));
            }
        }
    }

    public static string FixtureDir(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static IEnumerable<object[]> Fixtures() =>
        Directory.EnumerateDirectories(Path.Combine(AppContext.BaseDirectory, "Fixtures")).Select(d => new object[] { Path.GetFileName(d) });

    public static EdenSchema SchemaFor(string fixture) =>
        EdenSchema.All.Single(s => fixture.EndsWith(s.Label.Replace("nightly-", ""), StringComparison.Ordinal) ||
                                   fixture == $"stable-{s.Label}" || fixture == s.Label);

    public string? Env(string name) => Vars.TryGetValue(name, out var v) ? v : null;

    public EdenPaths Paths(string? cwd = null) => EdenPaths.Resolve(cwd ?? Path.Combine(Home, "cwd"), Env);

    public AppPaths AppPaths() => new(Env);

    public string ConfigFile => Path.Combine(Home, ".config", "eden", "qt-config.ini");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
