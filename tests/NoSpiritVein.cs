using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 替身：一个**不含灵脉加成**的修炼处（灵气浓度恒 1.0）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：M3-5 起 <see cref="CultivationSystem.SpeedMultiplierAt"/> 会乘上第四项
/// （§8.8 的灵气浓度），而 §4.2 / §8.3 与备案 #67/#68 那几组数**都不含灵脉**（它们都在前）。
/// 一批已经在量的用例（打坐一小时的收益、倍率相乘不相加、一个春季走完炼气期）里每个数都逐项
/// 对得上文档的出处，喂一个 ×1.0 的替身，那些数就不用改，也不会让人以为「11 点」里有灵脉的一份
/// ——同 <c>MeditationTests</c> 用替身表量边界的做法。
/// </para>
/// <para>
/// <b>真表那一侧的账由 <c>SpiritLandTests</c> 量</b>：微型 1.10 … 龙脉 6.00，以及
/// 「灵脉升一级、同一时刻同一灵根练得更快」那条跨模块断言。
/// </para>
/// <para>
/// <b>1.0 是替身，不是游戏里会出现的状态</b>：§8.8 说农场初始就是微型灵脉（+10%），
/// 所以真玩家那里的乘数永远 ≥ 1.10。它不是「缺省值」，只是「把这一项暂时拿掉」。
/// </para>
/// </remarks>
internal sealed class NoSpiritVein : ISpiritVeinSource
{
    public double DensityMultiplier => 1.0;
}
