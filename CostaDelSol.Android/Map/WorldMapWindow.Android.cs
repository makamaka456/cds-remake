using Android.App;
using Android.Widget;
using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CostaDelSol.Droid.Game;
using CostaDelSol.Droid.Input;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 바다 화면의 안드로이드 쪽 — PC 판 생성자 · 상태 시계(0.1초 틱) · 글쇠 · 마우스 · 알림 띠 · 타이틀 오가기 자리를 채운다.
/// 모두 게임 줄(GameThread)에서 돈다 — PC 판이 한 줄(UI 스레드)에서 돌던 것과 같다.
/// </summary>
public sealed partial class WorldMapWindow
{
    private readonly WorldMapHost _host = new();
    private readonly Engine.Game _game = new();
    private bool _started;
    private int _askedCity = -1;
    private bool _asking;

    // ── PC 판 화면 칸들의 자리(값만 든다) ─────────────────────────────────

    private sealed class TextHolder { public string Text { get; set; } = ""; }
    private sealed class Ticker { public bool Running; public void Start() => Running = true; public void Stop() => Running = false; }
    private sealed class Screen { public object? Content { get; set; } }

    private readonly TextHolder _status = new();
    private readonly Ticker _statusTimer = new();
    private readonly Screen _screen = new();
    private readonly object _mapRoot = new();
    private readonly object _titleRoot = new();

    private readonly SeaWeather _seaWeather = new();
    private WindTable? _weatherWind;
    private const int RainSoundPart = 0x3F - WaveBank.FirstSoundId;

    /// <summary>판(주인공 · 표 · 곡)을 밖에서 쓴다 — 설정 · 저장 · 띠.</summary>
    public Engine.Game Game => _game;

    /// <summary>지도 엔진 — GL 지도와 덧그림 판이 그 프레임을 그린다.</summary>
    public WorldMapHost Host => _host;

    /// <summary>바다 화면이 떠 있는지(타이틀이 아니고 판이 시작됐다).</summary>
    public bool OnMap => _started && ReferenceEquals(_screen.Content, _mapRoot);
    /// <summary>놀이가 시작됐다(타이틀을 지났다).</summary>
    public bool Started => _started;

    /// <summary>지금 비 · 눈(그림은 덧그림 판이 그린다).</summary>
    public SeaWeather.Kind Weather { get; private set; } = SeaWeather.Kind.None;

    /// <summary>알림 띠에 글이 왔다(화면 줄에서 띠가 깜빡이며 낸다).</summary>
    public event Action<string>? Noted;

    /// <summary>타이틀로 돌아가야 한다(화면 줄에서 타이틀을 다시 띄운다).</summary>
    public event Action? TitleWanted;

    /// <summary>바다 판이 섰다(지도를 띄운다).</summary>
    public event Action? MapShown;

    public WorldMapWindow()
    {
        _screen.Content = _titleRoot;
        // 안 선 도시도, 아직 모르는 도시도 다가가도 안 물어보고 지도에서도 지운다.
        _host.CityOpen = _game.CityVisible;
        // 지도에 남의 배를 낸다 — 누가 어디 있는지는 인물 세상이 안다.
        _host.FolkAt = FolkAfloat;
        // 자동항해가 도착하거나 막혀서 스스로 멎으면 아래 띠로 알린다.
        _host.AutoSailEnded += Say;
        Current = this;
    }

    internal static WorldMapWindow? Current { get; private set; }

    /// <summary>판 폴더를 정한다(PC 판 OnLoaded 의 앞부분).</summary>
    public void SetDirectory(string dir)
    {
        _game.SetDirectory(dir);
        _game.Bgm.Enabled = GameSettings.BgmEnabled;
    }

    // ── 타이틀 차림표에서 부른다(게임 줄) ───────────────────────────────

    public void TitleNewGame() => NewGame();
    public void TitleLoadGame() => StartMap(fresh: false);
    public void TitleContinue() => StartMap(fresh: false, auto: true);
    public void TitleMiniGames() => MiniGames();
    public bool HasAutoSave => File.Exists(GameSave.AutoPath);

    // ── 프레임 · 틱(게임 줄) ─────────────────────────────────────────────

    private double _tickAccum;

    /// <summary>
    /// 한 프레임 — 지도 엔진을 돌리고(PC 판 CompositionTarget.Rendering), 0.1초마다 상태 시계의 한 틱을 돈다.
    /// </summary>
    public void Frame(double dt, int width, int height)
    {
        if (!OnMap) { HideOverlays(); return; }
        _host.Step(dt, width, height);
        _tickAccum += dt;
        if (_tickAccum < 0.1) return;
        _tickAccum = 0;
        if (_statusTimer.Running) StatusTick();
        SyncOverlays();
    }

    /// <summary>PC 판 상태 시계 한 틱의 진행 부분 그대로(칸 글은 띠가 주인공에서 바로 읽는다).</summary>
    private void StatusTick()
    {
        // 한 틱의 차례는 원본 고리 그대로다(0x0048EF18~0x0048EF7D) —
        // 조우 → 극지 → 발견 → 도시 발견·입항 물음 → 이동·날 눈금. 조우가 걸린 틱은 나머지를 통째로 건너뛴다.
        _folkEntered = MeetFolk();
        if (_folkEntered) return;
        if (!CheckPolar()) return;
        CheckDiscovery();
        SpotCities();
        CheckPort();
        PassTime();
        MarkSeen();
        // 커서 쪽 길찾기 보정은 항해사(자리 1)와 나침반이 있을 때만 든다(0x0048ECEF).
        _host.PathAssist = _game.Player.MateAt(NavigatorSlot).Length > 0 && _game.Player.Items.Contains(CompassItem);
        // 그림은 기함 것으로 그린다 — 항구 함대편성에서 기함을 바꾸면 배 모양도 바뀐다.
        ShipSprites.Use(_game.Player.FlagshipHull?.Hull);
        SyncSeaMusic();
    }

    // ── 글쇠 · 마우스(화면 줄에서 게임 줄로 넘어온다) ────────────────────

    /// <summary>원본 지도 글쇠(<c>OnMapKey</c>) — 숫자판 조타 · 0 커맨드 · D 발견물 지도 · V 저장 · ESC.</summary>
    public void Key(GameKey key)
    {
        if (!OnMap) return;
        if (key == GameKey.Escape)
        {
            if (CommandMenu.IsOpen) { CommandMenu.Close(); return; }
            if (InfoMenu.IsOpen) { InfoMenu.Close(); return; }
            return;
        }
        if (key is >= GameKey.D1 and <= GameKey.D9)
        {
            if (_host.SeaBlocked || _host.Paused) return;
            int pad = (key - GameKey.D0) switch { 1 => 6, 2 => 8, 3 => 10, 4 => 4, 5 => -1, 6 => 12, 7 => 2, 8 => 0, _ => 14 };
            if (pad < 0) _host.ToggleAnchor(); else _host.SteerTo(pad);
            return;
        }
        if (key == GameKey.D0) { ShowCommandMenu(); return; }
        if (key == GameKey.D)
        {
            // 안드로이드에서는 Select 단추가 이 글쇠라 늘 연다(PC 판은 모드에서 켜야 먹는다).
            Hold(ShowDiscoveryMap);
            return;
        }
        if (key == GameKey.V) Hold(() => SaveByKey(this));
    }

    /// <summary>지도를 눌렀다 — 왼쪽은 닻(커서 조타를 깨운다), 오른쪽은 커맨드 창(PC 판 그대로).</summary>
    public void Click(bool left)
    {
        if (!OnMap || _host.SeaBlocked || _host.Paused) return;
        if (!left) { ShowCommandMenu(); return; }
        _host.SteerArmed = true;
        _host.ToggleAnchor();
        _game.Sfx?.Play(SoundBank.AnchorPart);
    }

    /// <summary>커서(화면 픽셀, 지도 판 기준).</summary>
    public void Mouse(double x, double y, bool inside) => _host.SetMouse(new Point(x, y), inside);

    private void ShowCommandMenu()
    {
        if (_host.SeaBlocked) return;
        if (CommandMenu.IsOpen) { CommandMenu.Close(); return; }   // 0 을 한 번 더 누르면 닫힌다
        CommandMenu.Open(CommandMenuBox);
        _host.Paused = true;
    }

    // ── 알림 띠 ──────────────────────────────────────────────────────────

    public void Say(string text)
    {
        // 게임은 글을 넣기 앞서 통을 비운다(0x0040E0D7) — 그래서 통에는 마지막 하나만 남는다.
        if (text.Length > 0) _lastNote = text;
        Noted?.Invoke(text);
    }

    /// <summary>알림 띠를 눌렀다 — 마지막 글을 「Information」 창으로 다시 본다.</summary>
    public void ReadNote()
    {
        if (_lastNote.Length == 0) return;
        NoticeDialog.Show(this, _lastNote, "Information");
    }

    // ── 타이틀 · 불러오기 ────────────────────────────────────────────────

    public void ReturnToTitle()
    {
        if (ReferenceEquals(_screen.Content, _titleRoot)) return;
        CommandMenu.Close();
        InfoMenu.Close();
        _statusTimer.Stop();
        _askedCity = -1;
        EndWeather();
        _screen.Content = _titleRoot;
        _game.Bgm.Play(BgmPlayer.TitleTrack);
        TitleWanted?.Invoke();
    }

    public void LoadGame()
    {
        CommandMenu.Close();
        InfoMenu.Close();
        _askedCity = -1;
        StartMap(fresh: false);
    }

    internal void SaveByKey(Window owner)
    {
        if (!OnMap) return;
        _game.Player.SetSeaCell(_host.SeaSpot);
        GameSystemMenu.Save(owner, _game);
    }


    private static void WithMenuHidden(Window? menu, Action show)
    {
        if (menu != null) menu.Visibility = Visibility.Hidden;
        try { show(); }
        finally { if (menu != null) menu.Visibility = Visibility.Visible; }
    }

    private void HideTitleMenu(bool hide) { }

    private void SyncOverlay()
    {
        // StartMap 이 지도를 세우고 부른다 — 화면 줄에 지도를 띄우게 알린다.
        if (OnMap) MapShown?.Invoke();
    }

    private void LoadSprites() { }

    private void SetInCity(bool on) => _host.InCity = on;

    /// <summary>지도 자리(원본 점) — 두 띠 사이다. 사건 그림 · 효과가 이 안에 선다.</summary>
    private Rect MapAreaOnScreen() => new(0, 29, 640, 480 - 58);

    // ── 날씨(비 · 눈) — 셈은 원본(SeaWeather), 그림은 덧그림 판 ──────────

    private void RollWeather()
    {
        var (lat, lon) = _host.ShipLatLon;
        int latRaw = (int)((90 - lat) / 180 * 20000), lonRaw = (int)((lon + 180) / 360 * 40000);
        _weatherWind ??= WindTable.Open(_game.Directory);
        int zone = _weatherWind?.ZoneAt(WindTable.CellOf(lonRaw, latRaw)) ?? -1;
        if (_seaWeather.Roll(zone, _game.Player.Date.Month, latRaw, _game.Random) is not { } now) return;

        Weather = now;
        if (now == SeaWeather.Kind.None)
        {
            _game.Sfx?.StopLoop();          // 0x00422A40(0x3F, 3)
            return;
        }
        if (now == SeaWeather.Kind.Rain) _game.Sfx?.PlayLoop(RainSoundPart);
    }

    private void EndWeather()
    {
        _seaWeather.Stop();
        Weather = SeaWeather.Kind.None;
        _game.Sfx?.StopLoop();
    }

    // ── 지도 창들(4단계에서 원본 화면으로) ───────────────────────────────

    internal void PlayEventScene(int scene)
    {
        var (w, h) = _host.SurfaceSize;
        Point? ship = null;
        if (_host.ShipOnSurface is { } p && w > 0 && h > 0)
            ship = new Point(p.X * 640.0 / w, p.Y * (480 - 58.0) / h);
        EventAnimationPopup.Play(this, _game, scene, MapAreaOnScreen(), 1, ship);
    }

    internal void ShowSeaChart(Window owner, Window? menu) =>
        WithMenuHidden(menu, () => SeaChartDialog.ShowWorld(owner, _host, _game.Player.Explored));

    /// <summary>타이틀 바탕 무늬(놀이 끝 화면도 같이 쓴다) — PC 판 TitleBackground 그대로.</summary>
    internal static System.Windows.Media.Brush TitleBackground()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "asset", "title", "title-tile.png");
        var fallback = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x31, 0x18, 0x18));
        if (!File.Exists(path)) return fallback;
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        if (bmp.PixelWidth == 0) return fallback;
        return new System.Windows.Media.ImageBrush(bmp)
        {
            TileMode = System.Windows.Media.TileMode.Tile,
            ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute,
            Viewport = new System.Windows.Rect(0, 0, bmp.PixelWidth * TilePack, bmp.PixelHeight * TilePack),
            Stretch = System.Windows.Media.Stretch.Fill,
        };
    }

    // ── 앱 차림표(PC 판 제목 줄 햄버거) ──

    /// <summary>설정 — PC 판 설정 창. 지도 배율은 고르는 그 자리에서 지도에 먹인다.</summary>
    public void ShowSettingsDialog() => SettingsDialog.Show(this, _game.Bgm, s => _host.ApplyMapScale(s));

    public void ShowBarmaidBook() => BarmaidBookDialog.Show(this, _game);

    public void ShowPersonMove() => PersonMoveDialog.Show(this, _game);

    /// <summary>모드 — 원본에 없는 편의 기능을 켜고 끈다.</summary>
    public void ShowModDialog() => ModDialog.Show(this, new ModDialog.Options
    {
        MiniMapOn = () => GameSettings.ShowMiniMap,
        SetMiniMap = on => GameSettings.ShowMiniMap = on,
        ConditionOn = () => GameSettings.ShowConditionOverlay,
        SetCondition = on => GameSettings.ShowConditionOverlay = on,
        ArrowsOn = () => _host.ShowFlowArrows,
        SetArrows = on =>
        {
            _host.ShowFlowArrows = on;
            GameSettings.ShowFlowArrows = on;
        },
    });

    /// <summary>개발 — 소지금 · 명성을 손으로 넣고, 모의 싸움(일기토 · 육상전 · 해전)을 돌린다.</summary>
    public void ShowDevDialog() => DevDialog.Show(this, _game.Player, new DevDialog.Options
    {
        CoordsOn = () => GameSettings.ShowCoordOverlay,
        SetCoords = on => GameSettings.ShowCoordOverlay = on,
        RuseTable = () => RuseEditDialog.Show(this),
        Duel = PlayDuel,
        LandSpar = () => LandSparDialog.Play(this, _game),
        SeaSpar = MockSeaBattle,
        ToolBarOn = () => GameSettings.ShowToolBar,
        SetToolBar = on => GameSettings.ShowToolBar = on,
        AutoSail = () =>
        {
            if (_host.IsOnLand) Say("바다에 있을 때만 자동항해를 쓸 수 있습니다");
            else ShowAutoSailDialog();
        },
    });

    /// <summary>
    /// 도구 — 리메이크 도구 앱(CdsHelperWindow) 차림표의 게임 자료 편집 창들. 창은 PC 판 그대로다.
    /// </summary>
    public static readonly (string Name, Action<Window> Open)[] Tools =
    [
        ("게임 데이터", w => GameDataDialog.Show(w)),
        ("초상화 도감", w => new PortraitBookDialog { Owner = w }.ShowDialog()),
        ("도시 · 문화권 · 왕국", w => new CityCultureDialog { Owner = w }.ShowDialog()),
        ("나라 편집", w => NationEditDialog.Show(w)),
        ("선수상 편집", w => FigureheadEditDialog.Show(w)),
        ("문화권 편집", w => CityCultureEditDialog.Show(w)),
        ("사건 대본 편집기", w => DisevEditorDialog.Show(w)),
        ("항해자 편집", w => VoyagerEditDialog.Show(w)),
        ("건물 목록", w => BuildingListDialog.Show(w)),
        ("인물 편집", w => PersonEditDialog.Show(w)),
        ("육상전 진형", w => LandFormationDialog.Show(w)),
        ("운명 자리(여급 궁합)", w => FortuneDialog.Show(w)),
        ("술집 힌트 편집", w => TavernHintEditDialog.Show(w)),
        ("발견물 편집", w => DiscoveryEditDialog.Show(w)),
        ("책 편집", w => BookEditDialog.Show(w)),
        ("묘책 확률 표", w => RuseEditDialog.Show(w)),
        ("메뉴 디자이너", w => new MenuDesignerDialog { Owner = w }.ShowDialog()),
        ("모션 메이커(일기토 그림)", w => new MotionMakerDialog { Owner = w }.ShowDialog()),
        // 도구 앱 본체 화면(XAML 틀을 코드로 옮긴 것)
        .. CostaDelSol.Droid.Tools.ToolPages.Pages,
    ];

    public void OpenTool(int at) => Tools[at].Open(this);

    /// <summary>D 글쇠 — 어디서나 발견물 지도를 편다(PC 판 MapByKey).</summary>
    internal void MapByKey()
    {
        ShowDiscoveryMap();
    }

    private void ShowDiscoveryMap()
    {
        var all = new ExploredMap();
        all.RevealAll();

        var chart = _host.Chart(all, out int w, out int h);
        if (chart == null) { NoticeDialog.Show(this, "지도를 아직 못 읽었습니다"); return; }

        var at = _host.ShipCell is { } cell ? ((double, double)?)(cell.CellX, cell.CellY) : null;
        DiscoveryMapDialog.Show(this, chart, w, h, _game.Discoveries?.Table, _game.Player, at,
                                WindTable.Open(_game.Directory), WarpTo, AutoSail);
    }

    // ── 위 띠 칸 · 도시정보 창(PC 판 InfoCell · ShowCityInfoMenu) ──────────

    /// <summary>띠 칸의 차례와 기본 켬 — PC 판 gameCells 에 꽂는 차례 그대로.</summary>
    private static readonly (string Name, bool On)[] BarOrder =
    [
        (CityInfoMenu.Date, true), (CityInfoMenu.Crew, true), (CityInfoMenu.Stores, false),
        (CityInfoMenu.DaysLeft, false), (CityInfoMenu.Wind, false), (CityInfoMenu.Coord, true),
        (CityInfoMenu.Gold, true), (CityInfoMenu.Fame, true), (CityInfoMenu.Fatigue, false),
        (CityInfoMenu.Morale, true), (CityInfoMenu.City, false), (CityInfoMenu.Language, false),
        (CityInfoMenu.Rate, false), (CityInfoMenu.Current, false), (CityInfoMenu.Vitality, false),
    ];

    /// <summary>띠 칸을 켜고 끄면 알린다 — 안드로이드 띠(SeaBars)가 다시 그린다.</summary>
    public event Action? BarCellsChanged;

    /// <summary>그 칸이 켜져 있는지. 한 번도 안 건드렸으면 기본값이다(PC 판 InfoCell).</summary>
    private static bool BarOn(string name) =>
        GameSettings.BarCells?.Contains(name) ?? BarOrder.FirstOrDefault(c => c.Name == name).On;

    /// <summary>켜진 칸들의 글 — 서식은 PC 판 상태 시계(0x0047DCE2 · 0x0056BEA8 · 0x0056BFB0 …) 그대로.</summary>
    public IReadOnlyList<string> BarCells()
    {
        var p = _game.Player;
        var list = new List<string>();
        foreach (var (name, _) in BarOrder)
        {
            if (!BarOn(name)) continue;
            list.Add(name switch
            {
                CityInfoMenu.Date => $"{p.Date.Year,4}년{p.Date.Month,2}월{p.Date.Day,2}일",
                CityInfoMenu.Crew => $"{(_host.IsOnLand ? "대원" : "선원")}{p.Crew,4}명",
                CityInfoMenu.Stores => $"물{p.SupplyOf(SupplyKind.Water),4}통 식량{p.SupplyOf(SupplyKind.Food),4}통",
                CityInfoMenu.DaysLeft => p.Contract is { } deal ? $"남은일수{deal.DaysLeftOn(p.Date),4}" : "남은일수----",
                CityInfoMenu.Wind => WindLine(),
                CityInfoMenu.Coord => BarCoord(),
                CityInfoMenu.Gold => $"소지금{p.Gold,6}닢",
                CityInfoMenu.Fame => $"명성{p.Fame,6}",
                CityInfoMenu.Fatigue => $"피로도{p.Fatigue,4}",
                CityInfoMenu.Morale => $"규칙{p.Morale,4}",
                CityInfoMenu.City => p.CityName.Length > 0 ? p.CityName : "-------------------",
                CityInfoMenu.Language => CityLanguage(),
                CityInfoMenu.Rate => CityRate(),
                CityInfoMenu.Current => CurrentLine(),
                CityInfoMenu.Vitality => $"HP:{p.Condition,4}",
                _ => name,
            });
        }
        return list;
    }

    /// <summary>좌표 칸 — 육분의가 있어야 보인다(0x0047DCE2 가 0x0047CE20(0x22) 를 본다).</summary>
    private string BarCoord()
    {
        var (lat, lon) = _host.ShipLatLon;
        return _game.Player.Items.Contains(SextantItem)
            ? $"{(lat >= 0 ? "북" : "남")}위 {Math.Abs(lat),3:F0}  {(lon >= 0 ? "동" : "서")}경 {Math.Abs(lon),3:F0}  "
            : "위도 ---′ 경도 ---′";
    }

    /// <summary>
    /// 위 띠를 길게 눌렀다(PC 판은 오른쪽 단추) — 띠에 무엇을 띄울지 켜고 끄는 「도시정보」 창.
    /// 바다에서는 창이 떠 있는 동안 배를 세운다.
    /// </summary>
    public void ShowCityInfoMenu()
    {
        // 원본은 도시 안에서만 낸다 — 우리는 바다·뭍에서도 낸다(PC 판과 같다).
        if (!OnMap) return;
        if (InfoMenu.IsOpen) { InfoMenu.Close(); return; }
        CommandMenu.Close();
        InfoMenu.Open(BuildCityInfo);
        if (!_host.InCity && !_host.IsOnLand) _host.Paused = true;
    }

    private GameMenu BuildCityInfo() => CityInfoMenu.Build(
        name => BarOrder.Any(c => c.Name == name) ? BarOn(name) : null,
        name =>
        {
            var on = BarOrder.Where(c => BarOn(c.Name)).Select(c => c.Name).ToList();
            if (!on.Remove(name)) on.Add(name);
            GameSettings.BarCells = on;
            InfoMenu.Refresh();
            BarCellsChanged?.Invoke();
        },
        InfoMenu.Close);
}
