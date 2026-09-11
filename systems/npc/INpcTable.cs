using System.Collections.Generic;

namespace XingGame.Systems.Npc;

/// <summary>静态 NPC 表。只读，进程内共享一份。</summary>
public interface INpcTable
{
    /// <summary>按 JSON 里的顺序排列，与 `All` 一致的顺序也是 UI 列表的顺序。</summary>
    IReadOnlyCollection<NpcDefinition> All { get; }

    bool TryGet(string id, out NpcDefinition definition);

    /// <summary>找不到抛 <see cref="KeyNotFoundException"/>，消息里带 id。</summary>
    NpcDefinition Get(string id);
}
