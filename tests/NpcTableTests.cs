using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Systems.Npc;
using XingGame.Core.Time;

namespace XingGame.Tests;

/// <summary>
/// NPC 表（M2-A）。这里守三件事：① 坏数据必须在加载时炸；② <c>data/npcs/npcs.json</c> 与附录 C
/// 「NPC 列表（完整）」逐列一致——生日抄错一天，生日礼物的 ×8 就落在错的日子上；
/// ③ §10.3 的日程是数据且查得出「此刻在哪」。
/// </summary>
/// <remarks>
/// <b>主表逐条来自附录 C，不是 §10.1</b>：附录 C 多出 ID 与讨厌物品两列，且 §10.1 结尾自己指着它说
/// 「完整 NPC 表见附录 C」。所以本文件里凡是断言姓名/角色/生日/可攻略/喜爱/讨厌的地方，
/// 依据都是附录 C（<c>docs/public/design.md</c> 1676-1692 行）。
/// </remarks>
public class NpcTableTests
{
    /// <summary>
    /// 断言跑在**真实数据文件**上：手抄一份 JSON 到测试里，表里的抄写错误就永远抓不到了。
    /// </summary>
    private static readonly NpcTable Shipped = NpcTable.LoadDefault();

    /// <summary>附录 C 的 14 位，顺序照表。</summary>
    private static readonly string[] AppendixCIds =
    {
        "npc_mayor", "npc_elena", "npc_brent", "npc_martha", "npc_harvey", "npc_willy", "npc_robin",
        "npc_merlin", "npc_grace", "npc_kaz", "npc_leah", "npc_dan", "npc_sarah", "npc_nia",
    };

    [Fact]
    public void 缺省表_十四位都在_且顺序与附录C一致()
    {
        Assert.Equal(14, Shipped.All.Count);
        Assert.Equal(AppendixCIds, Shipped.All.Select(npc => npc.Id).ToArray());
    }

    /// <summary>
    /// 逐列对文档。一条 InlineData = 附录 C 的一行，抄错任何一列都会红。
    /// 喜爱/讨厌物品用逗号串传递，比对时按顺序切回来——顺序换了也说明表和文档不一致。
    /// </summary>
    [Theory]
    [InlineData("npc_mayor", "罗威尔", "镇长", false, "春 10", "南瓜派,铱锭", "垃圾")]
    [InlineData("npc_elena", "艾琳娜", "图书馆管理员", true, "春 18", "旧日记,钻石", "鱼")]
    [InlineData("npc_brent", "布伦特", "铁匠", true, "夏 5", "铁矿,宝石", "花")]
    [InlineData("npc_martha", "玛莎", "杂货店老板", false, "夏 22", "草莓,果酱", "矿石")]
    [InlineData("npc_harvey", "哈维", "医生", true, "秋 3", "咖啡,药草", "酒")]
    [InlineData("npc_willy", "威利", "渔夫", false, "秋 15", "鲟鱼,海藻", "垃圾")]
    [InlineData("npc_robin", "罗宾", "木匠", true, "冬 8", "木材,硬木", "矿石")]
    [InlineData("npc_merlin", "墨林", "巫师", true, "冬 17", "虚空精华,太阳精华", "蔬菜")]
    [InlineData("npc_grace", "格蕾丝", "酒馆老板", true, "春 25", "葡萄酒,啤酒", "鱼")]
    [InlineData("npc_kaz", "卡兹", "旅行商人", false, "夏 12", "稀有物品", "普通物品")]
    [InlineData("npc_leah", "莉亚", "艺术家", true, "秋 20", "野花,果酒", "垃圾")]
    [InlineData("npc_dan", "丹", "矿工", true, "冬 3", "矿石,宝石", "花")]
    [InlineData("npc_sarah", "赛拉", "教师", true, "春 7", "书籍,花朵", "矿石")]
    [InlineData("npc_nia", "妮娅", "神秘少女", true, "夏 28", "月光水母,珍珠", "垃圾")]
    public void 缺省表的每一位_逐列对得上附录C(
        string id, string name, string role, bool romanceable, string birthday, string loved, string hated)
    {
        NpcDefinition npc = Shipped.Get(id);

        Assert.Equal(id, npc.Id);
        Assert.Equal(name, npc.Name);
        Assert.Equal(role, npc.Role);
        Assert.Equal(romanceable, npc.Romanceable);
        Assert.Equal(birthday, npc.Birthday.ToString());
        Assert.Equal(loved.Split(','), npc.LovedItems);
        Assert.Equal(hated.Split(','), npc.HatedItems);
    }

    /// <summary>§10.5「可攻略角色：10 位」——这条数得出来，所以钉住；将来改了会红。</summary>
    [Fact]
    public void 可攻略角色恰好十位()
    {
        string[] romanceable = Shipped.All.Where(npc => npc.Romanceable).Select(npc => npc.Id).ToArray();

        Assert.Equal(10, romanceable.Length);
        Assert.Equal(
            new[] { "npc_elena", "npc_brent", "npc_harvey", "npc_robin", "npc_merlin",
                    "npc_grace", "npc_leah", "npc_dan", "npc_sarah", "npc_nia" },
            romanceable);
    }

    /// <summary>
    /// 两份表打架的那一处：§10.1 说妮娅的角色是「未知」，附录 C 说「神秘少女」。
    /// 采信附录 C（自称完整、逐列齐全），这条用例把那次裁决钉死——改回「未知」就会红。
    /// </summary>
    [Fact]
    public void 妮娅的角色取附录C的神秘少女_而不是10_1的未知()
    {
        Assert.Equal("神秘少女", Shipped.Get("npc_nia").Role);
    }

    [Fact]
    public void Get_未知_id_抛_KeyNotFoundException_且消息含_id()
    {
        KeyNotFoundException ex = Assert.Throws<KeyNotFoundException>(() => Shipped.Get("npc_nobody"));

        Assert.Contains("npc_nobody", ex.Message);
    }

    [Fact]
    public void TryGet_未知_id_返回_false_且不抛()
    {
        Assert.False(Shipped.TryGet("npc_nobody", out NpcDefinition definition));
        Assert.Null(definition);
    }

    [Fact]
    public void Get_null_抛_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Shipped.Get(null!));
    }

    // ── §10.3 日程：只做数据（查表），不做寻路 ─────────────────────────────

    [Fact]
    public void 艾琳娜的日程_逐条对得上10_3的示例()
    {
        NpcSchedule schedule = Shipped.Get("npc_elena").Schedule;

        Assert.Equal("家", schedule.RainyLocation);
        Assert.Equal(9, schedule.Entries.Count);

        // 春/夏/秋 那一行是一行盖三季，所以三季都在同一条 entry 里
        Season[] springSummerAutumn = { Season.Spring, Season.Summer, Season.Autumn };
        Assert.Equal(
            new[] { (9, "图书馆"), (12, "杂货店"), (14, "图书馆"), (18, "酒馆"), (22, "回家") },
            schedule.Entries.Take(5).Select(e => (e.Hour, e.Location)).ToArray());
        Assert.All(schedule.Entries.Take(5), e => Assert.Equal(springSummerAutumn, e.Seasons));

        Assert.Equal(
            new[] { (10, "图书馆"), (13, "诊所"), (15, "图书馆"), (19, "回家") },
            schedule.Entries.Skip(5).Select(e => (e.Hour, e.Location)).ToArray());
        Assert.All(schedule.Entries.Skip(5), e => Assert.Equal(new[] { Season.Winter }, e.Seasons));
    }

    /// <summary>文档只给了艾琳娜一位（§10.3 标明「示例」），其余十三位没有日程数据——不许自己编。</summary>
    [Fact]
    public void 文档未给日程的十三位_日程为空()
    {
        string[] withSchedule = Shipped.All.Where(npc => !npc.Schedule.IsEmpty).Select(npc => npc.Id).ToArray();

        Assert.Equal(new[] { "npc_elena" }, withSchedule);
    }

    [Theory]
    // 取「已开始且最晚」的那一行：13:00 落在 12:00 那行上，晚的安排盖住早的
    [InlineData(Season.Spring, 9, "图书馆")]
    [InlineData(Season.Spring, 11, "图书馆")]
    [InlineData(Season.Spring, 12, "杂货店")]
    [InlineData(Season.Spring, 13, "杂货店")]
    [InlineData(Season.Spring, 18, "酒馆")]
    [InlineData(Season.Spring, 23, "回家")]
    [InlineData(Season.Summer, 22, "回家")]
    [InlineData(Season.Autumn, 14, "图书馆")]
    [InlineData(Season.Winter, 10, "图书馆")]
    [InlineData(Season.Winter, 14, "诊所")]
    [InlineData(Season.Winter, 19, "回家")]
    public void 日程查询_按季节与钟点查得到地点(Season season, int hour, string expected)
    {
        Assert.Equal(expected, Shipped.Get("npc_elena").Schedule.LocationAt(season, hour, isRaining: false));
    }

    /// <summary>
    /// 日程开始之前（春 8:00 早于 9:00 那行）文档确实没写他该在哪，
    /// 返回 null 表示「文档没说」——编一个「家」出来就是替文档做主。
    /// </summary>
    [Fact]
    public void 日程开始之前_查不出地点()
    {
        Assert.Null(Shipped.Get("npc_elena").Schedule.LocationAt(Season.Spring, 8, isRaining: false));
        Assert.Null(Shipped.Get("npc_elena").Schedule.LocationAt(Season.Spring, 0, isRaining: false));
    }

    [Fact]
    public void 文档未给日程的NPC_任何时刻都查不出地点()
    {
        NpcSchedule schedule = Shipped.Get("npc_mayor").Schedule;

        Assert.True(schedule.IsEmpty);
        Assert.Null(schedule.LocationAt(Season.Spring, 9, isRaining: false));
        Assert.Null(schedule.LocationAt(Season.Spring, 9, isRaining: true));
    }

    /// <summary>§10.3「雨天：全天在家」——雨天是整天覆盖，与钟点无关。</summary>
    [Theory]
    [InlineData(Season.Spring, 9)]
    [InlineData(Season.Spring, 23)]
    [InlineData(Season.Winter, 3)]
    public void 雨天_全天在家(Season season, int hour)
    {
        Assert.Equal("家", Shipped.Get("npc_elena").Schedule.LocationAt(season, hour, isRaining: true));
    }

    /// <summary>
    /// 文档没写「有日程但没有雨天安排」的 NPC 雨天去哪。「按平时的日程」是这里选的默认，
    /// 因为另一种解释（雨天把人变没）会让 NPC 凭空消失——这条钉住默认，别悄悄改。
    /// </summary>
    [Fact]
    public void 没写雨天安排时_按平时日程而不是消失()
    {
        NpcTable table = NpcTable.FromJson(JsonWith(Npc(
            schedule: ", \"schedule\": { \"entries\": [ { \"seasons\": [\"Spring\"], \"hour\": 9, \"location\": \"广场\" } ] }")));

        NpcSchedule schedule = table.Get("npc_test").Schedule;

        Assert.Null(schedule.RainyLocation);
        Assert.Equal("广场", schedule.LocationAt(Season.Spring, 10, isRaining: true));
    }

    // ── 坏数据：每一条都要在加载时炸 ───────────────────────────────────

    [Fact]
    public void 空_id_加载即抛()
    {
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => NpcTable.FromJson(JsonWith(Npc(id: ""))));

        Assert.Contains("空 id", ex.Message);
    }

    [Fact]
    public void 缺_id_字段_加载即抛()
    {
        Assert.Throws<InvalidDataException>(() => NpcTable.FromJson(
            "{ \"npcs\": [ { \"name\": \"无\", \"role\": \"角色\", \"romanceable\": false, \"birthday\": \"春 1\" } ] }"));
    }

    [Fact]
    public void 重复_id_加载即抛()
    {
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            NpcTable.FromJson(JsonWith(Npc(id: "npc_dup"), Npc(id: "npc_dup", name: "另一个"))));

        Assert.Contains("npc_dup", ex.Message);
    }

    [Fact]
    public void 缺_npcs_数组_加载即抛()
    {
        Assert.Throws<InvalidDataException>(() => NpcTable.FromJson("{ \"npc\": [] }"));
    }

    /// <summary>
    /// 生日格式非法：认不出的季节、缺一段、日越界、英文季节名、中文数字。
    /// 只放宽到「能认出来就行」的话，抄错的生日会静默变成另一位 NPC 的生日。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("春")]
    [InlineData("春 10 日")]
    [InlineData("Spring 10")]
    [InlineData("春十")]
    [InlineData("春 0")]
    [InlineData("春 29")]
    [InlineData("春 -1")]
    [InlineData("春 1.5")]
    [InlineData("10 春")]
    public void 非法生日_加载即抛_且消息里带_id_与原文(string birthday)
    {
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            NpcTable.FromJson(JsonWith(Npc(id: "npc_bad", birthday: birthday))));

        Assert.Contains("npc_bad", ex.Message);
        Assert.Contains(birthday, ex.Message);
    }

    [Fact]
    public void 生日_季节名与天数落在类型上()
    {
        Birthday birthday = Birthday.Parse("冬 17");

        Assert.Equal(Season.Winter, birthday.Season);
        Assert.Equal(17, birthday.Day);
        Assert.Equal("冬 17", birthday.ToString());
    }

    [Fact]
    public void 同一个物品既喜爱又讨厌_加载即抛()
    {
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            NpcTable.FromJson(JsonWith(Npc(lovedItems: "[\"花\",\"宝石\"]", hatedItems: "[\"宝石\"]"))));

        Assert.Contains("宝石", ex.Message);
    }

    [Fact]
    public void 喜爱物品里有空名字_加载即抛()
    {
        Assert.Throws<InvalidDataException>(() => NpcTable.FromJson(JsonWith(Npc(lovedItems: "[\"\"]"))));
    }

    [Fact]
    public void 日程钟点越界_加载即抛()
    {
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => NpcTable.FromJson(JsonWith(Npc(
            schedule: ", \"schedule\": { \"entries\": [ { \"seasons\": [\"Spring\"], \"hour\": 24, \"location\": \"广场\" } ] }"))));

        Assert.Contains("24", ex.Message);
    }

    /// <summary>
    /// 季节必须逐字写枚举名。「3」这种序号写法 <c>Enum.TryParse</c> 也认（解析成 Winter），
    /// 放行就等于多出第二份格式——将来往枚举中间插一季，旧数据会静默指到别的季节。
    /// </summary>
    [Theory]
    [InlineData("Monsoon")]
    [InlineData("3")]
    [InlineData("winter")]
    [InlineData("")]
    public void 日程季节认不出_加载即抛(string season)
    {
        Assert.Throws<InvalidDataException>(() => NpcTable.FromJson(JsonWith(Npc(
            schedule: $", \"schedule\": {{ \"entries\": [ {{ \"seasons\": [\"{season}\"], \"hour\": 9, \"location\": \"广场\" }} ] }}"))));
    }

    [Fact]
    public void 日程_seasons_为空_加载即抛()
    {
        Assert.Throws<InvalidDataException>(() => NpcTable.FromJson(JsonWith(Npc(
            schedule: ", \"schedule\": { \"entries\": [ { \"seasons\": [], \"hour\": 9, \"location\": \"广场\" } ] }"))));
    }

    [Fact]
    public void 日程缺_location_加载即抛()
    {
        Assert.Throws<InvalidDataException>(() => NpcTable.FromJson(JsonWith(Npc(
            schedule: ", \"schedule\": { \"entries\": [ { \"seasons\": [\"Spring\"], \"hour\": 9 } ] }"))));
    }

    [Fact]
    public void romanceable_不是布尔_加载即抛()
    {
        Assert.Throws<InvalidDataException>(() =>
            NpcTable.FromJson(JsonWith(Npc(romanceable: "\"是\""))));
    }

    // ── 造数据的小工具：坏数据用例只改一个字段，不必每次抄一整份 JSON ─────────

    private static string JsonWith(params string[] entries) => "{ \"npcs\": [" + string.Join(",", entries) + "] }";

    private static string Npc(
        string id = "npc_test",
        string name = "测试",
        string role = "角色",
        string romanceable = "true",
        string birthday = "春 1",
        string lovedItems = "[\"甲\"]",
        string hatedItems = "[\"乙\"]",
        string schedule = "") =>
        $"{{ \"id\": \"{id}\", \"name\": \"{name}\", \"role\": \"{role}\", " +
        $"\"romanceable\": {romanceable}, \"birthday\": \"{birthday}\", " +
        $"\"lovedItems\": {lovedItems}, \"hatedItems\": {hatedItems}{schedule} }}";
}
