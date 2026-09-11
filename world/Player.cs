using Godot;
using XingGame.Core.Time;
using XingGame.Systems.Buffs;
using XingGame.Systems.Farming;
using XingGame.Systems.Items;
using XingGame.Systems.Player;

namespace XingGame.World;

/// <summary>
/// 玩家节点（桥接层）。只做三件事：读输入 → 调 <see cref="PlayerMotor"/> / <see cref="FarmingSystem"/> →
/// 把结果喂给引擎（ADR-007）。位置由引擎物理持有，纯 C# 区不掺和（ADR-011）——碰撞与滑动只有引擎知道结果。
/// </summary>
public partial class Player : CharacterBody2D
{
    /// <summary>占位外观边长（像素）。美术一律先用占位符（铁律 7）。</summary>
    private const float PlaceholderSize = 14f;

    private static readonly Color PlaceholderColor = new(0.42f, 0.72f, 0.94f);

    /// <summary>快捷栏容量。§17.1 的快捷栏就是背包的前 9 格，不做第二份物品状态（同 ADR-009 的理由）。</summary>
    public const int HotbarSlotCount = 9;

    private static readonly StringName UseToolAction = "use_tool";

    private static readonly StringName[] HotbarActions =
    {
        "hotbar_1", "hotbar_2", "hotbar_3", "hotbar_4", "hotbar_5",
        "hotbar_6", "hotbar_7", "hotbar_8", "hotbar_9",
    };

    private PlayerMotor _motor = null!;
    private FarmingSystem _farming = null!;
    private IInventory _inventory = null!;
    private IItemTable _items = null!;

    /// <summary>移速增益（§8.2 的轻身术）。读它要搭配下面的游戏时刻——增益是按时刻到期的。</summary>
    private IBuffSystem _buffs = null!;

    /// <summary>取「现在几点」只为一件事：问增益系统此刻的移速修正。</summary>
    private ITimeService _time = null!;

    /// <summary>当前选中的快捷栏格（0..8），指向 <see cref="IInventory.Slots"/> 的前 9 格。</summary>
    /// <remarks>
    /// 选中下标存在玩家节点上，理由：它是「玩家手上拿着什么」，是玩家的状态。
    /// <b>不进 <see cref="IInventory"/></b>——那是物品状态，塞一个纯 UI 概念进去，会让 ADR-012 的定长槽位数组
    /// 多出一个与物品无关的字段；<b>也不进 <see cref="FarmingSystem"/></b>——种植规则不认识「谁被选中」，
    /// 它只认识「你递给我一件什么物品」。工具栏（<c>ui/ToolbarHud</c>）按 NodePath 读这里，
    /// 与 <c>ui/InteractPrompt</c> 读玩家位置是同一个做法。
    /// </remarks>
    public int SelectedHotbarSlot { get; private set; }

    /// <summary>手上拿的物品；选中的格是空格时为 null（空手）。</summary>
    public ItemDefinition? SelectedItem
    {
        get
        {
            ItemStack slot = _inventory.Slots[SelectedHotbarSlot];
            return slot.Count == 0 ? null : _items.Get(slot.ItemId);
        }
    }

    /// <summary>使用键作用的那一格：面朝方向相邻的一格（ARCHITECTURE「目标格怎么算」）。</summary>
    /// <remarks>
    /// 用朝向而不是脚下：格子只有 16 像素，要站在作物上才能收它，就等于让玩家看不见自己踩了什么。
    /// 公式只写这一份——<c>world/FarmView</c> 的目标格高亮也读它，两处各算一次迟早在其中一处先改。
    /// <para>
    /// 开局没走动过时 <see cref="PlayerMotor.Facing"/> 是零向量，目标格就是脚下那一格：
    /// 这不是特例分支，是「还没朝任何方向走过」的自然结果。
    /// </para>
    /// </remarks>
    public TileCoord TargetTile => FarmGrid.ToTile(ToPure(GlobalPosition) + _motor.Facing * FarmGrid.TileSize);

    public override void _Ready()
    {
        _motor = new PlayerMotor(PlayerConfig.LoadDefault().MoveSpeed);

        // 耕地相关的服务都由组合根构造并注册（ADR-007）
        _farming = GameRoot.Services.Get<FarmingSystem>();
        _inventory = GameRoot.Services.Get<IInventory>();
        _items = GameRoot.Services.Get<IItemTable>();
        _buffs = GameRoot.Services.Get<IBuffSystem>();
        _time = GameRoot.Services.Get<ITimeService>();
    }

    public override void _PhysicsProcess(double delta)
    {
        // 移速修正每帧现问：增益会到期，缓存下来就会在到期之后还快着一整段路。
        // 纯 C# 的 PlayerMotor 不认识游戏时刻（ADR-002），所以由桥上问、桥上喂（同 UseOn 收「手上拿的什么」）
        _motor.Step(ReadInput(), (float)_buffs.MultiplierFor(BuffTarget.MoveSpeed, _time.Now));

        // System.Numerics.Vector2 → Godot 的 Vector2 的转换只在边界处发生（ADR-010）
        System.Numerics.Vector2 velocity = _motor.Velocity;
        Velocity = new Vector2(velocity.X, velocity.Y);

        MoveAndSlide();
    }

    /// <summary>
    /// 使用键与快捷栏数字键。走 <c>_UnhandledInput</c> 而不是在 <c>_PhysicsProcess</c> 里判按下状态：
    /// 后者每帧都要问一次「按了没」，而按键是离散事件。按住不放也不会连发——
    /// <c>IsActionPressed</c> 默认不认键盘重复（echo）事件。
    /// </summary>
    /// <remarks>
    /// 玩家打开背包面板时整棵树被暂停（备案 #36），本节点的 <c>_UnhandledInput</c> 随之不再被调用：
    /// 面板压着屏幕时按空格不会把地翻开。
    /// </remarks>
    public override void _UnhandledInput(InputEvent @event)
    {
        for (int slot = 0; slot < HotbarActions.Length; slot++)
        {
            if (!@event.IsActionPressed(HotbarActions[slot])) continue;

            SelectedHotbarSlot = slot;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!@event.IsActionPressed(UseToolAction)) return;

        // 规则全在 FarmingSystem.UseOn 里（ADR-007）：桥上不判断手上是什么工具，
        // 也不判断这一格能不能开垦——那些是种植领域的事，写在这里就逃过了编译器的看管。
        TileCoord tile = TargetTile;
        ItemDefinition? selected = SelectedItem;
        bool used = _farming.UseOn(tile, selected);

        GD.Print($"[种植] ({tile.X}, {tile.Y}) 用「{selected?.Name ?? "空手"}」→ {(used ? "成功了" : "没有变化")}");

        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// 取各轴的原始按下状态，而不是 <c>Input.GetVector</c>——后者会顺手把长度钳到 1，
    /// 归一化就有一半发生在引擎里了。ADR-011 说归一化归纯 C# 的 <see cref="PlayerMotor"/> 管，
    /// 桥上只负责把原始轴读出来。
    /// </summary>
    private static System.Numerics.Vector2 ReadInput()
    {
        float x = (Input.IsActionPressed("move_right") ? 1f : 0f) - (Input.IsActionPressed("move_left") ? 1f : 0f);
        float y = (Input.IsActionPressed("move_down") ? 1f : 0f) - (Input.IsActionPressed("move_up") ? 1f : 0f);

        return new System.Numerics.Vector2(x, y);
    }

    /// <summary>Godot 的 <c>Vector2</c> → <c>System.Numerics.Vector2</c>，只在边界处转换（ADR-010）。</summary>
    private static System.Numerics.Vector2 ToPure(Vector2 value) => new(value.X, value.Y);

    /// <summary>占位方块。外观不随朝向变化，故只画一次，不逐帧 QueueRedraw。</summary>
    public override void _Draw() => DrawRect(new Rect2(-PlaceholderSize / 2f, -PlaceholderSize / 2f, PlaceholderSize, PlaceholderSize), PlaceholderColor);
}
