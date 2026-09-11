namespace XingGame.Systems.Cultivation;

/// <summary>
/// 一种**具体的**灵根：§4.3 的四种变异灵根（<c>docs/public/design.md</c> 173-179 行）与
/// §4.4 的四种先天异灵根（181-187 行）。品级（<see cref="SpiritRootGrade"/>）管「修炼多快、
/// 突破多难」，这里管「是哪一种、有什么特效」。
/// </summary>
/// <remarks>
/// <para>
/// <b>四档普通品级（伪 / 真三 / 真双 / 天）在这里没有条目</b>：§4.3 与 §4.4 只点名了这八种
/// 特殊灵根，普通品级在文档里就是「几种属性」而不是某个名字。所以玩家的灵根是
/// <b>品级 + 可选的具体灵根</b>两件事，而不是一个 id——不替文档编出「金灵根」「木灵根」这些名字。
/// </para>
/// <para>
/// <b>特效只是文本（本切片刻意不实现机制）</b>：§4.3 的「对应游戏机制」与 §4.4 的「游戏效果」
/// 两列合并成 <see cref="GameEffect"/>（同一含义，只是两节的列名不同），列名与原文照抄。
/// 它落成的字段是给人读的说明，不是可以调用的行为——伤害 +50%、可隐身这些要等对应的
/// 战斗/种植/移动系统来读（M3 之后）。见 <c>tests/M3Audit_Cultivation.cs</c> 的钉子。
/// </para>
/// </remarks>
public sealed record SpiritRootDefinition(
    string Id,
    string Name,
    string GradeId,
    string? MutationSource,
    string? Trait,
    string? ExclusiveDao,
    string? GameEffect);
