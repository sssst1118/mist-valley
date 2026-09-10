using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using XingGame.Core.Events;
using XingGame.Core.Save;

namespace XingGame.Core.Time;

/// <summary>
/// 时间系统的实现。只推进游戏分钟、推导时段、按日掷天气并广播事件——不产生任何玩法效果（ADR-006）。
/// </summary>
public sealed class TimeService : ITimeService, ISaveable
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

    public string SaveKey => "time";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    public int Version => 1;

    /// <summary>
    /// 只存时刻与天气，<b>不存 worldSeed</b>——种子在存档 meta 表里（由 <c>TryPeekWorldSeed</c> 读），
    /// 同一个事实存两处早晚会对不上（ARCHITECTURE「TimeService 接入存档」）。
    /// 天气存的是当下实际值而非按种子重掷：存档是权威事实，重掷会抹掉当天真实发生过的天气。
    /// </summary>
    public string Serialize() =>
        JsonSerializer.Serialize(
            new SavedTime(_now.Year, _now.Season, _now.Day, _now.Hour, _now.Minute, _weather),
            _saveJsonOptions);

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009 同款理由）
        if (fromVersion > Version)
            throw new NotSupportedException($"时间存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedTime saved = JsonSerializer.Deserialize<SavedTime>(json, _saveJsonOptions)
            ?? throw new InvalidDataException("时间存档内容为空");

        Validate(saved);

        // 载入是整状态覆盖，故不发任何事件：组合根在 _Ready 里调用，订阅者此刻多半还没建好；
        // 就算建好了，重放跨天事件也会让按 tick 记账的订阅者多算一天（同 Sleep 的理由）。
        _now = new GameTime(saved.Year, saved.Season, saved.Day, saved.Hour, saved.Minute);
        _weather = saved.Weather;
    }

    /// <summary>
    /// 存档是外部输入，越界值不拦会让 EpochDay/TotalMinutes 算出一个不存在的一天。当场抛比
    /// 让坏数据渗进天气序列强——后者要到很久以后才显形，且现场离病因太远。
    /// </summary>
    private static void Validate(SavedTime saved)
    {
        if (saved.Year < 1)
            throw new InvalidDataException($"时间存档年份 {saved.Year} 越界");

        if (!Enum.IsDefined(saved.Season))
            throw new InvalidDataException($"时间存档季节值 {(int)saved.Season} 越界");

        if (saved.Day < 1 || saved.Day > GameTime.DaysPerSeason)
            throw new InvalidDataException($"时间存档日 {saved.Day} 越界（应在 1..{GameTime.DaysPerSeason}）");

        if (saved.Hour < 0 || saved.Hour >= HoursPerDay)
            throw new InvalidDataException($"时间存档小时 {saved.Hour} 越界");

        if (saved.Minute < 0 || saved.Minute >= MinutesPerHour)
            throw new InvalidDataException($"时间存档分钟 {saved.Minute} 越界");
    }

    /// <summary>存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。</summary>
    private sealed record SavedTime(int Year, Season Season, int Day, int Hour, int Minute, Weather Weather);

    /// <summary>枚举写成名字（"Rainy"）而非数字：Blob 要能被人和 Mod 读懂，将来往枚举里插值也不会错位。</summary>
    private static readonly JsonSerializerOptions _saveJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

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
