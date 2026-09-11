using Godot;
using XingGame.Systems.Interaction;
using XingGame.Ui;

namespace XingGame.World;

/// <summary>
/// 可交互物（桥接层）。只做三件事：进树时向 <see cref="IInteractionSystem"/> 登记、离树时退订、
/// 把 <see cref="IInteractable"/> 的成员映射到自己的节点状态（ADR-007）。
/// 具体玩法（种植、采集、开门）归各自的系统，本切片只把链路接通（铁律 3）。
/// </summary>
public partial class Interactable : Node2D, IInteractable
{
    /// <summary>占位外观边长（像素）。美术一律先用占位符（铁律 7）。</summary>
    private const float PlaceholderSize = 20f;

    private static readonly Color IdleColor = new(0.85f, 0.72f, 0.42f);
    private static readonly Color UsedColor = new(0.45f, 0.82f, 0.55f);

    /// <summary>可交互范围圈的淡色。它是画给开发看的调试辅助，上线换成真美术时一起消失。</summary>
    private static readonly Color RangeColor = new(1f, 1f, 1f, 0.12f);

    /// <summary>可交互半径（像素）。每个目标自带一个，不做全局统一半径。</summary>
    [Export] public float Radius { get; set; } = 60f;

    /// <summary>为假时仍会被找到（提示会变灰），但按 E 不会发生任何事。</summary>
    [Export] public bool CanInteract { get; set; } = true;

    /// <summary>提示文案，如「浇水」。</summary>
    [Export] public string Prompt { get; set; } = "交互";

    /// <summary>
    /// 这个目标按 E 要开的面板（M2-C）。**留空就维持占位行为**——给还没做面板的东西留着，
    /// 也免得六个模块没做完之前场景装配不起来。
    /// </summary>
    [Export] public PackedScene? PanelScene { get; set; }

    private bool _used;

    /// <summary>退订要用同一个实例，故缓存之——也免得离树时再去注册表绕一圈。</summary>
    private IInteractionSystem? _interactions;

    public override void _Ready()
    {
        _interactions = GameRoot.Services.Get<IInteractionSystem>();
        _interactions.Register(this);
    }

    /// <summary>不退订的话，注册表里会留下一个指向已销毁节点的条目（ADR-005 里那条 stale reference）。</summary>
    public override void _ExitTree() => _interactions?.Unregister(this);

    /// <summary>位置上报成纯 C# 的向量类型，Godot 的 <c>Vector2</c> 只活在桥接层这一行里（ADR-010）。</summary>
    System.Numerics.Vector2 IInteractable.Position => new(GlobalPosition.X, GlobalPosition.Y);

    float IInteractable.Radius => Radius;

    bool IInteractable.CanInteract => CanInteract;

    string IInteractable.Prompt => Prompt;

    /// <summary>
    /// 配了 <see cref="PanelScene"/> 就把开合整个转发给 <see cref="PanelHost"/>（哪个面板开着、
    /// 暂停怎么算，都是宿主的事，这里一概不管——ADR-007）；没配则维持占位效果。
    /// </summary>
    /// <remarks>
    /// 占位效果是「换个颜色 + 打一行日志」，只为证明「按下的 E 真的走到了这个目标身上」。
    /// 浇水 / 采摘 / 开采是各系统在 M1-5 起的事——现在写任何玩法逻辑都会被推翻重来（铁律 3）。
    /// </remarks>
    void IInteractable.Interact()
    {
        if (PanelScene is not null)
        {
            PanelHost.Instance.Toggle(PanelScene);
            return;
        }

        _used = !_used;
        GD.Print($"[交互] {Prompt}");
        QueueRedraw();
    }

    /// <summary>
    /// 占位外观：一个方块加一圈范围。范围圈是给开发看的——没有它，「提示怎么还不出现」
    /// 只能靠对着坐标猜半径。
    /// </summary>
    public override void _Draw()
    {
        DrawRect(
            new Rect2(-PlaceholderSize / 2f, -PlaceholderSize / 2f, PlaceholderSize, PlaceholderSize),
            _used ? UsedColor : IdleColor);
        DrawArc(Vector2.Zero, Radius, 0f, Mathf.Tau, 64, RangeColor, 1f);
    }

}
