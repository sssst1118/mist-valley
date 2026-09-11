using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Core.Save;
using XingGame.Systems.Combat;
using XingGame.Systems.Items;
using Xunit;

namespace XingGame.Tests;

/// <summary>
/// 战斗与矿洞的行为（M2-B）：按层掷遭遇、回合式结算、掉落入包、矿洞进度与它的存档。
/// </summary>
/// <remarks>
/// <para>
/// 机制用例自己带数据（怪物表、矿洞表、物品表都是测试里的小份）：<b>缺省数据的数值一律是
/// 「文档未给」的 0</b>，拿它验不了「谁赢谁输」——那要靠 <see cref="MonsterTableTests"/> 去钉
/// 「不许编」，这里钉的是「给了数值以后算得对不对」。
/// </para>
/// <para>
/// 时间与事件总线都用不上：本模块不认识时间（§7.2 里没有回合与时间的关系），也不发事件。
/// </para>
/// </remarks>
public sealed class CombatSystemTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "material_copper_ore", "name": "铜矿",   "description": "测试用。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_gem",        "name": "宝石",   "description": "测试用。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "tool_hoe",            "name": "锄头",   "description": "测试用。", "category": "Tool",     "maxStack": 1,   "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string MonstersJson = """
    {
      "monsters": [
        { "id": "monster_slime", "name": "史莱姆", "maxHealth": 10, "attack": 3,  "minLayer": 1,  "maxLayer": 10,
          "drops": [ { "itemId": "material_copper_ore", "count": 2 } ] },
        { "id": "monster_bat",   "name": "蝙蝠",   "maxHealth": 6,  "attack": 2,  "minLayer": 1,  "maxLayer": 10,
          "drops": [ { "itemId": "material_copper_ore", "count": 1 } ] },
        { "id": "monster_golem", "name": "石魔",   "maxHealth": 40, "attack": 12, "minLayer": 15, "maxLayer": 20,
          "drops": [ { "itemId": "material_gem", "count": 1 },
                     { "itemId": "material_copper_ore", "count": 1 } ] }
      ]
    }
    """;

    /// <summary>20 层、每 10 层电梯——层数为 20 是为了让超界的边界用例一眼看得出来。</summary>
    private const string MinesJson = """
    {
      "mines": [
        { "id": "mine_test", "name": "测试矿洞", "layerCount": 20, "elevatorInterval": 10,
          "chestInterval": 10, "auraInterval": 20, "auraBonusPercent": 50,
          "ores": [ "material_copper_ore", "material_gem" ] }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);

    private static MineDefinition TestMine => MineTable.FromJson(MinesJson, Items).Get("mine_test");

    /// <summary>挑战者：20 点血、5 点攻击——正好打得过史莱姆（10 血 3 攻）、打不过石魔（40 血 12 攻）。</summary>
    private static readonly CombatStats Challenger = new(maxHealth: 20, attack: 5);

    private static CombatSystem NewSystem(IInventory? inventory = null) =>
        new(MonsterTable.FromJson(MonstersJson, Items), new MineProgress(TestMine), inventory ?? NewInventory());

    private static Inventory NewInventory(int slotCount = 4) => new(Items, slotCount);

    // ——— 按层掷遭遇 ———

    [Fact]
    public void 掷遭遇_同层同种子必得同结果_跨实例也一致()
    {
        CombatSystem first = NewSystem();
        CombatSystem second = NewSystem();

        for (int layer = 1; layer <= 20; layer++)
        {
            for (int seed = 0; seed < 8; seed++)
            {
                MonsterDefinition? a = first.RollEncounter(layer, seed);
                MonsterDefinition? b = second.RollEncounter(layer, seed);

                Assert.Equal(a?.Id, b?.Id);
            }
        }
    }

    [Fact]
    public void 掷遭遇_只掷出该层的怪_且种子的确在起作用()
    {
        CombatSystem system = NewSystem();

        var rolled = new HashSet<string>(StringComparer.Ordinal);
        for (int seed = 0; seed < 64; seed++)
        {
            MonsterDefinition? monster = system.RollEncounter(layer: 5, seed);

            Assert.NotNull(monster);
            Assert.Contains(monster!.Id, new[] { "monster_slime", "monster_bat" });   // 第 5 层只有这两只
            rolled.Add(monster.Id);
        }

        // 掷点若忘了把种子混进去，这里就只会剩一种——「同参数同结果」也会假绿
        Assert.Equal(2, rolled.Count);

        Assert.Equal("monster_golem", system.RollEncounter(layer: 15, seed: 0)?.Id);
    }

    [Fact]
    public void 掷遭遇_该层没有怪返回_null_不是抛()
    {
        CombatSystem system = NewSystem();

        // 第 11-14 层：两只怪的层数区间都够不着，这是合法的数据形态，不是错误
        Assert.Null(system.RollEncounter(layer: 11, seed: 0));
        Assert.Null(system.RollEncounter(layer: 14, seed: 7));
    }

    [Fact]
    public void 掷遭遇_层号越界_当场抛()
    {
        CombatSystem system = NewSystem();

        // 层号的上界来自矿洞（§7.1 的 120 层），不是怪物表——第 21 层在这座 20 层的矿洞里不存在
        Assert.Throws<ArgumentOutOfRangeException>(() => system.RollEncounter(0, seed: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.RollEncounter(-1, seed: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.RollEncounter(21, seed: 0));

        Assert.NotNull(system.RollEncounter(20, seed: 0));
    }

    // ——— 结算 ———

    [Fact]
    public void 结算_挑战者强则胜_怪物血量钳到零_不是负血()
    {
        CombatResult result = NewSystem().Resolve("monster_slime", Challenger);

        Assert.True(result.Victory);
        Assert.Equal(0, result.MonsterRemainingHealth);
        Assert.Equal(17, result.ChallengerRemainingHealth);   // 第 1 回合挨了 3 点
        Assert.Equal(2, result.Rounds);
    }

    [Fact]
    public void 结算_怪物强则败_血量不为负_且没有掉落()
    {
        CombatResult result = NewSystem().Resolve("monster_golem", Challenger);

        Assert.False(result.Victory);
        Assert.Equal(0, result.ChallengerRemainingHealth);   // 钳到 0，不是 -4
        Assert.Equal(30, result.MonsterRemainingHealth);
        Assert.Empty(result.Loot);
        Assert.Empty(result.Overflow);
    }

    [Fact]
    public void 结算_同参数必得同结果()
    {
        CombatSystem system = NewSystem();

        // 挑一场有掉落的胜仗。字段逐个比，不用 record 整体相等——两个结果里的掉落列表
        // 是各自 new 出来的 List，整体相等会退化成引用比较，反而看不出内容变没变
        var strong = new CombatStats(maxHealth: 200, attack: 20);
        CombatResult first = system.Resolve("monster_golem", strong);
        CombatResult second = system.Resolve("monster_golem", strong);

        Assert.True(first.Victory);
        Assert.Equal(first.MonsterId, second.MonsterId);
        Assert.Equal(first.Victory, second.Victory);
        Assert.Equal(first.Rounds, second.Rounds);
        Assert.Equal(first.ChallengerRemainingHealth, second.ChallengerRemainingHealth);
        Assert.Equal(first.MonsterRemainingHealth, second.MonsterRemainingHealth);
        Assert.Equal(first.Loot.ToArray(), second.Loot.ToArray());
        Assert.Equal(first.Overflow.ToArray(), second.Overflow.ToArray());
    }

    [Fact]
    public void 结算_胜利掉落进背包()
    {
        Inventory inventory = NewInventory();
        CombatResult result = NewSystem(inventory).Resolve("monster_slime", Challenger);

        Assert.True(result.Victory);
        Assert.Equal(new ItemStack("material_copper_ore", 2), Assert.Single(result.Loot));
        Assert.Empty(result.Overflow);
        Assert.Equal(2, inventory.Count("material_copper_ore"));
    }

    [Fact]
    public void 结算_败北时背包一个物品都不动()
    {
        Inventory inventory = NewInventory();
        inventory.Add("material_copper_ore", 5);

        CombatResult result = NewSystem(inventory).Resolve("monster_golem", Challenger);

        Assert.False(result.Victory);
        Assert.Equal(5, inventory.Count("material_copper_ore"));
    }

    [Fact]
    public void 结算_背包满_装得下的进背包_装不下的进溢出清单_不静默吞掉()
    {
        // 单格背包 + 一把不可堆叠的锄头 = 一点空间都没有
        Inventory inventory = NewInventory(slotCount: 1);
        inventory.Add("tool_hoe", 1);

        CombatResult result = NewSystem(inventory).Resolve("monster_slime", Challenger);

        Assert.True(result.Victory);
        Assert.Empty(result.Loot);
        Assert.Equal(new ItemStack("material_copper_ore", 2), Assert.Single(result.Overflow));

        // 背包里原有的东西一件不少：装不下不该顺手把别人的位置腾出来（金币不归本模块管，见 CombatResult 注释）
        Assert.Equal(1, inventory.Count("tool_hoe"));
        Assert.Equal(0, inventory.Count("material_copper_ore"));
        Assert.Equal(1, inventory.SlotCount);
    }

    [Fact]
    public void 结算_背包只放得下一部分_两串相加等于掉落总量()
    {
        // 单格背包先装 998 个铜矿（上限 999）：只剩 1 个位置，掉落 2 个
        Inventory inventory = NewInventory(slotCount: 1);
        inventory.Add("material_copper_ore", 998);

        CombatResult result = NewSystem(inventory).Resolve("monster_slime", Challenger);

        Assert.Equal(new ItemStack("material_copper_ore", 1), Assert.Single(result.Loot));
        Assert.Equal(new ItemStack("material_copper_ore", 1), Assert.Single(result.Overflow));
        Assert.Equal(999, inventory.Count("material_copper_ore"));
    }

    [Fact]
    public void 结算_多件掉落逐条分账()
    {
        Inventory inventory = NewInventory();
        CombatResult result = NewSystem(inventory).Resolve("monster_golem", new CombatStats(maxHealth: 200, attack: 20));

        Assert.True(result.Victory);
        Assert.Equal(
            new[] { new ItemStack("material_gem", 1), new ItemStack("material_copper_ore", 1) },
            result.Loot.ToArray());
        Assert.Equal(1, inventory.Count("material_gem"));
        Assert.Equal(1, inventory.Count("material_copper_ore"));
    }

    [Fact]
    public void 结算_怪物_id_不在表里_抛_KeyNotFound_且消息里带_id()
    {
        KeyNotFoundException error =
            Assert.Throws<KeyNotFoundException>(() => NewSystem().Resolve("monster_nope", Challenger));

        Assert.Contains("monster_nope", error.Message);
    }

    [Fact]
    public void 挑战者数值非正_构造就抛()
    {
        // 攻击为 0 会让回合永远打不完；血量为 0 的人压根不该进场
        Assert.Throws<ArgumentOutOfRangeException>(() => new CombatStats(maxHealth: 0, attack: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CombatStats(maxHealth: 20, attack: 0));
    }

    [Fact]
    public void 结算_缺省怪物表_血量未给_遭遇即判胜且无掉落()
    {
        ItemTable items = ItemTable.LoadDefault();
        MineDefinition mine = MineTable.LoadDefault(items).Get("mine_valley");
        var system = new CombatSystem(MonsterTable.LoadDefault(items), new MineProgress(mine), NewInventory());

        CombatResult result = system.Resolve("monster_slime", Challenger);

        // 这是「文档未给血量就一个数都不编」的直接后果：补上血量那天，这一条会红，提醒改它
        Assert.True(result.Victory);
        Assert.Equal(0, result.MonsterRemainingHealth);
        Assert.Equal(1, result.Rounds);
        Assert.Empty(result.Loot);
    }

    // ——— 矿洞进度 ———

    [Fact]
    public void 矿洞进度_进入下一层离开()
    {
        var progress = new MineProgress(TestMine);

        Assert.False(progress.IsUnderground);
        Assert.Equal(MineProgress.Surface, progress.CurrentLayer);

        Assert.True(progress.Enter());
        Assert.Equal(1, progress.CurrentLayer);

        Assert.True(progress.Descend());
        Assert.Equal(2, progress.CurrentLayer);

        Assert.True(progress.Leave());
        Assert.False(progress.IsUnderground);

        // 已经在矿洞里再进入 = false（不是把层数重置回 1 这种「看起来也没事」的行为）
        progress.Enter();
        Assert.True(progress.Descend());
        Assert.False(progress.Enter());
        Assert.Equal(2, progress.CurrentLayer);

        // 已经在地面上再离开 = false
        Assert.True(progress.Leave());
        Assert.False(progress.Leave());
    }

    [Fact]
    public void 矿洞进度_下到最深层就下不去了()
    {
        var progress = new MineProgress(TestMine);
        progress.Enter();

        while (progress.Descend()) { }

        Assert.Equal(20, progress.CurrentLayer);
        Assert.False(progress.Descend());       // 不会越界到第 21 层
        Assert.Equal(20, progress.CurrentLayer);
    }

    [Fact]
    public void 矿洞进度_地面上的下一层与离开都是_false()
    {
        var progress = new MineProgress(TestMine);

        Assert.False(progress.Descend());
        Assert.False(progress.Leave());
        Assert.Equal(MineProgress.Surface, progress.CurrentLayer);
    }

    [Fact]
    public void 矿洞进度_电梯只停在有电梯的层()
    {
        var progress = new MineProgress(TestMine);
        progress.Enter();

        Assert.True(progress.HasElevatorAt(10));
        Assert.False(progress.HasElevatorAt(11));

        Assert.True(progress.TakeElevator(10));
        Assert.Equal(10, progress.CurrentLayer);

        // 第 11 层没有电梯：拒绝，且位置一动不动（不能「先跳过去再说」）
        Assert.False(progress.TakeElevator(11));
        Assert.Equal(10, progress.CurrentLayer);

        Assert.False(progress.TakeElevator(0));     // 0 是地面，不是电梯层
    }

    // ——— 存档 ———

    [Fact]
    public void 存档往返_矿洞层数经_SQLite_还原()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xing-combat-save-" + Guid.NewGuid().ToString("N"));

        try
        {
            var saves = new SqliteSaveService(directory);

            var original = new MineProgress(TestMine);
            original.Enter();
            for (int step = 0; step < 7; step++) original.Descend();

            saves.Save(1, new SaveMeta(20260911, "测试农场", "元年春 8 日 06:00"), new ISaveable[] { original });

            var restored = new MineProgress(TestMine);
            Assert.True(saves.Load(1, new ISaveable[] { restored }));

            Assert.Equal(8, restored.CurrentLayer);
            Assert.True(restored.IsUnderground);
        }
        finally
        {
            // SQLite 连接池可能还攥着文件句柄，清不掉临时目录不该让测试失败
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void 序列化往返_地面上与矿洞里的层数都还原()
    {
        var onSurface = new MineProgress(TestMine);
        var restoredSurface = new MineProgress(TestMine);
        restoredSurface.Deserialize(onSurface.Serialize(), onSurface.Version);
        Assert.Equal(MineProgress.Surface, restoredSurface.CurrentLayer);

        var underground = new MineProgress(TestMine);
        underground.Enter();
        underground.TakeElevator(20);

        var restored = new MineProgress(TestMine);
        restored.Deserialize(underground.Serialize(), underground.Version);
        Assert.Equal(20, restored.CurrentLayer);
    }

    [Theory]
    [InlineData("""{ "MineId": "mine_test", "CurrentLayer": 21 }""")]     // 超出 20 层
    [InlineData("""{ "MineId": "mine_test", "CurrentLayer": -1 }""")]      // 负层
    [InlineData("""{ "MineId": "mine_other", "CurrentLayer": 3 }""")]      // 别的矿洞的进度
    [InlineData("""{ "MineId": "", "CurrentLayer": 3 }""")]                // 缺矿洞 id
    public void 读档_坏值当场抛(string json)
    {
        var progress = new MineProgress(TestMine);

        Assert.Throws<InvalidDataException>(() => progress.Deserialize(json, progress.Version));
    }

    [Fact]
    public void 读档_存档版本高于当前_抛_NotSupported_不猜着读()
    {
        var progress = new MineProgress(TestMine);

        Assert.Throws<NotSupportedException>(() =>
            progress.Deserialize("""{ "MineId": "mine_test", "CurrentLayer": 3 }""", progress.Version + 1));
    }

    [Fact]
    public void 读档_内容为空_当场抛()
    {
        var progress = new MineProgress(TestMine);

        Assert.Throws<InvalidDataException>(() => progress.Deserialize("null", progress.Version));
    }
}
