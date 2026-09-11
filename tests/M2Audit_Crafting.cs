using System;
using XingGame.Systems.Crafting;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 加工与烹饪的对抗性审计（M2）。只补既有用例没在看的分支：
/// <b>缺省配方表里的每一条都真的能做出来</b>（既有用例用的是自己写的小份配方，
/// 缺省那八条从没被端到端做过一次），多做几份时放不下就不做，以及存档里重复配方 id 的语义。
/// </summary>
public class M2Audit_Crafting
{
    [Fact]
    public void 缺省配方每一条都能真的做出来_材料扣得一件不差()
    {
        ItemTable items = ItemTable.LoadDefault();
        AlchemyRankTable ranks = AlchemyRankTable.LoadDefault();
        RecipeTable recipes = RecipeTable.LoadDefault(items, ranks);

        Assert.NotEmpty(recipes.All);   // 空表会让下面整个循环白跑

        foreach (RecipeDefinition recipe in recipes.All)
        {
            var inventory = new Inventory(items, slotCount: 8);
            var crafting = new CraftingSystem(recipes, inventory, items, ranks);

            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                // 先确认材料真的放得进去，否则后面「材料被扣光」是假通过
                Assert.Equal(0, inventory.Add(ingredient.ItemId, ingredient.Count));
                Assert.Equal(ingredient.Count, inventory.Count(ingredient.ItemId));
            }

            Assert.True(crafting.Unlock(recipe.Id));

            // M3-7 起丹药有品级门槛（§8.7）：把这位玩家调到**刚好够炼这一条**的那一品。
            // 要不到的那一品会在这里红——「配方的门槛谁也够不着」= 一条永远做不出来的配方
            crafting.SetAlchemyRank(crafting.RequiredAlchemyRank(recipe.Id) ?? crafting.AlchemyRank);

            Assert.True(crafting.TryCraft(recipe.Id), $"缺省配方 {recipe.Id} 应该能做出来");

            int total = 0;
            foreach (ItemStack slot in inventory.Slots) total += slot.Count;

            // 守恒：进来的 = 剩下的。材料一件不剩，背包里只有成品
            foreach (RecipeIngredient ingredient in recipe.Ingredients)
                Assert.Equal(0, inventory.Count(ingredient.ItemId));

            Assert.Equal(recipe.OutputCount, inventory.Count(recipe.OutputItemId));
            Assert.Equal(recipe.OutputCount, total);
        }
    }

    private const string ItemsJson = """
    {
      "items": [
        { "id": "material_wood", "name": "木材",   "description": "审计用。", "category": "Material",  "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "craft_widget",  "name": "小装置", "description": "审计用。", "category": "Equipment", "maxStack": 10,  "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string RecipesJson = """
    {
      "recipes": [
        { "id": "recipe_widget", "category": "Device", "outputItemId": "craft_widget", "outputCount": 1,
          "ingredients": [ { "itemId": "material_wood", "count": 2 } ] }
      ]
    }
    """;

    /// <summary>夹具品级表：审计用的配方不是丹药，这张表只为满足那条交叉校验。</summary>
    private static readonly AlchemyRankTable Ranks = AlchemyRankTable.FromJson("""
    {
      "ranks": [
        { "rank": 1, "name": "一品炼丹学徒", "maxTier": 1 }
      ]
    }
    """);

    private static CraftingSystem NewSystem(out Inventory inventory)
    {
        ItemTable items = ItemTable.FromJson(ItemsJson);
        RecipeTable recipes = RecipeTable.FromJson(RecipesJson, items, Ranks);
        inventory = new Inventory(items, slotCount: 2);
        return new CraftingSystem(recipes, inventory, items, Ranks);
    }

    [Fact]
    public void 多做几份放不下时_一份都不做_材料一件不少()
    {
        CraftingSystem crafting = NewSystem(out Inventory inventory);

        Assert.Equal(0, inventory.Add("material_wood", 10));   // 第 0 格：材料（足够做 5 份）
        Assert.Equal(0, inventory.Add("craft_widget", 9));     // 第 1 格：成品堆到 9/10，只放得下 1 个

        crafting.Unlock("recipe_widget");

        // 要做 2 份但只能放 1 个成品：一份都不做，材料一个都不扣（ADR-012 的全有或全无）
        Assert.False(crafting.CanCraft("recipe_widget", 2));
        Assert.False(crafting.TryCraft("recipe_widget", 2));

        Assert.Equal(10, inventory.Count("material_wood"));
        Assert.Equal(9, inventory.Count("craft_widget"));
    }

    [Fact]
    public void 存档_重复的配方id_按集合语义读成一条_而不是报错()
    {
        CraftingSystem crafting = NewSystem(out _);

        // 解锁表是集合语义（解锁与否是布尔），同一条写两遍等价于写一遍——
        // 与牧场/图鉴那种「条数本身有意义」的表不同，重复在这里没有第二种解释
        crafting.Deserialize("""{ "UnlockedRecipes": [ "recipe_widget", "recipe_widget" ] }""", 1);

        Assert.Equal(new[] { "recipe_widget" }, crafting.UnlockedRecipes);
    }
}
