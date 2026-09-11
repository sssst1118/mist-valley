using System.Collections.Generic;
using Godot;
using XingGame.Systems.Economy;
using XingGame.Systems.Items;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 全屏商店面板（桥接层，M2-C）：读服务 → 调 <see cref="IShopSystem"/> → 把结果画出来（ADR-007）。
/// 打烊、钱不够、装不下、没定价全由系统判定，本类只把 <see cref="TradeResult"/> 翻成人话。
/// </summary>
/// <remarks>
/// <para>
/// <b>暂停不归面板管</b>：打开即冻树、关闭即还回来是 <see cref="PanelHost"/> 的事（备案 #36）。
/// 面板再设一次 <c>GetTree().Paused</c>，「开 A → 开 B」那一轮就会存两次暂停、还两次，
/// 而这类计数错乱只在换面板时才显形。
/// </para>
/// <para>
/// <b>关面板先吃键、后关</b>：<see cref="PanelHost.CloseAll"/> 一返回本面板已经不在树里，
/// <c>GetViewport()</c> 随即是 null——顺序反了会在 <c>_Input</c> 里抛 NRE，而 <c>_Input</c> 里的异常
/// 只报日志、不崩游戏，「跑起来没事」很容易把它糊过去。
/// </para>
/// </remarks>
public partial class ShopPanel : Control
{
    /// <summary>关面板的动作名，六个面板共用（映射在 <c>project.godot</c> 的 <c>[input]</c> 段）。</summary>
    private static readonly StringName CloseAction = "close_panel";

    /// <summary>占位列宽（像素）。美术一律先用占位符（铁律 7）。</summary>
    private static readonly Vector2 NameWidth = new(200f, 0f);
    private static readonly Vector2 PriceWidth = new(130f, 0f);

    /// <summary>成功绿 / 失败红：让「成交」与「这家店打烊了」在结果行里一眼分得开。</summary>
    private static readonly Color SuccessColor = new(0.45f, 0.82f, 0.55f);
    private static readonly Color FailureColor = new(0.9f, 0.45f, 0.4f);

    /// <summary>结果行要覆盖的字色主题项名。</summary>
    private static readonly StringName FontColorName = "font_color";

    /// <summary>
    /// 开哪家店。**在 .tscn 的导出槽里填**——<see cref="PanelHost"/> 实例化面板时不给任何参数。
    /// </summary>
    [Export] public string ShopId { get; set; } = "general_store";

    private IEconomySystem _economy = null!;
    private IShopSystem _shops = null!;
    private IItemTable _items = null!;
    private IInventory _inventory = null!;

    private Label _shopLabel = null!;
    private Label _goldLabel = null!;
    private Label _openLabel = null!;
    private Label _resultLabel = null!;
    private VBoxContainer _offerList = null!;
    private VBoxContainer _sellList = null!;

    public override void _Ready()
    {
        // 宿主只 AddChild、从不设 Visible，面板得自己保证是可见的。写成显式一句而不是靠 .tscn 的缺省，
        // 是为了挡住「照抄 ui/InventoryPanel.tscn」——那份是自己开关的，根节点带着 visible = false
        Visible = true;

        _shopLabel = GetNode<Label>("Center/Content/TopBar/ShopLabel");
        _goldLabel = GetNode<Label>("Center/Content/TopBar/GoldLabel");
        _openLabel = GetNode<Label>("Center/Content/TopBar/OpenLabel");
        _offerList = GetNode<VBoxContainer>("Center/Content/Scroll/Rows/OfferList");
        _sellList = GetNode<VBoxContainer>("Center/Content/Scroll/Rows/SellList");
        _resultLabel = GetNode<Label>("Center/Content/ResultLabel");

        _economy = GameRoot.Services.Get<IEconomySystem>();
        _shops = GameRoot.Services.Get<IShopSystem>();
        _items = GameRoot.Services.Get<IItemTable>();
        _inventory = GameRoot.Services.Get<IInventory>();

        Render();
    }

    /// <summary>
    /// 关面板的键是离散量，用 <c>_Input</c> 接（同 <c>ui/InventoryPanel</c>），不每帧轮询。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed(CloseAction)) return;

        // 这两行的顺序是契约：CloseAll 返回时本面板已不在树里，之后再去碰 GetViewport() 就是 NRE
        GetViewport().SetInputAsHandled();
        PanelHost.Instance.CloseAll();
    }

    /// <summary>
    /// 整屏重画。只在 <c>_Ready</c> 与每笔买卖之后调一次：面板开着时整棵树是暂停的，
    /// 时间不走、背包也只有本面板能动，没有需要每帧盯着的量。
    /// </summary>
    private void Render()
    {
        // 导出槽里填错 id 是场景装配错误。店名这一行退回 id 本身免得当场 NRE，
        // 紧接着的 IsOpen / Offers 会按接口契约抛 KeyNotFoundException，那条消息里带 id、比这里更好查
        _shops.TryGetShop(ShopId, out ShopDefinition? shop);
        _shopLabel.Text = shop?.Name ?? ShopId;

        _goldLabel.Text = $"金币 {_economy.Gold}";
        _openLabel.Text = _shops.IsOpen(ShopId) ? "营业中" : "已打烊";

        Clear(_offerList);
        Clear(_sellList);

        foreach (ShopOffer offer in _shops.Offers(ShopId)) _offerList.AddChild(OfferRow(offer));
        foreach (string itemId in SellableItemIds()) _sellList.AddChild(SellRow(itemId));
    }

    /// <summary>货架一行：名字 / 今日买价 / 今日收购价 / 背包持有 / 「买 1」。</summary>
    private Control OfferRow(ShopOffer offer)
    {
        int buyPrice = _shops.BuyPriceOf(offer.ItemId);
        int sellPrice = _shops.SellPriceOf(offer.ItemId);

        var row = new HBoxContainer();

        row.AddChild(Cell(_items.Get(offer.ItemId).Name, NameWidth));

        // 价 0 是「文档未给价」，不是免费——系统接口特意交代 UI 显示成「暂不出售」（§12.1 一个价都没给）
        row.AddChild(Cell(buyPrice > 0 ? $"买 {buyPrice}" : "暂不出售", PriceWidth));
        row.AddChild(Cell(sellPrice > 0 ? $"收 {sellPrice}" : "不收", PriceWidth));
        row.AddChild(Cell($"持有 {_inventory.Count(offer.ItemId)}", PriceWidth));

        // 不因为「没定价」「打烊」把按钮置灰：那两条判断是系统的，按下去由它回话，
        // 面板少一处会跟系统脱节的规则（ADR-007）
        var buy = new Button { Text = "买 1" };
        buy.Pressed += () => Trade(_shops.Buy(ShopId, offer.ItemId, 1), buying: true);
        row.AddChild(buy);

        return row;
    }

    /// <summary>卖出区一行：名字 / 今日收购价 / 背包持有 / 「卖 1」。</summary>
    private Control SellRow(string itemId)
    {
        var row = new HBoxContainer();

        row.AddChild(Cell(_items.Get(itemId).Name, NameWidth));
        row.AddChild(Cell($"收 {_shops.SellPriceOf(itemId)}", PriceWidth));
        row.AddChild(Cell($"持有 {_inventory.Count(itemId)}", PriceWidth));

        var sell = new Button { Text = "卖 1" };
        sell.Pressed += () => Trade(_shops.Sell(ShopId, itemId, 1), buying: false);
        row.AddChild(sell);

        return row;
    }

    /// <summary>
    /// 背包里这家店收的东西。按物品 id 去重：同一件东西占了多格时 <see cref="IInventory.Count"/>
    /// 已经跨格统计，不去重玩家会看见三行一模一样的「灵芽草」。
    /// </summary>
    private List<string> SellableItemIds()
    {
        var ids = new List<string>();

        foreach (ItemStack slot in _inventory.Slots)
        {
            if (slot.Count == 0) continue;
            if (ids.Contains(slot.ItemId)) continue;
            if (_shops.SellPriceOf(slot.ItemId) <= 0) continue; // 价 0 是「文档未给价」，不是白送

            ids.Add(slot.ItemId);
        }

        return ids;
    }

    /// <summary>
    /// 一笔买卖。<b>成功与失败都要写进结果行</b>——只报成功的话，玩家按了「买」没看见动静
    /// 会以为游戏卡了；成交与否是系统说了算，这里只负责说人话再把新状态画一遍。
    /// </summary>
    private void Trade(TradeResult result, bool buying)
    {
        _resultLabel.Text = Message(result, buying);
        _resultLabel.AddThemeColorOverride(FontColorName, result == TradeResult.Success ? SuccessColor : FailureColor);

        Render();
    }

    /// <summary>
    /// <see cref="TradeResult"/> 翻成人话。失败原因在系统里分得这么细，就是为了这里能说清楚。
    /// </summary>
    /// <remarks>
    /// <b>为什么要 <paramref name="buying"/></b>：<see cref="TradeResult.PriceNotSet"/> 在系统里同时覆盖
    /// 「买价未给」与「卖价未给」两种情形（<c>ShopSystem.Buy</c> 与 <c>Sell</c> 都在单价 ≤ 0 时返回它），
    /// 于是这一条是整张表里**唯一分方向**的文案——不分开的话，玩家点「买 1」会被告知「不能卖」。
    /// 其余几条要么只可能出现在一边（钱不够、没这么多），要么两边都成立（打烊、不进这个货），故不必分。
    /// </remarks>
    private static string Message(TradeResult result, bool buying) => result switch
    {
        TradeResult.Success => "成交",
        TradeResult.ShopClosed => "这家店打烊了",
        TradeResult.ItemNotStocked => "这家店不进这个货",
        TradeResult.PriceNotSet => buying ? "没定价，暂不出售" : "没定价，不能卖",
        TradeResult.InsufficientFunds => "金币不够",
        TradeResult.InventoryFull => "背包放不下",
        TradeResult.ItemNotOwned => "背包里没这么多",
        // 将来加了新原因就显示原始枚举名：漏翻译一眼看得见，比临时编一句说辞安全
        _ => result.ToString(),
    };

    /// <summary>
    /// 清空一个列表。<b>先摘再删</b>：<c>QueueFree</c> 要到帧末才真的删，只调它的话重画之后
    /// 旧行会跟新行在同一帧里一起画出来。
    /// </summary>
    private static void Clear(Container list)
    {
        foreach (Node child in list.GetChildren())
        {
            list.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>占位单元格：定宽 + 裁字，理由同 <c>ui/InventoryPanel</c>——最长的名字不该拽歪整排。</summary>
    private static Label Cell(string text, Vector2 width) => new()
    {
        Text = text,
        CustomMinimumSize = width,
        VerticalAlignment = VerticalAlignment.Center,
        ClipText = true,
    };
}
