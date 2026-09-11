using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using XingGame.Systems.Items;

namespace XingGame.Systems.Farming;

/// <summary>
/// 静态作物表，数据在 <c>data/crops/crops.json</c>。加载时逐条校验——数据表是外部输入，
/// 错在表里就要在加载时炸，不要等到播种那天才炸（照 <see cref="ItemTable"/> 的先例）。
/// </summary>
/// <remarks>
/// <para>
/// <b>数值全部抄自 §6.2 作物列表</b>（<c>docs/public/design.md</c> 的 11 种作物），
/// 生长天数与「可多次收获」两列一个数都没编。可多次收获的只有草莓、蓝莓、蔓越莓三种。
/// </para>
/// <para>
/// 加载后<b>必须</b>用物品表交叉校验：<c>seedId</c>/<c>cropId</c> 都要能在物品表里找到。
/// 两张表对不上是「各自的测试都发现不了」的那种错——作物表自身完全自洽，物品表也是，
/// 只有把两张表放在一起看才露馅，而症状要到玩家播种或收获时才显形。
/// </para>
/// <para>
/// <b>不实现生长阶段</b>：§6.1 说「4-6 阶段、每阶段 1-3 天」，与 §6.2 的逐作物总天数对不上
/// （按 §6.1 最多 18 天，而冰灵草 20 天）。M1 以 §6.2 的总天数为准（ADR-014），
/// 存档只记已生长天数，将来真做阶段也是「把总天数切分成阶段」，不影响存档。
/// </para>
/// </remarks>
public sealed class CropTable : ICropTable
{
    /// <summary>缺省作物表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/crops/crops.json";

    private readonly Dictionary<string, CropDefinition> _bySeed;
    private readonly Dictionary<string, CropDefinition> _byCrop;
    private readonly ReadOnlyCollection<CropDefinition> _all;

    private CropTable(
        Dictionary<string, CropDefinition> bySeed,
        Dictionary<string, CropDefinition> byCrop,
        ReadOnlyCollection<CropDefinition> all)
    {
        _bySeed = bySeed;
        _byCrop = byCrop;
        _all = all;
    }

    /// <summary>按 JSON 里的顺序排列，图鉴列表直接照用。</summary>
    public IReadOnlyCollection<CropDefinition> All => _all;

    public bool TryGetBySeed(string seedId, out CropDefinition definition)
    {
        if (_bySeed.TryGetValue(seedId, out CropDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;   // out 必须先赋值：找不到以返回值 false 表达（同 ItemTable）
        return false;
    }

    public CropDefinition GetBySeed(string seedId) =>
        _bySeed.TryGetValue(seedId, out CropDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"作物表里没有种子 id「{seedId}」");

    public bool TryGetByCrop(string cropId, out CropDefinition definition)
    {
        if (_byCrop.TryGetValue(cropId, out CropDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    /// <param name="items">用于交叉校验两张表是否对得上——这是本方法需要物品表的原因，不是可选装饰。</param>
    public static CropTable FromJson(string json, IItemTable items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("crops", out JsonElement crops) ||
            crops.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("作物表缺少 crops 数组");
        }

        var bySeed = new Dictionary<string, CropDefinition>(StringComparer.Ordinal);
        var byCrop = new Dictionary<string, CropDefinition>(StringComparer.Ordinal);
        var all = new List<CropDefinition>();

        foreach (JsonElement element in crops.EnumerateArray())
        {
            CropDefinition definition = ParseCrop(element);

            // 重复 id 会让「按 id 取到的是哪一份」取决于文件顺序，且两份的天数可能不同——数据错误，启动即报
            if (!bySeed.TryAdd(definition.SeedId, definition))
                throw new InvalidDataException($"作物表出现重复的种子 id：{definition.SeedId}");

            // 两种种子结同一种作物会让 TryGetByCrop 的结果取决于文件顺序
            if (!byCrop.TryAdd(definition.CropId, definition))
                throw new InvalidDataException($"作物表出现重复的作物 id：{definition.CropId}");

            all.Add(definition);
        }

        CrossCheck(all, items);

        return new CropTable(bySeed, byCrop, all.AsReadOnly());
    }

    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static CropTable FromFile(string path, IItemTable items) => FromJson(File.ReadAllText(path), items);

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省作物表，与 <c>ItemTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static CropTable LoadDefault(IItemTable items)
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path, items);
    }

    /// <summary>
    /// 两张表对不上就是数据错误：种子表里写了一个物品表里没有的 id，玩家种下去才发现取不到物品。
    /// 这里一次把全部对不上的条目都列出来——一条一条修比每次重跑才发现下一条快得多。
    /// </summary>
    private static void CrossCheck(List<CropDefinition> crops, IItemTable items)
    {
        var missing = new List<string>();

        foreach (CropDefinition crop in crops)
        {
            if (!items.TryGet(crop.SeedId, out _)) missing.Add(crop.SeedId);
            if (!items.TryGet(crop.CropId, out _)) missing.Add(crop.CropId);
        }

        if (missing.Count > 0)
            throw new InvalidDataException($"作物表里有物品表找不到的 id：{string.Join("、", missing)}");
    }

    private static CropDefinition ParseCrop(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("作物表出现不是对象的条目");

        string? seedId = OptionalString(element, "seedId");
        if (string.IsNullOrWhiteSpace(seedId))
            throw new InvalidDataException("作物表出现空 seedId");

        string? cropId = OptionalString(element, "cropId");
        if (string.IsNullOrWhiteSpace(cropId))
            throw new InvalidDataException($"作物 {seedId} 缺少 cropId");

        int growthDays = RequiredInt(element, "growthDays", seedId!);

        // 非正的生长天数意味着「播下去就熟」或「永远不熟」，两种都不是设计文档的意思
        if (growthDays <= 0)
            throw new InvalidDataException($"作物 {seedId} 的 growthDays 为 {growthDays}，必须为正");

        return new CropDefinition(seedId!, cropId!, growthDays, RequiredBool(element, "regrowable", seedId!));
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int RequiredInt(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"作物 {id} 缺少数字字段 {property}");

        return value.GetInt32();
    }

    private static bool RequiredBool(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
            throw new InvalidDataException($"作物 {id} 缺少布尔字段 {property}");

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"作物 {id} 的 {property} 不是布尔值"),
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
