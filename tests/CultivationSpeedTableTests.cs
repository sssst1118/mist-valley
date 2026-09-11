using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Core.Time;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 修炼速度表（M3-2）。表里的数分两组，**出处不同，测法也不同**：
/// ① 季节与时辰的倍率是 §8.3 直给的（<c>docs/public/design.md</c> 817-818 行）——逐条照抄对账，
/// 抄错一个字就是玩法变了；② 基础速度与逐层开销是 <c>ARCHITECTURE.md</c> 未定义项备案 #67/#68
/// **推导**出来的（§8.1 的进度锚点 + §3.2 的 28 天 + §3.1 的清晨打坐 + §4.2 的灵根倍率两头夹出），
/// 所以测的是**自洽**：10 × n 的台阶加起来，1.0x 灵根正好在一个春季内走完。
/// </summary>
/// <remarks>
/// 两条最容易被「顺手调平」的东西钉在这里：每层开销是 <c>10 × n</c>（不是 10 一条等差数列的其它写法）、
/// 基础速度是 <c>10</c>/游戏小时。改动它们等于改整个炼气期的节奏——而 §8.1 把炼气期锚在「第 1 年春季」，
/// 那是有意为之的进度设计，不是随手定的数。
/// </remarks>
public class CultivationSpeedTableTests
{
    private static readonly RealmTable Realms = RealmTable.LoadDefault();
    private static readonly CultivationSpeedTable Speed = CultivationSpeedTable.LoadDefault(Realms);

    // ── 备案 #67 / #68：推导出来的那两行 ──────────────────────────────

    [Fact]
    public void 基础速度_每游戏小时十点修为()
    {
        // 备案 #68。1.0x 灵根不含季节加成时就是 10 点/小时——「78 小时走完炼气期」的分子就在这里
        Assert.Equal(10, Speed.BasePointsPerHour);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 30)]
    [InlineData(6, 60)]
    [InlineData(11, 110)]
    [InlineData(12, 120)]
    public void 逐层开销_第n层是十的n倍(int stage, int expected)
    {
        // 备案 #67 的 `10 × n`。取几条采样：改公式（比如 11 × n）会让这几条同时红
        Assert.Equal(expected, Speed.PointsToAdvance(stage));
    }

    [Fact]
    public void 逐层开销_十二步加起来正好七百八十()
    {
        // 13 层要 12 步（一层走到十三层），累计 780 是备案 #67 与 #68 咬合的地方：
        // 780 ÷ 10 = 78 小时，而一个春季按每天打坐 3 小时是 84 小时——正好塞得下
        int total = Enumerable.Range(1, 12).Sum(Speed.PointsToAdvance);

        Assert.Equal(780, total);
        Assert.Equal(78, total / Speed.BasePointsPerHour);
        Assert.True(78 < 28 * 3, "78 小时必须小于「每天 3 小时 × 28 天」= 84 小时，否则 §8.1 的进度锚点对不上");
    }

    [Fact]
    public void 逐层开销_层数跟着境界表走_顶点没有下一层()
    {
        // 13 层是顶点：问它的开销必须当场抛，而不是返回 0——返回 0 会让「十三层还能再升」成立
        Assert.Throws<ArgumentOutOfRangeException>(() => Speed.PointsToAdvance(13));
        Assert.Throws<ArgumentOutOfRangeException>(() => Speed.PointsToAdvance(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Speed.PointsToAdvance(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Speed.PointsToAdvance(14));
    }

    [Fact]
    public void 覆盖范围_只有炼气期录了升层开销()
    {
        // 筑基及以后是 §8.4 的跨大境界突破（要丹药/天材地宝），没有「攒够就升」这回事
        Assert.True(Speed.Covers("qi_refining"));

        foreach (RealmDefinition realm in Realms.Realms.Where(realm => realm.Id != "qi_refining"))
        {
            Assert.False(Speed.Covers(realm.Id));
        }

        // 表里没有的境界一律 false：这不是「查不到就抛」的地方——问的是「有没有录」，答案就是没有
        Assert.False(Speed.Covers("realm_nope"));
    }

    // ── §8.3 直给的两组倍率 ───────────────────────────────────────────

    [Theory]
    [InlineData(Season.Spring, 1.10)]
    [InlineData(Season.Summer, 1.05)]
    [InlineData(Season.Autumn, 1.10)]
    [InlineData(Season.Winter, 0.90)]
    public void 季节倍率_四季逐条对文档(Season season, double expected)
    {
        // §8.3「春季 +10%，夏季 +5%，秋季 +10%，冬季 -10%」。注意冬天是**降**的：
        // 写成 1.10 那种手滑会让冬季变成一年里最快的季节，而它只影响长期节奏，短期看不出来
        Assert.Equal(expected, Speed.SeasonMultiplier(season), precision: 10);
    }

    [Fact]
    public void 时辰带_两段的区间与名字对文档()
    {
        // §8.3「子时（23:00-1:00）+30%，午时（11:00-13:00）+20%」
        Assert.Equal(2, Speed.HourBands.Count);

        Assert.Equal("子时", Speed.HourBands[0].Name);
        Assert.Equal(23, Speed.HourBands[0].FromHour);
        Assert.Equal(1, Speed.HourBands[0].ToHour);    // 跨午夜：23 点与 0 点
        Assert.Equal(1.30, Speed.HourBands[0].Multiplier, precision: 10);

        Assert.Equal("午时", Speed.HourBands[1].Name);
        Assert.Equal(11, Speed.HourBands[1].FromHour);
        Assert.Equal(13, Speed.HourBands[1].ToHour);
        Assert.Equal(1.20, Speed.HourBands[1].Multiplier, precision: 10);
    }

    [Theory]
    [InlineData(23, 1.30)]   // 子时从 23 点整开始
    [InlineData(0, 1.30)]    // ……到 0 点（跨过午夜）
    [InlineData(1, 1.00)]    // 1 点整已不在子时里（§8.3 写的是 23:00-1:00）
    [InlineData(22, 1.00)]
    [InlineData(11, 1.20)]   // 午时从 11 点整开始
    [InlineData(12, 1.20)]
    [InlineData(13, 1.00)]   // 13 点整已不在午时里
    [InlineData(10, 1.00)]
    [InlineData(6, 1.00)]    // §3.1 的清晨打坐落在这一段：没有时辰加成
    [InlineData(9, 1.00)]
    public void 时辰倍率_边界钟点一个不漏(int hour, double expected)
    {
        // 带是「闭开」的：起点算在内、终点不算。差一位的后果是「11:00 打坐白坐了」这种
        // 只在整点才显形的怪事
        Assert.Equal(expected, Speed.HourMultiplier(hour), precision: 10);
    }

    [Fact]
    public void 时辰倍率_钟点越界当场抛()
    {
        // 24 点与 -1 点不是「没有加成」，是不存在的钟点：静默返回 1.0 会把调用方算错的时刻藏起来
        Assert.Throws<ArgumentOutOfRangeException>(() => Speed.HourMultiplier(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Speed.HourMultiplier(24));
    }

    // ── 缺省数据文件本身 ──────────────────────────────────────────────

    [Fact]
    public void 缺省数据_确实是从工程里那份文件读出来的()
    {
        // LoadDefault 靠「从输出目录逐级上溯」找文件（技术债，M8 改注入）。找不到会抛，
        // 所以这条顺带守住「数据文件没被误删/改名」
        Assert.Equal(10, Speed.BasePointsPerHour);
        Assert.Equal(2, Speed.HourBands.Count);
    }

    [Fact]
    public void 数据文件里没有那七个因素的字段_刻意的()
    {
        // §8.3 的表里还有灵脉等级 / 聚灵阵 / 风水 / 功法品阶 / 丹药 / 心境 / 双修七行，
        // 各自的系统都还不存在。**先建字段就是建一批没人读的数**，而它们与真数据长得一模一样，
        // 将来没人分得清「文档写了」与「我们编的」——所以连 TODO 常量都不留（铁律 11）。
        // 反射那条钉子（M3Audit_Cultivation）管的是类型，这条管的是**数据文件**：
        // 加载器不认识的多余键会被静静忽略，只盯代码是看不出来的
        using var document = JsonDocument.Parse(File.ReadAllText(FindDefaultFile()));

        string[] actual = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "_comment", "basePointsPerHour", "costRealmId", "hourBands", "seasonMultipliers", "stageCosts",
            },
            actual);
    }

    // ── 坏数据：加载即抛 ──────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BadTables))]
    public void 坏修炼速度表_加载即抛且指出坏在哪(string expectedInMessage, string json)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => CultivationSpeedTable.FromJson(json, Realms));

        Assert.Contains(expectedInMessage, exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> BadTables()
    {
        return new TheoryData<string, string>
        {
            { "不是对象", "[1, 2]" },
            { "缺少数字字段 basePointsPerHour", Table(Realm, Costs, Seasons, Bands) },
            { "不是正数", Table("\"basePointsPerHour\": 0", Realm, Costs, Seasons, Bands) },
            { "不是正数", Table("\"basePointsPerHour\": -10", Realm, Costs, Seasons, Bands) },
            { "缺少文本字段 costRealmId", Table(Base, Costs, Seasons, Bands) },
            { "不在境界表里", Table(Base, "\"costRealmId\": \"realm_nope\"", Costs, Seasons, Bands) },
            { "缺少 stageCosts 数组", Table(Base, Realm, Seasons, Bands) },
            { "不是数字的条目", Table(Base, Realm, "\"stageCosts\": [10, \"20\"]", Seasons, Bands) },
            { "非正的开销", Table(Base, Realm, "\"stageCosts\": [10, 0]", Seasons, Bands) },
            { "非正的开销", Table(Base, Realm, "\"stageCosts\": [10, -20]", Seasons, Bands) },
            // 条数与境界表对不上：少一条（12 层升不上去）与多一条（第 14 层不存在）都拦
            { "应有 12 条", Table(Base, Realm, "\"stageCosts\": [10, 20, 30]", Seasons, Bands) },
            { "应有 12 条", Table(Base, Realm, $"\"stageCosts\": [{string.Join(", ", Enumerable.Repeat(10, 13))}]", Seasons, Bands) },
            { "缺少 seasonMultipliers 数组", Table(Base, Realm, Costs, Bands) },
            { "不是对象的条目", Table(Base, Realm, Costs, "\"seasonMultipliers\": [ 1 ]", Bands) },
            { "缺少数字字段 multiplier", Table(Base, Realm, Costs, "\"seasonMultipliers\": [ { \"season\": \"Spring\" } ]", Bands) },
            { "认不出的季节", Table(Base, Realm, Costs, "\"seasonMultipliers\": [ { \"season\": \"spring\", \"multiplier\": 1.1 } ]", Bands) },
            // 按序号写季节：枚举加一个季节之后，旧数据会静默指向另一个季节
            { "认不出的季节", Table(Base, Realm, Costs, "\"seasonMultipliers\": [ { \"season\": \"0\", \"multiplier\": 1.1 } ]", Bands) },
            { "出现了两次", Table(Base, Realm, Costs, "\"seasonMultipliers\": [ { \"season\": \"Spring\", \"multiplier\": 1.1 }, { \"season\": \"Spring\", \"multiplier\": 1.2 } ]", Bands) },
            { "缺了季节", Table(Base, Realm, Costs, "\"seasonMultipliers\": [ { \"season\": \"Spring\", \"multiplier\": 1.1 } ]", Bands) },
            { "不是正数", Table(Base, Realm, Costs, "\"seasonMultipliers\": [ { \"season\": \"Spring\", \"multiplier\": 0 } ]", Bands) },
            { "缺少文本字段 season", Table(Base, Realm, Costs, "\"seasonMultipliers\": [ { \"multiplier\": 1.1 } ]", Bands) },
            { "缺少 hourBands 数组", Table(Base, Realm, Costs, Seasons) },
            { "不是对象的条目", Table(Base, Realm, Costs, Seasons, "\"hourBands\": [ 1 ]") },
            { "缺少文本字段 name", Table(Base, Realm, Costs, Seasons, "\"hourBands\": [ { \"fromHour\": 23, \"toHour\": 1, \"multiplier\": 1.3 } ]") },
            { "缺少数字字段 toHour", Table(Base, Realm, Costs, Seasons, "\"hourBands\": [ { \"name\": \"子时\", \"fromHour\": 23, \"multiplier\": 1.3 } ]") },
            { "不是 0..23 的钟点", Table(Base, Realm, Costs, Seasons, "\"hourBands\": [ { \"name\": \"子时\", \"fromHour\": -1, \"toHour\": 1, \"multiplier\": 1.3 } ]") },
            { "不是 0..23 的钟点", Table(Base, Realm, Costs, Seasons, "\"hourBands\": [ { \"name\": \"子时\", \"fromHour\": 24, \"toHour\": 1, \"multiplier\": 1.3 } ]") },
            { "不是 0..24 的钟点", Table(Base, Realm, Costs, Seasons, "\"hourBands\": [ { \"name\": \"子时\", \"fromHour\": 23, \"toHour\": 25, \"multiplier\": 1.3 } ]") },
            { "空带子", Table(Base, Realm, Costs, Seasons, "\"hourBands\": [ { \"name\": \"子时\", \"fromHour\": 23, \"toHour\": 23, \"multiplier\": 1.3 } ]") },
            { "不是正数", Table(Base, Realm, Costs, Seasons, "\"hourBands\": [ { \"name\": \"子时\", \"fromHour\": 23, \"toHour\": 1, \"multiplier\": 0 } ]") },
            // 重叠：同一时刻两份倍率，谁生效取决于文件顺序——「有时快有时慢」的幽灵 bug
            { "重叠", Table(Base, Realm, Costs, Seasons, "\"hourBands\": [ { \"name\": \"子时\", \"fromHour\": 23, \"toHour\": 1, \"multiplier\": 1.3 }, { \"name\": \"深夜\", \"fromHour\": 22, \"toHour\": 24, \"multiplier\": 1.5 } ]") },
        };
    }

    [Fact]
    public void 时辰带可以是空的_那时处处都是平峰()
    {
        // 「这一天没有特殊时辰」是自洽的状态（文档给了两段，但删掉它们并不矛盾），
        // 与「缺了整个 hourBands 数组」（多半是写坏了）是两件事
        CultivationSpeedTable plain = CultivationSpeedTable.FromJson(
            Table(Base, Realm, Costs, Seasons, "\"hourBands\": []"), Realms);

        Assert.Empty(plain.HourBands);
        Assert.Equal(1.0, plain.HourMultiplier(23), precision: 10);
        Assert.Equal(1.0, plain.HourMultiplier(12), precision: 10);
    }

    // ── 拼表的零件：每条坏数据只坏在一处 ──────────────────────────────

    private const string Base = "\"basePointsPerHour\": 10";
    private const string Realm = "\"costRealmId\": \"qi_refining\"";
    private const string Costs = "\"stageCosts\": [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120]";
    private const string Seasons =
        "\"seasonMultipliers\": ["
        + " { \"season\": \"Spring\", \"multiplier\": 1.10 },"
        + " { \"season\": \"Summer\", \"multiplier\": 1.05 },"
        + " { \"season\": \"Autumn\", \"multiplier\": 1.10 },"
        + " { \"season\": \"Winter\", \"multiplier\": 0.90 } ]";
    private const string Bands =
        "\"hourBands\": ["
        + " { \"name\": \"子时\", \"fromHour\": 23, \"toHour\": 1, \"multiplier\": 1.30 },"
        + " { \"name\": \"午时\", \"fromHour\": 11, \"toHour\": 13, \"multiplier\": 1.20 } ]";

    private static string Table(params string[] fields) => "{ " + string.Join(", ", fields) + " }";

    /// <summary>与 <c>BridgeContractTests.FindRepoRoot</c> 同款的上溯，只为读那份原始 JSON 的键。</summary>
    private static string FindDefaultFile()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, CultivationSpeedTable.DefaultRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException($"未找到 {CultivationSpeedTable.DefaultRelativePath}，本用例会变成假绿灯");
    }
}
