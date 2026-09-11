using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Systems.Crafting;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 炼丹这条门槛（M3-7，§8.7）：炼丹品级是状态（进存档），「能炼什么」是派生的（不存），
/// 而「几品能炼几阶」只由品级表回答。这里守的就是这三件事的接口。
/// </summary>
/// <remarks>
/// <b>最要紧的一条是「技能不够却炼成了」</b>——它不报错、不崩，只是把一件本不该做出来的东西做出来了。
/// 所以门槛既要在 <c>CanCraft</c> 里（界面要显示灰按钮），也要在 <c>TryCraft</c> 里（脚本、Mod、
/// 将来的自动炼丹炉会直接调它），失败时还要<b>逐材料比对 + 槽位快照</b>地证明什么都没动。
/// </remarks>
public sealed class AlchemyCraftingTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "material_wood",   "name": "木材",   "description": "制作材料。",       "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_coal",   "name": "煤",     "description": "制作材料。",       "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "craft_pill",      "name": "筑基丹", "description": "二阶丹药（夹具）。", "category": "Medicine", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "craft_scarecrow", "name": "稻草人", "description": "放在田里的稻草人。", "category": "Decor",    "maxStack": 1,   "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string RecipesJson = """
    {
      "recipes": [
        { "id": "recipe_pill", "category": "Pill", "station": "AlchemyRoom", "pillTier": 2,
          "outputItemId": "craft_pill", "outputCount": 1,
          "ingredients": [ { "itemId": "material_wood", "count": 2 } ] },
        { "id": "recipe_scarecrow", "category": "Decor", "station": "Unassigned",
          "outputItemId": "craft_scarecrow", "outputCount": 1,
          "ingredients": [ { "itemId": "material_wood", "count": 50 }, { "itemId": "material_coal", "count": 1 } ] }
      ]
    }
    """;

    /// <summary>夹具品级表：一至三品，各能炼同号的一阶。三品就是这份夹具里的「远超」。</summary>
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

    private static readonly RecipeTable Recipes = RecipeTable.FromJson(RecipesJson, Items, Ranks);

    private static Inventory NewInventory(int slotCount = 4) => new(Items, slotCount);

    private static CraftingSystem NewCrafting(IInventory inventory, params string[] unlocked)
    {
        var crafting = new CraftingSystem(Recipes, inventory, Items, Ranks);

        foreach (string recipeId in unlocked) crafting.Unlock(recipeId);

        return crafting;
    }

    /// <summary>材料的账要逐样比：只比总数会放过「这个少了一个、那个多了一个」。</summary>
    private static void AssertCounts(Inventory inventory, params (string ItemId, int Count)[] expected)
    {
        foreach ((string itemId, int count) in expected) Assert.Equal(count, inventory.Count(itemId));
    }

    /// <summary>槽位快照：失败之后连布局都不许变（同 CraftingSystemTests 的写法）。</summary>
    private static (string ItemId, int Count)[] Snapshot(IInventory inventory) =>
        inventory.Slots.Select(slot => (slot.ItemId, slot.Count)).ToArray();

    // ——— 起点与查询 ———

    [Fact]
    public void 新档的炼丹品级_是品级表的最低一档()
    {
        // 文档没写玩家从几品起（§4.6 的初始资源里也没有丹药），取最低那一档：
        // 同一档从表里问出来，而不是在代码里写个 1
        CraftingSystem crafting = NewCrafting(NewInventory());

        Assert.Equal(1, crafting.AlchemyRank);
        Assert.Equal(Ranks.All[0].Rank, crafting.AlchemyRank);
    }

    [Fact]
    public void 要几品才能炼_丹药由品级表回答_非丹药没有门槛()
    {
        CraftingSystem crafting = NewCrafting(NewInventory());

        // 「二阶丹药 ⇒ 二品炼丹师」这句只写在品级表里（§8.7：一品炼丹学徒 可炼制 一阶）
        Assert.Equal(2, crafting.RequiredAlchemyRank("recipe_pill"));

        // 稻草人不吃这一套：null 是「没有品级门槛」，不是「要零品」
        Assert.Null(crafting.RequiredAlchemyRank("recipe_scarecrow"));
    }

    [Fact]
    public void 要几品才能炼_未知配方抛_KeyNotFoundException()
    {
        CraftingSystem crafting = NewCrafting(NewInventory());

        Assert.Throws<KeyNotFoundException>(() => crafting.RequiredAlchemyRank("recipe_ghost"));
    }

    [Fact]
    public void 设定品级_重复设同一个值返回_false()
    {
        // 存档往返、任务重跑都会再设一次，不是错误（同 Unlock 的约定）
        CraftingSystem crafting = NewCrafting(NewInventory());

        Assert.True(crafting.SetAlchemyRank(2));
        Assert.False(crafting.SetAlchemyRank(2));
        Assert.Equal(2, crafting.AlchemyRank);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]      // 夹具表到三品为止：四品是「表里没有的品级」，不是「等级不够」
    public void 设定品级_品级表里没有的值当场抛(int rank)
    {
        // 夹逼或忽略都会让「设成 0 之后谁也炼不动」这种状态没人发现
        CraftingSystem crafting = NewCrafting(NewInventory());

        Assert.Throws<ArgumentOutOfRangeException>(() => crafting.SetAlchemyRank(rank));
        Assert.Equal(1, crafting.AlchemyRank);   // 抛了就不许改掉原值
    }

    // ——— 门槛：差一品 / 刚好 / 远超 ———

    [Fact]
    public void 门槛_差一品时炼不成_且材料一件不少()
    {
        Inventory inventory = NewInventory();
        inventory.Add("material_wood", 10);
        var crafting = new CraftingSystem(Recipes, inventory, Items, Ranks);
        crafting.Unlock("recipe_pill");

        (string ItemId, int Count)[] before = Snapshot(inventory);

        // 一品炼丹学徒只能炼一阶，这条是二阶（§8.7）——「技能不够却炼成了」是静默错，
        // 所以 CanCraft 与 TryCraft 两条路都要挡住
        Assert.False(crafting.CanCraft("recipe_pill"));
        Assert.False(crafting.TryCraft("recipe_pill"));

        Assert.Equal(before, Snapshot(inventory));   // 槽位布局都不许变
        AssertCounts(inventory, ("material_wood", 10), ("craft_pill", 0));
    }

    [Fact]
    public void 门槛_刚好够那一品就炼得成()
    {
        Inventory inventory = NewInventory();
        inventory.Add("material_wood", 10);
        CraftingSystem crafting = NewCrafting(inventory, "recipe_pill");
        crafting.SetAlchemyRank(2);

        Assert.True(crafting.CanCraft("recipe_pill"));
        Assert.True(crafting.TryCraft("recipe_pill"));

        AssertCounts(inventory, ("material_wood", 8), ("craft_pill", 1));
    }

    [Fact]
    public void 门槛_品级远超那一档照样炼得成()
    {
        // 高品级炼低阶丹药不该被挡住：门槛是「够不够」，不是「正好等于」
        Inventory inventory = NewInventory();
        inventory.Add("material_wood", 10);
        CraftingSystem crafting = NewCrafting(inventory, "recipe_pill");
        crafting.SetAlchemyRank(3);

        Assert.True(crafting.TryCraft("recipe_pill"));

        AssertCounts(inventory, ("material_wood", 8), ("craft_pill", 1));
    }

    [Fact]
    public void 门槛_不吃品级的配方在一品时也炼得成()
    {
        // 门槛只落在丹药上：稻草人（Decor）在一品炼丹学徒手里也该做得出来
        Inventory inventory = NewInventory();
        inventory.Add("material_wood", 60);
        inventory.Add("material_coal", 2);
        CraftingSystem crafting = NewCrafting(inventory, "recipe_scarecrow");

        Assert.Equal(1, crafting.AlchemyRank);
        Assert.True(crafting.CanCraft("recipe_scarecrow"));
        Assert.True(crafting.TryCraft("recipe_scarecrow"));

        AssertCounts(inventory, ("craft_scarecrow", 1));
    }

    // ——— 跨模块：炼丹房做筑基丹，走的就是既有那条 TryCraft ———

    [Fact]
    public void 跨模块_炼丹房做筑基丹_走既有_TryCraft_那条路()
    {
        // 缺省数据（§12.3 的配方 + §8.7 的品级表），不新开一条「炼丹」专用的路：
        // 站在炼丹房这件事今天还没有载体（那三座建筑都不存在），但配方归属与品级门槛都已经是数据了
        ItemTable items = ItemTable.LoadDefault();
        AlchemyRankTable ranks = AlchemyRankTable.LoadDefault();
        RecipeTable recipes = RecipeTable.LoadDefault(items, ranks);

        RecipeDefinition pill = recipes.Get("recipe_foundation_pill");
        Assert.Equal(CraftingStation.AlchemyRoom, pill.Station);
        Assert.Equal(2, pill.PillTier);   // §8.7 的丹药表：筑基丹是二阶

        var inventory = new Inventory(items, slotCount: 8);
        foreach (RecipeIngredient ingredient in pill.Ingredients)
            Assert.Equal(0, inventory.Add(ingredient.ItemId, ingredient.Count));

        var crafting = new CraftingSystem(recipes, inventory, items, ranks);
        crafting.Unlock(pill.Id);

        // 一品炼丹学徒炼不了二阶丹：材料一件都不许动
        Assert.False(crafting.TryCraft(pill.Id));
        foreach (RecipeIngredient ingredient in pill.Ingredients)
            Assert.Equal(ingredient.Count, inventory.Count(ingredient.ItemId));

        // 二品（= 品级表给二阶丹药开的那一品）就能炼：
        Assert.Equal(2, crafting.RequiredAlchemyRank(pill.Id));
        crafting.SetAlchemyRank(2);

        Assert.True(crafting.TryCraft(pill.Id));
        foreach (RecipeIngredient ingredient in pill.Ingredients)
            Assert.Equal(0, inventory.Count(ingredient.ItemId));

        Assert.Equal(1, inventory.Count(pill.OutputItemId));
    }

    // ——— 存档 ———

    [Fact]
    public void 存档往返_解锁表与炼丹品级都读回()
    {
        CraftingSystem original = NewCrafting(NewInventory());
        original.Unlock("recipe_pill");
        original.SetAlchemyRank(3);

        CraftingSystem restored = NewCrafting(NewInventory());
        restored.Deserialize(original.Serialize(), fromVersion: 2);

        Assert.Equal(new[] { "recipe_pill" }, restored.UnlockedRecipes.ToArray());
        Assert.Equal(3, restored.AlchemyRank);
    }

    [Fact]
    public void 存档_版本1的旧档没有品级这一列_读成最低一档()
    {
        // Version 1 的档写在品级诞生之前（M2 的加工切片）：那时没有这一列，不是坏档。读成最低一档
        CraftingSystem crafting = NewCrafting(NewInventory());
        crafting.SetAlchemyRank(3);

        crafting.Deserialize("""{ "UnlockedRecipes": [ "recipe_pill" ] }""", fromVersion: 1);

        Assert.Equal(new[] { "recipe_pill" }, crafting.UnlockedRecipes.ToArray());
        Assert.Equal(1, crafting.AlchemyRank);
    }

    [Fact]
    public void 存档_版本2起缺品级这一列是坏档_不猜成起点()
    {
        // 「这一列不在」与「这一列是 1」是两件事：猜着读会把「存档被截断 / 被手改过」
        // 变成「玩家掉了品级」，而两者在游戏里长得一模一样
        CraftingSystem crafting = NewCrafting(NewInventory());

        Assert.Throws<InvalidDataException>(
            () => crafting.Deserialize("""{ "UnlockedRecipes": [] }""", fromVersion: 2));
    }

    [Theory]
    [InlineData("""{ "UnlockedRecipes": [], "AlchemyRank": 0 }""")]    // 0 品不在表里
    [InlineData("""{ "UnlockedRecipes": [], "AlchemyRank": 4 }""")]    // 表到三品为止
    [InlineData("""{ "UnlockedRecipes": [], "AlchemyRank": null }""")] // 明确写了 null
    public void 存档_品级不合法时报错(string json)
    {
        CraftingSystem crafting = NewCrafting(NewInventory());

        Assert.Throws<InvalidDataException>(() => crafting.Deserialize(json, fromVersion: 2));
    }

    [Fact]
    public void 存档_坏档不会改掉现有的品级()
    {
        // 先整份校验再落盘（同解锁表那条）：坏存档不该让状态停在「读了一半」
        CraftingSystem crafting = NewCrafting(NewInventory());
        crafting.SetAlchemyRank(3);

        Assert.Throws<InvalidDataException>(
            () => crafting.Deserialize(
                """{ "UnlockedRecipes": [ "recipe_ghost" ], "AlchemyRank": 2 }""", fromVersion: 2));

        Assert.Equal(3, crafting.AlchemyRank);
    }

    [Fact]
    public void 存档只存品级那一个数_能炼到几阶不存()
    {
        // 「能炼什么」是品级表的输出，存进来就是同一个事实两处：表一改（比如三品改成能炼四阶），
        // 旧存档里那份就悄悄过时了
        CraftingSystem crafting = NewCrafting(NewInventory());

        string json = crafting.Serialize();

        Assert.Contains("AlchemyRank", json);
        Assert.DoesNotContain("Tier", json);
    }

    /// <summary>
    /// <b>刻意的否定式决定：本切片不做成功率</b>。§8.2 那句「一品筑基丹成功率约 30%，三品约 60%，
    /// 五品约 90%」说的是<b>筑基突破</b>的成功率，而且「一品/三品/五品」这套说法与 §8.7 的四品质
    /// （下品/中品/上品/极品）对不上——五品在四品质里没有位置。炼丹那一侧文档只给了「成功率增加」
    /// 的加成（§9.1/§9.3），一个基数都没给，所以一个数都不许编。等用户裁决后再加成员，那时删掉这条。
    /// </summary>
    [Fact]
    public void 刻意不做_制作的接口上没有任何成功率入口()
    {
        Assert.DoesNotContain(typeof(ICraftingSystem).GetMembers(), member => member.Name.Contains("Success"));
        Assert.DoesNotContain(typeof(ICraftingSystem).GetMembers(), member => member.Name.Contains("Chance"));

        Assert.DoesNotContain(typeof(RecipeDefinition).GetMembers(), member => member.Name.Contains("Success"));
        Assert.DoesNotContain(typeof(RecipeDefinition).GetMembers(), member => member.Name.Contains("Chance"));
    }
}
