using Android.App;
using Android.Widget;
using Android.Content;
using Android.Graphics;
using Android.Views;
using CdsHelper.Game.Local.Helpers;
using CostaDelSol.Droid.Ui;

namespace CostaDelSol.Droid.Town;

/// <summary>
/// 도시 화면의 그림 판 — 도시 그림(CITYCG 400x320), 건물 · 사람 이름표, 효과 애니메이션(96x96), 건물 사진(320x240)과 손님.
/// </summary>
/// <remarks>
/// 좌표는 원본 640x480 점이다. 지도는 남빛 막(<c>GameUi.MapCover</c> 24375B)으로 덮이고, 그림은 위 띠(29) 바로 밑
/// 왼쪽에 서며 사진은 그림 기준 (320, 240) 자리에 겹친다(원본처럼 그림의 오른쪽 아래 모서리만 겹친다 — 화면 밖으로
/// 나가면 안쪽으로 당긴다). 손(누르기)은 MainActivity 가 원본 점으로 바꿔 <see cref="Click"/> 로 넘긴다.
/// </remarks>
public sealed class CityStage : View
{
    public const int PicW = 400, PicH = 320, PhotoW = 320, PhotoH = 240, BarH = 29;
    private static readonly Color Cover = Color.Rgb(0x24, 0x37, 0x5B);
    private static readonly Color ShadeColor = Color.Argb(0x88, 0x18, 0x30, 0x70);
    private static readonly Paint Pixel = new() { FilterBitmap = false, AntiAlias = false };

    /// <summary>누를 자리 하나 — 건물이나 마을 사람.</summary>
    public sealed record Spot(RectF Area, string Tag, Action Left, Action? Right, int Code);

    /// <summary>사진 앞에 선 손님 하나.</summary>
    public sealed record Guest(Bitmap Art, string Name, Action? Click);

    private Bitmap? _picture;
    private Bitmap? _photo;
    private List<Guest> _guests = [];
    private Bitmap? _effect;
    private readonly List<Spot> _spots = [];
    private (RectF Area, string Text)? _tag;
    private bool _shade;

    /// <summary>표에 항구가 없는 도시 — 그림 아무 데나 누르면 이것.</summary>
    public Action? Anywhere { get; set; }

    /// <summary>빈 자리를 오른쪽으로 누르면(도시 커맨드).</summary>
    public Action? Empty { get; set; }

    public bool Active => _picture != null;

    public CityStage(Context context) : base(context)
    {
        Visibility = ViewStates.Gone;
    }

    public void Begin(uint[] picture)
    {
        _picture = Bitmap.CreateBitmap([.. picture.Select(p => (int)p)], PicW, PicH, Bitmap.Config.Argb8888!);
        _spots.Clear();
        _photo = null;
        _guests = [];
        _effect = null;
        _tag = null;
        _shade = false;
        Visibility = ViewStates.Visible;
        Invalidate();
        PlayOpening(CdsHelper.Game.Local.Settings.GameSettings.CityOpenEffect);
    }

    /// <summary>
    /// 도시 그림이 열릴 때의 효과 — PC 판 CityPicView.PlayOpening 의 펼침 · 넘김 · 페이드 · 확대를
    /// 안드로이드 뷰 움직임으로 옮겼다. 시간도 PC 판 그대로(펼침 220ms · 확대 320ms · 나머지 250ms).
    /// </summary>
    private void PlayOpening(CdsHelper.Game.Local.Settings.CityOpenEffect effect)
    {
        Animate()?.Cancel();
        Alpha = 1; ScaleX = 1; ScaleY = 1; TranslationX = 0;
        if (effect == CdsHelper.Game.Local.Settings.CityOpenEffect.None) return;
        long ms = effect switch
        {
            CdsHelper.Game.Local.Settings.CityOpenEffect.Expand => 220,
            CdsHelper.Game.Local.Settings.CityOpenEffect.Zoom => 320,
            _ => 250,
        };
        var go = Animate()!.SetDuration(ms);
        switch (effect)
        {
            case CdsHelper.Game.Local.Settings.CityOpenEffect.Expand:
                ScaleX = ScaleY = 0.05f;
                go.ScaleX(1).ScaleY(1);
                break;
            case CdsHelper.Game.Local.Settings.CityOpenEffect.Slide:
                TranslationX = Width > 0 ? Width : 2000;
                go.TranslationX(0);
                break;
            case CdsHelper.Game.Local.Settings.CityOpenEffect.Fade:
                Alpha = 0;
                go.Alpha(1);
                break;
            case CdsHelper.Game.Local.Settings.CityOpenEffect.Zoom:
                ScaleX = ScaleY = 0.3f; Alpha = 0;
                go.ScaleX(1).ScaleY(1).Alpha(1).SetInterpolator(new Android.Views.Animations.OvershootInterpolator(1.2f));
                break;
        }
        go.Start();
    }

    public void End()
    {
        _picture = null;
        _spots.Clear();
        _photo = null;
        _guests = [];
        Visibility = ViewStates.Gone;
    }

    public void AddSpot(Spot spot) => _spots.Add(spot);

    public void ShowTag(RectF area, string text) { _tag = (area, text); Invalidate(); }
    public void HideTag() { _tag = null; Invalidate(); }

    public void SetEffect(Bitmap? frame) { _effect = frame; Invalidate(); }

    public void SetShade(bool on) { _shade = on; Invalidate(); }

    public void SetPhoto(uint[]? bgra, IReadOnlyList<Guest> guests)
    {
        _photo = bgra == null ? null : Bitmap.CreateBitmap([.. bgra.Select(p => (int)p)], PhotoW, PhotoH, Bitmap.Config.Argb8888!);
        _guests = [.. guests];
        Invalidate();
    }

    // ── 자리 셈(원본 점) ─────────────────────────────────────────────────

    private float Scale => Height / (float)GameArt.GameHeight;
    private float AreaW => Width / Scale;
    private float PicX => (float)Math.Floor((AreaW - GameArt.GameWidth) / 2);
    private const float PicY = BarH;
    private float PhotoX => Math.Min(PicX + 320, PicX + GameArt.GameWidth - PhotoW);
    private float PhotoY => Math.Min(PicY + 240, GameArt.GameHeight - BarH - PhotoH);

    protected override void OnDraw(Canvas canvas)
    {
        if (_picture == null) return;
        float s = Scale;
        canvas.DrawColor(Cover);
        canvas.DrawBitmap(_picture, null, R(PicX, PicY, PicW, PicH, s), Pixel);

        if (_photo != null)
        {
            canvas.DrawBitmap(_photo, null, R(PhotoX, PhotoY, PhotoW, PhotoH, s), Pixel);
            float w = PhotoW;
            for (int i = 0; i < _guests.Count; i++)
            {
                var g = _guests[i];
                float gx = PhotoX + (i + 0.5f) * w / _guests.Count - g.Art.Width / 2f;
                canvas.DrawBitmap(g.Art, null, R(gx, PhotoY + PhotoH - g.Art.Height, g.Art.Width, g.Art.Height, s), Pixel);
            }
        }

        if (_tag is var (area, text))
        {
            int tw = GameArt.LeastBandWidth(text);
            float x = Math.Clamp(PicX + area.CenterX() - tw / 2f, PicX, PicX + PicW - tw);
            float below = PicY + area.Bottom + 2;
            float y = below + GameArt.BandHeight <= PicY + PicH ? below : Math.Max(PicY, PicY + area.Top - GameArt.BandHeight - 2);
            GameArt.DrawBand(Context!, canvas, BandStyle.Title, x, y, tw, s, text, ink: GameFont.WhiteColor);
        }

        if (_effect != null)
        {
            float e = _effect.Width;
            canvas.DrawBitmap(_effect, null, R(PicX + (PicW - e) / 2, PicY + (PicH - e) / 2, e, e, s), Pixel);
        }

        if (_shade)
        {
            using var p = new Paint { Color = ShadeColor };
            canvas.DrawRect(R(PicX, PicY, PicW, PicH, s), p);
        }

        // 리메이크 쪽지(함대 · 기능/언어) — 그림 옆(16:9) 또는 그림 가장자리(4:3).
        System.Windows.WpfHost.SideRoom = Math.Max(0, PicX);
        System.Windows.WpfHost.DrawTownLayers(canvas, s, PicX);
        PostInvalidateDelayed(200);
    }

    private static RectF R(float x, float y, float w, float h, float s) => new(x * s, y * s, (x + w) * s, (y + h) * s);

    /// <summary>
    /// 눌렀다(뷰 안 픽셀). 손님 → 건물(낮은 코드가 이긴다) → 마을 사람 → 그림 아무 데 차례다.
    /// </summary>
    /// <summary>사진 앞 손님을 눌렀으면 그 사람과 말을 나누고 참(뷰 안 픽셀).</summary>
    public bool ClickGuest(float vx, float vy)
    {
        if (_picture == null || _photo == null) return false;
        float gx = vx / Scale, gy = vy / Scale;
        for (int i = 0; i < _guests.Count; i++)
        {
            var g = _guests[i];
            float x0 = PhotoX + (i + 0.5f) * PhotoW / _guests.Count - g.Art.Width / 2f;
            if (gx >= x0 && gx < x0 + g.Art.Width && gy >= PhotoY + PhotoH - g.Art.Height && gy < PhotoY + PhotoH && g.Click != null)
            {
                g.Click();
                return true;
            }
        }
        return false;
    }

    public void Click(float vx, float vy, bool left)
    {
        if (_picture == null) return;
        float gx = vx / Scale, gy = vy / Scale;

        if (left && _photo != null)
            for (int i = 0; i < _guests.Count; i++)
            {
                var g = _guests[i];
                float x0 = PhotoX + (i + 0.5f) * PhotoW / _guests.Count - g.Art.Width / 2f;
                if (gx >= x0 && gx < x0 + g.Art.Width && gy >= PhotoY + PhotoH - g.Art.Height && gy < PhotoY + PhotoH && g.Click != null)
                {
                    g.Click();
                    return;
                }
            }

        float px = gx - PicX, py = gy - PicY;
        // 건물이 먼저(코드가 작을수록 위), 사람은 그 뒤다 — 자리가 겹치면 건물이 이긴다(0x00491DC0).
        foreach (var spot in _spots.Where(s => s.Code >= 0).OrderBy(s => s.Code).Concat(_spots.Where(s => s.Code < 0)))
        {
            if (!spot.Area.Contains(px, py)) continue;
            ShowTag(spot.Area, spot.Tag);
            if (left) spot.Left(); else (spot.Right ?? Empty)?.Invoke();
            return;
        }

        bool inPicture = px >= 0 && py >= 0 && px < PicW && py < PicH;
        if (left && inPicture && Anywhere != null) { Anywhere(); return; }
        if (!left) Empty?.Invoke();
    }
}
