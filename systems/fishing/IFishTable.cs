using System.Collections.Generic;
using XingGame.Core.Time;

namespace XingGame.Systems.Fishing;

/// <summary>静态鱼表。只读，进程内共享一份。</summary>
public interface IFishTable
{
    /// <summary>按 JSON 里的顺序排列，图鉴与调试列表直接照用。</summary>
    IReadOnlyCollection<FishDefinition> All { get; }

    bool TryGet(string fishId, out FishDefinition definition);

    /// <summary>找不到抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>，消息里带 id。</summary>
    FishDefinition Get(string fishId);

    /// <summary>
    /// 给定条件与捕获方式下的候选，<b>按表内顺序</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 顺序必须是确定的：掷鱼靠「权重前缀和」落在哪个候选上，候选顺序一变，同一组参数就会
    /// 掷出不同的鱼——存档回放与测试的可复现性都建在这条上（同 <c>ItemTable</c> 排序取文件的理由）。
    /// </para>
    /// <para>
    /// 捕获方式要传进来而不是写死：M2 的鱼竿掷鱼传 <see cref="CatchMethod.Rod"/>，而 §7.3 的蟹笼产出
    /// 走 <see cref="CatchMethod.CrabPot"/>——蟹笼的放置与每日收取是 M2 之后的事，但鱼表这一层
    /// 现在就得把两者分开，否则「用鱼竿钓上一只龙虾」从第一天起就是对的。
    /// </para>
    /// </remarks>
    IReadOnlyList<FishDefinition> Candidates(Season season, Weather weather, DayPhase phase, CatchMethod method);
}
