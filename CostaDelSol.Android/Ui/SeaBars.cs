using Android.App;
using Android.Widget;
using Android.Content;
using Android.Graphics;
using Android.Views;
using CdsHelper.Game.Local.Helpers;

namespace CostaDelSol.Droid.Ui;

/// <summary>
/// 바다 화면의 위아래 띠 — PC 판 <c>ShipMapWindow</c> 의 게임 띠(<c>gameBar</c>)와 알림 띠(<c>footer</c>)를 옮겼다.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>위 띠</b>: 얇은 액자(높이 29) 위에 켜진 베이지 칸들이 맞붙어 선다 — 기본은
///         날짜 · 선원 · 위도경도 · 소지금 · 명성 · 규칙(PC 판 InfoCell 의 on). 글은 원본 서식 그대로.</item>
///   <item><b>아래 띠</b>: 같은 액자 위에 <b>마지막 알림 하나</b>를 검은 글씨(74)로 왼쪽 12 에 적는다.
///         처음 40틱은 열 틱마다 한 틱 비워 깜빡이고, 100틱을 더 버틴 뒤 지운다(0x0040DE30 · NoteTick).</item>
/// </list>
/// 위 띠를 누르면 커맨드 창, 길게 누르면 도시정보 창(띠 칸 켜고 끄기), 아래 띠를 누르면 마지막 알림을 「Information」 창으로 다시 편다(원본 그대로).
/// </remarks>
public sealed class SeaBars : View
{
    public const int BarHeight = 7 * 2 + 15;   // GameUi.BarFrame: 얇은 테 두 겹 + 속 15
    private const int NoteLeft = 12;
    private const int NoteBlinkTicks = 0x28, NoteBlinkEvery = 10, NoteHoldTicks = 100;

    private IReadOnlyList<string> _cells = [];
    private string _note = "", _lastNote = "";
    private int _noteTick = -1;

    /// <summary>위 띠를 눌렀다 — 커맨드 창.</summary>
    public event Action? TopPressed;
    /// <summary>위 띠를 길게 눌렀다(PC 판 오른쪽 단추) — 도시정보 창.</summary>
    public event Action? TopLongPressed;
    private const long LongPressMs = 500;
    private long _downAt;
    /// <summary>아래 띠를 눌렀다 — 마지막 알림을 다시 편다.</summary>
    public event Action<string>? NotePressed;

    public SeaBars(Context context) : base(context) { }

    private float Scale => Height / (float)GameArt.GameHeight;

    /// <summary>게임 화면의 지도 자리(위아래 띠 사이)를 뷰 픽셀로.</summary>
    public int BarPixels => (int)Math.Round(BarHeight * Scale);

    public void SetCells(IReadOnlyList<string> cells)
    {
        if (cells.SequenceEqual(_cells)) return;
        _cells = cells;
        Invalidate();
    }

    /// <summary>알림을 띠에 적는다. 통에는 마지막 하나만 남는다(0x0040E0D7).</summary>
    public void Say(string text)
    {
        if (text.Length > 0) _lastNote = text;
        _note = text;
        _noteTick = text.Length > 0 ? 0 : -1;
        Invalidate();
    }

    /// <summary>0.1초마다 부른다 — 알림 깜빡임과 지우기.</summary>
    public void Tick()
    {
        if (_noteTick < 0) return;
        _noteTick++;
        if (_noteTick >= NoteBlinkTicks + NoteHoldTicks) { _note = ""; _noteTick = -1; }
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        float s = Scale;
        if (s <= 0) return;
        int gameW = (int)Math.Ceiling(Width / s);

        GameArt.DrawFrame(canvas, 0, 0, gameW, BarHeight, true, s);
        float x = 0, y = (BarHeight - GameArt.BandHeight) / 2f;
        // 칸 폭의 합이 화면 폭을 넘으면(4:3) 줄 전체를 가로로만 좁혀 한 줄에 다 넣는다. 넉넉하면(16:9) 그대로다.
        int total = _cells.Sum(GameArt.LeastBandWidth);
        float squeeze = total > gameW ? gameW / (float)total : 1f;
        canvas.Save();
        if (squeeze < 1f) canvas.Scale(squeeze, 1f);
        foreach (string cell in _cells)
        {
            int w = GameArt.LeastBandWidth(cell);
            GameArt.DrawBand(Context!, canvas, BandStyle.Button, x, y, w, s, cell);
            x += w;
        }
        canvas.Restore();

        float bottom = GameArt.GameHeight - BarHeight;
        GameArt.DrawFrame(canvas, 0, bottom, gameW, BarHeight, true, s);
        bool shown = _noteTick < 0 || _noteTick > NoteBlinkTicks || _noteTick % NoteBlinkEvery != 0;
        if (_note.Length > 0 && shown)
            GameText.Draw(canvas, _note, NoteLeft, bottom + (BarHeight - GameArt.TextHeight) / 2f,
                          GameArt.TextHeight, GameFont.BlackColor, false, s);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null) return false;
        float gy = e.GetY() / Scale;
        bool top = gy < BarHeight, bottom = gy > GameArt.GameHeight - BarHeight;
        if (!top && !bottom) return false;   // 지도 자리는 지도 몫이다
        if (e.Action == MotionEventActions.Down) _downAt = e.EventTime;
        if (e.Action == MotionEventActions.Up)
        {
            if (top && e.EventTime - _downAt >= LongPressMs) TopLongPressed?.Invoke();
            else if (top) TopPressed?.Invoke();
            else if (_lastNote.Length > 0) NotePressed?.Invoke(_lastNote);
        }
        return true;
    }
}
