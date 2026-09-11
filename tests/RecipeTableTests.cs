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

    /// <summary>
    /// 交叉校验用的品级表（夹具）。只有三品、且只到三阶：够测「丹药阶数要落在炼得出来的范围里」，
    /// 又多留了一档**炼不出来**的边界（四阶）给坏数据用例用。
    /// </summary>
    private const string RanksJson = """
    {
      "ranks": [
        { "rank": 1, "name": "一品炼丹学徒", "maxTier": 1 },
        { "rank": 2, "name": "二品炼丹师",   "maxTier": 2 },
        { "rank": 3, "name": "三品炼丹大师", "maxTier": 3 }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);

    private static readonly AlchemyRankTable Ranks = AlchemyRankTable.FromJson(RanksJson);

    private static readonly RecipeTable Table = RecipeTable.FromJson(ValidJson, Items, Ranks);

    /// <summary>把条目拼成一份表。坏数据用例只改一个字段，不必每次抄一整份 JSON。</summary>
    private static string JsonWith(params string[] entries) => "{ \"recipes\": [" + string.Join(",", entries) + "] }";

    /// <summary>
    /// 一条配方条目。<paramref name="station"/> 与 <paramref name="pillTier"/> 留空表示<b>字段不在</b>
    /// ——两者都在「缺省/缺失」上有语义（制作台缺省是 Unassigned，丹药缺阶数该报错），所以这里
    /// 不能给它们默认值。
    /// </summary>
    private static string Recipe(
        string id = "recipe_scarecrow",
        string category = "Decor",
        string outputItemId = "craft_scarecrow",
        string outputCount = "1",
        string station = "",
        string pillTier = "",
        string ingredients = """[ { "itemId": "material_wood", "count": 50 }, { "itemId": "material_coal", "count": 1 } ]""") =>
        $"{{ \"id\": \"{id}\", \"category\": \"{category}\", " +
        (station.Length == 0 ? "" : $"\"station\": \"{station}\", ") +
        $"\"outputItemId\": \"{outputItemId}\", \"outputCount\": {outputCount}, " +
        (pillTier.Length == 0 ? "" : $"\"pillTier\": {pillTier}, ") +
        $"\"ingredients\": {ingredients} }}";

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
        RecipeTable table = RecipeTable.FromJson(JsonWith(Recipe(category: "dEcOr")), Items, Ranks);

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
            () => RecipeTable.FromJson(JsonWith(Recipe(), Recipe(category: "Device")), Items, Ranks));

        Assert.Contains("recipe_scarecrow", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 加载_id_为空时报错(string id)
    {
        Assert.Throws<InvalidDataException>(() => RecipeTable.FromJson(JsonWith(Recipe(id: id)), Items, Ranks));
    }

    [Fact]
    public void 加载_缺_id_时报错()
    {
        const string entry =
            """{ "category": "Decor", "outputItemId": "craft_scarecrow", "outputCount": 1, "ingredients": [ { "itemId": "material_wood", "count": 1 } ] }""";

        Assert.Throws<InvalidDataException>(() => RecipeTable.FromJson(JsonWith(entry), Items, Ranks));
    }

    [Theory]
    [InlineData("Decor", "  ")]                // 产物 id 只有空白
    [InlineData("", "craft_scarecrow")]        // 分类为空
    public void 加载_产物或分类为空时报错(string category, string outputItemId)
    {
        Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(category: category, outputItemId: outputItemId)), Items, Ranks));
    }

    [Theory]
    [InlineData("武器店")]
    [InlineData("3")]      // 数字序号：Enum.TryParse 会静默接受，序号却会随枚举插值错位（ADR-012）
    [InlineData("")]
    public void 加载_分类不是合法的_RecipeCategory_时报错(string category)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(category: category)), Items, Ranks));

        Assert.Contains("recipe_scarecrow", error.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void 加载_产出数量非正时报错且消息里带_id(string outputCount)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(outputCount: outputCount)), Items, Ranks));

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
            () => RecipeTable.FromJson(JsonWith(Recipe(ingredients: ingredients)), Items, Ranks));
    }

    [Fact]
    public void 加载_同一材料写两遍时报错()
    {
        // 同一样材料写两条时「到底要几个」取决于把哪一条算数——要两个就该写成一条、数量填 2
        const string twice = """[ { "itemId": "material_wood", "count": 50 }, { "itemId": "material_wood", "count": 5 } ]""";

        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(ingredients: twice)), Items, Ranks));

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
        Assert.Throws<InvalidDataException>(() => RecipeTable.FromJson(JsonWith(entry), Items, Ranks));
    }

    [Fact]
    public void 加载_没有_recipes_数组时报错()
    {
        Assert.Throws<InvalidDataException>(() => RecipeTable.FromJson("""{ "配方": [] }""", Items, Ranks));
    }

    [Fact]
    public void 加载_传入空物品表时报错()
    {
        // 交叉校验是本表的必要步骤，没有物品表根本校验不了，不能悄悄跳过
        Assert.Throws<ArgumentNullException>(() => RecipeTable.FromJson(ValidJson, null!, Ranks));
    }

    [Fact]
    public void 加载_传入空品级表时报错()
    {
        // 同上：没有品级表就答不出「这条丹药配方做不做得出来」，那也是交叉校验的一部分
        Assert.Throws<ArgumentNullException>(() => RecipeTable.FromJson(ValidJson, Items, null!));
    }

    [Fact]
    public void 交叉校验_产物不在物品表里时报错且消息里带那个_id()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(outputItemId: "craft_ghost")), Items, Ranks));

        Assert.Contains("craft_ghost", error.Message);
    }

    [Fact]
    public void 交叉校验_材料不在物品表里时报错且消息里带那个_id()
    {
        const string ghost = """[ { "itemId": "material_ghost", "count": 1 } ]""";

        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(ingredients: ghost)), Items, Ranks));

        Assert.Contains("material_ghost", error.Message);
    }

    [Fact]
    public void 交叉校验_产物与材料都对不上时_一次把两边都报出来()
    {
        // 一条一条修比每次重跑才发现下一条快得多
        const string ghost = """[ { "itemId": "material_ghost", "count": 1 } ]""";

        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(outputItemId: "craft_ghost", ingredients: ghost)), Items, Ranks));

        Assert.Contains("craft_ghost", error.Message);
        Assert.Contains("material_ghost", error.Message);
    }

    // ——— 制作台（§12.3 的四个台子）：能推的照文档推，推不出来的一律 Unassigned ———

    [Fact]
    public void 制作台_写出来的台子逐项落到定义上_大小写不敏感()
    {
        // 「炼丹房」这一档出自 §6.7 的「炼丹房：炼制丹药」，是八条配方里唯一推得出归属的一条
        RecipeTable table = RecipeTable.FromJson(
            JsonWith(Recipe(station: "alchemyroom")), Items, Ranks);

        Assert.Equal(CraftingStation.AlchemyRoom, table.Get("recipe_scarecrow").Station);
    }

    [Fact]
    public void 制作台_字段不在时是_Unassigned_而不是背包内制作()
    {
        // 「文档没说」与「在背包里做」是两件事。缺省成 Unassigned 是唯一不撒谎的落点
        RecipeTable table = RecipeTable.FromJson(JsonWith(Recipe()), Items, Ranks);

        Assert.Equal(CraftingStation.Unassigned, table.Get("recipe_scarecrow").Station);
    }

    [Theory]
    [InlineData("背包里")]   // 中文名（文档里的名字不是枚举名）
    [InlineData("1")]        // 数字序号：Enum.TryParse 会当序号收下，序号却会随枚举插值错位（ADR-012）
    [InlineData("Kitchen")]  // §12.4 的厨房：刻意不是制作台的一档，写进来必须被拒
    public void 加载_制作台认不出时_报错且消息里带_id(string station)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(station: station)), Items, Ranks));

        Assert.Contains("recipe_scarecrow", error.Message);
    }

    /// <summary>
    /// <b>刻意的否定式决定：制作台里没有「厨房」这一档</b>。§12.4 的烹饪是另一套系统
    /// （§17.2 里「制作」与「烹饪」是两个菜单），把厨房塞进制作台枚举会让两套系统在数据类型上先纠缠起来。
    /// 三道料理因此是 Unassigned，不是「厨房」。真要合并时，先改这条用例。
    /// </summary>
    [Fact]
    public void 刻意不做_制作台里没有厨房这一档()
    {
        Assert.DoesNotContain(Enum.GetNames<CraftingStation>(), name => name.Contains("Kitchen"));
        Assert.DoesNotContain(Enum.GetNames<CraftingStation>(), name => name.Contains("Cook"));
    }

    // ——— 丹药的阶数（§8.7）：只属于丹药，而且必须有人炼得出来 ———

    [Fact]
    public void 丹药品阶_逐项落到定义上_非丹药没有这一位()
    {
        const string pill = """[ { "itemId": "material_wood", "count": 1 } ]""";

        RecipeTable table = RecipeTable.FromJson(
            JsonWith(Recipe(category: "Pill", station: "AlchemyRoom", pillTier: "2", ingredients: pill)), Items, Ranks);

        Assert.Equal(2, table.Get("recipe_scarecrow").PillTier);
        Assert.Null(Table.Get("recipe_scarecrow").PillTier);   // 稻草人不是丹药，这一位是 null
    }

    [Fact]
    public void 加载_丹药配方缺_pillTier_时报错且消息里带_id()
    {
        // §8.7 把丹药定义成一至十二阶：没有阶数就问不出「要几品炼丹师」，那是一条没人拦得住的配方
        const string pill = """[ { "itemId": "material_wood", "count": 1 } ]""";

        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(category: "Pill", ingredients: pill)), Items, Ranks));

        Assert.Contains("recipe_scarecrow", error.Message);
    }

    [Fact]
    public void 加载_非丹药配方带_pillTier_时报错()
    {
        // 阶是丹药的刻度：一件稻草人带个「二阶」是张冠李戴，放行之后没人会在意
        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(pillTier: "2")), Items, Ranks));

        Assert.Contains("recipe_scarecrow", error.Message);
    }

    [Theory]
    [InlineData("0")]     // 零阶：§8.7 的丹药从一阶起
    [InlineData("-1")]
    [InlineData("1.5")]   // 小数：手写 JSON 常事，报错要能定位到是哪一条
    [InlineData("\"二阶\"")]   // 中文名不是数
    public void 加载_丹药品阶不是正整数时报错(string pillTier)
    {
        const string pill = """[ { "itemId": "material_wood", "count": 1 } ]""";

        Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(JsonWith(Recipe(category: "Pill", pillTier: pillTier, ingredients: pill)),
                                       Items, Ranks));
    }

    [Fact]
    public void 交叉校验_丹药阶数没有哪一品炼得出来时报错()
    {
        // 夹具品级表只到三阶：四阶丹药在今天这张表下谁也炼不出来（§8.7 的表到九阶为止），
        // 而这条配方单独看完全自洽——只有拿品级表来对才露馅
        const string pill = """[ { "itemId": "material_wood", "count": 1 } ]""";

        var error = Assert.Throws<InvalidDataException>(
            () => RecipeTable.FromJson(
                JsonWith(Recipe(id: "recipe_pill_4", category: "Pill", pillTier: "4", ingredients: pill)),
                Items, Ranks));

        Assert.Contains("recipe_pill_4", error.Message);
    }

    // ——— 以下是针对缺省数据文件本身的用例：抄错了、多录了都要有人发现 ———

    private static RecipeTable DefaultTable() =>
        RecipeTable.LoadDefault(ItemTable.LoadDefault(), AlchemyRankTable.LoadDefault());

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

        // 筑基丹是二阶：§8.7 的丹药表把「筑基丹」写在这一行（docs/public/design.md 第 947 行）。
        // 这一位决定要几品炼丹师（品级表答「二品」），所以抄错阶数会让门槛整体错一档
        Assert.Equal(2, table.Get("recipe_foundation_pill").PillTier);

        // 其余七条不是丹药，这一位必须是 null——有值就说明有人张冠李戴了
        Assert.All(
            table.All.Where(recipe => recipe.Category != RecipeCategory.Pill),
            recipe => Assert.Null(recipe.PillTier));
    }

    [Fact]
    public void 缺省数据文件_制作台只推出了一条_其余七条都是_Unassigned()
    {
        // §12.3 只列了四个台子（第 1339 行），逐配方的归属文档一个字都没给（备案 #62）。
        // 八条里只有筑基丹推得出：§6.7 的建筑功能表写着「炼丹房：炼制丹药」（第 362 行）。
        // 其余七条**不许照直觉补**——推不出来是 Unassigned，不是「随便挑一个像的」：
        // 洒水器/稻草人/樱桃炸弹：§6.7 的工坊只写「杂交、制作」，而 §12.3 另有「背包内制作」，
        // 文档没给「哪些在背包里做」的界线；聚灵阵：四个台子之外的东西（§13.2 里它跟建筑并列）；
        // 三道料理：§12.4 的烹饪是另一套系统，厨房刻意不是制作台的一档。
        var expected = new (string RecipeId, CraftingStation Station)[]
        {
            ("recipe_sprinkler",              CraftingStation.Unassigned),
            ("recipe_scarecrow",              CraftingStation.Unassigned),
            ("recipe_cherry_bomb",            CraftingStation.Unassigned),
            ("recipe_foundation_pill",        CraftingStation.AlchemyRoom),
            ("recipe_spirit_gathering_array", CraftingStation.Unassigned),
            ("recipe_fried_egg",              CraftingStation.Unassigned),
            ("recipe_pumpkin_pie",            CraftingStation.Unassigned),
            ("recipe_spirit_grass_soup",      CraftingStation.Unassigned),
        };

        RecipeTable table = DefaultTable();

        Assert.Equal(8, table.All.Count);   // 别让少录一条配方从这条用例下面溜过去
        foreach ((string recipeId, CraftingStation station) in expected)
            Assert.Equal(station, table.Get(recipeId).Station);
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
        RecipeTable table = RecipeTable.LoadDefault(items, AlchemyRankTable.LoadDefault());

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
