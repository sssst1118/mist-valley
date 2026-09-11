namespace XingGame.Systems.Cultivation;

/// <summary>
/// 灵力随时间回的两种速率（ARCHITECTURE 未定义项备案 #71）：清醒 2/游戏小时、打坐 5/游戏小时。
/// </summary>
/// <remarks>
/// <para>
/// <b>枚举成员名就是数据表里的 id</b>（<c>data/cultivation/spirit_power.json</c> 按名字逐字对，
/// 不收数字）——同 <c>Season</c> 与 <see cref="CultivationGate"/> 的写法。
/// </para>
/// <para>
/// <b>睡眠刻意不在这里（不是漏做）</b>：备案 #71 的第三条是「睡眠全恢复」——它没有费率、
/// 也没有时长可言（<c>ITimeService.Sleep</c> 是**跳跃**而非流逝），塞进同一个枚举会让时长那个
/// 参数在那一档变成被忽略的摆设，而调用方还以为自己传的时长算数。它是
/// <see cref="ICultivationSystem.RecoverSpiritOnSleep"/> 那一个零参数入口。
/// </para>
/// </remarks>
public enum SpiritRecovery
{
    /// <summary>清醒（在忙别的事）：2 灵力/游戏小时（备案 #71）。</summary>
    Awake,

    /// <summary>打坐（§3.1 清晨那一项活动）：5 灵力/游戏小时（备案 #71）。</summary>
    Meditation,
}
