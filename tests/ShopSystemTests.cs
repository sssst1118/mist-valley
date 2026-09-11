using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XingGame.Core.Time;
using XingGame.Systems.Economy;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 商店与动态价格（M2 经济与商店）。守三件事：<b>打烊不卖</b>、<b>钱货两清</b>、
/// <b>价格只在 §12.2 给的区间里波动且读档可复现</b>。
/// </summary>
/// <remarks>
/// 时间、背包、钱包、价格都用真的——本切片要验的正是这几样之间的配合（谁先动、谁后动），
/// 用替身把它们换掉就什么也验不出来了。唯一用替身的是「拒绝扣款的钱包」，
/// 它是为了把那条理论上到不了的退回分支逼出来。
/// </remarks>
public class ShopSystemTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "seed_parsnip",     "name": "防风草种子", "description": "春季播种。", "category": "Seed",     "maxStack": 999, "buyPrice": 20,  "sellPrice": 0 },
        { "id": "crop_parsnip",     "name": "防风草",     "description": "春季作物。", "category": "Crop",     "maxStack": 999, "buyPrice": 0,   "sellPrice": 35 },
        { "id": "crop_melon",       "name": "甜瓜",       "description": "夏季作物。", "category": "Crop",     "maxStack": 999, "buyPrice": 100, "sellPrice": 250 },
        { "id": "material_wood",    "name": "木材",       "description": "建造材料。", "category": "Material", "maxStack": 999, "buyPrice": 0,   "sellPrice": 10 },
        { "id": "fertilizer_basic", "name": "基础肥料",   "description": "文档未给价。", "category": "Misc",   "maxStack": 999, "buyPrice": 0,   "sellPrice": 0 },
        { "id": "tool_hoe",         "name": "锄头",       "description": "开垦耕地。", "category": "Tool",     "maxStack": 1,   "buyPrice": 0,   "sellPrice": 0 }
      ]
    }
    """;

    private const string ShopsJson = """
    {
      "shops": [
        { "id": "general_store", "name": "杂货店", "openHour": 9, "closeHour": 17,
          "goods": [ "seed_parsnip", "fertilizer_basic" ] },
        { "id": "carpenter", "name": "木匠铺", "openHour": 9, "closeHour": 17, "goods": [] },
        { "id": "tavern", "name": "酒馆", "openHour": 12, "closeHour": 24,
          "goods": [ "crop_parsnip" ] },
        { "id": "desert_shop", "name": "沙漠商店", "openHour": 0, "closeHour": 24,
          "goods": [ "material_wood", "crop_melon" ] },
        { "id": "traveling_merchant", "name": "旅行商人", "schedule": "Irregular",
          "goods": [ "crop_melon" ] }
      ]
    }
    """;

    private const string GeneralStore = "general_store";
    private const string Tavern = "tavern";
    private const string DesertShop = "desert_shop";
    private const string TravelingMerchant = "traveling_merchant";

    private const string Seed = "seed_parsnip";
    private const string Parsnip = "crop_parsnip";
    private const string Melon = "crop_melon";
    private const string Wood = "material_wood";
    private const string Fertilizer = "fertilizer_basic";

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);
    private static readonly ShopTable ShopData = ShopTable.FromJson(ShopsJson, Items);

    private readonly Inventory _inventory = new(Items, slotCount: 8);
    private readonly Wallet _wallet = new();
    private readonly FakeTimeService _time = new();
    private readonly MarketPrices _prices = new(Items);

    private ShopSystem NewSystem(IEconomySystem? wallet = null, Inventory? inventory = null) =>
        new(ShopData, Items, inventory ?? _inventory, wallet ?? _wallet, _time, _prices);

    private void At(int day, int hour) => _time.Now = new GameTime(1, Season.Spring, day, hour, 0);

    private void OpenStore() => At(day: 1, hour: 12);

    /// <summary>背包整份快照：比数量更能抓出「槽位被动过」这类半个改动。</summary>
    private static string Snapshot(Inventory inventory)
    {
        var text = new StringBuilder();
        foreach (ItemStack slot in inventory.Slots) text.Append(slot.ItemId).Append(':').Append(slot.Count).Append('|');

        return text.ToString();
    }

    // ——— 按商店列商品 ———

    [Fact]
    public void 在售商品_列出货架与今日实价()
    {
        OpenStore();
        ShopSystem system = NewSystem();

        IReadOnlyList<ShopOffer> offers = system.Offers(GeneralStore);

        Assert.Equal(new[] { Seed, Fertilizer }, System.Linq.Enumerable.Select(offers, offer => offer.ItemId));
        Assert.Equal("防风草种子", offers[0].Name);
        Assert.Equal(20, offers[0].UnitPrice);
    }

    [Fact]
    public void 在售商品_文档未给价的商品也列出来_价填_0()
    {
        OpenStore();
        ShopSystem system = NewSystem();

        // 0 不是免费，是「没人定价」：UI 该显示成暂不出售，而不是让玩家白拿
        Assert.Equal(0, system.Offers(GeneralStore)[1].UnitPrice);
    }

    [Fact]
    public void 在售商品_打烊时照样列得出来()
    {
        At(day: 1, hour: 3);
        ShopSystem system = NewSystem();

        // 站在关门的店前，该看见「这家卖什么」，而不是一个空货架
        Assert.False(system.IsOpen(GeneralStore));
        Assert.Equal(2, system.Offers(GeneralStore).Count);
    }

    [Fact]
    public void 未知商店_id_抛_KeyNotFoundException()
    {
        ShopSystem system = NewSystem();

        Assert.Throws<KeyNotFoundException>(() => system.IsOpen("nope"));
        Assert.Throws<KeyNotFoundException>(() => system.Offers("nope"));
        Assert.Throws<KeyNotFoundException>(() => system.Buy("nope", Seed, 1));
        Assert.Throws<KeyNotFoundException>(() => system.Sell("nope", Seed, 1));
        Assert.False(system.TryGetShop("nope", out _));
    }

    // ——— 营业时间（§5.2 / §12.1） ———

    [Theory]
    [InlineData(8, false)]    // 开门前一小时
    [InlineData(9, true)]     // 左闭：9:00 整就开门了
    [InlineData(16, true)]
    [InlineData(17, false)]   // 右开：17:00 整已经关门
    public void 营业时间_左闭右开(int hour, bool expected)
    {
        At(day: 1, hour);
        ShopSystem system = NewSystem();

        Assert.Equal(expected, system.IsOpen(GeneralStore));
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(12, true)]
    [InlineData(23, true)]    // 酒馆 12:00-24:00：closeHour=24 能表达「到午夜」
    public void 营业时间_酒馆开到午夜(int hour, bool expected)
    {
        At(day: 1, hour);
        ShopSystem system = NewSystem();

        Assert.Equal(expected, system.IsOpen(Tavern));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(23)]
    public void 全天商店_任何钟点都开(int hour)
    {
        At(day: 1, hour);
        ShopSystem system = NewSystem();

        Assert.True(system.IsOpen(DesertShop));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(23)]
    public void 不定期商店_M2_一律打烊(int hour)
    {
        At(day: 1, hour);
        ShopSystem system = NewSystem();

        // §12.1 的「随机」「不定期」没有可算的开张条件，M2 不猜它开着
        Assert.False(system.IsOpen(TravelingMerchant));
    }

    // ——— 买入 ———

    [Fact]
    public void 买入_金币减少_背包增加()
    {
        OpenStore();
        _wallet.AddGold(500);
        ShopSystem system = NewSystem();

        Assert.Equal(TradeResult.Success, system.Buy(GeneralStore, Seed, 2));

        Assert.Equal(460, _wallet.Gold);
        Assert.Equal(2, _inventory.Count(Seed));
    }

    [Fact]
    public void 买入_恰好花光余额_余额归零()
    {
        OpenStore();
        _wallet.AddGold(40);
        ShopSystem system = NewSystem();

        Assert.Equal(TradeResult.Success, system.Buy(GeneralStore, Seed, 2));
        Assert.Equal(0, _wallet.Gold);
    }

    [Fact]
    public void 买入_金币不足_买卖双方都不变()
    {
        OpenStore();
        _wallet.AddGold(39);          // 差 1 金
        string before = Snapshot(_inventory);
        ShopSystem system = NewSystem();

        Assert.Equal(TradeResult.InsufficientFunds, system.Buy(GeneralStore, Seed, 2));

        // 这条是本切片最容易写反的地方：先扣钱再发现不够，余额就少了一块
        Assert.Equal(39, _wallet.Gold);
        Assert.Equal(before, Snapshot(_inventory));
    }

    [Fact]
    public void 买入_背包放不下_金币一分未动_背包回到原样()
    {
        OpenStore();
        _wallet.AddGold(100_000);
        var tiny = new Inventory(Items, slotCount: 1);
        string before = Snapshot(tiny);
        ShopSystem system = NewSystem(inventory: tiny);

        // 一格最多 999，买 1000 个必然装不下——装了的那 999 个要原样退回
        Assert.Equal(TradeResult.InventoryFull, system.Buy(GeneralStore, Seed, 1000));

        Assert.Equal(100_000, _wallet.Gold);
        Assert.Equal(before, Snapshot(tiny));
    }

    [Fact]
    public void 买入_该店不卖这个_ItemNotStocked()
    {
        OpenStore();
        _wallet.AddGold(500);
        ShopSystem system = NewSystem();

        // 木匠铺的货架是空的（文档只说了它「建造建筑、买家具」，没有具体商品名）
        Assert.Equal(TradeResult.ItemNotStocked, system.Buy("carpenter", Wood, 1));
    }

    [Fact]
    public void 买入_文档未给价_PriceNotSet()
    {
        OpenStore();
        _wallet.AddGold(500);
        ShopSystem system = NewSystem();

        Assert.Equal(TradeResult.PriceNotSet, system.Buy(GeneralStore, Fertilizer, 1));
        Assert.Equal(500, _wallet.Gold);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 买入_数量不是正数_抛(int count)
    {
        OpenStore();
        ShopSystem system = NewSystem();

        Assert.Throws<ArgumentOutOfRangeException>(() => system.Buy(GeneralStore, Seed, count));
    }

    [Fact]
    public void 买入_物品表里没有的_id_抛()
    {
        OpenStore();
        ShopSystem system = NewSystem();

        Assert.Throws<KeyNotFoundException>(() => system.Buy(GeneralStore, "seed_nope", 1));
    }

    [Fact]
    public void 买入_打烊时段_失败且两边都不变()
    {
        At(day: 1, hour: 8);
        _wallet.AddGold(500);
        string before = Snapshot(_inventory);
        ShopSystem system = NewSystem();

        Assert.Equal(TradeResult.ShopClosed, system.Buy(GeneralStore, Seed, 1));

        Assert.Equal(500, _wallet.Gold);
        Assert.Equal(before, Snapshot(_inventory));
    }

    [Fact]
    public void 买入_钱包扣不动款时_货要退回()
    {
        OpenStore();
        string before = Snapshot(_inventory);
        ShopSystem system = NewSystem(wallet: new StingyWallet());

        // 余额查着够、真扣却扣不动：这条路上不能留下「货已经在背包里、钱却没付」
        Assert.Equal(TradeResult.InsufficientFunds, system.Buy(GeneralStore, Seed, 2));

        Assert.Equal(before, Snapshot(_inventory));
    }

    // ——— 卖出 ———

    [Fact]
    public void 卖出_背包减少_金币增加_且用今日收购价()
    {
        OpenStore();
        _inventory.Add(Parsnip, 3);
        ShopSystem system = NewSystem();

        // 第 1 天（天序 0）crop_parsnip 的系数是 +29.3%：35 × 1.293 = 45.255 → 45
        Assert.Equal(45, system.SellPriceOf(Parsnip));
        Assert.Equal(TradeResult.Success, system.Sell(Tavern, Parsnip, 2));

        Assert.Equal(1, _inventory.Count(Parsnip));
        Assert.Equal(90, _wallet.Gold);
    }

    [Fact]
    public void 卖出_背包里不够_ItemNotOwned_且金币不变()
    {
        OpenStore();
        _inventory.Add(Parsnip, 1);
        string before = Snapshot(_inventory);
        ShopSystem system = NewSystem();

        Assert.Equal(TradeResult.ItemNotOwned, system.Sell(Tavern, Parsnip, 2));

        Assert.Equal(0, _wallet.Gold);
        Assert.Equal(before, Snapshot(_inventory));
    }

    [Fact]
    public void 卖出_文档未给价_商店不收()
    {
        OpenStore();
        _inventory.Add(Seed, 5);          // 种子的卖价文档没给（附录 A 的「种子价」是买价）
        ShopSystem system = NewSystem();

        Assert.Equal(TradeResult.PriceNotSet, system.Sell(Tavern, Seed, 1));
        Assert.Equal(5, _inventory.Count(Seed));
        Assert.Equal(0, _wallet.Gold);
    }

    [Fact]
    public void 卖出_打烊时段_失败且两边都不变()
    {
        At(day: 1, hour: 8);
        _inventory.Add(Parsnip, 2);
        string before = Snapshot(_inventory);
        ShopSystem system = NewSystem();

        Assert.Equal(TradeResult.ShopClosed, system.Sell(Tavern, Parsnip, 1));

        Assert.Equal(before, Snapshot(_inventory));
        Assert.Equal(0, _wallet.Gold);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 卖出_数量不是正数_抛(int count)
    {
        OpenStore();
        ShopSystem system = NewSystem();

        Assert.Throws<ArgumentOutOfRangeException>(() => system.Sell(Tavern, Parsnip, count));
    }

    [Fact]
    public void 卖出_物品表里没有的_id_抛()
    {
        OpenStore();
        ShopSystem system = NewSystem();

        Assert.Throws<KeyNotFoundException>(() => system.Sell(Tavern, "crop_nope", 1));
    }

    [Fact]
    public void 卖出_把作物卖给任何营业中的商店都收()
    {
        OpenStore();
        _inventory.Add(Wood, 1);
        ShopSystem system = NewSystem();

        // §12.1 只写了鱼店「卖鱼」这一处收购，逐店收购清单文档没给；M2 定成
        // 「营业中的店按物品卖价收任何可售物品」，否则玩家种出来的东西没法变现
        Assert.Equal(TradeResult.Success, system.Sell(GeneralStore, Wood, 1));

        Assert.Equal(10, _wallet.Gold);
    }

    // ——— §12.2 动态价格 ———

    [Theory]
    [InlineData(0, 1293)]
    [InlineData(1, 1000)]
    [InlineData(2, 1000)]
    [InlineData(3, 760)]
    [InlineData(7, 726)]
    [InlineData(12, 883)]
    [InlineData(27, 1407)]
    public void 动态价格_每日刷新_且同一天同一物品必得同一系数(int dayIndex, int expected)
    {
        // 天序 0 = 元年春 1 日。掷点是纯函数：同一天必然同一个价，
        // 读档不会有「价格跳变」——这一条由写死的期望值守着
        var prices = new MarketPrices(Items);
        prices.Refresh(dayIndex);

        Assert.Equal(expected, prices.MultiplierPermille(Parsnip));
        Assert.Equal(dayIndex, prices.DayIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(27)]
    public void 动态价格_全部落在_12_2_给的两段区间里(int dayIndex)
    {
        // 供大于求跌 10-30%（700‰-900‰）、供不应求涨 10-50%（1100‰-1500‰），其余原价
        ItemTable real = ItemTable.LoadDefault();
        var prices = new MarketPrices(real);
        prices.Refresh(dayIndex);

        foreach (ItemDefinition definition in real.All)
        {
            if (definition.Category != ItemCategory.Crop) continue;

            int permille = prices.MultiplierPermille(definition.Id);
            bool inBand = permille == 1000 || (permille >= 700 && permille <= 900) || (permille >= 1100 && permille <= 1500);

            Assert.True(inBand, $"{definition.Id} 在 {dayIndex} 的系数是 {permille}‰");
        }
    }

    [Fact]
    public void 动态价格_非农产品恒为原价()
    {
        var prices = new MarketPrices(Items);
        prices.Refresh(0);

        // §12.2 说的是「农产品」：种子、材料、杂货、工具的价格波动文档没提，不猜
        Assert.Equal(1000, prices.MultiplierPermille(Seed));
        Assert.Equal(1000, prices.MultiplierPermille(Wood));
        Assert.Equal(1000, prices.MultiplierPermille(Fertilizer));
        Assert.Equal(10, prices.Price(Wood, 10));
    }

    [Fact]
    public void 动态价格_没算过的那一天一律原价()
    {
        // 新档一次商店都没进过：价格不该凭空冒出波动
        var prices = new MarketPrices(Items);

        Assert.Equal(MarketPrices.NotPriced, prices.DayIndex);
        Assert.Equal(1000, prices.MultiplierPermille(Parsnip));
        Assert.Equal(35, prices.Price(Parsnip, 35));
    }

    [Fact]
    public void 动态价格_价格四舍五入到整数()
    {
        var prices = new MarketPrices(Items);
        prices.Refresh(0);

        // 35 × 1293‰ = 45.255 → 45；35 × 726‰（第 7 天）= 25.41 → 25
        Assert.Equal(45, prices.Price(Parsnip, 35));
        prices.Refresh(7);

        Assert.Equal(25, prices.Price(Parsnip, 35));
    }

    [Fact]
    public void 动态价格_跨天会变_且商店取价跟着走()
    {
        At(day: 1, hour: 12);
        _inventory.Add(Parsnip, 4);
        ShopSystem system = NewSystem();

        Assert.Equal(45, system.SellPriceOf(Parsnip));

        // 睡到第 4 天（天序 3）：同一样作物的行情变了
        At(day: 4, hour: 12);
        Assert.Equal(27, system.SellPriceOf(Parsnip));

        Assert.Equal(TradeResult.Success, system.Sell(Tavern, Parsnip, 1));
        Assert.Equal(27, _wallet.Gold);
    }

    [Fact]
    public void 动态价格_也作用于农产品的买入价()
    {
        OpenStore();
        ShopSystem system = NewSystem();

        // 沙漠商店的甜瓜买价 100；天序 0 的系数 739‰ → 74（四舍五入）
        Assert.Equal(74, system.BuyPriceOf(Melon));
    }

    // ——— 价格存档 ———

    [Fact]
    public void 存档往返_同一天的价格可复现()
    {
        OpenStore();
        ShopSystem system = NewSystem();
        int before = system.SellPriceOf(Parsnip);

        string json = _prices.Serialize();
        var restored = new MarketPrices(Items);
        restored.Deserialize(json, _prices.Version);

        Assert.Equal(_prices.DayIndex, restored.DayIndex);
        Assert.Equal(before, restored.Price(Parsnip, 35));
    }

    [Fact]
    public void 存档往返_价格表只写波动过的物品()
    {
        var prices = new MarketPrices(Items);
        prices.Refresh(0);

        string json = prices.Serialize();

        // 原价的物品不写：缺省即原价，写一堆 1000 只会让存档变长
        Assert.DoesNotContain(Wood, json, StringComparison.Ordinal);
        Assert.Contains(Parsnip, json, StringComparison.Ordinal);
    }

    [Fact]
    public void 存档往返_还没算过一天也能存()
    {
        var prices = new MarketPrices(Items);

        var restored = new MarketPrices(Items);
        restored.Deserialize(prices.Serialize(), prices.Version);

        Assert.Equal(MarketPrices.NotPriced, restored.DayIndex);
    }

    [Fact]
    public void 存档键与版本()
    {
        Assert.Equal("market", _prices.SaveKey);
        Assert.Equal(1, _prices.Version);
    }

    [Fact]
    public void 存档_版本高于当前_抛_NotSupportedException()
    {
        Assert.Throws<NotSupportedException>(
            () => _prices.Deserialize("""{ "Day": 0, "Multipliers": [] }""", fromVersion: 2));
    }

    [Theory]
    [InlineData(2000)]   // 超出 +50%
    [InlineData(600)]    // 超出 -30%
    [InlineData(999)]    // 落在两段区间之间
    [InlineData(1000)]   // 原价不该写进存档（缺省即原价）
    public void 存档_系数不在文档给的区间里_抛(int permille)
    {
        string json = $$"""{ "Day": 0, "Multipliers": [ { "ItemId": "crop_parsnip", "Permille": {{permille}} } ] }""";

        Assert.Throws<InvalidDataException>(() => _prices.Deserialize(json, _prices.Version));
    }

    [Fact]
    public void 存档_重复物品_抛()
    {
        const string json = """
        { "Day": 0, "Multipliers": [
            { "ItemId": "crop_parsnip", "Permille": 800 },
            { "ItemId": "crop_parsnip", "Permille": 900 } ] }
        """;

        Assert.Throws<InvalidDataException>(() => _prices.Deserialize(json, _prices.Version));
    }

    [Fact]
    public void 存档_物品不在表里_抛()
    {
        const string json = """
        { "Day": 0, "Multipliers": [ { "ItemId": "crop_nope", "Permille": 800 } ] }
        """;

        Assert.Throws<InvalidDataException>(() => _prices.Deserialize(json, _prices.Version));
    }

    [Fact]
    public void 存档_天序为负_除还没算过之外都抛()
    {
        const string json = """{ "Day": -2, "Multipliers": [] }""";

        Assert.Throws<InvalidDataException>(() => _prices.Deserialize(json, _prices.Version));
    }

    [Fact]
    public void 读档是整状态覆盖_存档里没有的物品回到原价()
    {
        var prices = new MarketPrices(Items);
        prices.Refresh(0);
        Assert.NotEqual(1000, prices.MultiplierPermille(Parsnip));

        prices.Deserialize("""{ "Day": 5, "Multipliers": [] }""", prices.Version);

        Assert.Equal(5, prices.DayIndex);
        Assert.Equal(1000, prices.MultiplierPermille(Parsnip));
    }

    // ——— 缺省数据文件 ———

    [Theory]
    // §12.1 商店系统（1314-1326）+ §5.2 雾谷镇设施（235-249）
    [InlineData("general_store", "杂货店", 9, 17)]
    [InlineData("blacksmith", "铁匠铺", 9, 16)]
    [InlineData("carpenter", "木匠铺", 9, 17)]
    [InlineData("fish_shop", "鱼店", 9, 17)]
    [InlineData("tavern", "酒馆", 12, 24)]
    [InlineData("clinic", "诊所", 9, 15)]
    [InlineData("desert_shop", "沙漠商店", 0, 24)]
    public void 缺省商店表_九家店里这七家的营业时间与文档一致(string id, string name, int openHour, int closeHour)
    {
        ShopTable table = ShopTable.LoadDefault(ItemTable.LoadDefault());
        ShopDefinition shop = table.Get(id);

        Assert.Equal(name, shop.Name);
        Assert.Equal(ShopSchedule.ClockRange, shop.Schedule);
        Assert.Equal(openHour, shop.OpenHour);
        Assert.Equal(closeHour, shop.CloseHour);
    }

    [Fact]
    public void 缺省商店表_两家不定期商店不写钟点()
    {
        ShopTable table = ShopTable.LoadDefault(ItemTable.LoadDefault());

        Assert.Equal(9, table.All.Count);
        Assert.Equal(ShopSchedule.Irregular, table.Get("traveling_merchant").Schedule);
        Assert.Equal(ShopSchedule.Irregular, table.Get("cultivator_market").Schedule);
    }

    [Fact]
    public void 缺省商店表_杂货店卖种子与肥料()
    {
        ShopTable table = ShopTable.LoadDefault(ItemTable.LoadDefault());

        // §12.1「种子、肥料、杂货」；杂货的具体商品文档一个名字都没给，只有种子与肥料有名字
        Assert.Equal(
            new[]
            {
                "seed_parsnip", "seed_potato", "seed_strawberry", "seed_spirit_grass",
                "seed_blueberry", "seed_melon", "seed_fire_spirit_flower",
                "seed_pumpkin", "seed_cranberry", "seed_gold_spirit_fruit", "seed_ice_spirit_grass",

                "fertilizer_basic", "fertilizer_quality", "fertilizer_speed",
                "fertilizer_moisture", "fertilizer_deluxe",
            },
            table.Get("general_store").Goods);
    }

    [Fact]
    public void 缺省商店表_铁匠铺卖矿石()
    {
        ShopTable table = ShopTable.LoadDefault(ItemTable.LoadDefault());

        Assert.Equal(new[] { "material_copper_ore" }, table.Get("blacksmith").Goods);
    }

    [Fact]
    public void 缺省物品表_五种肥料都在_价格一律填_0()
    {
        ItemTable items = ItemTable.LoadDefault();

        // §6.1 的五种肥料是全表唯一由商店带出来的新物品；价格文档未给（§12.1 一个价都没有），
        // 所以它们眼下都停在 PriceNotSet 上——数值补齐前不能开卖
        foreach (string id in new[]
                 {
                     "fertilizer_basic", "fertilizer_quality", "fertilizer_speed",
                     "fertilizer_moisture", "fertilizer_deluxe",
                 })
        {
            ItemDefinition fertilizer = items.Get(id);

            Assert.Equal(ItemCategory.Misc, fertilizer.Category);
            Assert.Equal(0, fertilizer.BuyPrice);
            Assert.Equal(0, fertilizer.SellPrice);
        }
    }

    // ——— 商店表加载校验（数据表是外部输入，坏值当场抛） ———

    [Fact]
    public void 加载_货架上有物品表里没有的_id_抛()
    {
        const string json = """
        { "shops": [ { "id": "general_store", "name": "杂货店", "openHour": 9, "closeHour": 17,
                       "goods": [ "seed_nope" ] } ] }
        """;

        // 货架 id 与物品表对不上，是两份数据各自的测试都发现不了的那种错
        Assert.Throws<InvalidDataException>(() => ShopTable.FromJson(json, Items));
    }

    [Fact]
    public void 加载_重复商店_id_抛()
    {
        const string json = """
        { "shops": [
            { "id": "general_store", "name": "杂货店", "openHour": 9, "closeHour": 17, "goods": [] },
            { "id": "general_store", "name": "另一家杂货店", "openHour": 9, "closeHour": 17, "goods": [] } ] }
        """;

        Assert.Throws<InvalidDataException>(() => ShopTable.FromJson(json, Items));
    }

    [Fact]
    public void 加载_货架上重复出现同一样东西_抛()
    {
        const string json = """
        { "shops": [ { "id": "general_store", "name": "杂货店", "openHour": 9, "closeHour": 17,
                       "goods": [ "seed_parsnip", "seed_parsnip" ] } ] }
        """;

        Assert.Throws<InvalidDataException>(() => ShopTable.FromJson(json, Items));
    }

    [Theory]
    [InlineData(17, 9)]     // 倒置
    [InlineData(9, 9)]      // 空区间（永远打烊）
    [InlineData(-1, 9)]
    [InlineData(9, 25)]
    public void 加载_无效的营业时间_抛(int openHour, int closeHour)
    {
        // 空区间与倒置区间都会让「营业时间」静默失效，而数据看着很正常
        string json = $$"""
        { "shops": [ { "id": "general_store", "name": "杂货店",
                       "openHour": {{openHour}}, "closeHour": {{closeHour}}, "goods": [] } ] }
        """;

        Assert.Throws<InvalidDataException>(() => ShopTable.FromJson(json, Items));
    }

    [Fact]
    public void 加载_不定期商店写了钟点_抛()
    {
        const string json = """
        { "shops": [ { "id": "cultivator_market", "name": "修士坊市", "schedule": "Irregular",
                       "openHour": 9, "closeHour": 17, "goods": [] } ] }
        """;

        // 写了也没人会读，留着就是一份会过期的假信息
        Assert.Throws<InvalidDataException>(() => ShopTable.FromJson(json, Items));
    }

    [Fact]
    public void 加载_认不出的开张方式_抛()
    {
        // 连 "1" 也不认：按序号写取值会随枚举插值错位（同 ItemTable 解析分类）
        const string json = """
        { "shops": [ { "id": "general_store", "name": "杂货店", "schedule": "1", "goods": [] } ] }
        """;

        Assert.Throws<InvalidDataException>(() => ShopTable.FromJson(json, Items));
    }

    [Theory]
    [InlineData("""{ "shops": [ { "name": "杂货店", "openHour": 9, "closeHour": 17, "goods": [] } ] }""")]
    [InlineData("""{ "shops": [ { "id": "general_store", "openHour": 9, "closeHour": 17, "goods": [] } ] }""")]
    [InlineData("""{ "shops": [ { "id": "general_store", "name": "杂货店", "closeHour": 17, "goods": [] } ] }""")]
    [InlineData("""{ "shops": [ { "id": "general_store", "name": "杂货店", "openHour": 9, "goods": [] } ] }""")]
    [InlineData("""{ "shops": [ { "id": "general_store", "name": "杂货店", "openHour": 9, "closeHour": 17 } ] }""")]
    public void 加载_缺字段或缺条目_抛(string json)
    {
        Assert.Throws<InvalidDataException>(() => ShopTable.FromJson(json, Items));
    }

    [Fact]
    public void 加载_缺_shops_数组_抛()
    {
        Assert.Throws<InvalidDataException>(() => ShopTable.FromJson("""{ "商店": [] }""", Items));
    }

    [Fact]
    public void 加载_空表是合法的_但查不到任何商店()
    {
        // 空 shops 与「缺 shops」不是一回事：前者是「暂时一家店都没有」，后者是数据写坏了
        ShopTable table = ShopTable.FromJson("""{ "shops": [] }""", Items);

        Assert.Empty(table.All);
        Assert.Throws<KeyNotFoundException>(() => table.Get("general_store"));
    }

    // ——— 替身 ———

    /// <summary>余额看着够、却永远扣不动款的钱包：用来逼出买入流程里那条退回分支。</summary>
    private sealed class StingyWallet : IEconomySystem
    {
        public int Gold => 1000;

        public void AddGold(int amount) => throw new NotSupportedException("替身不记账");

        public bool TrySpendGold(int amount) => false;
    }

    private sealed class FakeTimeService : ITimeService
    {
        public GameTime Now { get; set; } = new(1, Season.Spring, 1, 12, 0);

        public Weather Weather { get; set; } = Weather.Sunny;

        public bool IsPaused { get; set; }

        public void Advance(int gameMinutes) => throw new NotSupportedException("替身不推进时间");

        public void Sleep() => throw new NotSupportedException("替身不推进时间");
    }
}
