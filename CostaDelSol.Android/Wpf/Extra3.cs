using Android.App;
using Android.Widget;
using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// v1.1.x 의 PC 판 창들이 새로 쓰는 WPF 조각 — 틀 찍어 내기(FrameworkElementFactory), 창 손잡이, 그림 풀개 따위.

namespace System.Windows
{
    /// <summary>틀(FrameworkElementFactory) 하나를 실제 요소로 찍어 낸다 — 값은 같은 이름 속성으로, 묶음은 그 줄에서 읽어.</summary>
    internal static class TemplateBuilder
    {
        public static object Build(FrameworkElementFactory f, object? item)
        {
            var made = Activator.CreateInstance(f.Type)!;
            if (made is FrameworkElement fe) fe.DataContext = item;
            foreach (var (p, v) in f.Values) Apply(made, p, v);
            foreach (var (p, b) in f.Bindings) Apply(made, p, Resolve(b, item, p));
            foreach (var child in f.Children)
            {
                var c = Build(child, item);
                switch (made, c)
                {
                    case (Grid g, ColumnDefinition col): g.ColumnDefinitions.Add(col); break;
                    case (Grid g, RowDefinition row): g.RowDefinitions.Add(row); break;
                    case (Panel panel, UIElement e): panel.Children.Add(e); break;
                    case (Decorator d, UIElement e): d.Child = e; break;
                    case (ContentControl cc, _): cc.Content = c; break;
                }
            }
            return made;
        }

        private static void Apply(object target, DependencyProperty p, object? value)
        {
            if (target is DependencyObject d) { d.SetValue(p, value); return; }
            var prop = target.GetType().GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance);
            if (prop is { CanWrite: true }) prop.SetValue(target, value);
        }

        private static object? Resolve(System.Windows.Data.Binding b, object? item, DependencyProperty p)
        {
            object? value = b.Source ?? item;
            if (value != null && b.PathText.Length > 0)
                foreach (var part in b.PathText.Split('.'))
                    value = value?.GetType().GetProperty(part)?.GetValue(value);
            if (b.Converter != null)
                value = b.Converter.Convert(value!, typeof(object), b.ConverterParameter!, Globalization.CultureInfo.CurrentCulture);
            if (b.StringFormat != null && value != null) value = string.Format(b.StringFormat, value);
            return value;
        }
    }

}

namespace System.Windows.Controls
{
    /// <summary>줄들을 담을 판의 틀 — 안드로이드 목록은 늘 세로로 쌓는다.</summary>
    public sealed class ItemsPanelTemplate
    {
        public ItemsPanelTemplate() { }
        public ItemsPanelTemplate(FrameworkElementFactory root) { VisualTree = root; }
        public FrameworkElementFactory? VisualTree { get; set; }
    }

    public enum DataGridEditingUnit { Cell, Row }

    public readonly struct DataGridCellInfo
    {
        public DataGridCellInfo(object item, DataGridColumn column) { Item = item; Column = column; }
        public object? Item { get; }
        public DataGridColumn? Column { get; }
    }

    /// <summary>풍선 도움말 때 — 손가락에는 머무름이 없으므로 받기만 한다.</summary>
    public static class ToolTipService
    {
        public static void SetInitialShowDelay(DependencyObject d, int ms) { }
        public static void SetShowDuration(DependencyObject d, int ms) { }
        public static void SetBetweenShowDelay(DependencyObject d, int ms) { }
        public static void SetShowOnDisabled(DependencyObject d, bool on) { }
        public static void SetPlacement(DependencyObject d, Primitives.PlacementMode mode) { }
    }
}

namespace System.Windows.Interop
{
    public delegate IntPtr HwndSourceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

    /// <summary>Win32 창 메시지 손(HwndSource) — 안드로이드에는 창 메시지가 없다. 늘 null 이라 훅을 걸지 않는다.</summary>
    public sealed class HwndSource
    {
        public static HwndSource? FromHwnd(IntPtr hwnd) => null;
        public void AddHook(HwndSourceHook hook) { }
        public void RemoveHook(HwndSourceHook hook) { }
    }

    /// <summary>창 손잡이 — 안드로이드에는 HWND 가 없다. 떠 있는 창이면 0 아닌 값을 준다(살아 있는지만 보는 자리가 쓴다).</summary>
    public sealed class WindowInteropHelper(Window window)
    {
        public IntPtr Handle => window.IsVisible || window.IsLoaded ? new IntPtr(1) : IntPtr.Zero;
        public IntPtr Owner { get; set; }
        public IntPtr EnsureHandle() => new(1);
    }
}

namespace System.Windows.Media
{
    public abstract class Drawing { }

    public sealed class GeometryDrawing : Drawing
    {
        public GeometryDrawing() { }
        public GeometryDrawing(Brush? brush, Pen? pen, Geometry? geometry) { Brush = brush; Pen = pen; Geometry = geometry; }
        public Brush? Brush { get; set; }
        public Pen? Pen { get; set; }
        public Geometry? Geometry { get; set; }
    }

    /// <summary>무늬 붓(DrawingBrush) — 체크 무늬 바탕 따위. 안드로이드는 무늬를 그리지 않는다(바탕이 비어 보인다).</summary>
    public sealed class DrawingBrush : TileBrush
    {
        public DrawingBrush() { }
        public DrawingBrush(Drawing drawing) { Drawing = drawing; }
        public Drawing? Drawing { get; set; }
        public BrushMappingMode ViewportUnits { get; set; }
    }
}

namespace System.Windows.Media.Imaging
{
    /// <summary>그림 풀개(BitmapDecoder) — 안드로이드 풀개로 풀어 첫 장 하나를 낸다.</summary>
    public class BitmapDecoder
    {
        protected BitmapDecoder(BitmapFrame frame) { Frames = [frame]; }
        public IReadOnlyList<BitmapFrame> Frames { get; }
        public static BitmapDecoder Create(Uri uri, BitmapCreateOptions options, BitmapCacheOption cache) =>
            new(BitmapFrame.Create(uri, options, cache));
        public static BitmapDecoder Create(Stream stream, BitmapCreateOptions options, BitmapCacheOption cache) =>
            new(BitmapFrame.Create(stream, options, cache));
    }
}

namespace System.Windows
{
    [Flags]
    public enum MessageBoxOptions { None = 0, DefaultDesktopOnly = 0x20000, RightAlign = 0x80000, RtlReading = 0x100000, ServiceNotification = 0x200000 }
}

namespace System.Windows
{
    /// <summary>
    /// WPF 가 켤 때 하는 일 — 앱 안 자료 주소(<c>pack://application:,,,/…</c>)를 알아듣게 등록한다.
    /// 안 하면 .NET 이 「포트가 잘못됐다」며 그 주소를 못 만든다 — 모드 창의 리디바탕 글꼴(UiFont)을 처음 열 때 오류가 났다.
    /// </summary>
    internal static class PackScheme
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Register()
        {
            if (!UriParser.IsKnownScheme("pack"))
                UriParser.Register(new GenericUriParser(GenericUriParserOptions.GenericAuthority), "pack", -1);
        }
    }
}
