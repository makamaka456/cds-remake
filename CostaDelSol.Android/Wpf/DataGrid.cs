using System.Collections;
using System.Windows.Media;

namespace System.Windows.Data
{
    /// <summary>줄의 속성 이름 하나(표 칸이 읽고 쓴다).</summary>
    public class Binding
    {
        public Binding() { }
        public Binding(string path) { Path = new PropertyPath(path); PathText = path; }
        internal string PathText { get; } = "";
        public PropertyPath? Path { get; set; }
        public string? StringFormat { get; set; }
        public BindingMode Mode { get; set; }
        public IValueConverter? Converter { get; set; }
        public object? ConverterParameter { get; set; }
        public UpdateSourceTrigger UpdateSourceTrigger { get; set; }
        /// <summary>묶을 원천 — 안 주면 그 줄(DataContext)이다.</summary>
        public object? Source { get; set; }
        public static readonly object DoNothing = new();
    }

    public enum BindingMode { Default, OneWay, TwoWay, OneTime, OneWayToSource }
    public enum UpdateSourceTrigger { Default, PropertyChanged, LostFocus, Explicit }
}

namespace System.Windows.Controls
{
    public enum DataGridHeadersVisibility { None, Column, Row, All }
    public enum DataGridSelectionMode { Single, Extended }
    public enum DataGridEditAction { Cancel, Commit }
    public enum DataGridLengthUnitType { Auto, Pixel, SizeToCells, SizeToHeader, Star }

    public struct DataGridLength
    {
        public double Value;
        public DataGridLengthUnitType UnitType;
        public DataGridLength(double v) { Value = v; UnitType = DataGridLengthUnitType.Pixel; }
        public DataGridLength(double v, DataGridLengthUnitType t) { Value = v; UnitType = t; }
        public static implicit operator DataGridLength(double v) => new(v);
    }

    public abstract class DataGridColumn
    {
        public object? Header { get; set; }
        public DataGridLength Width { get; set; } = new(80);
        public bool IsReadOnly { get; set; }
        public Style? ElementStyle { get; set; }
        public Style? HeaderStyle { get; set; }
        public string SortMemberPath { get; set; } = "";
        public System.ComponentModel.ListSortDirection? SortDirection { get; set; }
        public bool CanUserSort { get; set; } = true;
    }

    public class DataGridTextColumn : DataGridColumn
    {
        public Data.Binding? Binding { get; set; }
        public Brush? Foreground { get; set; }
        public FontWeight FontWeight { get; set; } = FontWeights.Normal;
    }

    public sealed class DataGridCellEditEndingEventArgs : EventArgs
    {
        public DataGridEditAction EditAction { get; init; }
        public DataGridColumn Column { get; init; } = null!;
        public DataGridRow Row { get; init; } = null!;
        public FrameworkElement? EditingElement { get; init; }
        public bool Cancel { get; set; }
    }

    /// <summary>
    /// 표 — 머리줄과 줄들을 글로 그린다. 줄을 누르면 고르고, 고칠 수 있는 칸을 한 번 더 누르면 기기 글자판으로 고친다.
    /// 길면 끌어서 굴린다.
    /// </summary>
    public class DataGrid : Primitives.Selector
    {
        public DataGrid()
        {
            Background = Brushes.White;
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x68, 0x68, 0x68));
            BorderThickness = new Thickness(1);
            FontSize = 12;
            ClipToBounds = true;
            SelectionChanged += (_, _) => WpfHost.Touch();
        }

        public List<DataGridColumn> Columns { get; } = [];
        public bool AutoGenerateColumns { get; set; }
        public bool CanUserAddRows { get; set; }
        public bool CanUserDeleteRows { get; set; }
        public bool CanUserSortColumns { get; set; }
        public bool CanUserResizeColumns { get; set; }
        public bool CanUserReorderColumns { get; set; }
        public bool IsReadOnly { get; set; }
        public DataGridHeadersVisibility HeadersVisibility { get; set; } = DataGridHeadersVisibility.All;
        public new DataGridSelectionMode SelectionMode { get; set; }
        public Brush? AlternatingRowBackground { get; set; }
        public Brush? RowBackground { get; set; }
        public Brush? HorizontalGridLinesBrush { get; set; }
        public Brush? VerticalGridLinesBrush { get; set; }
        public DataGridGridLinesVisibility GridLinesVisibility { get; set; }
        public double RowHeight { get; set; } = double.NaN;
        public double ColumnHeaderHeight { get; set; } = double.NaN;
        public event EventHandler<DataGridCellEditEndingEventArgs>? CellEditEnding;

        public void CommitEdit() { }
        public bool CommitEdit(DataGridEditingUnit unit, bool exitEditingMode) => true;
        public DataGridCellInfo CurrentCell { get; set; }
        public bool BeginEdit() => false;
        public void CancelEdit() { }
        /// <summary>그 줄이 보이게 굴린다.</summary>
        public void ScrollIntoView(object item)
        {
            int at = AllItems.ToList().IndexOf(item);
            if (at < 0) return;
            double view = Math.Max(Line, ActualHeight - BorderThickness.H - Head);
            double top = at * Line, bottom = top + Line;
            double most = Math.Max(0, Head + Line * AllItems.Count() - (ActualHeight - BorderThickness.H));
            if (top < _scroll) _scroll = top;
            else if (bottom > _scroll + view) _scroll = bottom - view;
            _scroll = Math.Clamp(_scroll, 0, most);
            WpfHost.Touch();
        }

        private double Line => !double.IsNaN(RowHeight) ? RowHeight
            : Math.Max(Math.Ceiling(FontSize * 1.6), Columns.OfType<DataGridTemplateColumn>()
                .Select(c => c.CellTemplate?.VisualTree?.Values.GetValueOrDefault(FrameworkElement.HeightProperty) is double h ? h + 2 : 0).DefaultIfEmpty(0).Max());
        private double Head => HeadersVisibility is DataGridHeadersVisibility.Column or DataGridHeadersVisibility.All
            ? (double.IsNaN(ColumnHeaderHeight) ? Line + 2 : ColumnHeaderHeight) : 0;
        private double _scroll;
        private double? _grabY;
        private double _grab0;
        private bool _dragged;
        // 옆으로 밀기 — 칸 폭 합이 표보다 넓으면(나라 · 인물 · 책 편집 따위) 손가락으로 옆으로 민다.
        private double _hscroll, _grabX, _grabH;
        private int _axis;   // 0 아직 모름, 1 위아래, 2 옆

        /// <summary>진단 — 지금 굴린 양(위아래 · 옆).</summary>
        internal (double Y, double X) Scrolled => (_scroll, _hscroll);

        private double MostSideways()
        {
            double room = ActualWidth - BorderThickness.W;
            return Math.Max(0, Widths(room).Sum() - room);
        }

        internal override IEnumerable<UIElement> VisualChildren => [];

        protected override Size MeasureOverride(Size available)
        {
            double w = Columns.Sum(c => c.Width.UnitType == DataGridLengthUnitType.Pixel ? c.Width.Value : 80) + BorderThickness.W;
            double h = Head + Line * AllItems.Count() + BorderThickness.H;
            return new Size(Math.Min(w, available.Width), Math.Min(h, available.Height));
        }

        protected override Size ArrangeOverride(Size final) => final;

        private static string Cell(object? row, DataGridColumn col)
        {
            if (row != null && col is DataGridComboBoxColumn { SelectedValueBinding: { } sb } combo)
            {
                var value = row.GetType().GetProperty(sb.PathText)?.GetValue(row);
                foreach (var item in combo.ItemsSource?.Cast<object>() ?? [])
                    if (Equals(item.GetType().GetProperty(combo.SelectedValuePath)?.GetValue(item), value))
                        return item.GetType().GetProperty(combo.DisplayMemberPath)?.GetValue(item)?.ToString() ?? "";
                return value?.ToString() ?? "";
            }
            if (row != null && col is DataGridCheckBoxColumn { Binding: { } cb })
                return row.GetType().GetProperty(cb.PathText)?.GetValue(row) is true ? "☑" : "☐";
            if (row == null || col is not DataGridTextColumn { Binding: { } b }) return "";
            var prop = row.GetType().GetProperty(b.PathText);
            var v = prop?.GetValue(row);
            if (b.Converter != null) v = b.Converter.Convert(v, typeof(string), b.ConverterParameter, System.Globalization.CultureInfo.CurrentCulture);
            return v == null ? "" : b.StringFormat != null ? string.Format(b.StringFormat, v) : v.ToString() ?? "";
        }

        private double[] Widths(double room)
        {
            var w = Columns.Select(c => c.Width.UnitType == DataGridLengthUnitType.Pixel ? c.Width.Value : 0).ToArray();
            double fixedW = w.Sum();
            int stars = Columns.Count(c => c.Width.UnitType != DataGridLengthUnitType.Pixel);
            for (int i = 0; i < w.Length; i++)
                if (w[i] == 0) w[i] = stars > 0 ? Math.Max(40, (room - fixedW) / stars) : 80;
            return w;
        }

        internal override void Paint(Android.Graphics.Canvas c, float k)
        {
            base.Paint(c, k);
            var t = BorderThickness;
            double room = ActualWidth - t.W;
            var widths = Widths(room);
            var text = new Android.Graphics.Paint { AntiAlias = true, TextSize = (float)FontSize, Color = Ink.ColorOf(Foreground) };
            var line = new Android.Graphics.Paint { Color = new Android.Graphics.Color(0xD0, 0xD0, 0xD0, 255) };
            var fm = text.GetFontMetrics()!;
            float Base(double top, double h) => (float)(top + (h - (fm.Descent - fm.Ascent)) / 2 - fm.Ascent);

            c.Save();
            c.ClipRect((float)t.Left, (float)t.Top, (float)(ActualWidth - t.Right), (float)(ActualHeight - t.Bottom));
            double y = t.Top + Head - _scroll;
            int i = 0;
            foreach (var row in AllItems)
            {
                if (y + Line >= t.Top + Head && y < ActualHeight)
                {
                    Brush? back = i == SelectedIndex ? SelectedBrush : i % 2 == 1 ? AlternatingRowBackground ?? RowBackground : RowBackground;
                    Ink.Rect(c, back, t.Left, y, room, Line);
                    text.Color = i == SelectedIndex ? Android.Graphics.Color.White : Ink.ColorOf(Foreground);
                    double x = t.Left - _hscroll;
                    for (int col = 0; col < Columns.Count; col++)
                    {
                        c.Save();
                        c.ClipRect((float)x, (float)y, (float)(x + widths[col] - 1), (float)(y + Line));
                        if (Columns[col] is DataGridTemplateColumn { CellTemplate.VisualTree: { } tree } && ArtOf(row, tree) is { Bitmap: { } art })
                            c.DrawBitmap(art, null, new Android.Graphics.RectF((float)x + 1, (float)y + 1, (float)(x + 1 + Math.Min(widths[col] - 2, tree.Values.GetValueOrDefault(FrameworkElement.WidthProperty) as double? ?? Line)), (float)(y + Line - 1)), WpfPaints.Bitmap(true));
                        else c.DrawText(Cell(row, Columns[col]), (float)(x + 3), Base(y, Line), text);
                        c.Restore();
                        x += widths[col];
                    }
                    c.DrawRect((float)t.Left, (float)(y + Line - 1), (float)(t.Left + room), (float)(y + Line), line);
                }
                y += Line;
                i++;
            }
            if (Head > 0)
            {
                var headBack = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
                Ink.Rect(c, headBack, t.Left, t.Top, room, Head);
                text.Color = Android.Graphics.Color.Black;
                double x = t.Left - _hscroll;
                for (int col = 0; col < Columns.Count; col++)
                {
                    c.DrawText(Columns[col].Header?.ToString() ?? "", (float)(x + 3), Base(t.Top, Head), text);
                    x += widths[col];
                    c.DrawRect((float)(x - 1), (float)t.Top, (float)x, (float)(t.Top + Head), line);
                }
            }
            c.Restore();
        }

        private static ImageSource? ArtOf(object? row, FrameworkElementFactory tree)
        {
            if (row == null || !tree.Bindings.TryGetValue(Image.SourceProperty, out var b)) return null;
            object? v = row.GetType().GetProperty(b.PathText)?.GetValue(row);
            if (b.Converter != null) v = b.Converter.Convert(v, typeof(ImageSource), b.ConverterParameter, System.Globalization.CultureInfo.CurrentCulture);
            return v as ImageSource;
        }

        internal override bool HitSelf(Point p) => true;

        internal override void OnRaised(string name, RoutedEventArgs e)
        {
            if (e is not System.Windows.Input.MouseEventArgs m) return;
            var local = FromWindow(m.WindowPoint, false);
            switch (name)
            {
                case "MouseLeftButtonDown":
                    _grabY = local.Y; _grab0 = _scroll; _dragged = false;
                    _grabX = local.X; _grabH = _hscroll; _axis = 0;
                    CaptureMouse();
                    e.Handled = true;
                    break;
                case "MouseMove" when _grabY is { } g0:
                {
                    double dy = local.Y - g0, dx = local.X - _grabX;
                    double side = MostSideways();
                    if (_axis == 0 && Math.Max(Math.Abs(dx), Math.Abs(dy)) > 6)
                        _axis = Math.Abs(dx) > Math.Abs(dy) && side > 0 ? 2 : 1;
                    if (_axis == 1)
                    {
                        _dragged = true;
                        double most = Math.Max(0, Head + Line * AllItems.Count() - (ActualHeight - BorderThickness.H));
                        _scroll = Math.Clamp(_grab0 - dy, 0, most);
                        WpfHost.Touch();
                    }
                    else if (_axis == 2)
                    {
                        _dragged = true;
                        _hscroll = Math.Clamp(_grabH - dx, 0, side);
                        WpfHost.Touch();
                    }
                    break;
                }
                case "MouseRightButtonUp" when !IsReadOnly && local.Y >= BorderThickness.Top + Head:
                {
                    // 길게 누르기(오른쪽 딸깍) — 그 줄을 고르고 곧장 그 칸을 고친다(두 번 누르기와 같다).
                    int row = (int)((local.Y - BorderThickness.Top - Head + _scroll) / Line);
                    var rows = AllItems.ToList();
                    if (row < 0 || row >= rows.Count) break;
                    e.Handled = true;
                    SelectedIndex = row;
                    Edit(rows[row], local.X);
                    break;
                }
                case "MouseLeftButtonUp":
                    if (_grabY == null) break;
                    _grabY = null;
                    ReleaseMouseCapture();
                    e.Handled = true;
                    if (_dragged || local.Y < BorderThickness.Top + Head) break;
                    int at = (int)((local.Y - BorderThickness.Top - Head + _scroll) / Line);
                    var items = AllItems.ToList();
                    if (at < 0 || at >= items.Count) break;
                    if (at != SelectedIndex) { SelectedIndex = at; break; }
                    Edit(items[at], local.X);
                    break;
            }
        }

        /// <summary>고른 줄을 한 번 더 누르면 그 칸을 고친다.</summary>
        private void Edit(object row, double x)
        {
            if (IsReadOnly) return;
            var widths = Widths(ActualWidth - BorderThickness.W);
            double at = BorderThickness.Left - _hscroll;
            for (int col = 0; col < Columns.Count; col++)
            {
                if (x >= at && x < at + widths[col])
                {
                    if (Columns[col] is DataGridCheckBoxColumn { IsReadOnly: false, Binding: { } cb } check
                        && row.GetType().GetProperty(cb.PathText) is { CanWrite: true } flag)
                    {
                        flag.SetValue(row, flag.GetValue(row) is not true);
                        CellEditEnding?.Invoke(this, new DataGridCellEditEndingEventArgs { EditAction = DataGridEditAction.Commit, Column = check, Row = new DataGridRow(row) });
                        WpfHost.Touch();
                        return;
                    }
                    if (Columns[col] is DataGridComboBoxColumn { IsReadOnly: false, SelectedValueBinding: { } sb } combo
                        && row.GetType().GetProperty(sb.PathText) is { CanWrite: true } pick)
                    {
                        var choices = combo.ItemsSource?.Cast<object>().ToList() ?? [];
                        var names = choices.Select(c => c.GetType().GetProperty(combo.DisplayMemberPath)?.GetValue(c)?.ToString() ?? c.ToString() ?? "").ToList();
                        int at2 = CdsHelper.Game.UI.Views.ChoiceDialog.Pick(Window.GetWindow(this) ?? Window.Screen, combo.Header?.ToString() ?? "", names);
                        if (at2 < 0) return;
                        pick.SetValue(row, choices[at2].GetType().GetProperty(combo.SelectedValuePath)?.GetValue(choices[at2]));
                        CellEditEnding?.Invoke(this, new DataGridCellEditEndingEventArgs { EditAction = DataGridEditAction.Commit, Column = combo, Row = new DataGridRow(row) });
                        WpfHost.Touch();
                        return;
                    }
                    if (Columns[col] is not DataGridTextColumn { IsReadOnly: false, Binding: { } b } column) return;
                    var prop = row.GetType().GetProperty(b.PathText);
                    if (prop is not { CanWrite: true }) return;
                    var owner = Window.GetWindow(this) ?? Window.Screen;
                    string now = Cell(row, column), caption = column.Header?.ToString() ?? "";
                    var target = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    // 글 칸이고 길거나(30 자 넘게) 줄이 바뀌는 글이면 여러 줄 입력칸 — 책 본문 · 힌트 · 소문 따위.
                    // 예전에는 모두 한 줄 40 자로 잘렸다.
                    bool text = target == typeof(string);
                    string? typed = text && (now.Length > 30 || now.Contains('\n') || widths[col] >= 300)
                        ? CdsHelper.Game.UI.Views.TextInputDialog.AskLong(owner, now, caption)
                        : CdsHelper.Game.UI.Views.TextInputDialog.Ask(owner, now, text ? 400 : 40, caption);
                    if (typed is null)
                    {
                        CellEditEnding?.Invoke(this, new DataGridCellEditEndingEventArgs { EditAction = DataGridEditAction.Cancel, Column = column, Row = new DataGridRow(row) });
                        return;
                    }
                    try
                    {
                        object? value = b.Converter != null
                            ? b.Converter.ConvertBack(typed, prop.PropertyType, b.ConverterParameter, System.Globalization.CultureInfo.CurrentCulture)
                            : typed;
                        // 숫자 칸을 비우면 「값 없음」 — 고친 것을 걷고 원본 값으로 돌아가는 칸(int? 따위)이다.
                        if (value is string str && str.Length == 0 && !text && Nullable.GetUnderlyingType(prop.PropertyType) != null)
                            prop.SetValue(row, null);
                        else prop.SetValue(row, value is null ? null : Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture));
                    }
                    catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
                    {
                        // 예전에는 말없이 버렸다 — 무엇을 넣어야 하는지 알린다.
                        CdsHelper.Game.UI.Views.NoticeDialog.Show(owner, target == typeof(bool) ? "참/거짓(True/False)을 넣어 주세요"
                            : target.IsPrimitive || target == typeof(decimal) ? $"「{caption}」 칸에는 숫자를 넣어 주세요" : $"「{caption}」 칸에 넣을 수 없는 값입니다");
                        CellEditEnding?.Invoke(this, new DataGridCellEditEndingEventArgs { EditAction = DataGridEditAction.Cancel, Column = column, Row = new DataGridRow(row) });
                        return;
                    }
                    CellEditEnding?.Invoke(this, new DataGridCellEditEndingEventArgs
                    {
                        EditAction = DataGridEditAction.Commit, Column = column, Row = new DataGridRow(row), EditingElement = new TextBox { Text = typed },
                    });
                    WpfHost.Touch();
                    return;
                }
                at += widths[col];
            }
        }
    }

    public enum DataGridGridLinesVisibility { All, Horizontal, None, Vertical }
}

namespace System.Windows.Data
{
    public interface IValueConverter
    {
        object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture);
        object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture);
    }
}

namespace System.Windows
{
    /// <summary>표 칸 틀 — 안드로이드 표는 그림(Image) 칸만 그린다(얼굴 번호 → 얼굴).</summary>
    public sealed class FrameworkElementFactory(Type type)
    {
        public Type Type { get; } = type;
        internal readonly Dictionary<DependencyProperty, object?> Values = [];
        internal readonly Dictionary<DependencyProperty, Data.Binding> Bindings = [];
        public void SetValue(DependencyProperty p, object? v) => Values[p] = v;
        public void SetBinding(DependencyProperty p, Data.Binding b) => Bindings[p] = b;
        internal readonly List<FrameworkElementFactory> Children = [];
        public void AppendChild(FrameworkElementFactory child) => Children.Add(child);
    }
}

namespace System.Windows.Controls
{
    public class DataGridTemplateColumn : DataGridColumn
    {
        public DataTemplate? CellTemplate { get; set; }
    }

    /// <summary>탭 — 머리 줄을 누르면 그 쪽 내용으로 바뀐다.</summary>
    public class TabItem : ContentControl
    {
        public object? Header { get; set; }
        public bool IsSelected { get; set; }
    }

    public class TabControl : Primitives.Selector
    {
        private readonly StackPanel _heads = new() { Orientation = Orientation.Horizontal };
        private readonly Border _page = new();
        private readonly DockPanel _root = new();

        public TabControl()
        {
            DockPanel.SetDock(_heads, Dock.Top);
            _root.Children.Add(_heads);
            _root.Children.Add(_page);
            Adopt(_root);
            Items.Changed = Build;
            SelectionChanged += (_, _) => Build();
        }

        private void Build()
        {
            var tabs = Items.OfType<TabItem>().ToList();
            if (SelectedIndex < 0 && tabs.Count > 0) { SelectedIndex = 0; return; }
            _heads.Children.Clear();
            for (int i = 0; i < tabs.Count; i++)
            {
                int at = i;
                bool on = i == SelectedIndex;
                tabs[i].IsSelected = on;
                var head = new Border
                {
                    Background = new SolidColorBrush(on ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0xE0, 0xE0, 0xE0)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xAC, 0xAC, 0xAC)),
                    BorderThickness = new Thickness(1, 1, 1, on ? 0 : 1),
                    Padding = new Thickness(10, 3, 10, 3),
                    Child = new TextBlock { Text = tabs[i].Header?.ToString() ?? "" },
                };
                head.MouseLeftButtonUp += (_, e) => { e.Handled = true; SelectedIndex = at; };
                _heads.Children.Add(head);
            }
            var page = SelectedIndex >= 0 && SelectedIndex < tabs.Count ? tabs[SelectedIndex].Content as UIElement : null;
            if (!ReferenceEquals(_page.Child, page))
            {
                _page.Child = null;
                if (page != null && page.VisualParent is { } old) old.Orphan(page);
                _page.Child = page;
            }
        }

        internal override IEnumerable<UIElement> VisualChildren => [_root];

        protected override Size MeasureOverride(Size available)
        {
            _root.Measure(available);
            return _root.DesiredSize;
        }

        protected override Size ArrangeOverride(Size final)
        {
            _root.Arrange(new Rect(final));
            return final;
        }
    }
}
