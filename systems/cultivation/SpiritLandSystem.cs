using System;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 农场的灵脉等级与福地阶（§8.8 灵脉与福地体系，<c>docs/public/design.md</c> 1005-1031 行）。
/// </summary>
/// <remarks>
/// <para>
/// <b>只有等级进存档，浓度是算出来的</b>：存档里两个 id——灵脉与福地各一个；灵气浓度由
/// 灵脉等级查表得到（<see cref="DensityMultiplier"/>），**一个字节都不存**。存了它就有两份
/// 「这座农场多浓」：表一改（或 §8.8 补了福地那一侧的数值），旧档里的那份就是错的，
/// 而症状是「同一个灵脉等级，老玩家的农场练得更快」这种没人查得动的怪事（同 <c>worldSeed</c>
/// 与灵力上限不许存两处的理由）。
/// </para>
/// <para>
/// <b>构造与读档共用一份查表逻辑</b>（同 <see cref="CultivationSystem"/> 的写法）：等级 id
/// 认不出来在两处都抛 <see cref="InvalidDataException"/>——表和存档都是**数据**，对不上就是数据
/// 错误，不许「猜着读」（认不出的 id 静默退回「微型灵脉」会让一位龙脉玩家一夜掉回原点，
/// 而他完全不知道自己踩到了什么）。
/// </para>
/// <para>
/// <b>本类没有任何「升级」入口</b>：§8.8 的三条升级路径都还不存在，见
/// <see cref="ISpiritLandSystem"/> 的注释。将来加的时候，改的只有 <c>Deserialize</c> 的孪生兄弟
/// ——一处按 id 换等级并校验的私有方法。
/// </para>
/// </remarks>
public sealed class SpiritLandSystem : ISpiritLandSystem, ISaveable
{
    private readonly ISpiritLandTable _table;

    private SpiritVeinGrade _vein = null!;
    private BlessedLandGrade _land = null!;

    /// <param name="veinId">§8.8 的灵脉等级 id；新档传「微型灵脉」（农场初始状态）。</param>
    /// <param name="landId">§8.8 的福地阶 id；新档传「一阶」。</param>
    public SpiritLandSystem(ISpiritLandTable table, string veinId, string landId)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));

        (SpiritVeinGrade vein, BlessedLandGrade land) = Resolve(veinId, landId, "构造参数");
        Assign(vein, land);
    }

    public SpiritVeinGrade Vein => _vein;

    public BlessedLandGrade Land => _land;

    /// <summary>
    /// 农场的灵气浓度 = 灵脉等级那一档的乘数（§8.8 的表就是这一列）：微型 1.10 … 龙脉 6.00。
    /// </summary>
    /// <remarks>
    /// 现算，不缓存：缓存就等于把「等级 → 浓度」这条派生关系存了第二份，表一改就会与等级对不上
    /// （同 <see cref="ICultivationSystem.MaxSpirit"/> 的理由）。福地那一侧目前不参与这个乘数。
    /// </remarks>
    public double DensityMultiplier => _vein.ConcentrationMultiplier;

    public string SaveKey => "spirit_land";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    /// <remarks>
    /// <b>1</b>：本切片（M3-5）的第一版，两个 id。
    /// <b>旧档（M2 到 M3-4）里没有 <c>spirit_land</c> 这个键</b>，那不是版本迁移而是**整键缺席**：
    /// <c>SqliteSaveService</c> 对缺席的键是「跳过、让它保持自己的初始状态」，而这里的初始状态
    /// 正是 §8.8 说的「微型灵脉 + 一阶福地」——那几版的世界里农场也一直是这个样子，只是当时没有
    /// 任何东西能把它说出来。所以旧档不需要在 <see cref="Deserialize"/> 里补分支（同 M1 旧档没有
    /// 金币、没有灵根的处理：玩家早就开过局了，凭空补一份才是改档）。
    /// </remarks>
    public int Version => 1;

    public string Serialize() =>
        JsonSerializer.Serialize(new SavedSpiritLand(_vein.Id, _land.Id), _saveJsonOptions);

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009）
        if (fromVersion > Version)
            throw new NotSupportedException($"灵脉与福地存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedSpiritLand saved = JsonSerializer.Deserialize<SavedSpiritLand>(json, _saveJsonOptions)
            ?? throw new InvalidDataException("灵脉与福地存档内容为空");

        // 「字段不在」与「字段是空串」必须分开（ADR-009 / AGENT-BRIEF）：id 的合法值都是
        // 表里那几条，空串只可能是被人改过或写到一半崩了
        if (string.IsNullOrWhiteSpace(saved.VeinId))
            throw new InvalidDataException("灵脉与福地存档缺少 veinId");

        if (string.IsNullOrWhiteSpace(saved.LandId))
            throw new InvalidDataException("灵脉与福地存档缺少 landId");

        (SpiritVeinGrade vein, BlessedLandGrade land) = Resolve(saved.VeinId!, saved.LandId!, "灵脉与福地存档");

        // 先整份校验再落盘：坏存档不该让灵脉停在「读了一半」的状态（照 CultivationSystem 先例）
        Assign(vein, land);
    }

    /// <summary>
    /// 按 id 查出灵脉等级与福地阶，把两处对不上的情况拦下。
    /// </summary>
    private (SpiritVeinGrade Vein, BlessedLandGrade Land) Resolve(string veinId, string landId, string owner)
    {
        if (!_table.TryGetVein(veinId, out SpiritVeinGrade vein))
            throw new InvalidDataException($"{owner}里的灵脉等级「{veinId}」不在灵脉表里");

        if (!_table.TryGetLand(landId, out BlessedLandGrade land))
            throw new InvalidDataException($"{owner}里的福地阶「{landId}」不在福地表里");

        return (vein, land);
    }

    /// <summary>两个字段一起换：分开赋值会让「读了一半」成为可能。</summary>
    private void Assign(SpiritVeinGrade vein, BlessedLandGrade land)
    {
        _vein = vein;
        _land = land;
    }

    /// <summary>
    /// 存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。
    /// </summary>
    /// <remarks>
    /// <b>只有两个 id，没有等级数字、没有名字、更没有浓度</b>：名字与浓度都是表的输出，写进存档
    /// 就有了第二个来源。可空是为了把「字段不在」与「字段是空串」分开（ADR-009）。
    /// </remarks>
    private sealed record SavedSpiritLand(string? VeinId, string? LandId);

    /// <summary>字段名写全，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions _saveJsonOptions = new() { WriteIndented = true };
}
