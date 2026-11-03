using Android.App;
using Android.Widget;
using CostaDelSol.Droid.Input;

namespace CostaDelSol.Droid.Game;

/// <summary>
/// 따로 지은 창 하나를 게임 줄에서 다루는 손잡이 — 창은 떠 있고, 창이 낸 「일」(단추 이름 따위)을 하나씩 받아
/// 게임 줄에서 한다(그 일이 띄우는 물음창은 창 위에 겹친다). PC 판 창의 단추 처리기와 같은 몫이다.
/// </summary>
public sealed class WindowSession
{
    private readonly ChoicePanel.Layer _layer;
    private readonly Queue<string> _waiting = [];
    private Action<string>? _reply;
    private readonly object _gate = new();

    public WindowSession(ChoicePanel.IGameWindow window)
    {
        _layer = new ChoicePanel.Layer { Kind = ChoicePanel.Kind.Custom, Custom = window };
        GameThread.OnUi(() => GameThread.Panel.Push(_layer));
    }

    /// <summary>창이 일을 낸다(화면 줄). 게임 줄이 바쁘면 줄을 세운다.</summary>
    public void Act(string work)
    {
        Action<string>? reply;
        lock (_gate)
        {
            reply = _reply;
            _reply = null;
            if (reply == null) { if (_waiting.Count < 4) _waiting.Enqueue(work); return; }
        }
        reply(work);
    }

    /// <summary>다음 일을 기다린다(게임 줄).</summary>
    public string Next()
    {
        lock (_gate)
            if (_waiting.Count > 0) return _waiting.Dequeue();
        return GameThread.Wait<string>(reply =>
        {
            lock (_gate)
            {
                if (_waiting.Count > 0) { reply(_waiting.Dequeue()); return; }
                _reply = reply;
            }
        });
    }

    public void Repaint() => GameThread.OnUi(() => GameThread.Panel.Invalidate());

    public void Close() => GameThread.OnUi(() => GameThread.Panel.Remove(_layer));
}
