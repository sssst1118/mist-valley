using Godot;
using XingGame.Systems.Cultivation;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 灵力 HUD（桥接层）：一行「当前 / 上限」加一条占位色条，常驻显示玩家的灵力（§17.1 的 HUD 之一）。
/// 除读 <see cref="ICultivationSystem"/> 外不含任何逻辑（ADR-007）。
/// </summary>
/// <remarks>
/// <para>
/// <b>这里每帧轮询是对的</b>（ARCHITECTURE「关于『每帧轮询』」）：灵力是连续量，而且它没有变更事件
/// 可订阅——打坐、睡眠、施法各改各的，谁也不广播。判据是被观察的量离散还是连续，与 <c>ui/TimeHud</c>
/// 订阅事件并不矛盾。并且**值没变就不写 Label、不重画**：每帧赋值会让 Label 与色条重排重绘。
/// </para>
/// <para>
/// <b>上限是现算的</b>（<see cref="ICultivationSystem.MaxSpirit"/> 由层号派生）：本类不缓存
/// 「上限是多少」，只缓存「上一次画的是多少」用来比对——升层后上限会跟着涨（1 层 100 … 13 层 400）。
/// </para>
/// <para>
/// <b>它 <c>process_mode = Always</c>（见 <c>SpiritHud.tscn</c>），不像别的 HUD 那样跟着树一起冻</b>：
/// 面板开着时整棵树是暂停的（备案 #36），而**打坐恰好在那一刻改灵力**（还要升层补满）——
/// 冻住的灵力条会在「面板里写着 125、旁边那条还写着 100」上撒谎，直到玩家关掉面板才追上。
/// 只读的 HUD 不吃输入，跟着不暂停不违反任何契约（面板那两条暂停规矩管的是**开关**，本类一个都不碰）。
/// </para>
/// </remarks>
public partial class SpiritHud : Control
{
    /// <summary>
    /// 占位色条的几何（像素）：左下角，标签在它上面。整块要**抬到工具栏之上**
    /// （<c>ui/ToolbarHud.tscn</c> 占着屏幕底部 106 像素），否则灵力条会压在快捷栏第一格上。
    /// 美术一律先用占位符（铁律 7）。
    /// </summary>
    private const float BarLeft = 16f;
    private const float BarWidth = 240f;
    private const float BarHeight = 12f;
    private const float BarTopFromBottom = 130f;

    private static readonly Color BarBackColor = new(0.10f, 0.12f, 0.18f, 0.85f);
    private static readonly Color BarFillColor = new(0.45f, 0.78f, 0.95f);

    private Label _label = null!;
    private ICultivationSystem _cultivation = null!;

    private int _spirit = -1;
    private int _maxSpirit = -1;

    public override void _Ready()
    {
        _label = GetNode<Label>("SpiritLabel");

        // 灵力属于玩家的修仙状态，由组合根构造并注册（ADR-007）
        _cultivation = GameRoot.Services.Get<ICultivationSystem>();

        // 读档在 GameRoot._Ready 里已经跑完（autoload 先于主场景），先画一次，免得第一帧空白
        Render();
    }

    public override void _Process(double delta) => Render();

    /// <summary>
    /// 值变了才写。第一次跑时两个缓存值都是 -1，必定画一次；此后每帧只是两次整数比较。
    /// </summary>
    private void Render()
    {
        int spirit = _cultivation.Spirit;
        int maxSpirit = _cultivation.MaxSpirit;

        if (spirit == _spirit && maxSpirit == _maxSpirit) return;

        _spirit = spirit;
        _maxSpirit = maxSpirit;

        _label.Text = $"灵力 {spirit} / {maxSpirit}";
        QueueRedraw();
    }

    /// <summary>
    /// 占位色条：底 + 按比例填充。宽度取 <c>Size</c>（本节点铺满整屏），所以它贴着屏幕左下角，
    /// 窗口缩放时也跟着走。
    /// </summary>
    public override void _Draw()
    {
        float top = Size.Y - BarTopFromBottom;

        DrawRect(new Rect2(BarLeft, top, BarWidth, BarHeight), BarBackColor);

        // 上限为 0 只可能出现在读取服务之前（第一帧）：那时只画底，不做除零
        if (_maxSpirit <= 0) return;

        DrawRect(new Rect2(BarLeft, top, BarWidth * _spirit / _maxSpirit, BarHeight), BarFillColor);
    }
}
