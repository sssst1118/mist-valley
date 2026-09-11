using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 灵脉与福地这两张表的静态部分，数据在 <c>data/cultivation/spirit_land.json</c>。加载时逐条校验，
/// 理由同 <see cref="SpiritRootTable"/> / <see cref="CultivationSpeedTable"/>：
/// 表是外部输入，错在表里就该在加载时炸，而不是等玩家修好了「山谷之心」才发现升不了级。
/// </summary>
/// <remarks>
/// <para>
/// <b>这两张表的数一个都不是编的</b>：六级灵脉的浓度乘数与九阶福地的名称/说明/阶号，逐字来自 §8.8
/// （<c>docs/public/design.md</c> 1005-1031 行）。所以本文件的校验管的是「抄错了没有、表还成不成立」，
/// 不是「推导自不自洽」——后者是 <c>cultivation_speed.json</c> 那两张数的处境。
/// </para>
/// <para>
/// <b>灵脉的浓度必须逐级递增</b>：§8.8 那张表是按强弱排的（「10 条微型灵脉合一」），等级这个词
/// 本身就在说「越高越浓」。不递增的表会让「把灵脉升一级」变成负收益，而它只在升级的那一刻显形。
/// （与灵根表那条「倍率必须为正」同款：给出的数是玩家看得见的效果，方向错了就是错的。）
/// </para>
/// <para>
/// <b>福地的阶号必须从 1 起连续、不重不漏</b>：阶号是「升一阶」的刻度，缺一个（比如没有五阶）
/// 等于那条路在没人看得见的地方断了——而「灵气感知」界面会把阶号显示给玩家。
/// </para>
/// </remarks>
public sealed class SpiritLandTable : ISpiritLandTable
{
    /// <summary>缺省表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/cultivation/spirit_land.json";

    private readonly List<SpiritVeinGrade> _veins;
    private readonly List<BlessedLandGrade> _lands;
    private readonly Dictionary<string, SpiritVeinGrade> _veinsById;
    private readonly Dictionary<string, BlessedLandGrade> _landsById;

    private SpiritLandTable(
        List<SpiritVeinGrade> veins,
        List<BlessedLandGrade> lands,
        Dictionary<string, SpiritVeinGrade> veinsById,
        Dictionary<string, BlessedLandGrade> landsById)
    {
        _veins = veins;
        _lands = lands;
        _veinsById = veinsById;
        _landsById = landsById;
    }

    public IReadOnlyList<SpiritVeinGrade> Veins => _veins;

    public IReadOnlyList<BlessedLandGrade> Lands => _lands;

    public bool TryGetVein(string veinId, out SpiritVeinGrade vein)
    {
        if (veinId is not null && _veinsById.TryGetValue(veinId, out SpiritVeinGrade? found))
        {
            vein = found;
            return true;
        }

        // 可空警告：认不出时这个 out 一定是 null，而调用方只在返回 true 时读它
        vein = null!;
        return false;
    }

    public bool TryGetLand(string landId, out BlessedLandGrade land)
    {
        if (landId is not null && _landsById.TryGetValue(landId, out BlessedLandGrade? found))
        {
            land = found;
            return true;
        }

        land = null!;
        return false;
    }

    public static SpiritLandTable FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("灵脉与福地表不是对象");

        List<SpiritVeinGrade> veins = ParseVeins(document.RootElement);
        List<BlessedLandGrade> lands = ParseLands(document.RootElement);

        // 查表字典在**校验全部通过之后**才建：坏表不该先造出一半索引再炸（同 CultivationSystem
        // 「先整份校验再落盘」的理由——调用方要么拿到一份完整的表，要么什么都没拿到）
        var veinsById = new Dictionary<string, SpiritVeinGrade>(StringComparer.Ordinal);
        foreach (SpiritVeinGrade vein in veins) veinsById.Add(vein.Id, vein);

        var landsById = new Dictionary<string, BlessedLandGrade>(StringComparer.Ordinal);
        foreach (BlessedLandGrade land in lands) landsById.Add(land.Id, land);

        return new SpiritLandTable(veins, lands, veinsById, landsById);
    }

    public static SpiritLandTable FromFile(string path) => FromJson(File.ReadAllText(path));

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省表，与 <c>RealmTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static SpiritLandTable LoadDefault()
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path);
    }

    /// <summary>
    /// 六级灵脉。**浓度逐级递增**（见类注释）：这张表的序就是等级，倒挂的表让「升级」变成负收益。
    /// </summary>
    private static List<SpiritVeinGrade> ParseVeins(JsonElement element)
    {
        JsonElement veins = RequiredArray(element, "veins", "灵脉与福地表");

        var parsed = new List<SpiritVeinGrade>();
        var byId = new Dictionary<string, SpiritVeinGrade>(StringComparer.Ordinal);

        foreach (JsonElement entry in veins.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("灵脉表的 veins 里出现不是对象的条目");

            string id = RequiredText(entry, "id", "灵脉表");
            string name = RequiredText(entry, "name", id);
            string description = RequiredText(entry, "description", id);

            double multiplier = RequiredDouble(entry, "concentrationMultiplier", id);

            // 0 倍让这一级灵气归零（打坐永远练不出东西）、负数倒着扣——文档给的是 +10% 起
            if (multiplier <= 0)
                throw new InvalidDataException($"灵脉「{id}」的灵气浓度乘数 {multiplier} 不是正数");

            if (parsed.Count > 0 && multiplier <= parsed[^1].ConcentrationMultiplier)
                throw new InvalidDataException(
                    $"灵脉「{id}」的灵气浓度乘数 {multiplier} 不比前一级"
                    + $"「{parsed[^1].Name}」的 {parsed[^1].ConcentrationMultiplier} 大——"
                    + "表序就是等级，倒挂的表会让「升一级」变成负收益");

            var vein = new SpiritVeinGrade(id, name, multiplier, description);

            // 重复 id 会让「这条灵脉多浓」读出来哪一份取决于文件顺序，而两份还可能一个 +10% 一个 +500%
            if (!byId.TryAdd(id, vein))
                throw new InvalidDataException($"灵脉表出现重复 id：{id}");

            parsed.Add(vein);
        }

        // 空表答不出任何问题（农场在哪一级、浓度多少），只可能是文件被写坏了
        if (parsed.Count == 0)
            throw new InvalidDataException("灵脉表的 veins 是空的");

        return parsed;
    }

    /// <summary>
    /// 九阶福地。**阶号必须从 1 起连续**（见类注释）：这是「升一阶」的刻度。
    /// </summary>
    private static List<BlessedLandGrade> ParseLands(JsonElement element)
    {
        JsonElement lands = RequiredArray(element, "lands", "灵脉与福地表");

        var parsed = new List<BlessedLandGrade>();
        var byId = new Dictionary<string, BlessedLandGrade>(StringComparer.Ordinal);
        var byOrder = new Dictionary<int, BlessedLandGrade>();

        foreach (JsonElement entry in lands.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("福地表的 lands 里出现不是对象的条目");

            string id = RequiredText(entry, "id", "福地表");
            string name = RequiredText(entry, "name", id);
            string description = RequiredText(entry, "description", id);
            int order = RequiredInt(entry, "order", id);

            if (order < 1)
                throw new InvalidDataException($"福地「{id}」的阶号 {order} 不是从 1 起的");

            var land = new BlessedLandGrade(id, order, name, description);

            // 同一阶两条：显示时「他现在几阶」与升级时「下一阶是谁」会各读到一条
            if (!byOrder.TryAdd(order, land))
                throw new InvalidDataException(
                    $"福地表里 {order} 阶出现了两次（「{byOrder[order].Name}」与「{name}」）");

            if (!byId.TryAdd(id, land))
                throw new InvalidDataException($"福地表出现重复 id：{id}");

            parsed.Add(land);
        }

        if (parsed.Count == 0)
            throw new InvalidDataException("福地表的 lands 是空的");

        parsed.Sort((left, right) => left.Order.CompareTo(right.Order));

        // 断档（1、2、4）与不从 1 起（2、3、4）都在这里被拦：前者让「升一阶」没有落点，
        // 后者让「一阶福地」这个 §8.8 的起点根本不存在
        for (int index = 0; index < parsed.Count; index++)
        {
            if (parsed[index].Order != index + 1)
                throw new InvalidDataException(
                    $"福地表的阶号必须从 1 起连续（一阶 ⇒ 1），第 {index + 1} 条却是 {parsed[index].Order} 阶"
                    + $"「{parsed[index].Name}」——断档会让「升一阶」在没人看得见的地方断掉");
        }

        return parsed;
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上是谁才定位得到那一行。</summary>
    private static string RequiredText(JsonElement element, string property, string owner)
    {
        string? text = OptionalString(element, property);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"{owner} 缺少文本字段 {property}");

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
            throw new InvalidDataException($"{owner} 缺少数字字段 {property}");

        return value.GetInt32();
    }

    private static double RequiredDouble(JsonElement element, string property, string owner)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"{owner} 缺少数字字段 {property}");

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
