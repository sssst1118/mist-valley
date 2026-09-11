using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using XingGame.Systems.Items;

namespace XingGame.Systems.Economy;

/// <summary>
/// 商店表，数据在 <c>data/economy/shops.json</c>。加载时逐条校验——数据表是外部输入，
/// 错在表里就要在加载时炸，不要等到玩家点开商店才炸（那时现场离病因已经很远）。
/// </summary>
/// <remarks>
/// <para>
/// <b>出处（行号对 <c>docs/public/design.md</c>；剧透版内容与之一致）：</b>
/// §12.1 商店系统（第 1314-1326 行）给了九家店卖什么大类，§5.2 雾谷镇设施（第 235-249 行）
/// 给了营业时间，两处合起来才是一家完整的商店。两表重叠的六家（杂货店／铁匠铺／木匠铺／
/// 鱼店／酒馆／诊所）营业时间完全一致，没有冲突。
/// </para>
/// <para>
/// <b>§12.1 一个商品价都没给</b>，所以商品条目的价格一律留 0（「文档未给，待补」），
/// 由 <see cref="ShopSystem"/> 在成交处以 <see cref="TradeResult.PriceNotSet"/> 挡住——
/// 编一个价出去比不卖更糟，将来要逐条推翻。
/// </para>
/// <para>
/// <b>为什么货架大多是空的：</b>文档只写了各店经营的大类（「种子、肥料、杂货」「鱼饵、鱼竿」
/// 「药品」…），除种子与肥料外一个具体商品名都没给。这里只录<b>文档里确有名字</b>的东西，
/// 宁可空着——空着是一条待补的清单，编出来则是一堆将来要逐条核对删除的假数据。
/// 各店缺的具体商品：鱼店＝鱼竿（§9 第 413 行的四种鱼竿）与鱼饵（归钓鱼模块的
/// <c>data/items/fishing.json</c>，不代录）；木匠铺＝家具（§13 第 1412 行的七种家具）；
/// 酒馆／诊所＝食物／药品（文档未给名字）；沙漠商店＝铱锭（归采矿区）；
/// 修士坊市＝灵石／丹药／法宝／功法（M3 的事）。
/// §5.2 的图书馆／社区中心／博物馆／巫师塔<b>不是商店</b>（捐赠、阅读、剧情），不录。
/// </para>
/// </remarks>
public sealed class ShopTable
{
    /// <summary>缺省商店表位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/economy/shops.json";

    private readonly Dictionary<string, ShopDefinition> _byId;
    private readonly ReadOnlyCollection<ShopDefinition> _all;

    private ShopTable(Dictionary<string, ShopDefinition> byId, ReadOnlyCollection<ShopDefinition> all)
    {
        _byId = byId;
        _all = all;
    }

    /// <summary>按 JSON 里的顺序排列，UI 列表直接照用。</summary>
    public IReadOnlyList<ShopDefinition> All => _all;

    public bool TryGet(string shopId, out ShopDefinition shop)
    {
        if (_byId.TryGetValue(shopId, out ShopDefinition? found))
        {
            shop = found;
            return true;
        }

        shop = null!;   // out 必须先赋值：找不到以返回值 false 表达，输出用 null（同 ItemTable）
        return false;
    }

    public ShopDefinition Get(string shopId) =>
        _byId.TryGetValue(shopId, out ShopDefinition? shop)
            ? shop
            : throw new KeyNotFoundException($"商店表里没有 id 为「{shopId}」的商店");

    /// <summary>
    /// 三个入口都要 <see cref="IItemTable"/>：货架上的 id 是否真实存在，只能在<b>加载时</b>校验。
    /// 留一个不要物品表的版本，就等于给「绕过校验」留了条路——那正是「两份数据各自的测试都
    /// 发现不了」那种错的口子（同 <c>CropTable</c>）。
    /// </summary>
    public static ShopTable FromJson(string json, IItemTable items)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("shops", out JsonElement shops) ||
            shops.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("商店表缺少 shops 数组");
        }

        var byId = new Dictionary<string, ShopDefinition>(StringComparer.Ordinal);
        var all = new List<ShopDefinition>();

        foreach (JsonElement element in shops.EnumerateArray())
        {
            ShopDefinition shop = ParseShop(element, items);

            // 重复 id 会让「按 id 取到的是哪一家」取决于文件顺序，且两家的营业时间可能不同——数据错误，启动即报
            if (!byId.TryAdd(shop.Id, shop))
                throw new InvalidDataException($"商店表出现重复 id：{shop.Id}");

            all.Add(shop);
        }

        return new ShopTable(byId, all.AsReadOnly());
    }

    public static ShopTable FromFile(string path, IItemTable items) =>
        FromJson(File.ReadAllText(path), items);

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省商店表，与 <c>ItemTable.LoadDefault()</c> 同款做法
    /// （见 ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」，M8 改为桥接层注入路径）。
    /// </summary>
    public static ShopTable LoadDefault(IItemTable items)
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path, items);
    }

    private static ShopDefinition ParseShop(JsonElement element, IItemTable items)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("id", out JsonElement idElement) ||
            idElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("商店表出现缺少 id 字段的条目");
        }

        string id = idElement.GetString()!;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("商店表出现空 id");

        string name = RequiredString(element, "name", id);

        ShopSchedule schedule = ParseSchedule(element, id);

        (int openHour, int closeHour) = ParseHours(element, id, schedule);

        return new ShopDefinition(id, name, schedule, openHour, closeHour, ParseGoods(element, id, items));
    }

    /// <summary>
    /// 缺省是 <see cref="ShopSchedule.ClockRange"/>：九家店里七家都是钟点营业，
    /// 让多数条目少写一个字段；不定期/随机的那两家必须<b>显式</b>写出来。
    /// </summary>
    private static ShopSchedule ParseSchedule(JsonElement element, string id)
    {
        if (!element.TryGetProperty("schedule", out JsonElement scheduleElement)) return ShopSchedule.ClockRange;

        if (scheduleElement.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"商店 {id} 的 schedule 不是字符串");

        string text = scheduleElement.GetString()!;
        foreach (ShopSchedule candidate in Enum.GetValues<ShopSchedule>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase)) return candidate;
        }

        // 不用 Enum.TryParse：它会把 "1" 认成序号 1 的取值，于是「按数字写开张方式」这种
        // 会随枚举插值错位的事被静默接受（同 ItemTable 解析分类）
        throw new InvalidDataException($"商店 {id} 的开张方式「{text}」不是合法的 ShopSchedule");
    }

    /// <summary>
    /// 营业钟点。<b>左闭右开</b>，且允许 close = 24（酒馆 12:00-24:00、全天商店 0:00-24:00）。
    /// 不定期/随机商店<b>不许</b>写钟点：写了的那个数没人会读，留着就是一份会过期的假信息。
    /// </summary>
    private static (int OpenHour, int CloseHour) ParseHours(JsonElement element, string id, ShopSchedule schedule)
    {
        bool hasOpen = element.TryGetProperty("openHour", out JsonElement openElement);
        bool hasClose = element.TryGetProperty("closeHour", out JsonElement closeElement);

        if (schedule == ShopSchedule.Irregular)
        {
            if (hasOpen || hasClose)
                throw new InvalidDataException($"商店 {id} 是{schedule}的，不该写营业钟点");

            return (0, 0);
        }

        if (!hasOpen || openElement.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"商店 {id} 缺少数字字段 openHour");

        if (!hasClose || closeElement.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"商店 {id} 缺少数字字段 closeHour");

        int openHour = openElement.GetInt32();
        int closeHour = closeElement.GetInt32();

        // 空区间与倒置区间都会让「营业时间」静默失效（永远打烊），而数据看着很正常
        if (openHour < 0 || closeHour > 24 || openHour >= closeHour)
            throw new InvalidDataException($"商店 {id} 的营业时间 {openHour}:00-{closeHour}:00 不是有效区间");

        return (openHour, closeHour);
    }

    private static IReadOnlyList<string> ParseGoods(JsonElement element, string id, IItemTable items)
    {
        if (!element.TryGetProperty("goods", out JsonElement goodsElement) ||
            goodsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"商店 {id} 缺少 goods 数组");
        }

        var goods = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonElement good in goodsElement.EnumerateArray())
        {
            if (good.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"商店 {id} 的 goods 里出现非字符串条目");

            string itemId = good.GetString()!;

            // 货架上出现两次会让 Offers 列出同一格两遍、买入的数量含义也变得可疑
            if (!seen.Add(itemId))
                throw new InvalidDataException($"商店 {id} 的货架上重复出现「{itemId}」");

            // 货架 id 与物品表对不上，是两份数据各自的测试都发现不了的那种错——只能在这里拦
            if (!items.TryGet(itemId, out _))
                throw new InvalidDataException($"商店 {id} 的货架上有物品表里没有的 id「{itemId}」");

            goods.Add(itemId);
        }

        return goods.AsReadOnly();
    }

    /// <summary>字段缺失或类型不对就当场报错：表是外部输入，报错消息里带上 id 才定位得到那一行。</summary>
    private static string RequiredString(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"商店 {id} 缺少字符串字段 {property}");

        return value.GetString()!;
    }

    private static string? FindDefaultFile()
    {
        foreach (string root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, DefaultRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
