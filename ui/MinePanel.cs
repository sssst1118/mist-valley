using System.Collections.Generic;
using Godot;
using XingGame.Systems.Combat;
using XingGame.Systems.Items;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 全屏矿洞面板（桥接层）：读 <see cref="ICombatSystem"/> → 把当前深度、本层怪物、一次遭遇的结算结果
/// 画出来（ADR-007——本类里没有一行玩法规则）。
/// </summary>
/// <remarks>
/// <b>暂停不归本面板管</b>：开合与整棵树的暂停都在 <see cref="PanelHost"/>（备案 #36）。
/// 面板自己再写一次 <c>GetTree().Paused</c>，「开 A → 开 B」那一轮会存两次、还两次，
/// 而这类计数错乱只在换面板时才显形。
/// </remarks>
public partial class MinePanel : Control
{
    /// <summary>关面板的动作名，映射在 <c>project.godot</c> 的 <c>[input]</c> 段。</summary>
    private static readonly StringName CloseAction = "close_panel";

    /// <summary>数据里 0 是「文档未给，待补」的哨兵值，照原样画成数字会被当成真数值。</summary>
    private const string NotGiven = "—";

    /// <summary>
    /// 占位挑战者：玩家的战斗属性要到 M3 灵根才有，这里取 <see cref="CombatStats"/> 能接受的
    /// <b>最小合法构造</b>（血量与攻击都必须为正，见其构造校验）。
    /// 刻意不取「像模像样」的数：编出来的和真数据长得一模一样，将来没人分得清哪些是文档定的。
    /// </summary>
    private static readonly CombatStats PlaceholderChallenger = new(1, 1);

    private Label _title = null!;
    private Label _encounterLabel = null!;
    private Label _resultLabel = null!;
    private Label _navLabel = null!;
    private VBoxContainer _monsterList = null!;
    private GridContainer _elevatorGrid = null!;
    private Button _encounterButton = null!;
    private Button _fightButton = null!;
    private Button _descendButton = null!;
    private Button _surfaceButton = null!;
    private Button _enterButton = null!;

    private ICombatSystem _combat = null!;
    private IItemTable _items = null!;

    /// <summary>「遇敌」掷出来的那只怪。换层即作废——不是这一层的怪就不该还能打。</summary>
    private MonsterDefinition? _encounter;

    /// <summary>本次开面板按了几次「遇敌」，只当作种子的增量，不参与任何玩法判断。</summary>
    private int _rolls;

    public override void _Ready()
    {
        // 面板由 PanelHost 实例化后直接 AddChild，宿主从不设 Visible——场景里若照抄
        // ui/InventoryPanel.tscn 的 visible = false（那是「常驻 HUD、自己开关」的写法），
        // 这里就会得到一块永远不显示的面板。显式点亮是第二道闸。
        Visible = true;

        _title = GetNode<Label>("Center/Content/TitleLabel");
        _monsterList = GetNode<VBoxContainer>("Center/Content/MonsterList");
        _encounterLabel = GetNode<Label>("Center/Content/EncounterLabel");
        _resultLabel = GetNode<Label>("Center/Content/ResultLabel");
        _navLabel = GetNode<Label>("Center/Content/NavLabel");
        _elevatorGrid = GetNode<GridContainer>("Center/Content/ElevatorGrid");
        _encounterButton = GetNode<Button>("Center/Content/ButtonRow/EncounterButton");
        _fightButton = GetNode<Button>("Center/Content/ButtonRow/FightButton");
        _descendButton = GetNode<Button>("Center/Content/MoveRow/DescendButton");
        _surfaceButton = GetNode<Button>("Center/Content/MoveRow/SurfaceButton");
        _enterButton = GetNode<Button>("Center/Content/MoveRow/EnterButton");

        _combat = GameRoot.Services.Get<ICombatSystem>();
        _items = GameRoot.Services.Get<IItemTable>();

        _encounterButton.Pressed += OnEncounterPressed;
        _fightButton.Pressed += OnFightPressed;
        _descendButton.Pressed += OnDescendPressed;
        _surfaceButton.Pressed += OnSurfacePressed;
        _enterButton.Pressed += OnEnterPressed;

        BuildElevatorButtons();
        Refresh();
    }

    /// <summary>
    /// Esc 关面板。<b>顺序不能反</b>：<c>CloseAll</c> 一返回本面板就已经不在树里，
    /// <c>GetViewport()</c> 随即是 null。反过来写会在 <c>_Input</c> 里抛 NRE——而 <c>_Input</c> 里的异常
    /// 只报日志、不崩游戏，「跑起来没事」很容易把它糊过去（契约「关面板：顺序反了会静默出错」）。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed(CloseAction)) return;

        GetViewport().SetInputAsHandled();
        PanelHost.Instance.CloseAll();
    }

    /// <summary>
    /// 掷一次遭遇。种子 = <see cref="GameRoot.WorldSeed"/> 加掷次：系统要求「同 (层, 种子) 必得同结果」，
    /// 所以「连按出不同的怪」靠换种子，而不是靠把随机数发生器弄得更随机。种子随存档走，
    /// 于是同一个世界里、同一层第几次掷出什么怪是一定的——面板不读时钟、不用 <c>Random.Shared</c>。
    /// </summary>
    private void OnEncounterPressed()
    {
        _encounter = _combat.RollEncounter(_combat.Progress.CurrentLayer, GameRoot.WorldSeed + _rolls++);

        _resultLabel.Text = string.Empty;
        _encounterLabel.Text = _encounter is null ? "这层很安静" : $"遇敌：{_encounter.Name}";
        _fightButton.Disabled = _encounter is null;
    }

    /// <summary>
    /// 打一场。<b>胜负都要显示</b>：败北时 <see cref="CombatResult.Loot"/> 为空是规则（掉落是击杀掉落），
    /// 面板若「没掉落就什么都不说」，玩家分不清是败了还是赢了没掉东西。
    /// </summary>
    private void OnFightPressed()
    {
        // 没有遭遇时按钮是灰的；这一行是给「将来有人把它解开」留的闸，免得拿空 id 去查表
        if (_encounter is null) return;

        CombatResult result = _combat.Resolve(_encounter.Id, PlaceholderChallenger);

        // 血量现在不画：挑战者那一侧是占位值（玩家的战斗属性要到 M3 才有），画出来只会被当成真数值
        string text = (result.Victory ? "胜利" : "败北") + $" · {result.Rounds} 回合";

        if (result.Victory)
            text += "\n掉落：" + (result.Loot.Count == 0 ? "无" : Describe(result.Loot));

        // 装不下的那部分单独说一句：Overflow 存在的唯一理由就是不被静默吞掉（见 CombatResult 的类注释）
        if (result.Overflow.Count > 0)
            text += "\n背包没装下：" + Describe(result.Overflow);

        _resultLabel.Text = text;
    }

    private void OnDescendPressed()
    {
        if (!_combat.Progress.Descend())
        {
            _navLabel.Text = "已经在最深层了";
            return;
        }

        int layer = _combat.Progress.CurrentLayer;
        Refresh();
        _navLabel.Text = $"下探到第 {layer} 层";
    }

    private void OnSurfacePressed()
    {
        if (!_combat.Progress.Leave()) return;

        Refresh();
        _navLabel.Text = "回到地面";
    }

    private void OnEnterPressed()
    {
        if (!_combat.Progress.Enter()) return;

        Refresh();
        _navLabel.Text = "下到第 1 层";
    }

    /// <summary>
    /// 电梯层各一个按钮（§7.1「每 10 层有电梯」）。<b>这是本面板唯一的「往上走」入口</b>：
    /// <see cref="MineProgress"/> 只有 <c>Enter</c> / <c>Descend</c> / <c>Leave</c> / <c>TakeElevator</c>，
    /// 没有「上一层」——逐层上行要么坐电梯，要么等系统补一个 Ascend（已报阻塞）。
    /// 层号与间隔全部从 <see cref="MineDefinition"/> 算，不写死 10。
    /// </summary>
    private void BuildElevatorButtons()
    {
        MineDefinition mine = _combat.Progress.Mine;

        for (int layer = mine.ElevatorInterval; layer <= mine.LayerCount; layer += mine.ElevatorInterval)
        {
            // 间隔不整除层数时最后几层不是电梯层，照 IsElevatorLayer 的判定来，别自己算第二遍
            if (!mine.IsElevatorLayer(layer)) continue;

            // 循环变量要过一道局部变量再进 lambda：直接捕获会被下一次迭代改掉，12 个按钮全指向最后一层
            int target = layer;
            var button = new Button { Text = $"电梯 {target}" };
            button.Pressed += () => OnElevatorPressed(target);
            _elevatorGrid.AddChild(button);
        }
    }

    private void OnElevatorPressed(int layer)
    {
        bool moved = _combat.Progress.TakeElevator(layer);

        Refresh();
        _navLabel.Text = moved ? $"乘电梯到第 {layer} 层" : $"第 {layer} 层没有电梯";
    }

    /// <summary>
    /// 按当前深度重铺内容。<b>深度只有 <see cref="MineProgress.CurrentLayer"/> 一个真相源</b>，
    /// 所以改层后整体重铺，而不是逐个控件去改——漏掉一个就是「层号变了、列表没变」。
    /// </summary>
    private void Refresh()
    {
        bool underground = _combat.Progress.IsUnderground;
        MineDefinition mine = _combat.Progress.Mine;
        int layer = _combat.Progress.CurrentLayer;

        _title.Text = underground
            ? $"{mine.Name} · 第 {layer} 层 / {mine.LayerCount}"
            : $"{mine.Name} · 在地面";

        _encounterButton.Visible = underground;
        _fightButton.Visible = underground;
        _monsterList.Visible = underground;
        _descendButton.Visible = underground;
        _surfaceButton.Visible = underground;
        _enterButton.Visible = !underground;

        // 上一层的遭遇与结算都作废，不留着让玩家在下一层打上一层的怪
        _encounter = null;
        _fightButton.Disabled = true;
        _encounterLabel.Text = string.Empty;
        _resultLabel.Text = string.Empty;
        _navLabel.Text = string.Empty;

        ClearChildren(_monsterList);

        // ForLayer 只接受 1..层数，地面（0）调它会抛；地面本来也没有「本层怪物」这回事
        if (!underground) return;

        FillMonsterList(layer);
    }

    /// <summary>
    /// 本层可能出现的怪。按 <c>ForLayer</c> 给的顺序铺，不排序——那个顺序就是掷点用的顺序，
    /// 重排一遍会让界面和表里的顺序对不上。
    /// </summary>
    private void FillMonsterList(int layer)
    {
        foreach (MonsterDefinition monster in _combat.Monsters.ForLayer(layer))
            _monsterList.AddChild(new Label { Text = DescribeMonster(monster) });
    }

    private static string DescribeMonster(MonsterDefinition monster) =>
        $"{monster.Name}    血量 {DescribeStat(monster.MaxHealth)}    攻击 {DescribeStat(monster.Attack)}";

    /// <summary>
    /// 0 画成破折号：它是「文档未给，待补」的哨兵值，不是「一滴血、零攻击」（见
    /// <see cref="MonsterDefinition"/> 的类注释）。数据一补上，这里自动显示成数字。
    /// </summary>
    private static string DescribeStat(int value) => value == 0 ? NotGiven : value.ToString();

    private string Describe(IReadOnlyList<ItemStack> stacks)
    {
        var parts = new List<string>(stacks.Count);

        foreach (ItemStack stack in stacks)
            parts.Add($"{_items.Get(stack.ItemId).Name} ×{stack.Count}");

        return string.Join("、", parts);
    }

    /// <summary>
    /// 先摘、再删：<c>QueueFree</c> 要到帧末才真删，只 QueueFree 的话这一帧里新旧标签会一起挂在容器上。
    /// </summary>
    private static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
