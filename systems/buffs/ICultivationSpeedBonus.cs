using XingGame.Core.Time;

namespace XingGame.Systems.Buffs;

/// <summary>
/// 打坐要读的那一项：限时增益对修炼速度的总修正（§8.3 表里的「丹药」那一行，如聚气散 +50%）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是窄接口，而不是把整个 <see cref="IBuffSystem"/> 交给打坐</b>：打坐只欠这一个数，
/// 而它一旦拿到整个系统就能顺手查移速、清增益、碰存档格式——那些都不该是修炼领域的事
/// （同 <c>ISpiritVeinSource</c>：打坐只看那一个浓度，不收整个农场状态）。
/// </para>
/// <para>
/// <b>没有生效的增益时恒为 1.0</b>：它是乘法的单位元，不是「没有这一项」——合成器那一行永远乘
/// 五项，没有增益就是 ×1，所以调用方不必为它写分支。
/// </para>
/// </remarks>
public interface ICultivationSpeedBonus
{
    /// <summary>
    /// 此刻的修炼速度修正：把 <paramref name="now"/> 这一刻生效的修炼类增益连乘起来
    /// （一条都没有时是 1.0）。
    /// </summary>
    double MultiplierAt(GameTime now);
}
