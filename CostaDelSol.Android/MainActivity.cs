using Android.Widget;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Input;
using Android.Opengl;
using Android.OS;
using Android.Views;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Support.Local.Models;
using CostaDelSol.Droid.Game;
using CostaDelSol.Droid.Input;
using CostaDelSol.Droid.Ui;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Game.UI.Views;
using CostaDelSol.Droid.Town;
using CostaDelSol.Droid.Map;
using EngineGame = CdsHelper.Game.Engine.Game;

namespace CostaDelSol.Droid;

/// <summary>
/// 안드로이드판 놀이 화면 — 주인공 고르기 → 바다 → 항구.
/// </summary>
/// <remarks>
/// <b>입력은 에뮬레이터처럼 짠다.</b> 게임패드 버튼·화면 패드·가상 키보드·실제 키보드가 모두 원본의
/// 키보드 키(<see cref="GameKey"/>)나 마우스 클릭을 "누르고", 게임 쪽은 원본 단축키 그대로 받는다
/// (<see cref="PressKey"/> · <see cref="Click"/>).
/// <list type="bullet">
///   <item>패드 버튼 → 표준 버튼(<see cref="PadInput"/>) → 할 일(<see cref="PadBindings"/>, 버튼 매핑 화면에서 바꾼다)</item>
///   <item>R스틱 기본 = 배 이동: 기울인 8방향이 숫자판 1~9 키가 된다(원본 숫자판 조타)</item>
///   <item>L스틱 기본 = 마우스 이동: 화면에 커서를 그린다. R1 왼쪽 클릭 · L1 오른쪽 클릭</item>
///   <item>터치: 톡 = 왼쪽 클릭, 길게 = 오른쪽 클릭, 두 손가락 = 확대·축소</item>
/// </list>
/// 그림과 항해 셈은 GL 스레드(<see cref="MapRenderer"/>), 입력과 화면 글자는 UI 스레드다.
/// </remarks>
[Activity(
    Label = "대항해시대3",
    MainLauncher = true,
    Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout
                         | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
public sealed class MainActivity : Activity
{
    private const int TickMs = 100;
    private const int FastMs = 16;
    private const float ZoomStep = 1.25f;
    private const int TiredToRest = 60;          // WorldMapWindow.TiredToRest
    private const float CursorSpeedDp = 900f;    // 스틱을 끝까지 밀었을 때 커서가 1초에 가는 dp
    private const int RepeatDelayMs = 400, RepeatEveryMs = 110;

    private FrameLayout _root = null!;
    private AspectFrame _gameArea = null!;
    private TitleView _title = null!;
    private BgmPlayer _bgm = null!;
    private TextView _status = null!;
    private ChoicePanel _panel = null!;
    private OnScreenPad _pad = null!;
    private VirtualKeyboard _keyboard = null!;
    private CursorView _cursor = null!;
    private SeaBars? _bars;
    private GLSurfaceView? _gl;
    private MapRenderer? _renderer;
    private readonly Handler _ui = new(Looper.MainLooper!);

    private GameData? _data;

    /// <summary>
    /// 원본 바다 화면(PC 판 WorldMapWindow 를 옮긴 것) — 판 · 지도 엔진 · 조우 · 발견 · 입항 · 상륙 · 커맨드가 다 여기 있다.
    /// 게임 줄(GameThread)에서 돈다.
    /// </summary>
    private readonly WorldMapWindow _map = new();

    private CityStage _cityStage = null!;
    private MapOverlay? _overlay;
    private PadBindings _bindings = new();
    private readonly PadInput _padInput = new();
    private long _lastFast;
    private long _nextDeviceCheck;

    // 화면 스틱을 쥐고 있으면 그 값이 패드 스틱보다 앞선다.
    private readonly Dictionary<PadControl, (float X, float Y)> _touchStick = [];
    // 배 스틱이 지금 누르고 있는 숫자판 키(1~9). 0 이면 가운데.
    private readonly Dictionary<PadControl, int> _shipDigit = [];
    // 패드 방향을 누르고 있는 동안 키를 되풀이한다(창에서 줄 옮기기) — 누른 때.
    private readonly Dictionary<PadControl, long> _heldSince = [];
    private readonly Dictionary<PadControl, long> _nextRepeat = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _bindings = PadBindings.Load(this);
        SaveFolder.Apply(this);
        GameText.Init(this);
        _padInput.Button += (control, down, repeat) => OnPadButton(control, down, repeat);

        _root = new FrameLayout(this);
        _root.SetBackgroundColor(Color.Black);

        // 게임 화면 틀 — 고른 비율(4:3 · 16:9)로 가운데에 선다. 지도·글자·타이틀은 이 안에 든다.
        _gameArea = new AspectFrame(this);
        _root.AddView(_gameArea, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent, GravityFlags.Center));
        _status = new TextView(this) { Text = "불러오는 중…", Gravity = GravityFlags.Center };
        _status.SetTextColor(Color.White);
        _status.SetTextSize(Android.Util.ComplexUnitType.Sp, 18);
        _gameArea.AddView(_status, Match());
        _title = new TitleView(this) { Visibility = ViewStates.Gone };
        _gameArea.AddView(_title, Match());

        // 배경음악 — PC 판 BgmPlayer 를 그대로 쓰고, 곡은 APK 안(asset)에서 연다.
        BgmPlayer.Locate = track => $"asset:///bgm/Track{track:D2}.mp3";
        _bgm = _map.Game.Bgm;   // 판의 곡 재생기 하나를 바다 · 도시가 같이 쓴다(PC 판과 같다)
        _bgm.Enabled = GameSettings.BgmEnabled;

        // 쌓는 차례(아래 → 위): 지도 · 글자 · 가상 키보드 · 물음창 · 화면 패드 · 커서.
        // 화면 패드가 맨 위라 창이 떠 있어도 A·B 단추가 먹고, 단추가 아닌 자리는 아래 창으로 넘긴다.
        _keyboard = new VirtualKeyboard(this);
        _keyboard.KeyPressed += key => PressKey(key);
        _root.AddView(_keyboard, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));

        _panel = new ChoicePanel(this) { Area = GameAreaRect };
        // 시설 차림표가 떠 있어도 사진 앞 손님은 눌린다(원본 여관 · 술집).
        _panel.Outside = (x, y) =>
        {
            if (CityPicView.Active == null || !_cityStage.Active) return;
            int[] panelAt = new int[2], stageAt = new int[2];
            _panel.GetLocationInWindow(panelAt);
            _cityStage.GetLocationInWindow(stageAt);
            _cityStage.ClickGuest(x + panelAt[0] - stageAt[0], y + panelAt[1] - stageAt[1]);
        };
        _root.AddView(_panel, Match());
        // 영상 — 창 판 바로 위(WPF 동영상 칸이 그 자리를 알려 준다).
        CostaDelSol.Droid.Ui.VideoLayer.Current = new CostaDelSol.Droid.Ui.VideoLayer(this);
        _root.AddView(CostaDelSol.Droid.Ui.VideoLayer.Current, Match());

        // 도시 · 시설 차림표는 게임 줄에서 돌고 창은 이 판에 겹쳐 선다(PC 판 ShowDialog 와 같은 흐름).
        GameThread.Start(this, _panel);
        GameThread.Failed += ex => _panel.Tell($"진행 중에 문제가 생겼습니다.\n{ex.Message}");
        _cityStage = new CityStage(this);
        CityPicView.Stage = _cityStage;
        _map.Noted += text => RunOnUiThread(() => Say(text));
        _map.TitleWanted += () => RunOnUiThread(ShowTitle);
        _map.MapShown += () => RunOnUiThread(ShowSea);
        // 지도 엔진의 프레임 · 상태 시계는 게임 줄이 쉴 때마다 돈다(PC 판 렌더 고리 · 0.1초 타이머).
        GameThread.Idle = dt =>
        {
            var (w, h) = _renderer?.Size ?? (0, 0);
            if (w > 1 && h > 1) _map.Frame(dt, w, h);
        };

        _pad = new OnScreenPad(this) { Hint = HintOf, Function = FunctionOf };
        _pad.PassThrough = (_, y) => _panel.IsOpen || _title.IsOpen || (_keyboard.IsOpen && y >= _keyboard.Top);
        _pad.Button += (control, down) => OnPadButton(control, down, false);
        _pad.StickMoved += (stick, x, y) =>
        {
            if (x == 0 && y == 0) _touchStick.Remove(stick); else _touchStick[stick] = (x, y);
        };
        _pad.MapClick += (x, y, left) => { _cursor.MoveTo(x, y); Click(x, y, left); };
        _pad.Pinch += Zoom;
        _pad.EditDone += () => ShowPadMenu();
        // 메뉴 · 차림표가 떠 있는 동안은 결정 · 취소 단추만 남긴다.
        _pad.KeepInMenu = c => FunctionOf(c) is "결정" or "취소";
        _panel.Changed += SyncMenuPad;
        _root.AddView(_pad, Match());

        _cursor = new CursorView(this);
        _root.AddView(_cursor, Match());
        SetContentView(_root);
        HideSystemBars();
        CheckDevices();

        Task.Run(() =>
        {
            try
            {
                var data = GameData.Load(this, text => RunOnUiThread(() => _status.Text = text));
                _map.SetDirectory(GameData.GameRoot(this));
                // PC 판 화면 도구(GameUi)가 쓰는 원본 글꼴 · 띠 조각.
                CdsHelper.Game.UI.Views.GameUi.Font = GameText.Font;
                // 도구 창들(효과음 · 발견물 그림 · 일기토 소리 …)은 「마지막 세이브 자리」 옆에서 게임 자료를 찾는다 — 앱 폴더를 가리킨다.
                string gameSave = System.IO.Path.Combine(GameData.GameRoot(this), "SAVEDATA.CDS");
                if (CdsHelper.Support.Local.Settings.AppSettings.LastSaveFilePath != gameSave)
                    CdsHelper.Support.Local.Settings.AppSettings.LastSaveFilePath = gameSave;
                CdsHelper.Game.UI.Views.GameUi.Sprites = CdsHelper.Game.Local.Helpers.UiSprites.Open(GameData.GameRoot(this));
                RunOnUiThread(() => { _data = data; ChooseHero(); });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => _status.Text = $"자료를 불러오지 못했습니다.\n{ex.Message}");
            }
        });
        _ui.PostDelayed(Fast, FastMs);
    }

    /// <summary>게임 화면 틀의 자리(뿌리 뷰 좌표). 원본 창들이 이 한가운데에 서고 이 높이로 키운다.</summary>
    private RectF GameAreaRect() =>
        new(_gameArea.Left, _gameArea.Top, _gameArea.Right, _gameArea.Bottom);

    private static FrameLayout.LayoutParams Match() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);

    // ── 시작 ────────────────────────────────────────────────────────────────

    /// <summary>NEW GAME 의 초심자용(EASY) 두 사람 가운데 고른다(<see cref="Beginner"/>).</summary>
    /// <summary>
    /// 첫 화면 — 세이브 폴더 권한이 없으면 처음 한 번 청하고, 타이틀(메인메뉴)을 띄운다.
    /// </summary>
    private void ChooseHero()
    {
        _status.Text = _data!.Problems.Count == 0 ? "" : "못 연 표: " + string.Join(", ", _data.Problems);
        if (AskAccessOnce(ChooseHero)) return;
        if (_introDone) { ShowTitle(); return; }
        _introDone = true;
        // 모드 「오프닝 동영상」을 끄면 둘 다 건너뛰고 곧장 메인메뉴다(PC 판과 같다).
        if (GameSettings.PlayOpeningMovie) PlayIntro(["LOGO", "OPEN"], ShowTitle);
        else ShowTitle();
    }

    private bool _introDone;
    private Action? _skipIntro;

    /// <summary>
    /// 시작 영상 — 리메이크처럼 LOGO → OPEN 을 게임 화면 틀에 튼다(asset/movie 의 MP4). 누르거나 글쇠를 치면 넘긴다.
    /// 파일이 없거나 못 틀면 그냥 다음으로 간다.
    /// </summary>
    private void PlayIntro(IReadOnlyList<string> stems, Action then)
    {
        var video = CostaDelSol.Droid.Ui.VideoLayer.Current;
        string? path = null;
        int at = 0;
        while (at < stems.Count && (path = CdsHelper.Game.Local.Helpers.MovieFiles.Resolve(null, stems[at])) == null) at++;
        if (video == null || path == null)
        {
            if (video != null) video.TapToSkip = null;
            _skipIntro = null;
            then();
            return;
        }
        var rest = stems.Skip(at + 1).ToList();
        bool moved = false;
        void Next()
        {
            if (moved) return;
            moved = true;
            video.Stop();
            PlayIntro(rest, then);
        }
        _skipIntro = Next;
        video.TapToSkip = Next;
        _bgm.Stop();
        video.Play(path, GameAreaRect(), 1, Next, Next);
    }

    /// <summary>
    /// 타이틀 메인메뉴 — PC 판과 같은 다섯 줄. 고르면 원본 바다 화면의 그 길(NewGame · StartMap)이 게임 줄에서 돈다 —
    /// NEW GAME 의 EASY/NORMAL · 주인공 고르기 · 모험 중단 물음, 불러오기의 물음 · 에러 창이 다 원본 그대로다.
    /// </summary>
    private void ShowTitle()
    {
        HideSea();
        var start = new Player().Date;   // 새 판은 1480년 1월 1일 — 원본 타이틀의 위 띠 날짜다
        _title.Show($"{start.Year}년 {start.Month}월 {start.Day}일",
        [
            new("NEW GAME", true, () => GameThread.Post(_map.TitleNewGame)),
            new("LOAD GAME", _map.HasManualSave, () => GameThread.Post(_map.TitleLoadGame)),
            new("CONTINUE", _map.HasAutoSave, () => GameThread.Post(_map.TitleContinue)),
            new("MINI GAME", true, () => GameThread.Post(_map.TitleMiniGames)),
            new("END GAME", true, FinishAffinity),
        ]);
        _bgm.Play(BgmPlayer.TitleTrack);
    }

    /// <summary>「모든 파일 접근」을 아직 안 물었으면 한 번 묻는다. 물었으면 true(답한 뒤 <paramref name="then"/>).</summary>
    private bool AskAccessOnce(Action then)
    {
        var prefs = GetSharedPreferences("save-folder", FileCreationMode.Private)!;
        if (SaveFolder.HasAccess(this) || prefs.GetBoolean("asked", false)) return false;
        prefs.Edit()!.PutBoolean("asked", true)!.Apply();
        _panel.Show("세이브를 내장 저장소의 CostaDelSol 폴더에 두려면 「모든 파일 접근」 권한이 필요합니다. " +
                    "그 폴더의 SAVEDATA.CDS 는 윈도우판(%APPDATA%\\CdsHelper)과 서로 옮겨 쓸 수 있고, 앱을 지워도 남습니다.",
            ["권한을 허용하러 간다", "나중에 (앱 안에 저장 — 앱을 지우면 같이 지워집니다)"], at =>
            {
                if (at == 0) SaveFolder.RequestAccess(this);
                then();
            }, cancelIndex: 1);
        return true;
    }

    /// <summary>원본 바다 화면이 판을 세웠다 — 지도(GL) · 덧그림 · 띠를 처음 한 번 짓고 띄운다.</summary>
    private void ShowSea()
    {
        _title.Hide();
        if (_gl != null)
        {
            _gl.Visibility = ViewStates.Visible;
            _overlay!.Visibility = ViewStates.Visible;
            _bars!.Visibility = ViewStates.Visible;
            return;
        }
        var data = _data!;
        _status.Text = "바다를 준비하는 중…";
        Task.Run(() =>
        {
            try
            {
                // 도시 · 발견물 덮기는 원본 지도 엔진이 판마다 칸 덧칠로 한다(HideCities) — 여기서는 원래 지도를 굽는다.
                var textures = MapTextures.Build(data, _ => true);
                RunOnUiThread(() => BuildSea(textures));
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => _status.Text = $"바다를 준비하지 못했습니다.\n{ex.Message}");
            }
        });
    }

    private void BuildSea(MapTextures textures)
    {
        _renderer = new MapRenderer(_data!, textures, _map.Host)
        {
            Failed = text => RunOnUiThread(() => { _status.Text = "화면을 그리지 못했습니다.\n" + text; _status.BringToFront(); }),
        };
        _gl = new GLSurfaceView(this);
        _gl.SetEGLContextClientVersion(3);
        _gl.PreserveEGLContextOnPause = true;
        _gl.SetRenderer(_renderer);
        _gameArea.AddView(_gl, 0, Match());
        _overlay = new MapOverlay(this, _map);
        _gameArea.AddView(_overlay, 1, Match());
        _gameArea.AddView(_cityStage, 2, Match());

        // 위아래 띠(원본 게임 띠 · 알림 띠). 지도는 그 사이에 선다 — 띠 높이만큼 위아래를 비운다.
        _bars = new SeaBars(this);
        _bars.TopPressed += () => { if (!_panel.IsOpen) GameThread.Post(() => _map.Key(GameKey.D0)); };
        _bars.TopLongPressed += () => { if (!_panel.IsOpen) GameThread.Post(_map.ShowCityInfoMenu); };
        _bars.NotePressed += _ => { if (!_panel.IsOpen) GameThread.Post(_map.ReadNote); };
        _gameArea.AddView(_bars, 3, Match());
        _gameArea.LayoutChange += (_, _) => FitMapBetweenBars();
        FitMapBetweenBars();

        _status.Text = "";
        _ui.PostDelayed(Tick, TickMs);
    }

    /// <summary>타이틀로 돌아갔다 — 지도와 띠를 감춘다(다시 들어오면 그대로 쓴다).</summary>
    private void HideSea()
    {
        if (_gl == null) return;
        _gl.Visibility = ViewStates.Gone;
        _overlay!.Visibility = ViewStates.Gone;
        _bars!.Visibility = ViewStates.Gone;
        _cityStage.End();
    }

    // ── 0.1초마다 — 위 띠 ───────────────────────────────────────────────────

    private (float, float) _lastHover;

    private void Tick()
    {
        if (_bars != null && _map.OnMap)
        {
            _bars.SetCells(BarCells());
            _bars.Tick();
        }
        // 창(WPF 화면)이 떠 있으면 커서 자리를 알린다 — 책 이름표 · 단추 밝힘 같은 「쥐 올림」이 산다.
        if (_panel.IsOpen && _cursor.X0 + _cursor.Y0 > 0 && (_cursor.X0, _cursor.Y0) != _lastHover)
        {
            _lastHover = (_cursor.X0, _cursor.Y0);
            var (hx, hy) = Local(_panel, _cursor.X0, _cursor.Y0);
            _panel.Hover(hx, hy);
        }
        // 커서가 지도 위면 원본처럼 커서 쪽으로 뱃머리를 돌린다(SteerArmed 가 서 있을 때 — 왼쪽 클릭이 깨운다).
        if (_gl is { } gl && _cursor.X0 + _cursor.Y0 > 0)
        {
            int[] at = new int[2], root = new int[2];
            gl.GetLocationInWindow(at);
            _root.GetLocationInWindow(root);
            float x = _cursor.X0 - (at[0] - root[0]), y = _cursor.Y0 - (at[1] - root[1]);
            bool inside = x >= 0 && y >= 0 && x < gl.Width && y < gl.Height;
            _map.Mouse(Math.Clamp(x, 0, gl.Width), Math.Clamp(y, 0, gl.Height), inside);
        }
        _ui.PostDelayed(Tick, TickMs);
    }

    /// <summary>알림 — 원본처럼 아래 띠에 마지막 하나를 적는다(바다 화면이 뜨기 전에는 가운데 글자로).</summary>
    private void Say(string text)
    {
        if (_bars == null || _bars.Visibility != ViewStates.Visible) { _status.Text = text; return; }
        _bars.Say(text);
    }

    /// <summary>지도를 위아래 띠 사이에 앉힌다 — 띠 높이(원본 29점)를 화면 배율로 바꿔 비운다.</summary>
    private void FitMapBetweenBars()
    {
        if (_gl == null || _bars == null) return;
        int bar = (int)Math.Round(SeaBars.BarHeight * (_gameArea.Height / (float)GameArt.GameHeight));
        foreach (var view in new View[] { _gl, _overlay! })
            if (view.LayoutParameters is FrameLayout.LayoutParams lp && (lp.TopMargin != bar || lp.BottomMargin != bar))
            {
                lp.TopMargin = bar;
                lp.BottomMargin = bar;
                view.Post(() => view.LayoutParameters = lp);
            }
    }

    private const int SextantItem = 0x22;   // WorldMapWindow.SextantItem — 육분의가 있어야 좌표가 보인다

    /// <summary>
    /// 위 띠의 칸들 — PC 판 InfoCell 의 기본 켬(날짜 · 선원 · 위도경도 · 소지금 · 명성 · 규칙)과 서식 그대로
    /// (<c>WorldMapWindow</c> 상태 시계: 0x0047DCE2 · 0x0056BEA8 · 0x0056BFB0). 뭍이면 「대원」이다.
    /// </summary>
    /// <summary>위 띠의 칸들 — 도시정보 창에서 켠 칸만, PC 판 서식 그대로(<see cref="WorldMapWindow.BarCells"/>).</summary>
    private IReadOnlyList<string> BarCells() => _map.BarCells();

    // ── 16ms 마다 — 스틱, 커서, 되풀이 ────────────────────────────────────

    private void Fast()
    {
        long now = SystemClock.UptimeMillis();
        float dt = _lastFast == 0 ? 0 : Math.Min(0.05f, (now - _lastFast) / 1000f);
        _lastFast = now;

        Stick(PadControl.LeftStick, _bindings.LeftStick, _padInput.LeftStick, dt);
        Stick(PadControl.RightStick, _bindings.RightStick, _padInput.RightStick, dt);

        // HAT 십자키는 키 되풀이가 없다 — 누르고 있으면 우리가 되풀이를 낸다(창에서 줄 옮기기).
        foreach (var control in new[] { PadControl.Up, PadControl.Down, PadControl.Left, PadControl.Right })
        {
            if (!_heldSince.TryGetValue(control, out long since)) continue;
            if (now - since < RepeatDelayMs) continue;
            if (_nextRepeat.TryGetValue(control, out long next) && now < next) continue;
            _nextRepeat[control] = now + RepeatEveryMs;
            OnPadButton(control, true, true, fromRepeat: true);
        }

        if (now >= _nextDeviceCheck) { _nextDeviceCheck = now + 2000; CheckDevices(); }
        _ui.PostDelayed(Fast, FastMs);
    }

    private void Stick(PadControl stick, StickMode mode, (float X, float Y) hardware, float dt)
    {
        var (x, y) = _touchStick.TryGetValue(stick, out var touch) ? touch : hardware;
        float mag = MathF.Sqrt(x * x + y * y);
        float dead = _bindings.DeadZone;

        if (mode == StickMode.Mouse)
        {
            _shipDigit.Remove(stick);
            if (mag < dead || dt <= 0) return;
            float strength = Math.Clamp((mag - dead) / (1 - dead), 0, 1);
            float speed = CursorSpeedDp * Resources!.DisplayMetrics!.Density * strength * dt;
            _cursor.MoveBy(x / mag * speed, y / mag * speed);
            return;
        }

        // 배 이동 — 8방향을 숫자판 키로. 방향이 바뀔 때만 한 번 누른다(원본도 키를 한 번 누르면 그쪽으로 계속 간다).
        if (mag < dead * 0.5f) { _shipDigit[stick] = 0; return; }
        if (mag < dead) return;
        int digit = DigitOf(x, y);
        if (_shipDigit.TryGetValue(stick, out int was) && was == digit) return;
        _shipDigit[stick] = digit;
        PressKey(GameKey.D0 + digit);
    }

    /// <summary>스틱 방향 → 숫자판 키. 경계는 원본 커서 조타(기울기 1/2·2)와 같다.</summary>
    private static int DigitOf(float dx, float dy)
    {
        float ax = Math.Abs(dx), ay = Math.Abs(dy);
        if (ay * 2 <= ax) return dx >= 0 ? 6 : 4;
        if (ax * 2 <= ay) return dy > 0 ? 2 : 8;
        return dx > 0 ? (dy > 0 ? 3 : 9) : (dy > 0 ? 1 : 7);
    }

    private void CheckDevices()
    {
        bool connected = false;
        foreach (int id in InputDevice.GetDeviceIds() ?? [])
        {
            var device = InputDevice.GetDevice(id);
            if (device == null || device.IsVirtual) continue;
            var sources = device.Sources;
            if ((sources & InputSourceType.Gamepad) == InputSourceType.Gamepad ||
                (sources & InputSourceType.Joystick) == InputSourceType.Joystick)
            { connected = true; break; }
        }
        _pad.SetPadConnected(connected);
    }

    // ── 입력 → 할 일 ────────────────────────────────────────────────────────

    /// <summary>샘플 프리셋 단추 이름 — 그 단추가 지금 하는 일(4글자까지).</summary>
    private string FunctionOf(PadControl control) => control switch
    {
        PadControl.LeftStick or PadControl.RightStick => HintOf(control),
        PadControl.Up => "↑", PadControl.Down => "↓", PadControl.Left => "←", PadControl.Right => "→",
        _ => _bindings.TargetOf(control) switch
        {
            { Key: GameKey.Escape } => "취소",
            { Key: GameKey.Enter } => "결정",
            { Key: GameKey.Space } => "선택",
            { Key: GameKey.V } => "저장",
            { Key: GameKey.D } => "지도",
            { Key: GameKey.D5 } => "닻",
            { Key: GameKey.D0 } => "커맨드",
            { Key: GameKey.PageUp } => "앞장",
            { Key: GameKey.PageDown } => "뒷장",
            _ => HintOf(control),
        },
    };

    /// <summary>화면 단추 밑에 적을 할 일.</summary>
    private string HintOf(PadControl control) => control switch
    {
        PadControl.LeftStick => _bindings.LeftStick == StickMode.Ship ? "배" : "마우스",
        PadControl.RightStick => _bindings.RightStick == StickMode.Ship ? "배" : "마우스",
        _ => _bindings.TargetOf(control) switch
        {
            { Key: GameKey.D5 } => "닻",
            { Key: GameKey.D0 } => "커맨드",
            { Key: { } k } => GameKeys.Name(k),
            { MouseLeft: true } => "클릭",
            { MouseLeft: false } => "우클릭",
            { App: AppAction.VirtualKeyboard } => "키보드",
            { App: AppAction.Menu } => "메뉴",
            _ => "",
        },
    };

    /// <summary>표준 버튼 하나가 눌렸다/떼졌다 — 패드든 화면 단추든 여기로 온다.</summary>
    /// <summary>가상 키보드를 여닫는다 — 떠 있는 동안 화면 패드는 메뉴 단추만 남기고 숨긴다.</summary>
    private void ToggleKeyboard()
    {
        _keyboard.Toggle();
        _pad.KeyboardOpen = _keyboard.IsOpen;
    }

    private void OnPadButton(PadControl control, bool down, bool repeat, bool fromRepeat = false)
    {
        // 시작 영상 중에는 어느 단추든 넘기기다(실제 게임패드 단추가 여기로 바로 오는 기기도 있다).
        if (_skipIntro is { } skip) { if (!down) skip(); return; }
        if (control is PadControl.Up or PadControl.Down or PadControl.Left or PadControl.Right && !fromRepeat)
        {
            if (down && !repeat) { _heldSince[control] = SystemClock.UptimeMillis(); _nextRepeat.Remove(control); }
            else if (!down) { _heldSince.Remove(control); _nextRepeat.Remove(control); }
            // 실제 키 되풀이(KEYCODE_DPAD 를 누르고 있음)가 오면 우리 되풀이는 접는다 — 둘이 겹치지 않게.
            if (repeat) { _heldSince.Remove(control); return; }
        }
        if (!down || _pad.Editing) return;

        var target = _bindings.TargetOf(control);
        if (target.Key is { } key)
        {
            // 키를 누르고 있어 되풀이되는 것은 방향키만 받는다 — 원본도 목록에서 그것만 쓴다.
            if (repeat && key is not (GameKey.Up or GameKey.Down or GameKey.Left or GameKey.Right)) return;
            PressKey(key);
        }
        else if (repeat) return;
        else if (target.MouseLeft is { } left) Click(_cursor.X0, _cursor.Y0, left);
        else if (target.App == AppAction.VirtualKeyboard) ToggleKeyboard();
        else if (target.App == AppAction.Menu) ToggleMainMenu();
    }

    /// <summary>
    /// 원본 키보드 키 하나를 누른다 — 패드·화면 단추·가상 키보드·실제 키보드가 다 여기로 온다.
    /// 무엇을 할지는 원본 단축키 그대로다(<c>WorldMapWindow.OnMapKey</c> · 대화상자).
    /// </summary>
    private void PressKey(GameKey key)
    {
        if (_skipIntro is { } skip) { skip(); return; }
        if (_pad.Editing) return;

        // 창이 떠 있으면 창이 받는다. 커맨드 창은 0(Start)을 한 번 더 누르면 닫힌다.
        if (_panel.IsOpen)
        {
            if (key == GameKey.D0 && _panel.Tag0 == "command") { _panel.Cancel(); return; }
            _panel.HandleKey(key);
            return;
        }
        if (_title.IsOpen) { _title.HandleKey(key); return; }

        // 도시 화면 — 방향키로 건물 고르기, 엔터 · 스페이스로 묻기, 0 은 도시 커맨드(CityPicView.PickByKey).
        if (CityPicView.Active is { } town && _cityStage.Active)
        {
            if (key == GameKey.V) GameThread.Post(() => _map.SaveByKey(_map));
            else town.Key(key);
            return;
        }

        switch (key)
        {
            case GameKey.Plus: Zoom(1.25f); return;
            case GameKey.Minus: Zoom(1 / 1.25f); return;
        }
        // 바다 · 뭍 — 원본 지도 글쇠(숫자판 조타 · 0 커맨드 · D 발견물 지도 · V 저장 · ESC).
        if (_map.OnMap) GameThread.Post(() => _map.Key(key));
    }

    /// <summary>
    /// 마우스 클릭. 창이 떠 있으면 왼쪽은 그 자리를 누르고 오른쪽은 닫는다(원본 대화상자). 지도에서는
    /// 왼쪽이 닻, 오른쪽이 커맨드 창이다(원본 <c>WorldMapWindow</c>).
    /// </summary>
    private void Click(float x, float y, bool left)
    {
        if (_pad.Editing) return;
        if (_panel.IsOpen)
        {
            if (left) Tap(_panel, x, y);
            else
            {
                var (px, py) = Local(_panel, x, y);
                _panel.RightClick(px, py);
            }
            return;
        }
        if (_keyboard.IsOpen && y >= _keyboard.Top && left) { Tap(_keyboard, x, y); return; }
        if (_title.IsOpen) { if (left) Tap(_title, x, y); return; }
        if (CityPicView.Active != null && _cityStage.Active)
        {
            int[] at = new int[2], root = new int[2];
            _cityStage.GetLocationInWindow(at);
            _root.GetLocationInWindow(root);
            _cityStage.Click(x - (at[0] - root[0]), y - (at[1] - root[1]), left);
            return;
        }
        if (!_map.OnMap) return;
        // 커서를 그 자리로 옮기고 원본 지도 손(왼쪽 = 닻 · 커서 조타 깨우기, 오른쪽 = 커맨드).
        _cursor.MoveTo(x, y);
        Tick();
        GameThread.Post(() => _map.Click(left));
    }

    /// <summary>뿌리 화면 좌표를 그 뷰 안 좌표로.</summary>
    private (float X, float Y) Local(View view, float x, float y)
    {
        int[] at = new int[2], root = new int[2];
        view.GetLocationInWindow(at);
        _root.GetLocationInWindow(root);
        return (x - (at[0] - root[0]), y - (at[1] - root[1]));
    }

    /// <summary>화면 좌표 한 점을 그 뷰에 톡 친다 — 마우스 왼쪽 클릭을 창의 줄·키보드 단추에 먹인다.</summary>
    private void Tap(View view, float x, float y)
    {
        long t = SystemClock.UptimeMillis();
        int[] at = new int[2], root = new int[2];
        view.GetLocationInWindow(at);
        _root.GetLocationInWindow(root);
        float lx = x - (at[0] - root[0]), ly = y - (at[1] - root[1]);
        var down = MotionEvent.Obtain(t, t, MotionEventActions.Down, lx, ly, 0)!;
        var up = MotionEvent.Obtain(t, t + 50, MotionEventActions.Up, lx, ly, 0)!;
        view.DispatchTouchEvent(down);
        view.DispatchTouchEvent(up);
        down.Recycle();
        up.Recycle();
    }

    /// <summary>손가락 벌리기 · ＋ · － — 지도 배율(칸/픽셀)을 바꾼다.</summary>
    private void Zoom(float factor)
    {
        var host = _map.Host;
        GameThread.Post(() => host.CellsPerPixel = host.CellsPerPixel / factor);
    }

    /// <summary>
    /// 시작 영상이 도는 동안은 화면 어디를 눌러도 넘긴다 — 화면 가상패드 · 가상 키보드 단추도 그렇다.
    /// 예전에는 실제 글쇠(DispatchKeyEvent)와 영상 위 탭만 넘겨, 가상패드로 누르면 영상이 안 꺼졌다.
    /// </summary>
    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (_skipIntro is { } skip && e != null)
        {
            if (e.ActionMasked == MotionEventActions.Up) skip();
            return true;
        }
        return base.DispatchTouchEvent(e);
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e == null) return base.DispatchKeyEvent(e);

        // 안드로이드 뒤로 버튼 = 메뉴(앱 설정)를 여닫는다. 게임을 끝내는 길은 원본처럼 게임 안(기능·END GAME)에만 있다.
        if (e.KeyCode == Keycode.Back)
        {
            if (e.Action != KeyEventActions.Up) return true;
            if (_pad.Editing) return true;
            ToggleMainMenu();
            return true;
        }

        // 시작 영상이 도는 동안은 아무 글쇠 · 단추나 넘기기다.
        if (_skipIntro is { } skip && e.KeyCode != Keycode.Back)
        {
            if (e.Action == KeyEventActions.Up) skip();
            return true;
        }

        if (_padInput.Key(e)) return true;

        // 실제 키보드 — 원본 키 그대로 받는다.
        if (PadInput.KeyboardKey(e.KeyCode) is { } key)
        {
            if (e.Action == KeyEventActions.Down &&
                (e.RepeatCount == 0 || key is GameKey.Up or GameKey.Down or GameKey.Left or GameKey.Right))
                PressKey(key);
            return true;
        }
        return base.DispatchKeyEvent(e);
    }

    public override bool OnGenericMotionEvent(MotionEvent? e) =>
        (e != null && _padInput.Motion(e)) || base.OnGenericMotionEvent(e);

    /// <summary>창을 닫고 바다를 다시 굴린다.</summary>
    private void Resume()
    {
        if (!_panel.IsOpen && _held) { _held = false; _map.Host.Paused = false; }
    }

    private bool _held;

    // ── 메뉴 버튼 ───────────────────────────────────────────────────────────

    private void ToggleMainMenu()
    {
        if (_panel.IsOpen && _panel.Tag0 == "menu") { _panel.Cancel(); return; }
        if (_panel.IsOpen) return;   // 물음창 위에는 안 연다 — 그 창부터 답한다
        if (_keyboard.IsOpen) ToggleKeyboard();
        ShowMainMenu();
    }

    /// <summary>메뉴를 여는 동안 배를 세운다(이미 멈춰 있었으면 건드리지 않는다).</summary>
    private void Hold()
    {
        if (!_map.OnMap || _map.Host.Paused) return;
        _held = true;
        _map.Host.Paused = true;
    }

    /// <summary>차림표 줄이 띄운 창 수 — 떠 있는 동안 가상 패드는 결정 · 취소만 남긴다.</summary>
    private int _menuWindows;

    private void SyncMenuPad() => _pad.MenuOpen = _menuWindows > 0 || (_panel.IsOpen && _panel.Tag0 == "menu");

    /// <summary>차림표 줄의 창을 게임 줄에서 띄운다 — 배 · 말을 세우고(PC MenuItem), 떠 있는 동안 패드를 줄인다.</summary>
    private void RunMenuWindow(Action show)
    {
        _menuWindows++;
        SyncMenuPad();
        GameThread.Post(() =>
        {
            try { _map.MenuItem(show)(); }
            finally { RunOnUiThread(() => { _menuWindows--; SyncMenuPad(); }); }
        });
    }

    private void ShowMainMenu()
    {
        Hold();
        string[] rows = ["차림표", "버튼 매핑", "화면 패드", "가상 키보드 열기/닫기", "대항해시대3 단축키",
                         "세이브 저장 장소", "돌아간다"];
        _panel.Show("메뉴", rows, at =>
        {
            switch (at)
            {
                case 0: ShowAppMenu(); break;
                case 1: StartActivity(new Intent(this, typeof(PadSettingsActivity))); Resume(); break;
                case 2: ShowPadMenu(); break;
                case 3: ToggleKeyboard(); Resume(); break;
                case 4: _panel.ShowText("대항해시대3 단축키", ShortcutText(), ShowMainMenu, tag: "menu"); break;
                case 5: ShowSaveFolderMenu(); break;
                default: Resume(); break;
            }
        }, cancelIndex: rows.Length - 1, tag: "menu");
    }

    /// <summary>
    /// 차림표 — PC 판 제목 줄의 햄버거(<c>ChromeTitleBar</c>) 줄들이다. 아직 옮기지 않은 창은 그렇다고 알린다.
    /// </summary>
    private void ShowAppMenu()
    {
        // PC 판 햄버거와 같은 줄 — 원본에 없는 줄(여급 수첩 · 인물 이동 · 발견물 지도)은 모드 창에서 켜야 뜬다.
        var rows = new List<(string Text, Action Run, bool Dim)>
        {
            ("설정", () => RunMenuWindow(_map.ShowSettingsDialog), false),
            ("화면 · 소리", ShowSettings, false),
        };
        if (GameSettings.ShowBarmaidBookMenu) rows.Add(("여급 수첩", () => RunMenuWindow(_map.ShowBarmaidBook), !_map.Started));
        if (GameSettings.ShowPersonMoveMenu) rows.Add(("인물 이동", () => RunMenuWindow(_map.ShowPersonMove), !_map.Started));
        // 사람이 지어 넣는 발견물 — 원본 v1.1.89 에 개발 창 탭에서 도시락 줄로 옮겼다. 모드 「추가 발견물」을 켜야 줄이 선다.
        if (GameSettings.ExtraDiscoveries) rows.Add(("발견물 추가", () => RunMenuWindow(_map.ShowUserDiscoveries), !_map.Started));
        rows.Add(("발견물 지도", () => GameThread.Post(() => _map.Key(GameKey.D)), !_map.OnMap));
        rows.Add(("모드", () => RunMenuWindow(_map.ShowModDialog), false));
        rows.Add(("개발", () => RunMenuWindow(_map.ShowDevDialog), !_map.Started));
        rows.Add(("도구", ShowToolMenu, false));
        // 역대 판 노트(원본 리메이크의 릴리즈노트.md) — 판 목록 · 낱말 찾기.
        rows.Add(("릴리즈 노트", () => RunMenuWindow(_map.ShowReleaseNotes), false));
        rows.Add(("출처", () => RunMenuWindow(_map.ShowCredits), false));   // 더해 넣은 발견물 글 · 그림의 출처(CC BY-SA)
        rows.Add(("돌아간다", Resume, false));
        _panel.Show("차림표", [.. rows.Select(r => r.Text)], at =>
        {
            var row = rows[Math.Clamp(at, 0, rows.Count - 1)];
            if (row.Text is not ("화면 · 소리" or "도구")) Resume();
            row.Run();
        }, cancelIndex: rows.Count - 1, tag: "menu", dim: [.. rows.Select(r => r.Dim)]);
    }

    /// <summary>도구 — 리메이크 도구 앱의 게임 자료 편집 창들(PC 판 그대로).</summary>
    private void ShowToolMenu()
    {
        var tools = WorldMapWindow.Tools;
        string[] rows = [.. tools.Select(t => t.Name), "돌아간다"];
        _panel.Show("도구", rows, at =>
        {
            if (at < 0 || at >= tools.Length) { ShowAppMenu(); return; }
            Resume();
            RunMenuWindow(() => _map.OpenTool(at));
        }, cancelIndex: rows.Length - 1, tag: "menu");
    }

    /// <summary>
    /// 설정 — PC 판 설정 창(<c>SettingsDialog</c>)의 배경음악 켬/끔·크기에 안드로이드의 화면 비율을 더했다.
    /// 값은 PC 판과 같은 설정 파일(<see cref="GameSettings"/>)에 적힌다.
    /// </summary>
    private void ShowSettings()
    {
        Hold();
        string[] rows =
        [
            $"배경음악   {(GameSettings.BgmEnabled ? "켬" : "끔")}",
            $"배경음악 크기 ◀  ({GameSettings.BgmVolume})",
            $"배경음악 크기 ▶  ({GameSettings.BgmVolume})",
            $"화면 비율   {AspectFrame.Label(_gameArea.Ratio)}",
            "돌아간다",
        ];
        _panel.Show("설정", rows, at =>
        {
            switch (at)
            {
                case 0:
                    GameSettings.BgmEnabled = !GameSettings.BgmEnabled;
                    _bgm.Enabled = GameSettings.BgmEnabled;
                    break;
                case 1 or 2:
                    GameSettings.BgmVolume = Math.Clamp(GameSettings.BgmVolume + (at == 1 ? -1 : 1) * GameSettings.VolumeStep,
                                                        0, GameSettings.MaxVolume);
                    _bgm.Volume = GameSettings.BgmVolume / (double)GameSettings.MaxVolume;
                    break;
                case 3:
                    _gameArea.SetRatio(_gameArea.Ratio == ScreenRatio.FourThree ? ScreenRatio.SixteenNine : ScreenRatio.FourThree);
                    break;
                default:
                    ShowAppMenu();
                    return;
            }
            ShowSettings();
        }, cancelIndex: rows.Length - 1, tag: "menu");
    }

    private void ShowPadMenu()
    {
        Hold();
        string[] rows =
        [
            $"보이기: {OnScreenPad.ModeLabel(_pad.Mode)}",
            "가상패드 세부설정",
            "화면 패드를 기본값으로",
            "돌아간다",
        ];
        _panel.Show("화면 패드", rows, at =>
        {
            switch (at)
            {
                case 0:
                    _pad.SetMode(_pad.Mode switch
                    {
                        OnScreenMode.Auto => OnScreenMode.Shown,
                        OnScreenMode.Shown => OnScreenMode.Hidden,
                        _ => OnScreenMode.Auto,
                    });
                    ShowPadMenu();
                    break;
                case 1:
                    _pad.BeginEdit();   // 「완료」를 누르면 EditDone 이 이 메뉴로 돌아온다
                    break;
                case 2:
                    _pad.ResetToDefaults();
                    Say("화면 패드를 기본값으로 되돌렸습니다.");
                    ShowPadMenu();
                    break;
                default:
                    ShowMainMenu();
                    break;
            }
        }, cancelIndex: rows.Length - 1, tag: "menu");
    }

    /// <summary>세이브 저장 장소 — 기본 폴더(내장 저장소/CostaDelSol) 또는 직접 고른 폴더.</summary>
    private void ShowSaveFolderMenu()
    {
        Hold();
        bool access = SaveFolder.HasAccess(this);
        string now = SaveFolder.Apply(this);
        var rows = new List<(string Text, Action Run)>();
        if (!access) rows.Add(("「모든 파일 접근」 권한 허용하기", () => { SaveFolder.RequestAccess(this); ShowMainMenu(); }));
        rows.Add(($"기본 폴더로 ({SaveFolder.DefaultPath})", () =>
        {
            SaveFolder.Choose(this, null);
            Say("세이브 폴더: " + SaveFolder.Apply(this));
            ShowSaveFolderMenu();
        }));
        rows.Add(("폴더를 직접 고른다…", () =>
        {
            if (!SaveFolder.HasAccess(this)) { Say("먼저 「모든 파일 접근」 권한을 허용해 주십시오."); ShowSaveFolderMenu(); return; }
            SaveFolder.Pick(this);
        }));
        rows.Add(("돌아간다", ShowMainMenu));

        string body = $"지금 폴더: {now}\n" +
                      (access ? "윈도우판의 %APPDATA%\\CdsHelper 폴더와 SAVEDATA.CDS · AUTOSAVE.CDS 를 서로 옮겨 쓸 수 있습니다."
                              : "권한이 없어 앱 안에 저장합니다. 앱을 지우면 세이브도 지워집니다.");
        _panel.Show("세이브 저장 장소", rows.Select(r => r.Text).ToList(), at =>
        {
            if (at < 0 || at >= rows.Count) { ShowMainMenu(); return; }
            rows[at].Run();
        }, cancelIndex: rows.Count - 1, tag: "menu", body: body);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != SaveFolder.RequestPickFolder) return;
        if (resultCode != Result.Ok || SaveFolder.FromPicked(data?.Data) is not { } path)
        {
            Say("폴더를 고르지 않았습니다.");
            ShowSaveFolderMenu();
            return;
        }
        // 정말 쓸 수 있는지 한 번 적어 본다 — SD 카드 등은 권한이 있어도 막힐 수 있다.
        try
        {
            Directory.CreateDirectory(path);
            string probe = System.IO.Path.Combine(path, ".costadelsol-write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            SaveFolder.Choose(this, path);
            Say("세이브 폴더: " + SaveFolder.Apply(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("그 폴더에는 쓸 수 없습니다: " + ex.Message);
        }
        ShowSaveFolderMenu();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == SaveFolder.RequestLegacyPermission) Say("세이브 폴더: " + SaveFolder.Apply(this));
    }

    public static string ShortcutText() => string.Join("\n\n", GameKeys.Shortcuts.Select(g =>
        $"【{g.Group}】\n" + string.Join("\n", g.Rows.Select(r => $"  {r.Keys}  —  {r.Use}"))));

    // ── 수명 ────────────────────────────────────────────────────────────────

    protected override void OnResume()
    {
        base.OnResume();
        _bindings = PadBindings.Load(this);   // 버튼 매핑 화면에서 돌아왔을 수 있다
        SaveFolder.Apply(this);               // 「모든 파일 접근」 설정 화면에서 돌아왔을 수 있다
        _shipDigit.Clear();
        _pad.Invalidate();
        _gl?.OnResume();
        if (_bgm != null) _bgm.Enabled = GameSettings.BgmEnabled;   // 밖에 나갔다 오면 다시 튼다
        HideSystemBars();
        System.Windows.Application.Current.RaiseActive(true);   // PC 판 Application.Activated(쪽지 · 소리가 본다)
    }

    protected override void OnPause()
    {
        _padInput.Reset();
        _bgm.Enabled = false;   // 앱이 뒤로 가면 음악을 멈춘다
        System.Windows.Application.Current.RaiseActive(false);
        _gl?.OnPause();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _ui.RemoveCallbacksAndMessages(null);
        base.OnDestroy();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) HideSystemBars();
    }

    private void HideSystemBars()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            var controller = Window?.InsetsController;
            if (controller == null) return;
            controller.Hide(WindowInsets.Type.SystemBars());
            controller.SystemBarsBehavior = 2;   // BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
        else
        {
#pragma warning disable CA1422, CS0618 // API 30 미만에서만 쓰는 옛 방식
            if (Window?.DecorView is { } decor)
                decor.SystemUiVisibility = (StatusBarVisibility)(
                    SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky);
#pragma warning restore CA1422, CS0618
        }
    }
}
