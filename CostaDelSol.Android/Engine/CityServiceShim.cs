using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 도구 앱의 도시 서비스 자리 — PC 판은 도시 DB(EF)를 쓰지만, 세이브 읽개(SaveDataService)가 부르는 것은
/// 도시 번호 → 이름 하나뿐이라 그것만 같은 셈으로 둔다.
/// </summary>
public sealed class CityService
{
    public string GetCityName(byte index, IEnumerable<City> cities)
    {
        if (index == 255) return "함대소속";
        var city = cities.FirstOrDefault(c => c.Id == index);
        return city?.Name ?? $"미확인({index})";
    }
}
