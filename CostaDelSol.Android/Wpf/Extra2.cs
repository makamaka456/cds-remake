using System.Collections;
using System.Windows.Media;
using ACanvas = Android.Graphics.Canvas;
using APaint = Android.Graphics.Paint;

namespace System.ComponentModel
{
    public struct SortDescription
    {
        public SortDescription(string propertyName, ListSortDirection direction) { PropertyName = propertyName; Direction = direction; }
        public string PropertyName { get; set; }
        public ListSortDirection Direction { get; set; }
    }
}

namespace System.Windows.Media
{
    /// <summary>그림 한 장을 손으로 그리는 판(RenderOpen → DrawingContext). 그린 것을 차례로 담아 두었다가 찍을 때 되풀이한다.</summary>
    public class DrawingVisual : UIElement
    {
        internal readonly List<Action<ACanvas>> Ops = [];
        public DrawingContext RenderOpen() { Ops.Clear(); return new DrawingContext(this); }
        internal override void Paint(ACanvas c, float k) { foreach (var op in Ops) op(c); }
    }

    public sealed class DrawingContext : IDisposable
    {
        private readonly DrawingVisual _v;
        internal DrawingContext(DrawingVisual v) { _v = v; }

        public void DrawRectangle(Brush? fill, Pen? pen, Rect r) => _v.Ops.Add(c =>
        {
            if (fill != null) System.Windows.Controls.Ink.Rect(c, fill, r.X, r.Y, r.Width, r.Height);
            if (pen?.Brush != null)
            {
                var p = new APaint { AntiAlias = true, StrokeWidth = (float)pen.Thickness, Color = System.Windows.Controls.Ink.ColorOf(pen.Brush) };
                p.SetStyle(APaint.Style.Stroke);
                c.DrawRect((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom, p);
            }
        });

        public void DrawLine(Pen pen, Point a, Point b) => _v.Ops.Add(c =>
        {
            var p = new APaint { AntiAlias = true, StrokeWidth = (float)pen.Thickness, Color = System.Windows.Controls.Ink.ColorOf(pen.Brush) };
            c.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, p);
        });

        public void DrawImage(ImageSource? source, Rect r) => _v.Ops.Add(c =>
        {
            if (source?.Bitmap is { } bmp)
                c.DrawBitmap(bmp, null, new Android.Graphics.RectF((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom), WpfPaints.Bitmap(true));
        });

        public void DrawEllipse(Brush? fill, Pen? pen, Point center, double rx, double ry) => _v.Ops.Add(c =>
        {
            if (fill == null) return;
            var p = new APaint { AntiAlias = true, Color = System.Windows.Controls.Ink.ColorOf(fill) };
            c.DrawOval(new Android.Graphics.RectF((float)(center.X - rx), (float)(center.Y - ry), (float)(center.X + rx), (float)(center.Y + ry)), p);
        });

        public void Close() { }
        public void Dispose() { }
    }

    public abstract class CacheMode { }
    public sealed class BitmapCache : CacheMode
    {
        public BitmapCache() { }
        public BitmapCache(double scale) { RenderAtScale = scale; }
        public double RenderAtScale { get; set; } = 1;
        public bool SnapsToDevicePixels { get; set; }
    }
}

namespace System.Windows.Media.Effects
{
    public enum KernelType { Gaussian, Box }
    public sealed class BlurEffect : Effect
    {
        public double Radius { get; set; }
        public KernelType KernelType { get; set; }
    }
}

namespace Microsoft.Win32
{
    /// <summary>
    /// 파일 고르기 — 안드로이드에는 PC 판의 파일 창이 없다. 늘 「고르지 않음」으로 돌아간다
    /// (세이브에서 표 굽기 · 그림/폴더 가져오기 같은 PC 파일 일은 안드로이드에서 하지 않는다).
    /// </summary>
    public class FileDialog
    {
        public string Title { get; set; } = "";
        public string Filter { get; set; } = "";
        public string FileName { get; set; } = "";
        public string[] FileNames => FileName.Length > 0 ? [FileName] : [];
        public string InitialDirectory { get; set; } = "";
        public string DefaultExt { get; set; } = "";
        public bool Multiselect { get; set; }
        public bool CheckFileExists { get; set; }
        public bool OverwritePrompt { get; set; }
        public bool? ShowDialog() => ShowDialog(null);
        public bool? ShowDialog(System.Windows.Window? owner)
        {
            CdsHelper.Game.UI.Views.ConfirmDialog.Tell(owner ?? System.Windows.Window.Screen, "안드로이드판에서는 PC 파일을 고를 수 없습니다.");
            return false;
        }
    }

    public sealed class OpenFileDialog : FileDialog { }
    public sealed class SaveFileDialog : FileDialog { }

    public sealed class OpenFolderDialog : FileDialog
    {
        public string FolderName { get; set; } = "";
    }
}

namespace System.Windows.Data
{
    public static class BindingDoNothing { }
}

namespace System.Windows.Controls
{
    public sealed class DataGridRow
    {
        internal DataGridRow(object? item) { Item = item; }
        public object? Item { get; }
    }

    public enum GridResizeDirection { Auto, Columns, Rows }
    public enum GridResizeBehavior { BasedOnAlignment, CurrentAndNext, PreviousAndCurrent, PreviousAndNext }

    /// <summary>칸 나누개 — 안드로이드에서는 손으로 끌지 않고 가는 줄로만 보인다.</summary>
    public class GridSplitter : Control
    {
        public GridResizeDirection ResizeDirection { get; set; }
        public GridResizeBehavior ResizeBehavior { get; set; }
        public bool ShowsPreview { get; set; }
        public GridSplitter() { Background = Brushes.Gray; }
        protected override Size MeasureOverride(Size available) => new(0, 0);
    }

    public class DataGridComboBoxColumn : DataGridColumn
    {
        public IEnumerable? ItemsSource { get; set; }
        public string DisplayMemberPath { get; set; } = "";
        public string SelectedValuePath { get; set; } = "";
        public Data.Binding? SelectedValueBinding { get; set; }
        public Data.Binding? SelectedItemBinding { get; set; }
    }

    public class DataGridCheckBoxColumn : DataGridColumn
    {
        public Data.Binding? Binding { get; set; }
    }
}

namespace System.Windows.Controls
{
    /// <summary>표 칸 — 안드로이드 표는 칸을 요소로 짓지 않으므로 찾아도 안 나온다.</summary>
    public sealed class DataGridCell : ContentControl
    {
        public DataGridColumn? Column { get; set; }
    }
}

namespace System.Windows
{
    public delegate void DragEventHandler(object sender, DragEventArgs e);

    /// <summary>끌어다 놓기 — 안드로이드에는 PC 탐색기에서 파일을 끌어올 일이 없어 오지 않는다.</summary>
    public sealed class DragEventArgs : RoutedEventArgs
    {
        public IDataObject Data { get; } = new DataObject();
        public DragDropEffects Effects { get; set; }
        public Point GetPosition(IInputElement? relativeTo) => default;
    }

    public interface IDataObject
    {
        bool GetDataPresent(string format);
        object? GetData(string format);
    }

    public sealed class DataObject : IDataObject
    {
        public bool GetDataPresent(string format) => false;
        public object? GetData(string format) => null;
    }

    [Flags] public enum DragDropEffects { None = 0, Copy = 1, Move = 2, Link = 4, Scroll = int.MinValue, All = -2147483641 }

    public static class DataFormats
    {
        public const string FileDrop = "FileDrop";
        public const string Text = "Text";
    }
}
