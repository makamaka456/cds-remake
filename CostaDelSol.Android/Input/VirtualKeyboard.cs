using Android.App;
using Android.Widget;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;

namespace CostaDelSol.Droid.Input;

/// <summary>
/// 가상 키보드 — 일반 컴퓨터 자판(기능키 줄 · 숫자 줄 · 글자 세 줄 · 맨 아랫줄 · 방향키 · 편집키) 그대로다.
/// L3(기본)나 메뉴에서 연다.
/// </summary>
/// <remarks>
/// 누른 키는 패드·실제 키보드와 같은 길(<see cref="KeyPressed"/>)로 게임에 간다. Shift · Ctrl · Alt 는 한 번 누르면
/// 켜져 있다가 다음 키와 함께 나가고 꺼진다(<see cref="Modifiers"/>). 키 폭은 실제 자판의 비율(1 = 글자 키 하나)이다.
/// </remarks>
public sealed class VirtualKeyboard : LinearLayout
{
    public event Action<GameKey>? KeyPressed;

    /// <summary>지금 켜 둔 고침 키. 다음 키를 누르면 같이 나가고 꺼진다.</summary>
    public HashSet<GameKey> Modifiers { get; } = [];

    // (키, 폭). 키가 null 이면 「닫기」다.
    private static readonly (GameKey? Key, float Width)[][] Rows =
    [
        [(GameKey.Escape, 1), (GameKey.F1, 1), (GameKey.F2, 1), (GameKey.F3, 1), (GameKey.F4, 1), (GameKey.F5, 1),
         (GameKey.F6, 1), (GameKey.F7, 1), (GameKey.F8, 1), (GameKey.F9, 1), (GameKey.F10, 1), (GameKey.F11, 1),
         (GameKey.F12, 1), (GameKey.Insert, 1), (GameKey.Delete, 1), (GameKey.Home, 1), (GameKey.End, 1),
         (GameKey.PageUp, 1), (GameKey.PageDown, 1), (null, 1.5f)],
        [(GameKey.Grave, 1), (GameKey.D1, 1), (GameKey.D2, 1), (GameKey.D3, 1), (GameKey.D4, 1), (GameKey.D5, 1),
         (GameKey.D6, 1), (GameKey.D7, 1), (GameKey.D8, 1), (GameKey.D9, 1), (GameKey.D0, 1), (GameKey.Minus, 1),
         (GameKey.Plus, 1), (GameKey.Backspace, 2)],
        [(GameKey.Tab, 1.5f), (GameKey.Q, 1), (GameKey.W, 1), (GameKey.E, 1), (GameKey.R, 1), (GameKey.T, 1),
         (GameKey.Y, 1), (GameKey.U, 1), (GameKey.I, 1), (GameKey.O, 1), (GameKey.P, 1), (GameKey.BracketLeft, 1),
         (GameKey.BracketRight, 1), (GameKey.Backslash, 1.5f)],
        [(GameKey.CapsLock, 1.75f), (GameKey.A, 1), (GameKey.S, 1), (GameKey.D, 1), (GameKey.F, 1), (GameKey.G, 1),
         (GameKey.H, 1), (GameKey.J, 1), (GameKey.K, 1), (GameKey.L, 1), (GameKey.Semicolon, 1),
         (GameKey.Apostrophe, 1), (GameKey.Enter, 2.25f)],
        [(GameKey.Shift, 2.25f), (GameKey.Z, 1), (GameKey.X, 1), (GameKey.C, 1), (GameKey.V, 1), (GameKey.B, 1),
         (GameKey.N, 1), (GameKey.M, 1), (GameKey.Comma, 1), (GameKey.Period, 1), (GameKey.Slash, 1),
         (GameKey.Shift, 1.75f), (GameKey.Up, 1)],
        [(GameKey.Ctrl, 1.5f), (GameKey.Alt, 1.5f), (GameKey.Space, 6.25f), (GameKey.Alt, 1.5f), (GameKey.Ctrl, 1.25f),
         (GameKey.Left, 1), (GameKey.Down, 1), (GameKey.Right, 1)],
    ];

    private static bool IsModifier(GameKey key) => key is GameKey.Shift or GameKey.Ctrl or GameKey.Alt;

    private readonly List<(GameKey Key, TextView View)> _keys = [];
    private readonly float _dp;

    public bool IsOpen => Visibility == ViewStates.Visible;

    public VirtualKeyboard(Context context) : base(context)
    {
        _dp = context.Resources!.DisplayMetrics!.Density;
        Orientation = Orientation.Vertical;
        Visibility = ViewStates.Gone;
        Clickable = true;
        SetBackgroundColor(Color.Argb(230, 24, 18, 14));
        int pad = (int)(3 * _dp);
        SetPadding(pad, pad, pad, pad);

        foreach (var row in Rows)
        {
            var line = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            foreach (var (key, width) in row)
            {
                var b = new TextView(context)
                {
                    Text = key is { } k ? GameKeys.Name(k) : "닫기",
                    Gravity = GravityFlags.Center,
                };
                b.SetTextColor(Color.White);
                b.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
                b.SetSingleLine(true);
                Paint(b, false);
                var lp = new LayoutParams(0, (int)(34 * _dp), width) { LeftMargin = pad, RightMargin = pad, TopMargin = pad };
                b.Click += (_, _) =>
                {
                    if (key is not { } pressed) { Toggle(); return; }
                    if (IsModifier(pressed))
                    {
                        if (!Modifiers.Remove(pressed)) Modifiers.Add(pressed);
                        Refresh();
                        return;
                    }
                    KeyPressed?.Invoke(pressed);
                    if (Modifiers.Count > 0) { Modifiers.Clear(); Refresh(); }   // 고침 키는 한 번 쓰면 꺼진다
                };
                if (key is { } kk) _keys.Add((kk, b));
                line.AddView(b, lp);
            }
            AddView(line);
        }
    }

    private void Paint(TextView view, bool on)
    {
        var bg = new GradientDrawable();
        bg.SetColor(on ? Color.Rgb(140, 100, 60) : Color.Rgb(70, 50, 36));
        bg.SetStroke((int)_dp, Color.Rgb(214, 189, 140));
        bg.SetCornerRadius(4 * _dp);
        view.Background = bg;
    }

    private void Refresh()
    {
        foreach (var (key, view) in _keys)
            if (IsModifier(key)) Paint(view, Modifiers.Contains(key));
    }

    public void Toggle() => Visibility = IsOpen ? ViewStates.Gone : ViewStates.Visible;
}

/// <summary>마우스 커서 — R스틱(기본)으로 옮긴다. 한동안 안 쓰면 숨는다.</summary>
public sealed class CursorView : View
{
    private readonly Paint _fill = new() { AntiAlias = true, Color = Color.White };
    private readonly Paint _edge = new() { AntiAlias = true, Color = Color.Black };
    private readonly Android.Graphics.Path _arrow = new();
    private readonly float _dp;
    private long _shownUntil;

    public float X0 { get; private set; }
    public float Y0 { get; private set; }

    public CursorView(Context context) : base(context)
    {
        _dp = context.Resources!.DisplayMetrics!.Density;
        _edge.SetStyle(Paint.Style.Stroke);
        _edge.StrokeWidth = 1.5f * _dp;
        Clickable = false;
        Focusable = false;
    }

    public bool Showing => Android.OS.SystemClock.UptimeMillis() < _shownUntil;

    /// <summary>커서를 그만큼 옮긴다(화면 픽셀). 화면 밖으로는 못 나간다.</summary>
    public void MoveBy(float dx, float dy)
    {
        if (X0 == 0 && Y0 == 0) { X0 = Width / 2f; Y0 = Height / 2f; }
        X0 = Math.Clamp(X0 + dx, 0, Math.Max(0, Width - 1));
        Y0 = Math.Clamp(Y0 + dy, 0, Math.Max(0, Height - 1));
        Poke();
    }

    public void MoveTo(float x, float y)
    {
        X0 = x;
        Y0 = y;
    }

    /// <summary>쓰는 중이라고 알린다 — 5초 동안 보인다.</summary>
    public void Poke()
    {
        _shownUntil = Android.OS.SystemClock.UptimeMillis() + 5000;
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (!Showing) return;
        float s = 22 * _dp;
        _arrow.Reset();
        _arrow.MoveTo(X0, Y0);
        _arrow.LineTo(X0, Y0 + s);
        _arrow.LineTo(X0 + s * 0.28f, Y0 + s * 0.74f);
        _arrow.LineTo(X0 + s * 0.48f, Y0 + s * 1.1f);
        _arrow.LineTo(X0 + s * 0.62f, Y0 + s * 1.02f);
        _arrow.LineTo(X0 + s * 0.44f, Y0 + s * 0.68f);
        _arrow.LineTo(X0 + s * 0.78f, Y0 + s * 0.68f);
        _arrow.Close();
        canvas.DrawPath(_arrow, _fill);
        canvas.DrawPath(_arrow, _edge);
    }
}
