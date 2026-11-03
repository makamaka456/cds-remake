using Android.App;
using Android.Widget;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Views
{
    /// <summary>
    /// PC 판 지도 그리개(Direct3D)의 자리 — 안드로이드에서는 그릴 것을 <b>들고만</b> 있고, GL 지도와 덧그림 판이
    /// 그대로 가져가 그린다. 이름 · 인자는 PC 판 <c>MapD3DRenderer</c> 와 같아서 원본 지도 엔진(ShipMapHost)을 고치지 않는다.
    /// </summary>
    public sealed class MapD3DRenderer : IDisposable
    {
        public readonly record struct RouteDraw(float X, float Y, bool Active);
        public readonly record struct FolkDraw(float X, float Y, int Frame, float Scale);
        public readonly record struct CloudDraw(float X, float Y, int Frame, float Scale);

        public const int MaxFolk = 16, MaxRoutePoints = 32, FolkFrames = 8, FolkLandFrame = 4, FolkSize = 48, MaxClouds = 6;
        public const int OverlaySize = 16;

        private readonly object _gate = new();
        public object Gate => _gate;

        public uint[]? Sprite { get; private set; }
        public int SpriteVersion { get; private set; }
        public uint[]? Overlay { get; private set; }
        public int OverlayVersion { get; private set; }
        public uint[]? FolkSprites { get; private set; }
        public uint[]? CloudSpriteSheet { get; private set; }
        public FolkDraw[] Folk { get; private set; } = [];
        public RouteDraw[] Route { get; private set; } = [];
        public CloudDraw[] Clouds { get; private set; } = [];
        public uint[]? FlowGrid { get; private set; }
        public ushort[]? RippleTiles { get; private set; }
        public Dictionary<(int X, int Y), ushort> Erased { get; } = [];
        public int ErasedVersion { get; private set; }

        public (float DirX, float DirY, float Speed, float Tick) Ripple { get; set; }
        public bool ShowArrows { get; set; }
        public (float R, float G, float B, float A) Cover { get; set; }

        public void Initialize(byte[] worldData, OceanTiles ocean) { }

        public void SetSprite(ReadOnlySpan<uint> bgra) { lock (_gate) { Sprite = bgra.ToArray(); SpriteVersion++; } }
        public void SetOverlay(ReadOnlySpan<uint> bgra) { lock (_gate) { Overlay = bgra.ToArray(); OverlayVersion++; } }
        public void SetFolkSprites(ReadOnlySpan<uint> bgra) { lock (_gate) FolkSprites = bgra.ToArray(); }
        public void SetCloudSprites(ReadOnlySpan<uint> bgra) { lock (_gate) CloudSpriteSheet = bgra.ToArray(); }
        public void SetFolk(ReadOnlySpan<FolkDraw> folk) { lock (_gate) Folk = folk.ToArray(); }
        public void SetRoute(ReadOnlySpan<RouteDraw> points) { lock (_gate) Route = points.ToArray(); }
        public void SetClouds(ReadOnlySpan<CloudDraw> clouds) { lock (_gate) Clouds = clouds.ToArray(); }
        public void SetFlowGrid(ReadOnlySpan<uint> grid) { lock (_gate) FlowGrid = grid.ToArray(); }
        public void SetRippleTiles(ushort[] nextTiles) { lock (_gate) RippleTiles = nextTiles; }

        /// <summary>
        /// 지도 칸 덧칠(숨긴 도시 · 못 찾은 발견물) — PC 판처럼 <b>통째로 갈아 끼운다</b>: 지도 = 원래 칸 + 이 덧칠.
        /// GL 지도가 앞 덧칠과 견줘 바뀐 칸만 고친다.
        /// </summary>
        public void Erase(IReadOnlyDictionary<(int X, int Y), ushort> patch)
        {
            lock (_gate)
            {
                Erased.Clear();
                foreach (var (k, v) in patch) Erased[k] = v;
                ErasedVersion++;
            }
        }

        public void Dispose() { }
    }
}

namespace CdsHelper.Game.Local.Helpers
{
    /// <summary>
    /// 떠 있는 PC 게임(cds_95)의 메모리에서 함대를 읽는 것(윈도우 전용) — 안드로이드에는 그 게임이 없다.
    /// 늘 「안 붙었다」고 답해 원본 지도 엔진이 제 그림(asset)으로 그리게 한다.
    /// </summary>
    public sealed class GameShipReader : IDisposable
    {
        public const int SpriteW = 48;
        public const int SpriteSize = SpriteW * SpriteW;
        public bool IsAttached => false;
        public bool TryAttach() => false;
        public (double CellX, double CellY)? TryReadCell() => null;
        public byte[]? TryReadSprite() => null;
        public byte[]? TryReadSprite(int heading16, bool onLand, int phase = -1) => null;
        public void Dispose() { }
    }
}
