using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Core.Events;
using XingGame.Core.Time;
using XingGame.Systems.Items;
using XingGame.Systems.Ranching;

namespace XingGame.Tests;

/// <summary>
/// 畜牧系统（M2）。这里守的是「谁在什么时候驱动牧场」：产出由 <c>DayStarted</c> 驱动而不是每帧
/// （ADR-006，同 ADR-014 对作物生长的定论），喂食状态每天清空，没喂食只是停滞。
/// </summary>
/// <remarks>
/// <para>
/// 时间在本系统里<b>没有依赖</b>（§6.5 与 §3.3 都没有一条动物与天气/季节有关的规则），
/// 所以这里连时间替身都不需要——只发事件。
/// </para>
/// <para>
/// 存档用例（<c>Ranch</c> 的坏值校验）也在这一个文件里：畜牧只有这两个测试文件。
/// </para>
/// </remarks>
public class RanchingSystemTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "material_hay",             "name": "干草",   "description": "普通动物的饲料。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_spirit_grass",    "name": "灵草",   "description": "灵兽的饲料。",     "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "food_egg",                 "name": "鸡蛋",   "description": "白鸡的产出。",     "category": "Food",     "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "food_large_egg",           "name": "大鸡蛋", "description": "棕鸡的产出。",     "category": "Food",     "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "food_milk",                "name": "牛奶",   "description": "牛的产出。",       "category": "Food",     "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_wool",            "name": "羊毛",   "description": "羊的产出。",       "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_spirit_fox_fire", "name": "灵狐火", "description": "灵狐的产出。",     "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_stone",           "name": "石头",   "description": "填背包用。",       "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string AnimalsJson = """
    {
      "animals": [
        { "id": "white_chicken", "name": "白鸡", "building": "鸡舍",   "buyPrice": 800,   "produceId": "food_egg",                 "productionIntervalDays": 1, "type": "Normal", "growthDays": 0 },
        { "id": "brown_chicken", "name": "棕鸡", "building": "鸡舍",   "buyPrice": 800,   "produceId": "food_large_egg",           "productionIntervalDays": 1, "type": "Normal", "growthDays": 0 },
        { "id": "sheep",         "name": "羊",   "building": "畜棚",   "buyPrice": 8000,  "produceId": "material_wool",            "productionIntervalDays": 3, "type": "Normal", "growthDays": 0 },
        { "id": "spirit_fox",    "name": "灵狐", "building": "灵兽园", "buyPrice": 15000, "produceId": "material_spirit_fox_fire", "productionIntervalDays": 5, "type": "Spirit", "growthDays": 0 }
      ]
    }
    """;

    /// <summary>成长 2 天的合成表：缺省数据里每种动物都是 0 天（§6.5 未给），只有造一张表才验得到幼年。</summary>
    private const string ChicksJson = """
    {
      "animals": [
        { "id": "chick", "name": "雏鸡", "building": "鸡舍", "buyPrice": 0, "produceId": "food_egg", "productionIntervalDays": 1, "type": "Normal", "growthDays": 2 }
      ]
    }
    """;

    private const string Chicken = "white_chicken";
    private const string Sheep = "sheep";
    private const string Fox = "spirit_fox";
    private const string Hay = AnimalDefinition.HayItemId;
    private const string SpiritGrass = AnimalDefinition.SpiritGrassItemId;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);
    private static readonly AnimalTable Animals = AnimalTable.FromJson(AnimalsJson, Items);
    private static readonly AnimalTable Chicks = AnimalTable.FromJson(ChicksJson, Items);

    private readonly EventBus _bus = new();
    private readonly Ranch _ranch = new(Animals);
    private readonly Inventory _inventory = new(Items, slotCount: 4);

    private RanchingSystem NewSystem() => new(_bus, _ranch, _inventory);

    /// <summary>第 day 天的日界事件（6:00）。</summary>
    private static DayStarted DayStartedOf(int day) =>
        new(new GameTime(1, Season.Spring, day, GameTime.FirstHour, 0));

    // ——— 养 ———

    [Fact]
    public void 新增动物_加到牧场_id_从1起递增()
    {
        using RanchingSystem system = NewSystem();

        int first = system.AddAnimal(Chicken);
        int second = system.AddAnimal(Fox, "小狐");

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(new[] { 1, 2 }, system.Animals.Select(animal => animal.Id).ToArray());
        Assert.Equal("小狐", _ranch.Get(second).Name);
        Assert.Equal(string.Empty, _ranch.Get(first).Name);   // 没给名字 = 未命名
        Assert.Equal(Chicken, _ranch.Get(first).Definition.AnimalId);
    }

    [Fact]
    public void 新增动物_未知动物抛_KeyNotFoundException_且消息含_id()
    {
        using RanchingSystem system = NewSystem();

        var error = Assert.Throws<KeyNotFoundException>(() => system.AddAnimal("ghost"));

        Assert.Contains("ghost", error.Message);
    }

    [Fact]
    public void 改名_与空白名字()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken, "小白");

        system.Rename(id, "大黄");
        Assert.Equal("大黄", _ranch.Get(id).Name);

        // 空白名字等同于未命名：§6.5 只说「每只动物可命名」，没规定名字必须非空
        system.Rename(id, "   ");
        Assert.Equal(string.Empty, _ranch.Get(id).Name);
    }

    // ——— 每日产出（由 DayStarted 驱动）———

    [Fact]
    public void 发_DayStarted_喂过的动物产出进背包()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);
        _inventory.Add(Hay, 1);
        Assert.True(system.TryFeed(id));

        _bus.Publish(DayStartedOf(2));

        Assert.Equal(1, _inventory.Count("food_egg"));
        Assert.Equal(1, _ranch.Get(id).AgeDays);
        Assert.False(_ranch.Get(id).FedToday);          // 喂食状态每天清空
        Assert.Equal(0, _ranch.Get(id).DaysSinceProduce);
    }

    [Fact]
    public void 发_DayStarted_没喂食的动物不产出_且什么都不损失()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);

        _bus.Publish(DayStartedOf(2));

        Assert.Equal(0, _inventory.Count("food_egg"));

        // 「没喂只是停滞，不会枯萎」——同 ADR-014 对不浇水的取舍：不产出，但年龄照长、
        // 心情不掉、动物还在、产出计时不后退
        AnimalInfo animal = _ranch.Get(id);
        Assert.Equal(1, animal.AgeDays);
        Assert.Equal(Ranch.InitialMood, animal.Mood);
        Assert.Equal(0, animal.DaysSinceProduce);
    }

    [Fact]
    public void 发_DayStarted_动物随天数长大()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);

        for (int day = 2; day <= 5; day++) _bus.Publish(DayStartedOf(day));

        Assert.Equal(4, _ranch.Get(id).AgeDays);
    }

    [Fact]
    public void 每天产出的动物_喂一天产一天()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);

        for (int day = 2; day <= 4; day++)
        {
            _inventory.Add(Hay, 1);
            Assert.True(system.TryFeed(id));
            _bus.Publish(DayStartedOf(day));
        }

        Assert.Equal(3, _inventory.Count("food_egg"));   // 三天三个蛋
    }

    [Fact]
    public void 产出周期_每3天的羊_喂满三天才产一份()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Sheep);

        for (int day = 2; day <= 3; day++)
        {
            _inventory.Add(Hay, 1);
            Assert.True(system.TryFeed(id));
            _bus.Publish(DayStartedOf(day));
        }

        Assert.Equal(0, _inventory.Count("material_wool"));   // 周期没到，不产出

        _inventory.Add(Hay, 1);
        Assert.True(system.TryFeed(id));
        _bus.Publish(DayStartedOf(4));
        Assert.Equal(1, _inventory.Count("material_wool"));   // 第 3 次喂食后产一份

        // 再一个周期：又是三次喂食才产一份，说明产完后重新计时而不是一次喂食永久生效
        for (int day = 5; day <= 6; day++)
        {
            _inventory.Add(Hay, 1);
            Assert.True(system.TryFeed(id));
            _bus.Publish(DayStartedOf(day));
        }

        Assert.Equal(1, _inventory.Count("material_wool"));

        _inventory.Add(Hay, 1);
        Assert.True(system.TryFeed(id));
        _bus.Publish(DayStartedOf(7));
        Assert.Equal(2, _inventory.Count("material_wool"));
    }

    [Fact]
    public void 没喂食的那天_产出计时冻结_既不清零也不推进()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Sheep);   // 周期 3 天

        _inventory.Add(Hay, 1);
        Assert.True(system.TryFeed(id));
        _bus.Publish(DayStartedOf(2));
        Assert.Equal(1, _ranch.Get(id).DaysSinceProduce);

        _bus.Publish(DayStartedOf(3));      // 这天没喂：计时停住
        Assert.Equal(1, _ranch.Get(id).DaysSinceProduce);

        _inventory.Add(Hay, 1);
        Assert.True(system.TryFeed(id));
        _bus.Publish(DayStartedOf(4));

        // 停住而不是清零：喂过的那两天都算数，只是中间那天白过了
        Assert.Equal(2, _ranch.Get(id).DaysSinceProduce);
        Assert.Equal(0, _inventory.Count("material_wool"));
    }

    [Fact]
    public void 只喂一次_不会连着两天产出()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);

        _inventory.Add(Hay, 1);
        Assert.True(system.TryFeed(id));
        _bus.Publish(DayStartedOf(2));
        _bus.Publish(DayStartedOf(3));

        Assert.Equal(1, _inventory.Count("food_egg"));
    }

    // ——— 喂食（牧场 ↔ 背包）———

    [Fact]
    public void 喂食_扣掉一份饲料_并记下今天喂过()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);
        _inventory.Add(Hay, 2);

        Assert.True(system.TryFeed(id));

        Assert.Equal(1, _inventory.Count(Hay));
        Assert.True(_ranch.Get(id).FedToday);
    }

    [Fact]
    public void 喂食_灵兽要灵草_干草喂不进去也不扣()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Fox);
        _inventory.Add(Hay, 1);

        Assert.False(system.TryFeed(id));

        Assert.Equal(1, _inventory.Count(Hay));      // 一份干草都没被扣
        Assert.False(_ranch.Get(id).FedToday);

        _inventory.Add(SpiritGrass, 1);
        Assert.True(system.TryFeed(id));
        Assert.Equal(0, _inventory.Count(SpiritGrass));
    }

    [Fact]
    public void 喂食_今天已经喂过_返回_false_且不重复扣()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);
        _inventory.Add(Hay, 3);

        Assert.True(system.TryFeed(id));
        Assert.False(system.TryFeed(id));    // 白扣一份饲料是玩家看不见的损失

        Assert.Equal(2, _inventory.Count(Hay));
    }

    [Fact]
    public void 喂食_背包里没有饲料_返回_false_且什么都不发生()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);

        Assert.False(system.TryFeed(id));

        Assert.False(_ranch.Get(id).FedToday);
        Assert.Equal(0, _ranch.Get(id).AgeDays);
    }

    [Fact]
    public void 喂食_未知动物抛_KeyNotFoundException_且不碰背包()
    {
        using RanchingSystem system = NewSystem();
        _inventory.Add(Hay, 1);

        var error = Assert.Throws<KeyNotFoundException>(() => system.TryFeed(99));

        Assert.Contains("99", error.Message);
        Assert.Equal(1, _inventory.Count(Hay));   // 先取动物再扣饲料，顺序反了就会白扣
    }

    // ——— 产物上限 ———

    [Fact]
    public void 产物上限_背包满时产出丢失_动物本身照常计时()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);

        // 4 格全占满（每格 999 上限）。M1 没有掉落物系统，背包满时溢出部分丢失——
        // 与 FarmingSystem.TryHarvest 同款取舍（备案 #35），M2 做掉落物时改
        foreach (string filler in new[] { Hay, SpiritGrass, "food_milk", "material_stone" })
            Assert.Equal(0, _inventory.Add(filler, 999));

        Assert.True(system.TryFeed(id));    // 干草那格扣掉一份，但仍是「有东西的格」
        _bus.Publish(DayStartedOf(2));

        Assert.Equal(0, _inventory.Count("food_egg"));   // 放不下 → 丢了

        AnimalInfo animal = _ranch.Get(id);
        Assert.Equal(1, animal.AgeDays);
        Assert.Equal(0, animal.DaysSinceProduce);        // 产出照常发生（只是没地方放），不会卡在那里反复产
    }

    [Fact]
    public void 产物上限_一次产出给的个数是_ProduceYield()
    {
        // §6.5 没有产量列，取 1（待裁决）
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);
        _inventory.Add(Hay, 1);
        Assert.True(system.TryFeed(id));

        _bus.Publish(DayStartedOf(2));

        Assert.Equal(Ranch.ProduceYield, _inventory.Count("food_egg"));
    }

    // ——— 心情与好感度（§6.5：0-100 / 0-5 心）———

    [Fact]
    public void 心情与好感_新动物的初值()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);

        Assert.Equal(Ranch.InitialMood, _ranch.Get(id).Mood);
        Assert.Equal(Ranch.InitialAffection, _ranch.Get(id).Affection);
    }

    [Fact]
    public void 设置心情与好感_存得住()
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);

        system.SetMood(id, 37);
        system.SetAffection(id, 4);

        Assert.Equal(37, _ranch.Get(id).Mood);
        Assert.Equal(4, _ranch.Get(id).Affection);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void 设置心情_越界抛(int mood)
    {
        // 钳一下会让多出来的部分静默消失，调用方的 bug 就查不出来了
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);

        Assert.Throws<ArgumentOutOfRangeException>(() => system.SetMood(id, mood));
        Assert.Equal(Ranch.InitialMood, _ranch.Get(id).Mood);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void 设置好感度_越界抛(int hearts)
    {
        using RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);

        Assert.Throws<ArgumentOutOfRangeException>(() => system.SetAffection(id, hearts));
        Assert.Equal(Ranch.InitialAffection, _ranch.Get(id).Affection);
    }

    [Fact]
    public void 未知动物_读取抛_KeyNotFoundException_而_TryGet_返回_false()
    {
        Assert.Throws<KeyNotFoundException>(() => _ranch.Get(99));
        Assert.False(_ranch.TryGet(99, out _));
    }

    // ——— 幼年（缺省数据里成长天数是 0，故只能造一张表来验）———

    [Fact]
    public void 幼年_成长天数没到之前不产出_成年当天开始产()
    {
        var ranch = new Ranch(Chicks);
        var inventory = new Inventory(Items, slotCount: 4);
        using var system = new RanchingSystem(_bus, ranch, inventory);

        int id = system.AddAnimal("chick");   // 成长天数 2
        inventory.Add(Hay, 3);

        Assert.True(system.TryFeed(id));
        _bus.Publish(DayStartedOf(2));
        Assert.Equal(1, ranch.Get(id).AgeDays);
        Assert.False(ranch.Get(id).IsAdult);
        Assert.Equal(0, inventory.Count("food_egg"));   // 幼年：喂了也不产，且喂食状态不会留到明天

        Assert.True(system.TryFeed(id));
        _bus.Publish(DayStartedOf(3));
        Assert.True(ranch.Get(id).IsAdult);
        Assert.Equal(1, inventory.Count("food_egg"));
    }

    // ——— 退订（ADR-005）与构造 ———

    [Fact]
    public void Dispose_之后再发_DayStarted_不再有任何反应()
    {
        RanchingSystem system = NewSystem();
        int id = system.AddAnimal(Chicken);
        _inventory.Add(Hay, 1);
        Assert.True(system.TryFeed(id));

        system.Dispose();
        _bus.Publish(DayStartedOf(2));

        Assert.Equal(0, _ranch.Get(id).AgeDays);
        Assert.Equal(0, _inventory.Count("food_egg"));
        Assert.True(_ranch.Get(id).FedToday);   // 连喂食状态都不该被清
    }

    [Fact]
    public void Dispose_可以重复调用()
    {
        RanchingSystem system = NewSystem();

        system.Dispose();
        system.Dispose();   // 桥接层节点在异常路径上重复 Dispose 是常事，不该抛
    }

    [Fact]
    public void 构造_缺少任何一个依赖都抛()
    {
        Assert.Throws<ArgumentNullException>(() => new RanchingSystem(null!, _ranch, _inventory));
        Assert.Throws<ArgumentNullException>(() => new RanchingSystem(_bus, null!, _inventory));
        Assert.Throws<ArgumentNullException>(() => new RanchingSystem(_bus, _ranch, null!));
        Assert.Throws<ArgumentNullException>(() => new Ranch(null!));
    }

    // ——— 存档（ISaveable）———

    [Fact]
    public void 存档_SaveKey_与_Version()
    {
        Assert.Equal("ranching", _ranch.SaveKey);
        Assert.Equal(1, _ranch.Version);
    }

    [Fact]
    public void 存档往返_动物的全部状态一致()
    {
        int chicken = _ranch.Add(Chicken, "小白");
        int fox = _ranch.Add(Fox);
        _ranch.SetMood(chicken, 42);
        _ranch.SetAffection(chicken, 3);

        _inventory.Add(Hay, 1);
        _inventory.Add(SpiritGrass, 1);

        using (RanchingSystem system = NewSystem())
        {
            Assert.True(system.TryFeed(fox));       // 灵草
            _bus.Publish(DayStartedOf(2));          // 一天过去：两边的喂食状态都被清空
            Assert.True(system.TryFeed(chicken));   // 干草，留下 FedToday = true 供比对
        }

        string json = _ranch.Serialize();
        var restored = new Ranch(Animals);
        restored.Deserialize(json, _ranch.Version);

        // 同一份状态序列化两次结果相同（顺序稳定，diff 可读）
        Assert.Equal(json, restored.Serialize());

        // AnimalInfo 是 record：整体比较会把定义、名字、年龄、喂食、计时、心情、好感都比一遍
        Assert.Equal(_ranch.Animals, restored.Animals);

        AnimalInfo chickenAfter = restored.Get(chicken);
        Assert.Equal("小白", chickenAfter.Name);
        Assert.Equal(1, chickenAfter.AgeDays);
        Assert.True(chickenAfter.FedToday);
        Assert.Equal(42, chickenAfter.Mood);
        Assert.Equal(3, chickenAfter.Affection);

        AnimalInfo foxAfter = restored.Get(fox);
        Assert.False(foxAfter.FedToday);
        Assert.Equal(1, foxAfter.DaysSinceProduce);
    }

    [Fact]
    public void 存档往返_读档后新增动物不撞已有的_id()
    {
        _ranch.Add(Chicken);
        _ranch.Add(Fox);

        var restored = new Ranch(Animals);
        restored.Deserialize(_ranch.Serialize(), _ranch.Version);

        // 下一号 id 由已有 id 推出，不单独存——存两处早晚会对不上，撞了就是两只动物同 id
        Assert.Equal(3, restored.Add(Chicken));
        Assert.Equal(3, restored.Animals.Count);
    }

    [Fact]
    public void 存档_空牧场往返()
    {
        var restored = new Ranch(Animals);
        restored.Deserialize(_ranch.Serialize(), _ranch.Version);

        Assert.Empty(restored.Animals);
        Assert.Equal(1, restored.Add(Chicken));   // 空档读回来仍从 1 号开始
    }

    /// <summary>把动物条目拼成一份存档 JSON。坏档用例只改一个字段，不必每次抄一整份。</summary>
    private static string SavedJson(params string[] entries) =>
        "{ \"Animals\": [" + string.Join(",", entries) + "] }";

    private static string SavedAnimal(
        string id = "1",
        string animalId = Chicken,
        string name = "\"小白\"",
        string ageDays = "0",
        string fedToday = "false",
        string daysSinceProduce = "0",
        string mood = "100",
        string affection = "0") =>
        $"{{ \"Id\": {id}, \"AnimalId\": \"{animalId}\", \"Name\": {name}, \"AgeDays\": {ageDays}, " +
        $"\"FedToday\": {fedToday}, \"DaysSinceProduce\": {daysSinceProduce}, " +
        $"\"Mood\": {mood}, \"Affection\": {affection} }}";

    [Fact]
    public void 存档_版本高于当前时抛_NotSupportedException()
    {
        var restored = new Ranch(Animals);

        Assert.Throws<NotSupportedException>(
            () => restored.Deserialize(SavedJson(SavedAnimal()), _ranch.Version + 1));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    public void 存档_内容为空或缺动物数组时抛(string json)
    {
        var restored = new Ranch(Animals);

        Assert.Throws<InvalidDataException>(() => restored.Deserialize(json, _ranch.Version));
    }

    [Fact]
    public void 存档_重复的动物_id_时抛且消息里带那个_id()
    {
        var restored = new Ranch(Animals);

        var error = Assert.Throws<InvalidDataException>(
            () => restored.Deserialize(SavedJson(SavedAnimal(), SavedAnimal(name: "\"另一只\"")), _ranch.Version));

        Assert.Contains("1", error.Message);
    }

    [Fact]
    public void 存档_动物_id_不在表里时抛且消息里带那个_id()
    {
        var restored = new Ranch(Animals);

        var error = Assert.Throws<InvalidDataException>(
            () => restored.Deserialize(SavedJson(SavedAnimal(animalId: "ghost")), _ranch.Version));

        Assert.Contains("ghost", error.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void 存档_非法动物_id_时抛(string id)
    {
        var restored = new Ranch(Animals);

        Assert.Throws<InvalidDataException>(
            () => restored.Deserialize(SavedJson(SavedAnimal(id: id)), _ranch.Version));
    }

    [Fact]
    public void 存档_负年龄时抛()
    {
        var restored = new Ranch(Animals);

        Assert.Throws<InvalidDataException>(
            () => restored.Deserialize(SavedJson(SavedAnimal(ageDays: "-1")), _ranch.Version));
    }

    [Fact]
    public void 存档_产出计时超过周期时抛且消息里带当前周期()
    {
        var restored = new Ranch(Animals);

        // 白鸡的周期是 1 天：计时到 1 就该清零，2 只能是坏档（或表里的周期被改小了）
        var error = Assert.Throws<InvalidDataException>(
            () => restored.Deserialize(SavedJson(SavedAnimal(daysSinceProduce: "2")), _ranch.Version));

        Assert.Contains("1", error.Message);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    public void 存档_心情越界时抛(string mood)
    {
        var restored = new Ranch(Animals);

        Assert.Throws<InvalidDataException>(
            () => restored.Deserialize(SavedJson(SavedAnimal(mood: mood)), _ranch.Version));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("6")]
    public void 存档_好感度越界时抛(string affection)
    {
        var restored = new Ranch(Animals);

        Assert.Throws<InvalidDataException>(
            () => restored.Deserialize(SavedJson(SavedAnimal(affection: affection)), _ranch.Version));
    }

    [Fact]
    public void 存档_名字为_null_时读成未命名()
    {
        var restored = new Ranch(Animals);

        restored.Deserialize(SavedJson(SavedAnimal(name: "null")), _ranch.Version);

        Assert.Equal(string.Empty, restored.Get(1).Name);
    }

    [Fact]
    public void 存档_坏档不会让牧场停在读了一半的状态()
    {
        _ranch.Add(Chicken, "小白");
        string good = _ranch.Serialize();

        // 整份校验再落盘：坏档抛出后，牧场还是原来那份状态，而不是被清成一半
        var bad = new Ranch(Animals);
        Assert.Throws<InvalidDataException>(
            () => bad.Deserialize(good.Replace("\"Mood\": 100", "\"Mood\": 999"), _ranch.Version));
        Assert.Empty(bad.Animals);

        // 读一份好的进去，再拿一份坏的覆盖——失败后仍该是前一份好的状态
        bad.Deserialize(good, _ranch.Version);
        Assert.Throws<InvalidDataException>(
            () => bad.Deserialize(SavedJson(SavedAnimal(), SavedAnimal(animalId: "ghost")), _ranch.Version));

        Assert.Single(bad.Animals);
        Assert.Equal("小白", bad.Get(1).Name);
    }
}
