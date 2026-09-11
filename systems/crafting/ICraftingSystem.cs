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

    /// <summary>配方 id 不在配方表里时抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>。</summary>
    bool IsUnlocked(string recipeId);

    /// <summary>
    /// 解锁一条配方。<b>重复解锁返回 false</b>（集合没变），不是错误——存档往返、任务重跑都可能再来一次。
    /// </summary>
    bool Unlock(string recipeId);

    /// <summary>
    /// 能不能做 <paramref name="count"/> 份：配方表里有、已解锁、材料够、背包放得下产物，四条全中才算能。
    /// 配方 id 不在配方表里时抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>。
    /// </summary>
    bool CanCraft(string recipeId, int count = 1);

    /// <summary>
    /// 做 <paramref name="count"/> 份：扣材料、进成品，<b>全有或全无</b>。
    /// </summary>
    /// <returns>
    /// 没做成时返回 false，且<b>什么都没变</b>——材料一个都没扣，成品一个都没进（ADR-012 的全有或全无：
    /// 扣了一半是最难查的那种 bug，调用方以为失败了、物品却已经少了）。
    /// </returns>
    /// <remarks>
    /// <paramref name="count"/> 非正时抛 <see cref="System.ArgumentOutOfRangeException"/>；
    /// 配方 id 不在配方表里时抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>。
    /// </remarks>
    bool TryCraft(string recipeId, int count = 1);
}
