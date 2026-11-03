using Android.App;
using Android.Widget;
using Android.Content;
using Android.Graphics;
using CdsHelper.Game.Local.Helpers;

namespace CostaDelSol.Droid.Ui;

/// <summary>
/// 원본 비트맵 글꼴(ALL_FONT.16P · ANKFONT.DAT)로 글을 찍는다 — PC 판 <see cref="GameFont"/> 를 그대로 쓴다.
/// </summary>
/// <remarks>
/// 한글은 16x14, 반각은 8x16 이다. 색은 원본 색표 번호(<see cref="GameFont.TitleColor"/> 등). 찍은 글은 원본 점
/// 그대로의 그림이라 화면 배율로 키울 때 보간하지 않는다 — 획이 원본처럼 또렷하다.
/// </remarks>
public static class GameText
{
    private static GameFont? _font;
    private static readonly Dictionary<(string, byte, bool, int), Bitmap?> Cache = [];
    private static readonly Paint Pixel = new() { FilterBitmap = false, AntiAlias = false };

    /// <summary>원본 글꼴 두 파일을 앱 폴더에 꺼내 연다. 한 번만.</summary>
    public static void Init(Context context)
    {
        if (_font != null) return;
        string dir = System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "font");
        Directory.CreateDirectory(dir);
        foreach (string name in new[] { "ALL_FONT.16P", "ANKFONT.DAT" })
        {
            string target = System.IO.Path.Combine(dir, name);
            if (File.Exists(target)) continue;
            using var input = context.Assets!.Open("font/" + name);
            using var output = File.Create(target);
            input.CopyTo(output);
        }
        _font = GameFont.Open(dir);
    }

    public static GameFont? Font => _font;

    /// <summary>글 폭(원본 점). 글꼴이 없으면 한 자 8점으로 어림한다.</summary>
    public static int Width(string? text) =>
        _font?.TextWidth(text) ?? (text ?? "").Sum(c => c > 0x7F ? 16 : 8);

    /// <summary>
    /// 한 줄을 찍는다. (x, y) 는 글 상자의 왼쪽 위(원본 점), <paramref name="height"/> 는 상자 높이 —
    /// 글은 그 안에서 세로 가운데로 선다(원본 <c>GameFont.Render</c>).
    /// </summary>
    public static void Draw(Canvas canvas, string text, float x, float y, int height, byte color,
                            bool shadow, float scale)
    {
        if (string.IsNullOrEmpty(text) || _font == null) return;
        var key = (text, color, shadow, height);
        Bitmap? bitmap;
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out bitmap))
            {
                var argb = _font.Render(text, color, shadow, GameFont.ShadowColor, height, out int w);
                bitmap = argb == null ? null : Bitmap.CreateBitmap(argb.Select(p => (int)p).ToArray(), w, height, Bitmap.Config.Argb8888!);
                if (Cache.Count > 600) Cache.Clear();
                Cache[key] = bitmap;
            }
        }
        if (bitmap == null) return;
        canvas.DrawBitmap(bitmap, null,
            new RectF(x * scale, y * scale, (x + bitmap.Width) * scale, (y + height) * scale), Pixel);
    }

    /// <summary>가운데 맞춰 찍는다 — <paramref name="cx"/> 가 글의 가운데(원본 점).</summary>
    public static void DrawCentered(Canvas canvas, string text, float cx, float y, int height, byte color,
                                    bool shadow, float scale) =>
        Draw(canvas, text, cx - Width(text) / 2f, y, height, color, shadow, scale);

    /// <summary>
    /// 글을 그 폭에 맞춰 줄로 끊는다 — 띄어쓰기에서 끊고, 이미 든 줄바꿈은 그 자리에서 끊는다(원본 <c>ConfirmDialog.Wrap</c>).
    /// </summary>
    public static List<string> Wrap(string text, int maxWidth)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r", "").Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string joined = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Width(joined) > maxWidth) { lines.Add(line); line = word; }
                else line = joined;
            }
            lines.Add(line);
        }
        return lines;
    }
}
