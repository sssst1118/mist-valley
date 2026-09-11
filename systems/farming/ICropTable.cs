using System.Collections.Generic;

namespace XingGame.Systems.Farming;

/// <summary>
/// 静态作物表。只读，进程内共享一份。
/// </summary>
/// <remarks>
/// 主键是<b>种子 id</b>（ADR-013）：种植系统拿到手的是玩家选中的那粒种子，
/// 而收获时给回背包的是作物 id，所以两个方向各有一个查询入口。
/// </remarks>
public interface ICropTable
{
    IReadOnlyCollection<CropDefinition> All { get; }

    bool TryGetBySeed(string seedId, out CropDefinition definition);

    /// <summary>找不到抛 <c>KeyNotFoundException</c>，消息里带 seedId。</summary>
    CropDefinition GetBySeed(string seedId);

    /// <summary>按收获产出的作物 id 反查。用于图鉴/任务这类「手里只有作物」的场合。</summary>
    bool TryGetByCrop(string cropId, out CropDefinition definition);
}
