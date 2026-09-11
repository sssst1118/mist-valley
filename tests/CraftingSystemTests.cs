using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Systems.Crafting;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 制作系统（M2 加工）。这里守的核心只有一条：<b>全有或全无</b>——没做成时材料一个都不能少。
/// 所以失败用例一律<b>逐材料比对</b>并连槽位快照一起比，不是只看总数量：只会看总数的话，
/// 「木材少了 50、煤多了一个」也能蒙混过关。
/// </summary>
public sealed class CraftingSystemTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "material_wood",       "name": "木材",   "description": "建造与制作材料。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_coal",       "name": "煤",     "description": "制作材料。",       "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_iron_ingot", "name": "铁锭",   "description": "制作材料。",       "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "craft_scarecrow",     "name": "稻草人", "description": "放在田里的稻草人。", "category": "Decor",    "maxStack": 1,   "buyPrice": 0, "sellPrice": 0 },
        { "id": "craft_sprinkler",     "name": "洒水器", "description": "放置后自动浇水。", "category": "Equipment", "maxStack": 1,   "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string RecipesJson = """
    {
      "recipes": [
        { "id": "recipe_scarecrow", "category": "Decor", "outputItemId": "craft_scarecrow", "outputCount": 1,
          "ingredients": [ { "itemId": "material_wood", "count": 50 }, { "itemId": "material_coal", "count": 1 } ] },
        { "id": "recipe_sprinkler", "category": "Device", "outputItemId": "craft_sprinkler", "outputCount": 1,
          "ingredients": [ { "itemId": "material_iron_ingot", "count": 2 } ] }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);

    private static readonly RecipeTable Recipes = RecipeTable.FromJson(RecipesJson, Items);

    private static Inventory NewInventory(int slotCount = 4) => new(Items, slotCount);

    private static CraftingSystem NewCrafting(IInventory inventory, params string[] unlocked)
    {
        var crafting = new CraftingSystem(Recipes, inventory, Items);

        foreach (string recipeId in unlocked) crafting.Unlock(recipeId);

        return crafting;
    }

    /// <summary>逐材料比对：只比总数会放过「这个少了一个、那个多了一个」。</summary>
    private static void AssertCounts(Inventory inventory, params (string ItemId, int Count)[] expected)
    {
        foreach ((string itemId, int count) in expected) Assert.Equal(count, inventory.Count(itemId));
    }

    /// <summary>槽位快照逐格比：数量对了但物品在槽位之间搬了家，也算「动了背包」。</summary>
    private static ItemStack[] Snapshot(Inventory inventory) => inventory.Slots.ToArray();

    // ——— 能不能做 ———

    [Fact]
    public void 未解锁时_材料再齐也做不了_且一个材料都没扣()
    {
        Inventory inventory = NewInventory();
        inventory.Add("material_wood", 50);
        inventory.Add("material_coal", 1);

        CraftingSystem crafting = NewCrafting(inventory);   // 一条都没解锁
        ItemStack[] before = Snapshot(inventory);

        Assert.False(crafting.IsUnlocked("recipe_scarecrow"));
        Assert.False(crafting.CanCraft("recipe_scarecrow"));   // 未解锁返回 false，不抛
        Assert.False(crafting.TryCraft("recipe_scarecrow"));

        Assert.Equal(before, Snapshot(inventory));
        AssertCounts(inventory, ("material_wood", 50), ("material_coal", 1), ("craft_scarecrow", 0));
    }

    [Fact]
    public void 未知配方_四个入口都抛_且消息里带_id()
    {
        // 凭空问一个配方表里没有的 id 是编程错误，不是运行时状况（同 Inventory.Add 取不到物品）
        CraftingSystem crafting = NewCrafting(NewInventory());

        var isUnlocked = Assert.Throws<KeyNotFoundException>(() => crafting.IsUnlocked("recipe_ghost"));
        Assert.Contains("recipe_ghost", isUnlocked.Message);

        Assert.Throws<KeyNotFoundException>(() => crafting.Unlock("recipe_ghost"));
        Assert.Throws<KeyNotFoundException>(() => crafting.CanCraft("recipe_ghost"));
        Assert.Throws<KeyNotFoundException>(() => crafting.TryCraft("recipe_ghost"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 份数非正时抛(int count)
    {
        CraftingSystem crafting = NewCrafting(NewInventory(), "recipe_scarecrow");

        Assert.Throws<ArgumentOutOfRangeException>(() => crafting.CanCraft("recipe_scarecrow", count));
        Assert.Throws<ArgumentOutOfRangeException>(() => crafting.TryCraft("recipe_scarecrow", count));
    }

    // ——— 做成了 ———

    [Fact]
    public void 材料齐时_制作成功_材料与成品同时变化()
    {
        Inventory inventory = NewInventory();
        inventory.Add("material_wood", 50);
        inventory.Add("material_coal", 1);

        CraftingSystem crafting = NewCrafting(inventory, "recipe_scarecrow");

        Assert.True(crafting.CanCraft("recipe_scarecrow"));
        Assert.True(crafting.TryCraft("recipe_scarecrow"));

        AssertCounts(inventory, ("material_wood", 0), ("material_coal", 0), ("craft_scarecrow", 1));
    }

    [Fact]
    public void 材料刚好够时_成功_且正好扣到零()
    {
        // 差一个就该失败、多一个也是刚好——边界上最容易写成「>」而不是「>=」
        Inventory inventory = NewInventory();
        inventory.Add("material_wood", 50);
        inventory.Add("material_coal", 1);

        Assert.True(NewCrafting(inventory, "recipe_scarecrow").TryCraft("recipe_scarecrow"));

        AssertCounts(inventory, ("material_wood", 0), ("material_coal", 0), ("craft_scarecrow", 1));
    }

    [Fact]
    public void 材料跨多个堆叠时_照样扣得干净()
    {
        // 堆叠上限在测试数据里压小，才能让一样材料跨格（同 InventoryTests 的做法）
        const string smallStackItems = """
        {
          "items": [
            { "id": "material_wood",   "name": "木材",   "description": "建造与制作材料。", "category": "Material", "maxStack": 30, "buyPrice": 0, "sellPrice": 0 },
            { "id": "material_coal",   "name": "煤",     "description": "制作材料。",       "category": "Material", "maxStack": 30, "buyPrice": 0, "sellPrice": 0 },
            { "id": "craft_scarecrow", "name": "稻草人", "description": "放在田里的稻草人。", "category": "Decor",    "maxStack": 1,  "buyPrice": 0, "sellPrice": 0 }
          ]
        }
        """;

        const string scarecrowOnlyRecipes = """
        {
          "recipes": [
            { "id": "recipe_scarecrow", "category": "Decor", "outputItemId": "craft_scarecrow", "outputCount": 1,
              "ingredients": [ { "itemId": "material_wood", "count": 50 }, { "itemId": "material_coal", "count": 1 } ] }
          ]
        }
        """;

        ItemTable items = ItemTable.FromJson(smallStackItems);
        var inventory = new Inventory(items, slotCount: 6);
        inventory.Add("material_wood", 61);   // 占三格：30 + 30 + 1
        inventory.Add("material_coal", 2);    // 占一格

        var crafting = new CraftingSystem(RecipeTable.FromJson(scarecrowOnlyRecipes, items), inventory, items);
        crafting.Unlock("recipe_scarecrow");

        Assert.True(crafting.TryCraft("recipe_scarecrow"));

        AssertCounts(inventory, ("material_wood", 11), ("material_coal", 1), ("craft_scarecrow", 1));
    }

    // ——— 没做成：一个材料都不许扣 ———

    [Theory]
    [InlineData(49, 1)]     // 木材差一个
    [InlineData(999, 0)]    // 木材管够、煤一个都没有——正是「扣到一半才发现不够」的那一条
    [InlineData(0, 999)]    // 反过来
    public void 材料不足时_逐材料比对确认一个都没扣(int wood, int coal)
    {
        Inventory inventory = NewInventory();
        if (wood > 0) inventory.Add("material_wood", wood);   // Add 不收 0：零个就当没放
        if (coal > 0) inventory.Add("material_coal", coal);

        CraftingSystem crafting = NewCrafting(inventory, "recipe_scarecrow");
        ItemStack[] before = Snapshot(inventory);

        Assert.False(crafting.CanCraft("recipe_scarecrow"));
        Assert.False(crafting.TryCraft("recipe_scarecrow"));

        Assert.Equal(before, Snapshot(inventory));
        AssertCounts(inventory,
            ("material_wood", wood), ("material_coal", coal), ("craft_scarecrow", 0));
    }

    [Fact]
    public void 多做一个不够时_连一个都不做()
    {
        // 材料只够一份：做两份就得一份都不做，而不是「先做一份、第二份失败」
        Inventory inventory = NewInventory();
        inventory.Add("material_wood", 99);
        inventory.Add("material_coal", 2);

        CraftingSystem crafting = NewCrafting(inventory, "recipe_scarecrow");

        Assert.True(crafting.CanCraft("recipe_scarecrow", 1));
        Assert.False(crafting.CanCraft("recipe_scarecrow", 2));

        Assert.False(crafting.TryCraft("recipe_scarecrow", 2));

        AssertCounts(inventory, ("material_wood", 99), ("material_coal", 2), ("craft_scarecrow", 0));
    }

    [Fact]
    public void 多做一份_材料正好翻倍时_一次做出两个()
    {
        Inventory inventory = NewInventory();
        inventory.Add("material_wood", 100);
        inventory.Add("material_coal", 2);

        Assert.True(NewCrafting(inventory, "recipe_scarecrow").TryCraft("recipe_scarecrow", 2));

        AssertCounts(inventory, ("material_wood", 0), ("material_coal", 0), ("craft_scarecrow", 2));
    }

    [Fact]
    public void 背包放不下成品时_一个材料都不扣()
    {
        // 两格全占满，没有空格也没有同种堆叠——成品进不去。材料扣了而成品丢失，是玩家主动点
        // 「制作」时的白扔，比收获溢出更该避免
        Inventory inventory = NewInventory(slotCount: 2);
        inventory.Add("material_wood", 999);
        inventory.Add("material_coal", 999);

        CraftingSystem crafting = NewCrafting(inventory, "recipe_scarecrow");
        ItemStack[] before = Snapshot(inventory);

        Assert.False(crafting.CanCraft("recipe_scarecrow"));
        Assert.False(crafting.TryCraft("recipe_scarecrow"));

        Assert.Equal(before, Snapshot(inventory));
        AssertCounts(inventory, ("material_wood", 999), ("material_coal", 999), ("craft_scarecrow", 0));
    }

    [Fact]
    public void 背包多一个空格_同样的材料就能做()
    {
        // 与上一条只差一格：证明卡住的确实是容量，不是材料
        Inventory inventory = NewInventory(slotCount: 3);
        inventory.Add("material_wood", 999);
        inventory.Add("material_coal", 1);

        Assert.True(NewCrafting(inventory, "recipe_scarecrow").TryCraft("recipe_scarecrow"));

        AssertCounts(inventory, ("material_wood", 949), ("material_coal", 0), ("craft_scarecrow", 1));
    }

    [Fact]
    public void 材料只占一格时_成品进另一个空格()
    {
        Inventory inventory = NewInventory(slotCount: 2);
        inventory.Add("material_iron_ingot", 6);

        Assert.True(NewCrafting(inventory, "recipe_sprinkler").TryCraft("recipe_sprinkler"));

        AssertCounts(inventory, ("material_iron_ingot", 4), ("craft_sprinkler", 1));
    }

    [Fact]
    public void 容量按堆叠上限算_没装满的堆叠也算空间()
    {
        // 背包里没有空格，但有一格只装了 4 个稻草人（上限 5）——再做一份补进去正好装满。
        // 若把「能不能放」写成「有没有空格」，这一条就会红
        const string stackableItems = """
        {
          "items": [
            { "id": "material_wood",   "name": "木材",   "description": "建造与制作材料。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
            { "id": "craft_scarecrow", "name": "稻草人", "description": "放在田里的稻草人。", "category": "Decor",    "maxStack": 5,   "buyPrice": 0, "sellPrice": 0 }
          ]
        }
        """;

        const string recipes = """
        {
          "recipes": [
            { "id": "recipe_scarecrow", "category": "Decor", "outputItemId": "craft_scarecrow", "outputCount": 1,
              "ingredients": [ { "itemId": "material_wood", "count": 50 } ] }
          ]
        }
        """;

        ItemTable items = ItemTable.FromJson(stackableItems);
        var inventory = new Inventory(items, slotCount: 2);
        inventory.Add("material_wood", 999);   // 占满第一格
        inventory.Add("craft_scarecrow", 4);   // 第二格只装了 4 个

        var crafting = new CraftingSystem(RecipeTable.FromJson(recipes, items), inventory, items);
        crafting.Unlock("recipe_scarecrow");

        Assert.True(crafting.TryCraft("recipe_scarecrow"));

        AssertCounts(inventory, ("material_wood", 949), ("craft_scarecrow", 5));
    }

    // ——— 解锁 ———

    [Fact]
    public void 重复解锁_返回_false_且解锁表不变()
    {
        CraftingSystem crafting = NewCrafting(NewInventory());

        Assert.True(crafting.Unlock("recipe_scarecrow"));
        Assert.False(crafting.Unlock("recipe_scarecrow"));   // 集合没变，但不是错误

        Assert.Equal(new[] { "recipe_scarecrow" }, crafting.UnlockedRecipes.ToArray());
    }

    [Fact]
    public void 解锁表_按_Ordinal_升序返回()
    {
        CraftingSystem crafting = NewCrafting(NewInventory());

        crafting.Unlock("recipe_sprinkler");
        crafting.Unlock("recipe_scarecrow");

        Assert.Equal(new[] { "recipe_scarecrow", "recipe_sprinkler" }, crafting.UnlockedRecipes.ToArray());
    }

    [Fact]
    public void 构造_依赖为_null_时抛()
    {
        Assert.Throws<ArgumentNullException>(() => new CraftingSystem(null!, NewInventory(), Items));
        Assert.Throws<ArgumentNullException>(() => new CraftingSystem(Recipes, null!, Items));
        Assert.Throws<ArgumentNullException>(() => new CraftingSystem(Recipes, NewInventory(), null!));
    }

    // ——— 存档 ———

    [Fact]
    public void 存档往返_解锁的配方原样读回()
    {
        CraftingSystem original = NewCrafting(NewInventory());
        original.Unlock("recipe_scarecrow");
        original.Unlock("recipe_sprinkler");

        CraftingSystem restored = NewCrafting(NewInventory());
        restored.Deserialize(original.Serialize(), fromVersion: 1);

        Assert.Equal(original.UnlockedRecipes.ToArray(), restored.UnlockedRecipes.ToArray());
        Assert.True(restored.IsUnlocked("recipe_scarecrow"));
    }

    [Fact]
    public void 存档_配方按_id_存_而不是配方表里的序号()
    {
        CraftingSystem crafting = NewCrafting(NewInventory());
        crafting.Unlock("recipe_scarecrow");

        string json = crafting.Serialize();
        Assert.Contains("recipe_scarecrow", json);

        // 配方表换了顺序也读得回来：存序号的话这里会整体错位（ADR-012 枚举序列化成名字的理由）
        const string reorderedRecipes = """
        {
          "recipes": [
            { "id": "recipe_sprinkler", "category": "Device", "outputItemId": "craft_sprinkler", "outputCount": 1,
              "ingredients": [ { "itemId": "material_iron_ingot", "count": 2 } ] },
            { "id": "recipe_scarecrow", "category": "Decor", "outputItemId": "craft_scarecrow", "outputCount": 1,
              "ingredients": [ { "itemId": "material_wood", "count": 50 }, { "itemId": "material_coal", "count": 1 } ] }
          ]
        }
        """;

        var restored = new CraftingSystem(RecipeTable.FromJson(reorderedRecipes, Items), NewInventory(), Items);
        restored.Deserialize(json, fromVersion: 1);

        Assert.Equal(new[] { "recipe_scarecrow" }, restored.UnlockedRecipes.ToArray());
    }

    [Fact]
    public void 读档是整状态覆盖_不是往现有解锁表里加()
    {
        CraftingSystem crafting = NewCrafting(NewInventory());
        crafting.Unlock("recipe_scarecrow");
        crafting.Unlock("recipe_sprinkler");

        // 存档里只有洒水器：读回来之后旧的「稻草人」不该还在
        const string saved = """{ "UnlockedRecipes": [ "recipe_sprinkler" ] }""";
        crafting.Deserialize(saved, fromVersion: 1);

        Assert.Equal(new[] { "recipe_sprinkler" }, crafting.UnlockedRecipes.ToArray());
    }

    [Fact]
    public void 存档往返_空解锁表也读得回空()
    {
        CraftingSystem restored = NewCrafting(NewInventory());
        restored.Unlock("recipe_scarecrow");

        restored.Deserialize(NewCrafting(NewInventory()).Serialize(), fromVersion: 1);

        Assert.Empty(restored.UnlockedRecipes);
    }

    [Fact]
    public void 反序列化_拒绝来自更新版本的存档()
    {
        CraftingSystem crafting = NewCrafting(NewInventory());

        Assert.Throws<NotSupportedException>(() => crafting.Deserialize(crafting.Serialize(), fromVersion: 2));
    }

    [Fact]
    public void 反序列化_配方_id_不在配方表里时报错且消息里带那个_id()
    {
        CraftingSystem crafting = NewCrafting(NewInventory());
        const string saved = """{ "UnlockedRecipes": [ "recipe_ghost" ] }""";

        var error = Assert.Throws<InvalidDataException>(() => crafting.Deserialize(saved, fromVersion: 1));

        Assert.Contains("recipe_ghost", error.Message);
    }

    [Fact]
    public void 反序列化_坏存档不会让解锁表停在读了一半的状态()
    {
        CraftingSystem crafting = NewCrafting(NewInventory());
        crafting.Unlock("recipe_sprinkler");

        // 第二条才是坏的：先整份校验再落盘，第一条不该被读进去
        const string saved = """{ "UnlockedRecipes": [ "recipe_scarecrow", "recipe_ghost" ] }""";

        Assert.Throws<InvalidDataException>(() => crafting.Deserialize(saved, fromVersion: 1));
        Assert.Equal(new[] { "recipe_sprinkler" }, crafting.UnlockedRecipes.ToArray());
    }

    [Theory]
    [InlineData("""{ "UnlockedRecipes": [ "" ] }""")]     // 空 id
    [InlineData("""{ "UnlockedRecipes": [ "  " ] }""")]   // 只有空白
    [InlineData("""{ }""")]                               // 缺数组
    [InlineData("""{ "UnlockedRecipes": null }""")]       // 数组是 null
    [InlineData("null")]                                  // 内容为空
    public void 反序列化_坏数据时报错(string json)
    {
        CraftingSystem crafting = NewCrafting(NewInventory());

        Assert.Throws<InvalidDataException>(() => crafting.Deserialize(json, fromVersion: 1));
    }

    [Fact]
    public void 反序列化_不是_JSON_时抛_JsonException()
    {
        CraftingSystem crafting = NewCrafting(NewInventory());

        Assert.Throws<System.Text.Json.JsonException>(() => crafting.Deserialize("这不是 JSON", fromVersion: 1));
    }
}
