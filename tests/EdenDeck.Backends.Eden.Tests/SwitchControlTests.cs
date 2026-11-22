using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EdenDeck.Backends.Eden.Library;
using Xunit;

namespace EdenDeck.Backends.Eden.Tests;

public class SwitchControlTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("scanline-control-").FullName;
    private static readonly byte[] Kak = RandomNumberGenerator.GetBytes(16);
    private static readonly byte[] Kek = RandomNumberGenerator.GetBytes(16);
    private static readonly byte[] ContentKey = RandomNumberGenerator.GetBytes(16);
    private static readonly byte[] Icon = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 0xFF, 0xD9];

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Dictionary<string, byte[]> Keys => new()
    {
        ["header_key"] = FakeNsp.HeaderKey,
        ["key_area_key_application_02"] = Kak,
        ["titlekek_02"] = Kek,
    };

    private static byte[] EcbEncrypt(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptEcb(data, PaddingMode.None);
    }

    /// <summary>A RomFS with control.nacp and an icon, laid out the way the console's tools write it.</summary>
    private static byte[] RomFs(string name)
    {
        var nacp = new byte[0x4000];
        Encoding.UTF8.GetBytes(name).CopyTo(nacp, 0);
        (string Name, byte[] Data)[] files = [("control.nacp", nacp), ("icon_AmericanEnglish.dat", Icon)];
        var meta = new MemoryStream();
        var data = new MemoryStream();
        foreach (var (fname, bytes) in files)
        {
            var n = Encoding.UTF8.GetBytes(fname);
            var e = new byte[0x20 + ((n.Length + 3) & ~3)];
            BinaryPrimitives.WriteInt64LittleEndian(e.AsSpan(8), data.Length);
            BinaryPrimitives.WriteInt64LittleEndian(e.AsSpan(16), bytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(28), n.Length);
            n.CopyTo(e, 0x20);
            meta.Write(e);
            data.Write(bytes);
            while (data.Length % 16 != 0)
            {
                data.WriteByte(0);
            }
        }
        var header = new byte[0x50];
        BinaryPrimitives.WriteInt64LittleEndian(header, 0x50);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(0x38), 0x50);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(0x40), meta.Length);
        var dataOff = (0x50 + meta.Length + 15) & ~15;
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(0x48), dataOff);
        var all = new byte[(dataOff + data.Length + 0x1FF) & ~0x1FF];
        header.CopyTo(all, 0);
        meta.ToArray().CopyTo(all, 0x50);
        data.ToArray().CopyTo(all, dataOff);
        return all;
    }

    private static byte[] ControlNca(string name, byte[]? rightsId)
    {
        const long sectionStart = 0xC00;
        const long romfsAt = 0x200;  // IVFC data level offset inside the section
        var romfs = RomFs(name);
        var section = new byte[romfsAt + romfs.Length];
        romfs.CopyTo(section, romfsAt);
        var h = new byte[0xC00];
        Encoding.ASCII.GetBytes("NCA3").CopyTo(h, 0x200);
        h[0x205] = 2;  // control
        h[0x220] = 3;  // key generation 3 -> master key 02
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x240), (uint)(sectionStart / 0x200));
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x244), (uint)((sectionStart + section.Length) / 0x200));
        if (rightsId != null)
        {
            rightsId.CopyTo(h, 0x230);
        }
        else
        {
            EcbEncrypt(Kak, ContentKey).CopyTo(h, 0x300 + 32);
        }
        var fsh = h.AsSpan(0x400, 0x200);
        fsh[2] = 0;
        fsh[3] = 3;
        fsh[4] = 3;
        Encoding.ASCII.GetBytes("IVFC").CopyTo(fsh[8..]);
        BinaryPrimitives.WriteUInt32LittleEndian(fsh[0x14..], 7);
        BinaryPrimitives.WriteInt64LittleEndian(fsh[(0x18 + 5 * 0x18)..], romfsAt);
        var ctr = RandomNumberGenerator.GetBytes(8);
        ctr.CopyTo(fsh[0x140..]);
        var ctrHigh = ctr.Reverse().ToArray();
        var enc = SwitchControl.Transform(section, ContentKey, ctrHigh, sectionStart);
        return [.. NcaHeaderReader.XtsEncrypt(h, FakeNsp.HeaderKey, 0x200), .. enc];
    }

    private static byte[] Ticket(byte[] rightsId, byte[] encTitleKey)
    {
        var t = new byte[0x2C0];
        BinaryPrimitives.WriteUInt32LittleEndian(t, 0x10004);  // RSA-2048 SHA-256
        encTitleKey.CopyTo(t, 0x140 + 0x40);
        rightsId.CopyTo(t, 0x140 + 0x160);
        return t;
    }

    [Fact]
    public void ReadsIconAndNameThroughTheKeyArea()
    {
        var nsp = Path.Combine(_dir, "g.nsp");
        FakeNsp.Write(nsp, ("meta.cnmt.nca", FakeNsp.NcaHeader(1, 0x0100000000010000)), ("control.nca", ControlNca("Mountain Climb", null)));
        var c = SwitchControl.Read(nsp, Keys);
        Assert.NotNull(c);
        Assert.Equal(Icon, c.IconJpeg);
        Assert.Equal("Mountain Climb", c.Name);
    }

    [Fact]
    public void ReadsTitleKeyNcasWithTheTicketOrTitleKeys()
    {
        var rid = RandomNumberGenerator.GetBytes(16);
        var encKey = EcbEncrypt(Kek, ContentKey);
        var withTicket = Path.Combine(_dir, "t.nsp");
        FakeNsp.Write(withTicket, ("control.nca", ControlNca("Sky Racers", rid)), ("x.tik", Ticket(rid, encKey)));
        Assert.Equal("Sky Racers", SwitchControl.Read(withTicket, Keys)?.Name);

        var noTicket = Path.Combine(_dir, "n.nsp");
        FakeNsp.Write(noTicket, ("control.nca", ControlNca("Sky Racers", rid)));
        Assert.Null(SwitchControl.Read(noTicket, Keys));
        var titleKeys = new Dictionary<string, byte[]> { [Convert.ToHexString(rid).ToLowerInvariant()] = encKey };
        Assert.Equal(Icon, SwitchControl.Read(noTicket, Keys, titleKeys)?.IconJpeg);
    }

    [Fact]
    public void WithoutTheRightKeysThereIsNoIcon()
    {
        var nsp = Path.Combine(_dir, "g.nsp");
        FakeNsp.Write(nsp, ("control.nca", ControlNca("X", null)));
        Assert.Null(SwitchControl.Read(nsp, new Dictionary<string, byte[]> { ["header_key"] = FakeNsp.HeaderKey }));
        Assert.Null(SwitchControl.Read(nsp, new Dictionary<string, byte[]>()));
    }
}
