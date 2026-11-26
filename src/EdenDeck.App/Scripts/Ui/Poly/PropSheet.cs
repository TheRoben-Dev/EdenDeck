using Godot;

namespace EdenDeck.App.Ui.Poly;

/// <summary>
/// Development tool (`--render-props &lt;file.png&gt;`): renders every prop of the 3D theme alone,
/// idle and at a few moments after a tap, into one contact sheet. One row per prop.
/// </summary>
public partial class PropSheet(string path) : Node
{
    private const int Cell = 160;
    private static readonly float[] Moments = [0.25f, 0.6f, 1.2f, 2.2f, 4f];

    public override async void _Ready()
    {
        var view = new SubViewport
        {
            OwnWorld3D = true,
            Size = new Vector2I(Cell, Cell),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Disabled,
        };
        AddChild(view);
        var env = new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("1b1d2a") } };
        view.AddChild(env);
        var cam = new Camera3D { Fov = 32, Current = true };
        view.AddChild(cam);
        cam.LookAtFromPosition(new Vector3(0, 0.8f, 4.4f), Vector3.Zero, Vector3.Up);
        // Like the library backdrop: its props stand 7-9 units out, in fog from 4 to 24 units.
        var look = new Ps1.Look
        {
            SnapRes = new Vector2(Cell, Cell), Fog = new Color("1b1d2a"), FogNear = 0.5f, FogFar = 20.5f,
            Ambient = new Color("1b1d2a").Lerp(new Color(0.42f, 0.42f, 0.5f), 0.8f),
        };

        var all = Props.All;
        var sheet = Image.CreateEmpty(Cell * (Moments.Length + 1), Cell * all.Length, false, Image.Format.Rgba8);
        const float Dt = 1 / 30f;
        for (var row = 0; row < all.Length; row++)
        {
            var prop = all[row](look);
            view.AddChild(prop.Root);
            var t = 12f;
            for (var i = 0; i < 20; i++)
            {
                prop.Update(t += Dt, Dt);
            }
            await Shot(view, sheet, 0, row);
            prop.Tap();
            var age = 0f;
            for (var col = 0; col < Moments.Length; col++)
            {
                while (age < Moments[col])
                {
                    prop.Update(t += Dt, Dt);
                    age += Dt;
                }
                await Shot(view, sheet, col + 1, row);
            }
            GD.Print($"props: {prop.Name}");
            prop.Root.QueueFree();
        }
        sheet.SavePng(path);
        GD.Print($"props: wrote {path}");
        GetTree().Quit();
    }

    private async Task Shot(SubViewport view, Image sheet, int col, int row)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = view.GetTexture().GetImage();
        img.Convert(Image.Format.Rgba8);
        sheet.BlitRect(img, new Rect2I(0, 0, Cell, Cell), new Vector2I(col * Cell, row * Cell));
    }
}
