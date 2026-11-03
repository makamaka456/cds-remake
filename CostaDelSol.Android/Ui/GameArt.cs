using Android.App;
using Android.Widget;
using Android.Content;
using Android.Graphics;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.UI.Views;

namespace CostaDelSol.Droid.Ui;

/// <summary>
/// 원본 화면 조각을 그린다 — PC 판 <c>GameUi</c> 의 띠(<c>BandFrame</c>)·액자(<see cref="FrameArt"/>)·초점 테를
/// 같은 규칙, 같은 색으로 옮겼다. 좌표와 크기는 모두 <b>원본 점</b>(640x480 화면의 점)이고, scale 은 원본 한 점이
/// 화면 몇 픽셀인가다. 그림은 보간 없이 키운다.
/// </summary>
public static class GameArt
{
    public const int GameWidth = 640, GameHeight = 480;

    // UiSprites: 띠는 왼끝·가운데(되풀이)·오른끝, 높이 24, 폭 16 + 8n + 16.
    public const int BandHeight = 24, BandEnd = 16, BandMid = 8;
    // GameSettings.DefaultBandPad — 글자 바깥에 비워 두는 폭.
    public const int BandPad = 12;
    // GameUi.ItemTextHeight — 글 한 줄 높이.
    public const int TextHeight = 16;

    // 원본 색(GameUi · ConfirmDialog · ShipMapWindow).
    public static readonly Color MenuBack = Color.Rgb(0x31, 0x18, 0x18);   // GameUi.MenuBack · Back
    public static readonly Color MenuEdge = Color.Rgb(0xB3, 0xAA, 0x96);   // GameUi.MenuEdge
    public static readonly Color AskEdge = Color.Rgb(0x9B, 0x8D, 0x7D);    // ConfirmDialog.FrameEdge
    public static readonly Color FocusLight = Color.Rgb(0xEC, 0xE4, 0xD2); // GameUi.FocusLight
    public static readonly Color FocusDark = Color.Rgb(0x14, 0x0C, 0x0A);  // GameUi.FocusDark
    public const long FocusBlinkMs = 500;                                  // GameUi.FocusBlink

    /// <summary>흐린 줄 글자색(색인 21) — GameButton.</summary>
    public const byte DimColor = 21;

    private static readonly Dictionary<string, Bitmap?> Cache = [];
    private static readonly Dictionary<(int, int, bool), Bitmap?> Frames = [];
    private static readonly Paint Pixel = new() { FilterBitmap = false, AntiAlias = false };
    private static readonly Paint Line = new() { AntiAlias = false };

    public static Bitmap? Load(Context context, string asset)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(asset, out var cached)) return cached;
            Bitmap? bitmap = null;
            try
            {
                using var input = context.Assets!.Open(asset);
                bitmap = BitmapFactory.DecodeStream(input);
            }
            catch (Java.IO.IOException) { }
            Cache[asset] = bitmap;
            return bitmap;
        }
    }

    // ── 띠 ───────────────────────────────────────────────────────────────

    /// <summary>담을 폭을 덮는 가장 작은 띠 폭 — <c>UiSprites.CellsFor</c> · <c>WidthFor</c>.</summary>
    public static int BandWidth(float contentWidth)
    {
        float need = Math.Max(0, contentWidth - BandEnd * 2);
        int cells = Math.Max(1, (int)Math.Ceiling(need / BandMid));
        return BandEnd * 2 + BandMid * cells;
    }

    /// <summary>글 한 줄이 드는 띠의 최소 폭 — <c>GameUi.BandFrame</c> 의 max(한 칸 띠, 글 + 좌우 BandPad).</summary>
    public static int LeastBandWidth(string text) =>
        BandWidth(Math.Max(BandWidth(1), BandPad * 2 + GameText.Width(text)));

    private static string Prefix(BandStyle style) => style switch
    {
        BandStyle.Title => "title",
        BandStyle.Alt => "alt",
        _ => "button",
    };

    /// <summary>
    /// 띠 하나 — 원본 <c>GameButton</c>. 제목 띠는 밝은 글씨(26)에 그림자, 나머지는 먹색(254) 글씨,
    /// 흐린 줄은 회색(21). 글은 띠 한가운데.
    /// </summary>
    public static void DrawBand(Context context, Canvas canvas, BandStyle style, float x, float y, int width,
                                float scale, string text, bool dim = false, bool focused = false, byte? ink = null)
    {
        string p = "ui/band/" + Prefix(style);
        var left = Load(context, p + "-left.png");
        var mid = Load(context, p + "-mid.png");
        var right = Load(context, p + "-right.png");
        width = BandWidth(width);
        if (left != null && mid != null && right != null)
        {
            Blit(canvas, left, x, y, BandEnd, BandHeight, scale);
            int mids = (width - BandEnd * 2) / BandMid;
            for (int i = 0; i < mids; i++) Blit(canvas, mid, x + BandEnd + i * BandMid, y, BandMid, BandHeight, scale);
            Blit(canvas, right, x + width - BandEnd, y, BandEnd, BandHeight, scale);
        }

        byte color = ink ?? (style == BandStyle.Title ? GameFont.TitleColor : dim ? DimColor : GameFont.ButtonColor);
        GameText.DrawCentered(canvas, text, x + width / 2f, y + (BandHeight - TextHeight) / 2f, TextHeight,
                              color, style == BandStyle.Title, scale);
        if (focused) DrawFocusRing(canvas, x, y, width, BandHeight, scale);
    }

    /// <summary>
    /// 초점 테 — 고른 단추의 <b>안쪽 테</b>가 0.5초마다 밝고 어둡게 깜빡인다(GameButton 의 2점 안쪽 1점 테).
    /// 부르는 쪽은 깜빡임을 보이려고 0.5초마다 다시 그린다.
    /// </summary>
    public static void DrawFocusRing(Canvas canvas, float x, float y, float w, float h, float scale)
    {
        bool light = Android.OS.SystemClock.UptimeMillis() / FocusBlinkMs % 2 == 0;
        Line.Color = light ? FocusLight : FocusDark;
        float t = Math.Max(1, scale);
        float l = (x + 2) * scale, top = (y + 2) * scale, r = (x + w - 2) * scale, b = (y + h - 2) * scale;
        canvas.DrawRect(l, top, r, top + t, Line);
        canvas.DrawRect(l, b - t, r, b, Line);
        canvas.DrawRect(l, top, l + t, b, Line);
        canvas.DrawRect(r - t, top, r, b, Line);
    }

    // ── 상자 · 액자 ──────────────────────────────────────────────────────

    /// <summary>민 상자 — 바탕 한 색에 테 한 점(원본 메뉴·물음창).</summary>
    public static void DrawBox(Canvas canvas, float x, float y, float w, float h, Color back, Color edge, float scale)
    {
        Line.Color = back;
        canvas.DrawRect(x * scale, y * scale, (x + w) * scale, (y + h) * scale, Line);
        Line.Color = edge;
        float t = Math.Max(1, scale);
        float l = x * scale, top = y * scale, r = (x + w) * scale, b = (y + h) * scale;
        canvas.DrawRect(l, top, r, top + t, Line);
        canvas.DrawRect(l, b - t, r, b, Line);
        canvas.DrawRect(l, top, l + t, b, Line);
        canvas.DrawRect(r - t, top, r, b, Line);
    }

    /// <summary>원본 액자 — PC 판 <see cref="FrameArt"/> 의 점 규칙 그대로(상단·하단 띠는 얇은 테).</summary>
    public static void DrawFrame(Canvas canvas, float x, float y, int w, int h, bool thin, float scale)
    {
        Bitmap? bitmap;
        lock (Frames)
        {
            if (!Frames.TryGetValue((w, h, thin), out bitmap))
            {
                var px = FrameArt.Pixels(w, h, thin);
                bitmap = px == null ? null : Bitmap.CreateBitmap(px.Select(p => (int)p).ToArray(), w, h, Bitmap.Config.Argb8888!);
                Frames[(w, h, thin)] = bitmap;
            }
        }
        if (bitmap != null) Blit(canvas, bitmap, x, y, w, h, scale);
    }

    /// <summary>원본 점 단위로 그림 한 장을 그대로 키워 그린다.</summary>
    public static void Blit(Canvas canvas, Bitmap bitmap, float x, float y, int w, int h, float scale)
    {
        var dst = new RectF(x * scale, y * scale, (x + w) * scale, (y + h) * scale);
        canvas.DrawBitmap(bitmap, null, dst, Pixel);
    }

    /// <summary>그림을 바둑판처럼 깐다(타이틀 바탕).</summary>
    public static void Tile(Canvas canvas, Bitmap bitmap, float scale, float width, float height)
    {
        for (float y = 0; y < height; y += bitmap.Height * scale)
            for (float x = 0; x < width; x += bitmap.Width * scale)
                canvas.DrawBitmap(bitmap, null, new RectF(x, y, x + bitmap.Width * scale, y + bitmap.Height * scale), Pixel);
    }

    /// <summary>
    /// 원본 아이콘 한 칸(16x16) — <c>asset/ui/misc-03.png</c> 에 여섯 칸이 세로로 쌓여 있다
    /// (0 없음 · 1 위 · 2 아래 · 3 어둠 · 4 빈칸 · 5 계산기, <c>UiSprites.Icon*</c>).
    /// </summary>
    public static void Icon(Context context, Canvas canvas, int index, float x, float y, float scale)
    {
        var sheet = Load(context, "asset/ui/misc-03.png");
        if (sheet == null) return;
        var src = new Rect(0, index * 16, 16, index * 16 + 16);
        canvas.DrawBitmap(sheet, src, new RectF(x * scale, y * scale, (x + 16) * scale, (y + 16) * scale), Pixel);
    }

    private static Bitmap? _closeBox;

    /// <summary>닫기 조각(16x16) — PC 판 <c>GameUi.DrawCloseBox</c> 의 점 그대로(테 A79787 · 밝은 줄 F7F6F5 · 바탕 DECEBD · 빗금 311818).</summary>
    public static void CloseBox(Canvas canvas, float x, float y, float scale)
    {
        if (_closeBox == null)
        {
            const uint edge = 0xFFA79787, light = 0xFFF7F6F5, fill = 0xFFDECEBD, ink = 0xFF311818;
            const int n = 16, last = n - 1;
            var px = new int[n * n];
            for (int yy = 0; yy < n; yy++)
                for (int xx = 0; xx < n; xx++)
                    px[yy * n + xx] = (int)(xx == 0 || yy == 0 || xx == last || yy == last ? edge : xx == 1 || yy == 1 ? light : fill);
            for (int k = 3; k <= 12; k++)
                for (int t = 0; t < 2; t++)
                {
                    px[k * n + Math.Min(k + t, last - 1)] = unchecked((int)ink);
                    px[k * n + Math.Max(last - k + t - 1, 1)] = unchecked((int)ink);
                }
            _closeBox = Bitmap.CreateBitmap(px, n, n, Bitmap.Config.Argb8888!);
        }
        Blit(canvas, _closeBox!, x, y, 16, 16, scale);
    }
}
