namespace XingGame.Systems.Cultivation;

/// <summary>
/// §8.2 末尾「游戏绑定」的三条门槛（<c>docs/public/design.md</c> 496-502 行）：
/// 种植灵植要炼气 4 层、进入秘境要 7 层、招募外门弟子要 5 层。
/// </summary>
/// <remarks>
/// <b>枚举成员名就是数据表里的 id</b>（<c>RealmTable</c> 按名字逐字对，不收数字）——
/// 把这三条做成枚举而不是三个 bool 开关，是因为调用方要传的是「哪一条门槛」，
/// 而「种植灵植到底要几层」只该由境界表回答一次（同 <c>Season</c> 的写法）。
/// </remarks>
public enum CultivationGate
{
    /// <summary>种植灵植（灵气浇灌），炼气 4 层。</summary>
    SpiritPlanting,

    /// <summary>进入秘境（可施展基础防护法术），炼气 7 层。</summary>
    SecretRealm,

    /// <summary>招募外门弟子（玩家修为足够镇场），炼气 5 层。</summary>
    OuterDisciple,
}

/// <summary>
/// 一条门槛的完整内容：门槛本身 + 它的中文名 + 要求的大境界/层数 + 文档给的括号理由。
/// </summary>
/// <remarks>
/// <see cref="Name"/> 与 <see cref="Reason"/> 是给 UI 的文案（「招募外门弟子」/「玩家修为足够镇场」），
/// 让「为什么不能做」这句话有出处——否则面板只能自己编一句解释，而那正是各模块都在躲的
/// 「面板里没有玩法判断」（ADR-007）。
/// </remarks>
public sealed record CultivationGateRequirement(
    CultivationGate Gate,
    string Name,
    string RealmId,
    int Stage,
    string Reason);
