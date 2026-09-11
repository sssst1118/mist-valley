using System;
using System.Globalization;
using XingGame.Core.Time;

namespace XingGame.Systems.Npc;

/// <summary>
/// NPC 生日：季节 + 该季第几天。§10.1 与附录 C 里的「春 10」「夏 28」就是这两个字段。
/// </summary>
/// <remarks>
/// <para>
/// 复用 <see cref="Season"/>（§3.2：每季 28 天）而不是自造一套季节名——生日的「春」与天气的「春」
/// 是同一个春，两份季节枚举迟早会对不上（铁律 12）。
/// </para>
/// <para>
/// 天数上限取 <see cref="GameTime.DaysPerSeason"/> 而不是写死 28：两者必须同步变化，
/// 照抄一份数字就有第二份会先过期。
/// </para>
/// </remarks>
public readonly record struct Birthday(Season Season, int Day)
{
    /// <summary>按文档写法输出（「春 10」），不是 <c>Season</c> 枚举的英文名。</summary>
    public override string ToString() => $"{SeasonName(Season)} {Day}";

    /// <summary>
    /// 解析文档写法（「春 10」）。<b>只认文档里出现过的写法</b>——放宽到英文季节名或「10 春」
    /// 就等于多出一份没被文档承认的格式，将来两处写法打架时无从裁决。
    /// </summary>
    /// <exception cref="FormatException">认不出、或日超出 1..28。</exception>
    public static Birthday Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("生日为空");

        // 只按空格切：文档写法是「季节 空格 日」，中间多敲几个空格不算错，别的分隔符算
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new FormatException($"生日应为「季节 日」两段，如「春 10」，实际是「{text}」");

        Season season = ParseSeason(parts[0]);

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int day))
            throw new FormatException($"生日里的「{parts[1]}」不是数字");

        // 日越界不是「晚几天」，而是换了一季或跨年——照原样收下会让生日礼物的 ×8 落在错的一天
        if (day < 1 || day > GameTime.DaysPerSeason)
            throw new FormatException($"生日 {day} 超出 1..{GameTime.DaysPerSeason}");

        return new Birthday(season, day);
    }

    private static Season ParseSeason(string text) => text switch
    {
        "春" => Season.Spring,
        "夏" => Season.Summer,
        "秋" => Season.Autumn,
        "冬" => Season.Winter,
        _ => throw new FormatException($"认不出季节「{text}」，只认「春」「夏」「秋」「冬」"),
    };

    /// <summary>§3.2 的四季在文档里一律写作「春夏秋冬」。</summary>
    internal static string SeasonName(Season season) => season switch
    {
        Season.Spring => "春",
        Season.Summer => "夏",
        Season.Autumn => "秋",
        Season.Winter => "冬",
        _ => throw new ArgumentOutOfRangeException(nameof(season), season, "未知季节"),
    };
}
