using Android.App;
using Android.Widget;
using Android.Views;

namespace CostaDelSol.Droid.Input;

/// <summary>
/// 게임패드의 버튼·스틱·트리거·HAT 를 표준 버튼(<see cref="PadControl"/>)의 누름/뗌과 스틱 값으로 바꾼다.
/// </summary>
/// <remarks>
/// Kairo 의 GamepadMapper 를 줄여 옮겼다.
/// <list type="bullet">
///   <item>십자키는 버튼(<c>KEYCODE_DPAD_*</c>)으로 오든 HAT 축으로 오든 같은 Up/Down/Left/Right 다.</item>
///   <item>트리거는 버튼으로 오든 축(<c>LTRIGGER/RTRIGGER</c>, <c>BRAKE/GAS</c>)으로 오든 L2/R2 다.</item>
///   <item>축 누름은 0.5 이상, 뗌은 0.25 이하 — 떨림에 누름/뗌이 깜빡이지 않게 사이를 둔다.</item>
///   <item>왼스틱 <c>AXIS_X/Y</c>, 오른스틱 <c>AXIS_Z/RZ</c>(없으면 <c>RX/RY</c>).</item>
/// </list>
/// </remarks>
public sealed class PadInput
{
    private const float PressAt = 0.5f, ReleaseAt = 0.25f;

    /// <summary>표준 버튼이 눌렸다(true)/떼졌다(false). 세 번째는 누르고 있어 되풀이된 것인지.</summary>
    public event Action<PadControl, bool, bool>? Button;

    public (float X, float Y) LeftStick { get; private set; }
    public (float X, float Y) RightStick { get; private set; }

    private readonly HashSet<PadControl> _axisHeld = [];

    /// <summary>패드 버튼이면 처리하고 true.</summary>
    public bool Key(KeyEvent e)
    {
        if (!PadBindings.FromPad(e) || PadBindings.ControlOf(e.KeyCode) is not { } control) return false;
        if (e.Action == KeyEventActions.Down) Button?.Invoke(control, true, e.RepeatCount > 0);
        else if (e.Action == KeyEventActions.Up) Button?.Invoke(control, false, false);
        return true;
    }

    /// <summary>패드 축이면 처리하고 true.</summary>
    public bool Motion(MotionEvent e)
    {
        if ((e.Source & InputSourceType.Joystick) != InputSourceType.Joystick || e.Action != MotionEventActions.Move)
            return false;

        LeftStick = (e.GetAxisValue(Axis.X), e.GetAxisValue(Axis.Y));
        float zx = e.GetAxisValue(Axis.Z), zy = e.GetAxisValue(Axis.Rz);
        float rx = e.GetAxisValue(Axis.Rx), ry = e.GetAxisValue(Axis.Ry);
        RightStick = Math.Abs(zx) + Math.Abs(zy) >= Math.Abs(rx) + Math.Abs(ry) ? (zx, zy) : (rx, ry);

        float hatX = e.GetAxisValue(Axis.HatX), hatY = e.GetAxisValue(Axis.HatY);
        Press(PadControl.Left, -hatX);
        Press(PadControl.Right, hatX);
        Press(PadControl.Up, -hatY);
        Press(PadControl.Down, hatY);
        Press(PadControl.L2, Math.Max(e.GetAxisValue(Axis.Ltrigger), e.GetAxisValue(Axis.Brake)));
        Press(PadControl.R2, Math.Max(e.GetAxisValue(Axis.Rtrigger), e.GetAxisValue(Axis.Gas)));
        return true;
    }

    private void Press(PadControl control, float value)
    {
        if (value >= PressAt && _axisHeld.Add(control)) Button?.Invoke(control, true, false);
        else if (value <= ReleaseAt && _axisHeld.Remove(control)) Button?.Invoke(control, false, false);
    }

    /// <summary>축으로 눌린 것 — 십자키 HAT 를 누르고 있으면 UI 가 되풀이를 낸다.</summary>
    public bool AxisHeld(PadControl control) => _axisHeld.Contains(control);

    public void Reset()
    {
        LeftStick = RightStick = (0, 0);
        foreach (var held in _axisHeld.ToList()) Button?.Invoke(held, false, false);
        _axisHeld.Clear();
    }

    /// <summary>블루투스·USB 키보드의 키 → 게임 키. 패드에서 온 것은 여기로 오지 않는다.</summary>
    public static GameKey? KeyboardKey(Keycode key) => key switch
    {
        Keycode.Escape => GameKey.Escape,
        Keycode.Enter or Keycode.NumpadEnter => GameKey.Enter,
        Keycode.Space => GameKey.Space,
        Keycode.DpadUp => GameKey.Up,
        Keycode.DpadDown => GameKey.Down,
        Keycode.DpadLeft => GameKey.Left,
        Keycode.DpadRight => GameKey.Right,
        >= Keycode.Num0 and <= Keycode.Num9 => GameKey.D0 + (key - Keycode.Num0),
        >= Keycode.Numpad0 and <= Keycode.Numpad9 => GameKey.D0 + (key - Keycode.Numpad0),
        Keycode.V => GameKey.V,
        Keycode.D => GameKey.D,
        Keycode.W => GameKey.W,
        Keycode.C => GameKey.C,
        Keycode.PageUp => GameKey.PageUp,
        Keycode.PageDown => GameKey.PageDown,
        Keycode.F5 => GameKey.F5,
        Keycode.Del => GameKey.Backspace,
        Keycode.Plus or Keycode.NumpadAdd or Keycode.Equals => GameKey.Plus,
        Keycode.Minus or Keycode.NumpadSubtract => GameKey.Minus,
        Keycode.A => GameKey.A, Keycode.B => GameKey.B, Keycode.E => GameKey.E, Keycode.F => GameKey.F,
        Keycode.G => GameKey.G, Keycode.H => GameKey.H, Keycode.I => GameKey.I, Keycode.J => GameKey.J,
        Keycode.K => GameKey.K, Keycode.L => GameKey.L, Keycode.M => GameKey.M, Keycode.N => GameKey.N,
        Keycode.O => GameKey.O, Keycode.P => GameKey.P, Keycode.Q => GameKey.Q, Keycode.R => GameKey.R,
        Keycode.S => GameKey.S, Keycode.T => GameKey.T, Keycode.U => GameKey.U, Keycode.X => GameKey.X,
        Keycode.Y => GameKey.Y, Keycode.Z => GameKey.Z,
        Keycode.F1 => GameKey.F1, Keycode.F2 => GameKey.F2, Keycode.F3 => GameKey.F3, Keycode.F4 => GameKey.F4,
        Keycode.F6 => GameKey.F6, Keycode.F7 => GameKey.F7, Keycode.F8 => GameKey.F8, Keycode.F9 => GameKey.F9,
        Keycode.F10 => GameKey.F10, Keycode.F11 => GameKey.F11, Keycode.F12 => GameKey.F12,
        Keycode.Tab => GameKey.Tab, Keycode.CapsLock => GameKey.CapsLock,
        Keycode.ShiftLeft or Keycode.ShiftRight => GameKey.Shift,
        Keycode.CtrlLeft or Keycode.CtrlRight => GameKey.Ctrl,
        Keycode.AltLeft or Keycode.AltRight => GameKey.Alt,
        Keycode.Insert => GameKey.Insert, Keycode.ForwardDel => GameKey.Delete,
        Keycode.MoveHome => GameKey.Home, Keycode.MoveEnd => GameKey.End,
        Keycode.Grave => GameKey.Grave, Keycode.LeftBracket => GameKey.BracketLeft,
        Keycode.RightBracket => GameKey.BracketRight, Keycode.Backslash => GameKey.Backslash,
        Keycode.Semicolon => GameKey.Semicolon, Keycode.Apostrophe => GameKey.Apostrophe,
        Keycode.Comma => GameKey.Comma, Keycode.Period => GameKey.Period, Keycode.Slash => GameKey.Slash,
        _ => null,
    };
}
