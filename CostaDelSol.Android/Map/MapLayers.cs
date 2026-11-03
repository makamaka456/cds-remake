using Android.App;
using Android.Widget;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Views
{
    /// <summary>
    /// PC 판 지도 그리개(Direct3D)의 자리 — 안드로이드에서는 그릴 것을 <b>들고만</b> 있고, GL 지도와 덧그림 판이
    /// 그대로 가져가 그린다. 이름 · 인자는 PC 판 <c>MapD3DRenderer</c> 와 같아서 원본 지도 엔진(WorldMapHost)을 고치지 않는다.
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

        public void Initialize(byte[] worldData, OceanTiles ocean) { lock (_gate) _world = (byte[])worldData.Clone(); }

        // ── 바뀐 지도(수에즈 운하 따위) — PC 판 UpdateWorld 는 칸 텍스처를 통째로 다시 올린다.
        //    안드로이드는 앞 지도와 견줘 바뀐 칸만 모아 두고, GL 지도(MapRenderer.ApplyWorld)가 그 칸을 고친다.
        private byte[]? _world;

        /// <summary>원래 지도에서 바뀐 칸 — (칸 x, 칸 y) → 타일 번호.</summary>
        public Dictionary<(int X, int Y), ushort> WorldPatch { get; } = [];
        public int WorldVersion { get; private set; }

        public void UpdateWorld(byte[] world)
        {
            lock (_gate)
            {
                if (_world is { } old && old.Length == world.Length)
                {
                    int stride = global::CostaDelSol.Droid.WorldMapImage.RawStride, half = global::CostaDelSol.Droid.WorldMapImage.CellW;
                    for (int i = 0; i + 1 < world.Length; i += 2)
                    {
                        if (old[i] == world[i] && old[i + 1] == world[i + 1]) continue;
                        int row = i / stride, col = (i % stride) / 2;
                        if (col >= half) continue;
                        int x = col + (row % 2 == 1 ? half : 0), y = row / 2;
                        WorldPatch[(x, y)] = (ushort)((world[i] | (world[i + 1] << 8)) & OceanTiles.TileMask);
                    }
                }
                _world = (byte[])world.Clone();
                WorldVersion++;
            }
        }

        // ── v1.1.x 의 지도 그림 새 기능(바다 효과 · 항적 · 도시 스프라이트 · 고해상 바다 · 배 출렁임 …).
        //    안드로이드 GL 지도는 아직 이 기능들을 그리지 않는다 — 값만 받아 두고, 「준비됨」은 받은 뒤 참이 되어
        //    엔진이 자료를 거듭 올리지 않게 한다. 나중 판에서 GL 지도에 하나씩 옮긴다.
        public const int MaxWake = 12;
        public const int FolkSkins = 4, FolkWays = 8;
        public const int FolkLandPhases = CdsHelper.Game.Local.Helpers.ShipSprites.WalkPhases;
        public const int CitySpriteSide = 48, CitySpritesPerRow = 16;

        public bool ShipFx { get; set; }
        public float ShipBob { get; set; }
        public (float X, float Y, float DirX, float DirY, float Strength, float Tick) FlowDrift { get; set; }
        public float FlowAmount { get; set; } = 0.5f;
        public bool SeaEffect { get; set; }
        public float SeaTime { get; set; }
        public float SeaBrightness { get; set; } = 1f;
        public bool SmoothClouds { get; set; } = true;
        public bool HiResSea { get; set; }
        public bool PixelFilter { get; set; }
        public bool LandDetail { get; set; }
        public bool Software => false;
        public bool CitySprites { get; private set; }
        public bool CityArtReady { get; private set; }
        public bool TileKindsReady { get; private set; }
        public bool WaterPaletteReady { get; private set; }
        public bool SeaDepthReady { get; private set; }

        public void SetWake(ReadOnlySpan<(float X, float Y, float Age)> points) { }
        public void SetShipHi(uint[]? premultiplied, int side) { }
        public void SetCityArt(uint[] atlas, int atlasW, int atlasH, (int X, int Y, ushort[] Block, int Width)[] blocks) => CityArtReady = true;
        public void SetCityHiRes(uint[] atlas, int atlasW, int atlasH, float[] info, int count) { }
        public void SetCitySprites(bool on) => CitySprites = on;
        public void SetHiddenCities(IEnumerable<int> hidden) { }
        public void SetTileKinds(byte[] kinds) => TileKindsReady = true;
        public void SetWaterPalette(byte[] water) => WaterPaletteReady = true;
        public void SetSeaDepth(byte[] depth) => SeaDepthReady = true;

        public void SetSprite(ReadOnlySpan<uint> bgra) { lock (_gate) { Sprite = bgra.ToArray(); SpriteVersion++; } }
        public void SetOverlay(ReadOnlySpan<uint> bgra) { lock (_gate) { Overlay = bgra.ToArray(); OverlayVersion++; } }
        public void SetFolkSprites(ReadOnlySpan<uint> bgra) { lock (_gate) FolkSprites = bgra.ToArray(); }
        public void SetCloudSprites(ReadOnlySpan<uint> bgra) { lock (_gate) CloudSpriteSheet = bgra.ToArray(); }
        public void SetFolk(ReadOnlySpan<FolkDraw> folk) { lock (_gate) Folk = folk.ToArray(); }
        public void SetRoute(ReadOnlySpan<RouteDraw> points, float width = 1, float opacity = 1) { lock (_gate) Route = points.ToArray(); }
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
