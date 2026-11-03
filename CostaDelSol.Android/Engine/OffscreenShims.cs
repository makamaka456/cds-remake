// PC 판 CdsHelper.Game/UI/Testing/OffscreenHarness.cs 의 자리 — 원본 제작자의 화면 밖 시험 틀이다.
// 그 틀은 Win32 창 손잡이(HWND)를 써서 안드로이드에서는 돌 수 없다. 설정 창이 부르는 값만 같은 이름으로 둔다.
// (안드로이드의 화면 밖 시험은 devtools/sim 의 헤드리스 시뮬레이터가 맡는다.)
namespace CdsHelper.Game.UI.Testing
{
    internal static class OffscreenHarness
    {
        /// <summary>시험 창을 화면 밖으로 밀어 두는 자리(PC 판 값 그대로).</summary>
        public const double OffscreenLeft = -8000, OffscreenTop = -8000;
    }
}

namespace CdsHelper.Game.UI.Views
{
    /// <summary>개발 창 「창 주인 보기」(PC 판 WindowTreeWindow — Win32 창 나무를 읽는다). 안드로이드에는 창 나무가 없다.</summary>
    internal static class WindowTreeWindow
    {
        public static void Open() =>
            NoticeDialog.Show(System.Windows.Window.Screen, "안드로이드판에는 없는 개발 도구입니다(PC 창 나무 보기).");
    }
}
