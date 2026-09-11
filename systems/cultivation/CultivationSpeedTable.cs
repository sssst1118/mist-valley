using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Core.Time;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 打坐这张账的静态表，数据在 <c>data/cultivation/cultivation_speed.json</c>。加载时逐条校验，
/// 理由同 <see cref="SpiritRootTable"/> / <see cref="RealmTable"/>：表是外部输入，错在表里就该在加载时炸。
/// </summary>
/// <remarks>
/// <para>
/// <b>两组数的来源不同，注释里要分清</b>：季节与时辰的倍率是 §8.3 **直给**的
/// （<c>docs/public/design.md</c> 817-818 行）；基础速度与逐层开销是 ARCHITECTURE 未定义项备案
/// #67/#68 **推导**的（§8.1 的进度锚点 + §3.2 的 28 天 + §3.1 的清晨打坐 + §4.2 的灵根倍率两头夹出来）。
/// 前者的错是抄错文档，后者的错是推导不自洽——两者的测试也不同。
/// </para>
/// <para>
/// <b>只给一个境界录曲线</b>（<c>costRealmId</c>）：炼气期是 §8.1 里唯一「逐层递进」的大境界，
/// 筑基及以上是 §8.4 的跨大境界突破（要丹药/天材地宝），没有「攒够就升」这回事。所以本表
/// 不做成「每境界一段」的字典——那会为一段永远不存在的曲线先造一个容器。
/// </para>
/// </remarks>
public sealed class CultivationSpeedTable : ICultivationSpeedTable
{
    /// <summary>缺省表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/cultivation/cultivation_speed.json";

    private readonly string _costRealmId;
    private readonly ReadOnlyCollection<int> _stageCosts;
    private readonly Dictionary<Season, double> _bySeason;
    private readonly ReadOnlyCollection<HourBand> _hourBands;

    private CultivationSpeedTable(
        int basePointsPerHour,
        string costRealmId,
        ReadOnlyCollection<int> stageCosts,
        Dictionary<Season, double> bySeason,
        ReadOnlyCollection<HourBand> hourBands)
    {
        BasePointsPerHour = basePointsPerHour;
        _costRealmId = costRealmId;
        _stageCosts = stageCosts;
        _bySeason = bySeason;
        _hourBands = hourBands;
    }

    public int BasePointsPerHour { get; }

    public IReadOnlyList<HourBand> HourBands => _hourBands;

    public bool Covers(string realmId) => string.Equals(realmId, _costRealmId, StringComparison.Ordinal);

    public int PointsToAdvance(int stage)
    {
        if (stage < 1 || stage > _stageCosts.Count)
            throw new ArgumentOutOfRangeException(
                nameof(stage), stage,
                $"{_costRealmId} 只有 1..{_stageCosts.Count} 层有「升到下一层」的开销——"
                + "顶点的开销不存在，不是 0");

        return _stageCosts[stage - 1];
    }

    public double SeasonMultiplier(Season season) =>
        _bySeason.TryGetValue(season, out double multiplier)
            ? multiplier
            : throw new KeyNotFoundException($"修炼速度表里没有季节「{season}」的倍率");

    public double HourMultiplier(int hour)
    {
        if (hour < 0 || hour > 23)
            throw new ArgumentOutOfRangeException(nameof(hour), hour, "钟点必须是 0..23");

        // 一天里绝大多数钟点都不在特殊时辰上（§8.3 只点了子时与午时），所以默认 1.0 走快路
        foreach (HourBand band in _hourBands)
        {
            if (band.Contains(hour)) return band.Multiplier;
        }

        return 1.0;
    }

    /// <param name="realms">
    /// 校验 <c>costRealmId</c> 与 <c>stageCosts</c> 的条数用：开销是**逐层**的，条数必须与那个
    /// 大境界的小境界数对得上（13 层要 12 条），否则「第 12 层升到第 13 层要多少」就没有答案。
    /// </param>
    public static CultivationSpeedTable FromJson(string json, IRealmTable realms)
    {
        ArgumentNullException.ThrowIfNull(realms);

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("修炼速度表不是对象");

        int basePointsPerHour = RequiredInt(document.RootElement, "basePointsPerHour", "修炼速度表");

        // 0 或负的基础速度会让打坐永远没有产出、或倒着扣——两种都不是 §8.3 的形状，只可能是漏填
        if (basePointsPerHour <= 0)
            throw new InvalidDataException($"修炼速度表的基础速度 {basePointsPerHour} 不是正数");

        string costRealmId = RequiredText(document.RootElement, "costRealmId", "修炼速度表");

        return new CultivationSpeedTable(
            basePointsPerHour,
            costRealmId,
            ParseStageCosts(document.RootElement, costRealmId, realms),
            ParseSeasonMultipliers(document.RootElement),
            ParseHourBands(document.RootElement));
    }

    public static CultivationSpeedTable FromFile(string path, IRealmTable realms) =>
        FromJson(File.ReadAllText(path), realms);

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省表，与 <c>SpiritRootTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static CultivationSpeedTable LoadDefault(IRealmTable realms)
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path, realms);
    }

    /// <summary>
    /// 逐层开销：第 n 条 = 从第 n 层升到 n+1 层所需修为，条数必须正好等于「层数 − 1」。
    /// </summary>
    /// <remarks>
    /// 条数不对（少一条、多一条）不会当场显形，而是等到有人真的走到那一层才炸——那时玩家已经在
    /// 游戏里练了几个小时。所以在这里与境界表对上：层数是境界表的知识，开销是这张表的知识，
    /// 两个数字各归各的表，交叉点就在这里查（同 <c>RealmTable</c> 校验门槛引用的境界是否存在）。
    /// </remarks>
    private static ReadOnlyCollection<int> ParseStageCosts(JsonElement element, string realmId, IRealmTable realms)
    {
        RealmDefinition realm = realms.TryGet(realmId, out RealmDefinition? found)
            ? found
            : throw new InvalidDataException($"修炼速度表的 costRealmId「{realmId}」不在境界表里");

        JsonElement costs = RequiredArray(element, "stageCosts", "修炼速度表");

        var parsed = new List<int>();
        foreach (JsonElement cost in costs.EnumerateArray())
        {
            if (cost.ValueKind != JsonValueKind.Number)
                throw new InvalidDataException($"{realm.Name}的 stageCosts 里有不是数字的条目");

            int value = cost.GetInt32();

            // 0 开销会让这一层一打坐就升（跨层不再是一道台阶），负数会倒着退——都不是文档里的形状
            if (value <= 0)
                throw new InvalidDataException($"{realm.Name}的 stageCosts 里有非正的开销 {value}");

            parsed.Add(value);
        }

        if (parsed.Count != realm.StageCount - 1)
            throw new InvalidDataException(
                $"{realm.Name}有 {realm.StageCount} 个小境界，升层开销应有 {realm.StageCount - 1} 条"
                + $"（每条 = 从第 n 层升到 n+1 层），现在是 {parsed.Count} 条");

        return parsed.AsReadOnly();
    }

    /// <summary>
    /// §8.3 的四季倍率。**四季必须齐全**：缺哪个季节就等于「那个季节不能修炼」，
    /// 而文档给的是四季各有倍率——漏一条是漏抄，不是设计。
    /// </summary>
    private static Dictionary<Season, double> ParseSeasonMultipliers(JsonElement element)
    {
        JsonElement seasons = RequiredArray(element, "seasonMultipliers", "修炼速度表");
        var parsed = new Dictionary<Season, double>();

        foreach (JsonElement entry in seasons.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("修炼速度表的 seasonMultipliers 里出现不是对象的条目");

            string id = RequiredText(entry, "season", "修炼速度表");

            // 必须逐字对上枚举名，不收数字：Enum.TryParse 连 "0" 都收（解析成 Spring），放行它就等于
            // 允许按序号写季节——往枚举中间插一个季节，旧数据会静默指向另一个季节（同 RealmTable 的门槛）
            if (!Enum.GetNames<Season>().Contains(id, StringComparer.Ordinal))
                throw new InvalidDataException($"修炼速度表里有认不出的季节「{id}」");

            var season = Enum.Parse<Season>(id);

            if (!parsed.TryAdd(season, PositiveMultiplier(entry, $"季节「{id}」")))
                throw new InvalidDataException($"修炼速度表里季节「{id}」出现了两次");
        }

        foreach (Season season in Enum.GetValues<Season>())
        {
            if (!parsed.ContainsKey(season))
                throw new InvalidDataException($"修炼速度表缺了季节「{season}」的倍率");
        }

        return parsed;
    }

    /// <summary>
    /// §8.3 的两段特殊时辰。**带与带不许重叠**：同一时刻掉进两条带时，「哪条生效」会取决于
    /// 文件顺序——那是各表都专门拦过的幽灵 bug（同 <c>RealmTable</c> 的分层带断档/重叠）。
    /// </summary>
    private static ReadOnlyCollection<HourBand> ParseHourBands(JsonElement element)
    {
        JsonElement bands = RequiredArray(element, "hourBands", "修炼速度表");

        var parsed = new List<HourBand>();
        foreach (JsonElement entry in bands.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("修炼速度表的 hourBands 里出现不是对象的条目");

            string name = RequiredText(entry, "name", "修炼速度表");
            int from = RequiredInt(entry, "fromHour", name);
            int to = RequiredInt(entry, "toHour", name);

            if (from < 0 || from > 23)
                throw new InvalidDataException($"时辰「{name}」的起点 {from} 不是 0..23 的钟点");

            // 右端可以写到 24（= 当天结束），因为它是不含的端点
            if (to < 0 || to > 24)
                throw new InvalidDataException($"时辰「{name}」的终点 {to} 不是 0..24 的钟点");

            if (from == to)
                throw new InvalidDataException($"时辰「{name}」的起止都是 {from} 点——那是一个不含任何钟点的空带子");

            HourBand band = new(from, to, name, PositiveMultiplier(entry, $"时辰「{name}」"));

            // 逐钟点查重比重叠区间判起来简单，也不会漏掉跨午夜那种绕回来的重叠（子时 23-1 与 22-24）
            for (int hour = 0; hour < 24; hour++)
            {
                if (band.Contains(hour) && parsed.Any(existing => existing.Contains(hour)))
                    throw new InvalidDataException(
                        $"时辰「{name}」与另一段在 {hour} 点重叠——同一时刻两份倍率，谁生效取决于文件顺序");
            }

            parsed.Add(band);
        }

        return parsed.AsReadOnly();
    }

    /// <summary>倍率必须为正：0 倍速让这一档永远练不出东西，负倍速倒着扣（同灵根表那条判据）。</summary>
    private static double PositiveMultiplier(JsonElement element, string owner)
    {
        double multiplier = RequiredDouble(element, "multiplier", owner);

        if (multiplier <= 0)
            throw new InvalidDataException($"修炼速度表里{owner}的倍率 {multiplier} 不是正数");

        return multiplier;
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上是谁才定位得到那一行。</summary>
    private static string RequiredText(JsonElement element, string property, string owner)
    {
        string? text = OptionalString(element, property);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"{owner}缺少文本字段 {property}");

        return text!;
    }

    private static JsonElement RequiredArray(JsonElement element, string property, string tableName)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{tableName}缺少 {property} 数组");

        return value;
    }

    private static int RequiredInt(JsonElement element, string property, string owner)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"{owner}缺少数字字段 {property}");

        return value.GetInt32();
    }

    private static double RequiredDouble(JsonElement element, string property, string owner)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"修炼速度表里{owner}缺少数字字段 {property}");

        return value.GetDouble();
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
