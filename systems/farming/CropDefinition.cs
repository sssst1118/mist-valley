namespace XingGame.Systems.Farming;

/// <summary>
/// 一种作物的种植数据，来自 §6.2 作物列表。
/// </summary>
/// <remarks>
/// 这些字段<b>不属于</b><see cref="XingGame.Systems.Items.ItemDefinition"/>：物品表回答「这东西是什么」，
/// 而生长天数回答的是「种子种下去之后会怎样」——后者是种植领域的概念（ADR-013）。
/// </remarks>
/// <param name="SeedId">种子物品 id，如 <c>seed_parsnip</c>。作物表以此为主键。</param>
/// <param name="CropId">收获产出的物品 id，如 <c>crop_parsnip</c>。</param>
/// <param name="GrowthDays">§6.2「生长天数」——从播种到成熟所需的天数（浇水或下雨才计入）。</param>
/// <param name="Regrowable">§6.2「可多次收获」。收获后不拔株，重新计时再长一轮。</param>
public sealed record CropDefinition(
    string SeedId,
    string CropId,
    int GrowthDays,
    bool Regrowable);
