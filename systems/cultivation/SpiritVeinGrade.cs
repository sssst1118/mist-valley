namespace XingGame.Systems.Cultivation;

/// <summary>
/// 一级灵脉（§8.8 的灵脉等级表，<c>docs/public/design.md</c> 1006-1014 行）。数据在
/// <c>data/cultivation/spirit_land.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>「灵气浓度」那一列就是加成百分数，这里落成乘数</b>（+10% ⇒ 1.10，同
/// <c>SpiritRootGrade.CultivationSpeedMultiplier</c> 与 <c>cultivation_speed.json</c> 的写法）：
/// 同一个事实在代码里只能有一个含义。再发明一套「0-100 的浓度值」会让「灵脉 +10%」这句话
/// 需要换一次算才看得懂——而换算写错了不会报错，它只是练得快一点或慢一点。
/// </para>
/// <para>
/// <b>福地那一侧没有乘数（刻意的，不是漏做）</b>：§8.8 的九阶表给了名称与说明，**一个数都没给**
/// （二阶的「灵气浓度提升」、三阶的「可建造修炼室」是定性描述）。所以农场此刻的灵气浓度只由灵脉
/// 等级决定，乘数就是本记录的那一个字段；等文档给福地的数，它才会作为第二项乘进
/// <see cref="ISpiritVeinSource.DensityMultiplier"/>——那一处仍是唯一的合成点。
/// </para>
/// </remarks>
/// <param name="Description">§8.8「游戏表现」一列的原文（「农场初始状态，灵气稀薄」…），逐字录。</param>
public sealed record SpiritVeinGrade(
    string Id,
    string Name,
    double ConcentrationMultiplier,
    string Description);
