// PC 판 애드온(Lua — CdsHelper.Game/UI/AddOns)의 자리. 안드로이드판은 애드온을 싣지 않는다(사용자 결정: 애드온 · Lua 빼기).
// 게임 코드가 사건마다 알리는 자리(Fire)와 개발 창이 짚는 자리만 같은 이름으로 두고, 아무 일도 하지 않는다.
namespace CdsHelper.Game.UI.AddOns
{
    public static class AddOnHost
    {
        /// <summary>애드온 폴더(PC 판 이름 그대로) — 안드로이드는 읽지 않는다.</summary>
        public static string Root => CdsHelper.Support.Local.Helpers.UserDataFolder.Combine("AddOns");

        public static IReadOnlyList<object> Loaded => [];
        public static void Reload() { }
        public static void Fire(string name, params object?[] args) { }
        public static void SetEnabled(string name, bool enabled) { }
        public static void SetVisible(bool visible) { }
        public static void Relayout() { }
    }
}
