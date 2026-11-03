using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Views;
using Android.Widget;

namespace CostaDelSol.Droid.Ui;

/// <summary>
/// 영상 한 편을 창 판 위 한 자리에 튼다 — WPF 동영상 칸(<see cref="System.Windows.Controls.MediaElement"/>)이 부른다.
/// 원본 영상(asset/movie · asset/video 의 MP4)을 안드로이드 재생기로 틀고, 가로세로 비를 지켜 그 칸에 맞춘다.
/// </summary>
public sealed class VideoLayer : FrameLayout, TextureView.ISurfaceTextureListener
{
    /// <summary>화면에 하나뿐이다(MainActivity 가 둔다).</summary>
    public static VideoLayer? Current { get; set; }

    private readonly TextureView _view;
    private MediaPlayer? _player;
    private Surface? _surface;
    private string? _pending;
    private RectF _box = new();
    private Action? _ended, _failed;
    private float _volume = 1;

    public VideoLayer(Context context) : base(context)
    {
        _view = new TextureView(context) { SurfaceTextureListener = this };
        AddView(_view, new LayoutParams(1, 1));
        Visibility = ViewStates.Gone;
        SetBackgroundColor(Android.Graphics.Color.Transparent);
    }

    /// <summary>이 자리(이 판 안 좌표)에 튼다. 끝나면 <paramref name="ended"/>, 못 틀면 <paramref name="failed"/>(화면 줄).</summary>
    public void Play(string path, RectF box, double volume, Action ended, Action failed)
    {
        Stop();
        _box = box;
        _ended = ended;
        _failed = failed;
        _volume = (float)Math.Clamp(volume, 0, 1);
        _pending = path;
        Visibility = ViewStates.Visible;
        Place(box.Width(), box.Height());
        if (_surface != null) Start();
    }

    /// <summary>누르면 넘긴다(시작 영상) — 정해 두면 이 층이 손가락을 받는다.</summary>
    public Action? TapToSkip { get; set; }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (TapToSkip is not { } skip || Visibility != ViewStates.Visible) return false;
        if (e?.Action == MotionEventActions.Up) skip();
        return true;
    }

    public void Stop()
    {
        _pending = null;
        var player = _player;
        _player = null;
        if (player != null)
        {
            try { player.Stop(); } catch (Java.Lang.IllegalStateException) { }
            player.Release();
        }
        Visibility = ViewStates.Gone;
    }

    private void Start()
    {
        if (_pending is not { } path) return;
        _pending = null;
        var player = new MediaPlayer();
        _player = player;
        try
        {
            player.SetDataSource(path);
            player.SetSurface(_surface);
            player.SetVolume(_volume, _volume);
            player.Prepared += (_, _) =>
            {
                if (!ReferenceEquals(_player, player)) return;
                // 가로세로 비를 지켜 칸 안에 맞춘다(WPF Stretch.Uniform).
                float vw = Math.Max(1, player.VideoWidth), vh = Math.Max(1, player.VideoHeight);
                float k = Math.Min(_box.Width() / vw, _box.Height() / vh);
                Place(vw * k, vh * k);
                player.Start();
            };
            player.Completion += (_, _) => { if (ReferenceEquals(_player, player)) { var done = _ended; Stop(); done?.Invoke(); } };
            player.Error += (_, e) => { e.Handled = true; if (ReferenceEquals(_player, player)) { var fail = _failed; Stop(); fail?.Invoke(); } };
            player.PrepareAsync();
        }
        catch (Exception)
        {
            var fail = _failed;
            Stop();
            fail?.Invoke();
        }
    }

    private void Place(float w, float h)
    {
        var lp = new LayoutParams((int)Math.Max(1, w), (int)Math.Max(1, h))
        {
            LeftMargin = (int)(_box.Left + (_box.Width() - w) / 2),
            TopMargin = (int)(_box.Top + (_box.Height() - h) / 2),
        };
        _view.LayoutParameters = lp;
    }

    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        _surface = new Surface(surface);
        Start();
    }

    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
    {
        Stop();
        _surface?.Release();
        _surface = null;
        return true;
    }

    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height) { }
    public void OnSurfaceTextureUpdated(SurfaceTexture surface) { }
}
