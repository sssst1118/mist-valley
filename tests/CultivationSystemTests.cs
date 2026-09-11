using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using XingGame.Core.Time;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 玩家的灵根与境界（M3-1）：解锁判定与存档往返。
/// </summary>
/// <remarks>
/// 解锁判定是本切片对外的**唯一价值**（§8.2 末尾的三条「游戏绑定」），所以边界层数全部逐条测：
/// 3 层不能种灵植、4 层能；6 层不能进秘境、7 层能；4 层不能收徒、5 层能。
/// 这三对边界是**差一位就破坏玩法**的地方——门槛写成 5 层会让玩家多打坐半天还不知道为什么。
/// </remarks>
public class CultivationSystemTests
{
    private static readonly SpiritRootTable Roots = SpiritRootTable.LoadDefault();
    private static readonly RealmTable Realms = RealmTable.LoadDefault();
    private static readonly CultivationSpeedTable Speed = CultivationSpeedTable.LoadDefault(Realms);
    private static readonly SpiritPowerTable SpiritPower = SpiritPowerTable.LoadDefault();

    private static CultivationSystem NewSystem(
        string gradeId = "grade_false",
        string? rootId = null,
        string realmId = "qi_refining",
        int stage = 1) =>
        new(Roots, Realms, Speed, SpiritPower, gradeId, rootId, realmId, stage);

    // ── 读状态 ────────────────────────────────────────────────────────

    [Fact]
    public void 初始状态_灵根与境界读得出来()
    {
        CultivationSystem system = NewSystem("grade_true_dual", realmId: "qi_refining", stage: 4);

        Assert.Equal("grade_true_dual", system.Grade.Id);
        Assert.Equal(1.0, system.Grade.CultivationSpeedMultiplier);
        Assert.Null(system.Root);   // 普通品级没有具体灵根（§4.3/§4.4 只给变异与先天异）
        Assert.Equal("qi_refining", system.Realm.Id);
        Assert.Equal(4, system.Stage);
        Assert.Equal("四层", system.Realm.StageName(system.Stage));
        Assert.Equal("中期", system.Realm.BandAt(system.Stage)!.Name);
    }

    [Fact]
    public void 具体灵根_变异灵根带着它的特效数据一起读得出来()
    {
        CultivationSystem system = NewSystem("grade_mutation", rootId: "root_thunder");

        Assert.Equal("雷灵根", system.Root!.Name);
        Assert.Equal(2.5, system.Grade.CultivationSpeedMultiplier);   // §4.2 变异灵根那一档
        Assert.Equal("金 + 木", system.Root.MutationSource);
        Assert.Equal("对妖兽和魔修伤害 +50%", system.Root.GameEffect);
    }

    // ── 构造：坏参数当场抛 ────────────────────────────────────────────

    [Fact]
    public void 构造_品级或境界不在表里_当场抛()
    {
        // 表里没有的 id 是数据错误：不猜着读、也不退回某个默认档——退回默认会让「引用的灵根删了」
        // 这种事故表现为「玩家莫名其妙变成伪灵根」，谁都想不到是这里
        InvalidDataException unknownGrade = Assert.Throws<InvalidDataException>(() => NewSystem(gradeId: "grade_nope"));
        Assert.Contains("不在灵根表里", unknownGrade.Message, StringComparison.Ordinal);

        InvalidDataException unknownRoot =
            Assert.Throws<InvalidDataException>(() => NewSystem("grade_mutation", rootId: "root_nope"));
        Assert.Contains("不在灵根表里", unknownRoot.Message, StringComparison.Ordinal);

        InvalidDataException unknownRealm = Assert.Throws<InvalidDataException>(() => NewSystem(realmId: "realm_nope"));
        Assert.Contains("不在境界表里", unknownRealm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 构造_灵根与品级对不上_当场抛()
    {
        // root_sword 是先天异灵根（§4.4），记成变异灵根的话，修炼速度按变异算、特效按剑道算——
        // 两个字段单看都合法，只有放一起才矛盾，所以只能在这里拦
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => NewSystem("grade_mutation", rootId: "root_sword"));

        Assert.Contains("自相矛盾", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(14)]
    public void 构造_层号越界_当场抛(int stage)
    {
        // 构造参数是代码给的（常量或表里的值），写错是编程错误——所以是 ArgumentOutOfRangeException，
        // 与坏存档的 InvalidDataException 分开（ADR-009 的那条线）
        Assert.Throws<ArgumentOutOfRangeException>(() => NewSystem(stage: stage));
    }

    [Fact]
    public void 构造_层号上界跟着大境界走()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewSystem(realmId: "tribulation", stage: 3));

        CultivationSystem tribulation = NewSystem(realmId: "tribulation", stage: 2);
        Assert.Equal("渡劫中", tribulation.Realm.StageName(tribulation.Stage));
    }

    [Fact]
    public void 构造_表是null_抛()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CultivationSystem(null!, Realms, Speed, SpiritPower, "grade_false", null, "qi_refining", 1));
        Assert.Throws<ArgumentNullException>(
            () => new CultivationSystem(Roots, null!, Speed, SpiritPower, "grade_false", null, "qi_refining", 1));
        Assert.Throws<ArgumentNullException>(
            () => new CultivationSystem(Roots, Realms, null!, SpiritPower, "grade_false", null, "qi_refining", 1));
        Assert.Throws<ArgumentNullException>(
            () => new CultivationSystem(Roots, Realms, Speed, null!, "grade_false", null, "qi_refining", 1));
    }

    [Fact]
    public void 新档_修为从零起()
    {
        // 起点是构造出来的，不是练出来的：新档不该凭空带着一层进度
        Assert.Equal(0, NewSystem().Cultivation);
        Assert.Equal(0, NewSystem(stage: 7).Cultivation);
    }

    // ── §8.2 三条游戏绑定的解锁判定 ───────────────────────────────────

    [Theory]
    [InlineData(1, CultivationGate.SpiritPlanting, false)]     // §8.2：种植灵植要 4 层
    [InlineData(3, CultivationGate.SpiritPlanting, false)]
    [InlineData(4, CultivationGate.SpiritPlanting, true)]
    [InlineData(13, CultivationGate.SpiritPlanting, true)]
    [InlineData(6, CultivationGate.SecretRealm, false)]        // 进入秘境要 7 层
    [InlineData(7, CultivationGate.SecretRealm, true)]
    [InlineData(4, CultivationGate.OuterDisciple, false)]      // 招募外门弟子要 5 层
    [InlineData(5, CultivationGate.OuterDisciple, true)]
    public void 解锁_三条绑定在门槛层数的两侧(int stage, CultivationGate gate, bool expected)
    {
        Assert.Equal(expected, NewSystem(stage: stage).Meets(gate));
    }

    [Fact]
    public void 解锁_跨大境界算达到_筑基修士不会被炼气门槛挡住()
    {
        // §8.1 的表序就是修炼顺序。只比层号的话，筑基初期的「层号 1」会小于「4 层」，
        // 于是修为更高的玩家反而种不了灵植、进不了秘境——门槛把强的人挡在外面，最说不通
        CultivationSystem foundation = NewSystem("grade_true_dual", realmId: "foundation_establishment", stage: 1);

        Assert.True(foundation.Meets(CultivationGate.SpiritPlanting));
        Assert.True(foundation.Meets(CultivationGate.SecretRealm));
        Assert.True(foundation.Meets(CultivationGate.OuterDisciple));
    }

    [Fact]
    public void 解锁_炼气十三层仍然够不着更高境界的门槛()
    {
        // 反方向的边界：炼气大圆满也还没有筑基，所以「筑基初期」这个层次他还没到
        CultivationSystem qi = NewSystem(realmId: "qi_refining", stage: 13);

        Assert.True(qi.Reaches("qi_refining", 13));
        Assert.False(qi.Reaches("foundation_establishment", 1));
        Assert.False(qi.Reaches("tribulation", 1));
    }

    [Fact]
    public void 可达判定_坏境界与坏层号都当场抛()
    {
        // 门槛写错（「至少炼气 14 层」）不许静默为真，也不许夹到 13 层——两种都会让
        // 一条写错的门槛看起来正常工作
        CultivationSystem system = NewSystem(stage: 7);

        Assert.Throws<KeyNotFoundException>(() => system.Reaches("realm_nope", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Reaches("qi_refining", 14));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Reaches("tribulation", 3));
    }

    // ── 存档 ─────────────────────────────────────────────────────────

    [Fact]
    public void 存档键与版本()
    {
        CultivationSystem system = NewSystem();

        Assert.Equal("cultivation", system.SaveKey);
        // 3：M3-3 加了「灵力」一列（M3-2 的 2 是加了「修为」）。旧档的读法见 ReadCultivation
        // （Version 1 缺修为时读成 0）与 ReadSpirit（Version 1/2 缺灵力时读成满）
        Assert.Equal(3, system.Version);
    }

    [Fact]
    public void 存档往返_灵根与境界原样读回()
    {
        // 灵根与境界没有第二个来源算得出来，读不回来就是永久丢失——玩家会「一觉醒来变回伪灵根炼气一层」
        CultivationSystem saved = NewSystem("grade_innate", rootId: "root_star", realmId: "golden_core", stage: 3);
        CultivationSystem loaded = NewSystem();

        loaded.Deserialize(saved.Serialize(), saved.Version);

        Assert.Equal("grade_innate", loaded.Grade.Id);
        Assert.Equal("root_star", loaded.Root!.Id);
        Assert.Equal("星辰灵根", loaded.Root.Name);
        Assert.Equal("golden_core", loaded.Realm.Id);
        Assert.Equal(3, loaded.Stage);
        Assert.Equal("后期", loaded.Realm.StageName(3));
    }

    [Fact]
    public void 存档往返_修为原样读回()
    {
        // 修为是「练出来的」，没有第二个来源算得出来——读不回来就是白练。读档方用另一个起点构造，
        // 证明读回的是存档里的数而不是恰好相同的初始值
        CultivationSystem saved = NewSystem("grade_true_dual", realmId: "qi_refining", stage: 3);
        saved.Meditate(new GameTime(1, Season.Spring, 1, 6, 0), 60);   // 1.0x × 春 1.10 = 11 点

        CultivationSystem loaded = NewSystem("grade_innate", rootId: "root_star", realmId: "qi_refining", stage: 9);
        loaded.Deserialize(saved.Serialize(), saved.Version);

        Assert.Equal(11, saved.Cultivation);
        Assert.Equal(3, loaded.Stage);
        Assert.Equal(11, loaded.Cultivation);
    }

    [Fact]
    public void 旧档_Version1_没有修为字段_读成零并保留灵根与境界()
    {
        // M3-1 写下的 blob：那一版既没有修为这一列，也没有攒修为的入口（M3-1 刻意不建这些字段）。
        // 读成 0 是**迁移决定**，不是猜着读：那份存档里的玩家确实一点修为都没攒过，
        // 而修为的初值本来就该是 0。下一条用例是它的配对——**同样的缺席在 Version 2 里必须抛**，
        // 分界线是 fromVersion，不是「字段缺了就当 0」。
        const string version1 = """
            { "GradeId": "grade_mutation", "RootId": "root_ice", "RealmId": "qi_refining", "Stage": 6 }
            """;

        CultivationSystem loaded = NewSystem();
        loaded.Deserialize(version1, fromVersion: 1);

        Assert.Equal("grade_mutation", loaded.Grade.Id);
        Assert.Equal("root_ice", loaded.Root!.Id);
        Assert.Equal(6, loaded.Stage);
        Assert.Equal(0, loaded.Cultivation);
    }

    [Fact]
    public void 坏档_Version2_缺修为字段_当场抛()
    {
        // 本版本自己写出去的 blob 一定带着 cultivation：缺了说明这份数据不是本系统写的
        // （被人改过、或写到一半崩了）。这里若也读成 0，就等于把坏档悄悄当成「没练过」
        const string noCultivation = """
            { "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 1 }
            """;

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(() => NewSystem().Deserialize(noCultivation, fromVersion: 2));

        Assert.Contains("cultivation", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    // 负的修为：凭空倒扣，且会让升层判定永远不动
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 1, "Cultivation": -1 }""")]
    // 一层攒到 10 点：那是「该升二层」的账，不存在「停在一层还继续攒」的状态
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 1, "Cultivation": 10 }""")]
    // 十三层是顶点：没有「下一层」，修为只能是 0
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 13, "Cultivation": 1 }""")]
    // 筑基期不攒修为：升层开销表只覆盖炼气期（§8.4 的突破要丹药，不是攒够就升）
    [InlineData("""{ "GradeId": "grade_true_dual", "RootId": null, "RealmId": "foundation_establishment", "Stage": 1, "Cultivation": 5 }""")]
    public void 坏存档_修为不在本层该有的范围里_当场抛(string json)
    {
        Assert.Throws<InvalidDataException>(() => NewSystem().Deserialize(json, fromVersion: 2));
    }

    [Fact]
    public void 合法边界_差一点攒够是能读的()
    {
        // 上一条 theory 的另一半：判据是「>= 开销才算坏档」，差 1 点是合法状态——
        // 写成 > 的话，每个刚好攒够一层的档都会被当成坏档
        CultivationSystem loaded = NewSystem();
        loaded.Deserialize(
            """{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 1, "Cultivation": 9 }""",
            fromVersion: 2);

        Assert.Equal(9, loaded.Cultivation);
        Assert.Equal(1, loaded.Stage);
        Assert.Equal(100, loaded.Spirit);   // 这份 Version 2 的档没有灵力列 → 读成满（见 ReadSpirit）
    }

    [Fact]
    public void 存档往返_灵力原样读回()
    {
        // 灵力是**花得出去**的资源，读不回来等于玩家白花。读档方用另一个起点构造，
        // 而且存档里的数（115）与构造出来的初值（一层的 100）不同——否则这条会因为
        // 「恰好相等」而假绿（同修为往返那条的写法）
        CultivationSystem saved = NewSystem(stage: 4);   // 四层满池 175
        Assert.True(saved.TrySpendSpirit(60));           // → 115

        CultivationSystem loaded = NewSystem();
        loaded.Deserialize(saved.Serialize(), saved.Version);

        Assert.Equal(4, loaded.Stage);
        Assert.Equal(115, loaded.Spirit);
        Assert.Equal(175, loaded.MaxSpirit);             // 上限跟着层数回来，它不是存档里的一列
    }

    [Fact]
    public void 坏档_Version3_缺灵力字段_当场抛()
    {
        // 本版本自己写出去的 blob 一定带着 spirit：缺了说明这份数据不是本系统写的
        // （被人改过、或写到一半崩了）。这里若也读成「满」或 0，就等于把坏档悄悄当成没花过灵力
        const string noSpirit = """
            { "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 1, "Cultivation": 0 }
            """;

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(() => NewSystem().Deserialize(noSpirit, fromVersion: 3));

        Assert.Contains("spirit", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    // 负数：凭空倒扣，且会让「够不够花」的判断恒为假
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 1, "Cultivation": 0, "Spirit": -1 }""")]
    // 一层上限 100，记着 101
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 1, "Cultivation": 0, "Spirit": 101 }""")]
    // 四层上限 175（100 + 25 × 3），记着 176
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 4, "Cultivation": 0, "Spirit": 176 }""")]
    // 十三层上限 400，记着 401
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 13, "Cultivation": 0, "Spirit": 401 }""")]
    public void 坏存档_灵力不在本层该有的范围里_当场抛(string json)
    {
        // 越上限只可能来自「有人手改存档」或「上限系数被改小」——两种的正确处置都不是悄悄夹一刀
        Assert.Throws<InvalidDataException>(() => NewSystem().Deserialize(json, fromVersion: 3));
    }

    [Fact]
    public void 合法边界_刚好满池是能读的()
    {
        // 上一条 theory 的另一半：判据是「> 上限才算坏档」，满池正是最常见的那种档——
        // 写成 >= 的话，每一个睡过觉的存档都会被当成坏档
        CultivationSystem loaded = NewSystem();
        loaded.Deserialize(
            """{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 4, "Cultivation": 0, "Spirit": 175 }""",
            fromVersion: 3);

        Assert.Equal(4, loaded.Stage);
        Assert.Equal(175, loaded.Spirit);
    }

    [Fact]
    public void 存档_没有具体灵根时读回来还是null()
    {
        // 既不是「保留上一份」也不是「补一个默认值」：读档是整份覆盖，没写的部分就该是空
        CultivationSystem saved = NewSystem("grade_heaven", rootId: null, realmId: "qi_refining", stage: 9);
        CultivationSystem loaded = NewSystem("grade_innate", rootId: "root_sword", realmId: "tribulation", stage: 2);

        loaded.Deserialize(saved.Serialize(), saved.Version);

        Assert.Null(loaded.Root);
        Assert.Equal("grade_heaven", loaded.Grade.Id);
        Assert.Equal(9, loaded.Stage);

        using var document = JsonDocument.Parse(saved.Serialize());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("RootId").ValueKind);
    }

    [Theory]
    // 缺 stage（而层号合法值从 1 起，静默读成 0 会让玩家停在不存在的层次上）
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining" }""")]
    // 层号 0 / 越界 / 渡劫期只有两层
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 0 }""")]
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 14 }""")]
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "tribulation", "Stage": 3 }""")]
    // 缺 GradeId / RealmId
    [InlineData("""{ "RootId": null, "RealmId": "qi_refining", "Stage": 1 }""")]
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "Stage": 1 }""")]
    [InlineData("""{ "GradeId": "", "RootId": null, "RealmId": "qi_refining", "Stage": 1 }""")]
    // 表里没有的 id（多半是卸载了一个 Mod）
    [InlineData("""{ "GradeId": "grade_nope", "RootId": null, "RealmId": "qi_refining", "Stage": 1 }""")]
    [InlineData("""{ "GradeId": "grade_mutation", "RootId": "root_nope", "RealmId": "qi_refining", "Stage": 1 }""")]
    [InlineData("""{ "GradeId": "grade_false", "RootId": null, "RealmId": "realm_nope", "Stage": 1 }""")]
    // 灵根与品级对不上
    [InlineData("""{ "GradeId": "grade_false", "RootId": "root_star", "RealmId": "qi_refining", "Stage": 1 }""")]
    // 空内容
    [InlineData("null")]
    public void 坏存档_当场抛_InvalidDataException(string json)
    {
        Assert.Throws<InvalidDataException>(() => NewSystem().Deserialize(json, 1));
    }

    [Fact]
    public void 坏存档被拒后_原来的状态还在()
    {
        // 校验没过就一个字段都不该动——「读了一半」会让玩家掉进一个从没存在过的境界组合，
        // 而修为是练出来的：少一个点，玩家就是白坐了那一炷香
        CultivationSystem system = NewSystem("grade_true_dual", realmId: "qi_refining", stage: 5);
        system.Meditate(new GameTime(1, Season.Spring, 1, 6, 0), 60);

        Assert.Throws<InvalidDataException>(
            () => system.Deserialize("""{ "GradeId": "grade_nope", "RealmId": "qi_refining", "Stage": 1 }""", 2));

        Assert.Equal("grade_true_dual", system.Grade.Id);
        Assert.Equal("qi_refining", system.Realm.Id);
        Assert.Equal(5, system.Stage);
        Assert.Equal(11, system.Cultivation);
    }

    [Fact]
    public void 存档版本过高_抛_NotSupportedException()
    {
        // 来自更新版本的存档不能猜着读（ADR-009）。当前 Version 是 3，所以拿 4 来试——
        // 写 3 的话这条会变成「刚好等于当前版本也不许读」，与迁移用例直接矛盾
        Assert.Throws<NotSupportedException>(
            () => NewSystem().Deserialize(
                """{ "GradeId": "grade_false", "RootId": null, "RealmId": "qi_refining", "Stage": 1 }""",
                fromVersion: 4));
    }
}
