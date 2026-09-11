using System;
using System.IO;
using XingGame.Core.Time;
using XingGame.Systems.Economy;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 经济与商店的对抗性审计（M2）。只补既有用例<b>没在看</b>的分支：
/// 一轮买卖两边的账、失败路径退回后物品总数守恒、动态价格的幂等与「存档是权威事实」、
/// 以及 §12.2「只作用于农产品」在存档路径上的兜底。
/// </summary>
/// <remarks>
/// 时间用替身（本切片不验时间系统的行为，只验「取价时用到的是不是当天的」）；
/// 物品表、商店表、背包、钱包、价格全用真的——这里验的正是它们之间的配合。
/// </remarks>
public class M2Audit_Economy
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "seed_parsnip",  "name": "防风草种子", "description": "审计用。", "category": "Seed",     "maxStack": 999, "buyPrice": 20, "sellPrice": 0 },
        { "id": "crop_parsnip",  "name": "防风草",     "description": "审计用。", "category": "Crop",     "maxStack": 999, "buyPrice": 0,  "sellPrice": 35 },
        { "id": "material_wood", "name": "木材",       "description": "审计用。", "category": "Material", "maxStack": 999, "buyPrice": 10, "sellPrice": 6 }
      ]
    }
    """;

    private const string ShopsJson = """
    {
      "shops": [
        { "id": "general_store", "name": "杂货店", "openHour": 9, "closeHour": 17,
          "goods": [ "seed_parsnip", "material_wood" ] },
        { "id": "tavern", "name": "酒馆", "openHour": 12, "closeHour": 24, "goods": [ "crop_parsnip" ] }
      ]
    }
    """;

    private const string Wood = "material_wood";
    private const string Parsnip = "crop_parsnip";

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);
    private static readonly ShopTable Shops = ShopTable.FromJson(ShopsJson, Items);

    private sealed class Clock : ITimeService
    {
        public GameTime Now { get; set; } = new(1, Season.Spring, 1, 12, 0);

        public Weather Weather => Weather.Sunny;

        public bool IsPaused { get; set; }

        public void Advance(int gameMinutes) => throw new NotSupportedException("审计用例不推进时间");

        public void Sleep() => throw new NotSupportedException("审计用例不推进时间");
    }

    private readonly Wallet _wallet = new();
    private readonly Clock _clock = new();

    private ShopSystem NewSystem(Inventory? inventory = null, MarketPrices? prices = null) =>
        new(Shops, Items, inventory ?? new Inventory(Items, slotCount: 8), _wallet, _clock,
            prices ?? new MarketPrices(Items));

    // ——— 数值守恒：进来的 = 出去的 + 剩下的 ———

    [Fact]
    public void 买卖一轮_金币与物品两边的账都平得上()
    {
        _wallet.AddGold(500);
        Inventory inventory = new(Items, slotCount: 8);
        ShopSystem system = NewSystem(inventory);

        // 木材是非农产品 → 不参与 §12.2 的波动，买卖价就是物品表里那两个数，账可以逐分核对
        Assert.Equal(10, system.BuyPriceOf(Wood));
        Assert.Equal(6, system.SellPriceOf(Wood));

        Assert.Equal(TradeResult.Success, system.Buy("general_store", Wood, 4));
        Assert.Equal(460, _wallet.Gold);
        Assert.Equal(4, inventory.Count(Wood));

        Assert.Equal(TradeResult.Success, system.Sell("general_store", Wood, 4));
        Assert.Equal(484, _wallet.Gold);              // 500 − 4×10 + 4×6
        Assert.Equal(0, inventory.Count(Wood));
    }

    [Fact]
    public void 买入装不下而退回时_物品总数一件不多一件不少()
    {
        _wallet.AddGold(100_000);
        Inventory inventory = new(Items, slotCount: 2);
        Assert.Equal(0, inventory.Add(Wood, 990));
        Assert.Equal(0, inventory.Add(Wood, 990));   // 两格共 1980，只剩 18 个位置

        ShopSystem system = NewSystem(inventory);

        // 买 30 个：装得下 18 个、剩下 12 个装不下 → 整单取消，装进去的那 18 个要原样退回
        Assert.Equal(TradeResult.InventoryFull, system.Buy("general_store", Wood, 30));

        Assert.Equal(100_000, _wallet.Gold);
        Assert.Equal(1980, inventory.Count(Wood));
    }

    [Fact]
    public void 买入_一格都装不下时_整单取消而不是抛异常()
    {
        _wallet.AddGold(500);
        Inventory inventory = new(Items, slotCount: 1);
        Assert.Equal(0, inventory.Add(Wood, 999));   // 唯一的一格被别的物品占满

        ShopSystem system = NewSystem(inventory);

        // 一件都装不下（leftover == count）时，旧代码会执行 Remove(id, 0)，而「数量必须为正」当场抛。
        // 背包 24 格全满时买任何东西都会走到这里——这不是理论边界，是玩家真能撞上的那条路径，
        // 且与「装不下就整单取消」的注释直接相悖
        Assert.Equal(TradeResult.InventoryFull, system.Buy("general_store", "seed_parsnip", 1));

        Assert.Equal(500, _wallet.Gold);                  // 失败路径上一分钱都没碰
        Assert.Equal(0, inventory.Count("seed_parsnip"));
        Assert.Equal(999, inventory.Count(Wood));         // 原有的那格没被「退回」改乱
    }

    [Fact]
    public void 卖出_余额加上总价会超出_int_范围时_货不出手且余额不动()
    {
        // Wallet.Deserialize 只拒负数、不设上限：手改或 Mod 写的存档能把余额放到上限附近
        _wallet.AddGold(int.MaxValue);
        Inventory inventory = new(Items, slotCount: 8);
        Assert.Equal(0, inventory.Add(Parsnip, 3));

        ShopSystem system = NewSystem(inventory);

        // 「货先出手、钱后到手」的顺序下，AddGold 的 checked 溢出会在货已经没了之后才抛——
        // 挡住「数量 × 单价」的那一条漏的正是「余额 + 总价」（ADR-012 全有或全无）
        Assert.Throws<InvalidOperationException>(() => system.Sell("tavern", Parsnip, 1));

        Assert.Equal(3, inventory.Count(Parsnip));   // 货一件不少
        Assert.Equal(int.MaxValue, _wallet.Gold);    // 钱一分未动
    }

    [Fact]
    public void 买入_单价乘以数量超出_int_范围时_按钱不够处理而不是买得起()
    {
        _wallet.AddGold(int.MaxValue);   // 钱包能到的最大值
        Inventory inventory = new(Items, slotCount: 8);
        ShopSystem system = NewSystem(inventory);

        // 10 × 3 亿 = 30 亿 > int.MaxValue：若用 int 算总价会溢出成负数，
        // 于是「钱不够」会被判成「钱够」，玩家白拿一批货
        Assert.Equal(TradeResult.InsufficientFunds, system.Buy("general_store", Wood, 300_000_000));

        Assert.Equal(int.MaxValue, _wallet.Gold);
        Assert.Equal(0, inventory.Count(Wood));
    }

    // ——— §12.2 动态价格 ———

    [Fact]
    public void 动态价格_同一天重复刷新是幂等的_不做累乘()
    {
        var prices = new MarketPrices(Items);

        prices.Refresh(3);
        int third = prices.MultiplierPermille(Parsnip);

        prices.Refresh(3);
        Assert.Equal(third, prices.MultiplierPermille(Parsnip));

        // 中间跨过别的天再回到第 3 天：必须回到同一个数，
        // 而不是在上一次的基础上再涨跌一次（那会让价格随刷新次数漂移）
        prices.Refresh(7);
        prices.Refresh(3);

        Assert.Equal(third, prices.MultiplierPermille(Parsnip));
        Assert.Equal(3, prices.DayIndex);
    }

    [Fact]
    public void 动态价格_读档后当天就用存档里的系数_不重掷()
    {
        var prices = new MarketPrices(Items);

        // 存档停在第 0 天，系数 700 是手写的（第 0 天真实的掷点是 1293‰，见 ShopSystemTests）。
        // 存档是权威事实（同 ARCHITECTURE 备案 #16 的天气）：读回来必须用它，不按种子重掷一遍
        prices.Deserialize("""{ "Day": 0, "Multipliers": [ { "ItemId": "crop_parsnip", "Permille": 700 } ] }""", 1);

        Assert.Equal(0, prices.DayIndex);
        Assert.Equal(700, prices.MultiplierPermille(Parsnip));
        Assert.Equal(25, prices.Price(Parsnip, 35));   // 35 × 0.7 = 24.5 → 25
    }

    [Fact]
    public void 动态价格_跨天之后取价自动重算_不靠订阅事件()
    {
        var prices = new MarketPrices(Items);
        prices.Refresh(3);   // 读档回来停在第 3 天（读档不重放 DayStarted，备案 #15）

        // 时间已经走到第 7 天（春 8 日），这期间没有任何人发事件
        _clock.Now = new GameTime(1, Season.Spring, 8, 12, 0);

        ShopSystem system = NewSystem(prices: prices);

        var fresh = new MarketPrices(Items);
        fresh.Refresh(7);

        Assert.Equal(fresh.Price(Parsnip, 35), system.SellPriceOf(Parsnip));
        Assert.Equal(7, prices.DayIndex);   // 取价那一下把表推到了今天
    }

    [Fact]
    public void 动态价格_非农产品即使在存档里硬写了系数也不生效()
    {
        var prices = new MarketPrices(Items);

        // §12.2 只说了「农产品」随供需波动。存档里被人为塞进一条 Material 的系数时，
        // 取价路径不认它（表只按农产品建）——这条兜底写在断言里，免得被顺手删掉
        prices.Deserialize("""{ "Day": 4, "Multipliers": [ { "ItemId": "material_wood", "Permille": 700 } ] }""", 1);

        Assert.Equal(MarketPrices.FlatPermille, prices.MultiplierPermille(Wood));
        Assert.Equal(10, prices.Price(Wood, 10));
    }

    // ——— 坏存档 ———

    [Theory]
    [InlineData("null")]                                    // 内容为空
    [InlineData("""{ }""")]                                 // Day 与 Multipliers 都不在
    [InlineData("""{ "Day": 3 }""")]                        // 缺 Multipliers
    [InlineData("""{ "Day": 3, "Multipliers": null }""")]   // Multipliers 是 null
    public void 市场价格存档_坏数据一律当场抛(string json)
    {
        var prices = new MarketPrices(Items);

        Assert.Throws<InvalidDataException>(() => prices.Deserialize(json, 1));
    }

    [Fact]
    public void 市场价格存档_系数数组里出现null元素时_抛坏档而不是NRE()
    {
        var prices = new MarketPrices(Items);

        // 手写的 JSON 里数组元素可以是 null，System.Text.Json 照收。按 ADR-009 这是坏档
        // （InvalidDataException 的语义），而不是让 NRE 冒出去——将来按约定 catch 坏档的代码要接得住它
        Assert.Throws<InvalidDataException>(
            () => prices.Deserialize("""{ "Day": 3, "Multipliers": [ null ] }""", 1));
    }

    [Fact]
    public void 市场价格存档_缺少Day字段时_抛而不是当成第0天()
    {
        var prices = new MarketPrices(Items);

        // 0 是合法的第 0 天，所以「字段不在」必须与「字段是 0」分开（ADR-009）。
        // 静默当成第 0 天会让今天的价格悄悄对不上，且下一次 Serialize 就把 0 写回去
        Assert.Throws<InvalidDataException>(
            () => prices.Deserialize("""{ "Multipliers": [] }""", 1));

        Assert.Equal(MarketPrices.NotPriced, prices.DayIndex);   // 抛之前不许动内存里那份
    }

    [Fact]
    public void 钱包存档_内容为空当场抛()
    {
        var wallet = new Wallet();

        Assert.Throws<InvalidDataException>(() => wallet.Deserialize("null", wallet.Version));
    }
}
