namespace XingGame.Systems.Economy;

/// <summary>
/// 商店的开张方式。§12.1 的九家店里有两家不是按钟点营业的——
/// 旅行商人「随机」、修士坊市「不定期」——它们的开张由事件决定，钟点区间表达不了。
/// </summary>
/// <remarks>
/// 「全天」<b>不需要单列一档</b>：§12.1/§5.2 的「全天」就是 0:00–24:00，用
/// <see cref="ClockRange"/> + 0/24 表达得下，多一档就多一条要维护的等价规则。
/// </remarks>
public enum ShopSchedule
{
    /// <summary>钟点区间营业（含「全天」= 0:00–24:00）。</summary>
    ClockRange,

    /// <summary>
    /// 不定期 / 随机（§12.1 的修士坊市、旅行商人）。**M2 一律视为打烊**——
    /// 开张条件（节日？月相？随机事件？）文档没给，编一个出来就是自创设定。
    /// 等到 M3/M6 有事件系统了，由事件把状态推进来。
    /// </summary>
    Irregular,
}
