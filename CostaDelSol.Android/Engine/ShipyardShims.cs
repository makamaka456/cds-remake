using Android.App;
using Android.Widget;
using System.Windows;
using Android.Graphics;
using MotionEventActions = Android.Views.MotionEventActions;
using SoftInput = Android.Views.SoftInput;
using CdsHelper.Support.Local.Models;
using CostaDelSol.Droid.Game;
using CostaDelSol.Droid.Input;
using CostaDelSol.Droid.Ui;

// 조선소 창들과 글자 입력 · 하루 넘김 — PC 판 ShipSellDialog · ShipRepairDialog · HullSelectDialog · ShipNameDialog ·
// TextInputDialog · DayPass 와 같은 이름 · 인자 · 결과. 줄 글(칸 수로 맞춘 것)과 머리 글은 PC 판 그대로다.

namespace CdsHelper.Game.UI.Views
{


    internal static class Ledger
    {
        public static IReadOnlyList<int>? Open(string title, string head, List<(string, bool)> rows,
                                               Func<IReadOnlyCollection<int>, string> total) =>
            GameThread.Wait<IReadOnlyList<int>?>(reply =>
            {
                ChoicePanel.Layer layer = null!;
                var window = new LedgerWindow(title, head, rows, total, done => { GameThread.Panel.Remove(layer); reply(done); });
                layer = new ChoicePanel.Layer { Kind = ChoicePanel.Kind.Custom, Custom = window };
                GameThread.Panel.Push(layer);
            });
    }


    public static class ShipNameDialog
    {
        public static string? Ask(Window owner, string current, bool mustName = false) =>
            GameThread.Wait<string?>(reply =>
            {
                ChoicePanel.Layer layer = null!;
                var window = new NameWindow(current, ShipNames.All, mustName,
                    done => { GameThread.Panel.Remove(layer); reply(done); },
                    (now, typed) => TextInputDialog.OpenOnUi(now, ShipNames.MaxLength, "문자입력", typed));
                layer = new ChoicePanel.Layer { Kind = ChoicePanel.Kind.Custom, Custom = window };
                GameThread.Panel.Push(layer);
            });
    }

    /// <summary>
    /// 글자 입력(<c>TextInputDialog</c>). 원본은 자판 그림 창이지만 안드로이드에서는 기기 글자판(한글 입력기)으로 받는다 —
    /// 제목 · 글자 수 한도 · 결과(중단이면 null)는 같다.
    /// </summary>
    public static class TextInputDialog
    {
        public static string? Ask(Window owner, string start, int maxLength, string caption = "문자입력") =>
            GameThread.Wait<string?>(reply => OpenOnUi(start, maxLength, caption, reply));

        internal static void OpenOnUi(string start, int maxLength, string caption, Action<string?> reply)
        {
            var activity = GameThread.Activity;
            if (activity == null) { reply(null); return; }
            var edit = new EditText(activity) { Text = start };
            edit.SetSingleLine(true);
            edit.SetFilters([new Android.Text.InputFilterLengthFilter(maxLength)]);
            edit.SetSelection(edit.Text?.Length ?? 0);
            bool answered = false;
            var dialog = new AlertDialog.Builder(activity)
                .SetTitle(caption)!
                .SetView(edit)!
                .SetPositiveButton("결정", (_, _) => { answered = true; reply(edit.Text?.Trim()); })!
                .SetNegativeButton("중단", (_, _) => { answered = true; reply(null); })!
                .Create()!;
            dialog.DismissEvent += (_, _) => { if (!answered) reply(null); };
            dialog.Window?.SetSoftInputMode(SoftInput.StateVisible);
            dialog.Show();
        }
    }

}
