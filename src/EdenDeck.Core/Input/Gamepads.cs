using System.Globalization;

namespace EdenDeck.Core.Input;

/// <summary>
/// A connected controller as the emulators' SDL will see it, with every way an emulator might
/// address it. Built by <see cref="GamepadScan"/> from sysfs, so it works with or without the app
/// (Steam shortcuts launch through the CLI).
/// </summary>
public sealed record Gamepad
{
    /// <summary>The name SDL gives it: for a Steam Input pad, the real controller's name from Steam.</summary>
    public required string Name { get; init; }

    /// <summary>SDL's joystick name (Steam's name for a Steam Input pad, else the kernel's); never renamed.</summary>
    public string JoystickName { get; init; } = "";

    /// <summary>SDL joystick GUID with the name CRC cleared (bus, vendor, product, version of the evdev device).</summary>
    public required string Guid { get; init; }

    /// <summary>The real controller's USB ids (for a Steam Input pad, the controller behind it).</summary>
    public int Vendor { get; init; }
    public int Product { get; init; }

    /// <summary>The evdev device's own ids (28de:11ff for every Steam Input pad).</summary>
    public int RawVendor { get; init; }
    public int RawProduct { get; init; }

    /// <summary>Steam Input's controller slot, or null for a pad that isn't a Steam Input pad.</summary>
    public int? SteamSlot { get; init; }

    /// <summary>The handheld's own controls (Steam Deck), not a controller plugged into it.</summary>
    public bool BuiltIn { get; init; }

    /// <summary>Position in SDL's joystick list (melonDS's JoystickID).</summary>
    public int JoystickIndex { get; init; }

    /// <summary>Position among SDL game controllers (PCSX2 / DuckStation "SDL-N").</summary>
    public int GamepadIndex { get; init; }

    /// <summary>Position among pads with the same GUID (Eden's "port").</summary>
    public int GuidPort { get; init; }

    /// <summary>1-based position among pads with the same name (RPCS3 numbers them "Name 1", "Name 2").</summary>
    public int NameNumber { get; init; } = 1;

    public override string ToString() =>
        $"{Name} [{Vendor:x4}:{Product:x4}{(SteamSlot is { } s ? $", Steam slot {s}" : "")}{(BuiltIn ? ", built-in" : "")}]";
}

/// <summary>
/// Finds connected controllers the way SDL does on Linux and decides who is player 1.
/// <para>
/// Order, mirroring SDL's startup scan (src/joystick/linux/SDL_sysjoystick.c): pads SDL's HIDAPI
/// drivers take (Sony, Nintendo, Valve's own) first, then Steam Input's virtual pads sorted by
/// Steam slot ("Microsoft X-Box 360 pad N"), then other evdev pads by js/event number. Devices in
/// SDL_GAMECONTROLLER_IGNORE_DEVICES (Steam lists the physical pads it handles there) are left out.
/// Steam writes the real controller behind each slot to the file named by SteamVirtualGamepadInfo,
/// which is how the Deck's built-in controls are told apart from a pad plugged into the dock.
/// </para>
/// </summary>
public static class GamepadScan
{
    private const int Valve = 0x28de, SteamVirtualGamepad = 0x11ff, SteamDeck = 0x1205;

    public sealed record Source(string SysClassInput, Func<string, string?> Env, string? DmiProductName)
    {
        public static Source System => new("/sys/class/input", Environment.GetEnvironmentVariable, ReadOrNull("/sys/class/dmi/id/product_name"));
    }

    internal sealed record Raw(int EventNumber, int InputNumber, int? JsNumber, string Name, int Bus, int Vendor, int Product, int Version);

    /// <summary>Connected controllers in SDL order. Empty when there's no sysfs (not Linux) or nothing connected.</summary>
    public static IReadOnlyList<Gamepad> Scan(Source? source = null)
    {
        source ??= Source.System;
        try
        {
            return Build(ReadDevices(source.SysClassInput), source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Players in order: controllers plugged in (or paired) take player 1, 2, ... in the order they
    /// connected; the handheld's own controls are player 1 only when nothing else is connected.
    /// </summary>
    public static IReadOnlyList<Gamepad> Players(IReadOnlyList<Gamepad> pads)
    {
        var external = pads.Where(p => !p.BuiltIn)
            .OrderBy(p => p.SteamSlot is null ? 1 : 0)
            .ThenBy(p => p.SteamSlot ?? 0)
            .ThenBy(p => p.JoystickIndex)
            .ToList();
        return external.Count > 0 ? external : pads.Where(p => p.BuiltIn).Take(1).ToList();
    }

    internal static IReadOnlyList<Gamepad> Build(IReadOnlyList<Raw> raw, Source source)
    {
        var ignore = IgnoreList(source.Env("SDL_GAMECONTROLLER_IGNORE_DEVICES"));
        var except = IgnoreList(source.Env("SDL_GAMECONTROLLER_IGNORE_DEVICES_EXCEPT"));
        bool Ignored(Raw r) => ignore.Contains((r.Vendor, r.Product)) || except.Count > 0 && !except.Contains((r.Vendor, r.Product));
        var allowVirtual = Truthy(source.Env("SDL_GAMECONTROLLER_ALLOW_STEAM_VIRTUAL_GAMEPAD"));
        var info = ReadVirtualInfo(source.Env("SteamVirtualGamepadInfo"));
        var onDeck = source.DmiProductName?.Trim() is "Jupiter" or "Galileo" || raw.Any(r => r.Vendor == Valve && r.Product == SteamDeck);

        int? Slot(Raw r) => r.Vendor == Valve && r.Product == SteamVirtualGamepad ? SlotFromName(r.Name) : null;
        static int Group(Raw r, int? slot) => slot != null ? 1 : HidapiVendor(r.Vendor, r.Product) ? 0 : 2;

        var ordered = raw.Where(r => !Ignored(r))
            .Select(r => (Raw: r, Slot: Slot(r)))
            .OrderBy(x => Group(x.Raw, x.Slot))
            .ThenBy(x => x.Slot ?? 0)
            .ThenBy(x => x.Raw.JsNumber ?? int.MaxValue)
            .ThenBy(x => x.Raw.EventNumber)
            .ToList();

        var pads = new List<Gamepad>();
        int gamepadIndex = 0;
        var guidCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var nameCount = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++)
        {
            var (r, slot) = ordered[i];
            var guid = SdlGuid(r.Bus, r.Vendor, r.Product, r.Version);
            var port = guidCount.GetValueOrDefault(guid);
            guidCount[guid] = port + 1;
            if (slot != null && !allowVirtual)
            {
                continue;  // SDL hides Steam Input pads from games Steam didn't launch
            }
            var meta = slot is { } s ? info.GetValueOrDefault(s) : null;
            var name = meta?.Name is { Length: > 0 } n ? n : r.Name;
            var vendor = meta?.Vendor ?? r.Vendor;
            var product = meta?.Product ?? r.Product;
            var builtIn = onDeck && (slot != null
                ? meta != null && vendor == Valve && product != SteamVirtualGamepad
                : r.Vendor == Valve && r.Product == SteamDeck);
            var number = nameCount.GetValueOrDefault(name) + 1;
            nameCount[name] = number;
            pads.Add(new Gamepad
            {
                Name = name,
                JoystickName = name,
                Guid = guid,
                Vendor = vendor,
                Product = product,
                RawVendor = r.Vendor,
                RawProduct = r.Product,
                SteamSlot = slot,
                BuiltIn = builtIn,
                JoystickIndex = i,
                GamepadIndex = gamepadIndex++,
                GuidPort = port,
                NameNumber = number,
            });
        }
        return pads;
    }

    /// <summary>bus, crc (cleared), vendor, 0, product, 0, version, 0 — each 16-bit little-endian, as SDL builds it for evdev.</summary>
    public static string SdlGuid(int bus, int vendor, int product, int version)
    {
        static string Le(int v) => $"{v & 0xff:x2}{(v >> 8) & 0xff:x2}";
        return $"{Le(bus)}0000{Le(vendor)}0000{Le(product)}0000{Le(version)}0000";
    }

    private static bool HidapiVendor(int vendor, int product) =>
        vendor is 0x054c or 0x057e || vendor == Valve && product != SteamVirtualGamepad;

    internal static int? SlotFromName(string name)
    {
        var at = name.IndexOf("pad ", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }
        var digits = new string(name[(at + 4)..].TakeWhile(char.IsAsciiDigit).ToArray());
        return digits.Length > 0 ? int.Parse(digits, CultureInfo.InvariantCulture) : null;
    }

    private static bool Truthy(string? v) => v != null && v.Trim() is not ("" or "0") && !v.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);

    internal static HashSet<(int, int)> IgnoreList(string? value)
    {
        var set = new HashSet<(int, int)>();
        foreach (var part in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ids = part.Split('/');
            if (ids.Length == 2 && Hex(ids[0]) is { } v && Hex(ids[1]) is { } p)
            {
                set.Add((v, p));
            }
        }
        return set;
    }

    private static int? Hex(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }
        return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private sealed record SlotInfo(string? Name, int? Vendor, int? Product);

    /// <summary>Steam's "[slot N]" file: name=, VID=, PID=, type=, handle= per slot.</summary>
    private static Dictionary<int, SlotInfo> ReadVirtualInfo(string? file)
    {
        var result = new Dictionary<int, SlotInfo>();
        if (string.IsNullOrWhiteSpace(file) || ReadOrNull(file) is not { } text)
        {
            return result;
        }
        int? slot = null;
        string? name = null;
        int? vid = null, pid = null;
        void Flush()
        {
            if (slot is { } s)
            {
                result[s] = new SlotInfo(name, vid, pid);
            }
        }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("[slot ", StringComparison.Ordinal) && line.EndsWith(']') &&
                int.TryParse(line[6..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                Flush();
                slot = n;
                name = null;
                vid = pid = null;
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            var (key, value) = (line[..eq], line[(eq + 1)..]);
            switch (key)
            {
                case "name": name = value; break;
                case "VID": vid = ParseNumber(value); break;
                case "PID": pid = ParseNumber(value); break;
            }
        }
        Flush();
        return result;
    }

    /// <summary>strtoul with base 0: 0x prefix hex, else decimal.</summary>
    private static int? ParseNumber(string s) =>
        s.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Hex(s)
        : int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static List<Raw> ReadDevices(string sysClassInput)
    {
        var list = new List<Raw>();
        if (!Directory.Exists(sysClassInput))
        {
            return list;
        }
        foreach (var eventDir in Directory.EnumerateFileSystemEntries(sysClassInput, "event*"))
        {
            if (!int.TryParse(Path.GetFileName(eventDir)[5..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var eventNumber))
            {
                continue;
            }
            var dev = Path.Combine(eventDir, "device");
            if (!Directory.Exists(dev) || !IsJoystick(dev))
            {
                continue;
            }
            var target = new DirectoryInfo(dev).ResolveLinkTarget(returnFinalTarget: true)?.Name ?? "";
            var inputNumber = target.StartsWith("input", StringComparison.Ordinal) && int.TryParse(target[5..], out var inNum) ? inNum : eventNumber;
            int? js = Directory.EnumerateDirectories(dev, "js*")
                .Select(d => int.TryParse(Path.GetFileName(d)[2..], out var j) ? j : (int?)null)
                .FirstOrDefault(j => j != null);
            int Id(string f) => Hex(ReadOrNull(Path.Combine(dev, "id", f)) ?? "") ?? 0;
            list.Add(new Raw(eventNumber, inputNumber, js, (ReadOrNull(Path.Combine(dev, "name")) ?? "").Trim(),
                Id("bustype"), Id("vendor"), Id("product"), Id("version")));
        }
        return list;
    }

    /// <summary>Like SDL's GuessDeviceClass: two absolute axes and joystick/gamepad buttons (not motion sensors or touchpads).</summary>
    private static bool IsJoystick(string dev)
    {
        var abs = Bits(ReadOrNull(Path.Combine(dev, "capabilities", "abs")));
        var key = Bits(ReadOrNull(Path.Combine(dev, "capabilities", "key")));
        bool Has(List<ulong> words, int bit) => bit / 64 < words.Count && (words[bit / 64] >> (bit % 64) & 1) != 0;
        return Has(abs, 0) && Has(abs, 1) && Enumerable.Range(0x120, 0x20).Any(b => Has(key, b));
    }

    /// <summary>A sysfs bitmap ("1f 0 3ff", most significant word first) as words, least significant first.</summary>
    private static List<ulong> Bits(string? text) =>
        (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Reverse()
            .Select(w => ulong.TryParse(w, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0UL)
            .ToList();

    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
