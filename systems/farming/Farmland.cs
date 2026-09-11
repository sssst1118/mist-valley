using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;

namespace XingGame.Systems.Farming;

/// <summary>
/// 耕地的全部状态。纯 C#：不认识 Godot，也<b>不认识事件总线</b>——
/// 「什么时候长一天」是 <see cref="FarmingSystem"/> 的事，本类只负责「长一天」这个动作。
/// </summary>
/// <remarks>
/// <para>
/// 状态按格存在字典里，<b>不在字典里的格就是未开垦</b>。于是「从未被碰过的格」不占内存，
/// 存档也不必为整张地图写一堆空对象——大地图上的耕地是稀疏的。
/// </para>
/// <para>
/// 三条定死的规则（ADR-014）：耕地是格子概念不是实体；生长由 <c>DayStarted</c> 驱动而非每帧；
/// 换季枯萎由 <c>SeasonChanged</c> 驱动。本类只提供动作，不自己订阅任何事件。
/// </para>
/// <para>
/// <b>不浇水只是停滞，不会枯萎</b>：§3.2 只说换季时非温室作物枯萎，没说缺水会死。
/// 忘浇一天水就死一片，是玩家最恨的那种惩罚。
/// </para>
/// </remarks>
public sealed class Farmland : ISaveable
{
    /// <summary>
    /// 单次收获的产量。<b>设计文档未给产量，待裁决</b>——§6.2 只有生长天数与售价，
    /// 没有任何一列是「每次收获几个」。取 1 是最保守的取值：将来补上产量表时，
    /// 改的只是这个常量，不影响存档（存档不记产量）。
    /// </summary>
    public const int HarvestYield = 1;

    private readonly ICropTable _crops;
    private readonly Dictionary<TileCoord, Plot> _plots = new();

    public Farmland(ICropTable crops) =>
        _crops = crops ?? throw new ArgumentNullException(nameof(crops));

    /// <summary>未记录的格一律是未开垦。</summary>
    public SoilState StateOf(TileCoord tile) =>
        _plots.TryGetValue(tile, out Plot? plot) ? plot.State : SoilState.Untilled;

    public bool HasCrop(TileCoord tile) => _plots.TryGetValue(tile, out Plot? plot) && plot.Crop is not null;

    public int DaysGrown(TileCoord tile) => _plots.TryGetValue(tile, out Plot? plot) ? plot.DaysGrown : 0;

    public bool IsReadyToHarvest(TileCoord tile) =>
        _plots.TryGetValue(tile, out Plot? plot) && plot.Crop is not null && plot.DaysGrown >= plot.Crop.GrowthDays;

    /// <summary>
    /// 这一格现在锄得动吗（还是未开垦）。<b>只读</b>：不改任何状态。
    /// </summary>
    /// <remarks>
    /// <b>「能不能」与「做不做」必须是同一条判据</b>：灵锄术要在扣灵力**之前**知道这一片有几格真的
    /// 动得了（<c>LifeSpellSystem.TryCastAt</c>），而它不能靠先试着锄一下来问——那已经把地改了。
    /// 所以判据只留这一份，<see cref="TryTill"/> 用的也是它：分开写两份的话，改了一处忘了另一处，
    /// 症状就是「灵力扣了、地没动」。
    /// </remarks>
    public bool CanTill(TileCoord tile) => StateOf(tile) == SoilState.Untilled;

    /// <summary>
    /// 这一格现在浇得动吗（已开垦、且还没浇）。<b>只读</b>：不改任何状态。
    /// </summary>
    /// <remarks>与 <see cref="CanTill"/> 同理：法术要在花灵力之前先问这一句。</remarks>
    public bool CanWater(TileCoord tile) =>
        _plots.TryGetValue(tile, out Plot? plot) && plot.State == SoilState.Tilled;

    /// <summary>未开垦 → 已开垦。已经开垦（含已浇水）的格再锄一次返回 false，不改变任何状态。</summary>
    public bool TryTill(TileCoord tile)
    {
        if (!CanTill(tile)) return false;

        _plots[tile] = new Plot(SoilState.Tilled);
        return true;
    }

    /// <summary>
    /// 已开垦 → 已浇水。未开垦的格浇不了（水会流走），已经浇过的格再浇一次返回 false。
    /// </summary>
    public bool TryWater(TileCoord tile)
    {
        if (!CanWater(tile)) return false;

        _plots[tile].State = SoilState.Watered;
        return true;
    }

    /// <summary>
    /// 播种。格必须是开垦过的、且当前没有作物。
    /// </summary>
    /// <remarks>
    /// 播种<b>不改土壤状态</b>：在浇过水的格上下种，当天就算浇过水，这是玩家期待的顺序
    /// （先浇水再播种不该白浇一天）。
    /// <para>
    /// 种子 id 不在作物表里是<b>编程错误</b>，抛 <c>KeyNotFoundException</c> 而不是返回 false——
    /// 与 <c>Inventory.Add</c> 对未知物品 id 的处理一致：凭空种一粒表里没有的种子，必然是 id 写错了，
    /// 静默失败会让这个错拖到收获时才炸。
    /// </para>
    /// </remarks>
    public bool TryPlant(TileCoord tile, string seedId)
    {
        CropDefinition definition = _crops.GetBySeed(seedId);

        if (!_plots.TryGetValue(tile, out Plot? plot) || plot.Crop is not null) return false;

        plot.Crop = definition;
        plot.DaysGrown = 0;
        return true;
    }

    /// <summary>
    /// 收获。成功时输出作物 id 与数量，并把该格恢复成「已开垦、无作物」；
    /// 可多次收获的作物收获后重新开始计时（同一个 <see cref="CropDefinition.GrowthDays"/>，
    /// 文档没给再生天数）。
    /// </summary>
    /// <remarks>
    /// 收获后土壤回到「已开垦」而不是保留浇水状态：浇水只对当天有效，而收获之后作物要重新长一轮，
    /// 明天照样得浇。留着浇水状态会让「收完不用浇」变成一个没人设计过的免费天数。
    /// </remarks>
    public bool TryHarvest(TileCoord tile, out string cropId, out int count)
    {
        cropId = string.Empty;
        count = 0;

        if (!_plots.TryGetValue(tile, out Plot? plot) || plot.Crop is null) return false;
        if (plot.DaysGrown < plot.Crop.GrowthDays) return false;

        CropDefinition definition = plot.Crop;
        cropId = definition.CropId;
        count = HarvestYield;

        if (definition.Regrowable)
        {
            plot.DaysGrown = 0;      // 留着株，重新计时
        }
        else
        {
            plot.Crop = null;
            plot.DaysGrown = 0;
        }

        plot.State = SoilState.Tilled;
        return true;
    }

    /// <summary>
    /// 推进一天：浇过水（或下雨）的作物生长 +1；随后<b>所有格</b>的浇水状态重置为「已开垦」。
    /// </summary>
    /// <param name="rained">当天是否下雨（含暴风雨）。由 <see cref="FarmingSystem"/> 读天气后传入。</param>
    public void AdvanceDay(bool rained)
    {
        foreach (Plot plot in _plots.Values)
        {
            // 顺序要紧：先按今天的浇水状态决定长不长，再清掉浇水状态——反了的话今天浇的水就白浇了
            if (plot.Crop is not null &&
                (rained || plot.State == SoilState.Watered) &&
                plot.DaysGrown < plot.Crop.GrowthDays)
            {
                plot.DaysGrown++;
            }

            if (plot.State == SoilState.Watered) plot.State = SoilState.Tilled;
        }
    }

    /// <summary>
    /// 换季枯萎：清掉所有作物，<b>保留开垦状态</b>（§3.2 只说作物枯萎，没说土壤变回荒地）。
    /// </summary>
    /// <remarks>
    /// 已浇水的格一并回到「已开垦」：作物都没了，水留着没有任何作用；而且换季是新的一天，
    /// 上一天浇的水不该跨季生效。
    /// </remarks>
    public void WitherAll()
    {
        foreach (Plot plot in _plots.Values)
        {
            plot.Crop = null;
            plot.DaysGrown = 0;
            plot.State = SoilState.Tilled;
        }
    }

    public string SaveKey => "farmland";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    public int Version => 1;

    /// <summary>
    /// 只存开垦过的格——<b>未开垦的格不必存</b>，读回来时未记录的格本来就是未开垦，
    /// 而地图上大部分格永远是荒地。已开垦但空着的格<b>要</b>存：那是玩家的劳动成果，
    /// 不存的话读档后锄过的地就变回荒野了。
    /// <para>
    /// 按 (Y, X) 排序输出：字典的遍历顺序随插入顺序而变，稳定排序让存档的 diff 可读，
    /// 也让「同一份状态序列化两次结果相同」成立。
    /// </para>
    /// </summary>
    public string Serialize()
    {
        var saved = new List<SavedPlot>(_plots.Count);
        foreach (KeyValuePair<TileCoord, Plot> pair in _plots)
        {
            saved.Add(new SavedPlot(
                pair.Key.X,
                pair.Key.Y,
                pair.Value.State.ToString(),
                pair.Value.Crop?.SeedId,
                pair.Value.DaysGrown));
        }

        saved.Sort(static (left, right) => left.Y != right.Y ? left.Y.CompareTo(right.Y) : left.X.CompareTo(right.X));

        return JsonSerializer.Serialize(new SavedFarmland(saved.ToArray()), SaveJsonOptions);
    }

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009 同款理由）
        if (fromVersion > Version)
            throw new NotSupportedException($"耕地存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedFarmland saved = JsonSerializer.Deserialize<SavedFarmland>(json, SaveJsonOptions)
            ?? throw new InvalidDataException("耕地存档内容为空");

        SavedPlot[] plots = saved.Plots ?? throw new InvalidDataException("耕地存档缺少格子数组");

        // 先整份校验再落盘：坏存档不该让耕地停在「读了一半」的状态（同 Inventory）
        var restored = new Dictionary<TileCoord, Plot>(plots.Length);
        foreach (SavedPlot plot in plots)
        {
            TileCoord tile = new(plot.X, plot.Y);

            // 同一格出现两次时，谁生效取决于文件顺序——那是「有时对有时不对」的幽灵 bug
            if (!restored.TryAdd(tile, Restore(tile, plot)))
                throw new InvalidDataException($"耕地存档出现重复的格子 ({plot.X}, {plot.Y})");
        }

        // 整状态覆盖：存档里没有的格回到未开垦，而不是把旧状态留着
        _plots.Clear();
        foreach (KeyValuePair<TileCoord, Plot> pair in restored) _plots[pair.Key] = pair.Value;
    }

    /// <summary>存档是外部输入，坏值当场抛——越界值渗进耕地后，症状会出现在离病因很远的地方。</summary>
    private Plot Restore(TileCoord tile, SavedPlot saved)
    {
        if (!TryParseState(saved.State, out SoilState state))
            throw new InvalidDataException($"耕地存档 ({tile.X}, {tile.Y}) 的土壤状态「{saved.State}」不是合法的 SoilState");

        // 未开垦的格不该出现在存档里：写它的人对格式的理解与读它的人不一致，这正是该停下来的时刻
        if (state == SoilState.Untilled)
            throw new InvalidDataException($"耕地存档 ({tile.X}, {tile.Y}) 记的是未开垦——未开垦的格不该写进存档");

        if (saved.DaysGrown < 0)
            throw new InvalidDataException($"耕地存档 ({tile.X}, {tile.Y}) 的生长天数为 {saved.DaysGrown}");

        if (string.IsNullOrEmpty(saved.SeedId))
        {
            if (saved.DaysGrown != 0)
                throw new InvalidDataException($"耕地存档 ({tile.X}, {tile.Y}) 有生长天数却没有种子 id");

            return new Plot(state);
        }

        if (!_crops.TryGetBySeed(saved.SeedId, out CropDefinition definition))
            throw new InvalidDataException($"耕地存档 ({tile.X}, {tile.Y}) 的种子 id「{saved.SeedId}」不在作物表里");

        if (saved.DaysGrown > definition.GrowthDays)
            throw new InvalidDataException(
                $"耕地存档 ({tile.X}, {tile.Y}) 的生长天数 {saved.DaysGrown} 超过 {definition.SeedId} 的 {definition.GrowthDays} 天");

        return new Plot(state) { Crop = definition, DaysGrown = saved.DaysGrown };
    }

    /// <summary>
    /// 状态名按<b>大小写不敏感</b>匹配（同 <see cref="ItemTable"/> 解析分类）。
    /// 不用 <c>Enum.TryParse</c>：它会把 "1" 认成序号 1 的状态，于是「按数字写状态」这种会随枚举插值
    /// 错位的事被静默接受，而存档是比 JSON 数据表更该守死的地方。
    /// </summary>
    private static bool TryParseState(string? text, out SoilState state)
    {
        foreach (SoilState candidate in Enum.GetValues<SoilState>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                state = candidate;
                return true;
            }
        }

        state = default;
        return false;
    }

    /// <summary>一格的全部状态。私有且可变：外部只该经本类的动作改它，绕过动作直接改就绕过了所有规则。</summary>
    private sealed class Plot
    {
        public Plot(SoilState state) => State = state;

        public SoilState State { get; set; }

        public CropDefinition? Crop { get; set; }

        public int DaysGrown { get; set; }
    }

    /// <summary>存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。</summary>
    private sealed record SavedFarmland(SavedPlot[] Plots);

    /// <param name="State">土壤状态名（"Tilled"/"Watered"），不写序号——序号会随枚举插值错位（ADR-012）。</param>
    private sealed record SavedPlot(int X, int Y, string State, string? SeedId, int DaysGrown);

    /// <summary>字段名与状态名都写出来，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions SaveJsonOptions = new() { WriteIndented = true };
}
