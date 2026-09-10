using Godot;
using XingGame.Systems.Interaction;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 交互提示（桥接层）：读输入 → 调 <see cref="IInteractionSystem"/> → 把结果画成一行文案（ADR-007）。
/// 有目标就显示它的 <see cref="IInteractable.Prompt"/>，够得着但条件不满足时变灰，附近没东西就隐藏。
/// </summary>
/// <remarks>
/// <b>这里每帧轮询是对的</b>（ARCHITECTURE「关于『每帧轮询』」）：玩家位置是连续量，
/// 「附近有没有东西」每一帧都可能不同，没有对应的事件可订阅。与 <c>TimeHud</c> 订阅事件并不矛盾——
/// 判断标准是被观察的量是离散还是连续，时间是整点才变的离散量。
/// </remarks>
public partial class InteractPrompt : Control
{
    /// <summary>可用 / 暂时不可用两种样式。变灰是给玩家的信号：这东西在，但现在用不了。</summary>
    private static readonly Color AvailableColor = new(1f, 0.94f, 0.75f);

    private static readonly Color UnavailableColor = new(0.55f, 0.55f, 0.58f);

    /// <summary>玩家节点路径，由场景注入。提示要跟着它走，所以它必须找得到。</summary>
    [Export] public NodePath PlayerPath { get; set; } = "../../Player";

    private Label _label = null!;
    private Node2D? _player;
    private IInteractionSystem _interactions = null!;

    public override void _Ready()
    {
        _label = GetNode<Label>("PromptLabel");

        // 与 world/Interactable 共用同一个实例——由组合根构造并注册（ADR-007）
        _interactions = GameRoot.Services.Get<IInteractionSystem>();

        // 场景被单独打开时树里没有玩家。静静不显示，好过每帧刷 NRE
        _player = GetNodeOrNull<Node2D>(PlayerPath);
        if (_player is null) _label.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_player is null) return;

        Vector2 playerPosition = _player.GlobalPosition;

        // 「按 E」与「显示提示」查的是同一个最近目标，否则会出现提示写着 A、按下去动的却是 B
        if (Input.IsActionJustPressed("interact")) _interactions.TryInteract(ToPure(playerPosition));

        Render(_interactions.FindNearest(ToPure(playerPosition)));
    }

    private void Render(IInteractable? target)
    {
        if (target is null)
        {
            _label.Visible = false;
            return;
        }

        _label.Visible = true;
        _label.Text = $"[E] {target.Prompt}";
        _label.AddThemeColorOverride("font_color", target.CanInteract ? AvailableColor : UnavailableColor);
    }

    /// <summary>Godot 的 <c>Vector2</c> → <c>System.Numerics.Vector2</c>，只在边界处转换（ADR-010）。</summary>
    private static System.Numerics.Vector2 ToPure(Vector2 position) => new(position.X, position.Y);
}
