using System;
using System.IO;
using XingGame.Core.Time;
using XingGame.Systems.Fishing;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 钓鱼的对抗性审计（M2）。既有用例已经把三个条件维度、权重分布、背包满时的全有或全无
/// 都钉住了，这里只补两条<b>跨步骤</b>的一致性：「先看后钓」必须钓到同一条鱼，
/// 以及背包与图鉴两边记的条数不许脱节。
/// </summary>
public class M2Audit_Fishing
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "fish_any",    "name": "杂鱼", "description": "审计用。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "fish_summer", "name": "夏鱼", "description": "审计用。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "fish_fog",    "name": "雾鱼", "description": "审计用。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string FishJson = """
    {
      "fish": [
        { "id": "fish_any",    "itemId": "fish_any",    "method": "Rod" },
        { "id": "fish_summer", "itemId": "fish_summer", "method": "Rod", "seasons": [ "Summer" ] },
        { "id": "fish_fog",    "itemId": "fish_fog",    "method": "Rod", "weather": [ "Foggy" ] }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);
    private static readonly FishTable Fish = FishTable.FromJson(FishJson, Items);

    private static FishingSystem NewSystem(out Inventory inventory, out FishCodex codex)
    {
        inventory = new Inventory(Items, slotCount: 8);
        codex = new FishCodex(Fish);
        return new FishingSystem(Fish, inventory, codex);
    }

    [Fact]
    public void 同一竿序_TryRoll_与_TryCatch_必得同一条鱼()
    {
        (Weather Weather, DayPhase Phase)[] conditions =
        {
            (Weather.Sunny, DayPhase.Morning),
            (Weather.Foggy, DayPhase.Morning),   // 雾天多一条候选
            (Weather.Sunny, DayPhase.LateNight),
        };

        foreach ((Weather weather, DayPhase phase) in conditions)
        {
            for (int cast = 0; cast < 12; cast++)
            {
                FishingSystem peek = NewSystem(out _, out _);
                bool rolled = peek.TryRoll(Season.Spring, weather, phase, cast, seed: 4242, out FishDefinition expected);

                FishingSystem system = NewSystem(out Inventory inventory, out FishCodex codex);
                bool caught = system.TryCatch(Season.Spring, weather, phase, cast, seed: 4242, out FishDefinition actual);

                // 「先看一眼有没有鱼、再收杆」不能变成两条不同的鱼
                Assert.Equal(rolled, caught);
                if (!rolled) continue;

                Assert.Equal(expected.Id, actual.Id);
                Assert.Equal(1, inventory.Count(actual.ItemId));
                Assert.Equal(1, codex.CountOf(actual.Id));
            }
        }
    }

    [Fact]
    public void 鱼类图鉴存档_数组里出现null元素时_抛坏档而不是NRE()
    {
        var codex = new FishCodex(Fish);

        // 手写的 JSON 里数组元素可以是 null，System.Text.Json 照收。按 ADR-009 这是坏档
        // （InvalidDataException 的语义），不许让 NRE 冒出去
        Assert.Throws<InvalidDataException>(() => codex.Deserialize("""{ "Caught": [ null ] }""", 1));
    }

    [Fact]
    public void 连钓三条_背包与图鉴两边的条数始终一致()
    {
        FishingSystem system = NewSystem(out Inventory inventory, out FishCodex codex);

        for (int cast = 0; cast < 3; cast++)
        {
            Assert.True(system.TryCatch(Season.Spring, Weather.Sunny, DayPhase.Morning, cast, seed: 7, out _));
        }

        int inBag = 0;
        foreach (ItemStack slot in inventory.Slots) inBag += slot.Count;

        // 春天晴天的上午只有 fish_any 可钓：三条进背包、三条记图鉴，两边谁都不许少记
        Assert.Equal(3, inBag);
        Assert.Equal(3, codex.CountOf("fish_any"));
        Assert.Single(codex.CaughtSpecies);
    }
}
