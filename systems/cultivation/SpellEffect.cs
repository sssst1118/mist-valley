namespace XingGame.Systems.Cultivation;

/// <summary>
/// 一条法术**做什么**（§8.2 表里的「游戏表现」一列）。数据在 <c>data/cultivation/spells.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么「做什么」也进数据，而不是在代码里按法术 id 分支</b>：灵雨术与灵锄术跟灵气浇灌 /
/// 灵锄是**同一件事配一个更大的范围**（「范围浇水」= 浇灌 + 3×3）。按 id 写 <c>if</c> 的话，
/// 「再加一条范围更大的法术」就变成了改代码，而它本该只是表里多一行。效果的种类有限且互斥，
/// 正好是一个枚举——同 <see cref="SpiritRecovery"/> 与 <see cref="CultivationGate"/> 的写法。
/// </para>
/// <para>
/// <b>枚举成员名就是数据表里的 id</b>（加载时逐字对，不收数字）：<c>Enum.TryParse</c> 连 "0" 都收，
/// 放行它就等于允许按序号写效果——往枚举中间插一项时，旧数据会静默指向另一个效果。
/// </para>
/// </remarks>
public enum SpellEffect
{
    /// <summary>
    /// 感知：只有解锁，没有作用——§8.2 的「灵气感知」解锁的是一个界面（看灵田的灵气浓度），
    /// 而**灵气浓度属于灵脉/福地那一刀**（备案 #72），本切片不去编一个假的浓度出来。
    /// </summary>
    Sense,

    /// <summary>浇灌：把范围内的格浇上水（灵气浇灌 / 灵雨术）。§8.2 的「消耗灵力代替浇水」。</summary>
    Water,

    /// <summary>开垦：把范围内的格开垦成耕地（灵锄术）。§8.2 的「范围耕地」。</summary>
    Till,
}
