using System;
using System.IO;
using System.Linq;
using XingGame.Core.Events;
using XingGame.Core.Save;
using XingGame.Core.Time;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 打坐这项**活动**（M3-8）：§3.1 的清晨那一项，把三笔账——修为、灵力、游戏时间——接在一处。
/// </summary>
/// <remarks>
/// <para>
/// <b>本文件量的是「连接」而不是「结算」</b>：一次打坐涨多少修为、什么时候升层、到顶点怎么办，
/// 由 <c>MeditationTests</c> 逐条对文档量过（那是 M3-2 的账）。这里量的是本切片新加的那一层——
/// 三笔账有没有按固定顺序一起做完、时间是不是真的走了、按哪一刻的倍率计价，以及「连接为什么不许
/// 回到桥接层」。
/// </para>
/// <para>
/// 时钟一律用**真的 <see cref="TimeService"/></b>（不是替身）：本切片最要紧的一条承诺就是
/// 「打坐真的让时间走了，所以时间真的过掉了三小时」，拿一个自己写的假时钟去量，等于自己证明自己。
/// 天气发生器与种子只影响天气，与本文件无关。
/// </para>
/// </remarks>
public class MeditationSystemTests
{
    private static readonly SpiritRootTable Roots = SpiritRootTable.LoadDefault();
    private static readonly RealmTable Realms = RealmTable.LoadDefault();
    private static readonly CultivationSpeedTable Speed = CultivationSpeedTable.LoadDefault(Realms);
    private static readonly SpiritPowerTable SpiritPower = SpiritPowerTable.LoadDefault();

    /// <summary>§3.1 的清晨 6:00——打坐那一档的起点，也是日界。</summary>
    private static GameTime Dawn(int day = 1) => new(1, Season.Spring, day, GameTime.FirstHour, 0);

    private static TimeService Clock(GameTime start) =>
        new(new EventBus(), new WeatherGenerator(WeatherTable.LoadDefault()), worldSeed: 1, start: start);

    /// <summary>
    /// 灵脉与增益两项都喂替身（×1.0）：本文件每个数都要逐项对得上 §3.1 / §4.2 / §8.3 与备案
    /// #67/#68 的出处，农场自带的那一成（微型灵脉 1.10）由 <c>SpiritLandTests</c> 单独量
    /// （同 <c>MeditationTests</c> 的分工）。
    /// </summary>
    private static CultivationSystem Cultivation(
        string gradeId = "grade_true_dual", string realmId = "qi_refining", int stage = 1) =>
        new(Roots, Realms, Speed, SpiritPower, new NoSpiritVein(), new NoSpeedBonus(),
            gradeId, rootId: null, realmId, stage);

    private static MeditationSystem NewSystem(TimeService time, CultivationSystem cultivation) =>
        new(cultivation, time, Speed);

    // ── 一次打坐坐多久 ────────────────────────────────────────────────

    /// <summary>
    /// 时长出自 §3.1：打坐是「清晨 6:00-9:00」那一档的活动，一档三小时。
    /// 6:00 起坐一次正好坐到 9:00——这不是巧合，是那一档的两个端点。
    /// </summary>
    [Fact]
    public void 一次打坐三小时_从清晨六点正好坐到九点()
    {
        TimeService time = Clock(Dawn());
        MeditationSystem system = NewSystem(time, Cultivation());

        Assert.Equal(180, system.SessionMinutes);

        MeditationResult result = system.Meditate();

        Assert.Equal(Dawn(), result.From);
        Assert.Equal(new GameTime(1, Season.Spring, 1, 9, 0), result.To);
        Assert.Equal(new GameTime(1, Season.Spring, 1, 9, 0), time.Now);
    }

    /// <summary>
    /// 时间是**真的**走掉的：时钟自己会发 <c>HourChanged</c>（打更音效、增益计时那些订阅者要它），
    /// 而 6:00→9:00 没有跨过日界，所以不该有 <c>DayStarted</c>（跨了就该有，见下一条）。
    /// </summary>
    [Fact]
    public void 打坐真的让时间走了_时钟照常发整点事件()
    {
        var bus = new EventBus();
        var time = new TimeService(bus, new WeatherGenerator(WeatherTable.LoadDefault()),
            worldSeed: 1, start: Dawn());

        int hours = 0;
        int days = 0;
        bus.Subscribe<HourChanged>(_ => hours++);
        bus.Subscribe<DayStarted>(_ => days++);

        NewSystem(time, Cultivation()).Meditate();

        Assert.Equal(3, hours);
        Assert.Equal(0, days);
    }

    /// <summary>
    /// 打坐跨过午夜时，倍率按**起坐那一刻**算：23:00 起坐落在 §8.3 的子时带里（+30%），
    /// 三小时后收功的 2:00 是平峰——这一坐仍按 23:00 的 1.30 计价。
    /// 43 点 = 3 小时 × 10（备案 #68）× 1.30（§8.3 子时）× 1.0（§4.2 双灵根）× 1.10（§8.3 春），
    /// 四舍五入自 42.9；若按收功那一刻（1.0）算只有 33，两个数一眼分得开。
    /// <para>
    /// 顺带钉住日界：跨过午夜**不算换了一天**——0:00-5:59 属于前一个游戏日（备案 #7），
    /// 所以 23:00 坐三小时仍是春 1 日 2:00，而不是 2 日 2:00。
    /// </para>
    /// </summary>
    [Fact]
    public void 跨午夜_按起坐那一刻的倍率结算()
    {
        var start = new GameTime(1, Season.Spring, 1, 23, 0);
        TimeService time = Clock(start);
        MeditationSystem system = NewSystem(time, Cultivation());

        MeditationResult result = system.Meditate();

        Assert.Equal(new GameTime(1, Season.Spring, 1, 23, 0), result.From);
        Assert.Equal(new GameTime(1, Season.Spring, 1, 2, 0), result.To);
        Assert.Equal(new GameTime(1, Season.Spring, 1, 2, 0), time.Now);
        Assert.Equal(43, result.PointsGained);
    }

    /// <summary>
    /// 同一件事的另一个方向：清晨 8:00 起坐三小时，收功的 11:00 落在 §8.3 的午时带里（+20%），
    /// 但这一坐按 8:00 的平峰计价——33 点，不是 3 × 10 × 1.1 × 1.2 = 39.6 → 40。
    /// </summary>
    [Fact]
    public void 坐进午时_也不算午时那一档()
    {
        TimeService time = Clock(new GameTime(1, Season.Spring, 1, 8, 0));
        MeditationSystem system = NewSystem(time, Cultivation());

        MeditationResult result = system.Meditate();

        Assert.Equal(new GameTime(1, Season.Spring, 1, 11, 0), result.To);
        Assert.Equal(33, result.PointsGained);
    }

    // ── 三笔账一起做完 ───────────────────────────────────────────────

    /// <summary>
    /// 修为与灵力是**同一次调用里的两笔账**：各自按同一段时长计价（备案 #71 的打坐档 5 点/游戏小时），
    /// 3 小时回 15 点——五层上限 200，先花掉 100 才看得出这一笔。
    /// </summary>
    [Fact]
    public void 修为与灵力两笔账一起算_各自按同一段时长()
    {
        TimeService time = Clock(Dawn());
        CultivationSystem cultivation = Cultivation(stage: 5);
        Assert.True(cultivation.TrySpendSpirit(100));

        MeditationResult result = NewSystem(time, cultivation).Meditate();

        Assert.Equal(33, result.PointsGained);          // 五层要 50 点，这一坐升不了层
        Assert.Equal(15, result.SpiritRestored);        // 3 小时 × 5 点/小时（备案 #71）
        Assert.Equal(115, cultivation.Spirit);
        Assert.Equal(5, result.StageBefore);
        Assert.Equal(5, result.StageAfter);
        Assert.Equal(33, cultivation.Cultivation);
    }

    /// <summary>
    /// 灵力封顶：满池时打坐照旧结算修为，但回上来的点数是 0（不是负的，也不是「溢出到下一次」）。
    /// </summary>
    [Fact]
    public void 灵力满着时_回上来的点数是零()
    {
        CultivationSystem cultivation = Cultivation(stage: 5);

        MeditationResult result = NewSystem(Clock(Dawn()), cultivation).Meditate();

        Assert.Equal(0, result.SpiritRestored);
        Assert.Equal(cultivation.MaxSpirit, cultivation.Spirit);
        Assert.Equal(33, result.PointsGained);
    }

    /// <summary>
    /// 一次打坐可以连升几层，而**升层会把灵力补满**——于是「灵力 +0」与「灵力条涨了一截」同时成立，
    /// 这正是 <c>MeditationResult</c> 要带上层号的理由（界面靠它把那两个数讲圆）。
    /// 一层 33 点：先扣 10 升二层、再扣 20 升三层，剩 3 点；灵力补到三层上限 150（备案 #69）。
    /// </summary>
    [Fact]
    public void 升层把灵力补满_返回值如实报零()
    {
        TimeService time = Clock(Dawn());
        CultivationSystem cultivation = Cultivation(stage: 1);
        Assert.True(cultivation.TrySpendSpirit(60));   // 一层上限 100，先花掉大半

        MeditationResult result = NewSystem(time, cultivation).Meditate();

        Assert.Equal(1, result.StageBefore);
        Assert.Equal(3, result.StageAfter);
        Assert.Equal(33, result.PointsGained);         // 记下的是全额，不因为升层而缩水
        Assert.Equal(0, result.SpiritRestored);        // 补满之后没有余量可回
        Assert.Equal(150, cultivation.Spirit);
        Assert.Equal(3, cultivation.Cultivation);
    }

    // ── 顶点与「不靠攒修为」的大境界 ──────────────────────────────────

    /// <summary>
    /// 炼气十三层是 §8.1 的顶点：打坐不抛、不涨修为（溢出的部分作废），但照旧回灵力、时间照旧走。
    /// </summary>
    [Fact]
    public void 顶点_打坐不涨修为但照旧回灵力与走时间()
    {
        TimeService time = Clock(Dawn());
        CultivationSystem cultivation = Cultivation(stage: 13);
        Assert.True(cultivation.TrySpendSpirit(100));   // 十三层上限 400

        MeditationResult result = NewSystem(time, cultivation).Meditate();

        Assert.Equal(0, result.PointsGained);
        Assert.Equal(15, result.SpiritRestored);
        Assert.Equal(13, result.StageAfter);
        Assert.Equal(new GameTime(1, Season.Spring, 1, 9, 0), time.Now);
    }

    /// <summary>
    /// 筑基及以上（§8.4 的跨大境界突破，要丹药或天材地宝）**不攒修为**——那边
    /// <see cref="ICultivationSystem.Meditate"/> 是当场抛的，而打坐这条活动照旧要能用：
    /// 只回灵力、只走时间。少了这一条，筑基修士按一下「打坐」就会在按钮里抛异常
    /// （而 <c>_Pressed</c> 里的异常只报日志、不崩游戏，「跑起来没事」很容易把它糊过去）。
    /// </summary>
    [Fact]
    public void 筑基打坐_只回灵力不涨修为()
    {
        TimeService time = Clock(Dawn());
        CultivationSystem cultivation = Cultivation(realmId: "foundation_establishment", stage: 1);
        Assert.True(cultivation.TrySpendSpirit(50));
        MeditationSystem system = NewSystem(time, cultivation);

        Assert.False(system.AdvancesByMeditation);
        Assert.Null(system.PointsToNextStage);

        MeditationResult result = system.Meditate();

        Assert.Equal(0, result.PointsGained);
        Assert.Equal(15, result.SpiritRestored);
        Assert.Equal(new GameTime(1, Season.Spring, 1, 9, 0), time.Now);
    }

    /// <summary>
    /// 进度那一格：有下一层就报还差多少，到顶报 null。**炼气与筑基的 null 是两件事**，
    /// 靠 <see cref="IMeditationSystem.AdvancesByMeditation"/> 分开——界面据此说「已至顶点」还是
    /// 「此境靠突破晋升」，混成一句会让站在顶点的玩家以为自己练错了路。
    /// </summary>
    [Fact]
    public void 进度_炼气一层要十点_十三层是顶点_筑基没有这一格()
    {
        MeditationSystem early = NewSystem(Clock(Dawn()), Cultivation(stage: 1));
        Assert.True(early.AdvancesByMeditation);
        Assert.Equal(10, early.PointsToNextStage);          // 备案 #67：第 n 层要 10 × n

        MeditationSystem apex = NewSystem(Clock(Dawn()), Cultivation(stage: 13));
        Assert.True(apex.AdvancesByMeditation);             // 顶点仍然是「靠打坐晋升」的境界
        Assert.Null(apex.PointsToNextStage);

        MeditationSystem foundation = NewSystem(Clock(Dawn()), Cultivation(realmId: "foundation_establishment"));
        Assert.False(foundation.AdvancesByMeditation);
        Assert.Null(foundation.PointsToNextStage);
    }

    /// <summary>
    /// 时钟被别处停着时，<c>ITimeService.Advance</c> 是空操作（它的既有语义：暂停时不吞分钟数、
    /// 解冻后也不补跑）。本系统不为此另发明一套「暂停就不许打坐」的规则——今天也没有第二个停表的人
    /// （面板的暂停归 <c>PanelHost</c>、只冻树）。这条用例把依赖摆明：时间走不走，由 <c>ITimeService</c> 说了算。
    /// </summary>
    [Fact]
    public void 时钟被停着时_修为照算而时间不动()
    {
        TimeService time = Clock(Dawn());
        time.IsPaused = true;
        MeditationSystem system = NewSystem(time, Cultivation(stage: 5));

        MeditationResult result = system.Meditate();

        Assert.Equal(result.From, result.To);
        Assert.Equal(Dawn(), time.Now);
        Assert.Equal(33, result.PointsGained);
    }

    // ── 构造校验与「不带状态」的钉子 ──────────────────────────────────

    [Fact]
    public void 构造_任一依赖为null当场抛()
    {
        CultivationSystem cultivation = Cultivation();

        Assert.Throws<ArgumentNullException>(() => new MeditationSystem(null!, Clock(Dawn()), Speed));
        Assert.Throws<ArgumentNullException>(() => new MeditationSystem(cultivation, null!, Speed));
        Assert.Throws<ArgumentNullException>(() => new MeditationSystem(cultivation, Clock(Dawn()), null!));
    }

    /// <summary>
    /// 本系统**不带状态**（刻意的）：修为与灵力在 <see cref="ICultivationSystem"/> 上、时刻在
    /// <c>ITimeService</c> 上、下一层要多少修为由速度表现算。给它一个存档键就是同一个事实的第二处
    /// （同 <c>worldSeed</c> 不许存两处的理由）；哪天有人往里加了要存的东西，这条会红，
    /// 那时该做的是把状态挪回它该在的地方，而不是顺手补一个 blob。
    /// </summary>
    [Fact]
    public void 不带状态_不进存档()
    {
        Assert.False(typeof(ISaveable).IsAssignableFrom(typeof(MeditationSystem)));
        Assert.False(typeof(ISaveable).IsAssignableFrom(typeof(IMeditationSystem)));

        Assert.DoesNotContain(typeof(MeditationSystem).GetMembers(),
            member => member.Name.Contains("Save") || member.Name.Contains("Version"));
        Assert.DoesNotContain(typeof(IMeditationSystem).GetMembers(),
            member => member.Name.Contains("Save") || member.Name.Contains("Version"));
    }

    /// <summary>
    /// 本切片最要紧的一条**否定式**决定：三笔账（结修为 / 回灵力 / 推时间）只许在
    /// <see cref="IMeditationSystem"/> 里拼。面板一旦自己调其中任何一个，桥接层就多了一条玩法判断
    /// ——它逃过了编译器的看管（ADR-002 够不到 <c>ui/</c>），只能靠这条用例抓。
    /// </summary>
    /// <remarks>
    /// 拿源码文本判，是因为纯 C# 测试够不到 Godot 的 <c>ui/</c>（同 <c>BridgeContractTests</c> 的做法）。
    /// 只扫**非注释行**：面板的类注释里**就该**写着「本类不取时间服务」这句为什么，
    /// 不剥的话那是假红，而假红的下场是被真人删掉。
    /// </remarks>
    [Fact]
    public void 面板不许自己拼三笔账()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "CultivationPanel.cs"));
        string code = WithoutCommentLines(source);

        Assert.DoesNotContain(".RecoverSpirit(", code);
        Assert.DoesNotContain(".TrySpendSpirit(", code);
        Assert.DoesNotContain(".Advance(", code);
        Assert.DoesNotContain("ITimeService", code);

        // 反过来：它必须真的调那一个入口，否则上面几条可以靠「什么都不做」通过
        Assert.Contains(".Meditate()", code);
    }

    /// <summary>只留非注释行。本文件判的源码里注释一律是行注释（<c>//</c> 与 <c>///</c>）。</summary>
    private static string WithoutCommentLines(string source) =>
        string.Join("\n", source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    /// <summary>
    /// 从测试输出目录逐级上溯找仓库根（同时含 <c>ui/</c> 与 <c>systems/</c> 的那一级）——
    /// 与 <c>BridgeContractTests</c>、各表读数据文件的做法同款。找不到就抛：
    /// 扫源码的用例最怕的是静悄悄扫了零个文件还绿灯。
    /// </summary>
    private static string RepoRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "ui")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "systems")))
                    return directory.FullName;
            }
        }

        throw new InvalidOperationException("找不到仓库根（同时含 ui/ 与 systems/ 的目录）：本用例靠读面板源码守契约");
    }
}
