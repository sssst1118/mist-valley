using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Core.Time;
using XingGame.Systems.Fishing;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 掷鱼与钓鱼流程（M2-A 钓鱼）。这里守四件事：① 掷鱼是<b>确定性纯函数</b>（同参数必得同结果，
/// 不读时钟、不用 Random.Shared）；② 季节/天气/时段三个条件真的拦得住；③ 大样本下分布符合权重；
/// ④ 钓到的鱼进背包、进图鉴，且背包满时全有或全无。
/// </summary>
public class FishingSystemTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "fish_any",    "name": "杂鱼",   "description": "测试用。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "fish_summer", "name": "夏鱼",   "description": "测试用。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "fish_fog",    "name": "雾鱼",   "description": "测试用。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "fish_night",  "name": "夜鱼",   "description": "测试用。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "fish_pot",    "name": "笼货",   "description": "测试用。", "category": "Food", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "tool_rod",    "name": "测试钓竿", "description": "测试用。", "category": "Tool", "maxStack": 1,   "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    /// <summary>四条都不带条件的鱼，权重 1/2/3/4——用来验分布。</summary>
    private const string WeightedJson = """
    {
      "fish": [
        { "id": "fish_any",    "itemId": "fish_any",    "method": "Rod", "weight": 1 },
        { "id": "fish_summer", "itemId": "fish_summer", "method": "Rod", "weight": 2 },
        { "id": "fish_fog",    "itemId": "fish_fog",    "method": "Rod", "weight": 3 },
        { "id": "fish_night",  "itemId": "fish_night",  "method": "Rod", "weight": 4 }
      ]
    }
    """;

    /// <summary>每条各限一个维度，外加一条蟹笼产出——用来验条件过滤。</summary>
    private const string ConditionalJson = """
    {
      "fish": [
        { "id": "fish_any",    "itemId": "fish_any",    "method": "Rod" },
        { "id": "fish_summer", "itemId": "fish_summer", "method": "Rod", "seasons": ["Summer"] },
        { "id": "fish_fog",    "itemId": "fish_fog",    "method": "Rod", "weather": ["Foggy"] },
        { "id": "fish_night",  "itemId": "fish_night",  "method": "Rod", "phases": ["LateNight"] },
        { "id": "fish_pot",    "itemId": "fish_pot",    "method": "CrabPot" }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);

    private static readonly FishTable Weighted = FishTable.FromJson(WeightedJson, Items);
    private static readonly FishTable Conditional = FishTable.FromJson(ConditionalJson, Items);

    private static FishingSystem NewSystem(FishTable table) =>
        new(table, new Inventory(Items, Inventory.DefaultSlotCount), new FishCodex(table));

    // ——— 确定性 ———

    [Fact]
    public void TryRoll_同参数必得同结果()
    {
        FishingSystem system = NewSystem(Weighted);

        foreach (Season season in Enum.GetValues<Season>())
        foreach (Weather weather in Enum.GetValues<Weather>())
        foreach (DayPhase phase in Enum.GetValues<DayPhase>())
        for (int castIndex = 0; castIndex < 50; castIndex++)
        for (int seed = -3; seed <= 3; seed++)
        {
            bool first = system.TryRoll(season, weather, phase, castIndex, seed, out FishDefinition firstFish);
            bool second = system.TryRoll(season, weather, phase, castIndex, seed, out FishDefinition secondFish);

            Assert.Equal(first, second);
            Assert.Equal(firstFish?.Id, secondFish?.Id);
            Assert.True(first);
        }
    }

    [Fact]
    public void TryRoll_与调用顺序无关()
    {
        FishingSystem first = NewSystem(Weighted);
        FishingSystem second = NewSystem(Weighted);

        for (int castIndex = 0; castIndex < 500; castIndex++)
        {
            Season season = (Season)(castIndex % 4);

            // 插一堆无关的调用进去，不该影响结果
            second.TryRoll(Season.Winter, Weather.Storm, DayPhase.Collapsed, castIndex + 7000, 99, out _);

            Assert.True(first.TryRoll(season, Weather.Sunny, DayPhase.Morning, castIndex, 5, out FishDefinition expected));
            Assert.True(second.TryRoll(season, Weather.Sunny, DayPhase.Morning, castIndex, 5, out FishDefinition actual));
            Assert.Equal(expected.Id, actual.Id);
        }
    }

    [Fact]
    public void TryRoll_竿序不同给出不同结果()
    {
        // 同一天同一时段连甩两竿该出不同的鱼：竿序不进哈希的话，玩家每一竿都钓到同一条
        FishingSystem system = NewSystem(Weighted);

        var seen = new HashSet<string>();
        for (int castIndex = 0; castIndex < 200; castIndex++)
        {
            Assert.True(system.TryRoll(Season.Spring, Weather.Sunny, DayPhase.Morning, castIndex, 7, out FishDefinition fish));
            seen.Add(fish.Id);
        }

        Assert.Equal(Weighted.All.Count, seen.Count);
    }

    [Fact]
    public void TryRoll_世界种子起作用()
    {
        FishingSystem system = NewSystem(Weighted);

        int differences = 0;
        for (int castIndex = 0; castIndex < 200; castIndex++)
        {
            system.TryRoll(Season.Spring, Weather.Sunny, DayPhase.Morning, castIndex, 1, out FishDefinition a);
            system.TryRoll(Season.Spring, Weather.Sunny, DayPhase.Morning, castIndex, 2, out FishDefinition b);

            if (a.Id != b.Id) differences++;
        }

        Assert.True(differences > 40, $"两个种子在 200 竿里只有 {differences} 竿不同，种子没起作用");
    }

    // ——— 条件过滤 ———

    [Fact]
    public void TryRoll_季节限制生效_春季钓不到夏季的鱼()
    {
        FishingSystem system = NewSystem(Conditional);

        AssertNever(system, Season.Spring, Weather.Sunny, DayPhase.Morning, "fish_summer");
        AssertEventually(system, Season.Summer, Weather.Sunny, DayPhase.Morning, "fish_summer");
    }

    [Fact]
    public void TryRoll_天气限制生效_晴天钓不到雾天的鱼()
    {
        FishingSystem system = NewSystem(Conditional);

        foreach (Season season in Enum.GetValues<Season>())
            AssertNever(system, season, Weather.Sunny, DayPhase.Morning, "fish_fog");

        AssertEventually(system, Season.Spring, Weather.Foggy, DayPhase.Morning, "fish_fog");
    }

    [Fact]
    public void TryRoll_时段限制生效_白天钓不到深夜的鱼()
    {
        FishingSystem system = NewSystem(Conditional);

        foreach (DayPhase phase in new[] { DayPhase.Morning, DayPhase.Forenoon, DayPhase.Afternoon, DayPhase.Evening })
            AssertNever(system, Season.Spring, Weather.Sunny, phase, "fish_night");

        AssertEventually(system, Season.Spring, Weather.Sunny, DayPhase.LateNight, "fish_night");
    }

    [Fact]
    public void TryRoll_蟹笼产出不会被鱼竿钓到()
    {
        FishingSystem system = NewSystem(Conditional);

        foreach (Season season in Enum.GetValues<Season>())
        foreach (Weather weather in Enum.GetValues<Weather>())
            AssertNever(system, season, weather, DayPhase.Morning, "fish_pot");
    }

    [Fact]
    public void TryRoll_没有任何候选时返回_false_且输出为_null()
    {
        // 只有一条夏季鱼的表：春季一竿都甩不出东西——这正是「钓不到」的表达方式
        FishTable summerOnly = FishTable.FromJson(
            """{ "fish": [ { "id": "fish_summer", "itemId": "fish_summer", "method": "Rod", "seasons": ["Summer"] } ] }""",
            Items);

        FishingSystem system = NewSystem(summerOnly);

        for (int castIndex = 0; castIndex < 100; castIndex++)
        {
            Assert.False(system.TryRoll(Season.Spring, Weather.Sunny, DayPhase.Morning, castIndex, 1, out FishDefinition fish));
            Assert.Null(fish);
        }

        Assert.True(system.TryRoll(Season.Summer, Weather.Sunny, DayPhase.Morning, 0, 1, out _));
    }

    [Fact]
    public void TryRoll_大样本分布接近权重()
    {
        const int Samples = 20000;
        const double Tolerance = 0.02;

        FishingSystem system = NewSystem(Weighted);
        int totalWeight = Weighted.All.Sum(fish => fish.Weight);

        var counts = Weighted.All.ToDictionary(fish => fish.Id, _ => 0);
        for (int castIndex = 0; castIndex < Samples; castIndex++)
        {
            Assert.True(system.TryRoll(Season.Spring, Weather.Sunny, DayPhase.Morning, castIndex, 8, out FishDefinition fish));
            counts[fish.Id]++;
        }

        foreach (FishDefinition fish in Weighted.All)
        {
            double expected = fish.Weight / (double)totalWeight;
            double actual = counts[fish.Id] / (double)Samples;

            Assert.True(
                Math.Abs(expected - actual) <= Tolerance,
                $"{fish.Id} 期望 {expected:P1}，实测 {actual:P1}");
        }
    }

    // ——— 进背包与图鉴 ———

    [Fact]
    public void TryRoll_不碰背包也不记图鉴()
    {
        // 纯函数：掷一百竿，背包与图鉴都该是空的
        var inventory = new Inventory(Items, Inventory.DefaultSlotCount);
        var codex = new FishCodex(Weighted);
        var system = new FishingSystem(Weighted, inventory, codex);

        for (int castIndex = 0; castIndex < 100; castIndex++)
            Assert.True(system.TryRoll(Season.Spring, Weather.Sunny, DayPhase.Morning, castIndex, 1, out _));

        Assert.All(inventory.Slots, slot => Assert.Equal(0, slot.Count));
        Assert.Empty(codex.CaughtSpecies);
    }

    [Fact]
    public void TryCatch_钓到的鱼进背包()
    {
        var inventory = new Inventory(Items, Inventory.DefaultSlotCount);
        var codex = new FishCodex(Weighted);
        var system = new FishingSystem(Weighted, inventory, codex);

        Assert.True(system.TryCatch(Season.Spring, Weather.Sunny, DayPhase.Morning, 3, 1, out FishDefinition fish));

        Assert.Equal(1, inventory.Count(fish.ItemId));
        Assert.Equal(1, codex.CountOf(fish.Id));
        Assert.True(codex.HasCaught(fish.Id));
    }

    [Fact]
    public void TryCatch_背包满时_整条不钓且不记图鉴()
    {
        // 全有或全无：「图鉴记了、鱼没了」比钓不到更莫名其妙
        var inventory = new Inventory(Items, slotCount: 1);
        Assert.Equal(0, inventory.Add("tool_rod", 1));   // 工具不可堆叠，一格已占满

        var codex = new FishCodex(Weighted);
        var system = new FishingSystem(Weighted, inventory, codex);

        for (int castIndex = 0; castIndex < 50; castIndex++)
        {
            Assert.False(system.TryCatch(Season.Spring, Weather.Sunny, DayPhase.Morning, castIndex, 1, out FishDefinition fish));
            Assert.Null(fish);
        }

        Assert.Equal(1, inventory.Count("tool_rod"));
        Assert.Equal(1, inventory.Slots.Count(slot => slot.Count != 0));
        Assert.Empty(codex.CaughtSpecies);
    }

    [Fact]
    public void TryCatch_没鱼上钩时_不记图鉴也不进背包()
    {
        FishTable summerOnly = FishTable.FromJson(
            """{ "fish": [ { "id": "fish_summer", "itemId": "fish_summer", "method": "Rod", "seasons": ["Summer"] } ] }""",
            Items);

        var inventory = new Inventory(Items, Inventory.DefaultSlotCount);
        var codex = new FishCodex(summerOnly);
        var system = new FishingSystem(summerOnly, inventory, codex);

        Assert.False(system.TryCatch(Season.Winter, Weather.Snowy, DayPhase.Morning, 0, 1, out FishDefinition fish));

        Assert.Null(fish);
        Assert.Empty(codex.CaughtSpecies);
        Assert.All(inventory.Slots, slot => Assert.Equal(0, slot.Count));
    }

    [Fact]
    public void 图鉴_同一条鱼钓到多次_条数累加且首次顺序不变()
    {
        var codex = new FishCodex(Weighted);

        codex.Record("fish_fog");
        codex.Record("fish_any");
        codex.Record("fish_fog");

        Assert.Equal(new[] { "fish_fog", "fish_any" }, codex.CaughtSpecies.ToArray());
        Assert.Equal(2, codex.CountOf("fish_fog"));
        Assert.Equal(1, codex.CountOf("fish_any"));
        Assert.Equal(0, codex.CountOf("fish_night"));
        Assert.False(codex.HasCaught("fish_night"));
    }

    [Fact]
    public void 图鉴_记一笔不在表里的鱼_抛()
    {
        var codex = new FishCodex(Weighted);

        var error = Assert.Throws<ArgumentException>(() => codex.Record("fish_unknown"));

        Assert.Contains("fish_unknown", error.Message);
        Assert.Empty(codex.CaughtSpecies);
    }

    // ——— 图鉴存档 ———

    [Fact]
    public void 图鉴_存档往返_原样读回()
    {
        var codex = new FishCodex(Weighted);
        codex.Record("fish_night");
        codex.Record("fish_any");
        codex.Record("fish_night");

        var restored = new FishCodex(Weighted);
        restored.Deserialize(codex.Serialize(), codex.Version);

        Assert.Equal(codex.CaughtSpecies.ToArray(), restored.CaughtSpecies.ToArray());
        Assert.Equal(2, restored.CountOf("fish_night"));
        Assert.Equal(1, restored.CountOf("fish_any"));
        Assert.Equal("fishing", restored.SaveKey);
        Assert.Equal(1, restored.Version);
    }

    [Fact]
    public void 图鉴_读档_版本过高时抛_NotSupportedException()
    {
        var codex = new FishCodex(Weighted);

        Assert.Throws<NotSupportedException>(() => codex.Deserialize("""{ "caught": [] }""", fromVersion: 2));
    }

    [Theory]
    [InlineData("null")]                                                            // 内容为空
    [InlineData("{}")]                                                              // 缺 caught 数组
    [InlineData("""{ "caught": [ { "fishId": "fish_fog", "count": 0 } ] }""")]       // 数量为 0
    [InlineData("""{ "caught": [ { "fishId": "fish_fog", "count": -1 } ] }""")]      // 数量为负
    [InlineData("""{ "caught": [ { "fishId": "", "count": 1 } ] }""")]               // 空 id
    [InlineData("""{ "caught": [ { "fishId": "fish_unknown", "count": 1 } ] }""")]   // 鱼不在表里
    [InlineData("""{ "caught": [ { "fishId": "fish_fog", "count": 1 }, { "fishId": "fish_fog", "count": 2 } ] }""")]  // 重复条目
    public void 图鉴_读档_坏数据当场抛(string json)
    {
        var codex = new FishCodex(Weighted);

        Assert.Throws<InvalidDataException>(() => codex.Deserialize(json, fromVersion: 1));
    }

    [Fact]
    public void 图鉴_读档失败时不改动已有内容()
    {
        // 先整份校验再落盘：坏存档不该让图鉴停在「读了一半」的状态
        var codex = new FishCodex(Weighted);
        codex.Record("fish_fog");

        Assert.Throws<InvalidDataException>(
            () => codex.Deserialize("""{ "caught": [ { "fishId": "fish_any", "count": 1 }, { "fishId": "fish_bad", "count": 1 } ] }""", 1));

        Assert.Equal(new[] { "fish_fog" }, codex.CaughtSpecies.ToArray());
        Assert.Equal(0, codex.CountOf("fish_any"));
    }

    [Fact]
    public void 构造函数_传_null_时抛()
    {
        var inventory = new Inventory(Items, Inventory.DefaultSlotCount);
        var codex = new FishCodex(Weighted);

        Assert.Throws<ArgumentNullException>(() => new FishingSystem(null!, inventory, codex));
        Assert.Throws<ArgumentNullException>(() => new FishingSystem(Weighted, null!, codex));
        Assert.Throws<ArgumentNullException>(() => new FishingSystem(Weighted, inventory, null!));
        Assert.Throws<ArgumentNullException>(() => new FishCodex(null!));
    }

    // ——— 缺省数据文件的端到端 ———

    [Fact]
    public void 缺省数据文件_晴天一千竿钓不到幽灵鱼_雾天能钓到()
    {
        // §3.3：「雾天…幽灵鱼」。这条用真数据把整条链路（表 → 掷鱼 → 条件）串起来验一次
        ItemTable items = ItemTable.LoadDefault();
        FishTable table = FishTable.LoadDefault(items);
        var inventory = new Inventory(items, Inventory.DefaultSlotCount);
        var codex = new FishCodex(table);
        var system = new FishingSystem(table, inventory, codex);

        foreach (Season season in Enum.GetValues<Season>())
            AssertNever(system, season, Weather.Sunny, DayPhase.Morning, "fish_ghost");

        bool seenInFog = false;
        for (int castIndex = 0; castIndex < 500 && !seenInFog; castIndex++)
        {
            Assert.True(system.TryRoll(Season.Spring, Weather.Foggy, DayPhase.LateNight, castIndex, 4, out FishDefinition fish));
            seenInFog = fish.Id == "fish_ghost";
        }

        Assert.True(seenInFog, "雾天甩了 500 竿都没见到幽灵鱼");
    }

    [Fact]
    public void 缺省数据文件_钓上来的鱼真的进了背包()
    {
        ItemTable items = ItemTable.LoadDefault();
        FishTable table = FishTable.LoadDefault(items);
        var inventory = new Inventory(items, Inventory.DefaultSlotCount);
        var codex = new FishCodex(table);
        var system = new FishingSystem(table, inventory, codex);

        Assert.True(system.TryCatch(Season.Spring, Weather.Foggy, DayPhase.Morning, 0, 1, out FishDefinition fish));

        Assert.Equal(1, inventory.Count(fish.ItemId));
        Assert.Equal(1, codex.CountOf(fish.Id));
        Assert.Equal(ItemCategory.Food, items.Get(fish.ItemId).Category);
    }

    /// <summary>该条件下甩 <paramref name="casts"/> 竿，断言某条鱼一次都没出现。</summary>
    private static void AssertNever(
        FishingSystem system, Season season, Weather weather, DayPhase phase, string fishId, int casts = 2000)
    {
        for (int castIndex = 0; castIndex < casts; castIndex++)
        {
            system.TryRoll(season, weather, phase, castIndex, 11, out FishDefinition fish);
            Assert.NotEqual(fishId, fish?.Id);
        }
    }

    /// <summary>该条件下甩 <paramref name="casts"/> 竿，断言某条鱼出现过——只测「钓不到」会让「永远钓不到」蒙混过关。</summary>
    private static void AssertEventually(
        FishingSystem system, Season season, Weather weather, DayPhase phase, string fishId, int casts = 2000)
    {
        for (int castIndex = 0; castIndex < casts; castIndex++)
        {
            system.TryRoll(season, weather, phase, castIndex, 11, out FishDefinition fish);
            if (fish?.Id == fishId) return;
        }

        Assert.Fail($"{season}/{weather}/{phase} 下甩了 {casts} 竿都没见到 {fishId}");
    }
}
