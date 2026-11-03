using Android.App;
using Android.Widget;
using CdsHelper.Game.Local.Helpers;
using Android.Content;
using Android.Graphics;
using Android.Views;
using CostaDelSol.Droid.Input;

namespace CostaDelSol.Droid.Ui;

/// <summary>
/// 타이틀 화면 — PC 판 <c>ShipMapWindow.BuildTitleScreen</c> 을 원본 값 그대로 옮겼다.
/// </summary>
/// <remarks>
/// <code>
///   바탕       asset/title/title-tile.png 를 바둑판으로
///   위 띠      얇은 액자(FrameArt) 위에 날짜 칸 하나(켜진 베이지 띠)  ·  아래 띠  빈 액자
///   메인메뉴   상자(311818, 테 C2AE95 한 점, 안쪽 6) 안에 제목 띠 「메인메뉴」와 다섯 줄 — 줄은 맞붙어 있고
///              모두 베이지 띠(최소 폭 124)다. 고른 줄은 안쪽 테가 깜빡인다(원본 초점)
///   자리       가로 가운데, 세로는 위아래 띠 사이 가운데에서 높이의 5% 만큼 위(TitleMenuRise)
/// </code>
/// CONTINUE 는 자동저장이 없으면 흐린 글씨로 서고 안 눌린다(PC 판과 같다).
/// </remarks>
public sealed class TitleView : View
{
    public sealed record Item(string Text, bool Enabled, Action Run);

    private const int ItemMinWidth = 124;      // ShipMapWindow.TitleItemMinWidth
    private const float MenuRise = 0.05f;      // ShipMapWindow.TitleMenuRise
    private const int BoxPad = 6;              // 메인메뉴 상자 Padding
    private static readonly Color BoxEdge = Color.Rgb(0xC2, 0xAE, 0x95);   // ShipMapWindow.TitleBoxEdge
    private static readonly Color BoxBack = Color.Rgb(0x4A, 0x2A, 0x22);   // ShipMapWindow.MenuBack
    // GameUi.BarFrame: 높이 = 얇은 테(7) 두 겹 + 속(BarInside 15) = 29. 칸(24)은 액자 테 위에 올라앉아 세로 가운데에 선다.
    private const int StripHeight = 7 * 2 + 15;

    private readonly List<Item> _items = [];
    private int _selected;
    private string _date = "";

    public TitleView(Context context) : base(context) => Clickable = true;

    public bool IsOpen => Visibility == ViewStates.Visible;

    public void Show(string date, IEnumerable<Item> items)
    {
        _date = date;
        _items.Clear();
        _items.AddRange(items);
        _selected = Math.Max(0, _items.FindIndex(i => i.Enabled));
        Visibility = ViewStates.Visible;
        Invalidate();
    }

    public void Hide() => Visibility = ViewStates.Gone;

    private float Scale => Height / (float)GameArt.GameHeight;

    private int RowWidth => Math.Max(GameArt.BandWidth(ItemMinWidth),
                                     _items.Select(i => GameArt.LeastBandWidth(i.Text)).DefaultIfEmpty(0).Max());

    private (float X, float Y, float W, float H) Box()
    {
        float w = RowWidth + (BoxPad + 1) * 2;
        float h = GameArt.BandHeight * (_items.Count + 1) + (BoxPad + 1) * 2;
        float areaTop = StripHeight, areaH = GameArt.GameHeight - StripHeight * 2;
        float y = areaTop + (areaH - h) / 2f - GameArt.GameHeight * MenuRise;
        return ((Width / Scale - w) / 2f, y, w, h);
    }

    protected override void OnDraw(Canvas canvas)
    {
        float s = Scale;
        if (s <= 0) return;
        int gameW = (int)Math.Ceiling(Width / s);

        if (GameArt.Load(Context!, "title/title-tile.png") is { } tile) GameArt.Tile(canvas, tile, s, Width, Height);
        else canvas.DrawColor(Color.Rgb(0xD4, 0xC8, 0xB0));

        // 위아래 액자 띠 — 위에는 날짜 칸 하나(켜진 베이지 띠).
        GameArt.DrawFrame(canvas, 0, 0, gameW, StripHeight, true, s);
        GameArt.DrawFrame(canvas, 0, GameArt.GameHeight - StripHeight, gameW, StripHeight, true, s);
        if (_date.Length > 0)
            GameArt.DrawBand(Context!, canvas, BandStyle.Button, 0, (StripHeight - GameArt.BandHeight) / 2f,
                             GameArt.LeastBandWidth(_date), s, _date);

        // 메인메뉴 상자.
        var (x, y, w, h) = Box();
        GameArt.DrawBox(canvas, x, y, w, h, BoxBack, BoxEdge, s);
        float ix = x + BoxPad + 1, iy = y + BoxPad + 1;
        GameArt.DrawBand(Context!, canvas, BandStyle.Title, ix, iy, RowWidth, s, "메인메뉴");
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            GameArt.DrawBand(Context!, canvas, BandStyle.Button, ix, iy + GameArt.BandHeight * (i + 1), RowWidth, s,
                             item.Text, dim: !item.Enabled, focused: i == _selected && item.Enabled);
        }
        PostInvalidateDelayed(GameArt.FocusBlinkMs / 2);   // 초점 테 깜빡임
    }

    /// <summary>원본 키(FocusGroup) — ↑·← 앞 줄, ↓·→ 뒤 줄(흐린 줄은 건너뛴다), 엔터·스페이스 고르기.</summary>
    public bool HandleKey(GameKey key)
    {
        if (!IsOpen || _items.Count == 0) return false;
        switch (key)
        {
            case GameKey.Up or GameKey.Left: Move(-1); break;
            case GameKey.Down or GameKey.Right: Move(+1); break;
            case GameKey.Enter or GameKey.Space: Run(_selected); break;
        }
        return true;
    }

    private void Move(int delta)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            _selected = (_selected + delta + _items.Count) % _items.Count;
            if (_items[_selected].Enabled) break;
        }
        Invalidate();
    }

    private void Run(int index)
    {
        if (index < 0 || index >= _items.Count || !_items[index].Enabled) return;
        _items[index].Run();
    }

    private int _pressed = -1;

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null || !IsOpen) return false;
        float s = Scale;
        var (x, y, w, _) = Box();
        float gx = e.GetX() / s, gy = e.GetY() / s;
        int row = (int)Math.Floor((gy - (y + BoxPad + 1)) / GameArt.BandHeight) - 1;
        bool inside = gx >= x && gx <= x + w && row >= 0 && row < _items.Count;
        if (e.Action == MotionEventActions.Down)
        {
            _pressed = inside ? row : -1;
            if (inside && _items[row].Enabled) { _selected = row; Invalidate(); }
        }
        else if (e.Action == MotionEventActions.Up)
        {
            if (inside && row == _pressed) Run(row);
            _pressed = -1;
        }
        return true;
    }
}
