using Godot;

namespace EdenDeck.App.Ui;

/// <summary>Dark, high-contrast 10-foot theme built in code (no .tres to drift out of sync).</summary>
public static class UiTheme
{
    public static readonly Color Bg = new("0d0f14");
    public static readonly Color Panel = new("161a22");
    public static readonly Color PanelHi = new("1f2430");
    public static readonly Color Accent = new("7c6cff");
    public static readonly Color Text = new("e8eaf0");
    public static readonly Color Dim = new("8a91a3");
    public static readonly Color Good = new("4ccb8a");
    public static readonly Color Warn = new("f0b452");
    public static readonly Color Bad = new("ef6b6b");

    public const int FontSize = 20;
    public const int TitleSize = 34;
    public const int SmallSize = 15;

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = FontSize };

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
        b.SetCornerRadiusAll(radius);
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

    /// <summary>Focus ring drawn over the normal style: thick accent border, slight glow.</summary>
    public static StyleBoxFlat Focus()
    {
        var b = Box(new Color(0, 0, 0, 0), 3, 10, Accent);
        b.ShadowColor = Accent with { A = 0.35f };
        b.ShadowSize = 8;
        b.ExpandMarginLeft = b.ExpandMarginRight = b.ExpandMarginTop = b.ExpandMarginBottom = 2;
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
        return Color.FromHsv(h % 360 / 360f, 0.45f, 0.30f);
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
