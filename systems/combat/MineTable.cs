using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using XingGame.Systems.Items;

namespace XingGame.Systems.Combat;

/// <summary>
/// 静态矿洞表，数据在 <c>data/combat/mines.json</c>。加载时逐条校验，并<b>用物品表交叉校验矿石</b>——
/// 矿石写了一个物品表里没有的 id，等玩家挥镐子那天才会炸，而那时没人会想到是这张表写错了。
/// </summary>
/// <remarks>
/// <para><b>§7.1 矿洞（384-398）给了什么</b>：层数 120、每 10 层有电梯、每 10 层有宝箱（含稀有物品）、
/// 四种特殊层（怪物层/黑暗层/岩浆层/蘑菇层）、危险（生命值归零则昏倒，损失金币和物品）、
/// 每 30 层有灵气浓郁区域（修炼速度 +50%）、矿石六种（铜、铁、金、铱、煤、宝石）。</para>
///
/// <para><b>没给的</b>：矿石分布在哪几层（一个字都没有，所以 <see cref="MineDefinition.Ores"/> 只有一份
/// 无层数的清单）；宝箱里具体是什么；特殊层怎么生成、第几层是特殊层；沙漠矿洞（§5.1 提到存在，
/// 但没有任何数值）——这些一律不录，录进来就是一堆填着 0 的占位行。</para>
///
/// <para><b>§5.1 的解锁条件（第 5 天）也不在本表</b>：那是区域解锁，归 M2 集成时的世界/任务模块，
/// 本模块没有消费者。</para>
/// </remarks>
public sealed class MineTable : IMineTable
{
    /// <summary>缺省矿洞表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/combat/mines.json";

    private readonly Dictionary<string, MineDefinition> _byId;
    private readonly ReadOnlyCollection<MineDefinition> _all;

    private MineTable(Dictionary<string, MineDefinition> byId, ReadOnlyCollection<MineDefinition> all)
    {
        _byId = byId;
        _all = all;
    }

    public IReadOnlyCollection<MineDefinition> All => _all;

    public bool TryGet(string id, out MineDefinition definition)
    {
        if (_byId.TryGetValue(id, out MineDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;   // out 必须先赋值：找不到以返回值 false 表达（同 ItemTable）
        return false;
    }

    public MineDefinition Get(string id) =>
        _byId.TryGetValue(id, out MineDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"矿洞表里没有 id 为「{id}」的矿洞");

    /// <param name="items">用于交叉校验矿石是否都在物品表里——这是本方法需要物品表的理由，不是可选装饰。</param>
    public static MineTable FromJson(string json, IItemTable items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("mines", out JsonElement mines) ||
            mines.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("矿洞表缺少 mines 数组");
        }

        var byId = new Dictionary<string, MineDefinition>(StringComparer.Ordinal);
        var all = new List<MineDefinition>();

        foreach (JsonElement element in mines.EnumerateArray())
        {
            MineDefinition definition = ParseMine(element, items);

            // 重复 id 会让「按 id 取到的是哪一份」取决于文件顺序（同怪物表）
            if (!byId.TryAdd(definition.Id, definition))
                throw new InvalidDataException($"矿洞表出现重复 id：{definition.Id}");

            all.Add(definition);
        }

        return new MineTable(byId, all.AsReadOnly());
    }

    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static MineTable FromFile(string path, IItemTable items) => FromJson(File.ReadAllText(path), items);

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省矿洞表，与 <c>ItemTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static MineTable LoadDefault(IItemTable items)
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path, items);
    }

    private static MineDefinition ParseMine(JsonElement element, IItemTable items)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("矿洞表出现不是对象的条目");

        string? id = OptionalString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("矿洞表出现空 id");

        string? name = OptionalString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"矿洞 {id} 缺少 name");

        int layerCount = RequiredInt(element, "layerCount", id!);
        if (layerCount <= 0)
            throw new InvalidDataException($"矿洞 {id} 的 layerCount 为 {layerCount}，必须为正");

        int elevatorInterval = RequiredInterval(element, "elevatorInterval", id!, layerCount);
        int chestInterval = RequiredInterval(element, "chestInterval", id!, layerCount);
        int auraInterval = RequiredInterval(element, "auraInterval", id!, layerCount);

        int auraBonusPercent = RequiredInt(element, "auraBonusPercent", id!);
        if (auraBonusPercent < 0)
            throw new InvalidDataException($"矿洞 {id} 的 auraBonusPercent 为 {auraBonusPercent}，不能为负");

        return new MineDefinition(
            id!, name!, layerCount, elevatorInterval, chestInterval, auraInterval, auraBonusPercent,
            ParseOres(element, id!, items));
    }

    /// <summary>
    /// 间隔必须落在 1..层数 之间：为 0 会在取余时抛 <see cref="DivideByZeroException"/>。
    /// 大于层数则意味着这层数下压根没有电梯层/宝箱层/灵气层——玩家永远碰不到，是数据写错了而不是设计如此。
    /// </summary>
    private static int RequiredInterval(JsonElement element, string property, string id, int layerCount)
    {
        int interval = RequiredInt(element, property, id);

        if (interval <= 0 || interval > layerCount)
            throw new InvalidDataException($"矿洞 {id} 的 {property} 为 {interval}，必须落在 1..{layerCount} 之间");

        return interval;
    }

    /// <summary>
    /// 矿石逐条校验：物品表里没有的 id、空 id、重复 id。
    /// 一次把全部问题都列出来——一条一条修比每次重跑才发现下一条快得多。
    /// </summary>
    private static IReadOnlyList<string> ParseOres(JsonElement element, string id, IItemTable items)
    {
        if (!element.TryGetProperty("ores", out JsonElement ores) || ores.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"矿洞 {id} 缺少 ores 数组（没有矿石就写空数组）");

        var parsed = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonElement ore in ores.EnumerateArray())
        {
            if (ore.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(ore.GetString()))
                throw new InvalidDataException($"矿洞 {id} 的 ores 里出现空的物品 id");

            string itemId = ore.GetString()!;

            if (!items.TryGet(itemId, out _))
                throw new InvalidDataException($"矿洞 {id} 的矿石「{itemId}」不在物品表里");

            if (!seen.Add(itemId))
                throw new InvalidDataException($"矿洞 {id} 重复写了矿石「{itemId}」");

            parsed.Add(itemId);
        }

        return parsed.AsReadOnly();
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int RequiredInt(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"矿洞 {id} 缺少数字字段 {property}");

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
