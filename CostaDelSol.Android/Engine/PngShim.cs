using Android.App;
using Android.Widget;
using Android.Graphics;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// PC 판 그림 읽개들이 WPF 의 PNG 풀개로 하던 일 — 안드로이드 풀개로 풀어 BGRA 점(uint)으로 낸다.
/// </summary>
internal static class PngShim
{
    /// <summary>PNG 한 장을 푼다. 없거나 크기가 다르면 null(그러면 PC 판처럼 CDS 로 물러선다).</summary>
    public static uint[]? Decode(string path, int width, int height)
    {
        try
        {
            using var bitmap = BitmapFactory.DecodeFile(path);
            if (bitmap == null || bitmap.Width != width || bitmap.Height != height) return null;
            var argb = new int[width * height];
            bitmap.GetPixels(argb, 0, width, 0, 0, width, height);
            var bgra = new uint[argb.Length];
            // 안드로이드의 ARGB 정수와 WPF 의 Bgra32 를 uint 로 읽은 값은 같은 꼴(A 가 맨 위 바이트)이다.
            for (int i = 0; i < argb.Length; i++) bgra[i] = (uint)argb[i];
            return bgra;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>크기를 따지지 않고 푼다(BGRA). <paramref name="premultiply"/> 면 색에 투명도를 곱해 둔다(WPF Pbgra32).</summary>
    public static (uint[] Pixels, int Width, int Height)? DecodeAny(string path, bool premultiply = false)
    {
        try
        {
            using var bitmap = BitmapFactory.DecodeFile(path);
            if (bitmap == null) return null;
            int w = bitmap.Width, h = bitmap.Height;
            var argb = new int[w * h];
            bitmap.GetPixels(argb, 0, w, 0, 0, w, h);
            var px = new uint[argb.Length];
            for (int i = 0; i < argb.Length; i++) px[i] = premultiply ? Premultiply((uint)argb[i]) : (uint)argb[i];
            return (px, w, h);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static uint Premultiply(uint c)
    {
        uint a = c >> 24;
        if (a == 255) return c;
        uint M(int sh) => (((c >> sh) & 0xFF) * a / 255) << sh;
        return (a << 24) | M(16) | M(8) | M(0);
    }
}
