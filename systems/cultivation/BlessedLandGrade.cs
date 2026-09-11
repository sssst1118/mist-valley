namespace XingGame.Systems.Cultivation;

/// <summary>
/// 一阶福地（§8.8 的「福地与洞天」表，<c>docs/public/design.md</c> 1015-1026 行）。数据在
/// <c>data/cultivation/spirit_land.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>九阶表没有数值列，所以本记录也没有数值字段</b>：§8.8 给的是名称与说明（「可建造修炼室、
/// 炼丹房」「独立小世界，不受外界季节天气影响」…），那些是**资格**与**玩法开关**，不是百分比。
/// 唯一提到数值的是末尾那句「福地等级决定农场的灵气浓度」，而它没有配套的表——编一个出来就是
/// 自造设定（见 <see cref="SpiritVeinGrade"/> 的注释）。本记录只把原文录下来，供灵气感知界面
/// 显示「你现在是几阶、它意味着什么」。
/// </para>
/// <para>
/// <b>阶号单独存一个字段，而不是拿数组下标当阶号</b>：下标取决于文件顺序，而「几阶」是文档里的
/// 事实（一阶就是 1）。表的加载会要求九阶连续、不重不漏——少了「五阶」的话，「升一阶」这条路
/// 就在没人看得见的地方断了。
/// </para>
/// </remarks>
/// <param name="Order">§8.8「等级」列的阶号（一阶 ⇒ 1）。</param>
/// <param name="Description">§8.8「说明」一列的原文，逐字录。</param>
public sealed record BlessedLandGrade(
    string Id,
    int Order,
    string Name,
    string Description);
