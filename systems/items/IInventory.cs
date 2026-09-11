using System.Collections.Generic;

namespace XingGame.Systems.Items;

/// <summary>
/// 背包。存在的意义是让「按接口取用」这条规矩不至于在背包这里破个口子——
/// 否则第一个看到 <c>Get&lt;Inventory&gt;()</c> 的人会以为按具体类型取服务是可以的，
/// 然后这条口子会越撕越大。
/// </summary>
/// <remarks>
/// 变更通知：本接口<b>不</b>提供「背包变了」的事件。UI 目前每帧比对槽位快照来刷新
/// （见 <c>ui/InventoryHud</c> 的说明）。真需要事件时再补，别提前加。
/// </remarks>
public interface IInventory
{
    /// <summary>槽位总数，定长（ADR-012：背包是定长槽位数组，不是字典）。</summary>
    int SlotCount { get; }

    /// <summary>全部槽位，含空槽——空槽的位置对调用方有意义，不要过滤。</summary>
    IReadOnlyList<ItemStack> Slots { get; }

    /// <summary>先填已有的未满堆叠，再占空槽。返回<b>没装下</b>的数量（全装下则为 0）。</summary>
    int Add(string itemId, int count);

    /// <summary>跨多个堆叠统计总数。</summary>
    int Count(string itemId);

    /// <summary>全有或全无：数量不足时一个都不删并返回 false。</summary>
    bool Remove(string itemId, int count);
}
