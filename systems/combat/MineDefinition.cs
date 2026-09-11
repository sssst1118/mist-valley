using System.Collections.Generic;

namespace XingGame.Systems.Combat;

/// <summary>
/// 一座矿洞。数值全部来自 <c>data/combat/mines.json</c>，逐条对应 §7.1 矿洞那一节（出处见 <see cref="MineTable"/> 类注释）。
/// </summary>
/// <remarks>
/// <b>只建模文档给了间隔的那几项</b>：层数、电梯、宝箱、灵气层。宝箱里装什么、特殊层怎么生成、
/// 矿石分布在哪几层——文档一个字都没给，本类也不留空字段等着填（留着就是一堆没人读的 0）。
/// </remarks>
public sealed record MineDefinition(
    string Id,
    string Name,
    int LayerCount,
    int ElevatorInterval,
    int ChestInterval,
    int AuraInterval,
    int AuraBonusPercent,
    IReadOnlyList<string> Ores)
{
    /// <summary>层号是否落在矿洞内（1..<see cref="LayerCount"/>）。0 是地面，不是矿洞的层。</summary>
    public bool Contains(int layer) => layer >= 1 && layer <= LayerCount;

    /// <summary>§7.1「每 10 层有电梯」——电梯层的层号是间隔的整数倍，0 层（地面）不算电梯层。</summary>
    public bool IsElevatorLayer(int layer) => Contains(layer) && layer % ElevatorInterval == 0;

    /// <summary>§7.1「每 10 层有宝箱」。</summary>
    public bool IsChestLayer(int layer) => Contains(layer) && layer % ChestInterval == 0;

    /// <summary>§7.1「每 30 层有灵气浓郁区域，修炼速度 +50%」。</summary>
    public bool IsAuraLayer(int layer) => Contains(layer) && layer % AuraInterval == 0;
}
