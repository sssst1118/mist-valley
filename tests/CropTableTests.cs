using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Systems.Farming;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 作物表（M1-5）。这里守两件事：① 坏数据必须在加载时炸；② <c>data/crops/crops.json</c> 与
/// §6.2 逐条一致——生长天数抄错、可多次收获标错（多标一种就会白送玩家一茬）都要有人发现。
/// </summary>
/// <remarks>
/// 交叉校验（作物表 ↔ 物品表）的两张表对不上，是两边的测试都发现不了的那种错：
/// 单看作物表完全自洽，单看物品表也是，所以用例刻意分成「两张表都造」来守。
/// </remarks>
public class CropTableTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "seed_parsnip",    "name": "防风草种子", "description": "春季播种。", "category": "Seed", "maxStack": 999, "buyPrice": 20, "sellPrice": 0 },
        { "id": "crop_parsnip",    "name": "防风草",     "description": "春季作物。", "category": "Crop", "maxStack": 999, "buyPrice": 0,  "sellPrice": 35 },
        { "id": "seed_strawberry", "name": "草莓种子",   "description": "春季播种。", "category": "Seed", "maxStack": 999, "buyPrice": 100, "sellPrice": 0 },
        { "id": "crop_strawberry", "name": "草莓",       "description": "春季作物。", "category": "Crop", "maxStack": 999, "buyPrice": 0,  "sellPrice": 120 }
      ]
    }
    """;

    private const string ValidJson = """
    {
      "crops": [
        { "seedId": "seed_parsnip",    "cropId": "crop_parsnip",    "growthDays": 4, "regrowable": false },
        { "seedId": "seed_strawberry", "cropId": "crop_strawberry", "growthDays": 8, "regrowable": true }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);

    private static readonly CropTable Table = CropTable.FromJson(ValidJson, Items);

    /// <summary>把条目拼成一份表。坏数据用例只改一个字段，不必每次抄一整份 JSON。</summary>
    private static string JsonWith(params string[] entries) => "{ \"crops\": [" + string.Join(",", entries) + "] }";

    private static string Crop(
        string seedId = "seed_parsnip",
        string cropId = "crop_parsnip",
        int growthDays = 4,
        string regrowable = "false") =>
        $"{{ \"seedId\": \"{seedId}\", \"cropId\": \"{cropId}\", " +
        $"\"growthDays\": {growthDays}, \"regrowable\": {regrowable} }}";

    [Fact]
    public void 正常加载_All_的数量与顺序与文件一致()
    {
        Assert.Equal(2, Table.All.Count);
        Assert.Equal(
            new[] { "seed_parsnip", "seed_strawberry" },
            Table.All.Select(crop => crop.SeedId).ToArray());
    }

    [Fact]
    public void 正常加载_字段逐项落到定义上()
    {
        CropDefinition parsnip = Table.GetBySeed("seed_parsnip");

        Assert.Equal("seed_parsnip", parsnip.SeedId);
        Assert.Equal("crop_parsnip", parsnip.CropId);
        Assert.Equal(4, parsnip.GrowthDays);
        Assert.False(parsnip.Regrowable);

        CropDefinition strawberry = Table.GetBySeed("seed_strawberry");
        Assert.Equal("crop_strawberry", strawberry.CropId);
        Assert.Equal(8, strawberry.GrowthDays);
        Assert.True(strawberry.Regrowable);
    }

    [Fact]
    public void 按作物_id_反查_拿得到同一份定义()
    {
        Assert.True(Table.TryGetByCrop("crop_strawberry", out CropDefinition definition));

        Assert.Equal("seed_strawberry", definition.SeedId);
        Assert.Equal(8, definition.GrowthDays);
    }

    [Fact]
    public void GetBySeed_未知种子_抛_KeyNotFoundException_且消息含_id()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => Table.GetBySeed("seed_potato"));

        Assert.Contains("seed_potato", error.Message);
    }

    [Fact]
    public void TryGetBySeed_未知种子_返回_false_而不是抛()
    {
        Assert.False(Table.TryGetBySeed("seed_potato", out _));
        Assert.False(Table.TryGetByCrop("crop_potato", out _));
    }

    [Fact]
    public void 加载_重复的种子_id_时报错且消息里带那个_id()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => CropTable.FromJson(JsonWith(Crop(), Crop(cropId: "crop_other")), Items));

        Assert.Contains("seed_parsnip", error.Message);
    }

    [Fact]
    public void 加载_重复的作物_id_时报错且消息里带那个_id()
    {
        // 两种种子结同一种作物会让「按作物反查」的结果取决于文件顺序
        var error = Assert.Throws<InvalidDataException>(
            () => CropTable.FromJson(JsonWith(Crop(), Crop(seedId: "seed_strawberry")), Items));

        Assert.Contains("crop_parsnip", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 加载_seedId_为空时报错(string seedId)
    {
        Assert.Throws<InvalidDataException>(() => CropTable.FromJson(JsonWith(Crop(seedId: seedId)), Items));
    }

    [Fact]
    public void 加载_cropId_为空时报错且消息里带_seedId()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => CropTable.FromJson(JsonWith(Crop(cropId: "  ")), Items));

        Assert.Contains("seed_parsnip", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 加载_growthDays_非正时报错且消息里带_seedId(int growthDays)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => CropTable.FromJson(JsonWith(Crop(growthDays: growthDays)), Items));

        Assert.Contains("seed_parsnip", error.Message);
    }

    [Theory]
    [InlineData("""{ "cropId": "crop_parsnip", "growthDays": 4, "regrowable": false }""")]        // 缺 seedId
    [InlineData("""{ "seedId": "seed_parsnip", "growthDays": 4, "regrowable": false }""")]         // 缺 cropId
    [InlineData("""{ "seedId": "seed_parsnip", "cropId": "crop_parsnip", "regrowable": false }""")] // 缺 growthDays
    [InlineData("""{ "seedId": "seed_parsnip", "cropId": "crop_parsnip", "growthDays": 4 }""")]     // 缺 regrowable
    public void 加载_缺字段时报错(string entry)
    {
        Assert.Throws<InvalidDataException>(() => CropTable.FromJson(JsonWith(entry), Items));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"是\"")]
    [InlineData("null")]
    public void 加载_regrowable_不是布尔时报错(string regrowable)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => CropTable.FromJson(JsonWith(Crop(regrowable: regrowable)), Items));

        Assert.Contains("regrowable", error.Message);
    }

    [Fact]
    public void 加载_没有_crops_数组时报错()
    {
        Assert.Throws<InvalidDataException>(() => CropTable.FromJson("""{ "作物": [] }""", Items));
    }

    [Fact]
    public void 交叉校验_种子_id_不在物品表里时报错且消息里带那个_id()
    {
        // 作物表自己完全自洽，只有把两张表放在一起看才露馅
        var error = Assert.Throws<InvalidDataException>(
            () => CropTable.FromJson(JsonWith(Crop(seedId: "seed_ghost")), Items));

        Assert.Contains("seed_ghost", error.Message);
    }

    [Fact]
    public void 交叉校验_作物_id_不在物品表里时报错且消息里带那个_id()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => CropTable.FromJson(JsonWith(Crop(cropId: "crop_ghost")), Items));

        Assert.Contains("crop_ghost", error.Message);
    }

    [Fact]
    public void 交叉校验_两条都对不上时_一次把两个_id_都报出来()
    {
        // 一条一条修比每次重跑才发现下一条快得多
        var error = Assert.Throws<InvalidDataException>(
            () => CropTable.FromJson(JsonWith(Crop(seedId: "seed_ghost", cropId: "crop_ghost")), Items));

        Assert.Contains("seed_ghost", error.Message);
        Assert.Contains("crop_ghost", error.Message);
    }

    // ——— 以下是针对缺省数据文件本身的用例：抄错了、漏录了都要有人发现 ———

    [Fact]
    public void 缺省数据文件_11种作物都在且只录了这11种()
    {
        // §6.2 的 11 种作物。该表里那行「冬 | 温室作物 | - | - | - | -」是占位而不是作物，不该录
        string[] expected =
        {
            "seed_parsnip", "seed_potato", "seed_strawberry", "seed_spirit_grass",
            "seed_blueberry", "seed_melon", "seed_fire_spirit_flower",
            "seed_pumpkin", "seed_cranberry", "seed_gold_spirit_fruit", "seed_ice_spirit_grass",
        };

        CropTable table = CropTable.LoadDefault(ItemTable.LoadDefault());

        Assert.Equal(expected.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                     table.All.Select(crop => crop.SeedId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    // §6.2 作物列表（权威作物表）：春
    [InlineData("seed_parsnip", 4)]
    [InlineData("seed_potato", 6)]
    [InlineData("seed_strawberry", 8)]
    [InlineData("seed_spirit_grass", 12)]
    // 夏
    [InlineData("seed_blueberry", 13)]
    [InlineData("seed_melon", 12)]
    [InlineData("seed_fire_spirit_flower", 15)]
    // 秋
    [InlineData("seed_pumpkin", 13)]
    [InlineData("seed_cranberry", 7)]
    [InlineData("seed_gold_spirit_fruit", 18)]
    // 冬
    [InlineData("seed_ice_spirit_grass", 20)]
    public void 缺省数据文件_11种作物的生长天数与_6_2_逐条一致(string seedId, int growthDays)
    {
        CropTable table = CropTable.LoadDefault(ItemTable.LoadDefault());

        Assert.Equal(growthDays, table.GetBySeed(seedId).GrowthDays);
    }

    [Fact]
    public void 缺省数据文件_可多次收获的只有草莓蓝莓蔓越莓()
    {
        // §6.2「可多次收获」列只有这三个「是」。多标一种就等于白送玩家一茬收成
        CropTable table = CropTable.LoadDefault(ItemTable.LoadDefault());

        Assert.Equal(
            new[] { "seed_blueberry", "seed_cranberry", "seed_strawberry" },
            table.All.Where(crop => crop.Regrowable)
                      .Select(crop => crop.SeedId)
                      .OrderBy(id => id, StringComparer.Ordinal)
                      .ToArray());
    }

    [Fact]
    public void 缺省数据文件_作物_id_与种子_id_一一对应()
    {
        CropTable table = CropTable.LoadDefault(ItemTable.LoadDefault());

        // 每条记录的 CropId 都不重复，且正好是那 11 种作物
        Assert.Equal(
            table.All.Select(crop => crop.SeedId).Select(id => id.Replace("seed_", "crop_", StringComparison.Ordinal))
                 .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            table.All.Select(crop => crop.CropId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void 缺省数据文件_种子与作物在物品表里的分类对得上()
    {
        // 条数对了但张冠李戴（比如把某条作物的 cropId 写到材料上）也该被发现
        ItemTable items = ItemTable.LoadDefault();

        Assert.All(CropTable.LoadDefault(items).All, crop =>
        {
            Assert.Equal(ItemCategory.Seed, items.Get(crop.SeedId).Category);
            Assert.Equal(ItemCategory.Crop, items.Get(crop.CropId).Category);
        });
    }
}
