using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Core.Time;

namespace XingGame.Systems.Npc;

/// <summary>
/// 静态 NPC 表，数据在 <c>data/npcs/npcs.json</c>。加载时逐条校验——数据表是外部输入，
/// 错在表里就要在加载时炸，不要等到送生日礼物那天才炸（照 <c>ItemTable</c>/<c>CropTable</c> 的先例）。
/// </summary>
/// <remarks>
/// <para>
/// <b>内容出处：附录 C「NPC 列表（完整）」（<c>docs/public/design.md</c> 1676-1692 行）</b>，
/// 共 14 位，逐列照抄。<b>不采信 §10.1 的列表</b>：那张表没有 ID、没有讨厌物品，且第 1211 行自己
/// 指着附录 C 说「完整 NPC 表见附录 C」——它是摘要，附录 C 是权威表。
/// </para>
/// <para>
/// 两处不一致已在 <see cref="NpcDefinition"/> 的注释里写明采信理由（妮娅的角色、姓名是否带称号）。
/// 这个仓库因为「只查了一节而漏掉权威表」返工过两次（见 ADR-014 末尾的两次栽跟头），
/// 所以这次是用 <c>npc_</c> 与全部 NPC 名全文检索过设计文档后才定的：全文只有这两处 NPC 表，
/// 没有第三份。
/// </para>
/// <para>
/// <b>不做的事</b>：不校验喜爱/讨厌物品是否在物品表里（那些是中文泛称，如「垃圾」「矿石」「花」，
/// 物品表里根本没有对应条目——**待与物品表对接**），也不做寻路（§10.3 只做数据，M2-A 契约）。
/// </para>
/// </remarks>
public sealed class NpcTable : INpcTable
{
    /// <summary>缺省 NPC 表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/npcs/npcs.json";

    private readonly Dictionary<string, NpcDefinition> _byId;
    private readonly ReadOnlyCollection<NpcDefinition> _all;

    private NpcTable(Dictionary<string, NpcDefinition> byId, ReadOnlyCollection<NpcDefinition> all)
    {
        _byId = byId;
        _all = all;
    }

    public IReadOnlyCollection<NpcDefinition> All => _all;

    public bool TryGet(string id, out NpcDefinition definition)
    {
        if (_byId.TryGetValue(id, out NpcDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;   // out 必须先赋值：找不到以返回值 false 表达（同 ItemTable/CropTable）
        return false;
    }

    public NpcDefinition Get(string id)
    {
        // id 为 null 时字典会抛 ArgumentNullException，但那个消息里没有「NPC 表」这层语境，
        // 查到一半的程序员看不出是哪张表在报错
        if (id is null) throw new ArgumentNullException(nameof(id));

        return _byId.TryGetValue(id, out NpcDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"NPC 表里没有 id 为「{id}」的 NPC");
    }

    public static NpcTable FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("npcs", out JsonElement npcs) ||
            npcs.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("NPC 表缺少 npcs 数组");
        }

        var byId = new Dictionary<string, NpcDefinition>(StringComparer.Ordinal);
        var all = new List<NpcDefinition>();

        foreach (JsonElement element in npcs.EnumerateArray())
        {
            NpcDefinition definition = ParseNpc(element);

            // 重复 id 会让「按 id 取到的是哪一份」取决于文件顺序，两份的生日还可能不同——
            // 于是同一天送礼有时 ×8 有时不 ×8。数据错误，启动即报
            if (!byId.TryAdd(definition.Id, definition))
                throw new InvalidDataException($"NPC 表出现重复 id：{definition.Id}");

            all.Add(definition);
        }

        return new NpcTable(byId, all.AsReadOnly());
    }

    public static NpcTable FromFile(string path) => FromJson(File.ReadAllText(path));

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省 NPC 表，与 <c>ItemTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static NpcTable LoadDefault()
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path);
    }

    private static NpcDefinition ParseNpc(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("NPC 表出现不是对象的条目");

        string? id = OptionalString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("NPC 表出现空 id");

        string? name = OptionalString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"NPC {id} 缺少 name");

        string? role = OptionalString(element, "role");
        if (string.IsNullOrWhiteSpace(role))
            throw new InvalidDataException($"NPC {id} 缺少 role");

        string? birthdayText = OptionalString(element, "birthday");
        if (string.IsNullOrWhiteSpace(birthdayText))
            throw new InvalidDataException($"NPC {id} 缺少 birthday");

        List<string> loved = ParseItemNames(element, "lovedItems", id);
        List<string> hated = ParseItemNames(element, "hatedItems", id);

        // 同一个名字同时出现在两个名单里，结果就取决于 TasteOf 先查哪个——而玩家的观感是
        // 「送他最爱的东西居然掉好感」。两份名单互斥是数据该保证的事，这里当场拦下
        string? both = loved.FirstOrDefault(item => hated.Contains(item, StringComparer.Ordinal));
        if (both is not null)
            throw new InvalidDataException($"NPC {id} 的物品「{both}」同时列在喜爱与讨厌里");

        return new NpcDefinition(
            id!,
            name!,
            role!,
            RequiredBool(element, "romanceable", id!),
            ParseBirthday(birthdayText!, id!),
            NpcDefinition.Freeze(loved),
            NpcDefinition.Freeze(hated),
            ParseSchedule(element, id!));
    }

    /// <summary>生日认不出要带上 NPC 的 id 再抛——只说「格式非法」是定位不到那一行的。</summary>
    private static Birthday ParseBirthday(string text, string id)
    {
        try
        {
            return Birthday.Parse(text);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"NPC {id} 的生日「{text}」非法：{ex.Message}");
        }
    }

    private static List<string> ParseItemNames(JsonElement element, string property, string id)
    {
        // 两个名单允许为空数组（改版 NPC 可能一样都不讨厌），但给了就必须是数组
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return new List<string>();

        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"NPC {id} 的 {property} 不是数组");

        var names = new List<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                throw new InvalidDataException($"NPC {id} 的 {property} 里有空物品名");
            else
                names.Add(item.GetString()!);
        }

        return names;
    }

    private static NpcSchedule ParseSchedule(JsonElement element, string id)
    {
        // 日程整段可以不给：文档只写了艾琳娜一位（§10.3 标明「示例」），其余 13 位「文档未给，待补」
        if (!element.TryGetProperty("schedule", out JsonElement schedule) || schedule.ValueKind == JsonValueKind.Null)
            return NpcSchedule.Empty;

        if (schedule.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"NPC {id} 的 schedule 不是对象");

        string? rainyLocation = OptionalString(schedule, "rainyLocation");

        if (!schedule.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind == JsonValueKind.Null)
            return new NpcSchedule(Array.Empty<ScheduleEntry>(), rainyLocation);

        if (entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"NPC {id} 的 schedule.entries 不是数组");

        var parsed = new List<ScheduleEntry>();
        foreach (JsonElement entry in entries.EnumerateArray())
            parsed.Add(ParseScheduleEntry(entry, id));

        RejectDuplicateHours(parsed, id);

        return new NpcSchedule(parsed, rainyLocation);
    }

    private static ScheduleEntry ParseScheduleEntry(JsonElement entry, string id)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"NPC {id} 的日程里出现不是对象的条目");

        if (!entry.TryGetProperty("seasons", out JsonElement seasons) || seasons.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"NPC {id} 的日程缺少 seasons 数组");

        var parsedSeasons = new List<Season>();
        foreach (JsonElement season in seasons.EnumerateArray())
        {
            string? text = season.ValueKind == JsonValueKind.String ? season.GetString() : null;

            // 必须逐字对上枚举名：Enum.TryParse 连 "3" 都收（解析成 Winter），放行它就等于允许
            // 按序号写季节——将来往 Season 中间插一季，旧数据会静默指向另一个季节（ADR-012 同款理由）
            if (text is null || !Enum.GetNames<Season>().Contains(text, StringComparer.Ordinal))
                throw new InvalidDataException($"NPC {id} 的日程里有认不出的季节「{season}」");

            var value = Enum.Parse<Season>(text);

            // 同一行里重复写一季，会让「这一季有几条日程」对不上文档
            if (parsedSeasons.Contains(value))
                throw new InvalidDataException($"NPC {id} 的日程里季节「{value}」重复");

            parsedSeasons.Add(value);
        }

        if (parsedSeasons.Count == 0)
            throw new InvalidDataException($"NPC {id} 的日程有 seasons 为空的条目——那一行永远不会生效");

        int hour = RequiredInt(entry, "hour", id);

        // 钟点越界会被 LocationAt 永远跳过（它按 hour <= 当前小时匹配），是一行静默失效的日程
        if (hour < 0 || hour > 23)
            throw new InvalidDataException($"NPC {id} 的日程钟点 {hour} 超出 0..23");

        string? location = OptionalString(entry, "location");
        if (string.IsNullOrWhiteSpace(location))
            throw new InvalidDataException($"NPC {id} 的日程缺少 location");

        return new ScheduleEntry(parsedSeasons.AsReadOnly(), hour, location!);
    }

    /// <summary>
    /// 同一季节同一钟点写两条日程，加载即抛。
    /// </summary>
    /// <remarks>
    /// <c>LocationAt</c> 取「已开始且最晚」的那一行，比较用的是 <c>&gt;=</c>，于是两条都合法时
    /// 谁生效取决于文件里谁在后面——正是各表都专门拦过的「谁生效取决于文件顺序」
    /// （同 id 重复、一行里季节重复）。不同季节的同一钟点不冲突，不在拦截范围内。
    /// </remarks>
    private static void RejectDuplicateHours(List<ScheduleEntry> entries, string id)
    {
        var seen = new HashSet<(Season Season, int Hour)>();

        foreach (ScheduleEntry entry in entries)
        {
            foreach (Season season in entry.Seasons)
            {
                if (!seen.Add((season, entry.Hour)))
                    throw new InvalidDataException(
                        $"NPC {id} 的日程里 {season} 的 {entry.Hour}:00 出现了两条——谁生效取决于文件顺序");
            }
        }
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int RequiredInt(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"NPC {id} 缺少数字字段 {property}");

        return value.GetInt32();
    }

    private static bool RequiredBool(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
            throw new InvalidDataException($"NPC {id} 缺少布尔字段 {property}");

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"NPC {id} 的 {property} 不是布尔值"),
        };
    }

    private static string? FindDefaultFile()
    {
        foreach (string root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, DefaultRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
