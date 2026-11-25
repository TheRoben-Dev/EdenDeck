using EdenDeck.App.Ui;
using EdenDeck.App.Ui.Poly;
using Godot;

namespace EdenDeck.App;

/// <summary>
/// Steam artwork in the 32-bit 3D theme's style, rendered offscreen from the theme's real scene
/// (fogged grid floor, era props, a 3D cartridge with the game's own label, the title in the
/// theme's lettering): portrait capsule, wide capsule, hero, logo and icon, the files Steam shows
/// for a shortcut. Scanline's own set is rendered once with <c>-- --render-steam-art &lt;dir&gt;</c> and
/// shipped (tools/release/steam-art); each game's set is rendered on the player's machine when the
/// game is added to Steam. Nothing is downloaded.
/// </summary>
public static class SteamArt
{
    private sealed record Shot(string File, Vector2I Size, bool Backdrop, Rect2[] Carts, Rect2? Title, float TitleScale, bool Transparent = false, Vector2I? Output = null);

    // Sizes Steam asks for: capsule 600x900, wide capsule 920x430, hero 3840x1240, logo up to 1280x720.
    private static readonly Shot[] Shots =
    [
        new("capsule.png", new(600, 900), true, [new Rect2(90, 90, 420, 520)], new Rect2(30, 630, 540, 240), 1f),
        new("wide.png", new(920, 430), true, [new Rect2(40, 35, 330, 360)], new Rect2(370, 60, 520, 310), 1f),
        // Hero: no title (Steam lays the logo over it, bottom left); carts on the right.
        new("hero.png", new(3840, 1240), true,
            [new Rect2(1900, 330, 560, 560), new Rect2(3260, 260, 520, 520), new Rect2(2420, 110, 1000, 1000)], null, 0),
        new("logo.png", new(1280, 720), false, [], new Rect2(40, 110, 1200, 500), 2.2f, Transparent: true),
        // Icon: rendered large and scaled down so the cart label stays readable.
        new("icon.png", new(1024, 1024), false, [new Rect2(72, 32, 880, 960)], null, 0, Output: new(256, 256)),
    ];

    /// <summary>The theme's colours lifted a little: Steam shows art small and on dark UI, where the app's near-black would read as empty.</summary>
    private static ThemeSpec Art(ThemeSpec polygon) => polygon with
    {
        Bg = new("222a78"), Panel = new("3a42a8"), Line = new("c0c6ff"), Info = new("7fd8ff"),
    };

    /// <summary>Renders the set for <paramref name="title"/> into <paramref name="dir"/> (capsule.png, wide.png, hero.png, logo.png, icon.png).</summary>
    /// <summary>Bump when the look changes: sets rendered by an older version are drawn again.</summary>
    public const int Version = 4;

    public static async Task Render(Node host, string title, string dir, string? iconPath = null, int settleFrames = 12)
    {
        Directory.CreateDirectory(dir);
        Image? icon = null;
        if (iconPath != null && File.Exists(iconPath))
        {
            icon = new Image();
            if (icon.LoadJpgFromBuffer(File.ReadAllBytes(iconPath)) != Error.Ok && icon.LoadPngFromBuffer(File.ReadAllBytes(iconPath)) != Error.Ok)
            {
                icon = null;
            }
        }
        foreach (var shot in Shots)
        {
            var view = UiTheme.With("polygon", () => Build(shot, title, icon));
            host.AddChild(view);
            // Let the low-res stages size themselves and the carts settle into their pose.
            for (var i = 0; i < settleFrames; i++)
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var img = view.GetTexture().GetImage();
            view.QueueFree();
            if (shot.Output is { } o)
            {
                img.Resize(o.X, o.Y, Image.Interpolation.Lanczos);
            }
            var path = Path.Combine(dir, shot.File);
            var tmp = path + ".tmp.png";
            img.SavePng(tmp);
            File.Move(tmp, path, overwrite: true);
        }
        File.WriteAllText(Path.Combine(dir, "version"), Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>True when <paramref name="dir"/> holds a set drawn by this version.</summary>
    public static bool IsCurrent(string dir) =>
        File.Exists(Path.Combine(dir, "capsule.png")) && File.Exists(Path.Combine(dir, "version")) &&
        File.ReadAllText(Path.Combine(dir, "version")).Trim() == Version.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Title text size for a box: long names get smaller lettering and wrap.</summary>
    private static int TitleSize(string title, float scale)
    {
        var n = title.Length;
        var size = n <= 8 ? 92 : n <= 14 ? 72 : n <= 24 ? 58 : n <= 40 ? 46 : 38;
        return (int)(size * scale);
    }

    private static SubViewport Build(Shot s, string title, Image? icon)
    {
        var view = new SubViewport
        {
            Size = s.Size,
            TransparentBg = s.Transparent,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        var root = new Control { Size = s.Size };
        view.AddChild(root);
        if (s.Backdrop)
        {
            var bg = new PolyBackdrop(Art(UiTheme.Spec), showcase: true);
            root.AddChild(bg);
            bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            bg.Size = s.Size;
        }
        else if (!s.Transparent)
        {
            // Icon: the theme's night-blue tile behind the cart.
            var tile = new Panel { Size = s.Size };
            tile.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = UiTheme.Spec.Panel,
                CornerRadiusTopLeft = 160, CornerRadiusTopRight = 160, CornerRadiusBottomLeft = 160, CornerRadiusBottomRight = 160,
                BorderColor = UiTheme.Accent, BorderWidthBottom = 24, BorderWidthTop = 24, BorderWidthLeft = 24, BorderWidthRight = 24,
            });
            root.AddChild(tile);
        }
        foreach (var cart in s.Carts)
        {
            root.AddChild(new CartStage(title, UiTheme.CurrentFont, icon) { Position = cart.Position, Size = cart.Size });
        }
        if (s.Title is { } box)
        {
            var size = TitleSize(title, s.TitleScale);
            root.AddChild(new Label
            {
                Text = title.ToUpperInvariant(),
                Position = box.Position,
                Size = box.Size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                ClipText = true,
                LabelSettings = new LabelSettings
                {
                    Font = UiTheme.CurrentFont,
                    FontSize = size,
                    LineSpacing = -size * 0.12f,
                    FontColor = UiTheme.Accent,
                    OutlineSize = Math.Max(6, size / 7),
                    OutlineColor = UiTheme.Bg,
                    ShadowColor = new Color("7fd8ff") with { A = 0.85f },
                    ShadowOffset = new Vector2(Math.Max(3, size / 22), Math.Max(3, size / 22)),
                    ShadowSize = 1,
                },
            });
        }
        return view;
    }
}

/// <summary>Development tool (<c>-- --render-steam-art &lt;dir&gt; [title] [icon]</c>): renders Scanline's own set (or a sample game's) and quits.</summary>
public partial class SteamArtRenderer : Node
{
    private readonly string _dir;
    private readonly string _title;
    private readonly string? _icon;

    public SteamArtRenderer(string dir, string title = "Scanline", string? icon = null) => (_dir, _title, _icon) = (dir, title, icon);

    public override async void _Ready()
    {
        await SteamArt.Render(this, _title, _dir, _icon, settleFrames: 45);
        GD.Print($"steam-art: wrote {_dir}");
        GetTree().Quit();
    }
}
