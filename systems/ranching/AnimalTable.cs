using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using XingGame.Systems.Items;

namespace XingGame.Systems.Ranching;

/// <summary>
/// §6.5「类型」列：普通 / 灵兽。两种类型<b>唯一的行为差别是饲料</b>——
/// §6.5 写的是「灵兽需喂灵草」，见 <see cref="AnimalDefinition.FeedItemId"/>。
/// </summary>
public enum AnimalType
{
    Normal,
    Spirit,
}

/// <summary>§6.5 畜牧系统表的一行：动物、建筑、购买价、产出、产出周期、类型。</summary>
/// <param name="BuyPrice">
/// §6.5 的「购买价」。<b>蓝鸡那一格写的是「稀有」而不是数字</b>，无从填起，故为 0
/// （文档未给，待补）。0 同时表示「没有出处」，别把它读成「免费」。
/// </param>
/// <param name="ProductionIntervalDays">§6.5「产出周期」折算的天数：每天 1、每 2 天 2……</param>
/// <param name="GrowthDays">
/// 长到成年所需的天数。<b>§6.5 没有这一列，11 种全部填 0（文档未给，待补）</b>——
/// 0 即「买入当天就成年」，所以缺省数据下产出不受年龄影响；文档将来补上数值时只改 JSON。
/// </param>
public sealed record AnimalDefinition(
    string AnimalId,
    string Name,
    string Building,
    int BuyPrice,
    string ProduceItemId,
    int ProductionIntervalDays,
    AnimalType Type,
    int GrowthDays)
{
    /// <summary>普通动物的饲料：干草（§6.5「喂养：干草或放牧」）。</summary>
    public const string HayItemId = "material_hay";

    /// <summary>灵兽的饲料：灵草（§6.5「灵兽需喂灵草」）。</summary>
    public const string SpiritGrassItemId = "material_spirit_grass";

    /// <summary>
    /// 这种动物该喂什么，<b>由类型推导而不是每行各录一列</b>：§6.5 的规则本身就是按类型分的
    /// （「灵兽需喂灵草」），同一个事实存两处，早晚会有一行对不上（ADR-009 里
    /// worldSeed 不许存两处是同一个理由）。
    /// </summary>
    public string FeedItemId => Type == AnimalType.Spirit ? SpiritGrassItemId : HayItemId;
}

/// <summary>静态动物表。只读，进程内共享一份。</summary>
public interface IAnimalTable
{
    IReadOnlyCollection<AnimalDefinition> All { get; }
    bool TryGet(string animalId, out AnimalDefinition definition);
    AnimalDefinition Get(string animalId);   // 找不到抛 KeyNotFoundException，消息里带 id
}

/// <summary>
/// 静态动物表，数据在 <c>data/ranching/animals.json</c>。加载时逐条校验——数据表是外部输入，
/// 错在表里就要在加载时炸（照 <see cref="ItemTable"/>/<c>CropTable</c> 的先例）。
/// </summary>
/// <remarks>
/// <para>
/// <b>出处：§6.5 畜牧系统（<c>docs/public/design.md</c> 第 308-330 行）——畜牧的全部出处。</b>
/// 已 grep 全文核对过：动物种类、建筑、购买价、产出、产出周期、类型六列只有这一张表
/// （附录 A 只有作物，§6.4 是果树，§12.1 的商店表没有动物）。
/// </para>
/// <para><b>§6.5 给了什么</b>（照抄，一个数都没编）：</para>
/// <list type="bullet">
/// <item>11 种动物：白鸡、棕鸡、蓝鸡、牛、山羊、羊、猪、鸭、灵兔、灵狐、灵鹤，各带
/// 建筑 / 购买价 / 产出 / 产出周期 / 类型五列。</item>
/// <item>心情值 0-100，影响产出品质；好感度 0-5 心，影响产出频率。</item>
/// <item>喂养：干草或放牧，灵兽需喂灵草。命名：每只动物可命名。</item>
/// <item>宠物：猫/狗，可互动，影响心情。</item>
/// </list>
/// <para><b>§6.5 没给什么</b>（一律不编，见各处的「待补」）：</para>
/// <list type="bullet">
/// <item><b>成长天数</b>——没有这一列，故 <see cref="AnimalDefinition.GrowthDays"/> 全填 0。</item>
/// <item><b>每次产出几个</b>——没有产量列，故 <see cref="Ranch.ProduceYield"/> = 1（待裁决）。</item>
/// <item><b>产出物的价格</b>——产出只给了名字，故 <c>data/items/ranching.json</c> 里两个价格字段
/// 都填 0（文档未给，待补）。畜产物的售价别处也没有：§6.2 的售价列只管作物。</item>
/// <item><b>蓝鸡的购买价</b>——那一格是「稀有」，不是数字，填 0。</item>
/// <item><b>心情/好感度的加成公式</b>——只说「影响品质」「影响频率」，没有一个数，故本模块
/// 只存值、不做加成（编一个公式等于替文档决定玩家的产出）。</item>
/// <item><b>放牧</b>——需要牧场/建筑系统（M2+）；猪的产出周期写的是「每天（户外）」，
/// 户外条件同样没落地，故按每天产出。</item>
/// <item><b>宠物（猫/狗）</b>——只说了「可互动、影响心情」，没有数值，本模块不实现。</item>
/// <item><b>饲料从哪来</b>（割草/商店/筒仓）——§6.5 与 §6.7 都只说「筒仓：储存干草」，
/// 没说干草怎么获得；M2 集成时由经济/建造模块补。</item>
/// </list>
/// <para>
/// <b>加载后必须用物品表交叉校验</b>（产出物与饲料都要能在物品表里找到）：动物表与物品表
/// 对不上是「各自的测试都发现不了」的那种错——动物表自身完全自洽，物品表也是，
/// 而症状要到玩家收畜产或喂食时才显形。
/// </para>
/// </remarks>
public sealed class AnimalTable : IAnimalTable
{
    /// <summary>缺省动物表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/ranching/animals.json";

    private readonly Dictionary<string, AnimalDefinition> _byId;
    private readonly ReadOnlyCollection<AnimalDefinition> _all;

    private AnimalTable(Dictionary<string, AnimalDefinition> byId, ReadOnlyCollection<AnimalDefinition> all)
    {
        _byId = byId;
        _all = all;
    }

    /// <summary>按 JSON 里的顺序排列，牧场菜单直接照用。</summary>
    public IReadOnlyCollection<AnimalDefinition> All => _all;

    public bool TryGet(string animalId, out AnimalDefinition definition)
    {
        if (_byId.TryGetValue(animalId, out AnimalDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;   // out 必须先赋值：找不到以返回值 false 表达（同 ItemTable）
        return false;
    }

    public AnimalDefinition Get(string animalId) =>
        _byId.TryGetValue(animalId, out AnimalDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"动物表里没有 id 为「{animalId}」的动物");

    /// <param name="items">用于交叉校验两张表是否对得上——这是本方法需要物品表的原因，不是可选装饰。</param>
    public static AnimalTable FromJson(string json, IItemTable items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("animals", out JsonElement animals) ||
            animals.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("动物表缺少 animals 数组");
        }

        var byId = new Dictionary<string, AnimalDefinition>(StringComparer.Ordinal);
        var all = new List<AnimalDefinition>();

        foreach (JsonElement element in animals.EnumerateArray())
        {
            AnimalDefinition definition = ParseAnimal(element);

            // 重复 id 会让「按 id 取到的是哪一份」取决于文件顺序，且两份的产出可能不同——数据错误，启动即报
            if (!byId.TryAdd(definition.AnimalId, definition))
                throw new InvalidDataException($"动物表出现重复的动物 id：{definition.AnimalId}");

            all.Add(definition);
        }

        CrossCheck(all, items);

        return new AnimalTable(byId, all.AsReadOnly());
    }

    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static AnimalTable FromFile(string path, IItemTable items) => FromJson(File.ReadAllText(path), items);

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省动物表，与 <c>CropTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    /// <param name="items">同 <see cref="FromJson"/>。</param>
    public static AnimalTable LoadDefault(IItemTable items)
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path, items);
    }

    /// <summary>
    /// 两张表对不上就是数据错误：动物表里写了一个物品表里没有的 id，玩家收到畜产或喂食时才发现取不到物品。
    /// 这里一次把全部对不上的条目都列出来——一条一条修比每次重跑才发现下一条快得多。
    /// </summary>
    private static void CrossCheck(List<AnimalDefinition> animals, IItemTable items)
    {
        var missing = new List<string>();

        foreach (AnimalDefinition animal in animals)
        {
            // 饲料一并校验：喂食要扣它，而它缺了不会报错，只会让「喂不了」变成一个查不出的谜
            if (!items.TryGet(animal.ProduceItemId, out _)) missing.Add(animal.ProduceItemId);
            if (!items.TryGet(animal.FeedItemId, out _)) missing.Add(animal.FeedItemId);
        }

        if (missing.Count > 0)
            throw new InvalidDataException($"动物表里有物品表找不到的 id：{string.Join("、", missing)}");
    }

    private static AnimalDefinition ParseAnimal(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("动物表出现不是对象的条目");

        string? animalId = OptionalString(element, "id");
        if (string.IsNullOrWhiteSpace(animalId))
            throw new InvalidDataException("动物表出现空 id");

        string? name = OptionalString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"动物 {animalId} 缺少 name");

        string? building = OptionalString(element, "building");
        if (string.IsNullOrWhiteSpace(building))
            throw new InvalidDataException($"动物 {animalId} 缺少 building");

        int buyPrice = RequiredInt(element, "buyPrice", animalId!);

        // 负的购买价会让「买动物倒赚一笔」，0 才是「文档未给」
        if (buyPrice < 0)
            throw new InvalidDataException($"动物 {animalId} 的 buyPrice 为 {buyPrice}，不能为负");

        string? produceId = OptionalString(element, "produceId");
        if (string.IsNullOrWhiteSpace(produceId))
            throw new InvalidDataException($"动物 {animalId} 缺少 produceId");

        int intervalDays = RequiredInt(element, "productionIntervalDays", animalId!);

        // 非正的产出周期意味着「每天产出」或「永远不产出」，两种都不是 §6.5 的意思
        if (intervalDays <= 0)
            throw new InvalidDataException($"动物 {animalId} 的 productionIntervalDays 为 {intervalDays}，必须为正");

        int growthDays = RequiredInt(element, "growthDays", animalId!);
        if (growthDays < 0)
            throw new InvalidDataException($"动物 {animalId} 的 growthDays 为 {growthDays}，不能为负");

        return new AnimalDefinition(
            animalId!,
            name!,
            building!,
            buyPrice,
            produceId!,
            intervalDays,
            RequiredType(element, animalId!),
            growthDays);
    }

    /// <summary>
    /// 类型名按<b>大小写不敏感</b>匹配（同 <see cref="ItemTable"/> 解析分类）。
    /// 不用 <c>Enum.TryParse</c>：它会把 "1" 认成序号 1 的类型，于是「按数字写类型」这种
    /// 会随枚举插值错位的事被静默接受。
    /// </summary>
    private static AnimalType RequiredType(JsonElement element, string animalId)
    {
        if (!element.TryGetProperty("type", out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"动物 {animalId} 缺少字符串字段 type");

        string text = value.GetString()!;
        foreach (AnimalType candidate in Enum.GetValues<AnimalType>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase)) return candidate;
        }

        throw new InvalidDataException($"动物 {animalId} 的类型「{text}」不是合法的 AnimalType");
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int RequiredInt(JsonElement element, string property, string animalId)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"动物 {animalId} 缺少数字字段 {property}");

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
