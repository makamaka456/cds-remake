using Android.App;
using Android.Widget;
using System.Collections.Concurrent;
using Android.Graphics;
using Android.Util;
using CostaDelSol.Droid.Input;

namespace CostaDelSol.Droid.Game;

/// <summary>
/// 게임 흐름이 도는 일꾼 줄 — PC 판의 「창을 띄우고 닫힐 때까지 기다린다(ShowDialog)」를 그대로 살린다.
/// </summary>
/// <remarks>
/// PC 판 시설 차림표(항구 · 조선소 · 술집 …)는 물음창을 띄우고 답이 올 때까지 멈춰 섰다가 이어 간다.
/// 안드로이드 화면 줄(UI 스레드)은 멈출 수 없으므로 그 코드를 이 줄에서 돌리고, 창을 띄울 때만
/// 화면 줄에 부탁한 뒤 답을 기다린다(<see cref="Wait{T}"/>). 그래서 PC 판 코드를 한 줄도 안 고치고 쓴다.
/// 한 번에 한 일만 돈다 — 원본도 창 하나가 떠 있는 동안 딴 일은 안 받는다.
/// </remarks>
public static class GameThread
{
    private static readonly BlockingCollection<Action> Queue = new();
    private static Thread? _thread;
    private static Activity? _ui;
    private static int _running;

    /// <summary>창을 그리는 판(원본 창들이 겹쳐 서는 곳).</summary>
    public static ChoicePanel Panel { get; private set; } = null!;

    /// <summary>일이 도는 중이거나 기다리는 일이 있다 — 그동안 차림표 줄은 또 눌리지 않는다.</summary>
    public static bool Busy => Volatile.Read(ref _working) > 0 || Queue.Count > 0;

    private static int _working;

    public static bool IsCurrent => Thread.CurrentThread == _thread;

    /// <summary>화면(기기 글자판 따위를 띄울 때).</summary>
    public static Activity? Activity => _ui;

    /// <summary>일이 터졌을 때 알릴 곳(화면 줄에서 부른다).</summary>
    public static event Action<Exception>? Failed;

    public static void Start(Activity ui, ChoicePanel panel)
    {
        _ui = ui;
        Panel = panel;
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "game" };
        _thread.Start();
    }

    /// <summary>
    /// 할 일이 없을 때 16ms 마다 부른다(지난번부터 흐른 초) — 지도 엔진의 프레임과 상태 시계(PC 판
    /// CompositionTarget.Rendering · 0.1초 타이머)가 여기서 돈다. 창이 일을 붙들고 있는 동안에는 안 돈다(PC 판 모달과 같다).
    /// </summary>
    public static Action<double>? Idle { get; set; }

    private static void Loop()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double last = 0;
        while (true)
        {
            Action? work;
            try { Queue.TryTake(out work, System.Windows.WpfHost.Wait()); }
            catch (ObjectDisposedException) { return; }
            Interlocked.Increment(ref _running);
            try
            {
                if (work != null)
                {
                    Interlocked.Increment(ref _working);
                    try { work(); }
                    finally { Interlocked.Decrement(ref _working); }
                }
                else if (Idle is { } idle)
                {
                    double now = clock.Elapsed.TotalSeconds;
                    double dt = Math.Min(0.1, now - last);
                    last = now;
                    if (dt > 0) idle(dt);
                }
                if (work != null) last = clock.Elapsed.TotalSeconds;   // 창을 기다린 동안은 시간이 안 흐른다
                System.Windows.WpfHost.Tick();
            }
            catch (Exception ex)
            {
                Log.Error("CostaDelSol", ex.ToString());
                OnUi(() => Failed?.Invoke(ex));
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    /// <summary>
    /// 이 자리에서 일을 돌린다 — <paramref name="keepGoing"/> 가 거짓이 될 때까지(WPF 의 PushFrame · 모달 창).
    /// 줄에 선 일(손 · 시계 · 부탁)을 차례로 돌리고 틈마다 WPF 창을 다시 그린다. 지도 프레임(Idle)은 돌지 않는다.
    /// </summary>
    public static void Pump(Func<bool> keepGoing)
    {
        if (!IsCurrent) throw new InvalidOperationException("Pump 는 게임 줄에서만 부를 수 있습니다");
        while (keepGoing())
        {
            if (Queue.TryTake(out var work, System.Windows.WpfHost.Wait()))
            {
                try { work(); }
                catch (Exception ex)
                {
                    Log.Error("CostaDelSol", ex.ToString());
                    OnUi(() => Failed?.Invoke(ex));
                }
            }
            System.Windows.WpfHost.Tick();
        }
    }

    /// <summary>게임 일을 줄에 세운다.</summary>
    public static void Post(Action work) => Queue.Add(work);

    /// <summary>화면 줄에서 돌린다.</summary>
    public static void OnUi(Action work) => _ui?.RunOnUiThread(work);

    /// <summary>
    /// 화면 줄에서 창을 띄우고 그 창이 답을 줄 때까지 기다린다. 게임 줄에서만 부른다.
    /// </summary>
    /// <param name="open">화면 줄에서 불린다 — 창을 띄우고, 닫힐 때 받은 손으로 답을 넘긴다.</param>
    public static T Wait<T>(Action<Action<T>> open)
    {
        if (!IsCurrent)
            throw new InvalidOperationException("창을 기다리는 일은 게임 줄에서만 부를 수 있습니다");
        using var done = new ManualResetEventSlim();
        T result = default!;
        int answered = 0;
        OnUi(() => open(value =>
        {
            if (Interlocked.Exchange(ref answered, 1) != 0) return;
            result = value;
            done.Set();
        }));
        done.Wait();
        return result;
    }

    /// <summary>창 하나를 얹고 답을 기다린다.</summary>
    public static T Show<T>(Func<Action<T>, ChoicePanel.Layer> make) =>
        Wait<T>(reply => Panel.Push(make(reply)));

    /// <summary>원본 초상화(80x96 BGRA 점)를 그림으로.</summary>
    public static Bitmap? Face(uint[]? bgra, int width = 80, int height = 96)
    {
        if (bgra == null || bgra.Length < width * height) return null;
        var argb = new int[width * height];
        for (int i = 0; i < argb.Length; i++) argb[i] = (int)bgra[i];
        return Bitmap.CreateBitmap(argb, width, height, Bitmap.Config.Argb8888!);
    }
}
