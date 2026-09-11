using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using XingGame.Core.Time;

namespace XingGame.Systems.Npc;

/// <summary>
/// 日程里的一行：从 <see cref="Hour"/> 点起，该 NPC 待在某地（§10.3 的「9:00 图书馆」）。
/// </summary>
/// <param name="Seasons">这一行对哪些季节生效。文档的一行可覆盖多季（「春/夏/秋：…」）。</param>
public readonly record struct ScheduleEntry(IReadOnlyList<Season> Seasons, int Hour, string Location);

/// <summary>
/// 一位 NPC 的日程表。<b>只做数据，不做寻路</b>（M2-A 契约）：本类只回答「此刻他在哪」，
/// 怎么走过去是桥接层与 M2 之后的事。
/// </summary>
/// <remarks>
/// <para>
/// 数据出处：§10.3 NPC 日程。文档只给了<b>艾琳娜</b>一位的完整日程（标明「示例」），
/// 其余 13 位文档没写——那些 NPC 的 <see cref="Entries"/> 为空，标「文档未给，待补」。
/// </para>
/// <para>
/// 地点、钟点、季节全部<b>照抄原文</b>，包括 22:00 那行的「回家」——原文写的就是这两个字，
/// 它是动作而不是地点，但改写就是替设计文档做决定。
/// </para>
/// </remarks>
public sealed class NpcSchedule
{
    /// <summary>文档未给日程的 NPC 用这一份：查不出任何地点，见 <see cref="LocationAt"/>。</summary>
    public static readonly NpcSchedule Empty = new(Array.Empty<ScheduleEntry>(), null);

    private readonly ReadOnlyCollection<ScheduleEntry> _entries;

    public NpcSchedule(IReadOnlyList<ScheduleEntry> entries, string? rainyLocation)
    {
        _entries = new ReadOnlyCollection<ScheduleEntry>(entries.ToArray());
        RainyLocation = rainyLocation;
    }

    public IReadOnlyList<ScheduleEntry> Entries => _entries;

    /// <summary>§10.3「雨天：全天在家」的「家」；文档未给雨天安排时为 null。</summary>
    public string? RainyLocation { get; }

    /// <summary>文档没给这一位的日程。</summary>
    public bool IsEmpty => _entries.Count == 0 && RainyLocation is null;

    /// <summary>
    /// 查这一位此刻在哪。<b>是查表，不是寻路</b>。
    /// </summary>
    /// <returns>
    /// 地点；查不出时返回 null。**null 的含义是「文档没说」而不是「不在任何地方」**——
    /// 日程开始之前（如艾琳娜春日的 8:00）文档确实没写他该在哪，编一个「家」出来就等于替文档做主。
    /// </returns>
    public string? LocationAt(Season season, int hour, bool isRaining)
    {
        // 雨天是整天覆盖，与钟点无关，所以先于一切钟点判断
        if (isRaining && RainyLocation is not null) return RainyLocation;

        string? location = null;
        int latestHour = -1;

        foreach (ScheduleEntry entry in _entries)
        {
            if (entry.Hour > hour) continue;
            if (!entry.Seasons.Contains(season)) continue;

            // 取「已开始且最晚」的那一行：一天里晚的安排覆盖早的，与文档从上往下的读法一致
            if (entry.Hour >= latestHour)
            {
                latestHour = entry.Hour;
                location = entry.Location;
            }
        }

        // 这一位没有任何日程数据时走到这里，返回 null——雨天没有专门安排时也走这里，
        // 即「没写雨天安排」按平时日程算，而不是把人凭空变没
        return location;
    }
}
