// 시험기용 가짜 — 안드로이드 화면 대신 콘솔에 찍고, 묻는 것에는 정해 둔 답(Script)으로 답한다.
using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace Sim
{
    public static class Log
    {
        public static readonly List<string> Lines = [];
        public static void W(string s) { Lines.Add(s); Console.WriteLine(s); }
    }

    /// <summary>답 대본 — 물음 · 고르기에 차례로 쓴다. 비면 기본값(YES · 첫 줄 · 최대).</summary>
    public static class Script
    {
        public static readonly Queue<string> Answers = new();
        public static string? Next() => Answers.Count > 0 ? Answers.Dequeue() : null;
        public static int Steps;
        public static void Tick() { if (++Steps > 20000) throw new Exception("대본이 끝나지 않는다(2만 걸음)"); }
    }
}

namespace CostaDelSol.Droid.Game
{
    using Sim;
    public static class GameThread
    {
        public static bool Busy => false;
        public static bool IsCurrent => true;
        public static Android.Content.Context? Activity => null;
        public static readonly Queue<Action> Work = new();
        public static void Post(Action a) => Work.Enqueue(a);
        public static void OnUi(Action a) => a();
        public static Android.Graphics.Bitmap? Face(uint[]? bgra, int w = 80, int h = 96) =>
            bgra == null ? null : Android.Graphics.Bitmap.CreateBitmap(bgra.Select(v => (int)v).ToArray(), w, h, Android.Graphics.Bitmap.Config.Argb8888);
        public static CostaDelSol.Droid.Input.ChoicePanel Panel { get; } = new();
        public static void Drain() { while (Work.Count > 0) { Script.Tick(); Work.Dequeue()(); } }

        /// <summary>모달 — 줄에 선 일을 돌리고, 일이 없으면 시험 대본(<see cref="Sim.Wpf.Idle"/>)이 손을 넣는다.</summary>
        public static void Pump(Func<bool> keepGoing)
        {
            var quiet = System.Diagnostics.Stopwatch.StartNew();
            while (keepGoing())
            {
                if (Work.Count > 0) { Script.Tick(); Work.Dequeue()(); quiet.Restart(); }
                else
                {
                    System.Windows.WpfHost.Tick();
                    bool ticking = System.Windows.WpfHost.Timers.Any(t => t.IsEnabled);
                    if (ticking && quiet.ElapsedMilliseconds < Sim.Wpf.QuietMs) { Thread.Sleep(2); continue; }
                    Script.Tick();
                    if (Sim.Wpf.Idle is { } act) act(); else throw new Exception("모달이 끝나지 않는다(대본 없음)");
                    quiet.Restart();
                }
                System.Windows.WpfHost.Tick();
            }
        }
    }
    internal static class NotYet { public static void Tell(string what) => Log.W($"[아직] {what}"); }
}

namespace CostaDelSol.Droid.Input
{
    public sealed class ChoicePanel
    {
        public enum Kind { Menu, Ask, Tell, Custom }
        public interface IGameWindow
        {
            float Width { get; }
            float Height { get; }
            void Draw(Android.Content.Context context, Android.Graphics.Canvas canvas, float x, float y, float scale, bool top);
            void Key(GameKey key);
            void Touch(Android.Views.MotionEventActions action, float x, float y);
            void Cancel();
            float? Left => null;
            float? Top => null;
            void Hover(float x, float y) { }
            bool RightClick(float x, float y) => false;
        }
        public sealed class Layer { public Kind Kind; public IGameWindow? Custom; public bool Sticky, Hidden; }
        public readonly List<Layer> Layers = [];
        public bool IsOpen => Layers.Any(l => !l.Hidden);
        public void Push(Layer l) => Layers.Add(l);
        public void Remove(Layer l) => Layers.Remove(l);
        public void Invalidate() { }
        public Android.Graphics.RectF BoardToView(float x, float y, float w, float h) => new(x, y, x + w, y + h);
    }
}
namespace CostaDelSol.Droid.Ui
{
    public sealed class VideoLayer
    {
        public static VideoLayer? Current { get; set; } = new();
        public void Play(string path, Android.Graphics.RectF box, double volume, Action ended, Action failed) { Sim.Log.W($"[영상] {System.IO.Path.GetFileName(path)} {box.Width()}x{box.Height()}"); ended(); }
        public void Stop() { }
    }
}

namespace Sim
{
    /// <summary>WPF 창 시험 손 — 모달이 쉴 때 부른다.</summary>
    public static class Wpf
    {
        public static Action? Idle;
        public static long Clock;
        public static int QuietMs = 400;
    }
}

namespace CostaDelSol.Droid.Town
{
    public sealed class CityStage
    {
        public const int PicW = 400, PicH = 320;
        public sealed record Spot(Android.Graphics.RectF Area, string Tag, Action Left, Action? Right, int Code);
        public sealed record Guest(Android.Graphics.Bitmap Art, string Name, Action? Click);
        public readonly List<Spot> Spots = [];
        public Action? Anywhere { get; set; }
        public Action? Empty { get; set; }
        public bool Active { get; private set; }
        public void Begin(uint[] p) { Active = true; Spots.Clear(); Sim.Log.W("[도시 그림]"); }
        public void End() { Active = false; Sim.Log.W("[도시 그림 걷음]"); }
        public void AddSpot(Spot s) => Spots.Add(s);
        public void ShowTag(Android.Graphics.RectF a, string t) { }
        public void HideTag() { }
        public void SetEffect(Android.Graphics.Bitmap? b) { }
        public void SetShade(bool on) { }
        public void SetPhoto(uint[]? p, IReadOnlyList<Guest> g) { if (p != null) Sim.Log.W($"[사진] 손님 {g.Count}"); }
    }
}

namespace Android.App { internal static class A_ { } }
namespace Android.Widget { internal static class W_ { } }
namespace System.Windows.Media
{
    public sealed class MediaPlayer
    {
        public event EventHandler? MediaEnded;
        public double Volume { get; set; }
        public void Open(Uri u) { }
        public void Play() { }
        public void Stop() { }
        public void Close() { }
        public void Pause() { }
        public TimeSpan Position { get; set; }
    }
}
namespace System.Media
{
    public sealed class SoundPlayer : IDisposable
    {
        public SoundPlayer() { }
        public SoundPlayer(Stream s) { }
        public SoundPlayer(string s) { }
        public Stream? Stream { get; set; }
        public void Play() { }
        public void PlayLooping() { }
        public void Stop() { }
        public void Load() { }
        public void Dispose() { }
    }
}
namespace CdsHelper.Game.Local.Helpers
{
    internal static class PngShim
    {
        public static uint[]? Decode(string p, int w, int h)
        {
            try
            {
                using var f = File.OpenRead(p);
                var px = Sim.PngRead.Decode(f, out int pw, out int ph);
                if (px == null || (w > 0 && (pw != w || ph != h))) return null;
                return px.Select(v => (uint)v).ToArray();
            }
            catch { return null; }
        }

        public static (uint[] Pixels, int Width, int Height)? DecodeAny(string p, bool premultiply = false)
        {
            try
            {
                using var f = File.OpenRead(p);
                var px = Sim.PngRead.Decode(f, out int pw, out int ph);
                if (px == null) return null;
                return (px.Select(v =>
                {
                    uint c = (uint)v, a = c >> 24;
                    if (!premultiply || a == 255) return c;
                    uint M(int sh) => (((c >> sh) & 0xFF) * a / 255) << sh;
                    return (a << 24) | M(16) | M(8) | M(0);
                }).ToArray(), pw, ph);
            }
            catch { return null; }
        }
    }
}

namespace CdsHelper.Game.Engine.Menu
{
    using Sim;
    internal sealed record GameMenuRow(string Text, Action? Run = null, BandStyle? Style = null, bool Dim = false, bool Grey = false);
    internal interface IMenuRing { bool Active { get; } bool Enter(int way); bool Move(int way); void Run(); void Clear(); }
    internal sealed class GameMenu
    {
        public string Title { get; }
        public IReadOnlyList<GameMenuRow> Rows { get; }
        public Action? OnClose { get; }
        public Func<IMenuRing?>? Ring { get; set; }
        public GameMenu(string title, IReadOnlyList<GameMenuRow> rows, Action? onClose = null) { Title = title; Rows = rows; OnClose = onClose; }
        public GameMenu(string title, Action? onClose, params (string Text, Action? Run)[] rows) : this(title, [.. rows.Select(r => new GameMenuRow(r.Text, r.Run))], onClose) { }
        public GameMenu(params (string Text, Action? Run)[] rows) : this("", null, rows) { }
    }
    internal sealed class GameMenuHost(Window owner)
    {
        /// <summary>마지막에 띄운 차림표 — 시험기가 줄을 고른다.</summary>
        public static GameMenuHost? Last;
        private readonly List<Func<GameMenu>> _stack = [];
        public GameMenu? Shown;
        private readonly Window _w = new();
        public bool IsOpen => Shown != null;
        public Window? Window => Shown != null ? _w : null;
        public event Action? Closed;
        public void Open(Func<GameMenu> build, Point? at = null) { _stack.Clear(); _stack.Add(build); Show(); }
        public void Push(Func<GameMenu> build) { if (Shown == null) { Open(build); return; } _stack.Add(build); Show(); }
        public void Pop() { if (_stack.Count <= 1) { Close(); return; } _stack.RemoveAt(_stack.Count - 1); Show(); }
        public void Refresh() { if (Shown != null) Show(); }
        public void Focus() { }
        public void Close() { if (Shown == null) return; Shown = null; _stack.Clear(); Log.W("[창 닫음]"); Closed?.Invoke(); }
        private void Show()
        {
            Shown = _stack[^1]();
            Last = this;
            Log.W($"[차림표] {Shown.Title}: " + string.Join(" | ", Shown.Rows.Select(r => (r.Run == null || r.Dim ? "~" : "") + r.Text)));
        }
        /// <summary>그 글의 줄을 누른다.</summary>
        public bool Press(string text)
        {
            var row = Shown?.Rows.FirstOrDefault(r => r.Text == text);
            if (row?.Run is not { } run || row.Dim) { Log.W($"  (눌리지 않음: {text})"); return false; }
            Log.W($"> {text}");
            run();
            return true;
        }
    }
}

namespace CdsHelper.Game.UI.Views
{
    using Sim;
    using CostaDelSol.Droid.Game;

    public static class ConfirmDialog
    {
        public static bool Ask(Window owner, string text, string? title = null, uint[]? face = null, Action<Window>? place = null)
        {
            if (CdsHelper.Game.Engine.Town.KeeperTongue.Hear(text, face) is { } got)
            {
                var (heard, relayFace, relay) = got;
                if (relay == null) text = heard; else { Tell(owner, heard, title); text = relay; face = relayFace; }
            }
            string? a = Script.Next(); bool yes = a == null || a is "Y" or "YES" or "예" || a == "0";
            Log.W($"[물음{(title != null ? " " + title : "")}] {text} → {(yes ? "YES" : "NO")}"); Script.Tick(); return yes;
        }
        public static void Tell(Window owner, string text, string? title = null, uint[]? face = null, double indent = 0, Window? under = null, Action<Window>? place = null)
        {
            string? relay = null; uint[]? relayFace = null;
            if (CdsHelper.Game.Engine.Town.KeeperTongue.Hear(text, face) is { } got) (text, relayFace, relay) = got;
            Log.W($"[말{(title != null ? " " + title : "")}{(face != null ? " 얼굴" : "")}] {text}"); Script.Tick();
            if (relay != null) Tell(owner, relay, title, relayFace);
        }
        public static Window Hold(Window owner, string text, string? title = null) { Log.W($"[띄워 둔 글] {text}"); return new Window(); }
    }
    public static class TalkDialog
    {
        public static void Say(Window owner, uint[]? face, string speaker, string text) => ConfirmDialog.Tell(owner, text, speaker.Length > 0 ? speaker : null, face);
        public static int Ask(Window owner, uint[]? face, string speaker, string text, params string[] choices)
        {
            if (text.Length > 0) Say(owner, face, speaker, text);
            if (choices.Length == 0) return -1;
            int p = ChoiceDialog.Ask(owner, "", choices[..^1], choices[^1]);
            return p >= 0 ? p : choices.Length - 1;
        }
    }
    public static class GameDialog { public static void Show(Window o, string t, string? title = null) => ConfirmDialog.Tell(o, t, title); }
    public static class NoticeDialog
    {
        public static void Show(Window o, string t, string? title = null) => ConfirmDialog.Tell(o, t, title);
        public static void Explain(Window o, string t, string title = "게임 설명") => ConfirmDialog.Tell(o, t, title);
        public static void Notes(Window o, string t, string title) => ConfirmDialog.Tell(o, t, title);
    }
    internal sealed class LoadingDialog
    {
        public static LoadingDialog Open(Window o, string text) { Log.W($"[읽는 중] {text}"); return new(); }
        public void Close() { }
    }
    internal sealed class ChoiceDialog : Window
    {
        static string? _last; static int _rep;
        static int Pick(string title, IReadOnlyList<string> rows, bool[]? dim = null)
        {
            string? a = Script.Next(); int at = 0;
            if (a != null) { at = int.TryParse(a, out int n) ? n : rows.ToList().IndexOf(a); }
            else
            {
                string key = title + "|" + string.Join("|", rows);
                _rep = key == _last ? _rep + 1 : 0; _last = key;
                if (Sim.Drive.Qa && _rep >= 3) at = _rep / 3 % rows.Count;   // 같은 물음이 되풀이되면 다른 줄로
                while (dim != null && at < rows.Count && dim[at]) at++;
            }
            Log.W($"[고르기 {title}] {string.Join(" | ", rows)} → {(at >= 0 && at < rows.Count ? rows[at] : "취소")}"); Script.Tick();
            return at;
        }
        public static int Ask(Window owner, string title, IReadOnlyList<string> rows, string? cancel = "취소", int dim = -1, Window? under = null, int scrollRows = 0)
        { int at = Pick(title, cancel != null ? [.. rows, cancel] : rows); return at >= rows.Count ? -1 : at; }
        public static int Pick(Window owner, string title, IReadOnlyList<string> rows) => Pick(title, rows);
        public static int Pick(Window owner, string title, IReadOnlyList<(string Text, bool On)> rows, Action<Window>? place = null, bool exitRow = true)
            => Pick(title, [.. rows.Select(r => r.Text)], [.. rows.Select(r => !r.On)]);
    }
    public static class CountDialog
    {
        public readonly record struct Gauge(string Name, int Value, string? Unit = null);
        public static int NextShare(int now, int max) { if (max <= 0) return 0; int half = max / 2; if (now == max) return half; if (now == half && half > 0) return 0; return max; }
        public static int Ask(Window owner, string caption, string label, string unit, int max, int step = 1, bool full = false, params Gauge[] lines)
        { string? a = Script.Next(); int v = a != null && int.TryParse(a, out int n) ? Math.Min(n, max) : max; Log.W($"[수량 {caption}] 최대 {max} → {v}"); return Math.Max(0, v); }
        public static int? Set(Window owner, string caption, string label, string unit, int start, int max, params Gauge[] lines) => Ask(owner, caption, label, unit, max);
    }
    public static class NumberPadDialog { public static int? Ask(Window o, int start, int min, int max, string prompt = "") => CountDialog.Ask(o, "계산기 " + prompt, "", "", max); }
    public static class HintListDialog
    {
        public static void Show(Window o, IReadOnlyList<string> h, string caption = "취득 힌트 일람", string whenEmpty = "설득 가능한 힌트가 없습니다")
            => Log.W($"[목록 {caption}] " + (h.Count == 0 ? whenEmpty : string.Join(" | ", h)));
        public static int Pick(Window o, IReadOnlyList<string> items, string caption = "취득 힌트 일람", string whenEmpty = "설득 가능한 힌트가 없습니다", string header = "",
                               IReadOnlyList<uint[]?>? faces = null, IReadOnlyList<string>? subtitles = null, IReadOnlyList<bool>? marks = null)
        {
            if (items.Count == 0) { NoticeDialog.Show(o, whenEmpty); return -1; }
            string? want = Environment.GetEnvironmentVariable("PICK"); int at = want != null ? Math.Max(0, items.ToList().FindIndex(x => x.Contains(want))) : 0;
            Log.W($"[목록 {caption}] {string.Join(" | ", items)} → {(at >= 0 && at < items.Count ? items[at] : "중단")}"); return at;
        }
        public static IReadOnlyList<int> PickMany(Window o, IReadOnlyList<string> items, string caption, IReadOnlyCollection<int>? preset = null)
        { int at = Pick(o, items, caption); return at >= 0 ? [at] : []; }
    }
    public static class ShipNameDialog
    {
        public static string? Ask(Window o, string current) { Log.W($"[선명] {current}"); return current; }
        public static string? Ask(Window o, string current, IReadOnlyList<string> names, string heading, int maxLength) { Log.W($"[{heading}] {current}"); return current; }
        public static string Settle(Window o, string current) => Ask(o, current) is { Length: > 0 } n ? n : current;
    }
    public static class TextInputDialog
    {
        public static string? Ask(Window o, string start, int max, string caption = "문자입력") { Log.W($"[글자 {caption}] {start}"); return start.Length > 0 ? start : "테스트"; }
        public static string? AskLong(Window o, string start, string caption) { Log.W($"[긴 글 {caption}] {start.Length}자"); return start; }
    }
}

namespace CdsHelper.Game.UI.Views
{
    using Sim;
    internal static class EventAnimationPopup { public static void Play(Window o, Engine.Game g, int scene, Rect area, double scale, Point? ship, int heading = -1) => Log.W($"[사건 연출 {scene}]"); }
}
namespace CostaDelSol.Droid.Game
{
    public static class GameThreadExt { }
}
