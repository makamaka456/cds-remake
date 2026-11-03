using Android.App;
using Android.Widget;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace CostaDelSol.Droid.Input;

/// <summary>
/// 버튼 매핑 — 패드 버튼마다 원본 키보드 키 · 마우스 클릭 · 앱 기능을 붙인다(Kairo98 처럼 버튼에 키를 직접).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>줄을 누르면(패드면 위/아래로 고르고 엔터에 붙은 버튼) 붙일 것을 고르는 목록이 뜬다.</item>
///   <item><b>스틱은 「배 이동(8방향)」과 「마우스 이동」 둘만</b> 고른다.</item>
///   <item>메뉴 버튼은 바꿀 수 없다 — 메뉴로 가는 길이 사라지지 않게.</item>
///   <item>오른쪽 위 「대항해시대3 단축키」는 원본 키보드 단축키 목록을 띄운다.</item>
///   <item>패드 버튼을 누르면 맨 아래에 그 버튼 이름이 뜬다 — 어느 줄인지 찾기 쉽게.</item>
/// </list>
/// 바꾼 것은 곧바로 기기에 남는다.
/// </remarks>
[Activity(Label = "버튼 매핑",
          Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
          ScreenOrientation = ScreenOrientation.SensorLandscape)]
public sealed class PadSettingsActivity : Activity
{
    private static readonly PadControl[] Rows =
    [
        PadControl.RightStick, PadControl.LeftStick,
        PadControl.A, PadControl.B, PadControl.X, PadControl.Y,
        PadControl.Up, PadControl.Down, PadControl.Left, PadControl.Right,
        PadControl.L1, PadControl.R1, PadControl.L2, PadControl.R2,
        PadControl.L3, PadControl.R3,
        PadControl.Select, PadControl.Start, PadControl.Menu,
    ];

    private PadBindings _bindings = new();
    private readonly PadInput _input = new();
    private readonly List<TextView> _rows = [];
    private ScrollView _scroll = null!;
    private TextView _last = null!, _deadZone = null!;
    private ChoicePanel _panel = null!;
    private int _selected;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _bindings = PadBindings.Load(this);
        _input.Button += OnPad;
        float dp = Resources!.DisplayMetrics!.Density;
        int pad = (int)(14 * dp);

        var frame = new FrameLayout(this);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.Rgb(40, 26, 18));
        root.SetPadding(pad, pad, pad, pad);
        frame.AddView(root);

        // 위 줄 — 제목과 오른쪽 위 「대항해시대3 단축키」.
        var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var title = new TextView(this) { Text = "버튼 매핑" };
        title.SetTextColor(Color.White);
        title.SetTextSize(Android.Util.ComplexUnitType.Sp, 22);
        top.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        var shortcuts = new Button(this) { Text = "대항해시대3 단축키" };
        shortcuts.SetAllCaps(false);
        shortcuts.Click += (_, _) => ShowShortcuts();
        top.AddView(shortcuts);
        root.AddView(top);

        var help = new TextView(this)
        {
            Text = "줄을 누르면 그 버튼에 붙일 키를 고릅니다. 게임은 원본처럼 키보드 키를 받습니다. " +
                   "스틱은 배 이동(8방향)과 마우스 이동 가운데 하나만 고릅니다.",
        };
        help.SetTextColor(Color.Rgb(214, 189, 140));
        help.SetPadding(0, (int)(4 * dp), 0, (int)(8 * dp));
        root.AddView(help);

        _scroll = new ScrollView(this);
        var list = new LinearLayout(this) { Orientation = Orientation.Vertical };
        for (int i = 0; i < Rows.Length; i++)
        {
            int at = i;
            var row = new TextView(this);
            row.SetTextColor(Color.White);
            row.SetTextSize(Android.Util.ComplexUnitType.Sp, 17);
            row.SetPadding((int)(10 * dp), (int)(9 * dp), (int)(10 * dp), (int)(9 * dp));
            row.Click += (_, _) => { _selected = at; Refresh(); Pick(Rows[at]); };
            list.AddView(row);
            _rows.Add(row);
        }
        _scroll.AddView(list);
        root.AddView(_scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var bottom = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _deadZone = new TextView(this);
        _deadZone.SetTextColor(Color.White);
        _deadZone.SetPadding(0, 0, (int)(8 * dp), 0);
        bottom.AddView(_deadZone);
        AddButton(bottom, "−", () => StepDeadZone(-0.05f));
        AddButton(bottom, "＋", () => StepDeadZone(+0.05f));
        AddButton(bottom, "기본값으로", AskReset);
        AddButton(bottom, "닫기", Finish);
        root.AddView(bottom);

        _last = new TextView(this) { Text = "방금 누른 패드 버튼: -" };
        _last.SetTextColor(Color.Rgb(180, 180, 180));
        root.AddView(_last);

        _panel = new ChoicePanel(this);
        frame.AddView(_panel, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        SetContentView(frame);
        Refresh();
    }

    private void AddButton(LinearLayout parent, string text, Action run)
    {
        var b = new Button(this) { Text = text };
        b.SetAllCaps(false);
        b.Click += (_, _) => run();
        parent.AddView(b);
    }

    private void Refresh()
    {
        for (int i = 0; i < Rows.Length; i++)
        {
            var control = Rows[i];
            string what = control switch
            {
                PadControl.LeftStick => PadBindings.StickLabel(_bindings.LeftStick),
                PadControl.RightStick => PadBindings.StickLabel(_bindings.RightStick),
                _ => _bindings.TargetOf(control).Label + (PadBindings.Locked(control) ? "  (고정)" : ""),
            };
            if (_bindings.TargetOf(control).Key is { } key && GameKeys.Use(key) is { Length: > 0 } use)
                what += $"   · {use}";
            _rows[i].Text = $"{PadBindings.ControlName(control),-16}  →  {what}";
            _rows[i].SetBackgroundColor(i == _selected ? Color.Rgb(110, 76, 46) : Color.Transparent);
        }
        _deadZone.Text = $"스틱 데드존 {_bindings.DeadZone:0.00}";
    }

    // ── 고르기 ─────────────────────────────────────────────────────────

    private void Pick(PadControl control)
    {
        if (PadBindings.Locked(control))
        {
            _panel.ShowText("메뉴 버튼", "메뉴 버튼은 늘 메뉴를 엽니다. 메뉴로 가는 길이 사라지지 않게 바꿀 수 없습니다.");
            return;
        }

        if (control is PadControl.LeftStick or PadControl.RightStick)
        {
            StickMode[] modes = [StickMode.Ship, StickMode.Mouse];
            _panel.Show($"{PadBindings.ControlName(control)} — 쓰임을 고르십시오",
                [.. modes.Select(PadBindings.StickLabel), "돌아간다"], at =>
                {
                    if (at < 0 || at >= modes.Length) return;
                    if (control == PadControl.LeftStick) _bindings.LeftStick = modes[at];
                    else _bindings.RightStick = modes[at];
                    Changed();
                }, cancelIndex: modes.Length);
            return;
        }

        var targets = new List<PadTarget>();
        targets.AddRange(Enum.GetValues<GameKey>().Select(PadTarget.Of));
        targets.Add(PadTarget.LeftClick);
        targets.Add(PadTarget.RightClick);
        targets.Add(PadTarget.Of(AppAction.VirtualKeyboard));
        targets.Add(PadTarget.None);
        var labels = targets.Select(t => t.Key is { } k && GameKeys.Use(k) is { Length: > 0 } use
            ? $"{GameKeys.Name(k),-6}  {use}" : t.Label).ToList();
        labels.Add("돌아간다");

        _panel.Show($"{PadBindings.ControlName(control)} — 지금: {_bindings.TargetOf(control).Label}", labels, at =>
        {
            if (at < 0 || at >= targets.Count) return;
            _bindings.Set(control, targets[at]);
            Changed();
        }, cancelIndex: labels.Count - 1);
    }

    private void Changed()
    {
        _bindings.Save(this);
        Refresh();
    }

    private void StepDeadZone(float step)
    {
        _bindings.DeadZone = MathF.Round(Math.Clamp(_bindings.DeadZone + step, 0.1f, 0.9f), 2);
        Changed();
    }

    private void AskReset()
    {
        _panel.Show("모든 버튼을 기본값으로 되돌릴까요?", ["예", "아니오"], at =>
        {
            if (at != 0) return;
            _bindings.ResetDefaults();
            Changed();
        }, cancelIndex: 1);
    }

    private void ShowShortcuts() => _panel.ShowText("대항해시대3 단축키", MainActivity.ShortcutText());

    // ── 패드로 다루기 ─────────────────────────────────────────────────

    private void OnPad(PadControl control, bool down, bool repeat)
    {
        if (!down) return;
        if (!repeat) _last.Text = $"방금 누른 패드 버튼: {PadBindings.ControlName(control)}";

        // 이 화면에서도 매핑을 그대로 따른다 — 엔터에 붙은 버튼이 고르고 ESC 에 붙은 버튼이 물러난다.
        var key = control switch
        {
            PadControl.Up => GameKey.Up,
            PadControl.Down => GameKey.Down,
            PadControl.Left => GameKey.Left,
            PadControl.Right => GameKey.Right,
            _ => _bindings.TargetOf(control).Key,
        };
        if (repeat && key is not (GameKey.Up or GameKey.Down)) return;
        if (key is not { } k) return;

        if (_panel.IsOpen) { _panel.HandleKey(k); return; }
        switch (k)
        {
            case GameKey.Up: _selected = (_selected + Rows.Length - 1) % Rows.Length; Refresh(); ScrollToSelected(); break;
            case GameKey.Down: _selected = (_selected + 1) % Rows.Length; Refresh(); ScrollToSelected(); break;
            case GameKey.Enter or GameKey.Space: Pick(Rows[_selected]); break;
            case GameKey.Escape: Finish(); break;
        }
    }

    private void ScrollToSelected()
    {
        var row = _rows[_selected];
        _scroll.Post(() => _scroll.SmoothScrollTo(0, Math.Max(0, row.Top - _scroll.Height / 2)));
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e == null) return base.DispatchKeyEvent(e);
        if (e.KeyCode == Keycode.Back)
        {
            if (e.Action == KeyEventActions.Up) { if (_panel.IsOpen) _panel.Cancel(); else Finish(); }
            return true;
        }
        if (_input.Key(e)) return true;
        if (PadInput.KeyboardKey(e.KeyCode) is { } key)
        {
            if (e.Action == KeyEventActions.Down)
            {
                if (_panel.IsOpen) _panel.HandleKey(key);
                else if (key == GameKey.Up) OnPad(PadControl.Up, true, e.RepeatCount > 0);
                else if (key == GameKey.Down) OnPad(PadControl.Down, true, e.RepeatCount > 0);
                else if (key is GameKey.Enter or GameKey.Space) Pick(Rows[_selected]);
                else if (key == GameKey.Escape) Finish();
            }
            return true;
        }
        return base.DispatchKeyEvent(e);
    }

    public override bool OnGenericMotionEvent(MotionEvent? e) =>
        (e != null && _input.Motion(e)) || base.OnGenericMotionEvent(e);
}
