namespace XingGame.Systems.Economy;

/// <summary>
/// 一次买卖的结果。<b>失败的原因分得这么细，是因为 UI 要说人话</b>——
/// 「打烊了」「你钱不够」「这家店不收这个」在界面上是三句不同的话，
/// 用 bool 表达就得让 UI 自己去猜，猜法迟早和数据对不上。
/// </summary>
/// <remarks>
/// 这里<b>没有</b>「商店不存在」「物品不存在」两条：未知 id 是编程错误（数据写错、UI 传错），
/// 抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>——与 <c>ItemTable.Get</c>、
/// <c>CropTable.GetBySeed</c> 同一套分工：<b>运行时状况用返回值，写错的 id 用异常</b>。
/// </remarks>
public enum TradeResult
{
    Success,

    /// <summary>打烊（§5.2 / §12.1 的营业时间）。买卖都不做。</summary>
    ShopClosed,

    /// <summary>这家店不进这个货（买入时用）。</summary>
    ItemNotStocked,

    /// <summary>
    /// 价格文档未给（物品表里买价或卖价为 0）。§12.1 一个价都没给，
    /// 所以 M2 大部分商品都停在这一步——数值补齐前不能开卖，编一个价出去比不卖更糟。
    /// </summary>
    PriceNotSet,

    /// <summary>金币不足。<b>余额一分未动</b>。</summary>
    InsufficientFunds,

    /// <summary>背包放不下。<b>金币一分未动、背包也回到原样</b>。</summary>
    InventoryFull,

    /// <summary>卖出的东西背包里不够（全有或全无，同 <c>Inventory.Remove</c>）。</summary>
    ItemNotOwned,
}
