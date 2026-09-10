namespace XingGame.Core.Time;

/// <summary>
/// 不可变的游戏时刻。内部单位只有「游戏分钟」——不持有现实时间，现实映射由桥接层按
/// <see cref="TimeConfig.MinutesPerRealSecond"/> 换算（ADR-006）。
/// </summary>
/// <remarks>
/// <see cref="Hour"/> 是自然钟点（0–23），但 <see cref="Day"/> 的翻页在 <see cref="FirstHour"/>（6:00）：
/// 0:00–5:59 属于前一个游戏日。玩家的一天是 6:00 起床 → 次日 2:00 昏倒（§3.1），必然横跨午夜；
/// 日界若设在午夜，「今天浇过水没有」这类当日标记会在游玩途中清零（ARCHITECTURE「日界与事件时序」）。
/// </remarks>
public readonly record struct GameTime(int Year, Season Season, int Day, int Hour, int Minute)
{
    public const int DaysPerSeason  = 28;   // §3.2
    public const int SeasonsPerYear = 4;
    public const int FirstHour      = 6;    // §3.1 清晨起点，同时是日界
    public const int CollapseHour   = 2;    // §3.1 昏倒线（次日）

    private const int MinutesPerHour = 60;
    private const int HoursPerDay    = 24;

    /// <summary>由 Hour 推导，见 ARCHITECTURE.md「DayPhase 推导表」。</summary>
    public DayPhase Phase => Hour switch
    {
        >= FirstHour and < 9    => DayPhase.Morning,
        >= 9 and < 12           => DayPhase.Forenoon,
        >= 12 and < 18          => DayPhase.Afternoon,
        >= 18 and < 22          => DayPhase.Evening,
        >= 22 or < CollapseHour => DayPhase.LateNight,
        _                       => DayPhase.Collapsed,
    };

    /// <summary>
    /// 自元年春 1 日 6:00（开局时刻）起算的累计分钟数，跨日/季/年严格递增。
    /// 0:00–5:59 折算到当日 6:00 之后，所以它比同一个游戏日的 23:59 还大——这样按分钟推进
    /// 才不会在午夜倒流。
    /// </summary>
    public int TotalMinutes => ((EpochDay - 1) * HoursPerDay * MinutesPerHour) + MinutesSinceDayStart;

    /// <summary>自当日 6:00 起算的分钟偏移，恒在 [0, 1440)。</summary>
    private int MinutesSinceDayStart =>
        ((((Hour - FirstHour) + HoursPerDay) % HoursPerDay) * MinutesPerHour) + Minute;

    /// <summary>1 基的累计游戏日序。1 基是为了让 (Day - 1) 在换算中自然消掉。</summary>
    private int EpochDay => (((Year - 1) * SeasonsPerYear) + (int)Season) * DaysPerSeason + Day;
}
