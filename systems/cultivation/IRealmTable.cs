using System.Collections.Generic;

namespace XingGame.Systems.Cultivation;

/// <summary>静态境界表。只读，进程内共享一份。</summary>
public interface IRealmTable
{
    /// <summary>
    /// 九大境界，**按 §8.1 的表序**（炼气 → 筑基 → 金丹 → 元婴 → 化神 → 炼虚 → 合体 → 大乘 → 渡劫）。
    /// 这个顺序就是修炼顺序，也是门槛比较的方向，不是随便排的。
    /// </summary>
    IReadOnlyList<RealmDefinition> Realms { get; }

    bool TryGet(string id, out RealmDefinition realm);

    /// <summary>找不到抛 <see cref="KeyNotFoundException"/>，消息里带 id。</summary>
    RealmDefinition Get(string id);

    /// <summary>大境界在 <see cref="Realms"/> 里的下标（从 0 起）。比较两个境界谁高谁低时用它。</summary>
    /// <exception cref="KeyNotFoundException">表里没有这个境界。</exception>
    int OrderOf(string realmId);

    /// <summary>§8.2 的三条「游戏绑定」，按文档顺序。</summary>
    IReadOnlyCollection<CultivationGateRequirement> Gates { get; }

    /// <summary>
    /// 某一条门槛要求的大境界与层数。
    /// </summary>
    /// <exception cref="KeyNotFoundException">表里没有这一条——枚举有而数据表没录，是数据缺口，不猜。</exception>
    CultivationGateRequirement RequirementOf(CultivationGate gate);
}
