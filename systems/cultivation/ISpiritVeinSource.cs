namespace XingGame.Systems.Cultivation;

/// <summary>
/// 打坐处此刻的灵气浓度：§8.3 那七个因素里排在灵脉等级名下的那一项，也是
/// <see cref="CultivationSystem.SpeedMultiplierAt"/> 连乘的**第四项**。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是一个只有单个成员的接口，而不是把农场那件状态整个交给打坐</b>：
/// <see cref="CultivationSystem"/> 要的只是「这里灵气多浓」一个数——它不认识灵脉等级、福地阶、
/// 也不认识「山谷之心修好了没有」，那些是农场的事（同 <c>LifeSpellSystem</c> 只认
/// <c>Farmland</c>、不要整个 <c>FarmingSystem</c> 的理由：多要一层，就是让这个系统欠下一份
/// 它本来不欠的构造顺序）。
/// </para>
/// <para>
/// <b>名字里用「灵脉」是因为今天只有它在出这个数</b>：§8.8 灵脉等级表的第二列就叫「灵气浓度」，
/// 六级各一个百分数。福地那一侧文档一个数都没给（见 <see cref="BlessedLandGrade"/> 的注释），
/// 等补了数它就作为第二项乘进同一个方法——**合成的口子只有这一处**，加因素不用碰打坐的时间账与
/// 升层判定。
/// </para>
/// </remarks>
public interface ISpiritVeinSource
{
    /// <summary>
    /// 这里的灵气浓度，落成乘数（§8.8：微型灵脉 +10% ⇒ 1.10 … 龙脉 +500% ⇒ 6.00）。
    /// **恒为正**：表的加载已经拦下非正数，而这个数是要乘进修炼速度的——0 会让进度永远停住，
    /// 负数会倒着扣。
    /// </summary>
    double DensityMultiplier { get; }
}
