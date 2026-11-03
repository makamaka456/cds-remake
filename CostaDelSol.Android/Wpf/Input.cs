namespace System.Windows.Input
{
    public enum Key
    {
        None = 0, Cancel, Back, Tab, LineFeed, Clear, Return, Enter = 6, Pause, Capital, CapsLock = 8,
        Escape = 13, Space = 18, PageUp = 19, Prior = 19, PageDown = 20, Next = 20, End, Home, Left, Up, Right, Down,
        Select, Print, Execute, Snapshot, PrintScreen = 30, Insert, Delete, Help,
        D0 = 34, D1, D2, D3, D4, D5, D6, D7, D8, D9,
        A = 44, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        LWin, RWin, Apps, Sleep,
        NumPad0 = 74, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
        Multiply, Add, Separator, Subtract, Decimal, Divide,
        F1 = 90, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        NumLock = 114, Scroll, LeftShift, RightShift, LeftCtrl, RightCtrl, LeftAlt, RightAlt,
        OemSemicolon = 140, Oem1 = 140, OemPlus, OemComma, OemMinus, OemPeriod, OemQuestion, Oem2 = 145, OemTilde, Oem3 = 146,
        OemOpenBrackets = 149, Oem4 = 149, OemPipe, Oem5 = 150, OemCloseBrackets, Oem6 = 151, OemQuotes, Oem7 = 152,
        OemBackslash = 154, ImeProcessed = 155, System = 156,
    }

    [Flags] public enum ModifierKeys { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }
    public enum MouseButton { Left, Middle, Right, XButton1, XButton2 }
    public enum MouseButtonState { Released, Pressed }
    public enum KeyStates { None = 0, Down = 1, Toggled = 2 }

    public sealed class Cursor
    {
        internal Cursor() { }
    }

    public static class Cursors
    {
        public static readonly Cursor Arrow = new(), Hand = new(), None = new(), Wait = new(), IBeam = new(), Cross = new(),
                                      SizeAll = new(), No = new(), SizeWE = new(), SizeNS = new(), ScrollAll = new();
    }

    public interface ICommand
    {
        bool CanExecute(object? parameter);
        void Execute(object? parameter);
        event EventHandler? CanExecuteChanged;
    }

    public delegate void MouseButtonEventHandler(object sender, MouseButtonEventArgs e);
    public delegate void MouseEventHandler(object sender, MouseEventArgs e);
    public delegate void MouseWheelEventHandler(object sender, MouseWheelEventArgs e);
    public delegate void KeyEventHandler(object sender, KeyEventArgs e);
    public delegate void TextCompositionEventHandler(object sender, TextCompositionEventArgs e);
    public delegate void KeyboardFocusChangedEventHandler(object sender, KeyboardFocusChangedEventArgs e);

    public class InputEventArgs : RoutedEventArgs
    {
        public int Timestamp { get; init; } = Environment.TickCount;
    }

    public class MouseEventArgs : InputEventArgs
    {
        internal Point WindowPoint;
        internal Window? Window;
        public Point GetPosition(IInputElement? relativeTo) =>
            relativeTo is UIElement e ? e.FromWindow(WindowPoint, false) : WindowPoint;
        public MouseButtonState LeftButton => Mouse.LeftButton;
        public MouseButtonState RightButton => Mouse.RightButton;
        public MouseDevice MouseDevice => new();
    }

    public sealed class MouseDevice
    {
        public IInputElement? Captured => Mouse.Captured;
    }

    public sealed class MouseButtonEventArgs : MouseEventArgs
    {
        public MouseButton ChangedButton { get; init; }
        public MouseButtonState ButtonState { get; init; }
        public int ClickCount { get; init; } = 1;
    }

    public sealed class MouseWheelEventArgs : MouseEventArgs
    {
        public int Delta { get; init; }
    }

    public sealed class KeyEventArgs : InputEventArgs
    {
        public KeyEventArgs(Key key) { Key = key; }
        public Key Key { get; }
        public Key SystemKey => Key.None;
        public Key ImeProcessedKey => Key.None;
        public bool IsRepeat { get; init; }
        public bool IsDown { get; init; } = true;
        public bool IsUp => !IsDown;
        public KeyStates KeyStates => IsDown ? KeyStates.Down : KeyStates.None;
    }

    public sealed class TextCompositionEventArgs : InputEventArgs
    {
        public string Text { get; init; } = "";
    }

    public sealed class KeyboardFocusChangedEventArgs : InputEventArgs
    {
        public IInputElement? NewFocus { get; init; }
        public IInputElement? OldFocus { get; init; }
    }

    public static class Mouse
    {
        public static readonly RoutedEvent MouseDownEvent = new("MouseDown"), MouseUpEvent = new("MouseUp"),
                                           MouseMoveEvent = new("MouseMove"), PreviewMouseDownEvent = new("PreviewMouseDown");
        public static IInputElement? Captured { get; private set; }
        public static MouseButtonState LeftButton { get; internal set; }
        public static MouseButtonState RightButton { get; internal set; }
        public static IInputElement? DirectlyOver => Hovered.LastOrDefault();
        public static Cursor? OverrideCursor { get; set; }

        internal static readonly List<UIElement> Hovered = [];
        internal static bool Over(UIElement e) => Hovered.Contains(e);

        public static bool Capture(IInputElement? e)
        {
            var was = Captured as UIElement;
            Captured = e;
            if (was != null && !ReferenceEquals(was, e)) was.Raise("LostMouseCapture", new MouseEventArgs());
            return true;
        }

        public static Point GetPosition(IInputElement? relativeTo) =>
            relativeTo is UIElement e ? e.FromWindow(WpfHost.LastPoint, false) : WpfHost.LastPoint;

        public static void Synchronize() { }
    }

    public static class Keyboard
    {
        public static readonly RoutedEvent KeyDownEvent = new("KeyDown"), KeyUpEvent = new("KeyUp"), PreviewKeyDownEvent = new("PreviewKeyDown");
        public static ModifierKeys Modifiers => ModifierKeys.None;
        public static IInputElement? FocusedElement { get; private set; }
        public static bool IsKeyDown(Key key) => false;
        public static bool IsKeyUp(Key key) => true;
        public static KeyStates GetKeyStates(Key key) => KeyStates.None;

        public static IInputElement? Focus(IInputElement? e)
        {
            var was = FocusedElement;
            FocusedElement = e;
            if (!ReferenceEquals(was, e))
            {
                (was as UIElement)?.Raise("LostFocus", new RoutedEventArgs());
                (e as UIElement)?.Raise("GotFocus", new RoutedEventArgs());
            }
            return e;
        }

        public static void ClearFocus() => FocusedElement = null;
    }

    public static class FocusManager
    {
        public static void SetFocusedElement(DependencyObject scope, IInputElement? e) => Keyboard.Focus(e);
        public static IInputElement? GetFocusedElement(DependencyObject scope) => Keyboard.FocusedElement;
        public static void SetIsFocusScope(DependencyObject d, bool v) { }
    }

    public static class InputMethod
    {
        public static void SetIsInputMethodEnabled(DependencyObject d, bool v) { }
    }

    public sealed class RoutedCommand : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
