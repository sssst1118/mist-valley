using System;
using System.IO;
using XingGame.Core.Events;
using XingGame.Core.Time;
using XingGame.Systems.Items;
using XingGame.Systems.Ranching;

namespace XingGame.Tests;

/// <summary>
/// 畜牧的对抗性审计（M2）。只补既有用例没在看的分支：
/// 心情/好感度「只存值不做加成」这条备案必须有人守着、七天里饲料与畜产两本账、
/// 以及读档后新动物的编号不许撞回已有的 id。
/// </summary>
public class M2Audit_Ranching
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "material_hay", "name": "干草", "description": "审计用。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "food_egg",     "name": "鸡蛋", "description": "审计用。", "category": "Food",     "maxStack": 999, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string AnimalsJson = """
    {
      "animals": [
        { "id": "white_chicken", "name": "白鸡", "building": "鸡舍", "buyPrice": 800, "produceId": "food_egg",
          "productionIntervalDays": 1, "type": "Normal", "growthDays": 0 }
      ]
    }
    """;

    private const string Chicken = "white_chicken";
    private const string Egg = "food_egg";

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);
    private static readonly AnimalTable Animals = AnimalTable.FromJson(AnimalsJson, Items);

    private readonly EventBus _bus = new();
    private readonly Ranch _ranch = new(Animals);
    private readonly Inventory _inventory = new(Items, slotCount: 8);

    private RanchingSystem NewSystem() => new(_bus, _ranch, _inventory);

    private static DayStarted DayStartedOf(int day) =>
        new(new GameTime(1, Season.Spring, day, GameTime.FirstHour, 0));

    [Fact]
    public void 心情与好感只存值_不影响产出()
    {
        int low = _ranch.Add(Chicken);
        int high = _ranch.Add(Chicken);

        _ranch.SetMood(low, 0);
        _ranch.SetAffection(low, 0);
        _ranch.SetMood(high, Ranch.MaxMood);
        _ranch.SetAffection(high, Ranch.MaxAffection);

        int lowYield = 0;
        int highYield = 0;

        for (int day = 0; day < 5; day++)
        {
            _ranch.MarkFed(low);
            _ranch.MarkFed(high);

            foreach (AnimalProduce produce in _ranch.AdvanceDay())
            {
                if (produce.AnimalId == low) lowYield += produce.Count;
                else if (produce.AnimalId == high) highYield += produce.Count;
            }
        }

        // §6.5 只说了心情影响品质、好感影响频率，一个公式都没给（备案 #45：只存值、不做加成）。
        // 谁要是顺手把加成做进来，这条会红
        Assert.Equal(5, lowYield);
        Assert.Equal(lowYield, highYield);
    }

    [Fact]
    public void 连续七天_饲料的消耗与畜产各算各的账()
    {
        using RanchingSystem system = NewSystem();
        int first = system.AddAnimal(Chicken);
        int second = system.AddAnimal(Chicken);

        Assert.Equal(0, _inventory.Add(AnimalDefinition.HayItemId, 20));

        for (int day = 1; day <= 7; day++)
        {
            Assert.True(system.TryFeed(first));
            Assert.True(system.TryFeed(second));
            _bus.Publish(DayStartedOf(day));
        }

        // 7 天 × 2 只 = 14 份干草换 14 个蛋：进来的与出去的都得对得上
        Assert.Equal(20 - 14, _inventory.Count(AnimalDefinition.HayItemId));
        Assert.Equal(14, _inventory.Count(Egg));
    }

    // ——— 坏存档：坏档一律 InvalidDataException，且「字段不在」不许混同「字段是 0」（ADR-009）———

    [Fact]
    public void 牧场存档_数组里出现null元素时_抛坏档而不是NRE()
    {
        var ranch = new Ranch(Animals);

        // 手写的 JSON 里数组元素可以是 null，System.Text.Json 照收
        Assert.Throws<InvalidDataException>(() => ranch.Deserialize("""{ "Animals": [ null ] }""", 1));
    }

    [Theory]
    [InlineData("""{ "Animals": [ { "Id": 1, "AnimalId": "white_chicken", "Name": "小白" } ] }""")]
    [InlineData("""{ "Animals": [ { "Id": 1, "AnimalId": "white_chicken", "AgeDays": 0, "FedToday": false, "DaysSinceProduce": 0, "Mood": 100 } ] }""")]
    [InlineData("""{ "Animals": [ { "Id": 1, "AnimalId": "white_chicken", "AgeDays": 0, "FedToday": false, "DaysSinceProduce": 0, "Affection": 0 } ] }""")]
    public void 牧场存档_缺少状态字段时_抛而不是静默读成0(string json)
    {
        var ranch = new Ranch(Animals);

        // AgeDays / Mood / Affection 的 0 与 FedToday 的 false 都是合法状态（后两行正是各缺一个字段），
        // 所以「字段不在」必须与「字段是 0」分开。静默读成 0 之后 Serialize 就把这份 0 写回去——
        // 那是真的把状态抹平了，而不只是少读一次
        Assert.Throws<InvalidDataException>(() => ranch.Deserialize(json, 1));
    }

    [Fact]
    public void 牧场存档_动物编号已到上限时_读档当场抛而不是让新动物拿到负数()
    {
        var ranch = new Ranch(Animals);

        // _nextId = MaxId + 1 在 int.MaxValue 上会回绕成 int.MinValue，
        // 于是 Add 返回一个负数 id——编号这种东西负数就是坏掉了，读档那一步就该停住
        Assert.Throws<InvalidDataException>(() => ranch.Deserialize(
            """
            { "Animals": [ { "Id": 2147483647, "AnimalId": "white_chicken", "Name": "小白", "AgeDays": 0,
                             "FedToday": false, "DaysSinceProduce": 0, "Mood": 100, "Affection": 0 } ] }
            """,
            1));
    }

    [Fact]
    public void 存档_读档后新增动物的_id_必须大于已有的最大_id()
    {
        // 存档里只有 7 号（将来有了「卖掉动物」之后必然出现的空档）
        _ranch.Deserialize(
            """
            { "Animals": [ { "Id": 7, "AnimalId": "white_chicken", "Name": "小白", "AgeDays": 3,
                             "FedToday": false, "DaysSinceProduce": 0, "Mood": 100, "Affection": 0 } ] }
            """,
            1);

        // 下一号 id 由现有 id 推出（不单独存，免得同一个事实存两处）
        Assert.Equal(8, _ranch.Add(Chicken));
    }
}
