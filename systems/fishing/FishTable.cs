using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using XingGame.Core.Time;
using XingGame.Systems.Items;

namespace XingGame.Systems.Fishing;

/// <summary>
/// 静态鱼表，数据在 <c>data/fishing/fish.json</c>。加载时逐条校验、并与物品表交叉校验——
/// 数据表是外部输入，错在表里就要在加载时炸，不要等玩家抛竿那天才炸（照 <see cref="ItemTable"/>、
/// <c>CropTable</c> 的先例）。
/// </summary>
/// <remarks>
/// <para>
/// <b>出处（行号对 <c>docs/public/design.md</c>，剧透版内容与之一致）：§7.3 钓鱼系统（第 412-424 行）
/// 是唯一给鱼的地方，§3.2 季节（106-121）、§3.3 天气（123-131）、§3.1 时段（95-104）补条件。</b>
/// </para>
/// <para><b>§7.3 给了什么：</b></para>
/// <list type="bullet">
/// <item>鱼竿的名字：竹竿、玻璃鱼竿、铱鱼竿、灵鱼竿（第 413 行）。只有名字，没有价格、没有门槛规则。</item>
/// <item>操作与小游戏（415-417 行）——<b>M2 不做</b>（ARCHITECTURE「接口契约 · M2-A」：钓鱼小游戏留到 M8）。</item>
/// <item>「鱼种：每季 20+ 种，不同水域、时间、天气影响」（第 419 行）——<b>规模是给了，一个鱼名都没给。</b></item>
/// <item>特殊鱼：传说鱼（5 种）、幽灵鱼、水母、灵鱼（第 421 行）——<b>传说鱼说了 5 种却没给名单</b>，
/// 故本表一条都不录它；另外三种有名字，录。</item>
/// <item>蟹笼：放置在水域，每日收取龙虾、螃蟹、虾等（第 423 行）——三个名字录，那个「等」是开放集，
/// 其余没给名的不录。</item>
/// <item>条件：§3.3 明写「雾天…幽灵鱼」（第 131 行）；§3.4 月光水母节「夜间钓鱼、水母观赏」
/// （第 139 行，夏 28 日）是水母唯一的季节/时段线索。</item>
/// <item>鲟鱼：来自附录 C 的 NPC 礼物表（威利，第 1202 行）——它是文档里唯一另一个被点名的鱼。</item>
/// </list>
/// <para><b>§7.3 没给什么（所以本表不做，不是漏做）：</b></para>
/// <list type="bullet">
/// <item><b>没有鱼名清单</b>：说「每季 20+ 种」，但除上面那几个名字外一个都没写。所以缺省表里只有
/// 文档点过名的 7 条，<b>不是 20×4 条</b>——凑数是编，等设计补附录。</item>
/// <item><b>没有水域维度</b>：只说「不同水域影响」，没给任何一条鱼属于哪个水域。故
/// <see cref="FishDefinition"/> 里<b>没有</b>地点字段——文档没给的维度不做。</item>
/// <item><b>没有权重/稀有度</b>：见 <see cref="FishDefinition.DefaultWeight"/>。</item>
/// <item><b>没有鱼价</b>：§7.3 与 §12.1 鱼店都没有鱼价，故 <c>data/items/fish.json</c> 里全部填 0
/// （文档未给，待补）。鱼竿同理。</item>
/// <item><b>没有鱼竿门槛</b>：四种鱼竿给了名字却没给任何规则（哪根能钓什么、价钱多少都没有），
/// 故鱼表里<b>不建鱼竿模型</b>；它们只作为工具物品存在于 <c>data/items/fish.json</c>，
/// 供 §12.1 鱼店与 §9.1「鱼竿品质提升」将来使用。</item>
/// <item><b>「雨天特定鱼」「暴风雨特殊鱼」（§3.3）没说具体哪种</b>：故不录——按名字对不上号的
/// 条件，只能靠编。雾天的幽灵鱼是唯一明写的一对。</item>
/// </list>
/// </remarks>
public sealed class FishTable : IFishTable
{
    /// <summary>缺省鱼表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/fishing/fish.json";

    private readonly Dictionary<string, FishDefinition> _byId;
    private readonly ReadOnlyCollection<FishDefinition> _all;

    private FishTable(Dictionary<string, FishDefinition> byId, ReadOnlyCollection<FishDefinition> all)
    {
        _byId = byId;
        _all = all;
    }

    /// <summary>按 JSON 里的顺序排列，图鉴列表直接照用。</summary>
    public IReadOnlyCollection<FishDefinition> All => _all;

    public bool TryGet(string fishId, out FishDefinition definition)
    {
        if (_byId.TryGetValue(fishId, out FishDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;   // out 必须先赋值：找不到以返回值 false 表达（同 ItemTable / CropTable）
        return false;
    }

    public FishDefinition Get(string fishId) =>
        _byId.TryGetValue(fishId, out FishDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"鱼表里没有 id 为「{fishId}」的鱼");

    public IReadOnlyList<FishDefinition> Candidates(Season season, Weather weather, DayPhase phase, CatchMethod method)
    {
        var candidates = new List<FishDefinition>();
        foreach (FishDefinition fish in _all)
        {
            if (fish.Method == method && fish.IsAvailable(season, weather, phase))
                candidates.Add(fish);
        }

        return candidates.AsReadOnly();
    }

    /// <param name="items">用于交叉校验两张表是否对得上——这是本方法需要物品表的原因，不是可选装饰。</param>
    public static FishTable FromJson(string json, IItemTable items)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("fish", out JsonElement fish) ||
            fish.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("鱼表缺少 fish 数组");
        }

        var byId = new Dictionary<string, FishDefinition>(StringComparer.Ordinal);
        var all = new List<FishDefinition>();

        foreach (JsonElement element in fish.EnumerateArray())
        {
            FishDefinition definition = ParseFish(element);

            // 重复 id 会让「按 id 取到的是哪一份」取决于文件顺序，且两份的条件可能不同——数据错误，启动即报
            if (!byId.TryAdd(definition.Id, definition))
                throw new InvalidDataException($"鱼表出现重复 id：{definition.Id}");

            all.Add(definition);
        }

        CrossCheck(all, items);

        return new FishTable(byId, all.AsReadOnly());
    }

    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static FishTable FromFile(string path, IItemTable items) => FromJson(File.ReadAllText(path), items);

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省鱼表，与 <c>ItemTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static FishTable LoadDefault(IItemTable items)
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path, items);
    }

    /// <summary>
    /// 两张表对不上就是数据错误：鱼表写了一个物品表里没有的 id，玩家钓上来才发现进不了背包。
    /// 这里一次把全部对不上的条目都列出来——一条一条修比每次重跑才发现下一条快得多（同 CropTable）。
    /// </summary>
    private static void CrossCheck(List<FishDefinition> fish, IItemTable items)
    {
        var missing = new List<string>();

        foreach (FishDefinition definition in fish)
        {
            if (!items.TryGet(definition.ItemId, out _)) missing.Add($"{definition.Id}→{definition.ItemId}");
        }

        if (missing.Count > 0)
            throw new InvalidDataException($"鱼表里有物品表找不到的 id：{string.Join("、", missing)}");
    }

    private static FishDefinition ParseFish(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("鱼表出现不是对象的条目");

        string? id = OptionalString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("鱼表出现空 id");

        string itemId = RequiredString(element, "itemId", id!);
        if (string.IsNullOrWhiteSpace(itemId))
            throw new InvalidDataException($"鱼 {id} 的 itemId 为空");

        string methodText = RequiredString(element, "method", id!);
        if (!TryParseName(methodText, out CatchMethod method))
            throw new InvalidDataException($"鱼 {id} 的捕获方式「{methodText}」不是合法的 CatchMethod");

        int weight = OptionalInt(element, "weight", id!, FishDefinition.DefaultWeight);

        // 非正的权重意味着这条鱼永远掷不到（或权重前缀和会算出负数区间），两种都不是表该有的状态
        if (weight <= 0)
            throw new InvalidDataException($"鱼 {id} 的 weight 为 {weight}，必须为正");

        return new FishDefinition(
            id!,
            itemId,
            method,
            NameList<Season>(element, "seasons", id!),
            NameList<Weather>(element, "weather", id!),
            NameList<DayPhase>(element, "phases", id!),
            weight);
    }

    /// <summary>
    /// 读一个「条件」字段：字段缺失 = 该维度不限，返回空表。
    /// </summary>
    /// <remarks>
    /// 空数组<b>不</b>等同于缺失，而是数据错误：写 <c>"seasons": []</c> 的人多半以为它表示「不限」，
    /// 但它更可能表示「这条鱼一个季节都不出现」——两种理解差着一条永远钓不到的鱼，宁可直接报错，
    /// 让「不限就省略字段」成为唯一写法。
    /// </remarks>
    private static IReadOnlyList<TEnum> NameList<TEnum>(JsonElement element, string property, string id)
        where TEnum : struct, Enum
    {
        if (!element.TryGetProperty(property, out JsonElement value)) return Array.Empty<TEnum>();

        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"鱼 {id} 的 {property} 不是数组");

        var parsed = new List<TEnum>();
        foreach (JsonElement entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String || !TryParseName(entry.GetString()!, out TEnum name))
                throw new InvalidDataException($"鱼 {id} 的 {property} 里有不认识的值：{entry}");

            if (parsed.Contains(name))
                throw new InvalidDataException($"鱼 {id} 的 {property} 里重复写了 {name}");

            parsed.Add(name);
        }

        if (parsed.Count == 0)
            throw new InvalidDataException($"鱼 {id} 的 {property} 是空数组：不限就省略该字段");

        return parsed.AsReadOnly();
    }

    /// <summary>
    /// 枚举名按<b>大小写不敏感</b>匹配，但<b>不认数字</b>（与 <c>ItemTable.TryParseCategory</c> 一致）。
    /// 不用 <c>Enum.TryParse</c>：它会把 "3" 解析成序号为 3 的枚举值，于是「按序号写季节/天气」这种
    /// ADR-012 明确要避开的事会被静默接受，而序号会随枚举插值错位。
    /// </summary>
    private static bool TryParseName<TEnum>(string text, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string RequiredString(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"鱼 {id} 缺少字符串字段 {property}");

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int OptionalInt(JsonElement element, string property, string id, int fallback)
    {
        if (!element.TryGetProperty(property, out JsonElement value)) return fallback;

        if (value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"鱼 {id} 的 {property} 不是数字");

        return value.GetInt32();
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
