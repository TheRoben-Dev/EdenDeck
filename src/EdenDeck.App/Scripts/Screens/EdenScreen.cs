using EdenDeck.Backends.Eden.SystemFiles;
using EdenDeck.Backends.Eden;
using EdenDeck.App.Ui;
using EdenDeck.Backends.Eden.Install;
using EdenDeck.Core.Backends;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// Eden manager in two panes (see <see cref="TwoPaneScreen"/>): install/update from Eden's official
/// feed and rollback, import of the user's own keys and firmware (default and per-game sets), frame
/// generation status, and paths. Nothing here downloads keys or firmware.
/// </summary>
public partial class EdenScreen : TwoPaneScreen
{
    protected override string Heading => "Eden";

    protected override IReadOnlyList<(string Name, string Summary)> Categories
    {
        get
        {
            var cur = Host.Eden.Install.Current;
            var sets = Host.Eden.KeySets.List().Count;
            return
            [
                ("Emulator", cur != null ? $"Eden {Cap(cur.Channel)} {cur.Version}: install, update, roll back or open Eden." : "Eden isn't installed yet: install it here."),
                ("Keys and firmware", "Your own prod.keys and firmware, used by every game."),
                ("Key sets", sets == 0 ? "Older or newer keys for specific games." : $"{sets} key set{(sets == 1 ? "" : "s")} for specific games."),
                ("Frame generation", "Lossless Scaling frame generation (lsfg-vk): what's found and what's missing."),
                ("Advanced", "Another Eden AppImage, settings format and where Eden keeps its files."),
            ];
        }
    }

    /// <summary>A dot on categories that need attention.</summary>
    protected override string? Badge(string name) => name switch
    {
        "Emulator" when Host.Eden.Install.Current == null || App.EdenUpdate != null => "•",
        "Keys and firmware" when Host.Eden.SystemFilesImpl.Status().Any(s => !s.Present && s.Kind != "title.keys") => "•",
        "Frame generation" when Host.Eden.FindLsfgVk().Kind != LsfgVk.Kind.V2 || !Host.Eden.LosslessDllAvailable() => "•",
        _ => null,
    };

    protected override void Build()
    {
        App.CheckForEdenUpdate();
        base.Build();
    }

    protected override void BuildCategory(string name, VBoxContainer col, Action<Control> Add)
    {
        switch (name)
        {
            case "Emulator":
                BuildEmulator(col, Add);
                break;
            case "Keys and firmware":
                BuildKeys(col, Add);
                break;
            case "Key sets":
                BuildKeySets(col, Add);
                break;
            case "Frame generation":
                BuildFrameGen(col, Add);
                break;
            case "Advanced":
                BuildAdvanced(col, Add);
                break;
        }
    }

    private Label Note(string text, Color? color = null) => UiTheme.Label(text, UiTheme.SmallSize + 1, color ?? UiTheme.Dim, wrap: true);

    private void BuildEmulator(VBoxContainer col, Action<Control> Add)
    {
        var install = Host.Eden.Install;
        if (App.EdenUpdate is { } upd && install.Current is { } now)
        {
            // Banner: a newer build on the channel you use (found by the background check).
            var banner = new PanelContainer();
            banner.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.PanelHi, 2, 12, UiTheme.Warn));
            var inner = Column(8);
            inner.AddChild(UiTheme.Label($"Eden {Cap(upd.Channel)} {upd.Version} is available", UiTheme.FontSize + 2, UiTheme.Warn));
            inner.AddChild(Note($"You have {now.Version}. The update is checksum-verified and your current version is kept for rollback."));
            banner.AddChild(inner);
            col.AddChild(banner);
            Anim.FadeIn(banner);
            Add(new ActionButton($"Update to {upd.Version}", () => Install(upd), "Downloads from Eden's official site."));
        }
        if (install.Current is { } cur)
        {
            col.AddChild(UiTheme.Label($"Eden {Cap(cur.Channel)} {cur.Version}", 24));
            col.AddChild(Note((cur.ManagedByEdenDeck ? "Installed by Scanline · " : "Your own copy · ") + cur.ExecutablePath));
        }
        else
        {
            col.AddChild(UiTheme.Label("Eden isn't installed yet.", 24, UiTheme.Warn));
            col.AddChild(Note("Install Stable to get started. Nightly has the newest features."));
        }
        foreach (var channel in install.Channels)
        {
            var ch = channel;
            Add(new ActionButton($"Install or update {Cap(ch)}", () => CheckAndInstall(ch),
                ch == "nightly"
                    ? "Newest features. Downloaded from Eden's official site and checksum-verified."
                    : "Recommended. Downloaded from Eden's official site and checksum-verified."));
        }
        if (install.Previous is { } prev)
        {
            Add(new ActionButton($"Roll back to {Cap(prev.Channel)} {prev.Version}", () => App.Guard(() =>
            {
                var back = install.Rollback();
                App.Success($"Now using Eden {back.Version}");
                Rebuild();
            }), "Switches back to the version you used before the last update."));
        }
        if (install.Current != null)
        {
            Add(new ActionButton("Open Eden", App.OpenEden,
                "Opens Eden's own window for anything Scanline doesn't cover yet. Close it to come back."));
        }
    }

    private void BuildKeys(VBoxContainer col, Action<Control> Add)
    {
        foreach (var s in Host.Eden.SystemFilesImpl.Status())
        {
            col.AddChild(UiTheme.Label($"{(s.Present ? "✓" : s.Kind == "title.keys" ? "·" : "✗")}  {s.Kind}   {s.Detail}", UiTheme.SmallSize + 2,
                s.Present ? UiTheme.Good : s.Kind == "title.keys" ? UiTheme.Dim : UiTheme.Warn, wrap: true));
        }
        var defaultMax = EdenBackend.MaxMasterKeyOf(Path.Combine(Host.Eden.Paths.KeysDir, "prod.keys"));
        if (defaultMax >= 0)
        {
            col.AddChild(Note($"Your keys cover games up to firmware {KeyRequirements.FirmwareFor(defaultMax)} (master key {defaultMax:x2})."));
        }
        Add(new ActionButton("Import prod.keys", ImportKeys,
            "Copies your own prod.keys (and title.keys from the same folder, if present) into Eden."));
        Add(new ActionButton("Import firmware folder", ImportFirmware,
            "Copies your own firmware .nca files into Eden. The current firmware is backed up first."));
        col.AddChild(Note("Scanline never downloads keys, firmware or games. Use your own dumps."));
    }

    private void BuildKeySets(VBoxContainer col, Action<Control> Add)
    {
        col.AddChild(Note("Some games need older or newer keys. Add a set here, then choose it on the game's page. It's swapped in only while that game runs."));
        foreach (var k in Host.Eden.KeySets.List())
        {
            var set = k;
            var max = EdenBackend.MaxMasterKeyOf(Path.Combine(set.Directory, "prod.keys"));
            var upTo = max >= 0 ? $"  ·  up to fw {KeyRequirements.FirmwareFor(max)}" : "";
            Add(new ActionButton($"{set.Label}{upTo}{(set.HasFirmware ? "  ·  firmware" : "")}{(set.HasTitleKeys ? "  ·  title.keys" : "")}",
                () => App.Push(new ChoicePage($"Key set \"{set.Label}\"",
                    ("Keep", () => { }),
                    ("Delete this set", () => App.Guard(() =>
                    {
                        Host.Eden.KeySets.Delete(set.Id);
                        Rebuild();
                    })))),
                set.Directory));
        }
        Add(new ActionButton("Add key set", AddKeySet, "Pick a prod.keys file. The folder name becomes the set's name."));
    }

    private void BuildFrameGen(VBoxContainer col, Action<Control> Add)
    {
        col.AddChild(Note("Eden's own frame generation only exists on Android, so Scanline uses lsfg-vk with your copy of Lossless Scaling. " +
                          "It doubles 30 fps games to 60 and never goes above 60. Turn it on per game on the game's page."));
        var lsfg = Host.Eden.FindLsfgVk();
        var (lsfgText, lsfgColor) = lsfg.Kind switch
        {
            LsfgVk.Kind.V2 => ($"✓  lsfg-vk   {lsfg.Manifest}", UiTheme.Good),
            LsfgVk.Kind.Legacy => ($"✗  lsfg-vk   version 1.x found ({lsfg.Manifest}); Scanline needs 2.x", UiTheme.Warn),
            _ => ("✗  lsfg-vk   not installed", UiTheme.Warn),
        };
        col.AddChild(UiTheme.Label(lsfgText, UiTheme.SmallSize + 2, lsfgColor, wrap: true));
        var dll = Host.Eden.LosslessDllPath();
        col.AddChild(UiTheme.Label(dll != null ? $"✓  Lossless.dll   {dll}" : "✗  Lossless.dll   not found in your Steam libraries",
            UiTheme.SmallSize + 2, dll != null ? UiTheme.Good : UiTheme.Warn, wrap: true));
        if (dll == null)
        {
            col.AddChild(Note("Install Lossless Scaling from your Steam library (it's a paid app). SD card libraries are found too."));
        }
        if (lsfg.Kind != LsfgVk.Kind.V2)
        {
            col.AddChild(Note("lsfg-vk 2.x isn't installed yet. Bundling it inside Scanline is coming in a later update."));
        }
        if (lsfg.Kind == LsfgVk.Kind.V2 && dll != null)
        {
            col.AddChild(Note("Everything is in place. Your own lsfg-vk settings aren't changed; Scanline only turns it on for Eden games that need it.", UiTheme.Good));
        }
        Add(new ActionButton("Check again", () =>
        {
            Rebuild();
            App.Toast("Checked lsfg-vk and Lossless.dll.");
        }, "Looks for lsfg-vk and Lossless.dll again, after installing either."));
    }

    private void BuildAdvanced(VBoxContainer col, Action<Control> Add)
    {
        Add(new ActionButton("Use an Eden AppImage from this device", PickExternal,
            "Point Scanline at an Eden AppImage you already have."));
        col.AddChild(Section("Details"));
        col.AddChild(Note($"Settings format: {Host.Eden.Schema.Label}"));
        var p = Host.Eden.Paths;
        col.AddChild(Note($"Data: {p.DataDir}"));
        col.AddChild(Note($"Config: {p.ConfigDir}"));
        col.AddChild(Note($"Keys: {p.KeysDir}"));
        col.AddChild(Note($"Firmware: {p.FirmwareDir}"));
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private void CheckAndInstall(string channel)
    {
        var install = Host.Eden.Install;
        App.Busy($"Checking Eden {Cap(channel)}…", (_, ct) => install.CheckForUpdateAsync(channel, ct), release =>
        {
            if (release == null)
            {
                App.Toast(install.Current?.Channel == channel ? "Already up to date." : "No Linux build in Eden's feed right now.");
                return;
            }
            var size = release.Size > 0 ? $" ({release.Size / 1048576} MB)" : "";
            var check = release.Sha256 == null ? "\n\nEden didn't publish a checksum for this build." : "";
            App.Confirm($"Install Eden {Cap(release.Channel)} {release.Version}{size}?{check}", "Install", () => Install(release));
        });
    }

    private void Install(AvailableRelease release) =>
        App.Busy($"Downloading Eden {release.Version}…", (p, ct) => Host.Eden.Install.InstallAsync(release, p, ct), installed =>
        {
            Host.Eden.ReloadPaths();
            App.SetEdenUpdate(null);
            App.Success($"Eden {installed.Version} installed{(release.Sha256 != null ? ", checksum verified" : "")}.");
            Rebuild();
        });

    private void PickExternal() =>
        App.Push(new FilePicker("Choose an Eden AppImage", pickFolder: false, path => App.Guard(() =>
        {
            Host.Eden.Install.UseExternal(EdenInstallManager.FromFile(path, managed: false));
            App.Success("Using " + Path.GetFileName(path));
            Rebuild();
        }), null, ".appimage"));

    private void ImportKeys() =>
        App.Push(new FilePicker("Choose your prod.keys", pickFolder: false, path => App.Guard(() =>
        {
            var title = SiblingTitleKeys(path);
            Host.Eden.SystemFilesImpl.ImportDefaultKeys(path, title);
            App.Success(title != null ? "Imported prod.keys and title.keys." : "Imported prod.keys.");
            Rebuild();
        }), null, ".keys"));

    private void ImportFirmware() =>
        App.Push(new FilePicker("Choose your firmware folder", pickFolder: true, dir =>
            App.Confirm("Replace Eden's firmware with the files in this folder? The current firmware is backed up first.", "Replace firmware", () =>
                App.Busy("Importing firmware…", (_, _) => Task.FromResult(Host.Eden.SystemFilesImpl.ImportFirmware(dir)), n =>
                {
                    App.Success($"Imported {n} firmware files.");
                    Rebuild();
                }, cancellable: false))));

    private void AddKeySet() =>
        App.Push(new FilePicker("Choose the set's prod.keys", pickFolder: false, prod =>
        {
            var label = Path.GetFileName(Path.GetDirectoryName(prod)) ?? "Key set";
            var title = SiblingTitleKeys(prod);
            void Import(string? firmware) => App.Guard(() =>
            {
                var set = Host.Eden.KeySets.Import(label, prod, title, firmware);
                App.Success($"Added key set \"{set.Label}\"{(set.HasFirmware ? " with firmware" : "")}.");
                Rebuild();
            });
            App.Push(new ChoicePage($"Add firmware to \"{label}\"? Games using this set will start with it instead of Eden's firmware.",
                ("Keys only", () => Import(null)),
                ("Pick a firmware folder", () => App.Push(new FilePicker("Choose the set's firmware folder", pickFolder: true, fw => Import(fw), Path.GetDirectoryName(prod))))));
        }, null, ".keys"));

    private static string? SiblingTitleKeys(string prodKeys)
    {
        var t = Path.Combine(Path.GetDirectoryName(prodKeys) ?? "", "title.keys");
        return File.Exists(t) ? t : null;
    }

}
