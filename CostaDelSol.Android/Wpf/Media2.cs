namespace System.Windows.Controls
{
    public enum MediaState { Manual, Play, Close, Pause, Stop }

    /// <summary>
    /// 동영상 칸 — 아직 안드로이드에서 원본 영상(AVI)을 못 틀어 「틀지 못함」으로 알린다. 부르는 쪽(발견 창)은
    /// 그때 그 칸을 접고 알림을 그대로 이어 간다(PC 판에서 코덱이 없을 때와 같다).
    /// </summary>
    public class MediaElement : FrameworkElement
    {
        public Uri? Source { get; set; }
        public MediaState LoadedBehavior { get; set; } = MediaState.Play;
        public MediaState UnloadedBehavior { get; set; } = MediaState.Close;
        public System.Windows.Media.Stretch Stretch { get; set; }
        public double Volume { get; set; } = 0.5;
        public bool IsMuted { get; set; }
        public TimeSpan Position { get; set; }
        public bool ScrubbingEnabled { get; set; }
        public System.Windows.Duration NaturalDuration => new(TimeSpan.Zero);
        public event RoutedEventHandler? MediaEnded, MediaOpened;
        public event EventHandler<ExceptionRoutedEventArgs>? MediaFailed;

        private bool _playing;

        /// <summary>그 칸 자리에 튼다 — 판 위 영상 층(VideoLayer)이 맡는다. 못 틀면 MediaFailed.</summary>
        public void Play()
        {
            string? path = Source?.IsAbsoluteUri == true ? Source.LocalPath : Source?.OriginalString;
            var host = Window.GetWindow(this);
            if (path == null || !System.IO.File.Exists(path) || host == null || WpfHost.LayerOf(host) is not { } layer)
            {
                Dispatcher.BeginInvoke(() => MediaFailed?.Invoke(this, new ExceptionRoutedEventArgs(new System.IO.FileNotFoundException(path))));
                return;
            }
            WpfHost.LayoutNow();
            var a = ToWindow(new Point(0, 0), false);
            var b = ToWindow(new Point(RenderSize.Width, RenderSize.Height), false);
            var board = layer.ToBoard(new Rect(a, b));
            _playing = true;
            double volume = IsMuted ? 0 : Volume * 2;   // WPF 기본 0.5 가 제 소리다
            CostaDelSol.Droid.Game.GameThread.OnUi(() =>
            {
                if (!_playing || CostaDelSol.Droid.Ui.VideoLayer.Current is not { } video) return;
                var view = CostaDelSol.Droid.Game.GameThread.Panel.BoardToView((float)board.X, (float)board.Y, (float)board.Width, (float)board.Height);
                video.Play(path, view, volume,
                    () => CostaDelSol.Droid.Game.GameThread.Post(() => { _playing = false; MediaEnded?.Invoke(this, new RoutedEventArgs()); }),
                    () => CostaDelSol.Droid.Game.GameThread.Post(() => { _playing = false; MediaFailed?.Invoke(this, new ExceptionRoutedEventArgs(new NotSupportedException(path))); }));
            });
            MediaOpened?.Invoke(this, new RoutedEventArgs());
        }

        public void Pause() { }

        public void Stop()
        {
            if (!_playing) return;
            _playing = false;
            CostaDelSol.Droid.Game.GameThread.OnUi(() => CostaDelSol.Droid.Ui.VideoLayer.Current?.Stop());
        }

        public void Close() => Stop();

        protected override Size MeasureOverride(Size available) => new(0, 0);
        internal void FireEnded() => MediaEnded?.Invoke(this, new RoutedEventArgs());
        internal void FireOpened() => MediaOpened?.Invoke(this, new RoutedEventArgs());
    }
}

namespace System.Windows
{
    public sealed class ExceptionRoutedEventArgs(Exception error) : RoutedEventArgs
    {
        public Exception ErrorException => error;
    }
}
