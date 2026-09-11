using System.Collections.Generic;
using XingGame.Core.Time;

namespace XingGame.Systems.Fishing;

/// <summary>
/// 一条鱼。条件维度只有设计文档点过名的三个：季节、天气、时段（§7.3「不同水域、时间、天气影响」、
/// §3.2「新鱼类出现」、§3.3 各天气的效果列）。
/// </summary>
/// <remarks>
/// <para>
/// <b>条件列表为空 = 该维度不限</b>，不是「只在某值出现」。文档没给条件的鱼就是全季、全天候可钓——
/// 那是「照抄文档」的结果，不是漏录。
/// </para>
/// <para>
/// <see cref="ItemId"/> 与 <see cref="Id"/> 今天取值相同，但它们是两件事：前者是进背包的物品，
/// 后者是图鉴与存档的键。分开写是有意的——图鉴键跟着鱼走，不该被物品表改名牵着走
/// （同 ADR-012「存档不存序号」的理由：键要经得起表的变化）。
/// </para>
/// </remarks>
/// <param name="Id">鱼表主键，也是图鉴里记的那个 id，如 <c>fish_ghost</c>。</param>
/// <param name="ItemId">钓上来之后进背包的物品 id，如 <c>fish_ghost</c>。加载时校验它必须在物品表里。</param>
/// <param name="Method">按哪种方式获得（见 <see cref="CatchMethod"/>）。</param>
/// <param name="Seasons">出现的季节；空 = 不限。</param>
/// <param name="Weathers">出现的天气；空 = 不限。</param>
/// <param name="Phases">出现的时段；空 = 不限。</param>
/// <param name="Weight">同条件候选之间的相对权重。文档未给出现权重，缺省 <see cref="DefaultWeight"/>。</param>
public sealed record FishDefinition(
    string Id,
    string ItemId,
    CatchMethod Method,
    IReadOnlyList<Season> Seasons,
    IReadOnlyList<Weather> Weathers,
    IReadOnlyList<DayPhase> Phases,
    int Weight)
{
    /// <summary>
    /// 权重缺省值。<b>设计文档没给任何鱼的出现权重或稀有度</b>（§7.3 只有「每季 20+ 种」这种规模描述，
    /// §9.1 的「稀有鱼概率 +10%」是钓鱼技能满级奖励、也不是权重表），所以缺省 1 = 同条件等概率。
    /// 这是本切片定下的，<b>待用户裁决</b>——真正的稀有度表要等设计补（同备案 #30 的先例）。
    /// </summary>
    public const int DefaultWeight = 1;

    /// <summary>该季节是否可能出现。</summary>
    public bool AppearsIn(Season season) => Seasons.Count == 0 || Contains(Seasons, season);

    /// <summary>该天气是否可能出现。</summary>
    public bool AppearsIn(Weather weather) => Weathers.Count == 0 || Contains(Weathers, weather);

    /// <summary>该时段是否可能出现。</summary>
    public bool AppearsIn(DayPhase phase) => Phases.Count == 0 || Contains(Phases, phase);

    /// <summary>三个条件同时满足才可能出现。掷鱼拿它做过滤，不用逐维各问一次。</summary>
    public bool IsAvailable(Season season, Weather weather, DayPhase phase) =>
        AppearsIn(season) && AppearsIn(weather) && AppearsIn(phase);

    private static bool Contains<T>(IReadOnlyList<T> list, T value)
    {
        for (int index = 0; index < list.Count; index++)
            if (EqualityComparer<T>.Default.Equals(list[index], value))
                return true;

        return false;
    }
}
