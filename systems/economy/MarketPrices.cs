using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;
using XingGame.Core.Time;
using XingGame.Systems.Items;

namespace XingGame.Systems.Economy;

/// <summary>
/// §12.2 动态市场价格。M2 <b>只做骨架</b>：波动幅度照抄文档，<b>触发条件是本切片定的</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不按文档的供需来：</b>§12.2 的「供大于求 / 供不应求」要有<b>全服交易数据</b>才谈得上
/// （卖出多少、买入多少），单机 M2 一条交易记录都没有，硬做只能编一套假的供需。所以这里用
/// 「物品 id + 游戏日」的确定性掷点<b>模拟</b>每日的供需涨落：同一天里不同物品涨跌不同、
/// 同一物品跨天会变，§12.2 要的「每日刷新、玩家可查看价格趋势」就都立住了。
/// 将来接入真实供需统计时，只需替换 <see cref="RollPermille"/> 这一个纯函数——
/// 价格状态、存档形态、调用方全都不用动。
/// </para>
/// <para>
/// <b>只作用于农产品</b>（§12.2 的原话是「农产品价格随季节和供需波动」）：本切片按
/// <see cref="ItemCategory.Crop"/> 判定。种子、材料、畜产的价格波动文档没提，不猜。
/// </para>
/// <para>
/// <b>为什么按天算而不是掷一次存下来：</b>掷点只依赖（物品 id, 游戏日），所以读档后价格必然
/// 复现，不存在「读档价格跳变」。但当下这一天的系数仍然<b>要存</b>——存档是权威事实
/// （同 ARCHITECTURE 备案 #16：天气存当下实际值而非按种子重掷），万一掷法改了，
/// 旧档里那一天的价格也不会跟着变。
/// </para>
/// </remarks>
public sealed class MarketPrices : ISaveable
{
    /// <summary>供大于求：价格下降 10-30%（§12.2）→ 系数 700‰–900‰。</summary>
    public const int MinDownPermille = 700;

    /// <summary>供大于求里跌得最少的一档：-10%（§12.2）。</summary>
    public const int MaxDownPermille = 900;

    /// <summary>供需相当：原价。</summary>
    public const int FlatPermille = 1000;

    /// <summary>供不应求里涨得最少的一档：+10%（§12.2）。</summary>
    public const int MinUpPermille = 1100;

    /// <summary>供不应求：价格上升 10-50%（§12.2）→ 系数 1100‰–1500‰。</summary>
    public const int MaxUpPermille = 1500;

    /// <summary>还没算过任何一天的价格（新档在进商店之前就存档）。</summary>
    public const int NotPriced = -1;

    private const int MinutesPerDay = 24 * 60;

    private readonly IItemTable _items;

    /// <summary>只装<b>农产品</b>的当日系数；不在表里的物品一律按原价（1000‰）。</summary>
    private readonly Dictionary<string, int> _permille = new(StringComparer.Ordinal);

    public MarketPrices(IItemTable items) =>
        _items = items ?? throw new ArgumentNullException(nameof(items));

    /// <summary>当前算的是哪一天（<see cref="NotPriced"/> = 还没算过）。</summary>
    public int DayIndex { get; private set; } = NotPriced;

    /// <summary>
    /// 游戏时刻 → 累计天序（元年春 1 日 = 0）。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="GameTime.TotalMinutes"/> 整除一天，而不是 (年, 季, 日) 自己拼一个数：
    /// <c>TotalMinutes</c> 已经把 0:00–5:59 折算到当日 6:00 之后（ARCHITECTURE 备案 #7），
    /// 于是整除的分界点天然落在 6:00 这个日界上——与「日界在 6:00」的既有约定自动一致，
    /// 不必在这里再抄一遍日界规则。
    /// </remarks>
    public static int DayIndexOf(GameTime time) => time.TotalMinutes / MinutesPerDay;

    /// <summary>
    /// 按给定的一天重算全部农产品的系数。同一天重复调用是<b>幂等</b>的——
    /// 每次都从掷点函数重新算，不做「在上一次的基础上再涨跌」的累乘。
    /// </summary>
    public void Refresh(int dayIndex)
    {
        if (dayIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(dayIndex), dayIndex, "天序不能为负");

        _permille.Clear();
        foreach (ItemDefinition definition in _items.All)
        {
            if (definition.Category != ItemCategory.Crop) continue;

            _permille[definition.Id] = RollPermille(definition.Id, dayIndex);
        }

        DayIndex = dayIndex;
    }

    /// <summary>某物品今日的系数（千分比）。非农产品恒为原价，未算过的一天也按原价。</summary>
    public int MultiplierPermille(string itemId) =>
        _items.Get(itemId).Category != ItemCategory.Crop || !_permille.TryGetValue(itemId, out int permille)
            ? FlatPermille
            : permille;

    /// <summary>
    /// 今日实价。<b>四舍五入到整数</b>：35 金 × 700‰ = 24.5 → 25。
    /// 用四舍五入而不是向下取整，是因为「跌 30%」在 1 金的物品上会变成「白送」——
    /// 取整方向一致地偏向玩家，免得小面额商品被取整吃掉。
    /// </summary>
    public int Price(string itemId, int basePrice)
    {
        if (basePrice < 0)
            throw new ArgumentOutOfRangeException(nameof(basePrice), basePrice, "原价不能为负");

        // 先乘再除，且用 long 过渡：basePrice × 1500 在 int 里会溢出
        long scaled = ((long)basePrice * MultiplierPermille(itemId)) + 500;
        return (int)(scaled / 1000);
    }

    /// <summary>
    /// 每日系数掷点。纯函数（同一天同一物品必得同一结果），<b>与进程无关</b>。
    /// </summary>
    /// <remarks>
    /// 自己写 FNV-1a 而不是用 <c>string.GetHashCode</c>：后者的值是<b>每个进程随机化</b>的，
    /// 同一份存档换一次启动就会算出不一样的价格——那正是本类要避免的「读档价格跳变」。
    /// 也不用 <c>System.HashCode</c>，它同样不承诺跨进程稳定。
    /// </remarks>
    private static int RollPermille(string itemId, int dayIndex)
    {
        uint hash = Fnv1a(itemId, dayIndex);

        // 三档：供大于求 / 供需相当 / 供不应求。三档的边界与区间取值都照抄 §12.2，
        // 只是「这一物品今天属于哪一档」由掷点定——那正是本切片替文档补的那一半。
        int bucket = (int)(hash % 100u);

        if (bucket < 33) return MinDownPermille + (int)(hash / 100u % (uint)(MaxDownPermille - MinDownPermille + 1));
        if (bucket < 66) return FlatPermille;

        return MinUpPermille + (int)(hash / 100u % (uint)(MaxUpPermille - MinUpPermille + 1));
    }

    private static uint Fnv1a(string itemId, int dayIndex)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in itemId) hash = (hash ^ c) * 16777619u;

            // 天序也过一遍哈希：只把它当加数的话，(物品, 第 n 天) 与 (物品, 第 n+1 天)
            // 会落在相邻的桶里，价格变成「每天挪一格」的规律波动
            hash = (hash ^ (uint)dayIndex) * 16777619u;
            return hash;
        }
    }

    public string SaveKey => "market";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    public int Version => 1;

    /// <summary>
    /// 存当天（<see cref="DayIndex"/> 那天）的系数，原价的物品<b>不写</b>——
    /// 缺省即原价是这套格式的约定，写一堆 1000 只会让存档变长。
    /// </summary>
    public string Serialize()
    {
        var saved = new List<SavedPrice>(_permille.Count);
        foreach (KeyValuePair<string, int> pair in _permille)
        {
            if (pair.Value == FlatPermille) continue;

            saved.Add(new SavedPrice(pair.Key, pair.Value));
        }

        saved.Sort(static (left, right) => string.CompareOrdinal(left.ItemId, right.ItemId));

        return JsonSerializer.Serialize(new SavedMarket(DayIndex, saved.ToArray()), SaveJsonOptions);
    }

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009 同款理由）
        if (fromVersion > Version)
            throw new NotSupportedException($"市场价格存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedMarket saved = JsonSerializer.Deserialize<SavedMarket>(json, SaveJsonOptions)
            ?? throw new InvalidDataException("市场价格存档内容为空");

        SavedPrice[] prices = saved.Multipliers
            ?? throw new InvalidDataException("市场价格存档缺少系数数组");

        // 0（第 0 天）与 -1（还没算过）都是合法值，所以「字段不在」必须与「字段是 0」分开——
        // 可空类型是这两者的唯一分界线（同 Wallet 的 SavedWallet）。静默读成 0 会让今天的价格
        // 悄悄对不上，且下次 Serialize 就把 0 写死
        if (saved.Day is not int day)
            throw new InvalidDataException("市场价格存档缺少 Day 字段");

        if (day < NotPriced)
            throw new InvalidDataException($"市场价格存档的天序为 {day}");

        // 先整份校验再落盘：坏存档不该让价格停在「读了一半」的状态（同 Inventory）
        var restored = new Dictionary<string, int>(prices.Length, StringComparer.Ordinal);
        foreach (SavedPrice price in prices)
        {
            // 数组里可以写 null，System.Text.Json 照收。不判就是下面第一行一个 NRE 冒出去——
            // 按 ADR-009 坏档该抛 InvalidDataException，将来 catch 坏档的代码要接得住
            if (price is null)
                throw new InvalidDataException("市场价格存档里有一条空系数");

            if (string.IsNullOrEmpty(price.ItemId))
                throw new InvalidDataException("市场价格存档出现空的物品 id");

            if (!_items.TryGet(price.ItemId, out _))
                throw new InvalidDataException($"市场价格存档的物品 id「{price.ItemId}」不在物品表里");

            if (price.Permille == FlatPermille || !IsWithinDocumentedBand(price.Permille))
                throw new InvalidDataException(
                    $"市场价格存档里「{price.ItemId}」的系数 {price.Permille} 不是 §12.2 给的波动区间" +
                    $"（原价的物品本就不该写进来）");

            // 同一物品出现两次时，谁生效取决于文件顺序——那是「有时对有时不对」的幽灵 bug
            if (!restored.TryAdd(price.ItemId, price.Permille))
                throw new InvalidDataException($"市场价格存档出现重复的物品 id「{price.ItemId}」");
        }

        // 整状态覆盖：存档里没写的物品回到原价，而不是把上一天的系数留着
        _permille.Clear();
        foreach (KeyValuePair<string, int> pair in restored) _permille[pair.Key] = pair.Value;

        DayIndex = day;
    }

    /// <summary>系数必须落在 §12.2 给的两段区间里（不含原价 1000）。</summary>
    private static bool IsWithinDocumentedBand(int permille) =>
        permille is >= MinDownPermille and <= MaxDownPermille
                or >= MinUpPermille and <= MaxUpPermille;

    /// <summary>
    /// 存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。
    /// <c>int?</c> 是为了把「Day 字段不在」与「Day 是 0」分开——后者是合法的第 0 天。
    /// </summary>
    private sealed record SavedMarket(int? Day, SavedPrice[] Multipliers);

    private sealed record SavedPrice(string ItemId, int Permille);

    /// <summary>字段名与物品 id 都写出来，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions SaveJsonOptions = new() { WriteIndented = true };
}
