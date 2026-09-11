using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 背包（M1-4）。堆叠上限在测试数据里压到 5——物品本身仍取文档里有的，只是把上限改小，
/// 这样「跨多个槽位」不用塞 999 个才能触发。
/// </summary>
public sealed class InventoryTests
{
    private const string TestJson = """
    {
      "items": [
        { "id": "crop_parsnip",  "name": "防风草", "description": "春季作物。",     "category": "Crop",     "maxStack": 5, "buyPrice": 0, "sellPrice": 35 },
        { "id": "material_wood", "name": "木材",   "description": "建造与制作材料。", "category": "Material", "maxStack": 5, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_coal", "name": "煤",     "description": "制作材料。",       "category": "Material", "maxStack": 5, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    /// <summary>同一份物品表，只是条目顺序反了——用来证明存档认的是 id，不是物品表里的序号。</summary>
    private const string ReorderedTestJson = """
    {
      "items": [
        { "id": "material_coal", "name": "煤",     "description": "制作材料。",       "category": "Material", "maxStack": 5, "buyPrice": 0, "sellPrice": 0 },
        { "id": "material_wood", "name": "木材",   "description": "建造与制作材料。", "category": "Material", "maxStack": 5, "buyPrice": 0, "sellPrice": 0 },
        { "id": "crop_parsnip",  "name": "防风草", "description": "春季作物。",       "category": "Crop",     "maxStack": 5, "buyPrice": 0, "sellPrice": 35 }
      ]
    }
    """;

    private static readonly ItemTable Table = ItemTable.FromJson(TestJson);
    private static readonly ItemStack Empty = new(string.Empty, 0);

    private static Inventory NewInventory(int slotCount = 3) => new(Table, slotCount);

    [Fact]
    public void 默认槽位数_为_24()
    {
        // 设计文档未定义背包容量，24 是本切片的选择（待裁决）
        Assert.Equal(24, Inventory.DefaultSlotCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 构造_槽位数非正时抛(int slotCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory(Table, slotCount));
    }

    [Fact]
    public void 加到空背包_占第一格()
    {
        Inventory inventory = NewInventory();

        Assert.Equal(0, inventory.Add("crop_parsnip", 3));

        Assert.Equal(new ItemStack("crop_parsnip", 3), inventory.Slots[0]);
        Assert.Equal(Empty, inventory.Slots[1]);
        Assert.Equal(3, inventory.Count("crop_parsnip"));
    }

    [Fact]
    public void 加到已有堆叠_先填旧堆叠再占新槽()
    {
        Inventory inventory = NewInventory();
        inventory.Add("crop_parsnip", 3);

        Assert.Equal(0, inventory.Add("crop_parsnip", 4));   // 3 + 2 填满第一格，剩下的开新格

        Assert.Equal(new ItemStack("crop_parsnip", 5), inventory.Slots[0]);
        Assert.Equal(new ItemStack("crop_parsnip", 2), inventory.Slots[1]);
        Assert.Equal(Empty, inventory.Slots[2]);
    }

    [Fact]
    public void 数量超过堆叠上限_跨多个槽位()
    {
        Inventory inventory = NewInventory();

        Assert.Equal(0, inventory.Add("crop_parsnip", 12));   // 5 + 5 + 2

        Assert.Equal(new ItemStack("crop_parsnip", 5), inventory.Slots[0]);
        Assert.Equal(new ItemStack("crop_parsnip", 5), inventory.Slots[1]);
        Assert.Equal(new ItemStack("crop_parsnip", 2), inventory.Slots[2]);
    }

    [Fact]
    public void 装不下时_返回剩余数量_已装的保留()
    {
        Inventory inventory = NewInventory(slotCount: 2);   // 满打满算装得下 10 个

        Assert.Equal(2, inventory.Add("crop_parsnip", 12));

        // 不是全有或全无：装进去的那 10 个留下（全有或全无是 Remove 的规矩）
        Assert.Equal(10, inventory.Count("crop_parsnip"));
    }

    [Fact]
    public void 背包满时_一个都装不下()
    {
        Inventory inventory = NewInventory(slotCount: 2);
        inventory.Add("material_wood", 10);

        Assert.Equal(3, inventory.Add("material_coal", 3));

        Assert.Equal(0, inventory.Count("material_coal"));
        Assert.Equal(10, inventory.Count("material_wood"));   // 原有的东西一个没动
    }

    [Fact]
    public void Count_跨多个堆叠统计()
    {
        Inventory inventory = NewInventory(slotCount: 4);
        inventory.Add("crop_parsnip", 12);   // 5 + 5 + 2，占三格
        inventory.Add("material_wood", 4);   // 第四格，与防风草互不干扰

        Assert.Equal(12, inventory.Count("crop_parsnip"));
        Assert.Equal(4, inventory.Count("material_wood"));
        Assert.Equal(0, inventory.Count("material_coal"));
    }

    [Fact]
    public void Remove_跨多个堆叠扣减()
    {
        Inventory inventory = NewInventory();
        inventory.Add("crop_parsnip", 12);   // 5 + 5 + 2

        Assert.True(inventory.Remove("crop_parsnip", 7));

        Assert.Equal(5, inventory.Count("crop_parsnip"));
        Assert.Equal(Empty, inventory.Slots[0]);                        // 清空的槽位连同 id 一起复位
        Assert.Equal(new ItemStack("crop_parsnip", 3), inventory.Slots[1]);
        Assert.Equal(new ItemStack("crop_parsnip", 2), inventory.Slots[2]);
    }

    [Fact]
    public void Remove_数量不足时_一个都不删_且逐槽未变()
    {
        Inventory inventory = NewInventory();
        inventory.Add("crop_parsnip", 8);   // 5 + 3

        ItemStack[] before = inventory.Slots.ToArray();

        Assert.False(inventory.Remove("crop_parsnip", 9));

        // 只比总数会漏掉「前面几格被扣了、后面没动」这种半删——逐槽比才守得住（ADR-012）
        Assert.Equal(before, inventory.Slots.ToArray());
        Assert.Equal(8, inventory.Count("crop_parsnip"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Add_数量非正时抛(int count)
    {
        Inventory inventory = NewInventory();

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Add("crop_parsnip", count));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Remove_数量非正时抛(int count)
    {
        Inventory inventory = NewInventory();
        inventory.Add("crop_parsnip", 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Remove("crop_parsnip", count));

        Assert.Equal(3, inventory.Count("crop_parsnip"));   // 抛之前不许动数据
    }

    [Fact]
    public void Add_未知物品_id_抛_KeyNotFoundException()
    {
        Inventory inventory = NewInventory();

        var error = Assert.Throws<KeyNotFoundException>(() => inventory.Add("crop_unknown", 1));

        Assert.Contains("crop_unknown", error.Message);
        Assert.All(inventory.Slots, slot => Assert.Equal(Empty, slot));   // 抛出前不许占槽
    }

    [Fact]
    public void 存档往返_槽位逐个相等_含空槽的位置()
    {
        Inventory original = NewInventory(slotCount: 4);
        original.Add("crop_parsnip", 12);   // 占前三格
        original.Remove("crop_parsnip", 5); // 首格清空 → 空槽夹在前头，错位与否一眼看得出

        // 读之前的背包塞点东西：读档是整状态覆盖，不是「往现有背包里加」
        Inventory restored = NewInventory(slotCount: 4);
        restored.Add("material_coal", 5);

        restored.Deserialize(original.Serialize(), fromVersion: 1);

        Assert.Equal(original.Slots.ToArray(), restored.Slots.ToArray());
        for (int slot = 0; slot < original.SlotCount; slot++)
            Assert.Equal(original.Slots[slot], restored.Slots[slot]);
    }

    [Fact]
    public void 存档_空槽也原样存下_字段是具名的()
    {
        Inventory inventory = NewInventory(slotCount: 4);
        inventory.Add("crop_parsnip", 12);
        inventory.Remove("crop_parsnip", 5);   // 首格变成空槽

        using var document = JsonDocument.Parse(inventory.Serialize());
        JsonElement slots = document.RootElement.GetProperty("Slots");

        // 只存非空槽的话读回来槽位会整体前移，所以空槽也占一个条目
        Assert.Equal(4, slots.GetArrayLength());
        Assert.Equal(string.Empty, slots[0].GetProperty("ItemId").GetString());
        Assert.Equal(0, slots[0].GetProperty("Count").GetInt32());
    }

    [Fact]
    public void 存档_物品按_id_存_而不是按物品表里的序号()
    {
        Inventory original = NewInventory(slotCount: 2);
        original.Add("material_wood", 3);

        string json = original.Serialize();
        Assert.Contains("\"material_wood\"", json);

        // 物品表换了顺序也读得回来：存序号的话这里会整体错位（同 ADR-012 枚举序列化成名字的理由）
        Inventory restored = new(ItemTable.FromJson(ReorderedTestJson), slotCount: 2);
        restored.Deserialize(json, fromVersion: 1);

        Assert.Equal(original.Slots.ToArray(), restored.Slots.ToArray());
    }

    [Fact]
    public void 反序列化_拒绝来自更新版本的存档()
    {
        Inventory inventory = NewInventory();

        Assert.Throws<NotSupportedException>(() => inventory.Deserialize(inventory.Serialize(), fromVersion: 2));
    }

    [Fact]
    public void 反序列化_槽位数与当前背包不一致时报错()
    {
        Inventory inventory = NewInventory(slotCount: 2);
        const string threeSlots =
            """{"Slots":[{"ItemId":"","Count":0},{"ItemId":"","Count":0},{"ItemId":"","Count":0}]}""";

        Assert.Throws<InvalidDataException>(() => inventory.Deserialize(threeSlots, fromVersion: 1));
    }

    [Theory]
    [InlineData("""{"ItemId":"crop_parsnip","Count":-1}""")]     // 数量为负
    [InlineData("""{"ItemId":"","Count":3}""")]                  // 有数量却没有 id
    [InlineData("""{"ItemId":"crop_unknown","Count":3}""")]      // id 不在物品表里
    public void 反序列化_槽位数据非法时报错(string brokenSlot)
    {
        Inventory inventory = NewInventory(slotCount: 2);
        string json = "{ \"Slots\": [" + brokenSlot + ", { \"ItemId\": \"\", \"Count\": 0 }] }";

        Assert.Throws<InvalidDataException>(() => inventory.Deserialize(json, fromVersion: 1));
    }
}
