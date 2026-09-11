using System;
using System.Collections.Generic;
using XingGame.Core.Events;
using XingGame.Core.Time;
using XingGame.Systems.Farming;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 种植系统（M1-5）：把耕地接到时间与背包上。这里守的是「谁在什么时候驱动耕地」——
/// 时间系统只报时、不做效果（ADR-006），所以「下雨算不算浇过水」这类规则只有本系统一份。
/// </summary>
/// <remarks>
/// 时间用替身（本切片不验时间系统），事件总线、耕地、背包、作物表都用真的——
/// 真的事件总线才能验出「订阅了没」「退订了没」，而这正是本切片最容易错的地方。
/// </remarks>
public class FarmingSystemTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "seed_parsnip",    "name": "防风草种子", "description": "春季播种。", "category": "Seed", "maxStack": 999, "buyPrice": 20, "sellPrice": 0 },
        { "id": "crop_parsnip",    "name": "防风草",     "description": "春季作物。", "category": "Crop", "maxStack": 999, "buyPrice": 0,  "sellPrice": 35 },
        { "id": "seed_strawberry", "name": "草莓种子",   "description": "春季播种。", "category": "Seed", "maxStack": 999, "buyPrice": 100, "sellPrice": 0 },
        { "id": "crop_strawberry", "name": "草莓",       "description": "春季作物。", "category": "Crop", "maxStack": 999, "buyPrice": 0,  "sellPrice": 120 },

        { "id": "tool_hoe",           "name": "锄头",   "description": "开垦耕地。", "category": "Tool", "maxStack": 1, "buyPrice": 0, "sellPrice": 0 },
        { "id": "tool_watering_can",  "name": "洒水壶", "description": "给耕地浇水。", "category": "Tool", "maxStack": 1, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string CropsJson = """
    {
      "crops": [
        { "seedId": "seed_parsnip",    "cropId": "crop_parsnip",    "growthDays": 4, "regrowable": false },
        { "seedId": "seed_strawberry", "cropId": "crop_strawberry", "growthDays": 8, "regrowable": true }
      ]
    }
    """;

    private const string Parsnip = "seed_parsnip";
    private const string Strawberry = "seed_strawberry";

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);
    private static readonly CropTable Crops = CropTable.FromJson(CropsJson, Items);

    private static readonly TileCoord Tile = new(0, 0);
    private static readonly TileCoord Other = new(3, 2);

    private readonly EventBus _bus = new();
    private readonly FakeTimeService _time = new();
    private readonly Farmland _farmland = new(Crops);
    private readonly Inventory _inventory = new(Items, slotCount: 4);

    private FarmingSystem NewSystem() => new(_bus, _time, Crops, _farmland, _inventory);

    private static GameTime At(int day) => new(1, Season.Spring, day, GameTime.FirstHour, 0);

    /// <summary>第 day 天的日界事件（6:00）。</summary>
    private static DayStarted DayStartedOf(int day) => new(At(day));

    // ——— 推进一天 ———

    [Fact]
    public void 发_DayStarted_浇过水的作物长一天_浇水状态清空()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _farmland.TryPlant(Tile, Parsnip);
        _farmland.TryWater(Tile);

        _bus.Publish(DayStartedOf(2));

        Assert.Equal(1, _farmland.DaysGrown(Tile));
        Assert.Equal(SoilState.Tilled, _farmland.StateOf(Tile));
    }

    [Fact]
    public void 发_DayStarted_没浇水的作物不长()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _farmland.TryPlant(Tile, Parsnip);

        _bus.Publish(DayStartedOf(2));

        Assert.Equal(0, _farmland.DaysGrown(Tile));
        Assert.True(_farmland.HasCrop(Tile));
    }

    [Theory]
    // §3.3：雨天与暴风雨都「自动浇水」，其余天气不浇
    [InlineData(Weather.Rainy, 1)]
    [InlineData(Weather.Storm, 1)]
    [InlineData(Weather.Sunny, 0)]
    [InlineData(Weather.Windy, 0)]
    [InlineData(Weather.Snowy, 0)]
    [InlineData(Weather.Foggy, 0)]
    public void 发_DayStarted_雨天自动算浇水(Weather weather, int expectedDays)
    {
        using FarmingSystem system = NewSystem();
        _time.Weather = weather;
        _farmland.TryTill(Tile);
        _farmland.TryPlant(Tile, Parsnip);   // 刻意不浇水

        _bus.Publish(DayStartedOf(2));

        Assert.Equal(expectedDays, _farmland.DaysGrown(Tile));
    }

    [Fact]
    public void 发_DayStarted_只长一天_不会因为订阅了两次而长两天()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _farmland.TryPlant(Tile, Parsnip);
        _farmland.TryWater(Tile);

        _bus.Publish(DayStartedOf(2));
        _bus.Publish(DayStartedOf(3));

        Assert.Equal(1, _farmland.DaysGrown(Tile));   // 第二天没浇水，只该长第一天那一次
    }

    // ——— 换季枯萎 ———

    [Fact]
    public void 发_SeasonChanged_作物全部枯萎但开垦状态还在()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _farmland.TryPlant(Tile, Parsnip);
        _farmland.TryTill(Other);
        _farmland.TryPlant(Other, Strawberry);

        _bus.Publish(new SeasonChanged(At(1), Season.Spring));

        Assert.False(_farmland.HasCrop(Tile));
        Assert.False(_farmland.HasCrop(Other));
        Assert.Equal(SoilState.Tilled, _farmland.StateOf(Tile));
        Assert.Equal(SoilState.Tilled, _farmland.StateOf(Other));
    }

    // ——— 退订（ADR-005）———

    [Fact]
    public void Dispose_之后再发_DayStarted_不再有任何反应()
    {
        FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _farmland.TryPlant(Tile, Parsnip);
        _farmland.TryWater(Tile);

        system.Dispose();
        _bus.Publish(DayStartedOf(2));

        Assert.Equal(0, _farmland.DaysGrown(Tile));
        Assert.Equal(SoilState.Watered, _farmland.StateOf(Tile));   // 连浇水状态都不该被清
    }

    [Fact]
    public void Dispose_之后再发_SeasonChanged_不再有任何反应()
    {
        FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _farmland.TryPlant(Tile, Parsnip);

        system.Dispose();
        _bus.Publish(new SeasonChanged(At(1), Season.Spring));

        Assert.True(_farmland.HasCrop(Tile));
    }

    [Fact]
    public void Dispose_可以重复调用()
    {
        FarmingSystem system = NewSystem();

        system.Dispose();
        system.Dispose();   // 桥接层节点在异常路径上重复 Dispose 是常事，不该抛
    }

    // ——— 播种与收获（耕地 ↔ 背包）———

    [Fact]
    public void 播种_消耗背包里的一粒种子()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _inventory.Add(Parsnip, 2);

        Assert.True(system.TryPlant(Tile, Parsnip));

        Assert.True(_farmland.HasCrop(Tile));
        Assert.Equal(1, _inventory.Count(Parsnip));
    }

    [Fact]
    public void 播种_背包里没有这粒种子时失败_且什么都不发生()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);

        Assert.False(system.TryPlant(Tile, Parsnip));

        Assert.False(_farmland.HasCrop(Tile));
        Assert.Equal(0, _inventory.Count(Parsnip));
    }

    [Fact]
    public void 播种_格种不了时_种子不会被白扣()
    {
        using FarmingSystem system = NewSystem();
        _inventory.Add(Parsnip, 1);   // 格还没开垦

        Assert.False(system.TryPlant(Tile, Parsnip));

        Assert.Equal(1, _inventory.Count(Parsnip));
        Assert.False(_farmland.HasCrop(Tile));
    }

    [Fact]
    public void 播种_未知种子抛_KeyNotFoundException()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);

        Assert.Throws<KeyNotFoundException>(() => system.TryPlant(Tile, "seed_ghost"));
    }

    [Fact]
    public void 收获_成熟后作物收进背包_种子不退()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _inventory.Add(Parsnip, 1);
        Assert.True(system.TryPlant(Tile, Parsnip));

        for (int day = 0; day < 4; day++)
        {
            _farmland.TryWater(Tile);
            _bus.Publish(DayStartedOf(day + 2));   // 由事件驱动，走的是系统自己的那条路
        }

        Assert.True(_farmland.IsReadyToHarvest(Tile));
        Assert.True(system.TryHarvest(Tile));

        Assert.Equal(1, _inventory.Count("crop_parsnip"));
        Assert.Equal(0, _inventory.Count(Parsnip));
        Assert.False(_farmland.HasCrop(Tile));
    }

    [Fact]
    public void 收获_没成熟时失败_背包不变()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _farmland.TryPlant(Tile, Parsnip);
        _farmland.TryWater(Tile);
        _bus.Publish(DayStartedOf(2));

        Assert.False(system.TryHarvest(Tile));

        Assert.Equal(0, _inventory.Count("crop_parsnip"));
        Assert.True(_farmland.HasCrop(Tile));
    }

    [Fact]
    public void 收获_可多次收获的作物_收完还能再收一轮()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _farmland.TryPlant(Tile, Strawberry);
        _farmland.TryWater(Tile);

        // 「每次熟了就收、收完接着浇水等下一轮」：16 天正好收两轮，多收或少收都说明重生计时错了
        int harvests = 0;
        for (int day = 0; day < 16; day++)
        {
            _bus.Publish(DayStartedOf(day + 2));
            if (system.TryHarvest(Tile)) harvests++;

            if (_farmland.HasCrop(Tile)) _farmland.TryWater(Tile);
        }

        Assert.Equal(2, harvests);
        Assert.Equal(2, _inventory.Count("crop_strawberry"));
        Assert.True(_farmland.HasCrop(Tile));   // 可多次收获的株还在，没被拔掉
    }

    // ——— UseOn：用选中的物品作用于目标格 ———
    //
    // 这条规则住在这里而不是桥接层（ADR-007）——桥接层里的规则逃过编译器的看管，
    // 而下面每一条都能被逐条钉住。

    [Fact]
    public void UseOn_锄头开垦_未开垦的格变成已开垦()
    {
        using FarmingSystem system = NewSystem();

        Assert.True(system.UseOn(Tile, Items.Get(FarmingSystem.HoeItemId)));
        Assert.Equal(SoilState.Tilled, _farmland.StateOf(Tile));
    }

    [Fact]
    public void UseOn_洒水壶浇水_荒地浇不了()
    {
        using FarmingSystem system = NewSystem();

        Assert.False(system.UseOn(Tile, Items.Get(FarmingSystem.WateringCanItemId)));

        _farmland.TryTill(Tile);

        Assert.True(system.UseOn(Tile, Items.Get(FarmingSystem.WateringCanItemId)));
        Assert.Equal(SoilState.Watered, _farmland.StateOf(Tile));
    }

    [Fact]
    public void UseOn_种子播种_并扣掉一粒()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _inventory.Add(Parsnip, 2);

        Assert.True(system.UseOn(Tile, Items.Get(Parsnip)));

        Assert.True(_farmland.HasCrop(Tile));
        Assert.Equal(1, _inventory.Count(Parsnip));
    }

    [Fact]
    public void UseOn_空手_什么都不发生()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _inventory.Add(Parsnip, 1);

        Assert.False(system.UseOn(Tile, null));

        Assert.Equal(SoilState.Tilled, _farmland.StateOf(Tile));
        Assert.False(_farmland.HasCrop(Tile));
        Assert.Equal(1, _inventory.Count(Parsnip));
    }

    [Fact]
    public void UseOn_拿非种子非工具的物品_返回_false()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);

        Assert.False(system.UseOn(Tile, Items.Get("crop_parsnip")));

        Assert.Equal(SoilState.Tilled, _farmland.StateOf(Tile));
    }

    [Fact]
    public void UseOn_成熟时_手拿锄头也收得到_收获优先于工具()
    {
        using FarmingSystem system = NewSystem();
        _farmland.TryTill(Tile);
        _inventory.Add(Parsnip, 1);
        Assert.True(system.TryPlant(Tile, Parsnip));

        for (int day = 0; day < 4; day++)
        {
            _farmland.TryWater(Tile);
            _bus.Publish(DayStartedOf(day + 2));
        }

        Assert.True(_farmland.IsReadyToHarvest(Tile));

        // 契约：成熟作物优先收获，且任何手持物都能收——玩家不该因为忘了换工具干瞪眼
        Assert.True(system.UseOn(Tile, Items.Get(FarmingSystem.HoeItemId)));

        Assert.Equal(1, _inventory.Count("crop_parsnip"));
        Assert.False(_farmland.HasCrop(Tile));

        // 收获没有顺带把地也锄了或浇了：收完就是「已开垦」，不凭空多出一个免费天数
        Assert.Equal(SoilState.Tilled, _farmland.StateOf(Tile));
    }

    // ——— 构造 ———

    [Fact]
    public void 构造_缺少任何一个依赖都抛()
    {
        Assert.Throws<ArgumentNullException>(() => new FarmingSystem(null!, _time, Crops, _farmland, _inventory));
        Assert.Throws<ArgumentNullException>(() => new FarmingSystem(_bus, null!, Crops, _farmland, _inventory));
        Assert.Throws<ArgumentNullException>(() => new FarmingSystem(_bus, _time, null!, _farmland, _inventory));
        Assert.Throws<ArgumentNullException>(() => new FarmingSystem(_bus, _time, Crops, null!, _inventory));
        Assert.Throws<ArgumentNullException>(() => new FarmingSystem(_bus, _time, Crops, _farmland, null!));
    }

    /// <summary>
    /// 时间替身：本切片不验时间系统，只需要「今天什么天气」可随手改。
    /// 推进类方法直接抛——真被调到说明测试写错了，而不是让它悄悄跑过去。
    /// </summary>
    private sealed class FakeTimeService : ITimeService
    {
        public GameTime Now { get; set; } = new(1, Season.Spring, 1, GameTime.FirstHour, 0);

        public Weather Weather { get; set; } = Weather.Sunny;

        public bool IsPaused { get; set; }

        public void Advance(int gameMinutes) => throw new NotSupportedException("替身不推进时间");

        public void Sleep() => throw new NotSupportedException("替身不推进时间");
    }
}
