// 안드로이드판 MediaVoice — PC 판(CdsHelper.Game/Local/Helpers/MediaVoice.cs)은 WPF MediaPlayer 가 못 틀면
// WinRT MediaPlayer 로 갈아타는 윈도우 전용 손이다(원본 v1.1.96). 안드로이드는 재생기가 하나뿐이라
// 같은 이름 · 같은 모양으로 System.Windows.Media.MediaPlayer(안드로이드 재생기 대용품, Engine/MediaPlayerShim.cs)를 감싼다.
// BgmPlayer · SoundBank(공용 코드)가 이 이름을 부른다.

namespace CdsHelper.Game.Local.Helpers;

public sealed class MediaVoice
{
    /// <summary>지금 쓰는 쪽 — PC 판은 「wpf」·「winrt」. 안드로이드는 하나다.</summary>
    public static string Backend => "android";

    /// <summary>PC 판의 wmp.dll 유무 — 안드로이드에는 상관없어 늘 참이다.</summary>
    public static bool WmpPresent => true;

    /// <summary>마지막으로 못 튼 까닭. 없으면 빈 문자열.</summary>
    public static string LastFailure { get; private set; } = "";

    /// <summary>못 튼 횟수(모든 손 합).</summary>
    public static int Failures { get; private set; }

    public event EventHandler? MediaEnded;

    /// <summary>못 틀었다 — 까닭 한 줄.</summary>
#pragma warning disable CS0067
    public event Action<string>? Failed;
#pragma warning restore CS0067

    private readonly System.Windows.Media.MediaPlayer _player = new();
    private bool _playing, _open;

    public MediaVoice()
    {
        _player.MediaEnded += (_, e) => { _playing = false; MediaEnded?.Invoke(this, e); };
    }

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = value;
    }

    public TimeSpan Position => _open ? _player.Position : TimeSpan.Zero;

    /// <summary>곡 길이 — 안드로이드 재생기 대용품은 알려 주지 않는다.</summary>
    public TimeSpan? Duration => null;

    public string State => !_open ? "-" : _playing ? "android Playing" : "android Idle";

    public void Open(Uri uri)
    {
        _playing = false;
        try
        {
            _player.Open(uri);
            _open = true;
        }
        catch (Exception ex)
        {
            _open = false;
            Fail($"열기 — {ex.Message}");
        }
    }

    public void Play()
    {
        if (!_open) return;
        _playing = true;
        try { _player.Play(); }
        catch (Exception ex) { Fail($"틀기 — {ex.Message}"); }
    }

    public void Pause()
    {
        _playing = false;
        try { _player.Pause(); }
        catch (Exception) { }
    }

    /// <summary>처음으로 되감는다.</summary>
    public void Rewind()
    {
        try { if (_open) _player.Position = TimeSpan.Zero; }
        catch (Exception) { }
    }

    /// <summary>그 자리로 옮긴다 — 배경음악을 끊지 않고 숨만 죽였다가 멈춘 자리부터 잇는다(원본 v1.2.2 · v1.2.3).</summary>
    public void Seek(TimeSpan at)
    {
        try { if (_open) _player.Position = at; }
        catch (Exception) { }
    }

    public void Stop()
    {
        _playing = false;
        try { _player.Stop(); if (_open) _player.Position = TimeSpan.Zero; }
        catch (Exception) { }
    }

    public void Close()
    {
        _playing = false;
        _open = false;
        try { _player.Close(); }
        catch (Exception) { }
    }

    private void Fail(string why)
    {
        LastFailure = why;
        Failures++;
        Failed?.Invoke(why);
    }
}

/// <summary>
/// PC 판 AppVolumeKeeper(원본 v1.2.8) — Windows 볼륨 믹서의 앱 음량을 기억해 되돌리는 COM 손이다. 안드로이드에는 볼륨 믹서가
/// 없어 빈 대용품만 둔다(버그 리포트 소리 진단만 이 이름을 부른다).
/// </summary>
public static class AppVolumeKeeper
{
    public static string Describe() => "android — 없음";
}
