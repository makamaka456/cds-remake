using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.UI.Views;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CostaDelSol.Droid.Game;
using CostaDelSol.Droid.Input;
using CostaDelSol.Droid.Town;
using Sim;

// 시험기: 원본 바다 화면(WorldMapWindow)으로 새 판 → 도시 → 출항 → 항해를 화면 없이 돈다.
string root = args[0];
AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", root + "/");
TableCache.FolderOverride = root + "/exe-tables";
GameSave.FolderOverride = root + "/save";
Directory.CreateDirectory(root + "/save");
foreach (var f in Directory.GetFiles(root + "/save")) File.Delete(f);
foreach (var a in args.Skip(1)) Script.Answers.Enqueue(a);

Drive.Install();
if (Environment.GetEnvironmentVariable("QA") != null) Drive.Qa = true;
CdsHelper.Support.Local.Settings.AppSettings.LastSaveFilePath = Path.Combine(root, "SAVEDATA.CDS");
GameUi.Font = GameFont.Open(root);
GameUi.Sprites = UiSprites.Open(root);
var stage = new CityStage();
CityPicView.Stage = stage;
var map = new WorldMapWindow();
map.Noted += t => { if (t.Length > 0) Log.W($"[띠] {t}"); };
map.SetDirectory(root);
var player = () => map.Game.Player;

map.TitleNewGame();
GameThread.Drain();
Log.W($"새 판: {player().Name} 이야기 {player().ActiveStoryBook} 배 {player().Ships.Count} 지도 {map.OnMap}");
if (Environment.GetEnvironmentVariable("LOADOLD") is { } old)
{
    File.Copy(old, CdsHelper.Game.Engine.GameSave.Path, true);
    map.ReturnToTitle(); GameThread.Drain(); map.LoadGame(); GameThread.Drain();
    Log.W($"[옛 세이브] 배 {string.Join(",", player().Ships.Select(x => $"{x.Name}/{x.Hull.Name}/빌림={x.Lent}"))}");
    return;
}
if (Environment.GetEnvironmentVariable("MODE") == "qa-city")
{
    Leave();
    foreach (var step in (Environment.GetEnvironmentVariable("PLAN") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
    { var parts = step.Split(':'); Visit(parts[0], parts.Length > 1 ? parts[1].Split(',') : []); }
    var range = (Environment.GetEnvironmentVariable("RANGE") ?? "0-130").Split('-');
    Qa.Cities(map, stage, int.Parse(range[0]), int.Parse(range[1]));
    Qa.Save($"qa-city-{range[0]}-{range[1]}.txt");
    return;
}
if (Environment.GetEnvironmentVariable("MODE") == "qa-disc")
{
    Leave();
    var range = (Environment.GetEnvironmentVariable("RANGE") ?? "0-300").Split('-');
    Qa.Discoveries(map, int.Parse(range[0]), int.Parse(range[1]));
    Qa.Save($"qa-disc-{range[0]}-{range[1]}.txt");
    return;
}
if (Environment.GetEnvironmentVariable("MODE") == "notes")
{
    CdsHelper.Game.Local.Settings.GameSettings.ShowSkillOverlay = true;
    Leave();
    foreach (var step in (Environment.GetEnvironmentVariable("PLAN") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
    { var parts = step.Split(':'); Visit(parts[0], parts.Length > 1 ? parts[1].Split(',') : []); if (parts[0] == "저택" && parts.Length > 1) break; }
    System.Windows.WpfHost.LayoutNow();
    var shot = Android.Graphics.Bitmap.CreateBitmap(1000, 480, Android.Graphics.Bitmap.Config.Argb8888);
    shot.EraseColor(unchecked((int)0xFF405060));
    System.Windows.WpfHost.SideRoom = 180;
    System.Windows.WpfHost.DrawTownLayers(new Android.Graphics.Canvas(shot), 1, 180);
    Png.Save(shot, Path.Combine(Drive.Dir, "notes.png"));
    Log.W($"쪽지 {System.Windows.WpfHost.TownLayers.Count}개 배 {player().Ships.Count}");
    return;
}
if (Environment.GetEnvironmentVariable("MODE") == "land")
{
    Drive.Dir = Environment.GetEnvironmentVariable("SHOTS") ?? ".";
    Drive.Install();
    GameUi.Font = GameFont.Open(root);
    GameUi.Sprites = UiSprites.Open(root);
    Log.W($"띠 조각 {(GameUi.Sprites != null ? "있음" : "없음 " + UiSprites.LastError)}");
    Drive.Add("배치 판 찍기", w => Drive.Shot(w, "deploy0"));
    Drive.Add("1번 자리 누르기", w => Drive.Click(w, 16 + 40, 16 + 40));
    Drive.Add("배치 판 찍기", w => Drive.Shot(w, "deploy1"));
    Drive.Add("고르는 칸 1 → 2번 자리 끌기", w => Drive.Drag(w, 480 + 40, 16 + 40, 128 + 40, 16 + 40));
    Drive.Add("배치 판 찍기", w => Drive.Shot(w, "deploy2"));
    Drive.Add("대장 → 1번 자리 끌기", w => Drive.Drag(w, 368 + 40, 16 + 40, 16 + 40, 16 + 40));
    Drive.Add("결정", w => Drive.Click(w, 536 + 24, 292 + 12));
    var line = LandDeployDialog.Show(map, map.Game, "시험");
    Log.W($"배치 결과: {(line == null ? "취소" : string.Join(",", line))}");
    if (line != null)
    {
        var dice = new GameRandom(1234);
        var field = new CdsHelper.Game.Engine.Land.LandBattle(line, player(), null, 2, 0, 0, 0, dice, city: 1);
        for (int i = 0; i < 40; i++) Drive.Add($"싸움터 {i}", w => { Drive.Shot(w, $"battle{i:D2}"); Drive.Key(w, System.Windows.Input.Key.Enter); });
        bool won = LandBattleScene.Run(map, map.Game, field, dice);
        Log.W($"싸움 끝: 이김={won} 쓰러짐={field.Wiped}");
    }
    return;
}

void Leave()
{
    var m = GameMenuHost.Last;
    if (m?.Shown is { } menu) m.Press(menu.Rows[^1].Text);
    GameThread.Drain();
}
void Visit(string tag, params string[] rows)
{
    var spot = stage.Spots.FirstOrDefault(s => s.Tag == tag && s.Code >= 0);
    if (spot == null) { Log.W($"(건물 없음: {tag}) 있는 것: " + string.Join(",", stage.Spots.Select(s => s.Tag))); return; }
    Log.W($"--- {tag}");
    spot.Left(); GameThread.Drain();
    foreach (var r in rows) { GameMenuHost.Last?.Press(r); GameThread.Drain(); }
    if (stage.Active) Leave();
}
Leave();
var plan = Environment.GetEnvironmentVariable("PLAN") ?? "";
Drive.Add("힌트 책 읽기", w =>
{
    var want = Environment.GetEnvironmentVariable("BOOK") ?? "카르낙";
    foreach (var (img, r) in Drive.Find<System.Windows.Controls.Image>(w))
        if (img.Tag is BookTable.Book book && book.Hints.Any(h => (map.Game.Hints?.Find(h)?.Name ?? "").Contains(want)))
        {
            Log.W($"  책 {r} 힌트 {string.Join(",", book.Hints)}");
            Drive.ClickCenter(w, r);
            return;
        }
    Log.W("  힌트 책 없음");
});
Drive.Add("책 찍기", w => { Drive.Shot(w, "book"); Drive.Key(w, System.Windows.Input.Key.Escape); });
foreach (var step in plan.Split(';', StringSplitOptions.RemoveEmptyEntries))
{
    if (!stage.Active) break;
    var parts = step.Split(':');
    Visit(parts[0], parts.Length > 1 ? parts[1].Split(',') : []);
}
Log.W($"도시 끝: 도시 그림 {stage.Active} 금 {player().Gold} 배 {player().Ships.Count} 선원 {player().Crew} 날 {player().Date:yyyy-MM-dd}");

if (Environment.GetEnvironmentVariable("MODE") == "qa-long")
{
    // 긴 항해: 항구를 차례로 돈다 — 닿으면(지나가다 들른 곳도) 보급하고 다시 떠난다.
    Drive.Qa = true;
    var route = (Environment.GetEnvironmentVariable("ROUTE") ?? "7,12,72,78,85,7,0").Split(',').Select(int.Parse).ToList();
    var clock = System.Diagnostics.Stopwatch.StartNew();
    int ports = 0, errs = 0;
    string State() => $"{player().Date:yyyy-MM-dd} 금 {player().Gold} 배 {player().Ships.Count} 선원 {player().Crew} 물 {player().SupplyOf(SupplyKind.Water)} 식량 {player().SupplyOf(SupplyKind.Food)} 칸 {map.Host.ShipCell?.CellX:F0},{map.Host.ShipCell?.CellY:F0}";
    void Port()
    {
        ports++;
        Log.W($"[입항] {player().CityName} {State()}");
        Drive.Buttons["SupplyDialog"] = new Queue<string>(["최대", "결정"]);
        if (player().Gold < 20000) player().SetGold(100000);   // 검수용 — 보급비가 떨어져 멈추지 않게
        foreach (var t in new[] { "항구" })
        {
            var spot = stage.Spots.FirstOrDefault(s => s.Tag == t && s.Code >= 0);
            if (spot == null) { Log.W("  (항구 없음)"); Leave(); return; }
            spot.Left(); GameThread.Drain();
            foreach (var r in new[] { "보급", "출항" }) { GameMenuHost.Last?.Press(r); GameThread.Drain(); }
        }
        for (int k = 0; k < 4 && stage.Active; k++) Leave();
        Log.W($"[출항] {State()} 도시 그림 {stage.Active}");
    }
    foreach (int city in route)
    {
        GameMapCoords.TryCityCell(city, out double gx, out double gy);
        string goalName2 = map.Game.CityName(city);
        if (map.Host.CityOpen is { } isOpen && !isOpen(city)) { Log.W($"[건너뜀] {goalName2} — 아직 안 선 도시"); continue; }
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (stage.Active) Port();
            var (ok2, msg2) = map.Host.StartAutoSail(gx, gy);
            Log.W($"[항로] → {goalName2} {ok2} {msg2}");
            bool arrived = false;
            for (int i = 0; i < 20 * 900; i++)
            {
                try { map.Frame(0.05, 640, 422); GameThread.Drain(); }
                catch (Exception ex) { errs++; Log.W($"  !! {ex.GetType().Name}: {ex.Message} @ {ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("CdsHelper") || l.Contains("CostaDelSol"))?.Trim()}"); }
                if (stage.Active) { arrived = true; break; }
                if (player().Ships.Count == 0) break;
                if (!map.Host.AutoSailing && i > 40) break;
            }
            if (player().Ships.Count == 0) { Log.W("[끝] 배가 없다"); break; }
            if (arrived && player().CityName == goalName2) { Port(); break; }
            if (arrived) continue;
            // 자동항해가 닻을 내렸다 — 숫자판으로 항구 쪽으로 저어 들어간다.
            for (int k = 0; k < 40 && !stage.Active; k++)
            {
                var c0 = map.Host.ShipCell; if (c0 == null) break;
                map.Key(GameKey.D0 + Dir(gx - c0.Value.CellX, gy - c0.Value.CellY)); GameThread.Drain();
                for (int f = 0; f < 20 && !stage.Active; f++) { map.Frame(0.05, 640, 422); GameThread.Drain(); }
            }
            Log.W($"  [저어 감] 도시 그림 {stage.Active} {player().CityName} {State()}");
            if (stage.Active && player().CityName == goalName2) { Port(); break; }
        }
    }
    Log.W($"[긴 항해 끝] 입항 {ports} · 오류 {errs} · {clock.Elapsed.TotalMinutes:F1}분 · {State()} · 발견 {player().Discoveries.Count} · 억지로 닫음 {string.Join(",", Drive.Stuck)}");
    return;
}
if (Environment.GetEnvironmentVariable("MODE") == "gallery")
{
    var g = map.Game; var rnd = new Random(7);
    var only = Environment.GetEnvironmentVariable("ONLY"); void Try(string name, Action a) { if (only != null && !only.Split(',').Contains(name)) return; Log.W($"--- {name}"); try { a(); } catch (Exception ex) { Log.W($"  !! {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace?.Split('\n').Take(4).Aggregate((x, y) => x + "\n" + y)}"); } GameThread.Drain(); }
    Try("선원배분", () => Log.W($"  결과 {CrewShareDialog.Show(map, player())} 창 {string.Join(",", System.Windows.WpfHost.Shown.Select(x => x.GetType().Name + (x.IsVisible ? "" : "(숨김)")))}"));
    Try("도시정보", () =>
    {
        Log.W("  띠 앞: " + string.Join(" | ", map.BarCells()));
        map.ShowCityInfoMenu(); GameThread.Drain();
        var h = GameMenuHost.Last!; Log.W("  창: " + string.Join(" | ", h.Shown!.Rows.Select(r => r.Text)));
        foreach (var t in new[] { "시세", "남은일수", "명성" }) { var row = h.Shown!.Rows.First(r => r.Text.Contains(t)); h.Press(row.Text); GameThread.Drain(); }
        Log.W("  창 뒤: " + string.Join(" | ", h.Shown!.Rows.Select(r => r.Text)));
        Log.W("  띠 뒤: " + string.Join(" | ", map.BarCells()));
        h.Press("취소"); GameThread.Drain();
    });
    Try("미궁", () => Log.W($"  미궁 결과 {WorldMapWindow.MazeGame!(map, rnd, player(), null)}"));
    Try("함대정보", () => FleetInfoDialog.Show(map, player(), "", g.Items));
    Try("제독정보", () => PlayerInfoDialog.Show(map, g));
    Try("보관소", () => StorageDialog.Show(map, player(), g.Items));
    Try("백과사전", () => EncyclopediaDialog.Show(map, g));
    Try("연표", () => ChronicleDialog.ShowChronicle(map, player(), g.Discoveries?.Table));
    Try("항해일지", () => ChronicleDialog.ShowLogbook(map, player(), g.Discoveries?.Table));
    Try("부하", () => MateRosterDialog.Show(map, player()));
    Try("탑", () => TowerPuzzleDialog.Play(map, rnd));
    Try("스핑크스", () => SphinxQuizDialog.Play(map, rnd));
    Try("낚시", () => FishingGameDialog.Play(map, rnd));
    Try("코인", () => CoinPuzzleDialog.Play(map, rnd));
    Try("성배", () => GrailPuzzleDialog.Play(map, player(), rnd));
    Try("큐브", () => CubePuzzleDialog.Play(map, player(), rnd));
    Try("포커", () => PokerDialog.Play(map, g, 0));
    Try("캐릭터", () => CharacterMakeDialog.Show(map, player(), g.Directory));
    if (only == "일기토") { Drive.Add("아래", w => Drive.Key(w, System.Windows.Input.Key.Down)); Drive.Add("엔터", w => Drive.Key(w, System.Windows.Input.Key.Enter)); }
    if (only == "일기토")
        for (int i = 0; i < 12; i++)
        {
            int k = i;
            Drive.Add($"일기토 {k}", w =>
            {
                Drive.Shot(w, $"duel{k:D2}");
                var focused = Drive.Find<GameButton>(w).Where(b => b.E.Focused).Select(b => b.E.Text).FirstOrDefault();
                Log.W($"  초점: {focused}");
                Drive.Key(w, k % 3 == 0 ? System.Windows.Input.Key.Down : k % 3 == 1 ? System.Windows.Input.Key.Down : System.Windows.Input.Key.Enter);
            });
        }
    Try("일기토", () => typeof(WorldMapWindow).GetMethod("PlayDuel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(map, null));
    if (only == "도구") { CdsHelper.Game.Engine.GameSave.Save(g.Player); Log.W($"저장: {File.Exists(CdsHelper.Game.Engine.GameSave.Path)}");
        { var sv = new CdsHelper.Support.Local.Helpers.SaveDataService(new CdsHelper.Support.Local.Helpers.CityService()); var inf = sv.ReadSaveFile(CdsHelper.Game.Engine.GameSave.Path); var pd = sv.ReadPlayerData(CdsHelper.Game.Engine.GameSave.Path);
          Log.W($"세이브 읽기: 인물 {inf.Characters.Count} 고용가능 {inf.Characters.Count(c => c.CanRecruit)} 플레이어 {(pd == null ? "없음" : "있음")} 현재 {sv.CurrentPlayerData != null} 힌트 {inf.Hints?.Count}"); } for (int t = 0; t < WorldMapWindow.Tools.Length; t++) { int k = t; Log.W($"--- 도구 {WorldMapWindow.Tools[k].Name}"); try { map.OpenTool(k); } catch (Exception ex) { Log.W($"  !! {ex.GetType().Name}: {ex.Message} @ {ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("CdsHelper"))}"); } GameThread.Drain(); } return; }
    Try("발견지도", map.MapByKey);
    Try("설정", map.ShowSettingsDialog);
    Try("모드", map.ShowModDialog);
    Try("개발", map.ShowDevDialog);
    Try("여급", map.ShowBarmaidBook);
    Try("이동", map.ShowPersonMove);
    Try("묘책", () => RuseEditDialog.Show(map));
    return;
}
if (Environment.GetEnvironmentVariable("MODE") == "sea")
{
    Log.W("--- 모의해전");
    Drive.Add("아래 띠 살피기", w =>
    {
        foreach (var (img, r) in Drive.Find<System.Windows.Controls.Image>(w))
            if (r.Bottom > 500 && r.X > 300 && r.Right < 560)
                Log.W($"  그림 {r} {(img.Source as System.Windows.Media.Imaging.BitmapImage)?.UriSource?.LocalPath ?? img.Source?.GetType().Name} 어버이 {img.VisualParent?.GetType().Name} {System.Windows.Controls.Canvas.GetLeft(img)},{System.Windows.Controls.Canvas.GetTop(img)}");
        Drive.Key(w, System.Windows.Input.Key.Escape);
    });
    typeof(WorldMapWindow).GetMethod("MockSeaBattle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(map, null);
    GameThread.Drain();
    Log.W($"해전 뒤: 배 {player().Ships.Count} 선원 {player().Crew}");
    return;
}
if (Environment.GetEnvironmentVariable("MODE") == "overlay")
{
    CdsHelper.Game.Local.Settings.GameSettings.ShowMiniMap = true;
    CdsHelper.Game.Local.Settings.GameSettings.ShowConditionOverlay = true;
    CdsHelper.Game.Local.Settings.GameSettings.ShowCoordOverlay = true;
    CdsHelper.Game.Local.Settings.GameSettings.ShowPeopleOverlay = true;
    for (int f = 0; f < 60; f++) { map.Frame(0.05, 640, 422); GameThread.Drain(); System.Windows.WpfHost.Tick(); Thread.Sleep(30); }
    var shot = Android.Graphics.Bitmap.CreateBitmap(640, 422, Android.Graphics.Bitmap.Config.Argb8888);
    shot.EraseColor(unchecked((int)0xFF203858));
    System.Windows.WpfHost.LayoutNow();
    System.Windows.WpfHost.DrawMapLayers(new Android.Graphics.Canvas(shot), 1, 29);
    Png.Save(shot, Path.Combine(Drive.Dir, "overlay.png"));
    Log.W($"덧그림 {System.Windows.WpfHost.MapLayers.Count}개");
    return;
}
if (Environment.GetEnvironmentVariable("MODE") == "walk")
{
    foreach (var (key, name) in new[] { (GameKey.D6, "동"), (GameKey.D8, "북"), (GameKey.D2, "남"), (GameKey.D4, "서") })
    {
        var c0 = map.Host.ShipCell;
        for (int i = 0; i < 6; i++)
        {
            map.Key(key); GameThread.Drain();
            for (int f = 0; f < 30; f++) { map.Frame(0.033, 640, 422); GameThread.Drain(); }
        }
        Log.W($"걷기 {name}: {c0?.CellX:F1},{c0?.CellY:F1} → {map.Host.ShipCell?.CellX:F1},{map.Host.ShipCell?.CellY:F1} 뭍={map.Host.IsOnLand}");
    }
    return;
}
// 바다 — 목표 발견물 쪽으로 자동항해 → 상륙 → 걸어서 찾는다.
int goal = int.Parse(Environment.GetEnvironmentVariable("GOAL") ?? "20");
var goalName = Environment.GetEnvironmentVariable("GOALNAME") ?? "카르낙"; var row = map.Game.Discoveries!.Table.Discoveries.First(r => r.Name.Contains(goalName) && r.HasPlace);
double tx = (row.X1 + row.X2) / 2.0 / ExploredMap.CellsPerBlock * 4, ty = (row.Y1 + row.Y2) / 2.0 / ExploredMap.CellsPerBlock * 4;
Log.W($"목표 {row.Name} 칸 X{row.X1}-{row.X2} Y{row.Y1}-{row.Y2}");
void Run(int secs, Func<bool>? until = null)
{
    for (int i = 0; i < secs * 20; i++)
    {
        map.Frame(0.05, 640, 422);
        GameThread.Drain();
        if (i % 400 == 0) Log.W($"  {i / 20}s 칸 {map.Host.ShipCell?.CellX:F1},{map.Host.ShipCell?.CellY:F1} 뭍={map.Host.IsOnLand} 멈춤={map.Host.Paused} 자동={map.Host.AutoSailing} 날 {player().Date:MM-dd} 물 {player().SupplyOf(SupplyKind.Water)}");
        if (stage.Active) { Log.W("도시에 들어왔다"); Leave(); return; }
        if (until?.Invoke() == true) return;
    }
}
Log.W("배 칸 " + map.Host.ShipCell?.CellX + "," + map.Host.ShipCell?.CellY);
double cx = (row.X1 + row.X2) / 2.0, cy = (row.Y1 + row.Y2) / 2.0;
var (ok, msg) = map.Host.StartAutoSail(cx, cy);
Log.W($"자동항해: {ok} {msg}");
Run(int.Parse(Environment.GetEnvironmentVariable("SECS") ?? "120"), () => !map.Host.AutoSailing);
Log.W($"닿음: 칸 {map.Host.ShipCell?.CellX:F1},{map.Host.ShipCell?.CellY:F1} 뭍 가까움={map.Host.IsNearLand()}");
int Dir(double dx, double dy) => Math.Abs(dy) * 2 <= Math.Abs(dx) ? (dx > 0 ? 6 : 4) : Math.Abs(dx) * 2 <= Math.Abs(dy) ? (dy > 0 ? 2 : 8) : dx > 0 ? (dy > 0 ? 3 : 9) : (dy > 0 ? 1 : 7);
for (int k = 0; k < 30 && !map.Host.IsNearLand(); k++)
{
    var c0 = map.Host.ShipCell!.Value;
    map.Key(GameKey.D0 + Dir(cx - c0.CellX, cy - c0.CellY)); GameThread.Drain();
    Run(1);
}
Log.W($"해안: 칸 {map.Host.ShipCell?.CellX:F1},{map.Host.ShipCell?.CellY:F1} 뭍 가까움={map.Host.IsNearLand()}");
// 상륙
map.Key(GameKey.D0); GameThread.Drain();
GameMenuHost.Last?.Press("상륙"); GameThread.Drain();
GameMenuHost.Last?.Press("탐색"); GameThread.Drain();
Log.W($"상륙 뒤 뭍={map.Host.IsOnLand}");
// 걸어서 목표로 — 1초마다 숫자판으로 그쪽을 바라본다.
for (int k = 0; k < 120 && player().Discoveries.Count == 0; k++)
{
    if (k % 10 == 0) Log.W($"  걷기 칸 {map.Host.ShipCell?.CellX:F1},{map.Host.ShipCell?.CellY:F1}");
    var c = map.Host.ShipCell;
    if (c == null) break;
    double dx = cx - c.Value.CellX, dy = cy - c.Value.CellY;
    int d = Dir(dx, dy);
    map.Key(GameKey.D0 + d); GameThread.Drain();
    Run(1);
}
Log.W($"끝: {player().Date:yyyy-MM-dd} 발견 {string.Join(",", player().Discoveries)} 뭍={map.Host.IsOnLand}");
if (Environment.GetEnvironmentVariable("SAVETEST") != null)
{
    // 저장 → 불러오기 → 다시 저장 해서 두 기록이 같은지 본다.
    string Snap(string name)
    {
        string err = map.Game.Save();
        string dst = Path.Combine(Drive.Dir, name);
        File.Copy(CdsHelper.Game.Engine.GameSave.Path, dst, true);
        Log.W($"[저장] {name} {(err.Length == 0 ? "ok" : err)} {new FileInfo(dst).Length}B 날 {player().Date:yyyy-MM-dd} 금 {player().Gold} 배 {player().Ships.Count} 발견 {player().Discoveries.Count} 칸 {map.Host.ShipCell?.CellX:F1},{map.Host.ShipCell?.CellY:F1} 뭍={map.Host.IsOnLand}");
        return dst;
    }
    var a = Snap("save-a.cds");
    var dd = CdsHelper.Game.Engine.GameSave.Load(a);
    Log.W($"[읽음] Lent={string.Join(",", dd!.ShipStats!.Select(x => x.Lent))} 지금 {string.Join(",", player().Ships.Select(x => x.Lent))}");
    map.ReturnToTitle(); GameThread.Drain();
    map.LoadGame(); GameThread.Drain();
    Log.W($"[불러옴] 지금 {string.Join(",", player().Ships.Select(x => x.Lent))}");
    var b = Snap("save-b.cds");
    var la = File.ReadAllLines(a); var lb = File.ReadAllLines(b);
    var diff = la.Zip(lb).Select((p, i) => (p, i)).Where(x => x.p.First != x.p.Second).Take(20).ToList();
    Log.W($"[왕복] 줄 {la.Length}/{lb.Length} 다른 줄 {la.Zip(lb).Count(p => p.First != p.Second)}");
    foreach (var (p, i) in diff) Log.W($"   {i}: {p.First.Trim()}  ⇒  {p.Second.Trim()}");
    // 불러온 판으로 몇 초 더 항해해 본다.
    Run(20);
    Log.W($"[불러온 뒤 항해] 날 {player().Date:yyyy-MM-dd} 칸 {map.Host.ShipCell?.CellX:F1},{map.Host.ShipCell?.CellY:F1}");
}
