using Godot;
using static EdenDeck.App.Ui.Poly.Ps1;

namespace EdenDeck.App.Ui.Poly;

/// <summary>
/// Props built from the bought models (see <see cref="Models"/>). Each one keeps the idle motion
/// and tap bop of a generated prop and adds its own part animation. Used only when the model is
/// in the build; otherwise the catalogue falls back to the generated prop for that slot.
/// </summary>
public static partial class Props
{
    private static Func<Look, Prop> Bought(string model, Func<Look, Prop> make, Func<Look, Prop>? fallback = null) =>
        Models.Has(model) ? make : fallback ?? (_ => throw new InvalidOperationException(model));

    private static Func<Look, Prop>[] BoughtCatalogue()
    {
        var all = new List<Func<Look, Prop>>
        {
            Bought("tv", TvModel, Tv),
            Bought("tape", TapeModel, Tape),
            JewelCase,
            Bought("console", ConsoleModel, DiscPlayer),
            MemoryCard,
            PetEgg,
            Handheld,
            Bought("phone", PhoneModel, FlipPhone),
            LavaLamp,
            Floppy,
            Crystal,
            Bought("claw", ClawMachine, Mascot),
            Bought("crest", CrestModel, PowerRing),
        };
        // Extra props with no generated version join only when their model is there.
        foreach (var (model, make) in new (string, Func<Look, Prop>)[]
                 {
                     ("typewriter", Typewriter), ("pc", OldPc), ("car", ToyCar), ("drone", Drone),
                     ("lamp", DeskLamp), ("arcade", Arcade), ("disco", DiscoBall),
                 })
        {
            if (Models.Has(model))
            {
                all.Add(make);
            }
        }
        return all.ToArray();
    }

    private static (Prop P, Model M) FromModel(Look l, string model, Prop p)
    {
        var m = Models.Load(l, model, p.Body);
        return (p, m);
    }

    /// <summary>A damped wobble for springy parts (antennas, sticks) after a tap.</summary>
    private static float Spring(float age, float amp, float freq = 18, float decay = 3) =>
        age > 4 ? 0 : amp * Mathf.Sin(age * freq) * Mathf.Exp(-age * decay);

    /// <summary>CRT TV with rabbit ears. Idle: antennas sway. Tap: static, colour bars, then its own picture.</summary>
    public static Prop TvModel(Look l)
    {
        var (p, m) = FromModel(l, "tv", new Prop { Name = "tv", Radius = 0.8f, BaseRot = new Vector3(0, 18, 0), Style = Prop.Bop.Wiggle });
        var antL = m.Part("antenna_l");
        var antR = m.Part("antenna_r");
        var screen = m.Mats("screen").First();
        var picture = (Texture2D)screen.GetShaderParameter("tex");
        var noise = Tex(Noise(28, 20, 1));
        var bars = ColorBars();
        var seed = 2;
        p.Animate = (pr, t, dt) =>
        {
            var a = pr.TapAge;
            var wob = Spring(a, 14);
            antL.RotationDegrees = new Vector3(0, 0, Mathf.Sin(t * 1.3f + pr.Phase) * 4 + wob);
            antR.RotationDegrees = new Vector3(0, 0, Mathf.Sin(t * 1.1f + pr.Phase + 1) * 4 - wob);
            if (a < 0.2f)
            {
                screen.Param("use_tex", false);
                screen.Param("albedo", Colors.White);
                screen.Param("emission", C("aaaaaa"));
            }
            else if (a < 1.3f)
            {
                screen.Param("tex", noise);
                screen.Param("use_tex", true);
                screen.Param("emission", C("303030"));
                noise.Update(Noise(28, 20, seed++));
            }
            else if (a < 2.6f)
            {
                screen.Param("tex", bars);
                screen.Param("emission", C("202020"));
                screen.Param("uv_offset", new Vector2(0, a < 1.8f ? (a - 1.3f) * 2 : 0));
            }
            else if (a < 5.5f)
            {
                screen.Param("tex", picture);
                screen.Param("uv_offset", Vector2.Zero);
                screen.Param("emission", C("282828"));
            }
            else
            {
                // Off: a dark glass picture of what was on.
                screen.Param("tex", picture);
                screen.Param("use_tex", true);
                screen.Param("albedo", C("303838"));
                screen.Param("emission", Colors.Black);
                screen.Param("uv_offset", Vector2.Zero);
            }
        };
        return p;
    }

    /// <summary>Stack of video tapes. Tap: the top tape flips off the stack and lands, the loose one scoots out and back.</summary>
    public static Prop TapeModel(Look l)
    {
        var (p, m) = FromModel(l, "tape", new Prop { Name = "tape", Radius = 0.7f, BaseRot = new Vector3(48, -20, 0), Sway = 25, Style = Prop.Bop.Wiggle });
        var top = m.Part("top");
        var loose = m.Part("loose");
        var topRest = top.Position;
        var looseRest = loose.Position;
        p.Animate = (pr, t, dt) =>
        {
            var a = Mathf.Clamp(pr.TapAge / 0.9f, 0, 1);
            top.Position = topRest + new Vector3(0, Mathf.Sin(a * Mathf.Pi) * 0.45f, 0);
            top.RotationDegrees = new Vector3(-360 * Mathf.SmoothStep(0, 1, a), 0, 0);
            var s = pr.TapAge < 0.35f ? Overshoot(pr.TapAge / 0.35f) : pr.TapAge < 1.6f ? 1 : Mathf.Max(0, 1 - (pr.TapAge - 1.6f) / 0.4f);
            loose.Position = looseRest + new Vector3(0.18f * s, 0, 0.12f * s);
            loose.RotationDegrees = new Vector3(0, 30 * s, 0);
        };
        return p;
    }

    /// <summary>Home console with a pad. Tap: the disc lid pops open and the pad hops on its cable.</summary>
    public static Prop ConsoleModel(Look l)
    {
        var (p, m) = FromModel(l, "console", new Prop { Name = "console", Radius = 0.75f, BaseRot = new Vector3(18, -20, 0), Sway = 20, Style = Prop.Bop.Wiggle });
        var lid = m.Part("lid");
        var pad = m.Part("pad");
        p.Animate = (pr, t, dt) =>
        {
            pr.OpenTarget = pr.TapAge < 2.8f ? 1 : 0;
            lid.RotationDegrees = new Vector3(-95 * Overshoot(pr.Open), 0, 0);
            var hop = pr.TapAge < 0.8f ? Mathf.Sin(pr.TapAge / 0.8f * Mathf.Pi) : 0;
            pad.RotationDegrees = new Vector3(-28 * hop, Mathf.Sin(t * 0.8f + pr.Phase) * 4, Spring(pr.TapAge, 10, 22));
        };
        return p;
    }

    /// <summary>Rotary phone. Tap: it rings (the handset rattles on its cradle) and the dial turns and springs back.</summary>
    public static Prop PhoneModel(Look l)
    {
        var (p, m) = FromModel(l, "phone", new Prop { Name = "phone", Radius = 0.65f, BaseRot = new Vector3(10, 25, 0), Sway = 22, Style = Prop.Bop.Wiggle });
        var handset = m.Part("handset");
        var dial = m.Part("dial");
        var rest = handset.Position;
        p.Animate = (pr, t, dt) =>
        {
            var a = pr.TapAge;
            var ring = a < 1.6f ? Mathf.Abs(Mathf.Sin(a * 30)) * (1 - a / 1.6f) : 0;
            handset.Position = rest + new Vector3(0, ring * 0.08f, 0);
            handset.RotationDegrees = new Vector3(0, 0, a < 1.6f ? Mathf.Sin(a * 41) * 7 * (1 - a / 1.6f) : 0);
            // Dialling: wind round to the finger stop, then return at the governor's even pace.
            var turn = a < 0.5f ? Mathf.SmoothStep(0, 1, a / 0.5f) : a < 1.4f ? 1 - (a - 0.5f) / 0.9f : 0;
            m.Turn(dial, Vector3.Up, -300 * turn);
        };
        return p;
    }

    /// <summary>Bronze star crest, a classic key-item pickup. Spins; tap: spins up with a gold flash.</summary>
    public static Prop CrestModel(Look l)
    {
        var (p, m) = FromModel(l, "crest", new Prop { Name = "crest", Radius = 0.45f, SpinSpeed = 100, Sway = 0, BobAmp = 0.1f, Style = Prop.Bop.Spin });
        m.Scene.RotationDegrees = new Vector3(90, 0, 0);  // stand the coin up, face to the camera
        p.Animate = (pr, t, dt) =>
            m.Param(null, "emission", pr.TapAge < 0.8f ? C("ffd870") * (1 - pr.TapAge / 0.8f) : C("201400"));
        return p;
    }

    /// <summary>Typewriter. Tap: the carriage types along line by line, dings back, and the paper feeds up.</summary>
    public static Prop Typewriter(Look l)
    {
        var (p, m) = FromModel(l, "typewriter", new Prop { Name = "typewriter", Radius = 0.75f, BaseRot = new Vector3(16, -18, 0), Sway = 16, Style = Prop.Bop.Wiggle });
        var carriage = m.Part("carriage");
        var paper = m.Part("paper");
        var cRest = carriage.Position;
        var pRest = paper.Position;
        p.Animate = (pr, t, dt) =>
        {
            var a = pr.TapAge;
            float x;
            if (a < 1.4f)
            {
                x = -Mathf.Floor(a * 10) / 14 * 0.45f;  // a key per tick
            }
            else
            {
                x = -0.45f * (1 - Overshoot((a - 1.4f) / 0.3f));  // the return lever
            }
            carriage.Position = cRest + new Vector3(a < 3 ? x : 0, 0, 0);
            var feed = a < 1.4f ? 0 : a < 1.7f ? (a - 1.4f) / 0.3f : Mathf.Max(0, 1 - (a - 3f) / 1.5f);
            paper.Position = pRest + new Vector3(0, 0.07f * feed, 0);
        };
        return p;
    }

    /// <summary>Beige desktop PC, screen on. Tap: it reboots (black, a flash, then the desktop) and the keyboard rattles.</summary>
    public static Prop OldPc(Look l)
    {
        var (p, m) = FromModel(l, "pc", new Prop { Name = "pc", Radius = 0.85f, BaseRot = new Vector3(8, -22, 0), Sway = 14, Style = Prop.Bop.Wiggle });
        var screen = m.Mats("screen").First();
        var keyboard = m.Part("keyboard");
        var kRest = keyboard.Position;
        p.Animate = (pr, t, dt) =>
        {
            var a = pr.TapAge;
            if (a < 0.6f)
            {
                screen.Param("albedo", C("101010"));
                screen.Param("emission", Colors.Black);
            }
            else if (a < 0.8f)
            {
                screen.Param("albedo", Colors.White);
                screen.Param("emission", C("909090"));
            }
            else
            {
                screen.Param("albedo", Colors.White);
                screen.Param("emission", C("262626"));
            }
            var rattle = a < 0.6f ? Mathf.Abs(Mathf.Sin(a * 50)) * 0.025f : 0;
            keyboard.Position = kRest + new Vector3(0, rattle, 0);
        };
        return p;
    }

    /// <summary>Toy car. Idle: rolls back and forth with its wheels turning. Tap: pops a wheelie.</summary>
    public static Prop ToyCar(Look l)
    {
        var (p, m) = FromModel(l, "car", new Prop { Name = "car", Radius = 0.65f, BaseRot = new Vector3(10, -35, 0), Sway = 10, Style = Prop.Bop.Hop });
        string[] names = ["wheel_bl", "wheel_fl", "wheel_br", "wheel_fr"];
        var wheels = names.Select(m.Part).ToArray();
        var lastX = 0f;
        var spin = 0f;
        p.Animate = (pr, t, dt) =>
        {
            var x = Mathf.Sin(t * 0.7f + pr.Phase) * 0.22f;
            spin -= (x - lastX) / 0.1f;  // wheel radius ~0.1
            lastX = x;
            var a = pr.TapAge;
            var wheelie = a < 1.1f ? Mathf.Sin(a / 1.1f * Mathf.Pi) : 0;
            m.Scene.Position = new Vector3(x, wheelie * 0.1f, 0);
            m.Scene.RotationDegrees = new Vector3(0, 0, 28 * wheelie);
            foreach (var w in wheels)
            {
                w.Rotation = new Vector3(0, 0, spin);
            }
        };
        return p;
    }

    /// <summary>Delivery drone. Hovers with its rotors spinning. Tap: a barrel roll.</summary>
    public static Prop Drone(Look l)
    {
        var (p, m) = FromModel(l, "drone", new Prop { Name = "drone", Radius = 0.85f, BaseRot = new Vector3(12, 0, 0), Sway = 30, SwaySpeed = 0.3f, BobAmp = 0.12f, Style = Prop.Bop.Wiggle });
        var rotors = Enumerable.Range(0, 4).Select(i => m.Part($"rotor_{i}")).ToArray();
        var spin = 0f;
        p.Animate = (pr, t, dt) =>
        {
            // Not a multiple of the 30 Hz frame step, so the blades don't look parked.
            spin += dt * 1030;
            for (var i = 0; i < rotors.Length; i++)
            {
                rotors[i].RotationDegrees = new Vector3(0, i % 2 == 0 ? spin : -spin, 0);
            }
            var a = pr.TapAge;
            m.Scene.RotationDegrees = new Vector3(0, 0, a < 1 ? 360 * Mathf.SmoothStep(0, 1, a) : Mathf.Sin(t * 0.9f + pr.Phase) * 3);
        };
        return p;
    }

    /// <summary>Desk lamp. Idle: the arms breathe and the head looks around. Tap: switches the light, head nods.</summary>
    public static Prop DeskLamp(Look l)
    {
        var (p, m) = FromModel(l, "lamp", new Prop { Name = "lamp", Radius = 0.7f, BaseRot = new Vector3(5, -40, 0), Sway = 18, Style = Prop.Bop.Hop });
        var arm1 = m.Part("arm1");
        var arm2 = m.Part("arm2");
        var head = m.Part("head");
        var bulb = m.Mats("bulb").ToList();
        p.Animate = (pr, t, dt) =>
        {
            var s = Mathf.Sin(t * 0.8f + pr.Phase);
            arm1.RotationDegrees = new Vector3(0, 0, s * 6);
            arm2.RotationDegrees = new Vector3(0, 0, -s * 8);
            var nod = pr.TapAge < 0.6f ? Mathf.Sin(pr.TapAge / 0.6f * Mathf.Pi) * 30 : 0;
            head.RotationDegrees = new Vector3(Mathf.Sin(t * 0.5f + pr.Phase) * 10, Mathf.Sin(t * 0.37f) * 20, -nod);
            var on = pr.Taps % 2 == 0;
            foreach (var b in bulb)
            {
                b.Param("emission", on ? C("e0c070") : Colors.Black);
            }
        };
        return p;
    }

    /// <summary>Upright arcade cabinet with a glowing screen and marquee. Idle: attract mode. Tap: the stick jerks, buttons mash, the screen flares.</summary>
    public static Prop Arcade(Look l)
    {
        var (p, m) = FromModel(l, "arcade", new Prop { Name = "arcade", Radius = 0.85f, BaseRot = new Vector3(4, -25, 0), Sway = 14, Style = Prop.Bop.Wiggle });
        var stick = m.Part("stick");
        var buttons = Enumerable.Range(0, 3).Select(i => m.Part($"button_{i}")).ToArray();
        var rests = buttons.Select(b => b.Position).ToArray();
        p.Animate = (pr, t, dt) =>
        {
            var a = pr.TapAge;
            var busy = a < 1.5f;
            var f = busy ? 9f : 0.7f;
            stick.RotationDegrees = new Vector3(Mathf.Sin(t * f) * (busy ? 18 : 5), 0, Mathf.Cos(t * f * 1.3f) * (busy ? 18 : 5));
            for (var i = 0; i < buttons.Length; i++)
            {
                var down = busy && Mathf.PosMod(a * 7 + i * 0.37f, 1) < 0.4f;
                buttons[i].Position = rests[i] - new Vector3(0, down ? 0.008f : 0, 0);
            }
            var flicker = 0.85f + 0.15f * Mathf.Sin(t * 3.1f);
            m.Param(null, "glow", busy ? 1.6f * (1 - a / 1.5f) + flicker : flicker);
        };
        return p;
    }

    /// <summary>Mirror ball on its string. Turns slowly; tap: spins up and the mirrors sparkle.</summary>
    public static Prop DiscoBall(Look l)
    {
        var (p, m) = FromModel(l, "disco", new Prop { Name = "disco", Radius = 0.6f, Sway = 0, BobAmp = 0.03f, Style = Prop.Bop.Wiggle });
        var ball = m.Part("ball");
        var angle = 0f;
        p.Animate = (pr, t, dt) =>
        {
            var a = pr.TapAge;
            angle += dt * (40 + (a < 2.5f ? 500 * (1 - a / 2.5f) : 0));
            ball.RotationDegrees = new Vector3(0, angle, 0);
            var sparkle = a < 2.5f ? (Mathf.PosMod(a * 12, 1) < 0.5f ? 0.5f : 0.15f) * (1 - a / 2.5f) : 0;
            m.Param("ball", "emission", new Color(sparkle, sparkle, sparkle));
        };
        return p;
    }

    /// <summary>
    /// Claw machine (takes the mascot's slot). Idle: the claw drifts over the prizes and the
    /// cabinet lights pulse. Tap: the claw moves over a plush, drops, grabs it, lifts it... and
    /// it slips out, as it always does. Then it drives home.
    /// </summary>
    public static Prop ClawMachine(Look l)
    {
        var (p, m) = FromModel(l, "claw", new Prop { Name = "claw", Radius = 0.85f, BaseRot = new Vector3(6, -20, 0), Sway = 14, Style = Prop.Bop.Wiggle });
        var carriage = m.Part("carriage");
        var rope = m.Part("rope");
        var head = m.Part("head");
        var prize = m.Part("prize");
        var stick = m.Part("stick");
        var fingers = Enumerable.Range(0, 3).Select(i => m.Part($"finger_{i}")).ToArray();
        // Lean of each finger as the claw closes: towards the claw's centre line.
        Vector3[] close = [new(-30, 0, 0), new(30, 0, 0), new(0, 0, 30)];
        var cRest = carriage.Position;
        var hRest = head.Position;
        var pRest = prize.Position;
        // Built 0.16 model units below the carriage, so the model's scale falls out of that offset.
        var ropeLen = -hRest.Y;
        var fingerLen = ropeLen * 0.75f;
        var reachX = pRest.X - (cRest.X + hRest.X);
        var drop = cRest.Y + hRest.Y - pRest.Y - fingerLen * 0.6f;
        p.Animate = (pr, t, dt) =>
        {
            var a = pr.TapAge;
            float x, d = 0, grip = 0, lift = 0;
            if (a < 1)
            {
                x = reachX * Mathf.SmoothStep(0, 1, a);  // over the plush
            }
            else if (a < 3.8f)
            {
                x = reachX;
                d = a < 1.7f ? Mathf.SmoothStep(0, 1, (a - 1) / 0.7f) : a < 2.0f ? 1 : Mathf.Max(0, 1 - (a - 2.0f) / 0.8f);
                grip = a < 1.7f ? 0 : Mathf.Min(1, (a - 1.7f) / 0.3f);
                const float Slip = 2.45f;
                if (a < Slip)
                {
                    lift = 1 - d;  // carried up with the claw...
                }
                else
                {
                    // ...until the grip gives and it drops back onto the pile.
                    var held = 1 - Mathf.Max(0, 1 - (Slip - 2.0f) / 0.8f);
                    var fall = Mathf.Clamp((a - Slip) / 0.25f, 0, 1);
                    lift = held * (1 - fall * fall);
                    grip = Mathf.Max(0.55f, 1 - (a - Slip) * 3);
                }
            }
            else if (a < 4.8f)
            {
                x = reachX * (1 - Mathf.SmoothStep(0, 1, a - 3.8f));  // home
                grip = Mathf.Max(0, 0.55f - (a - 3.8f) * 2);
            }
            else
            {
                x = Mathf.Sin(t * 0.35f + pr.Phase) * reachX * 0.8f;  // idle drift
            }
            carriage.Position = cRest + new Vector3(x, 0, 0);
            head.Position = hRest - new Vector3(0, drop * d, 0);
            rope.Scale = new Vector3(1, 1 + drop * d / ropeLen, 1);
            for (var i = 0; i < fingers.Length; i++)
            {
                fingers[i].RotationDegrees = close[i] * grip - close[i] * 0.3f * (1 - grip);
            }
            prize.Position = pRest + new Vector3(0, drop * lift, 0);
            prize.RotationDegrees = new Vector3(0, 0, lift * 12);
            var busy = a < 4.8f;
            stick.RotationDegrees = busy ? new Vector3(Mathf.Sin(a * 7) * 15, 0, (x - reachX * 0.5f) * 60) : Vector3.Zero;
            m.Param(null, "glow", busy ? 0.9f + 0.6f * (Mathf.PosMod(a * 4, 1) < 0.5f ? 1 : 0) : 0.6f + 0.25f * Mathf.Sin(t * 1.7f));
        };
        return p;
    }
}
