using Android.App;
using Android.Widget;
using Android.Content;
using Android.Views;

namespace CostaDelSol.Droid.Ui;

/// <summary>화면 비율 — 메뉴에서 고른다.</summary>
public enum ScreenRatio { FourThree, SixteenNine }

/// <summary>
/// 게임 화면 틀 — 고른 비율(4:3 · 16:9)로 화면 가운데에 선다. 남는 양옆(또는 위아래)은 검게 남고,
/// 화면 패드 단추가 그 자리에 앉아 게임 화면을 덜 가린다.
/// </summary>
/// <remarks>원본은 640x480(4:3)이다. 원본 그림으로 짓는 화면(타이틀·도시)은 이 틀의 높이에 맞춰 키운다.</remarks>
public sealed class AspectFrame : FrameLayout
{
    private const string PrefsName = "display";

    public ScreenRatio Ratio { get; private set; } = ScreenRatio.FourThree;

    public AspectFrame(Context context) : base(context)
    {
        var saved = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.GetString("ratio", null);
        if (Enum.TryParse<ScreenRatio>(saved, out var ratio)) Ratio = ratio;
    }

    public static string Label(ScreenRatio ratio) => ratio == ScreenRatio.FourThree ? "4:3 (원본)" : "16:9";

    public void SetRatio(ScreenRatio ratio)
    {
        Ratio = ratio;
        Context!.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.Edit()!.PutString("ratio", ratio.ToString())!.Apply();
        RequestLayout();
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        int w = MeasureSpec.GetSize(widthMeasureSpec), h = MeasureSpec.GetSize(heightMeasureSpec);
        float want = Ratio == ScreenRatio.FourThree ? 4f / 3f : 16f / 9f;
        int fitW = w, fitH = h;
        if (h > 0 && w / (float)h > want) fitW = (int)(h * want);   // 가로가 넘치면 양옆을 비운다
        else if (w > 0) fitH = (int)(w / want);                     // 세로가 넘치면 위아래를 비운다
        base.OnMeasure(MeasureSpec.MakeMeasureSpec(fitW, MeasureSpecMode.Exactly),
                       MeasureSpec.MakeMeasureSpec(fitH, MeasureSpecMode.Exactly));
    }
}
