using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Systems.Items;
using XingGame.Systems.Ranching;

namespace XingGame.Tests;

/// <summary>
/// 动物表（M2 畜牧）。这里守两件事：① 坏数据必须在加载时炸；② <c>data/ranching/animals.json</c>
/// 与 §6.5 逐条一致——购买价抄错、产出周期抄错（少写一天就白送玩家一轮产出）都要有人发现。
/// </summary>
/// <remarks>
/// 交叉校验（动物表 ↔ 物品表）是两边的测试都发现不了的那种错：单看动物表完全自洽，单看物品表也是，
/// 所以用例刻意分成「两张表都造」来守——产出物与饲料都要能在物品表里找到。
/// </remarks>
public class AnimalTableTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "material_hay",          "name": "干草", "description": "普通动物的饲料。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_spirit_grass", "name": "灵草", "description": "灵兽的饲料。",     "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "food_egg",              "name": "鸡蛋", "description": "白鸡的产出。",     "category": "Food",     "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "food_milk",             "name": "牛奶", "description": "牛的产出。",       "category": "Food",     "maxStack": 999, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string ValidJson = """
    {
      "animals": [
        { "id": "white_chicken", "name": "白鸡", "building": "鸡舍", "buyPrice": 800, "produceId": "food_egg",  "productionIntervalDays": 1, "type": "Normal", "growthDays": 0 },
        { "id": "cow",           "name": "牛",   "building": "畜棚", "buyPrice": 1500, "produceId": "food_milk", "productionIntervalDays": 1, "type": "Normal", "growthDays": 0 }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);

    private static readonly AnimalTable Table = AnimalTable.FromJson(ValidJson, Items);

    /// <summary>把条目拼成一份表。坏数据用例只改一个字段，不必每次抄一整份 JSON。</summary>
    private static string JsonWith(params string[] entries) => "{ \"animals\": [" + string.Join(",", entries) + "] }";

    private static string Animal(
        string id = "white_chicken",
        string name = "白鸡",
        string building = "鸡舍",
        string buyPrice = "800",
        string produceId = "food_egg",
        string interval = "1",
        string type = "Normal",
        string growthDays = "0") =>
        $"{{ \"id\": \"{id}\", \"name\": \"{name}\", \"building\": \"{building}\", \"buyPrice\": {buyPrice}, " +
        $"\"produceId\": \"{produceId}\", \"productionIntervalDays\": {interval}, " +
        $"\"type\": \"{type}\", \"growthDays\": {growthDays} }}";

    [Fact]
    public void 正常加载_All_的数量与顺序与文件一致()
    {
        Assert.Equal(2, Table.All.Count);
        Assert.Equal(
            new[] { "white_chicken", "cow" },
            Table.All.Select(animal => animal.AnimalId).ToArray());
    }

    [Fact]
    public void 正常加载_字段逐项落到定义上()
    {
        AnimalDefinition chicken = Table.Get("white_chicken");

        Assert.Equal("white_chicken", chicken.AnimalId);
        Assert.Equal("白鸡", chicken.Name);
        Assert.Equal("鸡舍", chicken.Building);
        Assert.Equal(800, chicken.BuyPrice);
        Assert.Equal("food_egg", chicken.ProduceItemId);
        Assert.Equal(1, chicken.ProductionIntervalDays);
        Assert.Equal(AnimalType.Normal, chicken.Type);
        Assert.Equal(0, chicken.GrowthDays);
    }

    [Fact]
    public void Get_未知动物_抛_KeyNotFoundException_且消息含_id()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => Table.Get("sheep"));

        Assert.Contains("sheep", error.Message);
    }

    [Fact]
    public void TryGet_未知动物_返回_false_而不是抛()
    {
        Assert.False(Table.TryGet("sheep", out _));
    }

    // ——— 饲料由类型推导（§6.5「灵兽需喂灵草」）———

    [Fact]
    public void 饲料_普通动物吃干草_灵兽吃灵草()
    {
        // 同一个事实只该有一份：饲料不按行录制，而是由类型推出来
        Assert.Equal(AnimalDefinition.HayItemId, Table.Get("white_chicken").FeedItemId);

        AnimalTable spirit = AnimalTable.FromJson(
            JsonWith(Animal(id: "spirit_fox", name: "灵狐", building: "灵兽园", produceId: "food_egg", type: "Spirit")),
            Items);

        Assert.Equal(AnimalDefinition.SpiritGrassItemId, spirit.Get("spirit_fox").FeedItemId);
    }

    // ——— 加载时的校验：坏数据必须在这里炸，而不是等玩家收畜产时 ———

    [Fact]
    public void 加载_重复的动物_id_时报错且消息里带那个_id()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => AnimalTable.FromJson(JsonWith(Animal(), Animal(name: "另一只白鸡")), Items));

        Assert.Contains("white_chicken", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 加载_动物_id_为空时报错(string id)
    {
        Assert.Throws<InvalidDataException>(() => AnimalTable.FromJson(JsonWith(Animal(id: id)), Items));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 加载_name_为空时报错(string name)
    {
        Assert.Throws<InvalidDataException>(() => AnimalTable.FromJson(JsonWith(Animal(name: name)), Items));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 加载_building_为空时报错(string building)
    {
        Assert.Throws<InvalidDataException>(() => AnimalTable.FromJson(JsonWith(Animal(building: building)), Items));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-800)]
    public void 加载_buyPrice_为负时报错且消息里带_id(int buyPrice)
    {
        // 负的购买价会让「买动物倒赚一笔」
        var error = Assert.Throws<InvalidDataException>(
            () => AnimalTable.FromJson(JsonWith(Animal(buyPrice: buyPrice.ToString())), Items));

        Assert.Contains("white_chicken", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 加载_produceId_为空时报错(string produceId)
    {
        Assert.Throws<InvalidDataException>(() => AnimalTable.FromJson(JsonWith(Animal(produceId: produceId)), Items));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 加载_productionIntervalDays_非正时报错且消息里带_id(int interval)
    {
        // 非正的周期意味着「每天产出」或「永远不产出」，两种都不是 §6.5 的意思
        var error = Assert.Throws<InvalidDataException>(
            () => AnimalTable.FromJson(JsonWith(Animal(interval: interval.ToString())), Items));

        Assert.Contains("white_chicken", error.Message);
    }

    [Fact]
    public void 加载_growthDays_为负时报错()
    {
        Assert.Throws<InvalidDataException>(
            () => AnimalTable.FromJson(JsonWith(Animal(growthDays: "-1")), Items));
    }

    [Theory]
    [InlineData("""{ "name": "白鸡", "building": "鸡舍", "buyPrice": 800, "produceId": "food_egg", "productionIntervalDays": 1, "type": "Normal", "growthDays": 0 }""")]        // 缺 id
    [InlineData("""{ "id": "white_chicken", "building": "鸡舍", "buyPrice": 800, "produceId": "food_egg", "productionIntervalDays": 1, "type": "Normal", "growthDays": 0 }""")]    // 缺 name
    [InlineData("""{ "id": "white_chicken", "name": "白鸡", "buyPrice": 800, "produceId": "food_egg", "productionIntervalDays": 1, "type": "Normal", "growthDays": 0 }""")]        // 缺 building
    [InlineData("""{ "id": "white_chicken", "name": "白鸡", "building": "鸡舍", "produceId": "food_egg", "productionIntervalDays": 1, "type": "Normal", "growthDays": 0 }""")]     // 缺 buyPrice
    [InlineData("""{ "id": "white_chicken", "name": "白鸡", "building": "鸡舍", "buyPrice": 800, "productionIntervalDays": 1, "type": "Normal", "growthDays": 0 }""")]             // 缺 produceId
    [InlineData("""{ "id": "white_chicken", "name": "白鸡", "building": "鸡舍", "buyPrice": 800, "produceId": "food_egg", "type": "Normal", "growthDays": 0 }""")]                 // 缺 productionIntervalDays
    [InlineData("""{ "id": "white_chicken", "name": "白鸡", "building": "鸡舍", "buyPrice": 800, "produceId": "food_egg", "productionIntervalDays": 1, "growthDays": 0 }""")]      // 缺 type
    [InlineData("""{ "id": "white_chicken", "name": "白鸡", "building": "鸡舍", "buyPrice": 800, "produceId": "food_egg", "productionIntervalDays": 1, "type": "Normal" }""")]      // 缺 growthDays
    public void 加载_缺字段时报错(string entry)
    {
        Assert.Throws<InvalidDataException>(() => AnimalTable.FromJson(JsonWith(entry), Items));
    }

    [Theory]
    [InlineData("神兽")]   // 文档里有这个词（§8.9 妖兽），但不是 §6.5「类型」列的取值
    [InlineData("灵兽")]   // 连文档的中文都没放行：表里写的是英文枚举名
    [InlineData("Normal,Spirit")]
    public void 加载_type_不认识时报错且消息里带_id_与那个值(string type)
    {
        // 不用 Enum.TryParse：它会把 "1" 认成序号 1 的类型，而序号会随枚举插值错位
        var error = Assert.Throws<InvalidDataException>(
            () => AnimalTable.FromJson(JsonWith(Animal(type: type)), Items));

        Assert.Contains("white_chicken", error.Message);
        Assert.Contains(type, error.Message);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("true")]
    public void 加载_type_不是字符串时报错(string rawType)
    {
        string entry =
            $$"""{ "id": "white_chicken", "name": "白鸡", "building": "鸡舍", "buyPrice": 800, "produceId": "food_egg", "productionIntervalDays": 1, "type": {{rawType}}, "growthDays": 0 }""";

        var error = Assert.Throws<InvalidDataException>(() => AnimalTable.FromJson(JsonWith(entry), Items));

        Assert.Contains("type", error.Message);
    }

    [Fact]
    public void 加载_大小写不敏感的类型名能被接受()
    {
        AnimalTable table = AnimalTable.FromJson(JsonWith(Animal(type: "spirit")), Items);

        Assert.Equal(AnimalType.Spirit, table.Get("white_chicken").Type);
    }

    [Fact]
    public void 加载_没有_animals_数组时报错()
    {
        Assert.Throws<InvalidDataException>(() => AnimalTable.FromJson("""{ "动物": [] }""", Items));
    }

    // ——— 交叉校验（动物表 ↔ 物品表）———

    [Fact]
    public void 交叉校验_产出物不在物品表里时报错且消息里带那个_id()
    {
        // 动物表自己完全自洽，只有把两张表放在一起看才露馅
        var error = Assert.Throws<InvalidDataException>(
            () => AnimalTable.FromJson(JsonWith(Animal(produceId: "food_ghost")), Items));

        Assert.Contains("food_ghost", error.Message);
    }

    [Fact]
    public void 交叉校验_饲料不在物品表里时报错且消息里带那个_id()
    {
        // 饲料缺了不会报错，只会让「喂不了」变成一个查不出的谜
        var error = Assert.Throws<InvalidDataException>(
            () => AnimalTable.FromJson(JsonWith(Animal(type: "Spirit", produceId: "food_egg")),
                                       ItemTable.FromJson("""{ "items": [ { "id": "food_egg", "name": "鸡蛋", "description": "产出。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 } ] }""")));

        Assert.Contains(AnimalDefinition.SpiritGrassItemId, error.Message);
    }

    [Fact]
    public void 交叉校验_两条都对不上时_一次把两个_id_都报出来()
    {
        // 一条一条修比每次重跑才发现下一条快得多
        var error = Assert.Throws<InvalidDataException>(
            () => AnimalTable.FromJson(JsonWith(Animal(produceId: "food_ghost")),
                                       ItemTable.FromJson("""{ "items": [ { "id": "material_ghost", "name": "干草", "description": "饲料。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 } ] }""")));

        Assert.Contains("food_ghost", error.Message);
        Assert.Contains("material_hay", error.Message);
    }

    // ——— 以下是针对缺省数据文件本身的用例：抄错了、漏录了都要有人发现 ———

    [Fact]
    public void 缺省数据文件_11种动物都在且只录了这11种()
    {
        // §6.5 畜牧系统表（design.md 第 311-321 行）逐行
        string[] expected =
        {
            "white_chicken", "brown_chicken", "blue_chicken", "cow", "goat", "sheep", "pig", "duck",
            "spirit_rabbit", "spirit_fox", "spirit_crane",
        };

        AnimalTable table = AnimalTable.LoadDefault(ItemTable.LoadDefault());

        Assert.Equal(expected.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                     table.All.Select(animal => animal.AnimalId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    // §6.5（design.md 311-321 行）的 动物 / 建筑 / 购买价 / 产出 / 产出周期 / 类型 六列逐条照抄。
    // 蓝鸡的购买价格在文档里写的是「稀有」而不是数字，故填 0（文档未给，待补）。
    // 猪的产出周期写的是「每天（户外）」——户外条件尚未落地，按每天产出。
    [InlineData("white_chicken",  "白鸡",   "鸡舍",   800,   "food_egg",                     1, "Normal")]
    [InlineData("brown_chicken",  "棕鸡",   "鸡舍",   800,   "food_large_egg",               1, "Normal")]
    [InlineData("blue_chicken",   "蓝鸡",   "鸡舍",   0,     "food_blue_egg",                1, "Normal")]
    [InlineData("cow",            "牛",     "畜棚",   1500,  "food_milk",                    1, "Normal")]
    [InlineData("goat",           "山羊",   "畜棚",   4000,  "food_goat_milk",               2, "Normal")]
    [InlineData("sheep",          "羊",     "畜棚",   8000,  "material_wool",                3, "Normal")]
    [InlineData("pig",            "猪",     "畜棚",   16000, "food_truffle",                 1, "Normal")]
    [InlineData("duck",           "鸭",     "鸡舍",   1200,  "food_duck_egg",                2, "Normal")]
    [InlineData("spirit_rabbit",  "灵兔",   "灵兽园", 5000,  "material_spirit_rabbit_fur",   3, "Spirit")]
    [InlineData("spirit_fox",     "灵狐",   "灵兽园", 15000, "material_spirit_fox_fire",     5, "Spirit")]
    [InlineData("spirit_crane",   "灵鹤",   "灵兽园", 30000, "material_spirit_crane_feather", 7, "Spirit")]
    public void 缺省数据文件_11种动物的各列与_6_5_逐条一致(
        string animalId, string name, string building, int buyPrice, string produceId, int intervalDays, string type)
    {
        AnimalDefinition animal = AnimalTable.LoadDefault(ItemTable.LoadDefault()).Get(animalId);

        Assert.Equal(name, animal.Name);
        Assert.Equal(building, animal.Building);
        Assert.Equal(buyPrice, animal.BuyPrice);
        Assert.Equal(produceId, animal.ProduceItemId);
        Assert.Equal(intervalDays, animal.ProductionIntervalDays);
        Assert.Equal(Enum.Parse<AnimalType>(type), animal.Type);
    }

    [Fact]
    public void 缺省数据文件_成长天数全是0_因为文档没给()
    {
        // §6.5 没有「成长天数」这一列。0 即「买入当天就成年」，不替文档给玩家加一段等待期。
        // 文档补上数值后只改 JSON，本用例届时该跟着改成逐条比对
        Assert.All(AnimalTable.LoadDefault(ItemTable.LoadDefault()).All,
                   animal => Assert.Equal(0, animal.GrowthDays));
    }

    [Fact]
    public void 缺省数据文件_灵兽只有灵兔灵狐灵鹤()
    {
        // §6.5「类型」列只有这三种是「灵兽」，多标一种就会让它的饲料变成灵草
        AnimalTable table = AnimalTable.LoadDefault(ItemTable.LoadDefault());

        Assert.Equal(
            new[] { "spirit_crane", "spirit_fox", "spirit_rabbit" },
            table.All.Where(animal => animal.Type == AnimalType.Spirit)
                      .Select(animal => animal.AnimalId)
                      .OrderBy(id => id, StringComparer.Ordinal)
                      .ToArray());
    }

    [Theory]
    // §6.5「产出」列的名字与分类。兽产物没有价格（§6.2 的售价列只管作物），两个价格字段都是 0
    [InlineData("food_egg",                      "鸡蛋",   ItemCategory.Food)]
    [InlineData("food_large_egg",                "大鸡蛋", ItemCategory.Food)]
    [InlineData("food_blue_egg",                 "蓝蛋",   ItemCategory.Food)]
    [InlineData("food_milk",                     "牛奶",   ItemCategory.Food)]
    [InlineData("food_goat_milk",                "羊奶",   ItemCategory.Food)]
    [InlineData("material_wool",                 "羊毛",   ItemCategory.Material)]
    [InlineData("food_truffle",                  "松露",   ItemCategory.Food)]
    [InlineData("food_duck_egg",                 "鸭蛋",   ItemCategory.Food)]
    [InlineData("material_spirit_rabbit_fur",    "灵兔毛", ItemCategory.Material)]
    [InlineData("material_spirit_fox_fire",      "灵狐火", ItemCategory.Material)]
    [InlineData("material_spirit_crane_feather", "灵鹤羽", ItemCategory.Material)]
    // §6.5「喂养」列的两种饲料
    [InlineData("material_hay",                  "干草",   ItemCategory.Material)]
    [InlineData("material_spirit_grass",         "灵草",   ItemCategory.Material)]
    public void 缺省数据文件_畜产物与饲料在物品表里_名字与分类对得上(string id, string name, ItemCategory category)
    {
        // 条数对了但张冠李戴（比如把羊毛的名字写到灵狐火上）也该被发现
        ItemDefinition item = ItemTable.LoadDefault().Get(id);

        Assert.Equal(name, item.Name);
        Assert.Equal(category, item.Category);
        Assert.Equal(0, item.SellPrice);   // 文档未给，待补
        Assert.Equal(0, item.BuyPrice);    // 文档未给，待补
    }
}
