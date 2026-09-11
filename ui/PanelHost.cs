using System;
using Godot;

namespace XingGame.Ui;

/// <summary>
/// 面板宿主（桥接层，M2-C 骨架）：游戏里所有「走到某处按 E」开出来的全屏面板都挂在这一个节点下，
/// 它只管面板的<b>生命周期与暂停</b>，别的一概不管。
/// </summary>
/// <remarks>
/// <para>
/// <b>它不认识任何一个具体面板</b>（ADR-007）：只把 <see cref="PackedScene"/> 实例化、挂上去、拆下来；
/// 面板该显示什么、按钮点了算不算数，全是各系统的事，本类里没有一行玩法判断。
/// </para>
/// <para>
/// <b>暂停归宿主，不归面板</b>：打开即 <c>GetTree().Paused = true</c>（备案 #36），关闭时还回原状。
/// 之所以不收在每个面板里，是因为「换一个面板」要经过一次「先关旧的、再开新的」——各写各的就会在
/// 这一轮里存两次、还两次，谁盖掉谁全看调用顺序。面板侧只有两条义务：<c>process_mode = Always</c>
/// （见 <c>PanelHost.tscn</c>，否则连关自己的那次按键都收不到），以及自己读 <c>close_panel</c> 后调
/// <see cref="CloseAll"/>——契约要求「关掉的键面板自己处理」，这样某个面板将来想让 Esc 先「返回上一层」
/// 而不是直接关，不必改宿主。
/// </para>
/// <para>
/// <b>面板一律不许碰暂停开关</b>（M2-C 契约的一部分）：<c>GetTree().Paused</c> 与 <c>ITimeService.IsPaused</c>
/// 都归宿主。<c>tests/BridgeContractTests.cs</c> 扫源码钉着这一条，别去绕。
/// </para>
/// <para>
/// <b>即便如此，关闭顺序仍是「先摘、再删、最后恢复」</b>：<c>RemoveChild</c> 会同步跑完面板的
/// <c>_ExitTree</c>，万一有面板在里面写了暂停，也会被随后这次恢复盖掉——恢复权始终在宿主手里。
/// 顺序反过来，被盖掉的就是宿主，树会永远冻着。（这是顺序本身的理由，不是给面板写暂停的许可。）
/// </para>
/// <para>
/// <b>代价是 <see cref="CloseAll"/> 一返回，面板就已经不在树里了</b>：<c>GetViewport()</c> / <c>GetTree()</c>
/// 随即变 null，之后再去碰它们就是 NRE。所以面板收 Esc 的写法是<b>先吃掉按键、再关</b>：
/// <code>
/// if (!@event.IsActionPressed("close_panel")) return;
/// GetViewport().SetInputAsHandled();   // ← 必须在 CloseAll 之前
/// PanelHost.Instance.CloseAll();
/// </code>
/// </para>
/// </remarks>
public partial class PanelHost : Control
{
    private static PanelHost? _instance;

    /// <summary>
    /// 场景里唯一的面板宿主。**没有宿主时抛**：那意味着某个配了 <c>PanelScene</c> 的目标注定按 E 没反应，
    /// 这是场景装配错误——静默什么都不做比报错难查得多。
    /// </summary>
    public static PanelHost Instance =>
        _instance ?? throw new InvalidOperationException(
            "场景里没有 PanelHost：world/Main.tscn 的 Hud 下应挂一个 ui/PanelHost.tscn");

    /// <summary>当前开着的面板场景，用来判「同一个面板再调即关」。</summary>
    private PackedScene? _openScene;

    private Control? _openPanel;

    /// <summary>
    /// 打开前的暂停状态。关闭时「恢复原状」而不是一律置 false：将来若是别的东西先把时间停了
    /// （过场、读档），关一次面板不该顺手把它解开（同 <c>ui/InventoryPanel</c>）。
    /// </summary>
    private bool _pausedBefore;

    public override void _EnterTree()
    {
        // 用 _EnterTree 而不是 _Ready：Godot 挂载一棵子树是「先整棵 _EnterTree，再自下而上 _Ready」，
        // 所以同场景里任何节点在自己的 _Ready 里都取得到 Instance；放到 _Ready 里赋值就晚了半拍。
        if (_instance is null)
        {
            _instance = this;
            return;
        }

        // 场景里被误放两个是迟早的事。这里报错 + 就地销毁，让树收敛回契约写的「唯一」：
        // 「忍着」的话 Instance 指哪一个取决于挂载顺序，对着场景排查的人无从判断谁才是真的。
        // 保留先挂的那个而不是后来者覆盖——否则同一份场景的两次挂载会有两种结果。
        GD.PushError($"[PanelHost] 场景里已经有一个 PanelHost（{_instance.GetPath()}），" +
                     $"多余的这一个会被销毁：{GetPath()}");
        QueueFree();
    }

    public override void _ExitTree()
    {
        // 只有「我还是那个唯一」才清空：重复挂载的那个也会走这里，它不该把真的宿主抹掉。
        if (ReferenceEquals(_instance, this)) _instance = null;

        if (_openPanel is null) return;

        // 宿主被整体拆掉（换场景 / 退出）。面板作为子节点会跟着消失，但树还停在我们开的暂停上，
        // 新场景会一开场就是冻的——这里还回去。
        _openPanel = null;
        _openScene = null;

        SceneTree? tree = GetTree();
        if (tree is not null) tree.Paused = _pausedBefore;
    }

    /// <summary>开 / 关：同一个面板再调即关；换一个面板则先关旧的、再开新的。</summary>
    public void Toggle(PackedScene panelScene)
    {
        // 判「同一个」用引用相等：面板场景由 ResourceLoader 按路径缓存，同一个 .tscn 落在两个
        // Interactable 的导出槽里也是同一个实例；不同路径本就是不同面板。
        bool alreadyOpen = ReferenceEquals(panelScene, _openScene);

        // 先关完再开：_pausedBefore 因此总是「此刻真实的暂停状态」，而不是上一次打开时留下的旧值。
        CloseAll();

        // 同一个面板再调即关——上面已经关掉了，到此为止。
        if (alreadyOpen) return;

        Open(panelScene);
    }

    /// <summary>关掉当前面板并恢复暂停。没有面板开着时是空操作（也不动暂停）。</summary>
    public void CloseAll()
    {
        if (_openPanel is null) return;

        Control panel = _openPanel;
        _openPanel = null;
        _openScene = null;

        // 先摘、再删、最后恢复：RemoveChild 会同步跑面板的 _ExitTree，面板若在里面自己写回暂停，
        // 也被下面这次恢复盖掉——恢复权始终在宿主手里（理由见类注释）。
        RemoveChild(panel);
        panel.QueueFree();

        GetTree().Paused = _pausedBefore;
    }

    private void Open(PackedScene panelScene)
    {
        // 导出槽指到一个坏掉的场景（文件被换掉、序列化不全）是编辑期的手误。只报错、不开面板，
        // 更不暂停——把世界冻住却不给玩家关的入口，比什么都不做更糟。
        if (!panelScene.CanInstantiate())
        {
            GD.PushError($"[PanelHost] 面板场景无法实例化，已忽略：{panelScene.ResourcePath}");
            return;
        }

        // 面板每次开都新实例化、关掉就 QueueFree，不复用：面板的 _Ready 里按当时的数据把内容铺好
        // （照 ui/InventoryPanel），留着复用就得给每个面板再定一套「重画」契约，可开面板一年也没几次，
        // 省下的那点实例化开销换不来这份复杂度。顺带，面板若订阅过事件，也随之退干净。
        Node node = panelScene.Instantiate();

        // 根节点必须是个 Control：不是的话它既不会被居中也不会随窗口变化，只是块看不见的贴纸。
        // 与其让它在画面外默默失效，不如照着契约（面板一律 Control 派生）当场拒绝。
        if (node is not Control panel)
        {
            GD.PushError($"[PanelHost] 面板场景的根节点不是 Control，已忽略：{panelScene.ResourcePath}");

            // 这个节点还没进过树，get_tree() 是空的，queue_free() 对它无效（会静默漏掉），只能用 Free。
            node.Free();
            return;
        }

        // 先暂停再挂上树：面板的 _Ready 一睁眼看到的就该是「世界已经冻住」，而不是先闪一帧。
        _pausedBefore = GetTree().Paused;
        GetTree().Paused = true;

        _openScene = panelScene;
        _openPanel = panel;

        AddChild(panel);
    }
}
