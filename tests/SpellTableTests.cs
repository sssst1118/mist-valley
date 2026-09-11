using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Systems.Cultivation;
using XingGame.Systems.Farming;

namespace XingGame.Tests;

/// <summary>
/// 生活法术表（M3-4）：四条法术的数值逐条对回 §8.2 与备案 #70/#75，坏表在加载时炸。
/// </summary>
/// <remarks>
/// <para>
/// 解锁层数与消耗是**差一位就破坏玩法**的地方（3 层能不能浇、4 层要花多少），所以逐条写出来对账，
/// 而不是「表里有个正数就算过」。
/// </para>
/// <para>
/// 轻身术与小回春术**刻意不在表里**——它们各自的系统还不存在，录进来就是两条没有调用方的死数据
/// （铁律 11）。这条否定式决定由下面读原始 JSON 的用例钉着：只盯代码看不出来，因为表里多一行
/// 加载器照样收。
/// </para>
/// </remarks>
public class SpellTableTests
{
    private static readonly RealmTable Realms = RealmTable.LoadDefault();
    private static readonly SpellTable Spells = SpellTable.LoadDefault(Realms);

    private const string Sense = "spell_spirit_sense";
    private const string Watering = "spell_spirit_watering";
    private const string Rain = "spell_spirit_rain";
    private const string Hoe = "spell_spirit_hoe";

    // ── 缺省表：逐条对文档 ────────────────────────────────────────────

    [Fact]
    public void 缺省表_确实是从工程里那份文件读出来的()
    {
        // LoadDefault 靠「从输出目录逐级上溯」找文件（技术债，M8 改注入）。找不到会抛，
        // 所以这条顺带守住「数据文件没被误删/改名」
        Assert.Equal(4, Spells.Spells.Count);
    }

    [Fact]
    public void 缺省表_四条法术的名字与效果逐条对_8_2_的游戏表现()
    {
        // §8.2 的「游戏表现」一列（docs/public/design.md 479-485 行）：
        // 1-3 层解锁“灵气感知”、4-6 层解锁“灵气浇灌”（消耗灵力代替浇水）、
        // 10-12 层解锁“灵雨术”（范围浇水）与“灵锄术”（范围耕地）
        Assert.Equal("灵气感知", Spells.Get(Sense).Name);
        Assert.Equal("灵气浇灌", Spells.Get(Watering).Name);
        Assert.Equal("灵雨术", Spells.Get(Rain).Name);
        Assert.Equal("灵锄术", Spells.Get(Hoe).Name);

        Assert.Equal(SpellEffect.Sense, Spells.Get(Sense).Effect);
        Assert.Equal(SpellEffect.Water, Spells.Get(Watering).Effect);
        Assert.Equal(SpellEffect.Water, Spells.Get(Rain).Effect);   // 「范围浇水」= 浇水 + 更大的范围
        Assert.Equal(SpellEffect.Till, Spells.Get(Hoe).Effect);
    }

    [Fact]
    public void 缺省表_解锁层数是各档的下沿_记的是层号不是档号()
    {
        // §8.2 把炼气期排成 1-3 / 4-6 / 7-9 / 10-12 / 13 五档，而门槛比较要的是层号：
        // 灵气感知在 1-3 档 ⇒ 1 层、灵气浇灌在 4-6 档 ⇒ 4 层、灵雨与灵锄在 10-12 档 ⇒ 10 层。
        // 存 4 而不是「4-6」，是因为档的上沿没有任何调用方读（见 SpellDefinition 的注释）
        Assert.Equal(("qi_refining", 1), (Spells.Get(Sense).UnlockRealmId, Spells.Get(Sense).UnlockStage));
        Assert.Equal(("qi_refining", 4), (Spells.Get(Watering).UnlockRealmId, Spells.Get(Watering).UnlockStage));
        Assert.Equal(("qi_refining", 10), (Spells.Get(Rain).UnlockRealmId, Spells.Get(Rain).UnlockStage));
        Assert.Equal(("qi_refining", 10), (Spells.Get(Hoe).UnlockRealmId, Spells.Get(Hoe).UnlockStage));
    }

    [Fact]
    public void 缺省表_消耗与范围逐条对备案_70_与_75()
    {
        // 备案 #75：灵气浇灌 **5 灵力/格**（而它是单格法术，所以标价就是 5）、
        // 灵雨术与灵锄术各 **50 灵力**、覆盖以目标格为中心的 **3×3 格**；
        // 备案 #70 的另外两个数（轻身术 10 / 小回春术 30）不在这里，见下一条用例
        Assert.Equal((0, 1), (Spells.Get(Sense).SpiritCost, Spells.Get(Sense).AreaSize));       // §8.2：可感知灵气但无法施法
        Assert.Equal((5, 1), (Spells.Get(Watering).SpiritCost, Spells.Get(Watering).AreaSize));
        Assert.Equal((50, 3), (Spells.Get(Rain).SpiritCost, Spells.Get(Rain).AreaSize));
        Assert.Equal((50, 3), (Spells.Get(Hoe).SpiritCost, Spells.Get(Hoe).AreaSize));
    }

    [Fact]
    public void 缺省表_顺序就是解锁顺序()
    {
        // 表是给人看的，也是给将来的法术列表 UI 排的：文件顺序 = §8.2 的解锁先后
        // （感知 1 层 → 浇灌 4 层 → 灵雨/灵锄 10 层）
        Assert.Equal(
            new[] { Sense, Watering, Rain, Hoe },
            Spells.Spells.Select(spell => spell.Id).ToArray());
    }

    [Fact]
    public void 缺省数据文件里没有轻身术与小回春术_刻意的()
    {
        // 备案 #70 那张表里有四个数，本切片只录了灵雨术 50 与灵锄术 50：
        // **轻身术（10）要有「施放 → 施加限时增益」那条路**（移速 +20% 是个限时增益），
        // **小回春术（30）要有「体力」这个属性**（这个游戏现在根本没有它）。
        // M3-6 把增益系统做出来了（`data/buffs/buffs.json` 里已有 `buff_light_body`：轻身术
        // ×1.2 持续 1 小时），**但法术表这条入口仍缺最后一段**：施放时要有一个「这条法术施加
        // 哪条增益」的效果种类（SpellEffect 的新成员），而那是施法那一刀的事。所以这里仍然不录
        // ——先录进来就是一条放不出效果的死数据（铁律 11），跟着施法那一刀一起补。
        // 这条读**原始 JSON**：加载器不认识的多余条目照样会收，只盯代码看不出来
        using var document = JsonDocument.Parse(File.ReadAllText(FindDefaultFile()));

        string[] ids = document.RootElement.GetProperty("spells").EnumerateArray()
            .Select(spell => spell.GetProperty("id").GetString()!)
            .ToArray();

        Assert.Equal(new[] { Sense, Watering, Rain, Hoe }, ids);

        // 负向对照：判据本身有效——真塞一条轻身术进去，它会被认出来
        string withLightBody = """
        { "spells": [ { "id": "spell_light_body", "name": "轻身术", "effect": "Sense",
                        "unlockRealmId": "qi_refining", "unlockStage": 7, "spiritCost": 10, "areaSize": 1 } ] }
        """;

        SpellTable standIn = SpellTable.FromJson(withLightBody, Realms);
        Assert.Contains("spell_light_body", standIn.Spells.Select(spell => spell.Id));
    }

    // ── 作用范围 ──────────────────────────────────────────────────────

    [Fact]
    public void 范围_单格法术只给中心那一格()
    {
        TileCoord center = new(3, 4);

        Assert.Equal(new[] { center }, Spells.Get(Watering).AreaFrom(center).ToArray());
    }

    [Fact]
    public void 范围_三乘三是以中心那一格为中心的九格()
    {
        // 备案 #75：3×3 覆盖**以目标格为中心**的一片——不是以它为一个角的 3×3
        TileCoord center = new(10, 10);

        TileCoord[] area = Spells.Get(Rain).AreaFrom(center).ToArray();

        Assert.Equal(9, area.Length);
        Assert.Equal(9, area.Distinct().Count());   // 没有重复格（重复就是同一格被上两次）
        Assert.All(area, tile => Assert.InRange(tile.X, 9, 11));
        Assert.All(area, tile => Assert.InRange(tile.Y, 9, 11));
        Assert.Contains(center, area);

        // 四个角与四条边都在（少一个角就是「3×3」被写成了十字）
        foreach (TileCoord corner in new[]
                 {
                     new TileCoord(9, 9), new TileCoord(9, 10), new TileCoord(9, 11),
                     new TileCoord(10, 9), new TileCoord(10, 11),
                     new TileCoord(11, 9), new TileCoord(11, 10), new TileCoord(11, 11),
                 })
        {
            Assert.Contains(corner, area);
        }
    }

    [Fact]
    public void 范围_负坐标与远离原点的格都算得对()
    {
        // 耕地是稀疏字典（未记录的格就是未开垦），没有「地图边界」这回事——所以负格与远处
        // 一样是合法的落点，换算不能假设坐标非负（草稿里写 (X + 1) / 2 这类取整就会在这里翻车）
        Assert.Equal(9, Spells.Get(Hoe).AreaFrom(new TileCoord(-1, -1)).Distinct().Count());
        Assert.Contains(new TileCoord(-2, -2), Spells.Get(Hoe).AreaFrom(new TileCoord(-1, -1)));
        Assert.Contains(new TileCoord(0, 0), Spells.Get(Hoe).AreaFrom(new TileCoord(-1, -1)));

        Assert.Contains(new TileCoord(1000, -1000), Spells.Get(Rain).AreaFrom(new TileCoord(999, -999)));
    }

    // ── 坏表：加载即抛 ────────────────────────────────────────────────

    [Theory]
    [InlineData("[]")]                                                          // 根不是对象
    [InlineData("{ }")]                                                         // 缺 spells
    [InlineData("{ \"spells\": 1 }")]                                           // spells 不是数组
    [InlineData("{ \"spells\": [] }")]                                          // 空表答不出任何问题
    [InlineData("{ \"spells\": [ 1 ] }")]                                       // 条目不是对象
    [InlineData("{ \"spells\": [ { \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"spiritCost\": 5, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"  \", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"spiritCost\": 5, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"spiritCost\": 5, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockStage\": 4, \"spiritCost\": 5, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Flying\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"spiritCost\": 5, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"0\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"spiritCost\": 5, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_nope\", \"unlockStage\": 4, \"spiritCost\": 5, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 0, \"spiritCost\": 5, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 14, \"spiritCost\": 5, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"tribulation\", \"unlockStage\": 3, \"spiritCost\": 5, \"areaSize\": 1 } ] }")]   // 渡劫期只有 2 个小境界
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"spiritCost\": -1, \"areaSize\": 1 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"spiritCost\": 5 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"spiritCost\": 5, \"areaSize\": 0 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"spiritCost\": 5, \"areaSize\": -3 } ] }")]
    [InlineData("{ \"spells\": [ { \"id\": \"spell_x\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\", \"unlockStage\": 4, \"spiritCost\": 5, \"areaSize\": 2 } ] }")]
    public void 坏表_结构或字段不对_当场抛(string json)
    {
        // 表是外部输入，错在表里就该在加载时炸——玩家不会替我们发现「炼气 14 层才解锁」这种数
        Assert.Throws<InvalidDataException>(() => SpellTable.FromJson(json, Realms));
    }

    [Fact]
    public void 坏表_重复的法术_id_当场抛()
    {
        const string entry =
            "{{ \"id\": \"{0}\", \"name\": \"某法术\", \"effect\": \"Water\", \"unlockRealmId\": \"qi_refining\","
            + " \"unlockStage\": 4, \"spiritCost\": {1}, \"areaSize\": 1 }}";

        // 重复 id 时「这条法术花多少」会取决于文件顺序——同一份表里一条 5 一条 50，
        // 玩家看到的价钱与代码读到的价钱可以不是一个（同 RealmTable 拦重复 id 的理由）
        string json = "{ \"spells\": [ " + string.Format(entry, "spell_x", 5) + ", " + string.Format(entry, "spell_x", 50) + " ] }";

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => SpellTable.FromJson(json, Realms));
        Assert.Contains("重复 id", error.Message, StringComparison.Ordinal);
        Assert.Contains("spell_x", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 取法术_未知_id_抛_KeyNotFoundException_且消息里指得出是哪个()
    {
        // 法术 id 来自本表（桥接层的快捷键绑的就是它），对不上就是 id 写错了——
        // 静默退回某条默认法术会让「按了 F 却放出灵雨术」这种事故查无对证
        KeyNotFoundException error = Assert.Throws<KeyNotFoundException>(() => Spells.Get("spell_nope"));
        Assert.Contains("spell_nope", error.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentNullException>(() => Spells.Get(null!));
    }

    /// <summary>与 <c>CultivationSpeedTableTests.FindDefaultFile</c> 同款的上溯，只为读那份原始 JSON。</summary>
    private static string FindDefaultFile()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, SpellTable.DefaultRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException($"未找到 {SpellTable.DefaultRelativePath}，本用例会变成假绿灯");
    }
}
