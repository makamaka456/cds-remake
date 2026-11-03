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
}
