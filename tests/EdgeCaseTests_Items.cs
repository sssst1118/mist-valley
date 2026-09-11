using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 物品表与背包的边界（ADR-012、ADR-013）。<see cref="InventoryTests"/> 已覆盖堆叠、装不下、
/// 全有或全无与存档往返，这里只补：
/// ① <b>堆叠上限为 1</b>（不可堆叠物）这条从没被数据走到的分支；
/// ② 存档里那些「合法 JSON、但不该被接受」的形态。
/// </summary>
public sealed class EdgeCaseTests_Items
{
    /// <summary>堆叠上限压到 1：不可堆叠物（工具类）在 §12.3 与物品表注释里点名要用这个值。</summary>
    private const string SingleStackJson = """
    {
      "items": [
        { "id": "material_wood", "name": "木材", "description": "建造与制作材料。", "category": "Material", "maxStack": 1, "buyPrice": 0, "sellPrice": 0 }
      ]
    }
    """;

    /// <summary>上限 5，用来在三格之内造出「差一个」的局面。</summary>
    private const string TestJson = """
    {
      "items": [
        { "id": "crop_parsnip", "name": "防风草", "description": "春季作物。", "category": "Crop", "maxStack": 5, "buyPrice": 0, "sellPrice": 35 }
      ]
    }
    """;

    private static readonly ItemStack Empty = new(string.Empty, 0);

    [Fact]
    public void Add_堆叠上限为1的物品_每格一个_装满后返回剩余()
    {
        // maxStack=1 时 `MaxStack - Count` 的空间计算、以及「先填旧堆叠」那一圈都得直接跳过——
        // 现有用例的数据全是 999/5，这条分支一次都没被走到过。工具、法宝这类不可堆叠物全靠它。
        var inventory = new Inventory(ItemTable.FromJson(SingleStackJson), slotCount: 3);

        Assert.Equal(0, inventory.Add("material_wood", 2));

        Assert.Equal(new ItemStack("material_wood", 1), inventory.Slots[0]);
        Assert.Equal(new ItemStack("material_wood", 1), inventory.Slots[1]);
        Assert.Equal(Empty, inventory.Slots[2]);

        // 只剩一格：第三个装得下、第四个装不下，返回 1 而不是静默丢弃或把某一格堆到 2
        Assert.Equal(1, inventory.Add("material_wood", 2));
        Assert.Equal(3, inventory.Count("material_wood"));
        Assert.All(inventory.Slots, slot => Assert.True(slot.Count <= 1));

        // 已满：一个都装不下，且每格仍然只有一个
        Assert.Equal(4, inventory.Add("material_wood", 4));
        Assert.Equal(3, inventory.Count("material_wood"));
    }

    [Fact]
    public void 反序列化_单格数量超过堆叠上限_当场抛()
    {
        // 堆叠上限是槽位布局的依据（ADR-012 全篇的前提）。上限 5 的格子读进 9999，则
        // 「数量 → 占几格」的换算从此对不上，Add 也会因 MaxStack - Count <= 0 永远跳过这一格：
        // 玩家看到一格塞了 9999 个、却再也放不进一个。存档里的规则外状态会一路带到 UI 与交易结算，
        // 与「槽位数对不上」同类，属于该一并拒绝的那类。
        var inventory = new Inventory(ItemTable.FromJson(TestJson), slotCount: 2);
        const string overStacked =
            """{"Slots":[{"ItemId":"crop_parsnip","Count":9999},{"ItemId":"","Count":0}]}""";

        var error = Assert.Throws<InvalidDataException>(() => inventory.Deserialize(overStacked, fromVersion: 1));

        Assert.Contains("9999", error.Message);   // 报错要指得出是哪一格、哪个数量
    }

    [Fact]
    public void 反序列化_单格数量恰好等于堆叠上限_照读()
    {
        // 边界是「>」不是「>=」：上限 5 装满一格正是最满的**正常**存档。写成 >= 会把满堆叠的档
        // 全部拒之门外——玩家读档失败，而他的存档完全合法，且这种档在正常游玩里到处都是。
        var inventory = new Inventory(ItemTable.FromJson(TestJson), slotCount: 2);
        const string fullStack =
            """{"Slots":[{"ItemId":"crop_parsnip","Count":5},{"ItemId":"","Count":0}]}""";

        inventory.Deserialize(fullStack, fromVersion: 1);

        Assert.Equal(new ItemStack("crop_parsnip", 5), inventory.Slots[0]);
        Assert.Equal(5, inventory.Count("crop_parsnip"));
    }

    [Fact]
    public void 反序列化_跨格总数超过堆叠上限_仍照读()
    {
        // 校验的是**每一格**，不是总数：上限 5 的物品占两格共 8 个，正是 ADR-012 点名的正常形态
        // （「999 上限下 1500 个木材要占两格」）。拿 Count(itemId) 的总数去比 MaxStack 会把这最普通的
        // 一类存档判成坏档——而它恰恰是「多格堆叠」这个设计的全部意义所在。
        var inventory = new Inventory(ItemTable.FromJson(TestJson), slotCount: 3);
        const string spread =
            """{"Slots":[{"ItemId":"crop_parsnip","Count":5},{"ItemId":"crop_parsnip","Count":3},{"ItemId":"","Count":0}]}""";

        inventory.Deserialize(spread, fromVersion: 1);

        Assert.Equal(8, inventory.Count("crop_parsnip"));
    }

    [Fact]
    public void Remove_跨三格差一个_一个都不删()
    {
        // 「全有或全无」在 2 格上有用例，但真出事的是 3 格以上：边删边数的话会先把前两格扣光，
        // 到第三格才发现不够——调用方拿到 false，物品却已经少了（ADR-012 点名的那个坑）。
        var inventory = new Inventory(ItemTable.FromJson(TestJson), slotCount: 3);
        inventory.Add("crop_parsnip", 12);   // 5 + 5 + 2

        ItemStack[] before = inventory.Slots.ToArray();

        Assert.False(inventory.Remove("crop_parsnip", 13));   // 差一个

        // 逐格比，不比总数：只比总数会漏掉「前两格被扣了、第三格没动」这种半删
        Assert.Equal(before, inventory.Slots.ToArray());
        Assert.Equal(12, inventory.Count("crop_parsnip"));
    }

    [Fact]
    public void Remove_跨三格恰好取完_三格全部复位为空槽()
    {
        // 取完时每一格都要连同 id 一起复位（约定：空槽 ItemId 为空串）。
        // 只把 Count 置零、留着 id 的话，下次 Add 别的物品会看见一个「id 对不上」的格子，
        // 而 Count 为 0 的格子照样会被当成空槽占用——症状是背包凭空少格。
        var inventory = new Inventory(ItemTable.FromJson(TestJson), slotCount: 3);
        inventory.Add("crop_parsnip", 12);

        Assert.True(inventory.Remove("crop_parsnip", 12));

        Assert.Equal(0, inventory.Count("crop_parsnip"));
        Assert.Equal(new[] { Empty, Empty, Empty }, inventory.Slots.ToArray());
    }

    [Fact]
    public void Slots_是只读视图_绕不过_Add_Remove_改数据()
    {
        // 契约：Slots 是只读视图，防止调用方绕过 Add/Remove 直接改槽位（改了就绕过了堆叠上限、
        // 也绕过了「空槽 id 抹平」的约定）。只读是断言，不是注释——注释拦不住赋值语句。
        var inventory = new Inventory(ItemTable.FromJson(TestJson), slotCount: 2);
        inventory.Add("crop_parsnip", 3);

        var slots = (IList<ItemStack>)inventory.Slots;

        Assert.Throws<NotSupportedException>(() => slots[0] = Empty);
        Assert.Throws<NotSupportedException>(() => slots.Add(Empty));
        Assert.Equal(3, inventory.Count("crop_parsnip"));
    }

    [Fact]
    public void 反序列化_数量为零但带_id_的格子_规整为空槽()
    {
        // 存档里 Count=0 就是空槽，id 是脏数据（旧版本、或 Mod 手改的档）。
        // 规整掉是刻意的：留着 id 会让「空槽」有两种形态，之后每一次遍历都得记着两种都算空。
        var inventory = new Inventory(ItemTable.FromJson(TestJson), slotCount: 2);
        const string zeroWithId =
            """{"Slots":[{"ItemId":"crop_parsnip","Count":0},{"ItemId":"","Count":0}]}""";

        inventory.Deserialize(zeroWithId, fromVersion: 1);

        Assert.Equal(Empty, inventory.Slots[0]);
        Assert.Equal(0, inventory.Count("crop_parsnip"));
    }

    [Fact]
    public void 反序列化_缺_Slots_数组或内容为空_都当场抛()
    {
        // 两种「读不出槽位」的存档形态：JSON 合法但没有 Slots 键、以及整个内容就是 null。
        // 放任任一种下去，背包会被清成一格不剩地读进来——玩家看到的是「东西全没了」，
        // 而日志里一行错都没有。
        var inventory = new Inventory(ItemTable.FromJson(TestJson), slotCount: 2);

        Assert.Throws<InvalidDataException>(() => inventory.Deserialize("""{"Other":[]}""", fromVersion: 1));
        Assert.Throws<InvalidDataException>(() => inventory.Deserialize("null", fromVersion: 1));
    }

    [Fact]
    public void 物品表_条目不是对象时报错_而不是跳过()
    {
        // items 数组里混进一个字符串/数字（手写 JSON 漏了个大括号是很常见的）。
        // 跳过它等于「表少了一件物品」，而按 id 取值的地方会在很久以后才炸 KeyNotFoundException。
        var error = Assert.Throws<InvalidDataException>(() => ItemTable.FromJson("""
        { "items": [ "material_wood" ] }
        """));

        Assert.Contains("id", error.Message);
    }

    [Fact]
    public void 物品表_All_是只读视图()
    {
        // All 是进程内共享的一份，UI 列表直接照用（契约写明只读）。
        // 谁拿到它就能往表里塞条目的话，物品表就变成了可变的全局状态。
        ItemTable table = ItemTable.FromJson(TestJson);

        var all = (IList<ItemDefinition>)table.All;

        Assert.Throws<NotSupportedException>(() => all.Clear());
    }
}
