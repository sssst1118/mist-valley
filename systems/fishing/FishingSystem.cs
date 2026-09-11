using System;
using System.Collections.Generic;
using XingGame.Core.Time;
using XingGame.Systems.Items;

namespace XingGame.Systems.Fishing;

/// <summary>
/// 把鱼表接到背包与图鉴上：掷鱼、把钓到的鱼放进背包、记一笔图鉴。
/// </summary>
/// <remarks>
/// <para>
/// 掷鱼的确定性来自「参数哈希 → <see cref="Random"/> 定种构造」，与 <c>WeatherGenerator</c> 同款：
/// 不使用 <c>Random.Shared</c>、不读时钟，所以存档回放与测试都能复现同一竿的结果。
/// </para>
/// <para>
/// 本类<b>不订阅任何事件</b>：钓鱼是玩家动作（按键），不是时间推进的结果，所以没有
/// <c>Dispose</c> 要管（同 ADR-005 对无主订阅的要求——没有订阅就不留退订的坑）。
/// 季节/天气/时段由调用方从 <c>ITimeService</c> 取好传进来，本系统因此不必认识时间服务。
/// </para>
/// </remarks>
public sealed class FishingSystem : IFishingSystem
{
    private readonly IFishTable _fish;
    private readonly IInventory _inventory;
    private readonly FishCodex _codex;

    public FishingSystem(IFishTable fish, IInventory inventory, FishCodex codex)
    {
        _fish = fish ?? throw new ArgumentNullException(nameof(fish));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _codex = codex ?? throw new ArgumentNullException(nameof(codex));
    }

    public bool TryRoll(Season season, Weather weather, DayPhase phase, int castIndex, int seed, out FishDefinition fish)
    {
        IReadOnlyList<FishDefinition> candidates = _fish.Candidates(season, weather, phase, CatchMethod.Rod);
        if (candidates.Count == 0)
        {
            fish = null!;   // 没鱼上钩以返回值 false 表达（同 TryGet 的约定）
            return false;
        }

        int total = 0;
        foreach (FishDefinition candidate in candidates) total += candidate.Weight;

        int roll = new Random(Mix(seed, (int)season, (int)weather, (int)phase, castIndex)).Next(total);

        int cumulative = 0;
        foreach (FishDefinition candidate in candidates)
        {
            cumulative += candidate.Weight;
            if (roll < cumulative)
            {
                fish = candidate;
                return true;
            }
        }

        // 权重已由 FishTable 校验为正，roll < total 恒成立；走到这里说明表被绕过（同 WeatherGenerator）
        throw new InvalidOperationException($"鱼表候选权重合计 {total} 未覆盖 roll={roll}");
    }

    public bool TryCatch(Season season, Weather weather, DayPhase phase, int castIndex, int seed, out FishDefinition fish)
    {
        if (!TryRoll(season, weather, phase, castIndex, seed, out fish)) return false;

        // 一条鱼要么整条进背包，要么这竿算没钓到：Add 返回非 0 时它一个都没放进去，
        // 所以这里不需要回滚，只需要不把图鉴记上
        if (_inventory.Add(fish.ItemId, 1) != 0)
        {
            fish = null!;
            return false;
        }

        _codex.Record(fish.Id);
        return true;
    }

    /// <summary>
    /// 把 (seed, 季节, 天气, 时段, 竿序) 混成随机种子。末尾的雪崩步是必要的：
    /// <see cref="Random"/> 的定种构造对相邻种子会产生相关的首个输出，直接用线性组合会让
    /// 相邻两竿、相邻时段高度雷同（同 <c>WeatherGenerator.Mix</c>）。
    /// </summary>
    private static int Mix(int seed, int season, int weather, int phase, int castIndex)
    {
        unchecked
        {
            int hash = seed;
            hash = (hash * 31) + season;
            hash = (hash * 31) + weather;
            hash = (hash * 31) + phase;
            hash = (hash * 31) + castIndex;
            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= unchecked((int)0x846ca68b);
            hash ^= hash >> 16;
            return hash;
        }
    }
}
