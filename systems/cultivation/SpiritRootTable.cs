using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 静态灵根表，数据在 <c>data/cultivation/spirit_roots.json</c>。加载时逐条校验——
/// 数据表是外部输入，错在表里就要在加载时炸，不要等几个月后某次突破掷骰才发现
/// （照 <c>ItemTable</c>/<c>NpcTable</c> 的先例）。
/// </summary>
/// <remarks>
/// <para>
/// <b>内容出处：§4.2 六档品级（162-171 行）、§4.3 四种变异灵根（173-179 行）、
/// §4.4 四种先天异灵根（181-187 行）</b>，逐列照抄，一个字都没改。
/// </para>
/// <para>
/// <b>不校验的事</b>：不要求具体灵根的品级只许是「变异」或「先天异」——文档把它们挂在这两档下，
/// 但「某档灵根不许有具体名字」是文档没说的事，代码不替它决定（默认数据对不对由
/// <c>SpiritRootTableTests</c> 逐条对文档，那是数据的问题，不是加载器的规则）。
/// </para>
/// </remarks>
public sealed class SpiritRootTable : ISpiritRootTable
{
    /// <summary>缺省灵根表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/cultivation/spirit_roots.json";

    private readonly Dictionary<string, SpiritRootGrade> _gradesById;
    private readonly Dictionary<string, SpiritRootDefinition> _rootsById;
    private readonly ReadOnlyCollection<SpiritRootGrade> _grades;
    private readonly ReadOnlyCollection<SpiritRootDefinition> _roots;

    private SpiritRootTable(
        Dictionary<string, SpiritRootGrade> gradesById,
        Dictionary<string, SpiritRootDefinition> rootsById,
        ReadOnlyCollection<SpiritRootGrade> grades,
        ReadOnlyCollection<SpiritRootDefinition> roots)
    {
        _gradesById = gradesById;
        _rootsById = rootsById;
        _grades = grades;
        _roots = roots;
    }

    public IReadOnlyCollection<SpiritRootGrade> Grades => _grades;

    public IReadOnlyCollection<SpiritRootDefinition> Roots => _roots;

    public bool TryGetGrade(string id, out SpiritRootGrade grade)
    {
        if (_gradesById.TryGetValue(id, out SpiritRootGrade? found))
        {
            grade = found;
            return true;
        }

        grade = null!;   // out 必须先赋值：找不到以返回值 false 表达（同 ItemTable/NpcTable）
        return false;
    }

    public SpiritRootGrade GetGrade(string id)
    {
        // id 为 null 时字典会抛 ArgumentNullException，但那个消息里没有「灵根表」这层语境
        if (id is null) throw new ArgumentNullException(nameof(id));

        return _gradesById.TryGetValue(id, out SpiritRootGrade? grade)
            ? grade
            : throw new KeyNotFoundException($"灵根表里没有 id 为「{id}」的品级");
    }

    public bool TryGetRoot(string id, out SpiritRootDefinition root)
    {
        if (_rootsById.TryGetValue(id, out SpiritRootDefinition? found))
        {
            root = found;
            return true;
        }

        root = null!;
        return false;
    }

    public SpiritRootDefinition GetRoot(string id)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));

        return _rootsById.TryGetValue(id, out SpiritRootDefinition? root)
            ? root
            : throw new KeyNotFoundException($"灵根表里没有 id 为「{id}」的具体灵根");
    }

    public static SpiritRootTable FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("灵根表不是对象");

        // 品级可以一条都没有吗？不行：修炼速度与突破概率全在品级上，空表等于这张表答不出任何问题。
        // 具体灵根则允许为空——把变异/先天异整段删掉的表依然自洽（玩家就都是普通品级）
        JsonElement gradesElement = RequiredArray(document.RootElement, "grades", "灵根表");
        if (gradesElement.GetArrayLength() == 0)
            throw new InvalidDataException("灵根表的 grades 是空的——没有品级就没有修炼速度与突破概率");

        JsonElement rootsElement = RequiredArray(document.RootElement, "roots", "灵根表");

        var gradesById = new Dictionary<string, SpiritRootGrade>(StringComparer.Ordinal);
        var grades = new List<SpiritRootGrade>();
        foreach (JsonElement element in gradesElement.EnumerateArray())
        {
            SpiritRootGrade grade = ParseGrade(element);

            // 重复 id 会让「按 id 取到的是哪一份」取决于文件顺序：同一次突破有时按 30% 算有时按 60% 算
            if (!gradesById.TryAdd(grade.Id, grade))
                throw new InvalidDataException($"灵根表出现重复 id：{grade.Id}");

            grades.Add(grade);
        }

        var rootsById = new Dictionary<string, SpiritRootDefinition>(StringComparer.Ordinal);
        var roots = new List<SpiritRootDefinition>();
        foreach (JsonElement element in rootsElement.EnumerateArray())
        {
            SpiritRootDefinition root = ParseRoot(element);

            if (!rootsById.TryAdd(root.Id, root))
                throw new InvalidDataException($"灵根表出现重复 id：{root.Id}");

            // 品级引用要能在同一张表里查到——查不到就是 typo（grade_mutaton），
            // 而症状会是「这位玩家的修炼速度查不出来」，离病因很远
            if (!gradesById.ContainsKey(root.GradeId))
                throw new InvalidDataException($"具体灵根 {root.Id} 引用了不存在的品级「{root.GradeId}」");

            roots.Add(root);
        }

        // 品级与具体灵根共用一个 id 空间：两张表各有一套查法（GetGrade / GetRoot），但 id 是给
        // 存档、UI 与 Mod 看的——同一个 id 在表里指着两样东西，读的人会以为 GradeId 与 RootId
        // 填的是同一件事。同表内重名已经拦了，跨表这一种更隐蔽，一并拦掉
        string? collision = gradesById.Keys.FirstOrDefault(rootsById.ContainsKey);
        if (collision is not null)
            throw new InvalidDataException($"灵根表的品级与具体灵根共用了 id「{collision}」");

        return new SpiritRootTable(gradesById, rootsById, grades.AsReadOnly(), roots.AsReadOnly());
    }

    public static SpiritRootTable FromFile(string path) => FromJson(File.ReadAllText(path));

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省灵根表，与 <c>NpcTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static SpiritRootTable LoadDefault()
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path);
    }

    private static SpiritRootGrade ParseGrade(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("灵根表的 grades 里出现不是对象的条目");

        string id = RequiredId(element, "灵根表");
        string name = RequiredName(element, id);

        int min = RequiredInt(element, "attributeCountMin", id);
        int max = RequiredInt(element, "attributeCountMax", id);

        // 「五行杂灵根」是至少四属性，0 或负数只可能是漏填或写错字段名；倒挂的区间会让
        // 「这个灵根几种属性」在不同调用方给出不同答案（看 min 还是看 max）
        if (min < 1)
            throw new InvalidDataException($"灵根 {id} 的属性数量下限 {min} 小于 1");

        if (max < min)
            throw new InvalidDataException($"灵根 {id} 的属性数量区间倒挂：{min}..{max}");

        double multiplier = RequiredDouble(element, "cultivationSpeedMultiplier", id);

        // 0 倍速会让这一档灵根永远修不出东西、负倍速会倒着减——两者都不是文档里的档
        if (multiplier <= 0)
            throw new InvalidDataException($"灵根 {id} 的修炼速度倍率 {multiplier} 不是正数");

        return new SpiritRootGrade(
            id,
            name,
            min,
            max,
            multiplier,
            RequiredPercent(element, "foundationSuccessPercent", id),
            RequiredPercent(element, "goldenCoreSuccessPercent", id),
            RequiredPercent(element, "nascentSoulSuccessPercent", id));
    }

    private static SpiritRootDefinition ParseRoot(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("灵根表的 roots 里出现不是对象的条目");

        string id = RequiredId(element, "灵根表");
        string name = RequiredName(element, id);

        string? gradeId = OptionalString(element, "gradeId");
        if (string.IsNullOrWhiteSpace(gradeId))
            throw new InvalidDataException($"具体灵根 {id} 缺少 gradeId");

        // 特效文本本切片不实现机制，但**必须有**：§4.3/§4.4 每一行的存在意义就是那一格，
        // 缺了就是一条「名字之外什么都没有」的空条目，玩家选它和不选它毫无差别
        string? gameEffect = OptionalString(element, "gameEffect");
        if (string.IsNullOrWhiteSpace(gameEffect))
            throw new InvalidDataException($"具体灵根 {id} 缺少 gameEffect（特效文本）");

        return new SpiritRootDefinition(
            id,
            name,
            gradeId!,
            OptionalText(element, "mutationSource", id),
            OptionalText(element, "trait", id),
            OptionalText(element, "exclusiveDao", id),
            gameEffect);
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string RequiredId(JsonElement element, string tableName)
    {
        string? id = OptionalString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException($"{tableName}出现空 id");

        return id!;
    }

    private static string RequiredName(JsonElement element, string id)
    {
        string? name = OptionalString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"灵根 {id} 缺少 name");

        return name!;
    }

    /// <summary>可选的文本列：给了就得是句人话，空字符串与缺字段都算「没有」。</summary>
    private static string? OptionalText(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"灵根 {id} 的 {property} 不是有效文本");

        return value.GetString();
    }

    private static JsonElement RequiredArray(JsonElement element, string property, string tableName)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{tableName}缺少 {property} 数组");

        return value;
    }

    private static int RequiredInt(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"灵根 {id} 缺少数字字段 {property}");

        return value.GetInt32();
    }

    private static double RequiredDouble(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"灵根 {id} 缺少数字字段 {property}");

        return value.GetDouble();
    }

    /// <summary>概率是百分数（5 表示 5%）。超过 100 的概率会让「必定成功」变成必然溢出的骰子。</summary>
    private static double RequiredPercent(JsonElement element, string property, string id)
    {
        double percent = RequiredDouble(element, property, id);

        if (percent < 0 || percent > 100)
            throw new InvalidDataException($"灵根 {id} 的 {property} 是 {percent}，超出 0..100 的百分数范围");

        return percent;
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
