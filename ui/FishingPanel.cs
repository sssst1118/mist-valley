using System.Collections.Generic;
using Godot;
using XingGame.Core.Time;
using XingGame.Systems.Fishing;
using XingGame.Systems.Items;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 钓鱼面板（桥接层）：读当下的季节/天气/时段 → 问鱼表这个条件能钓到什么 → 按「抛竿」调
/// <see cref="IFishingSystem"/> → 把候选、图鉴与这一竿的结果画出来。
/// </summary>
/// <remarks>
/// <para>
/// <b>一条玩法规则都不在这里</b>（ADR-007）：条件过滤、权重掷鱼、鱼进背包、记图鉴全在
/// <c>systems/fishing/</c>，本类只做「读服务 → 调系统 → 画结果」。
/// </para>
/// <para>
/// <b>暂停不归本类管</b>：打开时整棵树已经由 <c>PanelHost</c> 冻结，本类只是在场景里声明
/// <c>process_mode = Always</c>，否则连关自己的那次按键都收不到。面板自己再写一次暂停，
/// 「开 A → 开 B」那一轮会存两次、还两次（ARCHITECTURE「接口契约 · M2-C」）。
/// </para>
/// <para>
/// <b>本类不订阅任何事件</b>：面板开着时整棵树是暂停的，时间不推进（<c>GameRoot._Process</c> 一起停住），
/// 顶栏的时段与天气在这期间不可能变——订阅了也永远收不到，那只是为「看起来完整」留的死代码（铁律 11）。
/// </para>
/// </remarks>
public partial class FishingPanel : Control
{
    /// <summary>关面板的动作名，映射在 <c>project.godot</c> 的 <c>[input]</c> 段。</summary>
    private static readonly StringName CloseAction = "close_panel";

    /// <summary>图鉴区的行，下标与 <see cref="_codexOrder"/> 一一对应。</summary>
    private readonly List<Label> _codexRows = new();

    private Label _status = null!;
    private Label _result = null!;

    private ITimeService _time = null!;
    private IFishTable _fish = null!;
    private IFishingSystem _fishing = null!;
    private FishCodex _codex = null!;
    private IItemTable _items = null!;

    /// <summary>本面板开出来之后甩过的竿数。见 <see cref="Seed"/> 对种子的说明。</summary>
    private int _castIndex;

    /// <summary>
    /// 图鉴画的是<b>全部</b>鱼（表内顺序，同候选区），钓到与没钓到都在里面——这份清单在面板的一生里不变，
    /// 所以行节点建一次，之后只改文字。
    /// </summary>
    private List<FishDefinition> _codexOrder = new();

    /// <summary>
    /// 本面板按 <see cref="CatchMethod.Rod"/> 取鱼：这个点是「走到水边按 E」的鱼竿钓点，
    /// 而 §7.3 的蟹笼是「放置后每日收取」，不经过玩家按 E 这道门（<see cref="CatchMethod"/> 分成两种的理由）。
    /// </summary>
    /// <remarks>
    /// <b>刻意不做成 <c>[Export]</c></b>：早先这里是个配置项，但它**只接了一半**——候选区按配置筛鱼，
    /// 而系统那边的 <c>FishingSystem</c> 把 <see cref="CatchMethod.Rod"/> 写死（<c>IFishingSystem</c> 压根
    /// 没有 method 参数）。填成 1 会让「此刻能钓到」列出蟹笼组的鱼、按下去却钓上竿钓组的鱼——
    /// **一个看着可配、实际只配一半的旋钮，比没有旋钮更坏**。等蟹笼真的落地、系统侧开了 method 参数，
    /// 再把这里接上（那时它才配叫配置项）。
    /// </remarks>
    private const CatchMethod Method = CatchMethod.Rod;

    public override void _Ready()
    {
        // 本面板由 PanelHost 实例化后直接挂上树，宿主只做 AddChild、从不设 Visible——
        // 场景里也刻意没写 visible = false（那会让面板永远不显示），这里再显式钉一次。
        Visible = true;

        _status = GetNode<Label>("Center/Content/StatusLabel");
        _result = GetNode<Label>("Center/Content/ResultLabel");

        _time = GameRoot.Services.Get<ITimeService>();
        _fish = GameRoot.Services.Get<IFishTable>();
        _fishing = GameRoot.Services.Get<IFishingSystem>();
        _codex = GameRoot.Services.Get<FishCodex>();
        _items = GameRoot.Services.Get<IItemTable>();

        GetNode<Button>("Center/Content/CastButton").Pressed += Cast;

        GameTime now = _time.Now;
        _status.Text = $"{SeasonName(now.Season)} {now.Day} 日 · {WeatherName(_time.Weather)} · {PhaseName(now.Phase)}";

        BuildCandidates(now);
        BuildCodex();
    }

    /// <summary>
    /// Esc 关面板。<b>顺序不能反</b>（ARCHITECTURE「关面板：顺序反了会静默出错」）：<c>CloseAll</c> 一返回，
    /// 本面板就已经不在树里，<c>GetViewport()</c> 随即是 null——先关再吃按键会在 <c>_Input</c> 里抛 NRE，
    /// 而 <c>_Input</c> 里的异常只报日志、不崩游戏，「跑起来没事」很容易把它糊过去。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed(CloseAction)) return;

        GetViewport().SetInputAsHandled();
        PanelHost.Instance.CloseAll();
    }

    /// <summary>
    /// 抛一竿。候选清单与图鉴行都不重建：面板开着时时间是冻住的，候选不会变，变的只有图鉴的条数与本竿结果。
    /// </summary>
    private void Cast()
    {
        GameTime now = _time.Now;
        int castIndex = _castIndex++;

        // 种子是桥接层的决定，不是系统的：IFishingSystem 是刻意的纯函数（同参同果，测试靠它），种子归调用方给。
        // 取存档里的世界种子加竿序——种子随存档走（GameRoot.WorldSeed），所以同一个世界里「第几竿出什么鱼」
        // 是一定的；连甩两竿出不同的鱼由竿序负责，系统把两者一起混（FishingSystem.Mix）。
        // 刻意不用面板内的 Random，也不用当前游戏时刻：前者关掉再打开就是一次重掷，白送一个刷鱼的口子；
        // 后者在面板开着时被冻住（打开面板即整棵树暂停），同一竿的结果反倒与世界无关。
        int seed = GameRoot.WorldSeed + castIndex;

        // 先单独掷一次，只为把「没有鱼咬钩」与「背包放不下」分开报：TryRoll 是纯函数，
        // 同一组参数必得同一条鱼，所以这里掷出来的正是下面 TryCatch 内部掷的那条
        if (!_fishing.TryRoll(now.Season, _time.Weather, now.Phase, castIndex, seed, out FishDefinition fish))
        {
            _result.Text = "空竿 —— 这个季节 / 天气 / 时段没有可钓的鱼";
            return;
        }

        if (!_fishing.TryCatch(now.Season, _time.Weather, now.Phase, castIndex, seed, out _))
        {
            _result.Text = $"背包放不下，「{FishName(fish)}」没能上岸";
            return;
        }

        _result.Text = $"钓到了「{FishName(fish)}」";
        RenderCodex();
    }

    /// <summary>
    /// 候选区：鱼表说这个条件下能钓到什么就画什么。钓不到不是错误——昏倒时段或某场暴风雪里
    /// 本来就可能一条候选都没有，那种时候如实画「没有」。
    /// </summary>
    private void BuildCandidates(GameTime now)
    {
        var list = GetNode<VBoxContainer>("Center/Content/Scroll/Lists/CandidatesList");

        IReadOnlyList<FishDefinition> candidates = _fish.Candidates(now.Season, _time.Weather, now.Phase, Method);

        if (candidates.Count == 0)
        {
            list.AddChild(new Label { Text = "（没有可钓的鱼）" });
            return;
        }

        foreach (FishDefinition candidate in candidates)
            list.AddChild(new Label { Text = FishName(candidate), ClipText = true });
    }

    /// <summary>图鉴区：全部鱼各一行，行的顺序取鱼表顺序，与候选区一致。</summary>
    private void BuildCodex()
    {
        var list = GetNode<VBoxContainer>("Center/Content/Scroll/Lists/CodexList");

        _codexOrder = new List<FishDefinition>(_fish.All);

        for (int index = 0; index < _codexOrder.Count; index++)
        {
            var row = new Label { ClipText = true };
            list.AddChild(row);
            _codexRows.Add(row);
        }

        RenderCodex();
    }

    /// <summary>钓上一条之后图鉴多了一笔，重画文字即可。</summary>
    private void RenderCodex()
    {
        for (int index = 0; index < _codexRows.Count; index++)
        {
            FishDefinition fish = _codexOrder[index];

            _codexRows[index].Text = _codex.HasCaught(fish.Id)
                ? $"已钓到  {FishName(fish)} ×{_codex.CountOf(fish.Id)}"
                : $"未钓到  {FishName(fish)}";
        }
    }

    /// <summary>
    /// 鱼的名字在物品表里：<see cref="FishDefinition"/> 只带 ItemId、不带显示名，显示名归物品表
    /// （与 <c>ui/InventoryPanel</c> 同一条路，鱼表不重复维护一份名字）。
    /// </summary>
    private string FishName(FishDefinition fish) => _items.Get(fish.ItemId).Name;

    // 下面三个译名与 ui/TimeHud 里的重复了一处。两处都是私有的，等 M1-6 的存档菜单把时间文本也接上，
    // 一并收敛到同一个格式化器（同 world/GameRoot 里 SeasonName 的注释）。

    private static string SeasonName(Season season) => season switch
    {
        Season.Spring => "春",
        Season.Summer => "夏",
        Season.Autumn => "秋",
        _             => "冬",
    };

    private static string WeatherName(Weather weather) => weather switch
    {
        Weather.Sunny => "晴",
        Weather.Rainy => "雨",
        Weather.Storm => "暴风雨",
        Weather.Snowy => "雪",
        Weather.Windy => "风",
        _             => "雾",
    };

    private static string PhaseName(DayPhase phase) => phase switch
    {
        DayPhase.Morning   => "清晨",
        DayPhase.Forenoon  => "上午",
        DayPhase.Afternoon => "下午",
        DayPhase.Evening   => "傍晚",
        DayPhase.Collapsed => "昏倒",
        _                  => "深夜",
    };
}
