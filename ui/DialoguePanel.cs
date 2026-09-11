using System;
using System.Collections.Generic;
using Godot;
using XingGame.Systems.Items;
using XingGame.Systems.Npc;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 全屏 NPC 面板（桥接层）：画出这位 NPC 的名字与好感度，并从背包里挑一件物品送给他（ADR-007）。
/// 好感档位、心数换算、送礼点数全部由 <c>systems/npc/</c> 决定，本类只负责把它们摆到屏幕上。
/// </summary>
/// <remarks>
/// <para>
/// <b>本面板一根手指都不碰 <c>GetTree().Paused</c></b>：暂停归 <see cref="PanelHost"/>。面板再设一次的话，
/// 「开 A → 开 B」那一轮会存两次、还两次，而这类计数错乱只在换面板时才显形（ARCHITECTURE M2-C）。
/// </para>
/// <para>
/// <b>不订阅任何事件</b>：面板打开期间整棵树是暂停的，好感度与背包都不会变——除非本面板自己送礼，
/// 那条路径显式刷一次就够了。真订阅了反而要在 <c>_ExitTree</c> 里退订。
/// 按钮的 <c>Pressed</c> 不算事件订阅：按钮是面板的子节点，随面板一起销毁，连接不会活过宿主。
/// </para>
/// </remarks>
public partial class DialoguePanel : Control
{
    /// <summary>关闭动作名，映射在 <c>project.godot</c> 的 <c>[input]</c> 段。</summary>
    private static readonly StringName CloseAction = "close_panel";

    /// <summary>
    /// 这位面板说话的 NPC。**每位 NPC 一个面板实例**：主会话在 <c>world/Main.tscn</c> 里摆一个
    /// <c>NpcSpot</c> 就换一个填了 id 的面板场景。id 全部来自 <c>data/npcs/npcs.json</c>。
    /// </summary>
    [Export] public string NpcId { get; set; } = "npc_mayor";

    /// <summary>礼物格：物品 id 与它那一格按钮，下标即摆放顺序。</summary>
    private readonly List<(string ItemId, Button Button)> _giftButtons = new();

    private Label _nameLabel = null!;
    private Label _statsLabel = null!;
    private GridContainer _giftList = null!;
    private Label _emptyHint = null!;
    private Button _giveButton = null!;
    private Label _resultLabel = null!;

    private INpcTable _npcs = null!;
    private IFriendshipSystem _friendship = null!;
    private IInventory _inventory = null!;
    private IItemTable _items = null!;

    /// <summary>挑中的物品 id，null 表示还没挑。**按 id 而不是槽位下标**：同一个物品可能占着两格堆叠。</summary>
    private string? _selected;

    public override void _Ready()
    {
        // PanelHost 只做 AddChild，从不把面板设为可见——场景一旦带上 visible = false（照抄
        // ui/InventoryPanel.tscn 就会），面板会永远不显示，而树已经被暂停，看上去就是「卡死」。
        // 这里兜一道：本类只在被宿主实例化打开时活着，从来没有「该隐藏」的时刻。
        Visible = true;

        _nameLabel = GetNode<Label>("Center/Content/NameLabel");
        _statsLabel = GetNode<Label>("Center/Content/StatsLabel");
        _giftList = GetNode<GridContainer>("Center/Content/GiftList");
        _emptyHint = GetNode<Label>("Center/Content/EmptyHintLabel");
        _giveButton = GetNode<Button>("Center/Content/GiveButton");
        _resultLabel = GetNode<Label>("Center/Content/ResultLabel");

        _npcs = GameRoot.Services.Get<INpcTable>();
        _friendship = GameRoot.Services.Get<IFriendshipSystem>();
        _inventory = GameRoot.Services.Get<IInventory>();
        _items = GameRoot.Services.Get<IItemTable>();

        // 认不出的 NpcId 是装配错误（.tscn 里填错了 id），照 INpcTable.Get 的契约定当场抛：
        // 静默开一个没有姓名的面板，只会让人对着「没有 NPC 的空窗」猜哪里配错了
        NpcDefinition npc = _npcs.Get(NpcId);
        _nameLabel.Text = $"{npc.Name}（{npc.Role}）";

        _giveButton.Pressed += GiveSelected;

        RenderStats();
        RebuildGiftList();
    }

    /// <summary>
    /// 按键是离散量，用 <c>_Input</c> 接（同 <c>ui/InventoryPanel</c>）——这里不该像 HUD 那样每帧问一次。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed(CloseAction)) return;

        // 顺序不能反：CloseAll 返回时本面板已经不在树里，GetViewport() 随即是 null，
        // 反过来写会在 _Input 里抛 NRE——而 _Input 里的异常只报日志、不崩游戏，很容易被糊过去
        GetViewport().SetInputAsHandled();
        PanelHost.Instance.CloseAll();
    }

    /// <summary>好感度三件套都问 <see cref="IFriendshipSystem"/>，本类不自己按点数算心数或档位。</summary>
    private void RenderStats()
    {
        _statsLabel.Text = $"好感 {_friendship.GetPoints(NpcId)} 点 · " +
                           $"{_friendship.GetHearts(NpcId)} 心 · " +
                           LevelName(_friendship.GetLevel(NpcId));
    }

    /// <summary>§10.4 的四个档在文档里一律写作「陌生 / 友好 / 亲密 / 挚爱」。</summary>
    private static string LevelName(FriendshipLevel level) => level switch
    {
        FriendshipLevel.Stranger => "陌生",
        FriendshipLevel.Friendly => "友好",
        FriendshipLevel.Close => "亲密",
        FriendshipLevel.Beloved => "挚爱",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "未知好感度阶段"),
    };

    /// <summary>
    /// 礼物格按背包当前内容重建。只列非空格——空格在这里不是信息（与背包面板不同，ADR-012 管的是背包）；
    /// 同一个物品只出一个按钮，数量取跨堆叠的总数，否则「萝卜 ×99」会出现两次而玩家分不清差别。
    /// </summary>
    private void RebuildGiftList()
    {
        foreach (Node child in _giftList.GetChildren())
        {
            // 先摘再删（同 PanelHost）：QueueFree 是延迟的，留在容器里会让这一帧多出一份旧内容
            _giftList.RemoveChild(child);
            child.QueueFree();
        }

        _giftButtons.Clear();

        var listed = new List<string>();

        for (int index = 0; index < _inventory.SlotCount; index++)
        {
            ItemStack slot = _inventory.Slots[index];

            if (slot.Count == 0 || listed.Contains(slot.ItemId)) continue;

            listed.Add(slot.ItemId);

            string itemId = slot.ItemId;
            var button = new Button();
            button.Pressed += () => Select(itemId);

            _giftList.AddChild(button);
            _giftButtons.Add((itemId, button));
        }

        _emptyHint.Visible = _giftButtons.Count == 0;

        // 送完了或背包变了，手上那件就不再是「选中」——留着会让送出按钮指向一个背包里没有的 id
        if (_selected is not null && !listed.Contains(_selected)) _selected = null;

        RenderSelection();
    }

    private void Select(string itemId)
    {
        _selected = itemId;
        RenderSelection();
    }

    /// <summary>
    /// 选中态只改按钮文字，**不重建列表**：本方法就在按钮自己的 <c>Pressed</c> 回调里跑，
    /// 这时候把这个按钮从树里摘掉是在信号派发途中动派发者。
    /// </summary>
    private void RenderSelection()
    {
        foreach ((string itemId, Button button) in _giftButtons) button.Text = GiftLabel(itemId);

        // 没挑东西时「送出」按不动，比按下去什么都不发生更好懂
        _giveButton.Disabled = _selected is null;
    }

    private string GiftLabel(string itemId) =>
        $"{(_selected == itemId ? "▶ " : string.Empty)}{_items.Get(itemId).Name} ×{_inventory.Count(itemId)}";

    /// <summary>
    /// 送礼：先扣物品、再算好感。
    /// <b>顺序由「失败的方向」决定</b>——反过来的话，扣不动时玩家白拿一次好感；先扣的话最坏只是
    /// 东西没了没加上，而那只可能来自「选中之后背包被别处改了」，暂停中不存在这样的别处。
    /// </summary>
    private void GiveSelected()
    {
        if (_selected is null) return;

        string itemId = _selected;
        string itemName = _items.Get(itemId).Name;

        if (!_inventory.Remove(itemId, 1))
        {
            _resultLabel.Text = $"背包里已经没有 {itemName} 了";
            RebuildGiftList();
            return;
        }

        // 生日判定在 systems/ 里没有落脚点（NpcDefinition.Birthday 与 ITimeService.Now 都取得到，
        // 但「今天是不是他生日」是玩法规则，写在这里就是桥接层做判断，ADR-007）。先按非生日送，
        // ×8 的规则本身仍在 ReceiveGift 里。见回报的阻塞。
        int delta = _friendship.ReceiveGift(NpcId, itemName, isBirthday: false);

        _selected = null;
        RenderStats();
        RebuildGiftList();
        _resultLabel.Text = $"送出 {itemName}：好感 {(delta > 0 ? "+" : string.Empty)}{delta}";
    }
}
