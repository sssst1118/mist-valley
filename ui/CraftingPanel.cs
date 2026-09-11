using System.Collections.Generic;
using Godot;
using XingGame.Systems.Crafting;
using XingGame.Systems.Items;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 全屏加工 / 烹饪面板（桥接层）：读配方表 → 调 <see cref="ICraftingSystem"/> → 把配方画成行（ADR-007）。
/// 「材料够不够」「能不能做」「锁没锁」全部由系统回答，本类只负责把答案摆出来。
/// </summary>
/// <remarks>
/// <b>暂停不归这里管</b>：开关面板要暂停整棵树，而暂停只在 <c>ui/PanelHost</c> 一处记账。
/// 面板里再写一次 <c>GetTree().Paused</c>，「开 A → 开 B」那一轮就会存两次、还两次——
/// 而这类计数错乱只在换面板时才显形（见 ARCHITECTURE 的 M2-C 契约）。
/// 面板侧只有一条义务：<c>process_mode = Always</c>（见 <c>CraftingPanel.tscn</c>），否则连关自己的那次按键都收不到。
/// </remarks>
public partial class CraftingPanel : Control
{
    /// <summary>关面板的动作名，映射在 <c>project.godot</c> 的 <c>[input]</c> 段。</summary>
    private static readonly StringName CloseAction = "close_panel";

    /// <summary>占位行高（像素）。美术一律先用占位符（铁律 7）。</summary>
    private const float RowMinHeight = 36f;

    /// <summary>标题文案。配置项走 <c>[Export]</c> + 场景里填值——<c>PanelHost</c> 实例化面板时不传任何参数。</summary>
    [Export] public string PanelTitle { get; set; } = "加工与烹饪";

    /// <summary>临时解锁入口的开关，理由见 <see cref="BuildRow"/>。</summary>
    [Export] public bool ShowTemporaryUnlock { get; set; } = true;

    private VBoxContainer _list = null!;
    private Label _title = null!;
    private Label _result = null!;

    private IRecipeTable _recipes = null!;
    private IItemTable _items = null!;
    private ICraftingSystem _crafting = null!;

    public override void _Ready()
    {
        _title = GetNode<Label>("Center/Content/TitleLabel");
        _list = GetNode<VBoxContainer>("Center/Content/RecipeScroll/RecipeList");
        _result = GetNode<Label>("Center/Content/ResultLabel");

        _recipes = GameRoot.Services.Get<IRecipeTable>();
        _items = GameRoot.Services.Get<IItemTable>();
        _crafting = GameRoot.Services.Get<ICraftingSystem>();

        _title.Text = PanelTitle;
        Rebuild();
    }

    /// <summary>
    /// 按键是离散量，用 <c>_Input</c> 接（同 <c>ui/InventoryPanel</c>）。
    /// </summary>
    /// <remarks>
    /// <b>先吃掉按键、再关面板，顺序不能反</b>：<c>CloseAll</c> 一返回本节点就已经不在树里，
    /// <c>GetViewport()</c> / <c>GetTree()</c> 随即是 null。反过来写会在 <c>_Input</c> 里抛 NRE——
    /// 而 <c>_Input</c> 里的异常只报日志、不崩游戏，「跑起来没事」很容易把它糊过去。
    /// </remarks>
    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed(CloseAction)) return;

        GetViewport().SetInputAsHandled();
        PanelHost.Instance.CloseAll();
    }

    /// <summary>
    /// 整列重建。面板每次打开都重新实例化（<c>PanelHost</c> 的约定），这里再刷的只有「刚点了按钮」这一次，
    /// 需要重建的控件屈指可数，按行增量更新换不来这份复杂度。
    /// </summary>
    private void Rebuild()
    {
        // RemoveChild 必须和 QueueFree 一起：QueueFree 要到帧末才真删，留在容器里的话
        // 旧行这一帧仍占着位置，新行会叠在旧行上。
        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        foreach (RecipeDefinition recipe in _recipes.All) _list.AddChild(BuildRow(recipe));
    }

    private Control BuildRow(RecipeDefinition recipe)
    {
        // CanCraft 自己就含「已解锁」这一条（接口约定：配方表里有、已解锁、材料够、背包放得下，四条全中才算能），
        // 所以下面不再单独判一次解锁——两处各判一次，改了一处就会不一致。
        bool unlocked = _crafting.IsUnlocked(recipe.Id);
        bool canCraft = _crafting.CanCraft(recipe.Id);
        ItemDefinition output = _items.Get(recipe.OutputItemId);

        var row = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0f, RowMinHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 12);

        // 产物：配方没有自己的名字，显示名一律走物品表（RecipeDefinition 的约定）
        row.AddChild(MakeLabel($"{output.Name} ×{recipe.OutputCount}", 200f, expand: false));

        // 材料：显示名同样从物品表拿
        var ingredients = new List<string>();
        foreach (RecipeIngredient ingredient in recipe.Ingredients)
            ingredients.Add($"{_items.Get(ingredient.ItemId).Name} ×{ingredient.Count}");

        row.AddChild(MakeLabel(string.Join("、", ingredients), 0f, expand: true));

        row.AddChild(MakeLabel(StateText(unlocked, canCraft), 100f, expand: false));

        // ⚠️ 临时入口：M2 还没有任何解锁来源（§12.3 的解锁途径是技能升级、任务奖励、商店购买、探索发现，
        // 分属 M3/M5/M6），没有这个按钮，整个面板的「制作」永远是灰的、等于空壳。
        // **解锁系统做出来时，连同这个按钮、它的处理函数与 ShowTemporaryUnlock 属性一起删掉。**
        if (ShowTemporaryUnlock)
        {
            var unlockButton = new Button { Text = "解锁(临时)", Disabled = unlocked };
            unlockButton.Pressed += () => Unlock(recipe);
            row.AddChild(unlockButton);
        }

        // 不可点时用 Disabled 而不是「点了再报错」：按钮灰着是玩家一眼能懂的状态，
        // 点下去弹一句失败则要靠读文字才知道发生了什么。
        var craftButton = new Button { Text = "制作", Disabled = !canCraft };
        craftButton.Pressed += () => Craft(recipe);
        row.AddChild(craftButton);

        return row;
    }

    /// <summary>
    /// CanCraft 把「材料不够」与「背包放不下」并成了一条 <c>false</c>，接口没有更细的出口，
    /// 所以这里不猜是哪一个——报「不可制作」而不是「材料不足」，才不会指着材料说错话。
    /// </summary>
    private static string StateText(bool unlocked, bool canCraft) => (unlocked, canCraft) switch
    {
        (false, _)     => "未解锁",
        (_, false)     => "不可制作",
        _              => "可制作",
    };

    private void Craft(RecipeDefinition recipe)
    {
        ItemDefinition output = _items.Get(recipe.OutputItemId);
        bool crafted = _crafting.TryCraft(recipe.Id);

        Rebuild();

        // 成功和失败都要写：只写成功的话，「点了没反应」在玩家眼里就是卡住了。
        _result.Text = crafted
            ? $"制作成功：{output.Name} ×{recipe.OutputCount}"
            : $"制作失败：{output.Name}（{FailureReason(recipe.Id)}）";
    }

    private void Unlock(RecipeDefinition recipe)
    {
        ItemDefinition output = _items.Get(recipe.OutputItemId);

        // 重复解锁返回 false 是接口约定的正常结果（存档往返、任务重跑都会再来一次），不是错误
        bool newlyUnlocked = _crafting.Unlock(recipe.Id);

        Rebuild();

        _result.Text = newlyUnlocked
            ? $"已解锁：{output.Name}"
            : $"{output.Name} 本来就是解锁的";
    }

    /// <summary>
    /// <c>TryCraft</c> 只回一个 <c>bool</c>，说不出为什么失败。这里也只报系统已经给出的事实，
    /// 不替它推断原因。
    /// </summary>
    private string FailureReason(string recipeId) =>
        _crafting.IsUnlocked(recipeId) ? "材料或背包空间不足" : "配方未解锁";

    private static Label MakeLabel(string text, float minWidth, bool expand)
    {
        var label = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            // 名字过长时裁掉，而不是把整行撑宽——否则列宽会被最长的那条拽歪
            ClipText = true,
            CustomMinimumSize = new Vector2(minWidth, 0f),
        };

        if (expand) label.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        return label;
    }
}
