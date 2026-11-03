using System.Windows.Media.Imaging;
using ABitmap = Android.Graphics.Bitmap;

namespace System.Windows.Media
{
    public struct Color : IEquatable<Color>
    {
        public byte A, R, G, B;
        public static Color FromRgb(byte r, byte g, byte b) => new() { A = 255, R = r, G = g, B = b };
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new() { A = a, R = r, G = g, B = b };
        public bool Equals(Color o) => A == o.A && R == o.R && G == o.G && B == o.B;
        public override bool Equals(object? o) => o is Color c && Equals(c);
        public override int GetHashCode() => Argb;
        public static bool operator ==(Color a, Color b) => a.Equals(b);
        public static bool operator !=(Color a, Color b) => !a.Equals(b);
        internal int Argb => A << 24 | R << 16 | G << 8 | B;
        public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
    }

    public static class Colors
    {
        public static Color Transparent => Color.FromArgb(0, 255, 255, 255);
        public static Color Black => Color.FromRgb(0, 0, 0);
        public static Color White => Color.FromRgb(255, 255, 255);
        public static Color Gray => Color.FromRgb(128, 128, 128);
        public static Color DarkGray => Color.FromRgb(169, 169, 169);
        public static Color LightGray => Color.FromRgb(211, 211, 211);
        public static Color Red => Color.FromRgb(255, 0, 0);
        public static Color Yellow => Color.FromRgb(255, 255, 0);
        public static Color Gold => Color.FromRgb(255, 215, 0);
        public static Color Orange => Color.FromRgb(255, 165, 0);
        public static Color Green => Color.FromRgb(0, 128, 0);
        public static Color Lime => Color.FromRgb(0, 255, 0);
        public static Color Blue => Color.FromRgb(0, 0, 255);
        public static Color Cyan => Color.FromRgb(0, 255, 255);
        public static Color Silver => Color.FromRgb(192, 192, 192);
        public static Color Maroon => Color.FromRgb(128, 0, 0);
        public static Color Navy => Color.FromRgb(0, 0, 128);
    }

    public abstract class Brush : Freezable
    {
        private double _opacity = 1;
        public double Opacity { get => _opacity; set { _opacity = value; WpfHost.Touch(); } }
        public Transform? Transform { get; set; }
        public Transform? RelativeTransform { get; set; }
        public Brush Clone() => (Brush)MemberwiseClone();
        public Brush CloneCurrentValue() => Clone();
    }

    public sealed class SolidColorBrush : Brush
    {
        private Color _color;
        public static readonly DependencyProperty ColorProperty = DependencyProperty.Register("Color", typeof(Color), typeof(SolidColorBrush));
        public SolidColorBrush() { }
        public SolidColorBrush(Color color) { _color = color; }
        public Color Color { get => _color; set { _color = value; WpfHost.Touch(); } }

        public override void SetValue(DependencyProperty p, object? value)
        {
            if (ReferenceEquals(p, ColorProperty) && value is Color c) Color = c;
            else base.SetValue(p, value);
        }

        public new SolidColorBrush Clone() => (SolidColorBrush)MemberwiseClone();
    }

    public enum Stretch { None, Fill, Uniform, UniformToFill }
    public enum TileMode { None, Tile, FlipX, FlipY, FlipXY }
    public enum AlignmentX { Left, Center, Right }
    public enum AlignmentY { Top, Center, Bottom }
    public enum BrushMappingMode { Absolute, RelativeToBoundingBox }
    public enum BitmapScalingMode { Unspecified, LowQuality, HighQuality, Linear = 1, Fant = 2, NearestNeighbor = 3 }
    public enum EdgeMode { Unspecified, Aliased }
    public enum PenLineCap { Flat, Square, Round, Triangle }
    public enum PenLineJoin { Miter, Bevel, Round }
    public enum FillRule { EvenOdd, Nonzero }
    public enum SweepDirection { Counterclockwise, Clockwise }
    public enum ClearTypeHint { Auto, Enabled }
    public enum TextRenderingMode { Auto, Aliased, Grayscale, ClearType }
    public enum TextFormattingMode { Ideal, Display }

    public abstract class TileBrush : Brush
    {
        public Stretch Stretch { get; set; } = Stretch.Fill;
        public TileMode TileMode { get; set; }
        public AlignmentX AlignmentX { get; set; } = AlignmentX.Center;
        public AlignmentY AlignmentY { get; set; } = AlignmentY.Center;
        public Rect Viewport { get; set; } = new(0, 0, 1, 1);
        public BrushMappingMode ViewportUnits { get; set; } = BrushMappingMode.RelativeToBoundingBox;
        public Rect Viewbox { get; set; } = new(0, 0, 1, 1);
        public BrushMappingMode ViewboxUnits { get; set; } = BrushMappingMode.RelativeToBoundingBox;
    }

    public sealed class ImageBrush : TileBrush
    {
        public ImageBrush() { }
        public ImageBrush(ImageSource? source) { ImageSource = source; }
        public ImageSource? ImageSource { get; set; }
    }

    public sealed class GradientStop
    {
        public GradientStop() { }
        public GradientStop(Color color, double offset) { Color = color; Offset = offset; }
        public Color Color { get; set; }
        public double Offset { get; set; }
    }

    public sealed class GradientStopCollection : List<GradientStop> { }

    public sealed class LinearGradientBrush : Brush
    {
        public LinearGradientBrush() { }
        public LinearGradientBrush(Color a, Color b, double angle) { GradientStops.Add(new(a, 0)); GradientStops.Add(new(b, 1)); EndPoint = angle == 90 ? new Point(0, 1) : new Point(1, 0); }
        public LinearGradientBrush(Color a, Color b, Point start, Point end) { GradientStops.Add(new(a, 0)); GradientStops.Add(new(b, 1)); StartPoint = start; EndPoint = end; }
        public GradientStopCollection GradientStops { get; set; } = [];
        public Point StartPoint { get; set; } = new(0, 0);
        public Point EndPoint { get; set; } = new(1, 1);
    }

    public static class Brushes
    {
        private static SolidColorBrush Of(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static readonly SolidColorBrush Transparent = Of(Colors.Transparent);
        public static readonly SolidColorBrush Black = Of(Colors.Black);
        public static readonly SolidColorBrush White = Of(Colors.White);
        public static readonly SolidColorBrush Gray = Of(Colors.Gray);
        public static readonly SolidColorBrush DarkGray = Of(Colors.DarkGray);
        public static readonly SolidColorBrush LightGray = Of(Colors.LightGray);
        public static readonly SolidColorBrush DimGray = Of(Color.FromRgb(105, 105, 105));
        public static readonly SolidColorBrush Red = Of(Colors.Red);
        public static readonly SolidColorBrush DarkRed = Of(Color.FromRgb(139, 0, 0));
        public static readonly SolidColorBrush Yellow = Of(Colors.Yellow);
        public static readonly SolidColorBrush Gold = Of(Colors.Gold);
        public static readonly SolidColorBrush Orange = Of(Colors.Orange);
        public static readonly SolidColorBrush Green = Of(Colors.Green);
        public static readonly SolidColorBrush LimeGreen = Of(Color.FromRgb(50, 205, 50));
        public static readonly SolidColorBrush Lime = Of(Colors.Lime);
        public static readonly SolidColorBrush Blue = Of(Colors.Blue);
        public static readonly SolidColorBrush Cyan = Of(Colors.Cyan);
        public static readonly SolidColorBrush Silver = Of(Colors.Silver);
        public static readonly SolidColorBrush Maroon = Of(Colors.Maroon);
        public static readonly SolidColorBrush Navy = Of(Colors.Navy);
        public static readonly SolidColorBrush Khaki = Of(Color.FromRgb(240, 230, 140));
        public static readonly SolidColorBrush Wheat = Of(Color.FromRgb(245, 222, 179));
        public static readonly SolidColorBrush Beige = Of(Color.FromRgb(245, 245, 220));
        public static readonly SolidColorBrush SaddleBrown = Of(Color.FromRgb(139, 69, 19));
        public static readonly SolidColorBrush Brown = Of(Color.FromRgb(165, 42, 42));
        public static readonly SolidColorBrush Ivory = Of(Color.FromRgb(255, 255, 240));
        public static readonly SolidColorBrush LightYellow = Of(Color.FromRgb(255, 255, 224));
        public static readonly SolidColorBrush SeaGreen = Of(Color.FromRgb(46, 139, 87));
        public static readonly SolidColorBrush OrangeRed = Of(Color.FromRgb(255, 69, 0));
        public static readonly SolidColorBrush WhiteSmoke = Of(Color.FromRgb(245, 245, 245));
        public static readonly SolidColorBrush Gainsboro = Of(Color.FromRgb(220, 220, 220));
        public static readonly SolidColorBrush DarkGreen = Of(Color.FromRgb(0, 100, 0));
        public static readonly SolidColorBrush DarkBlue = Of(Color.FromRgb(0, 0, 139));
        public static readonly SolidColorBrush DarkOrange = Of(Color.FromRgb(255, 140, 0));
        public static readonly SolidColorBrush Goldenrod = Of(Color.FromRgb(218, 165, 32));
        public static readonly SolidColorBrush DarkGoldenrod = Of(Color.FromRgb(184, 134, 11));
        public static readonly SolidColorBrush Crimson = Of(Color.FromRgb(220, 20, 60));
        public static readonly SolidColorBrush Tomato = Of(Color.FromRgb(255, 99, 71));
        public static readonly SolidColorBrush SkyBlue = Of(Color.FromRgb(135, 206, 235));
        public static readonly SolidColorBrush LightBlue = Of(Color.FromRgb(173, 216, 230));
        public static readonly SolidColorBrush LightGreen = Of(Color.FromRgb(144, 238, 144));
        public static readonly SolidColorBrush Pink = Of(Color.FromRgb(255, 192, 203));
        public static readonly SolidColorBrush Purple = Of(Color.FromRgb(128, 0, 128));
        public static readonly SolidColorBrush Tan = Of(Color.FromRgb(210, 180, 140));
        public static readonly SolidColorBrush Bisque = Of(Color.FromRgb(255, 228, 196));
        public static readonly SolidColorBrush Moccasin = Of(Color.FromRgb(255, 228, 181));
        public static readonly SolidColorBrush PaleGoldenrod = Of(Color.FromRgb(238, 232, 170));
        public static readonly SolidColorBrush Linen = Of(Color.FromRgb(250, 240, 230));
        public static readonly SolidColorBrush Sienna = Of(Color.FromRgb(160, 82, 45));
        public static readonly SolidColorBrush Peru = Of(Color.FromRgb(205, 133, 63));
        public static readonly SolidColorBrush SlateGray = Of(Color.FromRgb(112, 128, 144));
        public static readonly SolidColorBrush SteelBlue = Of(Color.FromRgb(70, 130, 180));
        public static readonly SolidColorBrush Teal = Of(Color.FromRgb(0, 128, 128));
        public static readonly SolidColorBrush Olive = Of(Color.FromRgb(128, 128, 0));
        public static readonly SolidColorBrush Firebrick = Of(Color.FromRgb(178, 34, 34));
        public static readonly SolidColorBrush ForestGreen = Of(Color.FromRgb(34, 139, 34));
        public static readonly SolidColorBrush MidnightBlue = Of(Color.FromRgb(25, 25, 112));
        public static readonly SolidColorBrush Coral = Of(Color.FromRgb(255, 127, 80));
        public static readonly SolidColorBrush Salmon = Of(Color.FromRgb(250, 128, 114));
        public static readonly SolidColorBrush Chocolate = Of(Color.FromRgb(210, 105, 30));
        public static readonly SolidColorBrush Magenta = Of(Color.FromRgb(255, 0, 255));
        public static readonly SolidColorBrush Violet = Of(Color.FromRgb(238, 130, 238));
        public static readonly SolidColorBrush Aqua = Of(Color.FromRgb(0, 255, 255));
        public static readonly SolidColorBrush GreenYellow = Of(Color.FromRgb(173, 255, 47));
        public static readonly SolidColorBrush YellowGreen = Of(Color.FromRgb(154, 205, 50));
        public static readonly SolidColorBrush Cornsilk = Of(Color.FromRgb(255, 248, 220));
        public static readonly SolidColorBrush AntiqueWhite = Of(Color.FromRgb(250, 235, 215));
        public static readonly SolidColorBrush DarkSlateGray = Of(Color.FromRgb(47, 79, 79));
        public static readonly SolidColorBrush RosyBrown = Of(Color.FromRgb(188, 143, 143));
        public static readonly SolidColorBrush IndianRed = Of(Color.FromRgb(205, 92, 92));
    }

    public sealed class BrushConverter
    {
        public object? ConvertFromString(string text) => new SolidColorBrush(ColorConverter.Parse(text));
    }

    public static class ColorConverter
    {
        public static object ConvertFromString(string text) => Parse(text);
        internal static Color Parse(string text)
        {
            string h = text.TrimStart('#');
            uint v = Convert.ToUInt32(h, 16);
            return h.Length <= 6 ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
                                 : Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
    }

    public sealed class Pen
    {
        public Pen() { }
        public Pen(Brush brush, double thickness) { Brush = brush; Thickness = thickness; }
        public Brush? Brush { get; set; }
        public double Thickness { get; set; } = 1;
        public void Freeze() { }
    }

    // ── 바꿈 ──

    public abstract class Transform : GeneralTransform
    {
        internal abstract Android.Graphics.Matrix Matrix(double w, double h);
        public static Transform Identity => new TranslateTransform();
        public override bool TryTransform(Point p, out Point r)
        {
            var pts = new float[] { (float)p.X, (float)p.Y };
            Matrix(0, 0).MapPoints(pts);
            r = new Point(pts[0], pts[1]);
            return true;
        }
        public GeneralTransform? Inverse => null;
    }

    public sealed class ScaleTransform : Transform
    {
        public static readonly DependencyProperty ScaleXProperty = DependencyProperty.Register("ScaleX", typeof(double), typeof(ScaleTransform));
        public static readonly DependencyProperty ScaleYProperty = DependencyProperty.Register("ScaleY", typeof(double), typeof(ScaleTransform));
        private double _x = 1, _y = 1;
        public ScaleTransform() { }
        public ScaleTransform(double x, double y) { _x = x; _y = y; }
        public ScaleTransform(double x, double y, double cx, double cy) { _x = x; _y = y; CenterX = cx; CenterY = cy; }
        public double ScaleX { get => _x; set { _x = value; WpfHost.Touch(); } }
        public double ScaleY { get => _y; set { _y = value; WpfHost.Touch(); } }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        internal override Android.Graphics.Matrix Matrix(double w, double h)
        {
            var m = new Android.Graphics.Matrix();
            m.SetScale((float)_x, (float)_y, (float)CenterX, (float)CenterY);
            return m;
        }
        public override void SetValue(DependencyProperty p, object? value)
        {
            if (ReferenceEquals(p, ScaleXProperty) && value is double x) ScaleX = x;
            else if (ReferenceEquals(p, ScaleYProperty) && value is double y) ScaleY = y;
        }
    }

    public sealed class TranslateTransform : Transform
    {
        public static readonly DependencyProperty XProperty = DependencyProperty.Register("X", typeof(double), typeof(TranslateTransform));
        public static readonly DependencyProperty YProperty = DependencyProperty.Register("Y", typeof(double), typeof(TranslateTransform));
        private double _x, _y;
        public TranslateTransform() { }
        public TranslateTransform(double x, double y) { _x = x; _y = y; }
        public double X { get => _x; set { _x = value; WpfHost.Touch(); } }
        public double Y { get => _y; set { _y = value; WpfHost.Touch(); } }
        internal override Android.Graphics.Matrix Matrix(double w, double h)
        {
            var m = new Android.Graphics.Matrix();
            m.SetTranslate((float)_x, (float)_y);
            return m;
        }
        public override void SetValue(DependencyProperty p, object? value)
        {
            if (ReferenceEquals(p, XProperty) && value is double x) X = x;
            else if (ReferenceEquals(p, YProperty) && value is double y) Y = y;
        }
    }

    public sealed class RotateTransform : Transform
    {
        public static readonly DependencyProperty AngleProperty = DependencyProperty.Register("Angle", typeof(double), typeof(RotateTransform));
        private double _a;
        public RotateTransform() { }
        public RotateTransform(double angle) { _a = angle; }
        public RotateTransform(double angle, double cx, double cy) { _a = angle; CenterX = cx; CenterY = cy; }
        public double Angle { get => _a; set { _a = value; WpfHost.Touch(); } }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        internal override Android.Graphics.Matrix Matrix(double w, double h)
        {
            var m = new Android.Graphics.Matrix();
            m.SetRotate((float)_a, (float)CenterX, (float)CenterY);
            return m;
        }
        public override void SetValue(DependencyProperty p, object? value)
        {
            if (ReferenceEquals(p, AngleProperty) && value is double a) Angle = a;
        }
    }

    public sealed class TransformGroup : Transform
    {
        public List<Transform> Children { get; set; } = [];
        internal override Android.Graphics.Matrix Matrix(double w, double h)
        {
            var m = new Android.Graphics.Matrix();
            foreach (var t in Children) m.PostConcat(t.Matrix(w, h));
            return m;
        }
    }

    public sealed class MatrixTransform : Transform
    {
        internal override Android.Graphics.Matrix Matrix(double w, double h) => new();
    }

    // ── 그림 ──

    public abstract class ImageSource : Freezable
    {
        public abstract double Width { get; }
        public abstract double Height { get; }
        internal abstract ABitmap? Bitmap { get; }
    }

    public static class PixelFormats
    {
        public static readonly PixelFormat Bgra32 = new(32, false);
        public static readonly PixelFormat Pbgra32 = new(32, true);
        public static readonly PixelFormat Bgr32 = new(32, false, opaque: true);
        public static readonly PixelFormat Bgr24 = new(24, false, opaque: true);
        public static readonly PixelFormat Indexed8 = new(8, false);
        public static readonly PixelFormat Gray8 = new(8, false, gray: true);
    }

    public readonly struct PixelFormat
    {
        internal readonly int Bits;
        internal readonly bool Premultiplied, Opaque, Gray;
        internal PixelFormat(int bits, bool pre, bool opaque = false, bool gray = false) { Bits = bits; Premultiplied = pre; Opaque = opaque; Gray = gray; }
        public int BitsPerPixel => Bits;
        public static bool operator ==(PixelFormat a, PixelFormat b) => a.Bits == b.Bits && a.Premultiplied == b.Premultiplied && a.Opaque == b.Opaque && a.Gray == b.Gray;
        public static bool operator !=(PixelFormat a, PixelFormat b) => !(a == b);
        public override bool Equals(object? o) => o is PixelFormat p && p == this;
        public override int GetHashCode() => Bits;
    }

    public static class RenderOptions
    {
        public static void SetBitmapScalingMode(DependencyObject target, BitmapScalingMode mode) => target.Put(typeof(BitmapScalingMode), mode);
        public static BitmapScalingMode GetBitmapScalingMode(DependencyObject target) => target.Get(typeof(BitmapScalingMode), BitmapScalingMode.Unspecified);
        public static void SetEdgeMode(DependencyObject target, EdgeMode mode) { }
        public static void SetClearTypeHint(DependencyObject target, ClearTypeHint hint) { }
        public static readonly DependencyProperty BitmapScalingModeProperty = DependencyProperty.RegisterAttached("BitmapScalingMode", typeof(BitmapScalingMode), typeof(RenderOptions));
        public static readonly DependencyProperty EdgeModeProperty = DependencyProperty.RegisterAttached("EdgeMode", typeof(EdgeMode), typeof(RenderOptions));
    }

    public static class TextOptions
    {
        public static void SetTextRenderingMode(DependencyObject target, TextRenderingMode mode) { }
        public static void SetTextFormattingMode(DependencyObject target, TextFormattingMode mode) { }
    }

    public readonly record struct DpiScale(double DpiScaleX, double DpiScaleY)
    {
        public double PixelsPerDip => DpiScaleX;
        public double PixelsPerInchX => 96 * DpiScaleX;
    }

    public static class VisualTreeHelper
    {
        public static DpiScale GetDpi(Visual visual) => new(1, 1);
        public static DependencyObject? GetParent(DependencyObject d) => (d as UIElement)?.VisualParent;
        public static int GetChildrenCount(DependencyObject d) => (d as UIElement)?.VisualChildren.Count() ?? 0;
        public static DependencyObject GetChild(DependencyObject d, int i) => ((UIElement)d).VisualChildren.ElementAt(i);
        public static Rect GetDescendantBounds(Visual v) => new(((UIElement)v).RenderSize);
        public static Vector GetOffset(Visual v) => new(((UIElement)v).Offset.X, ((UIElement)v).Offset.Y);
        public static HitTestResult? HitTest(Visual reference, Point p) =>
            WpfHost.HitTest((UIElement)reference, p) is { } hit ? new HitTestResult(hit) : null;
    }

    public sealed class HitTestResult(DependencyObject hit)
    {
        public DependencyObject VisualHit => hit;
    }

    public sealed class FontFamily
    {
        public FontFamily(string name) { Source = name; }
        public string Source { get; }
    }

    public sealed class Typeface
    {
        public Typeface(string name) { }
    }

    public sealed class PresentationSourceTarget
    {
        public Transform2 TransformFromDevice => new();
        public Transform2 TransformToDevice => new();
    }

    public sealed class Transform2
    {
        public Point Transform(Point p) => p;
        public double M11 => 1;
        public double M22 => 1;
    }

    public static class CompositionTarget
    {
        public static event EventHandler? Rendering
        {
            add => WpfHost.Rendering += value;
            remove => WpfHost.Rendering -= value;
        }
    }

    public sealed class RenderingEventArgs : EventArgs
    {
        public TimeSpan RenderingTime { get; init; }
    }

    // ── 모양(Path 의 Data) ──

    public abstract class Geometry : Freezable
    {
        internal abstract Android.Graphics.Path ToPath();
        public static Geometry Parse(string data) => new PathGeometry { Data = data };
        public static Geometry Empty => new PathGeometry();
        public virtual Rect Bounds => default;
    }

    public sealed class RectangleGeometry : Geometry
    {
        public RectangleGeometry() { }
        public RectangleGeometry(Rect rect) { Rect = rect; }
        public RectangleGeometry(Rect rect, double rx, double ry) { Rect = rect; }
        public Rect Rect { get; set; }
        public override Rect Bounds => Rect;
        internal override Android.Graphics.Path ToPath()
        {
            var p = new Android.Graphics.Path();
            p.AddRect((float)Rect.X, (float)Rect.Y, (float)Rect.Right, (float)Rect.Bottom, Android.Graphics.Path.Direction.Cw!);
            return p;
        }
    }

    public sealed class EllipseGeometry : Geometry
    {
        public EllipseGeometry() { }
        public EllipseGeometry(Point center, double rx, double ry) { Center = center; RadiusX = rx; RadiusY = ry; }
        public EllipseGeometry(Rect r) { Center = new(r.X + r.Width / 2, r.Y + r.Height / 2); RadiusX = r.Width / 2; RadiusY = r.Height / 2; }
        public Point Center { get; set; }
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
        internal override Android.Graphics.Path ToPath()
        {
            var p = new Android.Graphics.Path();
            p.AddOval((float)(Center.X - RadiusX), (float)(Center.Y - RadiusY), (float)(Center.X + RadiusX), (float)(Center.Y + RadiusY), Android.Graphics.Path.Direction.Cw!);
            return p;
        }
    }

    public sealed class LineGeometry : Geometry
    {
        public LineGeometry() { }
        public LineGeometry(Point a, Point b) { StartPoint = a; EndPoint = b; }
        public Point StartPoint { get; set; }
        public Point EndPoint { get; set; }
        internal override Android.Graphics.Path ToPath()
        {
            var p = new Android.Graphics.Path();
            p.MoveTo((float)StartPoint.X, (float)StartPoint.Y);
            p.LineTo((float)EndPoint.X, (float)EndPoint.Y);
            return p;
        }
    }

    public sealed class GeometryGroup : Geometry
    {
        public List<Geometry> Children { get; set; } = [];
        public FillRule FillRule { get; set; }
        internal override Android.Graphics.Path ToPath()
        {
            var p = new Android.Graphics.Path();
            foreach (var g in Children) p.AddPath(g.ToPath());
            return p;
        }
    }

    public sealed class PathFigure
    {
        public PathFigure() { }
        public PathFigure(Point start, IEnumerable<PathSegment> segments, bool closed) { StartPoint = start; Segments.AddRange(segments); IsClosed = closed; }
        public Point StartPoint { get; set; }
        public List<PathSegment> Segments { get; set; } = [];
        public bool IsClosed { get; set; }
        public bool IsFilled { get; set; } = true;
    }

    public abstract class PathSegment { }

    public sealed class LineSegment : PathSegment
    {
        public LineSegment() { }
        public LineSegment(Point p, bool stroked) { Point = p; }
        public Point Point { get; set; }
    }

    public sealed class PolyLineSegment : PathSegment
    {
        public PolyLineSegment() { }
        public PolyLineSegment(IEnumerable<Point> pts, bool stroked) { Points.AddRange(pts); }
        public List<Point> Points { get; set; } = [];
    }

    public sealed class ArcSegment : PathSegment
    {
        public ArcSegment() { }
        public ArcSegment(Point p, Size size, double angle, bool large, SweepDirection dir, bool stroked) { Point = p; Size = size; SweepDirection = dir; IsLargeArc = large; }
        public Point Point { get; set; }
        public Size Size { get; set; }
        public SweepDirection SweepDirection { get; set; }
        public bool IsLargeArc { get; set; }
    }

    public sealed class PathGeometry : Geometry
    {
        public PathGeometry() { }
        public PathGeometry(IEnumerable<PathFigure> figures) { Figures.AddRange(figures); }
        public List<PathFigure> Figures { get; set; } = [];
        public FillRule FillRule { get; set; }
        internal string? Data;

        internal override Android.Graphics.Path ToPath()
        {
            var p = new Android.Graphics.Path();
            if (Data != null) ParseInto(p, Data);
            foreach (var f in Figures)
            {
                p.MoveTo((float)f.StartPoint.X, (float)f.StartPoint.Y);
                var last = f.StartPoint;
                foreach (var s in f.Segments)
                    switch (s)
                    {
                        case LineSegment l: p.LineTo((float)l.Point.X, (float)l.Point.Y); last = l.Point; break;
                        case PolyLineSegment pl: foreach (var q in pl.Points) { p.LineTo((float)q.X, (float)q.Y); last = q; } break;
                        case ArcSegment a:
                            p.LineTo((float)a.Point.X, (float)a.Point.Y); last = a.Point; break;
                    }
                if (f.IsClosed) p.Close();
            }
            return p;
        }

        private static void ParseInto(Android.Graphics.Path p, string data)
        {
            var tokens = System.Text.RegularExpressions.Regex.Matches(data, @"[A-Za-z]|-?[\d.]+(?:[eE]-?\d+)?");
            char cmd = 'M';
            var nums = new List<double>();
            float cx = 0, cy = 0;
            void Flush()
            {
                int i = 0;
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                        while (i + 1 < nums.Count) { cx = (float)nums[i]; cy = (float)nums[i + 1]; if (i == 0) p.MoveTo(cx, cy); else p.LineTo(cx, cy); i += 2; }
                        break;
                    case 'L':
                        while (i + 1 < nums.Count) { cx = (float)nums[i]; cy = (float)nums[i + 1]; p.LineTo(cx, cy); i += 2; }
                        break;
                    case 'H': foreach (var n in nums) { cx = (float)n; p.LineTo(cx, cy); } break;
                    case 'V': foreach (var n in nums) { cy = (float)n; p.LineTo(cx, cy); } break;
                    case 'Z': p.Close(); break;
                }
                nums.Clear();
            }
            foreach (System.Text.RegularExpressions.Match t in tokens)
            {
                if (char.IsLetter(t.Value[0])) { Flush(); cmd = t.Value[0]; if (char.ToUpperInvariant(cmd) == 'Z') Flush(); }
                else nums.Add(double.Parse(t.Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            Flush();
        }
    }

    public sealed class StreamGeometry : Geometry
    {
        private readonly Android.Graphics.Path _path = new();
        public FillRule FillRule { get; set; }
        public StreamGeometryContext Open() => new(_path);
        internal override Android.Graphics.Path ToPath() => _path;
    }

    public sealed class StreamGeometryContext : IDisposable
    {
        private readonly Android.Graphics.Path _path;
        internal StreamGeometryContext(Android.Graphics.Path path) { _path = path; }
        public void BeginFigure(Point p, bool isFilled, bool isClosed) => _path.MoveTo((float)p.X, (float)p.Y);
        public void LineTo(Point p, bool isStroked, bool isSmoothJoin) => _path.LineTo((float)p.X, (float)p.Y);
        public void PolyLineTo(IList<Point> points, bool isStroked, bool isSmoothJoin) { foreach (var p in points) LineTo(p, isStroked, isSmoothJoin); }
        public void Close() { }
        public void Dispose() { }
    }

    public sealed class DoubleCollection : List<double>
    {
        public DoubleCollection() { }
        public DoubleCollection(IEnumerable<double> v) : base(v) { }
    }

    public sealed class PointCollection : List<Point>
    {
        public PointCollection() { }
        public PointCollection(IEnumerable<Point> v) : base(v) { }
    }

    public static class PresentationSourceShim { }
}

namespace System.Windows
{
    /// <summary>창이 놓인 화면 — 안드로이드에서는 원본 판 그대로(배율 1)다.</summary>
    public sealed class PresentationSource
    {
        public static PresentationSource? FromVisual(Media.Visual v) => null;
        public Media.PresentationSourceTarget CompositionTarget => new();
    }
}
