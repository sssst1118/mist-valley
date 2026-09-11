using System;
using System.IO;
using System.Linq;
using XingGame.Systems.Crafting;

namespace XingGame.Tests;

/// <summary>
/// 炼丹师等级表（M3-7，§8.7）。这里守两件事：① 坏数据必须在加载时炸；②
/// <c>data/crafting/alchemy_ranks.json</c> 与 §8.7 第 960-970 行那张表<b>逐行逐字</b>一致——
/// 品级名抄错一个字、可炼阶数差一阶、凭空多出一品，都要有人发现。
/// </summary>
/// <remarks>
/// 这张表是「几品能炼几阶」的<b>唯一住处</b>（配方只说自己是几阶丹药），所以它抄错的后果不是显示不好看，
/// 而是整条门槛平移一档：`AlchemyCraftingTests` 里那条「差一品就炼不成」会跟着一起错。
/// </remarks>
public class AlchemyRankTableTests
{
    /// <summary>把条目拼成一份表。坏数据用例只改一处，不必每次抄一整份 JSON。</summary>
    private static string JsonWith(params string[] entries) => "{ \"ranks\": [" + string.Join(",", entries) + "] }";

    private static string Rank(string rank = "1", string name = "一品炼丹学徒", string maxTier = "1") =>
        $"{{ \"rank\": {rank}, \"name\": \"{name}\", \"maxTier\": {maxTier} }}";

    private static readonly AlchemyRankTable Table = AlchemyRankTable.FromJson(
        JsonWith(Rank("1", "一品炼丹学徒", "1"), Rank("2", "二品炼丹师", "2"), Rank("3", "三品炼丹大师", "3")));

    [Fact]
    public void 缺省数据文件_九品与_8_7_逐条一致()
    {
        // 出处：docs/public/design.md 第 960-970 行。品级名与「可炼制丹药品阶」照抄，一个字都没编——
        // 包括文档自己那处重复（六至九品都叫「炼丹宗师」），那是文档的写法，不替它改
        var expected = new (int Rank, string Name, int MaxTier)[]
        {
            (1, "一品炼丹学徒",   1),
            (2, "二品炼丹师",     2),
            (3, "三品炼丹大师",   3),
            (4, "四品炼丹宗师",   4),
            (5, "五品炼丹大宗师", 5),
            (6, "六品炼丹宗师",   6),
            (7, "七品炼丹宗师",   7),
            (8, "八品炼丹宗师",   8),
            (9, "九品炼丹宗师",   9),
        };

        AlchemyRankTable table = AlchemyRankTable.LoadDefault();

        Assert.Equal(expected.Length, table.All.Count);   // 多一品、少一品都在这里红
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Rank, table.All[index].Rank);
            Assert.Equal(expected[index].Name, table.All[index].Name);
            Assert.Equal(expected[index].MaxTier, table.All[index].MaxTier);
        }
    }

    [Fact]
    public void 正常加载_按品级升序_一品在前()
    {
        // 「最低一档」是 RequiredRankForTier 与「新档起点」都要用的序，乱序会让两处都取错
        Assert.Equal(new[] { 1, 2, 3 }, Table.All.Select(rank => rank.Rank).ToArray());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void 几品能炼几阶_按表回答(int tier, int expectedRank)
    {
        Assert.Equal(expectedRank, Table.RequiredRankForTier(tier));
    }

    [Fact]
    public void 缺省数据文件_十阶丹药没有哪一品炼得出来()
    {
        // §8.7 的表到九阶为止，十阶（神丹）起文档写着「仅仙界存在」「凡界无法炼制」——
        // 所以问十阶必须抛，而不是回一个「九品也能炼」的假答案
        AlchemyRankTable table = AlchemyRankTable.LoadDefault();

        Assert.Equal(9, table.RequiredRankForTier(9));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.RequiredRankForTier(10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 几品能炼几阶_阶数非正时抛(int tier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Table.RequiredRankForTier(tier));
    }

    [Fact]
    public void TryGet_认得出的品级给出行_认不出的返回_false()
    {
        // 存档那条路要用它分岔（认不出 = 数据错误），不能靠 Get 抛异常来兜
        Assert.True(Table.TryGet(2, out AlchemyRankDefinition definition));
        Assert.Equal("二品炼丹师", definition.Name);

        Assert.False(Table.TryGet(0, out _));
        Assert.False(Table.TryGet(10, out _));
    }

    [Theory]
    [InlineData("1.5", "一品炼丹学徒", "1")]   // 品级是小数
    [InlineData("1", "一品炼丹学徒", "1.5")]   // 阶数是小数
    [InlineData("\"一\"", "一品炼丹学徒", "1")]  // 品级写成中文数字
    public void 加载_数字字段不是整数时报错(string rank, string name, string maxTier)
    {
        Assert.Throws<InvalidDataException>(() => AlchemyRankTable.FromJson(JsonWith(Rank(rank, name, maxTier))));
    }

    [Theory]
    [InlineData("0")]     // 品级从 1 起（§8.7 最低那一档就是「一品炼丹学徒」）
    [InlineData("-1")]
    [InlineData("2")]     // 不从 1 起：一阶丹药于是没人炼得出来
    public void 加载_品级不从_1_起时报错(string rank)
    {
        Assert.Throws<InvalidDataException>(
            () => AlchemyRankTable.FromJson(JsonWith(Rank(rank, "一品炼丹学徒", "1"))));
    }

    [Fact]
    public void 加载_品级断档时报错()
    {
        // 1、3：缺的那一品让「升一品」没有落点，§9.3 的 5 级/10 级分支也会对不上表
        var error = Assert.Throws<InvalidDataException>(
            () => AlchemyRankTable.FromJson(
                JsonWith(Rank("1", "一品炼丹学徒", "1"), Rank("3", "三品炼丹大师", "3"))));

        Assert.Contains("连续", error.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void 加载_可炼制阶数非正时报错(string maxTier)
    {
        // 能炼 0 阶等于这一品什么也炼不出来
        Assert.Throws<InvalidDataException>(
            () => AlchemyRankTable.FromJson(JsonWith(Rank("1", "一品炼丹学徒", maxTier))));
    }

    [Fact]
    public void 加载_可炼制阶数倒挂时报错()
    {
        // 一品能炼二阶、二品只能炼一阶：品级越高越不能炼，而它只在升级那一刻显形
        var error = Assert.Throws<InvalidDataException>(
            () => AlchemyRankTable.FromJson(
                JsonWith(Rank("1", "一品炼丹学徒", "2"), Rank("2", "二品炼丹师", "1"))));

        Assert.Contains("二品炼丹师", error.Message);
    }

    [Fact]
    public void 加载_两品能炼同一阶不算错()
    {
        // 拦的是「越高反而越少」，不是「没变多」。文档的表逐行都在抬高上限，但同阶不代表表不成立
        AlchemyRankTable table = AlchemyRankTable.FromJson(
            JsonWith(Rank("1", "一品炼丹学徒", "1"), Rank("2", "二品炼丹师", "1")));

        Assert.Equal(1, table.RequiredRankForTier(1));   // 取最低的那一品
    }

    [Fact]
    public void 加载_重复品级时报错()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => AlchemyRankTable.FromJson(
                JsonWith(Rank("1", "一品炼丹学徒", "1"), Rank("1", "一品炼丹学徒", "1"))));

        Assert.Contains("1 品", error.Message);
    }

    [Theory]
    [InlineData("""{ "rank": 1, "maxTier": 1 }""")]                       // 缺 name
    [InlineData("""{ "rank": 1, "name": "一品炼丹学徒" }""")]              // 缺 maxTier
    [InlineData("""{ "name": "一品炼丹学徒", "maxTier": 1 }""")]           // 缺 rank
    [InlineData("""{ "rank": 1, "name": "  ", "maxTier": 1 }""")]         // 名字只有空白
    public void 加载_缺字段时报错(string entry)
    {
        Assert.Throws<InvalidDataException>(() => AlchemyRankTable.FromJson(JsonWith(entry)));
    }

    [Fact]
    public void 加载_空表或没有_ranks_数组时报错()
    {
        Assert.Throws<InvalidDataException>(() => AlchemyRankTable.FromJson(JsonWith()));
        Assert.Throws<InvalidDataException>(() => AlchemyRankTable.FromJson("""{ "品级": [] }"""));
        Assert.Throws<InvalidDataException>(() => AlchemyRankTable.FromJson("""[ { "rank": 1 } ]"""));
    }

    [Fact]
    public void 加载_条目不是对象时报错()
    {
        Assert.Throws<InvalidDataException>(() => AlchemyRankTable.FromJson(JsonWith("\"一品炼丹学徒\"")));
    }

    /// <summary>
    /// <b>刻意的否定式决定：炼器那一侧的等级表今天不存在</b>（§8.7 的炼器师等级表 + 法宝等级五档）。
    /// 它九行答的全是「哪一品能炼哪一级法宝」，而今天一件法宝物品都没有、装备系统也还没有——
    /// 建出来的每一行都没有代码会读（铁律 11）。等法宝落地时连同装备系统一起加，那时删掉这条用例。
    /// </summary>
    [Fact]
    public void 刻意不做_今天没有炼器师等级表也没有法宝等级表()
    {
        Type[] types = typeof(AlchemyRankTable).Assembly.GetTypes();

        Assert.DoesNotContain(types, type => type.Name.Contains("ArtificerRank"));
        Assert.DoesNotContain(types, type => type.Name.Contains("ArtifactGrade"));
        Assert.DoesNotContain(types, type => type.Name.Contains("ArtifactTier"));
    }
}
