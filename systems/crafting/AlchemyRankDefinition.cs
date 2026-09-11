namespace XingGame.Systems.Crafting;

/// <summary>
/// 炼丹师的一品（§8.7，<c>docs/public/design.md</c> 第 958-970 行）：这一品叫什么 + 能炼到几阶丹药。
/// </summary>
/// <remarks>
/// <para>
/// <b>九行的名称与「可炼制丹药品阶」逐字照抄 §8.7</b>，包括文档自己那处重复（六至九品都叫
/// 「炼丹宗师」）——<b>不替文档改错字</b>，改了这里就再也说不清哪一行的名字出自哪里。
/// </para>
/// <para>
/// <b>「一品炼丹学徒 可炼制 一阶」这条关系的唯一住处就是本表</b>：配方只说「做出来的是几阶丹药」
/// （<c>RecipeDefinition.PillTier</c>），「要几品才炼得动」由本表答（<see cref="IAlchemyRankTable.RequiredRankForTier"/>）。
/// 两处各写一份的话，改了一处就会得到「界面说要二品、判定按一品放行」这种只在某一刻显形的错。
/// </para>
/// </remarks>
public sealed record AlchemyRankDefinition(int Rank, string Name, int MaxTier);
