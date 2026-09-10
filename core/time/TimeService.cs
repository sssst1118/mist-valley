using System;
using XingGame.Core.Events;

namespace XingGame.Core.Time;

/// <summary>
/// 时间系统的实现。只推进游戏分钟、推导时段、按日掷天气并广播事件——不产生任何玩法效果（ADR-006）。
/// </summary>
public sealed class TimeService : ITimeService
{
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;
    private const int MinutesPerDay = MinutesPerHour * HoursPerDay;
    private const int DaysPerYear = GameTime.SeasonsPerYear * GameTime.DaysPerSeason;

    private readonly IEventBus _bus;
    private readonly IWeatherGenerator _weatherGenerator;
    private readonly int _seed;

    private GameTime _now;
    private Weather _weather;

    /// <param name="worldSeed">决定天气序列，随存档走；同种子同一天必得同天气。</param>
    /// <param name="start">存档迁移或测试用的起始时刻，缺省为元年春 1 日 6:00。</param>
    public TimeService(
        IEventBus bus,
        IWeatherGenerator weatherGenerator,
        TimeConfig? config = null,
        int worldSeed = 0,
        GameTime? start = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _weatherGenerator = weatherGenerator ?? throw new ArgumentNullException(nameof(weatherGenerator));
        Config = config ?? new TimeConfig();
        _seed = worldSeed;
        _now = start ?? new GameTime(1, Season.Spring, 1, GameTime.FirstHour, 0);
        _weather = RollWeather(_now);
    }

    public TimeConfig Config { get; }

    public GameTime Now => _now;

    public Weather Weather => _weather;

    public bool IsPaused { get; set; }

    public void Advance(int gameMinutes)
    {
        // 暂停时不吞掉这批分钟数：解冻后补跑会让玩家回来发现过了一天
        if (IsPaused || gameMinutes <= 0) return;

        for (int minute = 0; minute < gameMinutes; minute++)
            TickOneMinute();
    }

    public void Sleep()
    {
        GameTime previous = _now;
        _now = NextDayWakeUp(_now);

        // Sleep 是跳跃而非流逝：不发 MinuteTicked/HourChanged——把 16 小时上报成一次 tick 会
        // 骗坏按 tick 记账的订阅者（打更音效、buff 计时）。需要绝对时刻的订阅者读 Now。
        // 跨天本身走 PublishDayChanged，与自然跨过 6:00 是同一段逻辑，不另写一份。
        PublishPhaseChanged(previous);
        PublishDayChanged(previous);
    }

    private void TickOneMinute()
    {
        GameTime previous = _now;
        _now = AddMinutes(_now, 1);

        _bus.Publish(new MinuteTicked(_now));

        if (_now.Hour != previous.Hour)
            _bus.Publish(new HourChanged(_now));

        // 次序即契约（ARCHITECTURE「日界与事件时序」）：时段先于日界，日界先于季/天气
        PublishPhaseChanged(previous);

        if (EpochDay(_now) != EpochDay(previous))
            PublishDayChanged(previous);
    }

    private void PublishPhaseChanged(GameTime previous)
    {
        if (_now.Phase != previous.Phase)
            _bus.Publish(new DayPhaseChanged(_now.Phase, previous.Phase));
    }

    /// <summary>
    /// 跨日（可能同时跨季）时的广播。调用方已先发过 <see cref="DayPhaseChanged"/>。
    /// 天气必须<b>先</b>换再发 <see cref="DayStarted"/>：
    /// 订阅者（如雨天自动浇水）在 DayStarted 回调里读到的得是今天的天气，不能是昨天的。
    /// </summary>
    private void PublishDayChanged(GameTime previous)
    {
        Weather previousWeather = _weather;
        _weather = RollWeather(_now);

        _bus.Publish(new DayStarted(_now));

        if (_now.Season != previous.Season)
            _bus.Publish(new SeasonChanged(_now, previous.Season));

        if (_weather != previousWeather)
            _bus.Publish(new WeatherChanged(_weather, previousWeather));
    }

    private Weather RollWeather(GameTime time) => _weatherGenerator.Roll(time.Season, EpochDay(time), _seed);

    /// <summary>0 基绝对日序。用绝对日序而非「季内第几天」，跨季跨年才不会有重复的天气序列。</summary>
    private static int EpochDay(GameTime time) =>
        ((((time.Year - 1) * GameTime.SeasonsPerYear) + (int)time.Season) * GameTime.DaysPerSeason) + (time.Day - 1);

    /// <summary>次日 6:00。日界就在 6:00，所以「次日」= 当前游戏日的下一个 1440 分钟槽。</summary>
    private static GameTime NextDayWakeUp(GameTime from) =>
        FromTotalMinutes((FloorDiv(from.TotalMinutes, MinutesPerDay) + 1) * MinutesPerDay);

    private static GameTime AddMinutes(GameTime from, int minutes) => FromTotalMinutes(from.TotalMinutes + minutes);

    /// <summary><see cref="GameTime.TotalMinutes"/> 的逆运算，两处的进位规则必须一致。</summary>
    private static GameTime FromTotalMinutes(int totalMinutes)
    {
        int dayOfEpoch = FloorDiv(totalMinutes, MinutesPerDay);   // 0 基
        int minuteOfDay = Mod(totalMinutes, MinutesPerDay);       // 自当日 6:00 起

        int year = FloorDiv(dayOfEpoch, DaysPerYear) + 1;
        int dayOfYear = Mod(dayOfEpoch, DaysPerYear);

        return new GameTime(
            year,
            (Season)(dayOfYear / GameTime.DaysPerSeason),
            (dayOfYear % GameTime.DaysPerSeason) + 1,
            ((minuteOfDay / MinutesPerHour) + GameTime.FirstHour) % HoursPerDay,
            minuteOfDay % MinutesPerHour);
    }

    private static int FloorDiv(int value, int divisor) => (int)Math.Floor((double)value / divisor);

    private static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
