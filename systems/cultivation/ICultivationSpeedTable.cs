using System.Collections.Generic;
using XingGame.Core.Time;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 打坐这张账：基础速度 + 逐层升层开销 + §8.3 里已经接得上的两个倍率（季节、时辰）。
/// 数据在 <c>data/cultivation/cultivation_speed.json</c>，只读、进程内共享一份。
/// </summary>
/// <remarks>
/// <para>
/// <b>倍率的合成不在这里</b>：本接口只回答「这一项是多少」，连乘在
/// <see cref="CultivationSystem.SpeedMultiplierAt"/> 那一处——三个因素分别来自灵根与时间，
/// 摊进表里会把「谁乘谁」藏进两个模块之间。
/// </para>
/// <para>
/// <b>§8.3 表里还有七个因素没有对应的表项</b>（灵脉等级 / 聚灵阵 / 风水 / 功法品阶 / 丹药 /
/// 心境 / 双修）：各自的系统都还不存在，先建字段就是建一批没人读的数，且与真数据长得一模一样。
/// 它们跟着各自的系统一起落地，见 <c>tests/M3Audit_Cultivation.cs</c> 的反射钉子。
/// </para>
/// </remarks>
public interface ICultivationSpeedTable
{
    /// <summary>打坐的基础速度，单位「修为 / 游戏小时」（ARCHITECTURE 未定义项备案 #68：<c>10</c>）。</summary>
    int BasePointsPerHour { get; }

    /// <summary>§8.3 的两段特殊时辰（子时 / 午时），按文档顺序。</summary>
    IReadOnlyList<HourBand> HourBands { get; }

    /// <summary>
    /// 本表给不给这个境界录了逐层升层开销。
    /// </summary>
    /// <remarks>
    /// 只有炼气期有（见类注释）：其余境界的晋升是 §8.4 的跨大境界突破，要丹药与天材地宝，
    /// 不是攒够修为自动升——所以那些境界上「攒修为」这件事本身不成立。
    /// </remarks>
    bool Covers(string realmId);

    /// <summary>
    /// 从第 <paramref name="stage"/> 层升到下一层所需的修为（第 n 条 = <c>10 × n</c>，备案 #67）。
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// 层号越界，或这一层没有「下一层」（顶点）——两种都当场抛，不返回 0：
    /// 0 会让调用方以为「攒够了」，于是所有顶点都被判成可升层。
    /// </exception>
    int PointsToAdvance(int stage);

    /// <summary>
    /// 季节倍率（§8.3）：春 1.10 / 夏 1.05 / 秋 1.10 / 冬 0.90。
    /// </summary>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// 表里没录这个季节——枚举有而数据没有是数据缺口，不猜（同 <c>IRealmTable.RequirementOf</c>）。
    /// 加载时已经要求四季齐全，所以走到这里就是有人给枚举加了新的季节却没补数据。
    /// </exception>
    double SeasonMultiplier(Season season);

    /// <summary>此刻的时辰倍率（§8.3）：落在某段时辰带里就是它的倍率，都不落在就是 <c>1.0</c>（平峰）。</summary>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="hour"/> 不是 0..23。</exception>
    double HourMultiplier(int hour);
}
