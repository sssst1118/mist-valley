using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Core.Save;
using XingGame.Core.Time;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 农场的灵脉与福地（M3-5）：起点是 §8.8 的「微型灵脉 + 一阶福地」、灵气浓度**真的接进了打坐**、
/// 等级跨进程不变。
/// </summary>
/// <remarks>
/// <para>
/// 本切片对外的价值全在第三件事上——「灵脉 +10%」在此之前只是 §8.3 表里的一行字。所以这里不单测
/// <c>SpiritLandSystem</c> 的读接口，而是**跨模块**地断言：同一时刻、同一灵根，把灵脉升一级，
/// <see cref="CultivationSystem.Meditate"/> 那一柱香就是收获得更多。乘法是不是真的接上了，
/// 只有这种断言说得清——单测一个乘数函数只能证明它自己算得对。
/// </para>
/// <para>
/// 时刻一律用 <see cref="GameTime"/> 明写（同 <c>MeditationTests</c>），倍率用**真表**：
/// 浓度的六级、季节与时辰的倍率都是文档直给的数，缺省表算出来的账才是玩家的账。
/// </para>
/// </remarks>
public sealed class SpiritLandTests : IDisposable
{
    private const int Slot = 1;
    private const int WorldSeed = 20260912;

    /// <summary>
    /// §8.8 末尾「游戏绑定」的原文起点：**农场初始为「微型灵脉 + 一阶福地」**。
    /// </summary>
    /// <remarks>
    /// 这两个 id 与 <c>world/GameRoot.cs</c> 的 <c>StartingVeinId</c> / <c>StartingLandId</c>
    /// 是同一对值——那个文件属于桥接层，测试工程引用不到（它只引 Core，ADR-001/002），
    /// 所以这里是照**文档原文**再写一遍：§8.8 说起点是这两个，哪一边改漏了都对不上。
    /// </remarks>
    private const string StartingVeinId = "vein_micro";

    /// <inheritdoc cref="StartingVeinId"/>
    private const string StartingLandId = "land_1";

    private static readonly SpiritLandTable Land = SpiritLandTable.LoadDefault();
    private static readonly SpiritRootTable Roots = SpiritRootTable.LoadDefault();
    private static readonly RealmTable Realms = RealmTable.LoadDefault();
    private static readonly CultivationSpeedTable Speed = CultivationSpeedTable.LoadDefault(Realms);
    private static readonly SpiritPowerTable SpiritPower = SpiritPowerTable.LoadDefault();

    private readonly string _saveDirectory =
        Path.Combine(Path.GetTempPath(), "xing-spirit-land-save-" + Guid.NewGuid().ToString("N"));

    /// <summary>SQLite 连接池可能还攥着文件句柄，清不掉临时目录不该让测试失败。</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_saveDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>§3.1 的清晨：6:00-9:00 是「打坐修炼」那一段。</summary>
    private static GameTime Morning(int day = 1, Season season = Season.Spring) =>
        new(1, season, day, GameTime.FirstHour, 0);

    private static SpiritLandSystem Farm(string veinId = StartingVeinId, string landId = StartingLandId) =>
        new(Land, veinId, landId);

    /// <summary>同一个玩家（双灵根 1.0x、炼气一层），打坐的地方换一个灵脉等级。</summary>
    private static CultivationSystem CultivationAt(string veinId) =>
        new(Roots, Realms, Speed, SpiritPower, Farm(veinId),
            "grade_true_dual", rootId: null, "qi_refining", stage: 1);

    // ── 起点与浓度 ──────────────────────────────────────────────────

    [Fact]
    public void 新档农场_微型灵脉加一阶福地_浓度是一点一零()
    {
        SpiritLandSystem farm = Farm();

        // §8.8 的两张表：微型灵脉那一格写的是 +10%（表里落成 1.10），一阶的名字就叫「福地」
        Assert.Equal("vein_micro", farm.Vein.Id);
        Assert.Equal("微型灵脉", farm.Vein.Name);
        Assert.Equal(1.10, farm.DensityMultiplier, precision: 10);

        Assert.Equal("land_1", farm.Land.Id);
        Assert.Equal(1, farm.Land.Order);
        Assert.Equal("福地", farm.Land.Name);

        // 起点是**最低那一级**：§8.8 给微型灵脉的「游戏表现」是「农场初始状态，灵气稀薄」，
        // 而这张表的序就是强弱。写成别的档（哪怕写成小型）都让「灵气稀薄」这句话落空
        Assert.Equal(Land.Veins.Min(vein => vein.ConcentrationMultiplier), farm.DensityMultiplier);
    }

    [Fact]
    public void 存档身份_键与版本()
    {
        // 键名进的是 SQLite 的 blob 表，改了它（或改了版本号）等于让所有旧档的这一列失效
        Assert.Equal("spirit_land", Farm().SaveKey);
        Assert.Equal(1, Farm().Version);
        Assert.True(typeof(ISaveable).IsAssignableFrom(typeof(SpiritLandSystem)));
    }

    [Fact]
    public void 浓度是从等级算出来的_不是第二份状态()
    {
        // 浓度没有 setter、也没有单独的存档字段（见下面那条「只存两个 id」）：它就是灵脉那一档的
        // §8.8 数值。这条钉的是「数据库里那份浓度」不会存在——它只是这里现算的
        for (int index = 0; index < Land.Veins.Count; index++)
        {
            SpiritVeinGrade vein = Land.Veins[index];
            Assert.Equal(vein.ConcentrationMultiplier, Farm(vein.Id).DensityMultiplier, precision: 10);
        }
    }

    // ── 接进修炼速度（本切片的核心价值）─────────────────────────────

    [Fact]
    public void 灵脉升一级_同一时刻同一灵根练得更快()
    {
        // 跨模块的行为断言：同一柱香（春季清晨 6:00 起坐一小时）、同一个双灵根玩家，
        // 只有修炼处的灵脉不一样。
        //   微型 1.10：10 × 1.0 × 1.10 × 1.10 = 12.1 → 12 点
        //   小型 1.25：10 × 1.0 × 1.10 × 1.25 = 13.75 → 14 点
        //   龙脉 6.00：10 × 1.0 × 1.10 × 6.00 = 66 点
        //（前两个数是 §4.2 的灵根倍率与 §8.3 的春季倍率，第四项才是 §8.8 的浓度）
        int micro = CultivationAt("vein_micro").Meditate(Morning(), 60);
        int small = CultivationAt("vein_small").Meditate(Morning(), 60);
        int dragon = CultivationAt("vein_dragon").Meditate(Morning(), 60);

        Assert.Equal(12, micro);
        Assert.Equal(14, small);
        Assert.Equal(66, dragon);

        Assert.True(small > micro, "灵脉升一级之后同一柱香没收获得更多——浓度那一项没接进乘法");
        Assert.True(dragon > small);

        // 与倍率函数那一侧对一次账：两个出口说的是同一件事（Meditate 也是拿它算的）
        Assert.Equal(1.10 * 1.10, CultivationAt("vein_micro").SpeedMultiplierAt(Morning()), precision: 10);
        Assert.Equal(1.10 * 1.25, CultivationAt("vein_small").SpeedMultiplierAt(Morning()), precision: 10);
    }

    [Fact]
    public void 浓度在乘法里_不是加法里()
    {
        // §8.3 的每一行都是独立的加成，同时成立时连乘：春季 1.10 × 微型灵脉 1.10 = 1.21，
        // 不是 1.20。相加会随因素增多越来越偏离文档，而偏差只在两个加成同时出现时才显形
        // （同 MeditationTests 那条「相乘不相加」的算法，这里把第四个因素也拉进来）
        CultivationSystem system = CultivationAt("vein_small");   // 1.25

        // 春（1.10）× 子时（1.30）× 小型灵脉（1.25）= 1.7875；相加会得到 1.65
        double multiplier = system.SpeedMultiplierAt(new GameTime(1, Season.Spring, 1, 23, 0));

        Assert.Equal(1.10 * 1.30 * 1.25, multiplier, precision: 10);
        Assert.NotEqual(1.10 + 0.30 + 0.25, multiplier, precision: 10);
    }

    [Fact]
    public void 浓度来自表_换个表就换一套数()
    {
        // 替身表：「数值不在系统里，在表上」这件事只有行为验得出来——成员名里藏不藏数字看不出来。
        // 换成 ×2.0 的表，同一柱香就该翻倍（10 × 1.0 × 1.10 × 2.0 = 22）。
        // 同 M3-3 对灵力那组数的做法（SpiritPowerTests 的「换个替身表就换一套数」）
        var doubled = new StandInTable(density: 2.0);
        var farm = new SpiritLandSystem(doubled, "vein_stand_in", "land_stand_in");

        Assert.Equal(2.0, farm.DensityMultiplier, precision: 10);

        var system = new CultivationSystem(Roots, Realms, Speed, SpiritPower, farm,
            "grade_true_dual", rootId: null, "qi_refining", 1);

        Assert.Equal(22, system.Meditate(Morning(), 60));
    }

    // ── 存档：往返、旧档、派生量 ────────────────────────────────────

    [Fact]
    public void 存档往返_等级跨进程不变()
    {
        // 存 → 读：读档方用**另一个起点**构造，证明结果来自存档而不是初始值恰好相同
        var saves = new SqliteSaveService(_saveDirectory);
        SpiritLandSystem original = Farm("vein_medium", "land_5");   // 中型灵脉 + 五阶顶级福地
        Assert.Equal(1.50, original.DensityMultiplier, precision: 10);

        saves.Save(Slot, new SaveMeta(WorldSeed, "升过级的农场", "第 1 年 春 3 日 16:00"),
            new ISaveable[] { original });

        SpiritLandSystem restored = Farm();
        Assert.True(saves.Load(Slot, new ISaveable[] { restored }));

        Assert.Equal("vein_medium", restored.Vein.Id);
        Assert.Equal("中型灵脉", restored.Vein.Name);
        Assert.Equal("land_5", restored.Land.Id);
        Assert.Equal(5, restored.Land.Order);
        Assert.Equal(1.50, restored.DensityMultiplier, precision: 10);   // 浓度跟着等级自己长回来
    }

    [Fact]
    public void M3_4及更早的旧档_没有这个键_读档照常且不覆盖构造时的状态()
    {
        // 那几版的存档里根本没有 spirit_land 这个键（那时还没有灵脉与福地这两个概念）。
        // SqliteSaveService 对缺席的键是「跳过、让它保持自己的初始状态」，所以这里要验的是：
        // 读档整体成功，农场**停在构造时给的那一级**，而不是被空 JSON 喂出一个半死的状态。
        // 真实游戏里那份构造状态就是起点（§8.8 的「微型灵脉 + 一阶福地」，由 GameRoot 传进来）；
        // 下面故意用别的等级构造，是为了证明「缺席 = 不动它」，而不是「缺席 = 悄悄读成某个默认值」
        //——后者会把将来换起点这件事变成静默失效（同 M1 旧档没有金币、没有灵根：玩家早就开过局了，
        // 凭空补一份才是改档）
        var saves = new SqliteSaveService(_saveDirectory);
        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 3 日 16:00"),
            new ISaveable[]
            {
                new RawBlob("time", 1, """{"Year":1,"Season":"Spring","Day":3,"Hour":16,"Minute":0}"""),
                new RawBlob("cultivation", 3,
                    """{ "GradeId": "grade_true_dual", "RootId": null, "RealmId": "qi_refining", "Stage": 4, "Cultivation": 7, "Spirit": 120 }"""),
            });

        SpiritLandSystem restored = Farm("vein_dragon", "land_9");   // 故意用别的起点构造

        Assert.True(saves.Load(Slot, new ISaveable[] { restored }));

        // 缺席的键**不覆盖**构造时给的状态：这份档没说过农场是哪一级，那就停在起点。
        // （注意这与「读成微型」不是一回事——将来若新档起点换了，旧档该跟着新起点走）
        Assert.Equal("vein_dragon", restored.Vein.Id);
        Assert.Equal("land_9", restored.Land.Id);
    }

    [Fact]
    public void 存档里只存两个id_浓度与名字都是派生量()
    {
        // 「同一个事实存两处」是这个仓库栽过跟头的地方：浓度存进档里就有了两份「这座农场多浓」——
        // 表一改（或 §8.8 补了福地那一侧的数值），旧档那份就是错的，而症状是「同一个灵脉等级，
        // 老玩家的农场练得更快」。所以落盘的 JSON 只许有两个键
        string json = Farm("vein_large", "land_6").Serialize();

        using var document = JsonDocument.Parse(json);
        string[] keys = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // 键名与其余 blob 一样是「字段名写全」（ADR-012）带来的 PascalCase——改大小写等于改存档格式
        Assert.Equal(new[] { "LandId", "VeinId" }, keys);

        // 两个值都是**字符串 id**：多出一个数字就说明有人把浓度（或阶号）也存了进来
        Assert.All(
            document.RootElement.EnumerateObject(),
            property => Assert.Equal(JsonValueKind.String, property.Value.ValueKind));

        // 名字、浓度、说明一个都不该出现：它们全是表算出来的
        Assert.DoesNotContain("Multiplier", json, StringComparison.Ordinal);
        Assert.DoesNotContain("大型", json, StringComparison.Ordinal);
        Assert.DoesNotContain("洞天", json, StringComparison.Ordinal);
    }

    // ── 非法输入：构造与读档 ────────────────────────────────────────

    [Fact]
    public void 构造_表是null_当场抛()
    {
        Assert.Throws<ArgumentNullException>(() => new SpiritLandSystem(null!, StartingVeinId, StartingLandId));
    }

    [Fact]
    public void 构造_等级不在表里_当场抛()
    {
        // 表里没有的 id 是数据错误：不猜着读、也不退回某个默认档——退回默认会让「改表时打错一个字」
        // 表现为「玩家的农场一夜之间灵气稀薄」，谁都想不到是这里（同 CultivationSystem 对灵根 id 的处理）
        InvalidDataException unknownVein =
            Assert.Throws<InvalidDataException>(() => Farm(veinId: "vein_nope"));

        Assert.Contains("vein_nope", unknownVein.Message, StringComparison.Ordinal);
        Assert.Contains("不在灵脉表里", unknownVein.Message, StringComparison.Ordinal);

        InvalidDataException unknownLand =
            Assert.Throws<InvalidDataException>(() => Farm(landId: "land_nope"));

        Assert.Contains("land_nope", unknownLand.Message, StringComparison.Ordinal);
        Assert.Contains("不在福地表里", unknownLand.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 读档_坏档一律是数据错误()
    {
        SpiritLandSystem farm = Farm();

        // 字段不在：id 的合法值都是表里那几条，缺了它只可能是这份数据不是本系统写的
        // （「字段不在」与「字段是空串」分开判——AGENT-BRIEF 那条，两者混为一谈就是猜着读）
        Assert.Throws<InvalidDataException>(() => farm.Deserialize("{}", 1));
        Assert.Throws<InvalidDataException>(() => farm.Deserialize("""{ "VeinId": "vein_micro" }""", 1));
        Assert.Throws<InvalidDataException>(() => farm.Deserialize("""{ "LandId": "land_1" }""", 1));
        Assert.Throws<InvalidDataException>(() => farm.Deserialize("""{ "VeinId": "", "LandId": "land_1" }""", 1));
        Assert.Throws<InvalidDataException>(() => farm.Deserialize("""{ "VeinId": "vein_micro", "LandId": " " }""", 1));

        // 空内容：反序列化得到 null，不是「一份空状态」
        Assert.Throws<InvalidDataException>(() => farm.Deserialize("null", 1));

        // id 认不出：档里的灵脉名与表对不上，同样是数据错误，**不退回默认档**——
        // 退回会让「改表时删掉一档」表现为「老玩家的农场一夜之间灵气稀薄」，谁都想不到是这里。
        // 消息里带 id：坏档要能一眼看出它说的是哪一档
        InvalidDataException unknownVein = Assert.Throws<InvalidDataException>(
            () => farm.Deserialize("""{ "VeinId": "vein_nope", "LandId": "land_1" }""", 1));
        Assert.Contains("vein_nope", unknownVein.Message, StringComparison.Ordinal);
        Assert.Contains("不在灵脉表里", unknownVein.Message, StringComparison.Ordinal);

        InvalidDataException unknownLand = Assert.Throws<InvalidDataException>(
            () => farm.Deserialize("""{ "VeinId": "vein_micro", "LandId": "land_nope" }""", 1));
        Assert.Contains("land_nope", unknownLand.Message, StringComparison.Ordinal);
        Assert.Contains("不在福地表里", unknownLand.Message, StringComparison.Ordinal);

        // 坏档不该把状态改掉一半：上面每一条抛完之后，农场还是原来那个
        Assert.Equal("vein_micro", farm.Vein.Id);
        Assert.Equal("land_1", farm.Land.Id);
    }

    [Fact]
    public void 读档_版本高于当前_当场抛()
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009，同 CultivationSystem）
        Assert.Throws<NotSupportedException>(
            () => Farm().Deserialize("""{ "VeinId": "vein_micro", "LandId": "land_1" }""", 2));
    }

    /// <summary>
    /// 替身表：一级灵脉、一阶福地，浓度由构造参数给。只为「数值在表上」那条行为断言存在。
    /// </summary>
    private sealed class StandInTable : ISpiritLandTable
    {
        private readonly SpiritVeinGrade _vein;
        private readonly BlessedLandGrade _land = new("land_stand_in", 1, "替身福地", "替身");

        public StandInTable(double density) =>
            _vein = new SpiritVeinGrade("vein_stand_in", "替身灵脉", density, "替身");

        public IReadOnlyList<SpiritVeinGrade> Veins => new[] { _vein };

        public IReadOnlyList<BlessedLandGrade> Lands => new[] { _land };

        public bool TryGetVein(string veinId, out SpiritVeinGrade vein)
        {
            vein = _vein;
            return veinId == _vein.Id;
        }

        public bool TryGetLand(string landId, out BlessedLandGrade land)
        {
            land = _land;
            return landId == _land.Id;
        }
    }

    /// <summary>
    /// 替身：只用来写旧格式的档，不参与读档（同 <c>CultivationSaveCompatTests.RawBlob</c>）——
    /// 用真的系统写出来的永远是新格式，那种「兼容」是假兼容。
    /// </summary>
    private sealed class RawBlob : ISaveable
    {
        private readonly int _version;
        private readonly string _json;

        public RawBlob(string key, int version, string json)
        {
            SaveKey = key;
            _version = version;
            _json = json;
        }

        public string SaveKey { get; }

        public int Version => _version;

        public string Serialize() => _json;

        public void Deserialize(string json, int fromVersion) => throw new NotSupportedException(
            "替身只用来写旧格式的档，不参与读档");
    }
}
