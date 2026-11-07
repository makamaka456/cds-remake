using System.Windows;
namespace Sim
{
    /// <summary>WPF 창 시험 손 — 차례로 할 일(누르기 · 찍기)을 쌓아 두면 모달이 쉴 때마다 하나씩 한다.</summary>
    public static class Drive
    {
        public static readonly Queue<(string Name, Action<Window> Run, string? For)> Steps = new();
        // 맨 앞 단계가 이 창 것인지 — For 를 준 단계는 그 이름의 창에만 걸린다(딴 창은 여느 때처럼 찍고 닫는다).
        private static bool StepFits(Window w) => Steps.Count > 0 && (Steps.Peek().For is not { } kind || kind == w.GetType().Name);
        public static string Dir = Environment.GetEnvironmentVariable("SHOTS") ?? ".";
        private static readonly Dictionary<string, int> Seen = [];
        private static Window? _last;
        private static int _empty;
        /// <summary>검수 모드 — 안 닫히는 창은 억지로 닫고 적어 둔다.</summary>
        public static bool Qa;
        public static readonly List<string> Stuck = [];
        public static void ResetSeen() { Seen.Clear(); Played.Clear(); }
        private static readonly HashSet<string> Played = [];
        /// <summary>창 이름마다 누를 단추 차례 — 환경 변수 WPF="SupplyDialog:최대,결정;…".</summary>
        public static readonly Dictionary<string, Queue<string>> Buttons = Parse(Environment.GetEnvironmentVariable("WPF") ?? "");
        private static Dictionary<string, Queue<string>> Parse(string text)
        {
            var d = new Dictionary<string, Queue<string>>();
            foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':');
                if (!d.TryGetValue(kv[0], out var q)) d[kv[0]] = q = new Queue<string>();
                foreach (var b in kv[1].Split(',')) q.Enqueue(b);
            }
            return d;
        }
        public static void Install()
        {
            Wpf.Idle = () =>
            {
                var w = WpfHost.Shown.LastOrDefault(x => x is not System.Windows.Controls.Primitives.PopupWindow);
                if (w == null)
                {
                    // 창 없이 기다리는 동안(타이머로 도는 연출) — 검수 모드에서는 잠깐 더 기다려 본다.
                    if (Qa && ++_empty < 400) { Thread.Sleep(20); return; }
                    throw new Exception($"띄운 창이 없다(떠 있는 것: {string.Join(",", WpfHost.Shown.Select(x => x.GetType().Name))})");
                }
                _empty = 0;
                if (!StepFits(w) && Buttons.TryGetValue(w.GetType().Name, out var queue) && queue.Count > 0)
                {
                    string text = queue.Dequeue();
                    Shot(w, $"{w.GetType().Name}-{text.Replace('/', '_')}");
                    if (!Press(w, text)) Key(w, System.Windows.Input.Key.Escape);
                    return;
                }
                if (!StepFits(w))
                {
                    // 대본이 없으면 찍고 ESC(안 받으면 오른쪽 딸깍)로 닫는다 — 안드로이드 「뒤로」와 같다.
                    string kind = w.GetType().Name;
                    int n = Seen.TryGetValue(kind, out int k) ? k + 1 : 1;
                    Seen[kind] = n;
                    if (Qa && kind == "LandDeployDialog" && n == 1)
                    {
                        // 부대배치: 고르는 넉 칸에서 판 자리 여섯으로 끌어다 놓고 「결정」.
                        (int X, int Y)[] pick = [(368, 16), (480, 16), (368, 160), (480, 160)];
                        (int X, int Y)[] slot = [(16, 16), (128, 16), (240, 16), (16, 160), (128, 160), (240, 160)];
                        int[] order = [0, 1, 2, 3, 1, 2];
                        for (int i = 0; i < 6; i++)
                            Drag(w, pick[order[i]].X + 48, pick[order[i]].Y + 48, slot[i].X + 48, slot[i].Y + 48);
                        Shot(w, "LandDeployDialog-placed");
                        _last = w;
                        if (Press(w, "결정")) return;
                    }
                    if (Qa && kind == "SeaCombatDialog" && n > 2 && n < 400)
                    {
                        // 해전: 이동 지시 없이 「Set」으로 턴을 넘겨 적이 다가와 싸우게 둔다.
                        if (n % 25 == 3) { Log.W($"  [해전 턴] {n}"); Shot(w, $"sea-{n:D3}"); }
                        _last = w;
                        Click(w, 705, 578); return;
                    }
                    if (Qa && ReferenceEquals(w, _last) && n > 3)
                    {
                        // 검수 모드: 뒤로로 안 닫히면 그 창의 차림표에서 물러나는 줄(포기 · 항복 · 그만)을 고르고, 그래도 안 되면 억지로 닫는다.
                        if (CdsHelper.Game.Engine.Menu.GameMenuHost.Last is { Shown: { } menu } host
                            && menu.Rows.FirstOrDefault(r => r.Run != null && (r.Text.Contains("포기") || r.Text.Contains("항복") || r.Text.Contains("그만") || r.Text.Contains("퇴각"))) is { } give)
                        { host.Press(give.Text); return; }
                        // 그다음엔 창 안 단추를 돌아가며 눌러 판을 끝까지 둔다(일기토 · 탑 등).
                        var buttons = Find<CdsHelper.Game.UI.Views.GameButton>(w).Where(b => b.E.IsEnabled && !string.IsNullOrEmpty(b.E.Text)).ToList();
                        if (buttons.Count > 0) Played.Add(kind);
                        if (buttons.Count > 0 && n <= 200)
                        {
                            var (b, r) = buttons[(n * 7) % buttons.Count];
                            if (n % 20 == 4 || Environment.GetEnvironmentVariable("DBG") != null) Log.W($"  [두기] {kind} {n}: {b.Text}");
                            ClickCenter(w, r); return;
                        }
                        if (Environment.GetEnvironmentVariable("DBG") != null) Log.W($"  [dbg] {kind} n={n} buttons={buttons.Count}");
                        if (n > (Played.Contains(kind) ? 200 : 8)) { Stuck.Add(kind); Log.W($"  [억지로 닫음] {kind}"); Shot(w, $"stuck-{kind}"); w.Close(); Seen.Remove(kind); Played.Remove(kind); return; }
                    }
                    else if (n > 6 && ReferenceEquals(w, _last)) { Shot(w, "stuck"); throw new Exception($"창 {kind} 이 안 닫힌다"); }
                    _last = w;
                    if (n <= 2 || !Qa) Shot(w, $"{kind}-{n}");
                    WpfHost.LayerOf(w)!.Cancel();
                    return;
                }
                var (name, run, _) = Steps.Dequeue();
                Log.W($"  [손] {name}");
                run(w);
            };
        }
        public static void Add(string name, Action<Window> run, string? forWindow = null) => Steps.Enqueue((name, run, forWindow));
        public static void Shot(Window w, string name)
        {
            if (WpfHost.LayerOf(w) is not { } layer) return;
            layer.RenderScale = 1;
            WpfHost.Touch(); WpfHost.LayoutNow();
            if (layer.Front is { } f) { Png.Save(f, Path.Combine(Dir, name + ".png")); Log.W($"  [찍음] {name}.png {f.Width}x{f.Height} 자리 {w.ScreenLeft},{w.ScreenTop}"); }
        }
        public static void Click(Window w, double x, double y)
        {
            WpfHost.Pointer(w, WpfHost.Touching.Move, new Point(x, y));
            WpfHost.Pointer(w, WpfHost.Touching.Down, new Point(x, y));
            WpfHost.Pointer(w, WpfHost.Touching.Up, new Point(x, y));
        }
        public static void Drag(Window w, double x0, double y0, double x1, double y1)
        {
            WpfHost.Pointer(w, WpfHost.Touching.Move, new Point(x0, y0));
            WpfHost.Pointer(w, WpfHost.Touching.Down, new Point(x0, y0));
            for (int i = 1; i <= 5; i++) WpfHost.Pointer(w, WpfHost.Touching.Move, new Point(x0 + (x1 - x0) * i / 5, y0 + (y1 - y0) * i / 5));
            WpfHost.Pointer(w, WpfHost.Touching.Up, new Point(x1, y1));
        }
        public static void Key(Window w, System.Windows.Input.Key k) => WpfHost.Key(w, k);

        /// <summary>창 안의 요소들(그 형)과 창 자리 네모.</summary>
        public static List<(T E, Rect R)> Find<T>(Window w) where T : UIElement
        {
            WpfHost.LayoutNow();
            var list = new List<(T, Rect)>();
            void Walk(UIElement e)
            {
                if (!e.ShowsUp) return;
                if (e is T t)
                {
                    var a = e.ToWindow(new Point(0, 0), false); var b = e.ToWindow(new Point(e.RenderSize.Width, e.RenderSize.Height), false);
                    list.Add((t, new Rect(a, b)));
                }
                foreach (var c in e.VisualChildren) Walk(c);
            }
            Walk(w);
            return list;
        }

        /// <summary>찍은 그림에서 그 자리의 색.</summary>
        public static (int R, int G, int B) ColorAt(Window w, double x, double y)
        {
            Shot(w, "_probe");
            var f = WpfHost.LayerOf(w)!.Front!;
            int c = f.Get((int)x, (int)y);
            return ((c >> 16) & 255, (c >> 8) & 255, c & 255);
        }

        public static void ClickCenter(Window w, Rect r) => Click(w, r.X + r.Width / 2, r.Y + r.Height / 2);

        /// <summary>그 글을 단 단추(GameButton · 띠)를 누른다.</summary>
        public static bool Press(Window w, string text)
        {
            foreach (var (b, r) in Find<CdsHelper.Game.UI.Views.GameButton>(w))
                if (b.Text == text) { ClickCenter(w, r); Log.W($"  [단추] {text}"); return true; }
            foreach (var (b, r) in Find<System.Windows.Controls.Button>(w))
                if (b.Content?.ToString() == text) { ClickCenter(w, r); Log.W($"  [단추] {text}"); return true; }
            // 「@Down」처럼 적으면 그 글쇠를 누른다(줄을 골라야 「결정」이 켜지는 창 따위).
            // 「@80/60」은 창 안 그 자리를 손가락으로 누른다(줄을 눌러 고르는 창).
            if (text.StartsWith("@") && text.Split('/') is [var xs, var ys] && double.TryParse(xs[1..], out double px) && double.TryParse(ys, out double py)) { Click(w, px, py); Log.W($"  [누름] {px},{py}"); return true; }
            if (text.StartsWith("@") && Enum.TryParse<System.Windows.Input.Key>(text[1..], out var key)) { Key(w, key); Log.W($"  [글쇠] {key}"); return true; }
            Log.W($"  [단추 없음] {text}: " + string.Join(",", Find<CdsHelper.Game.UI.Views.GameButton>(w).Select(b => b.E.Text)));
            return false;
        }
    }
}
