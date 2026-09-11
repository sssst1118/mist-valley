using System;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;
using XingGame.Core.Time;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 玩家的灵根、境界/层数与修为。数据出处：§4.2 六档灵根（<c>docs/public/design.md</c> 162-171 行）、
/// §8.1 九大境界与炼气 1-13 层（458-473 行）、§8.2 的三条游戏绑定（496-502 行）、
/// §8.3 修炼速度体系（804-819 行）。
/// </summary>
/// <remarks>
/// <para>
/// <b>状态是本类自己持有的，不是从别处推出来的</b>：灵根、境界与修为都是玩家做过的选择/经历的结果，
/// 没有第二个来源可以算出来，所以要进存档（<see cref="ISaveable"/>，键 <c>cultivation</c>）。
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
/// <see cref="ICultivationSpeedTable"/>（<c>data/cultivation/cultivation_speed.json</c>）。
/// 本类只负责「什么时候结算、怎么乘、什么时候升层」。
/// </para>
/// </remarks>
public sealed class CultivationSystem : ICultivationSystem, ISaveable
{
    private const int MinutesPerHour = 60;

    private readonly ISpiritRootTable _roots;
    private readonly IRealmTable _realms;
    private readonly ICultivationSpeedTable _speed;

    private SpiritRootGrade _grade = null!;
    private SpiritRootDefinition? _root;
    private RealmDefinition _realm = null!;
    private int _stage;
    private int _cultivation;

    /// <param name="gradeId">§4.2 的品级 id，必填。</param>
    /// <param name="rootId">§4.3/§4.4 的具体灵根 id；四档普通品级传 null。</param>
    /// <param name="stage">从 1 起的小境界层号。</param>
    public CultivationSystem(
        ISpiritRootTable roots,
        IRealmTable realms,
        ICultivationSpeedTable speed,
        string gradeId,
        string? rootId,
        string realmId,
        int stage)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _realms = realms ?? throw new ArgumentNullException(nameof(realms));
        _speed = speed ?? throw new ArgumentNullException(nameof(speed));

        (SpiritRootGrade grade, SpiritRootDefinition? root, RealmDefinition realm) =
            Resolve(gradeId, rootId, realmId, "构造参数");

        if (stage < 1 || stage > realm.StageCount)
            throw new ArgumentOutOfRangeException(
                nameof(stage), stage, $"{realm.Name}的层号必须在 1..{realm.StageCount} 之间");

        // 新档从「本层一点修为都没攒」开始：起点是构造出来的，不是练出来的
        Assign(grade, root, realm, stage, cultivation: 0);
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
    /// 此刻打坐有多快：§4.2 的灵根档位 × §8.3 的季节 × §8.3 的时辰。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>三个因素相乘，不相加</b>：§8.3 把它们列成一张「其他影响因素」表，每一行都是一个独立的
    /// 加成，同时成立时是几个乘数连乘——春季的 +10% 撞上子时的 +30% 是 ×1.43，不是 ×1.40。
    /// 相加会随因素增多越来越偏离文档，而偏差只在两个加成同时出现时才显形。
    /// </para>
    /// <para>
    /// <b>加第四个因素就在这一行再乘一项</b>（灵脉等级、聚灵阵、风水、功法品阶、丹药、心境、双修
    /// 都排在 §8.3 的表里等各自的系统）：合成只此一处，打坐的时间账与升层判定都不用改。
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
        * _speed.HourMultiplier(now.Hour);      // §8.3 时辰：子时 1.30 / 午时 1.20 / 其余 1.00

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
        }

        if (_stage < _realm.StageCount) return gained;

        int dropped = _cultivation;
        _cultivation = 0;
        return gained - dropped;
    }

    public string SaveKey => "cultivation";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    /// <remarks><b>2</b>：加了「修为」一列。Version 1 的旧档怎么读见 <see cref="ReadCultivation"/>。</remarks>
    public int Version => 2;

    public string Serialize() =>
        JsonSerializer.Serialize(
            new SavedCultivation(_grade.Id, _root?.Id, _realm.Id, _stage, _cultivation), _saveJsonOptions);

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

        // 先整份校验再落盘：坏存档不该让境界停在「读了一半」的状态（照 Inventory / FriendshipSystem 先例）
        Assign(grade, root, realm, stage, cultivation);
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

    /// <summary>五个字段一起换：分开赋值会让「读了一半」成为可能。</summary>
    private void Assign(
        SpiritRootGrade grade, SpiritRootDefinition? root, RealmDefinition realm, int stage, int cultivation)
    {
        _grade = grade;
        _root = root;
        _realm = realm;
        _stage = stage;
        _cultivation = cultivation;
    }

    /// <summary>
    /// 存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。
    /// </summary>
    /// <remarks>
    /// <c>Stage</c> 可空是为了把「字段不在」与「写了 0」分开（ADR-009）：层号的合法值从 1 起，
    /// 静默读成 0 会让玩家停在一个不存在的层次上。
    /// <c>Cultivation</c> 也可空，但缺席的含义**按 <c>fromVersion</c> 分岔**：Version 1 的旧档读成 0
    /// （迁移决定），Version 2 缺它就是坏档——判据在 <see cref="ReadCultivation"/> 里，两者绝不混着读。
    /// <c>RootId</c> 不设这个区分：null 与「字段不在」在这里本来就是一个意思——没有具体灵根。
    /// </remarks>
    private sealed record SavedCultivation(string? GradeId, string? RootId, string? RealmId, int? Stage, int? Cultivation);

    /// <summary>字段名写全，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions _saveJsonOptions = new() { WriteIndented = true };
}
