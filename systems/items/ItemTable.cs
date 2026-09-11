using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace XingGame.Systems.Items;

/// <summary>
/// 静态物品表，数据在 <c>data/items/items.json</c>。加载时逐条校验——数据表是外部输入，
/// 错在表里就要在加载时炸，不要等到背包去取它时才炸（那时现场离病因已经很远）。
///
/// <para>
/// <b>只录设计文档里明确出现过的物品，一个都不多造。</b>文档没提的物品一个都不要录——录进来
/// 只会是一堆填着 0 的占位行，将来还要逐条核对删除。
/// </para>
///
/// <para>出处（行号对 <c>docs/public/design.md</c>；剧透版内容与之一致）：</para>
/// <list type="bullet">
/// <item><b>§6.2 作物列表（第 269-284 行）是权威作物表</b>：11 种作物，生长天数与<b>售价</b>照抄
/// 该表。它末尾那句「完整作物表见附录 A」目前是空头支票——附录 A 只有 4 行，别把附录 A 当主表。</item>
/// <item>附录 A 作物表（第 1649-1656 行，仅 4 行、自称「示例」）：只用来补 §6.2 没有的
/// <b>「种子价」</b>列。该列是<b>买入价</b>，故填进 <c>buyPrice</c>；这 4 种种子的卖出价
/// 文档没给，<c>sellPrice</c> 填 0（文档未给，待补）。</item>
/// <item>§12.3 示例配方（第 1336-1354 行）：木材、煤、铜矿、铜锭、铁锭、灵泉水、灵石。
/// 只出现名字、没有任何价格，故两个价格字段都填 0（文档未给，待补）——不自己编一个数。</item>
/// <item>种子：每种作物都要有对应种子（M1-5 种植要用），共 11 种。除附录 A 那 4 种，其余 7 种的
/// 种子价文档没给，两个价格字段都填 0（文档未给，待补）。</item>
/// </list>
///
/// <para>
/// <b>不录</b>：§6.1 的五种肥料、§6.3 的杂交产物、§6.4 的八种果树、§12.1 的各类商品——
/// 它们在文档里只出现过名字、没有任何价格，且 M1 没有消费者。
/// </para>
///
/// <para>
/// 取舍（文档未定义、由本切片定下，待裁决）：① 物品种类还没有可引用的出处，故一个都不录，
/// 连「工具类 maxStack 用 1」这条规则目前也没有适用对象；② 灵植（灵芽草、火灵花、金灵果、
/// 冰灵草）归 <see cref="ItemCategory.Crop"/>——分类枚举里没有「灵植」，它是 §6.1 的作物子类，
/// 记在描述里。
/// </para>
/// </summary>
public sealed class ItemTable : IItemTable
{
    /// <summary>缺省物品表目录，相对工程根目录。目录下所有 <c>*.json</c> 会被合并成一张表。</summary>
    /// <remarks>
    /// 按领域分文件（<c>items.json</c> 基础、<c>fishing.json</c> 鱼、<c>ranching.json</c> 畜产…），
    /// 是为了让每个领域能独立加物品而不去改同一个文件——多人/多 agent 并行时那是必然的撞车点。
    /// 顺带也是 Mod 友好：覆盖某一个领域的数据不必动其他领域。
    /// </remarks>
    public const string DefaultRelativeDirectory = "data/items";

    private readonly Dictionary<string, ItemDefinition> _byId;
    private readonly ReadOnlyCollection<ItemDefinition> _all;

    private ItemTable(Dictionary<string, ItemDefinition> byId, ReadOnlyCollection<ItemDefinition> all)
    {
        _byId = byId;
        _all = all;
    }

    /// <summary>按 JSON 里的顺序排列，UI 列表直接照用。</summary>
    public IReadOnlyCollection<ItemDefinition> All => _all;

    public bool TryGet(string id, out ItemDefinition definition)
    {
        if (_byId.TryGetValue(id, out ItemDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;   // out 必须先赋值：找不到以返回值 false 表达，输出用 null（同 ServiceRegistry）
        return false;
    }

    public ItemDefinition Get(string id) =>
        _byId.TryGetValue(id, out ItemDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"物品表里没有 id 为「{id}」的物品");

    public static ItemTable FromJson(string json) => FromJsonDocuments(new[] { json });

    /// <summary>
    /// 多份 JSON 合并成一张表。
    /// </summary>
    /// <remarks>
    /// <b>重复 id 跨文件也算错，且报错里带上文件名。</b>物品按领域分文件放，正是为了让各领域
    /// 独立加东西；而跨文件撞 id 恰恰是这种放法最容易出的错——两份数据各自的测试都发现不了，
    /// 只能靠这里拦。带上文件名是因为「在哪个文件里」正是修它需要的信息。
    /// </remarks>
    public static ItemTable FromJsonDocuments(IEnumerable<string> jsons)
    {
        ArgumentNullException.ThrowIfNull(jsons);

        var byId = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        var all = new List<ItemDefinition>();

        foreach (string json in jsons) AppendDocument(json, byId, all);

        return new ItemTable(byId, all.AsReadOnly());
    }

    /// <summary>
    /// 目录下所有 <c>*.json</c> 合并。
    /// </summary>
    /// <remarks>
    /// 先按文件名排序：<c>Directory.GetFiles</c> 的顺序不保证稳定，不排序的话
    /// <see cref="All"/> 的顺序会随文件系统变化——UI 列表顺序飘忽，测试也会时绿时红。
    /// </remarks>
    public static ItemTable FromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string[] files = Directory.GetFiles(directory, "*.json");
        if (files.Length == 0) throw new FileNotFoundException($"目录里没有任何 *.json：{directory}");

        Array.Sort(files, StringComparer.Ordinal);

        var jsons = new List<string>(files.Length);
        foreach (string file in files) jsons.Add(File.ReadAllText(file));

        return FromJsonDocuments(jsons);
    }

    public static ItemTable FromFile(string path) => FromJson(File.ReadAllText(path));

    private static void AppendDocument(string json, Dictionary<string, ItemDefinition> byId, List<ItemDefinition> all)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("items", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("物品表缺少 items 数组");
        }

        foreach (JsonElement element in items.EnumerateArray())
        {
            ItemDefinition definition = ParseItem(element);

            // 重复 id 会让「按 id 取到的是哪一份」取决于文件顺序，且两份的价格可能不同——数据错误，启动即报
            if (!byId.TryAdd(definition.Id, definition))
                throw new InvalidDataException($"物品表出现重复 id：{definition.Id}");

            all.Add(definition);
        }
    }

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省物品表，与 <c>WeatherTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static ItemTable LoadDefault()
    {
        string? directory = FindDefaultDirectory();
        if (directory is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativeDirectory}/（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromDirectory(directory);
    }

    private static ItemDefinition ParseItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("id", out JsonElement idElement) ||
            idElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("物品表出现缺少 id 字段的条目");
        }

        string id = idElement.GetString()!;

        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("物品表出现空 id");

        string name = RequiredString(element, "name", id);
        string description = RequiredString(element, "description", id);

        string categoryText = RequiredString(element, "category", id);
        if (!TryParseCategory(categoryText, out ItemCategory category))
            throw new InvalidDataException($"物品 {id} 的分类「{categoryText}」不是合法的 ItemCategory");

        int maxStack = RequiredInt(element, "maxStack", id);
        if (maxStack <= 0)
            throw new InvalidDataException($"物品 {id} 的 maxStack 为 {maxStack}，必须为正");

        // 买价与卖价分开两个字段：塞成一个，M2 的商店就会「按进价出售」，买进卖出不亏不赚（ADR-012）
        int buyPrice = RequiredInt(element, "buyPrice", id);
        if (buyPrice < 0)
            throw new InvalidDataException($"物品 {id} 的 buyPrice 为 {buyPrice}，不能为负");

        int sellPrice = RequiredInt(element, "sellPrice", id);
        if (sellPrice < 0)
            throw new InvalidDataException($"物品 {id} 的 sellPrice 为 {sellPrice}，不能为负");

        return new ItemDefinition(id, name, description, category, maxStack, buyPrice, sellPrice);
    }

    /// <summary>
    /// 分类名按<b>大小写不敏感</b>匹配（与 <c>WeatherTable</c> 解析季节/天气名一致，Mod 作者手写
    /// JSON 时不必猜大小写）。
    /// <para>
    /// 不用 <c>Enum.TryParse</c>：它会把 "3" 解析成序号为 3 的 <see cref="ItemCategory"/>，
    /// 于是「按数字序号写分类」这种 ADR-012 明确要避开的事会被静默接受，而序号是会随枚举插值错位的。
    /// </para>
    /// </summary>
    private static bool TryParseCategory(string text, out ItemCategory category)
    {
        foreach (ItemCategory candidate in Enum.GetValues<ItemCategory>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                category = candidate;
                return true;
            }
        }

        category = default;
        return false;
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string RequiredString(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"物品 {id} 缺少字符串字段 {property}");

        return value.GetString()!;
    }

    private static int RequiredInt(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"物品 {id} 缺少数字字段 {property}");

        return value.GetInt32();
    }

    private static string? FindDefaultDirectory()
    {
        foreach (string root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, DefaultRelativeDirectory);
                if (Directory.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
