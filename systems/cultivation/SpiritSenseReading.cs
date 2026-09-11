namespace XingGame.Systems.Cultivation;

/// <summary>
/// 用「灵气感知」看到的东西（§8.2 炼气 1-3 层：「解锁『灵气感知』界面，可看到**灵田的灵气浓度**」）。
/// </summary>
/// <remarks>
/// <para>
/// <b>读数是**农场级**的，不是按格读的</b>：§8.8 给了灵脉六级与福地九阶两张表，两个都是整座农场的
/// 属性——文档里没有任何一处给出「这一格比那一格浓」的数。按格算浓度就得自己发明一套分布
/// （离灵脉近就浓？），那是自造设定，而且它会让「灵脉 +10%」这句话在代码里多出一个含义
/// （见 <see cref="SpiritVeinGrade"/> 的注释）。
/// </para>
/// <para>
/// <b>三样东西一起给，因为它们是同一句话的三个部分</b>：现在是几级灵脉、几阶福地、浓度多少。
/// 界面照这个形状直接画即可——用不着先问一次「解锁了没有」再分两次取两个字段
/// （那两次之间状态不会变，但读的人得先知道顺序）。
/// </para>
/// </remarks>
/// <param name="Vein">当前灵脉等级（§8.8 六级之一，含「游戏表现」原文）。</param>
/// <param name="Land">当前福地阶（§8.8 九阶之一，含「说明」原文）。</param>
public sealed record SpiritSenseReading(SpiritVeinGrade Vein, BlessedLandGrade Land)
{
    /// <summary>农场的灵气浓度乘数：就是灵脉那一档的 §8.8 数值（微型 1.10 … 龙脉 6.00）。</summary>
    /// <remarks>
    /// 与 <see cref="ISpiritVeinSource.DensityMultiplier"/> 是**同一个数的两个出口**（打坐那边走接口、
    /// 界面这边走读数），都是从 <see cref="Vein"/> 现算的派生量，所以不存在「两份对不上」的问题。
    /// </remarks>
    public double ConcentrationMultiplier => Vein.ConcentrationMultiplier;
}
