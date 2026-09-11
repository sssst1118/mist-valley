namespace XingGame.Systems.Cultivation;

/// <summary>
/// 农场的灵脉与福地：现在在哪一级、由此得到的灵气浓度。§8.8 末尾的「游戏绑定」——
/// 农场初始为「微型灵脉 + 一阶福地」，玩家可通过修复「山谷之心」、建造「聚灵阵」、种植灵植
/// 把两者升上去。
/// </summary>
/// <remarks>
/// <para>
/// <b>等级是状态，浓度是派生量</b>：等级进存档（它是玩家做过的事的结果，没有第二个来源能算出来），
/// 浓度**不进**——它就是 <see cref="SpiritVeinGrade.ConcentrationMultiplier"/> 那一个数，
/// 存第二份迟早与表对不上（同灵力上限不进存档的理由）。所以本接口也就没有「Set 浓度」这种入口。
/// </para>
/// <para>
/// <b>升级的口子还没有（刻意的，不是漏做）</b>：§8.8 列的三条升级路径（山谷之心 / 聚灵阵 / 种灵植）
/// 一条都还不存在。留一个永远成功的 <c>Upgrade</c> 空壳比没有它更坏——调用方会以为那就是规则
/// （同 <see cref="ICultivationSystem"/> 不建 <c>BreakThrough</c> 的理由）。今天能改等级的路径
/// 只有两条：构造（新档的起点）与读档。
/// </para>
/// <para>
/// <b>它同时是 <see cref="ISpiritVeinSource"/></b>：打坐要的就是 <see cref="ISpiritVeinSource.DensityMultiplier"/>
/// 那一个数，而那个数从这里来。窄接口在前、状态在后，所以修炼那边只欠一个浓度。
/// </para>
/// </remarks>
public interface ISpiritLandSystem : ISpiritVeinSource
{
    /// <summary>当前灵脉等级（§8.8 六级之一）。</summary>
    SpiritVeinGrade Vein { get; }

    /// <summary>当前福地阶（§8.8 九阶之一）。**只存等级、不带数值**：文档给的是名称与说明。</summary>
    BlessedLandGrade Land { get; }
}
