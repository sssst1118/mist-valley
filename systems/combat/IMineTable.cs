using System.Collections.Generic;

namespace XingGame.Systems.Combat;

/// <summary>静态矿洞表。只读，进程内共享一份。</summary>
public interface IMineTable
{
    IReadOnlyCollection<MineDefinition> All { get; }

    bool TryGet(string id, out MineDefinition definition);

    /// <summary>找不到抛 <see cref="KeyNotFoundException"/>，消息里带 id。</summary>
    MineDefinition Get(string id);
}
