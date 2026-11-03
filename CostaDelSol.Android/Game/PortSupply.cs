using Android.App;
using Android.Widget;
using CdsHelper.Support.Local.Models;

namespace CostaDelSol.Droid.Game;

/// <summary>
/// 항구 보급 — 식량과 물을 실을 수 있는 만큼, 돈이 닿는 만큼 채운다.
/// </summary>
/// <remarks>
/// PC 판 보급 화면(<c>SupplyDialog</c>)의 「자동」 셈을 그대로 옮겼다. 짐칸(통 수)과 적재량(무게)
/// 가운데 빡빡한 쪽까지 식량·물을 같은 통 수로 채우고, 돈이 모자라면 적은 쪽부터 맞춘 뒤 반씩 나눈다.
/// 시세는 아직 도시마다 따지지 않고 100(기준가)으로 셈한다.
/// </remarks>
public static class PortSupply
{
    public const int BaseRate = 100;

    /// <returns>더 실은 (식량, 물) 통 수와 치른 돈.</returns>
    public static (int Food, int Water, int Cost) Fill(Player player, int rate = BaseRate)
    {
        var food = Supply.Of(SupplyKind.Food);
        var water = Supply.Of(SupplyKind.Water);
        var material = Supply.Of(SupplyKind.Material);
        var ammo = Supply.Of(SupplyKind.Ammo);

        int haveFood = player.SupplyOf(SupplyKind.Food);
        int haveWater = player.SupplyOf(SupplyKind.Water);
        int haveMaterial = player.SupplyOf(SupplyKind.Material);
        int haveAmmo = player.SupplyOf(SupplyKind.Ammo);

        int room = (player.Capacity - player.CargoCount - haveMaterial - haveAmmo) / 2;
        int free = player.Tonnage - player.GunWeight - player.CargoWeight
                   - haveMaterial * material.UnitWeight - haveAmmo * ammo.UnitWeight;
        int byWeight = free / (food.UnitWeight + water.UnitWeight);
        int pair = Math.Max(0, Math.Min(room, byWeight));

        int foodPrice = food.PriceAt(rate), waterPrice = water.PriceAt(rate);
        int foodTo = pair, waterTo = pair;
        int gold = player.Gold;
        int cost = Math.Max(0, pair - haveFood) * foodPrice + Math.Max(0, pair - haveWater) * waterPrice;

        if (cost > gold)
        {
            int gap = Math.Abs(haveFood - haveWater);
            int foodMore = 0, waterMore = 0;
            if (gap > 0 && haveFood < haveWater)
            {
                if (foodPrice * gap > gold) foodMore = gold / foodPrice;
                else
                {
                    int each = (gold - foodPrice * gap) / (foodPrice + waterPrice);
                    waterMore = each;
                    foodMore = each + gap;
                }
            }
            else if (gap > 0)
            {
                if (waterPrice * gap > gold) waterMore = gold / waterPrice;
                else
                {
                    int each = (gold - waterPrice * gap) / (foodPrice + waterPrice);
                    foodMore = each;
                    waterMore = each + gap;
                }
            }
            else
            {
                foodMore = waterMore = gold / (foodPrice + waterPrice);
            }

            int left = gold - foodMore * foodPrice - waterMore * waterPrice;
            if (left > 0) foodMore += left / foodPrice;

            foodTo = haveFood + foodMore;
            waterTo = haveWater + waterMore;
        }

        int addFood = Math.Max(0, foodTo - haveFood);
        int addWater = Math.Max(0, waterTo - haveWater);
        int pay = addFood * foodPrice + addWater * waterPrice;
        if (pay <= 0 || !player.Pay(pay)) return (0, 0, 0);

        player.AddSupply(SupplyKind.Food, addFood);
        player.AddSupply(SupplyKind.Water, addWater);
        return (addFood, addWater, pay);
    }
}

/// <summary>
/// 여관 숙박 — 한 달 선불, 값은 문화권으로 갈린다. 묵으면 서른 날이 가고 피로가 날마다 1 씩 풀린다.
/// </summary>
/// <remarks>
/// PC 판 <c>Engine/Inn/Lodging</c> 의 값표와 <c>Stay</c> 를 옮겼다. 그쪽은 시세(<c>MarketRates</c>)를
/// 거치는데 그것이 게임 판 전체(<c>Game</c>)에 묶여 있어 끌어오지 못했다 — 지금 시세는 다 100 이라 값은 같다.
/// </remarks>
public static class PortInn
{
    private static readonly int[] MonthlyByCulture = [150, 120, 124, 90, 15, 60, 90, 30, 30, 60, 30];
    private const int Unknown = 90;
    public const int StayDays = 30;

    public static int PriceAt(CdsHelper.Game.Local.Helpers.CityExeTable? cities, int cityId)
    {
        int culture = cities?.CultureOf(cityId) ?? -1;
        return Math.Max(1, culture >= 0 && culture < MonthlyByCulture.Length ? MonthlyByCulture[culture] : Unknown);
    }

    /// <summary>묵는다. 돈이 모자라면 false 이고 아무것도 안 한다.</summary>
    public static bool Stay(Player player, int price)
    {
        if (!player.CanAfford(price)) return false;
        player.Pay(price);
        player.AdvanceDays(StayDays);   // 날을 넘기며 피로 -1/일, 사기 +3/일(Player.Recover)
        return true;
    }
}
