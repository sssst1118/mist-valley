using System.Collections.Generic;

namespace XingGame.Systems.Cultivation;

/// <summary>静态灵根表。只读，进程内共享一份。</summary>
public interface ISpiritRootTable
{
    /// <summary>六档品级，按 §4.2 的表序（由差到好）。</summary>
    IReadOnlyCollection<SpiritRootGrade> Grades { get; }

    /// <summary>八种点过名的具体灵根（§4.3 四种变异 + §4.4 四种先天异）。</summary>
    IReadOnlyCollection<SpiritRootDefinition> Roots { get; }

    bool TryGetGrade(string id, out SpiritRootGrade grade);

    /// <summary>找不到抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>，消息里带 id。</summary>
    SpiritRootGrade GetGrade(string id);

    bool TryGetRoot(string id, out SpiritRootDefinition root);

    /// <summary>找不到抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>，消息里带 id。</summary>
    SpiritRootDefinition GetRoot(string id);
}
