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

    /// <summary>
    /// 모드 「항해 일수」 동그라미에 적을 날수(PC 판 SyncDaysBadge) — 모드를 켰고, 지도가 앞이고, <b>바다</b>(뭍 · 도시 아님)이고
    /// 배가 있을 때만 출항한 지 며칠(<see cref="Player.DaysAtSea"/>). 아니면 -1. 덧그림 판이 그린다.
    /// </summary>
    public int SeaDaysShown =>
        GameSettings.ShowSeaDays && _started && OnMap && !_host.IsOnLand && !_host.InCity
        && _game.Player.Ships.Count > 0 && !OverlayNotes.Held
            ? _game.Player.DaysAtSea : -1;

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
        // 자동항해가 스스로 멎으면 알린다 — 내륙 도시면 「해안에 닿았습니다. 상륙해서 걸어가십시오」(PC 판 AutoSailEnded).
        _host.AutoSailEnded += AutoSailEnded;

        // ── PC 판 생성자가 꽂는 연결(진검수에서 빠진 것을 찾아 옮겼다) ──
        // 인물 세상을 새로 지을 때마다(불러오기 포함) 누적 캐릭터를 다시 앉힌다.
        _game.WorldBuilt = AttachCumulative;
        // 적기 바로 앞에 지도가 들고 있는 것(하루 눈금 · 뱃머리 · 쥔 바람 · 뭍 자리 · 대 둔 배)을 주인공 쪽에 옮긴다.
        _game.BeforeSave = CaptureMap;
        // 하드코어 판은 설정이 바뀔 때마다 켠 모드를 적어 둔다 — 한 번이라도 켰으면 기록에 남는다.
        HardcoreRules.Current = () => _game.Player;
        GameSettings.Changed += () => HardcoreRules.Note(_game.Player);
        // 바다 인물 함대의 바닷길(모드 「향상된 NPC 이동」) — 지도 배열을 가진 지도 엔진이 길을 찾아 준다.
        PersonWorld.SeaRouter = _host.SeaRouteBetween;
        // 육상전 · 일기토가 열려도 비 · 눈이 그친다(0x0044AA31 · 0x004AA87F) — 이 창 밖에서 열리므로 판 쪽이 알린다.
        Action endWeather = EndWeather;
        LandBattleScene.Opening += endWeather;
        DuelDialog.Opening += endWeather;

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
        // 배가 없으면 바다에 그릴 배가 없다. 뭍에서는 말이라 배가 없어도 그린다 — 안 그리면 보이지 않는 채로 돌아다닌다.
        _host.ShowShip = _game.Player.Ships.Count > 0 || _host.IsOnLand;
        _folkEntered = MeetFolk();
        if (_folkEntered) return;
        if (!CheckPolar()) return;
        CheckDiscovery();
        SpotCities();
        CheckPort();
        // 걸어서 대 둔 배 곁에 닿으면 한 번 승선을 묻는다(리메이크가 더한 물음).
        CheckBoard();
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

    /// <summary>
    /// 더해 넣은 발견물의 출처 — 설명 · 힌트 · 대사는 위키백과 문서(CC BY-SA 4.0)를 바탕으로 썼고, 그림은
    /// 위키미디어 공용의 퍼블릭 도메인 그림을 발견물 그림처럼 바꾼 것이다(<c>asset/dstill/README.md</c>).
    /// </summary>
    private const string ExtraCredits =
        "원본에 없던 발견물의 글과 그림은 아래에서 가져왔습니다.\n" +
        "\n" +
        "■ 돌묵상어\n" +
        "  글: 위키백과 「돌묵상어」\n" +
        "      ko.wikipedia.org/wiki/돌묵상어 (CC BY-SA 4.0)\n" +
        "  그림: Greg Skomal / NOAA Fisheries Service\n" +
        "      위키미디어 공용, 퍼블릭 도메인\n" +
        "\n" +
        "■ 여우원숭이\n" +
        "  글: 위키백과 「호랑꼬리여우원숭이」\n" +
        "      ko.wikipedia.org/wiki/호랑꼬리여우원숭이 (CC BY-SA 4.0)\n" +
        "  그림: Charles d'Orbigny, Dictionnaire universel\n" +
        "      d'histoire naturelle (1849) 도판\n" +
        "      위키미디어 공용, 퍼블릭 도메인";

    /// <summary>차림표 「출처」 — 더해 넣은 발견물의 글 · 그림 출처(PC 판 햄버거 「출처」와 같은 창).</summary>
    public void ShowCredits() => NoticeDialog.Notes(this, ExtraCredits, "출처");

    /// <summary>
    /// 세이브 목록 「삭제」 — 정말 지울지 묻고 그 칸의 파일을 지운다(안드로이드에만 있는 것, 사용자 요청).
    /// 손으로 적은 가장 새 칸은 세이브 파일(<see cref="GameSave.Path"/>)과 같은 판이라 둘 다 지운다 — 안 그러면 지운 줄이 다시 나온다.
    /// </summary>
    private static void DeleteSaveSlot(Window owner, GameSave.AutoSlot slot, string row)
    {
        string who = row.Split("  ", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        string city = string.IsNullOrEmpty(slot.Save.CityName) ? "바다" : slot.Save.CityName;
        string day = slot.Save.Date == default ? "" : $" {slot.Save.Date.Year}년 {slot.Save.Date.Month}월 {slot.Save.Date.Day}일";
        if (!ConfirmDialog.Ask(owner, $"{who} · {city}{day}\n이 세이브를 정말 삭제하시겠습니까?\n삭제하면 되돌릴 수 없습니다.", "세이브 삭제"))
            return;
        var files = new List<string> { slot.File };
        if (!string.Equals(slot.File, GameSave.Path, StringComparison.OrdinalIgnoreCase)
            && !GameSave.IsSuspendFile(slot.File)
            && GameSave.Load(GameSave.Path) is { } latest && latest.SavedAt == slot.Save.SavedAt)
            files.Add(GameSave.Path);
        try
        {
            foreach (var f in files)
                if (File.Exists(f)) File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoticeDialog.Show(owner, $"삭제하지 못했습니다.\n{ex.Message}");
        }
    }

    /// <summary>릴리즈 노트 — 역대 판 목록 · 판별 보기 · 낱말 찾기(PC 판 ShowReleaseNotes 와 같다). 노트는 APK 의 release-notes.md.</summary>
    public void ShowReleaseNotes() => ReleaseNotesDialog.Show(this);

    /// <summary>하드코어에서 단축키 · 커맨드로 저장하려 할 때(PC 판 문구 그대로).</summary>
    public const string HardcoreNoQuickSave = "하드코어 모드에서는 단축키로 저장할 수 없습니다. 여관에서 저장해 주세요.";

    internal void SaveByKey(Window owner)
    {
        if (!OnMap) return;
        // 하드코어는 원본처럼 시설에서만 적는다 — 단축키(V) · 커맨드 저장은 죽기 직전에 적어 두고 되돌리는 길이 된다.
        if (_game.Player.Hardcore)
        {
            NoticeDialog.Show(owner, HardcoreNoQuickSave);
            return;
        }
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

    // ── 단축키 창(PC 판 GameWindow 가 어느 창에서든 넘긴다 — 안드로이드는 가상 키보드 · 패드 매핑으로 같은 글쇠가 온다) ──

    /// <summary>
    /// 단축키로 <b>모드</b> 창을 연다 — 어느 창에서 눌러도 여기로 온다. 이미 떠 있으면 또 열지 않는다.
    /// </summary>
    internal void ModByKey()
    {
        if (!_started || !ReferenceEquals(_screen.Content, _mapRoot)) return;
        ShowModDialog();
    }

    /// <summary>모드 「수에즈 운하」를 켜고 끈 것을 지도에 곧바로 들인다.</summary>
    internal void RefreshSuezCanal() => _host.RefreshSuezCanal();

    /// <summary>
    /// 단축키로 <b>소지품 정보</b>를 연다 — 바다·뭍·도시 어디서든, 지금 손이 가 있는 창 위에 뜬다.
    /// 이미 떠 있으면(그 창 안에서 또 누르면) 또 열지 않는다.
    /// </summary>
    internal void ItemsByKey(Window owner)
    {
        if (!_started || _itemsOpen) return;
        _itemsOpen = true;
        try
        {
            BelongingsDialog.Show(owner, _game.Player, _game.Items, _game.ItemText, _game.ItemPictures,
                                  GameInfo.DiscoveryNames(_game), _game);
        }
        finally { _itemsOpen = false; }
    }

    /// <summary>소지품 창이 떠 있는지 — 그 창 안에서 R 을 또 눌러 겹쳐 뜨지 않게.</summary>
    private bool _itemsOpen;

    /// <summary>
    /// 단축키로 <b>힌트 정보</b>(취득 힌트 일람)를 연다 — 커맨드 「힌트정보」와 같은 창이다. 어디서든 지금 창 위에 뜨고,
    /// 이미 떠 있으면 또 열지 않는다.
    /// </summary>
    internal void HintsByKey(Window owner)
    {
        if (!_started || _hintsOpen) return;
        _hintsOpen = true;
        try { ShowHints(owner); }
        finally { _hintsOpen = false; }
    }

    /// <summary>힌트 창이 떠 있는지 — 그 창 안에서 H 를 또 눌러 겹쳐 뜨지 않게.</summary>
    private bool _hintsOpen;

    /// <summary>
    /// 단축키로 <b>인물정보</b>를 연다 — 커맨드 「인물정보」와 같은 판이다. 어디서든 지금 창 위에 뜨고,
    /// 이미 떠 있으면 또 열지 않는다.
    /// </summary>
    internal void PersonByKey(Window owner)
    {
        if (!_started || _personOpen) return;
        _personOpen = true;
        try { PersonInfoMenu.ShowByKey(owner, _game); }
        finally { _personOpen = false; }
    }

    /// <summary>인물정보가 떠 있는지 — 그 창 안에서 X 를 또 눌러 겹쳐 뜨지 않게.</summary>
    private bool _personOpen;

    /// <summary>
    /// 단축키로 <b>후원자 정보</b>(스폰서 일람)를 연다 — 도시 커맨드 「후원자 정보」와 같은 창이다. 어디서든 지금 창 위에
    /// 뜨고, 이미 떠 있으면 또 열지 않는다.
    /// </summary>
    internal void PatronsByKey(Window owner)
    {
        if (!_started || _patronsOpen) return;
        _patronsOpen = true;
        try { ShowPatrons(owner); }
        finally { _patronsOpen = false; }
    }

    /// <summary>후원자 정보가 떠 있는지 — 그 창 안에서 P 를 또 눌러 겹쳐 뜨지 않게.</summary>
    private bool _patronsOpen;

    /// <summary>
    /// 단축키로 <b>매입 정보</b>를 연다 — 함대정보 「짐」 판에서 교역품을 누를 때의 그 창(모드 「적하 시세 순위」)이다.
    /// 첫 짐으로 열고, 여럿이면 창 위 교역품 단추로 바꿔 본다.
    /// </summary>
    internal void CargoByKey(Window owner)
    {
        if (!_started || _cargoOpen) return;
        if (!GameSettings.CargoPriceRank)
        {
            NoticeDialog.Show(owner, "모드 「적하 시세 순위」를 켜면 쓸 수 있습니다.");
            return;
        }
        _cargoOpen = true;
        try
        {
            if (_game.Player.CargoHold.Count == 0)
            {
                NoticeDialog.Show(owner, "실은 교역품이 없습니다.");
                return;
            }
            CargoRankDialog.Show(owner, _game, 0, c => GoodsInfoDialog.Show(owner, _game, c.Kind, c.Shelf));
        }
        finally { _cargoOpen = false; }
    }

    /// <summary>매입 정보가 떠 있는지 — 그 창 안에서 T 를 또 눌러 겹쳐 뜨지 않게.</summary>
    private bool _cargoOpen;

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
        // 폭풍 · 눈보라 장면 동안은 구름을 걷는다 — 대본이 트는 폭풍도 같다. 끝나면 앞의 상태로 되돌린다(PC 판 그대로).
        bool stormy = scene is EventAnimation.Storm or EventAnimation.Blizzard;
        bool hidden = _host.CloudsHidden;
        if (stormy) _host.CloudsHidden = true;
        // 뱃머리(16방위)를 넘긴다 — 고래 · 돌고래 · 유령선 같은 바다 짐승이 배 앞뒤 어디에 나올지 이것으로 정한다.
        try { EventAnimationPopup.Play(this, _game, scene, MapAreaOnScreen(), 1, ship, _host.Heading); }
        finally { if (stormy) _host.CloudsHidden = hidden; }
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
                SetSea = on => { _host.SeaEffect = on; GameSettings.SeaEffect = on; },
                SeaBrightness = () => GameSettings.SeaBrightness,
                SetSeaBrightness = v => { GameSettings.SeaBrightness = v; _host.SeaBrightness = GameSettings.SeaBrightness; },
                SeaFlowAmount = () => GameSettings.SeaFlowAmount,
                SetSeaFlowAmount = v => { GameSettings.SeaFlowAmount = v; _host.SeaFlowAmount = GameSettings.SeaFlowAmount; },
                HiResSeaOn = () => GameSettings.HiResSea,
                SetHiResSea = on => { _host.HiResSea = on; GameSettings.HiResSea = on; },
                LandDetailOn = () => GameSettings.LandDetail,
                SetLandDetail = on => { _host.LandDetail = on; GameSettings.LandDetail = on; },
                ShipWakeOn = () => GameSettings.ShipWake,
                SetShipWake = on => { _host.ShipWake = on; GameSettings.ShipWake = on; },
                PixelFilterOn = () => GameSettings.PixelFilter,
                SetPixelFilter = on => { _host.PixelFilter = on; GameSettings.PixelFilter = on; },
                SmoothCloudsOn = () => GameSettings.SmoothClouds,
                SetSmoothClouds = on => { _host.SmoothClouds = on; GameSettings.SmoothClouds = on; },
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
        // v1.1.x 도구 앱에 새로 생긴 편집 창(PC 판 CdsHelperWindow 햄버거 차림표와 같은 것)
        ("퀘스트 편집기", w => DisevEditorDialog.Show(w, "PEX")),
        ("술집 소문 편집", w => TavernRumorEditDialog.Show(w)),
        ("패시브 편집", w => PassiveEditDialog.Show(w)),
        ("스폰서 목록", w => SponsorListDialog.Show(w)),
        ("괴물 관리", w => MonsterMotionDialog.Show(w)),
        ("초상화 필터", w => PortraitFilterDialog.Show(w)),
        ("도시 그림 조각", w => CitySpriteDialog.Show(w)),
        // 도구 앱 본체 화면(XAML 틀을 코드로 옮긴 것)
        .. CostaDelSol.Droid.Tools.ToolPages.Pages,
    ];

    public void OpenTool(int at) => Tools[at].Open(this);

    /// <summary>D 글쇠 — 어디서나 발견물 지도를 편다(PC 판 MapByKey).</summary>
    internal void MapByKey()
    {
        ShowDiscoveryMap();
    }

    // PC 판 ShowDiscoveryMap 그대로(도시 동그라미 · 건물 속 발견물 · 뭍 자동이동 · 대본만 마친 사건 · 순간이동 모드).
    private void ShowDiscoveryMap()
    {
        var all = new ExploredMap();
        all.RevealAll();

        var chart = _host.Chart(all, out int w, out int h);
        if (chart == null) { NoticeDialog.Show(this, "지도를 아직 못 읽었습니다"); return; }

        var at = _host.ShipCell is { } cell ? ((double, double)?)(cell.CellX, cell.CellY) : null;
        // 도시 — 지금 아는 도시만(자동항해 고르기와 같다). 도서관이 있으면 동그라미에 「도」.
        // 도서관은 원본 건물표로 가린다(cities.json 은 손으로 적어 항주 · 베니스가 빠져 있었다).
        var cities = Enumerable.Range(0, GameMapCoords.CityCount)
            .Where(id => _game.CityVisible(id))
            .Select(id => GameMapCoords.TryCityCell(id, out double cx, out double cy)
                ? ((string, double, double, bool)?)(_game.CityName(id), cx, cy,
                                                    _game.Buildings is { } bt
                                                        ? bt.Find(id, "도서관") != null
                                                        : _game.CityTable.Find(id)?.HasLibrary == true)
                : null)
            .OfType<(string Name, double X, double Y, bool Library)>()
            .ToList();

        // 건물 속에서 찾는 발견물(진흙 모스크 따위)은 바다 · 뭍 자리가 없다 — 그 도시 자리로 찍는다.
        var inTown = new Dictionary<int, (double X, double Y)>();
        foreach (var b in _game.Buildings?.Buildings ?? [])
            if (b.IsDiscovery && !inTown.ContainsKey(b.Discovery)
                && GameMapCoords.TryCityCell(b.City, out double bx, out double by))
                inTown[b.Discovery] = (bx, by);

        // 모드 「뭍 자동이동」 — 뭍에 있을 때 발견물 점을 오른쪽 단추로 누르면 그 자리까지 걸어간다.
        Func<double, double, (bool, string)>? walk = GameSettings.LandAutoWalk && _host.IsOnLand
            ? (x, y) => _host.StartAutoWalk(x, y)
            : null;
        // 도시 화면이 떠 있으면 그 창을 주인으로 삼는다 — 세계지도가 주인이면 도시 화면과 형제가 되어, 다른 창에
        // 갔다 오면 Windows 가 형제 차례를 되살리며 도시 화면 · 그 쪽지들이 발견물 지도 위로 올라왔다.
        Window owner = this;   // 안드로이드는 창 겹 차례가 판 하나라 도시 화면을 주인으로 삼지 않아도 위에 뜬다
        DiscoveryMapDialog.Show(owner, chart, w, h, _game.Discoveries?.Table, _game.Player, at,
                                WindTable.Open(_game.Directory), GameSettings.DiscoveryMapWarp ? WarpTo : null,
                                AutoSail, cities, inTown, walk,
                                EventsDoneDiscoveries());
    }

    /// <summary>
    /// 대본이 발견을 안 적는 사건(코페르니쿠스의 망원경 · 돌고래 따위) 가운데 사건을 마친 것 — 발견물 지도와
    /// 미니맵이 찾은 것으로 찍는다.
    /// </summary>
    private HashSet<int> EventsDoneDiscoveries() => _game.Player.Settled
        .Where(id => !_game.Player.HasFound(id) && !Engine.Disev.DisevRunner.RecordsDiscovery(_game, id))
        .ToHashSet();


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
