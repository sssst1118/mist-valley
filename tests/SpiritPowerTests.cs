using System;
using System.Collections.Generic;
using XingGame.Core.Time;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 灵力池（M3-3）：当前值 / 上限 / 恢复 / 消耗原语。
/// </summary>
/// <remarks>
/// <para>
/// 两类用例混着用：**缺省表**（备案 #69/#71 的那组数）对契约值与手感，**替身表**算边界——
/// 上限与速率的字面量在替身里才掰得开（同 <c>MeditationTests</c> 的分工）。
/// 缺省表自己的数值由 <c>SpiritPowerTableTests</c> 逐条对备案。
/// </para>
/// <para>
/// <b>本类不碰「桥接层什么时候调哪个入口」</b>：本切片只给原语——接按键、接「灵气浇灌」都是
/// 法术那一刀的事（§8.2 的消耗常量刻意不在这里，见 <c>M3Audit_Cultivation</c>）。
/// </para>
/// </remarks>
public class SpiritPowerTests
{
    private static readonly SpiritRootTable Roots = SpiritRootTable.LoadDefault();
    private static readonly RealmTable Realms = RealmTable.LoadDefault();
    private static readonly CultivationSpeedTable Speed = CultivationSpeedTable.LoadDefault(Realms);
    private static readonly SpiritPowerTable SpiritPower = SpiritPowerTable.LoadDefault();

    private static GameTime Morning(int day = 1, Season season = Season.Spring) =>
        new(1, season, day, GameTime.FirstHour, 0);

    private static CultivationSystem NewSystem(
        int stage = 1,
        ISpiritPowerTable? spiritPower = null,
        ICultivationSpeedTable? speed = null) =>
        new(Roots, Realms, speed ?? Speed, spiritPower ?? SpiritPower,
            "grade_true_dual", rootId: null, "qi_refining", stage);

    // ── 上限：随层数派生 ─────────────────────────────────────────────

    [Fact]
    public void 新档_灵力是满的_上限随层数()
    {
        // 存档里只有「当前灵力」一个数，上限是层数算出来的（备案 #69）。三层锚点从系统这一侧
        // 再验一次：表那边的逐条对账证明公式对，这里证明**系统用的是层号**（用错字段会让
        // 十层修士拿到一层的池子）。新档取满的两条理由见 CultivationSystem 的构造注释
        Assert.Equal(100, NewSystem(stage: 1).MaxSpirit);
        Assert.Equal(175, NewSystem(stage: 4).MaxSpirit);
        Assert.Equal(400, NewSystem(stage: 13).MaxSpirit);

        Assert.Equal(100, NewSystem(stage: 1).Spirit);
        Assert.Equal(175, NewSystem(stage: 4).Spirit);
        Assert.Equal(400, NewSystem(stage: 13).Spirit);
    }

    [Fact]
    public void 上限_不在存档里_改了层数就跟着改()
    {
        // 上限是派生量：读一份十层的档进来，上限立刻是十层的（325），不是「存档里存着的那个数」。
        // 上限若也存一份，玩家换版本、改系数之后就会看到两条互相矛盾的数
        CultivationSystem saved = NewSystem(stage: 10);
        CultivationSystem loaded = NewSystem(stage: 1);

        loaded.Deserialize(saved.Serialize(), saved.Version);

        Assert.Equal(10, loaded.Stage);
        Assert.Equal(325, loaded.MaxSpirit);
        Assert.Equal(325, loaded.Spirit);
    }

    // ── 恢复：两个速率 + 睡眠 ───────────────────────────────────────

    [Fact]
    public void 恢复_清醒一小时两点_打坐一小时五点()
    {
        // 备案 #71 的两个数。同一次「一段时间过去了」，玩家在做什么决定回多少——
        // 这正是「做成显式入口」的意义：桥接层知道玩家在干什么，系统按它说的算
        CultivationSystem awake = NewSystem(stage: 4);
        CultivationSystem meditating = NewSystem(stage: 4);
        Awake(awake);
        Awake(meditating);

        Assert.Equal(2, awake.RecoverSpirit(SpiritRecovery.Awake, 60));
        Assert.Equal(5, meditating.RecoverSpirit(SpiritRecovery.Meditation, 60));

        // 打坐比清醒快，是备案 #71 那句「没灵力了就去打坐」的全部内容
        Assert.True(
            meditating.RecoverSpirit(SpiritRecovery.Meditation, 60) > awake.RecoverSpirit(SpiritRecovery.Awake, 60));
    }

    [Fact]
    public void 恢复_到上限就停_返回值是真正回上的点数()
    {
        // 返回值必须是**真正加进去的**：UI 若照着参数算「回了 150 点」，玩家会发现条子只涨了 20 点
        CultivationSystem system = NewSystem(stage: 4);   // 175
        Assert.True(system.TrySpendSpirit(170));          // → 5

        // 打坐 30 小时想回 150 点，位子有 170：照实回 150
        Assert.Equal(150, system.RecoverSpirit(SpiritRecovery.Meditation, 1800));
        Assert.Equal(155, system.Spirit);

        // 再坐 30 小时：位子只剩 20 了，想回 150 也只回得上 20——封顶发生在这里
        Assert.Equal(20, system.RecoverSpirit(SpiritRecovery.Meditation, 1800));
        Assert.Equal(175, system.Spirit);

        // 满了之后一点都不涨，也不抛——「睡了一觉但没满」不是错误状态
        Assert.Equal(0, system.RecoverSpirit(SpiritRecovery.Awake, 600));
        Assert.Equal(175, system.Spirit);
    }

    [Fact]
    public void 睡眠_从零到满_返回值是真的回上的点数()
    {
        // 备案 #71 的第三条：睡眠**全恢复**。它不按小时计价（TimeService.Sleep 是跳跃而非流逝），
        // 所以是零参数入口——传时长的那个版本根本表达不了「一觉睡到 6:00」
        CultivationSystem system = NewSystem(stage: 13);   // 400
        Assert.True(system.TrySpendSpirit(400));
        Assert.Equal(0, system.Spirit);

        Assert.Equal(400, system.RecoverSpiritOnSleep());
        Assert.Equal(400, system.Spirit);

        Assert.Equal(0, system.RecoverSpiritOnSleep());    // 满着睡：回 0 点，不抛
    }

    [Fact]
    public void 恢复_零分钟不涨也不抛()
    {
        // 「按了一下但没睡成/没坐成」：既不是错误，也不该有产出（同 Meditate 的零分钟）
        CultivationSystem system = NewSystem(stage: 4);
        Awake(system);

        Assert.Equal(0, system.RecoverSpirit(SpiritRecovery.Awake, 0));
        Assert.Equal(0, system.RecoverSpirit(SpiritRecovery.Meditation, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void 非法输入_负的恢复时长当场抛(int minutes)
    {
        // 负数会让灵力倒着扣：睡一觉掉几百点，玩家只会以为存档坏了
        CultivationSystem system = NewSystem(stage: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => system.RecoverSpirit(SpiritRecovery.Awake, minutes));
        Assert.Equal(175, system.Spirit);
    }

    [Fact]
    public void 非法输入_认不出的恢复档位当场抛()
    {
        // 枚举里没有的档位（比如从存档/Mod 那边混进来的数字）：数据缺口不猜，当场抛
        // （同 ICultivationSpeedTable.SeasonMultiplier 对没录的季节）
        CultivationSystem system = NewSystem();

        Assert.Throws<KeyNotFoundException>(
            () => system.RecoverSpirit((SpiritRecovery)9, 60));
    }

    [Fact]
    public void 零头_一次调用只舍一次_拆成很多次会舍掉更多()
    {
        // 结算四舍五入到整点（同 Meditate）：清醒 2 点/小时，一分钟就是 0.03 点 → 0，
        // 所以调用方要按**整段**传时长（一小时回 2 点、半小时回 1 点），别拿一分钟的碎片来喂。
        // **刻意不留「不足一点的零头」缓冲**：那会是一份没进存档的状态——存下去等于多一列要
        // 迁移的数据，不存就等于每次读档悄悄丢掉最多一点灵力
        CultivationSystem whole = NewSystem(stage: 4);
        Awake(whole);
        Assert.Equal(2, whole.RecoverSpirit(SpiritRecovery.Awake, 60));
        Assert.Equal(1, whole.RecoverSpirit(SpiritRecovery.Awake, 30));

        CultivationSystem split = NewSystem(stage: 4);
        Awake(split);
        int total = 0;
        for (int minute = 0; minute < 60; minute++) total += split.RecoverSpirit(SpiritRecovery.Awake, 1);

        Assert.Equal(0, total);
    }

    // ── 消耗：全有或全无 ────────────────────────────────────────────

    [Fact]
    public void 消耗_够就扣_不够就一点都不扣()
    {
        // 失败语义照 Wallet.TrySpendGold / Inventory.Remove 的既有惯例：不够就是**没发生**。
        // 两个调用方的共同点是「先比较、后相减」——反过来写（先扣再比）在不够时会留下一个
        // 已经被改小的池子，而那种状态谁都没见过
        CultivationSystem system = NewSystem(stage: 4);   // 175

        Assert.True(system.TrySpendSpirit(50));    // 灵雨术那一档（备案 #70）花得起
        Assert.Equal(125, system.Spirit);

        Assert.False(system.TrySpendSpirit(126));  // 只差一点
        Assert.Equal(125, system.Spirit);          // 一点都不扣

        Assert.True(system.TrySpendSpirit(125));   // 刚好够，清空
        Assert.Equal(0, system.Spirit);
        Assert.False(system.TrySpendSpirit(1));    // 空了之后连 1 点也花不出去
        Assert.Equal(0, system.Spirit);
    }

    [Fact]
    public void 消耗_超过上限的数量也照样是失败()
    {
        // 池子大小不参与判定：超过上限的数只可能是调用方算错了，但它与「不够」是同一种结局
        // （false、不扣），不该为它单独抛——法术表算错一个消耗不该把游戏崩掉
        CultivationSystem system = NewSystem(stage: 1);   // 100

        Assert.False(system.TrySpendSpirit(101));
        Assert.Equal(100, system.Spirit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void 非法输入_非正的消耗数量当场抛(int amount)
    {
        // 0 点消耗会让「施法成功」与「什么都没发生」长得一样；负数等于给玩家回灵力——
        // 两种都是调用方算错了（同 TrySpendGold 的判据）
        CultivationSystem system = NewSystem(stage: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => system.TrySpendSpirit(amount));
        Assert.Equal(175, system.Spirit);
    }

    // ── 升层与灵力 ──────────────────────────────────────────────────

    [Fact]
    public void 升层_上限涨了当前值补满_不是卡在旧上限上()
    {
        // 本切片的定论（理由见 CultivationSystem 的类注释）：升层那一刻灵力补到**新**上限。
        // 「只涨上限」会让玩家刚跨过一道台阶就看到灵力条短了一截——升层是奖励时刻，
        // 而那个观感正好相反
        CultivationSystem system = NewSystem(stage: 1, speed: PlainSpeed());
        Assert.True(system.TrySpendSpirit(100));   // 一层满池 100，花光

        Assert.Equal(10, system.Meditate(Morning(), 10));   // 替身表：一层要 10 点，刚好升到二层

        Assert.Equal(2, system.Stage);
        Assert.Equal(125, system.MaxSpirit);
        Assert.Equal(125, system.Spirit);
    }

    [Fact]
    public void 升层_一次跨几层_补的是最后一层的上限()
    {
        // 长时间打坐会一次跨过好几层（同 Meditate 的升层用例）。补满要补在**每一层**上：
        // 只在循环外补一次、或者拿旧层号算上限，都会让玩家停在「上限涨了、灵力没涨」上
        CultivationSystem system = NewSystem(stage: 1, speed: PlainSpeed());
        Assert.True(system.TrySpendSpirit(100));

        Assert.Equal(105, system.Meditate(Morning(), 105));   // 10+20+30+40 → 五层，余 5 点

        Assert.Equal(5, system.Stage);
        Assert.Equal(200, system.MaxSpirit);   // 100 + 25 × 4
        Assert.Equal(200, system.Spirit);
    }

    [Fact]
    public void 打坐只结修为_不动灵力_刻意的()
    {
        // 打坐的那份灵力在 RecoverSpirit(Meditation, …) 那一处，**刻意不并进 Meditate**（不是漏做）：
        // 两笔账的数据来源不同（§8.3 的速度表 vs 备案 #71），而且筑基及以上的打坐回气但结不了修为
        // （那边升层要 §8.4 的丹药，Meditate 当场抛）——并进去就没法表达这个状态。
        // 这条钉住分开的写法：合并回 Meditate 的那一天它会红，而那天必须同时想清楚筑基那边怎么办
        CultivationSystem system = NewSystem(stage: 4);
        Awake(system);
        int before = system.Spirit;

        system.Meditate(Morning(), 180);

        Assert.Equal(4, system.Stage);        // 33 点还不够四层升五层要的 40——这一炷香没升层
        Assert.Equal(before, system.Spirit);
    }

    // ── 数据驱动：数值全在表上 ──────────────────────────────────────

    [Fact]
    public void 数值都在表上_换个替身表就换一套数()
    {
        // 上限公式的系数与两个速率**一个都不许写死在系统里**（架构原则「数据驱动」）。
        // 名字检查看不出这件事（数字没有名字），只能行为性地验：换一张系数完全不同的表，
        // 系统给出的上限与恢复量必须整套跟着换。钉住它，改数据文件之外的路就都得先过这条
        ISpiritPowerTable stub = new PlainSpiritTable(baseSpirit: 7, perStage: 3, awake: 1, meditation: 9);
        CultivationSystem system = NewSystem(stage: 4, spiritPower: stub);

        Assert.Equal(16, system.MaxSpirit);          // 7 + 3 × 3，不是 175
        Assert.Equal(16, system.Spirit);

        Assert.True(system.TrySpendSpirit(10));      // → 6
        Assert.Equal(1, system.RecoverSpirit(SpiritRecovery.Awake, 60));
        Assert.Equal(7, system.Spirit);
        Assert.Equal(9, system.RecoverSpirit(SpiritRecovery.Meditation, 60));
        Assert.Equal(16, system.Spirit);             // 封顶用的是替身表的上限
    }

    /// <summary>把池子花掉一点，好让恢复有「位子」可回（满池恢复恒为 0，什么都验不出来）。</summary>
    private static void Awake(CultivationSystem system) =>
        Assert.True(system.TrySpendSpirit(system.MaxSpirit / 2));

    // ── 替身表 ──────────────────────────────────────────────────────

    /// <summary>手写的替身（不引第三方依赖）：上限 7 + 3 × (层 − 1)，清醒 1/小时、打坐 9/小时。</summary>
    private sealed class PlainSpiritTable : ISpiritPowerTable
    {
        private readonly int _baseSpirit;
        private readonly int _perStage;
        private readonly int _awake;
        private readonly int _meditation;

        public PlainSpiritTable(int baseSpirit, int perStage, int awake, int meditation)
        {
            _baseSpirit = baseSpirit;
            _perStage = perStage;
            _awake = awake;
            _meditation = meditation;
        }

        public int MaxSpiritAt(int stage) => stage >= 1
            ? _baseSpirit + _perStage * (stage - 1)
            : throw new ArgumentOutOfRangeException(nameof(stage), stage, "层号从 1 起");

        public int RecoveryPerHour(SpiritRecovery recovery) => recovery switch
        {
            SpiritRecovery.Awake => _awake,
            SpiritRecovery.Meditation => _meditation,
            _ => throw new KeyNotFoundException($"替身表里没有「{recovery}」"),
        };
    }

    /// <summary>
    /// 替身打坐表（同 <c>MeditationTests.PlainSpeedTable</c>）：基础 60 点/小时、倍率全 1，
    /// 于是「打坐几分钟就是几点修为」，升层的台阶一步一格看得清。
    /// </summary>
    private static ICultivationSpeedTable PlainSpeed() => new PlainSpeedTable();

    private sealed class PlainSpeedTable : ICultivationSpeedTable
    {
        private static readonly int[] Costs = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };

        public int BasePointsPerHour => 60;

        public IReadOnlyList<HourBand> HourBands => Array.Empty<HourBand>();

        public bool Covers(string realmId) => realmId == "qi_refining";

        public int PointsToAdvance(int stage) => stage >= 1 && stage <= Costs.Length
            ? Costs[stage - 1]
            : throw new ArgumentOutOfRangeException(nameof(stage), stage, "这一层没有下一层");

        public double SeasonMultiplier(Season season) => 1.0;

        public double HourMultiplier(int hour) => 1.0;
    }
}
