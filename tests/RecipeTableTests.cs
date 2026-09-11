using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using XingGame.Systems.Crafting;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 配方表（M2 加工）。这里守两件事：① 坏数据必须在加载时炸；② <c>data/crafting/recipes.json</c> 与
/// §12.3 的五条示例配方逐条一致——材料抄错、多录一样、少录一样、凭空多出一条配方，都要有人发现。
/// </summary>
/// <remarks>
/// 交叉校验（配方表 ↔ 物品表）两张表对不上，是两边的测试都发现不了的那种错：单看配方表完全自洽，
/// 单看物品表也是，只有把两张表放在一起看才露馅。
/// </remarks>
public class RecipeTableTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "material_wood",     "name": "木材",   "description": "建造与制作材料。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_coal",     "name": "煤",     "description": "制作材料。",       "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "craft_scarecrow",   "name": "稻草人", "description": "放在田里的稻草人。", "category": "Decor",    "maxStack": 1,   "buyPrice": 0, "sellPrice": 0 },
        { "id": "craft_sprinkler",   "name": "洒水器", "description": "放置后自动浇水。", "category": "Equipment", "maxStack": 1,  "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string ValidJson = """
    {
      "recipes": [
        { "id": "recipe_scarecrow", "category": "Decor", "outputItemId": "craft_scarecrow", "outputCount": 1,
          "ingredients": [ { "itemId": "material_wood", "count": 50 }, { "itemId": "material_coal", "count": 1 } ] },
        { "id": "recipe_sprinkler", "category": "Device", "outputItemId": "craft_sprinkler", "outputCount": 1,
          "ingredients": [ { "itemId": "material_coal", "count": 1 } ] }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);

    private static readonly RecipeTable Table = RecipeTable.FromJson(ValidJson, Items);

    /// <summary>把条目拼成一份表。坏数据用例只改一个字段，不必每次抄一整份 JSON。</summary>
    private static string JsonWith(params string[] entries) => "{ \"recipes\": [" + string.Join(",", entries) + "] }";

    private static string Recipe(
        string id = "recipe_scarecrow",
        string category = "Decor",
        string outputItemId = "craft_scarecrow",
        string outputCount = "1",
        string ingredients = """[ { "itemId": "material_wood", "count": 50 }, { "itemId": "material_coal", "count": 1 } ]""") =>
        $"{{ \"id\": \"{id}\", \"category\": \"{category}\", \"outputItemId\": \"{outputItemId}\", " +
        $"\"outputCount\": {outputCount}, \"ingredients\": {ingredients} }}";

    [Fact]
    public void 正常加载_All_的数量与顺序与文件一致()
    {
        Assert.Equal(2, Table.All.Count);
        Assert.Equal(
            new[] { "recipe_scarecrow", "recipe_sprinkler" },
            Table.All.Select(recipe => recipe.Id).ToArray());
    }

    [Fact]
    public void 正常加载_字段逐项落到定义上()
    {
        RecipeDefinition scarecrow = Table.Get("recipe_scarecrow");

        Assert.Equal("recipe_scarecrow", scarecrow.Id);
        Assert.Equal(RecipeCategory.Decor, scarecrow.Category);
        Assert.Equal("craft_scarecrow", scarecrow.OutputItemId);
        Assert.Equal(1, scarecrow.OutputCount);
        Assert.Equal(
            new[] { new RecipeIngredient("material_wood", 50), new RecipeIngredient("material_coal", 1) },
            scarecrow.Ingredients.ToArray());
    }

    [Fact]
    public void 分类名_大小写不敏感()
    {
        // Mod 作者手写 JSON 时不该被大小写绊住（同 ItemTable 解析物品分类）
        RecipeTable table = RecipeTable.FromJson(JsonWith(Recipe(category: "dEcOr")), Items);

        Assert.Equal(RecipeCategory.Decor, table.Get("recipe_scarecrow").Category);
    }

    [Fact]
    public void Get_未知配方_抛_KeyNotFoundException_且消息含_id()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => Table.Get("recipe_ghost"));

        Assert.Contains("recipe_ghost", error.Message);
    }

    [Fact]
    public void TryGet_未知配方_返回_false_而不是抛()
    {
        Assert.False(Table.TryGet("recipe_ghost", out _));
    }

    [Fact]
    public void 加载_重复_id_时报错且消息里带那个_id()
    {
        // 重复 id 会让「按 id 取到的是哪一条」取决于文件顺序，而两条的材料可能不同
        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(), Recipe(category: "Device")), Items));

        Assert.Contains("recipe_scarecrow", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 加载_id_为空时报错(string id)
    {
        Assert.Throws<InvalidDataException>(() => RecipeTable.FromJson(JsonWith(Recipe(id: id)), Items));
    }

    [Fact]
    public void 加载_缺_id_时报错()
    {
        const string entry =
            """{ "category": "Decor", "outputItemId": "craft_scarecrow", "outputCount": 1, "ingredients": [ { "itemId": "material_wood", "count": 1 } ] }""";

        Assert.Throws<InvalidDataException>(() => RecipeTable.FromJson(JsonWith(entry), Items));
    }

    [Theory]
    [InlineData("Decor", "  ")]                // 产物 id 只有空白
    [InlineData("", "craft_scarecrow")]        // 分类为空
    public void 加载_产物或分类为空时报错(string category, string outputItemId)
    {
        Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(category: category, outputItemId: outputItemId)), Items));
    }

    [Theory]
    [InlineData("武器店")]
    [InlineData("3")]      // 数字序号：Enum.TryParse 会静默接受，序号却会随枚举插值错位（ADR-012）
    [InlineData("")]
    public void 加载_分类不是合法的_RecipeCategory_时报错(string category)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(category: category)), Items));

        Assert.Contains("recipe_scarecrow", error.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void 加载_产出数量非正时报错且消息里带_id(string outputCount)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(outputCount: outputCount)), Items));

        Assert.Contains("recipe_scarecrow", error.Message);
    }

    [Theory]
    [InlineData("[]")]                                                        // 材料列表为空
    [InlineData("""[ { "itemId": "material_wood", "count": 0 } ]""")]         // 数量为 0
    [InlineData("""[ { "itemId": "material_wood", "count": -1 } ]""")]        // 数量为负
    [InlineData("""[ { "itemId": "material_wood", "count": 1.5 } ]""")]       // 数量是小数
    public void 加载_材料列表为空或数量非正时报错(string ingredients)
    {
        Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(ingredients: ingredients)), Items));
    }

    [Fact]
    public void 加载_同一材料写两遍时报错()
    {
        // 同一样材料写两条时「到底要几个」取决于把哪一条算数——要两个就该写成一条、数量填 2
        const string twice = """[ { "itemId": "material_wood", "count": 50 }, { "itemId": "material_wood", "count": 5 } ]""";

        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(ingredients: twice)), Items));

        Assert.Contains("material_wood", error.Message);
    }

    [Theory]
    [InlineData("""{ "id": "recipe_scarecrow", "category": "Decor", "outputCount": 1, "ingredients": [ { "itemId": "material_wood", "count": 1 } ] }""")]  // 缺 outputItemId
    [InlineData("""{ "id": "recipe_scarecrow", "category": "Decor", "outputItemId": "craft_scarecrow", "ingredients": [ { "itemId": "material_wood", "count": 1 } ] }""")]  // 缺 outputCount
    [InlineData("""{ "id": "recipe_scarecrow", "category": "Decor", "outputItemId": "craft_scarecrow", "outputCount": 1 }""")]   // 缺 ingredients
    [InlineData("""{ "id": "recipe_scarecrow", "category": "Decor", "outputItemId": "craft_scarecrow", "outputCount": 1, "ingredients": [ { "count": 1 } ] }""")]  // 材料缺 itemId
    [InlineData("""{ "id": "recipe_scarecrow", "category": "Decor", "outputItemId": "craft_scarecrow", "outputCount": 1, "ingredients": [ { "itemId": "material_wood" } ] }""")]  // 材料缺 count
    public void 加载_缺字段时报错(string entry)
    {
        Assert.Throws<InvalidDataException>(() => RecipeTable.FromJson(JsonWith(entry), Items));
    }

    [Fact]
    public void 加载_没有_recipes_数组时报错()
    {
        Assert.Throws<InvalidDataException>(() => RecipeTable.FromJson("""{ "配方": [] }""", Items));
    }

    [Fact]
    public void 加载_传入空物品表时报错()
    {
        // 交叉校验是本表的必要步骤，没有物品表根本校验不了，不能悄悄跳过
        Assert.Throws<ArgumentNullException>(() => RecipeTable.FromJson(ValidJson, null!));
    }

    [Fact]
    public void 交叉校验_产物不在物品表里时报错且消息里带那个_id()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(outputItemId: "craft_ghost")), Items));

        Assert.Contains("craft_ghost", error.Message);
    }

    [Fact]
    public void 交叉校验_材料不在物品表里时报错且消息里带那个_id()
    {
        const string ghost = """[ { "itemId": "material_ghost", "count": 1 } ]""";

        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(ingredients: ghost)), Items));

        Assert.Contains("material_ghost", error.Message);
    }

    [Fact]
    public void 交叉校验_产物与材料都对不上时_一次把两边都报出来()
    {
        // 一条一条修比每次重跑才发现下一条快得多
        const string ghost = """[ { "itemId": "material_ghost", "count": 1 } ]""";

        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(outputItemId: "craft_ghost", ingredients: ghost)), Items));

        Assert.Contains("craft_ghost", error.Message);
        Assert.Contains("material_ghost", error.Message);
    }

    // ——— 以下是针对缺省数据文件本身的用例：抄错了、多录了都要有人发现 ———

    private static RecipeTable DefaultTable() => RecipeTable.LoadDefault(ItemTable.LoadDefault());

    /// <summary>材料数量逐条比对：多录一样、少录一样、数量抄错，三种都要红。</summary>
    private static void AssertIngredients(RecipeDefinition recipe, string spec)
    {
        // spec 形如 "material_wood:50,material_coal:1"
        string[] expectedIds = spec.Split(',')
            .Select(pair => pair.Split(':')[0])
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expectedIds,
            recipe.Ingredients.Select(ingredient => ingredient.ItemId)
                              .OrderBy(id => id, StringComparer.Ordinal)
                              .ToArray());

        foreach (string pair in spec.Split(','))
        {
            string[] parts = pair.Split(':');
            RecipeIngredient ingredient = recipe.Ingredients.Single(item => item.ItemId == parts[0]);

            Assert.Equal(int.Parse(parts[1], CultureInfo.InvariantCulture), ingredient.Count);
        }
    }

    [Fact]
    public void 缺省数据文件_五条示例配方与_12_3_逐条一致()
    {
        // 出处：docs/public/design.md 第 1345-1353 行。材料数量照抄，一个数都没编
        var expected = new (string RecipeId, string OutputItemId, RecipeCategory Category, string Ingredients)[]
        {
            ("recipe_sprinkler",              "craft_sprinkler",              RecipeCategory.Device,     "material_copper_ingot:1,material_iron_ingot:1"),
            ("recipe_scarecrow",              "craft_scarecrow",              RecipeCategory.Decor,      "material_wood:50,material_coal:1"),
            ("recipe_cherry_bomb",            "craft_cherry_bomb",            RecipeCategory.Consumable, "material_copper_ore:4,material_coal:1"),
            ("recipe_foundation_pill",        "craft_foundation_pill",        RecipeCategory.Pill,       "crop_spirit_grass:3,material_spirit_spring_water:1"),
            ("recipe_spirit_gathering_array", "craft_spirit_gathering_array", RecipeCategory.Formation,  "material_spirit_stone:100,crop_spirit_grass:20,crop_gold_spirit_fruit:5"),
        };

        RecipeTable table = DefaultTable();

        foreach ((string recipeId, string outputItemId, RecipeCategory category, string ingredients) in expected)
        {
            RecipeDefinition recipe = table.Get(recipeId);

            Assert.Equal(outputItemId, recipe.OutputItemId);
            Assert.Equal(category, recipe.Category);
            Assert.Equal(1, recipe.OutputCount);   // 一次做一件：文档没给产出数量

            AssertIngredients(recipe, ingredients);
        }
    }

    [Fact]
    public void 缺省数据文件_三道料理与_12_4_逐条一致()
    {
        // 出处：docs/public/design.md 第 1364-1368 行。三道料理的材料与数量照抄，一个数都没编；
        // 三道之外那「100+ 料理」文档只给了总数、没给名字，一条都不许有
        var expected = new (string RecipeId, string OutputItemId, string Ingredients)[]
        {
            ("recipe_fried_egg",         "craft_fried_egg",         "food_egg:1"),
            ("recipe_pumpkin_pie",       "craft_pumpkin_pie",       "crop_pumpkin:1,material_flour:1,food_milk:1,material_sugar:1"),
            ("recipe_spirit_grass_soup", "craft_spirit_grass_soup", "crop_spirit_grass:2,material_spirit_spring_water:1"),
        };

        RecipeTable table = DefaultTable();

        foreach ((string recipeId, string outputItemId, string ingredients) in expected)
        {
            RecipeDefinition recipe = table.Get(recipeId);

            Assert.Equal(outputItemId, recipe.OutputItemId);
            Assert.Equal(1, recipe.OutputCount);

            // 料理归 Consumable：§12.3 那九个分类名里没有「料理」，不新造文档里没有的分类名
            Assert.Equal(RecipeCategory.Consumable, recipe.Category);

            AssertIngredients(recipe, ingredients);
        }
    }

    [Fact]
    public void 缺省数据文件_只录文档点名的八条_不多一条()
    {
        // 五条制作（§12.3）+ 三道料理（§12.4）。§12.4 那句「100+ 料理」的其余部分没有名字，
        // 照分类名或照直觉补出来的第九条会在这里红
        RecipeTable table = DefaultTable();

        Assert.Equal(8, table.All.Count);
    }

    [Fact]
    public void 缺省数据文件_本模块新增的十件物品都在物品表里且有名字()
    {
        // 定义在 data/items/crafting.json（本模块自己的物品文件），会被并进同一张物品表
        ItemTable items = ItemTable.LoadDefault();

        var expected = new (string ItemId, ItemCategory Category, string Name)[]
        {
            // §12.3 五条配方的产物
            ("craft_sprinkler", ItemCategory.Equipment, "洒水器"),
            ("craft_scarecrow", ItemCategory.Decor, "稻草人"),
            ("craft_cherry_bomb", ItemCategory.Misc, "樱桃炸弹"),
            ("craft_foundation_pill", ItemCategory.Medicine, "筑基丹"),
            ("craft_spirit_gathering_array", ItemCategory.Misc, "聚灵阵"),

            // §12.4 南瓜派要用、文档里只出现过名字的两种材料
            ("material_flour", ItemCategory.Material, "小麦粉"),
            ("material_sugar", ItemCategory.Material, "糖"),

            // §12.4 点名的三道料理
            ("craft_fried_egg", ItemCategory.Food, "煎蛋"),
            ("craft_pumpkin_pie", ItemCategory.Food, "南瓜派"),
            ("craft_spirit_grass_soup", ItemCategory.Food, "灵芽羹"),
        };

        foreach ((string itemId, ItemCategory category, string name) in expected)
        {
            Assert.Equal(name, items.Get(itemId).Name);
            Assert.Equal(category, items.Get(itemId).Category);
        }
    }

    [Fact]
    public void 缺省数据文件_材料正好是这十四样_且分类对得上()
    {
        // 材料归 items.json / ranching.json，本模块只引用不重定义（小麦粉与糖是文档点过名的例外）。
        // 张冠李戴（比如把「灵泉水」写成一件装备）也该被发现
        ItemTable items = ItemTable.LoadDefault();
        RecipeTable table = RecipeTable.LoadDefault(items);

        string[] materials = table.All
            .SelectMany(recipe => recipe.Ingredients)
            .Select(ingredient => ingredient.ItemId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "crop_gold_spirit_fruit", "crop_pumpkin", "crop_spirit_grass", "food_egg", "food_milk",
                    "material_coal", "material_copper_ingot", "material_copper_ore", "material_flour",
                    "material_iron_ingot", "material_spirit_spring_water", "material_spirit_stone",
                    "material_sugar", "material_wood" },
            materials);

        // 灵植（灵芽草、金灵果、南瓜）归 Crop、矿石锭材与小麦粉/糖归 Material（未定义项备案 #27）、
        // 鸡蛋牛奶归 Food（畜牧切片定的）
        Assert.All(materials, itemId =>
            Assert.True(items.Get(itemId).Category is ItemCategory.Material or ItemCategory.Crop or ItemCategory.Food));
    }
}
