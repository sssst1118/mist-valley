using System.Collections.Generic;
using XingGame.Systems.Items;

namespace XingGame.Systems.Combat;

/// <summary>
/// 一次遭遇的结算结果。
/// </summary>
/// <remarks>
/// <para>
/// <b>恒等式</b>：<see cref="Loot"/> 与 <see cref="Overflow"/> 逐项相加 == 该怪物掉落的总量。
/// 背包满时没能进去的那部分<b>留在原地（记在 <see cref="Overflow"/> 里），不静默吞掉</b>——
/// 「打死怪、东西没了、界面也不说」是最让人恼火的一类 bug，而且现场过后无从查起。
/// </para>
/// <para>
/// <b>败北时两串都为空</b>：§7.2 的掉落是击杀掉落，没打赢就没有掉落。
/// 败北的惩罚（§7.1「生命值归零则昏倒，损失金币和物品」）不由本模块执行：金币归经济模块、
/// 物品归背包，本模块只报「败北」这个事实，扣什么是 M2 集成时的事。
/// </para>
/// <para>
/// 血量一律钳到 0 为止：<b>不会出现负血</b>，也不会出现「怪物血量还剩负数还在打」这种状态。
/// </para>
/// </remarks>
public sealed record CombatResult(
    string MonsterId,
    bool Victory,
    int ChallengerRemainingHealth,
    int MonsterRemainingHealth,
    int Rounds,
    IReadOnlyList<ItemStack> Loot,
    IReadOnlyList<ItemStack> Overflow);
