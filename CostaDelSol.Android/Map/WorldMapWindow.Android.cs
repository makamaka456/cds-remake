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
    /// <summary>LOAD GAME — 손으로 적은 세이브 칸(열 칸까지)을 목록에서 고른다(PC 판과 같다).</summary>
    public void TitleLoadGame()
    {
        if (PickManualSave(this) is { } file) StartMap(fresh: false, path: file);
    }

    /// <summary>CONTINUE — 입항 자동저장 칸을 목록에서 고른다(PC 판과 같다).</summary>
    public void TitleContinue()
    {
        if (PickAutoSave() is { } file) StartMap(fresh: false, path: file);
    }

    public void TitleMiniGames() => MiniGames();
    public bool HasManualSave => GameSave.HasManualSave();
    public bool HasAutoSave => GameSave.HasAutoSave();

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

    /// <param name="path">열 세이브 — 안 주면 손으로 적은 세이브(불러오기 목록 화면이 고른 칸을 준다).</param>
    public void LoadGame(string? path = null)
    {
        CommandMenu.Close();
        InfoMenu.Close();
        _askedCity = -1;
        StartMap(fresh: false, path);
    }

    internal void SaveByKey(Window owner)
    {
        if (!OnMap) return;
        _game.Player.SetSeaCell(_host.SeaSpot);
        GameSystemMenu.Save(owner, _game);
    }


    /// <param name="holdNotes">겹쳐 보기 쪽지도 걷어 둘지(PC 판 — 지도 창이 쪽지를 덮는다).</param>
    private static void WithMenuHidden(Window? menu, Action show, bool holdNotes = false)
    {
        if (menu != null) menu.Visibility = Visibility.Hidden;
        using var notes = holdNotes ? OverlayNotes.Hold() : null;
        try { show(); }
        finally { if (menu != null) menu.Visibility = Visibility.Visible; }
    }

    /// <summary>스폰서 일람 — 아는 후원자와 권력 · 친밀도. 도시 창이 없는 바다 · 뭍에서도 같은 창을 낸다(PC 판 그대로).</summary>
    private void ShowPatrons(Window owner) =>
        new PatronMenu(owner, _game, "", CommandMenu, CommandMenu, 0, 0, -1).ShowPatrons(owner);

    /// <summary>첫 출항 갈매기(장면 16)를 이미 치렀는가 — 원본 항해 고리의 <c>[ebp-0x2C]</c>.</summary>
    private bool _gullsShown;

    /// <summary>
    /// 대 둔 배 곁에서 승선을 이미 물었는지. 배에 타 있는 동안은 참이라 상륙하자마자 묻지 않고,
    /// 배에서 멀어지면 거짓이 되어 다시 다가올 때 한 번 묻는다(<see cref="CheckBoard"/>).
    /// </summary>
    private bool _askedBoard = true;

    /// <summary>속도 값 몇이 1노트인지 — 작위 혜택 「항해 속도 +1노트」가 더하는 몫. 적어도 1(PC 판 그대로).</summary>
    private static readonly int SpeedPerKnot =
        Math.Max(1, (int)Math.Round(10.0 / Math.Max(1e-6, KnotsOf(110) - KnotsOf(100))));

    private static double KnotsOf(int speed)
    {
        double cells = (Engine.Sea.Sailing.CellsPerTick(speed, true, false)
                        + Engine.Sea.Sailing.CellsPerTick(speed, false, false)) / 2;
        return cells * TerrainTable.TicksPerDay * NauticalMilesPerCell / 24;
    }

    /// <summary>
    /// 지금 자리의 기후대(0~12) — 바람 표 낱말의 비트 8~11(<c>0x00424FA0</c>)이다. 표를 못 읽으면 -1.
    /// </summary>
    /// <param name="latRaw">원본 위도 값(0~20000)을 함께 내준다.</param>
    private int ClimateZone(out int latRaw)
    {
        var (lat, lon) = _host.ShipLatLon;
        latRaw = (int)((90 - lat) / 180 * 20000);
        int lonRaw = (int)((lon + 180) / 360 * 40000);
        _weatherWind ??= WindTable.Open(_game.Directory);
        return _weatherWind?.ZoneAt(WindTable.CellOf(lonRaw, latRaw)) ?? -1;
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

    /// <summary>모드 — 원본에 없는 편의 기능을 켜고 끈다(PC 판 ShowModDialog 와 같은 줄).</summary>
    /// <remarks>
    /// 지도 그림 새 효과(바다 효과 · 밝기 · 물결 · 고해상 바다 · 지형 세부 · 항적 · 점 거르기 · 구름)는 값만 적어 둔다 —
    /// 안드로이드 GL 지도가 아직 그리지 않는다(다음 판에서 옮긴다).
    /// </remarks>
    public void ShowModDialog()
    {
        if (_modOpen) return;
        _modOpen = true;
        try
        {
            ModDialog.Show(this, new ModDialog.Options
            {
                DiscoveryCountOn = () => GameSettings.ShowDiscoveryCount,
                SetDiscoveryCount = on => GameSettings.ShowDiscoveryCount = on,
                MiniMapOn = () => GameSettings.ShowMiniMap,
                SetMiniMap = on => GameSettings.ShowMiniMap = on,
                MiniMapOpacity = () => GameSettings.MiniMapOpacity,
                SetMiniMapOpacity = v => GameSettings.MiniMapOpacity = v,
                ArrowsOn = () => _host.ShowFlowArrows,
                SetArrows = on =>
                {
                    _host.ShowFlowArrows = on;
                    GameSettings.ShowFlowArrows = on;
                },
                SeaOn = () => GameSettings.SeaEffect,
                SetSea = on => GameSettings.SeaEffect = on,
                SeaBrightness = () => GameSettings.SeaBrightness,
                SetSeaBrightness = v => GameSettings.SeaBrightness = v,
                SeaFlowAmount = () => GameSettings.SeaFlowAmount,
                SetSeaFlowAmount = v => GameSettings.SeaFlowAmount = v,
                HiResSeaOn = () => GameSettings.HiResSea,
                SetHiResSea = on => GameSettings.HiResSea = on,
                LandDetailOn = () => GameSettings.LandDetail,
                SetLandDetail = on => GameSettings.LandDetail = on,
                ShipWakeOn = () => GameSettings.ShipWake,
                SetShipWake = on => GameSettings.ShipWake = on,
                PixelFilterOn = () => GameSettings.PixelFilter,
                SetPixelFilter = on => GameSettings.PixelFilter = on,
                SmoothCloudsOn = () => GameSettings.SmoothClouds,
                SetSmoothClouds = on => GameSettings.SmoothClouds = on,
            });
        }
        finally { _modOpen = false; }
        _refreshApps();
        _host.RefreshGrownCities();   // 「도시 투자 성장」을 켜고 껐으면 자란 도시 그림을 바꾼다
    }

    private bool _modOpen;

    /// <summary>개발 — PC 판과 같은 줄(도우미 앱 띄우기만 뺀다 — 사용자 결정).</summary>
    public void ShowDevDialog() => DevDialog.Show(this, _game.Player, new DevDialog.Options
    {
        GiveItem = GiveItemToSave,
        CoordsOn = () => GameSettings.ShowCoordOverlay,
        SetCoords = on => GameSettings.ShowCoordOverlay = on,
        RuseTable = () => RuseEditDialog.Show(this),
        Scenarios = () => ScenarioEditDialog.Show(this, _game),
        Duel = PlayDuel,
        LandSpar = () => LandSparDialog.Play(this, _game),
        SeaSpar = MockSeaBattle,
        YearChanged = back => { if (back) _game.ResetWorld(); },
        Quest = () => QuestDialog.Show(this, _game),
        Discoveries = _game.Discoveries?.Table,
        DiscoveriesChanged = () => HideCities(),
        EventDone = id => _game.Player.IsSettled(id) && !Engine.Disev.DisevRunner.RecordsDiscovery(_game, id),
        UserDiscoveries = () =>
        {
            UserDiscoveryDialog.Show(this, DiscoveryAreaMap, _game);
            HideCities();
        },
        HistoryEvents = () => _game.HistoryCatalog(),
        Barmaids = _game.Barmaids,
        CityName = _game.CityName,
        Faces = _game.Faces,
    });

    /// <summary>
    /// 개발 창 「아이템 넣기」 — 세이브 파일을 고르고, 아이템을 골라 그 주인공 소지품에 넣는다. 그만둘 때까지 이어 넣는다.
    /// </summary>
    /// <remarks>파일만 고친다 — 지금 떠 있는 판에는 안 들고, 그 세이브를 불러와야 보인다.</remarks>
    private void GiveItemToSave()
    {
        if (_game.Items is not { } table) { NoticeDialog.Show(this, "아이템 표를 읽지 못했습니다."); return; }

        var slots = GameSave.ManualSaves().Select(s => (Slot: s, Auto: false))
            .Concat(GameSave.AutoSaves().Select(s => (Slot: s, Auto: true))).ToList();
        if (slots.Count == 0) { NoticeDialog.Show(this, "저장한 데이터가 없습니다"); return; }

        var rows = slots.Select(s =>
        {
            var d = s.Slot.Save;
            string who = !string.IsNullOrEmpty(d.Name) ? d.Name
                       : $"{d.Given} {d.Family}".Trim() is { Length: > 0 } whole ? whole : "이름 없는 제독";
            return new[]
            {
                s.Auto ? who + " (자동)" : who, string.IsNullOrEmpty(d.CityName) ? "바다" : d.CityName,
                $"{d.SavedAt:yyyy-MM-dd HH:mm}", $"{d.Items?.Count ?? 0}개",
            };
        }).ToList();
        int pick = SaveListDialog.Pick(this, "아이템 넣기 - 세이브 고르기", ["캐릭터", "도시", "저장한 시각", "소지품"], rows);
        if (pick < 0) return;
        string file = slots[pick].Slot.File;

        var items = table.Items.Where(i => !string.IsNullOrWhiteSpace(i.Name)).ToList();
        var lines = items.Select(i => GameUi.Pad(i.Name, 24) + i.CategoryName).ToList();
        int last = -1;
        while (true)
        {
            int at = ListPickDialog.Pick(this, lines, "아이템 넣기 - 아이템 고르기", "아이템이 없습니다", preselect: last);
            if (at < 0) return;
            last = at;
            string failed = GameSave.AddItem(file, items[at].Id);
            if (failed.Length > 0) { NoticeDialog.Show(this, failed); return; }
            Say($"{rows[pick][0]}의 세이브에 {items[at].Name}{GameUi.Josa(items[at].Name, "을", "를")} 넣었습니다. 불러오면 보입니다.");
        }
    }


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

    /// <summary>
    /// 띠 갈래마다 처음 켜져 있는 칸 — 도시정보 줄 차례(<see cref="CityInfoMenu.Rows"/>)의 비트다
    /// (PC 판 BarDefaults, <c>0x0047E167</c>~<c>0x0047E17F</c>).
    /// <code>
    ///   0 바다  0x000F  날짜 · 선원 · 물·식량 · 위도·경도
    ///   1 뭍    0x041B  날짜 · 대원 · 위도·경도 · 소지금 · 남은일수
    ///   2 도시  0x0691  날짜 · 소지금 · 도시명 · 시세 · 남은일수
    /// </code>
    /// </summary>
    private static readonly int[] BarDefaults = [0x000F, 0x041B, 0x0691];

    /// <summary>지금 띠 갈래 — 0 바다 · 1 뭍 · 2 도시(PC 판 SyncBarPlace 와 같은 셈).</summary>
    private int BarPlace => _host.InCity ? 2 : _host.IsOnLand ? 1 : 0;

    /// <summary>띠 칸을 켜고 끄면 알린다 — 안드로이드 띠(SeaBars)가 다시 그린다.</summary>
    public event Action? BarCellsChanged;

    /// <summary>그 자리에서 그 칸이 켜져 있는지. 그 자리에서 안 건드렸으면 게임 기본값이다.</summary>
    private static bool BarOn(int place, string name)
    {
        int i = Array.IndexOf(CityInfoMenu.Rows, name);
        return GameSettings.BarCellsAt(place)?.Contains(name) ?? (i >= 0 && ((BarDefaults[place] >> i) & 1) != 0);
    }

    /// <summary>켜진 칸들의 글 — 서식은 PC 판 상태 시계(0x0047DCE2 · 0x0056BEA8 · 0x0056BFB0 …) 그대로.</summary>
    public IReadOnlyList<string> BarCells() => BarCells(BarPlace);

    /// <summary>그 자리 갈래의 켜진 칸 글(타이틀 화면 띠는 바다 갈래를 쓴다).</summary>
    public IReadOnlyList<string> BarCells(int place)
    {
        var p = _game.Player;
        var list = new List<string>();
        foreach (var name in CityInfoMenu.Rows)
        {
            if (!BarOn(place, name)) continue;
            list.Add(name switch
            {
                CityInfoMenu.Date => $"{p.Date.Year,4}년{p.Date.Month,2}월{p.Date.Day,2}일",
                // 글을 통째로 둘로 나눠 둔다 — 낱말을 자리로 끼우면 번역표의 「선원{0}명」 틀에 안 맞는다(PC 판).
                CityInfoMenu.Crew => _host.IsOnLand ? $"대원{p.Crew,4}명" : $"선원{p.Crew,4}명",
                CityInfoMenu.Stores => $"물{p.SupplyOf(SupplyKind.Water),4}통 식량{p.SupplyOf(SupplyKind.Food),4}통",
                CityInfoMenu.DaysLeft => p.Contract is { } deal ? $"남은일수{deal.DaysLeftOn(p.Date),4}" : "남은일수----",
                CityInfoMenu.Wind => WindLine(),
                CityInfoMenu.Coord => BarCoord(),
                CityInfoMenu.Gold => $"소지금{p.Gold,6}닢",
                CityInfoMenu.Fame => $"명성{p.Fame,6}",
                CityInfoMenu.Fatigue => $"피로도{p.Fatigue,4}",
                CityInfoMenu.Morale => $"규율{p.Morale,4}",
                CityInfoMenu.City => p.CityName.Length > 0 ? p.CityName : NoCity,
                CityInfoMenu.Language => CityLanguage(),
                CityInfoMenu.Rate => CityRate(),
                CityInfoMenu.Current => CurrentLine(),
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

    private GameMenu BuildCityInfo()
    {
        int place = BarPlace;
        return CityInfoMenu.Build(
            name => Array.IndexOf(CityInfoMenu.Rows, name) >= 0 ? BarOn(place, name) : null,
            name =>
            {
                // 지금 자리 몫으로 적어 둔다 — 다음에 그 자리에 오면 이대로 선다(PC 판 SaveBarCells).
                var on = CityInfoMenu.Rows.Where(c => BarOn(place, c)).ToList();
                if (!on.Remove(name)) on.Add(name);
                GameSettings.SetBarCellsAt(place, on);
                InfoMenu.Refresh();
                BarCellsChanged?.Invoke();
            },
            InfoMenu.Close, place);
    }
}
