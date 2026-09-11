using System.Collections.Generic;
using Godot;
using XingGame.Core.Time;
using XingGame.Systems.Items;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 全屏背包面板（桥接层）：读输入 → 调 <see cref="IInventory"/> → 把槽位画成网格（ADR-007）。
/// 打开时暂停游戏、关闭时恢复。格数与内容全部来自背包，本类不持有任何物品知识。
/// </summary>
/// <remarks>
/// <b>「吃掉输入」为什么选整棵树暂停，而不是停掉玩家节点</b>：玩家是<b>轮询</b>输入的
/// （<c>world/Player.cs</c> 在 <c>_PhysicsProcess</c> 里读 <c>Input.IsActionPressed</c>），
/// 所以只把玩家节点停掉是不够的——<c>ui/InteractPrompt</c> 同样每帧轮询，面板压着屏幕的时候
/// 按 E 照样会把灵田开垦掉。逐个节点停是打地鼠：往后每加一个游戏内节点都要回来补一处，
/// 漏掉一个就是「面板开着还能操作世界」。暂停整棵树只要一句，且此后新增的节点自动被覆盖。
/// <para>
/// 代价是常驻 HUD 也一起停住——<b>可接受</b>：暂停期间背包不可能变化，冻住的摘要与实时的等价。
/// 面板自己必须 <c>process_mode = Always</c>（见 <c>InventoryPanel.tscn</c>），
/// 否则它连关闭自己的那一次按键都收不到。
/// </para>
/// </remarks>
public partial class InventoryPanel : Control
{
    /// <summary>开关动作名，映射在 <c>project.godot</c> 的 <c>[input]</c> 段。</summary>
    private static readonly StringName ToggleAction = "toggle_inventory";

    /// <summary>占位格尺寸（像素）。美术一律先用占位符（铁律 7）。</summary>
    private static readonly Vector2 CellSize = new(160f, 44f);

    /// <summary>槽位 → 格子的 Label，下标与 <see cref="IInventory.Slots"/> 一一对应。</summary>
    private readonly List<Label> _cells = new();

    private Label _title = null!;
    private IInventory _inventory = null!;
    private IItemTable _items = null!;
    private ITimeService _time = null!;

    // 打开前的暂停状态。关闭时「恢复原状」而不是一律置 false：将来若是别的东西先把时间停了
    // （过场、读档），关一次背包不该顺手把它解开
    private bool _treePausedBefore;
    private bool _timePausedBefore;

    public override void _Ready()
    {
        _title = GetNode<Label>("Center/Content/TitleLabel");

        _inventory = GameRoot.Services.Get<IInventory>();
        _items = GameRoot.Services.Get<IItemTable>();
        _time = GameRoot.Services.Get<ITimeService>();

        BuildGrid();
    }

    /// <summary>
    /// 按键是离散量，用 <c>_Input</c> 接（ARCHITECTURE「关于『每帧轮询』」）——
    /// 这里不该像 <c>InventoryHud</c> 那样每帧问一次。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed(ToggleAction)) return;

        SetOpen(!Visible);

        // 这一次按键已经用掉了。面板是全屏的，放它继续走会被 GUI 与 _UnhandledInput 那两趟再看见一次
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// 格子按 <see cref="IInventory.SlotCount"/> 建，不写死 24：容量是背包的属性，
    /// 写死就得两处一起改，而漏掉的那处只在容量变化时才暴露。
    /// </summary>
    private void BuildGrid()
    {
        var grid = GetNode<GridContainer>("Center/Content/SlotGrid");

        for (int index = 0; index < _inventory.SlotCount; index++)
        {
            var label = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // 名字过长时裁掉，而不是把格子撑大——否则整片网格的列宽会被最长的那个名字拽歪
                ClipText = true,
            };

            var cell = new PanelContainer { CustomMinimumSize = CellSize };
            cell.AddChild(label);
            grid.AddChild(cell);

            _cells.Add(label);
        }
    }

    private void SetOpen(bool open)
    {
        if (open)
        {
            Render();

            _treePausedBefore = GetTree().Paused;
            _timePausedBefore = _time.IsPaused;

            _time.IsPaused = true;
            GetTree().Paused = true;
        }
        else
        {
            _time.IsPaused = _timePausedBefore;
            GetTree().Paused = _treePausedBefore;
        }

        Visible = open;
    }

    /// <summary>
    /// 只在开合时刷一次：打开期间游戏是暂停的，背包不可能变——没有需要每帧盯着的量。
    /// </summary>
    private void Render()
    {
        int filled = 0;

        for (int index = 0; index < _cells.Count; index++)
        {
            ItemStack slot = _inventory.Slots[index];

            if (slot.Count == 0)
            {
                // 空槽留白：边框已经说明「这里有一格」，再写个「空」字只是噪声
                _cells[index].Text = string.Empty;
                continue;
            }

            _cells[index].Text = $"{_items.Get(slot.ItemId).Name} ×{slot.Count}";
            filled++;
        }

        _title.Text = $"背包  {filled}/{_inventory.SlotCount}";
    }
}
