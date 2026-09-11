namespace XingGame.Systems.Economy;

/// <summary>
/// 金币钱包。存在的意义同 <c>IInventory</c>：不让「按接口取用」这条规矩在钱这里破个口子。
/// </summary>
/// <remarks>
/// <para>
/// <b>只装金币。</b>灵石是物品，不是货币——它住在背包里（<c>material_spirit_stone</c>），
/// 理由见 <see cref="Wallet"/> 的类注释：同一个事实存两处，迟早对不上。
/// </para>
/// <para>
/// 初始资源<b>不在这里发</b>：§4.6 的「金币 500、灵石 0」是存档级的数据，由组合根在建档时
/// 调 <see cref="AddGold"/> 发放（同 §4.6 的工具与种子）。本接口只管「钱本身」。
/// </para>
/// </remarks>
public interface IEconomySystem
{
    /// <summary>金币余额。</summary>
    int Gold { get; }

    /// <summary>
    /// 加钱。<paramref name="amount"/> 必须为正——扣钱走 <see cref="TrySpendGold"/>，
    /// 用负数加法来表达「扣」会让所有调用点的意图看不出来。
    /// </summary>
    void AddGold(int amount);

    /// <summary>
    /// 扣钱，<b>全有或全无</b>：余额不足时返回 false，且<b>余额一分不变</b>
    /// （同 <c>Inventory.Remove</c>，ADR-012）。扣一半是最难查的那种 bug——
    /// 调用方以为失败了，钱却已经少了。
    /// </summary>
    bool TrySpendGold(int amount);
}
