using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.UI.Views;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;

namespace CostaDelSol.Droid.Tools;

/// <summary>
/// 도구 앱(CdsHelper) 본체 화면 — PC 판은 XAML 틀에 뷰모델을 묶는다. 뷰모델은 PC 판 그대로 쓰고,
/// XAML 의 틀만 코드로 옮겼다(<see cref="Bind"/> 가 {Binding} 몫을 한다).
/// </summary>
internal static class ToolPages
{
    public static readonly (string Name, Action<Window> Open)[] Pages =
    [
        ("아이템", w => Show(w, "아이템", ItemPage())),
        ("선수상", w => Show(w, "선수상", FigureheadPage())),
        ("후원자", w => Show(w, "후원자", PatronPage())),
        ("발견물 그림", w => Show(w, "발견물 그림", StillPage())),
        ("효과음 보기", w => new CdsHelper.Main.UI.Views.WaveBankDialog { Owner = w }.ShowDialog()),
        ("동영상 보기", w => new CdsHelper.Main.UI.Views.MovieBankDialog { Owner = w }.ShowDialog()),
    ];

    /// <summary>발견물 그림(DSTILL) — 왼쪽 목록(작은 그림 · 이름), 오른쪽 고른 그림 크게(PC 판 DiscoveryStillContent 틀).</summary>
    private static UIElement StillPage()
    {
        var vm = new DiscoveryStillContentViewModel();
        var big = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(12, 0, 0, 0) };
        var size = new TextBlock { Margin = new Thickness(12, 6, 0, 0), Foreground = Brushes.Gray };
        var list = new ListBox { Width = 300 };
        foreach (var still in vm.Stills)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new Border { Width = 40, Height = 40, BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), BorderThickness = new Thickness(1), Child = new Image { Source = still.Image, Stretch = Stretch.Uniform } });
            row.Children.Add(new TextBlock { Text = still.Title, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            list.Items.Add(row);
        }
        void Show()
        {
            big.Source = vm.Selected?.Image;
            size.Text = vm.Selected?.Size ?? "";
        }
        list.SelectionChanged += (_, _) => { vm.Selected = list.SelectedIndex >= 0 ? vm.Stills[list.SelectedIndex] : null; Show(); };
        list.SelectedIndex = vm.Selected == null ? -1 : vm.Stills.IndexOf(vm.Selected);
        Show();
        var note = Bind.Show(new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8) }, vm, nameof(vm.Note), v => v?.ToString() ?? "");
        var right = new DockPanel();
        DockPanel.SetDock(size, Dock.Bottom);
        right.Children.Add(size);
        right.Children.Add(big);
        var body = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(note, Dock.Top);
        DockPanel.SetDock(list, Dock.Left);
        body.Children.Add(note);
        body.Children.Add(new ScrollViewer { Content = list, Width = 310 });
        DockPanel.SetDock(body.Children[1], Dock.Left);
        body.Children.Add(right);
        return body;
    }


    private static void Show(Window owner, string title, UIElement body)
    {
        var window = new GameWindow
        {
            Title = title,
            Owner = owner,
            Width = 900,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
            Content = body,
        };
        window.ShowDialog();
    }

    // ── 화면들(PC 판 Themes/Views/*.xaml 의 틀) ──

    private static UIElement ItemPage()
    {
        var vm = new ItemContentViewModel(new ItemService());
        return FilterGrid(vm,
        [
            Label("아이템명/힌트:"), Bind.Text(new TextBox { Width = 150 }, vm, nameof(vm.ItemNameSearch)),
            Label("분류:"), Bind.Combo(new ComboBox { Width = 100 }, vm, nameof(vm.ItemCategories), nameof(vm.SelectedItemCategory)),
            Label("관련발견물:"), Bind.Text(new TextBox { Width = 150 }, vm, nameof(vm.ItemDiscoverySearch)),
            Bind.Command(new Button { Content = "필터 초기화", Width = 100 }, vm, "ResetFilterCommand"),
        ], nameof(vm.Items),
        [("ID", "Id", 50), ("아이템명", "Name", 150), ("분류", "Category", 80), ("효과", "Effect", 60),
         ("판매가", "SellPrice", 80), ("구매가", "BuyPrice", 80), ("힌트", "Hint", 150), ("관련발견물", "RelatedDiscovery", 0)]);
    }

    private static UIElement FigureheadPage()
    {
        var vm = new FigureheadContentViewModel(new FigureheadService());
        return FilterGrid(vm,
        [
            Label("선수상명/효과:"), Bind.Text(new TextBox { Width = 150 }, vm, nameof(vm.FigureheadNameSearch)),
            Label("기능:"), Bind.Combo(new ComboBox { Width = 80 }, vm, nameof(vm.FigureheadFunctions), nameof(vm.SelectedFigureheadFunction)),
            Label("레벨:"), Bind.Combo(new ComboBox { Width = 80 }, vm, nameof(vm.FigureheadLevels), nameof(vm.SelectedFigureheadLevel)),
            Bind.Command(new Button { Content = "필터 초기화", Width = 100 }, vm, "ResetFilterCommand"),
        ], nameof(vm.Figureheads),
        [("선수상", "Name", 120), ("기능", "Function", 60), ("레벨", "Level", 60), ("비고", "Note", 0)]);
    }

    private static UIElement PatronPage()
    {
        var vm = new PatronContentViewModel(new PatronService(), new SaveDataService(new CityService()));
        return FilterGrid(vm,
        [
            Label("후원자명:"), Bind.Text(new TextBox { Width = 120 }, vm, nameof(vm.PatronNameSearch)),
            Label("도시:"), Bind.Text(new TextBox { Width = 120 }, vm, nameof(vm.PatronCitySearch)),
            Label("국적:"), Bind.Combo(new ComboBox { Width = 150 }, vm, nameof(vm.Nationalities), nameof(vm.SelectedNationality)),
            Bind.Check(new CheckBox { Content = "활동중인 후원자만" }, vm, nameof(vm.ActivePatronsOnly)),
            Bind.Command(new Button { Content = "필터 초기화", Width = 100 }, vm, "ResetFilterCommand"),
        ], nameof(vm.Patrons),
        [("ID", "Id", 40), ("이름", "Name", 150), ("국적", "Nationality", 130), ("도시", "City", 100), ("직업", "Occupation", 80),
         ("원조율", "SupportRate", 70), ("감별력", "Discernment", 70), ("등장", "AppearYear", 60), ("은퇴", "RetireYear", 60),
         ("상태", "StatusDisplay", 70), ("명성", "Fame", 70), ("재력", "Wealth", 90), ("권력", "Power", 50),
         ("취향", "Preferences", 200), ("비고", "Note", 0)]);
    }


    // ── 짓는 도구 ──

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 5, 0) };

    private static UIElement Group(string header, UIElement body) => new StackPanel
    {
        Margin = new Thickness(0, 0, 0, 10),
        Children =
        {
            new TextBlock { Text = header, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) },
            new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = body },
        },
    };

    /// <summary>위 거르개 줄 + 아래 표(PC 판 화면들의 공통 꼴). 표 칸은 (머리, 속성, 폭 — 0 이면 남는 폭).</summary>
    private static UIElement FilterGrid(INotifyPropertyChanged vm, UIElement[] filters, string itemsProp,
                                        (string Header, string Path, double Width)[] columns)
    {
        var top = new WrapPanel { Margin = new Thickness(10) };
        foreach (var f in filters)
        {
            if (f is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; if (double.IsNaN(fe.Height) && f is not TextBlock) fe.Height = 25; }
            top.Children.Add(f);
        }
        var grid = new DataGrid { IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(10) };
        foreach (var (header, path, width) in columns)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(path),
                Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
            });
        Bind.Items(grid, vm, itemsProp);
        var status = vm.GetType().GetProperty("StatusText") != null
            ? Bind.Show(new TextBlock { Margin = new Thickness(10, 0, 10, 6) }, vm, "StatusText", v => v?.ToString() ?? "")
            : new TextBlock();
        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        page.Children.Add(top);
        page.Children.Add(status);
        page.Children.Add(grid);
        return page;
    }
}

/// <summary>{Binding} 흉내 — 뷰모델 속성과 요소를 PropertyChanged 로 잇는다.</summary>
internal static class Bind
{
    private static object? Get(object vm, string prop) => vm.GetType().GetProperty(prop)?.GetValue(vm);

    private static void Set(object vm, string prop, object? value)
    {
        var p = vm.GetType().GetProperty(prop);
        if (p is not { CanWrite: true }) return;
        try
        {
            var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            p.SetValue(vm, value == null || type.IsInstanceOfType(value) ? value : Convert.ChangeType(value, type));
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { }
    }

    private static void Watch(object vm, string prop, Action changed)
    {
        if (vm is INotifyPropertyChanged n)
            n.PropertyChanged += (_, e) => { if (e.PropertyName == prop || string.IsNullOrEmpty(e.PropertyName)) changed(); };
    }

    public static TextBox Text(TextBox box, object vm, string prop)
    {
        bool busy = false;
        box.Text = Get(vm, prop)?.ToString() ?? "";
        box.TextChanged += (_, _) => { if (busy) return; busy = true; Set(vm, prop, box.Text); busy = false; };
        Watch(vm, prop, () => { if (busy) return; busy = true; box.Text = Get(vm, prop)?.ToString() ?? ""; busy = false; });
        return box;
    }

    public static ComboBox Combo(ComboBox box, object vm, string itemsProp, string selectedProp)
    {
        bool busy = false;
        void Fill()
        {
            busy = true;
            box.Items.Clear();
            foreach (var item in (Get(vm, itemsProp) as IEnumerable)?.Cast<object>() ?? []) box.Items.Add(item);
            box.SelectedItem = Get(vm, selectedProp);
            busy = false;
        }
        Fill();
        if (Get(vm, itemsProp) is System.Collections.Specialized.INotifyCollectionChanged items) items.CollectionChanged += (_, _) => Fill();
        box.SelectionChanged += (_, _) => { if (!busy) Set(vm, selectedProp, box.SelectedItem); };
        Watch(vm, selectedProp, () => { busy = true; box.SelectedItem = Get(vm, selectedProp); busy = false; });
        return box;
    }

    public static ComboBox ComboIndex(ComboBox box, object vm, string itemsProp, string indexProp)
    {
        bool busy = false;
        foreach (var item in (Get(vm, itemsProp) as IEnumerable)?.Cast<object>() ?? []) box.Items.Add(item);
        box.SelectedIndex = Get(vm, indexProp) is int i ? i : -1;
        box.SelectionChanged += (_, _) => { if (!busy) Set(vm, indexProp, box.SelectedIndex); };
        Watch(vm, indexProp, () => { busy = true; box.SelectedIndex = Get(vm, indexProp) is int k ? k : -1; busy = false; });
        return box;
    }

    public static CheckBox Check(CheckBox box, object vm, string prop)
    {
        box.IsChecked = Get(vm, prop) is true;
        box.Checked += (_, _) => Set(vm, prop, true);
        box.Unchecked += (_, _) => Set(vm, prop, false);
        Watch(vm, prop, () => box.IsChecked = Get(vm, prop) is true);
        return box;
    }

    public static Button Command(Button button, object vm, string commandProp)
    {
        button.Click += (_, _) => (Get(vm, commandProp) as ICommand)?.Execute(null);
        return button;
    }

    public static DataGrid Items(DataGrid grid, object vm, string prop)
    {
        void Fill()
        {
            var items = Get(vm, prop) as IEnumerable;
            grid.ItemsSource = items?.Cast<object>().ToList();
        }
        Fill();
        Watch(vm, prop, () =>
        {
            Fill();
            if (Get(vm, prop) is System.Collections.Specialized.INotifyCollectionChanged c) c.CollectionChanged += (_, _) => Fill();
        });
        if (Get(vm, prop) is System.Collections.Specialized.INotifyCollectionChanged first) first.CollectionChanged += (_, _) => Fill();
        return grid;
    }

    public static TextBlock Show(TextBlock text, object vm, string prop, Func<object?, string> format)
    {
        text.Text = format(Get(vm, prop));
        Watch(vm, prop, () => text.Text = format(Get(vm, prop)));
        return text;
    }
}
