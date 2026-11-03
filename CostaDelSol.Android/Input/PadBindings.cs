using Android.App;
using Android.Widget;
using Android.Content;
using Android.Views;

namespace CostaDelSol.Droid.Input;

/// <summary>
/// 기기와 무관한 표준 패드 버튼. Kairo(Kairo98 의 공용 프런트엔드)의 표준 컨트롤 이름을 따랐다 —
/// 실제 버튼(키코드·축)은 먼저 이 이름으로 바뀌고, 이 이름에 할 일이 붙는다(두 단계 매핑).
/// 화면 단추도 같은 이름을 누르므로 패드와 터치가 한 매핑을 같이 쓴다.
/// </summary>
public enum PadControl
{
    Up, Down, Left, Right,
    A, B, X, Y,
    L1, R1, L2, R2,
    L3, R3,
    Start, Select, Menu,
    LeftStick, RightStick,
}

/// <summary>스틱이 할 수 있는 일 — 둘뿐이다.</summary>
public enum StickMode { Ship, Mouse }

/// <summary>앱 기능(게임 키가 아닌 것).</summary>
public enum AppAction { VirtualKeyboard, Menu }

/// <summary>
/// 버튼 하나에 붙는 할 일 — 게임 키 하나, 마우스 단추, 앱 기능 가운데 하나. 아무것도 없으면 <see cref="None"/>.
/// 저장은 글자로 한다: <c>key:Enter</c> · <c>mouse:left</c> · <c>app:Menu</c> · <c>none</c>.
/// </summary>
public readonly record struct PadTarget(GameKey? Key = null, bool? MouseLeft = null, AppAction? App = null)
{
    public static readonly PadTarget None = new();
    public static PadTarget Of(GameKey key) => new(Key: key);
    public static readonly PadTarget LeftClick = new(MouseLeft: true);
    public static readonly PadTarget RightClick = new(MouseLeft: false);
    public static PadTarget Of(AppAction app) => new(App: app);

    public bool IsNone => Key == null && MouseLeft == null && App == null;

    public string Id => Key is { } k ? "key:" + k
        : MouseLeft is { } left ? (left ? "mouse:left" : "mouse:right")
        : App is { } a ? "app:" + a
        : "none";

    public static PadTarget Parse(string? id)
    {
        if (id == null) return None;
        if (id.StartsWith("key:") && Enum.TryParse<GameKey>(id[4..], out var k)) return Of(k);
        if (id == "mouse:left") return LeftClick;
        if (id == "mouse:right") return RightClick;
        if (id.StartsWith("app:") && Enum.TryParse<AppAction>(id[4..], out var a)) return Of(a);
        return None;
    }

    public string Label => Key is { } k ? $"키보드 {GameKeys.Name(k)}"
        : MouseLeft is { } left ? (left ? "마우스 왼쪽 클릭" : "마우스 오른쪽 클릭")
        : App switch
        {
            AppAction.VirtualKeyboard => "가상 키보드 열기/닫기",
            AppAction.Menu => "메뉴",
            _ => "없음",
        };
}

/// <summary>
/// 패드 매핑 — 버튼마다 할 일, 스틱마다 쓰임. 기기에 남는다(SharedPreferences).
/// </summary>
/// <remarks>
/// 기본값은 사용자가 정한 배치다(2026-10-31):
/// <code>
///   L스틱 배 이동(8방향 = 숫자판 1~9)   R3 숫자 5(닻)        R스틱 마우스 이동   L3 가상 키보드
///   L1 마우스 오른쪽 클릭               R1 마우스 왼쪽 클릭  L2 PgUp            R2 PgDn
///   Select D(발견물 지도)               Start 숫자 0(커맨드)  Menu 메뉴
///   A ESC   B 엔터   X 스페이스   Y V(저장)   십자키 방향키
/// </code>
/// 메뉴(앱 설정)는 안드로이드 뒤로 버튼과 패드의 메뉴(Mode) 버튼이 연다 — 둘 다 바꿀 수 없다.
/// </remarks>
public sealed class PadBindings
{
    private const string PrefsName = "pad-bindings-v2";
    public const float DefaultDeadZone = 0.25f;

    public static readonly PadControl[] Buttons =
        Enum.GetValues<PadControl>().Where(c => c is not (PadControl.LeftStick or PadControl.RightStick)).ToArray();

    private readonly Dictionary<PadControl, PadTarget> _targets = [];
    public StickMode LeftStick { get; set; } = StickMode.Ship;
    public StickMode RightStick { get; set; } = StickMode.Mouse;
    public float DeadZone { get; set; } = DefaultDeadZone;

    public static PadTarget Default(PadControl control) => control switch
    {
        PadControl.Up => PadTarget.Of(GameKey.Up),
        PadControl.Down => PadTarget.Of(GameKey.Down),
        PadControl.Left => PadTarget.Of(GameKey.Left),
        PadControl.Right => PadTarget.Of(GameKey.Right),
        PadControl.A => PadTarget.Of(GameKey.Escape),
        PadControl.B => PadTarget.Of(GameKey.Enter),
        PadControl.X => PadTarget.Of(GameKey.Space),
        PadControl.Y => PadTarget.Of(GameKey.V),
        PadControl.L1 => PadTarget.RightClick,
        PadControl.R1 => PadTarget.LeftClick,
        PadControl.L2 => PadTarget.Of(GameKey.PageUp),
        PadControl.R2 => PadTarget.Of(GameKey.PageDown),
        PadControl.L3 => PadTarget.Of(AppAction.VirtualKeyboard),
        PadControl.R3 => PadTarget.Of(GameKey.D5),
        PadControl.Start => PadTarget.Of(GameKey.D0),
        PadControl.Select => PadTarget.Of(GameKey.D),
        PadControl.Menu => PadTarget.Of(AppAction.Menu),
        _ => PadTarget.None,
    };

    public static bool Locked(PadControl control) => control == PadControl.Menu;

    public PadTarget TargetOf(PadControl control) =>
        Locked(control) ? Default(control) : _targets.TryGetValue(control, out var t) ? t : Default(control);

    public void Set(PadControl control, PadTarget target)
    {
        if (!Locked(control)) _targets[control] = target;
    }

    public void ResetDefaults()
    {
        _targets.Clear();
        LeftStick = StickMode.Ship;
        RightStick = StickMode.Mouse;
        DeadZone = DefaultDeadZone;
    }

    public static string ControlName(PadControl control) => control switch
    {
        PadControl.Up => "십자키 위",
        PadControl.Down => "십자키 아래",
        PadControl.Left => "십자키 왼쪽",
        PadControl.Right => "십자키 오른쪽",
        PadControl.L3 => "L3 (왼스틱 누르기)",
        PadControl.R3 => "R3 (오른스틱 누르기)",
        PadControl.Select => "Select (−)",
        PadControl.Start => "Start (+)",
        PadControl.Menu => "메뉴",
        PadControl.LeftStick => "L스틱",
        PadControl.RightStick => "R스틱",
        PadControl.L2 => "L2 (왼쪽 트리거)",
        PadControl.R2 => "R2 (오른쪽 트리거)",
        _ => control.ToString(),
    };

    public static string StickLabel(StickMode mode) => mode == StickMode.Ship ? "배 이동 (8방향)" : "마우스 이동";

    public static PadBindings Load(Context context)
    {
        var map = new PadBindings();
        var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        foreach (var control in Buttons)
            if (prefs.GetString(control.ToString(), null) is { } id) map._targets[control] = PadTarget.Parse(id);
        // 스틱 칸 이름에 2 를 붙였다 — 기본이 바뀌었을 때(L 배 · R 마우스) 옛 값이 그대로 남지 않게.
        if (Enum.TryParse<StickMode>(prefs.GetString("LeftStick2", null), out var l)) map.LeftStick = l;
        if (Enum.TryParse<StickMode>(prefs.GetString("RightStick2", null), out var r)) map.RightStick = r;
        map.DeadZone = Math.Clamp(prefs.GetFloat("DeadZone", DefaultDeadZone), 0.1f, 0.9f);
        return map;
    }

    public void Save(Context context)
    {
        var edit = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.Edit()!;
        edit.Clear();
        foreach (var (control, target) in _targets) edit.PutString(control.ToString(), target.Id);
        edit.PutString("LeftStick2", LeftStick.ToString());
        edit.PutString("RightStick2", RightStick.ToString());
        edit.PutFloat("DeadZone", DeadZone);
        edit.Apply();
    }

    // ── 실제 버튼 → 표준 버튼 (Kairo PhysicalControllerBindings 의 기본표) ──

    public static PadControl? ControlOf(Keycode key) => key switch
    {
        Keycode.DpadUp => PadControl.Up,
        Keycode.DpadDown => PadControl.Down,
        Keycode.DpadLeft => PadControl.Left,
        Keycode.DpadRight => PadControl.Right,
        Keycode.ButtonA => PadControl.A,
        Keycode.ButtonB => PadControl.B,
        Keycode.ButtonX => PadControl.X,
        Keycode.ButtonY => PadControl.Y,
        Keycode.ButtonL1 => PadControl.L1,
        Keycode.ButtonR1 => PadControl.R1,
        Keycode.ButtonL2 => PadControl.L2,
        Keycode.ButtonR2 => PadControl.R2,
        Keycode.ButtonThumbl => PadControl.L3,
        Keycode.ButtonThumbr => PadControl.R3,
        Keycode.ButtonStart => PadControl.Start,
        Keycode.ButtonSelect => PadControl.Select,
        Keycode.ButtonMode => PadControl.Menu,
        _ => null,
    };

    /// <summary>패드에서 온 입력인지 — 키보드에서 온 방향키·엔터는 패드가 아니라 키보드로 받는다.</summary>
    public static bool FromPad(KeyEvent e) =>
        KeyEvent.IsGamepadButton(e.KeyCode) ||
        (e.Source & InputSourceType.Gamepad) == InputSourceType.Gamepad ||
        (e.Source & InputSourceType.Joystick) == InputSourceType.Joystick;
}
