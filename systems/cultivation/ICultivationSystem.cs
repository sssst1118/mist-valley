namespace XingGame.Systems.Cultivation;

/// <summary>
/// 玩家当前的修仙状态：灵根 + 境界 + 层数，以及由它派生的**解锁查询**。
/// 桥接层与将来的种植 / 秘境 / 宗门系统都只认这个接口（M2-A 契约：注册表按接口注册）。
/// </summary>
/// <remarks>
/// <para>
/// <b>本接口只回答「是什么」，不回答「怎么涨」</b>：修为的累积、突破的判定、打坐的收益
/// 都不在这里——那需要「每层所需修为」与「基础修炼速度」两个数，而设计文档没给
/// （<c>ARCHITECTURE.md</c> 备案 #67/#68，至今「待用户裁决」）。所以这一轮没有任何
/// <c>AddCultivation</c> / <c>BreakThrough</c> 之类的口子：留一个永远返回成功的空壳，
/// 比没有它更坏——调用方会以为那就是规则。
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
