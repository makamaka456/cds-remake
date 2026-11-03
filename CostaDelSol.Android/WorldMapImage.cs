using Android.App;
using Android.Widget;
using CdsHelper.Support.Local.Helpers;

namespace CostaDelSol.Droid;

/// <summary>
/// WORLD.CDS 와 OCEAN.CDS 로 세계지도 한 장(2500x1250, 칸 하나가 점 하나)을 만든다.
/// </summary>
/// <remarks>
/// 계산은 PC 판 <c>WorldMapRenderer.RenderSingleTile</c>(CdsHelper.Support) 의 OCEAN 경로와 같다.
/// 그쪽은 WPF <c>WriteableBitmap</c> 을 내므로 끌어다 쓸 수 없어 픽셀 계산만 옮겼다.
/// <para>WORLD.CDS 는 한 줄 2500바이트(칸 1250개 x 2바이트)이고 2500줄이다.
/// 짝수 줄이 지도 왼쪽 절반, 홀수 줄이 오른쪽 절반이다.</para>
/// </remarks>
public static class WorldMapImage
{
    public const int RawStride = 2500;   // 파일 한 줄 바이트
    public const int CellW = 1250;       // 반쪽 한 줄의 칸 수
    public const int Height = 1250;      // 펼친 지도 높이
    public const int Width = 2500;       // 펼친 지도 너비 (왼쪽 + 오른쪽)
    public const int DataSize = RawStride * Height * 2;

    /// <summary>
    /// 지도 점들을 ARGB(안드로이드 <c>Bitmap</c> 순서)로 돌려준다. 자리는 <c>y * Width + x</c>.
    /// </summary>
    public static int[] Render(byte[] world, OceanTiles ocean)
    {
        if (world.Length != DataSize)
            throw new InvalidDataException($"WORLD.CDS 크기가 다릅니다 ({world.Length} / {DataSize})");

        // 칸 하나가 점 하나이므로 타일(16x16) 전체의 평균색 하나면 된다.
        int[] avg = ocean.GetAverages(1);
        var pixels = new int[Width * Height];

        for (int ry = 0; ry < Height; ry++)
        {
            int offE = ry * 2 * RawStride;
            int offO = offE + RawStride;
            int row = ry * Width;
            for (int cx = 0; cx < CellW; cx++)
            {
                int o = cx * 2;
                pixels[row + cx] = unchecked((int)0xFF000000) | avg[Tile(world, offE + o)];
                pixels[row + cx + CellW] = unchecked((int)0xFF000000) | avg[Tile(world, offO + o)];
            }
        }
        return pixels;
    }

    /// <summary>칸 u16 의 하위 14비트가 OCEAN.CDS 타일 번호다(게임 0x48A40A 의 <c>and cx,0x3FFF</c>).</summary>
    private static int Tile(byte[] world, int offset) =>
        (world[offset] | (world[offset + 1] << 8)) & OceanTiles.TileMask;

    /// <summary>위도/경도를 지도 점 좌표로 바꾼다.</summary>
    public static (float x, float y) LatLonToPixel(double lat, double lon) =>
        ((float)((lon + 180.0) / 360.0 * Width), (float)((90.0 - lat) / 180.0 * Height));
}
