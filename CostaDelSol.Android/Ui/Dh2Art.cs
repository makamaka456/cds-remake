using Android.Graphics;
using WColor = System.Windows.Media.Color;
using Style = CdsHelper.Game.UI.Views.Dh2Style;

namespace CostaDelSol.Droid.Input;

/// <summary>
/// 모드 「대항해시대2 스타일 대화창」 — PC 판 <c>Dh2Style</c>(ConfirmDialog 의 dh2 갈래)를 안드로이드 물음창 · 알림창에 그린다.
/// 크림 바탕 · 일곱 겹 줄무늬 테 · 검은 글씨 · 톱니 명판 단추. 색은 PC 판과 같이 <see cref="Style.ColorOf"/> 에서 읽어
/// 개발 창 「대항2」 탭에서 고친 색도 따른다.
/// </summary>
internal static class Dh2
{
    public static bool On => CdsHelper.Game.Local.Settings.GameSettings.Dh2TalkStyle;

    /// <summary>명판 높이 · 안쪽 들임 · 가장 좁은 폭(PC 판 Dh2Style 의 PlaqueHeight · PlaqueInset · MinPlaqueWidth).</summary>
    public const float PlaqueHeight = 30, PlaqueInset = 5, MinPlaqueWidth = 72;

    /// <summary>단추 띠 높이 — 명판에 위아래 3(PC 판 PlaqueBar 의 Padding).</summary>
    public const int BarHeight = 36;

    /// <summary>줄무늬 테 일곱 겹(바깥 → 안쪽, 한 점씩).</summary>
    public const int Stripes = 7;

    private static Color C(string key)
    {
        WColor c = Style.ColorOf(key);
        return Color.Argb(255, c.R, c.G, c.B);
    }

    private static readonly Paint Fill = new() { AntiAlias = false };
    private static readonly Paint Edge = new() { AntiAlias = false };

    private static readonly Paint Label = new()
    {
        AntiAlias = true,
        TextAlign = Paint.Align.Center,
        // PC 판 명판 글씨 — Times New Roman · 바탕 굵게 15.
        TextSize = 15,
    };

    static Dh2()
    {
        Label.SetTypeface(Typeface.Create(Typeface.Serif, TypefaceStyle.Bold));
        Edge.SetStyle(Paint.Style.Stroke);
        Edge.StrokeJoin = Paint.Join.Miter;
    }

    /// <summary>명판 폭 — 글 폭 + 안쪽 들임 둘 + 20, 적어도 72(PC 판 Plaque).</summary>
    public static float PlaqueWidth(string text)
    {
        Label.TextSize = 15;
        return Math.Max(MinPlaqueWidth, (float)Math.Ceiling(Label.MeasureText(text)) + PlaqueInset * 2 + 20);
    }

    /// <summary>
    /// 상자 바깥에 일곱 겹 줄무늬 테를 두르고 안을 크림 바탕으로 칠한다(PC 판 <c>Dh2Style.Frame</c> — 테마다 한 점).
    /// </summary>
    public static void DrawFrame(Canvas canvas, float x, float y, float w, float h, float s)
    {
        for (int i = 0; i < Stripes; i++)
        {
            float o = Stripes - i;   // 바깥 줄부터
            Fill.Color = C($"stripe{i}");
            canvas.DrawRect((x - o) * s, (y - o) * s, (x + w + o) * s, (y + h + o) * s, Fill);
        }
        Fill.Color = C("paper");
        canvas.DrawRect(x * s, y * s, (x + w) * s, (y + h) * s, Fill);
    }

    /// <summary>
    /// 톱니 명판 하나 — 두 색 바둑 무늬 테(rim1 · rim2)를 갈색 선으로 두르고, 안쪽 판은 고르면 분홍(on) · 아니면
    /// 초록(off) 바둑 무늬에 그 글씨 색이다(PC 판 <c>Dh2Style.Plaque</c> · <c>CrownPoints</c>).
    /// </summary>
    public static void DrawPlaque(Canvas canvas, RectF r, string text, bool lit, float s)
    {
        float w = r.Width(), h = r.Height();
        var crown = Crown(w, h, s, r.Left * s, r.Top * s);
        Fill.SetShader(Checker(C("rim1"), C("rim2"), s));
        canvas.DrawPath(crown, Fill);
        Fill.SetShader(null);
        Edge.Color = C("brown");
        Edge.StrokeWidth = Math.Max(1, s);
        canvas.DrawPath(crown, Edge);

        var face = new RectF((r.Left + PlaqueInset) * s, (r.Top + PlaqueInset) * s, (r.Right - PlaqueInset) * s, (r.Bottom - PlaqueInset) * s);
        Fill.SetShader(lit ? Checker(C("on1"), C("on2"), s) : Checker(C("off1"), C("off2"), s));
        canvas.DrawRect(face, Fill);
        Fill.SetShader(null);
        canvas.DrawRect(face, Edge);

        Label.TextSize = 15 * s;
        Label.Color = lit ? C("onText") : C("offText");
        var fm = Label.GetFontMetrics()!;
        canvas.DrawText(text, face.CenterX(), face.CenterY() - (fm.Ascent + fm.Descent) / 2, Label);
    }

    /// <summary>한 점 바둑 무늬(PC 판 GameUi.Dither) — 게임 점 하나가 화면 s 화소다.</summary>
    private static Shader Checker(Color a, Color b, float s)
    {
        int cell = Math.Max(1, (int)Math.Round(s));
        var bmp = Bitmap.CreateBitmap(cell * 2, cell * 2, Bitmap.Config.Argb8888!)!;
        bmp.EraseColor(a);
        var c = new Canvas(bmp);
        var p = new Paint { Color = b };
        c.DrawRect(cell, 0, cell * 2, cell, p);
        c.DrawRect(0, cell, cell, cell * 2, p);
        return new BitmapShader(bmp, Shader.TileMode.Repeat!, Shader.TileMode.Repeat!);
    }

    /// <summary>톱니 테두리 — 위아래 가장자리에 톱니, 좌우 가운데에 홈(PC 판 CrownPoints 그대로).</summary>
    private static Android.Graphics.Path Crown(float w, float h, float s, float ox, float oy, float tooth = 6, float depth = 3)
    {
        var pts = new List<(float X, float Y)> { (0, 0) };
        int n = Math.Max(2, (int)Math.Round((w - 2 * tooth) / tooth));
        float step = (w - 2 * tooth) / n;
        pts.Add((tooth, depth));
        for (int k = 0; k < n; k++)
        {
            float x = tooth + k * step;
            pts.Add((x + step / 2, k % 2 == 0 ? 0.5f : depth + 0.5f));
            pts.Add((x + step, depth));
        }
        pts.Add((w, 0));
        pts.Add((w - depth, h / 2 - depth));
        pts.Add((w - depth - 1.5f, h / 2));
        pts.Add((w - depth, h / 2 + depth));
        pts.Add((w, h));
        pts.Add((w - tooth, h - depth));
        for (int k = 0; k < n; k++)
        {
            float x = w - tooth - k * step;
            pts.Add((x - step / 2, k % 2 == 0 ? h - 0.5f : h - depth - 0.5f));
            pts.Add((x - step, h - depth));
        }
        pts.Add((0, h));
        pts.Add((depth, h / 2 + depth));
        pts.Add((depth + 1.5f, h / 2));
        pts.Add((depth, h / 2 - depth));
        var path = new Android.Graphics.Path();
        path.MoveTo(ox + pts[0].X * s, oy + pts[0].Y * s);
        foreach (var (px, py) in pts.Skip(1)) path.LineTo(ox + px * s, oy + py * s);
        path.Close();
        return path;
    }
}
