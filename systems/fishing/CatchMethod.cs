namespace XingGame.Systems.Fishing;

/// <summary>
/// 捕获方式。§7.3 给了两套：鱼竿钓鱼、蟹笼收取。
/// </summary>
/// <remarks>
/// 分成两种而不是「都是鱼」：§7.3 的蟹笼产出是龙虾、螃蟹、虾——它们由「放置在水域、每日收取」
/// 获得，不是钓上来的。不分开，掷鱼就会让玩家用鱼竿钓出一只龙虾。
/// </remarks>
public enum CatchMethod
{
    /// <summary>鱼竿（§7.3「操作：蓄力抛竿 → 等待鱼漂下沉 → 点击收杆 → 小游戏」）。</summary>
    Rod,

    /// <summary>蟹笼（§7.3「放置在水域，每日收取龙虾、螃蟹、虾等」）。</summary>
    CrabPot,
}
