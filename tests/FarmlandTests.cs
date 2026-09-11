using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using XingGame.Systems.Farming;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 耕地（M1-5）。这里守的是「一天怎么过」：浇了水才长、下雨算浇过水、不浇水只是停滞、
/// 收获之后格子回到什么状态、换季清作物但不清开垦。
/// </summary>
/// <remarks>
/// 作物表用真文档里的两种代表：防风草（4 天、一次性）与草莓（8 天、可多次收获）——
/// 数值取自 §6.2，不另编一套，免得测试与数据各说各话。
/// </remarks>
public class FarmlandTests
{
    private const string ItemsJson = """
    {
      "items": [
        { "id": "seed_parsnip",    "name": "防风草种子", "description": "春季播种。", "category": "Seed", "maxStack": 999, "buyPrice": 20, "sellPrice": 0 },
        { "id": "crop_parsnip",    "name": "防风草",     "description": "春季作物。", "category": "Crop", "maxStack": 999, "buyPrice": 0,  "sellPrice": 35 },
        { "id": "seed_strawberry", "name": "草莓种子",   "description": "春季播种。", "category": "Seed", "maxStack": 999, "buyPrice": 100, "sellPrice": 0 },
        { "id": "crop_strawberry", "name": "草莓",       "description": "春季作物。", "category": "Crop", "maxStack": 999, "buyPrice": 0,  "sellPrice": 120 },
        { "id": "seed_potato",     "name": "土豆种子",   "description": "春季播种。", "category": "Seed", "maxStack": 999, "buyPrice": 50, "sellPrice": 0 }
      ]
    }
    """;

    private const string CropsJson = """
    {
      "crops": [
        { "seedId": "seed_parsnip",    "cropId": "crop_parsnip",    "growthDays": 4, "regrowable": false },
        { "seedId": "seed_strawberry", "cropId": "crop_strawberry", "growthDays": 8, "regrowable": true }
      ]
    }
    """;

    private const string Parsnip = "seed_parsnip";
    private const string Strawberry = "seed_strawberry";

    /// <summary>物品表里有、作物表里没有的种子——用来守「凭空播一粒表里没有的种子」。</summary>
    private const string NotACrop = "seed_potato";

    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);
    private static readonly CropTable Crops = CropTable.FromJson(CropsJson, Items);

    private static readonly TileCoord Tile = new(0, 0);
    private static readonly TileCoord Other = new(3, 2);

    private static Farmland NewFarmland() => new(Crops);

    /// <summary>「每天浇一次水、过一天」重复若干天——正常的种植流程。</summary>
    private static void WaterAndAdvance(Farmland farmland, TileCoord tile, int days)
    {
        for (int day = 0; day < days; day++)
        {
            Assert.True(farmland.TryWater(tile));
            farmland.AdvanceDay(rained: false);
        }
    }

    // ——— 起始状态 ———

    [Fact]
    public void 没被碰过的格_是未开垦且没有作物()
    {
        Farmland farmland = NewFarmland();

        Assert.Equal(SoilState.Untilled, farmland.StateOf(Tile));
        Assert.False(farmland.HasCrop(Tile));
        Assert.Equal(0, farmland.DaysGrown(Tile));
        Assert.False(farmland.IsReadyToHarvest(Tile));
    }

    // ——— 开垦 / 浇水 / 播种 ———

    [Fact]
    public void 开垦_未开垦变已开垦_再锄一次返回_false()
    {
        Farmland farmland = NewFarmland();

        Assert.True(farmland.TryTill(Tile));
        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));

        Assert.False(farmland.TryTill(Tile));
        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));
    }

    [Fact]
    public void 浇水_未开垦的格浇不了()
    {
        Farmland farmland = NewFarmland();

        Assert.False(farmland.TryWater(Tile));
        Assert.Equal(SoilState.Untilled, farmland.StateOf(Tile));
    }

    [Fact]
    public void 浇水_已开垦变已浇水_再浇一次返回_false()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);

        Assert.True(farmland.TryWater(Tile));
        Assert.Equal(SoilState.Watered, farmland.StateOf(Tile));

        Assert.False(farmland.TryWater(Tile));
        Assert.Equal(SoilState.Watered, farmland.StateOf(Tile));
    }

    [Fact]
    public void 能锄吗_与锄一下的返回值永远一致()
    {
        // CanTill / CanWater 是给「花灵力之前先问一句」用的（生活法术要在扣灵力之前知道这一片
        // 有几格真的动得了），所以它们必须与动作同一条判据——两份判据漂了，症状就是
        // 「灵力扣了、地没动」。这条用例逐种状态各验一遍，漂了立刻红
        Farmland farmland = NewFarmland();

        Assert.Equal(farmland.CanTill(Tile), farmland.TryTill(Tile));        // 未开垦 → 两边都为真
        Assert.Equal(farmland.CanTill(Tile), farmland.TryTill(Tile));        // 已开垦 → 两边都为假
        Assert.Equal(farmland.CanTill(Other), farmland.TryTill(Other));      // 另一格互不影响
        Assert.Equal(farmland.CanWater(Tile), farmland.TryWater(Tile));      // 已开垦 → 两边都为真
        Assert.Equal(farmland.CanWater(Tile), farmland.TryWater(Tile));      // 已浇水 → 两边都为假
        Assert.Equal(farmland.CanWater(Other), farmland.TryWater(Other));    // 已开垦但没浇 → 两边都为真
        Assert.Equal(farmland.CanTill(Other), farmland.TryTill(Other));      // 已浇水 → 两边都为假

        // 有作物的格也一样：CanX 与 TryX 不许各说各话
        Assert.Equal(farmland.CanWater(Tile), farmland.TryWater(Tile));
        Assert.Equal(farmland.CanTill(Tile), farmland.TryTill(Tile));
    }

    [Fact]
    public void 能不能_是只读的_问一百遍也不改状态()
    {
        // 法术会先问遍一片地再决定扣不扣灵力，所以这两个查询必须一个字节都不改
        Farmland farmland = NewFarmland();

        for (int i = 0; i < 100; i++)
        {
            Assert.True(farmland.CanTill(Tile));
            Assert.False(farmland.CanWater(Tile));
        }

        Assert.Equal(SoilState.Untilled, farmland.StateOf(Tile));
        Assert.Equal(0, farmland.DaysGrown(Tile));
        Assert.False(farmland.HasCrop(Tile));
    }

    [Fact]
    public void 播种_未开垦的格种不了()
    {
        Farmland farmland = NewFarmland();

        Assert.False(farmland.TryPlant(Tile, Parsnip));
        Assert.False(farmland.HasCrop(Tile));
        Assert.Equal(SoilState.Untilled, farmland.StateOf(Tile));
    }

    [Fact]
    public void 播种_已开垦的格成功_且不改土壤状态()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);

        Assert.True(farmland.TryPlant(Tile, Parsnip));

        Assert.True(farmland.HasCrop(Tile));
        Assert.Equal(0, farmland.DaysGrown(Tile));
        Assert.False(farmland.IsReadyToHarvest(Tile));
        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));
    }

    [Fact]
    public void 播种_浇过水的格上下种_当天算浇过水()
    {
        // 先浇水再播种不该白浇一天
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryWater(Tile);

        Assert.True(farmland.TryPlant(Tile, Parsnip));
        Assert.Equal(SoilState.Watered, farmland.StateOf(Tile));

        farmland.AdvanceDay(rained: false);
        Assert.Equal(1, farmland.DaysGrown(Tile));
    }

    [Fact]
    public void 播种_格上已经有作物时失败()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Parsnip);
        WaterAndAdvance(farmland, Tile, 2);

        Assert.False(farmland.TryPlant(Tile, Strawberry));   // 已经长了两天的防风草不该被顶掉
        Assert.Equal(2, farmland.DaysGrown(Tile));
    }

    [Fact]
    public void 播种_未知种子抛_KeyNotFoundException_且消息含_id()
    {
        // 物品表里有、但作物表里没有：凭空播一粒不是作物的东西，必然是传错了 id
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);

        var error = Assert.Throws<KeyNotFoundException>(() => farmland.TryPlant(Tile, NotACrop));

        Assert.Contains(NotACrop, error.Message);
        Assert.False(farmland.HasCrop(Tile));
    }

    [Fact]
    public void 播种_未知种子时即使格是荒地也先抛()
    {
        // 传错 id 是编程错误，不该被「这块地不能种」掩盖过去——否则 id 写错要拖到很久以后才发现
        Assert.Throws<KeyNotFoundException>(() => NewFarmland().TryPlant(Tile, "seed_ghost"));
    }

    // ——— 推进一天 ———

    [Fact]
    public void 推进一天_浇过水的作物长一天_且浇水状态清空()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Parsnip);
        farmland.TryWater(Tile);

        farmland.AdvanceDay(rained: false);

        Assert.Equal(1, farmland.DaysGrown(Tile));
        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));   // 浇的水只对今天有效
    }

    [Fact]
    public void 推进一天_没浇水的作物不生长_也不枯萎()
    {
        // §3.2 只说换季枯萎：忘浇一天水就死一片是玩家最恨的那种惩罚
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Parsnip);

        farmland.AdvanceDay(rained: false);
        farmland.AdvanceDay(rained: false);

        Assert.Equal(0, farmland.DaysGrown(Tile));
        Assert.True(farmland.HasCrop(Tile));
    }

    [Fact]
    public void 推进一天_下雨算作浇过水()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Parsnip);

        farmland.AdvanceDay(rained: true);

        Assert.Equal(1, farmland.DaysGrown(Tile));
        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));
    }

    [Fact]
    public void 推进一天_下过雨也不留着浇水状态()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryWater(Tile);

        farmland.AdvanceDay(rained: true);

        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));
    }

    [Fact]
    public void 推进一天_没种东西的格_浇水状态也照清()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryWater(Tile);

        farmland.AdvanceDay(rained: false);

        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));
    }

    [Fact]
    public void 成熟之后_生长天数不再往上加()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Parsnip);

        WaterAndAdvance(farmland, Tile, 6);   // 成熟只要 4 天

        Assert.Equal(4, farmland.DaysGrown(Tile));
        Assert.True(farmland.IsReadyToHarvest(Tile));
    }

    // ——— 收获 ———

    [Fact]
    public void 收获_没作物时报_false_且输出为空()
    {
        Farmland farmland = NewFarmland();

        Assert.False(farmland.TryHarvest(Tile, out string cropId, out int count));
        Assert.Equal(string.Empty, cropId);
        Assert.Equal(0, count);
    }

    [Fact]
    public void 收获_没成熟时报_false_且作物还在()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Parsnip);
        WaterAndAdvance(farmland, Tile, 3);   // 4 天才熟

        Assert.False(farmland.TryHarvest(Tile, out _, out _));
        Assert.True(farmland.HasCrop(Tile));
        Assert.Equal(3, farmland.DaysGrown(Tile));
    }

    [Fact]
    public void 收获_一次性作物_收完格子变空但保持开垦()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Parsnip);
        WaterAndAdvance(farmland, Tile, 4);

        Assert.True(farmland.IsReadyToHarvest(Tile));
        Assert.True(farmland.TryHarvest(Tile, out string cropId, out int count));

        Assert.Equal("crop_parsnip", cropId);
        Assert.Equal(Farmland.HarvestYield, count);
        Assert.False(farmland.HasCrop(Tile));
        Assert.Equal(0, farmland.DaysGrown(Tile));
        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));
        Assert.False(farmland.IsReadyToHarvest(Tile));
    }

    [Fact]
    public void 收获_可多次收获的作物_收完重新开始计时()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Strawberry);
        WaterAndAdvance(farmland, Tile, 8);

        Assert.True(farmland.TryHarvest(Tile, out string cropId, out _));
        Assert.Equal("crop_strawberry", cropId);

        // 留着株、重新计时：文档没给再生天数，用同一个 GrowthDays
        Assert.True(farmland.HasCrop(Tile));
        Assert.Equal(0, farmland.DaysGrown(Tile));
        Assert.False(farmland.IsReadyToHarvest(Tile));

        WaterAndAdvance(farmland, Tile, 8);
        Assert.True(farmland.TryHarvest(Tile, out _, out _));
        Assert.True(farmland.HasCrop(Tile));
    }

    [Fact]
    public void 收获_浇过水的格收完回到已开垦()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Parsnip);
        WaterAndAdvance(farmland, Tile, 4);
        farmland.TryWater(Tile);   // 熟了之后再浇一次水

        Assert.Equal(SoilState.Watered, farmland.StateOf(Tile));
        Assert.True(farmland.TryHarvest(Tile, out _, out _));

        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));
    }

    // ——— 换季枯萎 ———

    [Fact]
    public void 枯萎_清掉全部作物但保留开垦()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Parsnip);
        WaterAndAdvance(farmland, Tile, 2);

        farmland.TryTill(Other);
        farmland.TryPlant(Other, Strawberry);

        farmland.WitherAll();

        Assert.False(farmland.HasCrop(Tile));
        Assert.False(farmland.HasCrop(Other));
        Assert.Equal(0, farmland.DaysGrown(Tile));
        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));
        Assert.Equal(SoilState.Tilled, farmland.StateOf(Other));
    }

    [Fact]
    public void 枯萎_没被碰过的格还是未开垦()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.WitherAll();

        Assert.Equal(SoilState.Untilled, farmland.StateOf(Other));
    }

    [Fact]
    public void 枯萎_浇过水的格回到已开垦_且能重新播种()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryWater(Tile);
        farmland.TryPlant(Tile, Parsnip);
        farmland.TryWater(Other);   // 没开垦，浇不上

        farmland.WitherAll();

        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));
        Assert.True(farmland.TryPlant(Tile, Strawberry));   // 换季后还能接着种
        Assert.True(farmland.HasCrop(Tile));
    }

    // ——— 存档 ———

    [Fact]
    public void 存档_空耕地里一个格子都没有()
    {
        using var document = JsonDocument.Parse(NewFarmland().Serialize());

        Assert.Empty(document.RootElement.GetProperty("Plots").EnumerateArray());
    }

    [Fact]
    public void 存档往返_开垦浇水与生长天数都能还原()
    {
        Farmland original = NewFarmland();
        original.TryTill(Tile);
        original.TryPlant(Tile, Parsnip);
        WaterAndAdvance(original, Tile, 2);

        original.TryTill(Other);                    // 只开垦、没种东西的格

        var watered = new TileCoord(1, 1);
        original.TryTill(watered);
        original.TryWater(watered);                 // 只浇了水、没种东西的格

        Farmland restored = NewFarmland();
        restored.Deserialize(original.Serialize(), fromVersion: 1);

        Assert.Equal(2, restored.DaysGrown(Tile));
        Assert.True(restored.HasCrop(Tile));
        Assert.Equal(SoilState.Tilled, restored.StateOf(Tile));

        // 已开垦但空着的格要存：不存的话读档后锄过的地就变回荒野了
        Assert.Equal(SoilState.Tilled, restored.StateOf(Other));
        Assert.False(restored.HasCrop(Other));

        Assert.Equal(SoilState.Watered, restored.StateOf(watered));
        Assert.False(restored.HasCrop(watered));

        // 作物身份也要还原：再长两天熟了，收的必须是防风草
        WaterAndAdvance(restored, Tile, 2);
        Assert.True(restored.TryHarvest(Tile, out string cropId, out _));
        Assert.Equal("crop_parsnip", cropId);
    }

    [Fact]
    public void 存档往返_可多次收获的标记也要还原()
    {
        Farmland original = NewFarmland();
        original.TryTill(Tile);
        original.TryPlant(Tile, Strawberry);
        WaterAndAdvance(original, Tile, 3);

        Farmland restored = NewFarmland();
        restored.Deserialize(original.Serialize(), fromVersion: 1);

        Assert.Equal(3, restored.DaysGrown(Tile));

        WaterAndAdvance(restored, Tile, 5);
        Assert.True(restored.TryHarvest(Tile, out _, out _));

        Assert.True(restored.HasCrop(Tile));   // 读回来的要是可多次收获的草莓，不是一次性的
        Assert.Equal(0, restored.DaysGrown(Tile));
    }

    [Fact]
    public void 读档_存档里没有的格全部回到未开垦()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);
        farmland.TryPlant(Tile, Parsnip);
        farmland.TryTill(Other);

        farmland.Deserialize(SaveWith(Plot(Other.X, Other.Y)), fromVersion: 1);

        Assert.Equal(SoilState.Untilled, farmland.StateOf(Tile));
        Assert.False(farmland.HasCrop(Tile));
        Assert.Equal(0, farmland.DaysGrown(Tile));
    }

    [Fact]
    public void 读档_版本高于当前时抛_NotSupportedException()
    {
        Farmland farmland = NewFarmland();

        Assert.Throws<NotSupportedException>(() => farmland.Deserialize(SaveWith(Plot(0, 0)), fromVersion: 2));
    }

    [Fact]
    public void 读档_缺少格子数组时抛()
    {
        Assert.Throws<InvalidDataException>(() => NewFarmland().Deserialize("""{ "格子": [] }""", fromVersion: 1));
    }

    [Fact]
    public void 读档_种子_id_不在作物表里时抛且消息含_id()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => NewFarmland().Deserialize(SaveWith(Plot(0, 0, seedId: "seed_ghost", daysGrown: 1)), fromVersion: 1));

        Assert.Contains("seed_ghost", error.Message);
    }

    [Theory]
    [InlineData(5)]    // 防风草 4 天
    [InlineData(99)]
    public void 读档_生长天数超过成熟天数时抛(int daysGrown)
    {
        Assert.Throws<InvalidDataException>(
            () => NewFarmland().Deserialize(SaveWith(Plot(0, 0, seedId: Parsnip, daysGrown: daysGrown)), fromVersion: 1));
    }

    [Fact]
    public void 读档_生长天数为负时抛()
    {
        Assert.Throws<InvalidDataException>(
            () => NewFarmland().Deserialize(SaveWith(Plot(0, 0, daysGrown: -1)), fromVersion: 1));
    }

    [Fact]
    public void 读档_有生长天数却没有种子_id_时抛()
    {
        Assert.Throws<InvalidDataException>(
            () => NewFarmland().Deserialize(SaveWith(Plot(0, 0, daysGrown: 2)), fromVersion: 1));
    }

    [Theory]
    [InlineData("Waterd")]
    [InlineData("1")]        // 按数字序号写状态会随枚举插值错位，不能静默接受
    [InlineData("")]
    public void 读档_土壤状态名非法时抛(string state)
    {
        Assert.Throws<InvalidDataException>(
            () => NewFarmland().Deserialize(SaveWith(Plot(0, 0, state: state)), fromVersion: 1));
    }

    [Fact]
    public void 读档_未开垦的格写进存档时抛()
    {
        // 写它的人对格式的理解与读它的人不一致，这正是该停下来的时刻
        Assert.Throws<InvalidDataException>(
            () => NewFarmland().Deserialize(SaveWith(Plot(0, 0, state: "Untilled")), fromVersion: 1));
    }

    [Fact]
    public void 读档_同一格出现两次时抛()
    {
        Farmland farmland = NewFarmland();

        var error = Assert.Throws<InvalidDataException>(
            () => farmland.Deserialize(SaveWith(Plot(4, 7), Plot(4, 7, seedId: Parsnip)), fromVersion: 1));

        Assert.Contains("(4, 7)", error.Message);
    }

    [Fact]
    public void 读档_坏数据不会让耕地停在读了一半的状态()
    {
        Farmland farmland = NewFarmland();
        farmland.TryTill(Tile);

        // 第一格是好的、第二格是坏的：整份都该被拒绝，第一格也不能落进来
        Assert.Throws<InvalidDataException>(() => farmland.Deserialize(
            SaveWith(Plot(0, 0, seedId: Parsnip, daysGrown: 1), Plot(1, 0, seedId: "seed_ghost", daysGrown: 1)),
            fromVersion: 1));

        // 原状态原样留着：既没被清掉，也没被读了一半的存档改过
        Assert.Equal(SoilState.Tilled, farmland.StateOf(Tile));
        Assert.False(farmland.HasCrop(Tile));
        Assert.Equal(SoilState.Untilled, farmland.StateOf(new TileCoord(1, 0)));
    }

    /// <summary>把条目拼成一份存档 JSON。坏数据用例只改一个字段，不必每次抄一整份。</summary>
    private static string SaveWith(params string[] entries) => "{ \"Plots\": [" + string.Join(",", entries) + "] }";

    private static string Plot(
        int x, int y, string state = "Tilled", string? seedId = null, int daysGrown = 0) =>
        $"{{ \"X\": {x}, \"Y\": {y}, \"State\": \"{state}\", " +
        $"\"SeedId\": {(seedId is null ? "null" : $"\"{seedId}\"")}, \"DaysGrown\": {daysGrown} }}";
}
