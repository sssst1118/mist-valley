using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace XingGame.Systems.Crafting;

/// <summary>
/// 炼丹师等级表，数据在 <c>data/crafting/alchemy_ranks.json</c>。加载时逐条校验，理由同
/// <c>SpiritLandTable</c> / <c>RecipeTable</c>：表是外部输入，错在表里就该在加载时炸，
/// 而不是等玩家攒够了材料才发现这一味丹谁也炼不出来。
/// </summary>
/// <remarks>
/// <para>
/// <b>九行的数一个都不是编的</b>：品级名与「可炼制丹药品阶」逐字来自 §8.7
/// （<c>docs/public/design.md</c> 第 960-970 行）。所以本文件的校验管的是「抄错了没有、表还成不成立」。
/// </para>
/// <para>
/// <b>品级必须从 1 起连续、不重不漏</b>：品级是「升一品」的刻度，缺一个（比如没有五品）等于那条路
/// 在没人看得见的地方断了——而 §9.3 还给炼丹 5 级 / 10 级各留了两个分支，刻度断档会让它们对不上表。
/// </para>
/// <para>
/// <b>可炼制的阶数不得倒挂</b>：§8.7 那张表逐行都在抬高上限（一品一阶 … 九品九阶）。倒挂的表会让
/// 「升一品」变成能力倒退，而它只在升级的那一刻显形。同一阶出现在两品上<b>不算错</b>（那一品只是不再
/// 抬高上限），拦的是「越高反而越少」。
/// </para>
/// </remarks>
public sealed class AlchemyRankTable : IAlchemyRankTable
{
    /// <summary>缺省表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/crafting/alchemy_ranks.json";

    private readonly List<AlchemyRankDefinition> _all;
    private readonly Dictionary<int, AlchemyRankDefinition> _byRank;

    private AlchemyRankTable(List<AlchemyRankDefinition> all, Dictionary<int, AlchemyRankDefinition> byRank)
    {
        _all = all;
        _byRank = byRank;
    }

    /// <summary>按品级升序（一品在前）：<see cref="RequiredRankForTier"/> 靠这个顺序取「最低的那一品」。</summary>
    public IReadOnlyList<AlchemyRankDefinition> All => _all;

    public bool TryGet(int rank, out AlchemyRankDefinition definition)
    {
        if (_byRank.TryGetValue(rank, out AlchemyRankDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;   // out 必须先赋值：找不到以返回值 false 表达，输出用 null（同 ItemTable）
        return false;
    }

    public int RequiredRankForTier(int tier)
    {
        if (tier < 1)
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "丹药的阶数必须为正");

        // 升序扫第一行够得着的：那就是「最低品级」（今天 = 同号那一品，但答案来自表）
        foreach (AlchemyRankDefinition rank in _all)
        {
            if (rank.MaxTier >= tier) return rank.Rank;
        }

        throw new ArgumentOutOfRangeException(
            nameof(tier), tier,
            $"品级表里没有炼得出 {tier} 阶丹药的品级（表到 {_all[^1].MaxTier} 阶为止）");
    }

    public static AlchemyRankTable FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("ranks", out JsonElement ranks) ||
            ranks.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("炼丹师等级表缺少 ranks 数组");
        }

        var parsed = new List<AlchemyRankDefinition>();
        var byRank = new Dictionary<int, AlchemyRankDefinition>();

        foreach (JsonElement entry in ranks.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("炼丹师等级表出现不是对象的条目");

            int rank = RequiredInt(entry, "rank", "炼丹师等级表");
            string name = RequiredText(entry, "name", $"炼丹师等级表 {rank} 品");
            int maxTier = RequiredInt(entry, "maxTier", $"炼丹师等级表 {rank} 品");

            // 0 品 / 负品：品级是从 1 起的刻度（§8.7 最低那一档就是「一品炼丹学徒」）
            if (rank < 1)
                throw new InvalidDataException($"炼丹师等级表出现 {rank} 品：品级必须从 1 起");

            // 能炼 0 阶等于这一品什么也炼不出来；负数更是无意义
            if (maxTier < 1)
                throw new InvalidDataException($"炼丹师等级表「{name}」的可炼制阶数 {maxTier} 不是正数");

            var definition = new AlchemyRankDefinition(rank, name, maxTier);

            // 同一品两条：「这位玩家几品」与「几品能炼这一阶」会各读到一条
            if (!byRank.TryAdd(rank, definition))
                throw new InvalidDataException(
                    $"炼丹师等级表里 {rank} 品出现了两次（「{byRank[rank].Name}」与「{name}」）");

            parsed.Add(definition);
        }

        // 空表答不出任何问题（能炼什么、要几品），只可能是文件被写坏了
        if (parsed.Count == 0)
            throw new InvalidDataException("炼丹师等级表的 ranks 是空的");

        parsed.Sort((left, right) => left.Rank.CompareTo(right.Rank));

        // 断档（1、2、4）与不从 1 起（2、3、4）都在这里被拦：前者让「升一品」没有落点，
        // 后者让 §8.7 最低那一档「一品炼丹学徒」根本不存在
        for (int index = 0; index < parsed.Count; index++)
        {
            if (parsed[index].Rank != index + 1)
                throw new InvalidDataException(
                    $"炼丹师等级表的品级必须从 1 起连续（一品 ⇒ 1），第 {index + 1} 条却是 {parsed[index].Rank} 品"
                    + $"「{parsed[index].Name}」——断档会让「升一品」在没人看得见的地方断掉");

            if (index > 0 && parsed[index].MaxTier < parsed[index - 1].MaxTier)
                throw new InvalidDataException(
                    $"炼丹师等级表「{parsed[index].Name}」只能炼到 {parsed[index].MaxTier} 阶，"
                    + $"比前一品「{parsed[index - 1].Name}」的 {parsed[index - 1].MaxTier} 阶还低——"
                    + "品级越高越不能炼，是倒挂的表");
        }

        return new AlchemyRankTable(parsed, byRank);
    }

    public static AlchemyRankTable FromFile(string path) => FromJson(File.ReadAllText(path));

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省表，与 <c>RecipeTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static AlchemyRankTable LoadDefault()
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path);
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上是谁才定位得到那一行。</summary>
    private static string RequiredText(JsonElement element, string property, string owner)
    {
        string? text = OptionalString(element, property);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"{owner} 缺少文本字段 {property}");

        return text!;
    }

    private static int RequiredInt(JsonElement element, string property, string owner)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"{owner} 缺少数字字段 {property}");

        // 用 TryGetInt32 而不是 GetInt32：手写 JSON 里把品级写成 1.5 或 1e30 都是常事，
        // 而 GetInt32 会抛一条不带品级的 FormatException——报错里定位不到是哪一行
        if (!value.TryGetInt32(out int parsed))
            throw new InvalidDataException($"{owner} 的 {property} 不是整数：{value.GetRawText()}");

        return parsed;
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
