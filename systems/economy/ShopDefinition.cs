using System.Collections.Generic;
using XingGame.Core.Time;

namespace XingGame.Systems.Economy;

/// <summary>
/// 一家商店：卖什么、什么时候开。<b>只描述「是什么」，不带价格</b>——
/// 价格挂在物品上（§12.1 一个价都没给），当日实价由 <see cref="ShopSystem"/> 算。
/// </summary>
/// <remarks>
/// 营业时间的区间是<b>左闭右开</b>：9:00–17:00 表示 9:00 开、17:00 已经关门。
/// 开闭差一个钟点是「明明准时到了却买不到」这种投诉的来源，所以在这里钉死，
/// 并由用例守着。
/// </remarks>
/// <param name="Id">商店 id，形如 <c>general_store</c>（ADR-012 的 id 风格）。</param>
/// <param name="Name">显示名（§12.1 的中文名）。</param>
/// <param name="Goods">在售商品 id，一律写物品表里真实存在的 id（加载时交叉校验）。</param>
public sealed record ShopDefinition(
    string Id,
    string Name,
    ShopSchedule Schedule,
    int OpenHour,
    int CloseHour,
    IReadOnlyList<string> Goods)
{
    /// <summary>
    /// 这个时刻是否营业。<see cref="ShopSchedule.Irregular"/> 的店恒为 false（见该枚举的说明）。
    /// </summary>
    public bool IsOpen(GameTime time) =>
        Schedule == ShopSchedule.ClockRange && time.Hour >= OpenHour && time.Hour < CloseHour;
}
