using XingGame.Core.Time;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 玩家当前的修仙状态：灵根 + 境界 + 层数 + 修为，由它派生的**解锁查询**，以及唯一的入口
/// 「打坐」。桥接层与将来的种植 / 秘境 / 宗门系统都只认这个接口（M2-A 契约：注册表按接口注册）。
/// </summary>
/// <remarks>
/// <para>
/// <b>「怎么涨」到 M3-2 只走一步：<see cref="Meditate"/></b>——按一段时间结算修为、攒够就升层。
/// 炼气期内部没有突破关口（§8.1 说炼气「按 1-13 层逐层递进」，§8.4 的突破与渡劫是**跨大境界**
/// 的事），所以层与层之间是自动的。**跨大境界仍然没有口子**：炼气 → 筑基要筑基丹（§8.4），
/// 那是 M4 的事——留一个永远成功的 <c>BreakThrough</c> 空壳比没有它更坏，调用方会以为那就是规则。
/// </para>
/// <para>
/// <b>门槛比较跨大境界</b>：§8.2 的三条绑定写的是「至少炼气 N 层」，而筑基及以上的修士
/// 显然也算达到过。所以比较用的是「大境界在 §8.1 表里的先后 + 层号」，不是单看层号——
/// 详见 <see cref="Reaches"/>。
/// </para>
/// </remarks>
public interface ICultivationSystem
{
    /// <summary>当前灵根品级（§4.2 六档之一）：修炼速度倍率与三个突破概率都挂在它上面。</summary>
    SpiritRootGrade Grade { get; }

    /// <summary>当前的具体灵根（§4.3 变异 / §4.4 先天异）。**四档普通品级没有具体灵根**，为 null。</summary>
    SpiritRootDefinition? Root { get; }

    /// <summary>当前大境界。</summary>
    RealmDefinition Realm { get; }

    /// <summary>当前小境界，从 1 起：炼气期是 1..13 层，筑基到合体是 1..4（初期…大圆满），渡劫期是 1..2。</summary>
    int Stage { get; }

    /// <summary>
    /// 当前层里已攒的修为。**不是总共练了多少**：攒够本层开销就扣掉并升层（§8.1 的炼气期是
    /// 逐层递进的台阶）。所以它恒在 <c>[0, 升到下一层所需)</c> 之内；炼气十三层（顶点）恒为 0。
    /// </summary>
    int Cultivation { get; }

    /// <summary>
    /// 当前的灵力（§8.2「在丹田中积蓄气态灵力」）。**这是唯一被存下来的那一个数**；
    /// 上限是从层数算出来的，见 <see cref="MaxSpirit"/>。
    /// </summary>
    int Spirit { get; }

    /// <summary>
    /// 当前层数下的灵力上限：<c>100 + 25 × (层 - 1)</c>（备案 #69）。1 层 100、4 层 175、13 层 400。
    /// </summary>
    /// <remarks>
    /// <b>派生量，不是第二份状态</b>：上限现算（<see cref="ISpiritPowerTable.MaxSpiritAt"/>），
    /// 存档里只有 <see cref="Spirit"/> 一个数。若把上限也存一份，改系数或调层数时两份就会对不上——
    /// 那正是 M2 那批「同一个事实存两处」的老病（同 <c>worldSeed</c> 不许存两处的理由）。
    /// </remarks>
    int MaxSpirit { get; }

    /// <summary>
    /// 此刻打坐有多快（§4.2 灵根 × §8.3 季节 × §8.3 时辰 × §8.8 灵气浓度，**相乘**）。
    /// 给 UI 解释「现在打坐多快」用；要算具体涨多少修为走 <see cref="Meditate"/>。
    /// </summary>
    /// <remarks>
    /// 第四项（灵气浓度）来自打坐处——M3-5 起是农场的灵脉等级（微型 1.10 … 龙脉 6.00）。
    /// 它进的是**乘数**，与 §8.3 表里那一行的百分数是同一组数，不是另造的一套刻度。
    /// </remarks>
    double SpeedMultiplierAt(GameTime now);

    /// <summary>
    /// 打坐 <paramref name="minutes"/> 游戏分钟（§3.1 里这通常是清晨 6:00-9:00 那一项活动），
    /// 按 <paramref name="now"/> 这一刻的倍率结算修为，攒够本层开销就升层。
    /// </summary>
    /// <returns>这次打坐**真正记下**的修为：到了炼气十三层之后溢出的部分作废，不算在里面。</returns>
    int Meditate(GameTime now, int minutes);

    /// <summary>
    /// 花掉 <paramref name="amount"/> 点灵力，**全有或全无**：不够就一点都不扣，返回 false。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 失败语义照 <c>Wallet.TrySpendGold</c> 与 <c>Inventory.Remove</c> 的既有惯例（不够就是没发生），
    /// 不另发明第三种（比如扣到 0 再返回「还差 5 点」）——同一件事两种失败语义，调用方迟早按错的那种写。
    /// </para>
    /// <para>
    /// <b>这是「消耗的原语」，不是法术表</b>：第一个真正的消费者是 §8.2 的「灵气浇灌」（炼气 4 层起，
    /// 消耗灵力代替浇水），那要接 <c>FarmingSystem</c>，跟着法术那一刀走。本切片**不建**四个法术的
    /// 消耗常量（备案 #70 的轻身术 10 / 小回春术 30 / 灵雨术 50 / 灵锄术 50）——现在建就是四个
    /// 没有调用方的数（铁律 11）。
    /// </para>
    /// </remarks>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="amount"/> 不是正数。</exception>
    bool TrySpendSpirit(int amount);

    /// <summary>
    /// 玩家用 <paramref name="minutes"/> 游戏分钟做了 <paramref name="recovery"/> 这件事，
    /// 按对应速率回灵力（备案 #71：清醒 2/小时、打坐 5/小时），**封在上限**。
    /// </summary>
    /// <returns>这次**真正回上**的点数：已经在上限时是 0。</returns>
    /// <remarks>
    /// <para>
    /// <b>为什么是显式入口，而不是订阅时间事件自动涨</b>：同 <see cref="Meditate"/>——
    /// 「这段时间怎么过的」只有桥接层/组合根知道（<c>GameRoot._Process</c> → <c>_time.Advance</c>
    /// 那一处），系统不该替它猜玩家是在清醒地忙还是坐着入定。
    /// </para>
    /// <para>
    /// <b>打坐这一档刻意不并进 <see cref="Meditate"/>（不是漏做）</b>：两笔账的数据来源不同
    /// （修为走 §8.3 的速度表，灵力走备案 #71），而且筑基及以上的打坐**回气但不结修为**
    /// （那边升层要 §8.4 的丹药，<c>Meditate</c> 当场抛）——并进去就没法表达这个状态。
    /// 所以桥接层做「打坐」这个动作时要调两个入口，各自传同一段时长。
    /// </para>
    /// </remarks>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="minutes"/> 是负数。</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">表里没录这一档（见 <see cref="ISpiritPowerTable.RecoveryPerHour"/>）。</exception>
    int RecoverSpirit(SpiritRecovery recovery, int minutes);

    /// <summary>
    /// 睡了一觉：灵力**全恢复**（备案 #71 的第三条）。
    /// </summary>
    /// <returns>这次**真正回上**的点数：本来就是满的是 0。</returns>
    /// <remarks>
    /// <b>落脚点是 <c>ITimeService.Sleep()</c></b>（玩家睡觉时桥接层调的那个入口），
    /// **不是** <c>DayStarted</c>：玩家在 2:00 昏倒也会跨日（§3.1 的日循环），而昏倒不是睡觉——
    /// 拿跨日当睡眠，等于昏倒一次白送一池灵力。睡眠不按小时计价（<c>Sleep</c> 是**跳跃**而非流逝），
    /// 所以它没有时长参数，也不在 <see cref="SpiritRecovery"/> 里。
    /// </remarks>
    int RecoverSpiritOnSleep();

    /// <summary>
    /// 当前修为是否**已经达到**「<paramref name="realmId"/> 的 <paramref name="stage"/> 层」。
    /// </summary>
    /// <remarks>
    /// 这是给将来的门槛用的原语：同一个大境界里比层号，跨大境界时后一个大境界一律算达到
    /// （§8.1 的表序就是修炼顺序）。层号越界当场抛——「至少炼气 14 层」这种门槛不存在，
    /// 静默当成 13 层会让写错的门槛永远为真。
    /// </remarks>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">表里没有这个境界。</exception>
    /// <exception cref="System.ArgumentOutOfRangeException">层号不在该大境界的范围内。</exception>
    bool Reaches(string realmId, int stage);

    /// <summary>
    /// §8.2 末尾三条「游戏绑定」的解锁判定：种植灵植（炼气 4 层）、进入秘境（7 层）、
    /// 招募外门弟子（5 层）。层数由境界表回答，调用方不许自己写数字。
    /// </summary>
    bool Meets(CultivationGate gate);
}
