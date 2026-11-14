using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CostaDelSol.Droid.Game;
using CostaDelSol.Droid.Input;
using ABitmap = Android.Graphics.Bitmap;
using ACanvas = Android.Graphics.Canvas;
using AMatrix = Android.Graphics.Matrix;
using APaint = Android.Graphics.Paint;
using ARectF = Android.Graphics.RectF;

namespace System.Windows
{
    internal static class WpfPaints
    {
        [ThreadStatic] private static APaint? _sharp, _smooth;
        internal static APaint Bitmap(bool smooth) =>
            smooth ? _smooth ??= new APaint { FilterBitmap = true, AntiAlias = true }
                   : _sharp ??= new APaint { FilterBitmap = false, AntiAlias = false };
    }

    /// <summary>
    /// 띄운 WPF 창들을 재고(Measure/Arrange) 그리고(그림 한 장) 손을 나눠 주는 곳 — 모두 게임 줄에서 돈다.
    /// </summary>
    internal static class WpfHost
    {
        private static volatile bool _dirty;
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        internal static long Now => Clock.ElapsedMilliseconds;
        internal static readonly List<Window> Shown = [];
        internal static readonly List<DispatcherTimer> Timers = [];
        private static readonly List<Action> Pending = [];
        internal static Point LastPoint;
        internal static event EventHandler? Rendering;
        /// <summary>화면 프레임마다 도는 일이 걸려 있는지(낚시 · 입방체 퍼즐 따위) — 시험기가 그동안 손을 넣지 않고 기다린다.</summary>
        internal static bool RenderingBusy => Rendering != null;

        /// <summary>무엇이 바뀌었다 — 다음 틈에 다시 재고 그린다.</summary>
        internal static void Touch()
        {
            _dirty = true;
            if (TraceTouch && _inLayout && TouchSources.Count < 4000)
                TouchSources.Add(new System.Diagnostics.StackTrace(1, false).ToString());
        }

        /// <summary>진단 — 자리 잡기 도중 「바뀜」을 건 자리를 모은다(시험기 전용).</summary>
        internal static bool TraceTouch;
        internal static readonly List<string> TouchSources = [];
        private static bool _inLayout;

        /// <summary>자리 잡기가 끝난 뒤에 할 일(크기 바뀜 알림 따위).</summary>
        internal static void Later(Action work) { lock (Pending) Pending.Add(work); }

        // ── 창 ──

        internal static void Open(Window w)
        {
            if (w.IsShown)
            {
                if (w.Visibility != Visibility.Visible) w.Visibility = Visibility.Visible;
                return;
            }
            w.IsShown = true;
            w.Visibility = Visibility.Visible;
            var layer = new WpfLayer(w);
            Layers[w] = layer;
            Shown.Add(w);
            w.FireSourceInitialized();
            LayoutNow();
            if (TownTypes.Contains(w.GetType())) lock (TownLayers) TownLayers.Add(layer);   // 도시 그림 위 쪽지
            else if (w.OnMap) lock (MapLayers) MapLayers.Add(layer);   // 지도 위 덧그림 — 창 판에 안 얹어 손을 안 막는다
            else GameThread.OnUi(() => GameThread.Panel.Push(layer.Layer));
            Render(w);
            w.FireContentRendered();
            Touch();
        }

        internal static void Shut(Window w)
        {
            if (!w.IsShown) return;
            w.IsShown = false;
            Shown.Remove(w);
            if (Layers.Remove(w, out var layer))
            {
                lock (MapLayers) MapLayers.Remove(layer);
                lock (TownLayers) TownLayers.Remove(layer);
                GameThread.OnUi(() => GameThread.Panel.Remove(layer.Layer));
            }
            Unload(w);
            if (Input.Mouse.Captured is UIElement c && ReferenceEquals(c.Root(), w)) Input.Mouse.Capture(null);
            Input.Mouse.Hovered.RemoveAll(e => ReferenceEquals(e.Root(), w));
        }

        private static void Unload(UIElement e)
        {
            if (e is FrameworkElement f) f.FireUnloaded();
            if (e is Controls.MediaElement movie) movie.Stop();
            foreach (var c in e.VisualChildren) Unload(c);
        }

        private static readonly Dictionary<Window, WpfLayer> Layers = [];

        /// <summary>지도 위에 겹치는 창(좌표 · 컨디션 · 미니맵 상자) — 지도 덧그림 판이 그린다.</summary>
        internal static readonly List<WpfLayer> MapLayers = [];

        /// <summary>도시 그림 판에 겹치는 창의 형(기능 · 언어 쪽지 · 함대 쪽지) — 도시 그림 판이 그린다.</summary>
        internal static readonly HashSet<Type> TownTypes = [];
        internal static readonly List<WpfLayer> TownLayers = [];

        /// <summary>판 양옆에 남는 자리(판 점) — 16:9 면 그림 밖에 쪽지를 둘 수 있다. 도시 그림 판이 적는다.</summary>
        internal static volatile float SideRoom;

        internal static void DrawTownLayers(ACanvas canvas, float k, float boardLeft)
        {
            WpfLayer[] all;
            lock (TownLayers) all = [.. TownLayers];
            foreach (var l in all) l.DrawAt(canvas, k, 0, boardLeft);
        }

        /// <summary>지도 덧그림 판에 그린다(화면 줄). <paramref name="k"/> 는 판 점 하나의 화소 수, <paramref name="top"/> 은 지도 윗변의 판 y.</summary>
        internal static void DrawMapLayers(ACanvas canvas, float k, float top)
        {
            WpfLayer[] all;
            lock (MapLayers) all = [.. MapLayers];
            foreach (var l in all) l.DrawAt(canvas, k, top);
        }
        internal static WpfLayer? LayerOf(Window w) => Layers.GetValueOrDefault(w);

        // ── 한 바퀴 ──

        private static long _lastFrame;

        /// <summary>게임 줄이 틈날 때마다 부른다 — 시계 · 움직임 · 다시 그리기.</summary>
        internal static void Tick()
        {
            if (!GameThread.IsCurrent) return;
            long now = Now;
            foreach (var t in Timers.ToList())
                if (t.IsEnabled && now >= t.Due) t.Fire();
            Media.Animation.Animator.Step(now);
            if (Shown.Count > 0 && now - _lastFrame >= 15)
            {
                _lastFrame = now;
                Rendering?.Invoke(null, new RenderingEventArgs { RenderingTime = TimeSpan.FromMilliseconds(now) });
            }
            if (_dirty) LayoutNow();
            else if (_renderPending && now - _lastRender >= 16) RenderAll();
        }

        /// <summary>자리는 잡았는데 아직 안 그린 것이 있는지 — 다음 틱(화면 한 장)에 한 번 그린다.</summary>
        private static bool _renderPending;
        private static long _lastRender;

        private static void RenderAll()
        {
            _renderPending = false;
            long began = Now;
            foreach (var w in Shown.ToList()) Render(w);
            _lastRender = Now;
            StatRenders++;
            StatRenderMs += _lastRender - began;
        }

        /// <summary>다음 시계가 울릴 때까지 남은 ms(없으면 16).</summary>
        internal static int Wait()
        {
            if (Timers.Count == 0 && !Media.Animation.Animator.Busy && Rendering == null) return 16;
            long now = Now, next = now + 16;
            foreach (var t in Timers) next = Math.Min(next, t.Due);
            return (int)Math.Clamp(next - now, 1, 16);
        }

        /// <summary>창이 이보다 넓으면 줄바꿈해 맞춘다(판 640 의 1.5배).</summary>
        private const double SoftWidth = 960;
        [ThreadStatic] internal static bool SoftWrap;
        private static readonly List<Window> WrapNext = [];

        /// <summary>진단 — 자리 잡기 · 그리기 횟수와 걸린 시간(ms). 시험기가 렉을 잰다.</summary>
        internal static long StatLayouts, StatRounds, StatMaxedOut, StatLayoutMs, StatRenders, StatRenderMs;

        /// <param name="render">
        /// 거짓이면 자리만 잡고 그리기는 미룬다 — 손가락이 움직일 때마다(1초에 수십 번) 창 전체를 그리면 그림 많은 창
        /// (초상화 도감 따위)이 밀렸다. 미룬 것은 다음 틱에 한 번 그린다.
        /// </param>
        internal static void LayoutNow(bool render = true)
        {
            StatLayouts++;
            long began = Now;
            int rounds = 0;
            _inLayout = true;
            for (int round = 0; round < 4 && (_dirty || round == 0); round++)
            {
                rounds++;
                _dirty = false;
                var events = new List<Action>();
                foreach (var w in Shown.ToList())
                {
                    if (w.Visibility == Visibility.Collapsed) continue;
                    w.BeforeLayout();
                    var size = Fit(w);
                    w.Measure(size);
                    // 글 한 줄이 길어 창이 판의 1.5배를 넘으면, 그 폭에서 글을 줄바꿈해 다시 잰다 — 안 그러면 창 전체가 작게 줄어든다.
                    if (w.SizeToContent is SizeToContent.Width or SizeToContent.WidthAndHeight && w.DesiredSize.Width > SoftWidth)
                    {
                        SoftWrap = true;
                        try { w.Measure(new Size(SoftWidth, size.Height)); }
                        finally { SoftWrap = false; }
                        if (w.DesiredSize.Width > SoftWidth + 1) w.Measure(size);   // 글 말고 다른 것이 넓으면 그대로 둔다
                        else WrapNext.Add(w);
                    }
                    var final = w.SizeToContent == SizeToContent.Manual ? size : w.DesiredSize;
                    if (w.SizeToContent == SizeToContent.Width) final = new Size(w.DesiredSize.Width, size.Height);
                    if (w.SizeToContent == SizeToContent.Height) final = new Size(size.Width, w.DesiredSize.Height);
                    SoftWrap = WrapNext.Remove(w);
                    try { w.Arrange(new Rect(0, 0, final.Width, final.Height)); }
                    finally { SoftWrap = false; }
                    Place(w);
                    Walk(w, new AMatrix(), events);
                }
                lock (Pending) { events.AddRange(Pending); Pending.Clear(); }
                foreach (var e in events) e();
            }
            _inLayout = false;
            StatRounds += rounds;
            if (_dirty) StatMaxedOut++;   // 넷을 돌고도 여전히 바뀜 — 자리 잡기가 스스로 「바뀜」을 다시 거는 것
            StatLayoutMs += Now - began;
            if (render) RenderAll();
            else _renderPending = true;
        }

        private static Size Fit(Window w)
        {
            double width = double.IsNaN(w.Width) || w.SizeToContent is SizeToContent.Width or SizeToContent.WidthAndHeight ? double.PositiveInfinity : w.Width;
            double height = double.IsNaN(w.Height) || w.SizeToContent is SizeToContent.Height or SizeToContent.WidthAndHeight ? double.PositiveInfinity : w.Height;
            if (!double.IsFinite(width) && w.SizeToContent == SizeToContent.Manual) width = 640;
            if (!double.IsFinite(height) && w.SizeToContent == SizeToContent.Manual) height = 480;
            return new Size(width, height);
        }

        private static void Place(Window w)
        {
            double cw = w.ActualWidth, ch = w.ActualHeight;
            if (w.Placed && w.WindowStartupLocation == WindowStartupLocation.Manual)
            {
                w.ScreenLeft = w.Left; w.ScreenTop = w.Top;
                return;
            }
            // 판(640x480)보다 큰 창(해전 800x600 따위)은 줄여 한가운데 — 그리는 쪽(WpfLayer)이 배율을 건다.
            double fit = Math.Min(1, Math.Min(640 / Math.Max(1, cw), 480 / Math.Max(1, ch)));
            w.ScreenLeft = Math.Floor((640 - cw * fit) / 2);
            w.ScreenTop = Math.Floor((480 - ch * fit) / 2);
        }

        /// <summary>창에서 각 요소로 가는 셈을 적고, 처음 자리 잡은 요소 · 크기 바뀐 요소를 알린다.</summary>
        private static void Walk(UIElement e, AMatrix parent, List<Action> events)
        {
            var m = new AMatrix(parent);
            m.PreTranslate((float)e.Offset.X, (float)e.Offset.Y);
            if (e is FrameworkElement { LayoutScale: var ls } && ls != (1, 1)) m.PreScale((float)ls.X, (float)ls.Y);
            if (e.RenderTransform is { } t)
            {
                float ox = (float)(e.RenderTransformOrigin.X * e.RenderSize.Width), oy = (float)(e.RenderTransformOrigin.Y * e.RenderSize.Height);
                m.PreTranslate(ox, oy);
                m.PreConcat(t.Matrix(e.RenderSize.Width, e.RenderSize.Height));
                m.PreTranslate(-ox, -oy);
            }
            e.ToWindowMatrix = m;
            if (e is FrameworkElement f)
            {
                if (!f.IsLoaded) events.Add(f.FireLoaded);
                if (f.SizeMoved(out var args)) events.Add(() => f.FireSizeChanged(args));
            }
            foreach (var c in e.VisualChildren) Walk(c, m, events);
        }

        // ── 그리기 ──

        internal static void Render(Window w)
        {
            if (!Layers.TryGetValue(w, out var layer)) return;
            layer.Hidden = w.Visibility != Visibility.Visible;
            int k = Math.Max(1, layer.RenderScale);
            int bw = Math.Max(1, (int)Math.Ceiling(w.ActualWidth * k)), bh = Math.Max(1, (int)Math.Ceiling(w.ActualHeight * k));
            var back = layer.Back;
            if (back == null || back.Width != bw || back.Height != bh)
            {
                back = ABitmap.CreateBitmap(bw, bh, ABitmap.Config.Argb8888!)!;
            }
            back.EraseColor(0);
            var c = new ACanvas(back);
            c.Scale(k, k);
            DrawTree(c, w, k);
            layer.Swap(back, w.ScreenLeft, w.ScreenTop, w.ActualWidth, w.ActualHeight);
        }

        internal static void DrawTree(ACanvas c, UIElement e, float k)
        {
            if (!e.ShowsUp) return;
            c.Save();
            c.Translate((float)e.Offset.X, (float)e.Offset.Y);
            if (e is FrameworkElement { LayoutScale: var ls } && ls != (1, 1)) c.Scale((float)ls.X, (float)ls.Y);
            if (e.RenderTransform is { } t)
            {
                float ox = (float)(e.RenderTransformOrigin.X * e.RenderSize.Width), oy = (float)(e.RenderTransformOrigin.Y * e.RenderSize.Height);
                c.Translate(ox, oy);
                c.Concat(t.Matrix(e.RenderSize.Width, e.RenderSize.Height));
                c.Translate(-ox, -oy);
            }
            // 화면(잘린 칸) 밖에 통째로 있는 줄은 안 그린다 — 긴 목록(모드 창 따위)에서 스크롤로 가려진 줄까지 그리던 것.
            // 칸 밖으로 넘쳐 그리는 요소(Canvas, 크기 없는 것)는 그대로 그린다.
            if (Culls(e) && c.QuickReject(new ARectF(0, 0, (float)e.RenderSize.Width, (float)e.RenderSize.Height), ACanvas.EdgeType.Bw!))
            {
                c.Restore();
                return;
            }
            if (e.ClipToBounds) c.ClipRect(0, 0, (float)e.RenderSize.Width, (float)e.RenderSize.Height);
            if (e is FrameworkElement { LayoutClip: { } lc } && e.VisualParent is not Canvas)
                c.ClipRect((float)lc.X, (float)lc.Y, (float)lc.Right, (float)lc.Bottom);
            if (e.Clip is { } clip) c.ClipPath(clip.ToPath());
            bool layer = e.Opacity < 0.999;
            if (layer) c.SaveLayerAlpha(null, (int)(Math.Clamp(e.Opacity, 0, 1) * 255));
            if (e.Opacity > 0)
            {
                e.Paint(c, k);
                foreach (var child in e.VisualChildren) DrawTree(c, child, k);
            }
            if (layer) c.Restore();
            c.Restore();
        }

        /// <summary>건너뛰어도 되는 요소인지 — 줄을 쌓는 판(StackPanel · Grid)의 아이이고, 크기가 있고, 제 칸 밖에 그리지 않는 것.</summary>
        private static bool Culls(UIElement e) =>
            e.VisualParent is Controls.StackPanel or Controls.Grid or Controls.WrapPanel
            && e is not Controls.Canvas && e.RenderTransform == null
            && e.RenderSize.Width > 0 && e.RenderSize.Height > 0;

        // ── 손 ──

        internal static UIElement? HitTest(UIElement root, Point windowPoint)
        {
            if (!root.ShowsUp || !root.IsHitTestVisible) return null;
            if (root.ClipToBounds && !Inside(root, windowPoint)) return null;
            foreach (var c in root.VisualChildren.Reverse())
                if (HitTest(c, windowPoint) is { } hit) return hit;
            if (Inside(root, windowPoint))
            {
                var local = root.FromWindow(windowPoint, false);
                if (root.HitSelf(local)) return root;
            }
            return null;
        }

        private static bool Inside(UIElement e, Point windowPoint)
        {
            var p = e.FromWindow(windowPoint, false);
            return p.X >= 0 && p.Y >= 0 && p.X < e.RenderSize.Width && p.Y < e.RenderSize.Height;
        }

        private static List<UIElement> Route(UIElement? from)
        {
            var list = new List<UIElement>();
            for (var e = from; e != null; e = e.VisualParent) list.Add(e);
            return list;
        }

        private static long _lastDown;
        private static Point _lastDownAt;

        internal enum Touching { Down, Move, Up, RightDown, RightUp, Wheel }

        internal static void Pointer(Window w, Touching kind, Point p, int wheel = 0)
        {
            if (!w.IsShown) return;
            if (_dirty) LayoutNow(render: false);   // 손 · 글쇠를 받을 때는 자리만 잡고, 그리기는 다음 틱에 한 번
            LastPoint = p;
            var target = Input.Mouse.Captured as UIElement ?? HitTest(w, p) ?? w;
            UpdateHover(w, p);
            var route = Route(target);
            if (route.Count == 0 || !ReferenceEquals(route[^1], w)) route = Route(w);   // 딴 창에 잡힌 손이면 이 창으로

            switch (kind)
            {
                case Touching.Down:
                {
                    Input.Mouse.LeftButton = MouseButtonState.Pressed;
                    long now = Now;
                    int clicks = now - _lastDown < 500 && Math.Abs(p.X - _lastDownAt.X) < 6 && Math.Abs(p.Y - _lastDownAt.Y) < 6 ? 2 : 1;
                    _lastDown = now; _lastDownAt = p;
                    var e = Args(w, p, MouseButton.Left, MouseButtonState.Pressed, clicks, target);
                    Tunnel(route, e, "PreviewMouseDown", "PreviewMouseLeftButtonDown");
                    Bubble(route, e, "MouseLeftButtonDown", "MouseDown");
                    break;
                }
                case Touching.Up:
                {
                    Input.Mouse.LeftButton = MouseButtonState.Released;
                    var e = Args(w, p, MouseButton.Left, MouseButtonState.Released, 1, target);
                    Tunnel(route, e, "PreviewMouseUp", "PreviewMouseLeftButtonUp");
                    Bubble(route, e, "MouseLeftButtonUp", "MouseUp");
                    break;
                }
                case Touching.RightDown:
                {
                    Input.Mouse.RightButton = MouseButtonState.Pressed;
                    var e = Args(w, p, MouseButton.Right, MouseButtonState.Pressed, 1, target);
                    Tunnel(route, e, "PreviewMouseDown", "PreviewMouseRightButtonDown");
                    Bubble(route, e, "MouseRightButtonDown", "MouseDown");
                    break;
                }
                case Touching.RightUp:
                {
                    Input.Mouse.RightButton = MouseButtonState.Released;
                    var e = Args(w, p, MouseButton.Right, MouseButtonState.Released, 1, target);
                    Tunnel(route, e, "PreviewMouseUp", "PreviewMouseRightButtonUp");
                    Bubble(route, e, "MouseRightButtonUp", "MouseUp");
                    break;
                }
                case Touching.Move:
                {
                    var e = new MouseEventArgs { WindowPoint = p, Window = w, OriginalSource = target, Source = target };
                    Tunnel(route, e, "PreviewMouseMove");
                    Bubble(route, e, "MouseMove");
                    break;
                }
                case Touching.Wheel:
                {
                    var e = new MouseWheelEventArgs { WindowPoint = p, Window = w, Delta = wheel, OriginalSource = target, Source = target };
                    Tunnel(route, e, "PreviewMouseWheel");
                    Bubble(route, e, "MouseWheel");
                    break;
                }
            }
            if (_dirty) LayoutNow(render: false);   // 손 · 글쇠를 받을 때는 자리만 잡고, 그리기는 다음 틱에 한 번
        }

        private static MouseButtonEventArgs Args(Window w, Point p, MouseButton b, MouseButtonState s, int clicks, UIElement target) =>
            new() { WindowPoint = p, Window = w, ChangedButton = b, ButtonState = s, ClickCount = clicks, OriginalSource = target, Source = target };

        private static void Tunnel(List<UIElement> route, RoutedEventArgs e, params string[] names)
        {
            foreach (string name in names)
                for (int i = route.Count - 1; i >= 0 && !e.Handled; i--)
                {
                    if (!route[i].IsEnabled) continue;
                    route[i].Raise(name, e);
                }
        }

        private static void Bubble(List<UIElement> route, RoutedEventArgs e, params string[] names)
        {
            foreach (var at in route)
            {
                if (!at.IsEnabled) continue;
                foreach (string name in names)
                {
                    if (e.Handled) return;
                    e.Source = at;
                    at.Raise(name, e);
                }
            }
        }

        private static void UpdateHover(Window w, Point p)
        {
            var hit = HitTest(w, p);
            var now = Route(hit);
            foreach (var gone in Input.Mouse.Hovered.Where(e => !now.Contains(e)).ToList())
            {
                Input.Mouse.Hovered.Remove(gone);
                gone.Raise("MouseLeave", new MouseEventArgs { WindowPoint = p, Window = w });
            }
            foreach (var came in Enumerable.Reverse(now).Where(e => !Input.Mouse.Hovered.Contains(e)).ToList())
            {
                Input.Mouse.Hovered.Add(came);
                came.Raise("MouseEnter", new MouseEventArgs { WindowPoint = p, Window = w });
            }
        }

        internal static bool Key(Window w, Key key)
        {
            if (!w.IsShown) return false;
            if (_dirty) LayoutNow(render: false);   // 손 · 글쇠를 받을 때는 자리만 잡고, 그리기는 다음 틱에 한 번
            var focused = Keyboard.FocusedElement as UIElement;
            if (focused == null || !ReferenceEquals(focused.Root(), w)) focused = w;
            var route = Route(focused);
            var e = new KeyEventArgs(key) { OriginalSource = focused, Source = focused };
            Tunnel(route, e, "PreviewKeyDown");
            Bubble(route, e, "KeyDown");
            foreach (var (type, ev, handler) in EventManager.ClassHandlers)
                if (!e.Handled && ev.Name == "KeyDown" && type.IsInstanceOfType(w)) ((KeyEventHandler)handler)(w, e);
            if (!e.Handled) e.Handled = Fallback(w, key);
            bool handled = e.Handled;
            var up = new KeyEventArgs(key) { OriginalSource = focused, Source = focused, IsDown = false };
            Tunnel(route, up, "PreviewKeyUp");
            Bubble(route, up, "KeyUp");
            if (_dirty) LayoutNow(render: false);   // 손 · 글쇠를 받을 때는 자리만 잡고, 그리기는 다음 틱에 한 번
            return handled;
        }

        /// <summary>
        /// 아무도 안 받은 글쇠 — WPF 의 기본 동작: 엔터 = 기본 단추(IsDefault), ESC = 취소 단추(IsCancel),
        /// 위 · 아래 = 창의 목록(ListBox · 표) 고르기. 패드만으로 PC 판 창을 다룰 수 있게 한다.
        /// </summary>
        private static bool Fallback(Window w, Key key)
        {
            var all = Every(w).ToList();
            if (key is Input.Key.Enter or Input.Key.Space)
            {
                if (all.OfType<Controls.Button>().FirstOrDefault(b => b.IsDefault && b.IsEnabled) is { } ok) { ok.PerformClick(); return true; }
                if (key == Input.Key.Enter && all.OfType<Controls.Primitives.Selector>().FirstOrDefault(l => l is Controls.ListBox or Controls.DataGrid && l.SelectedIndex >= 0) is { } pickList)
                    pickList.Raise("MouseLeftButtonDown", new MouseButtonEventArgs { ChangedButton = MouseButton.Left, ClickCount = 2 });
            }
            if (key == Input.Key.Escape && all.OfType<Controls.Button>().FirstOrDefault(b => b.IsCancel && b.IsEnabled) is { } no) { no.PerformClick(); return true; }
            if (key is Input.Key.Up or Input.Key.Down
                && all.OfType<Controls.Primitives.Selector>().FirstOrDefault(l => l is Controls.ListBox or Controls.DataGrid) is { } list)
            {
                int n = list.AllItems.Count();
                if (n == 0) return false;
                list.SelectedIndex = Math.Clamp(list.SelectedIndex + (key == Input.Key.Down ? 1 : -1), 0, n - 1);
                return true;
            }
            return false;
        }

        private static IEnumerable<UIElement> Every(UIElement e)
        {
            if (!e.ShowsUp) yield break;
            yield return e;
            foreach (var c in e.VisualChildren)
                foreach (var d in Every(c)) yield return d;
        }

        internal static Key KeyOf(GameKey key) => key switch
        {
            GameKey.Escape => Input.Key.Escape,
            GameKey.Enter => Input.Key.Enter,
            GameKey.Space => Input.Key.Space,
            GameKey.Up => Input.Key.Up,
            GameKey.Down => Input.Key.Down,
            GameKey.Left => Input.Key.Left,
            GameKey.Right => Input.Key.Right,
            >= GameKey.D0 and <= GameKey.D9 => Input.Key.D0 + (key - GameKey.D0),
            GameKey.PageUp => Input.Key.PageUp,
            GameKey.PageDown => Input.Key.PageDown,
            GameKey.Backspace => Input.Key.Back,
            GameKey.Plus => Input.Key.OemPlus,
            GameKey.Minus => Input.Key.OemMinus,
            GameKey.Tab => Input.Key.Tab,
            GameKey.Insert => Input.Key.Insert,
            GameKey.Delete => Input.Key.Delete,
            GameKey.Home => Input.Key.Home,
            GameKey.End => Input.Key.End,
            GameKey.Comma => Input.Key.OemComma,
            GameKey.Period => Input.Key.OemPeriod,
            GameKey.F1 => Input.Key.F1, GameKey.F2 => Input.Key.F2, GameKey.F3 => Input.Key.F3, GameKey.F4 => Input.Key.F4,
            GameKey.F5 => Input.Key.F5, GameKey.F6 => Input.Key.F6, GameKey.F7 => Input.Key.F7, GameKey.F8 => Input.Key.F8,
            GameKey.F9 => Input.Key.F9, GameKey.F10 => Input.Key.F10, GameKey.F11 => Input.Key.F11, GameKey.F12 => Input.Key.F12,
            _ when Enum.TryParse<Key>(key.ToString(), out var k) => k,
            _ => Input.Key.None,
        };
    }

    /// <summary>창 판(ChoicePanel)에 얹힌 WPF 창 한 장 — 그림을 받아 그리고, 손을 게임 줄로 넘긴다.</summary>
    internal sealed class WpfLayer : ChoicePanel.IGameWindow
    {
        private readonly Window _window;
        private readonly object _gate = new();
        private ABitmap? _front;
        internal ABitmap? Back;
        internal ABitmap? Front => _front;
        private float _w, _h, _x, _y;
        internal volatile int RenderScale = 2;
        internal readonly ChoicePanel.Layer Layer;

        internal WpfLayer(Window window)
        {
            _window = window;
            Layer = new ChoicePanel.Layer { Kind = ChoicePanel.Kind.Custom, Custom = this, Sticky = true };
        }

        internal bool Hidden { set => Layer.Hidden = value; }

        /// <summary>판보다 큰 창을 줄여 그리는 배율(1 이하).</summary>
        private float _fit = 1;

        internal void Swap(ABitmap back, double x, double y, double w, double h)
        {
            lock (_gate)
            {
                Back = _front;
                _front = back;
                _fit = (float)Math.Min(1, Math.Min(640 / Math.Max(1, w), 480 / Math.Max(1, h)));
                _x = (float)x; _y = (float)y; _w = (float)w * _fit; _h = (float)h * _fit;
            }
            GameThread.OnUi(() => GameThread.Panel.Invalidate());
        }

        /// <summary>창 안 네모를 원본 판(640x480) 좌표로.</summary>
        internal Rect ToBoard(Rect r) => new(_x + r.X * _fit, _y + r.Y * _fit, r.Width * _fit, r.Height * _fit);

        public float Width => _w;
        public float Height => _h;
        public float? Left => _x;
        public float? Top => _y;

        internal void DrawAt(ACanvas canvas, float k, float top, float left = 0)
        {
            if (Layer.Hidden) return;
            int want = Math.Clamp((int)Math.Ceiling(k), 1, 4);
            if (want != RenderScale) { RenderScale = want; GameThread.Post(WpfHost.Touch); }
            lock (_gate)
            {
                if (_front == null) return;
                // 판 밖으로 나가면 안쪽으로 당긴다(4:3 처럼 그림 옆에 자리가 없을 때).
                float room = WpfHost.SideRoom;
                float x = left == 0 ? _x : Math.Clamp(_x, -room, 640 + room - _w);
                canvas.DrawBitmap(_front, null, new ARectF((left + x) * k, (_y - top) * k, (left + x + _w) * k, (_y - top + _h) * k), WpfPaints.Bitmap(true));
            }
        }

        public void Draw(Android.Content.Context context, ACanvas canvas, float x, float y, float s, bool top)
        {
            int want = Math.Clamp((int)Math.Ceiling(s), 1, 4);
            if (want != RenderScale) { RenderScale = want; GameThread.Post(WpfHost.Touch); }
            lock (_gate)
            {
                if (_front == null) return;
                canvas.DrawBitmap(_front, null, new ARectF(x * s, y * s, (x + _w) * s, (y + _h) * s), WpfPaints.Bitmap(true));
            }
            if (HasCloseBox) DrawCloseBox(canvas, x, y, s);
        }

        // ── 닫기 단추 — PC 판에서 윈도 제목 줄(X)로 닫던 창(편집 · 도구 창)은 안드로이드에 그 줄이 없다.
        //    창 오른쪽 위에 X 를 그려 주고, 누르면 그 X 를 누른 것처럼 닫는다. 게임 창(제목 줄 없음)에는 안 단다.

        private const float CloseSize = 22, CloseMargin = 2;
        private bool HasCloseBox => _window.WindowStyle != WindowStyle.None;
        private bool InCloseBox(float x, float y) =>
            HasCloseBox && x >= _w - CloseSize - CloseMargin && x <= _w - CloseMargin && y >= CloseMargin && y <= CloseMargin + CloseSize;
        private bool _closeDown;

        private static readonly APaint CloseFill = new() { Color = new Android.Graphics.Color(0xB0, 0x30, 0x28, 255), AntiAlias = true };
        private static readonly APaint CloseEdge = new() { Color = Android.Graphics.Color.White, AntiAlias = true, StrokeWidth = 2 };

        private void DrawCloseBox(ACanvas canvas, float x, float y, float s)
        {
            float l = (x + _w - CloseSize - CloseMargin) * s, t = (y + CloseMargin) * s, r = l + CloseSize * s, b = t + CloseSize * s;
            canvas.DrawRect(l, t, r, b, CloseFill);
            float pad = CloseSize * 0.28f * s;
            CloseEdge.StrokeWidth = Math.Max(2, 2 * s);
            canvas.DrawLine(l + pad, t + pad, r - pad, b - pad, CloseEdge);
            canvas.DrawLine(r - pad, t + pad, l + pad, b - pad, CloseEdge);
        }

        /// <summary>손을 흘려보낼 곳(떠 있는 쪽지면 주인 창).</summary>
        private WpfLayer Target => _window.PassThrough && _window.Owner is { } o && WpfHost.LayerOf(o) is { } l ? l.Target : this;

        public void Key(GameKey key)
        {
            if (Target != this) { Target.Key(key); return; }
            // 취소(ESC) — 창이 ESC 를 안 받았고 윈도 제목 줄이 있던 창(편집 · 도구 창)이면 그 닫기 단추(X)를 누른 것으로 친다.
            // 게임 창은 ESC 만 보낸다(「뒤로」처럼 오른쪽 딸깍까지 치면 해전 항복 차림표 따위가 뜬다).
            if (key == GameKey.Escape)
            {
                GameThread.Post(() =>
                {
                    if (WpfHost.Key(_window, System.Windows.Input.Key.Escape) || !_window.IsShown) return;
                    if (_window.WindowStyle != WindowStyle.None) _window.Close();
                });
                return;
            }
            var k = WpfHost.KeyOf(key);
            if (k == System.Windows.Input.Key.None) return;
            GameThread.Post(() => WpfHost.Key(_window, k));
        }

        public void Touch(Android.Views.MotionEventActions action, float x, float y)
        {
            if (Target is var t && t != this) { t.Touch(action, (x + _x - t._x), (y + _y - t._y)); return; }
            var p = new Point(x / _fit, y / _fit);
            var act = action & Android.Views.MotionEventActions.Mask;
            if (act == Android.Views.MotionEventActions.Down && InCloseBox(x, y)) { _closeDown = true; return; }
            if (_closeDown)
            {
                if (act is Android.Views.MotionEventActions.Up or Android.Views.MotionEventActions.Cancel)
                {
                    _closeDown = false;
                    if (act == Android.Views.MotionEventActions.Up && InCloseBox(x, y)) GameThread.Post(() => { if (_window.IsShown) _window.Close(); });
                }
                return;
            }
            switch (action & Android.Views.MotionEventActions.Mask)
            {
                case Android.Views.MotionEventActions.Down:
                    // 누름은 잠깐 미룬다 — 그대로 오래 누르고 있으면 오른쪽 딸깍(PC 판 우클릭: 배 정보 · 항복 차림표 …)이다.
                    _downAt = p;
                    _pending = true;
                    _longFired = false;
                    int stamp = ++_downStamp;
                    _longTimer?.Dispose();
                    _longTimer = new System.Threading.Timer(_ => GameThread.OnUi(() =>
                    {
                        if (!_pending || stamp != _downStamp) return;
                        _pending = false;
                        _longFired = true;
                        var at = _downAt;
                        GameThread.Post(() =>
                        {
                            WpfHost.Pointer(_window, WpfHost.Touching.Move, at);
                            WpfHost.Pointer(_window, WpfHost.Touching.RightDown, at);
                            WpfHost.Pointer(_window, WpfHost.Touching.RightUp, at);
                        });
                    }), null, LongPressMs, System.Threading.Timeout.Infinite);
                    break;
                case Android.Views.MotionEventActions.Move:
                    if (_longFired)
                    {
                        // 길게 누른 뒤에도(오른쪽 딸깍은 이미 갔다) 손가락이 움직이면 끌기로 잇는다 — 대고 잠깐 멈췄다가
                        // 밀면 목록 · 표가 안 굴러 「될 때도 안 될 때도」 있었다.
                        if (Math.Abs(p.X - _downAt.X) < Slop && Math.Abs(p.Y - _downAt.Y) < Slop) break;
                        _longFired = false;
                        Press();
                    }
                    if (_pending)
                    {
                        if (Math.Abs(p.X - _downAt.X) < Slop && Math.Abs(p.Y - _downAt.Y) < Slop) break;
                        Press();   // 끌기 시작 — 미뤄 둔 누름을 먼저 보낸다
                    }
                    PostMove(p);
                    break;
                case Android.Views.MotionEventActions.Up:
                    _longTimer?.Dispose();
                    if (_longFired) { _longFired = false; break; }
                    if (_pending) Press();
                    GameThread.Post(() => WpfHost.Pointer(_window, WpfHost.Touching.Up, p));
                    break;
                case Android.Views.MotionEventActions.Cancel:
                    _longTimer?.Dispose();
                    _pending = false;
                    GameThread.Post(() => WpfHost.Pointer(_window, WpfHost.Touching.Up, new Point(-9999, -9999)));
                    break;
            }
        }

        // 움직임 모으기 — 게임 줄이 아직 앞 움직임을 처리하지 못했으면 가장 새 자리만 남긴다(쌓여 밀리지 않게).
        private Point _latestMove;
        private volatile bool _movePosted;
        private void PostMove(Point p)
        {
            _latestMove = p;
            if (_movePosted) return;
            _movePosted = true;
            GameThread.Post(() => { _movePosted = false; WpfHost.Pointer(_window, WpfHost.Touching.Move, _latestMove); });
        }

        private const int LongPressMs = 500;
        private const double Slop = 6;
        private Point _downAt;
        private bool _pending, _longFired;
        private int _downStamp;
        private System.Threading.Timer? _longTimer;

        private void Press()
        {
            _pending = false;
            _longTimer?.Dispose();
            var at = _downAt;
            GameThread.Post(() => { WpfHost.Pointer(_window, WpfHost.Touching.Move, at); WpfHost.Pointer(_window, WpfHost.Touching.Down, at); });
        }

        public void Hover(float x, float y)
        {
            if (Target is var t && t != this) { t.Hover(x + _x - t._x, y + _y - t._y); return; }
            var p = new Point(x / _fit, y / _fit);
            GameThread.Post(() => WpfHost.Pointer(_window, WpfHost.Touching.Move, p));
        }

        public bool RightClick(float x, float y)
        {
            if (Target is var t && t != this) return t.RightClick(x + _x - t._x, y + _y - t._y);
            var p = new Point(x / _fit, y / _fit);
            GameThread.Post(() =>
            {
                WpfHost.Pointer(_window, WpfHost.Touching.Move, p);
                WpfHost.Pointer(_window, WpfHost.Touching.RightDown, p);
                WpfHost.Pointer(_window, WpfHost.Touching.RightUp, p);
            });
            return true;
        }

        /// <summary>ESC · 뒤로 — 글쇠 ESC 를 넘기고, 아무도 안 받으면 오른쪽 딸깍으로 친다(원본 창들이 그렇게 닫힌다).</summary>
        public void Cancel()
        {
            if (Target != this) { Target.Cancel(); return; }
            GameThread.Post(() =>
            {
                if (WpfHost.Key(_window, System.Windows.Input.Key.Escape) || !_window.IsShown) return;
                // 윈도 제목 줄이 있던 창(개발 · 편집 창)은 그 닫기 단추를 누른 것으로 친다.
                if (_window.WindowStyle != WindowStyle.None) { _window.Close(); return; }
                var at = WpfHost.LastPoint;
                WpfHost.Pointer(_window, WpfHost.Touching.RightDown, at);
                WpfHost.Pointer(_window, WpfHost.Touching.RightUp, at);
            });
        }

        /// <summary>
        /// 손가락 두 개 벌리기 · 오므리기 — 바퀴 한 칸(120)으로 바꿔 넘긴다. 지도 창(발견물 지도 · 발견물 자리 고르기)은 바퀴로 키우고 줄인다.
        /// 비가 한 번에 작게 오므로 쌓아 두었다가 1.35 배를 넘을 때마다 한 칸씩 보낸다.
        /// </summary>
        public void Pinch(float factor, float x, float y)
        {
            if (Target is var t && t != this) { t.Pinch(factor, x + _x - t._x, y + _y - t._y); return; }
            if (factor <= 0) { _pinchAcc = 1; return; }   // 새 벌리기의 시작 — 지난 것의 나머지를 버린다
            _pinchAcc *= factor;
            const float Step = 1.35f;   // 지도 배율 한 칸(약 1.5배)과 비슷하게 — 1.15 는 너무 민감했다
            while (_pinchAcc > Step) { _pinchAcc /= Step; Wheel(x / _fit, y / _fit, 120); }
            while (_pinchAcc < 1 / Step) { _pinchAcc *= Step; Wheel(x / _fit, y / _fit, -120); }
        }
        private float _pinchAcc = 1;

        /// <summary>바퀴(굴리기) — 손가락 두 개 끌기 따위에서 부른다.</summary>
        public void Wheel(float x, float y, int delta) =>
            GameThread.Post(() => WpfHost.Pointer(_window, WpfHost.Touching.Wheel, new Point(x, y), delta));
    }
}
