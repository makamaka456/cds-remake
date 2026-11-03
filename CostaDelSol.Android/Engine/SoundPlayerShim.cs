using Android.App;
using Android.Widget;
using Android.Media;

namespace System.Media;

/// <summary>
/// PC 판 효과음(<c>SoundBank</c>)이 쓰는 <c>System.Media.SoundPlayer</c> 대역 — 안드로이드 AudioTrack 으로 튼다.
/// </summary>
/// <remarks>
/// SoundBank 는 WAV 한 벌을 <see cref="Stream"/> 에 넣고 <see cref="Play"/> 한다. 여기서는 RIFF 머리를 읽어
/// PCM 을 꺼내 한 번 트는 AudioTrack(정적 모드)을 짓는다. 새 소리가 오면 앞 소리는 멈춘다(PC 와 같다).
/// </remarks>
public sealed class SoundPlayer : IDisposable
{
    private AudioTrack? _track;
    public System.IO.Stream? Stream { get; set; }

    public void Play() => Start(loop: false);

    /// <summary>끊지 않고 되풀이해 튼다(<see cref="Stop"/> 까지).</summary>
    public void PlayLooping() => Start(loop: true);

    private void Start(bool loop)
    {
        var stream = Stream;
        if (stream == null) return;
        byte[] wav;
        using (var buffer = new MemoryStream()) { stream.Position = 0; stream.CopyTo(buffer); wav = buffer.ToArray(); }
        if (!TryParse(wav, out int channels, out int rate, out int bits, out int start, out int length)) return;

        Stop();
        try
        {
            var encoding = bits == 8 ? Encoding.Pcm8bit : Encoding.Pcm16bit;
            var mask = channels == 2 ? ChannelOut.Stereo : ChannelOut.Mono;
            var track = new AudioTrack.Builder()
                .SetAudioAttributes(new AudioAttributes.Builder()!.SetUsage(AudioUsageKind.Game)!
                    .SetContentType(AudioContentType.Sonification)!.Build()!)!
                .SetAudioFormat(new AudioFormat.Builder()!.SetEncoding(encoding)!.SetSampleRate(rate)!
                    .SetChannelMask(mask)!.Build()!)!
                .SetTransferMode(AudioTrackMode.Static)!
                .SetBufferSizeInBytes(Math.Max(length, 4))!
                .Build()!;
            track.Write(wav, start, length);
            if (loop)
            {
                int frames = length / Math.Max(1, channels * bits / 8);
                if (frames > 1) track.SetLoopPoints(0, frames, -1);
            }
            track.Play();
            _track = track;
        }
        catch (Exception)
        {
            _track = null;
        }
    }

    public void Stop()
    {
        var track = _track;
        _track = null;
        if (track == null) return;
        try { track.Stop(); } catch (Java.Lang.IllegalStateException) { }
        track.Release();
    }

    public void Dispose() => Stop();

    private static bool TryParse(byte[] wav, out int channels, out int rate, out int bits, out int start, out int length)
    {
        channels = rate = bits = start = length = 0;
        if (wav.Length < 12 || wav[0] != 'R' || wav[1] != 'I' || wav[8] != 'W') return false;
        int at = 12;
        while (at + 8 <= wav.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(wav, at, 4);
            int size = BitConverter.ToInt32(wav, at + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(wav, at + 10);
                rate = BitConverter.ToInt32(wav, at + 12);
                bits = BitConverter.ToInt16(wav, at + 22);
            }
            else if (id == "data")
            {
                start = at + 8;
                length = Math.Min(size, wav.Length - start);
                return rate > 0 && channels > 0 && length > 0;
            }
            at += 8 + size + (size & 1);
        }
        return false;
    }
}
