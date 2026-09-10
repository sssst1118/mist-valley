using System;
using System.Collections.Generic;
using System.Linq;
using XingGame.Core.Events;
using XingGame.Core.Time;

namespace XingGame.Tests;

public class TimeServiceTests
{
    private const int Seed = 20260911;
    private const int OneHour = 60;

    private static readonly GameTime Spring1SixAm = new(1, Season.Spring, 1, GameTime.FirstHour, 0);

    /// <summary>scripted 按掷点次序逐个取用（构造时那次也算），用完就一直沿用最后一个。</summary>
    private static Harness Create(GameTime start, params Weather[] scripted)
        => new(
            new FakeEventBus(),
            new StubWeatherGenerator(scripted.Length > 0 ? scripted : new[] { Weather.Sunny }),
            start);

    [Fact]
    public void 构造_起始为元年春一日六点_并已掷出当天天气()
    {
        Harness harness = Create(Spring1SixAm, Weather.Foggy);

        Assert.Equal(new GameTime(1, Season.Spring, 1, 6, 0), harness.Time.Now);
        Assert.Equal(0, harness.Time.Now.TotalMinutes);
        Assert.Equal(Weather.Foggy, harness.Time.Weather);
        Assert.False(harness.Time.IsPaused);
        Assert.Equal((Season.Spring, 0, Seed), harness.WeatherGenerator.Calls.Single());
        Assert.Empty(harness.Bus.Published);
    }

    [Fact]
    public void 构造_可指定起始时刻与种子()
    {
        var harness = new Harness(new FakeEventBus(), new StubWeatherGenerator(Weather.Sunny), new GameTime(2, Season.Summer, 5, 20, 0), worldSeed: 7);

        // 绝对日序 = (2-1)*4*28 + 1*28 + (5-1) = 144
        Assert.Equal((Season.Summer, 144, 7), harness.WeatherGenerator.Calls.Single());
    }

    [Fact]
    public void Advance_一分钟_推进一分钟并发_MinuteTicked()
    {
        Harness harness = Create(Spring1SixAm);

        harness.Time.Advance(1);

        Assert.Equal(new GameTime(1, Season.Spring, 1, 6, 1), harness.Time.Now);
        Assert.Equal(1, harness.Bus.Count<MinuteTicked>());
        Assert.Equal(harness.Time.Now, harness.Bus.Last<MinuteTicked>().Time);
        Assert.Equal(0, harness.Bus.Count<HourChanged>());
        Assert.Equal(0, harness.Bus.Count<DayStarted>());
        Assert.Equal(0, harness.Bus.Count<DayPhaseChanged>());
        Assert.Equal(0, harness.Bus.Count<WeatherChanged>());
    }

    [Fact]
    public void Advance_跨小时_仅在整点发_HourChanged()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, 1, 6, 30));

        harness.Time.Advance(30);

        Assert.Equal(new GameTime(1, Season.Spring, 1, 7, 0), harness.Time.Now);
        Assert.Equal(30, harness.Bus.Count<MinuteTicked>());
        Assert.Equal(1, harness.Bus.Count<HourChanged>());
        Assert.Equal(new GameTime(1, Season.Spring, 1, 7, 0), harness.Bus.Last<HourChanged>().Time);
        Assert.Equal(0, harness.Bus.Count<DayStarted>());
    }

    [Fact]
    public void Advance_跨午夜_不发_DayStarted_也不换天气()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, 1, 23, 59));

        harness.Time.Advance(1);

        Assert.Equal(new GameTime(1, Season.Spring, 1, 0, 0), harness.Time.Now);   // 仍是第 1 日
        Assert.Equal(1, harness.Bus.Count<MinuteTicked>());
        Assert.Equal(1, harness.Bus.Count<HourChanged>());
        Assert.Equal(0, harness.Bus.Count<DayPhaseChanged>());   // 23:59 与 0:00 同属 LateNight
        Assert.Equal(0, harness.Bus.Count<DayStarted>());
        Assert.Equal(0, harness.Bus.Count<WeatherChanged>());
        Assert.Single(harness.WeatherGenerator.Calls);           // 构造时掷过一次，午夜不再掷
    }

    [Fact]
    public void Advance_跨入六点_发_DayStarted_并重掷当日天气()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, 1, 5, 59), Weather.Sunny, Weather.Rainy);

        harness.Time.Advance(1);

        Assert.Equal(new GameTime(1, Season.Spring, 2, 6, 0), harness.Time.Now);
        Assert.Equal(1, harness.Bus.Count<MinuteTicked>());
        Assert.Equal(1, harness.Bus.Count<HourChanged>());
        Assert.Equal(1, harness.Bus.Count<DayPhaseChanged>());
        Assert.Equal(1, harness.Bus.Count<DayStarted>());
        Assert.Equal(new GameTime(1, Season.Spring, 2, 6, 0), harness.Bus.Last<DayStarted>().Time);
        Assert.Equal(0, harness.Bus.Count<SeasonChanged>());
        Assert.Equal(Weather.Rainy, harness.Time.Weather);
        Assert.Equal(1, harness.Bus.Count<WeatherChanged>());
        Assert.Equal(Weather.Rainy, harness.Bus.Last<WeatherChanged>().Current);
        Assert.Equal(Weather.Sunny, harness.Bus.Last<WeatherChanged>().Previous);

        // 新一天用绝对日序 1 掷点，而不是「季内第 1 天」的 0
        Assert.Equal((Season.Spring, 1, Seed), harness.WeatherGenerator.Calls[1]);

        // 天气必须在 DayStarted 之前换好，否则订阅者会读到昨天的天气
        Assert.True(harness.Bus.IndexOfFirst<DayStarted>() < harness.Bus.IndexOfFirst<WeatherChanged>());
    }

    [Fact]
    public void Advance_跨入六点_事件次序符合契约()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, 1, 5, 59), Weather.Sunny, Weather.Rainy);

        harness.Time.Advance(1);

        Assert.Equal(
            new[]
            {
                nameof(MinuteTicked),
                nameof(HourChanged),
                nameof(DayPhaseChanged),
                nameof(DayStarted),
                nameof(WeatherChanged),
            },
            harness.Bus.Published.Select(evt => evt.GetType().Name));
    }

    [Fact]
    public void Advance_跨入新季六点_事件次序符合契约()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, GameTime.DaysPerSeason, 5, 59), Weather.Sunny, Weather.Rainy);

        harness.Time.Advance(1);

        Assert.Equal(
            new[]
            {
                nameof(MinuteTicked),
                nameof(HourChanged),
                nameof(DayPhaseChanged),
                nameof(DayStarted),
                nameof(SeasonChanged),
                nameof(WeatherChanged),
            },
            harness.Bus.Published.Select(evt => evt.GetType().Name));
    }

    [Fact]
    public void Advance_跨入六点但天气未变_不发_WeatherChanged()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, 1, 5, 59), Weather.Sunny);

        harness.Time.Advance(OneHour);

        Assert.Equal(1, harness.Bus.Count<DayStarted>());
        Assert.Equal(Weather.Sunny, harness.Time.Weather);
        Assert.Equal(0, harness.Bus.Count<WeatherChanged>());
    }

    [Fact]
    public void Advance_跨季_在新季首日六点发_SeasonChanged()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, GameTime.DaysPerSeason, 5, 59));

        harness.Time.Advance(1);

        Assert.Equal(new GameTime(1, Season.Summer, 1, 6, 0), harness.Time.Now);
        Assert.Equal(1, harness.Bus.Count<SeasonChanged>());
        Assert.Equal(Season.Summer, harness.Bus.Last<SeasonChanged>().Time.Season);
        Assert.Equal(Season.Spring, harness.Bus.Last<SeasonChanged>().Previous);
        Assert.Equal(1, harness.Bus.Count<DayStarted>());
        Assert.True(harness.Bus.IndexOfFirst<DayStarted>() < harness.Bus.IndexOfFirst<SeasonChanged>());
        Assert.Equal((Season.Summer, GameTime.DaysPerSeason, Seed), harness.WeatherGenerator.Calls[1]);
    }

    [Fact]
    public void Advance_跨年_年号递增并发_SeasonChanged()
    {
        Harness harness = Create(new GameTime(1, Season.Winter, GameTime.DaysPerSeason, 5, 59));

        harness.Time.Advance(1);

        Assert.Equal(new GameTime(2, Season.Spring, 1, 6, 0), harness.Time.Now);
        Assert.Equal(1, harness.Bus.Count<SeasonChanged>());
        Assert.Equal(Season.Spring, harness.Bus.Last<SeasonChanged>().Time.Season);
        Assert.Equal(Season.Winter, harness.Bus.Last<SeasonChanged>().Previous);
        Assert.Equal(1, harness.Bus.Count<DayStarted>());

        // 跨年后日序继续累加，不会与元年的同一天撞车
        Assert.Equal((Season.Spring, GameTime.SeasonsPerYear * GameTime.DaysPerSeason, Seed), harness.WeatherGenerator.Calls[1]);
    }

    [Theory]
    [InlineData(1, 2, DayPhase.Collapsed, DayPhase.LateNight)]
    [InlineData(8, 9, DayPhase.Forenoon, DayPhase.Morning)]
    [InlineData(21, 22, DayPhase.LateNight, DayPhase.Evening)]
    public void Advance_跨时段边界_发_DayPhaseChanged(int fromHour, int toHour, DayPhase current, DayPhase previous)
    {
        Harness harness = Create(new GameTime(1, Season.Spring, 1, fromHour, 0));

        harness.Time.Advance(OneHour);

        Assert.Equal(new GameTime(1, Season.Spring, 1, toHour, 0), harness.Time.Now);
        Assert.Equal(1, harness.Bus.Count<DayPhaseChanged>());
        Assert.Equal(current, harness.Bus.Last<DayPhaseChanged>().Current);
        Assert.Equal(previous, harness.Bus.Last<DayPhaseChanged>().Previous);
    }

    [Fact]
    public void Advance_清晨五点跨入六点_换日与换时段同发()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, 1, 5, 0));

        harness.Time.Advance(OneHour);

        Assert.Equal(new GameTime(1, Season.Spring, 2, 6, 0), harness.Time.Now);
        Assert.Equal(1, harness.Bus.Count<HourChanged>());
        Assert.Equal(1, harness.Bus.Count<DayPhaseChanged>());
        Assert.Equal(DayPhase.Morning, harness.Bus.Last<DayPhaseChanged>().Current);
        Assert.Equal(DayPhase.Collapsed, harness.Bus.Last<DayPhaseChanged>().Previous);
        Assert.Equal(1, harness.Bus.Count<DayStarted>());
    }

    [Fact]
    public void Advance_逐分钟推进与一次推进等价()
    {
        Harness stepwise = Create(new GameTime(1, Season.Spring, 1, 5, 30));
        Harness atOnce = Create(new GameTime(1, Season.Spring, 1, 5, 30));

        for (int minute = 0; minute < 120; minute++) stepwise.Time.Advance(1);
        atOnce.Time.Advance(120);

        Assert.Equal(new GameTime(1, Season.Spring, 2, 7, 30), atOnce.Time.Now);
        Assert.Equal(atOnce.Time.Now, stepwise.Time.Now);
        Assert.Equal(atOnce.Time.Weather, stepwise.Time.Weather);
        Assert.Equal(atOnce.Bus.Count<MinuteTicked>(), stepwise.Bus.Count<MinuteTicked>());
        Assert.Equal(atOnce.Bus.Count<HourChanged>(), stepwise.Bus.Count<HourChanged>());
        Assert.Equal(atOnce.Bus.Count<DayStarted>(), stepwise.Bus.Count<DayStarted>());
        Assert.Equal(atOnce.Bus.Count<DayPhaseChanged>(), stepwise.Bus.Count<DayPhaseChanged>());
    }

    [Fact]
    public void IsPaused_时_Advance_无效()
    {
        Harness harness = Create(Spring1SixAm);
        harness.Time.IsPaused = true;

        harness.Time.Advance(600);

        Assert.Equal(Spring1SixAm, harness.Time.Now);
        Assert.Empty(harness.Bus.Published);

        harness.Time.IsPaused = false;
        harness.Time.Advance(1);

        Assert.Equal(new GameTime(1, Season.Spring, 1, 6, 1), harness.Time.Now);
        Assert.Equal(1, harness.Bus.Count<MinuteTicked>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Advance_非正数_无效(int minutes)
    {
        Harness harness = Create(Spring1SixAm);

        harness.Time.Advance(minutes);

        Assert.Equal(Spring1SixAm, harness.Time.Now);
        Assert.Empty(harness.Bus.Published);
    }

    [Fact]
    public void Sleep_跳到次日六点_不发分钟事件()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, 1, 14, 0));

        harness.Time.Sleep();

        Assert.Equal(new GameTime(1, Season.Spring, 2, 6, 0), harness.Time.Now);
        Assert.Equal(0, harness.Bus.Count<MinuteTicked>());
        Assert.Equal(0, harness.Bus.Count<HourChanged>());
        Assert.Equal(1, harness.Bus.Count<DayStarted>());
        Assert.Equal(new GameTime(1, Season.Spring, 2, 6, 0), harness.Bus.Last<DayStarted>().Time);
        Assert.Equal(1, harness.Bus.Count<DayPhaseChanged>());
        Assert.Equal(DayPhase.Morning, harness.Bus.Last<DayPhaseChanged>().Current);
        Assert.Equal(DayPhase.Afternoon, harness.Bus.Last<DayPhaseChanged>().Previous);
    }

    [Fact]
    public void Sleep_重掷当日天气()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, 1, 14, 0), Weather.Sunny, Weather.Storm);

        harness.Time.Sleep();

        Assert.Equal(Weather.Storm, harness.Time.Weather);
        Assert.Equal(1, harness.Bus.Count<WeatherChanged>());
        Assert.Equal(Weather.Storm, harness.Bus.Last<WeatherChanged>().Current);
        Assert.Equal(Weather.Sunny, harness.Bus.Last<WeatherChanged>().Previous);
        Assert.Equal((Season.Spring, 1, Seed), harness.WeatherGenerator.Calls[1]);
    }

    [Fact]
    public void Sleep_跨季_发_SeasonChanged()
    {
        Harness harness = Create(new GameTime(1, Season.Spring, GameTime.DaysPerSeason, 20, 0));

        harness.Time.Sleep();

        Assert.Equal(new GameTime(1, Season.Summer, 1, 6, 0), harness.Time.Now);
        Assert.Equal(1, harness.Bus.Count<SeasonChanged>());
        Assert.Equal(Season.Summer, harness.Bus.Last<SeasonChanged>().Time.Season);
        Assert.Equal(Season.Spring, harness.Bus.Last<SeasonChanged>().Previous);
        Assert.True(harness.Bus.IndexOfFirst<DayStarted>() < harness.Bus.IndexOfFirst<SeasonChanged>());
    }

    [Fact]
    public void Sleep_跨年_年号递增()
    {
        Harness harness = Create(new GameTime(1, Season.Winter, GameTime.DaysPerSeason, 20, 0));

        harness.Time.Sleep();

        Assert.Equal(new GameTime(2, Season.Spring, 1, 6, 0), harness.Time.Now);
        Assert.Equal(1, harness.Bus.Count<SeasonChanged>());
        Assert.Equal(Season.Winter, harness.Bus.Last<SeasonChanged>().Previous);
    }

    [Theory]
    [InlineData(1, Season.Spring, 1)]
    [InlineData(1, Season.Spring, GameTime.DaysPerSeason)]
    [InlineData(1, Season.Winter, GameTime.DaysPerSeason)]
    public void Sleep_与自然跨过日界等价(int year, Season season, int day)
    {
        var beforeBoundary = new GameTime(year, season, day, 5, 59);
        Harness natural = Create(beforeBoundary, Weather.Sunny, Weather.Rainy);
        Harness slept = Create(beforeBoundary, Weather.Sunny, Weather.Rainy);

        natural.Time.Advance(1);   // 自然跨过 6:00
        slept.Time.Sleep();        // 跳到次日 6:00

        Assert.Equal(natural.Time.Now, slept.Time.Now);
        Assert.Equal(natural.Time.Weather, slept.Time.Weather);
        Assert.Equal(natural.WeatherGenerator.Calls[1], slept.WeatherGenerator.Calls[1]);
        Assert.Equal(BoundaryEvents(natural.Bus), BoundaryEvents(slept.Bus));
    }

    [Fact]
    public void Sleep_与推进同样长的时间等价()
    {
        var start = new GameTime(1, Season.Spring, 1, 14, 0);
        Harness natural = Create(start, Weather.Sunny, Weather.Rainy);
        Harness slept = Create(start, Weather.Sunny, Weather.Rainy);

        natural.Time.Advance(16 * 60);   // 14:00 走满 16 小时，正好落在次日 6:00
        slept.Time.Sleep();

        Assert.Equal(natural.Time.Now, slept.Time.Now);
        Assert.Equal(natural.Time.Weather, slept.Time.Weather);
        Assert.Equal(natural.WeatherGenerator.Calls.Count, slept.WeatherGenerator.Calls.Count);   // 都只掷一次新天气
        Assert.Equal(natural.WeatherGenerator.Calls[1], slept.WeatherGenerator.Calls[1]);
    }

    /// <summary>
    /// 日界事件序列，滤掉随分钟流逝的 tick——Sleep 是跳跃不逐分钟发 tick，但跨天本身两条路径必须一致。
    /// </summary>
    private static List<string> BoundaryEvents(FakeEventBus bus)
        => bus.Published
            .Where(evt => evt is not MinuteTicked and not HourChanged)
            .Select(evt => evt.ToString() ?? string.Empty)
            .ToList();

    private sealed record Harness
    {
        public Harness(FakeEventBus bus, StubWeatherGenerator weatherGenerator, GameTime start, int worldSeed = Seed)
        {
            Bus = bus;
            WeatherGenerator = weatherGenerator;
            Time = new TimeService(bus, weatherGenerator, new TimeConfig(), worldSeed, start);
        }

        public FakeEventBus Bus { get; }

        public StubWeatherGenerator WeatherGenerator { get; }

        public TimeService Time { get; }
    }

    /// <summary>
    /// 手写替身，不引 Moq：本切片只要求「按调用次序给出天气」与「记下掷点参数」两件事。
    /// </summary>
    private sealed class StubWeatherGenerator : IWeatherGenerator
    {
        private readonly Queue<Weather> _scripted;
        private Weather _current = Weather.Sunny;

        public StubWeatherGenerator(params Weather[] scripted) => _scripted = new Queue<Weather>(scripted);

        public List<(Season Season, int DayIndex, int Seed)> Calls { get; } = new();

        public Weather Roll(Season season, int dayIndex, int seed)
        {
            Calls.Add((season, dayIndex, seed));
            if (_scripted.Count > 0) _current = _scripted.Dequeue();
            return _current;
        }
    }

    private sealed class FakeEventBus : IEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();

        public List<object> Published { get; } = new();

        public void Publish<T>(in T evt)
        {
            Published.Add(evt!);

            if (!_handlers.TryGetValue(typeof(T), out var handlers)) return;
            foreach (var handler in handlers.ToArray()) ((Action<T>)handler)(evt);
        }

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (!_handlers.TryGetValue(typeof(T), out var handlers))
            {
                handlers = new List<Delegate>();
                _handlers[typeof(T)] = handlers;
            }

            handlers.Add(handler);
            return new Unsubscriber(() => handlers.Remove(handler));
        }

        public int Count<T>() => Published.Count(evt => evt is T);

        public T Last<T>() => Published.OfType<T>().Last();

        public int IndexOfFirst<T>() => Published.FindIndex(evt => evt is T);

        private sealed class Unsubscriber : IDisposable
        {
            private Action? _unsubscribe;

            public Unsubscriber(Action unsubscribe) => _unsubscribe = unsubscribe;

            public void Dispose()
            {
                _unsubscribe?.Invoke();
                _unsubscribe = null;
            }
        }
    }
}
