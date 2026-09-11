namespace XingGame.Systems.Farming;

/// <summary>
/// 一格的土壤状态。
/// </summary>
/// <remarks>
/// 浇水<b>不是</b>一层独立的标记，而是土壤状态的一个取值：一块地不可能「既是荒地又浇过水」，
/// 用两个字段表达就会容许这种不存在的组合。浇过水的效果只持续到当天推进结束（见
/// <see cref="Farmland.AdvanceDay"/>）。
/// </remarks>
public enum SoilState
{
    /// <summary>未开垦。没被锄头动过的地，不能播种。</summary>
    Untilled,

    /// <summary>已开垦。</summary>
    Tilled,

    /// <summary>已开垦且当天浇过水。当天推进时该格的作物会长一天。</summary>
    Watered,
}
