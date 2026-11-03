using System.Collections;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ACanvas = Android.Graphics.Canvas;
using APaint = Android.Graphics.Paint;
using ARectF = Android.Graphics.RectF;

namespace System.Windows.Controls
{
    public enum Orientation { Horizontal, Vertical }
    public enum Dock { Left, Top, Right, Bottom }
    public enum ScrollBarVisibility { Disabled, Auto, Hidden, Visible }
    public enum SelectionMode { Single, Multiple, Extended }
    public enum StretchDirection { UpOnly, DownOnly, Both }

    /// <summary>자식 묶음 — 넣고 빼면 어버이가 바뀐다.</summary>
    public sealed class UIElementCollection : IList<UIElement>
    {
        private readonly UIElement _owner;
        private readonly List<UIElement> _items = [];
        internal UIElementCollection(UIElement owner) { _owner = owner; }
        public UIElement this[int i] { get => _items[i]; set { _owner.Orphan(_items[i]); _owner.Adopt(value); _items[i] = value; } }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public int Add(UIElement e) { _owner.Adopt(e); _items.Add(e); return _items.Count - 1; }
        void ICollection<UIElement>.Add(UIElement e) => Add(e);
        public void Insert(int i, UIElement e) { _owner.Adopt(e); _items.Insert(i, e); }
        public bool Remove(UIElement e) { bool r = _items.Remove(e); if (r) { _owner.Orphan(e); WpfHost.Touch(); } return r; }
        public void RemoveAt(int i) { var e = _items[i]; _items.RemoveAt(i); _owner.Orphan(e); WpfHost.Touch(); }
        public void RemoveRange(int i, int n) { for (int k = 0; k < n; k++) RemoveAt(i); }
        public void Clear() { foreach (var e in _items) _owner.Orphan(e); _items.Clear(); WpfHost.Touch(); }
        public bool Contains(UIElement e) => _items.Contains(e);
        public int IndexOf(UIElement e) => _items.IndexOf(e);
        public void CopyTo(UIElement[] a, int i) => _items.CopyTo(a, i);
        public IEnumerator<UIElement> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
        internal List<UIElement> Items => _items;
    }

    // ── 칠하기 도구 ──
    internal static class Ink
    {
        [ThreadStatic] private static APaint? _fill;
        internal static APaint Fill => _fill ??= new APaint { AntiAlias = false };

        internal static void Rect(ACanvas c, Brush? brush, double x, double y, double w, double h, double opacity = 1)
        {
            if (brush == null || w <= 0 || h <= 0) return;
            var dst = new ARectF((float)x, (float)y, (float)(x + w), (float)(y + h));
            switch (brush)
            {
                case SolidColorBrush s:
                {
                    var p = Fill;
                    p.SetShader(null);
                    p.Color = new Android.Graphics.Color(s.Color.R, s.Color.G, s.Color.B, (int)(s.Color.A * brush.Opacity * opacity));
                    c.DrawRect(dst, p);
                    break;
                }
                case ImageBrush ib when ib.ImageSource?.Bitmap is { } bmp:
                {
                    var p = new APaint { FilterBitmap = false, Alpha = (int)(255 * brush.Opacity * opacity) };
                    if (ib.TileMode != TileMode.None)
                    {
                        var shader = new Android.Graphics.BitmapShader(bmp, Android.Graphics.Shader.TileMode.Repeat!, Android.Graphics.Shader.TileMode.Repeat!);
                        var m = new Android.Graphics.Matrix();
                        double vw = ib.ViewportUnits == BrushMappingMode.Absolute ? ib.Viewport.Width : ib.Viewport.Width * w;
                        double vh = ib.ViewportUnits == BrushMappingMode.Absolute ? ib.Viewport.Height : ib.Viewport.Height * h;
                        m.SetScale((float)(vw / bmp.Width), (float)(vh / bmp.Height));
                        m.PostTranslate((float)x, (float)y);
                        shader.SetLocalMatrix(m);
                        p.SetShader(shader);
                        c.DrawRect(dst, p);
                        break;
                    }
                    double bw = ib.ImageSource.Width, bh = ib.ImageSource.Height;
                    var place = Stretched(ib.Stretch, bw, bh, w, h, ib.AlignmentX, ib.AlignmentY);
                    c.Save();
                    c.ClipRect(dst);
                    c.DrawBitmap(bmp, null, new ARectF((float)(x + place.X), (float)(y + place.Y), (float)(x + place.Right), (float)(y + place.Bottom)), p);
                    c.Restore();
                    break;
                }
                case LinearGradientBrush lg when lg.GradientStops.Count > 0:
                {
                    var p = new APaint();
                    var colors = lg.GradientStops.Select(g => g.Color.Argb).ToArray();
                    var stops = lg.GradientStops.Select(g => (float)g.Offset).ToArray();
                    if (colors.Length == 1) { colors = [colors[0], colors[0]]; stops = [0, 1]; }
                    p.SetShader(new Android.Graphics.LinearGradient(
                        (float)(x + lg.StartPoint.X * w), (float)(y + lg.StartPoint.Y * h),
                        (float)(x + lg.EndPoint.X * w), (float)(y + lg.EndPoint.Y * h), colors, stops, Android.Graphics.Shader.TileMode.Clamp!));
                    p.Alpha = (int)(255 * brush.Opacity * opacity);
                    c.DrawRect(dst, p);
                    break;
                }
            }
        }

        internal static Rect Stretched(Stretch stretch, double bw, double bh, double w, double h,
                                       AlignmentX ax = AlignmentX.Center, AlignmentY ay = AlignmentY.Center)
        {
            if (bw <= 0 || bh <= 0) return new Rect(0, 0, w, h);
            double dw = bw, dh = bh;
            switch (stretch)
            {
                case Stretch.Fill: return new Rect(0, 0, w, h);
                case Stretch.Uniform: { double k = Math.Min(w / bw, h / bh); dw = bw * k; dh = bh * k; break; }
                case Stretch.UniformToFill: { double k = Math.Max(w / bw, h / bh); dw = bw * k; dh = bh * k; break; }
            }
            double x = ax switch { AlignmentX.Left => 0, AlignmentX.Right => w - dw, _ => (w - dw) / 2 };
            double y = ay switch { AlignmentY.Top => 0, AlignmentY.Bottom => h - dh, _ => (h - dh) / 2 };
            return new Rect(x, y, dw, dh);
        }

        internal static Android.Graphics.Color ColorOf(Brush? b, double opacity = 1) =>
            b is SolidColorBrush s ? new Android.Graphics.Color(s.Color.R, s.Color.G, s.Color.B, (int)(s.Color.A * b.Opacity * opacity))
                                   : Android.Graphics.Color.Black;

        internal static bool Paints(Brush? b) => b != null;
    }

    public abstract class Panel : FrameworkElement
    {
        private Brush? _background;
        protected Panel() { Children = new UIElementCollection(this); }
        public UIElementCollection Children { get; }
        public Brush? Background { get => _background; set { if (Equals(_background, value)) return; _background = value; WpfHost.Touch(); } }
        public bool IsItemsHost { get; set; }

        public static readonly DependencyProperty ZIndexProperty = DependencyProperty.RegisterAttached("ZIndex", typeof(int), typeof(Panel));
        public static void SetZIndex(UIElement e, int z) => e.Put(ZIndexProperty, z);
        public static int GetZIndex(UIElement e) => e.Get(ZIndexProperty, 0);

        internal override IEnumerable<UIElement> VisualChildren =>
            Children.Items.Any(c => GetZIndex(c) != 0)
                ? Children.Items.Select((c, i) => (c, i)).OrderBy(t => GetZIndex(t.c)).ThenBy(t => t.i).Select(t => t.c).ToList()
                : Children.Items.ToList();

        internal override void Paint(ACanvas c, float k) => Ink.Rect(c, _background, 0, 0, ActualWidth, ActualHeight);
        internal override bool HitSelf(Point p) => _background != null;
        internal override void Orphan(UIElement child) { base.Orphan(child); }
    }

    public class Canvas : Panel
    {
        public static readonly DependencyProperty LeftProperty = DependencyProperty.RegisterAttached("Left", typeof(double), typeof(Canvas));
        public static readonly DependencyProperty TopProperty = DependencyProperty.RegisterAttached("Top", typeof(double), typeof(Canvas));
        public static readonly DependencyProperty RightProperty = DependencyProperty.RegisterAttached("Right", typeof(double), typeof(Canvas));
        public static readonly DependencyProperty BottomProperty = DependencyProperty.RegisterAttached("Bottom", typeof(double), typeof(Canvas));
        public static void SetLeft(UIElement e, double v) => e.Put(LeftProperty, v);
        public static void SetTop(UIElement e, double v) => e.Put(TopProperty, v);
        public static void SetRight(UIElement e, double v) => e.Put(RightProperty, v);
        public static void SetBottom(UIElement e, double v) => e.Put(BottomProperty, v);
        public static double GetLeft(UIElement e) => e.Get(LeftProperty, double.NaN);
        public static double GetTop(UIElement e) => e.Get(TopProperty, double.NaN);
        public static double GetRight(UIElement e) => e.Get(RightProperty, double.NaN);
        public static double GetBottom(UIElement e) => e.Get(BottomProperty, double.NaN);

        protected override Size MeasureOverride(Size available)
        {
            foreach (var c in Children) c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size final)
        {
            foreach (var c in Children)
            {
                double x = GetLeft(c), y = GetTop(c);
                if (double.IsNaN(x)) { double r = GetRight(c); x = double.IsNaN(r) ? 0 : final.Width - c.DesiredSize.Width - r; }
                if (double.IsNaN(y)) { double b = GetBottom(c); y = double.IsNaN(b) ? 0 : final.Height - c.DesiredSize.Height - b; }
                c.Arrange(new Rect(x, y, c.DesiredSize.Width, c.DesiredSize.Height));
            }
            return final;
        }
    }

    public class StackPanel : Panel
    {
        private Orientation _o = Orientation.Vertical;
        public Orientation Orientation { get => _o; set { if (Equals(_o, value)) return; _o = value; WpfHost.Touch(); } }

        protected override Size MeasureOverride(Size available)
        {
            double w = 0, h = 0;
            bool v = _o == Orientation.Vertical;
            foreach (var c in Children)
            {
                c.Measure(v ? new Size(available.Width, double.PositiveInfinity) : new Size(double.PositiveInfinity, available.Height));
                if (v) { w = Math.Max(w, c.DesiredSize.Width); h += c.DesiredSize.Height; }
                else { h = Math.Max(h, c.DesiredSize.Height); w += c.DesiredSize.Width; }
            }
            return new Size(w, h);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double at = 0;
            bool v = _o == Orientation.Vertical;
            foreach (var c in Children)
            {
                if (v) { c.Arrange(new Rect(0, at, final.Width, c.DesiredSize.Height)); at += c.DesiredSize.Height; }
                else { c.Arrange(new Rect(at, 0, c.DesiredSize.Width, final.Height)); at += c.DesiredSize.Width; }
            }
            return final;
        }
    }

    public class WrapPanel : Panel
    {
        public Orientation Orientation { get; set; } = Orientation.Horizontal;
        public double ItemWidth { get; set; } = double.NaN;
        public double ItemHeight { get; set; } = double.NaN;

        private List<(UIElement C, Rect R)> Flow(double width)
        {
            var list = new List<(UIElement, Rect)>();
            double x = 0, y = 0, line = 0;
            foreach (var c in Children)
            {
                double w = double.IsNaN(ItemWidth) ? c.DesiredSize.Width : ItemWidth;
                double h = double.IsNaN(ItemHeight) ? c.DesiredSize.Height : ItemHeight;
                if (x > 0 && x + w > width) { x = 0; y += line; line = 0; }
                list.Add((c, new Rect(x, y, w, h)));
                x += w; line = Math.Max(line, h);
            }
            return list;
        }

        protected override Size MeasureOverride(Size available)
        {
            foreach (var c in Children) c.Measure(available);
            var flow = Flow(available.Width);
            return new Size(flow.Count == 0 ? 0 : flow.Max(f => f.R.Right), flow.Count == 0 ? 0 : flow.Max(f => f.R.Bottom));
        }

        protected override Size ArrangeOverride(Size final)
        {
            foreach (var (c, r) in Flow(final.Width)) c.Arrange(r);
            return final;
        }
    }

    public class DockPanel : Panel
    {
        public bool LastChildFill { get; set; } = true;
        public static readonly DependencyProperty DockProperty = DependencyProperty.RegisterAttached("Dock", typeof(Dock), typeof(DockPanel));
        public static void SetDock(UIElement e, Dock d) => e.Put(DockProperty, d);
        public static Dock GetDock(UIElement e) => e.Get(DockProperty, Dock.Left);

        protected override Size MeasureOverride(Size available)
        {
            double usedW = 0, usedH = 0, maxW = 0, maxH = 0;
            foreach (var c in Children)
            {
                c.Measure(new Size(Math.Max(0, available.Width - usedW), Math.Max(0, available.Height - usedH)));
                var d = c.DesiredSize;
                switch (GetDock(c))
                {
                    case Dock.Left or Dock.Right: maxH = Math.Max(maxH, usedH + d.Height); usedW += d.Width; break;
                    default: maxW = Math.Max(maxW, usedW + d.Width); usedH += d.Height; break;
                }
            }
            return new Size(Math.Max(maxW, usedW), Math.Max(maxH, usedH));
        }

        protected override Size ArrangeOverride(Size final)
        {
            double l = 0, t = 0, r = 0, b = 0;
            var kids = Children.Items;
            for (int i = 0; i < kids.Count; i++)
            {
                var c = kids[i];
                var d = c.DesiredSize;
                double w = Math.Max(0, final.Width - l - r), h = Math.Max(0, final.Height - t - b);
                if (i == kids.Count - 1 && LastChildFill) { c.Arrange(new Rect(l, t, w, h)); break; }
                switch (GetDock(c))
                {
                    case Dock.Left: c.Arrange(new Rect(l, t, Math.Min(d.Width, w), h)); l += Math.Min(d.Width, w); break;
                    case Dock.Right: c.Arrange(new Rect(final.Width - r - Math.Min(d.Width, w), t, Math.Min(d.Width, w), h)); r += Math.Min(d.Width, w); break;
                    case Dock.Top: c.Arrange(new Rect(l, t, w, Math.Min(d.Height, h))); t += Math.Min(d.Height, h); break;
                    default: c.Arrange(new Rect(l, final.Height - b - Math.Min(d.Height, h), w, Math.Min(d.Height, h))); b += Math.Min(d.Height, h); break;
                }
            }
            return final;
        }
    }

    public class ColumnDefinition
    {
        public static readonly DependencyProperty WidthProperty = DependencyProperty.Register("Width", typeof(GridLength), typeof(ColumnDefinition));
        public GridLength Width { get; set; } = new(1, GridUnitType.Star);
        public double MinWidth { get; set; }
        public double MaxWidth { get; set; } = double.PositiveInfinity;
        public double ActualWidth { get; internal set; }
    }

    public class RowDefinition
    {
        public GridLength Height { get; set; } = new(1, GridUnitType.Star);
        public double MinHeight { get; set; }
        public double MaxHeight { get; set; } = double.PositiveInfinity;
        public double ActualHeight { get; internal set; }
    }

    public class Grid : Panel
    {
        public List<ColumnDefinition> ColumnDefinitions { get; } = [];
        public List<RowDefinition> RowDefinitions { get; } = [];
        public bool ShowGridLines { get; set; }

        public static readonly DependencyProperty RowProperty = DependencyProperty.RegisterAttached("Row", typeof(int), typeof(Grid));
        public static readonly DependencyProperty ColumnProperty = DependencyProperty.RegisterAttached("Column", typeof(int), typeof(Grid));
        public static readonly DependencyProperty RowSpanProperty = DependencyProperty.RegisterAttached("RowSpan", typeof(int), typeof(Grid));
        public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached("ColumnSpan", typeof(int), typeof(Grid));
        public static void SetRow(UIElement e, int v) => e.Put(RowProperty, v);
        public static void SetColumn(UIElement e, int v) => e.Put(ColumnProperty, v);
        public static void SetRowSpan(UIElement e, int v) => e.Put(RowSpanProperty, v);
        public static void SetColumnSpan(UIElement e, int v) => e.Put(ColumnSpanProperty, v);
        public static int GetRow(UIElement e) => e.Get(RowProperty, 0);
        public static int GetColumn(UIElement e) => e.Get(ColumnProperty, 0);
        public static int GetRowSpan(UIElement e) => e.Get(RowSpanProperty, 1);
        public static int GetColumnSpan(UIElement e) => e.Get(ColumnSpanProperty, 1);

        private (GridLength L, double Min, double Max)[] Cols() =>
            ColumnDefinitions.Count == 0 ? [(new GridLength(1, GridUnitType.Star), 0, double.PositiveInfinity)]
                                         : ColumnDefinitions.Select(c => (c.Width, c.MinWidth, c.MaxWidth)).ToArray();

        private (GridLength L, double Min, double Max)[] Rows() =>
            RowDefinitions.Count == 0 ? [(new GridLength(1, GridUnitType.Star), 0, double.PositiveInfinity)]
                                      : RowDefinitions.Select(r => (r.Height, r.MinHeight, r.MaxHeight)).ToArray();

        private double[] _cw = [], _rh = [];

        private static int Clamp(int v, int n) => Math.Clamp(v, 0, n - 1);

        /// <summary>줄 · 칸 크기를 정한다. <paramref name="room"/> 이 끝없으면 별(*)도 바라는 만큼만.</summary>
        private static double[] Solve((GridLength L, double Min, double Max)[] defs, double room,
                                      IEnumerable<(int At, int Span, double Want)> wants)
        {
            int n = defs.Length;
            var size = new double[n];
            var list = wants.ToList();
            for (int i = 0; i < n; i++)
                if (defs[i].L.IsAbsolute) size[i] = defs[i].L.Value;
            // 바라는 크기 — 한 칸짜리만 auto · 별 칸에 먹인다.
            var want = new double[n];
            foreach (var (at, span, w) in list)
                if (span == 1) want[at] = Math.Max(want[at], w);
            for (int i = 0; i < n; i++)
                if (defs[i].L.IsAuto) size[i] = want[i];
            double stars = defs.Where(d => d.L.IsStar).Sum(d => d.L.Value);
            if (stars > 0)
            {
                if (double.IsFinite(room))
                {
                    double left = Math.Max(0, room - Enumerable.Range(0, n).Where(i => !defs[i].L.IsStar).Sum(i => size[i]));
                    for (int i = 0; i < n; i++) if (defs[i].L.IsStar) size[i] = left * defs[i].L.Value / stars;
                }
                else
                    for (int i = 0; i < n; i++) if (defs[i].L.IsStar) size[i] = want[i];
            }
            // 여러 칸에 걸친 것이 auto 칸을 늘린다.
            foreach (var (at, span, w) in list)
            {
                if (span <= 1) continue;
                double have = 0; int autos = 0;
                for (int i = at; i < Math.Min(n, at + span); i++) { have += size[i]; if (defs[i].L.IsAuto) autos++; }
                if (have < w && autos > 0)
                    for (int i = at; i < Math.Min(n, at + span); i++) if (defs[i].L.IsAuto) size[i] += (w - have) / autos;
            }
            for (int i = 0; i < n; i++) size[i] = Math.Clamp(size[i], defs[i].Min, Math.Max(defs[i].Min, defs[i].Max));
            return size;
        }

        protected override Size MeasureOverride(Size available)
        {
            var cols = Cols(); var rows = Rows();
            int nc = cols.Length, nr = rows.Length;
            // 먼저 끝없이 재 본다(auto · 별 칸 바람).
            foreach (var c in Children)
            {
                int col = Clamp(GetColumn(c), nc), row = Clamp(GetRow(c), nr);
                int cs = Math.Min(GetColumnSpan(c), nc - col), rs = Math.Min(GetRowSpan(c), nr - row);
                double w = SpanFixed(cols, col, cs) ?? (nc == 1 ? available.Width : double.PositiveInfinity);
                double h = SpanFixed(rows, row, rs) ?? (nr == 1 ? available.Height : double.PositiveInfinity);
                c.Measure(new Size(w, h));
            }
            _cw = Solve(cols, available.Width, Children.Select(c => (Clamp(GetColumn(c), nc), Math.Max(1, Math.Min(GetColumnSpan(c), nc - Clamp(GetColumn(c), nc))), c.DesiredSize.Width)));
            _rh = Solve(rows, available.Height, Children.Select(c => (Clamp(GetRow(c), nr), Math.Max(1, Math.Min(GetRowSpan(c), nr - Clamp(GetRow(c), nr))), c.DesiredSize.Height)));
            // 별 칸 자식은 정한 크기로 다시 잰다.
            foreach (var c in Children)
            {
                int col = Clamp(GetColumn(c), nc), row = Clamp(GetRow(c), nr);
                int cs = Math.Max(1, Math.Min(GetColumnSpan(c), nc - col)), rs = Math.Max(1, Math.Min(GetRowSpan(c), nr - row));
                double w = _cw.Skip(col).Take(cs).Sum(), h = _rh.Skip(row).Take(rs).Sum();
                if (!double.IsFinite(available.Width) && cols.Skip(col).Take(cs).Any(d => !d.L.IsAbsolute)) w = double.PositiveInfinity;
                if (!double.IsFinite(available.Height) && rows.Skip(row).Take(rs).Any(d => !d.L.IsAbsolute)) h = double.PositiveInfinity;
                c.Measure(new Size(w, h));
            }
            var cw2 = Solve(cols, double.PositiveInfinity, Children.Select(c => (Clamp(GetColumn(c), nc), Math.Max(1, Math.Min(GetColumnSpan(c), nc - Clamp(GetColumn(c), nc))), c.DesiredSize.Width)));
            var rh2 = Solve(rows, double.PositiveInfinity, Children.Select(c => (Clamp(GetRow(c), nr), Math.Max(1, Math.Min(GetRowSpan(c), nr - Clamp(GetRow(c), nr))), c.DesiredSize.Height)));
            return new Size(cw2.Sum(), rh2.Sum());
        }

        private static double? SpanFixed((GridLength L, double Min, double Max)[] defs, int at, int span)
        {
            double sum = 0;
            for (int i = at; i < Math.Min(defs.Length, at + Math.Max(1, span)); i++)
            {
                if (!defs[i].L.IsAbsolute) return null;
                sum += defs[i].L.Value;
            }
            return sum;
        }

        protected override Size ArrangeOverride(Size final)
        {
            var cols = Cols(); var rows = Rows();
            int nc = cols.Length, nr = rows.Length;
            _cw = Solve(cols, final.Width, Children.Select(c => (Clamp(GetColumn(c), nc), Math.Max(1, Math.Min(GetColumnSpan(c), nc - Clamp(GetColumn(c), nc))), c.DesiredSize.Width)));
            _rh = Solve(rows, final.Height, Children.Select(c => (Clamp(GetRow(c), nr), Math.Max(1, Math.Min(GetRowSpan(c), nr - Clamp(GetRow(c), nr))), c.DesiredSize.Height)));
            for (int i = 0; i < ColumnDefinitions.Count; i++) ColumnDefinitions[i].ActualWidth = _cw[i];
            for (int i = 0; i < RowDefinitions.Count; i++) RowDefinitions[i].ActualHeight = _rh[i];
            foreach (var c in Children)
            {
                int col = Clamp(GetColumn(c), nc), row = Clamp(GetRow(c), nr);
                int cs = Math.Max(1, Math.Min(GetColumnSpan(c), nc - col)), rs = Math.Max(1, Math.Min(GetRowSpan(c), nr - row));
                double x = _cw.Take(col).Sum(), y = _rh.Take(row).Sum();
                c.Arrange(new Rect(x, y, _cw.Skip(col).Take(cs).Sum(), _rh.Skip(row).Take(rs).Sum()));
            }
            return final;
        }
    }

    namespace Primitives
    {
        public class UniformGrid : Panel
        {
            public int Rows { get; set; }
            public int Columns { get; set; }
            public int FirstColumn { get; set; }

            private (int R, int C) Shape()
            {
                int n = Children.Count(c => c.Visibility != Visibility.Collapsed) + FirstColumn;
                int cols = Columns > 0 ? Columns : Rows > 0 ? (n + Rows - 1) / Rows : (int)Math.Ceiling(Math.Sqrt(n));
                int rows = Rows > 0 ? Rows : (n + Math.Max(1, cols) - 1) / Math.Max(1, cols);
                return (Math.Max(1, rows), Math.Max(1, cols));
            }

            protected override Size MeasureOverride(Size available)
            {
                var (r, c) = Shape();
                double w = 0, h = 0;
                foreach (var e in Children)
                {
                    e.Measure(new Size(available.Width / c, available.Height / r));
                    w = Math.Max(w, e.DesiredSize.Width); h = Math.Max(h, e.DesiredSize.Height);
                }
                return new Size(w * c, h * r);
            }

            protected override Size ArrangeOverride(Size final)
            {
                var (r, c) = Shape();
                double w = final.Width / c, h = final.Height / r;
                int i = FirstColumn;
                foreach (var e in Children)
                {
                    if (e.Visibility == Visibility.Collapsed) { e.Arrange(default); continue; }
                    e.Arrange(new Rect(i % c * w, i / c * h, w, h));
                    i++;
                }
                return final;
            }
        }

        public abstract class ButtonBase : ContentControl
        {
            public event RoutedEventHandler? Click;
            public System.Windows.Input.ICommand? Command { get; set; }
            public object? CommandParameter { get; set; }
            public bool IsPressed { get; private set; }
            public static readonly RoutedEvent ClickEvent = new("Click");

            internal override void OnRaised(string name, RoutedEventArgs e)
            {
                if (name == "MouseLeftButtonDown" && e is System.Windows.Input.MouseButtonEventArgs) { IsPressed = true; CaptureMouse(); e.Handled = true; }
                else if (name == "MouseLeftButtonUp" && IsPressed)
                {
                    IsPressed = false;
                    ReleaseMouseCapture();
                    e.Handled = true;
                    if (IsEnabled && IsMouseOver) OnClick();
                }
            }

            protected virtual void OnClick()
            {
                Click?.Invoke(this, new RoutedEventArgs(ClickEvent, this));
                Command?.Execute(CommandParameter);
            }

            internal void PerformClick() => OnClick();
        }

        public class ToggleButton : ButtonBase
        {
            public bool? IsChecked { get; set; } = false;
            public event RoutedEventHandler? Checked, Unchecked;
            protected override void OnClick()
            {
                IsChecked = IsChecked != true;
                (IsChecked == true ? Checked : Unchecked)?.Invoke(this, new RoutedEventArgs());
                base.OnClick();
            }
        }

        public class TextBoxBase : Control { }
        public enum PlacementMode { Absolute, Relative, Bottom, Center, Right, AbsolutePoint, RelativePoint, Mouse, MousePoint, Left, Top, Custom }

        /// <summary>
        /// 떠 있는 쪽지 — 열리면 제 창(손을 밑 창으로 흘려보내는)으로 뜬다. 자리는 붙은 요소 기준이다.
        /// </summary>
        public class Popup : FrameworkElement
        {
            private bool _open;
            private UIElement? _child;
            private PopupWindow? _window;
            public UIElement? Child { get => _child; set { _child = value; if (_window != null) _window.Content = value; } }
            public UIElement? PlacementTarget { get; set; }
            public PlacementMode Placement { get; set; } = PlacementMode.Bottom;
            public double HorizontalOffset { get => _h; set { if (Equals(_h, value)) return; _h = value; WpfHost.Touch(); } }
            public double VerticalOffset { get => _v; set { if (Equals(_v, value)) return; _v = value; WpfHost.Touch(); } }
            private double _h, _v;
            public bool AllowsTransparency { get; set; }
            public bool StaysOpen { get; set; } = true;
            /// <summary>안드로이드 — 지도 위에 겹쳐 그린다(창 판이 아니라 지도 덧그림 판).</summary>
            internal bool OnMap { get; set; }
            public PopupAnimation PopupAnimation { get; set; }
            public event EventHandler? Opened, Closed;

            public bool IsOpen
            {
                get => _open;
                set
                {
                    if (_open == value) return;
                    _open = value;
                    if (value)
                    {
                        _window = new PopupWindow(this) { Content = _child, OnMap = OnMap };
                        _window.Show();
                        Opened?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        var w = _window; _window = null;
                        if (w != null) { w.Content = null; w.Close(); }
                        Closed?.Invoke(this, EventArgs.Empty);
                    }
                }
            }

            internal Point Where(Size size)
            {
                var target = PlacementTarget ?? VisualParent;
                var at = target?.ToWindow(new Point(0, 0), toScreen: true) ?? new Point(0, 0);
                double tw = target?.RenderSize.Width ?? 0, th = target?.RenderSize.Height ?? 0;
                switch (Placement)
                {
                    case PlacementMode.Bottom: at.Y += th; break;
                    case PlacementMode.Right: at.X += tw; break;
                    case PlacementMode.Left: at.X -= size.Width; break;
                    case PlacementMode.Top: at.Y -= size.Height; break;
                    case PlacementMode.Center: at.X += (tw - size.Width) / 2; at.Y += (th - size.Height) / 2; break;
                    case PlacementMode.Absolute: at = new Point(0, 0); break;
                    case PlacementMode.Mouse or PlacementMode.MousePoint: at = WpfHost.LastPoint; break;
                }
                return new Point(at.X + _h, at.Y + _v);
            }
        }

        public enum PopupAnimation { None, Fade, Slide, Scroll }

        internal sealed class PopupWindow : Window
        {
            private readonly Popup _popup;
            public PopupWindow(Popup popup)
            {
                _popup = popup;
                Background = null;
                SizeToContent = SizeToContent.WidthAndHeight;
                WindowStartupLocation = WindowStartupLocation.Manual;
                PassThrough = true;
                Owner = Window.GetWindow(popup);
            }

            internal override void BeforeLayout()
            {
                var at = _popup.Where(RenderSize);
                SetPlace(at.X, at.Y);
            }
        }
        public class ScrollBar : Control { }
    }

    public class Decorator : FrameworkElement
    {
        private UIElement? _child;
        public virtual UIElement? Child
        {
            get => _child;
            set
            {
                if (ReferenceEquals(_child, value)) return;
                if (_child != null) Orphan(_child);
                _child = value;
                if (value != null) Adopt(value);
                WpfHost.Touch();
            }
        }

        internal override IEnumerable<UIElement> VisualChildren => _child == null ? [] : [_child];

        protected override Size MeasureOverride(Size available)
        {
            if (_child == null) return new Size(0, 0);
            _child.Measure(available);
            return _child.DesiredSize;
        }

        protected override Size ArrangeOverride(Size final)
        {
            _child?.Arrange(new Rect(final));
            return final;
        }
    }

    public class Border : Decorator
    {
        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register("Background", typeof(Brush), typeof(Border));
        public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register("BorderBrush", typeof(Brush), typeof(Border));
        public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register("BorderThickness", typeof(Thickness), typeof(Border));
        public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register("Padding", typeof(Thickness), typeof(Border));
        private Brush? _background, _borderBrush;
        private Thickness _thickness, _padding;
        public Brush? Background { get => _background; set { if (Equals(_background, value)) return; _background = value; WpfHost.Touch(); } }
        public Brush? BorderBrush { get => _borderBrush; set { if (Equals(_borderBrush, value)) return; _borderBrush = value; WpfHost.Touch(); } }
        public Thickness BorderThickness { get => _thickness; set { if (Equals(_thickness, value)) return; _thickness = value; WpfHost.Touch(); } }
        public Thickness Padding { get => _padding; set { if (Equals(_padding, value)) return; _padding = value; WpfHost.Touch(); } }
        public CornerRadius CornerRadius { get; set; }

        protected override Size MeasureOverride(Size available)
        {
            double ew = _thickness.W + _padding.W, eh = _thickness.H + _padding.H;
            if (Child == null) return new Size(ew, eh);
            Child.Measure(new Size(Math.Max(0, available.Width - ew), Math.Max(0, available.Height - eh)));
            return new Size(Child.DesiredSize.Width + ew, Child.DesiredSize.Height + eh);
        }

        protected override Size ArrangeOverride(Size final)
        {
            Child?.Arrange(new Rect(_thickness.Left + _padding.Left, _thickness.Top + _padding.Top,
                                    Math.Max(0, final.Width - _thickness.W - _padding.W), Math.Max(0, final.Height - _thickness.H - _padding.H)));
            return final;
        }

        internal override void Paint(ACanvas c, float k)
        {
            double w = ActualWidth, h = ActualHeight;
            Ink.Rect(c, _background, _thickness.Left, _thickness.Top, w - _thickness.W, h - _thickness.H);
            if (_borderBrush == null) return;
            var t = _thickness;
            Ink.Rect(c, _borderBrush, 0, 0, w, t.Top);
            Ink.Rect(c, _borderBrush, 0, h - t.Bottom, w, t.Bottom);
            Ink.Rect(c, _borderBrush, 0, t.Top, t.Left, h - t.H);
            Ink.Rect(c, _borderBrush, w - t.Right, t.Top, t.Right, h - t.H);
        }

        internal override bool HitSelf(Point p) => _background != null || (_borderBrush != null && _thickness.W + _thickness.H > 0 &&
            (p.X < _thickness.Left || p.Y < _thickness.Top || p.X >= ActualWidth - _thickness.Right || p.Y >= ActualHeight - _thickness.Bottom));
    }

    public class Viewbox : Decorator
    {
        public Stretch Stretch { get; set; } = Stretch.Uniform;
        public StretchDirection StretchDirection { get; set; } = StretchDirection.Both;
        private double _k = 1;

        protected override Size MeasureOverride(Size available)
        {
            if (Child == null) return new Size(0, 0);
            Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = Child.DesiredSize;
            if (d.Width <= 0 || d.Height <= 0) return d;
            double k = Math.Min(double.IsFinite(available.Width) ? available.Width / d.Width : double.PositiveInfinity,
                                double.IsFinite(available.Height) ? available.Height / d.Height : double.PositiveInfinity);
            if (!double.IsFinite(k)) k = 1;
            return new Size(d.Width * k, d.Height * k);
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (Child == null) return final;
            var d = Child.DesiredSize;
            _k = d.Width > 0 && d.Height > 0 ? Math.Min(final.Width / d.Width, final.Height / d.Height) : 1;
            Child.Arrange(new Rect(0, 0, d.Width, d.Height));
            Child.RenderTransform = new ScaleTransform(_k, _k);
            return final;
        }
    }

    public class Control : FrameworkElement
    {
        public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register("FontFamily", typeof(FontFamily), typeof(Control));
        private Brush? _background, _foreground, _borderBrush;
        public Brush? Background { get => _background; set { if (Equals(_background, value)) return; _background = value; WpfHost.Touch(); } }
        public Brush? Foreground { get => _foreground ?? Brushes.Black; set { if (Equals(_foreground, value)) return; _foreground = value; WpfHost.Touch(); } }
        public Brush? BorderBrush { get => _borderBrush; set { if (Equals(_borderBrush, value)) return; _borderBrush = value; WpfHost.Touch(); } }
        public Thickness BorderThickness { get; set; }
        public Thickness Padding { get; set; }
        public double FontSize { get; set; } = 12;
        public FontWeight FontWeight { get; set; } = FontWeights.Normal;
        public FontFamily? FontFamily { get; set; }
        public FontStyle FontStyle { get; set; }
        public HorizontalAlignment HorizontalContentAlignment { get; set; } = HorizontalAlignment.Left;
        public VerticalAlignment VerticalContentAlignment { get; set; } = VerticalAlignment.Top;
        public bool IsTabStop { get; set; } = true;
        public int TabIndex { get; set; }
        public object? Template { get; set; }
        public event System.Windows.Input.MouseButtonEventHandler? MouseDoubleClick, PreviewMouseDoubleClick;

        internal override void OnRaised(string name, RoutedEventArgs e)
        {
            if (name == "MouseLeftButtonDown" && e is System.Windows.Input.MouseButtonEventArgs { ClickCount: 2 } m)
                MouseDoubleClick?.Invoke(this, m);
        }

        internal override void Paint(ACanvas c, float k)
        {
            double w = ActualWidth, h = ActualHeight;
            var t = BorderThickness;
            Ink.Rect(c, _background, 0, 0, w, h);
            if (_borderBrush == null) return;
            Ink.Rect(c, _borderBrush, 0, 0, w, t.Top);
            Ink.Rect(c, _borderBrush, 0, h - t.Bottom, w, t.Bottom);
            Ink.Rect(c, _borderBrush, 0, t.Top, t.Left, h - t.H);
            Ink.Rect(c, _borderBrush, w - t.Right, t.Top, t.Right, h - t.H);
        }

        internal override bool HitSelf(Point p) => _background != null;
    }

    public class ContentControl : Control
    {
        private object? _content;
        private UIElement? _shown;

        public object? Content
        {
            get => _content;
            set
            {
                if (ReferenceEquals(_content, value)) return;
                if (_shown != null) Orphan(_shown);
                _content = value;
                _shown = value switch
                {
                    null => null,
                    UIElement e => e,
                    _ => new TextBlock { Text = value.ToString() ?? "" },
                };
                if (_shown is TextBlock tb && value is not UIElement)
                {
                    tb.Foreground = Foreground; tb.FontSize = FontSize; tb.FontWeight = FontWeight;
                    tb.HorizontalAlignment = HorizontalContentAlignment;
                    tb.VerticalAlignment = VerticalContentAlignment;
                }
                if (_shown != null) Adopt(_shown);
                WpfHost.Touch();
                OnContentChanged(null, value);
            }
        }

        protected virtual void OnContentChanged(object? oldContent, object? newContent) { }
        public bool HasContent => _content != null;

        internal override IEnumerable<UIElement> VisualChildren => _shown == null ? [] : [_shown];

        protected override Size MeasureOverride(Size available)
        {
            var t = BorderThickness; var p = Padding;
            double ew = t.W + p.W, eh = t.H + p.H;
            if (_shown == null) return new Size(ew, eh);
            _shown.Measure(new Size(Math.Max(0, available.Width - ew), Math.Max(0, available.Height - eh)));
            return new Size(_shown.DesiredSize.Width + ew, _shown.DesiredSize.Height + eh);
        }

        protected override Size ArrangeOverride(Size final)
        {
            var t = BorderThickness; var p = Padding;
            _shown?.Arrange(new Rect(t.Left + p.Left, t.Top + p.Top, Math.Max(0, final.Width - t.W - p.W), Math.Max(0, final.Height - t.H - p.H)));
            return final;
        }
    }

    public class ContentPresenter : ContentControl { }

    public class Label : ContentControl
    {
        public Label() { Padding = new Thickness(5); }
    }

    public class UserControl : ContentControl { }

    public class Button : Primitives.ButtonBase
    {
        public bool IsDefault { get; set; }
        public bool IsCancel { get; set; }

        public Button()
        {
            Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
            BorderThickness = new Thickness(1);
            Padding = new Thickness(1);
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
        }
    }

    public class CheckBox : Primitives.ToggleButton { }
    public class RadioButton : Primitives.ToggleButton { public string GroupName { get; set; } = ""; }

    public class Image : FrameworkElement
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(ImageSource), typeof(Image));
        public static readonly DependencyProperty StretchProperty = DependencyProperty.Register("Stretch", typeof(Stretch), typeof(Image));
        private ImageSource? _source;
        private Stretch _stretch = Stretch.Uniform;
        public ImageSource? Source { get => _source; set { if (Equals(_source, value)) return; _source = value; WpfHost.Touch(); } }
        public Stretch Stretch { get => _stretch; set { if (Equals(_stretch, value)) return; _stretch = value; WpfHost.Touch(); } }
        public StretchDirection StretchDirection { get; set; } = StretchDirection.Both;

        protected override Size MeasureOverride(Size available)
        {
            if (_source == null) return new Size(0, 0);
            double bw = _source.Width, bh = _source.Height;
            if (_stretch == Stretch.None || bw <= 0 || bh <= 0) return new Size(bw, bh);
            double kx = double.IsFinite(available.Width) ? available.Width / bw : double.PositiveInfinity;
            double ky = double.IsFinite(available.Height) ? available.Height / bh : double.PositiveInfinity;
            if (!double.IsFinite(kx) && !double.IsFinite(ky)) return new Size(bw, bh);
            switch (_stretch)
            {
                case Stretch.Fill:
                    return new Size(double.IsFinite(kx) ? available.Width : bw * ky, double.IsFinite(ky) ? available.Height : bh * kx);
                case Stretch.UniformToFill:
                {
                    double k = !double.IsFinite(kx) ? ky : !double.IsFinite(ky) ? kx : Math.Max(kx, ky);
                    return new Size(bw * k, bh * k);
                }
                default:
                {
                    double k = Math.Min(kx, ky);
                    return new Size(bw * k, bh * k);
                }
            }
        }

        protected override Size ArrangeOverride(Size final) => final;

        internal override void Paint(ACanvas c, float k)
        {
            if (_source?.Bitmap is not { } bmp) return;
            var r = Ink.Stretched(_stretch, _source.Width, _source.Height, ActualWidth, ActualHeight);
            var mode = RenderOptions.GetBitmapScalingMode(this);
            bool smooth = (mode is BitmapScalingMode.Fant or BitmapScalingMode.HighQuality) && (r.Width < _source.Width || r.Height < _source.Height);
            var p = WpfPaints.Bitmap(smooth);
            c.Save();
            c.ClipRect(0, 0, (float)ActualWidth, (float)ActualHeight);
            c.DrawBitmap(bmp, null, new ARectF((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom), p);
            c.Restore();
        }

        internal override bool HitSelf(Point p) => _source != null;
    }

    public class TextBlock : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(TextBlock));
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register("FontSize", typeof(double), typeof(TextBlock));
        public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register("FontWeight", typeof(FontWeight), typeof(TextBlock));
        public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register("TextWrapping", typeof(TextWrapping), typeof(TextBlock));
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register("Foreground", typeof(Brush), typeof(TextBlock));
        private string _text = "";
        private Brush? _foreground;
        private double _size = 12;
        public TextBlock() { Inlines = new Documents.InlineCollection(this); }
        public TextBlock(Documents.Inline inline) : this() { Inlines.Add(inline); }
        public string Text { get => Inlines.Count > 0 ? string.Concat(Inlines.Select(i => i.Plain)) : _text; set { if (Inlines.Count == 0 && _text == (value ?? "")) return; Inlines.Clear(); _text = value ?? ""; WpfHost.Touch(); } }
        public Brush? Foreground { get => _foreground ?? Brushes.Black; set { if (Equals(_foreground, value)) return; _foreground = value; WpfHost.Touch(); } }
        public Brush? Background { get; set; }
        public double FontSize { get => _size; set { if (Equals(_size, value)) return; _size = value; WpfHost.Touch(); } }
        public FontWeight FontWeight { get; set; } = FontWeights.Normal;
        public FontFamily? FontFamily { get; set; }
        public FontStyle FontStyle { get; set; }
        public TextWrapping TextWrapping { get; set; } = TextWrapping.NoWrap;
        public TextAlignment TextAlignment { get; set; }
        public TextTrimming TextTrimming { get; set; }
        public Thickness Padding { get; set; }
        public double LineHeight { get; set; } = double.NaN;
        public Documents.InlineCollection Inlines { get; }

        private List<(string Text, double Width)> _lines = [];

        internal APaint PaintFor(Documents.Inline? run = null)
        {
            var p = new APaint { AntiAlias = true };
            p.TextSize = (float)(run?.FontSize ?? _size);
            bool bold = (run?.FontWeight ?? FontWeight).Weight >= 600;
            p.SetTypeface(Android.Graphics.Typeface.Create(Android.Graphics.Typeface.Default, bold ? Android.Graphics.TypefaceStyle.Bold! : Android.Graphics.TypefaceStyle.Normal!));
            p.Color = Ink.ColorOf(run?.Foreground ?? Foreground);
            return p;
        }

        private double LineStep(APaint p) => double.IsNaN(LineHeight) ? Math.Ceiling(_size * 1.33) : LineHeight;

        protected override Size MeasureOverride(Size available)
        {
            var p = PaintFor();
            string text = Text;
            double room = available.Width - Padding.W;
            _lines = [];
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if ((TextWrapping != TextWrapping.NoWrap || WpfHost.SoftWrap) && double.IsFinite(room) && room > 0)
                {
                    int start = 0;
                    while (start < line.Length)
                    {
                        int n = p.BreakText(line, start, line.Length, true, (float)room, null);
                        if (n <= 0) n = 1;
                        int end = start + n;
                        if (end < line.Length)
                        {
                            int sp = line.LastIndexOf(' ', end - 1, n);
                            if (sp > start) end = sp + 1;
                        }
                        string part = line[start..end];
                        _lines.Add((part, p.MeasureText(part)));
                        start = end;
                    }
                    if (line.Length == 0) _lines.Add(("", 0));
                }
                else _lines.Add((line, p.MeasureText(line)));
            }
            double w = _lines.Count == 0 ? 0 : _lines.Max(l => l.Width);
            if (Inlines.Count > 0) w = Inlines.Sum(i => PaintFor(i).MeasureText(i.Plain));
            return new Size(Math.Ceiling(w) + Padding.W, LineStep(p) * Math.Max(1, _lines.Count) + Padding.H);
        }

        protected override Size ArrangeOverride(Size final) => final;

        internal override void Paint(ACanvas c, float k)
        {
            Ink.Rect(c, Background, 0, 0, ActualWidth, ActualHeight);
            var p = PaintFor();
            double step = LineStep(p);
            var fm = p.GetFontMetrics()!;
            double baseLine = Padding.Top + (step - (fm.Descent - fm.Ascent)) / 2 - fm.Ascent;
            if (Inlines.Count > 0)
            {
                double x = Padding.Left;
                foreach (var run in Inlines)
                {
                    var rp = PaintFor(run);
                    c.DrawText(run.Plain, (float)x, (float)baseLine, rp);
                    x += rp.MeasureText(run.Plain);
                }
                return;
            }
            double inner = ActualWidth - Padding.W;
            // 그림자(DropShadowEffect) — 흰 글씨를 그림 위에서도 읽히게 한 점 비켜 까맣게 먼저 찍는다.
            APaint? shadow = null;
            float sd = 0;
            if (Effect is Media.Effects.DropShadowEffect ds)
            {
                shadow = PaintFor();
                shadow.Color = new Android.Graphics.Color(ds.Color.R, ds.Color.G, ds.Color.B, (int)(255 * Math.Clamp(ds.Opacity, 0, 1)));
                sd = (float)Math.Max(1, ds.ShadowDepth);
            }
            for (int i = 0; i < _lines.Count; i++)
            {
                var (t, w) = _lines[i];
                double x = Padding.Left + TextAlignment switch
                {
                    TextAlignment.Center => (inner - w) / 2,
                    TextAlignment.Right => inner - w,
                    _ => 0,
                };
                if (shadow != null) c.DrawText(t, (float)x + sd, (float)(baseLine + i * step) + sd, shadow);
                c.DrawText(t, (float)x, (float)(baseLine + i * step), p);
            }
        }

        internal override bool HitSelf(Point p) => Background != null || Text.Length > 0;
    }

    public class AccessText : TextBlock { }

    /// <summary>글 칸 — 보여 주기는 글 줄과 같고, 고치기는 누르면 기기 글자판 창이 뜬다.</summary>
    public class TextBox : Primitives.TextBoxBase
    {
        private readonly TextBlock _shown = new();
        public TextBox()
        {
            Adopt(_shown);
            Padding = new Thickness(2);
            BorderThickness = new Thickness(1);
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3));
            Background = Brushes.White;
        }
        public string Text
        {
            get => _shown.Text;
            set { if (_shown.Text == value) return; _shown.Text = value ?? ""; TextChanged?.Invoke(this, new TextChangedEventArgs()); }
        }
        public int MaxLength { get; set; }
        public bool IsReadOnly { get; set; }
        public bool AcceptsReturn { get; set; }
        public bool AcceptsTab { get; set; }
        public TextWrapping TextWrapping { get => _shown.TextWrapping; set => _shown.TextWrapping = value; }
        public TextAlignment TextAlignment { get => _shown.TextAlignment; set => _shown.TextAlignment = value; }
        public ScrollBarVisibility VerticalScrollBarVisibility { get; set; }
        public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; }
        public Brush? CaretBrush { get; set; }
        public Brush? SelectionBrush { get; set; }
        public event TextChangedEventHandler? TextChanged;
        public void SelectAll() { }
        public void Clear() => Text = "";
        public void Select(int start, int length) { }
        public void ScrollToEnd() { }
        public void ScrollToHome() { }
        public int CaretIndex { get; set; }
        public string SelectedText { get; set; } = "";

        internal override IEnumerable<UIElement> VisualChildren => [_shown];

        // 넘치는 글은 끌어 굴린다.
        private double _scroll, _grab0, _scroll0;
        private bool _grabbing, _dragged;

        protected override Size MeasureOverride(Size available)
        {
            _shown.Foreground = Foreground; _shown.FontSize = FontSize; _shown.FontWeight = FontWeight;
            var t = BorderThickness; var p = Padding;
            _shown.Measure(new Size(Math.Max(0, available.Width - t.W - p.W), Math.Max(0, available.Height - t.H - p.H)));
            // 글 칸은 속에 굴림판이 있어 받은 높이보다 크게 바라지 않는다(WPF TextBox 와 같다).
            return new Size(_shown.DesiredSize.Width + t.W + p.W, Math.Min(available.Height, _shown.DesiredSize.Height + t.H + p.H));
        }

        protected override Size ArrangeOverride(Size final)
        {
            var t = BorderThickness; var p = Padding;
            double inner = Math.Max(0, final.Height - t.H - p.H);
            _shown.Measure(new Size(Math.Max(0, final.Width - t.W - p.W), double.PositiveInfinity));
            double tall = Math.Max(inner, _shown.DesiredSize.Height);
            _scroll = Math.Clamp(_scroll, 0, tall - inner);
            _shown.Arrange(new Rect(t.Left + p.Left, t.Top + p.Top - _scroll, Math.Max(0, final.Width - t.W - p.W), tall));
            ClipToBounds = true;
            return final;
        }

        internal override bool HitSelf(Point p) => true;

        internal override void OnRaised(string name, RoutedEventArgs e)
        {
            base.OnRaised(name, e);
            if (e is System.Windows.Input.MouseEventArgs me)
            {
                if (name == "MouseLeftButtonDown") { _grabbing = true; _dragged = false; _grab0 = me.WindowPoint.Y; _scroll0 = _scroll; CaptureMouse(); }
                else if (name == "MouseMove" && _grabbing && Math.Abs(me.WindowPoint.Y - _grab0) > 6)
                { _dragged = true; _scroll = _scroll0 - (me.WindowPoint.Y - _grab0); WpfHost.Touch(); }
                else if (name == "MouseLeftButtonUp") { _grabbing = false; ReleaseMouseCapture(); if (_dragged) return; }
            }
            if (name != "MouseLeftButtonUp" || IsReadOnly || !IsEnabled) return;
            e.Handled = true;
            Focus();
            var owner = Window.GetWindow(this) ?? Window.Screen;
            if (CdsHelper.Game.UI.Views.TextInputDialog.Ask(owner, Text, MaxLength > 0 ? MaxLength : 200) is { } typed) Text = typed;
        }
    }

    public delegate void TextChangedEventHandler(object sender, TextChangedEventArgs e);
    public sealed class TextChangedEventArgs : RoutedEventArgs { }

    public class PasswordBox : Control
    {
        public string Password { get; set; } = "";
        public void Clear() => Password = "";
    }

    public class ScrollViewer : ContentControl
    {
        public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.RegisterAttached("VerticalScrollBarVisibility", typeof(ScrollBarVisibility), typeof(ScrollViewer));
        public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty = DependencyProperty.RegisterAttached("HorizontalScrollBarVisibility", typeof(ScrollBarVisibility), typeof(ScrollViewer));
        /// <summary>보이는 폭.</summary>
        public double ViewportWidth => _viewportW > 0 ? _viewportW : ActualWidth;
        private double _offset, _extent, _viewport;
        // 가로 — 가로 스크롤을 켠 칸(HorizontalScrollBarVisibility 가 Disabled 가 아님)만 안을 넓게 재고 옆으로 민다.
        private double _hoffset, _extentW, _viewportW;
        public double ExtentWidth => _extentW;
        public double ScrollableWidth => Math.Max(0, _extentW - _viewportW);
        private bool Sideways => HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
        public ScrollBarVisibility VerticalScrollBarVisibility { get; set; } = ScrollBarVisibility.Visible;
        public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; } = ScrollBarVisibility.Disabled;
        public double VerticalOffset => _offset;
        public double HorizontalOffset => _hoffset;
        public double ExtentHeight => _extent;
        public double ViewportHeight => _viewport;
        public double ScrollableHeight => Math.Max(0, _extent - _viewport);
        public bool CanContentScroll { get; set; }
        public event ScrollChangedEventHandler? ScrollChanged;

        public ScrollViewer() { ClipToBounds = true; }

        /// <summary>
        /// 안의 것 사이 빈 곳을 눌러도 이 칸이 손을 받는다 — 안 그러면 그림 · 글 사이 빈칸에서 손가락으로 끌 때 굴지 않았다
        /// (초상화 도감, 도시 · 문화권 · 왕국의 인물 칸에서 「될 때도 안 될 때도」).
        /// </summary>
        internal override bool HitSelf(Point p) => ScrollableHeight > 0 || ScrollableWidth > 0;

        public void ScrollToVerticalOffset(double at)
        {
            double was = _offset;
            _offset = Math.Clamp(at, 0, ScrollableHeight);
            if (_offset != was) { WpfHost.Touch(); WpfHost.Later(() => ScrollChanged?.Invoke(this, new ScrollChangedEventArgs())); }
        }

        /// <summary>그 요소가 보이는 자리까지 굴린다(BringIntoView).</summary>
        internal void Reveal(UIElement e)
        {
            WpfHost.LayoutNow(render: false);
            double top = e.ToWindow(new Point(0, 0), false).Y - ToWindow(new Point(0, 0), false).Y + _offset;
            double bottom = top + e.RenderSize.Height;
            if (top < _offset) ScrollToVerticalOffset(top);
            else if (bottom > _offset + _viewport) ScrollToVerticalOffset(bottom - _viewport);
        }

        public void ScrollToTop() => ScrollToVerticalOffset(0);
        public void ScrollToEnd() => ScrollToVerticalOffset(double.MaxValue);
        public void ScrollToBottom() => ScrollToEnd();
        public void LineUp() => ScrollToVerticalOffset(_offset - 16);
        public void LineDown() => ScrollToVerticalOffset(_offset + 16);
        public void PageUp() => ScrollToVerticalOffset(_offset - _viewport);
        public void PageDown() => ScrollToVerticalOffset(_offset + _viewport);
        public void ScrollToHorizontalOffset(double at)
        {
            double was = _hoffset;
            _hoffset = Math.Clamp(at, 0, ScrollableWidth);
            if (_hoffset != was) { WpfHost.Touch(); WpfHost.Later(() => ScrollChanged?.Invoke(this, new ScrollChangedEventArgs())); }
        }
        public void ScrollToLeftEnd() => ScrollToHorizontalOffset(0);
        public void ScrollToRightEnd() => ScrollToHorizontalOffset(double.MaxValue);

        private UIElement? Inner => VisualChildren.FirstOrDefault();

        protected override Size MeasureOverride(Size available)
        {
            if (Inner is not { } child) return new Size(0, 0);
            child.Measure(new Size(Sideways ? double.PositiveInfinity : available.Width, double.PositiveInfinity));
            var d = child.DesiredSize;
            return new Size(Math.Min(d.Width, available.Width), Math.Min(d.Height, available.Height));
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (Inner is not { } child) return final;
            double was = _extent + _viewport * 7919;
            _extent = child.DesiredSize.Height;
            _viewport = final.Height;
            _offset = Math.Clamp(_offset, 0, ScrollableHeight);
            _extentW = Sideways ? Math.Max(final.Width, child.DesiredSize.Width) : final.Width;
            _viewportW = final.Width;
            _hoffset = Math.Clamp(_hoffset, 0, ScrollableWidth);
            child.Arrange(new Rect(-_hoffset, -_offset, _extentW, Math.Max(final.Height, _extent)));
            if (was != _extent + _viewport * 7919) WpfHost.Later(() => ScrollChanged?.Invoke(this, new ScrollChangedEventArgs()));
            return final;
        }

        private double? _grabY;
        private double _grabX;
        private double _grabOffset, _grabH;
        /// <summary>끄는 방향 — 0 아직 모름, 1 위아래, 2 옆. 처음 크게 움직인 쪽으로 정해 한 번 끄는 동안 바꾸지 않는다.</summary>
        private int _axis;

        internal override void OnRaised(string name, RoutedEventArgs e)
        {
            if (name == "MouseWheel" && e is System.Windows.Input.MouseWheelEventArgs w && !e.Handled)
            {
                ScrollToVerticalOffset(_offset - w.Delta / 120.0 * 48);
                e.Handled = true;
            }
            // 손가락으로 끌어 굴린다(안드로이드) — 안의 것이 누름을 안 먹었을 때만.
            else if (name == "PreviewMouseLeftButtonDown" && e is System.Windows.Input.MouseEventArgs d && (ScrollableHeight > 0 || ScrollableWidth > 0))
            { _grabY = d.WindowPoint.Y; _grabX = d.WindowPoint.X; _grabOffset = _offset; _grabH = _hoffset; _axis = 0; }
            else if (name == "PreviewMouseMove" && e is System.Windows.Input.MouseEventArgs m && _grabY is { } y0
                     && System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                double dy = m.WindowPoint.Y - y0, dx = m.WindowPoint.X - _grabX;
                if (_axis == 0 && Math.Max(Math.Abs(dx), Math.Abs(dy)) > 6)
                    _axis = Math.Abs(dx) > Math.Abs(dy) && ScrollableWidth > 0 ? 2 : ScrollableHeight > 0 ? 1 : 2;
                if (_axis == 1) ScrollToVerticalOffset(_grabOffset - dy);
                else if (_axis == 2) ScrollToHorizontalOffset(_grabH - dx);
            }
            else if (name == "PreviewMouseLeftButtonUp") _grabY = null;
        }
    }

    public delegate void ScrollChangedEventHandler(object sender, ScrollChangedEventArgs e);
    public sealed class ScrollChangedEventArgs : RoutedEventArgs
    {
        public double VerticalChange { get; init; }
        public double ExtentHeightChange { get; init; }
    }

    public class ItemCollection : List<object>
    {
        public List<System.ComponentModel.SortDescription> SortDescriptions { get; } = [];
        internal Action? Changed;
        public new void Add(object item) { base.Add(item); Changed?.Invoke(); }
        public new void Clear() { base.Clear(); Changed?.Invoke(); }
        public new void Insert(int i, object item) { base.Insert(i, item); Changed?.Invoke(); }
        public new bool Remove(object item) { bool r = base.Remove(item); Changed?.Invoke(); return r; }
        public new void RemoveAt(int i) { base.RemoveAt(i); Changed?.Invoke(); }
        public void Refresh() => Changed?.Invoke();
    }

    public class ItemsControl : Control
    {
        private readonly StackPanel _stack = new();
        /// <summary>목록 상자(ListBox)는 WPF 처럼 제 안에 스크롤 칸을 둔다 — 줄이 칸보다 많으면 손가락으로 굴린다.</summary>
        private ScrollViewer? _scroller;
        protected ItemsControl()
        {
            Items = new ItemCollection { Changed = Rebuild };
            Adopt(_stack);
        }

        /// <summary>줄 판을 스크롤 칸에 담는다(ListBox · ListView 따위가 생성자에서 부른다).</summary>
        protected void ScrollsItself()
        {
            if (_scroller != null) return;
            _scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _scroller.Content = _stack;
            Adopt(_scroller);
        }

        private UIElement Body => _scroller ?? (UIElement)_stack;
        public ItemCollection Items { get; }
        private IEnumerable? _source;
        public IEnumerable? ItemsSource { get => _source; set { _source = value; Rebuild(); } }
        public DataTemplate? ItemTemplate { get; set; }
        public string DisplayMemberPath { get; set; } = "";
        internal StackPanel Stack => _stack;

        public ItemsPanelTemplate? ItemsPanel { get; set; }

        internal virtual UIElement Wrap(object item, int index) =>
            item as UIElement ?? Templated(item) ?? new TextBlock { Text = item?.ToString() ?? "", Foreground = Foreground, FontSize = FontSize };

        /// <summary>줄 틀(ItemTemplate)이 있으면 그 틀로 그 줄을 찍어 낸다.</summary>
        internal UIElement? Templated(object? item) =>
            ItemTemplate?.VisualTree is { } tree ? TemplateBuilder.Build(tree, item) as UIElement : null;

        protected void Rebuild()
        {
            _stack.Children.Clear();
            int i = 0;
            foreach (var item in _source?.Cast<object>() ?? Items) _stack.Children.Add(Wrap(item, i++));
        }

        internal IEnumerable<object> AllItems
        {
            get
            {
                var all = _source?.Cast<object>() ?? Items;
                if (Items.SortDescriptions.Count == 0) return all;
                IOrderedEnumerable<object>? sorted = null;
                foreach (var d in Items.SortDescriptions)
                {
                    object? Key(object o) => o.GetType().GetProperty(d.PropertyName)?.GetValue(o);
                    bool down = d.Direction == System.ComponentModel.ListSortDirection.Descending;
                    sorted = sorted == null
                        ? (down ? all.OrderByDescending(Key) : all.OrderBy(Key))
                        : (down ? sorted.ThenByDescending(Key) : sorted.ThenBy(Key));
                }
                return sorted!.ToList();
            }
        }

        internal override IEnumerable<UIElement> VisualChildren => [Body];

        protected override Size MeasureOverride(Size available)
        {
            var t = BorderThickness; var p = Padding;
            double h = _scroller != null ? Math.Max(0, available.Height - t.H - p.H) : double.PositiveInfinity;
            Body.Measure(new Size(Math.Max(0, available.Width - t.W - p.W), h));
            return new Size(Body.DesiredSize.Width + t.W + p.W, Math.Min(available.Height, Body.DesiredSize.Height + t.H + p.H));
        }

        protected override Size ArrangeOverride(Size final)
        {
            var t = BorderThickness; var p = Padding;
            double h = _scroller != null ? Math.Max(0, final.Height - t.H - p.H) : _stack.DesiredSize.Height;
            Body.Arrange(new Rect(t.Left + p.Left, t.Top + p.Top, Math.Max(0, final.Width - t.W - p.W), h));
            return final;
        }
    }

    public class ListBoxItem : ContentControl
    {
        public bool IsSelected { get; set; }
    }

    public class ListBox : Primitives.Selector
    {
        public ListBox() { ScrollsItself(); }
        public SelectionMode SelectionMode { get; set; }
        public void ScrollIntoView(object item)
        {
            int at = AllItems.ToList().IndexOf(item);
            if (at >= 0 && at < Stack.Children.Count) (Stack.Children[at] as FrameworkElement)?.BringIntoView();
        }
    }

    public class ComboBoxItem : ListBoxItem { }

    /// <summary>펼침 목록 — 누르면 원본 고르기 창으로 줄을 고른다.</summary>
    public class ComboBox : Primitives.Selector
    {
        private readonly TextBlock _shown = new() { Margin = new Thickness(3, 1, 18, 1) };
        public ComboBox()
        {
            Adopt(_shown);
            Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xE5));
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xAC, 0xAC, 0xAC));
            BorderThickness = new Thickness(1);
            SelectionChanged += (_, _) => _shown.Text = Text;
        }
        public bool IsEditable { get; set; }
        public bool IsDropDownOpen { get; set; }
        public bool IsTextSearchEnabled { get; set; }
        public bool StaysOpenOnEdit { get; set; }
        public bool IsReadOnly { get; set; }
        public double MaxDropDownHeight { get; set; }
        private string _typed = "";
        public string Text
        {
            get => SelectedItem is { } item ? Label(item) : _typed;
            set
            {
                int i = AllItems.Select(Label).ToList().IndexOf(value);
                _typed = value;
                if (i >= 0) SelectedIndex = i; else _shown.Text = value;
            }
        }
        public event EventHandler? DropDownOpened, DropDownClosed;

        private static string Label(object item) => item switch
        {
            ContentControl c => c.Content?.ToString() ?? "",
            _ => item?.ToString() ?? "",
        };

        internal override IEnumerable<UIElement> VisualChildren => [_shown];

        protected override Size MeasureOverride(Size available)
        {
            _shown.Text = Text; _shown.Foreground = Foreground; _shown.FontSize = FontSize;
            _shown.Measure(available);
            double w = Math.Max(_shown.DesiredSize.Width, AllItems.Select(i => Label(i).Length * FontSize * 0.6).DefaultIfEmpty(0).Max() + 24);
            return new Size(w + BorderThickness.W, _shown.DesiredSize.Height + BorderThickness.H);
        }

        protected override Size ArrangeOverride(Size final)
        {
            _shown.Arrange(new Rect(BorderThickness.Left, BorderThickness.Top, Math.Max(0, final.Width - BorderThickness.W), Math.Max(0, final.Height - BorderThickness.H)));
            return final;
        }

        internal override void Paint(ACanvas c, float k)
        {
            base.Paint(c, k);
            // 오른쪽 끝 ▼
            var p = new APaint { AntiAlias = true, Color = Android.Graphics.Color.Black };
            var path = new Android.Graphics.Path();
            float x = (float)ActualWidth - 12, y = (float)ActualHeight / 2 - 2;
            path.MoveTo(x, y); path.LineTo(x + 7, y); path.LineTo(x + 3.5f, y + 4); path.Close();
            c.DrawPath(path, p);
        }

        internal override bool HitSelf(Point p) => true;

        internal override void OnRaised(string name, RoutedEventArgs e)
        {
            if (name != "MouseLeftButtonUp" || !IsEnabled) return;
            e.Handled = true;
            var items = AllItems.ToList();
            if (items.Count == 0) return;
            DropDownOpened?.Invoke(this, EventArgs.Empty);
            var owner = Window.GetWindow(this) ?? Window.Screen;
            int pick = CdsHelper.Game.UI.Views.ChoiceDialog.Pick(owner, "", items.Select(i => (Label(i), !(i is UIElement u && !u.IsEnabled))).ToArray());
            if (pick >= 0) SelectedIndex = pick;
            DropDownClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    public class ContextMenu : ItemsControl
    {
        public Primitives.PlacementMode Placement { get; set; }
        public bool IsOpen { get; set; }
        public UIElement? PlacementTarget { get; set; }
    }

    public class MenuItem : ContentControl
    {
        public object? Header { get; set; }
        public event RoutedEventHandler? Click;
        internal void Fire() => Click?.Invoke(this, new RoutedEventArgs());
    }

    public class Separator : Control { }

    public class ToolTip : ContentControl { }

    namespace Primitives
    {
        public class Selector : ItemsControl
        {
            private int _selected = -1;
            public event SelectionChangedEventHandler? SelectionChanged;
            public int SelectedIndex
            {
                get => _selected;
                set
                {
                    if (_selected == value) return;
                    _selected = value;
                    Paint();
                    SelectionChanged?.Invoke(this, new SelectionChangedEventArgs());
                }
            }
            public object? SelectedItem
            {
                get => _selected >= 0 && _selected < AllItems.Count() ? AllItems.ElementAt(_selected) : null;
                set => SelectedIndex = value == null ? -1 : AllItems.ToList().IndexOf(value);
            }

            public Brush SelectedBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF));

            internal override UIElement Wrap(object item, int index)
            {
                object shown = item is UIElement e ? e : Templated(item) is { } made ? made : item?.ToString() ?? "";
                var box = new ListBoxItem { Content = shown, Padding = new Thickness(2, 0, 2, 0), Background = Brushes.Transparent };
                if (box.Content is not UIElement || item is not UIElement) ((ListBoxItem)box).Foreground = Foreground;
                box.MouseLeftButtonUp += (_, _) => SelectedIndex = index;
                return box;
            }

            private void Paint()
            {
                int i = 0;
                foreach (var c in Stack.Children)
                {
                    if (c is ListBoxItem item) { item.IsSelected = i == _selected; item.Background = i == _selected ? SelectedBrush : Brushes.Transparent; }
                    i++;
                }
            }
        }
    }

    public delegate void SelectionChangedEventHandler(object sender, SelectionChangedEventArgs e);
    public sealed class SelectionChangedEventArgs : RoutedEventArgs
    {
        public IList AddedItems { get; init; } = new List<object>();
        public IList RemovedItems { get; init; } = new List<object>();
    }
}

namespace System.Windows.Documents
{
    /// <summary>글 조각의 바탕(<c>TextElement</c>) — 색 · 크기 · 굵기.</summary>
    public abstract class TextElement
    {
        public System.Windows.Media.Brush? Foreground { get; set; }
        public double? FontSize { get; set; }
        public FontWeight? FontWeight { get; set; }
    }

    public abstract class Inline : TextElement
    {
        internal abstract string Plain { get; }
    }

    public sealed class Run : Inline
    {
        public Run() { }
        public Run(string text) { Text = text; }
        public string Text { get; set; } = "";
        internal override string Plain => Text;
    }

    public sealed class InlineUIContainer : Inline
    {
        public InlineUIContainer() { }
        public InlineUIContainer(UIElement child) { Child = child; }
        public UIElement? Child { get; set; }
        public BaselineAlignment BaselineAlignment { get; set; }
        internal override string Plain => "";
    }

    public enum BaselineAlignment { Top, Center, Bottom, Baseline, TextTop, TextBottom, Subscript, Superscript }

    public sealed class LineBreak : Inline { internal override string Plain => "\n"; }

    public sealed class Bold : Inline
    {
        public Bold(Inline inner) { Inner = inner; FontWeight = FontWeights.Bold; }
        private Inline Inner { get; }
        internal override string Plain => Inner.Plain;
    }

    public sealed class InlineCollection : List<Inline>
    {
        private readonly Controls.TextBlock _owner;
        internal InlineCollection(Controls.TextBlock owner) { _owner = owner; }
        public new void Add(Inline i) { base.Add(i); WpfHost.Touch(); }
        public void Add(string text) => Add(new Run(text));
    }
}

namespace System.Windows.Shapes
{
    using System.Windows.Controls;

    public abstract class Shape : FrameworkElement
    {
        private Brush? _fill, _stroke;
        public Brush? Fill { get => _fill; set { if (Equals(_fill, value)) return; _fill = value; WpfHost.Touch(); } }
        public Brush? Stroke { get => _stroke; set { if (Equals(_stroke, value)) return; _stroke = value; WpfHost.Touch(); } }
        public double StrokeThickness { get; set; } = 1;
        public DoubleCollection? StrokeDashArray { get; set; }
        public PenLineCap StrokeStartLineCap { get; set; }
        public PenLineCap StrokeEndLineCap { get; set; }
        public PenLineJoin StrokeLineJoin { get; set; }
        public Stretch Stretch { get; set; }

        protected abstract Android.Graphics.Path Outline();

        internal override void Paint(ACanvas c, float k)
        {
            var path = Outline();
            if (_fill is SolidColorBrush)
            {
                var p = new APaint { AntiAlias = true };
                p.SetStyle(APaint.Style.Fill);
                p.Color = Ink.ColorOf(_fill);
                c.DrawPath(path, p);
            }
            else if (_fill != null)
            {
                var b = new ARectF(); path.ComputeBounds(b, true);
                c.Save(); c.ClipPath(path);
                Ink.Rect(c, _fill, b.Left, b.Top, b.Width(), b.Height());
                c.Restore();
            }
            if (_stroke != null && StrokeThickness > 0)
            {
                var p = new APaint { AntiAlias = true, StrokeWidth = (float)StrokeThickness };
                p.SetStyle(APaint.Style.Stroke);
                p.Color = Ink.ColorOf(_stroke);
                if (StrokeDashArray is { Count: > 1 } dash)
                    p.SetPathEffect(new Android.Graphics.DashPathEffect(dash.Select(d => (float)(d * StrokeThickness)).ToArray(), 0));
                c.DrawPath(path, p);
            }
        }

        internal override bool HitSelf(Point p)
        {
            if (_fill == null && _stroke == null) return false;
            var b = new ARectF(); Outline().ComputeBounds(b, true);
            b.Inset(-2, -2);
            return b.Contains((float)p.X, (float)p.Y);
        }
    }

    public sealed class Rectangle : Shape
    {
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
        protected override Size MeasureOverride(Size available) => new(0, 0);
        protected override Android.Graphics.Path Outline()
        {
            var p = new Android.Graphics.Path();
            double t = Stroke != null ? StrokeThickness / 2 : 0;
            p.AddRect((float)t, (float)t, (float)(ActualWidth - t), (float)(ActualHeight - t), Android.Graphics.Path.Direction.Cw!);
            return p;
        }
    }

    public sealed class Ellipse : Shape
    {
        protected override Size MeasureOverride(Size available) => new(0, 0);
        protected override Android.Graphics.Path Outline()
        {
            var p = new Android.Graphics.Path();
            p.AddOval(0, 0, (float)ActualWidth, (float)ActualHeight, Android.Graphics.Path.Direction.Cw!);
            return p;
        }
    }

    public sealed class Line : Shape
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        protected override Size MeasureOverride(Size available) => new(Math.Max(X1, X2), Math.Max(Y1, Y2));
        protected override Android.Graphics.Path Outline()
        {
            var p = new Android.Graphics.Path();
            p.MoveTo((float)X1, (float)Y1);
            p.LineTo((float)X2, (float)Y2);
            return p;
        }
    }

    public sealed class Path : Shape
    {
        public Geometry? Data { get; set; }
        protected override Size MeasureOverride(Size available)
        {
            if (Data == null) return new Size(0, 0);
            var b = new ARectF(); Data.ToPath().ComputeBounds(b, true);
            return new Size(Math.Max(0, b.Right), Math.Max(0, b.Bottom));
        }
        protected override Android.Graphics.Path Outline() => Data?.ToPath() ?? new Android.Graphics.Path();
    }

    public sealed class Polygon : Shape
    {
        public PointCollection Points { get; set; } = [];
        protected override Size MeasureOverride(Size available) =>
            Points.Count == 0 ? new Size(0, 0) : new Size(Points.Max(p => p.X), Points.Max(p => p.Y));
        protected override Android.Graphics.Path Outline()
        {
            var p = new Android.Graphics.Path();
            for (int i = 0; i < Points.Count; i++)
                if (i == 0) p.MoveTo((float)Points[i].X, (float)Points[i].Y); else p.LineTo((float)Points[i].X, (float)Points[i].Y);
            if (Points.Count > 0) p.Close();
            return p;
        }
    }

    public sealed class Polyline : Shape
    {
        public PointCollection Points { get; set; } = [];
        protected override Size MeasureOverride(Size available) =>
            Points.Count == 0 ? new Size(0, 0) : new Size(Points.Max(p => p.X), Points.Max(p => p.Y));
        protected override Android.Graphics.Path Outline()
        {
            var p = new Android.Graphics.Path();
            for (int i = 0; i < Points.Count; i++)
                if (i == 0) p.MoveTo((float)Points[i].X, (float)Points[i].Y); else p.LineTo((float)Points[i].X, (float)Points[i].Y);
            return p;
        }
    }
}

namespace System.Windows.Controls
{
    /// <summary>
    /// 조절 막대(<c>Slider</c>) — 손가락으로 좁은 막대를 끌기는 어렵다. 숫자 돌리개(<see cref="CdsHelper.Support.UI.Units.NumericSpinner"/>)
    /// 모양으로 낸다: 왼쪽 ◀ 은 한 눈금 내리고, 오른쪽 ▶ 은 올리고, 가운데를 누르면 값을 적는다.
    /// </summary>
    public class Slider : CdsHelper.Support.UI.Units.NumericSpinner
    {
        public Slider() { Minimum = 0; Maximum = 10; }

        /// <summary>한 눈금 — 누를 때마다 이만큼 움직인다.</summary>
        public double TickFrequency { get => Step; set => Step = value; }

        public bool IsSnapToTickEnabled { get; set; }
        public double SmallChange { get => Step; set => Step = value; }
        public double LargeChange { get; set; } = 1;
        public Orientation Orientation { get; set; } = Orientation.Horizontal;
    }
}

namespace System.ComponentModel
{
    /// <summary>
    /// 속성 값 바뀜 지켜보기(<c>DependencyPropertyDescriptor</c>). WpfLite 는 값 바뀜을 따로 알리지 않으므로 거는 일만 받는다.
    /// </summary>
    public sealed class DependencyPropertyDescriptor
    {
        public static DependencyPropertyDescriptor FromProperty(System.Windows.DependencyProperty property, Type owner) => new();
        public void AddValueChanged(object component, EventHandler handler) { }
        public void RemoveValueChanged(object component, EventHandler handler) { }
    }
}
