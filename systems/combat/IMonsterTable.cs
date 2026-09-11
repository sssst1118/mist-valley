using System.Collections.Generic;

namespace XingGame.Systems.Combat;

/// <summary>静态怪物表。只读，进程内共享一份。</summary>
public interface IMonsterTable
{
    IReadOnlyCollection<MonsterDefinition> All { get; }

    bool TryGet(string id, out MonsterDefinition definition);

    /// <summary>找不到抛 <see cref="KeyNotFoundException"/>，消息里带 id。</summary>
    MonsterDefinition Get(string id);

    /// <summary>
    /// 该层可能出现的怪物，按 <c>monsters.json</c> 里的顺序（稳定顺序，掷点才有可复现的前提）。
    /// </summary>
    /// <param name="layer">矿洞层号，从 1 起。小于 1 抛 <see cref="ArgumentOutOfRangeException"/>。</param>
    IReadOnlyList<MonsterDefinition> ForLayer(int layer);
}
