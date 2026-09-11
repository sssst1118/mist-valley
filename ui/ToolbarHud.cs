using System.Collections.Generic;
using Godot;
using XingGame.Systems.Items;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 工具栏 HUD（§17.1「右下：金币、灵石、当前工具、快捷栏」——金币与灵石要等 M2 的经济系统）：
/// 显示当前工具与快捷栏（背包前 9 格），高亮当前选中的那一格。除「id 换名字」外不含逻辑（ADR-007）。
/// </summary>
/// <remarks>
/// <para>
/// <b>这里每帧轮询是对的</b>（ARCHITECTURE「关于『每帧轮询』」）：背包内容与选中格随时会变，
/// 而 M1-4 的背包没有变更事件可订阅。判据同 <c>ui/InventoryHud</c>——被观察的量是连续的就别硬凑订阅。
/// </para>
/// <para>
/// 文本与高亮都<b>变了才写</b>：给 <c>Label.Text</c> 赋值或改主题覆盖都会让它重排重绘，
/// 60fps 下每秒几十次纯属无谓。
/// </para>
/// <para>
/// <b>快捷栏不是第二份物品状态</b>：它显示的就是 <see cref="IInventory.Slots"/> 的前 9 格，
/// 选中下标存在 <c>world/Player</c> 上（理由见那里的注释）。
/// </para>
/// </remarks>
public partial class ToolbarHud : Control
{
    /// <summary>占位格尺寸（像素）。美术一律先用占位符（铁律 7）。</summary>
    private static readonly Vector2 CellSize = new(118f, 38f);

    private static readonly Color SelectedCellColor = new(0.42f, 0.34f, 0.16f, 0.92f);
    private static readonly Color NormalCellColor = new(0.10f, 0.11f, 0.14f, 0.72f);
    private static readonly Color SelectedTextColor = new(1f, 0.95f, 0.78f);
    private static readonly Color NormalTextColor = new(0.82f, 0.82f, 0.86f);

    private static readonly StringName PanelStyleName = "panel";
    private static readonly StringName FontColorName = "font_color";

    /// <summary>选中下标在玩家身上，路径由场景注入（同 <c>ui/InteractPrompt</c>）。</summary>
    [Export] public NodePath PlayerPath { get; set; } = "../Player";

    private readonly List<Label> _cellLabels = new();
    private readonly List<PanelContainer> _cellPanels = new();

    /// <summary>每格上次写进去的文本，用来跳过没必要的赋值。</summary>
    private readonly List<string> _cellRendered = new();

    private StyleBoxFlat _selectedBox = null!;
    private StyleBoxFlat _normalBox = null!;

    private Label _toolLabel = null!;
    private IInventory _inventory = null!;
    private IItemTable _items = null!;
    private Player? _player;

    private string _toolRendered = string.Empty;

    /// <summary>上一次高亮的格；-1 表示还没高亮过（第一帧要把九格的样式都铺一遍）。</summary>
    private int _highlighted = -1;

    public override void _Ready()
    {
        _toolLabel = GetNode<Label>("Layout/ToolLabel");

        _inventory = GameRoot.Services.Get<IInventory>();
        _items = GameRoot.Services.Get<IItemTable>();

        // 场景被单独打开时树里没有玩家：照样画快捷栏，只是没有选中格
        _player = GetNodeOrNull<Player>(PlayerPath);

        _selectedBox = new StyleBoxFlat { BgColor = SelectedCellColor };
        _normalBox = new StyleBoxFlat { BgColor = NormalCellColor };

        BuildCells(GetNode<HBoxContainer>("Layout/SlotRow"));

        // 读档在 GameRoot._Ready 里已经跑完（autoload 先于主场景），先画一次，免得第一帧空白
        Render();
    }

    public override void _Process(double delta) => Render();

    /// <summary>
    /// 格子按快捷栏容量建。不做「取实际槽位数再取小」的兜底：背包 24 格是构造时定的，
    /// 而快捷栏 9 格是设计文档 §17.1 定的，两者不可能反超——为不可能发生的配置写分支只是噪声。
    /// </summary>
    private void BuildCells(HBoxContainer row)
    {
        for (int slot = 0; slot < Player.HotbarSlotCount; slot++)
        {
            var label = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // 名字过长时裁掉，而不是把格子撑大——否则整排的宽度会被最长的那个名字拽歪
                ClipText = true,
            };

            var cell = new PanelContainer { CustomMinimumSize = CellSize };
            cell.AddThemeStyleboxOverride(PanelStyleName, _normalBox);
            cell.AddChild(label);
            row.AddChild(cell);

            _cellLabels.Add(label);
            _cellPanels.Add(cell);
            _cellRendered.Add(string.Empty);
        }
    }

    private void Render()
    {
        for (int slot = 0; slot < _cellLabels.Count; slot++)
        {
            string text = SlotText(slot);
            if (!string.Equals(text, _cellRendered[slot]))
            {
                _cellRendered[slot] = text;
                _cellLabels[slot].Text = text;
            }
        }

        RenderHighlight();

        string tool = $"当前工具：{_player?.SelectedItem?.Name ?? "空手"}";
        if (string.Equals(tool, _toolRendered)) return;

        _toolRendered = tool;
        _toolLabel.Text = tool;
    }

    private string SlotText(int slot)
    {
        ItemStack stack = _inventory.Slots[slot];

        // 空格留白：格子本身已经说明「这里有一格」，再写个「空」字只是噪声（同 ui/InventoryPanel）
        return stack.Count == 0 ? string.Empty : $"{slot + 1} {_items.Get(stack.ItemId).Name}×{stack.Count}";
    }

    /// <summary>选中格只在真的换了格才重铺样式：九格 × 两处主题覆盖，没必要每帧来一遍。</summary>
    private void RenderHighlight()
    {
        int selected = _player?.SelectedHotbarSlot ?? -1;
        if (selected == _highlighted) return;

        _highlighted = selected;

        for (int slot = 0; slot < _cellPanels.Count; slot++)
        {
            bool isSelected = slot == selected;

            _cellPanels[slot].AddThemeStyleboxOverride(PanelStyleName, isSelected ? _selectedBox : _normalBox);
            _cellLabels[slot].AddThemeColorOverride(FontColorName, isSelected ? SelectedTextColor : NormalTextColor);
        }
    }
}
