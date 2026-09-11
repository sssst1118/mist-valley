using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Core.Time;
using XingGame.Systems.Fishing;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 鱼表（M2-A 钓鱼）。守三件事：① 坏数据必须在加载时炸；② 与物品表交叉校验（钓上来的鱼要进背包，
/// 鱼表里写了一个物品表没有的 id，玩家抛竿之后才会发现）；③ <c>data/fishing/fish.json</c> 与
/// 设计文档逐条一致——§7.3 给了什么就录什么，没给名字的一条都不许有。
/// </summary>
/// <remarks>
/// 第三条尤其要守：§7.3 说「每季 20+ 种」却没给鱼名，最容易出的错不是抄错，而是<b>凑数</b>——
/// 为了让表看着像样而编出几十条鱼。所以缺省表的用例是「正好这几条，多一条都不行」。
/// </remarks>
public class FishTableTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "fish_ghost",     "name": "幽灵鱼", "description": "雾天出没的特殊鱼。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "fish_jellyfish", "name": "水母",   "description": "夏夜出没的特殊鱼。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "fish_lobster",   "name": "龙虾",   "description": "蟹笼产出。",         "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string ValidJson = """
    {
      "fish": [
        { "id": "fish_ghost",     "itemId": "fish_ghost",     "method": "Rod", "weather": ["Foggy"], "weight": 2 },
        { "id": "fish_jellyfish", "itemId": "fish_jellyfish", "method": "Rod", "seasons": ["Summer"], "phases": ["LateNight"] },
        { "id": "fish_lobster",   "itemId": "fish_lobster",   "method": "CrabPot" }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);

    private static readonly FishTable Table = FishTable.FromJson(ValidJson, Items);

    /// <summary>把条目拼成一份表。坏数据用例只改一个字段，不必每次抄一整份 JSON。</summary>
    private static string JsonWith(params string[] entries) => "{ \"fish\": [" + string.Join(",", entries) + "] }";

    /// <param name="extra">追加的字段，如 <c>, "seasons": ["Summer"]</c>；没有就留空。</param>
    private static string Fish(
        string id = "fish_ghost",
        string itemId = "fish_ghost",
        string method = "\"Rod\"",
        string extra = "") =>
        $"{{ \"id\": \"{id}\", \"itemId\": \"{itemId}\", \"method\": {method}{extra} }}";

    [Fact]
    public void 正常加载_All_的数量与顺序与文件一致()
    {
        Assert.Equal(3, Table.All.Count);
        Assert.Equal(
            new[] { "fish_ghost", "fish_jellyfish", "fish_lobster" },
            Table.All.Select(fish => fish.Id).ToArray());
    }

    [Fact]
    public void 正常加载_字段逐项落到定义上()
    {
        FishDefinition ghost = Table.Get("fish_ghost");

        Assert.Equal("fish_ghost", ghost.ItemId);
        Assert.Equal(CatchMethod.Rod, ghost.Method);
        Assert.Equal(2, ghost.Weight);
        Assert.Equal(new[] { Weather.Foggy }, ghost.Weathers.ToArray());

        FishDefinition jellyfish = Table.Get("fish_jellyfish");

        Assert.Equal(new[] { Season.Summer }, jellyfish.Seasons.ToArray());
        Assert.Equal(new[] { DayPhase.LateNight }, jellyfish.Phases.ToArray());
        Assert.Equal(FishDefinition.DefaultWeight, jellyfish.Weight);
    }

    [Fact]
    public void 条件字段省略时_该维度不限()
    {
        // 文档没给条件的鱼就是全季全天候可钓，不是「只在某个值出现」
        FishDefinition lobster = Table.Get("fish_lobster");

        foreach (Season season in Enum.GetValues<Season>())
            Assert.True(lobster.AppearsIn(season), $"{season} 应当不限");

        foreach (Weather weather in Enum.GetValues<Weather>())
            Assert.True(lobster.AppearsIn(weather), $"{weather} 应当不限");

        foreach (DayPhase phase in Enum.GetValues<DayPhase>())
            Assert.True(lobster.AppearsIn(phase), $"{phase} 应当不限");
    }

    [Fact]
    public void 条件字段写了就生效_三个维度各自都拦得住()
    {
        FishDefinition ghost = Table.Get("fish_ghost");      // 只限雾天
        FishDefinition jellyfish = Table.Get("fish_jellyfish"); // 只限夏季深夜

        Assert.True(ghost.IsAvailable(Season.Spring, Weather.Foggy, DayPhase.Morning));
        Assert.False(ghost.IsAvailable(Season.Spring, Weather.Sunny, DayPhase.Morning));

        Assert.True(jellyfish.IsAvailable(Season.Summer, Weather.Sunny, DayPhase.LateNight));
        Assert.False(jellyfish.IsAvailable(Season.Winter, Weather.Sunny, DayPhase.LateNight));  // 季节拦下
        Assert.False(jellyfish.IsAvailable(Season.Summer, Weather.Sunny, DayPhase.Morning));    // 时段拦下
    }

    [Fact]
    public void Candidates_按捕获方式过滤_蟹笼产出不会被鱼竿钓到()
    {
        // 「用鱼竿钓上一只龙虾」与 §7.3 的蟹笼「每日收取」矛盾
        Assert.Equal(
            new[] { "fish_ghost" },
            Table.Candidates(Season.Spring, Weather.Foggy, DayPhase.Morning, CatchMethod.Rod)
                 .Select(fish => fish.Id).ToArray());

        Assert.Equal(
            new[] { "fish_lobster" },
            Table.Candidates(Season.Spring, Weather.Foggy, DayPhase.Morning, CatchMethod.CrabPot)
                 .Select(fish => fish.Id).ToArray());
    }

    [Fact]
    public void Candidates_按季节与天气与时段过滤()
    {
        Assert.Empty(Table.Candidates(Season.Spring, Weather.Foggy, DayPhase.LateNight, CatchMethod.Rod)
                          .Where(fish => fish.Id == "fish_jellyfish"));

        Assert.Contains(Table.Candidates(Season.Summer, Weather.Sunny, DayPhase.LateNight, CatchMethod.Rod),
                        fish => fish.Id == "fish_jellyfish");

        // 晴天不会有幽灵鱼——这是 §3.3「雾天…幽灵鱼」那一条的反面
        Assert.DoesNotContain(Table.Candidates(Season.Spring, Weather.Sunny, DayPhase.Morning, CatchMethod.Rod),
                              fish => fish.Id == "fish_ghost");
    }

    [Fact]
    public void Candidates_顺序与表内顺序一致()
    {
        // 掷鱼靠权重前缀和落在候选上：顺序一变，同一组参数就会掷出不同的鱼
        Assert.Equal(
            Table.All.Where(fish => fish.Method == CatchMethod.Rod).Select(fish => fish.Id).ToArray(),
            Table.Candidates(Season.Summer, Weather.Foggy, DayPhase.LateNight, CatchMethod.Rod)
                 .Select(fish => fish.Id).ToArray());
    }

    [Fact]
    public void Get_未知鱼_抛_KeyNotFoundException_且消息含_id()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => Table.Get("fish_ghost_king"));

        Assert.Contains("fish_ghost_king", error.Message);
    }

    [Fact]
    public void TryGet_未知鱼_返回_false_而不是抛()
    {
        Assert.False(Table.TryGet("fish_ghost_king", out _));
        Assert.True(Table.TryGet("fish_ghost", out FishDefinition definition));
        Assert.Equal("fish_ghost", definition.Id);
    }

    [Fact]
    public void 加载_重复_id_时报错且消息里带那个_id()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith(Fish(), Fish(itemId: "fish_other")), Items));

        Assert.Contains("fish_ghost", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 加载_id_为空时报错(string id)
    {
        Assert.Throws<InvalidDataException>(() => FishTable.FromJson(JsonWith(Fish(id: id)), Items));
    }

    [Fact]
    public void 加载_缺_itemId_时报错且消息里带_id()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith("""{ "id": "fish_ghost", "method": "Rod" }"""), Items));

        Assert.Contains("fish_ghost", error.Message);
    }

    [Fact]
    public void 加载_itemId_为空时报错()
    {
        Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith(Fish(itemId: "  ")), Items));
    }

    [Fact]
    public void 加载_缺_method_时报错()
    {
        Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith("""{ "id": "fish_ghost", "itemId": "fish_ghost" }"""), Items));
    }

    [Theory]
    [InlineData("\"Net\"")]     // 文档里没有第三种捕获方式
    [InlineData("true")]        // 不是字符串
    [InlineData("0")]           // 按枚举序号写：序号会随枚举插值错位，必须当场拒
    [InlineData("1")]
    public void 加载_method_不认识时报错且消息里带_id(string method)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith(Fish(method: method)), Items));

        Assert.Contains("fish_ghost", error.Message);
    }

    [Fact]
    public void 加载_method_大小写不敏感()
    {
        FishTable table = FishTable.FromJson(JsonWith(Fish(method: "\"crabpot\"")), Items);

        Assert.Equal(CatchMethod.CrabPot, table.Get("fish_ghost").Method);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void 加载_weight_非正时报错且消息里带_id(int weight)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith(Fish(extra: $", \"weight\": {weight}")), Items));

        Assert.Contains("fish_ghost", error.Message);
    }

    [Fact]
    public void 加载_weight_不是数字时报错()
    {
        Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith(Fish(extra: ", \"weight\": \"2\"")), Items));
    }

    [Theory]
    [InlineData(", \"seasons\": \"Summer\"")]
    [InlineData(", \"weather\": \"Foggy\"")]
    [InlineData(", \"phases\": \"LateNight\"")]
    public void 加载_条件字段不是数组时报错(string extra)
    {
        Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith(Fish(extra: extra)), Items));
    }

    [Theory]
    [InlineData(", \"seasons\": [\"Hiver\"]")]
    [InlineData(", \"seasons\": [0]")]
    [InlineData(", \"weather\": [\"Rain\"]")]
    [InlineData(", \"weather\": [3]")]
    [InlineData(", \"phases\": [\"Noon\"]")]
    [InlineData(", \"phases\": [2]")]
    public void 加载_条件里有不认识的名字时报错且消息里带_id(string extra)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith(Fish(extra: extra)), Items));

        Assert.Contains("fish_ghost", error.Message);
    }

    [Theory]
    [InlineData(", \"seasons\": []")]
    [InlineData(", \"weather\": []")]
    [InlineData(", \"phases\": []")]
    public void 加载_条件数组为空时报错(string extra)
    {
        // 空数组不是「不限」的写法（不限就省略字段）：它更可能是「一条都钓不到」，两种理解差一条鱼
        var error = Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith(Fish(extra: extra)), Items));

        Assert.Contains("fish_ghost", error.Message);
    }

    [Fact]
    public void 加载_条件里重复写同一个值时报错()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith(Fish(extra: ", \"weather\": [\"Foggy\", \"foggy\"]")), Items));

        Assert.Contains("Foggy", error.Message);
    }

    [Fact]
    public void 加载_没有_fish_数组时报错()
    {
        Assert.Throws<InvalidDataException>(() => FishTable.FromJson("""{ "鱼": [] }""", Items));
    }

    [Fact]
    public void 交叉校验_itemId_不在物品表里时报错且消息里带那个_id()
    {
        // 鱼表自己完全自洽，只有把两张表放在一起看才露馅
        var error = Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(JsonWith(Fish(id: "fish_ghost", itemId: "fish_nonexistent")), Items));

        Assert.Contains("fish_nonexistent", error.Message);
    }

    [Fact]
    public void 交叉校验_两条都对不上时_一次把两个_id_都报出来()
    {
        // 一条一条修比每次重跑才发现下一条快得多
        var error = Assert.Throws<InvalidDataException>(
            () => FishTable.FromJson(
                JsonWith(Fish(id: "fish_a", itemId: "fish_a"), Fish(id: "fish_b", itemId: "fish_b")),
                Items));

        Assert.Contains("fish_a", error.Message);
        Assert.Contains("fish_b", error.Message);
    }

    [Fact]
    public void 交叉校验_要物品表_传_null_时抛()
    {
        Assert.Throws<ArgumentNullException>(() => FishTable.FromJson(ValidJson, null!));
    }

    // ——— 以下是针对缺省数据文件本身的用例：抄错了、凑数了、漏录了都要有人发现 ———

    /// <summary>
    /// §7.3 只给了这些名字：幽灵鱼、水母、灵鱼（第 421 行「特殊鱼」）；龙虾、螃蟹、虾（第 423 行蟹笼）；
    /// 鲟鱼（附录 C 威利的礼物，第 1202 行）。<b>传说鱼说了「5 种」却没给名单，故一条都不录。</b>
    /// </summary>
    [Fact]
    public void 缺省数据文件_只录了文档点过名的这7条鱼()
    {
        string[] expected =
        {
            "fish_crab", "fish_ghost", "fish_jellyfish", "fish_lobster",
            "fish_shrimp", "fish_spirit", "fish_sturgeon",
        };

        FishTable table = LoadDefault();

        Assert.Equal(expected, table.All.Select(fish => fish.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void 缺省数据文件_幽灵鱼只在雾天_其余维度不限()
    {
        // §3.3（第 131 行）：「雾天…特殊鱼出现，幽灵鱼」——文档里唯一明写的鱼与天气的对应
        FishDefinition ghost = LoadDefault().Get("fish_ghost");

        Assert.Equal(CatchMethod.Rod, ghost.Method);
        Assert.Equal(new[] { Weather.Foggy }, ghost.Weathers.ToArray());
        Assert.Empty(ghost.Seasons);
        Assert.Empty(ghost.Phases);

        Assert.True(ghost.IsAvailable(Season.Spring, Weather.Foggy, DayPhase.Morning));
        Assert.False(ghost.IsAvailable(Season.Spring, Weather.Sunny, DayPhase.Morning));
    }

    [Fact]
    public void 缺省数据文件_水母只在夏季深夜()
    {
        // §3.4（第 139 行）：「夏 | 28 日 | 月光水母节 | 夜间钓鱼、水母观赏」——文档里水母唯一的
        // 季节与时段线索；「夜间」取 §3.1 的深夜档（22:00-2:00，那一段才列了「夜钓」）
        FishDefinition jellyfish = LoadDefault().Get("fish_jellyfish");

        Assert.Equal(new[] { Season.Summer }, jellyfish.Seasons.ToArray());
        Assert.Equal(new[] { DayPhase.LateNight }, jellyfish.Phases.ToArray());
        Assert.Empty(jellyfish.Weathers);

        Assert.True(jellyfish.IsAvailable(Season.Summer, Weather.Storm, DayPhase.LateNight));
        Assert.False(jellyfish.IsAvailable(Season.Autumn, Weather.Sunny, DayPhase.LateNight));
    }

    [Fact]
    public void 缺省数据文件_灵鱼与鲟鱼没给条件_故全季全天候()
    {
        // 文档没给这两条的条件。「文档没给」的正确结果是「不限」，不是自己编一个季节出来
        FishTable table = LoadDefault();

        Assert.All(new[] { "fish_spirit", "fish_sturgeon" }, id =>
        {
            FishDefinition fish = table.Get(id);

            Assert.Equal(CatchMethod.Rod, fish.Method);
            Assert.Empty(fish.Seasons);
            Assert.Empty(fish.Weathers);
            Assert.Empty(fish.Phases);
        });
    }

    [Fact]
    public void 缺省数据文件_蟹笼产出是龙虾螃蟹虾_且鱼竿钓不到它们()
    {
        // §7.3（第 423 行）：「蟹笼：放置在水域，每日收取龙虾、螃蟹、虾等」——「等」是开放集，
        // 没给名字的其余产出不录
        FishTable table = LoadDefault();

        Assert.Equal(
            new[] { "fish_crab", "fish_lobster", "fish_shrimp" },
            table.All.Where(fish => fish.Method == CatchMethod.CrabPot)
                 .Select(fish => fish.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());

        // 龙虾在四季的任意天气与时段都不该出现在鱼竿的候选里
        foreach (Season season in Enum.GetValues<Season>())
        foreach (Weather weather in Enum.GetValues<Weather>())
        foreach (DayPhase phase in Enum.GetValues<DayPhase>())
        {
            Assert.DoesNotContain(
                table.Candidates(season, weather, phase, CatchMethod.Rod),
                fish => fish.Method == CatchMethod.CrabPot);
        }
    }

    [Fact]
    public void 缺省数据文件_鱼竿的四种名字与_7_3_逐条一致()
    {
        // §7.3（第 413 行）：「鱼竿：竹竿、玻璃鱼竿、铱鱼竿、灵鱼竿」。文档只给了名字，没有价格
        ItemTable items = ItemTable.LoadDefault();

        Assert.Equal("竹竿", items.Get("tool_fishing_rod_bamboo").Name);
        Assert.Equal("玻璃鱼竿", items.Get("tool_fishing_rod_glass").Name);
        Assert.Equal("铱鱼竿", items.Get("tool_fishing_rod_iridium").Name);
        Assert.Equal("灵鱼竿", items.Get("tool_fishing_rod_spirit").Name);

        Assert.All(
            new[]
            {
                "tool_fishing_rod_bamboo", "tool_fishing_rod_glass",
                "tool_fishing_rod_iridium", "tool_fishing_rod_spirit",
            },
            id =>
            {
                ItemDefinition rod = items.Get(id);

                Assert.Equal(ItemCategory.Tool, rod.Category);
                Assert.Equal(1, rod.MaxStack);
                Assert.Equal(0, rod.BuyPrice);
                Assert.Equal(0, rod.SellPrice);
            });
    }

    [Fact]
    public void 缺省数据文件_鱼价与鱼竿价文档未给_一律填_0()
    {
        // §7.3 与 §12.1 鱼店都没给鱼价，鱼竿价也没有。填 0 是「待补」的记号，不是「免费」
        ItemTable items = ItemTable.LoadDefault();
        FishTable table = LoadDefault();

        Assert.All(table.All, fish =>
        {
            ItemDefinition item = items.Get(fish.ItemId);

            Assert.Equal(0, item.BuyPrice);
            Assert.Equal(0, item.SellPrice);
        });
    }

    [Fact]
    public void 缺省数据文件_鱼的_id_与_itemId_一一对应且物品表里找得到()
    {
        // 今天两者取值相同是有意的（图鉴键跟着鱼走）——但对不上就是数据错误，得有人发现
        FishTable table = LoadDefault();
        ItemTable items = ItemTable.LoadDefault();

        Assert.All(table.All, fish => Assert.Equal(fish.Id, fish.ItemId));

        Assert.Equal(
            table.All.Select(fish => fish.ItemId).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            table.All.Select(fish => fish.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());

        Assert.All(table.All, fish => Assert.Equal(ItemCategory.Food, items.Get(fish.ItemId).Category));
    }

    private static FishTable LoadDefault() => FishTable.LoadDefault(ItemTable.LoadDefault());
}
