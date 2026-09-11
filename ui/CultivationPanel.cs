using Godot;
using XingGame.Core.Time;
using XingGame.Systems.Cultivation;
using XingGame.World;

namespace XingGame.Ui;

/// <summary>
/// 全屏修仙面板（桥接层，M3-8）：读服务 → 调 <see cref="IMeditationSystem"/> → 把结果画出来（ADR-007）。
/// 灵根、境界、修为、灵力、灵脉福地、法术与三条门径全是**只读**展示，唯一的写入口是「打坐」。
/// </summary>
/// <remarks>
/// <para>
/// <b>暂停不归面板管</b>：打开即冻树、关闭即还回来是 <see cref="PanelHost"/> 的事（备案 #36）。
/// 面板再设一次 <c>GetTree().Paused</c>，「开 A → 开 B」那一轮就会存两次暂停、还两次，
/// 而这类计数错乱只在换面板时才显形。
/// </para>
/// <para>
/// <b>打坐只调一个入口（本切片最要紧的一条）</b>：修为 + 灵力 + 时间这三笔账由
/// <see cref="IMeditationSystem"/> 接在一处——那是玩法判断，不是界面能拼的东西。尤其**时间**：
/// 面板开着时整棵树是暂停的、时钟自己不走，「坐三小时」要真的过掉三小时只能显式推，
/// 而「推多少、按哪一刻的倍率结算」都不许由这里说了算（本类连 <c>ITimeService</c> 都不取）。
/// </para>
/// <para>
/// <b>关面板先吃键、后关</b>：<see cref="PanelHost.CloseAll"/> 一返回本面板已经不在树里，
/// <c>GetViewport()</c> 随即是 null——顺序反了会在 <c>_Input</c> 里抛 NRE，而 <c>_Input</c> 里的异常
/// 只报日志、不崩游戏，「跑起来没事」很容易把它糊过去。
/// </para>
/// </remarks>
public partial class CultivationPanel : Control
{
    /// <summary>关面板的动作名，六个面板共用（映射在 <c>project.godot</c> 的 <c>[input]</c> 段）。</summary>
    private static readonly StringName CloseAction = "close_panel";

    /// <summary>结果行要覆盖的字色主题项名。</summary>
    private static readonly StringName FontColorName = "font_color";

    /// <summary>三列占位宽度（像素）。美术一律先用占位符（铁律 7）。</summary>
    private static readonly Vector2 NameWidth = new(170f, 0f);
    private static readonly Vector2 RequireWidth = new(190f, 0f);
    private static readonly Vector2 StatusWidth = new(170f, 0f);

    /// <summary>开着的绿 / 关着的灰：一眼分得出哪几扇门已经开了。</summary>
    private static readonly Color OpenColor = new(0.45f, 0.82f, 0.55f);
    private static readonly Color ShutColor = new(0.72f, 0.72f, 0.66f);

    /// <summary>这一次打坐真有进项（绿）与白坐（灰）：顶点、或筑基及以上的打坐不涨修为。</summary>
    private static readonly Color GainedColor = new(0.45f, 0.82f, 0.55f);
    private static readonly Color NoGainColor = new(0.72f, 0.72f, 0.66f);

    private ICultivationSystem _cultivation = null!;
    private IMeditationSystem _meditation = null!;
    private IRealmTable _realms = null!;
    private ISpiritSenseSystem _sense = null!;
    private ISpellTable _spells = null!;
    private ILifeSpellSystem _lifeSpells = null!;

    private Label _rootLabel = null!;
    private Label _realmLabel = null!;
    private Label _progressLabel = null!;
    private Label _spiritLabel = null!;
    private Label _landLabel = null!;
    private Label _resultLabel = null!;
    private Button _meditateButton = null!;
    private VBoxContainer _gateList = null!;
    private VBoxContainer _spellList = null!;

    public override void _Ready()
    {
        // 宿主只 AddChild、从不设 Visible，面板得自己保证是可见的。写成显式一句而不是靠 .tscn 的缺省，
        // 是为了挡住「照抄 ui/InventoryPanel.tscn」——那份是自己开关的，根节点带着 visible = false
        Visible = true;

        _rootLabel = GetNode<Label>("Center/Content/RootLabel");
        _realmLabel = GetNode<Label>("Center/Content/RealmLabel");
        _progressLabel = GetNode<Label>("Center/Content/ProgressLabel");
        _spiritLabel = GetNode<Label>("Center/Content/SpiritLabel");
        _landLabel = GetNode<Label>("Center/Content/LandLabel");
        _resultLabel = GetNode<Label>("Center/Content/ResultLabel");
        _meditateButton = GetNode<Button>("Center/Content/MeditateButton");
        _gateList = GetNode<VBoxContainer>("Center/Content/Scroll/Rows/GateList");
        _spellList = GetNode<VBoxContainer>("Center/Content/Scroll/Rows/SpellList");

        _cultivation = GameRoot.Services.Get<ICultivationSystem>();
        _meditation = GameRoot.Services.Get<IMeditationSystem>();
        _realms = GameRoot.Services.Get<IRealmTable>();
        _sense = GameRoot.Services.Get<ISpiritSenseSystem>();
        _spells = GameRoot.Services.Get<ISpellTable>();
        _lifeSpells = GameRoot.Services.Get<ILifeSpellSystem>();

        _meditateButton.Pressed += Meditate;

        Render();
    }

    /// <summary>
    /// 关面板的键是离散量，用 <c>_Input</c> 接（同 <c>ui/ShopPanel</c>），不每帧轮询。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed(CloseAction)) return;

        // 这两行的顺序是契约：CloseAll 返回时本面板已不在树里，之后再去碰 GetViewport() 就是 NRE
        GetViewport().SetInputAsHandled();
        PanelHost.Instance.CloseAll();
    }

    /// <summary>
    /// 整屏重画。只在 <c>_Ready</c> 与每次打坐之后调一次：面板开着时整棵树是暂停的，
    /// 除了「打坐」这一下没有别人改得动这些数——而它自己会顺手再调一次本方法，
    /// 所以没有需要每帧盯着的量。
    /// </summary>
    private void Render()
    {
        _rootLabel.Text = $"灵根　{RootName()}";
        _realmLabel.Text = $"境界　{_cultivation.Realm.Name} · {_cultivation.Realm.StageName(_cultivation.Stage)}";
        _progressLabel.Text = ProgressText();
        _spiritLabel.Text = $"灵力　{_cultivation.Spirit} / {_cultivation.MaxSpirit}";
        _landLabel.Text = LandText();

        // 时长由系统给（§3.1 的清晨那一档），面板不认识 3 这个数
        _meditateButton.Text = $"打坐 {_meditation.SessionMinutes / 60} 小时";

        Clear(_gateList);
        foreach (CultivationGateRequirement gate in _realms.Gates) _gateList.AddChild(GateRow(gate));

        Clear(_spellList);
        foreach (SpellDefinition spell in _spells.Spells) _spellList.AddChild(SpellRow(spell));
    }

    /// <summary>
    /// 打坐一次。**面板不碰时长、不碰时钟**：坐多久、时间怎么走、按哪一刻的倍率结算，
    /// 全在 <see cref="IMeditationSystem.Meditate"/> 里（ADR-007）。
    /// </summary>
    private void Meditate()
    {
        MeditationResult result = _meditation.Meditate();

        _resultLabel.Text = ResultText(result);
        _resultLabel.AddThemeColorOverride(FontColorName, result.PointsGained > 0 ? GainedColor : NoGainColor);

        Render();
    }

    /// <summary>
    /// 把一次打坐翻成人话。**升层要单独点出来**：升层会把灵力补满，于是「灵力 +0」与
    /// 「灵力条涨了一截」会同时成立——不说明这一句，玩家看到的就是两个对不上的数。
    /// </summary>
    private string ResultText(MeditationResult result)
    {
        string text = $"打坐 {result.Minutes / 60} 小时：修为 +{result.PointsGained}，灵力 +{result.SpiritRestored}"
                    + $"（{Clock(result.From)} → {Clock(result.To)}）";

        if (result.StageAfter <= result.StageBefore) return text;

        return text + $"，突破至{_cultivation.Realm.Name}{_cultivation.Realm.StageName(result.StageAfter)}";
    }

    /// <summary>
    /// 品级 + 具体灵根。四档普通品级没有具体灵根（<see cref="ICultivationSystem.Root"/> 是 null），
    /// 只画品级——不替文档编一个「金灵根」出来。
    /// </summary>
    private string RootName() =>
        _cultivation.Root is null ? _cultivation.Grade.Name : $"{_cultivation.Grade.Name} · {_cultivation.Root.Name}";

    /// <summary>
    /// 修为那一行：有下一层就报「已攒 / 需要（还差多少）」，没有就照实说为什么没有
    /// ——顶点（§8.1 的炼气十三层）与不靠攒修为晋升的大境界（§8.4 的突破）是两件事，
    /// 混成一句话会让站在顶点上的玩家以为自己练错了路。
    /// </summary>
    private string ProgressText()
    {
        int cultivation = _cultivation.Cultivation;

        if (!_meditation.AdvancesByMeditation)
            return $"修为　{cultivation}（此境靠突破晋升，打坐不涨修为）";

        int? target = _meditation.PointsToNextStage;
        if (target is not int needed)
            return $"修为　{cultivation}（已至{_cultivation.Realm.Name}顶点）";

        return $"修为　{cultivation} / {needed}（还差 {needed - cultivation}）";
    }

    /// <summary>
    /// 灵脉与福地（§8.8）。读数走灵气感知：**没解锁时它返回 null**，因为那条法术买的正是「看得见」
    /// 本身——所以这里如实画成还没开启，不替它猜一个等级（同 <c>ui/ShopPanel</c> 对没定价的商品不猜）。
    /// 福地带阶号：§8.8 的一阶就叫「福地」，只写名字会读成一句废话。
    /// </summary>
    private string LandText()
    {
        SpiritSenseReading? reading = _sense.Read();

        return reading is null
            ? "灵脉　尚未开启灵气感知"
            : $"灵脉　{reading.Vein.Name}（灵气浓度 ×{reading.ConcentrationMultiplier:0.00}）"
              + $"　{reading.Land.Name}（{reading.Land.Order} 阶）";
    }

    /// <summary>
    /// 一条 §8.2 末尾的「游戏绑定」：名字 + 要求 + 开没开。<b>层数由境界表回答、过没过由
    /// <see cref="ICultivationSystem.Meets"/> 判</b>，面板两个数字都不自己比（面板里没有玩法判断）。
    /// </summary>
    private Control GateRow(CultivationGateRequirement gate)
    {
        RealmDefinition realm = _realms.Get(gate.RealmId);
        bool met = _cultivation.Meets(gate.Gate);

        var row = new HBoxContainer();

        row.AddChild(Cell(gate.Name, NameWidth));
        row.AddChild(Cell($"{realm.Name}{realm.StageName(gate.Stage)}", RequireWidth));
        row.AddChild(Cell(met ? "已开" : "未开", StatusWidth, met ? OpenColor : ShutColor));

        return row;
    }

    /// <summary>
    /// 一条法术：名字 + 解锁要求 + 习没习。**本切片只列出来看，不做施法**——法术选择与目标格是下一刀
    /// （<c>ILifeSpellSystem.TryCastAt</c> 今天仍然只有用例在调）。
    /// </summary>
    private Control SpellRow(SpellDefinition spell)
    {
        bool unlocked = _lifeSpells.IsUnlocked(spell.Id);
        RealmDefinition realm = _realms.Get(spell.UnlockRealmId);

        // 消耗 0 的是灵气感知（§8.2 说 1-3 层「可感知灵气但无法施法」）：它本来就不是施法，
        // 画一句「耗 0 灵力」会让玩家以为按一下就能放
        string status = unlocked
            ? spell.SpiritCost > 0 ? $"已习 · 耗 {spell.SpiritCost} 灵力" : "已习 · 不耗灵力"
            : "未习";

        var row = new HBoxContainer();

        row.AddChild(Cell(spell.Name, NameWidth, unlocked ? OpenColor : ShutColor));
        row.AddChild(Cell($"{realm.Name}{realm.StageName(spell.UnlockStage)}解锁", RequireWidth));
        row.AddChild(Cell(status, StatusWidth, unlocked ? OpenColor : ShutColor));

        return row;
    }

    /// <summary>面板里的时刻一律 <c>HH:MM</c>：打坐看的是「坐到几点」。</summary>
    private static string Clock(GameTime time) => $"{time.Hour:D2}:{time.Minute:D2}";

    /// <summary>
    /// 清空一个列表。<b>先摘再删</b>：<c>QueueFree</c> 要到帧末才真的删，只调它的话重画之后
    /// 旧行会跟新行在同一帧里一起画出来。
    /// </summary>
    private static void Clear(Container list)
    {
        foreach (Node child in list.GetChildren())
        {
            list.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>占位单元格：定宽 + 裁字，理由同 <c>ui/ShopPanel</c>——最长的那一行不该拽歪整排。</summary>
    private static Label Cell(string text, Vector2 width, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            CustomMinimumSize = width,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
        };

        if (color is Color value) label.AddThemeColorOverride(FontColorName, value);

        return label;
    }
}
