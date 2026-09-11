using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Core.Events;
using XingGame.Core.Time;

namespace XingGame.Tests;

/// <summary>
/// 时间系统的边界（ADR-006、ARCHITECTURE「日界与事件时序」）。<see cref="TimeServiceTests"/> 已覆盖
/// 逐分钟推进、事件次序、休眠与暂停，这里补的是**订阅者视角**与**长跨度/重入**这两类没人走过的地方：
/// 事件里能不能读到正确的当下状态、读档会不会把事件重放一遍。
/// </summary>
public sealed class EdgeCaseTests_Time
{
    private const int Seed = 20260911;

    /// <summary>全年分钟数。日界 6:00，故「整年」正好回到同一时刻、年号 +1。</summary>
    private const int MinutesPerYear = 1440 * GameTime.DaysPerSeason * GameTime.SeasonsPerYear;

    private static TimeService NewTime(GameTime? start = null) =>
        new(new EventBus(), new WeatherGenerator(WeatherTable.LoadDefault()), worldSeed: Seed, start: start);

    [Fact]
    public void 日界_订阅者读到的天气已经是新的一天_而不是昨天的()
    {
        // 契约：天气必须**先**换再发 DayStarted（ARCHITECTURE「日界与事件时序」）。
        // 雨天自动浇水这类订阅者在 DayStarted 回调里读 Weather，读到昨天的就会把昨天浇一遍、
        // 今天不浇——错误会推迟到作物枯死时才显形。这条把次序钉死在订阅者能观察到的那一面。
        var bus = new EventBus();
        var time = new TimeService(bus, new WeatherGenerator(WeatherTable.LoadDefault()), worldSeed: Seed);

        // 观察一：订阅者在 DayStarted 里读到的天气
        var readInHandler = new List<Weather>();
        bus.Subscribe<DayStarted>(_ => readInHandler.Add(time.Weather));

        // 观察二：独立地按日推进，每天推进结束后（日界已过、天气已换）记一次
        var afterDayAdvanced = new List<Weather>();
        for (int day = 0; day < 40; day++)
        {
            time.Advance(1440);
            afterDayAdvanced.Add(time.Weather);
        }

        Assert.Equal(40, readInHandler.Count);
        Assert.Equal(afterDayAdvanced, readInHandler);

        // 四十天里天气总得变过，否则两个观察永远相等，这条用例什么都没证明
        Assert.True(
            afterDayAdvanced.Distinct().Count() > 1,
            "40 天里天气始终没变，本用例失去意义");
    }

    [Fact]
    public void 日界_WeatherChanged_的旧天气参数是前一天的那个()
    {
        // 事件参数错了照样能跑：订阅者拿 Previous 去收尾（比如「昨天的雨把地浇了」），
        // 拿到新值就会把同一天算两遍。参数只有用例盯着才守得住。
        var bus = new EventBus();
        var time = new TimeService(bus, new WeatherGenerator(WeatherTable.LoadDefault()), worldSeed: Seed);
        var seen = new List<(Weather Current, Weather Previous)>();
        bus.Subscribe<WeatherChanged>(evt => seen.Add((evt.Current, evt.Previous)));

        for (int minute = 0; minute < 1440 * 40; minute++) time.Advance(1);

        Assert.NotEmpty(seen);
        for (int index = 0; index < seen.Count; index++)
        {
            Assert.NotEqual(seen[index].Previous, seen[index].Current);

            if (index > 0) Assert.Equal(seen[index - 1].Current, seen[index].Previous);
        }

        Assert.Equal(time.Weather, seen[^1].Current);
    }

    [Fact]
    public void 读档_不重放任何事件()
    {
        // 契约：读档是整状态覆盖，不发任何事件。理由有二——组合根在 _Ready 里读档时订阅者
        // 多半还没建好；就算建好了，重放跨天事件也会让按 tick 记账的订阅者多算一天。
        // 若哪天有人给 Deserialize 加上「补发事件」，游戏会在读档瞬间静默多做一天的事。
        var bus = new EventBus();
        var time = new TimeService(bus, new WeatherGenerator(WeatherTable.LoadDefault()), worldSeed: Seed);
        time.Advance(1440 * 40);
        string json = time.Serialize();
        GameTime expected = time.Now;
        Weather expectedWeather = time.Weather;

        int events = 0;
        bus.Subscribe<MinuteTicked>(_ => events++);
        bus.Subscribe<HourChanged>(_ => events++);
        bus.Subscribe<DayStarted>(_ => events++);
        bus.Subscribe<SeasonChanged>(_ => events++);
        bus.Subscribe<DayPhaseChanged>(_ => events++);
        bus.Subscribe<WeatherChanged>(_ => events++);

        time.Deserialize(json, fromVersion: 1);

        Assert.Equal(0, events);

        // 不发事件 ≠ 什么都没做：状态必须确实被覆盖，否则上面那句等于没验
        Assert.Equal(expected, time.Now);
        Assert.Equal(expectedWeather, time.Weather);
    }

    [Fact]
    public void Sleep_从昏倒线之后的凌晨睡去_只跳到当天清晨六点()
    {
        // 日界在 6:00，所以 0:00–5:59 属于**同一个游戏日**。凌晨三点睡下去就是睡到当天 6:00
        // （三个小时后），不是「次日」6:00——写成「Day + 1」的实现在这里会多跳一整天，
        // 而玩家看到的是「睡了一觉过了一天」，只会觉得日期莫名其妙。
        TimeService time = NewTime(new GameTime(1, Season.Spring, 1, GameTime.FirstHour, 0));
        time.Advance(21 * 60);   // 春1日 6:00 → 春1日 3:00（游戏日仍是 1）

        GameTime before = time.Now;
        Assert.Equal(3, before.Hour);
        Assert.Equal(1, before.Day);

        time.Sleep();

        Assert.Equal(new GameTime(1, Season.Spring, 2, GameTime.FirstHour, 0), time.Now);
        Assert.Equal(3 * 60, time.Now.TotalMinutes - before.TotalMinutes);
    }

    [Fact]
    public void Advance_推进整年_回到同一时刻而年号加一()
    {
        // 跨年换算的边界：一年 = 4 季 × 28 天，推进整年后必须**分毫不差**地回到 6:00，
        // 且年号 +1、季节回春。取整/取模写反的话，误差会随年数累积，玩到第三年就露馅。
        var bus = new EventBus();
        var time = new TimeService(bus, new WeatherGenerator(WeatherTable.LoadDefault()), worldSeed: Seed);
        var seasons = new List<(GameTime Time, Season Previous)>();
        bus.Subscribe<SeasonChanged>(evt => seasons.Add((evt.Time, evt.Previous)));

        time.Advance(MinutesPerYear);

        Assert.Equal(new GameTime(2, Season.Spring, 1, GameTime.FirstHour, 0), time.Now);
        Assert.Equal(MinutesPerYear, time.Now.TotalMinutes);

        // 走满一年必然经过三个季界（春→夏、夏→秋、秋→冬）；冬→春那次是跨年，同样要发
        Assert.Equal(4, seasons.Count);
        Assert.Equal(Season.Winter, seasons[^1].Previous);
        Assert.Equal(Season.Spring, seasons[^1].Time.Season);
        Assert.Equal(2, seasons[^1].Time.Year);
    }

    [Fact]
    public void 回调里读_Now_是本分钟的值_而不是整批跑完的值()
    {
        // Advance(5) 是「跑五轮 TickOneMinute」，不是「先把时间加 5 再发事件」。
        // 订阅者（打更音效、buff 计时）在回调里读 Now 必须拿到这一分钟——若哪天改成
        // 先批量推进再补发事件，所有按 tick 记账的系统都会集体对不上时间，而且不报错。
        var bus = new EventBus();
        var time = new TimeService(bus, new WeatherGenerator(WeatherTable.LoadDefault()), worldSeed: Seed);
        var seen = new List<int>();

        bus.Subscribe<MinuteTicked>(evt =>
        {
            Assert.Equal(evt.Time, time.Now);                 // 事件里的时刻就是当下的 Now
            seen.Add(evt.Time.TotalMinutes);
        });

        time.Advance(5);

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, seen);
    }

    [Fact]
    public void 回调里重入_Advance_分钟数累加_不多不少()
    {
        // 处理函数里再推时间是允许的（EventBus 本就是同步可重入的）。要守住的是
        // 「外层那批一分钟都不会丢」：外层循环用的是 for 计数而非「目标时刻快照」，
        // 若改成后者，内层推进的分钟会被外层当成已完成而吞掉，时间凭空少走几分钟。
        var bus = new EventBus();
        var time = new TimeService(bus, new WeatherGenerator(WeatherTable.LoadDefault()), worldSeed: Seed);
        int ticks = 0;
        bool reentered = false;

        bus.Subscribe<MinuteTicked>(_ =>
        {
            ticks++;
            if (reentered) return;

            reentered = true;
            time.Advance(3);   // 第一个 tick 的回调里再推三分钟
        });

        time.Advance(5);

        Assert.Equal(8, ticks);                            // 5 + 3，一分钟都没被吞
        Assert.Equal(8, time.Now.TotalMinutes);
    }

    [Fact]
    public void 反序列化_天气是枚举外的数字_当场抛_而不是读成_99()
    {
        // JsonStringEnumConverter 默认接受数字，所以 {"Weather":99} 会被静默读成 (Weather)99：
        // 不报错，而是渗进天气序列——全天所有 switch (Weather) 走 default、次日
        // WeatherChanged.Previous 带着 99、HUD 显示乱值，而且它会被原样写回存档。
        // 这正是 ADR-009「读错数据比读不出来更糟」要防的：坏值在读的那一刻就该被挡住。
        // 同一段 Validate 里 Season 有 Enum.IsDefined 把关，Weather 却漏了。
        TimeService time = NewTime();
        const string badWeather =
            """{"Year":1,"Season":"Spring","Day":1,"Hour":6,"Minute":0,"Weather":99}""";

        var error = Assert.Throws<InvalidDataException>(() => time.Deserialize(badWeather, fromVersion: 1));

        // 消息必须带非法值：否则现场只剩「读档失败」，查不出是哪个字段、哪个值
        Assert.Contains("99", error.Message);
    }

    [Fact]
    public void 反序列化_合法的天气值_名字与数字都照读()
    {
        // 加校验时最容易顺手的两种过火：①把 JsonStringEnumConverter 改成一律拒绝数字——
        // 那会影响所有存档的解析路径，超出本切片；②把合法范围写窄。这条同时钉住
        // 「名字照读」与「枚举定义域内的数字照读」，免得修复把正常档一起拒了。
        TimeService byName = NewTime();
        byName.Deserialize(
            """{"Year":1,"Season":"Spring","Day":1,"Hour":6,"Minute":0,"Weather":"Rainy"}""", fromVersion: 1);
        Assert.Equal(Weather.Rainy, byName.Weather);

        TimeService byNumber = NewTime();
        byNumber.Deserialize(
            """{"Year":1,"Season":"Spring","Day":1,"Hour":6,"Minute":0,"Weather":2}""", fromVersion: 1);
        Assert.Equal(Weather.Storm, byNumber.Weather);

        // 合法值照读之外还得确认状态真的落进去了，否则「不抛」可能只是校验把整段读档跳过了
        Assert.Equal(new GameTime(1, Season.Spring, 1, 6, 0), byName.Now);
    }

    [Fact]
    public void 反序列化_天气落在枚举定义域两侧_都当场抛()
    {
        // 定义域是 0..(Weather.Foggy)。边界值恰是「枚举减一」「循环里多算一格」这类真会写出来的
        // bug 的产物：只挡明显离谱的大数（比如 99）会漏掉它们，而它们同样会让 switch 走 default。
        foreach (string weather in new[] { "-1", ((int)Weather.Foggy + 1).ToString() })
        {
            TimeService time = NewTime();
            string json =
                """{"Year":1,"Season":"Spring","Day":1,"Hour":6,"Minute":0,"Weather":VALUE}"""
                    .Replace("VALUE", weather);

            Assert.Throws<InvalidDataException>(() => time.Deserialize(json, fromVersion: 1));
        }
    }
}
