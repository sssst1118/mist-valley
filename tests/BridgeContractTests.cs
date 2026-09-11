using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace XingGame.Tests;

/// <summary>
/// M2-C 桥接层契约的钉子：`ui/*Panel.cs` 与 `ui/*Panel.tscn` 的三条「必须」没有行为可测——它们是
/// Godot 节点之间的约定，而本工程是只引用 XingGame.Core 的纯 C#（ADR-002），够不到 `ui/`。所以改读源码文本断言。
/// </summary>
/// <remarks>
/// <para>
/// 这与 AGENT-BRIEF 里「用反射断言成员不存在」是同一思路：<b>否定式、顺序式的决定没有行为可测，
/// 但同样会被下一个人「顺手」加回来</b>。契约文档管不住代码，用例才管得住。
/// </para>
/// <para>
/// 三条契约（ARCHITECTURE「接口契约 · M2-C · 每个面板的规矩」，各面板的类注释里都有复述）：
/// <list type="number">
/// <item><b>暂停归宿主，面板一根手指都不许碰暂停开关</b>，两个开关都算，`.` 前面是谁不改变性质：
/// <c>GetTree().Paused</c> —— 面板再设一次，「开 A → 开 B」那一轮会存两次、还两次，这类计数错乱<b>只在换面板时才显形</b>；
/// <c>ITimeService.IsPaused</c> —— 这个<b>更险</b>：<c>PanelHost</c> 只还<b>树</b>暂停、压根不认识 <c>ITimeService</c>，
/// 所以宿主管理的面板一旦按下它，关面板时树会恢复、<b>时钟却永远停摆</b>（表现是「游戏还在跑、时间永远不走」，
/// 极难往面板上想）。判据是<b>赋值</b>、不是出现——<c>_treePausedBefore = GetTree().Paused;</c> 这种<b>读</b>不算违规。</item>
/// <item>收 Esc 必须先 <c>GetViewport().SetInputAsHandled()</c>、再 <c>PanelHost.Instance.CloseAll()</c>。
/// CloseAll 一返回面板已不在树里，<c>GetViewport()</c> 随即是 null，反了就会在 <c>_Input</c> 里抛 NRE——
/// 而 <c>_Input</c> 里的异常<b>只报日志、不崩游戏</b>，「跑起来没事」很容易把它糊过去。</item>
/// <item>面板 <c>.tscn</c> 的根节点块不许写 <c>visible = false</c>。<c>PanelHost.Open()</c> 只做 AddChild、
/// 从不设 <c>Visible = true</c>，照抄 <c>ui/InventoryPanel.tscn</c>（那份是自己开关的）写出来的面板<b>永远不会显示</b>。</item>
/// </list>
/// </para>
/// <para>
/// <b>本类最大的失败模式是「空跑绿灯」</b>：glob 写错、目录找错、正则写错的结果都不是红，而是
/// 「零个文件被检查 → 零条断言失败 → 绿灯」——那比不写还糟，因为它让人以为契约有人守着。所以有两道锁：
/// ①「扫到的清单非空、且点名具体文件」的前置断言（.cs 与 .tscn 各一条）；
/// ② 每条检查器都拿坏样本与好样本各过一遍的对照用例（既证明它认得出违规，也证明它不误伤合规）。
/// </para>
/// </remarks>
public class BridgeContractTests
{
    /// <summary>
    /// 免检：背包面板不是走 <c>PanelHost</c> 开出来的——它常驻挂在 <c>world/Main.tscn</c> 的 Hud 下、自己开关，
    /// 所以它<b>该</b>自己写两个暂停开关、它的 .tscn 根节点<b>该</b>带 <c>visible = false</c>。
    /// 不排除的话这几条就是假红，而假红的下场是被真人删掉，比不写还糟。
    /// <para>
    /// 例外本身有三条用例守着（见「免检自守」三节）：它确实<b>成套地存还原两个开关</b>、且确实<b>不走宿主</b>。
    /// 哪天它改了（自己不再管暂停、或开始被宿主管），那几条会红——那时该做的是把这两行例外连同免检名单一起删掉，
    /// 而不是留一个白挡一层的名单。
    /// </para>
    /// </summary>
    private const string ExemptPanelSource = "InventoryPanel.cs";

    private const string ExemptPanelScene = "InventoryPanel.tscn";

    /// <summary>样本里「不关心哪个开关」的占位：好样本压根不该判出违规，自然也没有开关可说。</summary>
    private const string AnySwitch = "";

    /// <summary>
    /// 扫到的面板数下限。现在有 7 个 <c>*Panel.cs</c>（免检 1 个），这里只要求 ≥5：
    /// 这条断言要挡的是「扫到 0 个还绿灯」，不是「数量正好等于某个数」——写等号会让每加一个面板都得回来改一次。
    /// </summary>
    private const int MinPanelCount = 5;

    /// <summary>
    /// 两个开关的称呼。分开两句话是刻意的：后果与修法完全不同，混成一个说法，下一个读的人就会按错的那个去修。
    /// </summary>
    private const string TreeSwitch = "树暂停（GetTree().Paused）";

    /// <inheritdoc cref="TreeSwitch"/>
    private const string ClockSwitch = "时钟暂停（ITimeService.IsPaused）";

    private const string CloseActionLiteral = "\"close_panel\"";
    private const string ConsumeInputCall = "SetInputAsHandled()";
    private const string CloseAllCall = "CloseAll(";

    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string UiDirectory = Path.Combine(RepoRoot, "ui");
    private static readonly string WorldDirectory = Path.Combine(RepoRoot, "world");

    /// <summary>
    /// 给任意 <c>.Paused</c> 赋值：含 <c>GetTree().Paused</c>，也含 <c>var tree = GetTree(); tree.Paused = …</c> 这种换引用写法。
    /// <c>(?!=)</c> 是别把比较 <c>Paused == true</c> 也算上。
    /// </summary>
    private static readonly Regex TreePausedAssignPattern = new(@"\.\s*Paused\s*=(?!=)");

    /// <summary>给任意 <c>.IsPaused</c> 赋值：挡住宿主管理的面板偷偷把时钟停掉（见类注释里这条为什么更险）。</summary>
    private static readonly Regex ClockPausedAssignPattern = new(@"\.\s*IsPaused\s*=(?!=)");

    /// <summary>根节点块里的 <c>visible = false</c>。<c>\s*</c> 是因为 Godot 接受 <c>visible=false</c> 这种没空格的写法。</summary>
    private static readonly Regex RootVisibleFalsePattern = new(@"visible\s*=\s*false\b");

    // ─────────────────────────── 前置断言：证明真的扫到了文件 ───────────────────────────

    /// <summary>
    /// 命门。扫源码的用例一旦扫不到东西，红不了，只会整齐地绿——所以除了数量，还点名要看见商店与矿洞面板。
    /// </summary>
    [Fact]
    public void 扫到的面板源码清单必须非空且点名()
    {
        string[] panels = PanelFiles(".cs");

        Assert.True(panels.Length >= MinPanelCount,
            $"只扫到 {panels.Length} 个 ui/*Panel.cs（下限 {MinPanelCount}）：{string.Join("、", panels)}");
        Assert.Contains("ShopPanel.cs", panels);
        Assert.Contains("MinePanel.cs", panels);
    }

    /// <summary>同上，对 .tscn 再来一遍：三条契约各扫一类文件，一类扫不到就得单独红。</summary>
    [Fact]
    public void 扫到的面板场景清单必须非空且点名()
    {
        string[] scenes = PanelFiles(".tscn");

        Assert.True(scenes.Length >= MinPanelCount,
            $"只扫到 {scenes.Length} 个 ui/*Panel.tscn（下限 {MinPanelCount}）：{string.Join("、", scenes)}");
        Assert.Contains("ShopPanel.tscn", scenes);
        Assert.Contains("MinePanel.tscn", scenes);
    }

    // ─────────────────────────── ① 暂停开关一律归宿主 ───────────────────────────

    [Theory]
    [MemberData(nameof(CheckedSources))]
    public void 面板源码不许自己动暂停开关(string fileName)
    {
        Assert.Empty(ScanPauseWrites(ReadPanelFile(fileName)).Select(write => $"{fileName} {write}"));
    }

    // ─────────────────────────── ② 关面板先吃键、后关 ───────────────────────────

    [Theory]
    [MemberData(nameof(CheckedSources))]
    public void 面板源码关面板必须先吃键再关(string fileName)
    {
        Assert.Empty(ScanCloseOrder(ReadPanelFile(fileName)).Select(violation => $"{fileName} {violation}"));
    }

    // ─────────────────────────── ③ 面板场景的根节点不许自带隐藏 ───────────────────────────

    [Theory]
    [MemberData(nameof(CheckedScenes))]
    public void 面板场景根节点不许写_visible_false(string fileName)
    {
        Assert.Empty(ScanRootVisibility(ReadPanelFile(fileName)).Select(violation => $"{fileName} {violation}"));
    }

    // ─────────────────────────── 免检自守：那两行例外凭什么成立 ───────────────────────────

    /// <summary>
    /// 免检成立的第一半：它<b>自己成套地存还原</b>——两个开关都自己管（打开前存下、关时还回去）。
    /// 只写一个的「免检」是不成立的，所以两个都要看见；哪天这套存还原被删了或只留一半，这条红。
    /// </summary>
    [Fact]
    public void 免检项_背包面板确实自己成套地存还原两个开关()
    {
        string[] switches = ScanPauseWrites(ReadPanelFile(ExemptPanelSource))
            .Select(write => write.Switch)
            .Distinct()
            .ToArray();

        Assert.Contains(TreeSwitch, switches);
        Assert.Contains(ClockSwitch, switches);
    }

    /// <summary>
    /// 免检成立的第二半：它<b>不走宿主</b>（<c>PanelHost</c> 只管自己实例化出来的面板）。
    /// 两半是一对：面板既自己存还原、又被宿主管着，就是两套账打架——那时该删的是例外，不是这条用例。
    /// </summary>
    [Fact]
    public void 免检项_背包面板不走宿主()
    {
        string code = StripComments(ReadPanelFile(ExemptPanelSource));
        Assert.False(code.Contains("PanelHost", StringComparison.Ordinal),
            $"{ExemptPanelSource} 里出现了 PanelHost：它一旦开始跟宿主打交道，免检的前提就没了");

        string[] spots = Directory.GetFiles(WorldDirectory, "*.tscn");
        Assert.True(spots.Length > 0, $"world/ 下一个 .tscn 都没扫到：{WorldDirectory}");

        // 「被宿主管」的样子就是某个采点的 PanelScene 指向它（主场景直接 instance 是常驻挂法，不算）
        string[] wired = spots
            .Where(path => File.ReadAllLines(path).Any(line =>
                line.Contains("PanelScene", StringComparison.Ordinal) &&
                line.Contains(ExemptPanelScene, StringComparison.Ordinal)))
            .Select(path => Path.GetFileName(path) ?? path)
            .ToArray();

        Assert.True(wired.Length == 0,
            $"{string.Join("、", wired)} 把 {ExemptPanelScene} 当 PanelScene 挂上了宿主：免检前提没了，"
            + "把免检名单（ExemptPanelSource / ExemptPanelScene）连同这几条自守用例一起删掉，"
            + "否则背包面板会一边自己存还原、一边被宿主存还原。");
    }

    /// <summary>免检的第三处：背包场景的根节点确实自带隐藏（它是常驻自己开关的，故合法）。</summary>
    [Fact]
    public void 免检项_背包场景确实根节点自带隐藏()
    {
        Assert.NotEmpty(ScanRootVisibility(ReadPanelFile(ExemptPanelScene)));
    }

    // ─────────────────────────── 检查器自己的对照：坏样本必须红、好样本不许红 ───────────────────────────

    /// <summary>
    /// 没有这一段，前面那些「合规」说明不了任何事：正则写空了、剥注释把整份源码剥没了，
    /// 结果同样是绿。所以拿样本喂一遍检查器，两个方向都要对。
    /// </summary>
    [Theory]
    [MemberData(nameof(PauseSamples))]
    public void 暂停检查器_坏样本必须判违规且不误伤好样本(string scenario, string source, bool shouldViolate, string expectedSwitch)
    {
        IReadOnlyList<PauseWrite> violations = ScanPauseWrites(source);
        AssertVerdict(scenario, violations.Count > 0, shouldViolate);

        if (expectedSwitch.Length > 0)
            Assert.True(violations.Any(write => write.Switch == expectedSwitch),
                $"{scenario}：判出了违规，却没一条点明是「{expectedSwitch}」——两个开关的后果与修法完全不同，"
                + "混成一个说法，下一个读的人就会按错的那个去修。");
    }

    [Theory]
    [MemberData(nameof(CloseOrderSamples))]
    public void 关闭顺序检查器_坏样本必须判违规且不误伤好样本(string scenario, string source, bool shouldViolate)
    {
        AssertVerdict(scenario, ScanCloseOrder(source).Count > 0, shouldViolate);
    }

    [Theory]
    [MemberData(nameof(RootVisibleSamples))]
    public void 根节点可见性检查器_坏样本必须判违规且不误伤好样本(string scenario, string source, bool shouldViolate)
    {
        AssertVerdict(scenario, ScanRootVisibility(source).Count > 0, shouldViolate);
    }

    // ─────────────────────────── 数据 ───────────────────────────

    /// <summary>受检的面板源码（除免检项）。给的是**文件名**：用例名与违规消息里只该出现文件名，不是整条路径。</summary>
    public static IEnumerable<object[]> CheckedSources =>
        PanelFiles(".cs").Where(name => name != ExemptPanelSource).Select(name => new object[] { name });

    /// <summary>受检的面板场景（除免检项）。</summary>
    public static IEnumerable<object[]> CheckedScenes =>
        PanelFiles(".tscn").Where(name => name != ExemptPanelScene).Select(name => new object[] { name });

    public static IEnumerable<object[]> PauseSamples => new[]
    {
        new object[]
        {
            "坏样本：面板按下了树开关",
            """
            public override void _Ready()
            {
                GetTree().Paused = true;
            }
            """,
            true,
            TreeSwitch,
        },
        new object[]
        {
            "坏样本：换个引用按同一个树开关（var tree = GetTree(); tree.Paused = true;）",
            """
            private void Freeze(Node tree)
            {
                tree.Paused = true;
            }
            """,
            true,
            TreeSwitch,
        },
        new object[]
        {
            "坏样本：面板按下了时钟开关——这个更险，宿主只还树暂停，时钟会永远停摆",
            """
            private void Freeze(ITimeService time)
            {
                time.IsPaused = true;
            }
            """,
            true,
            ClockSwitch,
        },
        new object[]
        {
            "坏样本：时钟开关写回 false 也算——面板一动这个开关，宿主的存还原账就乱了",
            """
            private void Thaw()
            {
                GameRoot.Services.Get<ITimeService>().IsPaused = false;
            }
            """,
            true,
            ClockSwitch,
        },
        new object[]
        {
            "好样本：只是读不算违规（判据是赋值、不是出现）——两个开关都是读了不写就动不了宿主的账",
            """
            private void Remember()
            {
                _treePausedBefore = GetTree().Paused;
                _timePausedBefore = _time.IsPaused;
            }
            """,
            false,
            AnySwitch,
        },
        new object[]
        {
            "好样本：注释里写着「不许碰 GetTree().Paused」「GetTree().Paused = true 会存两次」不算违规——面板的类注释本来就该写这句「为什么」",
            """
            /// <summary>本面板一根手指都不碰 GetTree().Paused / ITimeService.IsPaused：暂停归 PanelHost。</summary>
            public partial class SamplePanel : Control
            {
                // 面板再设一次 GetTree().Paused = true，「开 A → 开 B」那一轮就会存两次、还两次
            }
            """,
            false,
            AnySwitch,
        },
    };

    public static IEnumerable<object[]> CloseOrderSamples => new[]
    {
        new object[]
        {
            "好样本：先吃键、后关面板（契约顺序）",
            """
            public override void _Input(InputEvent @event)
            {
                if (!@event.IsActionPressed(CloseAction)) return;

                GetViewport().SetInputAsHandled();
                PanelHost.Instance.CloseAll();
            }
            """,
            false,
        },
        new object[]
        {
            "坏样本：两行对调——反了就在 _Input 里抛 NRE，而 _Input 里的异常只报日志、不崩游戏",
            """
            public override void _Input(InputEvent @event)
            {
                if (!@event.IsActionPressed(CloseAction)) return;

                PanelHost.Instance.CloseAll();
                GetViewport().SetInputAsHandled();
            }
            """,
            true,
        },
        new object[]
        {
            "坏样本：处理了 close_panel 却没吃掉按键",
            """
            private static readonly StringName CloseAction = "close_panel";

            public override void _Input(InputEvent @event)
            {
                if (!@event.IsActionPressed(CloseAction)) return;

                PanelHost.Instance.CloseAll();
            }
            """,
            true,
        },
        new object[]
        {
            "好样本：只吃键、不关面板不算违规——Esc 可以留给面板自己用（PanelHost 的注释写明留了「返回上一层」这个口子），ui/InventoryPanel.cs 就是这么用的",
            """
            public override void _Input(InputEvent @event)
            {
                if (!@event.IsActionPressed(ToggleAction)) return;

                SetOpen(!Visible);
                GetViewport().SetInputAsHandled();
            }
            """,
            false,
        },
        new object[]
        {
            "好样本：注释里贴着两行的「反面教材」，代码里的顺序是对的——比的是代码，不是注释",
            """
            /// 反面教材（别这么写）：PanelHost.Instance.CloseAll(); GetViewport().SetInputAsHandled();
            GetViewport().SetInputAsHandled();
            PanelHost.Instance.CloseAll();
            """,
            false,
        },
    };

    public static IEnumerable<object[]> RootVisibleSamples => new[]
    {
        new object[]
        {
            "坏样本：根节点带 visible = false（照抄 ui/InventoryPanel.tscn 的后果是面板永远不显示）",
            """
            [gd_scene load_steps=2 format=3]

            [node name="SamplePanel" type="Control"]
            process_mode = 3
            visible = false
            script = ExtResource("1_sample")

            [node name="Backdrop" type="ColorRect" parent="."]
            visible = false
            """,
            true,
        },
        new object[]
        {
            "坏样本：写成 visible=false（没有空格）同样是违规——两种写法 Godot 都收",
            """
            [node name="SamplePanel" type="Control"]
            process_mode=3
            visible=false
            """,
            true,
        },
        new object[]
        {
            "好样本：根节点不写 visible（同 ui/ShopPanel.tscn），可见性由 _Ready 里的 Visible = true 负责",
            """
            [node name="SamplePanel" type="Control"]
            process_mode = 3
            script = ExtResource("1_sample")

            [node name="Backdrop" type="ColorRect" parent="."]
            """,
            false,
        },
        new object[]
        {
            "好样本：子节点写 visible = false 是合法的（ui/DialoguePanel.tscn 的 EmptyHintLabel 就靠它藏空提示）——只该看根节点块",
            """
            [node name="SamplePanel" type="Control"]
            process_mode = 3
            script = ExtResource("1_sample")

            [node name="EmptyHintLabel" type="Label" parent="."]
            visible = false
            """,
            false,
        },
    };

    // ─────────────────────────── 检查器（对一段文本求值，才喂得进样本） ───────────────────────────

    /// <summary>
    /// 一次「面板自己动了暂停开关」的违规：<b>哪一个开关</b>、第几行、为什么。
    /// 开关分开记是刻意的——树暂停是「存两次、还两次」，时钟暂停是「永远停摆」，不是同一种修法。
    /// </summary>
    private readonly record struct PauseWrite(string Switch, int Line, string Why)
    {
        public override string ToString() => $"第 {Line} 行：给{Switch}赋值——{Why}";
    }

    private const string TreeWhy =
        "「开 A → 开 B」那一轮会存两次、还两次，而这类计数错乱只在换面板时才显形";

    private const string ClockWhy =
        "PanelHost 只还树暂停、压根不认识 ITimeService：宿主关面板时树会恢复、时钟却永远停摆，"
        + "表现是「游戏还在跑、时间永远不走」，极难往面板上想";

    /// <summary>
    /// 违规清单（空 = 合规）。两个开关都扫，判据是<b>赋值</b>不是出现：读一眼（<c>_treePausedBefore = GetTree().Paused;</c>）
    /// 动不了宿主的账，不冤枉它。换引用写（<c>var tree = GetTree(); tree.Paused = …</c>）一样要挡。
    /// </summary>
    private static IReadOnlyList<PauseWrite> ScanPauseWrites(string source)
    {
        string code = StripComments(source);
        var hits = new List<(int Index, PauseWrite Write)>();

        foreach (Match match in TreePausedAssignPattern.Matches(code))
            hits.Add((match.Index, new PauseWrite(TreeSwitch, LineOf(code, match.Index), TreeWhy)));

        foreach (Match match in ClockPausedAssignPattern.Matches(code))
            hits.Add((match.Index, new PauseWrite(ClockSwitch, LineOf(code, match.Index), ClockWhy)));

        // 按出现位置排，两个开关各说各的（同一条语句里两个都写就是两条，不合并）
        return hits.OrderBy(hit => hit.Index).Select(hit => hit.Write).ToArray();
    }

    /// <summary>
    /// 违规清单（空 = 合规）。两条规矩：
    /// ① 处理了 <c>close_panel</c> 就必须有一处 <c>SetInputAsHandled()</c>——不然那一次按键还会被
    /// GUI 与 <c>_UnhandledInput</c> 再看见一次；
    /// ② 两个调用都在时，所有吃键都必须在关面板之前——<c>CloseAll</c> 一返回面板已不在树里，
    /// <c>GetViewport()</c> 随即是 null。
    /// 只吃键、不关面板<b>不算</b>违规：<c>PanelHost</c> 的类注释写明留了「Esc 先返回上一层」这个口子。
    /// </summary>
    private static IReadOnlyList<string> ScanCloseOrder(string source)
    {
        string code = StripComments(source);
        var violations = new List<string>();

        int consume = code.LastIndexOf(ConsumeInputCall, StringComparison.Ordinal);
        int close = code.IndexOf(CloseAllCall, StringComparison.Ordinal);

        if (consume < 0 && code.Contains(CloseActionLiteral, StringComparison.Ordinal))
            violations.Add("处理了 close_panel，却没有一处 GetViewport().SetInputAsHandled()：按键没被吃掉，面板关了之后同一次按键还会被 GUI 与 _UnhandledInput 再看见一次");

        if (consume >= 0 && close >= 0 && consume > close)
            violations.Add(
                $"顺序反了：SetInputAsHandled() 在第 {LineOf(code, consume)} 行、CloseAll() 在第 {LineOf(code, close)} 行——"
                + "CloseAll 一返回面板已不在树里，GetViewport() 随即是 null，再去碰就是 NRE；而 _Input 里的异常只报日志、不崩游戏");

        return violations;
    }

    /// <summary>
    /// 违规清单（空 = 合规）。只看根节点块：<c>PanelHost.Open()</c> 只 AddChild、从不设 <c>Visible = true</c>，
    /// 根节点自带隐藏就等于开出来一个看不见的面板。子节点上的 <c>visible = false</c> 是合法的（藏一行空提示之类）。
    /// </summary>
    private static IReadOnlyList<string> ScanRootVisibility(string scene)
    {
        string? root = RootNodeBlock(scene);
        if (root is null)
            return new[] { "整份场景里找不到 [node 行——这不是一份 Godot 场景，别当它合规" };

        return RootVisibleFalsePattern.IsMatch(root)
            ? new[] { "根节点块里写了 visible = false：PanelHost.Open() 只 AddChild、从不设 Visible = true，面板开出来就永远不显示（多半是照抄了 ui/InventoryPanel.tscn）" }
            : Array.Empty<string>();
    }

    // ─────────────────────────── 读文件 / 取片段 ───────────────────────────

    /// <summary>
    /// 取根节点块：第一条 <c>[node</c> 行到第二条 <c>[node</c> 行之间（不含第二条）。
    /// </summary>
    private static string? RootNodeBlock(string scene)
    {
        string[] lines = scene.Split('\n');

        int start = Array.FindIndex(lines, line => line.StartsWith("[node ", StringComparison.Ordinal));
        if (start < 0) return null;

        int end = Array.FindIndex(lines, start + 1, line => line.StartsWith("[node ", StringComparison.Ordinal));
        return string.Join("\n", lines, start, (end < 0 ? lines.Length : end) - start);
    }

    /// <summary>
    /// 剥掉注释，**逐字符换成空格、长度不变**（不是删掉）：下标与行号必须和原文对齐，
    /// 违规消息里的行号才是原文的行号。
    /// 为什么非剥不可：面板的类注释里**故意**写着「本面板不碰 GetTree().Paused」「先 SetInputAsHandled 再 CloseAll」
    /// ——那是「为什么」。不剥的话这几条用例全是假红，而假红的下场是被真人删掉。
    /// （本仓库的面板没有逐字字符串，故不处理 <c>@"…"</c>：真出现了症状是假红而不是假绿，不会把违规漏过去。）
    /// </summary>
    private static string StripComments(string source)
    {
        var stripped = new char[source.Length];
        bool lineComment = false, blockComment = false, inString = false, inChar = false;

        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (lineComment)
            {
                stripped[i] = current == '\n' ? '\n' : ' ';
                lineComment = current != '\n';
            }
            else if (blockComment)
            {
                if (current == '*' && next == '/')
                {
                    stripped[i] = ' ';
                    stripped[i + 1] = ' ';
                    i++;
                    blockComment = false;
                }
                else
                {
                    stripped[i] = current == '\n' ? '\n' : ' ';
                }
            }
            else if (inString || inChar)
            {
                stripped[i] = current;
                if (current == '\\' && next != '\0')
                {
                    // 转义：把下一个字符一起抄走，别把它当成收尾的引号
                    stripped[i + 1] = next;
                    i++;
                }
                else if (inString ? current == '"' : current == '\'')
                {
                    inString = false;
                    inChar = false;
                }
            }
            else if (current == '/' && next == '/')
            {
                stripped[i] = ' ';
                lineComment = true;
            }
            else if (current == '/' && next == '*')
            {
                stripped[i] = ' ';
                blockComment = true;
            }
            else
            {
                stripped[i] = current;
                if (current == '"') inString = true;
                else if (current == '\'') inChar = true;
            }
        }

        return new string(stripped);
    }

    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') line++;

        return line;
    }

    private static string ReadPanelFile(string fileName) =>
        File.ReadAllText(Path.Combine(UiDirectory, fileName));

    private static string[] PanelFiles(string extension) =>
        Directory.GetFiles(UiDirectory, "*Panel" + extension)
            .Select(path => Path.GetFileName(path) ?? path)
            .Where(name => name.EndsWith("Panel" + extension, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// 从测试输出目录逐级上溯找仓库根（同时有 <c>ui/</c>、<c>world/</c> 与 <c>tests/</c> 的那一级），
    /// 与 <c>core/</c>、<c>systems/</c> 里读数据文件的「逐级上溯」同款做法
    /// （ARCHITECTURE「技术债：配置文件靠从输出目录逐级上溯定位」）——不再发明一套路径解析。
    /// 三个目录都要有才认，是为了不撞上某个恰好叫 ui 的中间目录；找不到就抛：
    /// 扫源码的用例最怕的就是静悄悄扫了零个文件还绿灯。
    /// </summary>
    private static string FindRepoRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                bool looksLikeRoot =
                    Directory.Exists(Path.Combine(directory.FullName, "ui")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "world")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "tests"));

                if (looksLikeRoot) return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "找不到仓库根（同时含 ui/、world/ 与 tests/ 的目录）：本用例靠读 ui/*Panel.cs 的文本守契约，"
            + $"扫不到就只能假绿灯。已从 {AppContext.BaseDirectory} 与 {Environment.CurrentDirectory} 逐级上溯。");
    }

    private static void AssertVerdict(string scenario, bool flagged, bool shouldViolate)
    {
        Assert.True(flagged == shouldViolate,
            $"{scenario}：检查器判为「{(flagged ? "违规" : "合规")}」，期望却是「{(shouldViolate ? "违规" : "合规")}」。"
            + (shouldViolate
                ? "坏样本认不出来，说明正则或剥注释坏了——真正的违规也会这样整齐地绿过去。"
                : "好样本被误判，说明检查器过严——假红的下场是被真人把用例删掉。"));
    }
}
