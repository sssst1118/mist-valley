using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Core.Events;
using XingGame.Core.Save;
using XingGame.Core.Time;

namespace XingGame.Tests;

/// <summary>
/// 时间系统接存档（M1-2）。SQLite 那部分跑在真文件上而非替身——存档的错大多出在
/// 「存下去的和读回来的不是一回事」，而替身恰好会把这类错抹平。
/// </summary>
public sealed class TimeServiceSaveTests : IDisposable
{
    private const int Seed = 20260911;
    private const int Slot = 1;

    private static readonly GameTime Spring1SixAm = new(1, Season.Spring, 1, GameTime.FirstHour, 0);

    private readonly string _saveDirectory =
        Path.Combine(Path.GetTempPath(), "xing-time-save-" + Guid.NewGuid().ToString("N"));

    /// <summary>SQLite 连接池可能还攥着文件句柄，清不掉临时目录不该让测试失败。</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_saveDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void 序列化往返_年月季日时分六个字段逐一还原()
    {
        TimeService original = NewTimeService(Seed, new GameTime(3, Season.Autumn, 17, 21, 43));

        TimeService restored = RoundTrip(original);

        Assert.Equal(3, restored.Now.Year);
        Assert.Equal(Season.Autumn, restored.Now.Season);
        Assert.Equal(17, restored.Now.Day);
        Assert.Equal(21, restored.Now.Hour);
        Assert.Equal(43, restored.Now.Minute);
        Assert.Equal(original.Now, restored.Now);
        Assert.Equal(original.Weather, restored.Weather);
    }

    [Fact]
    public void 序列化往返_把天气一并还原()
    {
        TimeService original = NewTimeService(Seed, new GameTime(1, Season.Spring, 1, 6, 0));
        // 雪只在冬天出现（§3.3），挑它来验「不是初始值的那种天气」也能还原；400 天 ≈ 三个冬天
        for (int day = 0; day < 400 && original.Weather != Weather.Snowy; day++) original.Sleep();

        TimeService restored = RoundTrip(original);

        Assert.Equal(Weather.Snowy, original.Weather);
        Assert.Equal(Weather.Snowy, restored.Weather);
        Assert.Equal(original.Now, restored.Now);
    }

    [Fact]
    public void 同一种子重开_必得同一份天气序列()
    {
        TimeService first = NewTimeService(Seed);
        var sequence = new List<Weather>();
        for (int day = 0; day < 60; day++)
        {
            first.Sleep();
            sequence.Add(first.Weather);
        }

        // 60 天里天气要是没变过，下面两条断言就什么都没证明
        Assert.True(sequence.Distinct().Count() > 1, "60 天里天气始终没变，本用例失去意义");

        TimeService reopened = RoundTrip(first);
        Assert.Equal(first.Weather, reopened.Weather);

        // 重开 = 同一种子 + 同一份存档；逐日比对，任一天不同都说明存档漏了什么
        TimeService twin = NewTimeService(Seed);
        for (int day = 0; day < sequence.Count; day++)
        {
            twin.Sleep();
            Assert.Equal(sequence[day], twin.Weather);
        }
    }

    [Fact]
    public void 跨天换了天气之后存档_读回来是新那天的天气()
    {
        TimeService time = NewTimeService(Seed);
        Weather before = time.Weather;

        for (int i = 0; i < 60 && time.Weather == before; i++) time.Sleep();
        Assert.NotEqual(before, time.Weather);

        TimeService restored = RoundTrip(time);

        Assert.Equal(time.Now, restored.Now);
        Assert.Equal(time.Weather, restored.Weather);
        Assert.NotEqual(before, restored.Weather);
    }

    [Fact]
    public void 反序列化_fromVersion_为_1_时正常工作()
    {
        TimeService original = NewTimeService(Seed, new GameTime(2, Season.Winter, 28, 23, 59));
        TimeService restored = NewTimeService(Seed);

        restored.Deserialize(original.Serialize(), fromVersion: 1);

        Assert.Equal(original.Now, restored.Now);
    }

    [Fact]
    public void 反序列化_拒绝来自更新版本的存档()
    {
        TimeService time = NewTimeService(Seed);

        // 猜着读比读不出来更糟：宁可拒绝，也不能把不认识的字段当真
        Assert.Throws<NotSupportedException>(() => time.Deserialize(time.Serialize(), fromVersion: 2));
    }

    [Fact]
    public void 反序列化_字段越界当场报错_而不是算出不存在的一天()
    {
        TimeService time = NewTimeService(Seed);
        const string day29 = """{"Year":1,"Season":"Spring","Day":29,"Hour":6,"Minute":0,"Weather":"Sunny"}""";

        Assert.Throws<InvalidDataException>(() => time.Deserialize(day29, fromVersion: 1));
    }

    [Fact]
    public void 反序列化_内容不是合法_JSON_时抛_JsonException()
    {
        TimeService time = NewTimeService(Seed);

        Assert.Throws<JsonException>(() => time.Deserialize("这不是 JSON", fromVersion: 1));
    }

    [Fact]
    public void 存档_blob_里不含世界种子_它只住在_meta()
    {
        string json = NewTimeService(Seed).Serialize();

        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
            Assert.False(
                property.Name.Contains("seed", StringComparison.OrdinalIgnoreCase),
                $"时间 Blob 里出现了 {property.Name}——世界种子存两份早晚会对不上");
    }

    [Fact]
    public void SQLite_往返_种子从_meta_取回_时刻与天气与存档时一致()
    {
        var saves = NewSaveService();
        TimeService time = NewTimeService(Seed, new GameTime(1, Season.Spring, 3, 14, 30));
        time.Advance(90);   // 走一段再存，否则「还原」与「初始值恰好相同」分不开

        Assert.False(saves.Exists(Slot));
        saves.Save(Slot, Meta(Seed), new ISaveable[] { time });
        Assert.True(saves.Exists(Slot));

        // 第一段：只读 meta 就能拿到种子——TimeService 的构造正等着它（ADR-009）
        Assert.True(saves.TryPeekWorldSeed(Slot, out int peeked));
        Assert.Equal(Seed, peeked);

        // 第二段：用 peek 到的种子构造，再反序列化各系统的 blob
        TimeService reloaded = NewTimeService(peeked);
        Assert.True(saves.Load(Slot, new ISaveable[] { reloaded }));

        Assert.Equal(time.Now.Year, reloaded.Now.Year);
        Assert.Equal(time.Now.Season, reloaded.Now.Season);
        Assert.Equal(time.Now.Day, reloaded.Now.Day);
        Assert.Equal(time.Now.Hour, reloaded.Now.Hour);
        Assert.Equal(time.Now.Minute, reloaded.Now.Minute);
        Assert.Equal(time.Now, reloaded.Now);
        Assert.Equal(time.Weather, reloaded.Weather);
    }

    [Fact]
    public void 不同种子的存档_读回来各是自己的时刻与天气()
    {
        var saves = NewSaveService();

        TimeService spring = NewTimeService(Seed);
        TimeService winter = NewTimeService(Seed + 1);
        for (int day = 0; day < 30; day++) winter.Sleep();

        saves.Save(1, Meta(Seed), new ISaveable[] { spring });
        saves.Save(2, Meta(Seed + 1), new ISaveable[] { winter });

        TimeService springBack = NewTimeService(Seed);
        TimeService winterBack = NewTimeService(Seed + 1);
        Assert.True(saves.Load(1, new ISaveable[] { springBack }));
        Assert.True(saves.Load(2, new ISaveable[] { winterBack }));

        Assert.Equal(spring.Now, springBack.Now);
        Assert.Equal(spring.Weather, springBack.Weather);
        Assert.Equal(winter.Now, winterBack.Now);
        Assert.Equal(winter.Weather, winterBack.Weather);
    }

    /// <summary>存→读。故意用与存档不同的初始时刻构造，证明结果是被存档覆盖的，不是初始值恰好相同。</summary>
    private static TimeService RoundTrip(TimeService original)
    {
        string json = original.Serialize();

        TimeService restored = NewTimeService(Seed, Spring1SixAm);
        restored.Deserialize(json, fromVersion: 1);
        return restored;
    }

    private static TimeService NewTimeService(int seed, GameTime? start = null) =>
        new(new EventBus(), new WeatherGenerator(WeatherTable.LoadDefault()), worldSeed: seed, start: start);

    private SqliteSaveService NewSaveService() => new(_saveDirectory);

    private static SaveMeta Meta(int worldSeed) => new(worldSeed, "测试农场", "第 1 年 春 3 日 16:00");
}
