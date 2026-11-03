using Android.App;
using Android.Widget;
using System.Diagnostics;
using Android.Opengl;
using Java.Nio;
using Javax.Microedition.Khronos.Opengles;
using CdsHelper.Game.UI.Views;
using CdsHelper.Support.Local.Helpers;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;

namespace CostaDelSol.Droid.Game;

/// <summary>
/// 세계지도를 OpenGL ES 3.0 으로 그린다 — 원점 · 배율 · 막은 원본 지도 엔진(WorldMapHost)이 짠 프레임 그대로다. 배 · 닻 · 남의 배 · 항로는 덧그림 판(MapOverlay)이 그린다.
/// </summary>
/// <remarks>
/// PC 판 <c>MapD3DRenderer</c> 의 셰이더를 GLSL 로 옮겼다 — 화면 점 → 칸 → 타일 번호 → 타일 안 점 → 팔레트.
/// 그래서 크게 키워도 게임 원본 그림이 그대로 나온다. 멀리서 볼 때(한 점이 칸 반 이상)는
/// 칸마다 평균색 한 장(개관)을 써서 반짝임을 없앤다. 가로는 2500 으로 접어 경도 ±180 을 잇는다.
/// </remarks>
public sealed class MapRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
{
    private const string VertexSource = """
        #version 300 es
        void main() {
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 300 es
        precision highp float;
        precision highp int;
        precision highp usampler2D;
        uniform usampler2D uCells;
        uniform usampler2D uAtlas;
        uniform sampler2D uPalette;
        uniform sampler2D uOverview;
        uniform vec2 uOrigin;
        uniform float uCellsPerPixel;
        uniform vec2 uViewport;
        uniform vec4 uCover;
        out vec4 outColor;
        void main() {
            vec2 px = vec2(gl_FragCoord.x, uViewport.y - gl_FragCoord.y);
            vec2 w = uOrigin + px * uCellsPerPixel;
            vec4 c;
            if (w.y < 0.0 || w.y >= 1250.0) {
                c = vec4(0.0, 0.0, 0.0, 1.0);
            } else if (uCellsPerPixel >= 0.5) {
                c = texture(uOverview, vec2(w.x / 2500.0, w.y / 1250.0));
            } else {
                float cx = mod(w.x, 2500.0);
                ivec2 cell = ivec2(int(cx), int(w.y));
                uint tile = texelFetch(uCells, cell, 0).r & 16383u;
                ivec2 inTile = ivec2(fract(vec2(cx, w.y)) * 16.0);
                ivec2 at = ivec2(int(tile % 128u) * 16, int(tile / 128u) * 16) + inTile;
                uint index = texelFetch(uAtlas, at, 0).r;
                c = texelFetch(uPalette, ivec2(int(index), 0), 0);
            }
            // 도시 · 사건 동안 지도에 씌우는 남색 막(PC 판 MapD3DRenderer.Cover).
            outColor = vec4(mix(c.rgb, uCover.rgb, uCover.a), 1.0);
        }
        """;

    /// <summary>
    /// PC 판 셰이더의 새 효과를 옮긴 것 — 바다 효과(물결 · 빛 반사 · 해안 물보라 · 수심) · 밝기, 고해상도 바다(B-스플라인 해안선 ·
    /// 해류 무늬 · 나일 강둑), 지형 세부, 점 거르기(PixelArt), 항적 물거품. 함수는 PC 판 HLSL 을 줄마다 옮겼다.
    /// 이 셰이더를 기기가 못 받으면 위의 기본 셰이더로 물러선다(지도는 늘 나온다).
    /// 셰이더 글에는 ASCII 만 쓴다 — 기기 드라이버마다 주석의 다른 글자에서 컴파일이 깨질 수 있다.
    /// </summary>
    private const string EffectsSource = """
        #version 300 es
        precision highp float;
        precision highp int;
        precision highp usampler2D;
        uniform usampler2D uCells;
        uniform usampler2D uAtlas;
        uniform sampler2D uPalette;
        uniform sampler2D uOverview;
        uniform usampler2D uSeaDepth;
        uniform usampler2D uTileKind;
        uniform usampler2D uPalWater;
        uniform vec2 uOrigin;
        uniform float uCellsPerPixel;
        uniform vec2 uViewport;
        uniform vec4 uCover;
        uniform vec4 uSea;      // x time, z brightness, w hi-res sea on
        uniform float uSeaOn;
        uniform vec4 uExtra;    // x pixel filter, y land detail
        uniform vec4 uFlow;     // xy drift, z strength * amount, w tick
        uniform vec2 uFlowDir;
        uniform vec4 uRipple;   // xy dir, z speed, w tick
        uniform vec4 uWake[12];
        uniform float uShipFx;
        out vec4 outColor;

        const float MW = 2500.0;
        const float MH = 1250.0;

        float sat(float x) { return clamp(x, 0.0, 1.0); }
        vec3 sat3(vec3 x) { return clamp(x, 0.0, 1.0); }

        float DepthAt(ivec2 q) {
            q.x = ((q.x % 2500) + 2500) % 2500;
            q.y = clamp(q.y, 0, 1249);
            return float(texelFetch(uSeaDepth, q, 0).r & 15u);
        }
        float RiverAt(ivec2 q) {
            q.x = ((q.x % 2500) + 2500) % 2500;
            q.y = clamp(q.y, 0, 1249);
            return float(texelFetch(uSeaDepth, q, 0).r >> 4);
        }
        float Hash(vec2 p) {
            uvec2 q = uvec2(ivec2(p) + ivec2(65536, 65536));
            uint h = q.x * 1597334677u ^ q.y * 3812015801u;
            h = (h ^ (h >> 15)) * 2246822519u;
            h = h ^ (h >> 13);
            return float(h & 0xFFFFu) / 65535.0;
        }
        float Noise(vec2 p) {
            vec2 i = floor(p);
            vec2 f = p - i;
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(Hash(i), Hash(i + vec2(1.0, 0.0)), u.x),
                       mix(Hash(i + vec2(0.0, 1.0)), Hash(i + vec2(1.0, 1.0)), u.x), u.y);
        }
        float Waves(vec2 p, vec2 dir, float time) {
            vec2 side = vec2(-dir.y, dir.x);
            float h = 0.50 * Noise(p * 1.7 - dir * time * 0.55)
                    + 0.30 * Noise(p * 3.3 + side * time * 0.40 - dir * time * 0.30 + 17.0)
                    + 0.20 * Noise(p * 6.1 - dir * time * 0.85 - side * time * 0.25 + 41.0);
            h += 0.18 * sin(dot(p, dir) * 2.4 - time * 1.3 + Noise(p * 0.6) * 4.0);
            return h;
        }
        uint PalAt(ivec2 gi) {
            int w16 = 40000;
            gi.x = ((gi.x % w16) + w16) % w16;
            gi.y = clamp(gi.y, 0, 19999);
            ivec2 c = gi / 16;
            uint tile = texelFetch(uCells, c, 0).r & 16383u;
            ivec2 org = ivec2(int(tile % 128u), int(tile / 128u));
            return texelFetch(uAtlas, org * 16 + (gi - c * 16), 0).r;
        }
        uint PixelArt(vec2 cell, uint e) {
            vec2 g = cell * 16.0;
            ivec2 gi = ivec2(floor(g));
            vec2 f = g - floor(g);
            uint B = PalAt(gi + ivec2(0, -1));
            uint D = PalAt(gi + ivec2(-1, 0));
            uint F = PalAt(gi + ivec2(1, 0));
            uint H = PalAt(gi + ivec2(0, 1));
            if (B == H || D == F) return e;
            if (f.x + f.y < 0.5 && D == B) return D;
            if ((1.0 - f.x) + f.y < 0.5 && B == F) return F;
            if (f.x + (1.0 - f.y) < 0.5 && D == H) return D;
            if ((1.0 - f.x) + (1.0 - f.y) < 0.5 && H == F) return F;
            return e;
        }
        uint TexelPal(vec2 g) { return PalAt(ivec2(floor(g))); }
        float WaterOf(uint pal) { return float(texelFetch(uPalWater, ivec2(int(pal), 0), 0).r); }
        vec3 PalColor(uint pal) { return texelFetch(uPalette, ivec2(int(pal), 0), 0).rgb; }
        vec4 BSpline(float t) {
            float t2 = t * t, t3 = t2 * t;
            return vec4((1.0 - t) * (1.0 - t) * (1.0 - t),
                        3.0 * t3 - 6.0 * t2 + 4.0,
                        -3.0 * t3 + 3.0 * t2 + 3.0 * t + 1.0,
                        t3) / 6.0;
        }
        vec3 HiResSeaCore(vec3 col, vec2 cell, uint pal) {
            vec2 g = cell * 16.0 - 0.5;
            vec2 b = floor(g);
            vec2 f = g - b;
            vec4 wx = BSpline(f.x);
            vec4 wy = BSpline(f.y);
            vec3 landSum = vec3(0.0);
            float landW = 0.0;
            for (int y = 1; y <= 2; y++) {
                for (int x = 1; x <= 2; x++) {
                    uint p = TexelPal(b + vec2(float(x - 1), float(y - 1)));
                    float w = wx[x] * wy[y];
                    if (WaterOf(p) < 0.5) { landSum += PalColor(p) * w; landW += w; }
                }
            }
            vec2 gc = cell * 8.0 - 0.5;
            vec2 bc = floor(gc);
            vec2 fc = gc - bc;
            vec4 cx4 = BSpline(fc.x);
            vec4 cy4 = BSpline(fc.y);
            float m = 0.0;
            for (int yy = 0; yy < 4; yy++) {
                for (int xx = 0; xx < 4; xx++) {
                    vec2 o = (bc + vec2(float(xx - 1), float(yy - 1))) * 2.0;
                    float wet = 0.25 * (WaterOf(TexelPal(o)) + WaterOf(TexelPal(o + vec2(1.0, 0.0)))
                                      + WaterOf(TexelPal(o + vec2(0.0, 1.0))) + WaterOf(TexelPal(o + vec2(1.0, 1.0))));
                    m += wet * cx4[xx] * cy4[yy];
                }
            }
            if (m <= 0.02) return col;
            float edge = smoothstep(0.42, 0.58, m);
            vec3 land = col;
            if (WaterOf(pal) > 0.5 && landW > 1e-4) land = landSum / landW;
            if (RiverAt(ivec2(floor(cell))) > 14.5) {
                float jag = 0.55 + 0.45 * Noise(cell * 22.0) + 0.25 * Noise(cell * 61.0);
                float rim = smoothstep(0.03, 0.22, m * jag) * (1.0 - edge);
                float lip = smoothstep(0.20, 0.40, m);
                vec3 cliff = mix(vec3(0.42, 0.27, 0.16), vec3(0.18, 0.10, 0.05), lip);
                land = mix(land, cliff, sat(rim));
            }
            vec2 cb = cell - 0.5;
            ivec2 cq = ivec2(floor(cb));
            vec2 ct = cb - floor(cb);
            float d = mix(mix(DepthAt(cq), DepthAt(cq + ivec2(1, 0)), ct.x),
                          mix(DepthAt(cq + ivec2(0, 1)), DepthAt(cq + ivec2(1, 1)), ct.x), ct.y);
            vec3 tone = mix(vec3(0.31, 0.44, 0.52), vec3(0.21, 0.34, 0.43), sat((d - 1.0) / 5.0));
            float t = uSea.x;
            vec2 drift = uFlow.xy;
            float grain = 0.55 * Noise((cell - drift * 0.25) * 21.0 + vec2(t * 0.35, t * 0.10))
                        + 0.45 * Noise((cell - drift * 0.5) * 8.5 - vec2(t * 0.15, t * 0.22) + 7.0);
            vec3 sea = tone * (0.92 + 0.16 * grain);
            float flow = uFlow.z;
            if (flow > 0.01) {
                vec2 fd = uFlowDir;
                vec2 q = cell - drift;
                float streak = 0.10 * Noise((q - fd * 0.66) * 7.0)
                             + 0.15 * Noise((q - fd * 0.44) * 7.0)
                             + 0.17 * Noise((q - fd * 0.22) * 7.0)
                             + 0.16 * Noise(q * 7.0)
                             + 0.17 * Noise((q + fd * 0.22) * 7.0)
                             + 0.15 * Noise((q + fd * 0.44) * 7.0)
                             + 0.10 * Noise((q + fd * 0.66) * 7.0);
                streak = sat((streak - 0.5) * 2.6 + 0.5);
                sea *= 1.0 + flow * 0.34 * (streak - 0.5);
                sea += flow * 0.09 * smoothstep(0.62, 0.90, streak);
                float band = (dot(uRipple.xy, cell) - uRipple.z * uFlow.w * 16.0) / 64.0;
                sea *= 1.0 + flow * 0.06 * sin(band * 0.3926991);
            }
            return mix(land, sea, edge);
        }
        vec3 HiResSea(vec3 col, vec2 cell, uint pal) {
            vec2 rb = cell - 0.5;
            ivec2 rq = ivec2(floor(rb));
            vec2 rt = rb - floor(rb);
            float river = mix(mix(RiverAt(rq), RiverAt(rq + ivec2(1, 0)), rt.x),
                              mix(RiverAt(rq + ivec2(0, 1)), RiverAt(rq + ivec2(1, 1)), rt.x), rt.y);
            float keep = sat((river - 0.5) / 3.0);
            if (keep <= 0.0) return col;
            return mix(col, HiResSeaCore(col, cell, pal), keep);
        }
        float CoastNear(vec2 cell) {
            vec2 g = cell * 16.0;
            float land = 0.0;
            for (int k = 0; k < 8; k++) {
                float a = float(k) * 0.785398;
                vec2 o = vec2(cos(a), sin(a)) * 3.0;
                land += 1.0 - WaterOf(TexelPal(g + o));
            }
            land += 2.0 * (1.0 - WaterOf(TexelPal(g + vec2(1.5, 0.0))));
            land += 2.0 * (1.0 - WaterOf(TexelPal(g + vec2(-1.5, 0.0))));
            land += 2.0 * (1.0 - WaterOf(TexelPal(g + vec2(0.0, 1.5))));
            land += 2.0 * (1.0 - WaterOf(TexelPal(g + vec2(0.0, -1.5))));
            return sat(land / 6.0);
        }
        vec3 SeaShade(vec3 col, vec2 cell, bool fullRes) {
            float water = sat((col.b - max(col.r, col.g) + 0.01) * 14.0);
            if (water <= 0.0) return col;
            vec3 base = col;
            vec2 b = cell - 0.5;
            ivec2 q = ivec2(floor(b));
            vec2 t = b - floor(b);
            float d = mix(mix(DepthAt(q), DepthAt(q + ivec2(1, 0)), t.x),
                          mix(DepthAt(q + ivec2(0, 1)), DepthAt(q + ivec2(1, 1)), t.x), t.y);
            float time = uSea.x;
            float fine = sat((0.30 - uCellsPerPixel) / 0.20);
            vec2 dir = uRipple.xy;
            dir = dot(dir, dir) > 1e-4 ? normalize(dir) : vec2(0.94, 0.34);
            vec2 p = cell;
            const float e = 0.05;
            float h0 = Waves(p, dir, time);
            float hx = Waves(p + vec2(e, 0.0), dir, time);
            float hy = Waves(p + vec2(0.0, e), dir, time);
            vec2 g = vec2(hx - h0, hy - h0) / e * 0.11;
            vec3 n = normalize(vec3(-g * fine, 1.0));
            vec3 L = normalize(vec3(-0.45, -0.55, 0.70));
            float diff = dot(n, L) / L.z;
            vec3 H = normalize(L + vec3(0.0, 0.0, 1.0));
            float glint = pow(sat(dot(n, H)), 90.0) * smoothstep(0.55, 0.85, h0);
            float spec = glint * 0.45 * fine;
            float deep = sat((d - 1.0) / 10.0);
            col *= mix(1.16, 0.98, deep);
            col *= mix(1.0, diff, 0.50);
            col += spec;
            if (fullRes) {
                float shore = sat(1.7 - d) * CoastNear(cell);
                float foam = shore * smoothstep(0.35, 0.75,
                    0.5 + 0.5 * sin(time * 1.4 - d * 6.0 + Noise(p * 2.0 + time * 0.2) * 5.0));
                col = mix(col, vec3(0.93, 0.96, 0.98), foam * 0.35 * sat(1.0 - uCellsPerPixel * 2.0));
            }
            col *= uSea.z;
            return mix(base, sat3(col), water);
        }
        vec3 LandDetail(vec3 col, vec2 cell, uint tile) {
            if (col.b - max(col.r, col.g) > 0.01) return col;
            uint kind = texelFetch(uTileKind, ivec2(int(tile % 128u), int(tile / 128u)), 0).r;
            if (kind <= 1u || kind == 7u) return col;
            float px = 1.0 / max(uCellsPerPixel, 1e-4);
            float amount = sat((px - 4.0) / 12.0);
            if (amount <= 0.0) return col;
            vec2 p = cell;
            float m;
            if (kind == 4u) {
                float dune = 0.5 + 0.5 * sin(dot(p, vec2(0.8, 0.6)) * 14.0 + Noise(p * 3.0) * 6.0);
                m = 0.96 + 0.05 * dune + 0.04 * (Noise(p * 90.0) - 0.5);
            } else if (kind == 3u) {
                float r = 1.0 - abs(Noise(p * 26.0) * 2.0 - 1.0);
                m = 0.86 + 0.22 * r * r + 0.08 * (Noise(p * 95.0) - 0.5);
            } else if (kind == 6u) {
                float leaf = smoothstep(0.30, 0.80, Noise(p * 48.0));
                m = 0.84 + 0.26 * leaf + 0.06 * (Noise(p * 120.0) - 0.5);
            } else {
                m = 0.96 + 0.06 * Noise(p * 14.0) + 0.04 * (Noise(p * 70.0) - 0.5);
            }
            return sat3(col * mix(1.0, m, amount));
        }
        vec3 WakeOver(vec3 col, vec2 px, vec2 cell, float wet) {
            if (uShipFx < 0.5 || wet <= 0.0) return col;
            float pxPerCell = 1.0 / max(uCellsPerPixel, 1e-5);
            float foam = 0.0;
            for (int k = 0; k < 11; k++) {
                if (uWake[k].w <= 0.0 || uWake[k + 1].w <= 0.0) continue;
                vec2 a = uWake[k].xy;
                vec2 ab = uWake[k + 1].xy - a;
                float t = sat(dot(px - a, ab) / max(dot(ab, ab), 1e-5));
                float d = distance(px, a + ab * t) / pxPerCell;
                float age = mix(uWake[k].z, uWake[k + 1].z, t);
                float r = d / (0.22 + 1.05 * age);
                if (r >= 1.0) continue;
                float rim = smoothstep(0.45, 0.85, r) * (1.0 - smoothstep(0.85, 1.0, r));
                float churn = (1.0 - r) * 0.35;
                float life = (1.0 - age) * (1.0 - age);
                foam = max(foam, (rim + churn) * life);
            }
            if (foam <= 0.0) return col;
            float n = 0.55 + 0.45 * Noise(cell * 13.0 + uSea.x * 0.6);
            return mix(col, vec3(0.90, 0.95, 0.97), sat(foam * n * 0.62) * wet);
        }
        void main() {
            vec2 px = vec2(gl_FragCoord.x, uViewport.y - gl_FragCoord.y);
            vec2 w = uOrigin + px * uCellsPerPixel;
            if (w.y < 0.0 || w.y >= MH) { outColor = vec4(0.0, 0.0, 0.0, 1.0); return; }
            vec2 cell = vec2(mod(w.x, MW), clamp(w.y, 0.0, MH - 0.001));
            float pxPerCell = 1.0 / max(uCellsPerPixel, 1e-5);
            bool fullRes = uCellsPerPixel < 0.5;
            vec4 c;
            uint pal = 0u;
            uint tile = 0u;
            if (!fullRes) {
                c = texture(uOverview, vec2(w.x / MW, w.y / MH));
            } else {
                ivec2 ci = ivec2(cell);
                tile = texelFetch(uCells, ci, 0).r & 16383u;
                ivec2 inTile = ivec2(fract(cell) * 16.0);
                ivec2 at = ivec2(int(tile % 128u) * 16, int(tile / 128u) * 16) + inTile;
                pal = texelFetch(uAtlas, at, 0).r;
                if (uExtra.x > 0.5) pal = PixelArt(cell, pal);
                c = texelFetch(uPalette, ivec2(int(pal), 0), 0);
            }
            bool detail = fullRes && pxPerCell > 4.0;
            if (uExtra.y > 0.5 && detail) c.rgb = LandDetail(c.rgb, cell, tile);
            if (uSea.w > 0.5 && detail) c.rgb = HiResSea(c.rgb, cell, pal);
            if (uSeaOn > 0.5) c.rgb = SeaShade(c.rgb, cell, fullRes);
            float wet = sat((c.b - max(c.r, c.g)) * 14.0 + 0.14);
            if (fullRes) c.rgb = WakeOver(c.rgb, px, cell, wet);
            outColor = vec4(mix(c.rgb, uCover.rgb, uCover.a), 1.0);
        }
        """;

    private readonly GameData _data;
    private readonly MapTextures _tex;
    private readonly WorldMapHost _host;

    private int _program;
    private int _width = 1, _height = 1;
    private readonly int[] _textures = new int[7];   // 칸 · 타일 · 팔레트 · 개관 · 수심/강 · 타일 갈래 · 물 팔레트
    private int _uOrigin, _uCellsPerPixel, _uViewport, _uCover;
    private int _uSea, _uSeaOn, _uExtra, _uFlow, _uFlowDir, _uRipple, _uWake, _uShipFx;
    private bool _effects;
    private int _effectSeen = -1;
    private readonly float[] _wake = new float[MapD3DRenderer.MaxWake * 4];

    /// <summary>효과 셰이더를 못 받아 기본 셰이더로 그리는 중이면 그 까닭.</summary>
    public string EffectsError { get; private set; } = "";
    private int _erasedSeen;

    public Action<string>? Failed;

    /// <summary>GL 판 크기(픽셀) — 지도 엔진이 그 크기로 프레임을 짠다.</summary>
    public (int W, int H) Size => (_width, _height);

    public MapRenderer(GameData data, MapTextures textures, WorldMapHost host)
    {
        _data = data;
        _tex = textures;
        _host = host;
    }

    public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
    {
        try
        {
            int[] max = new int[1];
            GLES30.GlGetIntegerv(GLES30.GlMaxTextureSize, max, 0);
            if (max[0] < MapW)
                throw new InvalidOperationException($"이 기기는 텍스처를 {max[0]}px 까지만 받습니다(필요 {MapW}px)");

            // 효과 셰이더부터 걸어 보고, 기기가 못 받으면 기본 셰이더로 물러선다.
            try { _program = BuildProgram(EffectsSource); _effects = true; }
            catch (InvalidOperationException ex)
            {
                EffectsError = ex.Message;
                Android.Util.Log.Warn("CostaDelSol", "지도 효과 셰이더를 못 받아 기본 셰이더로 그립니다: " + ex.Message);
                _program = BuildProgram(FragmentSource);
                _effects = false;
            }
            GLES30.GlUseProgram(_program);
            _uOrigin = GLES30.GlGetUniformLocation(_program, "uOrigin");
            _uCellsPerPixel = GLES30.GlGetUniformLocation(_program, "uCellsPerPixel");
            _uViewport = GLES30.GlGetUniformLocation(_program, "uViewport");
            _uCover = GLES30.GlGetUniformLocation(_program, "uCover");
            string[] samplers = ["uCells", "uAtlas", "uPalette", "uOverview", "uSeaDepth", "uTileKind", "uPalWater"];
            for (int i = 0; i < samplers.Length; i++)
                GLES30.GlUniform1i(GLES30.GlGetUniformLocation(_program, samplers[i]), i);
            _uSea = GLES30.GlGetUniformLocation(_program, "uSea");
            _uSeaOn = GLES30.GlGetUniformLocation(_program, "uSeaOn");
            _uExtra = GLES30.GlGetUniformLocation(_program, "uExtra");
            _uFlow = GLES30.GlGetUniformLocation(_program, "uFlow");
            _uFlowDir = GLES30.GlGetUniformLocation(_program, "uFlowDir");
            _uRipple = GLES30.GlGetUniformLocation(_program, "uRipple");
            _uWake = GLES30.GlGetUniformLocation(_program, "uWake");
            _uShipFx = GLES30.GlGetUniformLocation(_program, "uShipFx");

            int[] vao = new int[1];
            GLES30.GlGenVertexArrays(1, vao, 0);
            GLES30.GlBindVertexArray(vao[0]);

            GLES30.GlPixelStorei(GLES30.GlUnpackAlignment, 1);
            GLES30.GlGenTextures(_textures.Length, _textures, 0);
            Upload(0, GLES30.GlR16ui, MapW, MapH, GLES30.GlRedInteger, GLES30.GlUnsignedShort, _tex.Cells, false, false);
            Upload(1, GLES30.GlR8ui, MapTextures.AtlasSize, MapTextures.AtlasSize, GLES30.GlRedInteger, GLES30.GlUnsignedByte, _tex.Atlas, false, false);
            Upload(2, GLES30.GlRgba8, 256, 1, GLES30.GlRgba, GLES30.GlUnsignedByte, _tex.Palette, false, false);
            Upload(3, GLES30.GlRgba8, MapW, MapH, GLES30.GlRgba, GLES30.GlUnsignedByte, _tex.Overview, true, true);
            // 효과 자료는 엔진이 켤 때 지어 준다 — 그 전까지는 한 점짜리 빈 판을 걸어 둔다(표본기가 비면 안 된다).
            Upload(4, GLES30.GlR8ui, 1, 1, GLES30.GlRedInteger, GLES30.GlUnsignedByte, [0], false, false);
            Upload(5, GLES30.GlR8ui, 1, 1, GLES30.GlRedInteger, GLES30.GlUnsignedByte, [0], false, false);
            Upload(6, GLES30.GlR8ui, 1, 1, GLES30.GlRedInteger, GLES30.GlUnsignedByte, [0], false, false);
            _effectSeen = -1;
            _erasedSeen = 0;
            _applied = [];
            CheckError("텍스처 올리기");
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex.Message);
        }
    }

    public const int MapW = 2500, MapH = 1250;

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        GLES30.GlViewport(0, 0, _width, _height);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        var view = _host.View;
        if (view.Width <= 0) { GLES30.GlClearColor(0, 0, 0, 1); GLES30.GlClear(GLES30.GlColorBufferBit); return; }
        ApplyWorld();
        ApplyErased();

        // float 로 넘기기 전에 가로를 한 바퀴 안으로 접어 둔다(정밀도).
        double originX = view.OriginX - Math.Floor(view.OriginX / MapW) * MapW;
        // 엔진이 짠 크기와 GL 판 크기가 다르면(바뀐 직후) 배율을 맞춘다.
        double cpp = view.CellsPerPixel * view.Width / Math.Max(1, _width);

        GLES30.GlUseProgram(_program);
        GLES30.GlUniform2f(_uOrigin, (float)originX, (float)view.OriginY);
        GLES30.GlUniform1f(_uCellsPerPixel, (float)cpp);
        GLES30.GlUniform2f(_uViewport, _width, _height);
        GLES30.GlUniform4f(_uCover, view.Cover.R, view.Cover.G, view.Cover.B, view.Cover.A);
        if (_effects) ApplyEffects();
        for (int i = 0; i < _textures.Length; i++)
        {
            GLES30.GlActiveTexture(GLES30.GlTexture0 + i);
            GLES30.GlBindTexture(GLES30.GlTexture2d, _textures[i]);
        }
        GLES30.GlDrawArrays(GLES30.GlTriangles, 0, 3);
    }

    private Dictionary<(int X, int Y), ushort> _applied = [];
    private int _worldSeen;

    /// <summary>
    /// 효과 값을 넘긴다(PC 판 FrameCb 의 Sea · SeaOn · Extra · Flow · FlowDir · Ripple · Wake · Ship 과 같은 값).
    /// 엔진이 자료(수심 · 타일 갈래 · 물 팔레트)를 새로 지었으면 텍스처로 올린다.
    /// </summary>
    private void ApplyEffects()
    {
        var r = _host.Layers;
        byte[]? depth, kinds, water;
        int version;
        bool seaOn, hiSea, land;
        lock (r.Gate)
        {
            version = r.EffectDataVersion;
            depth = r.SeaDepth; kinds = r.TileKinds; water = r.WaterPalette;
            Array.Copy(r.Wake, _wake, _wake.Length);
            seaOn = r.SeaEffect && r.SeaDepthReady;
            hiSea = r.HiResSea && r.WaterPaletteReady && r.SeaDepthReady;
            land = r.LandDetail && r.TileKindsReady;
        }
        if (version != _effectSeen)
        {
            _effectSeen = version;
            if (depth is { Length: MapW * MapH })
                Upload(4, GLES30.GlR8ui, MapW, MapH, GLES30.GlRedInteger, GLES30.GlUnsignedByte, depth, false, false);
            if (kinds is { Length: MapTextures.AtlasTiles * MapTextures.AtlasTiles })
                Upload(5, GLES30.GlR8ui, MapTextures.AtlasTiles, MapTextures.AtlasTiles, GLES30.GlRedInteger, GLES30.GlUnsignedByte, kinds, false, false);
            if (water is { Length: 256 })
                Upload(6, GLES30.GlR8ui, 256, 1, GLES30.GlRedInteger, GLES30.GlUnsignedByte, water, false, false);
            GLES30.GlUseProgram(_program);
        }
        var drift = r.FlowDrift;
        var ripple = r.Ripple;
        GLES30.GlUniform4f(_uSea, r.SeaTime, r.SmoothClouds ? 1 : 0, r.SeaBrightness, hiSea ? 1 : 0);
        GLES30.GlUniform1f(_uSeaOn, seaOn ? 1 : 0);
        GLES30.GlUniform4f(_uExtra, r.PixelFilter ? 1 : 0, land ? 1 : 0, 0, 0);
        GLES30.GlUniform4f(_uFlow, drift.X, drift.Y, drift.Strength * r.FlowAmount, drift.Tick);
        GLES30.GlUniform2f(_uFlowDir, drift.DirX, drift.DirY);
        GLES30.GlUniform4f(_uRipple, ripple.DirX, ripple.DirY, ripple.Speed, ripple.Tick);
        GLES30.GlUniform4fv(_uWake, MapD3DRenderer.MaxWake, _wake, 0);
        GLES30.GlUniform1f(_uShipFx, r.ShipFx ? 1 : 0);
    }

    /// <summary>
    /// 지도가 바뀐 칸(수에즈 운하 따위, <c>MapD3DRenderer.UpdateWorld</c>)을 원래 칸으로 삼는다 — 덧칠(숨긴 도시)이
    /// 걸린 칸은 덧칠이 이기므로 바탕만 고쳐 두고, 아니면 텍스처도 고친다.
    /// </summary>
    private void ApplyWorld()
    {
        var layers = _host.Layers;
        List<((int X, int Y) At, ushort Tile)> changed;
        lock (layers.Gate)
        {
            if (layers.WorldVersion == _worldSeen) return;
            _worldSeen = layers.WorldVersion;
            changed = [.. layers.WorldPatch.Select(p => (p.Key, p.Value))];
        }
        GLES30.GlActiveTexture(GLES30.GlTexture0);
        GLES30.GlBindTexture(GLES30.GlTexture2d, _textures[0]);
        var one = ByteBuffer.AllocateDirect(2)!;
        one.Order(ByteOrder.NativeOrder()!);
        foreach (var ((x, y), tile) in changed)
        {
            if (x < 0 || y < 0 || x >= MapW || y >= MapH) continue;
            int o = (y * MapW + x) * 2;
            _tex.Cells[o] = (byte)tile;
            _tex.Cells[o + 1] = (byte)(tile >> 8);
            if (_applied.ContainsKey((x, y))) continue;
            one.Position(0);
            one.PutShort((short)tile);
            one.Position(0);
            GLES30.GlTexSubImage2D(GLES30.GlTexture2d, 0, x, y, 1, 1, GLES30.GlRedInteger, GLES30.GlUnsignedShort, one);
        }
    }

    /// <summary>엔진이 덧칠한 칸(숨긴 · 드러낸 도시)을 칸 텍스처에 반영한다 — 빠진 칸은 원래 칸으로 되돌린다.</summary>
    private void ApplyErased()
    {
        var layers = _host.Layers;
        Dictionary<(int X, int Y), ushort> next;
        lock (layers.Gate)
        {
            if (layers.ErasedVersion == _erasedSeen) return;
            _erasedSeen = layers.ErasedVersion;
            next = new(layers.Erased);
        }
        GLES30.GlActiveTexture(GLES30.GlTexture0);
        GLES30.GlBindTexture(GLES30.GlTexture2d, _textures[0]);
        var one = ByteBuffer.AllocateDirect(2)!;
        one.Order(ByteOrder.NativeOrder()!);
        void Put(int x, int y, ushort v)
        {
            if (x < 0 || y < 0 || x >= MapW || y >= MapH) return;
            one.Position(0);
            one.PutShort((short)(v & OceanTiles.TileMask));
            one.Position(0);
            GLES30.GlTexSubImage2D(GLES30.GlTexture2d, 0, x, y, 1, 1, GLES30.GlRedInteger, GLES30.GlUnsignedShort, one);
        }
        foreach (var (x, y) in _applied.Keys)
            if (!next.ContainsKey((x, y)) && x >= 0 && y >= 0 && x < MapW && y < MapH)
            {
                int o = (y * MapW + x) * 2;
                Put(x, y, (ushort)(_tex.Cells[o] | (_tex.Cells[o + 1] << 8)));
            }
        foreach (var ((x, y), v) in next)
            if (!_applied.TryGetValue((x, y), out ushort had) || had != v) Put(x, y, v);
        _applied = next;
    }

    private void Upload(int slot, int internalFormat, int width, int height, int format, int type,
                        byte[] pixels, bool linear, bool repeatX)
    {
        GLES30.GlActiveTexture(GLES30.GlTexture0 + slot);
        GLES30.GlBindTexture(GLES30.GlTexture2d, _textures[slot]);
        int filter = linear ? GLES30.GlLinear : GLES30.GlNearest;
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, filter);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, filter);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapS, repeatX ? GLES30.GlRepeat : GLES30.GlClampToEdge);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapT, GLES30.GlClampToEdge);
        GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, internalFormat, width, height, 0, format, type, Direct(pixels));
    }

    private static ByteBuffer Direct(byte[] bytes)
    {
        var buffer = ByteBuffer.AllocateDirect(bytes.Length)!;
        buffer.Order(ByteOrder.NativeOrder()!);
        buffer.Put(bytes);
        buffer.Position(0);
        return buffer;
    }

    private static int BuildProgram(string fragment)
    {
        int vs = Compile(GLES30.GlVertexShader, VertexSource);
        int fs = Compile(GLES30.GlFragmentShader, fragment);
        int program = GLES30.GlCreateProgram();
        GLES30.GlAttachShader(program, vs);
        GLES30.GlAttachShader(program, fs);
        GLES30.GlLinkProgram(program);
        int[] ok = new int[1];
        GLES30.GlGetProgramiv(program, GLES30.GlLinkStatus, ok, 0);
        if (ok[0] == 0) throw new InvalidOperationException("셰이더 연결 실패: " + GLES30.GlGetProgramInfoLog(program));
        return program;
    }

    private static int Compile(int kind, string source)
    {
        int shader = GLES30.GlCreateShader(kind);
        GLES30.GlShaderSource(shader, source);
        GLES30.GlCompileShader(shader);
        int[] ok = new int[1];
        GLES30.GlGetShaderiv(shader, GLES30.GlCompileStatus, ok, 0);
        if (ok[0] == 0) throw new InvalidOperationException("셰이더 컴파일 실패: " + GLES30.GlGetShaderInfoLog(shader));
        return shader;
    }

    private static void CheckError(string what)
    {
        int error = GLES30.GlGetError();
        if (error != GLES30.GlNoError) throw new InvalidOperationException($"{what} 중 GL 오류 0x{error:X}");
    }
}
