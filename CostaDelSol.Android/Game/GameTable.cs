using Android.App;
using Android.Widget;
using CostaDelSol.Droid.Input;
using CostaDelSol.Droid.Ui;

namespace CostaDelSol.Droid.Game;

/// <summary>
/// 목록 창 하나를 게임 줄에서 쓰는 손잡이 — PC 판 목록 창처럼 <b>창을 띄워 둔 채</b> 단추마다 일을 하고,
/// 그 일이 띄우는 물음창이 목록 위에 겹친다. 끝나면 <see cref="Close"/>.
/// </summary>
public sealed class GameTable
{
    private readonly ChoicePanel.Layer _layer;
    private readonly TableWindow _window;
    private Action<string?>? _reply;

    internal GameTable(TableWindow window)
    {
        _window = window;
        _layer = new ChoicePanel.Layer { Kind = ChoicePanel.Kind.Custom, Custom = window };
        window.Pressed = button =>
        {
            var reply = _reply;
            _reply = null;
            reply?.Invoke(button);
        };
        GameThread.OnUi(() => GameThread.Panel.Push(_layer));
    }

    internal TableWindow Window => _window;

    /// <summary>단추가 눌릴 때까지 기다린다 — 누른 단추 이름, 중단 · ESC 면 null.</summary>
    public string? Next() => GameThread.Wait<string?>(reply => _reply = reply);

    public IReadOnlyList<int> Chosen => _window.Chosen;
    public int Selected => _window.Selected;

    public void Rebuild(int count) => GameThread.OnUi(() => _window.Rebuild(count));

    public void Close() => GameThread.OnUi(() => GameThread.Panel.Remove(_layer));
}
