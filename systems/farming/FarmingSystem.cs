using System;
using XingGame.Core.Events;
using XingGame.Core.Time;
using XingGame.Systems.Items;

namespace XingGame.Systems.Farming;

/// <summary>
/// 把耕地接到时间与背包上：订阅事件、驱动 <see cref="Farmland"/>、把收获放进背包。
/// </summary>
/// <remarks>
/// <para>
/// 时间系统只报时、不做效果（ADR-006）：雨天自动浇水、换季枯萎都不在 <c>TimeService</c> 里，
/// 而是本类订阅事件后自己做的事。所以「下雨算不算浇过水」这条规则只在这里有一份。
/// </para>
/// <para>
/// 构造时订阅、<see cref="Dispose"/> 时退订——不要留无主订阅（ADR-005）。
/// 退订之后本系统对事件完全无反应，测试专门守着这条。
/// </para>
/// </remarks>
public sealed class FarmingSystem : IDisposable
{
    private readonly ITimeService _time;
    private readonly ICropTable _crops;
    private readonly Farmland _farmland;
    private readonly IInventory _inventory;
    private readonly IDisposable _daySubscription;
    private readonly IDisposable _seasonSubscription;

    private bool _disposed;

    /// <param name="time">天气的来源。作物长不长取决于当天是不是雨天，而天气只有时间系统知道。</param>
    public FarmingSystem(IEventBus bus, ITimeService time, ICropTable crops, Farmland farmland, IInventory inventory)
    {
        if (bus is null) throw new ArgumentNullException(nameof(bus));

        _time = time ?? throw new ArgumentNullException(nameof(time));
        _crops = crops ?? throw new ArgumentNullException(nameof(crops));
        _farmland = farmland ?? throw new ArgumentNullException(nameof(farmland));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));

        _daySubscription = bus.Subscribe<DayStarted>(OnDayStarted);
        _seasonSubscription = bus.Subscribe<SeasonChanged>(OnSeasonChanged);
    }

    /// <summary>
    /// 播种并消耗背包里的一粒种子。格不能种、或背包里没有这粒种子时<b>不消耗任何东西</b>并返回 false。
    /// </summary>
    /// <remarks>
    /// 先确认背包里确实有种子，再动耕地：反过来的话，播种成功而种子不足就会白送一株作物。
    /// </remarks>
    public bool TryPlant(TileCoord tile, string seedId)
    {
        // 未知种子是编程错误（同 Inventory.Add）：凭空播一粒表里没有的种子，必然是 id 写错了
        _crops.GetBySeed(seedId);

        if (_inventory.Count(seedId) < 1) return false;
        if (!_farmland.TryPlant(tile, seedId)) return false;

        _inventory.Remove(seedId, 1);
        return true;
    }

    /// <summary>
    /// 收获一格并把作物收进背包。没成熟时返回 false，什么都不发生。
    /// </summary>
    /// <remarks>
    /// 背包满时溢出的部分会丢——<b>M1 的已知取舍</b>：一次收获只产出 1 个，
    /// 而背包是 24 格 × 999 上限，正常玩法到不了这个边界；M2 做掉落物时再处理。
    /// </remarks>
    public bool TryHarvest(TileCoord tile)
    {
        if (!_farmland.TryHarvest(tile, out string cropId, out int count)) return false;

        _inventory.Add(cropId, count);
        return true;
    }

    /// <summary>退订全部订阅。</summary>
    public void Dispose()
    {
        // 重复 Dispose 不该把订阅表改坏：调用方（桥接层节点）在异常路径上重复调用是常事
        if (_disposed) return;

        _daySubscription.Dispose();
        _seasonSubscription.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// 读 <c>time.Weather</c> 而不是某个事件携带的天气：<c>TimeService</c> 在发 <c>DayStarted</c>
    /// <b>之前</b>就换好了当天的天气，所以此刻读到的一定是今天的。
    /// </summary>
    private void OnDayStarted(DayStarted e) => _farmland.AdvanceDay(IsRaining(_time.Weather));

    /// <summary>换季枯萎（§3.2）。温室是 M2+，M1 全部视为露天（ADR-014）。</summary>
    private void OnSeasonChanged(SeasonChanged e) => _farmland.WitherAll();

    /// <summary>§3.3「雨天自动浇水」——下雨与暴风雨都算。</summary>
    private static bool IsRaining(Weather weather) => weather is Weather.Rainy or Weather.Storm;
}
