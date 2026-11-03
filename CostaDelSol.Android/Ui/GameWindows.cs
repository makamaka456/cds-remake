using Android.App;
using Android.Widget;
using Android.Content;
using Android.Graphics;
using Android.Views;
using CdsHelper.Game.Local.Helpers;
using CostaDelSol.Droid.Input;

namespace CostaDelSol.Droid.Ui;

/// <summary>원본의 따로 지은 창들이 같이 쓰는 그리기 조각 — PC 판 <c>GameUi</c> 의 값 그대로다.</summary>
internal static class WindowArt
{
    public static readonly Color Back = Color.Rgb(0x31, 0x18, 0x18);       // GameUi.Back
    public static readonly Color Edge = Color.Rgb(0x0B, 0x05, 0x05);       // GameUi.Edge
    public static readonly Color PageFill = Color.Rgb(0xF2, 0xE4, 0xC8);   // GameUi.PageFill
    public static readonly Color ItemEdge = Color.Rgb(0x4A, 0x40, 0x30);   // GameUi.ItemEdge
    public static readonly Color CountLine = Color.Rgb(0x11, 0x09, 0x09);  // CountDialog.Line
    public static readonly Color PickFill = Color.Rgb(0x43, 0x56, 0x7A);   // HintListDialog.PickFill
    public static readonly Color PickEdge = Color.Rgb(0x05, 0x06, 0x09);   // HintListDialog.PickEdge
    public static readonly Color MarkFill = Color.Rgb(0xDE, 0xC6, 0xAD);   // HintListDialog.MarkFill

    private static readonly Paint Fill = new() { AntiAlias = false };

    public static void Rect(Canvas canvas, float x, float y, float w, float h, Color color, float s)
    {
        Fill.Color = color;
        canvas.DrawRect(x * s, y * s, (x + w) * s, (y + h) * s, Fill);
    }

    /// <summary>테만 — 굵기 <paramref name="t"/> 점.</summary>
    public static void Edges(Canvas canvas, float x, float y, float w, float h, Color color, float t, float s)
    {
        Rect(canvas, x, y, w, t, color, s);
        Rect(canvas, x, y + h - t, w, t, color, s);
        Rect(canvas, x, y, t, h, color, s);
        Rect(canvas, x + w - t, y, t, h, color, s);
    }

    /// <summary>
    /// 세 겹 정보 테(<c>GameUi.InfoFrame</c>) — 바깥 선 · 2 · 가운데 선 · 3 · 안쪽 선, 모두 1점. 안쪽이 시작하는 자리를 낸다.
    /// </summary>
    public const float InfoInset = 1 + 2 + 1 + 3 + 1;

    public static void InfoFrame(Canvas canvas, float x, float y, float w, float h, Color back, Color line, float s)
    {
        Rect(canvas, x, y, w, h, back, s);
        Edges(canvas, x, y, w, h, line, 1, s);
        Edges(canvas, x + 3, y + 3, w - 6, h - 6, line, 1, s);
        Edges(canvas, x + 7, y + 7, w - 14, h - 14, line, 1, s);
    }

    public static bool Hit(float px, float py, float x, float y, float w, float h) =>
        px >= x && px < x + w && py >= y && py < y + h;
}

/// <summary>
/// 수량 입력 창(<c>CountDialog</c>) — 제목 띠, 「이름 [값 칸][계산기] 단위」 줄과 눈금 줄들, 아래에 [최대] 결정 · 중단.
/// </summary>
/// <remarks>
/// ↑↓ 로 한 걸음(Shift 는 열 배), 엔터 · 스페이스 결정, ESC 중단. 값 칸이나 계산기를 누르면 계산기 창(<see cref="NumberPadWindow"/>)이다.
/// 결정은 0 보다 클 때만 산다(<c>zeroOk</c> 면 0 도 된다).
/// </remarks>
internal sealed class CountWindow : ChoicePanel.IGameWindow
{
    private const float FieldW = 44, FieldH = 18, RowGap = 4, NameGap = 8, ButtonW = 92, ButtonGap = 10, CalcW = 18;
    private const float RowsLeft = 12, RowsTop = 10, RowsRight = 16, RowsBottom = 4, ButtonsSide = 12, ButtonsTop = 10, ButtonsBottom = 12;

    private readonly string _caption, _label, _unit;
    private readonly int _max, _step;
    private readonly bool _full, _zeroOk;
    private readonly (string Name, string Value)[] _gauges;
    private readonly Action<int?> _done;
    private readonly Action<int, Action<int?>> _pad;
    private int _at;
    private int _focus;   // 단추 초점(0.. 단추 수-1)

    public CountWindow(string caption, string label, string unit, int max, int step, bool full,
                       (string Name, string Value)[] gauges, int start, bool zeroOk,
                       Action<int?> done, Action<int, Action<int?>> pad)
    {
        _caption = caption; _label = label; _unit = unit; _max = max; _step = Math.Max(1, step); _full = full;
        _gauges = gauges; _at = start; _zeroOk = zeroOk; _done = done; _pad = pad;
        _focus = _full ? 1 : 0;
        float nameW = Math.Max(GameText.Width(label), gauges.Select(g => GameText.Width(g.Name)).DefaultIfEmpty(0).Max());
        float valueW = Math.Max(FieldW + 2 + CalcW + 3 + GameText.Width(unit), gauges.Select(g => (float)GameText.Width(g.Value)).DefaultIfEmpty(0).Max());
        int buttons = _full ? 3 : 2;
        float inner = Math.Max(RowsLeft + nameW + NameGap + valueW + RowsRight,
                               ButtonsSide * 2 + buttons * ButtonW + (buttons - 1) * ButtonGap);
        inner = Math.Max(inner, GameArt.LeastBandWidth(caption));
        _nameW = nameW;
        Width = inner + WindowArt.InfoInset * 2;
        float rows = FieldH + gauges.Length * (GameArt.TextHeight + RowGap);
        Height = WindowArt.InfoInset * 2 + GameArt.BandHeight + RowsTop + rows + RowsBottom + ButtonsTop + GameArt.BandHeight + ButtonsBottom;
    }

    private readonly float _nameW;
    public float Width { get; }
    public float Height { get; }

    private string[] Labels => _full ? ["최대", "결정", "중단"] : ["결정", "중단"];
    private bool CanDecide => _at > 0 || _zeroOk;

    private (float X, float Y, float W)[] ButtonRects()
    {
        var labels = Labels;
        float inner = Width - WindowArt.InfoInset * 2 - ButtonsSide * 2;
        float w = (inner - (labels.Length - 1) * ButtonGap) / labels.Length;
        float y = Height - WindowArt.InfoInset - ButtonsBottom - GameArt.BandHeight;
        return [.. labels.Select((_, i) => (WindowArt.InfoInset + ButtonsSide + i * (w + ButtonGap), y, w))];
    }

    private (float X, float Y) FieldAt => (WindowArt.InfoInset + RowsLeft + _nameW + NameGap,
                                            WindowArt.InfoInset + GameArt.BandHeight + RowsTop);

    public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
    {
        WindowArt.InfoFrame(canvas, x, y, Width, Height, WindowArt.Back, WindowArt.CountLine, s);
        float i0 = WindowArt.InfoInset;
        GameArt.DrawBand(context, canvas, BandStyle.Title, x + i0, y + i0, (int)(Width - i0 * 2), s, _caption);

        var (fx, fy) = FieldAt;
        // 이름은 오른쪽 맞춤, 값 칸은 크림빛에 짙은 글씨.
        GameText.Draw(canvas, _label, x + fx - NameGap - GameText.Width(_label), y + fy + (FieldH - GameArt.TextHeight) / 2, GameArt.TextHeight, GameFont.WhiteColor, false, s);
        WindowArt.Rect(canvas, x + fx, y + fy, FieldW, FieldH, WindowArt.PageFill, s);
        WindowArt.Edges(canvas, x + fx, y + fy, FieldW, FieldH, WindowArt.ItemEdge, 1, s);
        GameText.Draw(canvas, _at.ToString(), x + fx + 3, y + fy + (FieldH - GameArt.TextHeight) / 2, GameArt.TextHeight, GameFont.ButtonColor, false, s);
        // 계산기 단추.
        float cx = x + fx + FieldW + 2;
        WindowArt.Rect(canvas, cx, y + fy, CalcW, FieldH, Color.Rgb(0xD2, 0xCA, 0xAD), s);
        WindowArt.Edges(canvas, cx, y + fy, CalcW, FieldH, WindowArt.ItemEdge, 1, s);
        GameText.DrawCentered(canvas, "#", cx + CalcW / 2, y + fy + (FieldH - GameArt.TextHeight) / 2, GameArt.TextHeight, GameFont.ButtonColor, false, s);
        GameText.Draw(canvas, _unit, cx + CalcW + 3, y + fy + (FieldH - GameArt.TextHeight) / 2, GameArt.TextHeight, GameFont.WhiteColor, false, s);

        float gy = fy + FieldH + RowGap;
        float valueRight = fx + FieldW + 2 + CalcW + 3 + GameText.Width(_unit);
        foreach (var (name, value) in _gauges)
        {
            GameText.Draw(canvas, name, x + fx - NameGap - GameText.Width(name), y + gy, GameArt.TextHeight, GameFont.WhiteColor, false, s);
            GameText.Draw(canvas, value, x + valueRight - GameText.Width(value), y + gy, GameArt.TextHeight, GameFont.WhiteColor, false, s);
            gy += GameArt.TextHeight + RowGap;
        }

        var labels = Labels;
        var rects = ButtonRects();
        for (int i = 0; i < labels.Length; i++)
        {
            bool dim = labels[i] == "결정" && !CanDecide;
            GameArt.DrawBand(context, canvas, BandStyle.Button, x + rects[i].X, y + rects[i].Y, (int)rects[i].W, s, labels[i],
                             dim: dim, focused: top && i == _focus && !dim);
        }
    }

    private void Press(string label)
    {
        switch (label)
        {
            case "최대": _at = _max; break;
            case "결정": if (CanDecide) _done(_at); break;
            case "중단": _done(null); break;
        }
    }

    public void Key(GameKey key)
    {
        switch (key)
        {
            case GameKey.Up: _at = Math.Clamp(_at + _step, 0, _max); break;
            case GameKey.Down: _at = Math.Clamp(_at - _step, 0, _max); break;
            case GameKey.PageUp: _at = Math.Clamp(_at + _step * 10, 0, _max); break;
            case GameKey.PageDown: _at = Math.Clamp(_at - _step * 10, 0, _max); break;
            case GameKey.Left: _focus = (_focus + Labels.Length - 1) % Labels.Length; break;
            case GameKey.Right: _focus = (_focus + 1) % Labels.Length; break;
            case GameKey.Enter or GameKey.Space:
                if (Labels[_focus] == "결정" && !CanDecide) break;
                Press(Labels[_focus]);
                break;
            case GameKey.Escape: _done(null); break;
            case >= GameKey.D0 and <= GameKey.D9:
                _at = Math.Clamp(_at * 10 + (key - GameKey.D0), 0, _max);
                break;
            case GameKey.Backspace: _at /= 10; break;
        }
    }

    public void Touch(MotionEventActions action, float px, float py)
    {
        if (action != MotionEventActions.Up) return;
        var (fx, fy) = FieldAt;
        if (WindowArt.Hit(px, py, fx, fy, FieldW + 2 + CalcW, FieldH))
        {
            _pad(_at, typed => { if (typed is { } v) _at = Math.Clamp(v, 0, _max); });
            return;
        }
        var rects = ButtonRects();
        for (int i = 0; i < rects.Length; i++)
            if (WindowArt.Hit(px, py, rects[i].X, rects[i].Y, rects[i].W, GameArt.BandHeight))
            {
                _focus = i;
                Press(Labels[i]);
            }
    }

    public void Cancel() => _done(null);
}

/// <summary>
/// 계산기 창(<c>NumberPadDialog</c>) — 숫자 칸 아래 7 8 9 AC / 4 5 6 DEL / 1 2 3 MAX / 0 00 000 MIN / ENTER CANCEL.
/// </summary>
/// <remarks>PC 판 <c>NumberPadDialog</c> 의 띠 단추 판과 같은 배치·색이다(바탕 311818, 테 0B0505 두 점, 칸 F2E4C8 · 4A4030).</remarks>
internal sealed class NumberPadWindow : ChoicePanel.IGameWindow
{
    private const float KeyW = 56, Margin = 4, Inner = 4;
    private static readonly string[][] Keys =
    [
        ["7", "8", "9", "AC"],
        ["4", "5", "6", "DEL"],
        ["1", "2", "3", "MAX"],
        ["0", "00", "000", "MIN"],
    ];

    private readonly int _min, _max;
    private readonly string _prompt;
    private readonly Action<int?> _done;
    private string _typed;

    public NumberPadWindow(int start, int min, int max, string prompt, Action<int?> done)
    {
        _min = min; _max = max; _prompt = prompt; _done = done;
        _typed = Math.Clamp(start, min, max).ToString();
        Width = 2 + Margin + Inner + KeyW * 4 + Inner + Margin + 2;
        Height = 2 + Margin + (prompt.Length > 0 ? 6 + GameArt.TextHeight : 0) + 6 + 28 + 2 + Inner
                 + GameArt.BandHeight * 5 + Inner + Margin + 2;
    }

    public float Width { get; }
    public float Height { get; }

    private float ScreenY => 2 + Margin + (_prompt.Length > 0 ? 6 + GameArt.TextHeight : 0) + 6;
    private float KeysY => ScreenY + 28 + 2 + Inner;
    private float KeysX => 2 + Margin + Inner;

    public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
    {
        WindowArt.Rect(canvas, x, y, Width, Height, WindowArt.Back, s);
        WindowArt.Edges(canvas, x + Margin, y + Margin, Width - Margin * 2, Height - Margin * 2, WindowArt.Edge, 2, s);
        if (_prompt.Length > 0)
            GameText.Draw(canvas, _prompt, x + 2 + Margin + 8, y + 2 + Margin + 6, GameArt.TextHeight, GameFont.WhiteColor, false, s);
        float sx = x + 2 + Margin + 6, sw = Width - (2 + Margin + 6) * 2;
        WindowArt.Rect(canvas, sx, y + ScreenY, sw, 28, WindowArt.PageFill, s);
        WindowArt.Edges(canvas, sx, y + ScreenY, sw, 28, WindowArt.ItemEdge, 2, s);
        GameText.Draw(canvas, _typed, sx + sw - 10 - GameText.Width(_typed), y + ScreenY + 6, GameArt.TextHeight, GameFont.BlackColor, false, s);

        for (int r = 0; r < Keys.Length; r++)
            for (int c = 0; c < 4; c++)
                GameArt.DrawBand(context, canvas, BandStyle.Button, x + KeysX + c * KeyW, y + KeysY + r * GameArt.BandHeight,
                                 (int)KeyW, s, Keys[r][c]);
        float by = y + KeysY + 4 * GameArt.BandHeight;
        GameArt.DrawBand(context, canvas, BandStyle.Button, x + KeysX, by, (int)(KeyW * 2), s, "ENTER");
        GameArt.DrawBand(context, canvas, BandStyle.Alt, x + KeysX + KeyW * 2, by, (int)(KeyW * 2), s, "CANCEL");
    }

    private void Press(string key)
    {
        switch (key)
        {
            case "AC": _typed = "0"; break;
            case "DEL": _typed = _typed.Length <= 1 ? "0" : _typed[..^1]; break;
            case "MAX": _typed = _max.ToString(); break;
            case "MIN": _typed = _min.ToString(); break;
            case "ENTER":
                _done(Math.Clamp(long.TryParse(_typed, out long v) ? (int)Math.Min(v, int.MaxValue) : _min, _min, _max));
                break;
            case "CANCEL": _done(null); break;
            default:
                string next = (_typed == "0" ? "" : _typed) + key;
                if (long.TryParse(next, out long n) && n <= _max) _typed = n.ToString();
                else _typed = _max.ToString();
                break;
        }
    }

    public void Key(GameKey key)
    {
        switch (key)
        {
            case >= GameKey.D0 and <= GameKey.D9: Press(((int)(key - GameKey.D0)).ToString()); break;
            case GameKey.Backspace: Press("DEL"); break;
            case GameKey.Enter or GameKey.Space: Press("ENTER"); break;
            case GameKey.Escape: Press("CANCEL"); break;
            case GameKey.Up: _typed = Math.Clamp((long.TryParse(_typed, out long u) ? u : 0) + 1, _min, _max).ToString(); break;
            case GameKey.Down: _typed = Math.Clamp((long.TryParse(_typed, out long d) ? d : 0) - 1, _min, _max).ToString(); break;
        }
    }

    public void Touch(MotionEventActions action, float px, float py)
    {
        if (action != MotionEventActions.Up) return;
        for (int r = 0; r < Keys.Length; r++)
            for (int c = 0; c < 4; c++)
                if (WindowArt.Hit(px, py, KeysX + c * KeyW, KeysY + r * GameArt.BandHeight, KeyW, GameArt.BandHeight))
                {
                    Press(Keys[r][c]);
                    return;
                }
        float by = KeysY + 4 * GameArt.BandHeight;
        if (WindowArt.Hit(px, py, KeysX, by, KeyW * 2, GameArt.BandHeight)) Press("ENTER");
        else if (WindowArt.Hit(px, py, KeysX + KeyW * 2, by, KeyW * 2, GameArt.BandHeight)) Press("CANCEL");
    }

    public void Cancel() => _done(null);
}

/// <summary>
/// 목록 창(<c>HintListDialog</c>) — 제목 띠, 양피지(F2E4C8) 칸 안의 글 줄들, 아래에 결정 · 중단(폭 106, 사이 12).
/// </summary>
/// <remarks>
/// 고른 줄은 남빛(43567A) 바탕에 테(050609). 여럿 고르기는 누를 때마다 켜고 끈다. 고르기 없는 창(보기만)은 결정 대신 중단만 산다.
/// 얼굴을 주면 줄마다 왼쪽에 40x48 로 줄여 붙인다. 목록이 280 점을 넘으면 굴린다.
/// </remarks>
internal sealed class ListWindow : ChoicePanel.IGameWindow
{
    private const float ListW = 264, ListMaxH = 280, RowPad = 3, PanelInset = 3, EdgeGap = 1;
    private const float ButtonW = 106, ButtonGap = 12, FaceW = 40, FaceH = 48, FaceGap = 4;

    private readonly string _caption, _header;
    private readonly IReadOnlyList<string> _items;
    private readonly Bitmap?[]? _faces;
    private readonly IReadOnlyList<string>? _subs;
    private readonly IReadOnlyList<bool>? _marks;
    private readonly bool _choosing, _multi;
    private readonly Action<int, IReadOnlyList<int>> _done;   // (고른 줄 또는 -1, 여럿 고른 줄)
    private readonly SortedSet<int> _chosen = [];
    private int _picked = -1;
    private int _cursor;
    private int _focus;          // 0 = 목록, 1 = 결정, 2 = 중단
    private float _scroll;
    private float _dragFrom = float.NaN, _dragScroll;
    private bool _dragged;

    public ListWindow(string caption, IReadOnlyList<string> items, bool choosing, bool multi, string header,
                      Bitmap?[]? faces, IReadOnlyList<string>? subs, IReadOnlyList<bool>? marks,
                      IReadOnlyCollection<int>? preset, Action<int, IReadOnlyList<int>> done)
    {
        _caption = caption; _items = items; _choosing = choosing; _multi = multi; _header = header;
        _faces = faces; _subs = subs; _marks = marks; _done = done;
        if (preset != null) foreach (int i in preset) if (i >= 0 && i < items.Count) _chosen.Add(i);
        float listW = faces == null ? ListW : ListW + FaceW + FaceGap;
        _listW = listW;
        _contentH = (header.Length > 0 ? GameArt.TextHeight + 2 : 0) + Enumerable.Range(0, items.Count).Sum(RowH);
        _viewH = Math.Min(ListMaxH, _contentH);
        float outer = 1 + EdgeGap + PanelInset + 1;
        Width = outer * 2 + 3 * 2 + 2 + 2 * 2 + listW;
        Height = outer * 2 + GameArt.BandHeight + 3 + 2 + 1 * 2 + _viewH + 5 + GameArt.BandHeight + 5;
    }

    private readonly float _listW, _contentH, _viewH;
    public float Width { get; }
    public float Height { get; }

    private float RowH(int i)
    {
        float text = _subs != null && i < _subs.Count && _subs[i].Length > 0 ? GameArt.TextHeight * 2 : GameArt.TextHeight;
        if (_faces != null) text = Math.Max(text, FaceH);
        return text + 2 + 2;   // 테 1 · 여백 1 위아래
    }

    private const float Outer = 1 + EdgeGap + PanelInset + 1;
    private float ListX => Outer + 3 + 1 + 2;
    private float ListY => Outer + GameArt.BandHeight + 3 + 1 + 1;
    private float ButtonsY => Height - Outer - 5 - GameArt.BandHeight;
    private float DecideX => (Width - (ButtonW * 2 + ButtonGap)) / 2;
    private float StopX => DecideX + ButtonW + ButtonGap;
    private bool CanDecide => _choosing && (_multi ? _chosen.Count > 0 : _picked >= 0);

    public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
    {
        WindowArt.Rect(canvas, x, y, Width, Height, WindowArt.Back, s);
        WindowArt.Edges(canvas, x, y, Width, Height, WindowArt.Edge, 1, s);
        float m = 1 + EdgeGap + PanelInset;
        WindowArt.Rect(canvas, x + m, y + m, Width - m * 2, Height - m * 2, GameArt.MenuBack, s);
        WindowArt.Edges(canvas, x + m, y + m, Width - m * 2, Height - m * 2, GameArt.MenuEdge, 1, s);
        GameArt.DrawBand(context, canvas, BandStyle.Title, x + Outer, y + Outer, (int)(Width - Outer * 2), s, _caption);

        float px = x + Outer + 3, py = y + Outer + GameArt.BandHeight + 3;
        WindowArt.Rect(canvas, px, py, Width - (Outer + 3) * 2, _viewH + 4, WindowArt.PageFill, s);
        WindowArt.Edges(canvas, px, py, Width - (Outer + 3) * 2, _viewH + 4, WindowArt.ItemEdge, 1, s);

        canvas.Save();
        canvas.ClipRect((x + ListX) * s, (y + ListY) * s, (x + ListX + _listW) * s, (y + ListY + _viewH) * s);
        float ry = y + ListY - _scroll;
        if (_header.Length > 0)
        {
            GameText.Draw(canvas, _header, x + ListX + 1 + RowPad, ry + 1, GameArt.TextHeight, GameFont.BlackColor, false, s);
            ry += GameArt.TextHeight + 2;
        }
        for (int i = 0; i < _items.Count; i++)
        {
            float h = RowH(i);
            bool on = _multi ? _chosen.Contains(i) : i == _picked;
            float rx = x + ListX;
            if (on) WindowArt.Rect(canvas, rx + 1, ry + 1, _listW - 2, h - 2, WindowArt.PickFill, s);
            else if (_marks != null && i < _marks.Count && _marks[i]) WindowArt.Rect(canvas, rx + 1, ry + 1, _listW - 2, h - 2, WindowArt.MarkFill, s);
            if (on || (top && _focus == 0 && i == _cursor && _choosing))
                WindowArt.Edges(canvas, rx + 1, ry + 1, _listW - 2, h - 2, WindowArt.PickEdge, 1, s);
            float tx = rx + 2 + RowPad;
            if (_faces != null)
            {
                if (i < _faces.Length && _faces[i] is { } face)
                    canvas.DrawBitmap(face, null, new RectF(tx * s, (ry + 2) * s, (tx + FaceW) * s, (ry + 2 + FaceH) * s), FacePaint);
                tx += FaceW + FaceGap;
            }
            byte ink = on ? GameFont.WhiteColor : GameFont.BlackColor;
            bool sub = _subs != null && i < _subs.Count && _subs[i].Length > 0;
            float textH = sub ? GameArt.TextHeight * 2 : GameArt.TextHeight;
            float ty = ry + (h - textH) / 2;
            GameText.Draw(canvas, _items[i], tx, ty, GameArt.TextHeight, ink, false, s);
            if (sub) GameText.Draw(canvas, _subs![i], tx, ty + GameArt.TextHeight, GameArt.TextHeight, ink, false, s);
            ry += h;
        }
        canvas.Restore();

        GameArt.DrawBand(context, canvas, BandStyle.Button, x + DecideX, y + ButtonsY, (int)ButtonW, s, "결정",
                         dim: !CanDecide, focused: top && _focus == 1 && CanDecide);
        GameArt.DrawBand(context, canvas, BandStyle.Button, x + StopX, y + ButtonsY, (int)ButtonW, s, "중단",
                         focused: top && _focus == 2);
    }

    private static readonly Paint FacePaint = new() { FilterBitmap = false, AntiAlias = false };

    private void Select(int index)
    {
        if (!_choosing) return;
        _cursor = index;
        if (_multi) { if (!_chosen.Remove(index)) _chosen.Add(index); }
        else _picked = index;
        Reveal();
    }

    private void Reveal()
    {
        float top = (_header.Length > 0 ? GameArt.TextHeight + 2 : 0) + Enumerable.Range(0, _cursor).Sum(RowH);
        float h = _cursor < _items.Count ? RowH(_cursor) : 0;
        if (top < _scroll) _scroll = top;
        if (top + h > _scroll + _viewH) _scroll = top + h - _viewH;
    }

    private void Decide()
    {
        if (!CanDecide) return;
        _done(_multi ? -1 : _picked, [.. _chosen]);
    }

    public void Key(GameKey key)
    {
        switch (key)
        {
            case GameKey.Up when _focus == 0 && _items.Count > 0:
                _cursor = (_cursor - 1 + _items.Count) % _items.Count;
                if (!_multi && _choosing) _picked = _cursor;
                Reveal();
                break;
            case GameKey.Down when _focus == 0 && _items.Count > 0:
                _cursor = (_cursor + 1) % _items.Count;
                if (!_multi && _choosing) _picked = _cursor;
                Reveal();
                break;
            case GameKey.Left or GameKey.Right:
                _focus = _focus == 0 ? (_choosing ? 1 : 2) : _focus == 1 ? 2 : (_choosing ? 0 : 2);
                break;
            case GameKey.Space when _focus == 0 && _multi: Select(_cursor); break;
            case GameKey.Enter or GameKey.Space:
                if (_focus == 2 || !_choosing) { _done(-1, []); break; }
                if (_focus == 0 && !_multi && _picked < 0 && _items.Count > 0) _picked = _cursor;
                if (_focus == 0 && _multi && _chosen.Count == 0) Select(_cursor);
                Decide();
                break;
            case GameKey.PageUp: _scroll = Math.Max(0, _scroll - _viewH); break;
            case GameKey.PageDown: _scroll = Math.Min(Math.Max(0, _contentH - _viewH), _scroll + _viewH); break;
            case GameKey.Escape: _done(-1, []); break;
        }
    }

    public void Touch(MotionEventActions action, float px, float py)
    {
        bool inList = WindowArt.Hit(px, py, ListX, ListY, _listW, _viewH);
        switch (action)
        {
            case MotionEventActions.Down:
                _dragFrom = inList ? py : float.NaN;
                _dragScroll = _scroll;
                _dragged = false;
                return;
            case MotionEventActions.Move when !float.IsNaN(_dragFrom):
                if (Math.Abs(py - _dragFrom) > 6) _dragged = true;
                if (_dragged) _scroll = Math.Clamp(_dragScroll - (py - _dragFrom), 0, Math.Max(0, _contentH - _viewH));
                return;
            case MotionEventActions.Up:
                if (_dragged) { _dragFrom = float.NaN; return; }
                _dragFrom = float.NaN;
                break;
            default:
                return;
        }

        if (WindowArt.Hit(px, py, DecideX, ButtonsY, ButtonW, GameArt.BandHeight)) { _focus = 1; Decide(); return; }
        if (WindowArt.Hit(px, py, StopX, ButtonsY, ButtonW, GameArt.BandHeight)) { _done(-1, []); return; }
        if (!inList) return;
        float ry = ListY - _scroll + (_header.Length > 0 ? GameArt.TextHeight + 2 : 0);
        for (int i = 0; i < _items.Count; i++)
        {
            float h = RowH(i);
            if (py >= ry && py < ry + h) { _focus = 0; Select(i); return; }
            ry += h;
        }
    }

    public void Cancel() => _done(-1, []);
}

/// <summary>
/// 정보 판(<c>InfoDialog</c>) — 세 겹 테 안에 판(색은 판마다) · 글 줄들 · 오른쪽 아래 단추들. 인물정보 · 함대정보 따위.
/// </summary>
/// <remarks>판 여백 14 · 10 · 14 · 2, 단추 줄 여백 10 · 0 · 10 · 10. 얼굴은 왼쪽 위에 두 점 테를 둘러 선다.</remarks>
internal sealed class SheetWindow : ChoicePanel.IGameWindow
{
    public static readonly Color Steel = Color.Rgb(92, 111, 147);
    public static readonly Color SteelEdge = Color.Rgb(54, 65, 86);

    public sealed record Line(string Text, byte Color = GameFont.BlackColor, float Gap = 0);

    private const float PadL = 14, PadT = 10, PadR = 14, PadB = 2, BtnL = 10, BtnR = 10, BtnB = 10;

    private readonly Color _board, _edge;
    private readonly float _boardW, _boardH;
    private readonly Bitmap? _face;
    private readonly IReadOnlyList<Line> _head, _body;
    private readonly (string Label, Action? Run)[] _buttons;
    private readonly Action _close;
    private int _focus;

    public SheetWindow(Color board, Color edge, float boardW, float boardH, Bitmap? face,
                       IReadOnlyList<Line> head, IReadOnlyList<Line> body, (string, Action?)[] buttons, Action close)
    {
        _board = board; _edge = edge; _boardW = boardW; _boardH = boardH; _face = face;
        _head = head; _body = body; _buttons = buttons; _close = close;
        _focus = Array.FindIndex(buttons, b => b.Item2 != null);
        Width = WindowArt.InfoInset * 2 + PadL + boardW + PadR;
        Height = WindowArt.InfoInset * 2 + PadT + boardH + PadB + GameArt.BandHeight + BtnB;
    }

    public float Width { get; }
    public float Height { get; }

    private float ButtonW(int i) => Math.Max(64, GameArt.LeastBandWidth(_buttons[i].Label));

    private (float X, float Y, float W)[] Rects()
    {
        var list = new (float, float, float)[_buttons.Length];
        float x = Width - WindowArt.InfoInset - BtnR, y = Height - WindowArt.InfoInset - BtnB - GameArt.BandHeight;
        for (int i = _buttons.Length - 1; i >= 0; i--)
        {
            float w = ButtonW(i);
            x -= w;
            list[i] = (x, y, w);
        }
        return list;
    }

    private static readonly Paint FacePaint = new() { FilterBitmap = false, AntiAlias = false };

    public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
    {
        WindowArt.InfoFrame(canvas, x, y, Width, Height, _board, _edge, s);
        float bx = x + WindowArt.InfoInset + PadL, by = y + WindowArt.InfoInset + PadT;
        float hx = bx, hy = by;
        if (_face != null)
        {
            WindowArt.Rect(canvas, bx, by, 84, 100, _edge, s);
            canvas.DrawBitmap(_face, null, new RectF((bx + 2) * s, (by + 2) * s, (bx + 82) * s, (by + 98) * s), FacePaint);
            hx = bx + 84 + 10;
        }
        foreach (var line in _head)
        {
            hy += line.Gap;
            GameText.Draw(canvas, line.Text, hx, hy, GameArt.TextHeight, line.Color, false, s);
            hy += GameArt.TextHeight;
        }
        float ly = _face != null ? Math.Max(hy, by + 100) : hy;
        foreach (var line in _body)
        {
            ly += line.Gap;
            GameText.Draw(canvas, line.Text, bx, ly, GameArt.TextHeight, line.Color, false, s);
            ly += GameArt.TextHeight;
        }
        var rects = Rects();
        for (int i = 0; i < _buttons.Length; i++)
            GameArt.DrawBand(context, canvas, BandStyle.Button, x + rects[i].X, y + rects[i].Y, (int)rects[i].W, s,
                             _buttons[i].Label, dim: _buttons[i].Run == null, focused: top && i == _focus);
    }

    public void Key(GameKey key)
    {
        switch (key)
        {
            case GameKey.Left: Step(-1); break;
            case GameKey.Right: Step(+1); break;
            case GameKey.Enter or GameKey.Space:
                if (_focus >= 0 && _buttons[_focus].Run is { } run) run(); else _close();
                break;
            case GameKey.Escape: _close(); break;
        }
    }

    private void Step(int d)
    {
        for (int n = 0; n < _buttons.Length; n++)
        {
            _focus = (_focus + d + _buttons.Length) % _buttons.Length;
            if (_buttons[_focus].Run != null) return;
        }
    }

    public void Touch(MotionEventActions action, float px, float py)
    {
        if (action != MotionEventActions.Up) return;
        var rects = Rects();
        for (int i = 0; i < rects.Length; i++)
            if (WindowArt.Hit(px, py, rects[i].X, rects[i].Y, rects[i].W, GameArt.BandHeight) && _buttons[i].Run is { } run)
            {
                _focus = i;
                run();
            }
    }

    public void Cancel() => _close();
}

/// <summary>
/// 목록 창(<c>GameList</c> 를 담은 창) — 제목 띠, 양피지 칸의 칸 나눈 줄들, 아래에 단추들(폭 110).
/// 시장 구입·매각, 조선소 매각·수리 따위가 이 모양이다.
/// </summary>
/// <remarks>
/// 창 테는 <c>GameUi.DialogEdge</c>(바탕 311818, 테 110505 한 점, 두 점 안에 또 한 점, 여백 4).
/// 줄 높이 22(글 16 + 6), 고른 줄 바탕 5C6F93 · 흰 글씨, 손이 간 줄은 0C0A08 테. 결정은 고른 것이 있어야 산다.
/// </remarks>
internal sealed class TableWindow : ChoicePanel.IGameWindow
{
    public enum PickMode { One, Many, None }

    /// <summary>칸 하나 — 오른쪽에 붙는 칸은 폭을 박고, 마지막 Fill 이 남는 자리를 먹는다.</summary>
    public sealed record Column(bool Right, float MarginLeft, float MarginRight, float Width = 0, bool AlignRight = false);

    private static readonly Color EdgeDark = Color.Rgb(0x11, 0x05, 0x05);
    private static readonly Color Picked = Color.Rgb(0x5C, 0x6F, 0x93);
    private static readonly Color Caret = Color.Rgb(0x0C, 0x0A, 0x08);
    private const float RowH = GameArt.TextHeight + 6, Frame = 1 + 2 + 1 + 4, ButtonW = 110, ButtonsTop = 4;

    private readonly string _title, _empty;
    private readonly Column[] _columns;
    private readonly Func<int, IReadOnlyList<string>> _cells;
    private readonly PickMode _pick;
    private readonly string[] _buttons;
    private readonly Func<string, bool>? _enabled;
    private readonly float _listW, _viewH;
    private int _count;
    private readonly HashSet<int> _chosen = [];
    private int _selected = -1;
    private int _focus = -1;   // -1 = 목록, 0.. = 단추
    private float _scroll;
    private float _dragFrom = float.NaN, _dragScroll;
    private bool _dragged;

    /// <summary>단추를 누르면(또는 ESC 면 null) 부른다.</summary>
    public Action<string?>? Pressed;

    public TableWindow(string title, Column[] columns, Func<int, IReadOnlyList<string>> cells, int count, PickMode pick,
                       string[] buttons, string empty = "", float width = 356, float maxHeight = 0, Func<string, bool>? enabled = null)
    {
        _title = title; _columns = columns; _cells = cells; _count = count; _pick = pick; _buttons = buttons;
        _empty = empty; _enabled = enabled;
        _listW = width;
        float full = Math.Max(1, count) * RowH + 2;
        _viewH = Math.Min(full, maxHeight > 0 ? maxHeight : 300);
        Width = Frame * 2 + width;
        Height = Frame * 2 + GameArt.BandHeight + _viewH + ButtonsTop + GameArt.BandHeight;
        if (pick == PickMode.One && count > 0) _selected = 0;
    }

    public float Width { get; }
    public float Height { get; }

    public IReadOnlyList<int> Chosen => _pick == PickMode.Many ? [.. _chosen.Order()] : _selected >= 0 ? [_selected] : [];
    public int Selected => _selected;

    /// <summary>줄 수가 바뀌었다(매각한 뒤 따위).</summary>
    public void Rebuild(int count)
    {
        _count = count;
        _chosen.RemoveWhere(i => i >= count);
        if (_selected >= count) _selected = count - 1;
    }

    private bool Live(string button) => button switch
    {
        "결정" => (_enabled?.Invoke(button) ?? true) && (_pick == PickMode.None || Chosen.Count > 0),
        _ => _enabled?.Invoke(button) ?? true,
    };

    private float ListY => Frame + GameArt.BandHeight;
    private float ButtonsY => Frame + GameArt.BandHeight + _viewH + ButtonsTop;
    private float ButtonX(int i) => (Width - _buttons.Length * ButtonW) / 2 + i * ButtonW;

    public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
    {
        WindowArt.Rect(canvas, x, y, Width, Height, WindowArt.Back, s);
        WindowArt.Edges(canvas, x, y, Width, Height, EdgeDark, 1, s);
        WindowArt.Edges(canvas, x + 3, y + 3, Width - 6, Height - 6, EdgeDark, 1, s);
        GameArt.DrawBand(context, canvas, BandStyle.Title, x + Frame, y + Frame, (int)_listW, s, _title);

        float lx = x + Frame, ly = y + ListY;
        WindowArt.Rect(canvas, lx, ly, _listW, _viewH, WindowArt.PageFill, s);
        WindowArt.Edges(canvas, lx, ly, _listW, _viewH, WindowArt.Edge, 1, s);
        canvas.Save();
        canvas.ClipRect((lx + 1) * s, (ly + 1) * s, (lx + _listW - 1) * s, (ly + _viewH - 1) * s);
        if (_count == 0 && _empty.Length > 0)
            GameText.Draw(canvas, _empty, lx + 3, ly + 4, GameArt.TextHeight, GameFont.ButtonColor, false, s);
        for (int i = 0; i < _count; i++)
        {
            float ry = ly + 1 + i * RowH - _scroll;
            if (ry + RowH < ly || ry > ly + _viewH) continue;
            bool here = i == _selected;
            bool on = _pick == PickMode.Many ? _chosen.Contains(i) : here && _pick == PickMode.One;
            if (on) WindowArt.Rect(canvas, lx + 1, ry, _listW - 2, RowH, Picked, s);
            if (here && _focus < 0 && top && _pick == PickMode.Many) WindowArt.Edges(canvas, lx + 1, ry, _listW - 2, RowH, Caret, 1, s);
            DrawCells(canvas, _cells(i), lx + 2, ry + 3, _listW - 4, on ? GameFont.WhiteColor : GameFont.ButtonColor, s);
        }
        canvas.Restore();

        for (int i = 0; i < _buttons.Length; i++)
        {
            bool live = Live(_buttons[i]);
            GameArt.DrawBand(context, canvas, BandStyle.Button, x + ButtonX(i), y + ButtonsY, (int)ButtonW, s, _buttons[i],
                             dim: !live, focused: top && _focus == i && live);
        }
    }

    private void DrawCells(Canvas canvas, IReadOnlyList<string> cells, float x, float y, float w, byte ink, float s)
    {
        float left = x, right = x + w;
        for (int c = 0; c < _columns.Length && c < cells.Count; c++)
        {
            var col = _columns[c];
            string text = cells[c];
            int tw = GameText.Width(text);
            if (col.Right)
            {
                float cw = col.Width > 0 ? col.Width : tw;
                float cellR = right - col.MarginRight, cellL = cellR - cw;
                float tx = col.AlignRight ? cellR - tw : cellL;
                GameText.Draw(canvas, text, tx, y, GameArt.TextHeight, ink, false, s);
                right = cellL - col.MarginLeft;
            }
            else if (col.Width > 0)
            {
                float cellL = left + col.MarginLeft, cellR = cellL + col.Width;
                GameText.Draw(canvas, text, col.AlignRight ? cellR - tw : cellL, y, GameArt.TextHeight, ink, false, s);
                left = cellR + col.MarginRight;
            }
            else
            {
                float cellL = left + col.MarginLeft, cellR = right - col.MarginRight;
                GameText.Draw(canvas, text, col.AlignRight ? cellR - tw : cellL, y, GameArt.TextHeight, ink, false, s);
            }
        }
    }

    private void Touch(int index)
    {
        if (index < 0 || index >= _count) return;
        _selected = index;
        if (_pick == PickMode.Many && !_chosen.Remove(index)) _chosen.Add(index);
        Reveal();
    }

    private void Reveal()
    {
        float top = _selected * RowH;
        if (top < _scroll) _scroll = top;
        if (top + RowH > _scroll + _viewH - 2) _scroll = top + RowH - _viewH + 2;
    }

    private void Press(string button)
    {
        if (!Live(button)) return;
        Pressed?.Invoke(button == "중단" ? null : button);
    }

    public void Key(GameKey key)
    {
        switch (key)
        {
            case GameKey.Up when _focus < 0 && _count > 0:
                _selected = _selected <= 0 ? _count - 1 : _selected - 1;
                Reveal();
                break;
            case GameKey.Down when _focus < 0 && _count > 0:
                _selected = (_selected + 1) % _count;
                Reveal();
                break;
            case GameKey.Left or GameKey.Right:
                if (_buttons.Length == 0) break;
                // 목록(-1) → 단추들 → 목록 차례로 돈다.
                int n = _buttons.Length + 1;
                _focus = ((_focus + 1 + (key == GameKey.Right ? 1 : -1)) % n + n) % n - 1;
                break;
            case GameKey.Space when _focus < 0 && _pick == PickMode.Many && _selected >= 0:
                if (!_chosen.Remove(_selected)) _chosen.Add(_selected);
                break;
            case GameKey.Enter or GameKey.Space:
                if (_focus >= 0) { Press(_buttons[_focus]); break; }
                if (_pick == PickMode.Many && _chosen.Count == 0 && _selected >= 0) _chosen.Add(_selected);
                if (_buttons.Contains("결정")) Press("결정");
                else if (_buttons.Length > 0) Press(_buttons[0]);
                break;
            case GameKey.PageUp: _scroll = Math.Max(0, _scroll - _viewH); break;
            case GameKey.PageDown: _scroll = Math.Min(Math.Max(0, _count * RowH + 2 - _viewH), _scroll + _viewH); break;
            case GameKey.Escape: Pressed?.Invoke(null); break;
        }
    }

    public void Touch(MotionEventActions action, float px, float py)
    {
        bool inList = WindowArt.Hit(px, py, Frame, ListY, _listW, _viewH);
        switch (action)
        {
            case MotionEventActions.Down:
                _dragFrom = inList ? py : float.NaN;
                _dragScroll = _scroll;
                _dragged = false;
                return;
            case MotionEventActions.Move when !float.IsNaN(_dragFrom):
                if (Math.Abs(py - _dragFrom) > 6) _dragged = true;
                if (_dragged) _scroll = Math.Clamp(_dragScroll - (py - _dragFrom), 0, Math.Max(0, _count * RowH + 2 - _viewH));
                return;
            case MotionEventActions.Up:
                bool dragged = _dragged;
                _dragFrom = float.NaN;
                if (dragged) return;
                break;
            default:
                return;
        }
        for (int i = 0; i < _buttons.Length; i++)
            if (WindowArt.Hit(px, py, ButtonX(i), ButtonsY, ButtonW, GameArt.BandHeight)) { _focus = i; Press(_buttons[i]); return; }
        if (inList)
        {
            _focus = -1;
            Touch((int)((py - ListY - 1 + _scroll) / RowH));
        }
    }

    public void Cancel() => Pressed?.Invoke(null);
}

/// <summary>
/// 아이템 설명 창(<c>ItemInfoDialog</c>) — 쇠빛 판(5C6F93) 560 폭, 머리 줄(이름 · 속성 · 효과), 왼쪽 그림, 오른쪽 설명, 확인(88).
/// </summary>
internal sealed class ItemWindow : ChoicePanel.IGameWindow
{
    private const float W = 560, ArtW = 120, ArtH = 120;   // ItemArt.Width · Height
    private static readonly Color Back = Color.Rgb(0x5C, 0x6F, 0x93);
    private static readonly Color Line = Color.Rgb(0x14, 0x18, 0x20);
    private static readonly Color Empty = Color.Rgb(0x48, 0x50, 0x66);
    private static readonly Paint ArtPaint = new() { FilterBitmap = false, AntiAlias = false };

    private readonly string _name, _kind, _effect;
    private readonly bool _equipped;
    private readonly List<string> _lines;
    private readonly Bitmap? _art;
    private readonly Action _close;

    public ItemWindow(string name, string kind, string effect, bool equipped, string description, Bitmap? art, Action close)
    {
        _name = name; _kind = kind; _effect = effect; _equipped = equipped; _art = art; _close = close;
        _lines = GameText.Wrap(description.Length > 0 ? description : "(설명이 없다)", (int)(W - ArtW - 60));
        Width = W;
        float body = Math.Max(ArtH, 2 + _lines.Count * GameArt.TextHeight);
        Height = WindowArt.InfoInset * 2 + 8 + GameArt.TextHeight * (equipped ? 2 : 1) + 4 + 12 + body + 8 + GameArt.BandHeight + 12;
    }

    public float Width { get; }
    public float Height { get; }

    public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
    {
        WindowArt.InfoFrame(canvas, x, y, Width, Height, Back, Line, s);
        float i0 = WindowArt.InfoInset;
        float hy = y + i0 + 8;
        GameText.Draw(canvas, _name, x + i0 + 12, hy, GameArt.TextHeight, GameFont.BlackColor, false, s);
        string effect = $"효과  {_effect}";
        float ex = x + Width - i0 - 8 - 16 - 12 - GameText.Width(effect);
        GameText.Draw(canvas, effect, ex, hy, GameArt.TextHeight, GameFont.BlackColor, false, s);
        if (_equipped) GameText.Draw(canvas, "장비중", ex + GameText.Width(effect) - GameText.Width("장비중"), hy + GameArt.TextHeight, GameArt.TextHeight, GameFont.BlackColor, false, s);
        string kind = $"속성/{_kind}";
        GameText.Draw(canvas, kind, ex - 24 - GameText.Width(kind), hy, GameArt.TextHeight, GameFont.BlackColor, false, s);

        float by = hy + GameArt.TextHeight * (_equipped ? 2 : 1) + 4 + 12;
        float ax = x + i0 + 14;
        WindowArt.Rect(canvas, ax, by, ArtW, ArtH, Empty, s);
        if (_art != null) canvas.DrawBitmap(_art, null, new RectF(ax * s, by * s, (ax + ArtW) * s, (by + ArtH) * s), ArtPaint);
        for (int i = 0; i < _lines.Count; i++)
            GameText.Draw(canvas, _lines[i], ax + ArtW + 14, by + 2 + i * GameArt.TextHeight, GameArt.TextHeight, GameFont.BlackColor, false, s);
        GameArt.DrawBand(context, canvas, BandStyle.Button, x + Width - i0 - 14 - 88, y + Height - i0 - 12 - GameArt.BandHeight, 88, s, "확인", focused: top);
    }

    public void Key(GameKey key)
    {
        if (key is GameKey.Escape or GameKey.Enter or GameKey.Space) _close();
    }

    public void Touch(MotionEventActions action, float px, float py)
    {
        if (action == MotionEventActions.Up) _close();
    }

    public void Cancel() => _close();
}

/// <summary>
/// 장부 창 — 제목 띠, 양피지(테 두 점) 안에 머리 줄 · 고를 줄들 · 합계 줄, 아래에 결정 · 중단(폭 128, 사이 12).
/// 조선소 「매각선박의 선택」·「수리선박 선택」이 이 모양이다(<c>ShipSellDialog</c> · <c>ShipRepairDialog</c>).
/// </summary>
/// <remarks>고른 줄은 4A649E 바탕에 검은 테. 못 고르는 줄은 흐리다. 창 테는 <c>GameUi.WindowFrame</c>.</remarks>
internal sealed class LedgerWindow : ChoicePanel.IGameWindow
{
    private static readonly Color PickFill = Color.Rgb(0x4A, 0x64, 0x9E);
    private const float ButtonW = 128, ButtonGap = 12, LineH = GameArt.TextHeight + 2, Inset = 6;

    private readonly string _title, _head;
    private readonly IReadOnlyList<(string Text, bool Live)> _rows;
    private readonly Func<IReadOnlyCollection<int>, string> _total;
    private readonly Action<IReadOnlyList<int>?> _done;
    private readonly HashSet<int> _picked = [];
    private int _cursor;
    private int _focus = -1;

    public LedgerWindow(string title, string head, IReadOnlyList<(string, bool)> rows,
                        Func<IReadOnlyCollection<int>, string> total, Action<IReadOnlyList<int>?> done)
    {
        _title = title; _head = head; _rows = rows; _total = total; _done = done;
        _cursor = Math.Max(0, rows.ToList().FindIndex(r => r.Item2));
        float textW = Math.Max(GameText.Width(head), rows.Select(r => GameText.Width(r.Item1)).DefaultIfEmpty(0).Max());
        _listW = textW + 4 * 2 + 2 + 2 * 2 + 2;
        Width = Inset * 2 + 3 * 2 + _listW;
        Height = Inset * 2 + GameArt.BandHeight + 3 + (rows.Count + 2) * LineH + 4 + 4 + 6 + GameArt.BandHeight + 4;
    }

    private readonly float _listW;
    public float Width { get; }
    public float Height { get; }

    private float ListX => Inset + 3;
    private float ListY => Inset + GameArt.BandHeight + 3;
    private float RowY(int i) => ListY + 2 + 2 + LineH * (i + 1);
    private float ButtonsY => Height - Inset - 4 - GameArt.BandHeight;
    private float DecideX => (Width - ButtonW * 2 - ButtonGap) / 2;

    public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
    {
        WindowArt.Rect(canvas, x, y, Width, Height, WindowArt.Back, s);
        WindowArt.Edges(canvas, x, y, Width, Height, WindowArt.Edge, 2, s);
        WindowArt.Edges(canvas, x + 3, y + 3, Width - 6, Height - 6, GameArt.MenuEdge, 1, s);
        GameArt.DrawBand(context, canvas, BandStyle.Title, x + Inset, y + Inset, (int)(Width - Inset * 2), s, _title);
        float lh = (_rows.Count + 2) * LineH + 8;
        WindowArt.Rect(canvas, x + ListX, y + ListY, _listW, lh, WindowArt.PageFill, s);
        WindowArt.Edges(canvas, x + ListX, y + ListY, _listW, lh, WindowArt.ItemEdge, 2, s);
        float tx = x + ListX + 2 + 2 + 4;
        GameText.Draw(canvas, _head, tx, y + ListY + 4, GameArt.TextHeight, GameFont.BlackColor, false, s);
        for (int i = 0; i < _rows.Count; i++)
        {
            float ry = y + RowY(i);
            bool on = _picked.Contains(i);
            if (on)
            {
                WindowArt.Rect(canvas, x + ListX + 4, ry, _listW - 8, LineH, PickFill, s);
                WindowArt.Edges(canvas, x + ListX + 4, ry, _listW - 8, LineH, Color.Black, 1, s);
            }
            else if (top && _focus < 0 && i == _cursor)
                WindowArt.Edges(canvas, x + ListX + 4, ry, _listW - 8, LineH, Color.Black, 1, s);
            GameText.Draw(canvas, _rows[i].Text, tx, ry + 1, GameArt.TextHeight, _rows[i].Live ? GameFont.BlackColor : GameArt.DimColor, false, s);
        }
        GameText.Draw(canvas, _total(_picked), tx, y + RowY(_rows.Count), GameArt.TextHeight, GameFont.BlackColor, false, s);
        GameArt.DrawBand(context, canvas, BandStyle.Button, x + DecideX, y + ButtonsY, (int)ButtonW, s, "결정",
                         dim: _picked.Count == 0, focused: top && _focus == 0 && _picked.Count > 0);
        GameArt.DrawBand(context, canvas, BandStyle.Button, x + DecideX + ButtonW + ButtonGap, y + ButtonsY, (int)ButtonW, s, "중단",
                         focused: top && _focus == 1);
    }

    private void Toggle(int i)
    {
        if (i < 0 || i >= _rows.Count || !_rows[i].Live) return;
        _cursor = i;
        if (!_picked.Remove(i)) _picked.Add(i);
    }

    private void Decide()
    {
        if (_picked.Count > 0) _done([.. _picked.Order()]);
    }

    public void Key(GameKey key)
    {
        switch (key)
        {
            case GameKey.Up when _focus < 0 && _rows.Count > 0: _cursor = (_cursor - 1 + _rows.Count) % _rows.Count; break;
            case GameKey.Down when _focus < 0 && _rows.Count > 0: _cursor = (_cursor + 1) % _rows.Count; break;
            case GameKey.Left or GameKey.Right: _focus = _focus switch { -1 => key == GameKey.Right ? 0 : 1, 0 => key == GameKey.Right ? 1 : -1, _ => key == GameKey.Right ? -1 : 0 }; break;
            case GameKey.Space when _focus < 0: Toggle(_cursor); break;
            case GameKey.Enter or GameKey.Space:
                if (_focus == 1) { _done(null); break; }
                if (_focus < 0 && _picked.Count == 0) Toggle(_cursor);
                Decide();
                break;
            case GameKey.Escape: _done(null); break;
        }
    }

    public void Touch(MotionEventActions action, float px, float py)
    {
        if (action != MotionEventActions.Up) return;
        if (WindowArt.Hit(px, py, DecideX, ButtonsY, ButtonW, GameArt.BandHeight)) { _focus = 0; Decide(); return; }
        if (WindowArt.Hit(px, py, DecideX + ButtonW + ButtonGap, ButtonsY, ButtonW, GameArt.BandHeight)) { _done(null); return; }
        for (int i = 0; i < _rows.Count; i++)
            if (WindowArt.Hit(px, py, ListX, RowY(i), _listW, LineH)) { _focus = -1; Toggle(i); return; }
    }

    public void Cancel() => _done(null);
}

/// <summary>
/// 표 창 — 머리 줄(DEC6AD) 아래 줄들(FFEFD6, 고른 줄 C4A88C), 칸 폭은 원본 값. 아래에 결정 · 중단.
/// 조선소 「선체종류 선택」(<c>HullSelectDialog</c>)이 이 모양이다.
/// </summary>
internal sealed class GridWindow : ChoicePanel.IGameWindow
{
    private static readonly Color HeadFill = Color.Rgb(0xDE, 0xC6, 0xAD);
    private static readonly Color RowFill = Color.Rgb(0xFF, 0xEF, 0xD6);
    private static readonly Color PickFill = Color.Rgb(0xC4, 0xA8, 0x8C);
    private static readonly Color EdgeDark = Color.Rgb(0x11, 0x05, 0x05);
    private const float RowH = GameArt.TextHeight + 2, Frame = 1 + 2 + 1 + 4, MaxH = 420, ButtonW = 110;

    private readonly string _title;
    private readonly string[] _head;
    private readonly float[] _widths;
    private readonly IReadOnlyList<string[]> _rows;
    private readonly Action<int> _done;
    private int _picked = -1, _cursor, _focus = -1;
    private float _scroll;

    public GridWindow(string title, string[] head, float[] widths, IReadOnlyList<string[]> rows, Action<int> done)
    {
        _title = title; _head = head; _widths = widths; _rows = rows; _done = done;
        _tableW = widths.Sum();
        _viewH = Math.Min(MaxH, rows.Count * RowH);
        Width = Frame * 2 + 8 * 2 + _tableW;
        Height = Frame * 2 + GameArt.BandHeight + 4 + RowH + _viewH + 8 + GameArt.BandHeight + 8;
    }

    private readonly float _tableW, _viewH;
    public float Width { get; }
    public float Height { get; }
    private float TableX => Frame + 8;
    private float TableY => Frame + GameArt.BandHeight + 4;
    private float ButtonsY => Height - Frame - 8 - GameArt.BandHeight;
    private float DecideX => (Width - ButtonW * 2) / 2;

    private void DrawRow(Canvas canvas, string[] cells, float x, float y, Color fill, bool head, float s)
    {
        WindowArt.Rect(canvas, x, y, _tableW, RowH, fill, s);
        float cx = x;
        for (int c = 0; c < _widths.Length && c < cells.Length; c++)
        {
            int tw = GameText.Width(cells[c]);
            float tx = head || c == 0 ? cx + (_widths[c] - tw) / 2 : cx + _widths[c] - 3 - tw;
            GameText.Draw(canvas, cells[c], tx, y + 1, GameArt.TextHeight, GameFont.BlackColor, false, s);
            cx += _widths[c];
        }
    }

    public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
    {
        WindowArt.Rect(canvas, x, y, Width, Height, WindowArt.Back, s);
        WindowArt.Edges(canvas, x, y, Width, Height, EdgeDark, 1, s);
        WindowArt.Edges(canvas, x + 3, y + 3, Width - 6, Height - 6, EdgeDark, 1, s);
        GameArt.DrawBand(context, canvas, BandStyle.Title, x + Frame, y + Frame, (int)(Width - Frame * 2), s, _title);
        DrawRow(canvas, _head, x + TableX, y + TableY, HeadFill, true, s);
        canvas.Save();
        canvas.ClipRect((x + TableX) * s, (y + TableY + RowH) * s, (x + TableX + _tableW) * s, (y + TableY + RowH + _viewH) * s);
        for (int i = 0; i < _rows.Count; i++)
        {
            float ry = y + TableY + RowH + i * RowH - _scroll;
            DrawRow(canvas, _rows[i], x + TableX, ry, i == _picked ? PickFill : RowFill, false, s);
            if (top && _focus < 0 && i == _cursor) WindowArt.Edges(canvas, x + TableX, ry, _tableW, RowH, Color.Black, 1, s);
        }
        canvas.Restore();
        GameArt.DrawBand(context, canvas, BandStyle.Button, x + DecideX, y + ButtonsY, (int)ButtonW, s, "결정",
                         dim: _picked < 0, focused: top && _focus == 0 && _picked >= 0);
        GameArt.DrawBand(context, canvas, BandStyle.Button, x + DecideX + ButtonW, y + ButtonsY, (int)ButtonW, s, "중단",
                         focused: top && _focus == 1);
    }

    private void Reveal()
    {
        float t = _cursor * RowH;
        if (t < _scroll) _scroll = t;
        if (t + RowH > _scroll + _viewH) _scroll = t + RowH - _viewH;
    }

    public void Key(GameKey key)
    {
        switch (key)
        {
            case GameKey.Up when _focus < 0 && _rows.Count > 0: _cursor = (_cursor - 1 + _rows.Count) % _rows.Count; _picked = _cursor; Reveal(); break;
            case GameKey.Down when _focus < 0 && _rows.Count > 0: _cursor = (_cursor + 1) % _rows.Count; _picked = _cursor; Reveal(); break;
            case GameKey.Left or GameKey.Right: _focus = _focus switch { -1 => key == GameKey.Right ? 0 : 1, 0 => key == GameKey.Right ? 1 : -1, _ => key == GameKey.Right ? -1 : 0 }; break;
            case GameKey.Enter or GameKey.Space:
                if (_focus == 1) { _done(-1); break; }
                if (_picked < 0) _picked = _cursor;
                _done(_picked);
                break;
            case GameKey.Escape: _done(-1); break;
        }
    }

    public void Touch(MotionEventActions action, float px, float py)
    {
        if (action != MotionEventActions.Up) return;
        if (WindowArt.Hit(px, py, DecideX, ButtonsY, ButtonW, GameArt.BandHeight)) { if (_picked >= 0) _done(_picked); return; }
        if (WindowArt.Hit(px, py, DecideX + ButtonW, ButtonsY, ButtonW, GameArt.BandHeight)) { _done(-1); return; }
        if (WindowArt.Hit(px, py, TableX, TableY + RowH, _tableW, _viewH))
        {
            int i = (int)((py - TableY - RowH + _scroll) / RowH);
            if (i >= 0 && i < _rows.Count) { _picked = _cursor = i; _focus = -1; }
        }
    }

    public void Cancel() => _done(-1);
}

/// <summary>
/// 선명입력 창(<c>ShipNameDialog</c>) — 이름 칸(옆에 입력 단추), 그 아래 300x300 이름 목록, 결정 · 중단.
/// 이름을 꼭 지어야 하는 창은 중단이 없다.
/// </summary>
internal sealed class NameWindow : ChoicePanel.IGameWindow
{
    private const float Pad = 4, ListW = 300, ListH = 300, RowH = GameArt.TextHeight + 2, ButtonW = 110;
    private readonly IReadOnlyList<string> _names;
    private readonly bool _must;
    private readonly Action<string?> _done;
    private readonly Action<string, Action<string?>> _type;
    private string _name;
    private float _scroll;
    private float _dragFrom = float.NaN, _dragScroll;
    private bool _dragged;
    private int _focus;

    private readonly string _heading;

    public NameWindow(string current, IReadOnlyList<string> names, bool must, Action<string?> done, Action<string, Action<string?>> type,
                      string heading = "선명입력")
    {
        _name = current; _names = names; _must = must; _done = done; _type = type; _heading = heading;
        Width = 2 + Pad + 4 + ListW + 4 + 4 + Pad + 2;
        Height = 2 + Pad + GameArt.BandHeight + 4 + 26 + 4 + ListH + 8 + 8 + GameArt.BandHeight + 8 + Pad + 2;
    }

    public float Width { get; }
    public float Height { get; }
    private float Left => 2 + Pad + 4;
    private float FieldY => 2 + Pad + GameArt.BandHeight + 4;
    private float ListY => FieldY + 26 + 4;
    private float ButtonsY => ListY + ListH + 8 + 8;
    private string[] Buttons => _must ? ["결정"] : ["결정", "중단"];
    private float ButtonX(int i) => (Width - Buttons.Length * ButtonW) / 2 + i * ButtonW;

    public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
    {
        WindowArt.Rect(canvas, x, y, Width, Height, WindowArt.Back, s);
        WindowArt.Edges(canvas, x + Pad, y + Pad, Width - Pad * 2, Height - Pad * 2, WindowArt.Edge, 2, s);
        GameArt.DrawBand(context, canvas, BandStyle.Title, x + 2 + Pad, y + 2 + Pad, (int)(Width - (2 + Pad) * 2), s, _heading);
        float fw = ListW + 8;
        WindowArt.Rect(canvas, x + Left, y + FieldY, fw, 26, WindowArt.PageFill, s);
        WindowArt.Edges(canvas, x + Left, y + FieldY, fw, 26, WindowArt.ItemEdge, 2, s);
        GameText.Draw(canvas, _name, x + Left + 2 + 8, y + FieldY + 5, GameArt.TextHeight, GameFont.BlackColor, false, s);
        float bx = x + Left + fw - 2 - 4 - 18;
        WindowArt.Rect(canvas, bx, y + FieldY + 4, 18, 18, Color.Rgb(0xD2, 0xCA, 0xAD), s);
        WindowArt.Edges(canvas, bx, y + FieldY + 4, 18, 18, WindowArt.ItemEdge, 1, s);
        GameText.DrawCentered(canvas, "#", bx + 9, y + FieldY + 5, GameArt.TextHeight, GameFont.ButtonColor, false, s);

        WindowArt.Rect(canvas, x + Left, y + ListY, fw, ListH + 8, WindowArt.PageFill, s);
        WindowArt.Edges(canvas, x + Left, y + ListY, fw, ListH + 8, WindowArt.ItemEdge, 2, s);
        canvas.Save();
        canvas.ClipRect((x + Left + 4) * s, (y + ListY + 4) * s, (x + Left + 4 + ListW) * s, (y + ListY + 4 + ListH) * s);
        for (int i = 0; i < _names.Count; i++)
        {
            float ry = y + ListY + 4 + i * RowH - _scroll;
            if (ry + RowH < y + ListY || ry > y + ListY + ListH + 8) continue;
            if (_names[i] == _name) WindowArt.Rect(canvas, x + Left + 4, ry, ListW, RowH, Color.Rgb(0xD2, 0xCA, 0xAD), s);
            GameText.Draw(canvas, _names[i], x + Left + 4 + 10, ry + 1, GameArt.TextHeight, GameFont.BlackColor, false, s);
        }
        canvas.Restore();
        var b = Buttons;
        for (int i = 0; i < b.Length; i++)
            GameArt.DrawBand(context, canvas, BandStyle.Button, x + ButtonX(i), y + ButtonsY, (int)ButtonW, s, b[i], focused: top && i == _focus);
    }

    private void Decide()
    {
        string name = _name.Trim();
        if (_must && name.Length == 0) return;
        _done(name);
    }

    private void Step(int d)
    {
        int at = _names.ToList().IndexOf(_name);
        at = at < 0 ? 0 : (at + d + _names.Count) % _names.Count;
        _name = _names[at];
        float t = at * RowH;
        if (t < _scroll) _scroll = t;
        if (t + RowH > _scroll + ListH) _scroll = t + RowH - ListH;
    }

    public void Key(GameKey key)
    {
        switch (key)
        {
            case GameKey.Up: Step(-1); break;
            case GameKey.Down: Step(+1); break;
            case GameKey.Left or GameKey.Right: _focus = (_focus + 1) % Buttons.Length; break;
            case GameKey.Enter or GameKey.Space: if (Buttons[_focus] == "결정") Decide(); else Cancel(); break;
            case GameKey.Escape: Cancel(); break;
        }
    }

    public void Touch(MotionEventActions action, float px, float py)
    {
        bool inList = WindowArt.Hit(px, py, Left, ListY, ListW + 8, ListH + 8);
        switch (action)
        {
            case MotionEventActions.Down: _dragFrom = inList ? py : float.NaN; _dragScroll = _scroll; _dragged = false; return;
            case MotionEventActions.Move when !float.IsNaN(_dragFrom):
                if (Math.Abs(py - _dragFrom) > 6) _dragged = true;
                if (_dragged) _scroll = Math.Clamp(_dragScroll - (py - _dragFrom), 0, Math.Max(0, _names.Count * RowH - ListH));
                return;
            case MotionEventActions.Up: if (_dragged) { _dragFrom = float.NaN; return; } _dragFrom = float.NaN; break;
            default: return;
        }
        var b = Buttons;
        for (int i = 0; i < b.Length; i++)
            if (WindowArt.Hit(px, py, ButtonX(i), ButtonsY, ButtonW, GameArt.BandHeight)) { _focus = i; if (b[i] == "결정") Decide(); else Cancel(); return; }
        if (WindowArt.Hit(px, py, Left, FieldY, ListW + 8, 26)) { _type(_name, typed => { if (typed != null) _name = typed; }); return; }
        if (inList)
        {
            int i = (int)((py - ListY - 4 + _scroll) / RowH);
            if (i >= 0 && i < _names.Count) _name = _names[i];
        }
    }

    public void Cancel()
    {
        if (!_must) _done(null);
    }
}

/// <summary>
/// 정보 판 틀(<c>InfoDialog.Build</c>) — 세 겹 테, 판(여백 14 · 10 · 14 · 2) 안은 부르는 쪽이 그리고, 오른쪽 아래에 단추들.
/// 판 안의 손과 키도 부르는 쪽이 받는다. 편성 · 대열 · 지도 창들이 이 틀을 쓴다.
/// </summary>
internal sealed class BoardWindow : ChoicePanel.IGameWindow
{
    private const float PadL = 14, PadT = 10, PadR = 14, PadB = 2, BtnR = 10, BtnB = 10, Gap = 4;

    private readonly Color _board, _edge;
    private readonly string _title;
    private readonly float _w, _h;
    private readonly (string Label, float Width)[] _buttons;
    private readonly Action<Canvas, float, float, float> _draw;
    private readonly Action<string> _act;
    private readonly Func<float, float, string?>? _hit;
    private readonly Func<GameKey, string?>? _keys;
    private int _focus;

    public BoardWindow(string title, float boardW, float boardH, (string Label, float Width)[] buttons,
                       Action<Canvas, float, float, float> draw, Action<string> act,
                       Func<float, float, string?>? hit = null, Func<GameKey, string?>? keys = null,
                       Color? board = null, Color? edge = null)
    {
        _title = title; _w = boardW; _h = boardH; _buttons = buttons; _draw = draw; _act = act; _hit = hit; _keys = keys;
        _board = board ?? WindowArt.Back;
        _edge = edge ?? WindowArt.CountLine;
        float titleH = title.Length > 0 ? GameArt.TextHeight + 6 : 0;
        _titleH = titleH;
        Width = WindowArt.InfoInset * 2 + PadL + boardW + PadR;
        Height = WindowArt.InfoInset * 2 + PadT + titleH + boardH + PadB + (buttons.Length > 0 ? GameArt.BandHeight + BtnB : 0);
    }

    private readonly float _titleH;
    public float Width { get; }
    public float Height { get; }

    /// <summary>판 안 왼쪽 위(창 기준 원본 점).</summary>
    public float BoardX => WindowArt.InfoInset + PadL;
    public float BoardY => WindowArt.InfoInset + PadT + _titleH;

    private float ButtonX(int i)
    {
        float x = Width - WindowArt.InfoInset - BtnR;
        for (int k = _buttons.Length - 1; k >= i; k--) x -= _buttons[k].Width + (k < _buttons.Length - 1 ? Gap : 0);
        return x;
    }

    private float ButtonsY => Height - WindowArt.InfoInset - BtnB - GameArt.BandHeight;

    public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
    {
        WindowArt.InfoFrame(canvas, x, y, Width, Height, _board, _edge, s);
        if (_title.Length > 0)
            GameText.Draw(canvas, _title, x + BoardX, y + WindowArt.InfoInset + PadT, GameArt.TextHeight, GameFont.WhiteColor, false, s);
        canvas.Save();
        canvas.ClipRect((x + BoardX) * s, (y + BoardY) * s, (x + BoardX + _w) * s, (y + BoardY + _h) * s);
        _draw(canvas, x + BoardX, y + BoardY, s);
        canvas.Restore();
        for (int i = 0; i < _buttons.Length; i++)
            GameArt.DrawBand(context, canvas, BandStyle.Button, x + ButtonX(i), y + ButtonsY, (int)_buttons[i].Width, s,
                             _buttons[i].Label, focused: top && i == _focus);
    }

    public void Key(GameKey key)
    {
        if (_keys?.Invoke(key) is { } work) { _act(work); return; }
        switch (key)
        {
            case GameKey.Tab: if (_buttons.Length > 0) _focus = (_focus + 1) % _buttons.Length; break;
            case GameKey.Enter or GameKey.Space: if (_buttons.Length > 0) _act(_buttons[_focus].Label); break;
            case GameKey.Escape: Cancel(); break;
        }
    }

    public void Touch(MotionEventActions action, float px, float py)
    {
        if (action != MotionEventActions.Up) return;
        for (int i = 0; i < _buttons.Length; i++)
            if (WindowArt.Hit(px, py, ButtonX(i), ButtonsY, _buttons[i].Width, GameArt.BandHeight)) { _focus = i; _act(_buttons[i].Label); return; }
        if (WindowArt.Hit(px, py, BoardX, BoardY, _w, _h) && _hit?.Invoke(px - BoardX, py - BoardY) is { } work) _act(work);
    }

    public void Cancel() => _act("cancel");
}
