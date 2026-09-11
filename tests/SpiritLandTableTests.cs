using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 灵脉与福地两张表（M3-5）：六级灵脉的浓度与九阶福地的名称/说明逐条对 §8.8，坏表在加载时就被拒。
/// </summary>
/// <remarks>
/// <para>
/// 这组数与打坐那张账**出处不同**：<c>cultivation_speed.json</c> 里的基础速度与逐层开销是
/// ARCHITECTURE 备案 #67/#68 推导出来的，而这里每一个数都是 §8.8 **直给**的
/// （<c>docs/public/design.md</c> 1005-1031 行）。所以「对账」对的就是文档原文，抄错一个字、少一阶、
/// 顺序摆错都要在这里现形——两者混着看，将来没人分得清哪个数是文档的、哪个是我们编的。
/// </para>
/// <para>
/// <b>灵脉那一侧只有五级能在 §8.3 里找到</b>（811 行那张「其他影响因素」表到巨型 +200% 为止），
/// 龙脉只写在 §8.8 上。本表的用例以 §8.8 为准取六级——两处冲突时 §8.8 是那张表的本体。
/// </para>
/// </remarks>
public class SpiritLandTableTests
{
    private static readonly SpiritLandTable Table = SpiritLandTable.LoadDefault();

    /// <summary>§8.8 灵脉表的六行，逐字：文档给的是加成的百分数（+10%），表里落成乘数（1.10）。</summary>
    public static TheoryData<int, string, string, double, string> VeinRows()
    {
        return new TheoryData<int, string, string, double, string>
        {
            { 0, "vein_micro",  "微型灵脉", 1.10, "农场初始状态，灵气稀薄" },
            { 1, "vein_small",  "小型灵脉", 1.25, "10 条微型灵脉合一，可支撑小型宗门" },
            { 2, "vein_medium", "中型灵脉", 1.50, "10 条小型灵脉合一，可支撑中型宗门" },
            { 3, "vein_large",  "大型灵脉", 2.00, "10 条中型灵脉合一，可支撑大型宗门" },
            { 4, "vein_giant",  "巨型灵脉", 3.00, "10 条大型灵脉合一，可支撑顶级宗门" },
            { 5, "vein_dragon", "龙脉",     6.00, "传说中的灵脉，可化龙升天" },
        };
    }

    /// <summary>§8.8 福地表的九行，逐字（阶号 + 名称 + 说明）。</summary>
    public static TheoryData<int, string, string, string> LandRows()
    {
        return new TheoryData<int, string, string, string>
        {
            { 1, "land_1", "福地",     "拥有独立灵气循环，可种植灵植" },
            { 2, "land_2", "小福地",   "灵气浓度提升，可养殖灵兽" },
            { 3, "land_3", "中福地",   "可建造修炼室、炼丹房" },
            { 4, "land_4", "大福地",   "可容纳弟子修炼" },
            { 5, "land_5", "顶级福地", "可孕育灵脉" },
            { 6, "land_6", "洞天",     "独立小世界，不受外界季节天气影响" },
            { 7, "land_7", "小洞天",   "可演化生态，诞生灵物" },
            { 8, "land_8", "大洞天",   "可容纳宗门，自成一界" },
            { 9, "land_9", "洞天福地", "传说中的境界，可化大千世界" },
        };
    }

    // ── 缺省表：逐条对 §8.8 ─────────────────────────────────────────

    [Fact]
    public void 灵脉_六级_一个不多一个不少()
    {
        Assert.Equal(6, Table.Veins.Count);
    }

    [Theory]
    [MemberData(nameof(VeinRows))]
    public void 灵脉_逐条对_8_8(int index, string id, string name, double multiplier, string description)
    {
        SpiritVeinGrade vein = Table.Veins[index];

        Assert.Equal(id, vein.Id);
        Assert.Equal(name, vein.Name);
        Assert.Equal(multiplier, vein.ConcentrationMultiplier, precision: 10);
        Assert.Equal(description, vein.Description);
    }

    [Fact]
    public void 灵脉_浓度逐级递增_等级越高灵气越浓()
    {
        // §8.8 那张表按强弱排（「10 条微型灵脉合一」），「等级」这个词本身就在说越高越浓。
        // 倒挂的表会让「把灵脉升一级」变成负收益——而它只在升级那一刻显形。
        // 加载时由 ParseVeins 拦下坏表，这里对的是缺省数据文件本身
        for (int index = 1; index < Table.Veins.Count; index++)
        {
            Assert.True(
                Table.Veins[index].ConcentrationMultiplier > Table.Veins[index - 1].ConcentrationMultiplier,
                $"「{Table.Veins[index].Name}」没比「{Table.Veins[index - 1].Name}」浓");
        }

        // 龙脉那一档是 §8.8 独有的（§8.3 的列表到巨型 +200% 为止），单拎出来钉一下：
        // 它是表里的顶点，也是「+500%」这个数唯一的落点
        Assert.Equal("龙脉", Table.Veins[^1].Name);
        Assert.Equal(6.00, Table.Veins[^1].ConcentrationMultiplier, precision: 10);
    }

    [Fact]
    public void 福地_九阶_一个不多一个不少()
    {
        Assert.Equal(9, Table.Lands.Count);
    }

    [Theory]
    [MemberData(nameof(LandRows))]
    public void 福地_逐条对_8_8(int order, string id, string name, string description)
    {
        BlessedLandGrade land = Table.Lands[order - 1];

        Assert.Equal(id, land.Id);
        Assert.Equal(order, land.Order);
        Assert.Equal(name, land.Name);
        Assert.Equal(description, land.Description);
    }

    [Fact]
    public void 福地_阶号从一连续到九_没有断档()
    {
        // 「升一阶」的刻度就是阶号：缺一个（比如没有五阶）等于那条路在没人看得见的地方断了
        for (int index = 0; index < Table.Lands.Count; index++)
        {
            Assert.Equal(index + 1, Table.Lands[index].Order);
        }
    }

    [Fact]
    public void 缺省数据_确实是从工程里那份文件读出来的()
    {
        // LoadDefault 靠「从输出目录逐级上溯」找文件（技术债，M8 改注入）。找不到会抛，
        // 所以这条顺带守住「数据文件没被误删/改名」
        Assert.Equal(6, Table.Veins.Count);
        Assert.Equal(9, Table.Lands.Count);
    }

    // ── 查表 ────────────────────────────────────────────────────────

    [Fact]
    public void 查表_认得的一找就到_认不出的一律返回假()
    {
        // 不抛而是返回 false：认不出的 id 要由调用方分岔（构造参数写错是编程错误、
        // 存档对不上是数据错误，两种异常的语义不同）——见 ISpiritLandTable.TryGetVein
        Assert.True(Table.TryGetVein("vein_dragon", out SpiritVeinGrade dragon));
        Assert.Equal("龙脉", dragon.Name);

        Assert.True(Table.TryGetLand("land_9", out BlessedLandGrade top));
        Assert.Equal(9, top.Order);

        Assert.False(Table.TryGetVein("vein_nope", out _));
        Assert.False(Table.TryGetLand("land_nope", out _));

        // null 也不炸：id 可能来自存档里被改成 null 的字段，那时该走「数据错误」那条路，
        // 而不是在字典里抛一个消息里没有上下文的 ArgumentNullException
        Assert.False(Table.TryGetVein(null!, out _));
        Assert.False(Table.TryGetLand(null!, out _));
    }

    // ── 坏表：加载即抛 ──────────────────────────────────────────────

    [Theory]
    [InlineData("[]")]     // 不是对象
    [InlineData("null")]
    [InlineData("""{ "lands": [] }""")]                                  // 缺 veins
    [InlineData("""{ "veins": [] }""")]                                  // 缺 lands
    [InlineData("""{ "veins": {}, "lands": [] }""")]                      // 不是数组
    [InlineData("""{ "veins": [], "lands": [] }""")]                      // 两张表都空：答不出任何问题
    public void 坏表_结构不对_当场抛(string json)
    {
        Assert.Throws<InvalidDataException>(() => SpiritLandTable.FromJson(json));
    }

    [Theory]
    // 缺字段：报错消息里要带上是谁（id）才定位得到那一行
    [InlineData("""{ "name": "微型灵脉", "concentrationMultiplier": 1.1, "description": "x" }""", "缺少文本字段 id")]
    [InlineData("""{ "id": "vein_a", "concentrationMultiplier": 1.1, "description": "x" }""", "vein_a 缺少文本字段 name")]
    [InlineData("""{ "id": "vein_a", "name": "甲", "concentrationMultiplier": 1.1 }""", "vein_a 缺少文本字段 description")]
    [InlineData("""{ "id": "vein_a", "name": "甲", "description": "x" }""", "vein_a 缺少数字字段 concentrationMultiplier")]
    [InlineData("""{ "id": "vein_a", "name": "甲", "concentrationMultiplier": "1.1", "description": "x" }""", "缺少数字字段")]
    [InlineData("""{ "id": "", "name": "甲", "concentrationMultiplier": 1.1, "description": "x" }""", "缺少文本字段 id")]
    // 非正的浓度：0 让这一级灵气归零（打坐永远练不出东西），负数倒着扣
    [InlineData("""{ "id": "vein_a", "name": "甲", "concentrationMultiplier": 0, "description": "x" }""", "不是正数")]
    [InlineData("""{ "id": "vein_a", "name": "甲", "concentrationMultiplier": -1.1, "description": "x" }""", "不是正数")]
    // 重复 id：谁生效取决于文件顺序，而两份还可能一个 +10% 一个 +500%
    [InlineData(
        """
        { "id": "vein_a", "name": "甲", "concentrationMultiplier": 1.1, "description": "x" },
        { "id": "vein_a", "name": "乙", "concentrationMultiplier": 1.2, "description": "y" }
        """,
        "重复 id")]
    // 浓度倒挂与持平：等级这个词本身就在说越高越浓
    [InlineData(
        """
        { "id": "vein_a", "name": "甲", "concentrationMultiplier": 1.5, "description": "x" },
        { "id": "vein_b", "name": "乙", "concentrationMultiplier": 1.25, "description": "y" }
        """,
        "负收益")]
    [InlineData(
        """
        { "id": "vein_a", "name": "甲", "concentrationMultiplier": 1.25, "description": "x" },
        { "id": "vein_b", "name": "乙", "concentrationMultiplier": 1.25, "description": "y" }
        """,
        "负收益")]
    // 条目不是对象
    [InlineData("""{ "id": "vein_a", "name": "甲", "concentrationMultiplier": 1.1, "description": "x" }, "乙" """, "不是对象")]
    public void 坏灵脉表_加载即抛且指出坏在哪(string veins, string expectedInMessage)
    {
        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(() => SpiritLandTable.FromJson(Json(veins: veins)));

        Assert.Contains(expectedInMessage, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "order": 1, "name": "福地", "description": "x" }""", "缺少文本字段 id")]
    [InlineData("""{ "id": "land_a", "order": 1, "description": "x" }""", "land_a 缺少文本字段 name")]
    [InlineData("""{ "id": "land_a", "order": 1, "name": "福地" }""", "land_a 缺少文本字段 description")]
    [InlineData("""{ "id": "land_a", "name": "福地", "description": "x" }""", "land_a 缺少数字字段 order")]
    [InlineData("""{ "id": "land_a", "order": "1", "name": "福地", "description": "x" }""", "缺少数字字段")]
    // 阶号不是从 1 起（0 与负数）：一阶是这个体系的起点，§8.8 的农场初始状态就落在它上面
    [InlineData("""{ "id": "land_a", "order": 0, "name": "福地", "description": "x" }""", "不是从 1 起")]
    [InlineData("""{ "id": "land_a", "order": -1, "name": "福地", "description": "x" }""", "不是从 1 起")]
    // 同一阶两条：显示「他现在几阶」与升级「下一阶是谁」会各读到一条
    [InlineData(
        """
        { "id": "land_a", "order": 1, "name": "福地", "description": "x" },
        { "id": "land_b", "order": 1, "name": "小福地", "description": "y" }
        """,
        "出现了两次")]
    // 重复 id
    [InlineData(
        """
        { "id": "land_a", "order": 1, "name": "福地", "description": "x" },
        { "id": "land_a", "order": 2, "name": "小福地", "description": "y" }
        """,
        "重复 id")]
    // 断档（1 之后直接 3）与不从 1 起（2、3）：两者都让「升一阶」没有落点
    [InlineData(
        """
        { "id": "land_a", "order": 1, "name": "福地", "description": "x" },
        { "id": "land_c", "order": 3, "name": "中福地", "description": "y" }
        """,
        "从 1 起连续")]
    [InlineData(
        """
        { "id": "land_b", "order": 2, "name": "小福地", "description": "x" },
        { "id": "land_c", "order": 3, "name": "中福地", "description": "y" }
        """,
        "从 1 起连续")]
    [InlineData("""{ "id": "land_a", "order": 1, "name": "福地", "description": "x" }, "福地" """, "不是对象")]
    public void 坏福地表_加载即抛且指出坏在哪(string lands, string expectedInMessage)
    {
        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(() => SpiritLandTable.FromJson(Json(lands: lands)));

        Assert.Contains(expectedInMessage, exception.Message, StringComparison.Ordinal);
    }

    // ── 数据文件本身 ────────────────────────────────────────────────

    [Fact]
    public void 数据文件_只有灵脉与福地两组_没有别的因素()
    {
        // 加载器不认识的多余键会被**静静忽略**，所以只盯代码看不出数据文件里多了什么。
        // 这里读原始 JSON 的键集合：§8.3 表里的另外六个因素（聚灵阵 / 风水 / 功法 / 丹药 / 心境 /
        // 双修）都不该在这个文件里长出字段来——它们各自的系统都还不存在（铁律 11），
        // 而两处各存一份「加成」也是这个仓库专门拦过的事。灵脉那一行则只许有这四个键：
        // 一旦多出一个（比如把「灵植生长加成」也塞进来），本条立刻红
        using var document = JsonDocument.Parse(File.ReadAllText(FindDefaultFile()));
        JsonElement root = document.RootElement;

        Assert.Equal(
            new[] { "_comment", "lands", "veins" },
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        foreach (JsonElement vein in root.GetProperty("veins").EnumerateArray())
        {
            Assert.Equal(
                new[] { "concentrationMultiplier", "description", "id", "name" },
                vein.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }

        foreach (JsonElement land in root.GetProperty("lands").EnumerateArray())
        {
            Assert.Equal(
                new[] { "description", "id", "name", "order" },
                land.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }
    }

    /// <summary>
    /// 一份合法的表，按参数改坏一处——**单点故障**才算得清是哪条校验拦下的。
    /// </summary>
    private static string Json(string? veins = null, string? lands = null) =>
        $$"""
        {
          "veins": [ {{veins ?? GoodVeins}} ],
          "lands": [ {{lands ?? GoodLands}} ]
        }
        """;

    private const string GoodVeins = """
        { "id": "vein_micro",  "name": "微型灵脉", "concentrationMultiplier": 1.10, "description": "农场初始状态，灵气稀薄" },
        { "id": "vein_small",  "name": "小型灵脉", "concentrationMultiplier": 1.25, "description": "10 条微型灵脉合一，可支撑小型宗门" }
        """;

    private const string GoodLands = """
        { "id": "land_1", "order": 1, "name": "福地",   "description": "拥有独立灵气循环，可种植灵植" },
        { "id": "land_2", "order": 2, "name": "小福地", "description": "灵气浓度提升，可养殖灵兽" }
        """;

    /// <summary>与 <c>SpiritPowerTableTests.FindDefaultFile</c> 同款的上溯，只为读那份原始 JSON 的键。</summary>
    private static string FindDefaultFile()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, SpiritLandTable.DefaultRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException($"未找到 {SpiritLandTable.DefaultRelativePath}，本用例会变成假绿灯");
    }
}
