using System.Collections.Generic;

namespace XingGame.Systems.Economy;

/// <summary>货架上的一格：商品 + 今日实价。价格是<b>当天</b>的（§12.2 每日刷新）。</summary>
/// <param name="UnitPrice">今日单价；<b>0 表示「文档未给价」</b>，UI 应显示成「暂不出售」而不是「免费」。</param>
public readonly record struct ShopOffer(string ItemId, string Name, int UnitPrice);

/// <summary>
/// 商店。买卖都从这里走，桥接层不自己算价、不自己记账。
/// </summary>
/// <remarks>
/// 「打烊不卖」与「钱货两清」两条规矩都关在这道门里：打烊时点是别人的（<c>ITimeService</c>），
/// 但<b>判断在系统里</b>——写在界面层的话，将来多开一个入口（快捷键、Mod）就多一处漏判。
/// </remarks>
public interface IShopSystem
{
    /// <summary>全部商店，按数据文件里的顺序。</summary>
    IReadOnlyList<ShopDefinition> Shops { get; }

    bool TryGetShop(string shopId, out ShopDefinition shop);

    /// <summary>此刻是否营业。<paramref name="shopId"/> 不在表里时抛 <c>KeyNotFoundException</c>。</summary>
    bool IsOpen(string shopId);

    /// <summary>在售商品与今日实价（含 §12.2 的波动）。<paramref name="shopId"/> 不在表里时抛。</summary>
    IReadOnlyList<ShopOffer> Offers(string shopId);

    /// <summary>今日售价（玩家付出的单价，含动态系数）。</summary>
    int BuyPriceOf(string itemId);

    /// <summary>今日收购价（玩家到手的单价，含动态系数）。</summary>
    int SellPriceOf(string itemId);

    /// <summary>
    /// 买入。<b>要么钱货两清，要么两边都不动</b>：钱不够、背包放不下、打烊、没价，
    /// 任何一条不满足时金币与背包都保持原样。
    /// </summary>
    TradeResult Buy(string shopId, string itemId, int count);

    /// <summary>卖出。同上，全有或全无。</summary>
    TradeResult Sell(string shopId, string itemId, int count);
}
