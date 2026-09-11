using System;
using System.Collections.Generic;
using XingGame.Core.Time;

namespace XingGame.Systems.Buffs;

/// <summary>
/// 玩家身上生效的**限时增益**（§8.3 的丹药、§8.2 的轻身术、§12.4 的料理）：施加、查询某个属性的
/// 总修正、到期清理、提前移除。桥接层与将来的丹药 / 法术系统都只认这个接口。
/// </summary>
/// <remarks>
/// <para>
/// <b>时间由调用方给，本系统不订阅任何事件（刻意的）</b>：<c>TimeService</c> 并不逐分钟广播，
/// 而增益是按游戏时间到期的——仓库里现有的形状是「谁需要推进，谁就在自己的入口里收
/// <see cref="GameTime"/> 与分钟数」（见 <c>CultivationSystem.Meditate</c> / <c>RecoverSpirit</c>），
/// 所以这里的每个入口也都收一个 <see cref="GameTime"/>。<b>不为一个增益去发明逐分钟事件</b>
/// （那要动时间系统，代价落在别处）；<b>也不拿 <c>DayStarted</c> 当日推进</b>——一天只发一次，
/// 7 天的增益与 1 小时的轻身术都会算错。
/// </para>
/// <para>
/// <b>到期由绝对时刻判，不记「还剩多久」</b>：见 <see cref="BuffDefinition.ExpiresAt"/>。
/// </para>
/// </remarks>
public interface IBuffSystem
{
    /// <summary>
    /// 施加 <paramref name="buffId"/> 这条增益，**从 <paramref name="now"/> 这一刻起算时长**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>同一个 id 再施加 = 刷新时长：不叠加强度，也不累加时长</b>（本切片的定论，文档没写这一条）。
    /// 再吃一颗聚气散仍是 +50%，只是「7 天」从这一刻重新开始。三条理由：
    /// ① §8.3 给的是「一颗丹 +50%」，文档没有第二颗的规则——而强度翻倍是玩家一眼就看得出的错
    /// （数值会从 1.5 变成 2.25，摆在那里）；
    /// ② 累加时长（旧到期时刻 + 7 天）会让攒一批丹药变成一个能无限期续上的护身符，而文档连丹药的
    /// 价钱与配方都还没给（备案 #73），累加等于给将来的经济系统埋一个没人算过的口子；
    /// ③ 刷新是玩家唯一想得到的语义——「我又吃了一颗，从现在起算」。
    /// 代价写在明处：在第 6 天再吃一颗，剩下那 1 天不会攒着（总时长从 13 天变成 12 天）。
    /// </para>
    /// <para>
    /// <b>不同 id 作用在同一属性上是相乘，不是取最大</b>：聚气散（×1.5）撞上灵芽羹（×1.2）是
    /// ×1.8 而不是 ×1.5。§8.3 把每一行都列成一个**独立的影响因素**，而合成器那边本来就是
    /// 「灵根 × 季节 × 时辰 × 灵脉」连乘不相加（那一行注释里明写了「再加因素就在这一行再乘一项」）；
    /// 取最大则会让「多吃一道菜」变成白吃。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="buffId"/> 是 null。</exception>
    /// <exception cref="KeyNotFoundException">表里没有这个 id（id 由调用方的数据给出，写错就是写错）。</exception>
    void Apply(string buffId, GameTime now);

    /// <summary>
    /// 提前结束 <paramref name="buffId"/> 这条增益（将来的解毒 / 驱散 / 剧情，以及调试）。
    /// </summary>
    /// <returns>
    /// 本来在不在身上。不在时返回 <c>false</c>，**不抛**——「身上本来就没有」是正常结果，不是编程
    /// 错误（同 <c>Inventory.Remove</c> 对不在物品表里的 id 的处理：删一个不存在的东西 = 没这回事）。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="buffId"/> 是 null。</exception>
    bool Remove(string buffId);

    /// <summary>
    /// 清掉此刻**已经到期**的增益，返回清掉的条数。
    /// </summary>
    /// <remarks>
    /// <b>查询与施加都会自动调一次，所以游戏里不必单独排期</b>（见 <see cref="MultiplierFor"/>）。
    /// 还没开始生效的（施加时刻在 <paramref name="now"/> 之后的）**不会被清**——它们只是「还没到时候」，
    /// 而不是过期。
    /// </remarks>
    int Purge(GameTime now);

    /// <summary>
    /// <paramref name="target"/> 这个属性此刻的总修正：把这一刻生效的增益连乘起来。
    /// 一条都没有（或全到期了）时是 <b>1.0</b>——乘法的单位元，可以直接乘进各自的合成器里。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>查询顺手清理到期的那几条（故意的，不是副作用失控）</b>：到期的那一条必须消失，否则它会
    /// 永远留在存档里——这个游戏没有逐分钟事件可以定时来清（见接口注释）。而清理与查询共用同一个
    /// <paramref name="now"/>，「查得到的」与「清掉的」才是同一把尺子（两处各判一次迟早会漂，
    /// 同 §8.3 时辰带只写一份 `Contains` 的理由）。清理是幂等的，也不影响本次结果：
    /// 清掉的本来就是不参与连乘的那些。
    /// </para>
    /// <para>
    /// <b>生效区间左闭右开</b>（<c>施加 ≤ now &lt; 到期</c>，同 §8.3 时辰带与各表区间的写法）：
    /// 到期那一刻起就不再生效，于是「同一时刻施加与到期」有唯一答案——不会出现两条增益在同一分钟里
    /// 既算「已到期」又算「还生效」。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="target"/> 不是枚举里的成员——悄悄当成「无修正」会让新加的目标属性永远是 1.0，
    /// 而没有任何报错（同 <c>LifeSpellSystem.CanAffect</c> 对没接上的效果的处理）。
    /// </exception>
    double MultiplierFor(BuffTarget target, GameTime now);
}
