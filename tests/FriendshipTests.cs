using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using XingGame.Systems.Npc;

namespace XingGame.Tests;

/// <summary>
/// 好感度（M2-A）。数值依据 §10.2（0-10 心、每心 250 点、送礼五档加点、生日 ×8），
/// 档位依据 §10.4（0-2 陌生 / 3-5 友好 / 6-8 亲密 / 9-10 挚爱）。
/// </summary>
/// <remarks>
/// 上下限那几条是重点：好感度封顶与触底是「数值溢出到 UI 上」的经典来源——
/// 一次没夹住，心数条就画到 11 心，而查出这个现象的地方离病因很远。
/// </remarks>
public class FriendshipTests
{
    private const string Elena = "npc_elena";

    /// <summary>用真实 NPC 表：好感度的档位查询要先按 id 找到人，假表会把这条链路测掉。</summary>
    private static readonly NpcTable Npcs = NpcTable.LoadDefault();

    private static FriendshipSystem NewSystem() => new(Npcs);

    // ── 基础读数 ──────────────────────────────────────────────────────

    [Fact]
    public void 没交往过的NPC_零点零心_陌生()
    {
        FriendshipSystem friendship = NewSystem();

        Assert.Equal(0, friendship.GetPoints(Elena));
        Assert.Equal(0, friendship.GetHearts(Elena));
        Assert.Equal(FriendshipLevel.Stranger, friendship.GetLevel(Elena));
    }

    [Fact]
    public void 存档键与版本()
    {
        var friendship = NewSystem();

        Assert.Equal("friendship", friendship.SaveKey);
        Assert.Equal(1, friendship.Version);
    }

    [Fact]
    public void 加好感度_累加并返回每次的实际增量()
    {
        FriendshipSystem friendship = NewSystem();

        Assert.Equal(20, friendship.AddPoints(Elena, 20));
        Assert.Equal(40, friendship.AddPoints(Elena, 40));
        Assert.Equal(60, friendship.GetPoints(Elena));
    }

    [Fact]
    public void 加零_不改变点数()
    {
        FriendshipSystem friendship = NewSystem();

        Assert.Equal(0, friendship.AddPoints(Elena, 0));
        Assert.Equal(0, friendship.GetPoints(Elena));
    }

    /// <summary>一位 NPC 的好感度不该记到另一位头上——字典串了键是最难看出来的一种。</summary>
    [Fact]
    public void 各NPC的好感度互不影响()
    {
        FriendshipSystem friendship = NewSystem();

        friendship.AddPoints(Elena, 100);

        Assert.Equal(100, friendship.GetPoints(Elena));
        Assert.Equal(0, friendship.GetPoints("npc_dan"));
    }

    // ── 上下限 ────────────────────────────────────────────────────────

    /// <summary>§10.2「心数：0-10 心」——2500 点封顶，且返回的是**实际**增量而不是传入值。</summary>
    [Fact]
    public void 加满封顶_十心_多给的点数不落地()
    {
        FriendshipSystem friendship = NewSystem();

        Assert.Equal(FriendshipSystem.MaxPoints, friendship.AddPoints(Elena, 9999));
        Assert.Equal(FriendshipSystem.MaxPoints, friendship.GetPoints(Elena));
        Assert.Equal(10, friendship.GetHearts(Elena));
        Assert.Equal(FriendshipLevel.Beloved, friendship.GetLevel(Elena));

        // 已经满了再加：实际增量是 0，不是又吃了 500
        Assert.Equal(0, friendship.AddPoints(Elena, 500));
        Assert.Equal(2500, friendship.GetPoints(Elena));
    }

    /// <summary>掉好感掉不到负数——负的心数在 UI 上没法显示，也会让档位判定失去意义。</summary>
    [Fact]
    public void 减到下限_停在零点_返回实际扣掉的那部分()
    {
        FriendshipSystem friendship = NewSystem();
        friendship.AddPoints(Elena, 30);

        Assert.Equal(-30, friendship.AddPoints(Elena, -40));
        Assert.Equal(0, friendship.GetPoints(Elena));

        // 已经是 0 再减：实际增量 0，而不是把负数记下来
        Assert.Equal(0, friendship.AddPoints(Elena, -40));
        Assert.Equal(0, friendship.GetPoints(Elena));
    }

    /// <summary>
    /// <c>int.MinValue</c> 直接加会溢出成一个大正数，于是「狠狠减好感」变成「加满」——
    /// 这是唯一必须用 long 再夹的地方，用例钉住它。
    /// </summary>
    [Fact]
    public void 一次减到_intMinValue_夹到下限而不是溢出成满值()
    {
        FriendshipSystem friendship = NewSystem();
        friendship.AddPoints(Elena, 500);

        Assert.Equal(-500, friendship.AddPoints(Elena, int.MinValue));
        Assert.Equal(0, friendship.GetPoints(Elena));
    }

    // ── 档位边界（§10.4，每档上下边界各测一条）────────────────────────

    [Theory]
    [InlineData(0, FriendshipLevel.Stranger)]      // 0-2 心 陌生：下边界
    [InlineData(749, FriendshipLevel.Stranger)]    // 2 心的上边界
    [InlineData(750, FriendshipLevel.Friendly)]    // 3 心 友好：下边界
    [InlineData(1499, FriendshipLevel.Friendly)]   // 5 心的上边界
    [InlineData(1500, FriendshipLevel.Close)]      // 6 心 亲密：下边界
    [InlineData(2249, FriendshipLevel.Close)]      // 8 心的上边界
    [InlineData(2250, FriendshipLevel.Beloved)]    // 9 心 挚爱：下边界
    [InlineData(2500, FriendshipLevel.Beloved)]    // 10 心：上边界
    public void 档位边界_每档的上下边界各一条(int points, FriendshipLevel expected)
    {
        FriendshipSystem friendship = NewSystem();
        friendship.AddPoints(Elena, points);

        Assert.Equal(points, friendship.GetPoints(Elena));
        Assert.Equal(expected, friendship.GetLevel(Elena));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(249, 0)]
    [InlineData(250, 1)]
    [InlineData(2250, 9)]
    [InlineData(2500, 10)]
    public void 心数_是点数整除每心二百五十点(int points, int expectedHearts)
    {
        FriendshipSystem friendship = NewSystem();
        friendship.AddPoints(Elena, points);

        Assert.Equal(expectedHearts, friendship.GetHearts(Elena));
    }

    // ── 送礼（§10.2）────────────────────────────────────────────────

    [Fact]
    public void 送喜爱物品_加一百二十()
    {
        Assert.Equal(120, NewSystem().ReceiveGift(Elena, "钻石", isBirthday: false));
    }

    /// <summary>
    /// 先攒一点好感再送，否则「-40」会被下限夹成 0——那测的是下限，不是档位点数。
    /// </summary>
    [Fact]
    public void 送讨厌物品_减四十()
    {
        FriendshipSystem friendship = NewSystem();
        friendship.AddPoints(Elena, 100);

        Assert.Equal(-40, friendship.ReceiveGift(Elena, "鱼", isBirthday: false));
        Assert.Equal(60, friendship.GetPoints(Elena));
    }

    /// <summary>不在两个名单里的算中立（+40）——「喜欢」「不喜欢」两档文档没给数据来源。</summary>
    [Fact]
    public void 送不在名单里的物品_算中立_加四十()
    {
        Assert.Equal(40, NewSystem().ReceiveGift(Elena, "石头", isBirthday: false));
    }

    /// <summary>§10.2「生日送礼：×8 倍」——喜爱 120 → 960。</summary>
    [Fact]
    public void 生日送喜爱物品_翻八倍()
    {
        Assert.Equal(960, NewSystem().ReceiveGift(Elena, "钻石", isBirthday: true));
    }

    /// <summary>
    /// 文档的「×8 倍」没写任何例外，所以讨厌物品的负数也照翻（-40 → -320）。
    /// 替文档「体贴玩家」地免掉这一下，就是在平衡上做了个没人批准的决定。
    /// </summary>
    [Fact]
    public void 生日送讨厌物品_同样翻八倍()
    {
        FriendshipSystem friendship = NewSystem();
        friendship.AddPoints(Elena, 1000);

        Assert.Equal(-320, friendship.ReceiveGift(Elena, "鱼", isBirthday: true));
        Assert.Equal(680, friendship.GetPoints(Elena));
    }

    [Fact]
    public void 送礼_每位NPC的名单各算各的()
    {
        // 矿石对丹是喜爱、对玛莎是讨厌——同一件礼物在不同人身上结果相反
        Assert.Equal(120, NewSystem().ReceiveGift("npc_dan", "矿石", isBirthday: false));

        FriendshipSystem martha = NewSystem();
        martha.AddPoints("npc_martha", 100);

        Assert.Equal(-40, martha.ReceiveGift("npc_martha", "矿石", isBirthday: false));
        Assert.Equal(60, martha.GetPoints("npc_martha"));
    }

    [Fact]
    public void 送礼_物品名为空_抛_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => NewSystem().ReceiveGift(Elena, "  ", isBirthday: false));
    }

    /// <summary>好感度封顶在送礼路径上也要生效：一束生日花不该把心数顶到 11 心。</summary>
    [Fact]
    public void 送礼_也受上限约束()
    {
        FriendshipSystem friendship = NewSystem();
        friendship.AddPoints(Elena, 2450);

        Assert.Equal(50, friendship.ReceiveGift(Elena, "钻石", isBirthday: false));
        Assert.Equal(2500, friendship.GetPoints(Elena));
    }

    // ── 未知 NPC ─────────────────────────────────────────────────────

    [Fact]
    public void 未知NPC_读或加都抛_KeyNotFoundException()
    {
        FriendshipSystem friendship = NewSystem();

        Assert.Throws<KeyNotFoundException>(() => friendship.GetPoints("npc_nobody"));
        Assert.Throws<KeyNotFoundException>(() => friendship.AddPoints("npc_nobody", 10));
        Assert.Throws<KeyNotFoundException>(() => friendship.ReceiveGift("npc_nobody", "钻石", isBirthday: false));
    }

    [Fact]
    public void 未知NPC_抛出的消息里带_id()
    {
        KeyNotFoundException ex = Assert.Throws<KeyNotFoundException>(() => NewSystem().GetPoints("npc_nobody"));

        Assert.Contains("npc_nobody", ex.Message);
    }

    // ── 存档 ─────────────────────────────────────────────────────────

    [Fact]
    public void 存档往返_点数原样读回()
    {
        FriendshipSystem saved = NewSystem();
        saved.AddPoints(Elena, 960);
        saved.AddPoints("npc_dan", 320);

        FriendshipSystem loaded = NewSystem();
        loaded.Deserialize(saved.Serialize(), saved.Version);

        Assert.Equal(960, loaded.GetPoints(Elena));
        Assert.Equal(320, loaded.GetPoints("npc_dan"));
        Assert.Equal(FriendshipLevel.Friendly, loaded.GetLevel(Elena));
    }

    /// <summary>没搭过话的 NPC 不该在存档里留 14 行零——存档是要被人和 Mod 读的。</summary>
    [Fact]
    public void 存档_只写非零的NPC()
    {
        FriendshipSystem friendship = NewSystem();
        friendship.AddPoints(Elena, 20);

        using var document = JsonDocument.Parse(friendship.Serialize());
        JsonElement points = document.RootElement.GetProperty("Points");

        Assert.Equal(1, points.GetArrayLength());
        Assert.Equal(Elena, points[0].GetProperty("NpcId").GetString());
        Assert.Equal(20, points[0].GetProperty("Points").GetInt32());
    }

    [Fact]
    public void 空存档_写出空数组_读回全零()
    {
        FriendshipSystem friendship = NewSystem();

        using var document = JsonDocument.Parse(friendship.Serialize());
        Assert.Equal(0, document.RootElement.GetProperty("Points").GetArrayLength());

        friendship.Deserialize(friendship.Serialize(), friendship.Version);
        Assert.Equal(0, friendship.GetPoints(Elena));
    }

    /// <summary>读档是整份覆盖：留着旧的点数会让「换了个存档还是老好感」这种幽灵 bug 出现。</summary>
    [Fact]
    public void 读档_整份覆盖而不是合并()
    {
        FriendshipSystem friendship = NewSystem();
        friendship.AddPoints(Elena, 500);
        friendship.AddPoints("npc_dan", 500);

        FriendshipSystem other = NewSystem();
        other.AddPoints("npc_dan", 250);

        friendship.Deserialize(other.Serialize(), other.Version);

        Assert.Equal(250, friendship.GetPoints("npc_dan"));
        Assert.Equal(0, friendship.GetPoints(Elena));
    }

    [Theory]
    // 数量超出 0..2500
    [InlineData("""{"Points":[{"NpcId":"npc_elena","Points":-1}]}""")]
    [InlineData("""{"Points":[{"NpcId":"npc_elena","Points":2501}]}""")]
    // 表里没有这位
    [InlineData("""{"Points":[{"NpcId":"npc_nobody","Points":10}]}""")]
    // 空 NpcId
    [InlineData("""{"Points":[{"NpcId":"","Points":10}]}""")]
    // 同一位写了两条
    [InlineData("""{"Points":[{"NpcId":"npc_elena","Points":10},{"NpcId":"npc_elena","Points":20}]}""")]
    // 缺 Points 数组
    [InlineData("""{"Hearts":[]}""")]
    // 空内容
    [InlineData("null")]
    public void 坏存档_当场抛_InvalidDataException(string json)
    {
        Assert.Throws<InvalidDataException>(() => NewSystem().Deserialize(json, 1));
    }

    /// <summary>坏存档不能把当前状态改坏：校验没过就一个字段都不该动。</summary>
    [Fact]
    public void 坏存档被拒后_原来的点数还在()
    {
        FriendshipSystem friendship = NewSystem();
        friendship.AddPoints(Elena, 500);

        Assert.Throws<InvalidDataException>(
            () => friendship.Deserialize("""{"Points":[{"NpcId":"","Points":10}]}""", 1));
        Assert.Equal(500, friendship.GetPoints(Elena));
    }

    /// <summary>来自更新版本的存档不能猜着读（ADR-009）。</summary>
    [Fact]
    public void 存档版本过高_抛_NotSupportedException()
    {
        NotSupportedException ex = Assert.Throws<NotSupportedException>(
            () => NewSystem().Deserialize("""{"Points":[]}""", fromVersion: 2));

        Assert.Contains("2", ex.Message);
    }
}
