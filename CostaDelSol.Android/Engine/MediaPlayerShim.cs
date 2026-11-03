using Android.App;
using Android.Widget;
using AndroidPlayer = Android.Media.MediaPlayer;

namespace System.Windows.Media;

/// <summary>
/// PC 판 <c>BgmPlayer</c> 가 쓰는 WPF <c>MediaPlayer</c> 자리를 안드로이드 재생기로 채운다 — 같은 이름·같은 모양이라
/// <c>BgmPlayer.cs</c>(곡 고르기·되풀이·해역이 바뀔 때 기다렸다 갈아타기)를 그대로 끌어다 쓴다.
/// </summary>
/// <remarks>
/// 주소는 <c>asset:///bgm/Track23.mp3</c> 꼴이다(<c>BgmPlayer.Locate</c>). 곡은 APK 안에 <b>눌리지 않은 채</b>
/// 들어 있어 파일로 꺼내지 않고 그 자리에서 튼다(<c>AndroidStoreUncompressedFileExtensions</c>).
/// </remarks>
public sealed class MediaPlayer
{
    private AndroidPlayer? _player;
    private double _volume = 1;

    public event EventHandler? MediaEnded;

    public void Open(Uri uri)
    {
        Close();
        var player = new AndroidPlayer();
        string path = uri.Scheme == "asset" ? uri.AbsolutePath.TrimStart('/') : uri.LocalPath;
        if (uri.Scheme == "asset")
        {
            using var fd = Android.App.Application.Context.Assets!.OpenFd(path);
            player.SetDataSource(fd.FileDescriptor, fd.StartOffset, fd.Length);
        }
        else player.SetDataSource(path);
        player.Prepare();
        player.Completion += (_, _) => MediaEnded?.Invoke(this, EventArgs.Empty);
        player.SetVolume((float)_volume, (float)_volume);
        _player = player;
    }

    public void Play() => _player?.Start();

    /// <summary>그 자리에서 멈춘다 — <see cref="Play"/> 가 멈춘 자리부터 잇는다.</summary>
    public void Pause()
    {
        try { if (_player?.IsPlaying == true) _player.Pause(); }
        catch (Java.Lang.IllegalStateException) { }
    }

    public void Stop()
    {
        if (_player is { IsPlaying: true } p) p.Pause();
    }

    public void Close()
    {
        _player?.Release();
        _player = null;
    }

    public TimeSpan Position
    {
        get => TimeSpan.FromMilliseconds(_player?.CurrentPosition ?? 0);
        set => _player?.SeekTo((int)value.TotalMilliseconds);
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            _player?.SetVolume((float)_volume, (float)_volume);
        }
    }
}
