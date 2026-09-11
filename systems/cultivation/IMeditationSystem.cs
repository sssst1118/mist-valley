namespace XingGame.Systems.Cultivation;

/// <summary>
/// 打坐：§3.1 日循环里「清晨 6:00-9:00 —— 打坐修炼」那一项活动。玩家真的去坐一炷香，
/// 于是它同时动三样东西——修为（<see cref="ICultivationSystem.Meditate"/>）、
/// 灵力（<see cref="ICultivationSystem.RecoverSpirit"/>）、游戏时间（<c>ITimeService.Advance</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>这三笔账为什么接在一个系统里，而不是桥接层各调一次</b>（ADR-007）：桥接层只做「读输入 →
/// 调 systems → 把结果画出来」。「坐着入定一段时间 = 时间流逝 + 修为增长 + 回灵力」是一条**玩法判断**，
/// 写进面板就逃过了编译器的看管、只能靠人审 diff；接在这里，面板就只剩一次调用与一次画结果。
/// 同理，别处若要「打坐」（将来的修炼室、宗门闭关），也该调这一个入口，而不是各拼各的三次调用。
/// </para>
/// <para>
/// <b>倍率按起坐那一刻算</b>：<see cref="ICultivationSystem.Meditate"/> 只收一个 <c>GameTime</c>，
/// 且整段按它计价（中途不换倍率）——所以本系统在起坐那一刻取一次 <c>ITimeService.Now</c> 交给它，
/// **然后**才推时间。跨午夜的那一坐（如 22:00 起坐三小时坐进子时）算的是 22:00 的倍率，不是子时的。
/// </para>
/// <para>
/// <b>时间必须显式推进</b>：面板开着时整棵树是暂停的（备案 #36），<c>GameRoot._Process</c> 不跑、
/// 时钟自己不走——不显式推，「打坐三小时」就只是一个数字。走不走得动由 <c>ITimeService</c> 说了算
/// （它在自己被暂停时是空操作），本系统不另发明一套判据。
/// </para>
/// <para>
/// <b>它不带状态、也不进存档（刻意的）</b>：修为与灵力在 <see cref="ICultivationSystem"/> 上、
/// 时刻在 <c>ITimeService</c> 上、进度由 <see cref="ICultivationSpeedTable"/> 现算——本系统只是把
/// 三件事按固定顺序做完的入口。给它存一份「上次坐到几点」，就是同一个事实的第二处。
/// </para>
/// </remarks>
public interface IMeditationSystem
{
    /// <summary>一次打坐的时长（游戏分钟）：§3.1 的「清晨 6:00-9:00 打坐修炼」= <c>180</c>。</summary>
    int SessionMinutes { get; }

    /// <summary>
    /// 此刻打坐**涨不涨修为**：炼气期有逐层晋升的账（攒够就升），筑基及以上是 §8.4 的跨大境界突破
    /// （要丹药或天材地宝），那边的打坐只回灵力。
    /// </summary>
    /// <remarks>
    /// 「哪些大境界有这张账」由 <see cref="ICultivationSpeedTable.Covers"/> 回答，本接口不复制一份判据
    /// ——多一份判据迟早会漂，而漂了就是「筑基修士打坐当场抛异常」这种玩家没法自己解决的症状。
    /// </remarks>
    bool AdvancesByMeditation { get; }

    /// <summary>
    /// 从当前层升到下一层还差多少修为（界面上那个进度条的分母）；**没有「下一层」时是 null**。
    /// </summary>
    /// <remarks>
    /// 两种情形都报 null：炼气十三层是 §8.1 的顶点（溢出的修为作废），以及不靠攒修为晋升的大境界。
    /// 界面要分清这两者，问 <see cref="AdvancesByMeditation"/>（同 <c>ISpiritSenseSystem</c> 把
    /// 「没解锁」与「解锁了却读不到」分成两个查询的做法）。
    /// 顶点报 null 而不是 0：0 会让界面画出一条永远满着的进度条，让玩家以为「攒够了」。
    /// </remarks>
    int? PointsToNextStage { get; }

    /// <summary>
    /// 打坐一次（<see cref="SessionMinutes"/> 分钟）：按起坐那一刻的倍率结修为、回灵力，
    /// 并让游戏时间真的走这么多。
    /// </summary>
    MeditationResult Meditate();
}
