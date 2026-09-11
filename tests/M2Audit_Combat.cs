using System;
using System.Collections.Generic;
using System.IO;
using XingGame.Systems.Combat;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 战斗与矿洞的对抗性审计（M2）。既有用例已经把「同参数必得同结果」「掉落不静默吞掉」
/// 钉住了，这里只补两条：掷遭遇是<b>纯查询</b>（换个调用顺序不改结果、也不碰矿洞进度），
/// 以及备案 #53「战斗模块不认识金币」这条否定式决定。
/// </summary>
public class M2Audit_Combat
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "material_copper_ore", "name": "铜矿", "description": "审计用。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_gem",        "name": "宝石", "description": "审计用。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private const string MonstersJson = """
    {
      "monsters": [
        { "id": "monster_twin",  "name": "镜像", "maxHealth": 10, "attack": 5,  "minLayer": 0,  "maxLayer": 0,
          "drops": [ { "itemId": "material_copper_ore", "count": 2 } ] },
        { "id": "monster_slime", "name": "史莱姆", "maxHealth": 20, "attack": 5,  "minLayer": 1,  "maxLayer": 5,
          "drops": [ { "itemId": "material_copper_ore", "count": 2 } ] },
        { "id": "monster_golem", "name": "石魔", "maxHealth": 40, "attack": 12, "minLayer": 6,  "maxLayer": 10,
          "drops": [ { "itemId": "material_gem", "count": 1 } ] }
      ]
    }
    """;

    private const string MinesJson = """
    {
      "mines": [
        { "id": "mine_test", "name": "测试矿洞", "layerCount": 10, "elevatorInterval": 5,
          "chestInterval": 5, "auraInterval": 10, "auraBonusPercent": 50,
          "ores": [ "material_copper_ore", "material_gem" ] }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);
    private static readonly MonsterTable Monsters = MonsterTable.FromJson(MonstersJson, Items);
    private static readonly MineDefinition Mine = MineTable.FromJson(MinesJson, Items).Get("mine_test");

    private static CombatSystem NewSystem(out MineProgress progress)
    {
        progress = new MineProgress(Mine);
        return new CombatSystem(Monsters, progress, new Inventory(Items, slotCount: 4));
    }

    [Fact]
    public void 掷遭遇_与调用顺序无关_且不改动矿洞进度()
    {
        int[] layers = { 1, 5, 3, 9, 2, 5, 1 };

        CombatSystem forwards = NewSystem(out MineProgress progress);
        var first = new List<string?>();
        foreach (int layer in layers) first.Add(forwards.RollEncounter(layer, seed: 99)?.Id);

        CombatSystem backwards = NewSystem(out _);
        var second = new string?[layers.Length];
        for (int index = layers.Length - 1; index >= 0; index--)
            second[index] = backwards.RollEncounter(layers[index], seed: 99)?.Id;

        // 掷怪是纯查询：同一 (层, 种子) 的结果不能取决于「之前掷过哪几层」
        Assert.Equal(first, second);

        // 也不该顺手把玩家挪下去——那是 Enter/Descend 的事
        Assert.False(progress.IsUnderground);
        Assert.Equal(MineProgress.Surface, progress.CurrentLayer);
    }

    [Fact]
    public void 矿洞存档_缺少CurrentLayer字段时_抛而不是静默回到地面()
    {
        var progress = new MineProgress(Mine);
        Assert.True(progress.Enter());
        Assert.True(progress.Descend());

        // 0 是「在地面」，是合法层号，所以「字段不在」必须与「字段是 0」分开（ADR-009）。
        // 静默读成 0 等于把矿洞里的档悄悄挪回地面——玩家再下去得重走一遍，且毫无提示
        Assert.Throws<InvalidDataException>(
            () => progress.Deserialize("""{ "MineId": "mine_test" }""", 1));

        Assert.Equal(2, progress.CurrentLayer);   // 抛之前不许动内存里那份
    }

    [Fact]
    public void 战斗模块不认识金币_掉落与结果里都没有钱()
    {
        // 备案 #53：金币归经济模块（§7.1「昏倒损失金币和物品」的惩罚也不在这里），
        // 战斗模块连玩家一侧的数值都是调用方传进来的。谁把金币加进结果或怪物定义，这条会红——
        // 这是刻意的，不是漏做（用反射钉住否定式决定，同 EconomyTests 的灵石那两条）
        foreach (Type type in new[]
                 {
                     typeof(CombatResult), typeof(MonsterDefinition), typeof(CombatStats),
                     typeof(ICombatSystem), typeof(IMonsterTable),
                 })
        {
            Assert.DoesNotContain(type.GetMembers(), member =>
                member.Name.Contains("Gold", StringComparison.Ordinal) ||
                member.Name.Contains("Coin", StringComparison.Ordinal) ||
                member.Name.Contains("Money", StringComparison.Ordinal));
        }
    }
}
