using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;

namespace XingGame.Systems.Items;

/// <summary>
/// 背包。内部是<b>定长槽位数组</b>，不是字典（ADR-012）：堆叠上限决定了同一个物品可以占多个槽
/// （999 上限下 1500 个木材要占两格），字典表达不了这个。
/// </summary>
public sealed class Inventory : IInventory, ISaveable
{
    /// <summary>
    /// 默认槽位数。**设计文档未定义背包容量，待裁决**——§17.2 只给了背包菜单的分类，
    /// §12.1 只给了各商店卖什么，都没有槽位数量。取 24 是同量级农场游戏的常见值。
    /// 契约里 slotCount 是构造参数，桥接层直接用这个常量即可。
    /// </summary>
    public const int DefaultSlotCount = 24;

    private static readonly ItemStack EmptySlot = new(string.Empty, 0);

    private readonly IItemTable _items;
    private readonly ItemStack[] _slots;
    private readonly ReadOnlyCollection<ItemStack> _slotView;

    public Inventory(IItemTable items, int slotCount)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));

        // 0 格背包能构造却什么都装不下，Add 恒等于「全没装下」——这种静默失效是配置写错时的
        // 典型症状，构造时就抛比事后查好定位（同 PlayerConfig 校验速度的先例）
        if (slotCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "槽位数必须为正");

        _slots = new ItemStack[slotCount];
        for (int slot = 0; slot < slotCount; slot++) _slots[slot] = EmptySlot;

        _slotView = Array.AsReadOnly(_slots);
    }

    public int SlotCount => _slots.Length;

    /// <summary>只读视图，防止调用方绕过 <see cref="Add"/>/<see cref="Remove"/> 直接改槽位。</summary>
    public IReadOnlyList<ItemStack> Slots => _slotView;

    /// <summary>
    /// 先填已有的未满堆叠，再占空槽。返回没装下的数量（全装下为 0）。
    /// 装不下时**已装进去的保留**——不做回滚；全有或全无是 <see cref="Remove"/> 的规矩。
    /// </summary>
    public int Add(string itemId, int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "数量必须为正");

        // 物品 id 不在表里是编程错误，不是运行时状况：背包只装表里有的东西，凭空造物品必然是写错了 id
        ItemDefinition definition = _items.Get(itemId);

        int remaining = count;

        // 先填旧堆叠：反过来的话，背包很快会被一堆半满的堆叠铺满
        for (int slot = 0; slot < _slots.Length && remaining > 0; slot++)
        {
            if (!string.Equals(_slots[slot].ItemId, itemId, StringComparison.Ordinal)) continue;

            int space = definition.MaxStack - _slots[slot].Count;
            if (space <= 0) continue;

            int moved = Math.Min(space, remaining);
            _slots[slot] = new ItemStack(itemId, _slots[slot].Count + moved);
            remaining -= moved;
        }

        for (int slot = 0; slot < _slots.Length && remaining > 0; slot++)
        {
            if (_slots[slot].Count != 0) continue;

            int moved = Math.Min(definition.MaxStack, remaining);
            _slots[slot] = new ItemStack(itemId, moved);
            remaining -= moved;
        }

        return remaining;
    }

    /// <summary>跨多个堆叠统计总数。</summary>
    public int Count(string itemId)
    {
        int total = 0;
        foreach (ItemStack slot in _slots)
        {
            if (string.Equals(slot.ItemId, itemId, StringComparison.Ordinal)) total += slot.Count;
        }

        return total;
    }

    /// <summary>
    /// 全有或全无：数量不足时一个都不删并返回 false（ADR-012）。先数够不够再动手——
    /// 边删边数会让「删到一半才发现不够」变成既成事实：调用方以为失败了，物品却已经少了。
    /// </summary>
    public bool Remove(string itemId, int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "数量必须为正");

        if (Count(itemId) < count) return false;

        int remaining = count;
        for (int slot = 0; slot < _slots.Length && remaining > 0; slot++)
        {
            if (!string.Equals(_slots[slot].ItemId, itemId, StringComparison.Ordinal)) continue;

            int taken = Math.Min(_slots[slot].Count, remaining);
            int left = _slots[slot].Count - taken;

            // 槽位清空时 id 要一并抹掉：空槽的约定是 ItemId 为空串、Count 为 0
            _slots[slot] = left == 0 ? EmptySlot : new ItemStack(itemId, left);
            remaining -= taken;
        }

        return true;
    }

    public string SaveKey => "inventory";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    public int Version => 1;

    /// <summary>
    /// 存整个槽位数组，<b>空槽也存</b>——只存非空槽会让读回来时槽位整体前移。
    /// 物品存 id 字符串而非它在物品表里的序号：往表里插一个新物品就会让旧存档的序号全部错位，
    /// 这与 ADR-012「枚举序列化成名字」是同一个理由。
    /// </summary>
    public string Serialize() => JsonSerializer.Serialize(new SavedInventory(_slots), _saveJsonOptions);

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009 同款理由）
        if (fromVersion > Version)
            throw new NotSupportedException($"背包存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedInventory saved = JsonSerializer.Deserialize<SavedInventory>(json, _saveJsonOptions)
            ?? throw new InvalidDataException("背包存档内容为空");

        ItemStack[] slots = saved.Slots
            ?? throw new InvalidDataException("背包存档缺少槽位数组");

        // 槽位数对不上不能猜着读：多了要丢物品，少了让后面的槽位整体前移，两种都是静默的数据损坏
        if (slots.Length != _slots.Length)
            throw new InvalidDataException($"背包存档有 {slots.Length} 个槽位，当前背包是 {_slots.Length} 个");

        // 先整份校验再落盘：坏存档不该让背包停在「读了一半」的状态
        var restored = new ItemStack[slots.Length];
        for (int slot = 0; slot < slots.Length; slot++) restored[slot] = ValidateSlot(slots[slot], slot);

        Array.Copy(restored, _slots, _slots.Length);
    }

    /// <summary>存档是外部输入，坏值当场抛——越界值渗进背包后，症状会出现在离病因很远的地方。</summary>
    private ItemStack ValidateSlot(ItemStack slot, int index)
    {
        if (slot.Count < 0)
            throw new InvalidDataException($"背包存档第 {index} 格的数量为 {slot.Count}");

        // 数量为 0 就是空槽，顺手把 id 抹平回约定形态（约定：空槽的 ItemId 为空串）
        if (slot.Count == 0) return EmptySlot;

        if (string.IsNullOrEmpty(slot.ItemId))
            throw new InvalidDataException($"背包存档第 {index} 格有数量 {slot.Count} 却没有物品 id");

        if (!_items.TryGet(slot.ItemId, out ItemDefinition definition))
            throw new InvalidDataException($"背包存档第 {index} 格的物品 id「{slot.ItemId}」不在物品表里");

        // 上限决定槽位布局（ADR-012）：放行超上限的格，则「数量 → 占几格」从此对不上，
        // Add 也会因 MaxStack - Count <= 0 永远跳过它——静默的规则外状态，与槽位数对不上同类
        if (slot.Count > definition.MaxStack)
            throw new InvalidDataException(
                $"背包存档第 {index} 格的数量 {slot.Count} 超过物品「{slot.ItemId}」的堆叠上限 {definition.MaxStack}");

        return slot;
    }

    /// <summary>存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。</summary>
    private sealed record SavedInventory(ItemStack[] Slots);

    /// <summary>字段名与物品 id 都写出来，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions _saveJsonOptions = new() { WriteIndented = true };
}
