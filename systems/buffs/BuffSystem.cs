using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Core.Save;
using XingGame.Core.Time;

namespace XingGame.Systems.Buffs;

/// <summary>
/// 玩家身上生效的限时增益。规则（施加 / 刷新 / 连乘 / 到期）在 <see cref="IBuffSystem"/> 上，
/// 数值在 <see cref="BuffTable"/> 上，本类只负责「现在哪几条还在、乘出来多少、怎么存下来」。
/// </summary>
/// <remarks>
/// <para>
/// <b>状态是本类自己持有的，进存档（键 <c>buffs</c>）</b>：哪条增益还在身上、什么时候到期，
/// 是玩家做过的事（吃了丹 / 放了法术）的结果，没有第二个来源算得出来。它**不进 <c>cultivation</c>
/// 那个 blob**：增益不是修仙概念——§8.2 的轻身术管的是移速，将来 §12.4 的料理还会管耕种，
/// 把它们塞进玩家的灵根与境界那一份里，就是让修炼模块替别的领域保管状态（同 M3-5 灵脉不进
/// <c>cultivation</c> 的理由）。代价是这一个新键：M2 到 M3-5 的旧档里没有它，
/// 而 <c>SqliteSaveService</c> 对缺席的键是「跳过、让它保持自己的初始状态」——那条路径正是
/// 「没吃过任何增益」，所以旧档不必在 <see cref="Deserialize"/> 里补任何分支，也不用动
/// <c>cultivation</c> 的版本号。
/// </para>
/// <para>
/// <b>存的是「哪条 + 什么时候到期」两个字段，不存别的</b>：倍率、名字、目标属性都是表的输出
/// （存了就是同一个事实两处，表一改旧档就错），而**施加时刻是从「到期时刻 − 时长」算出来的**
/// （时长也来自表），所以它同样不存。到期时刻用绝对分钟数，不用「还剩几分钟」——相对量跨日、
/// 跨季与读档之后都会漂（见 <see cref="BuffDefinition.ExpiresAt"/>）。
/// </para>
/// <para>
/// <b>已到期的那几条不剥掉而是原样读进来（不是漏做）</b>：存档躺在磁盘上的那段时间游戏时间也在
/// 走，所以「存的时候还在、读的时候已经过期」是正常结果，不是坏档。它们读进来之后不会再参与连乘
/// （见 <see cref="ActiveBuff.IsActiveAt"/>），并在第一次查询或施加时被 <see cref="Purge"/> 清掉。
/// </para>
/// </remarks>
public sealed class BuffSystem : IBuffSystem, ICultivationSpeedBonus, ISaveable
{
    private readonly IBuffTable _table;

    /// <summary>id → 那一条的「定义 + 到期时刻」。同一个 id 只有一条：再施加就是刷新（见 <see cref="Apply"/>）。</summary>
    private readonly Dictionary<string, ActiveBuff> _active = new(StringComparer.Ordinal);

    public BuffSystem(IBuffTable table)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    public void Apply(string buffId, GameTime now)
    {
        // 认不出的 id 在这里当场抛（KeyNotFoundException）：调用方给的是数据里的 id，写错就是写错
        BuffDefinition buff = _table.Get(buffId);

        // 施加也顺手清一次：任何一次交互都可能是一天里唯一碰这个系统的那个入口，
        // 过期的那几条不该等到下一次查询才消失（清理是幂等的，见 MultiplierFor 的注释）
        Purge(now);

        // 同 id 覆盖 = 刷新时长、不叠加强度：强度永远是表上那一个数（见 IBuffSystem.Apply）
        _active[buffId] = new ActiveBuff(buff, buff.ExpiresAt(now));
    }

    public bool Remove(string buffId)
    {
        if (buffId is null) throw new ArgumentNullException(nameof(buffId));

        return _active.Remove(buffId);
    }

    public int Purge(GameTime now)
    {
        // 先收集再删：边遍历边改字典会抛，而「到期的那几条」永远是个短名单
        List<string>? expired = null;
        foreach (KeyValuePair<string, ActiveBuff> entry in _active)
        {
            if (!entry.Value.IsExpiredAt(now)) continue;

            (expired ??= new List<string>()).Add(entry.Key);
        }

        if (expired is null) return 0;

        foreach (string id in expired) _active.Remove(id);
        return expired.Count;
    }

    public double MultiplierFor(BuffTarget target, GameTime now)
    {
        // 先挡认不出的目标：悄悄当成「无修正」会让新加的目标属性永远是 1.0，而没有任何报错
        if (!Enum.IsDefined(target))
            throw new ArgumentOutOfRangeException(nameof(target), target, "这个目标属性还没有接上任何消费者");

        // 查询即清理（理由写在 IBuffSystem.MultiplierFor 上）：查得到的与清掉的用同一个 now
        Purge(now);

        double product = 1.0;
        foreach (ActiveBuff buff in _active.Values)
        {
            if (buff.Definition.Target != target) continue;
            if (!buff.IsActiveAt(now)) continue;

            // 相乘不相加：同属性的几条各自是一个独立因素（§8.3 的写法），取最大会让多吃一道菜白吃
            product *= buff.Definition.Multiplier;
        }

        return product;
    }

    /// <summary>打坐的第五项（§8.3 的「丹药」那一行）。见 <see cref="ICultivationSpeedBonus"/>。</summary>
    public double MultiplierAt(GameTime now) => MultiplierFor(BuffTarget.CultivationSpeed, now);

    public string SaveKey => "buffs";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    /// <remarks>
    /// <b>1</b>：本切片（M3-6）的第一版，一个 active 数组、每条两个字段。
    /// <b>旧档（M2 到 M3-5）里没有 <c>buffs</c> 这个键</b>，那不是版本迁移而是**整键缺席**：
    /// <c>SqliteSaveService</c> 对缺席的键是「跳过、让它保持自己的初始状态」，而这里的初始状态
    /// （一条增益都没有）正是那几版的世界里成立的事实——只是当时没有任何东西能把它说出来。
    /// </remarks>
    public int Version => 1;

    /// <summary>
    /// 按 id 排序写出去：字典的遍历顺序不是语言承诺的，而存档要能被人 diff（ADR-012）。
    /// </summary>
    public string Serialize() =>
        JsonSerializer.Serialize(
            new SavedBuffs(
                _active.Values
                    .OrderBy(active => active.Definition.Id, StringComparer.Ordinal)
                    .Select(active => new SavedBuff(active.Definition.Id, active.ExpiresAt))
                    .ToList()),
            _saveJsonOptions);

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009）
        if (fromVersion > Version)
            throw new NotSupportedException($"增益存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedBuffs saved = JsonSerializer.Deserialize<SavedBuffs>(json, _saveJsonOptions)
            ?? throw new InvalidDataException("增益存档内容为空");

        // 「字段不在」与「数组是空的」必须分开（ADR-009 / AGENT-BRIEF）：一条增益都没有的玩家写出去
        // 的是一个空数组，而缺字段说明这份数据不是本系统写的（被人改过、或写到一半崩了）
        if (saved.Active is not List<SavedBuff> active)
            throw new InvalidDataException("增益存档缺少 active 字段");

        var loaded = new Dictionary<string, ActiveBuff>(StringComparer.Ordinal);
        foreach (SavedBuff entry in active)
        {
            if (string.IsNullOrWhiteSpace(entry.BuffId))
                throw new InvalidDataException("增益存档里有一条没有 buffId 的记录");

            if (entry.ExpiresAtMinute is not int expiresAt)
                throw new InvalidDataException($"增益存档里「{entry.BuffId}」缺少 expiresAtMinute 字段");

            // 0 或负数只可能是坏档：本系统写出去的到期时刻 = TotalMinutes（≥ 0）+ 正时长 > 0，
            // 而 0 是「元年春 1 日 6:00 整就到期了」——那是任何一条增益都到不了的时刻
            if (expiresAt <= 0)
                throw new InvalidDataException(
                    $"增益存档里「{entry.BuffId}」的到期时刻 {expiresAt} 不是正数——这个档自相矛盾");

            // 表与存档都是数据，对不上就是数据错误，不许「猜着读」（同 SpiritLandSystem 对等级 id 的处理）
            if (!_table.TryGet(entry.BuffId!, out BuffDefinition buff))
                throw new InvalidDataException($"增益存档里的「{entry.BuffId}」不在增益表里");

            // 同 id 只有一条（再施加是刷新），所以两份并存是自相矛盾；而哪一份生效取决于文件顺序
            if (!loaded.TryAdd(entry.BuffId!, new ActiveBuff(buff, expiresAt)))
                throw new InvalidDataException($"增益存档里「{entry.BuffId}」出现了两次");
        }

        // 先整份校验再落盘：坏存档不该让增益停在「读了一半」的状态（照 CultivationSystem 先例）
        _active.Clear();
        foreach (KeyValuePair<string, ActiveBuff> entry in loaded) _active.Add(entry.Key, entry.Value);
    }

    /// <summary>
    /// 身上那条增益：定义（表的引用，不是抄一份数）与到期时刻。
    /// </summary>
    /// <remarks>
    /// <b>施加时刻是算出来的，所以不存</b>：它就是「到期时刻 − 表上的时长」，而这个差值决定
    /// 「<paramref name="now"/> 是不是还没到它开始生效的时候」——传入一个比施加还早的时刻时，
    /// 这一条既不该算生效、也不该被当成过期清掉。查询因此是 (增益, 时刻) 的纯函数，
    /// 时刻乱序问也答得对。
    /// </remarks>
    private sealed class ActiveBuff
    {
        public ActiveBuff(BuffDefinition definition, int expiresAt)
        {
            Definition = definition;
            ExpiresAt = expiresAt;
        }

        public BuffDefinition Definition { get; }

        public int ExpiresAt { get; }

        /// <summary>施加那一刻。</summary>
        private int StartsAt => ExpiresAt - Definition.DurationMinutes;

        /// <summary>
        /// 左闭右开：施加那一刻算生效，到期那一刻起不算（同 §8.3 时辰带的边界写法）。
        /// </summary>
        /// <remarks>
        /// <b>上界走 <see cref="IsExpiredAt"/>，不在这里另写一遍 `now &lt; ExpiresAt`</b>：
        /// 「到期了没有」只写一份——两条互为否定的判据（`now &lt; 到期` 与 `now &gt;= 到期`）
        /// 摆在不同方法里迟早会漂，而漂了之后症状是「查询还会算它、清理却不认它」这种
        /// 只有把两处并排读才看得出的怪事。这里保留下界（施加之刻）是为了让这个判据**独立成立**：
        /// 就算调用方没有先清理，问「此刻生效吗」也答得对。
        /// </remarks>
        public bool IsActiveAt(GameTime now) => now.TotalMinutes >= StartsAt && !IsExpiredAt(now);

        public bool IsExpiredAt(GameTime now) => now.TotalMinutes >= ExpiresAt;
    }

    /// <summary>
    /// 存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。
    /// </summary>
    /// <remarks>
    /// <c>BuffId</c> 与 <c>ExpiresAtMinute</c> 都可空是为了把「字段不在」与「写了 0」分开
    /// （ADR-009）：到期时刻 0 不是合法值（见 <see cref="Deserialize"/>），静默读成它会让一条增益
    /// 永远不生效。
    /// </remarks>
    private sealed record SavedBuff(string? BuffId, int? ExpiresAtMinute);

    /// <inheritdoc cref="SavedBuff"/>
    private sealed record SavedBuffs(List<SavedBuff>? Active);

    /// <summary>字段名写全，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions _saveJsonOptions = new() { WriteIndented = true };
}
