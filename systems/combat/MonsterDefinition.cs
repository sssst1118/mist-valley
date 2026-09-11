using System.Collections.Generic;
using XingGame.Systems.Items;

namespace XingGame.Systems.Combat;

/// <summary>
/// 一只怪物。数据在 <c>data/combat/monsters.json</c>，出处与「文档没给什么」见 <see cref="MonsterTable"/> 类注释。
/// </summary>
/// <remarks>
/// <para>
/// <b>血量为 0 是「文档未给，待补」的哨兵值，不是「一滴血的怪」。</b>§7.1 只给了六个怪物名、
/// §8.9 只给了妖兽阶位与类型的规则，两者都没有任何一只怪物的数值。补数据前先去看文档，
/// 不要照着别处的数字把它填满。
/// </para>
/// <para>
/// <b>出现层数同样未给</b>：<see cref="MinLayer"/> 与 <see cref="MaxLayer"/> 同时为 0 表示
/// 「文档未给层数」→ 任何层都可能出现（§7.1 只说这些怪物在矿洞里，没说是哪几层）。
/// 只给一个端点是数据错误，加载时就抛。
/// </para>
/// </remarks>
public sealed record MonsterDefinition(
    string Id,
    string Name,
    int MaxHealth,
    int Attack,
    int MinLayer,
    int MaxLayer,
    IReadOnlyList<ItemStack> Drops)
{
    /// <summary>
    /// 该层是否可能出现。文档未给层数（两端都是 0）时任何层都算可能出现——
    /// 「未给」不等于「哪层都没有」，否则默认表里的怪物永远掷不出来。
    /// </summary>
    public bool AppearsAt(int layer)
    {
        if (MinLayer == 0 && MaxLayer == 0) return true;

        return layer >= MinLayer && layer <= MaxLayer;
    }
}
