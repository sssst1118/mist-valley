using System;
using System.Collections.Generic;
using Godot;
using XingGame.Systems.Economy;
using XingGame.Systems.Items;
using XingGame.Systems.Ranching;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 畜牧面板（桥接层）：读 <see cref="IRanchingSystem"/> / <see cref="IAnimalTable"/>，
/// 把栏里的动物与可养的目录画成两列行；喂食与买入转发给系统（ADR-007）。
/// </summary>
/// <remarks>
/// <para>
/// <b>开合与暂停都不归本类</b>：面板由 <see cref="PanelHost"/> 实例化、挂上、拆下，整棵树的暂停也在那里
/// （备案 #36）。本类里没有 <c>GetTree().Paused</c>——面板再设一次的话，「开 A → 开 B」那一轮会
/// 存两次暂停、还两次，而这类计数错乱只在换面板时才显形。
/// </para>
/// <para>
/// <b>没有玩法判断</b>：喂得成不成由 <see cref="IRanchingSystem.TryFeed"/> 说了算，买不买得起由
/// <see cref="IEconomySystem.TrySpendGold"/> 说了算。本类只把「为什么不行」画出来——
/// 失败没有原因，玩家看到的就是一个没反应的按钮。所有数值（饲料、买入价）都从数据表读，一个都没写死。
/// </para>
/// <para>
/// <b>买入为什么在桥接层扣钱</b>：<see cref="IRanchingSystem.AddAnimal"/> 自己声明了「不收钱，
/// 扣金币是经济模块与桥接层的事」——本类正是那个同时认识牧场与钱包的地方（两者都由 <c>world/GameRoot</c> 注册）。
/// </para>
/// </remarks>
public partial class RanchPanel : Control
{
    /// <summary>占位行高（像素）。美术一律先用占位符（铁律 7）。</summary>
    private static readonly Vector2 RowMinSize = new(0f, 34f);

    /// <summary>行内前两列的固定宽度，剩下的宽度归说明列（说明列长度不定，让它自己伸缩）。</summary>
    private const float NameColumnWidth = 200f;
    private const float MoodColumnWidth = 210f;

    /// <summary>价格列的固定宽度，让买入按钮在各行对齐。</summary>
    private const float PriceColumnWidth = 90f;

    private static readonly StringName FontColorName = "font_color";

    private static readonly Color ReadyColor = new(0.62f, 0.88f, 0.66f);
    private static readonly Color BlockedColor = new(0.92f, 0.66f, 0.55f);

    private IRanchingSystem _ranching = null!;
    private IAnimalTable _animals = null!;
    private IInventory _inventory = null!;
    private IItemTable _items = null!;
    private IEconomySystem _economy = null!;

    private Label _title = null!;
    private Label _message = null!;
    private VBoxContainer _animalList = null!;
    private VBoxContainer _catalogList = null!;

    public override void _Ready()
    {
        // 显式亮出来：<see cref="PanelHost"/> 只 AddChild、从不设 Visible，面板场景里一旦带上
        // <c>visible = false</c>（<c>ui/InventoryPanel.tscn</c> 就是那么写的，它自管开关）就是一块永远不显示的板子
        Visible = true;

        _title = GetNode<Label>("Center/Content/TitleLabel");
        _message = GetNode<Label>("Center/Content/MessageLabel");
        _animalList = GetNode<VBoxContainer>("Center/Content/AnimalsScroll/AnimalList");
        _catalogList = GetNode<VBoxContainer>("Center/Content/CatalogScroll/CatalogList");

        _ranching = GameRoot.Services.Get<IRanchingSystem>();
        _animals = GameRoot.Services.Get<IAnimalTable>();
        _inventory = GameRoot.Services.Get<IInventory>();
        _items = GameRoot.Services.Get<IItemTable>();
        _economy = GameRoot.Services.Get<IEconomySystem>();

        Rebuild();
    }

    /// <summary>
    /// Esc 关面板（动作名 `close_panel`，映射在 <c>project.godot</c> 的 <c>[input]</c> 段）。
    /// </summary>
    /// <remarks>
    /// <b>先吃键、再关，顺序不能反</b>：<see cref="PanelHost.CloseAll"/> 一返回，本节点就已经不在树里，
    /// <c>GetViewport()</c> 随即是 null，反过来写就在 <c>_Input</c> 里抛 NRE——
    /// 而 <c>_Input</c> 里的异常只报日志、不崩游戏，「跑起来没事」很容易把它糊过去。
    /// </remarks>
    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed("close_panel")) return;

        GetViewport().SetInputAsHandled();
        PanelHost.Instance.CloseAll();
    }

    /// <summary>
    /// 开面板时与每次动作后整块重画。面板开着时整棵树是暂停的，数据只可能被本面板的按钮改动——
    /// 没有需要每帧盯着的量（同 <c>ui/InventoryPanel</c>）。
    /// </summary>
    private void Rebuild()
    {
        IReadOnlyList<AnimalInfo> animals = _ranching.Animals;

        _title.Text = $"牧场    动物 {animals.Count}    金币 {_economy.Gold}";

        BuildAnimalRows(animals);
        BuildCatalogRows();
    }

    private void BuildAnimalRows(IReadOnlyList<AnimalInfo> animals)
    {
        ClearRows(_animalList);

        if (animals.Count == 0)
        {
            _animalList.AddChild(Note("栏里还没有动物——从下面的目录里买一只"));
            return;
        }

        foreach (AnimalInfo animal in animals) _animalList.AddChild(AnimalRow(animal));
    }

    private Control AnimalRow(AnimalInfo animal)
    {
        AnimalDefinition definition = animal.Definition;
        string feedId = definition.FeedItemId;

        HBoxContainer row = NewRow();

        string typeLabel = string.IsNullOrWhiteSpace(animal.Name)
            ? $"{TypeName(definition.Type)}，未命名"
            : TypeName(definition.Type);

        row.AddChild(Cell($"{DisplayName(animal)}（{typeLabel}）", NameColumnWidth));
        row.AddChild(Cell($"心情 {animal.Mood}/{Ranch.MaxMood}   好感 {animal.Affection}/{Ranch.MaxAffection} 心", MoodColumnWidth));

        // 这一列永远写着「现在能不能喂、不能是为什么」：喂食失败时玩家要看得见原因，
        // 而不是对着一个没反应的按钮猜。缺什么、缺多少都按背包现算。
        string? blocked = FeedBlockReason(animal);
        row.AddChild(blocked is null
            ? Cell($"{ItemName(feedId)} 备着 {_inventory.Count(feedId)} 份", 0f, ReadyColor)
            : Cell(blocked, 0f, BlockedColor));

        var feed = new Button { Text = "喂食" };
        feed.Pressed += () => Feed(animal);
        row.AddChild(feed);

        return row;
    }

    /// <summary>喂一次。<b>按钮一律可点</b>：成不成由系统说了算，失败照样给得出原因（见 <see cref="FeedBlockReason"/>）。</summary>
    private void Feed(AnimalInfo animal)
    {
        if (_ranching.TryFeed(animal.Id))
        {
            _message.Text = $"{DisplayName(animal)} 吃了 1 份{ItemName(animal.Definition.FeedItemId)}";
        }
        else
        {
            // 失败原因与行上写的是同一份判断，不另写一套说法——两套早晚会对不上
            _message.Text = $"没喂成——{FeedBlockReason(animal) ?? "牧场拒绝了这次喂食"}";
        }

        Rebuild();
    }

    /// <summary>
    /// 现在喂不了的缘由（能喂则为 null）。判据与 <see cref="IRanchingSystem.TryFeed"/> 一致：
    /// 今天已经喂过，或背包里一份饲料都没有。饲料由类型决定（§6.5「灵兽需喂灵草」），id 在动物表里。
    /// </summary>
    private string? FeedBlockReason(AnimalInfo animal)
    {
        if (animal.FedToday) return "今天已经喂过";

        string feedId = animal.Definition.FeedItemId;

        return _inventory.Count(feedId) >= 1 ? null : $"缺{ItemName(feedId)} ×1（背包里没有）";
    }

    private void BuildCatalogRows()
    {
        ClearRows(_catalogList);

        foreach (AnimalDefinition definition in _animals.All) _catalogList.AddChild(CatalogRow(definition));
    }

    private Control CatalogRow(AnimalDefinition definition)
    {
        HBoxContainer row = NewRow();

        row.AddChild(Cell($"{definition.Name}（{TypeName(definition.Type)}）", NameColumnWidth));
        row.AddChild(Cell(
            $"饲料 {ItemName(definition.FeedItemId)}   产出 {ItemName(definition.ProduceItemId)}／{definition.ProductionIntervalDays} 天",
            MoodColumnWidth + 40f));

        // §6.5 的「购买价」列里，蓝鸡那一格写的是「稀有」而不是数字，表里因此填 0——而
        // AnimalDefinition.BuyPrice 的注释明说了：0 是「没有出处」，别读成「免费」。
        // 没有价就不挂买入按钮：编一个数出来等于替文档定价，而且 Wallet.TrySpendGold(0) 会直接抛。
        if (definition.BuyPrice <= 0)
        {
            row.AddChild(Cell("出处：稀有（表里没给价）", 0f, BlockedColor));
            return row;
        }

        row.AddChild(Cell($"{definition.BuyPrice} 金", PriceColumnWidth));

        var buy = new Button { Text = "买入" };
        buy.Pressed += () => Buy(definition);
        row.AddChild(buy);

        return row;
    }

    /// <summary>
    /// 买入一只：先扣钱、再入场。<b>价格只有 <see cref="AnimalDefinition.BuyPrice"/> 一个来源</b>，本类里没有数字价格。
    /// </summary>
    /// <remarks>
    /// 这个顺序不会留下中间态：<see cref="IEconomySystem.TrySpendGold"/> 全有或全无，扣不动就到此为止；
    /// 而 <see cref="IRanchingSystem.AddAnimal"/> 收到的是刚从动物表里取出来的 id，不会抛——
    /// 也就不会出现「钱扣了、动物没进来」。
    /// </remarks>
    private void Buy(AnimalDefinition definition)
    {
        if (!_economy.TrySpendGold(definition.BuyPrice))
        {
            _message.Text = $"金币不够——{definition.Name} 要 {definition.BuyPrice} 金，现在有 {_economy.Gold} 金";
            return;
        }

        _ranching.AddAnimal(definition.AnimalId);
        _message.Text = $"买下了 {definition.Name}（-{definition.BuyPrice} 金）";

        Rebuild();
    }

    /// <summary>
    /// 显示用的名字。空名是<b>可达状态</b>而不是异常：<see cref="IRanchingSystem.AddAnimal"/> 的 name 缺省
    /// 就是空串，而系统把空白名视作「未命名」（见 <c>Ranch.Rename</c> 的注释）。名字列空着等于这一行没名字，
    /// 故退回品种名——品种名取自动物表，不是本类编的。
    /// </summary>
    private static string DisplayName(AnimalInfo animal) =>
        string.IsNullOrWhiteSpace(animal.Name) ? animal.Definition.Name : animal.Name;

    /// <summary>类型的中文名只是显示用（§6.5 的类型列「普通 / 灵兽」）；玩法上的差别只有饲料。</summary>
    /// <remarks>用穷举的 switch 而不是三元：将来多一个类型时，这里要红，而不是默默显示成「普通」。</remarks>
    private static string TypeName(AnimalType type) => type switch
    {
        AnimalType.Normal => "普通",
        AnimalType.Spirit => "灵兽",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "动物表出现了没有中文名的类型"),
    };

    /// <summary>物品名从物品表查，不写死——饲料与产出都是数据。</summary>
    private string ItemName(string itemId) => _items.Get(itemId).Name;

    private static HBoxContainer NewRow() => new()
    {
        CustomMinimumSize = RowMinSize,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };

    /// <summary>
    /// 一行里的一格。<paramref name="width"/> 为 0 表示「吃掉剩下的宽度」。
    /// 一律 <c>ClipText</c>：文字长了不该把行撑宽，否则最长的那一行会把整列对齐拽歪。
    /// </summary>
    private static Label Cell(string text, float width, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            ClipText = true,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(width, 0f),
        };

        if (width <= 0f) label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (color is { } value) label.AddThemeColorOverride(FontColorName, value);

        return label;
    }

    private static Label Note(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static void ClearRows(VBoxContainer list)
    {
        foreach (Node child in list.GetChildren())
        {
            // 先摘、再删：QueueFree 是延迟的，只 QueueFree 的话这一帧新旧两批行会同时占位
            list.RemoveChild(child);
            child.QueueFree();
        }
    }
}
