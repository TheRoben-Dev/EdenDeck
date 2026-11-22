#version 450
// In-game CRT filter. A line-for-line port of the app's own CRT effect
// (src/EdenDeck.App/Scripts/Ui/Scanlines.cs) so a game looks the way the app does at the same level.
// The math runs on the stored (gamma-encoded) values, like the app's does: the frame copy is read
// through a UNORM view, and for sRGB targets the result is converted back to linear before the store.
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 o_color;
layout(set = 0, binding = 0) uniform sampler2D screen;
layout(push_constant) uniform Params {
    vec2 size;      // swapchain extent in pixels
    float t;        // seconds
    float motion;   // 0 = still (Animations off)
    float strength;
    float curve;
    float chroma;
    float vignette;
    float srgb;     // 1 = target is an sRGB format: hand back linear so the store re-encodes our values
} p;

vec3 to_linear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c)); }

float hash(vec2 q) { return fract(sin(dot(q, vec2(127.1, 311.7))) * 43758.5453); }

void main() {
    vec2 size = p.size;
    float t = p.t;
    float scale = max(1.0, floor(size.y / 800.0 + 0.5));
    vec2 c = v_uv * 2.0 - 1.0;
    c *= 1.0 + p.curve * (c.yx * c.yx);
    vec2 uv = c * 0.5 + 0.5;
    float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    float row = floor(uv.y * size.y / scale);
    float wobble = sin(row * 0.045 + t * 2.3) * 0.35 + sin(row * 0.31 - t * 7.1) * 0.15;
    float k = floor(t * 3.0);
    float tear = step(0.985, hash(vec2(k, 0.0))) * exp(-pow((uv.y - hash(vec2(k, 1.0))) * 40.0, 2.0)) * 3.0;
    float shift = (wobble + tear) * p.motion * p.strength * scale / size.x;
    vec2 s = vec2(uv.x + shift, uv.y);
    float ca = p.chroma * scale / size.x;
    vec3 col = vec3(texture(screen, s + vec2(ca, 0.0)).r, texture(screen, s).g, texture(screen, s - vec2(ca, 0.0)).b);
    float lines = mod(uv.y * size.y / scale, 3.0) < 1.0 ? 1.0 - 0.18 * p.strength : 1.0;
    float band = exp(-pow((fract(t / 8.0) * 1.4 - 0.2 - uv.y) * 7.0, 2.0)) * 0.035 * p.strength * p.motion;
    float flicker = 1.0 - 0.02 * p.strength * p.motion * hash(vec2(floor(t * 30.0), 3.0));
    float vig = clamp(1.0 - p.vignette * dot(c * c, c * c) - p.vignette * 0.4 * dot(c, c), 0.0, 1.0);
    vec3 outc = clamp((col * lines * flicker + band) * vig * inside, 0.0, 1.0);
    o_color = vec4(p.srgb > 0.5 ? to_linear(outc) : outc, 1.0);
}
