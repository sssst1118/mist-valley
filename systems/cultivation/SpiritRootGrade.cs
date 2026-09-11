namespace XingGame.Systems.Cultivation;

/// <summary>
/// 灵根品级：§4.2 的六档（<c>docs/public/design.md</c> 162-171 行）。纯数据，无逻辑——
/// 校验在 <see cref="SpiritRootTable"/> 加载时逐条做（照 <c>ItemDefinition</c>/<c>NpcDefinition</c> 的分工）。
/// </summary>
/// <remarks>
/// <para>
/// <b>属性数量存成上下限两个数，不是要「更灵活」</b>：§4.2 那一格给伪灵根写的就是「4-5 种」，
/// 是个区间。存一个 int 就得替文档挑一个数，而挑出来的那个数将来没人分得清是文档定的还是我们定的。
/// 其余五档上下限相同，恰好等于文档写的单个值。
/// </para>
/// <para>
/// <b>三个概率的单位是「百分比」，不是 0-1 的小数</b>：文档给的是 5% / 0.1% / 100%。
/// 换算成小数写进数据表，等于每个读表的人都要在脑子里做一次换算，而 0.1% 这种值一旦有人
/// 看成 0.1（=10%）就会静默差 100 倍。用 <see cref="double"/> 是因为 0.1% 不是整数。
/// </para>
/// <para>
/// <b>「每层所需修为」不在这里（刻意的，不是漏做）</b>：档位管的是倍率、进度曲线管的是开销，
/// 两者出处不同（§4.2 的倍率 vs 未定义项备案 #67 的推导）。那组数落在 M3-2 的
/// <see cref="CultivationSpeedTable"/> 上；本记录只有 §4.2 那张表里的列。
/// 钉住这条分工的用例在 <c>tests/M3Audit_Cultivation.cs</c>。
/// </para>
/// </remarks>
public sealed record SpiritRootGrade(
    string Id,
    string Name,
    int AttributeCountMin,
    int AttributeCountMax,
    double CultivationSpeedMultiplier,
    double FoundationSuccessPercent,
    double GoldenCoreSuccessPercent,
    double NascentSoulSuccessPercent)
{
    /// <summary>这一档的灵根是不是「几种属性」（伪灵根的 4-5 种）。</summary>
    public bool HasAttributeCountRange => AttributeCountMin != AttributeCountMax;
}
