// 안드로이드판에서 뺀 PC 기능의 빈 대용품.
//  · 커스텀 팩(CustomPacks) — 이번 판에서는 뺐다. 늘 「팩 없음」으로 답한다(표는 기본 자리에서만 읽는다).
//  · 그래픽 정보(GpuInfo) — 오류 보고에 그래픽 장치 이름을 붙이는 것. 네트워크 보고를 뺐으므로 빈 글.
// PC 공용 코드가 이 이름을 부르므로 같은 이름 · 같은 꼴로 둔다.

namespace CdsHelper.Game.Local.Helpers
{
    public static class CustomPacks
    {
        public const int MaxNameLength = 40;

        /// <summary>켠 팩 — 안드로이드는 늘 없음.</summary>
        public static string? Active => null;

#pragma warning disable CS0067
        public static event Action? Changed;
#pragma warning restore CS0067

        public static IReadOnlyList<string> Names() => [];

        public static bool Exists(string name) => false;

        /// <summary>팩을 켜지 않는다 — 건너뛴 표 목록은 늘 비었다.</summary>
        public static IReadOnlyList<string> Use(string? name) => [];

        public static string Fingerprint(string? name) => "";

        /// <summary>세이브가 팩을 켜고 시작했어도 묻지 않는다 — 안드로이드에는 팩이 없어 고를 것이 없다.</summary>
        public static string? MismatchQuestion(string? savedPack, string? savedPrint) => null;
    }
}

namespace CdsHelper.Game.UI.Views
{
    public static class GpuInfo
    {
        public static string Describe(Exception ex) => "";
    }
}
