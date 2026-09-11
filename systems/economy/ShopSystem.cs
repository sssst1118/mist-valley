using System;
using System.Collections.Generic;
using XingGame.Core.Time;
using XingGame.Systems.Items;

namespace XingGame.Systems.Economy;

/// <summary>
/// 商店：把商店表、物品表、背包、钱包、时间五样接起来。桥接层只调这里，不自己算价、不自己记账。
/// </summary>
/// <remarks>
/// <para>
/// <b>两条承诺，都由本类的检查顺序实现：</b>
/// </para>
/// <list type="number">
/// <item><b>打烊不卖</b>：营业时间从 <see cref="ITimeService.Now"/> 读（§5.2 是游戏内钟点），
/// 每一笔买卖的第一关就是它——排在价格、余额、背包之前，免得打烊时还先动了一处状态。</item>
/// <item><b>钱货两清</b>：任何一笔失败都不会留下「钱扣了货没到」。
/// 买入的顺序是「先查钱 → 装货（装不下原样退回）→ 最后才扣钱」，
/// 卖出是「先查货 → 查钱包装不装得下 → 出货 → 最后才进钱」——<b>失败路径上一分钱都不碰</b>。</item>
/// </list>
/// <para>
/// <b>收购范围为什么是「任何有卖价的物品」：</b>§12.1 只写了鱼店「卖鱼」这一处收购，
/// 逐店的收购清单文档没给。可玩家的作物必须有地方变现——否则开局那 500 金币只出不进，
/// 经济是个死循环。所以 M2 定成「营业中的商店按物品的卖出价收购任何可售物品」，
/// 并<b>已上报待裁决</b>：等文档给了逐店收购清单，改的只是这一个判断。
/// </para>
/// </remarks>
public sealed class ShopSystem : IShopSystem
{
    private readonly ShopTable _shops;
    private readonly IItemTable _items;
    private readonly IInventory _inventory;
    private readonly IEconomySystem _wallet;
    private readonly ITimeService _time;
    private readonly MarketPrices _prices;

    public ShopSystem(
        ShopTable shops,
        IItemTable items,
        IInventory inventory,
        IEconomySystem wallet,
        ITimeService time,
        MarketPrices prices)
    {
        _shops = shops ?? throw new ArgumentNullException(nameof(shops));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _prices = prices ?? throw new ArgumentNullException(nameof(prices));
    }

    public IReadOnlyList<ShopDefinition> Shops => _shops.All;

    public bool TryGetShop(string shopId, out ShopDefinition shop) => _shops.TryGet(shopId, out shop);

    public bool IsOpen(string shopId) => _shops.Get(shopId).IsOpen(_time.Now);

    /// <summary>
    /// 在售商品与今日实价。<b>打烊时照样列得出来</b>——玩家站在关着门的店前，
    /// 该看见的是「这家卖什么、现在关门」，而不是一个空货架。
    /// 价 0 的商品也列出来（「文档未给价」），由 UI 决定怎么显示。
    /// </summary>
    public IReadOnlyList<ShopOffer> Offers(string shopId)
    {
        ShopDefinition shop = _shops.Get(shopId);

        var offers = new List<ShopOffer>(shop.Goods.Count);
        foreach (string itemId in shop.Goods)
        {
            offers.Add(new ShopOffer(itemId, _items.Get(itemId).Name, BuyPriceOf(itemId)));
        }

        return offers;
    }

    /// <summary>
    /// 今日售价。**0 表示「文档未给价」**（不是免费）：§12.1 一个价都没给，
    /// 这种商品在 <see cref="Buy"/> 里会被 <see cref="TradeResult.PriceNotSet"/> 挡住。
    /// </summary>
    public int BuyPriceOf(string itemId)
    {
        EnsureTodayPrices();
        return PriceOf(itemId, _items.Get(itemId).BuyPrice);
    }

    /// <summary>今日收购价。<b>0 表示文档未给价</b>（商店不收），同 <see cref="BuyPriceOf"/>。</summary>
    public int SellPriceOf(string itemId)
    {
        EnsureTodayPrices();
        return PriceOf(itemId, _items.Get(itemId).SellPrice);
    }

    /// <summary>物品表里的原价 → 今日实价。0 一律表示「文档未给价」：不是免费，是没人定价。</summary>
    private int PriceOf(string itemId, int basePrice) =>
        basePrice <= 0 ? 0 : _prices.Price(itemId, basePrice);

    public TradeResult Buy(string shopId, string itemId, int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "数量必须为正");

        ShopDefinition shop = _shops.Get(shopId);

        if (!shop.IsOpen(_time.Now)) return TradeResult.ShopClosed;

        EnsureTodayPrices();

        // 物品表里没有的 id 当场抛（写错的 id 是编程错误，同 ItemTable.Get）；
        // 「这家店进不进这个货」是下一关的事——两件事不一样，都得说清楚
        ItemDefinition definition = _items.Get(itemId);

        if (!Stocked(shop.Goods, itemId)) return TradeResult.ItemNotStocked;

        int unitPrice = PriceOf(itemId, definition.BuyPrice);
        if (unitPrice <= 0) return TradeResult.PriceNotSet;

        // 用 long 比：单价 × 数量的积在 int 里可能溢出，溢出的负数会让「钱不够」判断成「钱够」
        long total = (long)unitPrice * count;
        if (_wallet.Gold < total) return TradeResult.InsufficientFunds;

        // 装不下就整单取消：Add 是「尽力装」，装了一半的商品要原样退回，别让玩家花了钱买到半份。
        // 一件都没装下时（leftover == count）没有可退的东西——照旧写法会执行 Remove(id, 0)，
        // 而「数量必须为正」当场抛。24 格全满时买任何东西都走这条路，不是理论边界
        int leftover = _inventory.Add(itemId, count);
        if (leftover > 0)
        {
            if (leftover < count) _inventory.Remove(itemId, count - leftover);

            return TradeResult.InventoryFull;
        }

        // 扣钱放在最后：走到这里之前，失败路径一分钱都没碰过
        if (!_wallet.TrySpendGold((int)total))
        {
            // 理论上到不了（余额上面查过、中间没人动钱）。留着是为了让「钱只在这里动」这条
            // 不变量在代码里成立——真要漏了，结果是交易失败，而不是玩家白拿一批货
            _inventory.Remove(itemId, count);
            return TradeResult.InsufficientFunds;
        }

        return TradeResult.Success;
    }

    public TradeResult Sell(string shopId, string itemId, int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "数量必须为正");

        ShopDefinition shop = _shops.Get(shopId);

        if (!shop.IsOpen(_time.Now)) return TradeResult.ShopClosed;

        // SellPriceOf 内部会去物品表取定义：表里没有的 id 当场抛，与买入同一套分工
        int unitPrice = SellPriceOf(itemId);
        if (unitPrice <= 0) return TradeResult.PriceNotSet;

        // 先算总价：算不出整数金币的（天文数字）当场抛，别等到货已经出手才发现钱进不去
        long total = (long)unitPrice * count;
        if (total > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(count), count, $"卖出数量过大：{count} × {unitPrice} 超出金币能表示的范围");

        if (_inventory.Count(itemId) < count) return TradeResult.ItemNotOwned;

        // 余额 + 总价超出金币范围时先拒：货一旦出手就退不回来（ADR-012 全有或全无）。
        // 上面那条只挡住了「数量 × 单价」，漏的正是「余额 + 总价」——Wallet.Deserialize 只拒负数、
        // 不设上限，手改或 Mod 写的存档能把余额放到 int.MaxValue 附近，那时 AddGold 的 checked 溢出
        // 会在货已经没了之后才抛
        if ((long)_wallet.Gold + total > int.MaxValue)
            throw new InvalidOperationException(
                $"卖出 {count} 个「{itemId}」得 {total} 金币，钱包余额 {_wallet.Gold} 装不下");

        // 货先出手、钱后到手：反过来会在「货其实不够」时白送玩家一笔钱
        if (!_inventory.Remove(itemId, count)) return TradeResult.ItemNotOwned;

        _wallet.AddGold((int)total);
        return TradeResult.Success;
    }

    /// <summary>
    /// 货架上有没有这一件。手写循环而不是 LINQ 的 <c>Contains</c>：货架只有十几格，
    /// 为它引一次 <c>System.Linq</c> 不划算，而字符串比较要显式指明按序比较（Ordinal），
    /// 免得跟着运行时的区域设置飘。
    /// </summary>
    private static bool Stocked(IReadOnlyList<string> goods, string itemId)
    {
        for (int index = 0; index < goods.Count; index++)
        {
            if (string.Equals(goods[index], itemId, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>
    /// 把价格推到「今天」。<b>读时判断，不订阅 <c>DayStarted</c>。</b>
    /// </summary>
    /// <remarks>
    /// 读档不会重放 <c>DayStarted</c>（ARCHITECTURE 备案 #15），订阅式刷新会让读回来的档停在
    /// 上一天的价格上，还得额外补一次调用才对齐——而补漏的那一次迟早会漏。
    /// 「今天算过没有」做成读时判断之后，就没有这个缺口：任何时候取价，拿到的都是当天的。
    /// </remarks>
    private void EnsureTodayPrices()
    {
        int day = MarketPrices.DayIndexOf(_time.Now);
        if (_prices.DayIndex == day) return;

        _prices.Refresh(day);
    }
}
