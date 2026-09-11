using System.Collections.Generic;

namespace XingGame.Systems.Crafting;

/// <summary>
/// 一条材料需求：某种物品要几个。<see cref="Count"/> 恒为正——非正的需求没有意义，
/// 加载时就该被拒（配方表是外部输入）。
/// </summary>
public sealed record RecipeIngredient(string ItemId, int Count);

/// <summary>
/// 一条配方：<b>若干材料 → 一件产物</b>。数值出处见 <see cref="RecipeTable"/> 的类注释。
/// </summary>
/// <remarks>
/// <para>
/// <b>没有「配方名」字段</b>：一条配方做出来的就是那件产物，名字在物品表里已经有了
/// （<c>craft_sprinkler</c> → 「洒水器」）。两处各存一份名字，改一处就会不一致，而 UI 要显示名字时
/// 拿 <see cref="OutputItemId"/> 去物品表取即可。
/// </para>
/// <para>
/// <b>没有「效果」字段</b>：§12.4 的料理效果（恢复体力/灵力、临时增益）要有一套增益系统才有人消费，
/// 那是 M3 的事——本切片不提前实现高阶系统，也不留一个没人读的字段。三道料理的效果文字暂时写在
/// <b>产物的 description 里</b>（§12.4 原文，游戏里看得见）；机器可读的效果等增益系统落地再补。
/// </para>
/// <para>
/// <b><see cref="Station"/> 记的是「这条配方在哪个台上做」，今天只推出了一条</b>：§12.3 给了四个制作台
/// （背包内、工坊、炼丹房、炼器阁），但<b>哪条配方要在哪个台上做，文档一个字都没给</b>——能推的照
/// §6.7 的建筑功能表推（炼丹房的「炼制丹药」⇒ 筑基丹在炼丹房），推不出来的一律
/// <see cref="CraftingStation.Unassigned"/>，不许照直觉补。今天还没有运行期闸门（那三座建筑都不存在），
/// 这一位先落进数据，见 <see cref="CraftingStation"/> 的类注释。
/// </para>
/// <para>
/// <b><see cref="PillTier"/> 记的是「做出来的是几阶丹药」</b>（§8.7 一至十二阶），只有
/// <see cref="RecipeCategory.Pill"/> 允许有。它<b>不是门槛本身</b>：门槛由
/// <see cref="IAlchemyRankTable.RequiredRankForTier"/> 从阶数算出来（「一品炼丹学徒 可炼制 一阶」），
/// 所以「几品能炼几阶」只在品级表里写一份。**丹药必有阶**（§8.7 就是照阶把丹药定义下来的），
/// 所以丹药配方缺了这一位是数据错误，加载时就炸。
/// </para>
/// </remarks>
public sealed record RecipeDefinition(
    string Id,
    RecipeCategory Category,
    CraftingStation Station,
    string OutputItemId,
    int OutputCount,
    int? PillTier,
    IReadOnlyList<RecipeIngredient> Ingredients);
