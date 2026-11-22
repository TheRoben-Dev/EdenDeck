using Godot;

namespace EdenDeck.App.Ui;

/// <summary>Dark, high-contrast 10-foot theme built in code (no .tres to drift out of sync).</summary>
public static class UiTheme
{
    public static Color Bg { get; private set; }
    public static Color Panel { get; private set; }
    public static Color PanelHi { get; private set; }
    public static Color Accent { get; private set; }
    public static Color Text { get; private set; }
    public static Color Dim { get; private set; }
    public static Color Good { get; private set; }
    public static Color Warn { get; private set; }
    public static Color Bad { get; private set; }
    public static string Name { get; private set; } = "dark";
    public static Color Info { get; private set; }
    public static Color Line { get; private set; }
    public static bool Terminal => Name == "terminal";
    public static bool Nightgrid => Name == "nightgrid";

    /// <summary>Themes with uppercase, letter-spaced, monospace labels.</summary>
    public static bool Mono => Terminal || Nightgrid;
    private static Font? _font;

    public static readonly string[] Names = ["dark", "terminal", "nightgrid"];

    private static readonly string[] MonoFonts = ["JetBrains Mono", "Fira Code", "Cascadia Mono", "DejaVu Sans Mono", "Liberation Mono", "monospace"];

    static UiTheme() => Use("dark");

    /// <summary>Selects the palette. Call before building any UI (Main reloads the scene on change).</summary>
    public static void Use(string name)
    {
        Name = Names.Contains(name) ? name : "dark";
        Info = new("5cc8e0");
        if (Nightgrid)
        {
            // Nightgrid "void" tokens: near-black surfaces, hairline borders, magenta for
            // active/selected, cyan for info, green for OK. Chamfered corners, monospace type.
            Bg = new("07080B");
            Panel = new("0D0F14");
            PanelHi = new("141821");
            Line = new("1F2633");
            Accent = new("FF2A6D");
            Text = new("E6EDF3");
            Dim = new("8B98A9");
            Good = new("2BFF88");
            Warn = new("FF9F1C");
            Bad = new("FF003C");
            Info = new("00E5FF");
            _font = new FontVariation { BaseFont = new SystemFont { FontNames = MonoFonts }, SpacingGlyph = 1 };
        }
        else if (Terminal)
        {
            // Retro terminal: phosphor green on near-black, monospace type.
            Bg = new("050806");
            Panel = new("0b120d");
            PanelHi = new("122019");
            Accent = new("3dff8f");
            Text = new("c9ffdc");
            Dim = new("5f9a76");
            Good = new("3dff8f");
            Warn = new("ffd166");
            Bad = new("ff5f6d");
            Line = new("1d3a28");
            _font = new SystemFont { FontNames = MonoFonts };
        }
        else
        {
            Bg = new("0d0f14");
            Panel = new("161a22");
            PanelHi = new("1f2430");
            Accent = new("7c6cff");
            Text = new("e8eaf0");
            Dim = new("8a91a3");
            Good = new("4ccb8a");
            Warn = new("f0b452");
            Bad = new("ef6b6b");
            Line = new("262c38");
            _font = null;
        }
    }

    public const int FontSize = 20;
    public const int TitleSize = 34;
    public const int SmallSize = 15;

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = FontSize };
        if (_font != null)
        {
            t.DefaultFont = _font;
        }

        foreach (var type in new[] { "Button", "CheckButton" })
        {
            t.SetStylebox("normal", type, Box(Panel, 0));
            t.SetStylebox("hover", type, Box(PanelHi, 0));
            t.SetStylebox("pressed", type, Box(Accent.Darkened(0.35f), 0));
            t.SetStylebox("disabled", type, Box(Panel.Darkened(0.3f), 0));
            t.SetStylebox("focus", type, Focus());
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Text);
            t.SetColor("font_focus_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetColor("font_disabled_color", type, Dim.Darkened(0.3f));
        }
        t.SetConstant("h_separation", "Button", 12);

        t.SetColor("font_color", "Label", Text);
        t.SetStylebox("panel", "PanelContainer", Box(Panel, 0));
        t.SetStylebox("panel", "Panel", Box(Panel, 0));

        // Scrollbars stay thin: navigation is by focus, the bar is only an indicator.
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", bar, Thin(new Color(0, 0, 0, 0.15f)));
            t.SetStylebox("scroll_focus", bar, Thin(new Color(0, 0, 0, 0.15f)));
            t.SetStylebox("grabber", bar, Thin(Dim.Darkened(0.45f)));
            t.SetStylebox("grabber_highlight", bar, Thin(Dim));
            t.SetStylebox("grabber_pressed", bar, Thin(Accent));
        }
        return t;
    }

    public static StyleBoxFlat Box(Color bg, int border, int radius = 10, Color? borderColor = null)
    {
        var b = new StyleBoxFlat
        {
            BgColor = bg,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
            BorderColor = borderColor ?? Accent,
        };
        if (Nightgrid)
        {
            // Chamfered corners (a one-segment "radius" is a straight cut) and hairline borders.
            b.CornerDetail = 1;
            b.SetCornerRadiusAll(Math.Min(radius, 10));
            if (border == 0 && bg.A > 0.5f)
            {
                border = 1;
                b.BorderColor = borderColor ?? Line;
            }
        }
        else
        {
            b.SetCornerRadiusAll(Terminal ? Math.Min(radius, 3) : radius);
        }
        b.SetBorderWidthAll(border);
        return b;
    }

    private static StyleBoxFlat Thin(Color c)
    {
        var b = new StyleBoxFlat { BgColor = c };
        b.SetCornerRadiusAll(3);
        b.ContentMarginLeft = b.ContentMarginRight = 3;
        b.ContentMarginTop = b.ContentMarginBottom = 3;
        return b;
    }

    /// <summary>
    /// Focus ring drawn over the normal style: a flat accent tint and one crisp accent border.
    /// No shadow: a StyleBox shadow is drawn under the whole box, so behind a translucent fill it
    /// showed as a noisy, dithered glow on the Deck's panel, and its blur doubled the edge.
    /// </summary>
    public static StyleBoxFlat Focus()
    {
        var b = Box(Accent with { A = Nightgrid ? 0.12f : 0.16f }, 3, 10, Accent);
        return b;
    }

    /// <summary>A stable, muted tile colour per title so the grid isn't a wall of grey.</summary>
    public static Color TileColor(string seed)
    {
        uint h = 2166136261;
        foreach (var ch in seed)
        {
            h = (h ^ ch) * 16777619;
        }
        if (Nightgrid)
        {
            return Color.FromHsv(h % 360 / 360f, 0.35f, 0.13f + h % 5 / 100f);
        }
        return Terminal
            ? Color.FromHsv((130 + h % 40) / 360f, 0.55f, 0.16f + h % 7 / 100f)
            : Color.FromHsv(h % 360 / 360f, 0.45f, 0.30f);
    }

    public static Label Label(string text, int size = FontSize, Color? color = null, bool wrap = false)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c)
        {
            l.AddThemeColorOverride("font_color", c);
        }
        if (wrap)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        return l;
    }
}
