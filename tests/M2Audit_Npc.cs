using System;
using System.Collections.Generic;
using System.IO;
using XingGame.Core.Time;
using XingGame.Systems.Npc;

namespace XingGame.Tests;

/// <summary>
/// NPC 与好感度的对抗性审计（M2）。只补既有用例没在看的分支：
/// 送礼撞上未知 id、档位换算在越界心数上的行为、生日写法的往返、日程表对传入列表的拷贝。
/// </summary>
public class M2Audit_Npc
{
    private const string NpcsJson = """
    {
      "npcs": [
        { "id": "npc_elena", "name": "艾琳娜", "role": "图书管理员", "romanceable": true, "birthday": "春 10",
          "lovedItems": [ "南瓜派" ], "hatedItems": [ "矿石" ],
          "schedule": { "rainyLocation": "家",
            "entries": [ { "seasons": [ "Spring" ], "hour": 9,  "location": "图书馆" },
                         { "seasons": [ "Spring" ], "hour": 22, "location": "回家" } ] } },
        { "id": "npc_willy", "name": "威利", "role": "渔夫", "romanceable": false, "birthday": "夏 28" }
      ]
    }
    """;

    private const string Elena = "npc_elena";

    private static readonly NpcTable Npcs = NpcTable.FromJson(NpcsJson);

    [Fact]
    public void 送礼_未知NPC_抛且不在存档里留下这一位()
    {
        var friendship = new FriendshipSystem(Npcs);
        friendship.AddPoints(Elena, 250);

        Assert.Throws<KeyNotFoundException>(() => friendship.ReceiveGift("npc_nobody", "南瓜派", false));

        Assert.Equal(250, friendship.GetPoints(Elena));
        Assert.DoesNotContain("npc_nobody", friendship.Serialize(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, FriendshipLevel.Stranger)]   // 掉成负心数不可能，但换算不该炸，也不该越档
    [InlineData(11, FriendshipLevel.Beloved)]    // 越过 10 心时按最近的档算（上限未封顶时也不该炸在 UI 上）
    [InlineData(20, FriendshipLevel.Beloved)]
    public void 档位_心数越界时按就近的档算(int hearts, FriendshipLevel expected)
    {
        Assert.Equal(expected, FriendshipLevels.FromHearts(hearts));
    }

    [Fact]
    public void 生日_文档写法往返一致()
    {
        // Birthday 的对外写法就是文档里的「春 10」：解析→输出的往返一旦破掉，
        // 存档与 UI 上的生日会各写各的
        foreach (string text in new[] { "春 1", "春 10", "夏 28", "秋 15", "冬 28" })
        {
            Assert.Equal(text, Birthday.Parse(text).ToString());
        }
    }

    // ——— 坏存档：坏档一律 InvalidDataException，不许漏成 NRE（ADR-009）———

    [Fact]
    public void 好感度存档_数组里出现null元素时_抛坏档而不是NRE()
    {
        var friendship = new FriendshipSystem(Npcs);

        // 手写的 JSON 里数组元素可以是 null，System.Text.Json 照收
        Assert.Throws<InvalidDataException>(
            () => friendship.Deserialize("""{ "Points": [ null ] }""", 1));
    }

    [Fact]
    public void 好感度存档_条目缺少Points字段时_抛而不是当成0好感()
    {
        var friendship = new FriendshipSystem(Npcs);
        friendship.AddPoints(Elena, 250);

        // 0 是合法好感度（0 心），所以「字段不在」必须与「字段是 0」分开（ADR-009）。
        // 静默读成 0 会把玩家攒的好感度抹掉，而这份 0 随后会被 Serialize 写回档里——真抹掉了
        Assert.Throws<InvalidDataException>(
            () => friendship.Deserialize("""{ "Points": [ { "NpcId": "npc_elena" } ] }""", 1));

        Assert.Equal(250, friendship.GetPoints(Elena));   // 抛之前不许动内存里那份
    }

    // ——— 数据表：谁生效不许取决于文件顺序 ———

    [Fact]
    public void NPC表_同一季节同一钟点写两条日程_加载即抛()
    {
        // LocationAt 取「已开始且最晚」的那一行，且用 >= 比较——两份都合法时，谁生效取决于
        // 文件里谁在后面。这正是各表都专门拦过的「谁生效取决于文件顺序」（同 id 重复、
        // 一行里季节重复、存档里同一位写两条）
        const string duplicated = """
        {
          "npcs": [
            { "id": "npc_dup", "name": "重复", "role": "测试", "romanceable": false, "birthday": "春 1",
              "schedule": { "entries": [ { "seasons": [ "Spring" ], "hour": 9, "location": "图书馆" },
                                         { "seasons": [ "Spring" ], "hour": 9, "location": "广场" } ] } }
          ]
        }
        """;

        Assert.Throws<InvalidDataException>(() => NpcTable.FromJson(duplicated));

        // 不同季节的同一钟点不冲突：那一小时里只有一行的季节对得上。
        // 拦重复时不许连它一起拦掉
        const string differentSeasons = """
        {
          "npcs": [
            { "id": "npc_hour", "name": "同钟点", "role": "测试", "romanceable": false, "birthday": "春 1",
              "schedule": { "entries": [ { "seasons": [ "Spring" ], "hour": 9, "location": "图书馆" },
                                         { "seasons": [ "Summer" ], "hour": 9, "location": "广场" } ] } }
          ]
        }
        """;

        Assert.Equal("广场", NpcTable.FromJson(differentSeasons).Get("npc_hour")
            .Schedule.LocationAt(Season.Summer, 9, isRaining: false));
        Assert.Equal("图书馆", NpcTable.FromJson(differentSeasons).Get("npc_hour")
            .Schedule.LocationAt(Season.Spring, 9, isRaining: false));
    }

    [Fact]
    public void 日程_构造时拷贝传入的列表_外部改动不了它()
    {
        var entries = new List<ScheduleEntry>
        {
            new(new[] { Season.Spring }, 9, "图书馆"),
        };

        var schedule = new NpcSchedule(entries, rainyLocation: "家");

        // 传进来的那份列表后来被改了：日程表回答的是「文档写了什么」，不该跟着变
        entries.Add(new ScheduleEntry(new[] { Season.Spring }, 15, "广场"));

        Assert.Single(schedule.Entries);
        Assert.Equal("图书馆", schedule.LocationAt(Season.Spring, 16, isRaining: false));
    }
}
