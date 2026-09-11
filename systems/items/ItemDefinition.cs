namespace XingGame.Systems.Items;

/// <summary>物品的静态定义：一份数据表里的一行。运行时状态（数量）在 <see cref="ItemStack"/> 里。</summary>
public sealed record ItemDefinition(
    string       Id,
    string       Name,
    string       Description,
    ItemCategory Category,
    int          MaxStack,    // 堆叠上限；不可堆叠为 1
    int          BuyPrice,    // 买入价（商店售价）；0 表示「文档未给」或商店不卖
    int          SellPrice);  // 卖出价；0 表示「文档未给」或不可售
