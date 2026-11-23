using System.Globalization;

namespace EdenDeck.Backends.Mgba;

/// <summary>
/// Scanline's picture effects for mGBA, as one mGBA shader (manifest.ini + GLSL, mGBA's own format)
/// passed for that run with <c>-C shader=&lt;dir&gt;</c>. mGBA draws with OpenGL, so Scanline's
/// Vulkan layer can't reach it.
/// <list type="bullet">
/// <item>The CRT effect: the same Subtle/Medium/Strong numbers as the layer (layer/src/crt_settings.h),
/// scanlines on the game's own pixel rows. Nothing moves (mGBA gives shaders no clock).</item>
/// <item>The handheld frame: the side bars become an original handheld-style shell around a screen
/// bezel, tinted with the app's accent colour. Purely decorative: no button or d-pad shapes. GBA
/// and GB/GBC games get their own proportions (told apart by the picture size).</item>
/// </list>
/// With the frame on, mGBA is told not to letterbox (<c>lockAspectRatio=0</c>), so the shader gets
/// the whole screen and places the picture itself (at the right shape, sharp pixels).
/// </summary>
public static class MgbaShader
{
    public sealed record Look(int Crt, bool Frame, string? Accent = null)
    {
        public bool Any => CrtLook(Crt) != null || Frame;
    }

    internal sealed record CrtValues(float Strength, float Curve, float Chroma, float Vignette);

    internal static CrtValues? CrtLook(int level) => level switch
    {
        1 => new(0.7f, 0.02f, 0.6f, 0.15f),
        2 => new(1.0f, 0.035f, 1.0f, 0.25f),
        3 => new(1.4f, 0.05f, 1.5f, 0.28f),
        _ => null,
    };

    /// <summary>The frame's colour when the app gives none: a deep indigo.</summary>
    public const string DefaultAccent = "#6c5ce7";

    /// <summary>Where <see cref="Write"/> puts this look's shader (without writing it), or null when there's nothing to draw.</summary>
    public static string? DirFor(string dataDir, Look look)
    {
        if (!look.Any)
        {
            return null;
        }
        var crt = CrtLook(look.Crt) != null ? look.Crt : 0;
        var name = $"scanline-c{crt}" + (look.Frame ? "-f" + Hex(look.Accent) : "");
        return Path.Combine(dataDir, "mgba", "shaders", name);
    }

    /// <summary>Writes the shader for this look and returns its folder, or null when there's nothing to draw.</summary>
    public static string? Write(string dataDir, Look look)
    {
        if (DirFor(dataDir, look) is not { } dir)
        {
            return null;
        }
        Directory.CreateDirectory(dir);
        WriteIfChanged(Path.Combine(dir, "manifest.ini"), Manifest(look));
        WriteIfChanged(Path.Combine(dir, "scanline.fs"), Fragment);
        return dir;
    }

    private static string Hex(string? accent)
    {
        var a = (accent ?? DefaultAccent).TrimStart('#').ToLowerInvariant();
        return a.Length == 6 && a.All(char.IsAsciiHexDigit) ? a : DefaultAccent[1..];
    }

    private static void WriteIfChanged(string path, string text)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != text)
        {
            File.WriteAllText(path, text);
        }
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    internal static string Manifest(Look look)
    {
        var crt = CrtLook(look.Crt) ?? new CrtValues(0, 0, 0, 0);
        var hex = Hex(look.Accent);
        var rgb = Enumerable.Range(0, 3).Select(i => int.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber) / 255f).ToArray();
        string Uniform(string id, float value) => $"""

            [pass.0.uniform.{id}]
            type=float
            default={F(value)}
            min=0.0
            max=2.0

            """;
        var label = (CrtLook(look.Crt) != null ? look.Crt switch { 1 => "CRT Subtle", 2 => "CRT Medium", _ => "CRT Strong" } : null,
            look.Frame ? "frame" : null) switch
        {
            ({ } c, { } f) => $"{c} + {f}",
            ({ } c, null) => c,
            _ => "Frame",
        };
        return $"""
            [shader]
            name=Scanline ({label})
            author=Scanline
            description=Scanline's picture effects, written by the Scanline app.
            passes=1

            [pass.0]
            fragmentShader=scanline.fs
            width=0
            height=0
            filter=1

            """
            + Uniform("strength", crt.Strength) + Uniform("curve", crt.Curve) + Uniform("chroma", crt.Chroma) + Uniform("vignette", crt.Vignette)
            + Uniform("frame", look.Frame ? 1 : 0)
            + $"""

            [pass.0.uniform.accent]
            type=float3
            default[0]={F(rgb[0])}
            default[1]={F(rgb[1])}
            default[2]={F(rgb[2])}

            """;
    }

    // GLSL that compiles under mGBA's GL2 and GL3.2 headers (texture2D, varying, gl_FragColor).
    // Screen shape comes from derivatives (core in desktop GL). Everything is mirrored top/bottom
    // so it looks the same whichever way up the framebuffer is.
    internal const string Fragment = """
        // Scanline picture effects for mGBA. Generated by Scanline; changes are overwritten.
        uniform sampler2D tex;
        uniform vec2 texSize;
        varying vec2 texCoord;
        uniform float strength;
        uniform float curve;
        uniform float chroma;
        uniform float vignette;
        uniform float frame;
        uniform vec3 accent;

        float hash(vec2 p) {
            return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
        }

        // Signed distance to a rounded box (half size b, corner radius r).
        float box(vec2 p, vec2 b, float r) {
            vec2 q = abs(p) - b + r;
            return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
        }

        // The game picture at uv (0..1 over the picture), sharp pixels at any size.
        vec3 picture(vec2 uv, float pxPerTexel) {
            vec2 texel = uv * texSize;
            vec2 base = floor(texel);
            vec2 d = texel - base - 0.5;
            vec2 range = vec2(max(0.5 - 0.5 / pxPerTexel, 0.0));
            vec2 f = (d - clamp(d, -range, range)) * pxPerTexel + 0.5;
            vec2 st = (base + f) / texSize;
            if (chroma > 0.0) {
                float ca = chroma * 0.25 / texSize.x;
                return vec3(texture2D(tex, st + vec2(ca, 0.0)).r, texture2D(tex, st).g, texture2D(tex, st - vec2(ca, 0.0)).b);
            }
            return texture2D(tex, st).rgb;
        }

        // CRT treatment of the picture (no-op when strength is 0).
        vec3 game(vec2 uv, float pxPerTexel) {
            vec2 c = uv * 2.0 - 1.0;
            c *= 1.0 + curve * (c.yx * c.yx);
            vec2 cuv = c * 0.5 + 0.5;
            float inside = step(0.0, cuv.x) * step(cuv.x, 1.0) * step(0.0, cuv.y) * step(cuv.y, 1.0);
            vec3 col = picture(clamp(cuv, 0.0, 1.0), pxPerTexel);
            float row = fract(cuv.y * texSize.y);
            float lines = 1.0 - 0.18 * strength * smoothstep(0.55, 0.95, row);
            float vig = clamp(1.0 - vignette * dot(c * c, c * c) - vignette * 0.4 * dot(c, c), 0.0, 1.0);
            return clamp(col * lines * vig * inside, 0.0, 1.0);
        }

        void main() {
            // Output size in pixels, from how fast texCoord changes per pixel.
            vec2 px = vec2(1.0 / max(abs(dFdx(texCoord.x)), 1e-6), 1.0 / max(abs(dFdy(texCoord.y)), 1e-6));
            float outAspect = px.x / px.y;
            float gameAspect = texSize.x / texSize.y;
            bool handheld = gameAspect < 1.3;   // GB/GBC 10:9, GBA 3:2

            // Picture size (fraction of the output): fit, leaving room for the bezel when framed.
            float fill = frame > 0.5 ? (handheld ? 0.86 : 0.88) : 1.0;
            vec2 hs = 0.5 * fill * (outAspect > gameAspect ? vec2(gameAspect / outAspect, 1.0) : vec2(1.0, outAspect / gameAspect));
            vec2 p = texCoord - 0.5;
            vec2 uv = p / (2.0 * hs) + 0.5;
            float pxPerTexel = 2.0 * hs.y * px.y / texSize.y;

            if (frame < 0.5) {
                bool inPic = abs(p.x) <= hs.x && abs(p.y) <= hs.y;
                gl_FragColor = vec4(inPic ? game(uv, pxPerTexel) : vec3(0.0), 1.0);
                return;
            }

            // Work in pixels from the centre, so shapes stay round on any screen.
            vec2 q = p * px;
            vec2 pic = hs * px;
            float unit = px.y / 100.0;                // 1% of the screen height
            vec3 acc = clamp(accent, 0.0, 1.0);

            // Shell: accent-tinted plastic, lighter across the middle, curving away at the far edges.
            float lum = dot(acc, vec3(0.299, 0.587, 0.114));
            vec3 tint = mix(vec3(lum), acc, 0.75);
            vec3 shell = handheld ? mix(vec3(0.16, 0.17, 0.2), tint, 0.42) : mix(vec3(0.07, 0.07, 0.09), tint, 0.34);
            float across = abs(q.y) / (0.5 * px.y);
            shell *= 1.08 - 0.18 * across * across;
            float edge = abs(q.x) / (0.5 * px.x);
            shell *= 1.0 - 0.45 * smoothstep(0.86, 1.0, edge);              // rolls away from the light
            shell += tint * 0.10 * smoothstep(0.7, 0.86, edge) * (1.0 - smoothstep(0.86, 0.95, edge));  // sheen on the curve
            // Speckled plastic, in little 2-px cells.
            vec2 cell = floor((q + 0.5 * px) / 2.0);
            shell *= 0.965 + 0.07 * hash(cell);
            // Fine moulded ridges where hands would hold it.
            float ridges = 0.5 + 0.5 * cos(q.y / (0.55 * unit) * 3.14159);
            shell *= 1.0 - 0.06 * ridges * smoothstep(0.88, 0.93, edge) * (1.0 - smoothstep(0.97, 1.0, edge));
            if (handheld) {
                // Clear-plastic look: one broad, soft glint across each side.
                float glint = 1.0 - smoothstep(0.0, 9.0 * unit, abs(abs(q.x) * 0.6 + q.y * 0.35 - 0.42 * px.x));
                shell += vec3(0.05) * glint;
            }
            vec3 col = shell;
            // Three slanted accent stripes in each outer corner: 90s gadget trim, nothing to press.
            vec2 corner = vec2(0.5 * px.x - abs(q.x), 0.5 * px.y - abs(q.y)) / unit;   // distance from the corner, in units
            float slant = corner.x + corner.y * 0.9;
            float band = fract(slant / 3.2);
            float trim = (1.0 - smoothstep(0.0, 0.12, abs(band - 0.5) - 0.18)) * step(slant, 9.6) * step(1.2, slant);
            col = mix(col, mix(acc, vec3(1.0), 0.15), 0.6 * trim);

            // Screen bezel: a dark rounded plate around the picture.
            float pad = (handheld ? 4.2 : 3.2) * unit;
            vec2 bez = pic + vec2(pad);
            float bd = box(q, bez, 3.0 * unit);
            vec3 bezel = mix(vec3(0.045, 0.045, 0.055), acc * 0.12, 0.35) * (1.0 - 0.25 * abs(q.y) / bez.y);
            // Raised rim: a thin lit edge and a shadow just outside it.
            col *= 1.0 - 0.35 * (1.0 - smoothstep(0.0, 1.6 * unit, bd)) * step(0.0, bd);
            col = mix(col, bezel, 1.0 - smoothstep(-0.6, 0.6, bd));
            col += acc * 0.25 * (1.0 - smoothstep(0.0, 1.2, abs(bd + 0.6 * unit))) ;
            // Accent pinstripes above and below the picture (decoration only).
            float stripeY = pic.y + pad * 0.5;
            float stripe = (1.0 - smoothstep(0.15 * unit, 0.35 * unit, abs(abs(q.y) - stripeY))) * (1.0 - smoothstep(pic.x * 0.55, pic.x * 0.92, abs(q.x)));
            col = mix(col, acc, 0.8 * stripe * (1.0 - step(0.0, bd)));

            // The picture, with a soft inner shadow from the bezel.
            float pd = box(q, pic, 0.6 * unit);
            if (pd < 0.0) {
                vec3 g = game(uv, pxPerTexel);
                float lip = smoothstep(0.0, 1.2 * unit, -pd);
                col = g * (0.82 + 0.18 * lip);
            }
            gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
        }
        """;
}
