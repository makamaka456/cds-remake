using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;

namespace System.Windows
{
    /// <summary>
    /// PC 판 창(Window). 안드로이드에서는 원본 640x480 판 위에 뜨는 그림 한 장(WpfHost)이다.
    /// </summary>
    /// <remarks>
    /// PC 판 시설 차림표처럼 물음창만 부르는 창(지도 · 도시)은 <see cref="Show"/> 하지 않고 「주인」 표시로만 쓴다 —
    /// 그런 창은 판 전체(0,0,640,480) 크기로 친다. 손수 지은 창(육상전 · 해전 · 미니게임 …)은 <see cref="Show"/>
    /// 로 판 위에 뜨고, 요소 나무를 WpfHost 가 재고 그린다.
    /// </remarks>
    public class Window : ContentControl
    {
        public static readonly Window Screen = new();

        private Window? _owner;
        private double _left = double.NaN, _top = double.NaN;
        private bool _closed;
        internal bool IsShown;
        internal double ScreenLeft, ScreenTop;

        public Window()
        {
            Rendered = new Size(640, 480);      // 띄우지 않는 창(지도 · 도시)은 판 전체다
            Background = Brushes.White;
        }

        /// <summary>이 창을 낸 창 — 도시 화면이면 바다 화면이다.</summary>
        public Window? Owner
        {
            get => _owner;
            set
            {
                _owner?.Owned.Remove(this);
                _owner = value;
                value?.Owned.Add(this);
            }
        }

        internal readonly List<Window> Owned = [];
        public IEnumerable<Window> OwnedWindows => Owned.ToList();

        public string Title { get; set; } = "";
        public WindowStyle WindowStyle { get; set; } = WindowStyle.SingleBorderWindow;
        public ResizeMode ResizeMode { get; set; }
        public bool ShowInTaskbar { get; set; }
        public bool ShowActivated { get; set; } = true;
        public bool Topmost { get; set; }
        public bool AllowsTransparency { get; set; }
        public WindowStartupLocation WindowStartupLocation { get; set; }
        public SizeToContent SizeToContent { get; set; }
        public WindowState WindowState { get; set; }
        public ImageSource? Icon { get; set; }
        public bool IsActive => IsShown;

        public double Left
        {
            get => double.IsNaN(_left) ? ScreenLeft : _left;
            set { _left = value; WpfHost.Touch(); LocationChanged?.Invoke(this, EventArgs.Empty); }
        }

        public double Top
        {
            get => double.IsNaN(_top) ? ScreenTop : _top;
            set { _top = value; WpfHost.Touch(); LocationChanged?.Invoke(this, EventArgs.Empty); }
        }

        internal bool Placed => !double.IsNaN(_left) && !double.IsNaN(_top);

        /// <summary>손을 안 받고 밑의 주인 창으로 흘려보낸다(떠 있는 쪽지).</summary>
        internal bool PassThrough;

        /// <summary>지도 위 덧그림 창 — 창 판이 아니라 지도 덧그림 판에 그린다(손은 지도로 간다).</summary>
        internal bool OnMap;

        /// <summary>재기 직전(쪽지가 붙은 요소를 따라 자리를 다시 잡는다).</summary>
        internal virtual void BeforeLayout() { }

        internal void SetPlace(double x, double y) { _left = x; _top = y; }

        /// <summary>닫히지 않았다(안드로이드에서는 띄운 적 없는 창도 살아 있는 것으로 친다).</summary>
        public new bool IsLoaded => !_closed;

        public event EventHandler? Closed, Activated, Deactivated, ContentRendered, SourceInitialized, LocationChanged, StateChanged;
        public event CancelEventHandler? Closing;

        private bool? _dialogResult;

        public bool? DialogResult
        {
            get => _dialogResult;
            set { _dialogResult = value; Close(); }
        }

        public void Show()
        {
            if (_closed) return;
            WpfHost.Open(this);
        }

        public bool? ShowDialog()
        {
            Show();
            var frame = new Threading.DispatcherFrame();
            void Stop(object? s, EventArgs e) => frame.Continue = false;
            Closed += Stop;
            try { if (!_closed) Threading.Dispatcher.PushFrame(frame); }
            finally { Closed -= Stop; }
            return _dialogResult;
        }

        public void Hide() => Visibility = Visibility.Hidden;

        public virtual void Close()
        {
            if (_closed) return;
            var args = new CancelEventArgs();
            Closing?.Invoke(this, args);
            if (args.Cancel) return;
            _closed = true;
            foreach (var w in Owned.ToList()) w.Close();
            WpfHost.Shut(this);
            Owner = null;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public bool Activate() => true;
        public void DragMove() { }

        internal void FireContentRendered() => ContentRendered?.Invoke(this, EventArgs.Empty);
        internal void FireSourceInitialized() => SourceInitialized?.Invoke(this, EventArgs.Empty);

        public static Window? GetWindow(DependencyObject d) => (d as UIElement)?.Root() as Window;
    }

    public static class Clipboard
    {
        public static void SetText(string text) =>
            CostaDelSol.Droid.Game.GameThread.OnUi(() =>
            {
                var activity = CostaDelSol.Droid.Game.GameThread.Activity;
                if (activity?.GetSystemService(Android.Content.Context.ClipboardService) is Android.Content.ClipboardManager clip)
                    clip.PrimaryClip = Android.Content.ClipData.NewPlainText("CostaDelSol", text);
            });
        public static string GetText() => "";
        public static bool ContainsText() => false;
    }

    public static class MessageBox
    {
        public static MessageBoxResult Show(string text, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            if (button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel)
                return CdsHelper.Game.UI.Views.ConfirmDialog.Ask(Window.Screen, text) ? (button == MessageBoxButton.OKCancel ? MessageBoxResult.OK : MessageBoxResult.Yes)
                                                                                     : (button == MessageBoxButton.OKCancel ? MessageBoxResult.Cancel : MessageBoxResult.No);
            CdsHelper.Game.UI.Views.ConfirmDialog.Tell(Window.Screen, text);
            return MessageBoxResult.OK;
        }

        public static MessageBoxResult Show(Window owner, string text, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None) =>
            Show(text, caption, button, icon);
    }

    public enum MessageBoxButton { OK, OKCancel, YesNoCancel = 3, YesNo = 4 }
    public enum MessageBoxImage { None, Error = 16, Question = 32, Warning = 48, Information = 64, Asterisk = 64, Exclamation = 48, Hand = 16, Stop = 16 }
}

namespace System.Windows.Threading
{
    /// <summary>WPF 의 디스패처 자리 — 게임 줄이 곧 디스패처다.</summary>
    public sealed class Dispatcher
    {
        public static Dispatcher CurrentDispatcher { get; } = new();

        public DispatcherOperation BeginInvoke(Delegate work, params object?[] args)
        {
            var op = new DispatcherOperation();
            CostaDelSol.Droid.Game.GameThread.Post(() => { if (!op.Aborted) { work.DynamicInvoke(args.Length == 0 ? null : args); op.Done(); } });
            return op;
        }

        public DispatcherOperation BeginInvoke(Action work) => BeginInvoke((Delegate)work);
        public DispatcherOperation BeginInvoke(Action work, DispatcherPriority priority) => BeginInvoke((Delegate)work);
        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Delegate work) => BeginInvoke(work);
        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Action work) => BeginInvoke((Delegate)work);
        public DispatcherOperation InvokeAsync(Action work) => BeginInvoke((Delegate)work);
        public DispatcherOperation InvokeAsync(Action work, DispatcherPriority priority) => BeginInvoke((Delegate)work);

        public void Invoke(Action work) => Run(work);
        public void Invoke(Action work, DispatcherPriority priority)
        {
            // Render 이하로 한 바퀴 돌리라는 뜻 — 밀린 그림을 바로 그린다.
            Run(work);
            if (CostaDelSol.Droid.Game.GameThread.IsCurrent) WpfHost.Tick();
        }
        public void Invoke(DispatcherPriority priority, Action work) => Invoke(work, priority);
        public T Invoke<T>(Func<T> work) => work();

        private static void Run(Action work)
        {
            if (CostaDelSol.Droid.Game.GameThread.IsCurrent || CostaDelSol.Droid.Game.GameThread.Activity == null) work();
            else
            {
                using var done = new ManualResetEventSlim();
                CostaDelSol.Droid.Game.GameThread.Post(() => { try { work(); } finally { done.Set(); } });
                done.Wait();
            }
        }

        public bool CheckAccess() => CostaDelSol.Droid.Game.GameThread.IsCurrent;
        public void VerifyAccess() { }
        public bool HasShutdownStarted => false;
        public bool HasShutdownFinished => false;

        /// <summary>틀이 끝날 때까지 이 자리에서 일을 돌린다(모달).</summary>
        public static void PushFrame(DispatcherFrame frame) =>
            CostaDelSol.Droid.Game.GameThread.Pump(() => frame.Continue);

        public static DispatcherYield Yield(DispatcherPriority priority = DispatcherPriority.Background) => new();
        public static void ExitAllFrames() { }

        public event EventHandler? ShutdownStarted { add { } remove { } }
        public event DispatcherUnhandledExceptionEventHandler? UnhandledException { add { } remove { } }
    }

    public delegate void DispatcherUnhandledExceptionEventHandler(object sender, EventArgs e);

    public readonly struct DispatcherYield
    {
        public Awaiter GetAwaiter() => new();
        public readonly struct Awaiter : System.Runtime.CompilerServices.INotifyCompletion
        {
            public bool IsCompleted => false;
            public void OnCompleted(Action c) => CostaDelSol.Droid.Game.GameThread.Post(c);
            public void GetResult() { }
        }
    }

    public sealed class DispatcherOperation
    {
        internal bool Aborted;
        private readonly TaskCompletionSource _done = new();
        internal void Done() => _done.TrySetResult();
        public bool Abort() { Aborted = true; return true; }
        public Task Task => _done.Task;
        public DispatcherOperationStatus Status => _done.Task.IsCompleted ? DispatcherOperationStatus.Completed : DispatcherOperationStatus.Pending;
        public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() => _done.Task.GetAwaiter();
        public DispatcherOperationStatus Wait() { return Status; }
    }

    public enum DispatcherOperationStatus { Pending, Aborted, Completed, Executing }

    public enum DispatcherPriority
    {
        Invalid = -1, Inactive = 0, SystemIdle, ApplicationIdle, ContextIdle, Background, Input, Loaded, Render, DataBind, Normal, Send,
    }

    public sealed class DispatcherFrame
    {
        public DispatcherFrame() { }
        public DispatcherFrame(bool exitWhenRequested) { }
        public bool Continue { get; set; } = true;
    }

    /// <summary>게임 줄에서 도는 시계.</summary>
    public sealed class DispatcherTimer
    {
        private TimeSpan _interval;
        private bool _on;
        internal long Due;

        public DispatcherTimer() { }
        public DispatcherTimer(DispatcherPriority priority) { }
        public DispatcherTimer(DispatcherPriority priority, Dispatcher dispatcher) { }
        public DispatcherTimer(TimeSpan interval, DispatcherPriority priority, EventHandler tick, Dispatcher dispatcher)
        {
            _interval = interval;
            Tick += tick;
            Start();
        }

        public TimeSpan Interval
        {
            get => _interval;
            set { _interval = value; if (_on) Due = WpfHost.Now + (long)_interval.TotalMilliseconds; }
        }

        public bool IsEnabled
        {
            get => _on;
            set { if (value) Start(); else Stop(); }
        }

        public object? Tag { get; set; }
        public Dispatcher Dispatcher => Dispatcher.CurrentDispatcher;
        public event EventHandler? Tick;

        public void Start()
        {
            Due = WpfHost.Now + (long)_interval.TotalMilliseconds;
            if (_on) return;
            _on = true;
            WpfHost.Timers.Add(this);
        }

        public void Stop()
        {
            if (!_on) return;
            _on = false;
            WpfHost.Timers.Remove(this);
        }

        internal void Fire()
        {
            Due = WpfHost.Now + Math.Max(1, (long)_interval.TotalMilliseconds);
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}
