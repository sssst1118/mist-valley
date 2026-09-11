using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 静态境界表，数据在 <c>data/cultivation/realms.json</c>。加载时逐条校验，理由同
/// <see cref="SpiritRootTable"/>：表是外部输入，错在表里就该在加载时炸。
/// </summary>
/// <remarks>
/// <para>
/// <b>内容出处：§8.1 九大境界总览（<c>docs/public/design.md</c> 458-473 行）、
/// §8.2 炼气期的分层带与三条游戏绑定（476-503 行）。</b>
/// </para>
/// <para>
/// <b>渡劫期的小境界是「待劫 / 渡劫中」而不是初期/中期/后期/大圆满</b>：§8.1 那一格
/// 写的是「—（只分“待劫”和“渡劫中”）」。所以本表不把「四小境界」当成所有大境界的通则——
/// 那是数据，不是代码里的循环。
/// </para>
/// </remarks>
public sealed class RealmTable : IRealmTable
{
    /// <summary>缺省境界表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/cultivation/realms.json";

    private readonly Dictionary<string, RealmDefinition> _byId;
    private readonly Dictionary<string, int> _orderById;
    private readonly ReadOnlyCollection<RealmDefinition> _realms;
    private readonly Dictionary<CultivationGate, CultivationGateRequirement> _gatesByGate;
    private readonly ReadOnlyCollection<CultivationGateRequirement> _gates;

    private RealmTable(
        Dictionary<string, RealmDefinition> byId,
        Dictionary<string, int> orderById,
        ReadOnlyCollection<RealmDefinition> realms,
        Dictionary<CultivationGate, CultivationGateRequirement> gatesByGate,
        ReadOnlyCollection<CultivationGateRequirement> gates)
    {
        _byId = byId;
        _orderById = orderById;
        _realms = realms;
        _gatesByGate = gatesByGate;
        _gates = gates;
    }

    public IReadOnlyList<RealmDefinition> Realms => _realms;

    public IReadOnlyCollection<CultivationGateRequirement> Gates => _gates;

    public bool TryGet(string id, out RealmDefinition realm)
    {
        if (_byId.TryGetValue(id, out RealmDefinition? found))
        {
            realm = found;
            return true;
        }

        realm = null!;   // out 必须先赋值：找不到以返回值 false 表达（同 ItemTable/NpcTable）
        return false;
    }

    public RealmDefinition Get(string id)
    {
        // id 为 null 时字典会抛 ArgumentNullException，但那个消息里没有「境界表」这层语境
        if (id is null) throw new ArgumentNullException(nameof(id));

        return _byId.TryGetValue(id, out RealmDefinition? realm)
            ? realm
            : throw new KeyNotFoundException($"境界表里没有 id 为「{id}」的大境界");
    }

    public int OrderOf(string realmId)
    {
        if (realmId is null) throw new ArgumentNullException(nameof(realmId));

        return _orderById.TryGetValue(realmId, out int order)
            ? order
            : throw new KeyNotFoundException($"境界表里没有 id 为「{realmId}」的大境界");
    }

    public CultivationGateRequirement RequirementOf(CultivationGate gate) =>
        _gatesByGate.TryGetValue(gate, out CultivationGateRequirement? requirement)
            ? requirement
            : throw new KeyNotFoundException($"境界表里没有门槛「{gate}」");

    public static RealmTable FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("境界表不是对象");

        JsonElement realmsElement = RequiredArray(document.RootElement, "realms", "境界表");
        JsonElement gatesElement = RequiredArray(document.RootElement, "gates", "境界表");

        var byId = new Dictionary<string, RealmDefinition>(StringComparer.Ordinal);
        var orderById = new Dictionary<string, int>(StringComparer.Ordinal);
        var realms = new List<RealmDefinition>();

        foreach (JsonElement element in realmsElement.EnumerateArray())
        {
            RealmDefinition realm = ParseRealm(element);

            // 重复 id 会让「玩家在哪个境界」读出来哪一份取决于文件顺序，而两份的小境界还可能不同
            if (!byId.TryAdd(realm.Id, realm))
                throw new InvalidDataException($"境界表出现重复 id：{realm.Id}");

            orderById[realm.Id] = realms.Count;   // 顺序 = 修炼顺序，门槛比较靠它
            realms.Add(realm);
        }

        // 空表答不出任何问题（玩家在哪个境界、门槛够不够都无从查起），只可能是文件被写坏了
        if (realms.Count == 0)
            throw new InvalidDataException("境界表的 realms 是空的");

        var gatesByGate = new Dictionary<CultivationGate, CultivationGateRequirement>();
        var gates = new List<CultivationGateRequirement>();
        foreach (JsonElement element in gatesElement.EnumerateArray())
        {
            CultivationGateRequirement requirement = ParseGate(element, byId);

            if (!gatesByGate.TryAdd(requirement.Gate, requirement))
                throw new InvalidDataException($"境界表出现重复的门槛：{requirement.Gate}");

            gates.Add(requirement);
        }

        return new RealmTable(
            byId, orderById, realms.AsReadOnly(), gatesByGate, gates.AsReadOnly());
    }

    public static RealmTable FromFile(string path) => FromJson(File.ReadAllText(path));

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省境界表，与 <c>NpcTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static RealmTable LoadDefault()
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path);
    }

    private static RealmDefinition ParseRealm(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("境界表的 realms 里出现不是对象的条目");

        string? id = OptionalString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("境界表出现空 id");

        string? name = OptionalString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"大境界 {id} 缺少 name");

        var stages = new List<string>();
        foreach (JsonElement stage in RequiredArray(element, "stages", $"大境界 {id}").EnumerateArray())
        {
            if (stage.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(stage.GetString()))
                throw new InvalidDataException($"大境界 {id} 的 stages 里有空名字");

            stages.Add(stage.GetString()!);
        }

        // 没有小境界的大境界是进不去的：玩家在哪个层次都没法表示，门槛也永远算不出来
        if (stages.Count == 0)
            throw new InvalidDataException($"大境界 {id} 的 stages 是空的——没有小境界就没有「第几层」");

        return new RealmDefinition(id!, name!, stages.AsReadOnly(), ParseBands(element, id!, stages.Count));
    }

    /// <summary>
    /// 分层带（§8.2 的五档）。可以整段不给（§8.2 只给了炼气期），给了就必须从 1 层起、
    /// 首尾相接铺满到最后 1 层。
    /// </summary>
    /// <remarks>
    /// <b>为什么要求铺满</b>：断档或重叠的带子不会报错，只会让「十层是什么样的」这一个问题
    /// 有时答得出有时答不出——而漏掉的那一层恰好是别人要写 UI 时才发现。缺的应该是显式的
    /// 「这一档没有数据」，不是一段消失的区间。
    /// </remarks>
    private static ReadOnlyCollection<RealmBand> ParseBands(JsonElement element, string realmId, int stageCount)
    {
        if (!element.TryGetProperty("bands", out JsonElement bands) || bands.ValueKind == JsonValueKind.Null)
            return new List<RealmBand>().AsReadOnly();

        if (bands.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"大境界 {realmId} 的 bands 不是数组");

        var parsed = new List<RealmBand>();
        int expectedFrom = 1;

        foreach (JsonElement band in bands.EnumerateArray())
        {
            if (band.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"大境界 {realmId} 的 bands 里出现不是对象的条目");

            int from = RequiredInt(band, "fromStage", realmId);
            int to = RequiredInt(band, "toStage", realmId);

            // 先查这一段自己成不成立，再查它与上一段接不接得上——反过来的话，一段 3..1 会被
            // 报成「断档」，而真正的问题是区间写反了，查的人会去数上一段
            if (from < 1)
                throw new InvalidDataException($"大境界 {realmId} 的分层带从 {from} 层起——层号从 1 起");

            if (to < from)
                throw new InvalidDataException($"大境界 {realmId} 的分层带 {from}..{to} 区间倒挂");

            if (to > stageCount)
                throw new InvalidDataException($"大境界 {realmId} 的分层带到 {to} 层，超出它的 {stageCount} 个小境界");

            if (from != expectedFrom)
                throw new InvalidDataException(
                    $"大境界 {realmId} 的分层带断档或重叠：这一段从 {from} 层起，上一段应到 {expectedFrom - 1} 层");

            parsed.Add(new RealmBand(
                from,
                to,
                RequiredText(band, "name", realmId),
                RequiredText(band, "ability", realmId),
                RequiredText(band, "gameEffect", realmId)));

            expectedFrom = to + 1;
        }

        // 允许「一段都不给」（筑基及以后文档没写），但给了就得盖到最后一层
        if (parsed.Count > 0 && expectedFrom != stageCount + 1)
            throw new InvalidDataException($"大境界 {realmId} 的分层带只铺到 {expectedFrom - 1} 层，漏了 {stageCount} 层之前的部分");

        return parsed.AsReadOnly();
    }

    private static CultivationGateRequirement ParseGate(JsonElement element, Dictionary<string, RealmDefinition> realms)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("境界表的 gates 里出现不是对象的条目");

        string? id = OptionalString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("境界表出现空的门槛 id");

        // 必须逐字对上枚举名：Enum.TryParse 连 "1" 都收（解析成 SecretRealm），放行它就等于允许
        // 按序号写门槛——将来往枚举中间插一条，旧数据会静默指向另一条门槛（ADR-012 同款理由）
        if (!Enum.GetNames<CultivationGate>().Contains(id, StringComparer.Ordinal))
            throw new InvalidDataException($"境界表里有认不出的门槛 id「{id}」");

        var gate = Enum.Parse<CultivationGate>(id!);

        string realmId = RequiredText(element, "realmId", id!);
        int stage = RequiredInt(element, "stage", id!);

        if (!realms.TryGetValue(realmId, out RealmDefinition? realm))
            throw new InvalidDataException($"门槛 {id} 引用了不存在的大境界「{realmId}」");

        // 门槛落在不存在的层次上，等于这条绑定永远为假——玩家做什么都解不开，而没有任何报错
        if (stage < 1 || stage > realm.StageCount)
            throw new InvalidDataException($"门槛 {id} 要求 {realmId} 的 {stage} 层，而它只有 {realm.StageCount} 个小境界");

        return new CultivationGateRequirement(
            gate,
            RequiredText(element, "name", id!),
            realmId,
            stage,
            RequiredText(element, "reason", id!));
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
