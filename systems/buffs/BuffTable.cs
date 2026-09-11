using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace XingGame.Systems.Buffs;

/// <summary>
/// 增益表的静态部分，数据在 <c>data/buffs/buffs.json</c>。加载时逐条校验，理由同
/// <c>SpiritLandTable</c> / <c>SpellTable</c>：表是外部输入，错在表里就该在加载时炸，
/// 而不是等玩家吃下那颗丹才发现。
/// </summary>
/// <remarks>
/// <para>
/// <b>这张表里的数全是文档直给，没有一个是推的</b>：聚气散 +50% / 7 天出自 §8.3 修炼速度体系
/// （<c>docs/public/design.md</c> 815 行），灵芽羹 +20% / 1 天出自 §12.4 烹饪系统（1368 行），
/// 轻身术 +20% 出自 §8.2 炼气 7-9 层那一格（484 行）、时长出自 ARCHITECTURE 未定义项备案 #78。
/// 所以本文件的校验管的是「抄错了没有、表还成不成立」，不是「推导自不自洽」——后者是
/// <c>cultivation_speed.json</c> 那两张数的处境。
/// </para>
/// <para>
/// <b>倍率必须为正，但可以小于 1</b>：0 倍让这一档永远归零、负数直接倒着扣（症状是「吃了丹反而
/// 更慢」），两者都只可能是漏填；而小于 1 是合法的——§8.3 里就有「风水凶 -20%」「心境不稳 -30%」
/// 那种行，只是今天还没有那样的增益。同理，时长必须为正：0 是「施加了等于没施加」。
/// </para>
/// </remarks>
public sealed class BuffTable : IBuffTable
{
    /// <summary>缺省表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/buffs/buffs.json";

    private readonly ReadOnlyCollection<BuffDefinition> _buffs;
    private readonly Dictionary<string, BuffDefinition> _byId;

    private BuffTable(ReadOnlyCollection<BuffDefinition> buffs, Dictionary<string, BuffDefinition> byId)
    {
        _buffs = buffs;
        _byId = byId;
    }

    public IReadOnlyList<BuffDefinition> Buffs => _buffs;

    public BuffDefinition Get(string buffId)
    {
        // id 为 null 时字典会抛 ArgumentNullException，但那个消息里没有「增益表」这层语境
        if (buffId is null) throw new ArgumentNullException(nameof(buffId));

        return _byId.TryGetValue(buffId, out BuffDefinition? buff)
            ? buff
            : throw new KeyNotFoundException($"增益表里没有 id 为「{buffId}」的增益");
    }

    public bool TryGet(string buffId, out BuffDefinition buff)
    {
        if (buffId is not null && _byId.TryGetValue(buffId, out BuffDefinition? found))
        {
            buff = found;
            return true;
        }

        // 可空警告：认不出时这个 out 一定是 null，而调用方只在返回 true 时读它
        buff = null!;
        return false;
    }

    public static BuffTable FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("增益表不是对象");

        JsonElement buffs = RequiredArray(document.RootElement, "buffs", "增益表");

        var parsed = new List<BuffDefinition>();
        var byId = new Dictionary<string, BuffDefinition>(StringComparer.Ordinal);

        foreach (JsonElement element in buffs.EnumerateArray())
        {
            BuffDefinition buff = ParseBuff(element);

            // 重复 id 会让「这条增益乘多少、持续多久」读出来哪一份取决于文件顺序，
            // 而两份还可能一条 ×1.5、一条 ×1.2
            if (!byId.TryAdd(buff.Id, buff))
                throw new InvalidDataException($"增益表出现重复 id：{buff.Id}");

            parsed.Add(buff);
        }

        // 空表答不出任何问题（这个 id 是什么增益、乘多少），只可能是文件被写坏了
        if (parsed.Count == 0)
            throw new InvalidDataException("增益表的 buffs 是空的");

        return new BuffTable(parsed.AsReadOnly(), byId);
    }

    public static BuffTable FromFile(string path) => FromJson(File.ReadAllText(path));

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省表，与 <c>SpiritLandTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static BuffTable LoadDefault()
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path);
    }

    private static BuffDefinition ParseBuff(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("增益表的 buffs 里出现不是对象的条目");

        string id = RequiredText(element, "id", "增益表");
        string name = RequiredText(element, "name", id);

        // 目标属性逐字对枚举名，不收数字（同 SpiritLandTable 对季节、SpellTable 对效果的做法）：
        // 按序号写的那一刻起，往枚举中间插一项就会让旧数据静默指向另一个属性
        string targetName = RequiredText(element, "target", id);
        if (!Enum.GetNames<BuffTarget>().Contains(targetName, StringComparer.Ordinal))
            throw new InvalidDataException($"增益「{name}」的目标属性「{targetName}」不在 BuffTarget 里");

        double multiplier = RequiredDouble(element, "multiplier", id);

        // 非有限值（1e999 会被读成 NaN/∞）会让连乘结果整片变成 NaN，而 NaN 一路乘下去不会报错
        if (!double.IsFinite(multiplier) || multiplier <= 0)
            throw new InvalidDataException($"增益「{name}」的倍率 {multiplier} 不是正的有限数");

        int duration = RequiredInt(element, "durationMinutes", id);

        // 0 分钟 = 施加了正好等于到期（查的时候永远查不到），负数更是倒着算——都只可能是漏填
        if (duration <= 0)
            throw new InvalidDataException(
                $"增益「{name}」的时长 {duration} 不是正数——0 分钟等于施加了没施加");

        return new BuffDefinition(id, name, Enum.Parse<BuffTarget>(targetName), multiplier, duration);
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
