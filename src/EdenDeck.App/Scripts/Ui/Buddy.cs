using Godot;

namespace EdenDeck.App.Ui;

/// <summary>
/// Pip, the console buddy theme's mascot: a little pebble-shaped pastel handheld with a pixel face
/// drawn right on its shell, a d-pad, two buttons, stubby feet and a cartridge stuck in its top.
/// An original character (rounded pebble, no screen, no box body), drawn in code.
///
/// It lives in the background layer in the bottom-right corner and never takes input from the UI:
/// it breathes and blinks, glances toward whatever has focus, dozes off after a while without input
/// (little z's float up) and wakes when you press something, cheers when a game starts and when you
/// come back, and reacts to a tap on itself with one of a few small animations. Updated at 30 Hz,
/// frozen while a game runs, and holds still (no hops, no floating bits) when Animations is off.
/// </summary>
public partial class Buddy : Control
{
    private const double Step = 1.0 / 30;
    private const double NapAfter = 45;

    private static readonly Color Shell = new("ffc9ad");
    private static readonly Color ShellLight = new("ffe1d1");
    private static readonly Color ShellEdge = new("e0957a");
    private static readonly Color Ink = new("4a3550");
    private static readonly Color Cheek = new("ff8fa3");
    private static readonly Color Mint = new("86d9bd");
    private static readonly Color Lilac = new("bfa3f2");
    private static readonly Color Butter = new("ffe08a");

    /// <summary>The buddy on screen, if the current theme has one.</summary>
    public static Buddy? Instance { get; private set; }

    private enum Face { Open, Closed, Happy, Wide }

    private sealed class Bit
    {
        public required string Kind; // "z", "heart", "note", "spark"
        public Vector2 Pos;
        public Vector2 Vel;
        public float Life;
        public float Max;
    }

    private readonly List<Bit> _bits = [];
    private readonly RandomNumberGenerator _rng = new();
    private double _t;
    private double _acc;
    private double _idle;
    private double _blinkAt = 3;
    private double _blinkUntil;
    private double _hopStart = -10;
    private int _hops;
    private double _wiggleStart = -10;
    private double _happyUntil;
    private double _nextZ;
    private int _reaction;
    private bool _paused;
    private bool _pressed;
    private Vector2 _pressAt;
    private Vector2I _look;

    public Buddy()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _rng.Seed = 7;
    }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>True while a game runs or the app isn't focused: nothing moves or redraws.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            SetProcess(!value);
        }
    }

    /// <summary>True while Pip is asleep (no input for a while).</summary>
    public bool Asleep => _idle >= NapAfter;

    /// <summary>A game is starting, or you just came back from one.</summary>
    public void Cheer()
    {
        Wake();
        _happyUntil = _t + 2.2;
        Hop(2);
        Burst("spark", 6);
    }

    // ---- Layout -----------------------------------------------------------------------------------

    /// <summary>One "unit": Pip is 100 units wide. Scales with the window like the rest of the UI.</summary>
    private float U => Mathf.Clamp(Size.Y / 800f, 0.6f, 2.5f) * 1.15f;

    /// <summary>Where Pip's feet touch the ground: bottom-right, just above the hint bar.</summary>
    private Vector2 Feet => new(Size.X - 40 - 70 * U, Size.Y - 58 - 6 * U);

    private Rect2 HitBox => new(Feet + new Vector2(-55, -150) * U, new Vector2(110, 160) * U);

    // ---- Input: any press wakes Pip; a tap on Pip (not on the UI) gets a reaction ----------------

    public override void _Input(InputEvent e)
    {
        if (_paused || !IsVisibleInTree())
        {
            return;
        }
        if (e is InputEventKey { Pressed: true } or InputEventJoypadButton { Pressed: true } ||
            e is InputEventJoypadMotion jm && Mathf.Abs(jm.AxisValue) > 0.5f)
        {
            Wake();
            return;
        }
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            return;
        }
        Wake();
        if (mb.Pressed)
        {
            _pressAt = mb.Position;
            _pressed = true;
            return;
        }
        var local = mb.Position - GlobalPosition;
        if (!_pressed || (mb.Position - _pressAt).Length() > TouchScroll.Deadzone || !HitBox.HasPoint(local) ||
            UiHit.OverUi(this, mb.Position))
        {
            _pressed = false;
            return;
        }
        _pressed = false;
        Tap();
    }

    /// <summary>Pip's reaction to a tap; cycles through a few.</summary>
    public void Tap()
    {
        _happyUntil = _t + 1.4;
        switch (_reaction++ % 3)
        {
            case 0:
                Hop(1);
                Burst("heart", 3);
                break;
            case 1:
                _wiggleStart = _t;
                Burst("note", 2);
                break;
            default:
                Hop(1);
                Burst("spark", 5);
                break;
        }
        QueueRedraw();
    }

    private void Wake()
    {
        if (Asleep && Anim.Enabled)
        {
            Hop(1);  // a little start
        }
        _idle = 0;
    }

    private void Hop(int count)
    {
        if (!Anim.Enabled)
        {
            return;
        }
        _hopStart = _t;
        _hops = count;
    }

    private void Burst(string kind, int n)
    {
        if (!Anim.Enabled)
        {
            return;
        }
        for (var i = 0; i < n; i++)
        {
            var a = _rng.RandfRange(-2.4f, -0.7f);
            _bits.Add(new Bit
            {
                Kind = kind,
                Pos = new Vector2(_rng.RandfRange(-36, 30), -158),
                Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _rng.RandfRange(30, 70),
                Max = 1.2f,
                Life = 1.2f,
            });
        }
    }

    // ---- Clock --------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        _t += delta;
        _idle += delta;
        _acc += delta;
        if (_acc < Step)
        {
            return;
        }
        var dt = (float)_acc;
        _acc = 0;
        if (Anim.Enabled)
        {
            if (_t >= _blinkAt && !Asleep)
            {
                _blinkUntil = _t + 0.13;
                _blinkAt = _t + _rng.RandfRange(2.5f, 5.5f);
            }
            if (Asleep && _t >= _nextZ)
            {
                _nextZ = _t + 1.7;
                _bits.Add(new Bit { Kind = "z", Pos = new Vector2(22, -112), Vel = new Vector2(8, -22), Life = 2.4f, Max = 2.4f });
            }
            for (var i = _bits.Count - 1; i >= 0; i--)
            {
                var b = _bits[i];
                b.Life -= dt;
                b.Pos += b.Vel * dt;
                b.Vel = b.Kind == "z" ? b.Vel : b.Vel * (1 - 1.5f * dt) + new Vector2(0, -10 * dt);
                if (b.Life <= 0)
                {
                    _bits.RemoveAt(i);
                }
            }
        }
        else
        {
            _bits.Clear();
        }
        _look = LookToward();
        QueueRedraw();
    }

    /// <summary>Pupil offset (-1..1 pixels each way) toward the focused control.</summary>
    private Vector2I LookToward()
    {
        if (Asleep)
        {
            return Vector2I.Zero;
        }
        var focus = GetViewport()?.GuiGetFocusOwner();
        if (focus == null || !IsInstanceValid(focus))
        {
            return Vector2I.Zero;
        }
        var eyes = GlobalPosition + Feet + new Vector2(0, -78) * U;
        var d = focus.GetGlobalRect().GetCenter() - eyes;
        if (d.Length() < 40)
        {
            return Vector2I.Zero;
        }
        d = d.Normalized();
        return new Vector2I(Mathf.RoundToInt(d.X), Mathf.RoundToInt(d.Y * 0.8f));
    }

    // ---- Drawing ------------------------------------------------------------------------------------

    public override void _Draw()
    {
        var u = U;
        var t = (float)_t;
        var motion = Anim.Enabled;
        var asleep = Asleep;

        // Hop: a parabola per hop, with a squash on landing.
        var hopY = 0f;
        var squash = 0f;
        var since = (float)(_t - _hopStart);
        const float hopLen = 0.42f;
        if (motion && since < hopLen * _hops)
        {
            var k = since % hopLen / hopLen;
            hopY = -Mathf.Sin(k * Mathf.Pi) * 22;
            squash = k < 0.15f ? (0.15f - k) * 1.2f : k > 0.85f ? (k - 0.85f) * 1.2f : -0.06f * Mathf.Sin(k * Mathf.Pi);
        }
        var breathe = motion ? Mathf.Sin(t * (asleep ? 1.3f : 2.2f)) * (asleep ? 0.035f : 0.018f) : 0f;
        var wiggleK = (float)(_t - _wiggleStart);
        var tilt = motion && wiggleK < 0.8f ? Mathf.Sin(wiggleK * 24f) * 0.14f * (1 - wiggleK / 0.8f) : 0f;
        if (asleep && motion)
        {
            tilt += 0.08f;  // nods off to one side
        }

        var feet = Feet;
        // Ground shadow shrinks while in the air.
        var air = Mathf.Clamp(-hopY / 22f, 0, 1);
        DrawSetTransform(feet, 0, new Vector2(u, u * 0.28f));
        DrawCircle(Vector2.Zero, 46 * (1 - 0.3f * air), Ink with { A = 0.13f * (1 - 0.5f * air) });

        var sx = 1 + squash - breathe * 0.5f;
        var sy = 1 - squash + breathe;
        DrawSetTransform(feet + new Vector2(0, hopY * u), tilt, new Vector2(u * sx, u * sy));

        // Feet
        DrawEllipse(new Vector2(-20, -5), 13, 7, ShellEdge);
        DrawEllipse(new Vector2(20, -5), 13, 7, ShellEdge);

        // Cartridge sticking out of the top, slightly crooked.
        var cart = new[] { new Vector2(-24, -136), new Vector2(8, -140), new Vector2(10, -112), new Vector2(-22, -108) };
        DrawColoredPolygon(cart, Butter);
        DrawPolyline([.. cart, cart[0]], ShellEdge, 2.2f, true);
        DrawLine(new Vector2(-17, -131), new Vector2(3, -133), ShellEdge with { A = 0.6f }, 2f, true);
        DrawLine(new Vector2(-16, -125), new Vector2(4, -127), ShellEdge with { A = 0.6f }, 2f, true);

        // Pebble body: a soft superellipse, a little wider at the bottom.
        var body = Pebble(64);
        DrawColoredPolygon(body, Shell);
        // Lighter belly / top-left sheen
        var sheen = new Vector2[24];
        for (var i = 0; i < sheen.Length; i++)
        {
            var a = Mathf.Tau * i / sheen.Length;
            sheen[i] = new Vector2(-14 + Mathf.Cos(a) * 26, -88 + Mathf.Sin(a) * 18);
        }
        DrawColoredPolygon(sheen, ShellLight with { A = 0.7f });
        DrawPolyline([.. body, body[0]], ShellEdge, 3f, true);

        // Controls: d-pad lower left, two buttons lower right.
        var dp = new Vector2(-24, -38);
        DrawRect(new Rect2(dp + new Vector2(-10, -3.5f), new Vector2(20, 7)), Mint);
        DrawRect(new Rect2(dp + new Vector2(-3.5f, -10), new Vector2(7, 20)), Mint);
        DrawCircle(new Vector2(20, -44), 5.5f, Lilac);
        DrawCircle(new Vector2(31, -34), 5.5f, Lilac);

        // Face, drawn straight onto the shell in chunky pixels.
        var happy = _t < _happyUntil;
        var face = asleep || (motion && _t < _blinkUntil) ? Face.Closed : happy ? Face.Happy : Face.Open;
        DrawEye(new Vector2(-21, -86), face, _look);
        DrawEye(new Vector2(13, -86), face, _look);
        DrawEllipse(new Vector2(-30, -70), 7, 4, Cheek with { A = happy ? 0.75f : 0.45f });
        DrawEllipse(new Vector2(30, -70), 7, 4, Cheek with { A = happy ? 0.75f : 0.45f });
        DrawMouth(new Vector2(0, -68), asleep ? 0 : happy ? 2 : 1);

        // Floating bits (z's, hearts, notes, sparkles), in Pip's space but upright.
        DrawSetTransform(feet, 0, new Vector2(u, u));
        foreach (var b in _bits)
        {
            var a = Mathf.Clamp(b.Life / b.Max * 1.5f, 0, 1);
            switch (b.Kind)
            {
                case "z":
                    var grow = 1 + (1 - b.Life / b.Max) * 0.8f;
                    DrawPixels(b.Pos, ["####", "..#.", ".#..", "####"], 2.4f * grow, Ink with { A = a * 0.7f });
                    break;
                case "heart":
                    DrawPixels(b.Pos, [".#.#.", "#####", "#####", ".###.", "..#.."], 2.6f, Cheek with { A = a });
                    break;
                case "note":
                    DrawPixels(b.Pos, ["..##", "..#.", "..#.", "###.", "##.."], 2.6f, Lilac.Darkened(0.2f) with { A = a });
                    break;
                default:
                    DrawPixels(b.Pos, ["..#..", ".###.", "#####", ".###.", "..#.."], 2f, UiTheme.Accent with { A = a });
                    break;
            }
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private static Vector2[] Pebble(int n)
    {
        var pts = new Vector2[n];
        const float rx = 50, ry = 58, cy = -66, e = 2.6f;
        for (var i = 0; i < n; i++)
        {
            var a = Mathf.Tau * i / n;
            var c = Mathf.Cos(a);
            var s = Mathf.Sin(a);
            var x = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / e) * rx;
            var y = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / e) * ry;
            x *= 1 + 0.07f * s;               // wider toward the bottom (screen y grows down)
            y += 3 * Mathf.Sin(2 * a);        // a slight lean, so it reads as a pebble, not a box
            pts[i] = new Vector2(x, cy + y);
        }
        return pts;
    }

    private void DrawEllipse(Vector2 c, float rx, float ry, Color col)
    {
        var pts = new Vector2[20];
        for (var i = 0; i < pts.Length; i++)
        {
            var a = Mathf.Tau * i / pts.Length;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, col);
    }

    private void DrawPixels(Vector2 topLeft, string[] rows, float px, Color col)
    {
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                if (rows[y][x] == '#')
                {
                    DrawRect(new Rect2(topLeft + new Vector2(x, y) * px, new Vector2(px, px)), col);
                }
            }
        }
    }

    private const float Px = 3.4f;

    private void DrawEye(Vector2 at, Face face, Vector2I look)
    {
        switch (face)
        {
            case Face.Closed:
                DrawPixels(at + new Vector2(0, Px * 2), ["####"], Px, Ink);
                break;
            case Face.Happy:
                DrawPixels(at + new Vector2(0, Px), [".##.", "#..#"], Px, Ink);
                break;
            default:
                var o = new Vector2(look.X, look.Y) * Px * 0.5f;
                DrawPixels(at + o, [".##.", "####", "####", ".##."], Px, Ink);
                // Highlight pixel, so the eyes read as looking somewhere.
                DrawRect(new Rect2(at + o + new Vector2(Px, Px), new Vector2(Px, Px)), Colors.White with { A = 0.9f });
                break;
        }
    }

    private void DrawMouth(Vector2 at, int kind)
    {
        switch (kind)
        {
            case 0:
                DrawPixels(at + new Vector2(-Px, 0), ["##"], Px * 0.9f, Ink with { A = 0.7f });
                break;
            case 2:
                DrawPixels(at + new Vector2(-Px * 2, -Px * 0.5f), ["#..#", ".##."], Px * 0.9f, Ink);
                break;
            default:
                DrawPixels(at + new Vector2(-Px * 1.5f, 0), ["###"], Px * 0.9f, Ink);
                break;
        }
    }
}
