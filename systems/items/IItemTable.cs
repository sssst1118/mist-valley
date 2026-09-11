using System.Collections.Generic;

namespace XingGame.Systems.Items;

/// <summary>静态物品表。只读，进程内共享一份。</summary>
public interface IItemTable
{
    IReadOnlyCollection<ItemDefinition> All { get; }

    bool TryGet(string id, out ItemDefinition definition);

    /// <summary>找不到抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>，消息里带 id。</summary>
    ItemDefinition Get(string id);
}
