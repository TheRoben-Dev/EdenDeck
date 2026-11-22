using EdenDeck.Backends.Eden.SystemFiles;
using EdenDeck.Backends.Eden;
using EdenDeck.App.Ui;
using EdenDeck.Backends.Eden.Install;
using EdenDeck.Core.Backends;
using Godot;

namespace EdenDeck.App.Screens;

/// <summary>
/// Eden manager: install/update from Eden's official feed, rollback, and import of the user's
/// own keys and firmware (default and per-game sets). Nothing here downloads keys or firmware.
/// </summary>
public partial class EdenScreen : Screen
{
    private Control? _first;

    protected override void Build()
    {
        var col = Column(8);
        AddChild(Scroller(col));
        var items = new List<Control>();
        void Add(Control c)
        {
            col.AddChild(c);
            items.Add(c);
        }

        var install = Host.Eden.Install;
        col.AddChild(Section("Emulator"));
        if (install.Current is { } cur)
        {
            col.AddChild(UiTheme.Label($"Eden {Cap(cur.Channel)} {cur.Version}", 24));
            col.AddChild(UiTheme.Label((cur.ManagedByEdenDeck ? "Installed by Eden Deck · " : "Your own copy · ") + cur.ExecutablePath,
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        else
        {
            col.AddChild(UiTheme.Label("Eden isn't installed yet.", 24, UiTheme.Warn));
        }
        col.AddChild(UiTheme.Label($"Settings format: {Host.Eden.Schema.Label}", UiTheme.SmallSize + 1, UiTheme.Dim));

        foreach (var channel in install.Channels)
        {
            var ch = channel;
            Add(new ActionButton($"Install or update {Cap(ch)}", () => CheckAndInstall(ch),
                ch == "nightly"
                    ? "Newest features (frame generation, SGSR). Downloaded from Eden's official site and checksum-verified."
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
        Add(new ActionButton("Use an Eden AppImage from this device", PickExternal,
            "Point Eden Deck at an Eden AppImage you already have."));
        if (install.Current != null)
        {
            Add(new ActionButton("Open Eden", App.OpenEden,
                "Opens Eden's own window for anything Eden Deck doesn't cover yet, like controller mapping. Close it to come back."));
        }

        col.AddChild(Section("Your keys and firmware"));
        foreach (var s in Host.Eden.SystemFilesImpl.Status())
        {
            var row = UiTheme.Label($"{(s.Present ? "✓" : s.Kind == "title.keys" ? "·" : "✗")}  {s.Kind}   {s.Detail}", UiTheme.SmallSize + 2, s.Present ? UiTheme.Good : s.Kind == "title.keys" ? UiTheme.Dim : UiTheme.Warn, wrap: true);
            col.AddChild(row);
        }
        var defaultMax = EdenBackend.MaxMasterKeyOf(Path.Combine(Host.Eden.Paths.KeysDir, "prod.keys"));
        if (defaultMax >= 0)
        {
            col.AddChild(UiTheme.Label($"Your keys cover games up to firmware {KeyRequirements.FirmwareFor(defaultMax)} (master key {defaultMax:x2}).",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        Add(new ActionButton("Import prod.keys", ImportKeys,
            "Copies your own prod.keys (and title.keys from the same folder, if present) into Eden."));
        Add(new ActionButton("Import firmware folder", ImportFirmware,
            "Copies your own firmware .nca files into Eden. The current firmware is backed up first."));

        col.AddChild(Section("Key sets for specific games"));
        col.AddChild(UiTheme.Label("Some games need older or newer keys. Add a set here, then choose it on the game's page.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
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

        Focus.Column(items);
        Anim.Stagger(items, 0.015);
        _first = items.FirstOrDefault();
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

    public override void FocusDefault() => _first?.GrabFocus();
}
