using Android.App;
using Android.Widget;
using System.Windows;
using Android.Content;
using Android.Graphics;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CostaDelSol.Droid.Game;
using CostaDelSol.Droid.Input;
using CostaDelSol.Droid.Ui;
using MotionEventActions = Android.Views.MotionEventActions;
using Col = CostaDelSol.Droid.Ui.TableWindow.Column;

// 도시 화면 · 시설 차림표가 부르는 나머지 창들. 이름 · 인자 · 결과는 PC 판과 같다.
// (예전에는 아직 옮기지 않은 화면을 NotYet.Tell 로 알렸다 — 지금은 다 옮겨 부르는 곳이 없다.)
// 지어내지 않고 「아직 옮기지 않은 화면」이라고 알린다 — 다음 묶음에서 원본대로 옮긴다.

namespace CostaDelSol.Droid.Game
{
    internal static class NotYet
    {
        public static void Tell(string what) =>
            CdsHelper.Game.UI.Views.ConfirmDialog.Tell(Window.Screen, $"{what} 화면은 아직 옮기지 않았습니다. 다음 묶음에서 원본대로 옮깁니다.");

        /// <summary>창 하나를 얹고 그 창이 끝을 낼 때까지 기다린다.</summary>
        public static T Open<T>(Func<Action<T>, ChoicePanel.IGameWindow> make) =>
            GameThread.Wait<T>(reply =>
            {
                ChoicePanel.Layer layer = null!;
                var window = make(value => { GameThread.Panel.Remove(layer); reply(value); });
                layer = new ChoicePanel.Layer { Kind = ChoicePanel.Kind.Custom, Custom = window };
                GameThread.Panel.Push(layer);
            });

        /// <summary>정보 판(글 줄들 + 단추) 하나를 띄우고 닫힐 때까지 기다린다.</summary>
        public static void Sheet(string title, IReadOnlyList<string> lines, Bitmap? face = null, float width = 402,
                                 float height = 0, Color? board = null)
        {
            var body = new List<SheetWindow.Line>();
            if (title.Length > 0) body.Add(new SheetWindow.Line(title, GameFont.WhiteColor));
            body.AddRange(lines.Select(l => new SheetWindow.Line(l, board == null ? GameFont.WhiteColor : GameFont.BlackColor)));
            float h = height > 0 ? height : Math.Max(face != null ? 100 : 0, body.Count * GameArt.TextHeight) + 8;
            Open<bool>(done => new SheetWindow(board ?? WindowArt.Back, board == null ? WindowArt.CountLine : SheetWindow.SteelEdge,
                                               width, h, face, [], body, [("취소", () => done(true))], () => done(true)));
        }
    }

    /// <summary>
    /// 그림 한 장(발견물 · 사건 · 게임 오버) — 판 위쪽에 서고, 누르거나 키를 치면 닫힌다(<c>DiscoveryDialog.Still</c>).
    /// </summary>
    internal sealed class StillWindow(Bitmap art, bool closeOnTouch, Action close, bool up = true) : ChoicePanel.IGameWindow
    {
        private const float Frame = 4;
        private static readonly Paint Pixel = new() { FilterBitmap = false, AntiAlias = false };
        public float Width => art.Width + Frame * 2;
        // 알림창이 밑에 붙을 자리를 남기려고 위로 올린다.
        public float Height => up ? (art.Height + Frame * 2) * 2 + 8 : art.Height + Frame * 2;

        public void Draw(Context context, Canvas canvas, float x, float y, float s, bool top)
        {
            WindowArt.Rect(canvas, x, y, Width, art.Height + Frame * 2, WindowArt.Edge, s);
            WindowArt.Edges(canvas, x + 1, y + 1, Width - 2, art.Height + Frame * 2 - 2, GameArt.MenuEdge, 1, s);
            canvas.DrawBitmap(art, null, new RectF((x + Frame) * s, (y + Frame) * s, (x + Frame + art.Width) * s, (y + Frame + art.Height) * s), Pixel);
        }

        public void Key(GameKey key) { if (closeOnTouch) close(); }
        public void Touch(MotionEventActions action, float x, float y) { if (closeOnTouch && action == MotionEventActions.Up) close(); }
        public void Cancel() { if (closeOnTouch) close(); }
    }
}

namespace CdsHelper.Game.UI.Views
{





















}
