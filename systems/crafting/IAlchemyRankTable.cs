using System.Collections.Generic;

namespace XingGame.Systems.Crafting;

/// <summary>
/// 炼丹师等级表（§8.7，<c>docs/public/design.md</c> 第 958-970 行）：一品到九品，每品答「可炼制丹药品阶」。
/// 数据在 <c>data/crafting/alchemy_ranks.json</c>，只读、进程内共享一份（同 <c>IRecipeTable</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>它只回答「几品能炼几阶」</b>，不回答「这位玩家现在能炼什么」——后者是状态，在
/// <c>ICraftingSystem.AlchemyRank</c> 上、进存档。表是数据、状态是状态，同
/// <c>ISpiritRootTable</c> 与 <c>CultivationSystem</c> 的分工。
/// </para>
/// <para>
/// <b>它也不是 §9.1 那张技能列表里的「炼丹」技能</b>：§9.1/§9.2/§9.3 的「炼丹」是一条要攒经验、
/// 10 级封顶、5 级与 10 级各有分支的技能，而 §8.7 的「炼丹师等级」是九品、每品一条能力上限。
/// 两套刻度对不上（品是 1-9，技能级是 0-10；§9.3 的「炼丹 10 级：丹仙（可炼制圣丹）」与 §8.7 的
/// 「九品炼丹宗师：九阶（= 圣丹）」说的是同一件事的两遍）。文档没有一句话把两者等同起来，
/// 所以本表只录 §8.7 那一套——<b>技能怎么升品级、技能等级是否同时抬高上限，是文档未定义项</b>，
/// 等 §9 生活技能系统落地时由用户裁决，本切片一个数都不编。
/// </para>
/// <para>
/// <b>没有炼器师等级表</b>（刻意不做，不是漏了）：§8.7 的炼器师表是「品级 → 可炼制法宝等级
/// （下品法器 / 中品灵器 / … / 至宝）」，而今天一件法宝物品都没有、也没有装备系统，那张表的每一行
/// 都答不出任何有人问的问题。<b>等有了法宝物品再加</b>——那时它连同「法宝等级五档」一起落地。
/// </para>
/// </remarks>
public interface IAlchemyRankTable
{
    /// <summary>九品，按品级升序（一品在前，与 §8.7 的表序一致）。</summary>
    IReadOnlyList<AlchemyRankDefinition> All { get; }

    /// <remarks>
    /// 认不出的品级 <b>不抛</b>，由调用方分岔：存档里对不上是数据错误（<c>InvalidDataException</c>），
    /// 调用方给的品级越界是编程错误（<c>ArgumentOutOfRangeException</c>），两种要抛的异常不同
    /// （同 <c>ISpiritRootTable.TryGetGrade</c>）。
    /// </remarks>
    bool TryGet(int rank, out AlchemyRankDefinition definition);

    /// <summary>
    /// 炼得出 <paramref name="tier"/> 阶丹药<b>所需的最低品级</b>——§8.7 那句「一品炼丹学徒 可炼制
    /// 一阶」的机器可读形态。今天表里每品各对应同号的一阶，所以答案等于阶号；但这是<b>表的输出</b>，
    /// 不是一条写死的等式（文档若改成「三品可炼四阶」，改的是表）。
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// <paramref name="tier"/> 不是正数，或<b>没有哪一品炼得出来</b>（§8.7 的表到九阶为止，
    /// 十阶神丹起文档写着「仅仙界存在」「凡界无法炼制」）。
    /// </exception>
    int RequiredRankForTier(int tier);
}
