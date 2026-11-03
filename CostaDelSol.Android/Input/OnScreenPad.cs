using Android.App;
using Android.Widget;
using System.Text.Json;
using Android.Content;
using Android.Graphics;
using Android.Views;

namespace CostaDelSol.Droid.Input;

/// <summary>화면 패드를 언제 보일지. 자동이면 게임패드가 붙어 있는 동안 숨는다(Kairo OnScreenMode).</summary>
public enum OnScreenMode { Auto, Shown, Hidden }

/// <summary>
/// 화면 패드 — 게임패드가 없는 기기를 위한 화면 단추와 스틱. 패드와 <b>같은 표준 버튼</b>을 누르므로
/// 버튼 매핑을 바꾸면 화면 단추도 같이 바뀐다.
/// </summary>
/// <remarks>
/// 단추를 보이거나 숨기고, 끌어서 옮기고, 크기를 바꿀 수 있다(<see cref="BeginEdit"/>). 자리는 화면 비율로
/// 저장해 화면 크기가 달라도 같은 자리에 선다(Kairo OnScreenControls 와 같은 방식).
///
/// 단추가 아닌 곳의 손가락은 게임 화면 몫이다 — 톡 치기(왼쪽 클릭), 길게 누르기(오른쪽 클릭),
/// 두 손가락 오므리기/벌리기(확대·축소), 끌기(마우스 끌기).
/// </remarks>
public sealed class OnScreenPad : FrameLayout
{
    private const string PrefsName = "onscreen-pad-v2";   // v2: 배 스틱이 L 로 바뀌어 기본 배치를 새로 잡는다

    /// <summary>단추 하나의 자리 — 화면 비율 좌표(가운데)와 크기 배율.</summary>
    public sealed class Spot
    {
        public bool Shown { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Scale { get; set; } = 1f;
        /// <summary>불투명도(0.1~1).</summary>
        public float Alpha { get; set; } = 1f;
        /// <summary>단추에 적을 이름(4글자까지). 비면 단추 고유 이름(A · LB …)이다.</summary>
        public string? Label { get; set; }
    }

    private static readonly (PadControl Control, string Label, float X, float Y, bool Shown)[] Specs =
    [
        (PadControl.LeftStick, "L", .12f, .72f, true),
        (PadControl.RightStick, "R", .70f, .78f, false),
        (PadControl.Up, "↑", .30f, .44f, false),
        (PadControl.Down, "↓", .30f, .62f, false),
        (PadControl.Left, "←", .24f, .53f, false),
        (PadControl.Right, "→", .36f, .53f, false),
        (PadControl.A, "A", .87f, .86f, true),
        (PadControl.B, "B", .94f, .74f, true),
        (PadControl.X, "X", .80f, .74f, true),
        (PadControl.Y, "Y", .87f, .62f, true),
        (PadControl.L1, "LB", .06f, .30f, true),
        (PadControl.R1, "RB", .94f, .30f, true),
        (PadControl.L2, "LT", .06f, .17f, false),
        (PadControl.R2, "RT", .94f, .17f, false),
        (PadControl.L3, "LS", .24f, .93f, false),
        (PadControl.R3, "RS", .27f, .86f, true),
        (PadControl.Select, "SEL", .43f, .93f, true),
        (PadControl.Start, "STRT", .57f, .93f, true),
        (PadControl.Menu, "≡", .50f, .07f, true),
    ];

    // ── 밖으로 내는 것 ───────────────────────────────────────────────────

    /// <summary>표준 버튼 누름(true)/뗌(false).</summary>
    public event Action<PadControl, bool>? Button;
    /// <summary>화면 스틱 값(-1~1). 손을 떼면 (0, 0).</summary>
    public event Action<PadControl, float, float>? StickMoved;
    /// <summary>게임 화면을 톡 쳤다(왼쪽 클릭) · 길게 눌렀다(오른쪽 클릭). 화면 좌표.</summary>
    public event Action<float, float, bool>? MapClick;
    /// <summary>두 손가락으로 벌리고 오므린 배율.</summary>
    public event Action<float>? Pinch;
    /// <summary>게임 화면을 끈다(마우스 끌기). 단계 0 시작 · 1 움직임 · 2 끝.</summary>
    public event Action<float, float, int>? MapDrag;
    /// <summary>편집을 마쳤다.</summary>
    public event Action? EditDone;

    /// <summary>
    /// 단추가 아닌 곳을 눌렀을 때 아래 뷰(물음창·가상 키보드)로 넘길지. 참이면 손가락이 그쪽으로 간다 —
    /// 화면 패드는 늘 맨 위에 있어 창이 떠 있어도 A·B 단추를 누를 수 있다.
    /// </summary>
    public Func<float, float, bool>? PassThrough { get; set; }

    /// <summary>단추 글 밑에 작게 적을 할 일("엔터", "닻"). 매핑이 바뀌면 다시 부른다.</summary>
    public Func<PadControl, string>? Hint { get; set; }

    /// <summary>샘플 프리셋에 적을 그 단추의 기능 이름(「결정」 · 「닻」 …). 없으면 <see cref="Hint"/>.</summary>
    public Func<PadControl, string>? Function { get; set; }

    public OnScreenMode Mode { get; private set; } = OnScreenMode.Auto;
    public float GlobalScale { get; private set; } = 1f;
    private readonly Dictionary<PadControl, Spot> _spots = [];

    private readonly float _dp;
    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly Paint _edge = new() { AntiAlias = true };
    private readonly Paint _text = new() { AntiAlias = true, TextAlign = Paint.Align.Center, Color = Color.White };
    private readonly Paint _hint = new() { AntiAlias = true, TextAlign = Paint.Align.Center, Color = Color.Argb(220, 230, 210, 170) };

    private bool _padConnected;
    private bool _editing;
    private PadControl? _selected;
    private readonly LinearLayout _editBar;
    private readonly HorizontalScrollView _editScroll;
    private readonly TextView _editInfo;

    // 손가락 → 쥐고 있는 단추. 스틱은 손가락이 닿은 자리가 가운데다.
    private readonly Dictionary<int, PadControl> _owned = [];
    private readonly Dictionary<int, (float X, float Y)> _stickCenter = [];
    private readonly ScaleGestureDetector _scale;
    private readonly GestureDetector _gesture;
    private bool _mapGesture;
    private bool _dragging;
    private int _dragPointer = -1;
    private (float X, float Y) _dragStart;

    public OnScreenPad(Context context) : base(context)
    {
        _dp = context.Resources!.DisplayMetrics!.Density;
        SetWillNotDraw(false);
        MotionEventSplittingEnabled = true;
        _edge.SetStyle(Paint.Style.Stroke);
        _edge.StrokeWidth = 1.5f * _dp;
        _text.FakeBoldText = true;

        _scale = new ScaleGestureDetector(context, new ScaleListener(this));
        _gesture = new GestureDetector(context, new TapListener(this));
        _gesture.IsLongpressEnabled = true;

        _editBar = new LinearLayout(context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        _editBar.SetBackgroundColor(Color.Argb(225, 40, 26, 18));
        int pad = (int)(6 * _dp);
        _editBar.SetPadding(pad, pad, pad, pad);
        _editInfo = new TextView(context);
        _editInfo.SetTextColor(Color.White);
        _editInfo.SetPadding(pad, 0, pad * 2, 0);
        _editBar.AddView(_editInfo);
        AddBarButton("크기 −", () => Resize(-0.1f));
        AddBarButton("크기 ＋", () => Resize(+0.1f));
        AddBarButton("투명 ＋", () => Fade(-0.1f));
        AddBarButton("투명 −", () => Fade(+0.1f));
        AddBarButton("이름", AskName);
        AddBarButton("숨기기/보이기", ToggleShown);
        AddBarButton("프리셋 저장", SavePreset);
        AddBarButton("프리셋 불러오기", LoadPreset);
        AddBarButton("기본 배치", () => { ResetLayout(); Invalidate(); });
        AddBarButton("완료", EndEdit);
        // 단추가 많아 좁은 화면에서는 옆으로 밀어 본다.
        _editScroll = new HorizontalScrollView(context) { Visibility = ViewStates.Gone };
        _editBar.Visibility = ViewStates.Visible;
        _editScroll.AddView(_editBar);
        AddView(_editScroll, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Top | GravityFlags.CenterHorizontal));

        Load();
    }

    private void AddBarButton(string text, Action run)
    {
        var b = new Button(Context) { Text = text };
        b.SetAllCaps(false);
        b.Click += (_, _) => { run(); UpdateEditInfo(); };
        _editBar.AddView(b);
    }

    // ── 보이기 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 가상 키보드가 떠 있다 — 그동안은 메뉴(≡) 단추만 남기고 패드를 숨긴다.
    /// </summary>
    public bool KeyboardOpen
    {
        get => _keyboardOpen;
        set
        {
            if (_keyboardOpen == value) return;
            _keyboardOpen = value;
            if (value) ReleaseAll();
            Invalidate();
        }
    }
    private bool _keyboardOpen;

    /// <summary>지금 그리고 받는 단추인지 — 키보드가 떠 있으면 메뉴 단추만.</summary>
    private bool Live(PadControl control) => _editing || !_keyboardOpen || control == PadControl.Menu;

    public bool PadVisible => _editing || Mode switch
    {
        OnScreenMode.Shown => true,
        OnScreenMode.Hidden => false,
        _ => !_padConnected,
    };

    /// <summary>게임패드가 붙었는지 알려 준다(자동 모드가 따른다).</summary>
    public void SetPadConnected(bool connected)
    {
        if (_padConnected == connected) return;
        _padConnected = connected;
        if (!PadVisible) ReleaseAll();
        Invalidate();
    }

    public void SetMode(OnScreenMode mode)
    {
        Mode = mode;
        if (!PadVisible) ReleaseAll();
        Save();
        Invalidate();
    }

    public static string ModeLabel(OnScreenMode mode) => mode switch
    {
        OnScreenMode.Shown => "늘 보임",
        OnScreenMode.Hidden => "늘 숨김",
        _ => "자동 (게임패드가 붙으면 숨김)",
    };

    private float SizeOf(PadControl control)
    {
        float baseSize = control is PadControl.LeftStick or PadControl.RightStick ? 120 : 54;
        return baseSize * _dp * GlobalScale * _spots[control].Scale;
    }

    private (float X, float Y) CenterOf(PadControl control) => (_spots[control].X * Width, _spots[control].Y * Height);

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (!PadVisible) return;

        foreach (var (control, label, _, _, _) in Specs)
        {
            var spot = _spots[control];
            if (!spot.Shown && !_editing) continue;
            if (!Live(control)) continue;
            var (cx, cy) = CenterOf(control);
            float r = SizeOf(control) / 2;
            int alpha = (int)((spot.Shown ? 255 : 90) * Math.Clamp(spot.Alpha, 0.1f, 1f));
            bool held = _owned.ContainsValue(control);

            _fill.Color = _selected == control ? Color.Argb(Math.Max(alpha, 120) * 220 / 255, 90, 120, 160)
                : held ? Color.Argb(alpha * 200 / 255, 140, 100, 60)
                : Color.Argb(alpha * 110 / 255, 40, 25, 15);
            _edge.Color = Color.Argb(alpha * 200 / 255, 230, 210, 170);
            canvas.DrawCircle(cx, cy, r, _fill);
            canvas.DrawCircle(cx, cy, r, _edge);

            if (control is PadControl.LeftStick or PadControl.RightStick)
            {
                // 쥐고 있으면 손잡이가 손가락을 따른다.
                var knob = (cx, cy);
                foreach (var (pointer, owned) in _owned)
                    if (owned == control && _stickCenter.TryGetValue(pointer, out _)) knob = _knob[control];
                _fill.Color = Color.Argb(alpha * 150 / 255, 230, 210, 170);
                canvas.DrawCircle(knob.Item1, knob.Item2, r * 0.42f, _fill);
            }

            // 이름 한 줄 — 사용자가 적은 것(4글자까지), 없으면 단추 고유 이름.
            string name = string.IsNullOrEmpty(spot.Label) ? label : spot.Label!;
            _text.Color = Color.Argb(Math.Min(255, alpha), 255, 255, 255);
            _text.TextSize = Math.Max(10 * _dp, r * (name.Length <= 2 ? 0.62f : name.Length == 3 ? 0.5f : 0.42f));
            canvas.DrawText(name, cx, cy + _text.TextSize / 3, _text);
        }
    }

    private readonly Dictionary<PadControl, (float, float)> _knob = [];

    // ── 손가락 ───────────────────────────────────────────────────────────

    private PadControl? HitTest(float x, float y)
    {
        if (!PadVisible) return null;
        PadControl? best = null;
        float bestD = float.MaxValue;
        foreach (var (control, _, _, _, _) in Specs)
        {
            if (!_spots[control].Shown && !_editing) continue;
            if (!Live(control)) continue;
            var (cx, cy) = CenterOf(control);
            float r = SizeOf(control) / 2 * 1.15f;
            float d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            if (d <= r * r && d < bestD) { best = control; bestD = d; }
        }
        return best;
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null) return false;
        if (_editing) return EditTouch(e);

        int index = e.ActionIndex;
        int id = e.GetPointerId(index);
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
            {
                if (HitTest(e.GetX(index), e.GetY(index)) is { } control)
                {
                    _owned[id] = control;
                    if (control is PadControl.LeftStick or PadControl.RightStick)
                    {
                        _stickCenter[id] = CenterOf(control);
                        MoveStick(control, id, e.GetX(index), e.GetY(index));
                    }
                    else Button?.Invoke(control, true);
                    Invalidate();
                    return true;
                }
                if (e.ActionMasked == MotionEventActions.Down)
                {
                    if (PassThrough?.Invoke(e.GetX(), e.GetY()) == true) return false;   // 아래 창이 받는다
                    _mapGesture = true;
                }
                break;
            }
            case MotionEventActions.Move:
                for (int i = 0; i < e.PointerCount; i++)
                {
                    int pid = e.GetPointerId(i);
                    if (_owned.TryGetValue(pid, out var c) && c is PadControl.LeftStick or PadControl.RightStick)
                        MoveStick(c, pid, e.GetX(i), e.GetY(i));
                }
                break;
            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                Release(id);
                break;
            case MotionEventActions.Cancel:
                ReleaseAll();
                break;
        }

        // 단추를 쥔 손가락이 없을 때만 게임 화면 몸짓으로 본다 — 스틱을 쥔 손이 길게 누르기로 읽히지 않게.
        if (_mapGesture)
        {
            var mapped = MotionEvent.Obtain(e)!;
            _scale.OnTouchEvent(mapped);
            if (_owned.Count == 0) _gesture.OnTouchEvent(mapped);
            TrackDrag(e);
            mapped.Recycle();
        }
        if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel) _mapGesture = false;
        return true;
    }

    private void TrackDrag(MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _dragPointer = e.GetPointerId(0);
                _dragStart = (e.GetX(), e.GetY());
                _dragging = false;
                break;
            case MotionEventActions.Move:
                int at = e.FindPointerIndex(_dragPointer);
                if (at < 0 || e.PointerCount > 1 || _scale.IsInProgress) break;
                float x = e.GetX(at), y = e.GetY(at);
                if (!_dragging && Math.Abs(x - _dragStart.X) + Math.Abs(y - _dragStart.Y) > 16 * _dp)
                {
                    _dragging = true;
                    MapDrag?.Invoke(_dragStart.X, _dragStart.Y, 0);
                }
                if (_dragging) MapDrag?.Invoke(x, y, 1);
                break;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_dragging) MapDrag?.Invoke(e.GetX(), e.GetY(), 2);
                _dragging = false;
                _dragPointer = -1;
                break;
        }
    }

    private void MoveStick(PadControl stick, int pointer, float x, float y)
    {
        var (cx, cy) = _stickCenter[pointer];
        float r = SizeOf(stick) / 2;
        float dx = x - cx, dy = y - cy;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len > r) { dx *= r / len; dy *= r / len; }
        _knob[stick] = (cx + dx, cy + dy);
        StickMoved?.Invoke(stick, dx / r, dy / r);
        Invalidate();
    }

    private void Release(int pointer)
    {
        if (!_owned.Remove(pointer, out var control)) return;
        if (control is PadControl.LeftStick or PadControl.RightStick)
        {
            _stickCenter.Remove(pointer);
            StickMoved?.Invoke(control, 0, 0);
        }
        else Button?.Invoke(control, false);
        Invalidate();
    }

    private void ReleaseAll()
    {
        foreach (int pointer in _owned.Keys.ToList()) Release(pointer);
    }

    private sealed class ScaleListener(OnScreenPad pad) : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        public override bool OnScale(ScaleGestureDetector detector)
        {
            pad.Pinch?.Invoke(detector.ScaleFactor);
            return true;
        }
    }

    private sealed class TapListener(OnScreenPad pad) : GestureDetector.SimpleOnGestureListener
    {
        public override bool OnDown(MotionEvent e) => true;

        public override bool OnSingleTapUp(MotionEvent e)
        {
            if (!pad._dragging) pad.MapClick?.Invoke(e.GetX(), e.GetY(), true);
            return true;
        }

        public override void OnLongPress(MotionEvent e)
        {
            if (!pad._dragging && !pad._scale.IsInProgress) pad.MapClick?.Invoke(e.GetX(), e.GetY(), false);
        }
    }

    // ── 편집 ─────────────────────────────────────────────────────────────

    public bool Editing => _editing;

    /// <summary>배치·크기 편집을 연다. 숨긴 단추도 흐리게 보인다 — 눌러서 고르고 「숨기기/보이기」로 켠다.</summary>
    public void BeginEdit()
    {
        ReleaseAll();
        _editing = true;
        _selected = null;
        _editScroll.Visibility = ViewStates.Visible;
        UpdateEditInfo();
        Invalidate();
    }

    private void EndEdit()
    {
        _editing = false;
        _selected = null;
        _editScroll.Visibility = ViewStates.Gone;
        Save();
        Invalidate();
        EditDone?.Invoke();
    }

    private void UpdateEditInfo()
    {
        _editInfo.Text = _selected is { } c
            ? $"{PadBindings.ControlName(c)} · 크기 {_spots[c].Scale * GlobalScale:0.0}배 · 불투명 {_spots[c].Alpha * 100:0}% · {(_spots[c].Shown ? "보임" : "숨김")}"
            : $"단추를 눌러 고르고 끌어 옮기세요 · 고르지 않고 바꾸면 전체에 듭니다(크기 {GlobalScale:0.0}배)";
    }

    private void Resize(float step)
    {
        if (_selected is { } c) _spots[c].Scale = Math.Clamp(_spots[c].Scale + step, 0.5f, 2.5f);
        else GlobalScale = Math.Clamp(GlobalScale + step, 0.5f, 2.5f);
        Invalidate();
    }

    /// <summary>불투명도를 바꾼다 — 고른 단추, 고르지 않았으면 모두.</summary>
    private void Fade(float step)
    {
        foreach (var spot in _selected is { } c ? new[] { _spots[c] } : _spots.Values.ToArray())
            spot.Alpha = (float)Math.Round(Math.Clamp(spot.Alpha + step, 0.1f, 1f), 1);
        Invalidate();
    }

    /// <summary>고른 단추의 이름(4글자까지)을 적는다. 비우면 고유 이름으로 돌아간다.</summary>
    private void AskName()
    {
        if (_selected is not { } c) { Toast.MakeText(Context, "이름을 바꿀 단추를 먼저 누르세요", ToastLength.Short)!.Show(); return; }
        var input = new EditText(Context) { Text = _spots[c].Label ?? "" };
        input.SetFilters([new Android.Text.InputFilterLengthFilter(4)]);
        input.SetSingleLine(true);
        new AlertDialog.Builder(Context)
            .SetTitle($"{PadBindings.ControlName(c)} 이름 (4글자까지 · 비우면 {DefaultLabel(c)})")!
            .SetView(input)!
            .SetPositiveButton("확인", (_, _) =>
            {
                string text = input.Text?.Trim() ?? "";
                _spots[c].Label = text.Length == 0 ? null : text[..Math.Min(4, text.Length)];
                UpdateEditInfo();
                Invalidate();
            })!
            .SetNegativeButton("취소", (_, _) => { })!
            .Show();
    }

    private static string DefaultLabel(PadControl c) => Specs.First(s => s.Control == c).Label;

    // ── 프리셋 ──

    private const string PresetKey = "presets";
    private const string SampleName = "샘플: 기능 이름";

    private Dictionary<string, Stored> Presets()
    {
        try
        {
            var prefs = Context!.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
            return prefs.GetString(PresetKey, null) is { } json
                ? JsonSerializer.Deserialize<Dictionary<string, Stored>>(json) ?? []
                : [];
        }
        catch (JsonException) { return []; }
    }

    private void StorePresets(Dictionary<string, Stored> presets) =>
        Context!.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.Edit()!
            .PutString(PresetKey, JsonSerializer.Serialize(presets))!.Apply();

    private Stored Snapshot() => new(Mode.ToString(), GlobalScale,
        _spots.ToDictionary(p => p.Key.ToString(), p => new Spot { Shown = p.Value.Shown, X = p.Value.X, Y = p.Value.Y, Scale = p.Value.Scale, Alpha = p.Value.Alpha, Label = p.Value.Label }));

    private void Apply(Stored stored)
    {
        ResetLayout();
        GlobalScale = Math.Clamp(stored.Scale, 0.5f, 2.5f);
        foreach (var (name, spot) in stored.Spots)
            if (Enum.TryParse<PadControl>(name, out var control) && _spots.ContainsKey(control)) _spots[control] = spot;
        UpdateEditInfo();
        Invalidate();
    }

    private void SavePreset()
    {
        var input = new EditText(Context);
        input.SetSingleLine(true);
        new AlertDialog.Builder(Context)
            .SetTitle("지금 배치를 프리셋으로 저장 — 이름")!
            .SetView(input)!
            .SetPositiveButton("저장", (_, _) =>
            {
                string name = input.Text?.Trim() ?? "";
                if (name.Length == 0 || name == SampleName) return;
                var all = Presets();
                all[name] = Snapshot();
                StorePresets(all);
                Toast.MakeText(Context, $"「{name}」 저장", ToastLength.Short)!.Show();
            })!
            .SetNegativeButton("취소", (_, _) => { })!
            .Show();
    }

    private void LoadPreset()
    {
        var all = Presets();
        var names = new List<string> { SampleName };
        names.AddRange(all.Keys.OrderBy(n => n));
        new AlertDialog.Builder(Context)
            .SetTitle("프리셋 불러오기")!
            .SetItems(names.ToArray(), (_, e) =>
            {
                string name = names[e.Which];
                if (name == SampleName) { Apply(Sample()); return; }
                new AlertDialog.Builder(Context)
                    .SetTitle(name)!
                    .SetPositiveButton("불러오기", (_, _) => Apply(all[name]))!
                    .SetNeutralButton("지우기", (_, _) => { all.Remove(name); StorePresets(all); })!
                    .SetNegativeButton("취소", (_, _) => { })!
                    .Show();
            })!
            .SetNegativeButton("취소", (_, _) => { })!
            .Show();
    }

    /// <summary>
    /// 샘플 프리셋 — 기본 배치에 단추 고유 이름 대신 <b>지금 맡은 기능</b>(결정 · 취소 · 닻 …, 4글자까지)을 적은 것.
    /// 앱에 실려 있어 언제든 불러올 수 있다.
    /// </summary>
    private Stored Sample()
    {
        ResetLayout();
        var stored = Snapshot();
        foreach (var (control, _, _, _, _) in Specs)
        {
            string hint = Function?.Invoke(control) is { Length: > 0 } f ? f : Hint?.Invoke(control) ?? "";
            if (hint.Length > 0) stored.Spots[control.ToString()].Label = hint[..Math.Min(4, hint.Length)];
        }
        return stored;
    }

    private void ToggleShown()
    {
        if (_selected is { } c) _spots[c].Shown = !_spots[c].Shown;
        Invalidate();
    }

    private (float X, float Y) _grabOffset;

    private bool EditTouch(MotionEvent e)
    {
        float x = e.GetX(), y = e.GetY();
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _selected = HitTest(x, y);
                if (_selected is { } c)
                {
                    var (cx, cy) = CenterOf(c);
                    _grabOffset = (x - cx, y - cy);
                }
                UpdateEditInfo();
                Invalidate();
                return true;
            case MotionEventActions.Move:
                if (_selected is { } moving && Width > 0 && Height > 0)
                {
                    _spots[moving].X = Math.Clamp((x - _grabOffset.X) / Width, 0.02f, 0.98f);
                    _spots[moving].Y = Math.Clamp((y - _grabOffset.Y) / Height, 0.02f, 0.98f);
                    Invalidate();
                }
                return true;
        }
        return true;
    }

    // ── 저장 ─────────────────────────────────────────────────────────────

    private void ResetLayout()
    {
        _spots.Clear();
        foreach (var (control, _, x, y, shown) in Specs) _spots[control] = new Spot { Shown = shown, X = x, Y = y };
        GlobalScale = 1f;
    }

    public void ResetToDefaults()
    {
        ResetLayout();
        Mode = OnScreenMode.Auto;
        Save();
        Invalidate();
    }

    private sealed record Stored(string Mode, float Scale, Dictionary<string, Spot> Spots);

    private void Load()
    {
        ResetLayout();
        try
        {
            var prefs = Context!.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
            if (prefs.GetString("layout", null) is not { } json) return;
            var stored = JsonSerializer.Deserialize<Stored>(json);
            if (stored == null) return;
            if (Enum.TryParse<OnScreenMode>(stored.Mode, out var mode)) Mode = mode;
            GlobalScale = Math.Clamp(stored.Scale, 0.5f, 2.5f);
            foreach (var (name, spot) in stored.Spots)
                if (Enum.TryParse<PadControl>(name, out var control) && _spots.ContainsKey(control)) _spots[control] = spot;
        }
        catch (JsonException) { ResetLayout(); }
    }

    private void Save()
    {
        var stored = new Stored(Mode.ToString(), GlobalScale, _spots.ToDictionary(p => p.Key.ToString(), p => p.Value));
        Context!.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.Edit()!
            .PutString("layout", JsonSerializer.Serialize(stored))!.Apply();
    }
}
