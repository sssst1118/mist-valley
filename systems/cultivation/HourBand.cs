namespace XingGame.Systems.Cultivation;

/// <summary>
/// 一天里的一段时辰：§8.3 修炼速度体系里「子时 +30% / 午时 +20%」那两行
/// （<c>docs/public/design.md</c> 818 行）。数据在 <c>data/cultivation/cultivation_speed.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>区间闭开、允许跨午夜</b>：§8.3 写的是「子时（23:00-1:00）」——它从今天 23 点走到明天 1 点，
/// 落在自然钟点上就是 23 点与 0 点两个小时，所以 <see cref="FromHour"/> &gt; <see cref="ToHour"/>
/// 正是这种带。拆成「23-24」与「0-1」两条是把同一个事实存两处，改一处漏一处；跨午夜这件事
/// 只在 <see cref="Contains"/> 里判一次（同 <c>RealmBand.Contains</c> 的分工）。
/// </para>
/// <para>
/// <b>存倍率而不是百分数</b>：§8.3 给的是「+30%」，落成 1.30 与
/// <c>SpiritRootGrade.CultivationSpeedMultiplier</c> 同一套写法——乘数拿去直接乘，
/// 不必每个调用方各换算一次。
/// </para>
/// </remarks>
public sealed record HourBand(int FromHour, int ToHour, string Name, double Multiplier)
{
    /// <summary>
    /// 自然钟点 <paramref name="hour"/>（0..23）在不在这段时辰里。右端<b>不含</b>：
    /// 「11:00-13:00」是 11 点与 12 点两个小时，13:00 整已不在午时里。
    /// </summary>
    /// <remarks>
    /// <c>FromHour == ToHour</c> 是空带子（一个钟点都不含）——加载时会拒收，见 <see cref="CultivationSpeedTable"/>。
    /// </remarks>
    public bool Contains(int hour) => FromHour < ToHour
        ? hour >= FromHour && hour < ToHour
        : FromHour > ToHour && (hour >= FromHour || hour < ToHour);
}
