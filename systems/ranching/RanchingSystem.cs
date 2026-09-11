using System;
using System.Collections.Generic;
using XingGame.Core.Events;
using XingGame.Core.Time;
using XingGame.Systems.Items;

namespace XingGame.Systems.Ranching;

/// <summary>牧场对外的口子：养、喂、读心情与好感。桥接层只认这个接口（M2-A 共同规矩）。</summary>
public interface IRanchingSystem
{
    /// <summary>全部动物，按 id 升序。</summary>
    IReadOnlyList<AnimalInfo> Animals { get; }

    /// <summary>
    /// 养一只新动物，返回它的 id。<b>不收钱</b>——买入价在动物表里，扣金币是经济模块与桥接层的事；
    /// 本模块不认识金币，也就不会跟并行开发的经济模块绑在一起。
    /// </summary>
    int AddAnimal(string animalId, string name = "");

    /// <summary>喂食：扣掉一份该动物要的饲料（普通→干草、灵兽→灵草，§6.5）。</summary>
    bool TryFeed(int animalId);

    void Rename(int animalId, string name);

    void SetMood(int animalId, int mood);

    void SetAffection(int animalId, int hearts);
}

/// <summary>
/// 把牧场接到时间与背包上：订阅事件、驱动 <see cref="Ranch"/>、把畜产放进背包。
/// </summary>
/// <remarks>
/// <para>
/// 时间系统只报时、不做效果（ADR-006）：产出由 <c>DayStarted</c> 驱动，而不是每帧——
/// 动物一天产一次，是离散事件（同 ADR-014 对作物生长的定论）。
/// </para>
/// <para>
/// <b>不订阅 <c>WeatherChanged</c>，也不订阅 <c>SeasonChanged</c></b>：§3.2 只说换季时
/// 「非温室作物枯萎」，§3.3 的天气效果里没有一条与动物有关，§6.5 也没写。文档没写的不加。
/// </para>
/// <para>
/// 构造时订阅、<see cref="Dispose"/> 时退订——不要留无主订阅（ADR-005）。
/// 退订之后本系统对事件完全无反应，测试专门守着这条。
/// </para>
/// </remarks>
public sealed class RanchingSystem : IRanchingSystem, IDisposable
{
    private readonly Ranch _ranch;
    private readonly IInventory _inventory;
    private readonly IDisposable _daySubscription;

    private bool _disposed;

    public RanchingSystem(IEventBus bus, Ranch ranch, IInventory inventory)
    {
        if (bus is null) throw new ArgumentNullException(nameof(bus));

        _ranch = ranch ?? throw new ArgumentNullException(nameof(ranch));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));

        _daySubscription = bus.Subscribe<DayStarted>(OnDayStarted);
    }

    public IReadOnlyList<AnimalInfo> Animals => _ranch.Animals;

    public int AddAnimal(string animalId, string name = "") => _ranch.Add(animalId, name);

    /// <summary>
    /// 喂食。成功时<b>从背包扣掉一份饲料</b>；今天已经喂过、或背包里没有这份饲料时返回 false 且不扣任何东西。
    /// </summary>
    /// <remarks>
    /// 饲料由类型决定（§6.5「灵兽需喂灵草」），查的是 <see cref="AnimalDefinition.FeedItemId"/>，
    /// 所以给灵狐喂干草不会被接受——那会是一份玩家看得见却毫无作用的消耗。
    /// <para>
    /// 「今天已经喂过」返回 false 而不是再扣一份：§6.5 只说每天要喂，没说能叠加，
    /// 白扣一份饲料是玩家看不见的损失（同「播种失败不白扣种子」）。
    /// </para>
    /// </remarks>
    public bool TryFeed(int animalId)
    {
        // 表里没有的动物不该被喂：Get 对未知 id 抛，先于任何扣除发生
        AnimalInfo animal = _ranch.Get(animalId);

        if (animal.FedToday) return false;

        // Remove 是全有或全无：饲料不够时一份都不会被扣掉（ADR-012）
        if (!_inventory.Remove(animal.Definition.FeedItemId, 1)) return false;

        _ranch.MarkFed(animalId);
        return true;
    }

    public void Rename(int animalId, string name) => _ranch.Rename(animalId, name);

    public void SetMood(int animalId, int mood) => _ranch.SetMood(animalId, mood);

    public void SetAffection(int animalId, int hearts) => _ranch.SetAffection(animalId, hearts);

    /// <summary>退订全部订阅。</summary>
    public void Dispose()
    {
        // 重复 Dispose 不该把订阅表改坏：调用方（桥接层节点）在异常路径上重复调用是常事
        if (_disposed) return;

        _daySubscription.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// 新的一天：动物长一天（喂过的按周期产出），产出进背包。
    /// </summary>
    /// <remarks>
    /// 背包满时溢出的部分会丢——<b>M1 的已知取舍</b>，与 <c>FarmingSystem.TryHarvest</c> 同款：
    /// 掉落物系统是 M2 的事（ARCHITECTURE 未定义项备案 #35）。
    /// </remarks>
    private void OnDayStarted(DayStarted e)
    {
        foreach (AnimalProduce produce in _ranch.AdvanceDay())
        {
            _inventory.Add(produce.ItemId, produce.Count);
        }
    }
}
