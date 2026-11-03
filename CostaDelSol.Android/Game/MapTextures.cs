using Android.App;
using Android.Widget;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CostaDelSol.Droid.Game;

/// <summary>
/// GPU 에 올릴 지도 자료를 CPU 에서 미리 지어 둔다. PC 판 <c>MapD3DRenderer</c> 의 텍스처 구성과 같다.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>칸 지도 — 2500x1250 u16. 칸마다 OCEAN 타일 번호(하위 14비트).</item>
///   <item>타일 그림 — 2048x2048 u8. 16x16 타일 16,384장을 128x128 격자로 편 것.</item>
///   <item>팔레트 — 256색 RGBA.</item>
///   <item>개관 — 2500x1250 RGBA. 칸 하나를 타일 평균색 하나로. 멀리서 볼 때 쓴다(PC 판 Avg1).</item>
/// </list>
/// 아직 서지 않은 도시(1480년 기준)는 PC 판 <c>HideCities</c> 처럼 도시 그림을 걷어 낸다.
/// </remarks>
public sealed class MapTextures
{
    public const int AtlasTiles = 128;
    public const int AtlasSize = AtlasTiles * OceanTiles.TileW;   // 2048

    public required byte[] Cells { get; init; }      // u16 리틀엔디안, 2500x1250
    public required byte[] Atlas { get; init; }      // u8, 2048x2048
    public required byte[] Palette { get; init; }    // RGBA, 256x1
    public required byte[] Overview { get; init; }   // RGBA, 2500x1250

    public static MapTextures Build(GameData data, Func<int, bool> cityStanding)
    {
        const int w = WorldMapImage.Width, h = WorldMapImage.Height;
        var world = data.World;
        var cells = new ushort[w * h];
        for (int y = 0; y < h; y++)
        {
            int offE = y * 2 * WorldMapImage.RawStride;
            int offO = offE + WorldMapImage.RawStride;
            for (int x = 0; x < WorldMapImage.CellW; x++)
            {
                int o = x * 2;
                cells[y * w + x] = (ushort)((world[offE + o] | (world[offE + o + 1] << 8)) & OceanTiles.TileMask);
                cells[y * w + x + WorldMapImage.CellW] = (ushort)((world[offO + o] | (world[offO + o + 1] << 8)) & OceanTiles.TileMask);
            }
        }

        // 안 선 도시는 덧씌움 블록으로 걷는다(WorldMapHost.HideCities). 0xFFFF 는 「안 덮는다」.
        if (data.Cities is { } table)
        {
            for (int id = 0; id < CityExeTable.Count; id++)
            {
                if (cityStanding(id) || !table.TryCell(id, out int cx, out int cy, out _)) continue;
                var block = table.EraseOf(id);
                for (int i = 0; i < block.Length; i++)
                {
                    if (block[i] == CityExeTable.Keep) continue;
                    int x = (cx + i % CityExeTable.EraseWidth) % w, y = cy + i / CityExeTable.EraseWidth;
                    if (y >= 0 && y < h) cells[y * w + x] = (ushort)(block[i] & OceanTiles.TileMask);
                }
            }
        }

        var tiles = data.Ocean.TileData;
        var atlas = new byte[AtlasSize * AtlasSize];
        for (int t = 0; t < OceanTiles.TileCount; t++)
        {
            int ax = (t % AtlasTiles) * OceanTiles.TileW, ay = (t / AtlasTiles) * OceanTiles.TileW;
            for (int py = 0; py < OceanTiles.TileW; py++)
                Buffer.BlockCopy(tiles, t * OceanTiles.TilePixels + py * OceanTiles.TileW,
                                 atlas, (ay + py) * AtlasSize + ax, OceanTiles.TileW);
        }

        var palette = new byte[256 * 4];
        for (int i = 0; i < 256; i++)
        {
            palette[i * 4] = OceanPalette.Rgb[i * 3];
            palette[i * 4 + 1] = OceanPalette.Rgb[i * 3 + 1];
            palette[i * 4 + 2] = OceanPalette.Rgb[i * 3 + 2];
            palette[i * 4 + 3] = 255;
        }

        int[] avg = data.Ocean.GetAverages(1);
        var overview = new byte[w * h * 4];
        for (int i = 0; i < cells.Length; i++)
        {
            int c = avg[cells[i]];
            overview[i * 4] = (byte)(c >> 16);
            overview[i * 4 + 1] = (byte)(c >> 8);
            overview[i * 4 + 2] = (byte)c;
            overview[i * 4 + 3] = 255;
        }

        var cellBytes = new byte[cells.Length * 2];
        Buffer.BlockCopy(cells, 0, cellBytes, 0, cellBytes.Length);
        return new MapTextures { Cells = cellBytes, Atlas = atlas, Palette = palette, Overview = overview };
    }
}
