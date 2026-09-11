using System;
using XingGame.Core.Time;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 打坐这项活动（§3.1 的清晨那一项）。「为什么三笔账接在这里」写在
/// <see cref="IMeditationSystem"/> 的类注释上，本类只落实顺序。
/// </summary>
public sealed class MeditationSystem : IMeditationSystem
{
    /// <summary>
    /// 一次打坐坐多久：§3.1 把打坐放在「清晨 6:00-9:00」那一档里，一档就是三游戏小时。
    /// 它是**活动本身的长度**（玩家真去坐一炷香），不是可以随手调的性能参数——所以是常量，
    /// 不进配置表；文档若改了那一档，这里跟着改，别处没有第二份。
    /// </summary>
    private const int SessionInMinutes = 3 * 60;

    private readonly ICultivationSystem _cultivation;
    private readonly ITimeService _time;
    private readonly ICultivationSpeedTable _speed;

    /// <param name="speed">只为一件事：「这个大境界攒不攒修为」由 <see cref="CultivationSpeedTable"/> 回答。</param>
    public MeditationSystem(ICultivationSystem cultivation, ITimeService time, ICultivationSpeedTable speed)
    {
        _cultivation = cultivation ?? throw new ArgumentNullException(nameof(cultivation));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _speed = speed ?? throw new ArgumentNullException(nameof(speed));
    }

    public int SessionMinutes => SessionInMinutes;

    public bool AdvancesByMeditation => _speed.Covers(_cultivation.Realm.Id);

    /// <remarks>
    /// <b>顶点按层号判</b>（<c>Stage == StageCount</c>），不靠「<c>PointsToAdvance</c> 抛不抛」去问：
    /// 拿异常当查询是另一种猜，而且它分不清「到顶了」与「表里漏录了这一层」。
    /// </remarks>
    public int? PointsToNextStage =>
        !AdvancesByMeditation || _cultivation.Stage >= _cultivation.Realm.StageCount
            ? null
            : _speed.PointsToAdvance(_cultivation.Stage);

    public MeditationResult Meditate()
    {
        // 起坐这一刻取一次时刻。**必须在 Advance 之前取**：打坐整段按这一处倍率计价
        // （Meditate 自己不中途换），所以跨午夜坐进子时算的仍是起坐那一刻的倍率。
        GameTime from = _time.Now;
        int stageBefore = _cultivation.Stage;

        // 筑基及以上没有逐层晋升的账，那边 Meditate 当场抛——而打坐照旧回灵力、时间照旧走，
        // 所以这条路单独分开，不是「顺手判断」，是 ICultivationSystem.RecoverSpirit 注释里写明的那种状态
        int gained = AdvancesByMeditation ? _cultivation.Meditate(from, SessionInMinutes) : 0;

        // 两笔账各自传同一段时长（备案 #71 的打坐档：5 灵力/游戏小时）。
        // 先结修为、后回灵力：升层会把灵力补满，补满之后这次恢复的余量自然被算成 0——
        // 于是界面报的「灵力 +0」与灵力条的跳变（补满的那一截）由层号变化解释得通。
        // 反过来写会报一个已经被补满盖掉的 +15，玩家看到的跳变对不上那个数
        int restored = _cultivation.RecoverSpirit(SpiritRecovery.Meditation, SessionInMinutes);

        _time.Advance(SessionInMinutes);

        // To 取推完之后的时刻，不从 from 加：时间怎么走由 ITimeService 说了算（它跨日跨季自己会处理）
        return new MeditationResult(
            SessionInMinutes, gained, restored, stageBefore, _cultivation.Stage, from, _time.Now);
    }
}
