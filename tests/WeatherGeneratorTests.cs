using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Core.Time;

namespace XingGame.Tests;

public class WeatherGeneratorTests
{
    private static readonly Season[] Seasons = { Season.Spring, Season.Summer, Season.Autumn, Season.Winter };
    private static readonly Weather[] EveryWeather = Enum.GetValues<Weather>();
    private static readonly int[] Seeds = { 0, 1, 42, -7, 987654321 };

    private static WeatherGenerator NewGenerator() => new(WeatherTable.LoadDefault());

    [Fact]
    public void Roll_同参数必得同结果()
    {
        WeatherGenerator generator = NewGenerator();

        foreach (Season season in Seasons)
        foreach (int seed in Seeds)
        for (int dayIndex = 0; dayIndex < 100; dayIndex++)
        {
            Weather first = generator.Roll(season, dayIndex, seed);
            Weather second = generator.Roll(season, dayIndex, seed);

            Assert.Equal(first, second);
            Assert.Contains(first, EveryWeather);
        }
    }

    [Fact]
    public void Roll_与调用顺序无关()
    {
        WeatherGenerator first = NewGenerator();
        WeatherGenerator second = NewGenerator();

        for (int dayIndex = 0; dayIndex < 500; dayIndex++)
        {
            Season season = Seasons[dayIndex % Seasons.Length];
            second.Roll(Season.Winter, dayIndex + 1000, 99);   // 打乱顺序，不该影响结果
            Assert.Equal(first.Roll(season, dayIndex, 5), second.Roll(season, dayIndex, 5));
        }
    }

    [Fact]
    public void Roll_不同种子给出不同序列()
    {
        WeatherGenerator generator = NewGenerator();

        int differences = 0;
        for (int dayIndex = 0; dayIndex < 200; dayIndex++)
            if (generator.Roll(Season.Spring, dayIndex, 1) != generator.Roll(Season.Spring, dayIndex, 2))
                differences++;

        Assert.True(differences > 40, $"两个种子在 200 天里只有 {differences} 天不同，种子没起作用");
    }

    [Fact]
    public void Roll_同一天气不会常年不变()
    {
        WeatherGenerator generator = NewGenerator();

        int distinct = Enumerable.Range(0, 200)
            .Select(dayIndex => generator.Roll(Season.Spring, dayIndex, 3))
            .Distinct()
            .Count();

        Assert.True(distinct >= 3, $"200 天里只出现了 {distinct} 种天气，掷点退化成了常量");
    }

    [Theory]
    [InlineData(Season.Spring)]
    [InlineData(Season.Summer)]
    [InlineData(Season.Autumn)]
    [InlineData(Season.Winter)]
    public void Roll_只产出本季权重非零的天气(Season season)
    {
        WeatherGenerator generator = NewGenerator();
        List<Weather> allowed = EveryWeather.Where(weather => generator.Table.CanOccur(season, weather)).ToList();

        var seen = new HashSet<Weather>();
        foreach (int seed in Seeds)
        for (int dayIndex = 0; dayIndex < 1000; dayIndex++)
        {
            Weather weather = generator.Roll(season, dayIndex, seed);

            Assert.Contains(weather, allowed);
            seen.Add(weather);
        }

        Assert.Equal(allowed.Count, seen.Count);
    }

    [Fact]
    public void Roll_冬季绝不出暴风雨()
        => AssertNoWeather(Season.Winter, Weather.Storm);

    [Fact]
    public void Roll_夏季绝不出雪天与雾天()
    {
        AssertNoWeather(Season.Summer, Weather.Snowy);
        AssertNoWeather(Season.Summer, Weather.Foggy);
    }

    [Fact]
    public void Roll_春季绝不出雪天()
        => AssertNoWeather(Season.Spring, Weather.Snowy);

    [Fact]
    public void Roll_大样本分布接近权重()
    {
        const int SamplesPerSeason = 20000;
        const double Tolerance = 0.02;

        WeatherGenerator generator = NewGenerator();

        foreach (Season season in Seasons)
        {
            var counts = EveryWeather.ToDictionary(weather => weather, _ => 0);
            for (int dayIndex = 0; dayIndex < SamplesPerSeason; dayIndex++)
                counts[generator.Roll(season, dayIndex, 8)]++;

            foreach (Weather weather in EveryWeather)
            {
                double expected = generator.Table[season, weather] / (double)WeatherTable.ExpectedTotal;
                double actual = counts[weather] / (double)SamplesPerSeason;

                Assert.True(
                    Math.Abs(expected - actual) <= Tolerance,
                    $"{season} 的 {weather} 期望 {expected:P1}，实测 {actual:P1}");
            }
        }
    }

    [Fact]
    public void 权重表_四季合计各为一百()
    {
        WeatherTable table = WeatherTable.LoadDefault();

        foreach (Season season in Seasons)
            Assert.Equal(WeatherTable.ExpectedTotal, table.Total(season));
    }

    [Theory]
    [InlineData(Season.Spring, Weather.Sunny, 60)]
    [InlineData(Season.Spring, Weather.Rainy, 25)]
    [InlineData(Season.Spring, Weather.Storm, 5)]
    [InlineData(Season.Spring, Weather.Snowy, 0)]
    [InlineData(Season.Spring, Weather.Windy, 5)]
    [InlineData(Season.Spring, Weather.Foggy, 5)]
    [InlineData(Season.Summer, Weather.Sunny, 70)]
    [InlineData(Season.Summer, Weather.Rainy, 20)]
    [InlineData(Season.Summer, Weather.Storm, 8)]
    [InlineData(Season.Summer, Weather.Snowy, 0)]
    [InlineData(Season.Summer, Weather.Windy, 2)]
    [InlineData(Season.Summer, Weather.Foggy, 0)]
    [InlineData(Season.Autumn, Weather.Sunny, 55)]
    [InlineData(Season.Autumn, Weather.Rainy, 30)]
    [InlineData(Season.Autumn, Weather.Storm, 5)]
    [InlineData(Season.Autumn, Weather.Snowy, 0)]
    [InlineData(Season.Autumn, Weather.Windy, 5)]
    [InlineData(Season.Autumn, Weather.Foggy, 5)]
    [InlineData(Season.Winter, Weather.Sunny, 50)]
    [InlineData(Season.Winter, Weather.Rainy, 10)]
    [InlineData(Season.Winter, Weather.Storm, 0)]
    [InlineData(Season.Winter, Weather.Snowy, 30)]
    [InlineData(Season.Winter, Weather.Windy, 5)]
    [InlineData(Season.Winter, Weather.Foggy, 5)]
    public void 权重表_数值与设计文档_3_3_逐格一致(Season season, Weather weather, int expected)
    {
        Assert.Equal(expected, WeatherTable.LoadDefault()[season, weather]);
    }

    [Fact]
    public void 权重表_缺省文件可从构建输出上溯找到()
    {
        WeatherTable table = WeatherTable.LoadDefault();

        Assert.True(table.CanOccur(Season.Winter, Weather.Snowy));
        Assert.False(table.CanOccur(Season.Winter, Weather.Storm));
        Assert.False(table.CanOccur(Season.Summer, Weather.Foggy));
    }

    [Fact]
    public void 权重表_合计不为一百时报错()
    {
        const string broken = "{ \"spring\": { \"sunny\": 60, \"rainy\": 25, \"storm\": 5, \"windy\": 5, \"foggy\": 4 } }";

        var error = Assert.Throws<InvalidDataException>(() => WeatherTable.FromJson(broken));

        Assert.Contains("合计", error.Message);
    }

    [Fact]
    public void 权重表_未知天气名时报错()
    {
        const string broken = "{ \"spring\": { \"sunny\": 100, \"hail\": 0 } }";

        Assert.Throws<InvalidDataException>(() => WeatherTable.FromJson(broken));
    }

    [Fact]
    public void 权重表_季节名大小写不敏感()
    {
        const string json =
            "{ \"SPRING\": { \"SUNNY\": 70, \"RAINY\": 30 }, " +
            "  \"SUMMER\": { \"sunny\": 100 }, " +
            "  \"AUTUMN\": { \"sunny\": 100 }, " +
            "  \"WINTER\": { \"sunny\": 100 } }";

        WeatherTable table = WeatherTable.FromJson(json);

        Assert.Equal(70, table[Season.Spring, Weather.Sunny]);
        Assert.Equal(30, table[Season.Spring, Weather.Rainy]);
        Assert.Equal(WeatherTable.ExpectedTotal, table.Total(Season.Spring));
    }

    private static void AssertNoWeather(Season season, Weather forbidden)
    {
        WeatherGenerator generator = NewGenerator();

        foreach (int seed in Seeds)
        for (int dayIndex = 0; dayIndex < 2000; dayIndex++)
            Assert.NotEqual(forbidden, generator.Roll(season, dayIndex, seed));
    }
}
