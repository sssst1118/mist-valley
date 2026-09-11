using XingGame.Core.Time;

namespace XingGame.Systems.Fishing;

/// <summary>
/// 钓鱼：按当下的季节/天气/时段掷出钓到的鱼、判定上钩、把鱼放进背包。
/// </summary>
/// <remarks>
/// <b>钓鱼小游戏（蓄力抛竿、进度条）M2 不做</b>——ARCHITECTURE「接口契约 · M2-A」把它留到 M8 打磨。
/// 所以本接口回答的只是「这一竿钓不钓得到、钓到什么」，「怎么甩、怎么收」不在这里。
/// </remarks>
public interface IFishingSystem
{
    /// <summary>
    /// 掷一竿鱼：返回本竿有没有鱼上钩，以及上钩的是哪条。<b>纯函数</b>——不碰背包、不改图鉴，
    /// 同样的参数必得同样的结果。
    /// </summary>
    /// <param name="castIndex">
    /// 第几竿。由调用方给（桥接层用当前游戏时刻或本存档的抛竿计数），本系统不持有它，
    /// 所以它不必进存档：同一天同一时段连甩两竿该出不同的鱼，靠的就是这个参数在变。
    /// </param>
    /// <param name="seed">世界种子（随存档走，同 ADR-006 的天气种子）。</param>
    /// <returns>该条件下一条候选鱼都没有时为 <c>false</c>，此时 <paramref name="fish"/> 为 null。</returns>
    bool TryRoll(Season season, Weather weather, DayPhase phase, int castIndex, int seed, out FishDefinition fish);

    /// <summary>
    /// 收竿：掷鱼 → 鱼进背包 → 记图鉴。
    /// </summary>
    /// <remarks>
    /// <b>全有或全无</b>（同 <c>Inventory.Remove</c>）：背包放不下就整条不钓，也不记图鉴——
    /// 「图鉴记了、鱼没了」比钓不到更莫名其妙。
    /// </remarks>
    bool TryCatch(Season season, Weather weather, DayPhase phase, int castIndex, int seed, out FishDefinition fish);
}
