using System;
using System.Collections.Generic;
using XingGame.Core.Time;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 打坐与修为累积（M3-2）。这是本切片对外的**唯一价值**：玩家第一次能真的把修为练上去。
/// </summary>
/// <remarks>
/// <para>
/// 两类用例混着用：**缺省表**（<c>data/cultivation/cultivation_speed.json</c>）算真实的手感——
/// 一个春季能不能走完炼气期、伪灵根要几季，这些是跨模块的行为断言，比单测某个函数值钱；
/// **替身表**（基础 1 点/小时、倍率全 1）算边界——刚好够、差 1 点、顶点封顶，这些数不掰开揉碎
/// 就没法钉准。缺省表自己的数值由 <c>CultivationSpeedTableTests</c> 逐条对文档。
/// </para>
/// <para>
/// 时刻一律用 <see cref="GameTime"/> 明写（§3.1 的清晨 6:00 起坐是常见形状），**不读时钟**：
/// 打坐的收益是传入时刻的纯函数，测试才能靠「同参同果」。
/// </para>
/// </remarks>
public class MeditationTests
{
    private static readonly SpiritRootTable Roots = SpiritRootTable.LoadDefault();
    private static readonly RealmTable Realms = RealmTable.LoadDefault();
    private static readonly CultivationSpeedTable Speed = CultivationSpeedTable.LoadDefault(Realms);
    private static readonly SpiritPowerTable SpiritPower = SpiritPowerTable.LoadDefault();

    /// <summary>§3.1 的清晨：6:00-9:00 是「打坐修炼」那一段，季节与时辰的倍率都在这里显形。</summary>
    private static GameTime Morning(int day = 1, Season season = Season.Spring) =>
        new(1, season, day, GameTime.FirstHour, 0);

    private static CultivationSystem NewSystem(
        string gradeId = "grade_true_dual",
        string realmId = "qi_refining",
        int stage = 1,
        ICultivationSpeedTable? speed = null) =>
        // 灵脉那一项一律喂替身（×1.0）：§4.2 / §8.3 / 备案 #67/#68 那几组数都不含灵脉，
        // 本文件每个断言里的数都要逐项对得上它们的出处。农场自带的那一成（微型 1.10）与
        // 「灵脉升一级就更快」在 SpiritLandTests 里量——见 NoSpiritVein 的注释
        new(Roots, Realms, speed ?? Speed, SpiritPower, new NoSpiritVein(), new NoSpeedBonus(), gradeId, rootId: null, realmId, stage);

    // ── 一次打坐到底涨多少 ────────────────────────────────────────────

    [Fact]
    public void 打坐一小时_基础十点乘上灵根与季节()
    {
        // 备案 #68 的 10 点/小时 × §4.2 双灵根 1.0x × §8.3 春季 1.10 = 11 点。
        // 换成夏（1.05）就是 10.5 → 11，换成冬（0.90）是 9——四季不是同一个数，
        // 这正是「季节是玩法的一部分」而不是装饰
        CultivationSystem spring = NewSystem();
        CultivationSystem summer = NewSystem();
        CultivationSystem winter = NewSystem();

        Assert.Equal(11, spring.Meditate(Morning(season: Season.Spring), 60));
        Assert.Equal(11, summer.Meditate(Morning(season: Season.Summer), 60));
        Assert.Equal(9, winter.Meditate(Morning(season: Season.Winter), 60));

        // 十一层打坐一小时攒下的数原样留在「本层进度」上（五层要 50 点，还不到台阶）；
        // 一层那一柱香则当场升到二层、只余 1 点——攒够就交出去，见升层那几条用例
        CultivationSystem held = NewSystem(stage: 5);
        held.Meditate(Morning(), 60);
        Assert.Equal(11, held.Cultivation);
        Assert.Equal(5, held.Stage);
    }

    [Fact]
    public void 打坐_灵根档位直接把进度拉开几倍()
    {
        // §4.2 的六档倍率：伪 0.3x / 双 1.0x / 天 2.0x。同一柱香，三档差出好几倍——
        // 「灵根决定修炼速度」是 §4.2 全部存在的意义，掉在这里就是掉在玩法最中心
        int weak = NewSystem("grade_false").Meditate(Morning(), 60);
        int normal = NewSystem("grade_true_dual").Meditate(Morning(), 60);
        int gifted = NewSystem("grade_heaven").Meditate(Morning(), 60);

        Assert.Equal(3, weak);       // 10 × 0.3 × 1.10 = 3.3 → 3
        Assert.Equal(11, normal);    // 10 × 1.0 × 1.10 = 11
        Assert.Equal(22, gifted);    // 10 × 2.0 × 1.10 = 22
    }

    [Fact]
    public void 打坐_子时比清晨快_午时居中()
    {
        // §8.3「子时（23:00-1:00）+30%，午时（11:00-13:00）+20%」。清晨（§3.1 的正牌打坐时段）
        // 没有时辰加成，子时反而更快——这是有意的：夜里入定是修仙的老规矩
        CultivationSystem midnight = NewSystem();
        CultivationSystem noon = NewSystem();
        CultivationSystem morning = NewSystem();

        int atMidnight = midnight.Meditate(new GameTime(1, Season.Spring, 1, 23, 0), 60);
        int atNoon = noon.Meditate(new GameTime(1, Season.Spring, 1, 12, 0), 60);
        int atMorning = morning.Meditate(Morning(), 60);

        Assert.Equal(14, atMidnight);   // 10 × 1.0 × 1.10 × 1.30 = 14.3 → 14
        Assert.Equal(13, atNoon);       // 10 × 1.0 × 1.10 × 1.20 = 13.2 → 13
        Assert.Equal(11, atMorning);    // ×1.00
    }

    // ── 倍率的合成：相乘，不相加 ──────────────────────────────────────

    [Fact]
    public void 倍率_三个因素相乘而不是相加()
    {
        // §8.3 把每一项写成独立的加成，同时成立时是连乘：双灵根 1.0 × 春 1.10 × 子时 1.30 = 1.43。
        // 相加会得到 1.40——两个数在纸面上很像，但因素越多偏得越远（再叠一层灵脉 +10% 就是
        // 1.573 对 1.50），而偏差只在两个加成同时出现时才显形
        CultivationSystem system = NewSystem();

        double multiplier = system.SpeedMultiplierAt(new GameTime(1, Season.Spring, 1, 23, 0));

        Assert.Equal(1.43, multiplier, precision: 10);
        Assert.NotEqual(1.40, multiplier, precision: 10);

        // 再换一组，让「相加」的答案差得更远：伪灵根 0.3 × 冬 0.90 × 午时 1.20 = 0.324（相加是 0.40）
        double another = NewSystem("grade_false")
            .SpeedMultiplierAt(new GameTime(1, Season.Winter, 1, 12, 0));

        Assert.Equal(0.324, another, precision: 10);
        Assert.NotEqual(0.40, another, precision: 10);
    }

    [Fact]
    public void 倍率_没加成的时刻就是灵根与季节两项()
    {
        // 时辰那一项在平峰是 1.00，不是 0——写成「没加成就不乘」会得到同一个数，
        // 但写成「加上 0%」的实现在这里也一样绿：所以下面用子时那一条来抓后者（见上一条用例）
        Assert.Equal(1.10, NewSystem().SpeedMultiplierAt(Morning()), precision: 10);
        Assert.Equal(1.10, NewSystem("grade_true_dual").SpeedMultiplierAt(Morning(season: Season.Autumn)), precision: 10);
    }

    // ── 跨模块自洽：§8.1 的进度锚点 ───────────────────────────────────

    [Fact]
    public void 自洽_双灵根每天三小时_一个春季之内走完炼气十三层()
    {
        // 备案 #67/#68 的推导锚点：炼气期要在「第 1 年春季」（§8.1）之内走完（§3.2 每季 28 天，
        // §3.1 清晨打坐）。1.0x 灵根不含季节加成是 78 小时；春季 +10% 之下每天三小时是 33 点，
        // 24 天（72 小时）走完——正好装进 84 小时的春季，还留了几天余量
        CultivationSystem system = NewSystem();
        int days = 0;

        while (system.Stage < 13 && days < 28)
        {
            days++;
            system.Meditate(Morning(days), 180);
        }

        Assert.Equal(13, system.Stage);
        Assert.True(days <= 28, $"走完炼气期用了 {days} 天，超出一个春季（§8.1 的进度锚点）");

        // 走完的那一刻修为清零：十三层是顶点，没有「下一层」可以攒
        Assert.Equal(0, system.Cultivation);
    }

    [Fact]
    public void 自洽_伪灵根要走三季以上_对得上终生止步炼气期()
    {
        // 反向锚点：§4.2 的 0.3x。按春季的速度（10 × 0.3 × 1.10 = 3.3 → 3 点/小时）要 260 小时，
        // 也就是每天三小时要小半年——对上备案 #68 表里「三季以上」那行
        CultivationSystem system = NewSystem("grade_false");
        int hours = 0;

        while (system.Stage < 13 && hours < 400)
        {
            hours++;
            system.Meditate(Morning(), 60);
        }

        Assert.Equal(13, system.Stage);
        Assert.Equal(260, hours);

        // 一个春季（28 天 × 3 小时 = 84 小时）远不够：按每天 3 小时算，一季只攒到 280 点，
        // 也就是八层上下（10+20+…+70 = 280），离十三层的 780 点差着两个季节
        CultivationSystem firstSpring = NewSystem("grade_false");
        for (int day = 1; day <= 28; day++) firstSpring.Meditate(Morning(day), 180);

        Assert.True(firstSpring.Stage < 13, $"一个春季就走完炼气期了（第 {firstSpring.Stage} 层）——与备案 #68 那组数对不上");
    }

    // ── 升层的台阶：刚好够、差一点、封顶 ──────────────────────────────

    [Fact]
    public void 升层_差一点就停在原地_刚好够就上去()
    {
        // 替身表：基础 1 点/分钟、倍率全 1，于是「打坐几分钟 = 几点修为」，边界才算得准
        // （缺省表的季节与灵根小数会掺进来）。一层要 10 点，所以 9 分钟差一点、10 分钟刚好
        CultivationSystem system = NewSystem(speed: PlainSpeed());

        Assert.Equal(9, system.Meditate(Morning(), 9));
        Assert.Equal(1, system.Stage);
        Assert.Equal(9, system.Cultivation);

        Assert.Equal(1, system.Meditate(Morning(), 1));
        Assert.Equal(2, system.Stage);
        Assert.Equal(0, system.Cultivation);   // 台阶是扣掉制：攒够就交出去，不是累加记总数
    }

    [Fact]
    public void 升层_一次打坐可以连上几层_每层各扣各的()
    {
        // 长时间打坐（或者将来某个高倍率场景）会一次跨过好几层。扣错顺序——比如拿总开销乘层数、
        // 或者只扣第一层的开销——会让「一次练很久」反而升得慢，而短时间打坐时看不出来
        CultivationSystem system = NewSystem(speed: PlainSpeed());

        Assert.Equal(105, system.Meditate(Morning(), 105));

        // 10 + 20 + 30 + 40 = 100 升到五层，余 5 点（五层要 50，还不够）
        Assert.Equal(5, system.Stage);
        Assert.Equal(5, system.Cultivation);
    }

    [Fact]
    public void 封顶_十三层之后修为不再涨_返回值随之为零()
    {
        // §8.1 的炼气期到十三层为止（往上就是 §8.4 的筑基，那是丹药的事）。
        // 顶点没有「下一层」，攒下的修为无处可去——溢出的部分作废，也**不算进返回值**：
        // 否则 UI 会告诉玩家「修为 +500」，而存档里一点都没多
        CultivationSystem system = NewSystem(stage: 12, speed: PlainSpeed());

        Assert.Equal(120, system.Meditate(Morning(), 500));   // 只记下十二层升上去要的那 120 点
        Assert.Equal(13, system.Stage);
        Assert.Equal(0, system.Cultivation);

        Assert.Equal(0, system.Meditate(Morning(), 100));     // 已经在顶点：再坐也没有产出
        Assert.Equal(13, system.Stage);
        Assert.Equal(0, system.Cultivation);
    }

    [Fact]
    public void 封顶_一开始就在十三层_打坐不抛也不涨()
    {
        // 缺省表上的同一条边界：顶点不是错误状态（玩家就是练到头了），所以不抛异常，
        // 只是「这一炷香白坐了」——抛异常会让桥接层每次按键都崩
        CultivationSystem system = NewSystem(stage: 13);

        Assert.Equal(0, system.Meditate(Morning(), 180));
        Assert.Equal(13, system.Stage);
        Assert.Equal(0, system.Cultivation);
    }

    // ── 还没到那条路：筑基及以后 ──────────────────────────────────────

    [Fact]
    public void 跨大境界_筑基修士打坐当场抛_而不是攒一批用不上的修为()
    {
        // §8.4：炼气 → 筑基要筑基丹、可能引动雷劫，**不是攒够就升**。所以筑基期上「打坐涨修为」
        // 这件事本身没有定义。这里当场抛而不是默默攒着：攒下来的数等 M4 落地时是留是弃没人说得清
        CultivationSystem system = NewSystem(realmId: "foundation_establishment", stage: 1);

        NotSupportedException exception =
            Assert.Throws<NotSupportedException>(() => system.Meditate(Morning(), 60));

        Assert.Contains("§8.4", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, system.Cultivation);
    }

    // ── 非法输入 ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void 非法输入_负的打坐时长当场抛(int minutes)
    {
        // 负数会让修为倒着扣：练一炷香掉几百点，玩家只会以为存档坏了
        CultivationSystem system = NewSystem();

        Assert.Throws<ArgumentOutOfRangeException>(() => system.Meditate(Morning(), minutes));
        Assert.Equal(0, system.Cultivation);
    }

    [Fact]
    public void 零分钟_不涨也不抛()
    {
        // 「按了一下但没坐成」：既不是错误，也不该有产出
        CultivationSystem system = NewSystem();

        Assert.Equal(0, system.Meditate(Morning(), 0));
        Assert.Equal(1, system.Stage);
        Assert.Equal(0, system.Cultivation);
    }

    [Fact]
    public void 非法时刻_不存在的钟点与季节都当场抛()
    {
        // GameTime 是个 record struct，构造时可以塞进任意数字（同层号越界那类「写错就是写错」）。
        // 静默按平峰算等于把调用方算错的时刻藏起来——「修炼速度偶尔不对」是最难查的那种 bug
        CultivationSystem system = NewSystem();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => system.Meditate(new GameTime(1, Season.Spring, 1, 25, 0), 60));

        Assert.Throws<KeyNotFoundException>(
            () => system.Meditate(new GameTime(1, (Season)9, 1, 6, 0), 60));

        Assert.Equal(0, system.Cultivation);
    }

    // ── 结算的粒度：一次调用只舍一次零头 ──────────────────────────────

    [Fact]
    public void 零头_一次调用只舍一次_拆成很多次会舍掉更多()
    {
        // 结果四舍五入到整点：一小时 11 点，一分钟就是 0.18 点 → 0。所以「一次打坐」要按整段传时长，
        // 逐分钟调用会把每一段的零头都舍掉。这条把约定钉住：桥接层将来接按键时，收的是整段时长
        CultivationSystem whole = NewSystem();
        CultivationSystem split = NewSystem();

        Assert.Equal(11, whole.Meditate(Morning(), 60));

        int total = 0;
        for (int minute = 0; minute < 60; minute++) total += split.Meditate(Morning(), 1);

        Assert.Equal(0, total);
        Assert.Equal(0, split.Cultivation);
        Assert.Equal(1, split.Stage);   // 一小时拆成六十次，一次一层都升不上去
    }

    [Fact]
    public void 整段时间按传入时刻计价_不中途分段()
    {
        // 22:00 起坐三小时会横跨到子时（23:00）里。文档没有规定跨段怎么切，本系统的定论是
        // 「按开始那一刻算」——所以它与清晨那柱香涨得一样多，而整段落在子时里的同长打坐要多
        // （尾数是四舍五入的产物：42.9 → 43）。若中途换段，玩家会看到「同样的三小时，
        // 22:00 开始比 6:00 开始多」，而那个差额没法从文档里推出来
        CultivationSystem crossing = NewSystem();
        CultivationSystem morning = NewSystem();

        int at22 = crossing.Meditate(new GameTime(1, Season.Spring, 1, 22, 0), 180);
        int at6 = morning.Meditate(Morning(), 180);

        Assert.Equal(33, at22);                       // 10 × 1.10 × 3 小时（22:00 是平峰）
        Assert.Equal(at6, at22);

        Assert.Equal(43, NewSystem().Meditate(new GameTime(1, Season.Spring, 1, 23, 0), 180));   // ×1.30
    }

    // ── 替身表 ───────────────────────────────────────────────────────

    /// <summary>
    /// 手写的替身（不引第三方依赖，同全仓的测试替身写法）：基础 1 点/**分钟**、倍率全 1.0，
    /// 于是「打坐几分钟就是几点修为」，升层的台阶一步一格看得清。
    /// </summary>
    private static ICultivationSpeedTable PlainSpeed() => new PlainSpeedTable(
        10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120);

    private sealed class PlainSpeedTable : ICultivationSpeedTable
    {
        private readonly int[] _costs;

        public PlainSpeedTable(params int[] costs) => _costs = costs;

        public int BasePointsPerHour => 60;

        public IReadOnlyList<HourBand> HourBands => Array.Empty<HourBand>();

        public bool Covers(string realmId) => realmId == "qi_refining";

        public int PointsToAdvance(int stage) => stage >= 1 && stage <= _costs.Length
            ? _costs[stage - 1]
            : throw new ArgumentOutOfRangeException(nameof(stage), stage, "这一层没有下一层");

        public double SeasonMultiplier(Season season) => 1.0;

        public double HourMultiplier(int hour) => 1.0;
    }
}
