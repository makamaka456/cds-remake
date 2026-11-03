using Android.App;
using Android.Widget;
using System.Text.Json;
using Android.Content;
using Android.Graphics;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CostaDelSol.Droid.Game;

/// <summary>
/// 한 판에 쓰는 자료 — 지도, 타일 그림, 엔진 표, 배 그림, 도시 이름. 켤 때 한 번 올린다.
/// </summary>
/// <remarks>
/// 원본은 APK 의 Assets 에 있다. 엔진 표(exe-tables)는 PC 판 읽개가 파일 경로로 읽으므로
/// 앱 전용 폴더에 꺼내 두고 <see cref="TableCache.FolderOverride"/> 로 그 자리를 일러 준다.
/// </remarks>
public sealed class GameData
{
    public required byte[] World { get; init; }
    public required OceanTiles Ocean { get; init; }
    public WindTable? Wind { get; init; }
    public TerrainTable? Terrain { get; init; }
    public SailTable? Sails { get; init; }
    public CityExeTable? Cities { get; init; }
    public required string[] CityNames { get; init; }

    /// <summary>배 그림 — [그림벌][방향] 이 48x48 RGBA 바이트. 방향은 반시계 8방위(0 = 북).</summary>
    public required byte[]?[][] ShipFrames { get; init; }

    /// <summary>못 연 표들. 비어 있으면 다 열렸다.</summary>
    public required List<string> Problems { get; init; }

    public const int SpriteSize = 48;
    public const int Skins = 4;

    public string CityName(int id) => id >= 0 && id < CityNames.Length && CityNames[id].Length > 0
        ? CityNames[id] : $"도시 {id}";

    public int CityIdOf(string name) => Array.IndexOf(CityNames, name);

    /// <summary>그 날짜에 지도에 서 있는 도시인지(<c>Game.CityStanding</c>).</summary>
    public bool CityStanding(int id, Player player) =>
        CityFounding.Standing(id, player.Date, player.ScriptedCities);

    /// <summary>뱃머리(16방위)와 선체 그림벌에 맞는 그림. 없으면 다른 벌에서 찾는다.</summary>
    public byte[]? ShipFrame(int skin, int heading16)
    {
        int dir = (heading16 & 0xF) >> 1;
        skin = Math.Clamp(skin, 0, Skins - 1);
        return ShipFrames[skin][dir] ?? ShipFrames.Select(s => s[dir]).FirstOrDefault(f => f != null);
    }

    public static GameData Load(Context context, Action<string> progress)
    {
        var assets = context.Assets!;
        var problems = new List<string>();
        string files = context.FilesDir!.AbsolutePath;

        // 원본 자료를 앱 폴더(GameRoot)로 꺼내고 그 자리를 「실행 파일 폴더」로 건다. PC 판 읽개들은 실행 파일 옆의
        // asset/ · exe-tables/ · *.CDSX 를 찾으므로(AppContext.BaseDirectory, 43곳) 고치지 않고 그대로 돈다.
        string root = GameRoot(context);
        Unpack(context, root, progress);
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", root + "/");
        TableCache.FolderOverride = System.IO.Path.Combine(root, "exe-tables");

        progress("지도를 읽는 중…");
        byte[] world = File.ReadAllBytes(System.IO.Path.Combine(root, "world.cdsx"));
        if (world.Length != WorldMapImage.DataSize)
            throw new InvalidDataException($"WORLD 크기가 다릅니다 ({world.Length})");
        var ocean = OceanTiles.LoadFromDirectory(root)
                    ?? throw new InvalidDataException(OceanTiles.LastError);

        progress("표를 여는 중…");
        var wind = WindTable.Open("");
        if (wind == null) problems.Add("바람표: " + WindTable.LastError);
        var terrain = TerrainTable.Open("");
        if (terrain == null) problems.Add("지형표: " + TerrainTable.LastError);
        var sails = SailTable.Open("");
        if (sails == null) problems.Add("돛효율표: " + SailTable.LastError);
        var cities = CityExeTable.Open("");
        if (cities == null) problems.Add("도시표: " + CityExeTable.LastError);

        progress("배 그림을 읽는 중…");
        var frames = new byte[]?[Skins][];
        for (int s = 0; s < Skins; s++)
        {
            frames[s] = new byte[]?[8];
            for (int d = 0; d < 8; d++) frames[s][d] = ReadSprite(context, $"ship-g{s}/ship_{d}.png");
        }

        var cityNames = ReadCityNames(context);
        // PC 판 CityNames(작위 · 귀양 · 시나리오 글이 쓴다)는 실행 폴더의 cities.json 을 읽는다 — 안드로이드는 APK 안 것을 건넨다.
        CdsHelper.Support.Local.Helpers.CityNames.Names = cityNames;

        return new GameData
        {
            World = world,
            Ocean = ocean,
            Wind = wind,
            Terrain = terrain,
            Sails = sails,
            Cities = cities,
            CityNames = cityNames,
            ShipFrames = frames,
            Problems = problems,
        };
    }

    /// <summary>원본 자료를 꺼내 두는 앱 폴더 — PC 판의 실행 파일 폴더 자리다.</summary>
    public static string GameRoot(Context context) => System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "game");

    /// <summary>
    /// APK 의 원본 자료를 앱 폴더로 꺼낸다 — 원본 CDSX 스무 개(game/), asset/ 전부, exe-tables/.
    /// 앱을 새로 깔거나 바꿨을 때만 다시 꺼낸다(설치 시각을 적어 둔다). 처음 한 번 약 70MB 를 쓴다.
    /// </summary>
    private static void Unpack(Context context, string root, Action<string> progress)
    {
        var info = context.PackageManager!.GetPackageInfo(context.PackageName!, 0)!;
        string stamp = info.LastUpdateTime.ToString();
        string marker = System.IO.Path.Combine(root, ".unpacked");
        if (File.Exists(marker) && File.ReadAllText(marker) == stamp) return;

        Directory.CreateDirectory(root);
        var assets = context.Assets!;
        var all = new List<(string From, string To)>();
        foreach (string name in assets.List("game") ?? [])
            all.Add(("game/" + name, System.IO.Path.Combine(root, name)));
        foreach (string name in assets.List("exe-tables") ?? [])
            all.Add(("exe-tables/" + name, System.IO.Path.Combine(root, "exe-tables", name)));
        Walk(assets, "asset", path => all.Add((path, System.IO.Path.Combine(root, path))));
        // PC 판이 실행 파일 옆에서 읽는 json(도시 · 후원자 · 힌트 · 아이템 · 책 · 발견물 · 인물표 · 사건 · 이야기 대본).
        foreach (string name in assets.List("root") ?? [])
            all.Add(("root/" + name, System.IO.Path.Combine(root, name)));

        for (int i = 0; i < all.Count; i++)
        {
            var (from, to) = all[i];
            if (i % 20 == 0) progress($"원본 자료를 꺼내는 중… ({i * 100 / Math.Max(1, all.Count)}%)");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
            using var input = assets.Open(from);
            using var output = File.Create(to + ".part");
            input.CopyTo(output);
            output.Close();
            File.Move(to + ".part", to, overwrite: true);
        }
        File.WriteAllText(marker, stamp);
    }

    /// <summary>APK 안의 폴더를 훑어 파일마다 부른다(빈 목록이면 파일이다).</summary>
    private static void Walk(Android.Content.Res.AssetManager assets, string path, Action<string> file)
    {
        var children = assets.List(path) ?? [];
        if (children.Length == 0) { file(path); return; }
        foreach (string child in children) Walk(assets, path + "/" + child, file);
    }

    private static byte[] Read(Context context, string name)
    {
        using var input = context.Assets!.Open(name);
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>PNG 한 장을 48x48 RGBA 바이트로. 없거나 크기가 다르면 null.</summary>
    private static byte[]? ReadSprite(Context context, string name)
    {
        try
        {
            using var input = context.Assets!.Open(name);
            using var bitmap = BitmapFactory.DecodeStream(input);
            if (bitmap == null || bitmap.Width != SpriteSize || bitmap.Height != SpriteSize) return null;
            var argb = new int[SpriteSize * SpriteSize];
            bitmap.GetPixels(argb, 0, SpriteSize, 0, 0, SpriteSize, SpriteSize);
            var rgba = new byte[argb.Length * 4];
            for (int i = 0; i < argb.Length; i++)
            {
                int c = argb[i];
                rgba[i * 4] = (byte)(c >> 16);
                rgba[i * 4 + 1] = (byte)(c >> 8);
                rgba[i * 4 + 2] = (byte)c;
                rgba[i * 4 + 3] = (byte)(c >>> 24);
            }
            return rgba;
        }
        catch (Java.IO.IOException)
        {
            return null;
        }
    }

    /// <summary>cities.json(PC 판 CdsHelper/cities.json)에서 번호 → 이름.</summary>
    private static string[] ReadCityNames(Context context)
    {
        var names = new string[CityExeTable.Count];
        Array.Fill(names, "");
        byte[] json = Read(context, "cities.json");
        int skip = json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF ? 3 : 0;   // BOM
        using var doc = JsonDocument.Parse(json.AsMemory(skip));
        foreach (var city in doc.RootElement.EnumerateArray())
        {
            if (!city.TryGetProperty("id", out var idEl) || !city.TryGetProperty("name", out var nameEl)) continue;
            int id = idEl.GetInt32();
            if (id >= 0 && id < names.Length) names[id] = nameEl.GetString() ?? "";
        }
        return names;
    }
}
