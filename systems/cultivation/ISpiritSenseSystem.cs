namespace XingGame.Systems.Cultivation;

/// <summary>
/// 灵气感知（§8.2 炼气 1-3 层的那条生活法术）：**它买的是「看得见」本身**——解锁之后，
/// 玩家能读到灵田的灵气浓度；没解锁则什么都读不到。
/// </summary>
/// <remarks>
/// <para>
/// <b>与 <see cref="ILifeSpellSystem"/> 分开，因为它是唯一一条不落在格子上的法术</b>：
/// 灵气浇灌 / 灵雨术 / 灵锄术都对着某一格放，而感知不对着任何东西放——它的产物是一份读数。
/// 硬把它塞进 <c>TryCastAt</c> 就得给「法术没有落点」编一个返回值（真 / 假的布尔都说不通），
/// 所以 <see cref="ILifeSpellSystem.TryCastAt"/> 对它是当场抛，读数这条路在这里。
/// </para>
/// <para>
/// <b>解锁判定复用 <see cref="ILifeSpellSystem.IsUnlocked"/>，这里不自己比层号</b>：门槛只有一个
/// 来源（法术表的解锁层数 × 玩家的境界），多写一份判据迟早会漂，而漂了就是「都四层了还看不见灵气」
/// 这种玩家没法自己解决的问题。
/// </para>
/// <para>
/// <b>本接口不带状态、也不进存档</b>：解锁是拿层数现算的，浓度是从灵脉等级现算的，两者都已经
/// 在各自的存档里（<c>cultivation</c> 与 <c>spirit_land</c>）。再存一份「看得见什么」，就是同一个
/// 事实存三处。
/// </para>
/// </remarks>
public interface ISpiritSenseSystem
{
    /// <summary>此刻能不能感知灵气：表里那条感知法术解锁了没有。</summary>
    /// <remarks>
    /// 单独给一个查询，是为了让界面能分清两件事：「还没解锁」（界面上锁、给一句提示）与
    /// 「解锁了但什么都没看到」（今天不会发生，将来可能有别的条件）。只看
    /// <see cref="Read"/> 是不是 null 的话，这两种情况在代码里长得一模一样。
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>
    /// 看一眼灵田的灵气。**没解锁就是 null**——这条法术买的正是「看得见」本身，
    /// 所以「没解锁」不是错误、也不抛（同 <c>Farmland.CanWater</c> 那类查询的语义：
    /// 不能做是正常结果，不是异常）。
    /// </summary>
    SpiritSenseReading? Read();
}
