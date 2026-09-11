using System.Collections.Generic;

namespace XingGame.Systems.Crafting;

/// <summary>
/// 制作：按配方把材料变成成品（§12.3）。
/// </summary>
/// <remarks>
/// <para>
/// 已解锁的配方是<b>玩家档案</b>的一部分（§12.3：配方来源是技能升级、任务奖励、商店购买、探索发现），
/// 所以本接口持有那份状态，实现类同时实现 <c>ISaveable</c>。「怎么解锁」——技能表、任务、商店都还不存在
/// （M3/M6），本切片只留 <see cref="Unlock"/> 这个入口。
/// </para>
/// <para>
/// <b>配方表本身不在这里</b>：静态数据归 <see cref="IRecipeTable"/>，桥接层各取所需（要做配方列表 UI 就取
/// 配方表，要制作就取本接口），不必从一个口子把两件事都掏出来。
/// </para>
/// </remarks>
public interface ICraftingSystem
{
    /// <summary>已解锁的配方 id，按 Ordinal 升序。是快照，调用方改不动内部状态。</summary>
    IReadOnlyCollection<string> UnlockedRecipes { get; }

    /// <summary>
    /// 炼丹品级（§8.7 的炼丹师等级）：一品 = 1 … 九品 = 9。<b>是状态，进存档</b>；
    /// 「这一品能炼到几阶」由品级表现算（<see cref="IAlchemyRankTable.RequiredRankForTier"/>），不存
    /// ——同一个事实不存两处。
    /// </summary>
    int AlchemyRank { get; }

    /// <summary>这条配方要几品炼丹师才能炼；<b>null = 这一味不吃品级门槛</b>（今天只有丹药吃，§8.7）。</summary>
    /// <remarks>
    /// 「需要什么技能」的答案也在这里：品级行的名字本就带着手艺名（「二品炼丹师」），要显示给玩家就去
    /// <see cref="IAlchemyRankTable"/> 取那一行。<b>炼器那一侧今天没有对应成员</b>——一件法宝物品都没有，
    /// 问了也没人答得出（见 <see cref="IAlchemyRankTable"/> 的类注释）。
    /// </remarks>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">配方 id 不在配方表里时。</exception>
    int? RequiredAlchemyRank(string recipeId);

    /// <summary>设定炼丹品级。<b>品级表里没有的值当场抛</b>——那不是「等级不够」这种正常结果，是调用方算错了。</summary>
    /// <returns>
    /// 值没变时返回 false（同 <see cref="Unlock"/>：存档往返、任务重跑都可能再设一次），不是错误。
    /// </returns>
    /// <remarks>
    /// <b>这是本切片唯一的入口，也是刻意的</b>：§9.1 的「炼丹」技能（攒经验 → 升级）还不存在，
    /// 同 <see cref="Unlock"/> 当年只留一个入口的处境。<b>怎么升级、几品算门槛，本切片一个数都不编。</b>
    /// </remarks>
    /// <exception cref="System.ArgumentOutOfRangeException">品级不在品级表的范围内时。</exception>
    bool SetAlchemyRank(int rank);

    /// <summary>配方 id 不在配方表里时抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>。</summary>
    bool IsUnlocked(string recipeId);

    /// <summary>
    /// 解锁一条配方。<b>重复解锁返回 false</b>（集合没变），不是错误——存档往返、任务重跑都可能再来一次。
    /// </summary>
    bool Unlock(string recipeId);

    /// <summary>
    /// 能不能做 <paramref name="count"/> 份：配方表里有、已解锁、<b>品级够</b>、材料够、背包放得下产物，
    /// 五条全中才算能。配方 id 不在配方表里时抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>。
    /// </summary>
    bool CanCraft(string recipeId, int count = 1);

    /// <summary>
    /// 做 <paramref name="count"/> 份：扣材料、进成品，<b>全有或全无</b>。
    /// </summary>
    /// <returns>
    /// 没做成时返回 false，且<b>什么都没变</b>——材料一个都没扣，成品一个都没进（ADR-012 的全有或全无：
    /// 扣了一半是最难查的那种 bug，调用方以为失败了、物品却已经少了）。
    /// 品级不够也是「没做成」的一种（§8.7：一品炼丹学徒 可炼制 一阶），一样什么都不动。
    /// </returns>
    /// <remarks>
    /// <paramref name="count"/> 非正时抛 <see cref="System.ArgumentOutOfRangeException"/>；
    /// 配方 id 不在配方表里时抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>。
    /// </remarks>
    bool TryCraft(string recipeId, int count = 1);
}
