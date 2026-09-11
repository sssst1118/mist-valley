using System;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;
using XingGame.Core.Time;
using XingGame.Systems.Buffs;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 玩家的灵根、境界/层数、修为与灵力。数据出处：§4.2 六档灵根（<c>docs/public/design.md</c> 162-171 行）、
/// §8.1 九大境界与炼气 1-13 层（458-473 行）、§8.2 的三条游戏绑定（496-502 行）、
/// §8.3 修炼速度体系（804-819 行）；灵力那笔账的数值出自 ARCHITECTURE 未定义项备案 #69/#71，见
/// <see cref="ISpiritPowerTable"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>状态是本类自己持有的，不是从别处推出来的</b>：灵根、境界、修为与当前灵力都是玩家做过的选择/
/// 经历的结果，没有第二个来源可以算出来，所以要进存档（<see cref="ISaveable"/>，键 <c>cultivation</c>）。
/// <b>灵力上限不在其列</b>：它是从层数算出来的派生量（<see cref="MaxSpirit"/>）。
/// </para>
/// <para>
/// <b>升层时灵力补满（本切片的定论，不是顺手）</b>：上限随层数涨（每层 +25），升层那一刻
/// <see cref="Meditate"/> 会把当前灵力补到新上限。理由是手感与语义两条：玩家刚跨过一道台阶就看到
/// 灵力条短了一截，是升层这个奖励时刻最不该有的画面（而「只涨上限」正好是这个观感）；
/// §8.1 说炼气期就是「引天地灵气入体……在丹田中积蓄气态灵力」，层数上去本来就意味着容量上去、
/// 当即充满。它也不给玩家开什么口子：升一层要几小时的打坐，换来的只是把池子灌满。
/// </para>
/// <para>
/// <b>构造与读档共用一份查表逻辑，只有层号越界抛的异常不同</b>：构造参数里写错层号是编程错误
/// （<see cref="ArgumentOutOfRangeException"/>），存档里的层号越界是数据错误
/// （<see cref="InvalidDataException"/>，ADR-009 要求坏档一律是它，将来 catch 坏档的代码要接得住）。
/// id 查不到在两处都是 <see cref="InvalidDataException"/>——灵根与境界都是**数据**（表或存档），
/// 对不上就是数据错误，不许「猜着读」（同 <c>FriendshipSystem</c> 先例：认不出的 id 不接给谁）。
/// </para>
/// <para>
/// <b>数值一个都不在本类里</b>：基础速度、逐层开销、季节与时辰的倍率全在
/// <see cref="ICultivationSpeedTable"/>（<c>data/cultivation/cultivation_speed.json</c>），
/// 灵力上限的系数与两个恢复速率全在 <see cref="ISpiritPowerTable"/>
/// （<c>data/cultivation/spirit_power.json</c>），丹药等限时增益的倍率与时长全在增益表
/// （<c>data/buffs/buffs.json</c>，M3-6 起）——本类连「聚气散」这个名字都不认识，只认识
/// 「此刻修炼速度的总修正是多少」。本类只负责「什么时候结算、怎么乘、什么时候升层」。
/// </para>
/// </remarks>
public sealed class CultivationSystem : ICultivationSystem, ISaveable
{
    private const int MinutesPerHour = 60;

    private readonly ISpiritRootTable _roots;
    private readonly IRealmTable _realms;
    private readonly ICultivationSpeedTable _speed;
    private readonly ISpiritPowerTable _spiritPower;
    private readonly ISpiritVeinSource _spiritVein;
    private readonly ICultivationSpeedBonus _speedBonus;

    private SpiritRootGrade _grade = null!;
    private SpiritRootDefinition? _root;
    private RealmDefinition _realm = null!;
    private int _stage;
    private int _cultivation;
    private int _spirit;

    /// <param name="gradeId">§4.2 的品级 id，必填。</param>
    /// <param name="rootId">§4.3/§4.4 的具体灵根 id；四档普通品级传 null。</param>
    /// <param name="stage">从 1 起的小境界层号。</param>
    /// <param name="spiritVein">
    /// 打坐处的灵气浓度（§8.8 的灵脉等级）。**打坐只看这个数**，所以这里收的是窄接口而不是农场的
    /// 那件状态——见 <see cref="ISpiritVeinSource"/>。
    /// </param>
    /// <param name="speedBonus">
    /// 限时增益对修炼速度的总修正（§8.3 表里的「丹药」那一行，M3-6 起）。同 <paramref name="spiritVein"/>
    /// 一样是**窄接口**：打坐只欠这一个数，不欠整个 <see cref="IBuffSystem"/>（查移速、清增益、
    /// 存档格式都不是修炼领域的事）。
    /// </param>
    public CultivationSystem(
        ISpiritRootTable roots,
        IRealmTable realms,
        ICultivationSpeedTable speed,
        ISpiritPowerTable spiritPower,
        ISpiritVeinSource spiritVein,
        ICultivationSpeedBonus speedBonus,
        string gradeId,
        string? rootId,
        string realmId,
        int stage)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _realms = realms ?? throw new ArgumentNullException(nameof(realms));
        _speed = speed ?? throw new ArgumentNullException(nameof(speed));
        _spiritPower = spiritPower ?? throw new ArgumentNullException(nameof(spiritPower));
        _spiritVein = spiritVein ?? throw new ArgumentNullException(nameof(spiritVein));
        _speedBonus = speedBonus ?? throw new ArgumentNullException(nameof(speedBonus));

        (SpiritRootGrade grade, SpiritRootDefinition? root, RealmDefinition realm) =
            Resolve(gradeId, rootId, realmId, "构造参数");

        if (stage < 1 || stage > realm.StageCount)
            throw new ArgumentOutOfRangeException(
                nameof(stage), stage, $"{realm.Name}的层号必须在 1..{realm.StageCount} 之间");

        // 新档从「本层一点修为都没攒」开始：起点是构造出来的，不是练出来的。
        // 灵力则从「满」开始——丹田初开就有气（§8.1「引天地灵气入体……在丹田中积蓄气态灵力」），
        // 而且这与旧档的迁移（ReadSpirit 把缺字段读成满）是同一条推理的两端：两处若不一致，
        // 玩家跨版本读档就会看到灵力条凭空变长或变短
        Assign(grade, root, realm, stage, cultivation: 0, spirit: _spiritPower.MaxSpiritAt(stage));
    }

    public SpiritRootGrade Grade => _grade;

    public SpiritRootDefinition? Root => _root;

    public RealmDefinition Realm => _realm;

    public int Stage => _stage;

    /// <summary>本层里已攒的修为，恒在 <c>[0, 升到下一层所需)</c> 之内。</summary>
    /// <remarks>
    /// <b>不是「总共练了多少」</b>：攒够一层就扣掉、层号 +1（§8.1 的炼气期是逐层递进的台阶，
    /// 不是一条累加的经验条）。所以 UI 画进度条时，分母是
    /// <see cref="ICultivationSpeedTable.PointsToAdvance"/>，分子就是它。
    /// </remarks>
    public int Cultivation => _cultivation;

    /// <summary>当前灵力。存档里只存这一个数，上限现算。</summary>
    public int Spirit => _spirit;

    /// <summary>
    /// 当前层数下的灵力上限（备案 #69）：<c>100 + 25 × (层 - 1)</c>。
    /// </summary>
    /// <remarks>
    /// 每次读都现算，不缓存：缓存就等于把「层数 → 上限」这条派生关系存了第二份，
    /// 升层时漏更新一处就会让灵力条画得比上限还长（同「上限不进存档」的理由）。
    /// </remarks>
    public int MaxSpirit => _spiritPower.MaxSpiritAt(_stage);

    public bool Reaches(string realmId, int stage)
    {
        RealmDefinition target = _realms.Get(realmId);

        // 复用境界自己那条越界判据，不在这里写第二份规则——两份边界判据迟早会漂
        target.StageName(stage);

        int targetOrder = _realms.OrderOf(realmId);
        int currentOrder = _realms.OrderOf(_realm.Id);

        // 跨大境界只看 §8.1 的表序：筑基初期高过炼气十三层。只比层号的话，一位筑基修士会
        // 因为「层号 1 < 4」而种不了灵植——那正是「至少炼气 4 层」这条门槛最不该有的行为
        return currentOrder != targetOrder ? currentOrder > targetOrder : _stage >= stage;
    }

    public bool Meets(CultivationGate gate)
    {
        CultivationGateRequirement requirement = _realms.RequirementOf(gate);

        return Reaches(requirement.RealmId, requirement.Stage);
    }

    /// <summary>
    /// 此刻打坐有多快：§4.2 的灵根档位 × §8.3 的季节 × §8.3 的时辰 × §8.8 的灵脉（灵气浓度）
    /// × §8.3 的丹药等限时增益。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>五个因素相乘，不相加</b>：§8.3 把它们列成一张「其他影响因素」表，每一行都是一个独立的
    /// 加成，同时成立时是几个乘数连乘——春季的 +10% 撞上子时的 +30% 是 ×1.43，不是 ×1.40。
    /// 相加会随因素增多越来越偏离文档，而偏差只在两个加成同时出现时才显形。
    /// </para>
    /// <para>
    /// <b>第四个因素（M3-5 起：灵脉等级）以**乘数**的身份进来</b>：§8.3 那一行写的是
    /// 「微型灵脉 +10%，小型 +25%…」，§8.8 的表把它落成灵气浓度，两边是同一组数。所以这里乘的是
    /// <see cref="ISpiritVeinSource.DensityMultiplier"/>，不是又一套刻度——**别自创第二套**
    /// （比如 0-100 的浓度值），那会让「灵脉 +10%」在代码里有两个含义。
    /// </para>
    /// <para>
    /// <b>第五个因素（M3-6 起：丹药等限时增益）就是 §8.3 表里的「丹药」那一行</b>：聚气散
    /// 「+50% 持续 7 天」，所以这里乘的是 <see cref="ICultivationSpeedBonus.MultiplierAt"/>——
    /// 一条增益都没有时它是 1.0，不是「这一项不存在」。**数值全在
    /// <c>data/buffs/buffs.json</c> 上**，修炼速度表与这个类里一个数都不许有它（见
    /// <c>M3Audit_Cultivation</c> 的两条钉子）。
    /// </para>
    /// <para>
    /// <b>再加因素（聚灵阵、风水、功法品阶、心境、双修）也就在这一行再乘一项</b>：
    /// 合成只此一处，打坐的时间账与升层判定都不用改。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="now"/> 里的钟点不是 0..23——<see cref="GameTime"/> 是个 record struct，
    /// 构造时可以塞进任意数字，所以这里能撞上（同层号越界那类「写错就是写错」）。
    /// </exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">表里没给这个季节录倍率。</exception>
    public double SpeedMultiplierAt(GameTime now) =>
        _grade.CultivationSpeedMultiplier       // §4.2 六档灵根：0.3x .. 4.0x
        * _speed.SeasonMultiplier(now.Season)   // §8.3 季节：春 1.10 / 夏 1.05 / 秋 1.10 / 冬 0.90
        * _speed.HourMultiplier(now.Hour)       // §8.3 时辰：子时 1.30 / 午时 1.20 / 其余 1.00
        * _spiritVein.DensityMultiplier         // §8.8 灵气浓度：微型 1.10 … 龙脉 6.00
        * _speedBonus.MultiplierAt(now);        // §8.3 丹药等限时增益：聚气散 1.50 …（没有就是 1.00）

    /// <summary>
    /// 打坐 <paramref name="minutes"/> 游戏分钟，按 <paramref name="now"/> 这一刻的倍率结算修为，
    /// 攒够本层开销就升层。返回**真正记下**的修为。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么是显式入口，而不是「随时间自动涨」</b>：§3.1 的日循环里打坐是**清晨 6:00-9:00
    /// 的一项活动**（「起床、查看天气/电视、照料动物、打坐修炼」），不是被动回复——玩家得真的去坐
    /// 这一炷香。所以收的是一段时间，而不是每帧滴一点。按键与界面是后面切片的桥接层，本类只管算账。
    /// </para>
    /// <para>
    /// <b>炼气期内部没有突破关口</b>：§8.1 说炼气「按 1-13 层逐层递进」，§8.4 的突破与渡劫是
    /// **跨大境界**的事（要筑基丹、可能引动雷劫）——所以层与层之间攒够就升，没有另一次判定。
    /// 跨大境界这里仍然没有口子：十三层到顶就停（见下），炼气 → 筑基是 M4 的事。
    /// </para>
    /// <para>
    /// <b>整段时间按 <paramref name="now"/> 这一刻计价，中途不换倍率</b>：跨过时辰边界（如 22:00 起
    /// 坐三小时、坐到子时里）也照开始那一刻算。文档没有规定跨段怎么切，而编一套分段规则会让
    /// 「同样的时长、换个起点」出现玩家算不清的差异。要分段由调用方切成多次调用。
    /// </para>
    /// <para>
    /// <b>一次调用只舍一次零头</b>：结果四舍五入到整点（半分进位）。所以把一段时长拆成很多次
    /// 一分钟来调，每次的零头都会被舍掉——调用方应当按「一次打坐」传整段时长。
    /// </para>
    /// <para>
    /// <b>顶点的溢出直接作废</b>：到了炼气十三层（§8.1 的顶点），这一层没有「下一层」，
    /// 攒下的修为无处可去。溢出的部分不记账、也不留在存档里——留着只会让「大圆满」这一格
    /// 挂着一个指向不存在的进度条，而返回值仍如实反映这次真的记下了多少。
    /// </para>
    /// </remarks>
    /// <returns>这次打坐真正攒下的修为；已经在最后一层时恒为 0。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minutes"/> 是负数。</exception>
    /// <exception cref="NotSupportedException">
    /// 当前大境界没有升层开销（筑基及以后是 §8.4 的跨大境界突破，不是攒够就升）——
    /// 与其攒一批用不上的数，不如当场说清这里还没有这条路。
    /// </exception>
    public int Meditate(GameTime now, int minutes)
    {
        if (minutes < 0)
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "打坐时长不能是负数");

        if (!_speed.Covers(_realm.Id))
            throw new NotSupportedException(
                $"{_realm.Name}的晋升是 §8.4 的突破（要丹药或天材地宝、可能引动雷劫），不是攒修为——"
                + "本系统目前只结算炼气期的打坐");

        int gained = (int)Math.Round(
            minutes / (double)MinutesPerHour * _speed.BasePointsPerHour * SpeedMultiplierAt(now),
            MidpointRounding.AwayFromZero);

        _cultivation += gained;

        while (_stage < _realm.StageCount && _cultivation >= _speed.PointsToAdvance(_stage))
        {
            _cultivation -= _speed.PointsToAdvance(_stage);
            _stage++;

            // 升层即回满（见类注释「升层时灵力补满」）：上限刚涨了 25，当前值跟着补上去，
            // 而不是让它卡在旧上限上——「刚跨过一道台阶，灵力条却短了一截」是升层最坏的手感
            _spirit = MaxSpirit;
        }

        if (_stage < _realm.StageCount) return gained;

        int dropped = _cultivation;
        _cultivation = 0;
        return gained - dropped;
    }

    /// <summary>
    /// 花掉 <paramref name="amount"/> 点灵力，全有或全无。
    /// </summary>
    /// <remarks>
    /// <b>先比较、后相减的顺序就是这条承诺本身</b>——反过来写（先扣再比）在不够的时候会留下一个
    /// 已经被改小的池子（照 <c>Wallet.TrySpendGold</c> 的写法）。
    /// </remarks>
    public bool TrySpendSpirit(int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "消耗灵力的数量必须为正；回灵力请用 RecoverSpirit");

        if (_spirit < amount) return false;

        _spirit -= amount;
        return true;
    }

    /// <summary>
    /// 按备案 #71 的速率回灵力，封在上限。返回**真正回上**的点数。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>一次调用只舍一次零头</b>（同 <see cref="Meditate"/>）：结果四舍五入到整点，
    /// 所以调用方应当按**整段**传时长，别拿一分钟的碎片来调——2 点/小时的费率下，一分钟的零头
    /// 会被舍成 0。费率越低越明显，而症状是「灵力怎么不涨」。
    /// </para>
    /// <para>
    /// <b>刻意不留「不足一点的零头」这个缓冲字段（不是漏做）</b>：那会是一份**没进存档的状态**——
    /// 存下去等于又多一列要迁移的数据，不存就等于每次读档悄悄丢掉最多一点灵力，两条都不是好选择。
    /// 有 <c>SpiritPowerTests</c> 的用例钉着这条约定。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minutes"/> 是负数。</exception>
    public int RecoverSpirit(SpiritRecovery recovery, int minutes)
    {
        if (minutes < 0)
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "恢复时长不能是负数");

        int restored = (int)Math.Round(
            minutes / (double)MinutesPerHour * _spiritPower.RecoveryPerHour(recovery),
            MidpointRounding.AwayFromZero);

        return Restore(restored);
    }

    /// <summary>睡了一觉：灵力全恢复（备案 #71 的第三条）。见 <see cref="ICultivationSystem"/> 里的入口说明。</summary>
    public int RecoverSpiritOnSleep() => Restore(MaxSpirit);

    /// <summary>
    /// 把 <paramref name="amount"/> 点灵力加进池子，**封在上限**；返回真正加上的点数。
    /// </summary>
    /// <remarks>
    /// 三个入口（清醒 / 打坐 / 睡眠）共用这一处封顶：分散在三处写 <c>Math.Min</c> 早晚会漏一处，
    /// 而漏的那一处就是「灵力超过上限」——症状要到 UI 把灵力条画爆才显形。
    /// </remarks>
    private int Restore(int amount)
    {
        int room = MaxSpirit - _spirit;
        int restored = Math.Min(amount, room);

        _spirit += restored;
        return restored;
    }

    public string SaveKey => "cultivation";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    /// <remarks>
    /// <b>2</b>：加了「修为」一列（Version 1 的旧档怎么读见 <see cref="ReadCultivation"/>）。
    /// <b>3</b>：加了「灵力」一列（Version 1/2 的旧档怎么读见 <see cref="ReadSpirit"/>）。
    /// </remarks>
    public int Version => 3;

    public string Serialize() =>
        JsonSerializer.Serialize(
            new SavedCultivation(_grade.Id, _root?.Id, _realm.Id, _stage, _cultivation, _spirit), _saveJsonOptions);

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009）
        if (fromVersion > Version)
            throw new NotSupportedException($"修仙存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedCultivation saved = JsonSerializer.Deserialize<SavedCultivation>(json, _saveJsonOptions)
            ?? throw new InvalidDataException("修仙存档内容为空");

        if (string.IsNullOrWhiteSpace(saved.GradeId))
            throw new InvalidDataException("修仙存档缺少 gradeId");

        if (string.IsNullOrWhiteSpace(saved.RealmId))
            throw new InvalidDataException("修仙存档缺少 realmId");

        // 「字段不在」与「字段是 0」必须分开（ADR-009 / AGENT-BRIEF）：层号的合法值从 1 起，
        // 缺字段会静默读成 0，而 0 一旦被当成合法值，玩家就会停在一个不存在的层次上
        if (saved.Stage is not int stage)
            throw new InvalidDataException("修仙存档缺少 stage 字段");

        (SpiritRootGrade grade, SpiritRootDefinition? root, RealmDefinition realm) =
            Resolve(saved.GradeId!, saved.RootId, saved.RealmId!, "修仙存档");

        if (stage < 1 || stage > realm.StageCount)
            throw new InvalidDataException(
                $"修仙存档里的层号 {stage} 超出{realm.Name}的 1..{realm.StageCount}");

        int cultivation = ReadCultivation(saved, fromVersion);
        RequireCultivationInRange(cultivation, realm, stage);

        int spirit = ReadSpirit(saved, fromVersion, realm, stage);
        RequireSpiritInRange(spirit, realm, stage);

        // 先整份校验再落盘：坏存档不该让境界停在「读了一半」的状态（照 Inventory / FriendshipSystem 先例）
        Assign(grade, root, realm, stage, cultivation, spirit);
    }

    /// <summary>
    /// 读出「本层已攒的修为」，并在这里把旧档与新档分开。
    /// </summary>
    /// <remarks>
    /// <b>Version 1 的存档里没有这一列，读成 0 是**迁移决定**，不是猜着读</b>：那一版的 JSON 里
    /// 根本没有修为这个概念——M3-1 没有攒修为的入口，也没有每层开销，所以「字段不在」只可能是
    /// 「这位玩家一点修为都没攒过」，而修为的初值本来就该是 0，这个结论有据可依。
    /// <b>同样的缺席在 Version 2 里就是坏档</b>：本版本自己写出去的 blob 一定带着这一列，
    /// 缺了说明这份数据不是本系统写的（被人改过、或写到一半崩了）。
    /// 分界线就是 <paramref name="fromVersion"/>——AGENT-BRIEF 那条「『字段不在』与『字段是 0』
    /// 不是一回事」，能合并的只有这一种情况：旧格式里它压根不存在。
    /// </remarks>
    private static int ReadCultivation(SavedCultivation saved, int fromVersion)
    {
        if (saved.Cultivation is int stored) return stored;

        if (fromVersion < 2) return 0;

        throw new InvalidDataException("修仙存档缺少 cultivation 字段");
    }

    /// <summary>
    /// 修为必须落在「本层」这个台阶上：<c>[0, 升到下一层所需)</c>，顶层恒为 0。
    /// </summary>
    /// <remarks>
    /// <b>为什么到了开销就算坏档，而不是读进来顺手升一层</b>：那样等于替玩家做了一次他没做过的决定
    /// （悄悄给他升层，或悄悄扣掉他的修为），两种都是改档。代价写在明处：日后若把
    /// <c>data/cultivation/cultivation_speed.json</c> 的开销调小，旧档会被这一条挡下——
    /// 那时该补的是迁移（像 <see cref="ReadCultivation"/> 那样显式写清怎么改），不是把这条放宽。
    /// </remarks>
    private void RequireCultivationInRange(int cultivation, RealmDefinition realm, int stage)
    {
        if (cultivation < 0)
            throw new InvalidDataException($"修仙存档里的修为 {cultivation} 是负数");

        if (!_speed.Covers(realm.Id))
        {
            // 本表没给这个境界录曲线（筑基及以后）：那边没有「攒够就升」，所以修为只能是 0
            if (cultivation != 0)
                throw new InvalidDataException(
                    $"修仙存档在{realm.Name}上记着 {cultivation} 修为，而升层开销表只覆盖炼气期——"
                    + "这个境界不攒修为（§8.4 的突破要的是丹药与天材地宝）");

            return;
        }

        if (stage == realm.StageCount)
        {
            if (cultivation != 0)
                throw new InvalidDataException(
                    $"修仙存档在{realm.Name}{realm.StageName(stage)}（顶点）上记着 {cultivation} 修为——"
                    + "这一层没有下一层，它的修为只能是 0");

            return;
        }

        int needed = _speed.PointsToAdvance(stage);
        if (cultivation >= needed)
            throw new InvalidDataException(
                $"修仙存档在{realm.Name}{realm.StageName(stage)}上记着 {cultivation} 修为，"
                + $"而升到下一层只要 {needed}——攒到这个数就该升层了，这个档自相矛盾");
    }

    /// <summary>
    /// 读出「当前灵力」，并在这里把旧档与新档分开。
    /// </summary>
    /// <remarks>
    /// <b>Version 1/2 的存档里没有这一列，读成「满」是**迁移决定**，不是猜着读</b>：那两版的 JSON 里
    /// 根本没有灵力这个概念——没有任何入口能花掉它（消耗的原语与第一个消费者「灵气浇灌」都在本切片
    /// 之后），而能改动它的只有恢复，于是那份存档里的灵力只可能还停在满上。这与构造里「新档灵力取满」
    /// 是同一条推理的两端：跨版本读档时，玩家的灵力条不会凭空变化（读成 0 则会让老玩家发现自己的
    /// 池子是空的，而按 2/小时要挂机几十个小时才回得满，且他根本不知道自己少了什么）。
    /// <b>同样的缺席在 Version 3 里就是坏档</b>：本版本自己写出去的 blob 一定带着这一列，
    /// 缺了说明这份数据不是本系统写的（被人改过、或写到一半崩了）。
    /// 分界线就是 <paramref name="fromVersion"/>——AGENT-BRIEF 那条「『字段不在』与『字段是 0』
    /// 不是一回事」，能合并的只有这一种情况：旧格式里它压根不存在。
    /// </remarks>
    private int ReadSpirit(SavedCultivation saved, int fromVersion, RealmDefinition realm, int stage)
    {
        if (saved.Spirit is int stored) return stored;

        if (fromVersion < 3) return _spiritPower.MaxSpiritAt(stage);

        throw new InvalidDataException("修仙存档缺少 spirit 字段");
    }

    /// <summary>
    /// 灵力必须落在 <c>[0, 这一层的上限]</c> 之内。
    /// </summary>
    /// <remarks>
    /// <b>为什么越上限就算坏档，而不是夹到上限上</b>：夹一刀等于替玩家改档（同修为那条的推理）。
    /// 越上限只可能来自两处：有人手改了存档，或上限公式的系数被改小——后者的正确处置是补一条迁移
    /// （像 <see cref="ReadSpirit"/> 那样写清怎么改），不是把这条判据放宽。
    /// </remarks>
    private void RequireSpiritInRange(int spirit, RealmDefinition realm, int stage)
    {
        if (spirit < 0)
            throw new InvalidDataException($"修仙存档里的灵力 {spirit} 是负数");

        int max = _spiritPower.MaxSpiritAt(stage);
        if (spirit > max)
            throw new InvalidDataException(
                $"修仙存档在{realm.Name}{realm.StageName(stage)}上记着 {spirit} 灵力，"
                + $"超过这一层的上限 {max}——这个档自相矛盾");
    }

    /// <summary>
    /// 按 id 查出灵根与境界，并把两处对不上的情况拦下。
    /// </summary>
    /// <remarks>
    /// 「具体灵根」的品级与记录里的品级必须一致：<c>root_sword</c>（先天异）配 <c>grade_mutation</c>
    /// 会让修炼速度与特效各说各话——而这正是「同一个事实存两处」的典型症状，宁可当场抛。
    /// </remarks>
    private (SpiritRootGrade Grade, SpiritRootDefinition? Root, RealmDefinition Realm) Resolve(
        string gradeId, string? rootId, string realmId, string owner)
    {
        if (!_roots.TryGetGrade(gradeId, out SpiritRootGrade grade))
            throw new InvalidDataException($"{owner}里的灵根品级「{gradeId}」不在灵根表里");

        SpiritRootDefinition? root = null;
        if (rootId is not null)
        {
            if (!_roots.TryGetRoot(rootId, out SpiritRootDefinition? found))
                throw new InvalidDataException($"{owner}里的具体灵根「{rootId}」不在灵根表里");

            if (!string.Equals(found.GradeId, gradeId, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"{owner}里的灵根自相矛盾：具体灵根「{rootId}」属于品级「{found.GradeId}」，记的却是「{gradeId}」");

            root = found;
        }

        if (!_realms.TryGet(realmId, out RealmDefinition realm))
            throw new InvalidDataException($"{owner}里的境界「{realmId}」不在境界表里");

        return (grade, root, realm);
    }

    /// <summary>六个字段一起换：分开赋值会让「读了一半」成为可能。</summary>
    private void Assign(
        SpiritRootGrade grade, SpiritRootDefinition? root, RealmDefinition realm, int stage,
        int cultivation, int spirit)
    {
        _grade = grade;
        _root = root;
        _realm = realm;
        _stage = stage;
        _cultivation = cultivation;
        _spirit = spirit;
    }

    /// <summary>
    /// 存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。
    /// </summary>
    /// <remarks>
    /// <c>Stage</c> 可空是为了把「字段不在」与「写了 0」分开（ADR-009）：层号的合法值从 1 起，
    /// 静默读成 0 会让玩家停在一个不存在的层次上。
    /// <c>Cultivation</c> 与 <c>Spirit</c> 也可空，但缺席的含义**按 <c>fromVersion</c> 分岔**：
    /// Version 1 的旧档没有修为、Version 1/2 的旧档没有灵力，分别读成 0 与满
    /// （都是迁移决定，判据在 <see cref="ReadCultivation"/> / <see cref="ReadSpirit"/> 里，
    /// 两者绝不混着读）；缺了本版本该有的那一列就是坏档。
    /// <c>RootId</c> 不设这个区分：null 与「字段不在」在这里本来就是一个意思——没有具体灵根。
    /// </remarks>
    private sealed record SavedCultivation(
        string? GradeId, string? RootId, string? RealmId, int? Stage, int? Cultivation, int? Spirit);

    /// <summary>字段名写全，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions _saveJsonOptions = new() { WriteIndented = true };
}
