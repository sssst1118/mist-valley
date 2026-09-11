using System.Collections.Generic;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 灵脉与福地这两张表（§8.8）：灵脉六级（每级一个灵气浓度乘数）+ 福地九阶（只有名称与说明）。
/// 数据在 <c>data/cultivation/spirit_land.json</c>，只读、进程内共享一份。
/// </summary>
/// <remarks>
/// <para>
/// <b>两张表装在同一个接口里，因为它们问的是同一件事的两半</b>——「这座农场的灵气有多浓」：
/// 灵脉那一半给数，福地那一半给资格（可种植灵植 / 可建造修炼室 / 不受外界天气影响…），
/// 而 §8.8 末尾的「游戏绑定」本来就把两者写在同一段里（「农场初始为『微型灵脉 + 一阶福地』」）。
/// 拆成两个接口、两份数据文件，等于让每一份存档里的「灵脉三级 + 福地二阶」都要跨两个对象去凑，
/// 而它们永远是一起被读、一起被写的。
/// </para>
/// <para>
/// <b>表只答「有哪几级、每级是什么」，不答「玩家现在在哪一级」</b>：后者是状态，在
/// <see cref="ISpiritLandSystem"/> 上、进存档。同 <c>ISpiritRootTable</c> 与
/// <c>CultivationSystem</c> 的分工——表是数据，状态是状态，改数值不该碰存档，反之亦然。
/// </para>
/// </remarks>
public interface ISpiritLandTable
{
    /// <summary>六级灵脉，按 §8.8 的表序（由弱到强，浓度乘数逐级递增）。</summary>
    IReadOnlyList<SpiritVeinGrade> Veins { get; }

    /// <summary>九阶福地，按阶号升序（一阶在前）。</summary>
    IReadOnlyList<BlessedLandGrade> Lands { get; }

    /// <remarks>
    /// 认不出的 id **不抛**，由调用方分岔：构造参数里写错是编程错误、存档里对不上是数据错误，
    /// 两种要抛的异常不同（同 <c>ISpiritRootTable.TryGetGrade</c> 与
    /// <see cref="CultivationSystem"/> 的「构造与读档共用一份查表逻辑」）。
    /// </remarks>
    bool TryGetVein(string veinId, out SpiritVeinGrade vein);

    /// <inheritdoc cref="TryGetVein"/>
    bool TryGetLand(string landId, out BlessedLandGrade land);
}
