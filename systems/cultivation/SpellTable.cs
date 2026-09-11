using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 生活法术表的静态部分，数据在 <c>data/cultivation/spells.json</c>。加载时逐条校验，
/// 理由同 <see cref="RealmTable"/> / <see cref="CultivationSpeedTable"/>：
/// 表是外部输入，错在表里就该在加载时炸，而不是等玩家在田里按了键才发现。
/// </summary>
/// <remarks>
/// <b>为什么拿境界表交叉校验</b>：解锁层数是**境界表的知识**（炼气期有 13 个小境界），
/// 而它写在法术表里。一条要求「炼气 14 层」的法术永远解不开，而且没有任何报错——
/// 玩家只会觉得这个法术是坏的。所以两张表在加载时对一次，同 <c>CultivationSpeedTable</c>
/// 对逐层开销的条数、<c>RealmTable</c> 对门槛引用的境界。
/// </remarks>
public sealed class SpellTable : ISpellTable
{
    /// <summary>缺省表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/cultivation/spells.json";

    private readonly ReadOnlyCollection<SpellDefinition> _spells;
    private readonly Dictionary<string, SpellDefinition> _byId;

    private SpellTable(ReadOnlyCollection<SpellDefinition> spells, Dictionary<string, SpellDefinition> byId)
    {
        _spells = spells;
        _byId = byId;
    }

    public IReadOnlyList<SpellDefinition> Spells => _spells;

    public SpellDefinition Get(string spellId)
    {
        // id 为 null 时字典会抛 ArgumentNullException，但那个消息里没有「法术表」这层语境
        if (spellId is null) throw new ArgumentNullException(nameof(spellId));

        return _byId.TryGetValue(spellId, out SpellDefinition? spell)
            ? spell
            : throw new KeyNotFoundException($"法术表里没有 id 为「{spellId}」的法术");
    }

    /// <param name="realms">
    /// 校验每条法术的解锁层数用：层数是境界表的知识，法术表只记「要求第几层」——
    /// 两个数字各归各的表，交叉点就在这里查。
    /// </param>
    public static SpellTable FromJson(string json, IRealmTable realms)
    {
        ArgumentNullException.ThrowIfNull(realms);

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("法术表不是对象");

        JsonElement spells = RequiredArray(document.RootElement, "spells", "法术表");

        var parsed = new List<SpellDefinition>();
        var byId = new Dictionary<string, SpellDefinition>(StringComparer.Ordinal);

        foreach (JsonElement element in spells.EnumerateArray())
        {
            SpellDefinition spell = ParseSpell(element, realms);

            // 重复 id 会让「这条法术要几层、花多少」读出来哪一份取决于文件顺序，
            // 而两份还可能一条 5 灵力、一条 50 灵力
            if (!byId.TryAdd(spell.Id, spell))
                throw new InvalidDataException($"法术表出现重复 id：{spell.Id}");

            parsed.Add(spell);
        }

        // 空表答不出任何问题（玩家按了键该放什么、够不够层数），只可能是文件被写坏了
        if (parsed.Count == 0)
            throw new InvalidDataException("法术表的 spells 是空的");

        return new SpellTable(parsed.AsReadOnly(), byId);
    }

    public static SpellTable FromFile(string path, IRealmTable realms) =>
        FromJson(File.ReadAllText(path), realms);

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省表，与 <c>RealmTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static SpellTable LoadDefault(IRealmTable realms)
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path, realms);
    }

    private static SpellDefinition ParseSpell(JsonElement element, IRealmTable realms)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("法术表的 spells 里出现不是对象的条目");

        string id = RequiredText(element, "id", "法术表");
        string name = RequiredText(element, "name", id);

        // 必须逐字对上枚举名，不收数字：Enum.TryParse 连 "2" 都收，放行它就等于允许按序号写效果——
        // 将来往 SpellEffect 中间插一项，旧数据会静默变成另一个效果（同 RealmTable 的门槛写法）
        string effectId = RequiredText(element, "effect", id);
        if (!Enum.GetNames<SpellEffect>().Contains(effectId, StringComparer.Ordinal))
            throw new InvalidDataException($"法术「{id}」的效果「{effectId}」认不出（只能是 {string.Join(" / ", Enum.GetNames<SpellEffect>())}）");

        string realmId = RequiredText(element, "unlockRealmId", id);
        int unlockStage = RequiredInt(element, "unlockStage", id);

        if (!realms.TryGet(realmId, out RealmDefinition? realm))
            throw new InvalidDataException($"法术「{id}」引用了不存在的大境界「{realmId}」");

        // 解锁层数落在不存在的层次上，等于这条法术永远解不开——玩家按了键什么都不发生，而没有任何报错
        if (unlockStage < 1 || unlockStage > realm.StageCount)
            throw new InvalidDataException(
                $"法术「{id}」要求{realm.Name}的 {unlockStage} 层，而它只有 {realm.StageCount} 个小境界");

        int spiritCost = RequiredInt(element, "spiritCost", id);

        // 负数消耗是「放一次法反而涨灵力」——池子的 Restore 才是加灵力的入口，消耗只能是正数或 0
        // （0 有真实的一条：§8.2 的灵气感知「可感知灵气但无法施法」，它不花灵力）
        if (spiritCost < 0)
            throw new InvalidDataException($"法术「{id}」的灵力消耗 {spiritCost} 是负数");

        int areaSize = RequiredInt(element, "areaSize", id);

        // 范围是**以目标格为中心的正方形**，偶数边长没有中心——写 2 的人多半以为它是「2 格」，
        // 而实际会得到一块不知道偏向哪边、还要靠取整规则决定的区域
        if (areaSize < 1)
            throw new InvalidDataException($"法术「{id}」的作用范围 {areaSize} 不是正的边长");

        if (areaSize % 2 == 0)
            throw new InvalidDataException(
                $"法术「{id}」的作用范围边长 {areaSize} 是偶数——以目标格为中心的正方形只能是奇数边长");

        return new SpellDefinition(
            id, name, Enum.Parse<SpellEffect>(effectId), realmId, unlockStage, spiritCost, areaSize);
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string RequiredText(JsonElement element, string property, string id)
    {
        string? text = OptionalString(element, property);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"{id} 缺少文本字段 {property}");

        return text!;
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
            throw new InvalidDataException($"{id} 缺少数字字段 {property}");

        return value.GetInt32();
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
