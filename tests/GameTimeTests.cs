using XingGame.Core.Time;

namespace XingGame.Tests;

public class GameTimeTests
{
    [Theory]
    [InlineData(0, 0, DayPhase.LateNight)]
    [InlineData(1, 59, DayPhase.LateNight)]
    [InlineData(2, 0, DayPhase.Collapsed)]
    [InlineData(5, 0, DayPhase.Collapsed)]
    [InlineData(5, 59, DayPhase.Collapsed)]
    [InlineData(6, 0, DayPhase.Morning)]
    [InlineData(8, 59, DayPhase.Morning)]
    [InlineData(9, 0, DayPhase.Forenoon)]
    [InlineData(11, 59, DayPhase.Forenoon)]
    [InlineData(12, 0, DayPhase.Afternoon)]
    [InlineData(17, 59, DayPhase.Afternoon)]
    [InlineData(18, 0, DayPhase.Evening)]
    [InlineData(21, 59, DayPhase.Evening)]
    [InlineData(22, 0, DayPhase.LateNight)]
    [InlineData(23, 59, DayPhase.LateNight)]
    public void Phase_在时段边界上正确(int hour, int minute, DayPhase expected)
    {
        var time = new GameTime(1, Season.Spring, 1, hour, minute);

        Assert.Equal(expected, time.Phase);
    }

    [Theory]
    [InlineData(Season.Spring)]
    [InlineData(Season.Summer)]
    [InlineData(Season.Autumn)]
    [InlineData(Season.Winter)]
    public void Phase_不随季节变化(Season season)
    {
        Assert.Equal(DayPhase.Morning, new GameTime(1, season, 1, GameTime.FirstHour, 0).Phase);
        Assert.Equal(DayPhase.Forenoon, new GameTime(1, season, 28, 10, 0).Phase);
        Assert.Equal(DayPhase.Afternoon, new GameTime(9, season, 15, 13, 0).Phase);
        Assert.Equal(DayPhase.Evening, new GameTime(9, season, 15, 20, 0).Phase);
        Assert.Equal(DayPhase.LateNight, new GameTime(9, season, 15, 23, 0).Phase);
        Assert.Equal(DayPhase.Collapsed, new GameTime(9, season, 15, 3, 0).Phase);
    }

    [Fact]
    public void 常量与设计文档一致()
    {
        Assert.Equal(28, GameTime.DaysPerSeason);
        Assert.Equal(4, GameTime.SeasonsPerYear);
        Assert.Equal(6, GameTime.FirstHour);
        Assert.Equal(2, GameTime.CollapseHour);
    }

    [Theory]
    [InlineData(1, Season.Spring, 1, 6, 0, 0)]
    [InlineData(1, Season.Spring, 1, 7, 0, 60)]
    [InlineData(1, Season.Spring, 1, 23, 59, 1079)]
    [InlineData(1, Season.Spring, 1, 0, 0, 1080)]
    [InlineData(1, Season.Spring, 1, 5, 59, 1439)]
    [InlineData(1, Season.Spring, 2, 6, 0, 1440)]
    [InlineData(1, Season.Summer, 1, 6, 0, 40320)]
    [InlineData(1, Season.Winter, 28, 5, 59, 161279)]
    [InlineData(2, Season.Spring, 1, 6, 0, 161280)]
    public void TotalMinutes_自元年春一日六点起算(int year, Season season, int day, int hour, int minute, int expected)
    {
        var time = new GameTime(year, season, day, hour, minute);

        Assert.Equal(expected, time.TotalMinutes);
    }

    [Fact]
    public void Day_在午夜不翻页_在六点翻页()
    {
        var beforeMidnight = new GameTime(1, Season.Spring, 1, 23, 59);

        GameTime afterMidnight = beforeMidnight.AddOneMinuteForTest();

        Assert.Equal(new GameTime(1, Season.Spring, 1, 0, 0), afterMidnight);
        Assert.Equal(DayPhase.LateNight, afterMidnight.Phase);
        Assert.True(afterMidnight.TotalMinutes > beforeMidnight.TotalMinutes);  // 时钟回绕不倒流

        GameTime boundary = new GameTime(1, Season.Spring, 2, 5, 59).AddOneMinuteForTest();

        Assert.Equal(new GameTime(1, Season.Spring, 3, 6, 0), boundary);        // 6:00 才翻页
        Assert.Equal(beforeMidnight.Day + 2, boundary.Day);
    }

    [Fact]
    public void TotalMinutes_逐分钟严格递增()
    {
        var cursor = new GameTime(1, Season.Spring, 1, GameTime.FirstHour, 0);

        // 走满三个游戏日，覆盖午夜回绕与两次日界
        for (int minute = 1; minute <= 3 * 24 * 60; minute++)
        {
            GameTime next = cursor.AddOneMinuteForTest();

            Assert.Equal(cursor.TotalMinutes + 1, next.TotalMinutes);
            cursor = next;
        }

        Assert.Equal(new GameTime(1, Season.Spring, 4, 6, 0), cursor);
        Assert.Equal(3 * 24 * 60, cursor.TotalMinutes);
    }

    [Fact]
    public void 值语义_相等的时刻相等()
    {
        var time = new GameTime(1, Season.Autumn, 13, 18, 30);

        Assert.Equal(new GameTime(1, Season.Autumn, 13, 18, 30), time);
        Assert.NotEqual(new GameTime(1, Season.Autumn, 13, 18, 31), time);
        Assert.Equal(new GameTime(2, Season.Autumn, 13, 18, 30), time with { Year = 2 });
    }
}

file static class GameTimeTestExtensions
{
    /// <summary>
    /// 按「自然钟点 + 6:00 日界」走一分钟。测试自己实现进位，才验得出 GameTime 的 TotalMinutes
    /// 是否与日界自洽——拿被测代码算期望值等于没测。
    /// </summary>
    public static GameTime AddOneMinuteForTest(this GameTime time)
    {
        int minute = time.Minute + 1;
        int hour = time.Hour;
        bool hourAdvanced = false;

        if (minute == 60)
        {
            minute = 0;
            hour++;
            hourAdvanced = true;
        }

        if (hour == 24) hour = 0;

        int year = time.Year;
        Season season = time.Season;
        int day = time.Day;

        if (hourAdvanced && hour == GameTime.FirstHour)
        {
            day++;
            if (day > GameTime.DaysPerSeason)
            {
                day = 1;
                if (season == Season.Winter)
                {
                    season = Season.Spring;
                    year++;
                }
                else
                {
                    season++;
                }
            }
        }

        return new GameTime(year, season, day, hour, minute);
    }
}
