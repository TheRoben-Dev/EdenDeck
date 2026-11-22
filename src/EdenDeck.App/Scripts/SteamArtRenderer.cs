using EdenDeck.App.Ui;
using EdenDeck.App.Ui.Poly;
using Godot;

namespace EdenDeck.App;

/// <summary>
/// Development tool (<c>-- --render-steam-art &lt;dir&gt;</c>): renders Scanline's own Steam artwork from
/// the 32-bit 3D theme's real scene (fogged grid floor, era props, a 3D cartridge, the wordmark)
/// and saves the PNGs Steam uses for a shortcut: portrait capsule, wide capsule, hero, logo and
/// icon. The results are checked in under tools/release/steam-art and shipped with the app;
/// nothing is rendered or downloaded on players' machines.
/// </summary>
public partial class SteamArtRenderer : Node
{
    private sealed record Shot(string File, Vector2I Size, bool Backdrop, Rect2[] Carts, Rect2? Wordmark, int FontSize, bool Transparent = false, Vector2I? Output = null);

    // Sizes Steam asks for: capsule 600x900, wide capsule 920x430, hero 3840x1240, logo up to 1280x720.
    private static readonly Shot[] Shots =
    [
        new("capsule.png", new(600, 900), true, [new Rect2(90, 110, 420, 520)], new Rect2(0, 660, 600, 140), 92),
        new("wide.png", new(920, 430), true, [new Rect2(40, 35, 330, 360)], new Rect2(360, 135, 540, 160), 104),
        // Hero: no wordmark (Steam lays the logo over it, bottom left); carts on the right.
        new("hero.png", new(3840, 1240), true,
            [new Rect2(1900, 330, 560, 560), new Rect2(3260, 260, 520, 520), new Rect2(2420, 110, 1000, 1000)], null, 0),
        new("logo.png", new(1280, 720), false, [], new Rect2(0, 210, 1280, 300), 210, Transparent: true),
        // Icon: rendered large and scaled down so the cart label stays readable.
        new("icon.png", new(1024, 1024), false, [new Rect2(72, 32, 880, 960)], null, 0, Output: new(256, 256)),
    ];

    /// <summary>The theme's colours lifted a little: Steam shows art small and on dark UI, where the app's near-black would read as empty.</summary>
    private static ThemeSpec Art => UiTheme.Spec with
    {
        Bg = new("222a78"), Panel = new("3a42a8"), Line = new("c0c6ff"), Info = new("7fd8ff"),
    };

    private readonly string _dir;

    public SteamArtRenderer(string dir) => _dir = dir;

    public override async void _Ready()
    {
        Directory.CreateDirectory(_dir);
        UiTheme.Use("polygon", null);
        foreach (var shot in Shots)
        {
            var view = Build(shot);
            AddChild(view);
            // Let the low-res stages size themselves and the carts settle into their pose.
            for (var i = 0; i < 45; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var img = view.GetTexture().GetImage();
            if (shot.Output is { } o)
            {
                img.Resize(o.X, o.Y, Image.Interpolation.Lanczos);
            }
            var path = Path.Combine(_dir, shot.File);
            img.SavePng(path);
            GD.Print($"steam-art: wrote {path} ({shot.Size.X}x{shot.Size.Y})");
            view.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GetTree().Quit();
    }

    private static SubViewport Build(Shot s)
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
            var bg = new PolyBackdrop(Art, showcase: true);
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
            root.AddChild(new CartStage("Scanline") { Position = cart.Position, Size = cart.Size });
        }
        if (s.Wordmark is { } mark)
        {
            root.AddChild(new Label
            {
                Text = "SCANLINE",
                Position = mark.Position,
                Size = mark.Size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                LabelSettings = new LabelSettings
                {
                    Font = UiTheme.CurrentFont,
                    FontSize = s.FontSize,
                    FontColor = UiTheme.Accent,
                    OutlineSize = Math.Max(6, s.FontSize / 7),
                    OutlineColor = UiTheme.Bg,
                    ShadowColor = UiTheme.Spec.Info with { A = 0.85f },
                    ShadowOffset = new Vector2(Math.Max(3, s.FontSize / 22), Math.Max(3, s.FontSize / 22)),
                    ShadowSize = 1,
                },
            });
        }
        return view;
    }
}
