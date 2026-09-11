using XingGame.Core.Time;
using XingGame.Systems.Buffs;

namespace XingGame.Tests;

/// <summary>
/// 替身：**没有任何限时增益**（修炼速度修正恒 1.0）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：M3-6 起 <see cref="XingGame.Systems.Cultivation.CultivationSystem.SpeedMultiplierAt"/>
/// 会乘上第五项（§8.3 的「丹药」那一行），而 §4.2 / §8.3 与备案 #67/#68 那几组数**都不含丹药**
/// （它们都在前）。一批已经在量的用例（打坐一小时的收益、倍率相乘不相加、一个春季走完炼气期）
/// 里每个数都逐项对得上文档的出处，喂一个 ×1.0 的替身，那些数就不用改，也不会让人以为「11 点」
/// 里有聚气散的一份——同 <c>NoSpiritVein</c> 与各表替身的分工。
/// </para>
/// <para>
/// <b>真表那一侧的账由 <c>BuffSystemTests</c> 量</b>：聚气散 ×1.50、灵芽羹 ×1.20，
/// 以及「吃了聚气散之后同一时刻打坐真的更快」那条跨模块断言。
/// </para>
/// <para>
/// <b>1.0 是替身，不是游戏里会出现的状态</b>（乘法的单位元）：真玩家那里的这个乘数要么是 1.0
/// （身上没有增益）要么大于 1——但「没有增益」在真表下也是 1.0，与替身恰好同值。
/// 所以它不是「缺省值」，只是「把这一项固定成没有增益」。
/// </para>
/// </remarks>
internal sealed class NoSpeedBonus : ICultivationSpeedBonus
{
    public double MultiplierAt(GameTime now) => 1.0;
}
