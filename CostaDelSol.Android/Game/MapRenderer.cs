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

    private readonly GameData _data;
    private readonly MapTextures _tex;
    private readonly WorldMapHost _host;

    private int _program;
    private int _width = 1, _height = 1;
    private readonly int[] _textures = new int[4];   // 칸 · 타일 · 팔레트 · 개관
    private int _uOrigin, _uCellsPerPixel, _uViewport, _uCover;
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

            _program = BuildProgram();
            GLES30.GlUseProgram(_program);
            _uOrigin = GLES30.GlGetUniformLocation(_program, "uOrigin");
            _uCellsPerPixel = GLES30.GlGetUniformLocation(_program, "uCellsPerPixel");
            _uViewport = GLES30.GlGetUniformLocation(_program, "uViewport");
            _uCover = GLES30.GlGetUniformLocation(_program, "uCover");
            string[] samplers = ["uCells", "uAtlas", "uPalette", "uOverview"];
            for (int i = 0; i < samplers.Length; i++)
                GLES30.GlUniform1i(GLES30.GlGetUniformLocation(_program, samplers[i]), i);

            int[] vao = new int[1];
            GLES30.GlGenVertexArrays(1, vao, 0);
            GLES30.GlBindVertexArray(vao[0]);

            GLES30.GlPixelStorei(GLES30.GlUnpackAlignment, 1);
            GLES30.GlGenTextures(_textures.Length, _textures, 0);
            Upload(0, GLES30.GlR16ui, MapW, MapH, GLES30.GlRedInteger, GLES30.GlUnsignedShort, _tex.Cells, false, false);
            Upload(1, GLES30.GlR8ui, MapTextures.AtlasSize, MapTextures.AtlasSize, GLES30.GlRedInteger, GLES30.GlUnsignedByte, _tex.Atlas, false, false);
            Upload(2, GLES30.GlRgba8, 256, 1, GLES30.GlRgba, GLES30.GlUnsignedByte, _tex.Palette, false, false);
            Upload(3, GLES30.GlRgba8, MapW, MapH, GLES30.GlRgba, GLES30.GlUnsignedByte, _tex.Overview, true, true);
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
        for (int i = 0; i < _textures.Length; i++)
        {
            GLES30.GlActiveTexture(GLES30.GlTexture0 + i);
            GLES30.GlBindTexture(GLES30.GlTexture2d, _textures[i]);
        }
        GLES30.GlDrawArrays(GLES30.GlTriangles, 0, 3);
    }

    private Dictionary<(int X, int Y), ushort> _applied = [];

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

    private static int BuildProgram()
    {
        int vs = Compile(GLES30.GlVertexShader, VertexSource);
        int fs = Compile(GLES30.GlFragmentShader, FragmentSource);
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
