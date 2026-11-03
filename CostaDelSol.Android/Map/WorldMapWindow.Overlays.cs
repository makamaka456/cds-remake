using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Engine.Town;
using CostaDelSol.Droid.Game;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 리메이크가 지도 위에 겹쳐 띄우는 상자들 — 좌표(왼쪽 위) · 만난 사람(오른쪽 위) · 컨디션(왼쪽 아래) · 미니맵(오른쪽 아래).
/// 글과 셈은 PC 판(WorldMapWindow 의 FillOverlay · FillPeople · FillVital · SyncMiniMap) 그대로이고, 상자는 지도 덧그림 판에 그린다
/// (창 판에 얹지 않으므로 지도 손을 막지 않는다). 켜고 끄는 것은 모드 · 개발 창이다.
/// </summary>
public sealed partial class WorldMapWindow
{
    /// <summary>지도 폭(판 점) — 덧그림 판이 그릴 때마다 적는다(16:9 면 640 보다 넓다).</summary>
    internal float MapBoardWidth { get; set; } = 640;

    private const float MapTop = 29, MapHeight = 480 - 58;

    private static Border Box(UIElement child, Thickness pad) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x10)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0xC8, 0x0B, 0x05, 0x05)),
        BorderThickness = new Thickness(1),
        Padding = pad,
        IsHitTestVisible = false,
        Child = child,
    };

    private static Popup MapPopup(UIElement child) => new()
    {
        Placement = PlacementMode.Absolute,
        AllowsTransparency = true,
        IsHitTestVisible = false,
        OnMap = true,
        Child = child,
    };

    private TextBlock? _coordText, _peopleBoxText, _vitalLine;
    private Canvas? _vitalBars;
    private Popup? _coordBox, _peopleBox, _vitalBox, _miniBox;
    private MiniMapView? _miniView;
    private DateTime? _miniQuietSince;
    private const double VitalWidth = 220;

    private static TextBlock BoxText() => new() { Foreground = Brushes.White, FontSize = 11, LineHeight = 14 };

    /// <summary>0.1초마다(게임 줄) — 켤 상자를 켜고 채운다.</summary>
    private void SyncOverlays()
    {
        bool up = OnMap && !_host.SeaBlocked && _host.ShipCell is not null;
        bool calm = up && !_asking && !_host.Paused && !GameThread.Panel.IsOpen;

        // 좌표 상자 — 지도 왼쪽 위.
        bool coord = up && GameSettings.ShowCoordOverlay;
        if (coord)
        {
            _coordText ??= BoxText();
            _coordBox ??= MapPopup(Box(_coordText, new Thickness(8, 4, 8, 4)));
            FillCoords();
            _coordBox.HorizontalOffset = 6;
            _coordBox.VerticalOffset = MapTop + 6;
        }
        if (_coordBox != null) _coordBox.IsOpen = coord;

        // 만난 사람 — 오른쪽 위.
        bool people = up && GameSettings.ShowPeopleOverlay;
        if (people)
        {
            _peopleBoxText ??= BoxText();
            _peopleBox ??= MapPopup(Box(_peopleBoxText, new Thickness(8, 4, 8, 4)));
            FillPeopleBox();
            _peopleBox.Child!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _peopleBox.HorizontalOffset = Math.Max(0, MapBoardWidth - _peopleBox.Child.DesiredSize.Width - 6);
            _peopleBox.VerticalOffset = MapTop + 6;
        }
        if (_peopleBox != null) _peopleBox.IsOpen = people;

        // 컨디션 — 왼쪽 아래.
        bool vital = up && GameSettings.ShowConditionOverlay;
        if (vital)
        {
            _vitalLine ??= BoxText();
            _vitalBars ??= new Canvas { Width = VitalWidth, Height = 10, Margin = new Thickness(0, 4, 0, 0) };
            _vitalBox ??= MapPopup(Box(new StackPanel { Children = { _vitalLine, _vitalBars } }, new Thickness(8, 4, 8, 5)));
            FillVitalBox();
            _vitalBox.HorizontalOffset = 6;
            _vitalBox.VerticalOffset = MapTop + MapHeight - 44;
        }
        if (_vitalBox != null) _vitalBox.IsOpen = vital;

        // 미니맵 — 오른쪽 아래. 창이 뜨면 숨겼다가 조용해지고 1초 뒤 다시 낸다(PC 판 MiniCalm).
        if (!calm) _miniQuietSince = null; else _miniQuietSince ??= DateTime.UtcNow;
        bool mini = up && calm && GameSettings.ShowMiniMap && DateTime.UtcNow - _miniQuietSince >= TimeSpan.FromSeconds(1);
        if (mini)
        {
            _miniView ??= new MiniMapView();
            if (!_miniView.HasChart)
            {
                var all = new ExploredMap();
                all.RevealAll();
                if (_host.Chart(all, out int w, out int h) is { } chart) _miniView.SetChart(chart, w, h);
                else mini = false;
            }
            if (mini && _host.ShipCell is { } cell)
            {
                _miniView.Update(_game.Discoveries?.Table, _game.Player, cell.CellX, cell.CellY);
                _miniBox ??= MapPopup(_miniView);
                _miniBox.HorizontalOffset = Math.Max(0, MapBoardWidth - MiniMapView.ViewW - 6);
                _miniBox.VerticalOffset = MapTop + MapHeight - MiniMapView.ViewH - 6;
            }
        }
        if (_miniBox != null) _miniBox.IsOpen = mini;
    }

    /// <summary>지도를 떠나면 모두 걷는다.</summary>
    private void HideOverlays()
    {
        foreach (var p in new[] { _coordBox, _peopleBox, _vitalBox, _miniBox })
            if (p is { IsOpen: true }) p.IsOpen = false;
    }

    /// <summary>좌표 상자 — PC 판 FillOverlay 의 글 그대로(타일 그림은 뺐다).</summary>
    private void FillCoords()
    {
        if (_host.ShipCell is not { } v) { _coordText!.Text = "배가 아직 지도에 없습니다"; return; }
        var (lat, lon) = _host.ShipLatLon;
        var lines = new List<string>
        {
            $"칸        {v.X,7:F1}, {v.Y,6:F1}   (칸 {v.CellX}, {v.CellY})",
            $"WORLD.CDS 행 {v.Row,4} · 열 {v.Col,4} · 0x{v.Offset:X5}",
            $"칸 값     지형 {v.Terrain,3} · 속성 {v.Attr,3} · 타일 {v.Tile,5} · 육지 {v.LandRatio * 100,3:F0}%",
            $"위경도    {(lat >= 0 ? "북위" : "남위")} {Math.Abs(lat):F2} · {(lon >= 0 ? "동경" : "서경")} {Math.Abs(lon):F2}",
        };
        lines.AddRange(SpeedLines());
        var (city, cells) = _host.NearestDock(40);
        if (city >= 0) lines.Add($"가까운 항구 [{_game.CityName(city)}] {cells:F1}칸");
        if (_host.MouseCell is { } m) lines.Add($"커서      {m.X,7:F1}, {m.Y,6:F1}   0x{m.Offset:X5}");
        _coordText!.Text = string.Join("\n", lines);
    }

    /// <summary>만난 사람 상자 — PC 판 FillPeople 그대로.</summary>
    private void FillPeopleBox()
    {
        var player = _game.Player;
        var left = new List<string> { "여급 (친밀도 · 궁합)" };
        int mine = Engine.Town.Barmaids.FortuneOf(player);
        var table = _game.Barmaids;
        foreach (var (id, liking) in player.Liking.OrderByDescending(p => p.Value))
        {
            string name = table?.Find(id)?.Name ?? $"{id}번";
            string fit = table?.Find(id) is { } her ? BarmaidTable.Destined(mine, her.Fortune) ? "궁합 ○" : "궁합 ×" : "";
            left.Add($"  {GameUi.Pad(name, 16)}{liking,4}  {fit}");
        }
        if (left.Count == 1) left.Add("  아직 말을 걸어 본 여급이 없다");
        var right = new List<string> { "만난 인물" };
        foreach (string name in player.Met.OrderBy(n => n, StringComparer.Ordinal)) right.Add($"  {name}");
        if (right.Count == 1) right.Add("  아직 만난 인물이 없다");
        var lines = new List<string>();
        for (int i = 0; i < Math.Max(left.Count, right.Count); i++)
            lines.Add(GameUi.Pad(i < left.Count ? left[i] : "", 34) + (i < right.Count ? right[i] : ""));
        _peopleBoxText!.Text = string.Join("\n", lines);
    }

    /// <summary>컨디션 상자 — PC 판 FillVital 그대로.</summary>
    private void FillVitalBox()
    {
        int hp = _game.Player.Condition;
        string state = hp <= 0 ? "쓰러짐" : hp < Vitality.Faint ? "위험" : hp < Vitality.Pale ? "창백" : "괜찮음";
        _vitalLine!.Text = $"컨디션 {hp,4} / {Player.ConditionMax} · {state}";
        var fill = hp < Vitality.Faint ? Color.FromRgb(0xD0, 0x40, 0x30)
                 : hp < Vitality.Pale ? Color.FromRgb(0xE0, 0xA0, 0x30)
                 : Color.FromRgb(0x50, 0xB0, 0x60);
        double Scale(int v) => VitalWidth * Math.Clamp(v, 0, Player.ConditionMax) / Player.ConditionMax;
        var bars = _vitalBars!;
        bars.Children.Clear();
        bars.Children.Add(new System.Windows.Shapes.Rectangle { Width = VitalWidth, Height = 10, Fill = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)) });
        bars.Children.Add(new System.Windows.Shapes.Rectangle { Width = Scale(hp), Height = 10, Fill = new SolidColorBrush(fill) });
        foreach (int mark in new[] { Vitality.Faint, Vitality.Pale })
        {
            var tick = new System.Windows.Shapes.Rectangle { Width = 1, Height = 10, Fill = Brushes.Gold };
            Canvas.SetLeft(tick, Scale(mark));
            bars.Children.Add(tick);
        }
    }
}
