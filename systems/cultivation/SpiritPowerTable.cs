using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 灵力池这张账的静态表，数据在 <c>data/cultivation/spirit_power.json</c>。加载时逐条校验，
/// 理由同 <see cref="CultivationSpeedTable"/> / <see cref="SpiritRootTable"/>：表是外部输入，
/// 错在表里就该在加载时炸。
/// </summary>
/// <remarks>
/// <para>
/// <b>这组数的出处是备案 #69/#71，不是设计文档直给</b>（见 <see cref="ISpiritPowerTable"/>）：
/// 上限公式的错是推导不自洽，恢复速率的错是抄错备案——两者的用例也不同。
/// </para>
/// <para>
/// <b>不拿境界表交叉校验</b>（<see cref="CultivationSpeedTable"/> 要，本表不要）：那边校验的是
/// 「逐层开销有 12 条 = 炼气 13 层减 1」，一条**逐层**的表；这里只有一条从层号算上限的一元公式，
/// 没有第二张表可以对齐，层号的上下界由境界表在 <see cref="CultivationSystem"/> 那一侧守着。
/// </para>
/// </remarks>
public sealed class SpiritPowerTable : ISpiritPowerTable
{
    /// <summary>缺省表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/cultivation/spirit_power.json";

    private readonly int _maxSpiritBase;
    private readonly int _maxSpiritPerStage;
    private readonly Dictionary<SpiritRecovery, int> _byRecovery;

    private SpiritPowerTable(int maxSpiritBase, int maxSpiritPerStage, Dictionary<SpiritRecovery, int> byRecovery)
    {
        _maxSpiritBase = maxSpiritBase;
        _maxSpiritPerStage = maxSpiritPerStage;
        _byRecovery = byRecovery;
    }

    /// <summary>上限 = 起点 + 增量 × (层 − 1)。**唯一的算法**，其余地方只准调它。</summary>
    /// <exception cref="ArgumentOutOfRangeException">层号不是从 1 起的。</exception>
    public int MaxSpiritAt(int stage)
    {
        if (stage < 1)
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "层号从 1 起，没有第 0 层");

        return _maxSpiritBase + _maxSpiritPerStage * (stage - 1);
    }

    public int RecoveryPerHour(SpiritRecovery recovery) =>
        _byRecovery.TryGetValue(recovery, out int perHour)
            ? perHour
            : throw new KeyNotFoundException($"灵力表里没有恢复档位「{recovery}」的速率");

    public static SpiritPowerTable FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("灵力表不是对象");

        int maxSpiritBase = RequiredInt(document.RootElement, "maxSpiritBase", "灵力表");

        // 一层的上限是 0 或负数：那样的池子从开局就放不出任何法术（备案 #69 的 1 层是 100），
        // 只可能是漏填
        if (maxSpiritBase <= 0)
            throw new InvalidDataException($"灵力表的一层上限 {maxSpiritBase} 不是正数");

        int maxSpiritPerStage = RequiredInt(document.RootElement, "maxSpiritPerStage", "灵力表");

        // 上限不随层数增长：§8.2 从 4-6 层的「灵气储备可维持短时施法」写到 10-12 层的
        // 「灵气储备质变」，容量恒定与备案 #69 的形状不符；负增量还会让高层的池子比低层小
        if (maxSpiritPerStage <= 0)
            throw new InvalidDataException(
                $"灵力表的每层增量 {maxSpiritPerStage} 不是正数——上限就不随层数涨了");

        Dictionary<SpiritRecovery, int> byRecovery = ParseRecoveryRates(document.RootElement);
        RequireMeditationBeatsAwake(byRecovery);

        return new SpiritPowerTable(maxSpiritBase, maxSpiritPerStage, byRecovery);
    }

    public static SpiritPowerTable FromFile(string path) => FromJson(File.ReadAllText(path));

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省表，与 <c>SpiritRootTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static SpiritPowerTable LoadDefault()
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path);
    }

    /// <summary>
    /// 两个恢复速率。**每一档都必须有**：枚举里加一档而数据没补，就该在这里炸——
    /// 缺的那一档会在运行时变成「那一档永远回不了灵力」，而那时离病根已经很远。
    /// </summary>
    private static Dictionary<SpiritRecovery, int> ParseRecoveryRates(JsonElement element)
    {
        JsonElement entries = RequiredArray(element, "recoveryPerHour", "灵力表");
        var parsed = new Dictionary<SpiritRecovery, int>();

        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("灵力表的 recoveryPerHour 里出现不是对象的条目");

            string id = RequiredText(entry, "recovery", "灵力表");

            // 必须逐字对上枚举名，不收数字：Enum.TryParse 连 "0" 都收（解析成 Awake），放行它就等于
            // 允许按序号写档位——往枚举中间插一档，旧数据会静默指向另一档（同 RealmTable 的门槛）
            if (!Enum.GetNames<SpiritRecovery>().Contains(id, StringComparer.Ordinal))
                throw new InvalidDataException($"灵力表里有认不出的恢复档位「{id}」");

            var recovery = Enum.Parse<SpiritRecovery>(id);
            int perHour = RequiredInt(entry, "perHour", $"恢复档位「{id}」");

            // 0 速率让这一档永远回不了灵力、负速率倒着扣——两种都不是备案 #71 的形状
            if (perHour <= 0)
                throw new InvalidDataException($"灵力表里恢复档位「{id}」的速率 {perHour} 不是正数");

            if (!parsed.TryAdd(recovery, perHour))
                throw new InvalidDataException($"灵力表里恢复档位「{id}」出现了两次");
        }

        foreach (SpiritRecovery recovery in Enum.GetValues<SpiritRecovery>())
        {
            if (!parsed.ContainsKey(recovery))
                throw new InvalidDataException($"灵力表缺了恢复档位「{recovery}」的速率");
        }

        return parsed;
    }

    /// <summary>
    /// 打坐必须比清醒快（备案 #71 的理由原文：「打坐回得快是『修炼即回气』的常见设定，
    /// 也让『没灵力了就去打坐』成为一条教得会的循环」）。
    /// </summary>
    /// <remarks>
    /// 反过来的话，站着忙活比入定回得还快——那条循环就反了，而玩家只会觉得「打坐这个功能坏了」。
    /// 这是**两行数之间的关系**，只有加载时看得出：单看「打坐 2」这一行是合法的正数，
    /// 与清醒那行一起读才现形（同 <see cref="CultivationSpeedTable"/> 校验开销条数与境界数对得上）。
    /// </remarks>
    private static void RequireMeditationBeatsAwake(Dictionary<SpiritRecovery, int> byRecovery)
    {
        int awake = byRecovery[SpiritRecovery.Awake];
        int meditation = byRecovery[SpiritRecovery.Meditation];

        if (meditation > awake) return;

        throw new InvalidDataException(
            $"灵力表里打坐的恢复速率 {meditation}/小时不高于清醒的 {awake}/小时——"
            + "备案 #71 的「没灵力了就去打坐」这条循环就反了");
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
