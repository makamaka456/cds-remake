using Android.App;
using Android.Widget;
using Android.Content;
using Android.Graphics;
using Android.Views;
using CdsHelper.Game.Local.Helpers;
using CostaDelSol.Droid.Ui;

namespace CostaDelSol.Droid.Input;

/// <summary>
/// 원본 창 셋을 그린다 — 자리·크기·색은 PC 판 코드에 적힌 원본 값 그대로다.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>차림표</b>(<c>GameMenu</c> · <c>ChoiceDialog</c>): 진홍 제목 띠 밑에 베이지 띠 줄, <b>마지막 줄만 회녹색</b>.
///         상자는 바탕 <c>311818</c>, 밝은 테 한 점(<c>B3AA96</c>)에 안쪽 여백 여섯 점, 자연 폭의 1.1 배.</item>
///   <item><b>물음창</b>(<c>ConfirmDialog.Ask</c>): 흰 글씨(색인 10) 아래 YES · NO(64x24, 사이 16). 칸 수 = max(30, 가장 긴 줄),
///         폭 = 칸 수 x 8 + 32, 한 줄이면 272 x 80. 테 <c>9B8D7D</c>.</item>
///   <item><b>알림창</b>(<c>ConfirmDialog.Tell</c>): 물음창과 같고 단추가 「확인」 하나.</item>
/// </list>
/// 고른 단추는 안쪽 테가 깜빡인다(원본 초점). 키는 원본 그대로 ↑·← 앞, ↓·→ 뒤, 엔터·스페이스 고르기, ESC 취소.
/// 창은 게임 화면(4:3 · 16:9 틀) 한가운데에 선다.
/// </remarks>
public sealed class ChoicePanel : View
{
    public enum Kind { Menu, Ask, Tell, Custom }

    /// <summary>
    /// 원본의 따로 지은 창(수량 입력 · 목록 · 계산기 …). 자리는 게임 화면 한가운데이고 좌표는 원본 점이다.
    /// </summary>
    public interface IGameWindow
    {
        float Width { get; }
        float Height { get; }
        void Draw(Context context, Canvas canvas, float x, float y, float scale, bool top);
        void Key(GameKey key);
        /// <summary>창 왼쪽 위에서 잰 원본 점.</summary>
        void Touch(MotionEventActions action, float x, float y);
        void Cancel();
        /// <summary>원본 판(640x480)에서의 자리. 없으면 한가운데.</summary>
        float? Left => null;
        float? Top => null;
        /// <summary>마우스 커서가 그 자리에 있다(창 왼쪽 위에서 잰 원본 점).</summary>
        void Hover(float x, float y) { }
        /// <summary>오른쪽 클릭 — 받았으면 참(안 받으면 창을 닫는 것으로 친다).</summary>
        bool RightClick(float x, float y) => false;
    }

    // ── 원본 값 ──────────────────────────────────────────────────────────
    private const int MenuEdgeW = 1, MenuPad = 6;            // GameMenu.BoxEdge · BoxPad
    private const float MenuWiden = 1.1f;                    // GameMenu.BoxWiden
    private const int AskSide = 7, AskTop = 7, AskBottom = 15, AskEdgeW = 1;   // ConfirmDialog.SidePad · TopPad · BottomPad
    private const int BodyGap = 6, TextGap = 10;             // ConfirmDialog.BodyGap · TextGap
    private const int MinCells = 30, CellWidth = 8;          // ConfirmDialog.MinCells · CellWidth
    private const int ButtonWidth = 64, ButtonGap = 16;      // ConfirmDialog.ButtonWidth · ButtonGap
    private const int MaxTextWidth = 640;                    // ConfirmDialog.MaxTextWidth
    private const int FaceColumn = 96, FacePad = 3;          // ConfirmDialog.FaceColumn · FacePad
    private const int FaceW = 80, FaceH = 96;                // Portraits.Width · Height

    /// <summary>
    /// ESC·오른쪽 클릭이 줄을 고르지 않고 창만 닫는다 — 받는 쪽에는 -1 이 간다. 원본 커맨드 창처럼
    /// 「취소」 줄이 없는 창에 쓴다.
    /// </summary>
    public const int CloseOnCancel = -2;

    /// <summary>
    /// 떠 있는 창 하나. 원본처럼 창은 겹친다 — 시설 차림표 위에 물음창이 서고, 닫으면 밑의 차림표가 그대로 있다.
    /// 손(키·손가락)은 맨 위 창만 받는다.
    /// </summary>
    public sealed class Layer
    {
        public Kind Kind;
        public string Title = "";
        public List<string> Rows = [];
        public List<string> Lines = [];
        public Action<int>? Chosen;
        public int CancelIndex = -1;
        public bool[] Dim = [];
        /// <summary>줄마다 띠 무늬. null 이면 원본 규칙(마지막 줄만 회녹색).</summary>
        public BandStyle?[] Styles = [];
        public int Selected;
        public int Scroll;
        public Bitmap? Face;
        public string Tag = "";
        /// <summary>한 줄짜리라도 왼쪽에 붙인다(얼굴이 서거나 여러 줄이면 원래 왼쪽이다).</summary>
        public bool LeftText;
        public IGameWindow? Custom;
        /// <summary>줄을 골라도 창이 그대로 선다(시설 명령 창 — 닫는 것은 그 줄의 일이 한다).</summary>
        public bool Sticky;
        /// <summary>잠깐 접어 둔 창 — 안 그리고 손도 안 받는다.</summary>
        public bool Hidden;

        public bool IsDim(int index) => index < Dim.Length && Dim[index];

        internal BandStyle StyleOf(int index) =>
            index < Styles.Length && Styles[index] is { } s ? s
            : index == Rows.Count - 1 && Kind == Kind.Menu ? BandStyle.Alt : BandStyle.Button;

        internal void FirstLive() =>
            Selected = Math.Max(0, Enumerable.Range(0, Rows.Count).FirstOrDefault(i => !IsDim(i), 0));
    }

    private readonly List<Layer> _layers = [];
    private int _pressed = -1;

    /// <summary>게임 화면 틀(뷰 좌표). 창은 그 한가운데에 서고 그 높이로 배율을 잡는다.</summary>
    public Func<RectF>? Area { get; set; }

    /// <summary>차림표(Menu) 밖을 눌렀을 때 — 이 판 안 화소 좌표. 뒤의 화면에 넘긴다.</summary>
    public Action<float, float>? Outside { get; set; }
    private bool _outside;

    public bool IsOpen => _layers.Any(l => !l.Hidden);

    /// <summary>창이 하나라도 떠 있으면 바뀐다 — 뒤의 화면(도시 그림)이 손을 받을지 가린다.</summary>
    public event Action? Changed;

    /// <summary>지금 맨 위 창의 이름표 — 같은 단추로 여닫는 창(Start = 커맨드)을 가리는 데 쓴다.</summary>
    public string Tag0 => _layers.Count > 0 ? _layers[^1].Tag : "";

    public ChoicePanel(Context context) : base(context)
    {
        Visibility = ViewStates.Gone;
        Clickable = true;   // 창이 떠 있는 동안 뒤로 손이 새지 않게(원본 창은 모달이다)
    }

    private Layer? Top => _layers.LastOrDefault(l => !l.Hidden);

    // ── 띄우기(한 창) — 바다 쪽 코드가 쓴다. 떠 있던 창을 다 걷고 새로 띄운다. ─────────

    /// <summary>차림표(<c>GameMenu</c>). <paramref name="body"/> 를 주면 줄 위에 글을 적는다.</summary>
    /// <param name="cancelIndex">ESC·오른쪽 클릭이 고르는 줄. -1 이면 취소로 안 닫힌다(<see cref="CloseOnCancel"/> 참고).</param>
    public void Show(string title, IReadOnlyList<string> options, Action<int> chosen, int cancelIndex = -1,
                     string tag = "", string? body = null, bool[]? dim = null)
    {
        Clear();
        Push(MakeMenu(title, options, chosen, cancelIndex, tag, body, dim));
    }

    /// <summary>물음창 — YES 면 true(<c>ConfirmDialog.Ask</c>). ESC 는 NO 다.</summary>
    public void Ask(string text, Action<bool> answered, string? title = null, string tag = "")
    {
        Clear();
        Push(MakeAsk(text, title, null, answered, tag));
    }

    /// <summary>알림창 — 확인 하나(<c>ConfirmDialog.Tell</c>).</summary>
    public void Tell(string text, Action? closed = null, string? title = null, string tag = "")
    {
        Clear();
        Push(MakeTell(text, title, null, closed, tag));
    }

    /// <summary>예전 이름 — 알림창과 같다.</summary>
    public void ShowText(string title, string body, Action? closed = null, string tag = "") =>
        Tell(body, closed, title, tag);

    // ── 창 짓기(겹쳐 띄우기용) ───────────────────────────────────────────

    public static Layer MakeMenu(string title, IReadOnlyList<string> options, Action<int> chosen, int cancelIndex = -1,
                                 string tag = "", string? body = null, bool[]? dim = null, BandStyle?[]? styles = null,
                                 bool plain = false)
    {
        // 예/아니오 두 줄은 원본에서 물음창(YES · NO)이다 — 그 꼴로 띄운다.
        if (!plain && options.Count == 2 && options[0] == "예" && options[1] == "아니오" && body == null)
            return new Layer
            {
                Kind = Kind.Ask, Rows = ["YES", "NO"], Chosen = chosen, CancelIndex = 1, Tag = tag,
                Lines = GameText.Wrap(title, MaxTextWidth),
            };
        var layer = new Layer
        {
            Kind = Kind.Menu, Title = title, Rows = [.. options], Chosen = chosen, CancelIndex = cancelIndex, Tag = tag,
            Lines = body == null ? [] : GameText.Wrap(body, MaxTextWidth), Dim = dim ?? [], Styles = styles ?? [],
        };
        layer.FirstLive();
        return layer;
    }

    public static Layer MakeAsk(string text, string? title, Bitmap? face, Action<bool> answered, string tag = "") => new()
    {
        Kind = Kind.Ask, Title = title ?? "", Rows = ["YES", "NO"], Chosen = at => answered(at == 0), CancelIndex = 1,
        Tag = tag, Face = face, Lines = GameText.Wrap(text, MaxTextWidth),
    };

    public static Layer MakeTell(string text, string? title, Bitmap? face, Action? closed, string tag = "", bool left = false) => new()
    {
        Kind = Kind.Tell, Title = title ?? "", Rows = ["확인"], Chosen = _ => closed?.Invoke(), CancelIndex = 0,
        Tag = tag, Face = face, Lines = GameText.Wrap(text, MaxTextWidth), LeftText = left,
    };

    /// <summary>창을 맨 위에 얹는다.</summary>
    public void Push(Layer layer)
    {
        _layers.Add(layer);
        _pressed = -1;
        Visibility = ViewStates.Visible;
        Invalidate();
        Changed?.Invoke();
    }

    /// <summary>그 창을 그 자리에서 갈아 끼운다(차림표를 다시 지을 때). 없으면 맨 위에 얹는다.</summary>
    public void Replace(Layer old, Layer layer)
    {
        int at = _layers.IndexOf(old);
        if (at < 0) { Push(layer); return; }
        _layers[at] = layer;
        Invalidate();
    }

    /// <summary>그 창을 걷는다.</summary>
    public void Remove(Layer layer)
    {
        if (!_layers.Remove(layer)) return;
        if (_layers.Count == 0) Visibility = ViewStates.Gone;
        Invalidate();
        Changed?.Invoke();
    }

    public bool Contains(Layer layer) => _layers.Contains(layer);

    private void Clear()
    {
        _layers.Clear();
        _pressed = -1;
    }

    public void Hide()
    {
        Clear();
        Visibility = ViewStates.Gone;
        Changed?.Invoke();
    }

    // ── 키 ───────────────────────────────────────────────────────────────

    public bool HandleKey(GameKey key)
    {
        if (Top is not { } top) return false;
        if (top.Custom is { } custom) { custom.Key(key); Invalidate(); return true; }
        switch (key)
        {
            case GameKey.Up or GameKey.Left: Move(top, -1); break;
            case GameKey.Down or GameKey.Right: Move(top, +1); break;
            case GameKey.Enter or GameKey.Space: if (!top.IsDim(top.Selected)) Choose(top, top.Selected); break;
            case GameKey.Escape: Cancel(); break;
        }
        return true;
    }

    /// <summary>ESC · 오른쪽 클릭.</summary>
    public void Cancel()
    {
        if (Top is not { } top) return;
        if (top.Custom is { } custom) { custom.Cancel(); Invalidate(); return; }
        if (top.CancelIndex >= 0) Choose(top, top.CancelIndex);
        else if (top.CancelIndex == CloseOnCancel) Choose(top, -1);
    }

    private void Move(Layer top, int delta)
    {
        if (top.Rows.Count == 0) return;
        for (int i = 0; i < top.Rows.Count; i++)   // 흐린 줄은 건너뛴다(원본 초점은 고를 수 있는 줄만 돈다)
        {
            top.Selected = (top.Selected + delta + top.Rows.Count) % top.Rows.Count;
            if (!top.IsDim(top.Selected)) break;
        }
        Invalidate();
    }

    private void Choose(Layer layer, int index)
    {
        var chosen = layer.Chosen;
        if (layer.Sticky && index >= 0) { chosen?.Invoke(index); return; }
        layer.Chosen = null;
        Remove(layer);
        chosen?.Invoke(index);
    }

    // ── 자리 셈 ──────────────────────────────────────────────────────────

    private RectF AreaRect() => Area?.Invoke() is { } r && r.Height() > 0
        ? r
        : FitFourThree(Width, Height);

    private static RectF FitFourThree(int w, int h)
    {
        float aw = Math.Min(w, h * 4f / 3f), ah = aw * 3f / 4f;
        return new RectF((w - aw) / 2, (h - ah) / 2, (w + aw) / 2, (h + ah) / 2);
    }

    private sealed record BoxLayout(float X, float Y, float W, float H, float RowsX, float RowsY, int RowW,
                                 float TextX, float TextY, int VisibleRows, float ButtonsY);

    private static BoxLayout MeasureBox(Layer l0, float areaW, float areaH)
    {
        if (l0.Kind == Kind.Menu)
        {
            int natural = l0.Rows.Select(GameArt.LeastBandWidth).DefaultIfEmpty(GameArt.BandWidth(1)).Max();
            if (l0.Title.Length > 0) natural = Math.Max(natural, GameArt.LeastBandWidth(l0.Title));
            int textW = l0.Lines.Count > 0 ? l0.Lines.Max(GameText.Width) : 0;
            natural = Math.Max(natural, textW);
            float desired = natural + (MenuEdgeW + MenuPad) * 2;
            int boxW = (int)Math.Ceiling(desired * MenuWiden);
            int rowW = boxW - (MenuEdgeW + MenuPad) * 2;
            int titleH = l0.Title.Length > 0 ? GameArt.BandHeight : 0;
            int linesH = l0.Lines.Count * GameArt.TextHeight + (l0.Lines.Count > 0 ? BodyGap : 0);
            int fixedH = (MenuEdgeW + MenuPad) * 2 + titleH + linesH;
            int visible = Math.Max(1, Math.Min(l0.Rows.Count, (int)((areaH - 16 - fixedH) / GameArt.BandHeight)));
            float h = fixedH + visible * GameArt.BandHeight;
            float x = (areaW - boxW) / 2f, y = (areaH - h) / 2f;
            float inner = MenuEdgeW + MenuPad;
            return new BoxLayout(x, y, boxW, h, x + inner, y + inner + titleH + linesH, rowW,
                              x + inner, y + inner + titleH + (l0.Lines.Count > 0 ? BodyGap / 2f : 0), visible, 0);
        }

        // 물음창 · 알림창. 얼굴이 서면 96 만큼 넓고, 글은 얼굴 오른쪽 8 부터 왼쪽맞춤이다.
        int widest = l0.Lines.Count > 0 ? l0.Lines.Max(GameText.Width) : 0;
        int cells = Math.Max(MinCells, (int)Math.Ceiling(widest / (float)CellWidth));
        int barW = cells * CellWidth + CellWidth * 2 + (l0.Face != null ? FaceColumn : 0);
        float w = barW + (AskSide + AskEdgeW) * 2;
        bool dh2 = Dh2.On;
        // 대항해시대2 스타일이면 제목 띠가 없고(요청), 단추 줄은 명판(높이 30)에 위아래 3 을 둔 띠다.
        int titleBar = l0.Title.Length > 0 && !dh2 ? GameArt.BandHeight : 0;
        int bodyH = Math.Max(l0.Lines.Count * GameArt.TextHeight, l0.Face != null ? FaceH : 0);
        int buttonsH = dh2 ? Dh2.BarHeight : GameArt.BandHeight;
        float hAsk = AskEdgeW + AskTop + titleBar + BodyGap + bodyH + TextGap + buttonsH + AskBottom + AskEdgeW;
        float ax = (areaW - w) / 2f, ay = (areaH - hAsk) / 2f;
        float left = ax + AskEdgeW + AskSide;
        float textY = ay + AskEdgeW + AskTop + titleBar + BodyGap;
        return new BoxLayout(ax, ay, w, hAsk, left, ay + AskEdgeW + AskTop, barW,
                             l0.Face != null ? left + FacePad + FaceW + CellWidth : left, textY, 0, textY + bodyH + TextGap);
    }

    /// <summary>단추 자리들(원본 점) — 물음창은 가운데 모은 YES·NO, 차림표는 세로로 쌓인 줄.</summary>
    private static List<(int Index, RectF Rect)> Buttons(Layer l0, BoxLayout l)
    {
        var list = new List<(int, RectF)>();
        if (l0.Kind == Kind.Menu)
        {
            for (int i = 0; i < l.VisibleRows && l0.Scroll + i < l0.Rows.Count; i++)
            {
                float y = l.RowsY + i * GameArt.BandHeight;
                list.Add((l0.Scroll + i, new RectF(l.RowsX, y, l.RowsX + l.RowW, y + GameArt.BandHeight)));
            }
            return list;
        }
        if (Dh2.On)
        {
            // 명판은 띠 왼쪽부터 3 씩 띄워 늘어놓는다(PC 판 Dh2Style.PlaqueBar).
            float px = l.RowsX + 3;
            for (int i = 0; i < l0.Rows.Count; i++)
            {
                float pw = Dh2.PlaqueWidth(l0.Rows[i]);
                list.Add((i, new RectF(px, l.ButtonsY + 3, px + pw, l.ButtonsY + 3 + Dh2.PlaqueHeight)));
                px += pw + 3;
            }
            return list;
        }
        float total = l0.Rows.Count * ButtonWidth + (l0.Rows.Count - 1) * ButtonGap;
        float bx = l.RowsX + (l.RowW - total) / 2f;
        for (int i = 0; i < l0.Rows.Count; i++)
        {
            float x = bx + i * (ButtonWidth + ButtonGap);
            list.Add((i, new RectF(x, l.ButtonsY, x + ButtonWidth, l.ButtonsY + GameArt.BandHeight)));
        }
        return list;
    }

    // ── 그리기 ───────────────────────────────────────────────────────────

    private static readonly Paint FacePaint = new() { FilterBitmap = false, AntiAlias = false };

    protected override void OnDraw(Canvas canvas)
    {
        if (!IsOpen) return;
        var area = AreaRect();
        float s = area.Height() / GameArt.GameHeight;
        float areaW = area.Width() / s, areaH = GameArt.GameHeight;

        canvas.Save();
        canvas.Translate(area.Left, area.Top);
        var top = Top;
        foreach (var layer in _layers.ToList()) if (!layer.Hidden) DrawLayer(canvas, layer, areaW, areaH, s, layer == top);
        canvas.Restore();
        PostInvalidateDelayed(GameArt.FocusBlinkMs / 2);   // 초점 테가 깜빡이게
    }

    private void DrawLayer(Canvas canvas, Layer l0, float areaW, float areaH, float s, bool top)
    {
        if (l0.Custom is { } custom)
        {
            var (cx, cy) = Place(custom, areaW, areaH);
            custom.Draw(Context!, canvas, cx, cy, s, top);
            return;
        }
        var l = MeasureBox(l0, areaW, areaH);

        // 줄이 창보다 많으면 고른 줄이 보이게 굴린다.
        if (l0.Kind == Kind.Menu && l.VisibleRows > 0)
        {
            if (l0.Selected < l0.Scroll) l0.Scroll = l0.Selected;
            if (l0.Selected >= l0.Scroll + l.VisibleRows) l0.Scroll = l0.Selected - l.VisibleRows + 1;
        }

        if (l0.Kind == Kind.Menu)
        {
            GameArt.DrawBox(canvas, l.X, l.Y, l.W, l.H, GameArt.MenuBack, GameArt.MenuEdge, s);
            float inner = MenuEdgeW + MenuPad;
            if (l0.Title.Length > 0) GameArt.DrawBand(Context!, canvas, BandStyle.Title, l.X + inner, l.Y + inner, l.RowW, s, l0.Title);
            for (int i = 0; i < l0.Lines.Count; i++)
                GameText.Draw(canvas, l0.Lines[i], l.TextX, l.TextY + i * GameArt.TextHeight, GameArt.TextHeight, GameFont.WhiteColor, false, s);
            foreach (var (index, r) in Buttons(l0, l))
                GameArt.DrawBand(Context!, canvas, l0.StyleOf(index), r.Left, r.Top, l.RowW, s, l0.Rows[index],
                                 dim: l0.IsDim(index), focused: top && index == l0.Selected && !l0.IsDim(index));
            return;
        }

        bool dh2 = Dh2.On;
        if (dh2) Dh2.DrawFrame(canvas, l.X, l.Y, l.W, l.H, s);
        else GameArt.DrawBox(canvas, l.X, l.Y, l.W, l.H, GameArt.MenuBack, GameArt.AskEdge, s);
        if (l0.Title.Length > 0 && !dh2) GameArt.DrawBand(Context!, canvas, BandStyle.Title, l.RowsX, l.RowsY, l.RowW, s, l0.Title);
        byte ink = dh2 ? GameFont.BlackColor : GameFont.WhiteColor;
        if (l0.Face is { } face)
        {
            float fx = l.RowsX + FacePad, fy = l.TextY;
            canvas.DrawBitmap(face, null, new RectF(fx * s, fy * s, (fx + FaceW) * s, (fy + FaceH) * s), FacePaint);
        }
        bool center = l0.Lines.Count == 1 && l0.Face == null && !l0.LeftText;   // 한 줄이면 가운데, 여러 줄·얼굴이면 왼쪽(원본)
        for (int i = 0; i < l0.Lines.Count; i++)
        {
            float y = l.TextY + i * GameArt.TextHeight;
            if (center) GameText.DrawCentered(canvas, l0.Lines[i], l.RowsX + l.RowW / 2f, y, GameArt.TextHeight, ink, false, s);
            else GameText.Draw(canvas, l0.Lines[i], l.TextX, y, GameArt.TextHeight, ink, false, s);
        }
        foreach (var (index, r) in Buttons(l0, l))
        {
            if (dh2) Dh2.DrawPlaque(canvas, r, l0.Rows[index], lit: index == l0.Selected, s);
            else GameArt.DrawBand(Context!, canvas, BandStyle.Button, r.Left, r.Top, ButtonWidth, s, l0.Rows[index],
                                  focused: top && index == l0.Selected);
        }
    }

    /// <summary>마우스 커서(오른 막대) — 맨 위 창이 따로 지은 창이면 그 자리를 알린다. 이 판 안의 좌표.</summary>
    public void Hover(float x, float y)
    {
        if (Top?.Custom is not { } custom) return;
        var area = AreaRect();
        float s = area.Height() / GameArt.GameHeight;
        var (cx, cy) = Place(custom, area.Width() / s, GameArt.GameHeight);
        custom.Hover((x - area.Left) / s - cx, (y - area.Top) / s - cy);
    }

    /// <summary>오른쪽 클릭 — 맨 위 창이 받으면 거기서 끝, 아니면 닫기(취소)다. 이 판 안의 좌표.</summary>
    public void RightClick(float x, float y)
    {
        if (Top?.Custom is { } custom)
        {
            var area = AreaRect();
            float s = area.Height() / GameArt.GameHeight;
            var (cx, cy) = Place(custom, area.Width() / s, GameArt.GameHeight);
            if (custom.RightClick((x - area.Left) / s - cx, (y - area.Top) / s - cy)) return;
        }
        Cancel();
    }

    /// <summary>원본 판(640x480, 가운데 정렬) 좌표의 네모를 이 판 안 화소 네모로.</summary>
    public RectF BoardToView(float x, float y, float w, float h)
    {
        var area = AreaRect();
        float s = area.Height() / GameArt.GameHeight;
        float left = area.Left + ((float)Math.Floor((area.Width() / s - GameArt.GameWidth) / 2) + x) * s;
        float top = area.Top + y * s;
        return new RectF(left, top, left + w * s, top + h * s);
    }

    /// <summary>따로 지은 창의 자리 — 제 자리가 있으면 원본 판(640 폭, 가운데 정렬) 기준으로 놓는다.</summary>
    private static (float X, float Y) Place(IGameWindow w, float areaW, float areaH) =>
        w.Left is { } l && w.Top is { } t
            ? ((float)Math.Floor((areaW - GameArt.GameWidth) / 2) + l, t)
            : ((float)Math.Floor((areaW - w.Width) / 2), (float)Math.Floor((areaH - w.Height) / 2));

    // ── 손가락 · 마우스 ──────────────────────────────────────────────────

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null || Top is not { } top) return false;
        var area = AreaRect();
        float s = area.Height() / GameArt.GameHeight;
        if (top.Custom is { } custom)
        {
            var (cx, cy) = Place(custom, area.Width() / s, GameArt.GameHeight);
            custom.Touch(e.Action, (e.GetX() - area.Left) / s - cx, (e.GetY() - area.Top) / s - cy);
            Invalidate();
            return true;
        }
        var l = MeasureBox(top, area.Width() / s, GameArt.GameHeight);
        float gx = (e.GetX() - area.Left) / s, gy = (e.GetY() - area.Top) / s;
        int hit = -1;
        foreach (var (index, r) in Buttons(top, l))
            if (r.Contains(gx, gy)) hit = index;

        bool inBox = gx >= l.X && gy >= l.Y && gx < l.X + l.W && gy < l.Y + l.H;
        switch (e.Action)
        {
            case MotionEventActions.Down:
                _pressed = hit;
                _outside = !inBox;
                if (hit >= 0 && !top.IsDim(hit)) { top.Selected = hit; Invalidate(); }
                break;
            case MotionEventActions.Up:
                if (hit >= 0 && hit == _pressed && !top.IsDim(hit)) Choose(top, hit);
                // 차림표 밖을 눌렀다 — 원본처럼 뒤의 화면(여관 · 술집 사진의 손님 따위)이 받는다.
                else if (_outside && !inBox && top.Kind == Kind.Menu) Outside?.Invoke(e.GetX(), e.GetY());
                _pressed = -1;
                _outside = false;
                break;
        }
        return true;
    }
}
