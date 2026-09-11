using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Systems.Combat;
using XingGame.Systems.Items;
using Xunit;

namespace XingGame.Tests;

/// <summary>
/// 战斗与矿洞的<b>静态数据</b>（M2-B）：怪物表与矿洞表。
/// </summary>
/// <remarks>
/// <para>
/// 这里守的是「数据表里不许出现文档没给的东西」。§7.1 只给了六个怪物名、§7.2 只给了掉落的大类，
/// 所以缺省怪物表里血量与攻击一律是 0、掉落一律为空——<b>这些 0 是哨兵值，不是数值</b>，
/// 用例把它们钉死，好让「顺手填个 10 点血」这种事在测试上立刻露馅。
/// </para>
/// <para>
/// 缺省数据一律经 <c>LoadDefault()</c> 读真文件（而不是把 JSON 抄进测试里）：抄一份就等于
/// 测的是测试里那份，数据文件改了测试照样绿。
/// </para>
/// </remarks>
public sealed class MonsterTableTests
{
    /// <summary>机制用例用的小物品表：够放下「有掉落」这种形态即可。</summary>
    private const string ItemsJson = """
    {
      "items": [
        { "id": "material_copper_ore", "name": "铜矿", "description": "测试用。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_gem",        "name": "宝石", "description": "测试用。", "category": "Material", "maxStack": 999, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);

    // ——— 缺省怪物表：逐条对文档 ———

    [Fact]
    public void 缺省怪物表_六只怪物逐条对文档()
    {
        MonsterTable table = MonsterTable.LoadDefault(ItemTable.LoadDefault());

        // §7.1 第 387 行的六个名字，顺序照抄：史莱姆、蝙蝠、骷髅、幽灵、史莱姆王、石魔（原文末尾有「等」）
        Assert.Equal(
            new[] { "史莱姆", "蝙蝠", "骷髅", "幽灵", "史莱姆王", "石魔" },
            table.All.Select(monster => monster.Name).ToArray());
    }

    [Fact]
    public void 缺省怪物表_血量攻击掉落层数一律是文档未给的哨兵值()
    {
        MonsterTable table = MonsterTable.LoadDefault(ItemTable.LoadDefault());

        // 「文档给了多少录多少」：这四样文档一个字都没给，所以一个数都不许有
        Assert.All(table.All, monster =>
        {
            Assert.Equal(0, monster.MaxHealth);
            Assert.Equal(0, monster.Attack);
            Assert.Empty(monster.Drops);
            Assert.Equal(0, monster.MinLayer);
            Assert.Equal(0, monster.MaxLayer);
        });
    }

    [Fact]
    public void 缺省怪物表_每条掉落都能在缺省物品表里找到()
    {
        ItemTable items = ItemTable.LoadDefault();

        // 缺省表目前没有掉落，这条现在是空转的；它守的是「将来补掉落时 id 必须对得上物品表」
        Assert.All(MonsterTable.LoadDefault(items).All, monster =>
            Assert.All(monster.Drops, drop => Assert.True(items.TryGet(drop.ItemId, out _))));
    }

    // ——— 掉落与物品表的交叉校验 ———

    [Fact]
    public void 掉落物品不在物品表里_加载时就抛()
    {
        const string json = """
        {
          "monsters": [
            { "id": "monster_slime", "name": "史莱姆", "maxHealth": 0, "attack": 0,
              "minLayer": 0, "maxLayer": 0,
              "drops": [ { "itemId": "material_nope", "count": 1 } ] }
          ]
        }
        """;

        // 掉落里写了一个物品表里没有的 id，玩家打死怪的那一刻才会发现东西没进背包——那种现场离病因太远
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => MonsterTable.FromJson(json, Items));

        Assert.Contains("material_nope", error.Message);
    }

    [Fact]
    public void 掉落数量非正_加载时就抛()
    {
        const string json = """
        {
          "monsters": [
            { "id": "monster_slime", "name": "史莱姆", "maxHealth": 0, "attack": 0,
              "minLayer": 0, "maxLayer": 0,
              "drops": [ { "itemId": "material_copper_ore", "count": 0 } ] }
          ]
        }
        """;

        Assert.Throws<InvalidDataException>(() => MonsterTable.FromJson(json, Items));
    }

    [Fact]
    public void 同一怪物重复写同一个掉落_加载时就抛()
    {
        const string json = """
        {
          "monsters": [
            { "id": "monster_slime", "name": "史莱姆", "maxHealth": 0, "attack": 0,
              "minLayer": 0, "maxLayer": 0,
              "drops": [ { "itemId": "material_copper_ore", "count": 1 },
                         { "itemId": "material_copper_ore", "count": 2 } ] }
          ]
        }
        """;

        // 两处数量谁生效取决于读的顺序，「这个怪掉几个铜矿」从此有两份真相
        Assert.Throws<InvalidDataException>(() => MonsterTable.FromJson(json, Items));
    }

    [Fact]
    public void 缺少_drops_数组_加载时就抛()
    {
        const string json = """
        {
          "monsters": [
            { "id": "monster_slime", "name": "史莱姆", "maxHealth": 0, "attack": 0, "minLayer": 0, "maxLayer": 0 }
          ]
        }
        """;

        // 字段缺失与「掉落为空」是两回事：前者是漏写，后者是明确写了「没有掉落」
        Assert.Throws<InvalidDataException>(() => MonsterTable.FromJson(json, Items));
    }

    // ——— 校验分支：非法值都要被拒 ———

    [Fact]
    public void 怪物表_重复_id_加载时就抛()
    {
        const string json = """
        {
          "monsters": [
            { "id": "monster_slime", "name": "史莱姆", "maxHealth": 0, "attack": 0, "minLayer": 0, "maxLayer": 0, "drops": [] },
            { "id": "monster_slime", "name": "史莱姆王", "maxHealth": 0, "attack": 0, "minLayer": 0, "maxLayer": 0, "drops": [] }
          ]
        }
        """;

        Assert.Throws<InvalidDataException>(() => MonsterTable.FromJson(json, Items));
    }

    [Theory]
    [InlineData("""{ "monsters": [ { "id": "", "name": "史莱姆", "maxHealth": 0, "attack": 0, "minLayer": 0, "maxLayer": 0, "drops": [] } ] }""")]
    [InlineData("""{ "monsters": [ { "id": "monster_slime", "name": "", "maxHealth": 0, "attack": 0, "minLayer": 0, "maxLayer": 0, "drops": [] } ] }""")]
    [InlineData("""{ "monsters": [ { "id": "monster_slime", "name": "史莱姆", "maxHealth": -1, "attack": 0, "minLayer": 0, "maxLayer": 0, "drops": [] } ] }""")]
    [InlineData("""{ "monsters": [ { "id": "monster_slime", "name": "史莱姆", "maxHealth": 0, "attack": -1, "minLayer": 0, "maxLayer": 0, "drops": [] } ] }""")]
    [InlineData("""{ "monsters": [ { "id": "monster_slime", "name": "史莱姆", "maxHealth": 0, "attack": 0, "minLayer": 0, "maxLayer": 10, "drops": [] } ] }""")]
    [InlineData("""{ "monsters": [ { "id": "monster_slime", "name": "史莱姆", "maxHealth": 0, "attack": 0, "minLayer": 10, "maxLayer": 5, "drops": [] } ] }""")]
    [InlineData("""{ "monsters": [ { "id": "monster_slime", "name": "史莱姆", "maxHealth": 0, "attack": 0, "minLayer": -1, "maxLayer": 5, "drops": [] } ] }""")]
    [InlineData("""{ "items": [] }""")]
    public void 怪物表_坏数据_加载时就抛(string json)
    {
        // 层数只给一个端点、min>max、min<1、空 id、空 name、负血、负攻击、缺 monsters 数组
        Assert.Throws<InvalidDataException>(() => MonsterTable.FromJson(json, Items));
    }

    // ——— 按层取怪物 ———

    [Fact]
    public void 按层取怪物_文档未给层数的怪在任何层都可能出现()
    {
        MonsterTable table = MonsterTable.LoadDefault(ItemTable.LoadDefault());

        // 「未给」不等于「哪层都没有」：否则缺省表里的怪永远掷不出来，遭遇系统等于没接上
        Assert.Equal(6, table.ForLayer(1).Count);
        Assert.Equal(6, table.ForLayer(60).Count);
        Assert.Equal(6, table.ForLayer(120).Count);
    }

    [Fact]
    public void 按层取怪物_有线层数的怪只在自己那几层出现_顺序照表()
    {
        const string json = """
        {
          "monsters": [
            { "id": "monster_slime", "name": "史莱姆", "maxHealth": 0, "attack": 0, "minLayer": 1, "maxLayer": 10, "drops": [] },
            { "id": "monster_golem", "name": "石魔",   "maxHealth": 0, "attack": 0, "minLayer": 100, "maxLayer": 120, "drops": [] }
          ]
        }
        """;

        MonsterTable table = MonsterTable.FromJson(json, Items);

        Assert.Equal(new[] { "monster_slime" }, table.ForLayer(1).Select(m => m.Id).ToArray());
        Assert.Equal(new[] { "monster_slime" }, table.ForLayer(10).Select(m => m.Id).ToArray());
        Assert.Empty(table.ForLayer(50));
        Assert.Equal(new[] { "monster_golem" }, table.ForLayer(120).Select(m => m.Id).ToArray());
    }

    [Fact]
    public void 按层取怪物_层号小于一_当场抛()
    {
        MonsterTable table = MonsterTable.LoadDefault(ItemTable.LoadDefault());

        Assert.Throws<ArgumentOutOfRangeException>(() => table.ForLayer(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.ForLayer(-1));
    }

    [Fact]
    public void 取怪物_未知_id_抛_KeyNotFound_且消息里带_id()
    {
        MonsterTable table = MonsterTable.LoadDefault(ItemTable.LoadDefault());

        Assert.False(table.TryGet("monster_nope", out _));

        KeyNotFoundException error = Assert.Throws<KeyNotFoundException>(() => table.Get("monster_nope"));
        Assert.Contains("monster_nope", error.Message);
    }

    // ——— 缺省矿洞表：逐条对文档 ———

    [Fact]
    public void 缺省矿洞表_层数与三种间隔逐条对文档()
    {
        MineTable table = MineTable.LoadDefault(ItemTable.LoadDefault());

        MineDefinition mine = Assert.Single(table.All);

        // §7.1：120 层、每 10 层电梯、每 10 层宝箱、每 30 层灵气浓郁区域（修炼 +50%）
        Assert.Equal(120, mine.LayerCount);
        Assert.Equal(10, mine.ElevatorInterval);
        Assert.Equal(10, mine.ChestInterval);
        Assert.Equal(30, mine.AuraInterval);
        Assert.Equal(50, mine.AuraBonusPercent);
    }

    [Fact]
    public void 缺省矿洞表_矿石六种按文档顺序且都在物品表里()
    {
        ItemTable items = ItemTable.LoadDefault();
        MineDefinition mine = MineTable.LoadDefault(items).Get("mine_valley");

        // §7.1「矿石：铜、铁、金、铱、煤、宝石」——顺序照抄，写的是物品 id
        Assert.Equal(
            new[] { "material_copper_ore", "material_iron_ore", "material_gold_ore", "material_iridium_ore", "material_coal", "material_gem" },
            mine.Ores.ToArray());

        Assert.All(mine.Ores, ore => Assert.True(items.TryGet(ore, out _)));
    }

    [Fact]
    public void 矿洞_电梯层宝箱层与灵气层按间隔判定()
    {
        MineDefinition mine = MineTable.LoadDefault(ItemTable.LoadDefault()).Get("mine_valley");

        Assert.True(mine.IsElevatorLayer(10));
        Assert.True(mine.IsElevatorLayer(120));
        Assert.False(mine.IsElevatorLayer(11));
        Assert.False(mine.IsElevatorLayer(0));      // 0 是地面，不是矿洞的层

        Assert.True(mine.IsChestLayer(10));
        Assert.False(mine.IsChestLayer(11));

        Assert.True(mine.IsAuraLayer(30));
        Assert.True(mine.IsAuraLayer(120));
        Assert.False(mine.IsAuraLayer(40));

        // 层号超界一律 false：判定「第 121 层有没有电梯」本身就是个不该问的问题
        Assert.False(mine.IsElevatorLayer(130));
        Assert.False(mine.IsChestLayer(130));
        Assert.False(mine.IsAuraLayer(130));
    }

    // ——— 矿洞表的校验分支 ———

    [Fact]
    public void 矿洞_矿石不在物品表里_加载时就抛()
    {
        const string json = """
        {
          "mines": [
            { "id": "mine_test", "name": "测试矿洞", "layerCount": 10, "elevatorInterval": 10,
              "chestInterval": 10, "auraInterval": 10, "auraBonusPercent": 50,
              "ores": ["material_nope"] }
          ]
        }
        """;

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => MineTable.FromJson(json, Items));

        Assert.Contains("material_nope", error.Message);
    }

    [Theory]
    [InlineData("""{ "mines": [ { "id": "", "name": "矿洞", "layerCount": 120, "elevatorInterval": 10, "chestInterval": 10, "auraInterval": 30, "auraBonusPercent": 50, "ores": [] } ] }""")]
    [InlineData("""{ "mines": [ { "id": "mine_test", "name": "矿洞", "layerCount": 0, "elevatorInterval": 10, "chestInterval": 10, "auraInterval": 30, "auraBonusPercent": 50, "ores": [] } ] }""")]
    [InlineData("""{ "mines": [ { "id": "mine_test", "name": "矿洞", "layerCount": 120, "elevatorInterval": 0, "chestInterval": 10, "auraInterval": 30, "auraBonusPercent": 50, "ores": [] } ] }""")]
    [InlineData("""{ "mines": [ { "id": "mine_test", "name": "矿洞", "layerCount": 120, "elevatorInterval": 121, "chestInterval": 10, "auraInterval": 30, "auraBonusPercent": 50, "ores": [] } ] }""")]
    [InlineData("""{ "mines": [ { "id": "mine_test", "name": "矿洞", "layerCount": 120, "elevatorInterval": 10, "chestInterval": 10, "auraInterval": 30, "auraBonusPercent": -1, "ores": [] } ] }""")]
    [InlineData("""{ "mines": [ { "id": "mine_test", "name": "矿洞", "layerCount": 120, "elevatorInterval": 10, "chestInterval": 10, "auraInterval": 30, "auraBonusPercent": 50, "ores": ["material_copper_ore", "material_copper_ore"] } ] }""")]
    [InlineData("""{ "mines": [ { "id": "mine_test", "name": "矿洞", "layerCount": 120, "elevatorInterval": 10, "chestInterval": 10, "auraInterval": 30, "auraBonusPercent": 50, "ores": [] }, { "id": "mine_test", "name": "矿洞二号", "layerCount": 120, "elevatorInterval": 10, "chestInterval": 10, "auraInterval": 30, "auraBonusPercent": 50, "ores": [] } ] }""")]
    [InlineData("""{ "monsters": [] }""")]
    public void 矿洞表_坏数据_加载时就抛(string json)
    {
        // 空 id、层数非正、间隔为 0（取余会炸）、间隔大于层数（永远碰不到）、负加成、重复矿石、重复 id
        Assert.Throws<InvalidDataException>(() => MineTable.FromJson(json, Items));
    }

    [Fact]
    public void 矿洞表_取未知_id_抛_KeyNotFound_且消息里带_id()
    {
        MineTable table = MineTable.LoadDefault(ItemTable.LoadDefault());

        Assert.False(table.TryGet("mine_nope", out _));

        KeyNotFoundException error = Assert.Throws<KeyNotFoundException>(() => table.Get("mine_nope"));
        Assert.Contains("mine_nope", error.Message);
    }
}
