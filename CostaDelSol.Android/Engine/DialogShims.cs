using Android.App;
using Android.Widget;
using System.Windows;
using CdsHelper.Game.Local.Helpers;
using CostaDelSol.Droid.Game;
using CostaDelSol.Droid.Input;
using CostaDelSol.Droid.Ui;

// PC 판 대화창들의 안드로이드 판 — 이름 · 인자 · 돌려주는 값이 PC 판과 같다. 그래서 PC 판 시설 차림표
// (HarborMenu · ShipyardMenu · TavernMenu · HomeMenu · PatronMenu …)를 고치지 않고 그대로 끌어 쓴다.
// 창 모양 값(크기 · 색 · 여백)은 PC 판 파일에 적힌 원본 값 그대로이고, 그리기는 ChoicePanel · GameWindows 가 한다.
// 모두 게임 줄(GameThread)에서 불려 창이 닫힐 때까지 기다린다 — PC 판 ShowDialog 와 같다.

namespace CdsHelper.Game.UI.Views
{
    /// <summary>물음창 · 알림창(<c>ConfirmDialog</c>) — 원본 물음창 종류 0 · 1.</summary>
    public static class ConfirmDialog
    {
        public static bool Ask(Window owner, string text, string? title = null,
                               uint[]? face = null, Action<Window>? place = null) =>
            GameThread.Show<bool>(reply => ChoicePanel.MakeAsk(text, title, GameThread.Face(face), reply));

        public static void Tell(Window owner, string text, string? title = null,
                                uint[]? face = null, double indent = 0, Window? under = null,
                                Action<Window>? place = null) =>
            GameThread.Show<bool>(reply => ChoicePanel.MakeTell(text, title, GameThread.Face(face), () => reply(true),
                                                                left: indent > 0));
    }

    /// <summary>얼굴과 함께 하는 말(<c>TalkDialog</c>).</summary>
    public static class TalkDialog
    {
        public static void Say(Window owner, uint[]? face, string speaker, string text) =>
            ConfirmDialog.Tell(owner, text, speaker.Length > 0 ? speaker : null, face);

        /// <summary>대사 창을 내고, 확인하면 고를 줄을 낸다. 물러나면 마지막 줄이다.</summary>
        public static int Ask(Window owner, uint[]? face, string speaker, string text, params string[] choices)
        {
            if (text.Length > 0) Say(owner, face, speaker, text);
            if (choices.Length == 0) return -1;
            int picked = ChoiceDialog.Ask(owner, "", choices[..^1], choices[^1]);
            return picked >= 0 ? picked : choices.Length - 1;
        }
    }

    public static class GameDialog
    {
        public static void Show(Window owner, string text, string? title = null) =>
            ConfirmDialog.Tell(owner, text, title);
    }

    public static class NoticeDialog
    {
        public static void Show(Window owner, string text, string? title = null) =>
            ConfirmDialog.Tell(owner, text, title);

        /// <summary>게임 설명 — 62 칸에서 끊고 왼쪽을 세 칸 들인다(<c>NoticeDialog.Explain</c>).</summary>
        public static void Explain(Window owner, string text, string title = "게임 설명") =>
            ConfirmDialog.Tell(owner, Wrap(text, ExplainWidth), title, null, ExplainCells * 8);

        private const int ExplainCells = 3;
        private const int ExplainWidth = 62;

        private static string Wrap(string text, int cells)
        {
            var made = new System.Text.StringBuilder(text.Length + 16);
            foreach (string line in text.Split(Environment.NewLine))
            {
                int width = 0;
                foreach (char c in line)
                {
                    int w = c > 0x7F ? 2 : 1;
                    if (width + w > cells) { made.Append('\n'); width = 0; }
                    made.Append(c);
                    width += w;
                }
                made.Append('\n');
            }
            return made.ToString().TrimEnd();
        }
    }

    /// <summary>고르기 창(<c>ChoiceDialog</c>) — 원본 차림표 모양.</summary>
    internal static class ChoiceDialog
    {
        /// <summary>줄들 밑에 나가기 줄(<paramref name="cancel"/>)이 붙는다. 나가거나 물리면 -1.</summary>
        public static int Ask(Window owner, string title, IReadOnlyList<string> rows,
                              string cancel = "취소", int dim = -1)
        {
            var all = rows.Append(cancel).ToList();
            var dims = all.Select((_, i) => i == dim).ToArray();
            int cancelAt = all.Count - 1;
            return GameThread.Show<int>(reply => ChoicePanel.MakeMenu(title, all, i => reply(i == cancelAt ? -1 : i),
                                                                       cancelAt, dim: dims, plain: true));
        }

        /// <summary>나가기 줄 없이 줄이 모두 같은 단추. 물리면 -1.</summary>
        public static int Pick(Window owner, string title, IReadOnlyList<string> rows) =>
            GameThread.Show<int>(reply => ChoicePanel.MakeMenu(title, rows, reply, ChoicePanel.CloseOnCancel,
                styles: [.. rows.Select(_ => (BandStyle?)BandStyle.Button)], plain: true));

        /// <summary>줄마다 켜고 끌 수 있는 창. 꺼진 줄도 자리를 지킨다. 물리면 -1.</summary>
        public static int Pick(Window owner, string title, IReadOnlyList<(string Text, bool On)> rows,
                               Action<Window>? place = null, bool exitRow = true) =>
            GameThread.Show<int>(reply => ChoicePanel.MakeMenu(title, [.. rows.Select(r => r.Text)], reply,
                ChoicePanel.CloseOnCancel, dim: [.. rows.Select(r => !r.On)],
                styles: exitRow ? null : [.. rows.Select(_ => (BandStyle?)BandStyle.Button)], plain: true));
    }

    /// <summary>수량 입력 창(<c>CountDialog</c>).</summary>
    public static class CountDialog
    {
        public readonly record struct Gauge(string Name, int Value, string? Unit = null);

        /// <summary>수를 고르게 한다. 고른 수를 내고, 중단하거나 0 이면 0 이다.</summary>
        public static int Ask(Window owner, string caption, string label, string unit,
                              int max, int step = 1, bool full = false, params Gauge[] lines)
        {
            if (max <= 0) return 0;
            return Open(caption, label, unit, max, step, full, lines, 0, false) ?? 0;
        }

        /// <summary>지금 값을 고쳐 적게 한다 — 0 도 된다. 중단하면 null.</summary>
        public static int? Set(Window owner, string caption, string label, string unit,
                               int start, int max, params Gauge[] lines) =>
            Open(caption, label, unit, Math.Max(0, max), 1, false, lines, Math.Clamp(start, 0, Math.Max(0, max)), true);

        private static int? Open(string caption, string label, string unit, int max, int step, bool full,
                                 Gauge[] lines, int start, bool zeroOk)
        {
            var gauges = lines.Select(g => (g.Name, $"{g.Value}{g.Unit ?? unit}")).ToArray();
            return GameThread.Wait<int?>(reply =>
            {
                ChoicePanel.Layer layer = null!;
                var window = new CountWindow(caption, label, unit, max, step, full, gauges, start, zeroOk,
                    done => { GameThread.Panel.Remove(layer); reply(done); },
                    (at, typed) => NumberPadDialog.OpenOnUi(at, 0, max, "", typed));
                layer = new ChoicePanel.Layer { Kind = ChoicePanel.Kind.Custom, Custom = window };
                GameThread.Panel.Push(layer);
            });
        }
    }

    /// <summary>계산기 창(<c>NumberPadDialog</c>).</summary>
    public static class NumberPadDialog
    {
        public static int? Ask(Window owner, int start, int min, int max, string prompt = "") =>
            GameThread.Wait<int?>(reply => OpenOnUi(start, min, max, prompt, reply));

        /// <summary>화면 줄에서 곧장 띄운다(수량 입력 창의 계산기 단추).</summary>
        internal static void OpenOnUi(int start, int min, int max, string prompt, Action<int?> reply)
        {
            ChoicePanel.Layer layer = null!;
            var window = new NumberPadWindow(start, min, max, prompt, done => { GameThread.Panel.Remove(layer); reply(done); });
            layer = new ChoicePanel.Layer { Kind = ChoicePanel.Kind.Custom, Custom = window };
            GameThread.Panel.Push(layer);
        }
    }

    /// <summary>목록 창(<c>HintListDialog</c>) — 취득 힌트 일람 · 스폰서 일람 · 배 고르기 ….</summary>
    public static class HintListDialog
    {
        public static void Show(Window owner, IReadOnlyList<string> hints, string caption = "취득 힌트 일람",
                                string whenEmpty = "설득 가능한 힌트가 없습니다")
        {
            if (hints.Count == 0) { NoticeDialog.Show(owner, whenEmpty); return; }
            Open(caption, hints, false, false, "", null, null, null, null);
        }

        public static int Pick(Window owner, IReadOnlyList<string> items, string caption = "취득 힌트 일람",
                               string whenEmpty = "설득 가능한 힌트가 없습니다", string header = "",
                               IReadOnlyList<uint[]?>? faces = null, IReadOnlyList<string>? subtitles = null,
                               IReadOnlyList<bool>? marks = null)
        {
            if (items.Count == 0) { NoticeDialog.Show(owner, whenEmpty); return -1; }
            return Open(caption, items, true, false, header, faces, subtitles, marks, null).Picked;
        }

        public static IReadOnlyList<int> PickMany(Window owner, IReadOnlyList<string> items, string caption,
                                                  IReadOnlyCollection<int>? preset = null)
        {
            if (items.Count == 0) return [];
            return Open(caption, items, true, true, "", null, null, null, preset).Many;
        }

        private static (int Picked, IReadOnlyList<int> Many) Open(string caption, IReadOnlyList<string> items, bool choosing,
            bool multi, string header, IReadOnlyList<uint[]?>? faces, IReadOnlyList<string>? subs,
            IReadOnlyList<bool>? marks, IReadOnlyCollection<int>? preset)
        {
            // 초상화는 줄에서 40x48 로 줄여 붙는다 — 그림은 화면 줄에서 짓는다.
            return GameThread.Wait<(int, IReadOnlyList<int>)>(reply =>
            {
                var bitmaps = faces?.Select(f => GameThread.Face(f)).ToArray();
                ChoicePanel.Layer layer = null!;
                var window = new ListWindow(caption, items, choosing, multi, header, bitmaps, subs, marks, preset,
                    (picked, many) => { GameThread.Panel.Remove(layer); reply((picked, many)); });
                layer = new ChoicePanel.Layer { Kind = ChoicePanel.Kind.Custom, Custom = window };
                GameThread.Panel.Push(layer);
            });
        }
    }
}

namespace CdsHelper.Game.Engine.Menu
{
    /// <summary>메뉴 줄 하나(<c>GameMenuRow</c>). 손이 없으면 흐린 줄이다.</summary>
    internal sealed record GameMenuRow(string Text, Action? Run = null, BandStyle? Style = null, bool Dim = false);

    /// <summary>게임 명령 창 하나(<c>GameMenu</c>) — 안드로이드에서는 줄 목록만 들고 그리기는 <see cref="GameMenuHost"/> 가 한다.</summary>
    internal sealed class GameMenu
    {
        public string Title { get; }
        public IReadOnlyList<GameMenuRow> Rows { get; }
        public Action? OnClose { get; }

        public GameMenu(string title, IReadOnlyList<GameMenuRow> rows, Action? onClose = null)
        {
            Title = title;
            Rows = rows;
            OnClose = onClose;
        }

        public GameMenu(string title, Action? onClose, params (string Text, Action? Run)[] rows)
            : this(title, [.. rows.Select(r => new GameMenuRow(r.Text, r.Run))], onClose) { }

        public GameMenu(params (string Text, Action? Run)[] rows) : this("", null, rows) { }
    }

    /// <summary>
    /// 명령 창 하나를 띄우고 겹치고 되돌아가는 것(<c>GameMenuHost</c>). 줄을 누르면 그 일이 게임 줄에서 돈다 —
    /// 창은 그대로 서 있고, 그 일이 띄우는 물음창이 위에 겹친다(원본 모양).
    /// </summary>
    internal sealed class GameMenuHost(Window owner)
    {
        private readonly List<Func<GameMenu>> _stack = [];
        private ChoicePanel.Layer? _layer;
        private GameMenu? _shown;
        private readonly HostWindow _window = new();

        public bool IsOpen => _layer != null;

        /// <summary>떠 있으면 그 창 — 물음창이 그 위에 서고, 접으면(Visibility) 그림에서 빠진다.</summary>
        public Window? Window => _layer != null ? _window : null;

        /// <summary>명령 창 하나의 「창」 — 접고 펴면 판에서 숨긴다.</summary>
        private sealed class HostWindow : Window
        {
            public ChoicePanel.Layer? Layer;

            protected override void OnVisibilityChanged(Visibility value)
            {
                if (Layer is not { } layer) return;
                bool hidden = value != Visibility.Visible;
                Ui(() => { layer.Hidden = hidden; GameThread.Panel.Invalidate(); });
            }
        }

        public event Action? Closed;

        public void Open(Func<GameMenu> build, Point? at = null)
        {
            _stack.Clear();
            _stack.Add(build);
            Show();
        }

        public void Push(Func<GameMenu> build)
        {
            if (_layer == null) { Open(build); return; }
            _stack.Add(build);
            Show();
        }

        public void Pop()
        {
            if (_stack.Count <= 1) { Close(); return; }
            _stack.RemoveAt(_stack.Count - 1);
            Show();
        }

        public void Refresh()
        {
            if (_layer != null) Show();
        }

        public void Focus() { }

        public void Close()
        {
            if (_layer == null) return;
            var layer = _layer;
            _layer = null;
            _window.Layer = null;
            _window.Visibility = Visibility.Visible;
            _stack.Clear();
            Ui(() => GameThread.Panel.Remove(layer));
            Closed?.Invoke();
        }

        /// <summary>그 자리의 줄을 고른 것처럼(도시 그림이 쓴다).</summary>
        private void Run(int index)
        {
            if (_shown is not { } menu || index < 0 || index >= menu.Rows.Count) return;
            if (menu.Rows[index] is not { Run: { } run, Dim: false }) return;
            GameThread.Post(run);
        }

        private void Show()
        {
            var menu = _stack[^1]();
            _shown = menu;
            var rows = menu.Rows;
            var layer = ChoicePanel.MakeMenu(menu.Title, [.. rows.Select(r => r.Text)], i =>
                {
                    // 창이 닫히는 길(ESC · 오른쪽 클릭)이면 원본처럼 창만 닫는다.
                    if (i < 0) { GameThread.Post(Close); return; }
                    if (GameThread.Busy) return;   // 일이 도는 동안 또 눌리지 않게
                    Run(i);
                }, ChoicePanel.CloseOnCancel,
                dim: [.. rows.Select(r => r.Run == null || r.Dim)],
                styles: [.. rows.Select(r => r.Style)]);
            layer.Sticky = true;
            layer.Hidden = _window.Visibility != Visibility.Visible;
            var old = _layer;
            _layer = layer;
            _window.Layer = layer;
            Ui(() =>
            {
                if (old != null && GameThread.Panel.Contains(old)) GameThread.Panel.Replace(old, layer);
                else GameThread.Panel.Push(layer);
            });
        }

        private static void Ui(Action work)
        {
            if (GameThread.IsCurrent) GameThread.OnUi(work);
            else work();
        }
    }
}
