using XingGame.Core.Time;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 一次打坐发生了什么（<see cref="IMeditationSystem.Meditate"/> 的返回值）：涨了多少修为、回了多少灵力、
/// 从哪一刻坐到哪一刻、中途有没有跨过台阶。
/// </summary>
/// <remarks>
/// <para>
/// <b>字段都是「这一次」的量，不是状态</b>：修为余额、层号、灵力都还在各自的系统里，界面重画时从那边读
/// （同 <c>TradeResult</c> 只回一个结果、账在系统上）。
/// </para>
/// <para>
/// <b>为什么要带层号</b>：升层会把灵力**补满**（<see cref="ICultivationSystem.Meditate"/> 的定论），
/// 于是「灵力 +0」与「灵力条涨了一截」会同时成立——不把层号带出来，界面就只能报一个与画面对不上的数。
/// </para>
/// </remarks>
/// <param name="Minutes">这次打坐的时长（游戏分钟）。</param>
/// <param name="PointsGained">
/// 真正记下的修为：到顶点后溢出的部分不算（同 <see cref="ICultivationSystem.Meditate"/>）；
/// 筑基及以上本来就不攒修为（§8.4 的突破），恒为 0。
/// </param>
/// <param name="SpiritRestored">真正回上的灵力：已经在上限时是 0。**升层补满的那一截不在这里**。</param>
/// <param name="StageBefore">起坐时的层号。</param>
/// <param name="StageAfter">收功时的层号；一步没升就与 <paramref name="StageBefore"/> 相同。</param>
/// <param name="From">起坐时刻——倍率按这一刻算。</param>
/// <param name="To">收功时刻——时间真的走到了这里。</param>
public sealed record MeditationResult(
    int Minutes,
    int PointsGained,
    int SpiritRestored,
    int StageBefore,
    int StageAfter,
    GameTime From,
    GameTime To);
