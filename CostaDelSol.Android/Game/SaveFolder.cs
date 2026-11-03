using Android.App;
using Android.Widget;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using CdsHelper.Game.Engine;
using AndroidEnvironment = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;

namespace CostaDelSol.Droid.Game;

/// <summary>
/// 세이브 폴더 — 기본은 <c>내장 저장소/CostaDelSol</c>, 메뉴에서 직접 고를 수도 있다.
/// </summary>
/// <remarks>
/// 세이브 파일(<c>SAVEDATA.CDS</c> · <c>AUTOSAVE.CDS</c>)은 PC 판과 같은 코드(<see cref="GameSave"/>)로 적으므로
/// 윈도우의 <c>%APPDATA%\CdsHelper</c> 에 있는 파일과 서로 옮겨 쓸 수 있다.
///
/// 앱 폴더 밖에 적으려면 <b>「모든 파일 접근」 권한</b>이 있어야 한다(안드로이드 11 부터
/// <c>MANAGE_EXTERNAL_STORAGE</c>, 그 앞은 <c>WRITE_EXTERNAL_STORAGE</c>). 권한이 없으면 앱 전용 폴더에
/// 적는다 — 그 폴더는 앱을 지우면 같이 지워진다.
/// </remarks>
public static class SaveFolder
{
    private const string PrefsName = "save-folder";
    public const string DefaultName = "CostaDelSol";
    public const int RequestLegacyPermission = 4101;
    public const int RequestPickFolder = 4102;

    /// <summary>내장 저장소 맨 위의 <c>CostaDelSol</c>.</summary>
    public static string DefaultPath =>
        System.IO.Path.Combine(AndroidEnvironment.ExternalStorageDirectory!.AbsolutePath, DefaultName);

    /// <summary>권한이 없을 때 쓰는 앱 전용 폴더.</summary>
    public static string FallbackPath(Context context) =>
        System.IO.Path.Combine(context.GetExternalFilesDir(null)?.AbsolutePath ?? context.FilesDir!.AbsolutePath, "saves");

    public static bool HasAccess(Context context)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R) return AndroidEnvironment.IsExternalStorageManager;
        return context.CheckSelfPermission(Android.Manifest.Permission.WriteExternalStorage) == Permission.Granted;
    }

    /// <summary>사람이 고른 폴더. 고른 적 없으면 null(기본 폴더).</summary>
    public static string? Chosen(Context context) =>
        context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.GetString("path", null);

    public static void Choose(Context context, string? path) =>
        context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.Edit()!.PutString("path", path)!.Apply();

    /// <summary>지금 쓸 폴더를 정해 <see cref="GameSave.FolderOverride"/> 에 걸고 돌려준다.</summary>
    public static string Apply(Context context)
    {
        string folder = HasAccess(context) ? Chosen(context) ?? DefaultPath : FallbackPath(context);
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            folder = FallbackPath(context);
            Directory.CreateDirectory(folder);
        }
        GameSave.FolderOverride = folder;
        return folder;
    }

    /// <summary>
    /// 권한을 청한다. 안드로이드 11 부터는 설정 화면(「모든 파일 접근 허용」)으로 보낸다 — 돌아오면
    /// <see cref="Apply"/> 를 다시 부른다(OnResume).
    /// </summary>
    public static void RequestAccess(Activity activity)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            try
            {
                var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission,
                                        AndroidUri.Parse("package:" + activity.PackageName));
                activity.StartActivity(intent);
            }
            catch (ActivityNotFoundException)
            {
                activity.StartActivity(new Intent(Settings.ActionManageAllFilesAccessPermission));
            }
            return;
        }
        activity.RequestPermissions([Android.Manifest.Permission.WriteExternalStorage,
                                     Android.Manifest.Permission.ReadExternalStorage], RequestLegacyPermission);
    }

    /// <summary>안드로이드 폴더 고르기 창을 연다. 고른 결과는 <see cref="FromPicked"/> 로 경로가 된다.</summary>
    public static void Pick(Activity activity)
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        activity.StartActivityForResult(intent, RequestPickFolder);
    }

    /// <summary>
    /// 폴더 고르기 창이 돌려준 주소를 파일 경로로. 「모든 파일 접근」이 있으면 그 경로에 바로 쓸 수 있다.
    /// <c>primary:게임/세이브</c> → <c>/storage/emulated/0/게임/세이브</c>,
    /// <c>1234-ABCD:세이브</c>(SD 카드) → <c>/storage/1234-ABCD/세이브</c>.
    /// </summary>
    public static string? FromPicked(AndroidUri? uri)
    {
        if (uri == null) return null;
        string? id = DocumentsContract.GetTreeDocumentId(uri);
        if (string.IsNullOrEmpty(id)) return null;
        int colon = id.IndexOf(':');
        if (colon < 0) return null;
        string volume = id[..colon], rest = id[(colon + 1)..];
        string root = volume.Equals("primary", StringComparison.OrdinalIgnoreCase)
            ? AndroidEnvironment.ExternalStorageDirectory!.AbsolutePath
            : "/storage/" + volume;
        return rest.Length == 0 ? root : System.IO.Path.Combine(root, rest);
    }
}
