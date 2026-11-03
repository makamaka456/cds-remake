using CdsHelper.Game.Engine.Disev;
using CdsHelper.Game.UI.Views;
using CostaDelSol.Droid.Game;

namespace Sim
{
    /// <summary>자동 검수 — 결과 줄을 모아 파일로 남긴다.</summary>
    public static class Qa
    {
        public static readonly List<string> Report = [];

        static void Note(string line) { Report.Add(line); Log.W("QA " + line); }

        public static void Save(string name)
        {
            File.WriteAllLines(Path.Combine(Drive.Dir, name), Report);
        }

        static readonly string[] SkipRows = ["출항", "탐험을 떠난다", "떠난다", "은퇴", "기능", "게임 종료", "로드"];

        /// <summary>도시마다 들어가 건물을 누르고 차림표 줄을 하나씩 돌린다.</summary>
        public static void Cities(WorldMapWindow map, CostaDelSol.Droid.Town.CityStage stage, int from, int to)
        {
            Drive.Qa = true;
            var game = map.Game;
            if (Environment.GetEnvironmentVariable("NOSTORY") != null) game.Player.SetActiveStoryBook(null);
            var enter = typeof(WorldMapWindow).GetMethod("ShowCityPicture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            // 새 판은 리스본 그림에서 시작한다 — 먼저 걷어야 첫 도시가 제 그림으로 열린다.
            if (CityPicView.Active is { } first) { first.Close(); GameThread.Drain(); }
            for (int city = from; city <= to; city++)
            {
                string name = game.CityName(city);
                if (string.IsNullOrEmpty(name)) continue;
                int pressed = 0, failed = 0;
                var errors = new List<string>();
                Drive.Stuck.Clear();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                void Run(string what, Action a)
                {
                    Script.Steps = 0;
                    Drive.ResetSeen();
                    try { a(); GameThread.Drain(); }
                    catch (Exception ex)
                    {
                        failed++;
                        var inner = ex is System.Reflection.TargetInvocationException t && t.InnerException != null ? t.InnerException : ex;
                        errors.Add($"{what}: {inner.GetType().Name} {inner.Message} @ {inner.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("CdsHelper") || l.Contains("CostaDelSol"))?.Trim()}");
                    }
                    foreach (var w in System.Windows.WpfHost.Shown.Where(x => x is not (FleetLabelWindow or SkillOverlayWindow)).ToList()) { Drive.Stuck.Add(w.GetType().Name + "(남음)"); w.Close(); }
                }
                void CloseMenus()
                {
                    for (int i = 0; i < 6 && CdsHelper.Game.Engine.Menu.GameMenuHost.Last is { Shown: { } m } host; i++)
                        Run("닫기", () => host.Press(m.Rows[^1].Text));
                    for (int i = 0; i < 3; i++) Run("뒤로", () => CityPicView.Active?.Key(CostaDelSol.Droid.Input.GameKey.Escape));
                }
                bool Enter()
                {
                    if (stage.Active) return true;
                    game.Player.SetGold(500_000);
                    bool ok = false;
                    Run("입항", () => ok = (bool)enter.Invoke(map, [city, name, false, false])!);
                    CloseMenus();
                    return ok && stage.Active;
                }
                if (!Enter()) { Note($"{city,3} {name} — 도시 그림 없음(건너뜀)"); continue; }
                var spots = stage.Spots.Where(s => s.Code >= 0).Select(s => s.Tag).Distinct().ToList();
                foreach (string tag in spots)
                {
                    if (!Enter()) break;
                    var spot = stage.Spots.FirstOrDefault(s => s.Tag == tag && s.Code >= 0);
                    if (spot == null) continue;
                    Run(tag, spot.Left);
                    var rows = CdsHelper.Game.Engine.Menu.GameMenuHost.Last?.Shown?.Rows.Select(r => (r.Text, On: r.Run != null && !r.Dim)).ToList() ?? [];
                    if (rows.Count == 0) { CloseMenus(); Enter(); Run(tag, spot.Left); rows = CdsHelper.Game.Engine.Menu.GameMenuHost.Last?.Shown?.Rows.Select(r => (r.Text, On: r.Run != null && !r.Dim)).ToList() ?? []; }
                    Log.W($"  [건물] {tag} 줄 {rows.Count}");
                    CloseMenus();
                    foreach (var (text, on) in rows.Take(Math.Max(0, rows.Count - 1)))
                    {
                        if (!on || SkipRows.Any(text.Contains)) continue;
                        if (!Enter()) break;
                        Run(tag, spot.Left);
                        if (CdsHelper.Game.Engine.Menu.GameMenuHost.Last is { Shown: not null } h)
                        {
                            Run($"{tag}/{text}", () => h.Press(text));
                            pressed++;
                        }
                        CloseMenus();
                    }
                }
                if (CityPicView.Active is { } view) Run("나가기", view.Close);
                string stuck = Drive.Stuck.Count > 0 ? " · 억지로 닫음: " + string.Join(",", Drive.Stuck.Distinct()) : "";
                Note($"{city,3} {name} — 건물 {spots.Count} · 줄 {pressed} · 오류 {failed} · {clock.ElapsedMilliseconds}ms{stuck}");
                foreach (var e in errors.Distinct()) Note($"      ! {e}");
            }
        }

        /// <summary>발견물마다 사건 대본을 돌린다(관문을 건너뛴다).</summary>
        public static void Discoveries(WorldMapWindow map, int from, int to)
        {
            Drive.Qa = true;
            var game = map.Game;
            var table = game.Discoveries!.Table;
            foreach (var row in table.Discoveries.Where(r => r.Id >= from && r.Id <= to))
            {
                Script.Steps = 0;
                Drive.ResetSeen();
                Drive.Stuck.Clear();
                if (game.Player.Gold < 100_000) game.Player.SetGold(500_000);
                int before = Log.Lines.Count;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                string result;
                try
                {
                    bool scripted = DisevRunner.Run(map, game, row.Id);
                    GameThread.Drain();
                    result = scripted ? "대본" : "대본 없음";
                    if (DisevRunner.LastEndedInGameOver) result += " · 게임오버로 끝";
                }
                catch (Exception ex)
                {
                    var inner = ex is System.Reflection.TargetInvocationException t && t.InnerException != null ? t.InnerException : ex;
                    result = $"오류 {inner.GetType().Name}: {inner.Message} @ {inner.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("CdsHelper") || l.Contains("CostaDelSol"))?.Trim()}";
                }
                // 남은 창 걷기
                foreach (var w in System.Windows.WpfHost.Shown.Where(x => x is not (FleetLabelWindow or SkillOverlayWindow)).ToList()) { Drive.Stuck.Add(w.GetType().Name + "(남음)"); w.Close(); }
                int lines = Log.Lines.Count - before;
                string stuck = Drive.Stuck.Count > 0 ? " · 억지로 닫음: " + string.Join(",", Drive.Stuck) : "";
                Note($"{row.Id,3} {row.Name} — {result} · 줄 {lines} · {clock.ElapsedMilliseconds}ms{stuck}");
            }
        }
    }
}
