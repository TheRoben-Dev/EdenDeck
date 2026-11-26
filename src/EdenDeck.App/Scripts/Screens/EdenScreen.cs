using EdenDeck.Backends.Rpcs3;
using EdenDeck.Backends.Duckstation;
using EdenDeck.Backends.Pcsx2;
using EdenDeck.Backends.Mgba;
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
    protected override string Heading => "Emulators";

    protected override IReadOnlyList<(string Name, string Summary)> Categories
    {
        get
        {
            var cur = Host.Eden.Install.Current;
            var sets = Host.Eden.KeySets.List().Count;
            return
            [
                ("Eden (Switch)", cur != null ? $"Eden {Cap(cur.Channel)} {cur.Version}: install, update, roll back or open Eden." : "Eden isn't installed yet: install it here."),
                ("Keys and firmware", "Your own prod.keys and firmware, used by every game."),
                ("Key sets", sets == 0 ? "Older or newer keys for specific games." : $"{sets} key set{(sets == 1 ? "" : "s")} for specific games."),
                ("Frame generation", "Lossless Scaling frame generation (lsfg-vk): what's found and what's missing."),
                ("mGBA (Game Boy Advance)", Host.Mgba.Install.Current is { } m
                    ? $"mGBA {m.Version}: Game Boy Advance, Game Boy and Game Boy Color. Install, BIOS and settings."
                    : "mGBA isn't installed yet: install it here to play Game Boy Advance games."),
                ("RPCS3 (PlayStation 3)", Host.Rpcs3.Install.Current is { } r
                    ? $"RPCS3 {r.Version}: install, update, your PS3 system software and settings."
                    : "RPCS3 isn't installed yet: install it here to play PlayStation 3 games."),
                ("PCSX2 (PlayStation 2)", Host.Pcsx2.Install.Current is { } p
                    ? $"PCSX2 {p.Version}: install, update, your PS2 BIOS and settings."
                    : "PCSX2 isn't installed yet: install it here to play PlayStation 2 games."),
                ("DuckStation (PlayStation)", Host.Duckstation.Install.Current is { } d
                    ? $"DuckStation {d.Version}: install, update, your PS1 BIOS and settings."
                    : "DuckStation isn't installed yet: install it here to play PlayStation games."),
                ("Advanced", "Another Eden AppImage, settings format and where Eden keeps its files."),
            ];
        }
    }

    /// <summary>A dot on categories that need attention.</summary>
    protected override string? Badge(string name) => name switch
    {
        "Eden (Switch)" when Host.Eden.Install.Current == null || App.EdenUpdate != null => "•",
        "Keys and firmware" when Host.Eden.SystemFilesImpl.Status().Any(s => !s.Present && s.Kind != "title.keys") => "•",
        "RPCS3 (PlayStation 3)" when Host.Rpcs3.Install.Current != null && !Host.Rpcs3.FirmwareInstalled => "•",
        "PCSX2 (PlayStation 2)" when Host.Pcsx2.Install.Current != null && !Host.Pcsx2.BiosInstalled => "•",
        "DuckStation (PlayStation)" when Host.Duckstation.Install.Current != null && !Host.Duckstation.BiosInstalled => "•",
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
            case "Eden (Switch)":
                BuildEmulator(col, Add);
                break;
            case "mGBA (Game Boy Advance)":
                BuildMgba(col, Add);
                break;
            case "RPCS3 (PlayStation 3)":
                BuildRpcs3(col, Add);
                break;
            case "PCSX2 (PlayStation 2)":
                BuildPcsx2(col, Add);
                break;
            case "DuckStation (PlayStation)":
                BuildDuckstation(col, Add);
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
            if (App.EdenUpdate != null)
            {
                col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
            }
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

    private void BuildMgba(VBoxContainer col, Action<Control> Add)
    {
        var mgba = Host.Mgba;
        var install = mgba.Installs;
        if (install.Current is { } cur)
        {
            var flatpak = cur.ExecutablePath == MgbaInstallManager.FlatpakExe;
            col.AddChild(UiTheme.Label(flatpak ? "mGBA (Flatpak)" : $"mGBA {cur.Version}", 24));
            col.AddChild(Note(flatpak ? "Your Flatpak install · updates come through Discover or flatpak update."
                : (cur.ManagedByEdenDeck ? "Installed by Scanline · " : "Your own copy · ") + cur.ExecutablePath));
        }
        else
        {
            col.AddChild(UiTheme.Label("mGBA isn't installed yet.", 24, UiTheme.Warn));
            col.AddChild(Note("Install it here, or install mGBA from Discover (Flatpak) and Scanline finds it."));
        }
        Add(new ActionButton(install.Current is { ManagedByEdenDeck: true } ? "Update mGBA" : "Install mGBA", CheckAndInstallMgba,
            "Downloads the official AppImage from mGBA's GitHub releases, checksum-verified."));
        if (install.Current is { ManagedByEdenDeck: true } && install.FlatpakInstalled())
        {
            Add(new ActionButton("Use the Flatpak instead", () => App.Guard(() =>
            {
                install.UseExternal(install.DetectExternal().First(e => e.ExecutablePath == MgbaInstallManager.FlatpakExe));
                Rebuild();
            }), "Switches to the mGBA you installed from Discover."));
        }
        if (install.Previous is { } prev)
        {
            Add(new ActionButton($"Roll back to {prev.Version}", () => App.Guard(() =>
            {
                install.Rollback();
                Rebuild();
            }), "Switches back to the mGBA you used before."));
        }

        col.AddChild(Section("BIOS"));
        foreach (var st in mgba.Status())
        {
            col.AddChild(UiTheme.Label($"{(st.Present ? "✓" : "·")}  {st.Detail}", UiTheme.SmallSize + 2, st.Present ? UiTheme.Good : UiTheme.Dim, wrap: true));
        }
        Add(new ActionButton(mgba.BiosPath == null ? "Use your own GBA BIOS" : "Choose another BIOS file", () =>
            App.Push(new FilePicker("Choose your GBA BIOS (gba_bios.bin)", pickFolder: false, path => App.Guard(() =>
            {
                mgba.SetBios(path);
                App.Success("mGBA will use your BIOS.");
                Rebuild();
            }), null, ".bin")), "Optional: points mGBA at your own BIOS file. Nothing is copied or downloaded."));
        if (mgba.BiosPath != null)
        {
            Add(new ActionButton("Stop using the BIOS file", () => App.Guard(() =>
            {
                mgba.SetBios(null);
                Rebuild();
            }), "Back to mGBA's built-in BIOS replacement."));
        }

        col.AddChild(Section("Settings for all GBA games"));
        col.AddChild(Note("Games can override these on their own card. Saved straight to mGBA's config."));
        var settings = mgba.Settings;
        var global = Try(() => settings.ReadGlobal(), new Dictionary<string, string>(), "mGBA's settings", col);
        foreach (var o in settings.Options)
        {
            var opt = o;
            Add(OptionRows.Global(o, global.GetValueOrDefault(o.Id), v =>
                App.Guard(() => settings.WriteGlobal(new Dictionary<string, string?> { [opt.Id] = v }))));
        }

        col.AddChild(Section("Handheld frame"));
        Add(Toggle.Create("Frame around the picture", Host.Settings.HandheldFrame, on => Host.SaveSettings(Host.Settings with { HandheldFrame = on }),
            "Fills the black bars beside GBA, Game Boy and Game Boy Color games.",
            "A handheld-style shell and screen bezel in your accent colour, drawn by mGBA. It's decoration only: nothing in it is a button. Games can turn it off on their own card.",
            "Plain black bars, the way mGBA draws them."));

        col.AddChild(Section("Controls"));
        col.AddChild(Note("Scanline sets up your controller in mGBA before each game: A on the right face button, B on the bottom, L/R on the bumpers, Select/Start on View/Menu. If you change buttons in mGBA's own settings, Scanline keeps yours."));
        col.AddChild(Note(MgbaNames.Credit));
    }

    private void CheckAndInstallMgba()
    {
        var install = Host.Mgba.Install;
        App.Busy("Checking mGBA…", (_, ct) => install.CheckForUpdateAsync("stable", ct), release =>
        {
            if (release == null)
            {
                App.Toast(install.Current is { ManagedByEdenDeck: true } ? "Already up to date." : "No Linux build in mGBA's releases right now.");
                return;
            }
            var size = release.Size > 0 ? $" ({release.Size / 1048576} MB)" : "";
            App.Confirm($"Install mGBA {release.Version}{size}?", "Install", () =>
                App.Busy($"Downloading mGBA {release.Version}…", (p, ct) => install.InstallAsync(release, p, ct), installed =>
                {
                    App.Success($"mGBA {installed.Version} installed{(release.Sha256 != null ? ", checksum verified" : "")}.");
                    Rebuild();
                }));
        });
    }

    private void BuildRpcs3(VBoxContainer col, Action<Control> Add)
    {
        var rpcs3 = Host.Rpcs3;
        var install = rpcs3.Installs;
        if (install.Current is { } cur)
        {
            col.AddChild(UiTheme.Label(rpcs3.IsFlatpak ? "RPCS3 (Flatpak)" : $"RPCS3 {cur.Version}", 24));
            col.AddChild(Note(rpcs3.IsFlatpak ? "Your Flatpak install · updates come through Discover or flatpak update."
                : (cur.ManagedByEdenDeck ? "Installed by Scanline · " : "Your own copy · ") + cur.ExecutablePath));
            if (rpcs3.IsFlatpak)
            {
                col.AddChild(UiTheme.Label("Frame generation and the CRT effect can't reach into the Flatpak's sandbox. Install the AppImage to use them.",
                    UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
            }
        }
        else
        {
            col.AddChild(UiTheme.Label("RPCS3 isn't installed yet.", 24, UiTheme.Warn));
            col.AddChild(Note("Install it here (recommended: frame generation and the CRT effect work with it), or install RPCS3 from Discover (Flatpak) and Scanline finds it."));
        }
        Add(new ActionButton(install.Current is { ManagedByEdenDeck: true } ? "Update RPCS3" : "Install RPCS3", CheckAndInstallRpcs3,
            "Downloads the official RPCS3 AppImage (the build RPCS3's own updater offers), checksum-verified."));
        if (install.Current is { ManagedByEdenDeck: true } && install.FlatpakInstalled())
        {
            Add(new ActionButton("Use the Flatpak instead", () => App.Guard(() =>
            {
                install.UseExternal(install.DetectExternal().First(e => e.ExecutablePath == Rpcs3InstallManager.FlatpakExe));
                Rebuild();
            }), "Switches to the RPCS3 you installed from Discover (no frame generation or CRT effect)."));
        }
        if (install.Previous is { } prev)
        {
            Add(new ActionButton($"Roll back to {prev.Version}", () => App.Guard(() =>
            {
                install.Rollback();
                Rebuild();
            }), "Switches back to the RPCS3 you used before."));
        }

        col.AddChild(Section("PS3 system software"));
        foreach (var st in rpcs3.Status())
        {
            col.AddChild(UiTheme.Label($"{(st.Present ? "✓" : "✗")}  {st.Detail}", UiTheme.SmallSize + 2, st.Present ? UiTheme.Good : UiTheme.Warn, wrap: true));
        }
        if (install.Current != null)
        {
            Add(new ActionButton(rpcs3.FirmwareInstalled ? "Reinstall system software" : "Install your system software", () =>
                App.Push(new FilePicker("Choose your PS3UPDAT.PUP", pickFolder: false, path => App.InstallPs3Firmware(path), null, ".pup")),
                "Pick your own PS3UPDAT.PUP; RPCS3 installs it. Scanline never downloads it."));
            Add(new ActionButton("Open RPCS3", App.OpenRpcs3,
                "Opens RPCS3's own window: set up controllers under Pads, or anything else Scanline doesn't cover. Close it to come back."));
        }
        col.AddChild(Note("Games: add the folder with your PS3 game folders or .iso files under Settings → Library. Games installed in RPCS3 (PSN) are found by themselves."));

        col.AddChild(Section("Settings for all PS3 games"));
        col.AddChild(Note("Games can override these on their own card. Saved straight to RPCS3's config."));
        var settings = rpcs3.Settings;
        var global = Try(() => settings.ReadGlobal(), new Dictionary<string, string>(), "RPCS3's settings", col);
        foreach (var o in settings.Options)
        {
            var opt = o;
            Add(OptionRows.Global(o, global.GetValueOrDefault(o.Id), v =>
                App.Guard(() => settings.WriteGlobal(new Dictionary<string, string?> { [opt.Id] = v }))));
        }
    }

    private void CheckAndInstallRpcs3()
    {
        var install = Host.Rpcs3.Install;
        App.Busy("Checking RPCS3…", async (_, ct) =>
        {
            try
            {
                return await install.CheckForUpdateAsync("stable", ct);
            }
            catch (System.Text.Json.JsonException e)
            {
                throw new InvalidDataException("RPCS3's update service sent something Scanline can't read: " + e.Message, e);
            }
        }, release =>
        {
            if (release == null)
            {
                App.Toast(install.Current is { ManagedByEdenDeck: true } ? "Already up to date." : "RPCS3 has no Linux build for this computer right now.");
                return;
            }
            var size = release.Size > 0 ? $" ({release.Size / 1048576} MB)" : "";
            App.Confirm($"Install RPCS3 {release.Version}{size}?", "Install", () =>
                App.Busy($"Downloading RPCS3 {release.Version}…", (p, ct) => install.InstallAsync(release, p, ct), installed =>
                {
                    App.Success($"RPCS3 {installed.Version} installed{(release.Sha256 != null ? ", checksum verified" : "")}.");
                    Rebuild();
                }));
        });
    }

    private void BuildPcsx2(VBoxContainer col, Action<Control> Add)
    {
        var pcsx2 = Host.Pcsx2;
        var install = pcsx2.Installs;
        if (install.Current is { } cur)
        {
            col.AddChild(UiTheme.Label(pcsx2.IsFlatpak ? "PCSX2 (Flatpak)" : $"PCSX2 {cur.Version}", 24));
            col.AddChild(Note(pcsx2.IsFlatpak ? "Your Flatpak install · updates come through Discover or flatpak update."
                : (cur.ManagedByEdenDeck ? "Installed by Scanline · " : "Your own copy · ") + cur.ExecutablePath));
            if (pcsx2.IsFlatpak)
            {
                col.AddChild(UiTheme.Label("Frame generation and the CRT effect can't reach into the Flatpak's sandbox. Install the AppImage to use them.",
                    UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
            }
        }
        else
        {
            col.AddChild(UiTheme.Label("PCSX2 isn't installed yet.", 24, UiTheme.Warn));
            col.AddChild(Note("Install it here (recommended: frame generation and the CRT effect work with it), or install PCSX2 from Discover (Flatpak) and Scanline finds it."));
        }
        Add(new ActionButton(install.Current is { ManagedByEdenDeck: true } ? "Update PCSX2" : "Install PCSX2", CheckAndInstallPcsx2,
            "Downloads the official PCSX2 AppImage (the latest stable release on PCSX2's GitHub), checksum-verified."));
        if (install.Current is { ManagedByEdenDeck: true } && install.FlatpakInstalled())
        {
            Add(new ActionButton("Use the Flatpak instead", () => App.Guard(() =>
            {
                install.UseExternal(install.DetectExternal().First(e => e.ExecutablePath == Pcsx2InstallManager.FlatpakExe));
                Rebuild();
            }), "Switches to the PCSX2 you installed from Discover (no frame generation or CRT effect)."));
        }
        if (install.Previous is { } prev)
        {
            Add(new ActionButton($"Roll back to {prev.Version}", () => App.Guard(() =>
            {
                install.Rollback();
                Rebuild();
            }), "Switches back to the PCSX2 you used before."));
        }

        col.AddChild(Section("PS2 BIOS"));
        foreach (var st in pcsx2.Status())
        {
            col.AddChild(UiTheme.Label($"{(st.Present ? "✓" : "✗")}  {st.Detail}", UiTheme.SmallSize + 2, st.Present ? UiTheme.Good : UiTheme.Warn, wrap: true));
        }
        Add(new ActionButton(pcsx2.BiosInstalled ? "Choose another BIOS file" : "Use your own PS2 BIOS", () =>
            App.Push(new FilePicker("Choose your PS2 BIOS (such as SCPH-70012.bin)", pickFolder: false, path => App.Guard(() =>
            {
                var name = pcsx2.ImportBios(path);
                App.Success($"PCSX2 will use {name}.");
                Rebuild();
            }), null, ".bin")), "Copies the BIOS you dumped from your own PS2 into PCSX2 and selects it. Scanline never downloads it."));
        if (install.Current != null)
        {
            Add(new ActionButton("Open PCSX2", App.OpenPcsx2,
                "Opens PCSX2's own window: map controllers under Settings → Controllers, or anything else Scanline doesn't cover. Close it to come back."));
        }
        col.AddChild(Note("Games: add the folder with your PS2 discs (.iso, .chd, .cso, .zso or .cue) under Settings → Library."));

        col.AddChild(Section("Settings for all PS2 games"));
        col.AddChild(Note("Games can override these on their own card. Saved straight to PCSX2's config."));
        var settings = pcsx2.Settings;
        var global = Try(() => settings.ReadGlobal(), new Dictionary<string, string>(), "PCSX2's settings", col);
        foreach (var o in settings.Options)
        {
            var opt = o;
            Add(OptionRows.Global(o, global.GetValueOrDefault(o.Id), v =>
                App.Guard(() => settings.WriteGlobal(new Dictionary<string, string?> { [opt.Id] = v }))));
        }

        col.AddChild(Section("Controls"));
        col.AddChild(Note("Scanline sets up your controller in PCSX2 before each game, like a PS2 pad: Cross on the bottom face button, Circle on the right, L1/R1 on the bumpers, L2/R2 on the triggers, Select/Start on View/Menu, with rumble. If you map buttons in PCSX2 yourself, Scanline keeps yours."));
        col.AddChild(Note(Ps2Names.Credit));
    }

    private void CheckAndInstallPcsx2()
    {
        var install = Host.Pcsx2.Install;
        App.Busy("Checking PCSX2…", (_, ct) => install.CheckForUpdateAsync("stable", ct), release =>
        {
            if (release == null)
            {
                App.Toast(install.Current is { ManagedByEdenDeck: true } ? "Already up to date." : "No Linux build in PCSX2's releases right now.");
                return;
            }
            var size = release.Size > 0 ? $" ({release.Size / 1048576} MB)" : "";
            App.Confirm($"Install PCSX2 {release.Version}{size}?", "Install", () =>
                App.Busy($"Downloading PCSX2 {release.Version}…", (p, ct) => install.InstallAsync(release, p, ct), installed =>
                {
                    App.Success($"PCSX2 {installed.Version} installed{(release.Sha256 != null ? ", checksum verified" : "")}.");
                    Rebuild();
                }));
        });
    }

    private void BuildDuckstation(VBoxContainer col, Action<Control> Add)
    {
        var ds = Host.Duckstation;
        var install = ds.Installs;
        if (install.Current is { } cur)
        {
            col.AddChild(UiTheme.Label(ds.IsFlatpak ? "DuckStation (Flatpak)" : $"DuckStation {cur.Version}", 24));
            col.AddChild(Note(ds.IsFlatpak ? "Your Flatpak install · DuckStation no longer updates its Flatpak; install the AppImage here."
                : (cur.ManagedByEdenDeck ? "Installed by Scanline · " : "Your own copy · ") + cur.ExecutablePath));
            if (ds.IsFlatpak)
            {
                col.AddChild(UiTheme.Label("Frame generation and the CRT effect can't reach into the Flatpak's sandbox. Install the AppImage to use them.",
                    UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
            }
        }
        else
        {
            col.AddChild(UiTheme.Label("DuckStation isn't installed yet.", 24, UiTheme.Warn));
            col.AddChild(Note("Install it here (recommended: frame generation and the CRT effect work with it). A DuckStation AppImage you already have is found too."));
        }
        Add(new ActionButton(install.Current is { ManagedByEdenDeck: true } ? "Update DuckStation" : "Install DuckStation", CheckAndInstallDuckstation,
            "Downloads the official DuckStation AppImage, unchanged, from DuckStation's own GitHub release (its stable build)."));
        if (install.Current is { ManagedByEdenDeck: true } && install.FlatpakInstalled())
        {
            Add(new ActionButton("Use the Flatpak instead", () => App.Guard(() =>
            {
                install.UseExternal(install.DetectExternal().First(e => e.ExecutablePath == DuckstationInstallManager.FlatpakExe));
                Rebuild();
            }), "Switches to your DuckStation Flatpak (no frame generation or CRT effect)."));
        }
        if (install.Previous is { } prev)
        {
            Add(new ActionButton($"Roll back to {prev.Version}", () => App.Guard(() =>
            {
                install.Rollback();
                Rebuild();
            }), "Switches back to the DuckStation you used before."));
        }

        col.AddChild(Section("PS1 BIOS"));
        foreach (var st in ds.Status())
        {
            col.AddChild(UiTheme.Label($"{(st.Present ? "✓" : "✗")}  {st.Detail}", UiTheme.SmallSize + 2, st.Present ? UiTheme.Good : UiTheme.Warn, wrap: true));
        }
        Add(new ActionButton(ds.BiosInstalled ? "Add another BIOS file" : "Use your own PS1 BIOS", () =>
            App.Push(new FilePicker("Choose your PS1 BIOS (such as SCPH-1001.bin)", pickFolder: false, path => App.Guard(() =>
            {
                var name = ds.ImportBios(path);
                App.Success($"{name} added. DuckStation picks the BIOS matching each game's region.");
                Rebuild();
            }), null, ".bin")), "Copies the BIOS you dumped from your own PS1 (or PS2) into DuckStation. Add one per region (US, Europe, Japan) for best results. Scanline never downloads it."));
        if (install.Current != null)
        {
            Add(new ActionButton("Open DuckStation", App.OpenDuckstation,
                "Opens DuckStation's own window: map controllers under Settings → Controllers, or anything else Scanline doesn't cover. Close it to come back."));
        }
        col.AddChild(Note("Games: add the folder with your PS1 discs (.cue/.bin, .chd, .iso or .pbp) under Settings → Library. A .chd is recognised by its Redump name or a folder called psx or ps1."));

        col.AddChild(Section("Settings for all PS1 games"));
        col.AddChild(Note("Games can override these on their own card. Saved straight to DuckStation's config."));
        var settings = ds.Settings;
        var global = Try(() => settings.ReadGlobal(), new Dictionary<string, string>(), "DuckStation's settings", col);
        foreach (var o in settings.Options)
        {
            var opt = o;
            Add(OptionRows.Global(o, global.GetValueOrDefault(o.Id), v =>
                App.Guard(() => settings.WriteGlobal(new Dictionary<string, string?> { [opt.Id] = v }))));
        }

        col.AddChild(Section("Controls"));
        col.AddChild(Note("Scanline sets up your controller in DuckStation before each game, like a DualShock: Cross on the bottom face button, Circle on the right, L1/R1 on the bumpers, L2/R2 on the triggers, Select/Start on View/Menu, both sticks and rumble. If you map buttons in DuckStation yourself, Scanline keeps yours."));
        col.AddChild(Note(Ps1Names.Credit));
    }

    private void CheckAndInstallDuckstation()
    {
        var install = Host.Duckstation.Install;
        App.Busy("Checking DuckStation…", (_, ct) => install.CheckForUpdateAsync("stable", ct), release =>
        {
            if (release == null)
            {
                App.Toast(install.Current is { ManagedByEdenDeck: true } ? "Already up to date." : "No Linux build in DuckStation's releases right now.");
                return;
            }
            var size = release.Size > 0 ? $" ({release.Size / 1048576} MB)" : "";
            App.Confirm($"Install DuckStation {release.Version}{size}?", "Install", () =>
                App.Busy($"Downloading DuckStation {release.Version}…", (p, ct) => install.InstallAsync(release, p, ct), installed =>
                {
                    App.Success($"DuckStation {installed.Version} installed{(release.Sha256 != null ? ", checksum verified" : "")}.");
                    Rebuild();
                }));
        });
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
        col.AddChild(Note("Eden's own frame generation only exists on Android, so Scanline uses lsfg-vk (included, or your own install if you have one) with your copy of Lossless Scaling. " +
                          "It doubles 30 fps games to 60 and never goes above 60. Turn it on per game on the game's page."));
        var lsfg = Host.Eden.FindLsfgVk();
        var (lsfgText, lsfgColor) = lsfg.Kind switch
        {
            LsfgVk.Kind.V2 when lsfg.Bundled => ("✓  lsfg-vk   included with Scanline", UiTheme.Good),
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
            col.AddChild(Note("lsfg-vk 2.x isn't installed, and this copy of Scanline doesn't include it. Update Scanline, or install lsfg-vk 2.x yourself."));
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
