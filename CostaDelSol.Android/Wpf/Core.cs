using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AMatrix = Android.Graphics.Matrix;
using ACanvas = Android.Graphics.Canvas;

// WpfLite — PC 판 화면 코드(WPF 로 손수 지은 창들)를 고치지 않고 돌리려고 지은 작은 WPF.
// 요소 나무 · 자리 잡기(Measure/Arrange) · 그리기 · 손(마우스 · 글쇠) 갈래를 WPF 와 같은 이름 · 같은 뜻으로 둔다.
// 모든 요소는 게임 줄(GameThread)에서만 만지고, 그린 그림은 화면 줄의 창 판(ChoicePanel)에 얹힌다(WpfHost).

namespace System.Windows
{
    public readonly record struct Size(double Width, double Height)
    {
        public static Size Empty => new(0, 0);
        public bool IsEmpty => Width <= 0 && Height <= 0;
    }

    /// <summary>WPF 의 네모.</summary>
    public struct Rect
    {
        public double X, Y, Width, Height;
        public Rect(double x, double y, double width, double height) { X = x; Y = y; Width = Math.Max(0, width); Height = Math.Max(0, height); }
        public Rect(Point at, Size size) : this(at.X, at.Y, size.Width, size.Height) { }
        public Rect(Size size) : this(0, 0, size.Width, size.Height) { }
        public Rect(Point a, Point b) : this(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) { }
        public double Left => X;
        public double Top => Y;
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public Point Location => new(X, Y);
        public Size Size => new(Width, Height);
        public Point TopLeft => new(X, Y);
        public Point BottomRight => new(Right, Bottom);
        public bool IsEmpty => Width <= 0 && Height <= 0;
        public static Rect Empty => default;
        public bool Contains(Point p) => p.X >= X && p.Y >= Y && p.X < Right && p.Y < Bottom;
        public bool Contains(double x, double y) => Contains(new Point(x, y));
        public bool IntersectsWith(Rect r) => r.X < Right && r.Right > X && r.Y < Bottom && r.Bottom > Y;
        public void Offset(double dx, double dy) { X += dx; Y += dy; }
        public void Inflate(double dx, double dy) { X -= dx; Y -= dy; Width = Math.Max(0, Width + 2 * dx); Height = Math.Max(0, Height + 2 * dy); }
        public override string ToString() => $"{X},{Y},{Width},{Height}";
    }

    /// <summary>WPF 의 점.</summary>
    public struct Point : IEquatable<Point>
    {
        public double X, Y;
        public Point(double x, double y) { X = x; Y = y; }
        public void Deconstruct(out double x, out double y) { x = X; y = Y; }
        public static Point operator +(Point p, Vector v) => new(p.X + v.X, p.Y + v.Y);
        public static Point operator -(Point p, Vector v) => new(p.X - v.X, p.Y - v.Y);
        public static Vector operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
        public static bool operator ==(Point a, Point b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Point a, Point b) => !(a == b);
        public bool Equals(Point o) => this == o;
        public override bool Equals(object? o) => o is Point p && this == p;
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"{X},{Y}";
    }

    public struct Vector
    {
        public double X, Y;
        public Vector(double x, double y) { X = x; Y = y; }
        public double Length => Math.Sqrt(X * X + Y * Y);
        public static Vector operator *(Vector v, double k) => new(v.X * k, v.Y * k);
        public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y);
    }

    public struct Int32Rect
    {
        public int X, Y, Width, Height;
        public Int32Rect(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
        public static Int32Rect Empty => default;
    }

    public struct Thickness : IEquatable<Thickness>
    {
        public double Left, Top, Right, Bottom;
        public Thickness(double all) { Left = Top = Right = Bottom = all; }
        public Thickness(double left, double top, double right, double bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
        public bool Equals(Thickness o) => Left == o.Left && Top == o.Top && Right == o.Right && Bottom == o.Bottom;
        public override bool Equals(object? o) => o is Thickness t && Equals(t);
        public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
        public static bool operator ==(Thickness a, Thickness b) => a.Equals(b);
        public static bool operator !=(Thickness a, Thickness b) => !a.Equals(b);
        internal double W => Left + Right;
        internal double H => Top + Bottom;
    }

    public struct CornerRadius
    {
        public double TopLeft, TopRight, BottomRight, BottomLeft;
        public CornerRadius(double r) { TopLeft = TopRight = BottomRight = BottomLeft = r; }
        public CornerRadius(double a, double b, double c, double d) { TopLeft = a; TopRight = b; BottomRight = c; BottomLeft = d; }
    }

    public struct Duration
    {
        public TimeSpan TimeSpan;
        public Duration(TimeSpan span) { TimeSpan = span; }
        public bool HasTimeSpan => true;
        public static implicit operator Duration(TimeSpan span) => new(span);
    }

    public enum Visibility { Visible, Hidden, Collapsed }
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
    public enum TextAlignment { Left, Right, Center, Justify }
    public enum TextWrapping { WrapWithOverflow, NoWrap, Wrap }
    public enum TextTrimming { None, CharacterEllipsis, WordEllipsis }
    public enum FlowDirection { LeftToRight, RightToLeft }
    public enum WindowStyle { None, SingleBorderWindow, ThreeDBorderWindow, ToolWindow }
    public enum ResizeMode { NoResize, CanMinimize, CanResize, CanResizeWithGrip }
    public enum WindowStartupLocation { Manual, CenterScreen, CenterOwner }
    public enum SizeToContent { Manual, Width, Height, WidthAndHeight }
    public enum WindowState { Normal, Minimized, Maximized }
    public enum MessageBoxResult { None, OK, Cancel, Yes = 6, No = 7 }

    public readonly struct FontWeight
    {
        internal readonly int Weight;
        internal FontWeight(int w) { Weight = w; }
        public static bool operator ==(FontWeight a, FontWeight b) => a.Weight == b.Weight;
        public static bool operator !=(FontWeight a, FontWeight b) => a.Weight != b.Weight;
        public override bool Equals(object? o) => o is FontWeight f && f.Weight == Weight;
        public override int GetHashCode() => Weight;
    }

    public static class FontWeights
    {
        public static FontWeight Normal => new(400);
        public static FontWeight Regular => new(400);
        public static FontWeight SemiBold => new(600);
        public static FontWeight Bold => new(700);
        public static FontWeight Light => new(300);
        public static FontWeight Black => new(900);
        public static FontWeight Medium => new(500);
    }

    public readonly struct FontStyle
    {
        internal readonly int Kind;
        internal FontStyle(int k) { Kind = k; }
    }

    public static class FontStyles
    {
        public static FontStyle Normal => new(0);
        public static FontStyle Italic => new(1);
    }

    public static class SystemParameters
    {
        public static Rect WorkArea => new(0, 0, 640, 480);
        public static double PrimaryScreenWidth => 640;
        public static double PrimaryScreenHeight => 480;
        public static double VirtualScreenWidth => 640 + 2 * WpfHost.SideRoom;
        public static double VirtualScreenHeight => 480;
        public static double VirtualScreenLeft => -WpfHost.SideRoom;
        public static double VirtualScreenTop => 0;
        public static double FullPrimaryScreenWidth => 640;
        public static double FullPrimaryScreenHeight => 480;
        public static double MinimumHorizontalDragDistance => 4;
        public static double MinimumVerticalDragDistance => 4;
    }

    public enum GridUnitType { Auto, Pixel, Star }

    public struct GridLength
    {
        public double Value;
        public GridUnitType GridUnitType;
        public GridLength(double pixels) { Value = pixels; GridUnitType = GridUnitType.Pixel; }
        public GridLength(double value, GridUnitType type) { Value = value; GridUnitType = type; }
        public static GridLength Auto => new(1, GridUnitType.Auto);
        public bool IsAuto => GridUnitType == GridUnitType.Auto;
        public bool IsStar => GridUnitType == GridUnitType.Star;
        public bool IsAbsolute => GridUnitType == GridUnitType.Pixel;
        public static implicit operator GridLength(double pixels) => new(pixels);
    }

    // ── 의존 속성 흉내 ──────────────────────────────────────────────────────

    public sealed class DependencyProperty
    {
        public string Name { get; }
        /// <summary>붙임 속성(Grid.Column 따위)인지 — 아니면 같은 이름의 CLR 속성으로 값을 읽고 쓴다.</summary>
        internal bool Attached { get; init; }
        internal DependencyProperty(string name) { Name = name; }
        public static DependencyProperty Register(string name, Type t, Type owner, PropertyMetadata? meta = null) => new(name);
        public static DependencyProperty RegisterAttached(string name, Type t, Type owner, PropertyMetadata? meta = null) => new(name) { Attached = true };
        public static readonly object UnsetValue = new();
    }

    public class PropertyMetadata
    {
        public PropertyMetadata() { }
        public PropertyMetadata(object? value) { }
    }

    public class DependencyObject
    {
        private Dictionary<object, object?>? _values;
        public Dispatcher Dispatcher => Dispatcher.CurrentDispatcher;
        public bool CheckAccess() => true;
        public void VerifyAccess() { }

        internal T Get<T>(object key, T fallback) =>
            _values != null && _values.TryGetValue(key, out var v) && v is T t ? t : fallback;

        internal bool Has(object key) => _values != null && _values.ContainsKey(key);

        internal void Put(object key, object? value)
        {
            (_values ??= [])[key] = value;
            WpfHost.Touch();
        }

        public virtual object? GetValue(DependencyProperty p)
        {
            if (Clr(p) is { CanRead: true } prop) return prop.GetValue(this);
            return _values != null && _values.TryGetValue(p, out var v) ? v : null;
        }

        public virtual void SetValue(DependencyProperty p, object? value)
        {
            if (Clr(p) is { CanWrite: true } prop && Fits(prop.PropertyType, value)) { prop.SetValue(this, value); (_values ??= [])[p] = value; return; }
            Put(p, value);
        }

        public void ClearValue(DependencyProperty p) { _values?.Remove(p); WpfHost.Touch(); }

        /// <summary>그 값을 이 요소에 따로 적어 둔 적이 있으면 그 값, 없으면 <see cref="DependencyProperty.UnsetValue"/>.</summary>
        public object? ReadLocalValue(DependencyProperty p) =>
            _values != null && _values.TryGetValue(p, out var v) ? v : DependencyProperty.UnsetValue;

        /// <summary>
        /// 붙임 속성이 아니면 같은 이름의 CLR 속성(WpfLite 것만 — PC 판의 제 속성은 그쪽이 SetValue 를 부를 수 있다).
        /// 그래서 틀(FrameworkElementFactory) · 애니메이션이 Width · Margin · Background 같은 값을 그대로 쓴다.
        /// </summary>
        private System.Reflection.PropertyInfo? Clr(DependencyProperty p)
        {
            if (p.Attached) return null;
            var prop = GetType().GetProperty(p.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return prop != null && prop.GetIndexParameters().Length == 0
                   && prop.DeclaringType?.Namespace?.StartsWith("System.Windows", StringComparison.Ordinal) == true ? prop : null;
        }

        private static bool Fits(Type type, object? value) =>
            value == null ? !type.IsValueType || Nullable.GetUnderlyingType(type) != null : type.IsInstanceOfType(value);
    }

    public abstract class Freezable : DependencyObject
    {
        public bool IsFrozen { get; private set; }
        public void Freeze() => IsFrozen = true;
        public void BeginAnimation(DependencyProperty p, Media.Animation.AnimationTimeline? a) => Media.Animation.Animator.Begin(this, p, a);
        public bool CanFreeze => true;
    }

    // ── 손 사건 ────────────────────────────────────────────────────────────

    public sealed class RoutedEvent
    {
        public string Name { get; }
        internal RoutedEvent(string name) { Name = name; }
    }

    public class RoutedEventArgs : EventArgs
    {
        public RoutedEventArgs() { }
        public RoutedEventArgs(RoutedEvent e) { RoutedEvent = e; }
        public RoutedEventArgs(RoutedEvent e, object source) { RoutedEvent = e; Source = source; OriginalSource = source; }
        public bool Handled { get; set; }
        public object? Source { get; set; }
        public object? OriginalSource { get; set; }
        public RoutedEvent? RoutedEvent { get; set; }
    }

    public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);
    public delegate void RoutedPropertyChangedEventHandler<T>(object sender, RoutedPropertyChangedEventArgs<T> e);

    public class RoutedPropertyChangedEventArgs<T>(T oldValue, T newValue) : RoutedEventArgs
    {
        public T OldValue => oldValue;
        public T NewValue => newValue;
    }
    public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs e);

    public sealed class SizeChangedEventArgs : RoutedEventArgs
    {
        public Size NewSize { get; init; }
        public Size PreviousSize { get; init; }
        public bool WidthChanged => NewSize.Width != PreviousSize.Width;
        public bool HeightChanged => NewSize.Height != PreviousSize.Height;
    }

    public static class EventManager
    {
        internal static readonly List<(Type Type, RoutedEvent Event, Delegate Handler)> ClassHandlers = [];

        public static void RegisterClassHandler(Type type, RoutedEvent e, Delegate handler) =>
            ClassHandlers.Add((type, e, handler));

        public static RoutedEvent RegisterRoutedEvent(string name, RoutingStrategy strategy, Type handler, Type owner) => new(name);
    }

    public enum RoutingStrategy { Tunnel, Bubble, Direct }

    // ── 요소 ───────────────────────────────────────────────────────────────

    public class UIElement : Visual, IInputElement
    {
        internal UIElement? VisualParent;
        internal Size Desired;
        internal Size Rendered;
        internal Point Offset;           // 어버이 안에서의 자리(여백 · 맞춤까지 셈)
        internal AMatrix? ToWindowMatrix; // 창 왼쪽 위에서 이 요소 왼쪽 위로(그릴 때 셈)
        private Visibility _visibility = Visibility.Visible;
        private double _opacity = 1;
        private bool _hit = true, _enabled = true;
        private Transform? _renderTransform;
        private Point _origin;
        private bool _clip;

        public Size DesiredSize => Desired;
        public Size RenderSize { get => Rendered; set => Rendered = value; }

        public Visibility Visibility
        {
            get => _visibility;
            set { if (_visibility == value) return; _visibility = value; WpfHost.Touch(); OnVisibilityChanged(value); IsVisibleChanged?.Invoke(this, new DependencyPropertyChangedEventArgs()); }
        }

        public bool IsVisible
        {
            get
            {
                for (UIElement? e = this; e != null; e = e.VisualParent)
                {
                    if (e._visibility != Visibility.Visible) return false;
                    if (e is Window w) return w.IsShown;
                }
                return false;
            }
        }

        protected virtual void OnVisibilityChanged(Visibility value) { }

        public static readonly DependencyProperty OpacityProperty = DependencyProperty.Register("Opacity", typeof(double), typeof(UIElement));
        public static readonly DependencyProperty VisibilityProperty = DependencyProperty.Register("Visibility", typeof(Visibility), typeof(UIElement));

        public override void SetValue(DependencyProperty p, object? value)
        {
            if (ReferenceEquals(p, OpacityProperty) && value is double o) Opacity = o;
            else if (ReferenceEquals(p, VisibilityProperty) && value is Visibility v) Visibility = v;
            else base.SetValue(p, value);
        }

        public override object? GetValue(DependencyProperty p) =>
            ReferenceEquals(p, OpacityProperty) ? Opacity : ReferenceEquals(p, VisibilityProperty) ? Visibility : base.GetValue(p);

        public double Opacity { get => _opacity; set { _opacity = value; WpfHost.Touch(); } }
        public Brush? OpacityMask { get; set; }
        public bool IsHitTestVisible { get => _hit; set => _hit = value; }
        public bool IsEnabled { get => _enabled; set { _enabled = value; WpfHost.Touch(); IsEnabledChanged?.Invoke(this, new DependencyPropertyChangedEventArgs()); } }
        public Transform? RenderTransform { get => _renderTransform; set { _renderTransform = value; WpfHost.Touch(); } }
        public Point RenderTransformOrigin { get => _origin; set { _origin = value; WpfHost.Touch(); } }
        public bool ClipToBounds { get => _clip; set { _clip = value; WpfHost.Touch(); } }
        public Geometry? Clip { get; set; }
        public CacheMode? CacheMode { get; set; }
        public Media.Effects.Effect? Effect { get; set; }
        public bool Focusable { get; set; }
        public bool SnapsToDevicePixels { get; set; }
        public bool IsMouseOver => Input.Mouse.Over(this);
        public bool IsMouseCaptured => ReferenceEquals(Input.Mouse.Captured, this);
        public bool IsKeyboardFocused => ReferenceEquals(Input.Keyboard.FocusedElement, this);
        public bool IsKeyboardFocusWithin
        {
            get
            {
                for (var e = Input.Keyboard.FocusedElement as UIElement; e != null; e = e.VisualParent)
                    if (ReferenceEquals(e, this)) return true;
                return false;
            }
        }
        public bool IsFocused => IsKeyboardFocused;

        public event DependencyPropertyChangedEventHandler? IsVisibleChanged;
        public event DependencyPropertyChangedEventHandler? IsEnabledChanged;

        public event MouseButtonEventHandler? MouseLeftButtonDown, MouseLeftButtonUp, MouseRightButtonDown, MouseRightButtonUp,
                                              MouseDown, MouseUp, PreviewMouseLeftButtonDown, PreviewMouseLeftButtonUp,
                                              PreviewMouseRightButtonDown, PreviewMouseRightButtonUp, PreviewMouseDown, PreviewMouseUp;
        public event MouseEventHandler? MouseMove, MouseEnter, MouseLeave, PreviewMouseMove;
        public event MouseWheelEventHandler? MouseWheel, PreviewMouseWheel;
        public event KeyEventHandler? KeyDown, KeyUp, PreviewKeyDown, PreviewKeyUp;
        public event TextCompositionEventHandler? TextInput, PreviewTextInput;
        public event KeyboardFocusChangedEventHandler? GotKeyboardFocus, LostKeyboardFocus;
        public event RoutedEventHandler? GotFocus, LostFocus;
        public event MouseEventHandler? LostMouseCapture;
        public bool AllowDrop { get; set; }
        public event DragEventHandler? Drop, DragEnter, DragOver, DragLeave, PreviewDragOver, PreviewDrop;
        internal void FireDrop(DragEventArgs e) { Drop?.Invoke(this, e); DragEnter?.Invoke(this, e); DragOver?.Invoke(this, e); DragLeave?.Invoke(this, e); PreviewDragOver?.Invoke(this, e); PreviewDrop?.Invoke(this, e); }

        internal void Raise(string name, RoutedEventArgs e)
        {
            switch (name)
            {
                case nameof(MouseLeftButtonDown): MouseLeftButtonDown?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(MouseLeftButtonUp): MouseLeftButtonUp?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(MouseRightButtonDown): MouseRightButtonDown?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(MouseRightButtonUp): MouseRightButtonUp?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(MouseDown): MouseDown?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(MouseUp): MouseUp?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(PreviewMouseLeftButtonDown): PreviewMouseLeftButtonDown?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(PreviewMouseLeftButtonUp): PreviewMouseLeftButtonUp?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(PreviewMouseRightButtonDown): PreviewMouseRightButtonDown?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(PreviewMouseRightButtonUp): PreviewMouseRightButtonUp?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(PreviewMouseDown): PreviewMouseDown?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(PreviewMouseUp): PreviewMouseUp?.Invoke(this, (MouseButtonEventArgs)e); break;
                case nameof(MouseMove): MouseMove?.Invoke(this, (MouseEventArgs)e); break;
                case nameof(PreviewMouseMove): PreviewMouseMove?.Invoke(this, (MouseEventArgs)e); break;
                case nameof(MouseEnter): MouseEnter?.Invoke(this, (MouseEventArgs)e); break;
                case nameof(MouseLeave): MouseLeave?.Invoke(this, (MouseEventArgs)e); break;
                case nameof(MouseWheel): MouseWheel?.Invoke(this, (MouseWheelEventArgs)e); break;
                case nameof(PreviewMouseWheel): PreviewMouseWheel?.Invoke(this, (MouseWheelEventArgs)e); break;
                case nameof(KeyDown): KeyDown?.Invoke(this, (KeyEventArgs)e); break;
                case nameof(KeyUp): KeyUp?.Invoke(this, (KeyEventArgs)e); break;
                case nameof(PreviewKeyDown): PreviewKeyDown?.Invoke(this, (KeyEventArgs)e); break;
                case nameof(PreviewKeyUp): PreviewKeyUp?.Invoke(this, (KeyEventArgs)e); break;
                case nameof(TextInput): TextInput?.Invoke(this, (TextCompositionEventArgs)e); break;
                case nameof(PreviewTextInput): PreviewTextInput?.Invoke(this, (TextCompositionEventArgs)e); break;
                case nameof(LostMouseCapture): LostMouseCapture?.Invoke(this, (MouseEventArgs)e); break;
                case nameof(GotFocus): GotFocus?.Invoke(this, e); break;
                case nameof(LostFocus): LostFocus?.Invoke(this, e); break;
            }
            OnRaised(name, e);
        }

        /// <summary>제 갈래 처리(단추의 Click 따위).</summary>
        internal virtual void OnRaised(string name, RoutedEventArgs e) { }

        public void RaiseEvent(RoutedEventArgs e)
        {
            if (e.RoutedEvent?.Name is { } name)
                for (UIElement? at = this; at != null && !e.Handled; at = at.VisualParent) at.Raise(name, e);
        }

        public void AddHandler(RoutedEvent e, Delegate handler, bool handledEventsToo = false)
        {
            switch (e.Name)
            {
                case "MouseLeftButtonDown": MouseLeftButtonDown += (MouseButtonEventHandler)handler; break;
                case "MouseLeftButtonUp": MouseLeftButtonUp += (MouseButtonEventHandler)handler; break;
                case "MouseRightButtonUp": MouseRightButtonUp += (MouseButtonEventHandler)handler; break;
                case "MouseDown": MouseDown += (MouseButtonEventHandler)handler; break;
                case "MouseUp": MouseUp += (MouseButtonEventHandler)handler; break;
                case "KeyDown": KeyDown += (KeyEventHandler)handler; break;
                case "PreviewKeyDown": PreviewKeyDown += (KeyEventHandler)handler; break;
                case "MouseMove": MouseMove += (MouseEventHandler)handler; break;
                case "Click" when this is Controls.Primitives.ButtonBase b: b.Click += (RoutedEventHandler)handler; break;
            }
        }

        public bool Focus()
        {
            Input.Keyboard.Focus(this);
            return true;
        }

        public bool CaptureMouse() { Input.Mouse.Capture(this); return true; }
        public void ReleaseMouseCapture() { if (IsMouseCaptured) Input.Mouse.Capture(null); }

        public void InvalidateVisual() => WpfHost.Touch();
        public void InvalidateMeasure() => WpfHost.Touch();
        public void InvalidateArrange() => WpfHost.Touch();
        public void UpdateLayout() => WpfHost.LayoutNow();
        public void BeginAnimation(DependencyProperty p, Media.Animation.AnimationTimeline? a) => Media.Animation.Animator.Begin(this, p, a);

        public void Measure(Size available) => Desired = MeasureCore(available);
        public void Arrange(Rect final) => ArrangeCore(final);

        protected virtual Size MeasureCore(Size available) => new(0, 0);
        protected virtual void ArrangeCore(Rect final)
        {
            Offset = final.Location;
            Rendered = final.Size;
        }

        /// <summary>이 요소의 자식(그리기 · 손 찾기 차례).</summary>
        internal virtual IEnumerable<UIElement> VisualChildren => [];

        /// <summary>제 몫을 그린다(바탕 · 그림 · 글). 자식은 따로 그린다.</summary>
        internal virtual void Paint(ACanvas canvas, float k) { }

        /// <summary>손에 걸리는지 — 무엇이라도 칠해진 데만 걸린다(WPF 와 같다).</summary>
        internal virtual bool HitSelf(Point local) => false;

        internal bool ShowsUp => _visibility == Visibility.Visible;

        // ── 자리 셈 ──
        /// <summary>이 요소 안의 점을 딴 요소 안의 점으로(창 좌표를 거쳐).</summary>
        public Point TranslatePoint(Point p, UIElement relativeTo) => relativeTo.FromWindow(ToWindow(p, false), false);

        internal Point ToWindow(Point p, bool toScreen)
        {
            var m = ToWindowMatrix ?? new AMatrix();
            var pts = new float[] { (float)p.X, (float)p.Y };
            m.MapPoints(pts);
            var r = new Point(pts[0], pts[1]);
            if (toScreen && Root() is Window w) { r.X += w.ScreenLeft; r.Y += w.ScreenTop; }
            return r;
        }

        internal Point FromWindow(Point p, bool fromScreen)
        {
            if (fromScreen && Root() is Window w) { p.X -= w.ScreenLeft; p.Y -= w.ScreenTop; }
            var m = ToWindowMatrix ?? new AMatrix();
            var inv = new AMatrix();
            if (!m.Invert(inv)) return p;
            var pts = new float[] { (float)p.X, (float)p.Y };
            inv.MapPoints(pts);
            return new Point(pts[0], pts[1]);
        }

        internal Vector OffsetTo(UIElement ancestor)
        {
            var a = ToWindow(new Point(0, 0), false);
            var b = ancestor.ToWindow(new Point(0, 0), false);
            return new Vector(a.X - b.X, a.Y - b.Y);
        }

        internal UIElement Root()
        {
            var e = this;
            while (e.VisualParent != null) e = e.VisualParent;
            return e;
        }

        protected internal virtual void OnVisualParentChanged(DependencyObject? oldParent) { }

        internal void Adopt(UIElement child)
        {
            if (child.VisualParent != null && !ReferenceEquals(child.VisualParent, this))
                child.VisualParent.Orphan(child);
            child.VisualParent = this;
            WpfHost.Touch();
        }

        internal virtual void Orphan(UIElement child)
        {
            if (ReferenceEquals(child.VisualParent, this)) child.VisualParent = null;
        }
    }

    public delegate void DependencyPropertyChangedEventHandler(object sender, DependencyPropertyChangedEventArgs e);

    public struct DependencyPropertyChangedEventArgs
    {
        public object? NewValue { get; init; }
        public object? OldValue { get; init; }
    }

    public interface IInputElement { }

    public class FrameworkElement : UIElement
    {
        private double _w = double.NaN, _h = double.NaN, _minW, _minH, _maxW = double.PositiveInfinity, _maxH = double.PositiveInfinity;
        private Thickness _margin;
        private HorizontalAlignment _ha = HorizontalAlignment.Stretch;
        private VerticalAlignment _va = VerticalAlignment.Stretch;
        private bool _loaded;
        private Size _lastSize;

        public static readonly DependencyProperty WidthProperty = DependencyProperty.Register("Width", typeof(double), typeof(FrameworkElement));
        public static readonly DependencyProperty MinWidthProperty = DependencyProperty.Register("MinWidth", typeof(double), typeof(FrameworkElement));
        public static readonly DependencyProperty MinHeightProperty = DependencyProperty.Register("MinHeight", typeof(double), typeof(FrameworkElement));
        public static readonly DependencyProperty MarginProperty = DependencyProperty.Register("Margin", typeof(Thickness), typeof(FrameworkElement));
        public static readonly DependencyProperty HorizontalAlignmentProperty = DependencyProperty.Register("HorizontalAlignment", typeof(HorizontalAlignment), typeof(FrameworkElement));
        public static readonly DependencyProperty VerticalAlignmentProperty = DependencyProperty.Register("VerticalAlignment", typeof(VerticalAlignment), typeof(FrameworkElement));
        public static readonly RoutedEvent LoadedEvent = new("Loaded");
        public Style? FocusVisualStyle { get; set; }
        public static readonly DependencyProperty HeightProperty = DependencyProperty.Register("Height", typeof(double), typeof(FrameworkElement));
        public double Width { get => _w; set { _w = value; WpfHost.Touch(); } }
        public double Height { get => _h; set { _h = value; WpfHost.Touch(); } }
        public double MinWidth { get => _minW; set { _minW = value; WpfHost.Touch(); } }
        public double MinHeight { get => _minH; set { _minH = value; WpfHost.Touch(); } }
        public double MaxWidth { get => _maxW; set { _maxW = value; WpfHost.Touch(); } }
        public double MaxHeight { get => _maxH; set { _maxH = value; WpfHost.Touch(); } }
        public double ActualWidth => Rendered.Width;
        public double ActualHeight => Rendered.Height;
        public Thickness Margin { get => _margin; set { _margin = value; WpfHost.Touch(); } }
        public HorizontalAlignment HorizontalAlignment { get => _ha; set { _ha = value; WpfHost.Touch(); } }
        public VerticalAlignment VerticalAlignment { get => _va; set { _va = value; WpfHost.Touch(); } }
        public object? Tag { get; set; }
        public string Name { get; set; } = "";
        public object? ToolTip { get; set; }
        public Cursor? Cursor { get; set; }
        public object? DataContext { get; set; }
        public Transform? LayoutTransform { get => _layout; set { _layout = value; WpfHost.Touch(); } }
        private Transform? _layout;

        /// <summary>자리 바꿈(LayoutTransform) 가운데 배율만 — 원본 화면들은 배율로만 쓴다.</summary>
        internal (double X, double Y) LayoutScale =>
            _layout is ScaleTransform st && st.ScaleX > 0 && st.ScaleY > 0 ? (st.ScaleX, st.ScaleY) : (1, 1);
        public FlowDirection FlowDirection { get; set; }
        public bool UseLayoutRounding { get; set; }
        public Style? Style { get; set; }
        public ResourceDictionary Resources { get; set; } = new();
        public Controls.ContextMenu? ContextMenu { get; set; }
        public bool IsLoaded => _loaded;
        public DependencyObject? Parent => VisualParent;
        public bool ForceCursor { get; set; }

        public event RoutedEventHandler? Loaded, Unloaded;
        public event SizeChangedEventHandler? SizeChanged;

        public object? FindName(string name) => Find(this, name);

        private static object? Find(UIElement at, string name)
        {
            if (at is FrameworkElement f && f.Name == name) return f;
            foreach (var c in at.VisualChildren)
                if (Find(c, name) is { } hit) return hit;
            return null;
        }

        internal void FireLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Loaded?.Invoke(this, new RoutedEventArgs());
        }

        internal void FireUnloaded()
        {
            if (!_loaded) return;
            _loaded = false;
            Unloaded?.Invoke(this, new RoutedEventArgs());
        }

        internal bool SizeMoved(out SizeChangedEventArgs e)
        {
            e = null!;
            if (Rendered == _lastSize) return false;
            e = new SizeChangedEventArgs { NewSize = Rendered, PreviousSize = _lastSize };
            _lastSize = Rendered;
            return true;
        }

        internal void FireSizeChanged(SizeChangedEventArgs e)
        {
            SizeChanged?.Invoke(this, e);
            OnRenderSizeChanged(new SizeChangedInfo(e.PreviousSize, e.NewSize));
        }

        protected virtual void OnRenderSizeChanged(SizeChangedInfo info) { }

        protected virtual Size MeasureOverride(Size available) => new(0, 0);
        protected virtual Size ArrangeOverride(Size final) => final;

        protected sealed override Size MeasureCore(Size available)
        {
            if (Visibility == Visibility.Collapsed) return new Size(0, 0);
            var (lx, ly) = LayoutScale;
            double aw = Math.Max(0, available.Width - _margin.W) / lx, ah = Math.Max(0, available.Height - _margin.H) / ly;
            double minW = Math.Max(_minW, double.IsNaN(_w) ? 0 : _w), minH = Math.Max(_minH, double.IsNaN(_h) ? 0 : _h);
            double maxW = double.IsNaN(_w) ? _maxW : Math.Min(_maxW, Math.Max(_w, _minW));
            double maxH = double.IsNaN(_h) ? _maxH : Math.Min(_maxH, Math.Max(_h, _minH));
            minW = Math.Min(minW, maxW); minH = Math.Min(minH, maxH);
            aw = Math.Max(minW, Math.Min(aw, maxW));
            ah = Math.Max(minH, Math.Min(ah, maxH));
            var d = MeasureOverride(new Size(aw, ah));
            double w = Math.Max(minW, Math.Min(d.Width, maxW)), h = Math.Max(minH, Math.Min(d.Height, maxH));
            if (!double.IsNaN(_w)) w = Math.Max(_minW, Math.Min(_w, _maxW));
            if (!double.IsNaN(_h)) h = Math.Max(_minH, Math.Min(_h, _maxH));
            NaturalSize = new Size(w, h);
            w = w * lx + _margin.W; h = h * ly + _margin.H;
            return new Size(Math.Max(0, Fin(w)), Math.Max(0, Fin(h)));
        }

        private static double Fin(double v) => double.IsFinite(v) ? v : 0;

        /// <summary>여백을 뺀 바라는 크기.</summary>
        internal Size NaturalSize;
        internal Rect? LayoutClip;

        protected sealed override void ArrangeCore(Rect final)
        {
            if (Visibility == Visibility.Collapsed) { Rendered = new Size(0, 0); Offset = final.Location; return; }
            var (lx, ly) = LayoutScale;
            double aw = Math.Max(0, final.Width - _margin.W) / lx, ah = Math.Max(0, final.Height - _margin.H) / ly;
            double w = _ha == HorizontalAlignment.Stretch && double.IsNaN(_w) ? aw : Math.Min(NaturalSize.Width, double.IsNaN(_w) ? aw : double.PositiveInfinity);
            double h = _va == VerticalAlignment.Stretch && double.IsNaN(_h) ? ah : Math.Min(NaturalSize.Height, double.IsNaN(_h) ? ah : double.PositiveInfinity);
            if (!double.IsNaN(_w)) w = NaturalSize.Width;
            if (!double.IsNaN(_h)) h = NaturalSize.Height;
            w = Math.Max(w, Math.Min(_minW, _maxW)); h = Math.Max(h, Math.Min(_minH, _maxH));
            w = Math.Min(w, _maxW); h = Math.Min(h, _maxH);
            // 늘이기로 받았는데 바라는 것보다 좁으면 바라는 만큼은 준다(넘친 것은 잘린다).
            if (_ha == HorizontalAlignment.Stretch && w < NaturalSize.Width && double.IsNaN(_w)) w = NaturalSize.Width;
            if (_va == VerticalAlignment.Stretch && h < NaturalSize.Height && double.IsNaN(_h)) h = NaturalSize.Height;
            var size = ArrangeOverride(new Size(w, h));
            double x = final.X + _margin.Left, y = final.Y + _margin.Top;
            double room = (aw - size.Width) * lx, roomH = (ah - size.Height) * ly;
            if (_ha == HorizontalAlignment.Center || (_ha == HorizontalAlignment.Stretch && room > 0)) x += Math.Max(0, room) / 2;
            else if (_ha == HorizontalAlignment.Right) x += room;
            if (_va == VerticalAlignment.Center || (_va == VerticalAlignment.Stretch && roomH > 0)) y += Math.Max(0, roomH) / 2;
            else if (_va == VerticalAlignment.Bottom) y += roomH;
            if (_ha == HorizontalAlignment.Center && room < 0) x += room / 2;
            Offset = new Point(x, y);
            Rendered = size;
            // 받은 칸보다 크면 칸 밖은 잘린다(WPF 의 자리 오림).
            LayoutClip = size.Width > aw + 0.5 || size.Height > ah + 0.5
                ? new Rect((final.X + _margin.Left - x) / lx, (final.Y + _margin.Top - y) / ly, aw, ah)
                : null;
        }

        /// <summary>둘러싼 굴림 판(ScrollViewer)을 굴려 이 요소가 보이게 한다.</summary>
        public void BringIntoView()
        {
            for (var e = VisualParent; e != null; e = e.VisualParent)
                if (e is System.Windows.Controls.ScrollViewer sv) { sv.Reveal(this); return; }
        }
        public void SetResourceReference(DependencyProperty p, object key) { }
        public object? FindResource(object key) => null;
        public object? TryFindResource(object key) => null;
        public void SetBinding(DependencyProperty p, string path) { }

        /// <summary>
        /// 묶음 — 원천(Source)이 있으면 지금 값을 한 번 옮겨 둔다(WpfLite 는 값 바뀜 알림이 없어 따라가지 않는다).
        /// </summary>
        public void SetBinding(DependencyProperty p, System.Windows.Data.Binding b)
        {
            object? value = b.Source;
            if (value != null && b.PathText.Length > 0)
                foreach (var part in b.PathText.Split('.'))
                    value = value?.GetType().GetProperty(part)?.GetValue(value);
            if (b.Converter != null) value = b.Converter.Convert(value!, typeof(object), b.ConverterParameter!, System.Globalization.CultureInfo.CurrentCulture);
            if (b.Source != null) SetValue(p, value);
        }
    }

    public readonly record struct SizeChangedInfo(Size PreviousSize, Size NewSize)
    {
        public bool WidthChanged => PreviousSize.Width != NewSize.Width;
        public bool HeightChanged => PreviousSize.Height != NewSize.Height;
    }

    public sealed class WindowCollection : List<Window> { }

    public class Style { }
    public class ResourceDictionary : Dictionary<object, object> { }

    public class DataTemplate
    {
        public FrameworkElementFactory? VisualTree { get; set; }
    }

    /// <summary>창 바깥 크기 기준점 — 화면 = 원본 640x480 판.</summary>
    public static class Application
    {
        public static ApplicationInstance Current { get; } = new();
    }

    public sealed class ApplicationInstance
    {
        /// <summary>앱이 앞으로 왔다 · 뒤로 갔다(안드로이드 OnResume · OnPause 가 알린다).</summary>
        public event EventHandler? Activated, Deactivated;
        internal void RaiseActive(bool front) => (front ? Activated : Deactivated)?.Invoke(this, EventArgs.Empty);
        public Window? MainWindow { get; set; }
        public Dispatcher Dispatcher => Dispatcher.CurrentDispatcher;
        public WindowCollection Windows => [.. WpfHost.Shown];
        public void Shutdown() { }
    }
}

namespace System.Windows.Media.Effects
{
    public abstract class Effect { }
    public sealed class DropShadowEffect : Effect
    {
        public double BlurRadius { get; set; }
        public double ShadowDepth { get; set; }
        public double Opacity { get; set; }
        public double Direction { get; set; }
        public System.Windows.Media.Color Color { get; set; }
    }
}

namespace System.Windows.Media
{
    /// <summary>그려지는 것의 뿌리(WPF Visual).</summary>
    public abstract class Visual : DependencyObject
    {
        public Point PointToScreen(Point p) => ((UIElement)this).ToWindow(p, toScreen: true);
        public Point PointFromScreen(Point p) => ((UIElement)this).FromWindow(p, fromScreen: true);
        public GeneralTransform TransformToAncestor(Visual ancestor) => new OffsetTransform(((UIElement)this).OffsetTo((UIElement)ancestor));
        public GeneralTransform TransformToVisual(Visual other)
        {
            var me = ((UIElement)this).ToWindow(new Point(0, 0), false);
            var them = ((UIElement)other).ToWindow(new Point(0, 0), false);
            return new OffsetTransform(new Vector(me.X - them.X, me.Y - them.Y));
        }
        public bool IsAncestorOf(DependencyObject d)
        {
            for (var p = (d as UIElement)?.VisualParent; p != null; p = p.VisualParent)
                if (ReferenceEquals(p, this)) return true;
            return false;
        }
        public bool IsDescendantOf(DependencyObject d) => d is Visual v && v.IsAncestorOf(this);
    }

    public abstract class GeneralTransform : Freezable
    {
        public Point Transform(Point p) { TryTransform(p, out var r); return r; }
        public abstract bool TryTransform(Point p, out Point r);
    }

    internal sealed class OffsetTransform(Vector by) : GeneralTransform
    {
        public override bool TryTransform(Point p, out Point r) { r = new(p.X + by.X, p.Y + by.Y); return true; }
    }

}
