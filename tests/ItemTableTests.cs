using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 物品表（M1-4）。本工程第一次有真正的数据驱动内容，所以这里既守解析（坏数据必须在加载时炸），
/// 也守 <c>data/items/items.json</c> 本身——数值抄错、整条漏录（§6.2 的 11 种作物就漏过一次）
/// 都要有人发现。
/// </summary>
public class ItemTableTests
{
    private const string ValidJson = """
    {
      "items": [
        { "id": "crop_parsnip", "name": "防风草", "description": "春季作物。", "category": "Crop", "maxStack": 999, "buyPrice": 0, "sellPrice": 35 },
        { "id": "material_wood", "name": "木材", "description": "建造与制作材料。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private static readonly ItemTable Table = ItemTable.FromJson(ValidJson);

    /// <summary>把条目拼成一份表。坏数据用例只改一个字段，不必每次抄一整份 JSON。</summary>
    private static string JsonWith(params string[] entries) => "{ \"items\": [" + string.Join(",", entries) + "] }";

    private static string Item(
        string id, string category = "Crop", int maxStack = 999, int buyPrice = 0, int sellPrice = 35) =>
        $"{{ \"id\": \"{id}\", \"name\": \"名字\", \"description\": \"描述\", " +
        $"\"category\": \"{category}\", \"maxStack\": {maxStack}, " +
        $"\"buyPrice\": {buyPrice}, \"sellPrice\": {sellPrice} }}";

    [Fact]
    public void 正常加载_All_的数量与顺序与文件一致()
    {
        ItemTable table = ItemTable.FromJson(ValidJson);

        Assert.Equal(2, table.All.Count);
        Assert.Equal(
            new[] { "crop_parsnip", "material_wood" },
            table.All.Select(item => item.Id).ToArray());
    }

    [Fact]
    public void 正常加载_字段逐项落到定义上()
    {
        ItemDefinition parsnip = Table.Get("crop_parsnip");

        Assert.Equal("crop_parsnip", parsnip.Id);
        Assert.Equal("防风草", parsnip.Name);
        Assert.Equal("春季作物。", parsnip.Description);
        Assert.Equal(ItemCategory.Crop, parsnip.Category);
        Assert.Equal(999, parsnip.MaxStack);
        Assert.Equal(0, parsnip.BuyPrice);
        Assert.Equal(35, parsnip.SellPrice);
    }

    [Fact]
    public void 加载_重复_id_时报错且消息里带那个_id()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => ItemTable.FromJson(JsonWith(Item("crop_parsnip"), Item("crop_parsnip"))));

        Assert.Contains("crop_parsnip", error.Message);
    }

    [Fact]
    public void 加载_分类名非法_时报错且消息里带非法字符串与_id()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => ItemTable.FromJson(JsonWith(Item("crop_parsnip", category: "Crops"))));

        Assert.Contains("Crops", error.Message);
        Assert.Contains("crop_parsnip", error.Message);
    }

    [Fact]
    public void 加载_分类名写成数字序号_时报错()
    {
        // Enum.TryParse 会把 "3" 认成序号 3 的分类；ADR-012 明确要避开「按序号写分类」——
        // 序号会随枚举插值错位，而名字不会
        Assert.Throws<InvalidDataException>(() => ItemTable.FromJson(JsonWith(Item("crop_parsnip", category: "3"))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 加载_maxStack_非正时报错(int maxStack)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => ItemTable.FromJson(JsonWith(Item("crop_parsnip", maxStack: maxStack))));

        Assert.Contains("crop_parsnip", error.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void 加载_买入价为负时报错(int buyPrice)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => ItemTable.FromJson(JsonWith(Item("crop_parsnip", buyPrice: buyPrice))));

        Assert.Contains("crop_parsnip", error.Message);
    }

    [Fact]
    public void 加载_卖出价为负时报错()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => ItemTable.FromJson(JsonWith(Item("crop_parsnip", sellPrice: -1))));

        Assert.Contains("crop_parsnip", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 加载_id_为空时报错(string id)
    {
        Assert.Throws<InvalidDataException>(() => ItemTable.FromJson(JsonWith(Item(id))));
    }

    [Fact]
    public void 加载_缺字段时报错()
    {
        const string missingSellPrice =
            """{ "items": [ { "id": "crop_parsnip", "name": "防风草", "description": "春季作物。", "category": "Crop", "maxStack": 999, "buyPrice": 0 } ] }""";

        var error = Assert.Throws<InvalidDataException>(() => ItemTable.FromJson(missingSellPrice));

        Assert.Contains("crop_parsnip", error.Message);
    }

    [Fact]
    public void 加载_没有_items_数组时报错()
    {
        Assert.Throws<InvalidDataException>(() => ItemTable.FromJson("""{ "物品": [] }"""));
    }

    [Fact]
    public void Get_未知_id_抛_KeyNotFoundException_且消息含_id()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => Table.Get("crop_unknown"));

        Assert.Contains("crop_unknown", error.Message);
    }

    [Fact]
    public void TryGet_未知_id_返回_false_而不是抛()
    {
        Assert.False(Table.TryGet("crop_unknown", out _));
        Assert.True(Table.TryGet("crop_parsnip", out ItemDefinition definition));
        Assert.Equal("防风草", definition.Name);
    }

    [Fact]
    public void 分类名_大小写不敏感()
    {
        // 与 WeatherTable 解析季节/天气名的做法一致：Mod 作者手写 JSON 时不必猜大小写
        ItemTable table = ItemTable.FromJson(JsonWith(
            Item("crop_parsnip", category: "crop"),
            Item("material_wood", category: "MATERIAL")));

        Assert.Equal(ItemCategory.Crop, table.Get("crop_parsnip").Category);
        Assert.Equal(ItemCategory.Material, table.Get("material_wood").Category);
    }

    [Fact]
    public void 物品_id_大小写敏感()
    {
        // id 是精确键：大小写不同就是另一个 id，不该悄悄取到东西
        Assert.False(Table.TryGet("Crop_Parsnip", out _));
    }

    // ——— 以下是针对缺省数据文件本身的用例：抄错了、漏录了都要有人发现 ———

    [Fact]
    public void 缺省数据文件_物品集合与文档出处一致()
    {
        // §6.2 的 11 种作物 + 各自的种子 + §12.3 的七种材料。文档没提的一条都不该有
        string[] expected =
        {
            "crop_parsnip", "crop_potato", "crop_strawberry", "crop_spirit_grass",
            "crop_blueberry", "crop_melon", "crop_fire_spirit_flower",
            "crop_pumpkin", "crop_cranberry", "crop_gold_spirit_fruit", "crop_ice_spirit_grass",

            "seed_parsnip", "seed_potato", "seed_strawberry", "seed_spirit_grass",
            "seed_blueberry", "seed_melon", "seed_fire_spirit_flower",
            "seed_pumpkin", "seed_cranberry", "seed_gold_spirit_fruit", "seed_ice_spirit_grass",

            "material_wood", "material_coal", "material_copper_ore", "material_copper_ingot",
            "material_iron_ingot", "material_spirit_spring_water", "material_spirit_stone",
        };

        ItemTable table = ItemTable.LoadDefault();

        Assert.Equal(expected.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                     table.All.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());

        // 条数对了但张冠李戴（比如某条作物写成了 Material）也该被发现
        Assert.Equal(11, table.All.Count(item => item.Category == ItemCategory.Crop));
        Assert.Equal(11, table.All.Count(item => item.Category == ItemCategory.Seed));
        Assert.Equal(7, table.All.Count(item => item.Category == ItemCategory.Material));
    }

    [Theory]
    // §6.2 作物列表（权威作物表）：春
    [InlineData("crop_parsnip", 35)]
    [InlineData("crop_potato", 80)]
    [InlineData("crop_strawberry", 120)]
    [InlineData("crop_spirit_grass", 200)]
    // 夏
    [InlineData("crop_blueberry", 50)]
    [InlineData("crop_melon", 250)]
    [InlineData("crop_fire_spirit_flower", 350)]
    // 秋
    [InlineData("crop_pumpkin", 320)]
    [InlineData("crop_cranberry", 75)]
    [InlineData("crop_gold_spirit_fruit", 500)]
    // 冬
    [InlineData("crop_ice_spirit_grass", 600)]
    public void 缺省数据文件_11种作物都在_且售价与_6_2_逐条一致(string id, int expectedSellPrice)
    {
        ItemDefinition crop = ItemTable.LoadDefault().Get(id);

        Assert.Equal(ItemCategory.Crop, crop.Category);
        Assert.Equal(expectedSellPrice, crop.SellPrice);
    }

    [Theory]
    // 附录 A 作物表「种子价」列 —— 它是**买入价**，不是卖出价
    [InlineData("seed_parsnip", 20)]
    [InlineData("seed_potato", 50)]
    [InlineData("seed_strawberry", 100)]
    [InlineData("seed_spirit_grass", 150)]
    public void 缺省数据文件_这4种种子的买入价取附录A种子价(string id, int expectedBuyPrice)
    {
        ItemDefinition seed = ItemTable.LoadDefault().Get(id);

        Assert.Equal(ItemCategory.Seed, seed.Category);
        Assert.Equal(expectedBuyPrice, seed.BuyPrice);
    }

    [Fact]
    public void 缺省数据文件_文档未给的价格一律填_0()
    {
        ItemTable table = ItemTable.LoadDefault();

        // 全表只有附录 A 那 4 种种子的买入价非零；其余（作物、材料、另外 7 种种子）文档都没给
        Assert.Equal(
            new[] { "seed_parsnip", "seed_potato", "seed_strawberry", "seed_spirit_grass" },
            table.All.Where(item => item.BuyPrice != 0).Select(item => item.Id).ToArray());

        // 种子的卖出价文档没给——附录 A 的「种子价」是买价，塞进卖价会让 M2 的商店买进卖出不亏不赚
        Assert.All(table.All.Where(item => item.Category != ItemCategory.Crop),
                   item => Assert.Equal(0, item.SellPrice));
    }

    [Fact]
    public void 缺省数据文件_当前全是可堆叠物品_上限_999()
    {
        // 设计文档里还没有出现过具体的工具条目，所以「工具类 maxStack = 1」暂时没有适用对象
        Assert.All(ItemTable.LoadDefault().All, item => Assert.Equal(999, item.MaxStack));
    }
}
