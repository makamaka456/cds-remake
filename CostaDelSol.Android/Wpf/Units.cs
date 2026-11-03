using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Support.UI.Units;

/// <summary>
/// 도구 앱의 기능 · 언어 판(PC 판은 XAML 틀로 그린다). 안드로이드에서는 판 창(SkillBoardDialog)만 이것을 짓고
/// 놀이 화면은 쓰지 않으므로, 머리글과 줄을 글로만 늘어놓는다.
/// </summary>
public class SkillGroupBox : ContentControl
{
    public string Header { get; set; } = "";
    public IEnumerable? Skills { get => _skills; set { _skills = value; Rebuild(); } }
    public IEnumerable? Languages { get => _languages; set { _languages = value; Rebuild(); } }
    public string Column1Label { get; set; } = "";
    public string Column2Label { get; set; } = "";
    public string Column3Label { get; set; } = "";
    public string Column4Label { get; set; } = "";
    public string Column5Label { get; set; } = "";
    public string SkillsHeader { get; set; } = "기능";
    public string LanguagesHeader { get; set; } = "언어";
    private IEnumerable? _skills, _languages;

    private void Rebuild()
    {
        var text = new System.Text.StringBuilder();
        foreach (var (title, rows) in new[] { (SkillsHeader, _skills), (LanguagesHeader, _languages) })
        {
            if (rows == null) continue;
            text.AppendLine(title);
            foreach (var row in rows.Cast<CdsHelper.Support.Local.Models.SkillDisplayItem>()) text.AppendLine($"  {row.Name}  {row.BestLevel}");
        }
        Content = new TextBlock { Text = text.ToString(), Foreground = System.Windows.Media.Brushes.White };
    }
}

/// <summary>
/// 숫자 칸(◀ 값 ▶) — 도구 앱 편집 창들이 쓴다. 양끝을 누르면 한 칸씩, 가운데를 누르면 기기 글자판으로 바로 적는다.
/// </summary>
public class NumericSpinner : Control
{
    private readonly TextBlock _shown = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private double _value;

    public NumericSpinner()
    {
        Adopt(_shown);
        Background = System.Windows.Media.Brushes.White;
        BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAB, 0xAD, 0xB3));
        BorderThickness = new Thickness(1);
        Height = 22;
        MinWidth = 70;
        Refresh();
    }

    public double Minimum { get; set; } = double.MinValue;
    public double Maximum { get; set; } = double.MaxValue;
    public double Step { get; set; } = 1;
    public int DecimalPlaces { get; set; }

    public double Value
    {
        get => _value;
        set
        {
            double v = Math.Round(Math.Clamp(value, Minimum, Maximum), Math.Clamp(DecimalPlaces, 0, 10));
            if (v == _value) { Refresh(); return; }
            double old = _value;
            _value = v;
            Refresh();
            ValueChanged?.Invoke(this, new RoutedPropertyChangedEventArgs<double>(old, v));
        }
    }

    public event RoutedPropertyChangedEventHandler<double>? ValueChanged;

    private void Refresh() => _shown.Text = "◀ " + _value.ToString("F" + Math.Clamp(DecimalPlaces, 0, 10)) + " ▶";

    internal override IEnumerable<UIElement> VisualChildren => [_shown];

    protected override Size MeasureOverride(Size available)
    {
        _shown.Foreground = Foreground; _shown.FontSize = FontSize;
        _shown.Measure(available);
        return new Size(_shown.DesiredSize.Width + 8, _shown.DesiredSize.Height + 4);
    }

    protected override Size ArrangeOverride(Size final)
    {
        _shown.Arrange(new Rect(final));
        return final;
    }

    internal override bool HitSelf(Point p) => true;

    internal override void OnRaised(string name, RoutedEventArgs e)
    {
        if (name != "MouseLeftButtonUp" || !IsEnabled || e is not System.Windows.Input.MouseEventArgs m) return;
        e.Handled = true;
        double x = FromWindow(m.WindowPoint, false).X;
        if (x < ActualWidth * 0.3) Value -= Step;
        else if (x > ActualWidth * 0.7) Value += Step;
        else if (CdsHelper.Game.UI.Views.TextInputDialog.Ask(Window.GetWindow(this) ?? Window.Screen, _value.ToString(), 12, "값") is { } typed
                 && double.TryParse(typed, out double v)) Value = v;
    }
}
