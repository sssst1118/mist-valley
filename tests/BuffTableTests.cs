using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Systems.Buffs;

namespace XingGame.Tests;

/// <summary>
/// 增益表（M3-6）。表里的**每一条都是文档直给**，所以测法是对账：逐条把 id / 名字 / 目标属性 /
/// 倍率 / 时长对着设计文档抄一遍，抄错一个数就是玩法变了。
/// </summary>
/// <remarks>
/// <para>
/// 出处：聚气散与灵芽羹出自 §8.3 修炼速度体系的「丹药」那一行（<c>docs/public/design.md</c> 815 行）
/// 与 §12.4 烹饪系统（1368 行）；轻身术出自 §8.2 炼气 7-9 层那一格（484 行，只给了 +20%），
/// **时长是 ARCHITECTURE 未定义项备案 #78 推的**——所以那一条的时长既验值也验「它是推的」这件事
/// （见下面那条用例的注释）。
/// </para>
/// <para>
/// 时长一律按**游戏分钟**记（7 天 = 7 × 24 × 60 = 10080），所以对账时写成 <c>7 * 24 * 60</c>
/// 而不是 10080 这个魔数：单位换算错一位（写成 7 × 60）在数字上看起来很合理，在算式上一眼就露。
/// </para>
/// </remarks>
public class BuffTableTests
{
    private static readonly BuffTable Table = BuffTable.LoadDefault();

    // ── 逐条对文档 ──────────────────────────────────────────────────

    [Fact]
    public void 聚气散_修炼加五成_持续七天()
    {
        // §8.3 的原文：「修炼类丹药可提供临时加成（如『聚气散』+50% 持续 7 天）」。
        // 倍率记 1.5 而不是 50：文档给的是百分数，落进表里只留一个含义——免得每个读表的人
        // 都在脑子里换算一次，而那正是把 1.5 抄成 15 的地方（同 SpiritRootGrade 的写法）
        BuffDefinition buff = Table.Get("buff_gather_qi_powder");

        Assert.Equal("聚气散", buff.Name);
        Assert.Equal(BuffTarget.CultivationSpeed, buff.Target);
        Assert.Equal(1.5, buff.Multiplier, precision: 10);
        Assert.Equal(7 * 24 * 60, buff.DurationMinutes);
    }

    [Fact]
    public void 灵芽羹_修炼加两成_持续一天()
    {
        // §12.4 原文：「灵芽羹：灵芽草×2、灵泉水×1，恢复 300 灵力，修炼速度 +20% 持续 1 天」。
        // 「恢复 300 灵力」不在本表里——灵力走灵力池那条账，这条增益只管修炼速度那一项
        BuffDefinition buff = Table.Get("buff_spirit_grass_soup");

        Assert.Equal("灵芽羹", buff.Name);
        Assert.Equal(BuffTarget.CultivationSpeed, buff.Target);
        Assert.Equal(1.2, buff.Multiplier, precision: 10);
        Assert.Equal(24 * 60, buff.DurationMinutes);
    }

    [Fact]
    public void 轻身术_移速加两成_持续一小时_时长是备案推的()
    {
        // §8.2 炼气 7-9 层那一格只给了「移动速度 +20%」，**没给时长**；备案 #78 取 1 游戏小时，
        // 理由写在表文件与备案里：轻身术耗 10 灵力而清醒恢复只有 2/小时（备案 #71），
        // 回本要 5 小时——所以它是一次「跑一段路」的助力，取 5 小时以上就变成常驻开关。
        // 这条用例同时钉住「它是推的」：文档改口时先红的是它
        BuffDefinition buff = Table.Get("buff_light_body");

        Assert.Equal("轻身术", buff.Name);
        Assert.Equal(BuffTarget.MoveSpeed, buff.Target);
        Assert.Equal(1.2, buff.Multiplier, precision: 10);
        Assert.Equal(60, buff.DurationMinutes);
    }

    [Fact]
    public void 表里只有这三条_每条都有出处()
    {
        // 完整集合写在这里：多一条少一条都要先解释（多的是谁读它？少的是哪一处玩法断了？）。
        // 南瓜派（§12.4「耕种 +2」）**刻意不在表里**，见 BuffTarget 的注释与 M3Audit_Buffs
        Assert.Equal(
            new[] { "buff_gather_qi_powder", "buff_spirit_grass_soup", "buff_light_body" },
            Table.Buffs.Select(buff => buff.Id));
    }

    [Fact]
    public void 每个目标属性都至少有一条增益()
    {
        // 反向配对：枚举里多出一个成员、表里却没有任何一条用它，那个成员就是一条没有任何数据
        // 会走到的死路（铁律 11 的另一半）。这条与 M3Audit_Buffs 的「成员恰好两个」互为对照
        foreach (BuffTarget target in Enum.GetValues<BuffTarget>())
        {
            Assert.Contains(Table.Buffs, buff => buff.Target == target);
        }
    }

    // ── 取用 ────────────────────────────────────────────────────────

    [Fact]
    public void 取一条_认得出的取回_认不出的抛()
    {
        Assert.Equal("聚气散", Table.Get("buff_gather_qi_powder").Name);

        // id 认不出是编程错误（id 由调用方的数据给出），当场抛而不是静默退回某条默认增益
        Assert.Throws<KeyNotFoundException>(() => Table.Get("buff_nobody"));
        Assert.Throws<KeyNotFoundException>(() => Table.Get(""));
        Assert.Throws<ArgumentNullException>(() => Table.Get(null!));

        // 读档那条路走 TryGet：取不到用返回值表达，让调用方抛**数据错误**而不是编程错误
        Assert.True(Table.TryGet("buff_light_body", out BuffDefinition? found));
        Assert.Equal("轻身术", found.Name);
        Assert.False(Table.TryGet("buff_nobody", out _));
        Assert.False(Table.TryGet(null!, out _));
    }

    // ── 坏表：每条坏数据只坏在一处 ──────────────────────────────────

    /// <summary>拼表：一条 <c>buffs</c> 条目（字段列表由调用方给），只有被点名的那一处是坏的。</summary>
    private static string FromBuff(string fields) => $"{{ \"buffs\": [ {{ {fields} }} ] }}";

    private const string Good =
        "\"id\": \"buff_x\", \"name\": \"增益\", \"target\": \"CultivationSpeed\","
        + " \"multiplier\": 1.5, \"durationMinutes\": 60";

    public static TheoryData<string, string> BadTables()
    {
        var cases = new TheoryData<string, string>();

        void Bad(string because, string json) => cases.Add(because, json);

        Bad("根不是对象", "[ ]");
        Bad("缺 buffs 数组", "{ \"_comment\": \"只有注释\" }");
        Bad("buffs 不是数组", "{ \"buffs\": { } }");
        Bad("空表", "{ \"buffs\": [ ] }");
        Bad("条目不是对象", "{ \"buffs\": [ 1 ] }");
        Bad("缺 id", FromBuff("\"name\": \"增益\", \"target\": \"CultivationSpeed\", \"multiplier\": 1.5, \"durationMinutes\": 60"));
        Bad("id 是空串", FromBuff("\"id\": \"  \", \"name\": \"增益\", \"target\": \"CultivationSpeed\", \"multiplier\": 1.5, \"durationMinutes\": 60"));
        Bad("缺 name", FromBuff("\"id\": \"buff_x\", \"target\": \"CultivationSpeed\", \"multiplier\": 1.5, \"durationMinutes\": 60"));
        Bad("缺 target", FromBuff("\"id\": \"buff_x\", \"name\": \"增益\", \"multiplier\": 1.5, \"durationMinutes\": 60"));

        // 按序号写目标的那一刻起，往枚举中间插一项就会让旧数据静默指向另一个属性
        Bad("target 认不出（按序号写）", FromBuff("\"id\": \"buff_x\", \"name\": \"增益\", \"target\": \"0\", \"multiplier\": 1.5, \"durationMinutes\": 60"));
        Bad("target 认不出（拼错）", FromBuff("\"id\": \"buff_x\", \"name\": \"增益\", \"target\": \"MoveSpeeds\", \"multiplier\": 1.5, \"durationMinutes\": 60"));

        Bad("缺 multiplier", FromBuff("\"id\": \"buff_x\", \"name\": \"增益\", \"target\": \"MoveSpeed\", \"durationMinutes\": 60"));
        Bad("multiplier 是 0", FromBuff("\"id\": \"buff_x\", \"name\": \"增益\", \"target\": \"MoveSpeed\", \"multiplier\": 0, \"durationMinutes\": 60"));
        Bad("multiplier 是负数", FromBuff("\"id\": \"buff_x\", \"name\": \"增益\", \"target\": \"MoveSpeed\", \"multiplier\": -1.5, \"durationMinutes\": 60"));

        Bad("缺 durationMinutes", FromBuff("\"id\": \"buff_x\", \"name\": \"增益\", \"target\": \"MoveSpeed\", \"multiplier\": 1.5"));
        Bad("时长是 0", FromBuff("\"id\": \"buff_x\", \"name\": \"增益\", \"target\": \"MoveSpeed\", \"multiplier\": 1.5, \"durationMinutes\": 0"));
        Bad("时长是负数", FromBuff("\"id\": \"buff_x\", \"name\": \"增益\", \"target\": \"MoveSpeed\", \"multiplier\": 1.5, \"durationMinutes\": -60"));

        // 重复 id：同 id 的两条哪一条生效取决于文件顺序，而两份还可能一条 ×1.5 一条 ×1.2
        Bad("重复 id", "{ \"buffs\": [ { " + Good + " }, { " + Good + " } ] }");

        return cases;
    }

    [Theory]
    [MemberData(nameof(BadTables))]
    public void 坏表_加载时抛且说得清是哪一处(string because, string json)
    {
        var thrown = Assert.Throws<InvalidDataException>(() => BuffTable.FromJson(json));

        Assert.False(string.IsNullOrWhiteSpace(thrown.Message), $"{because}：异常消息不能是空的");
    }

    [Fact]
    public void 表可以只有一条_那不是坏表()
    {
        // 「今天只录了一条增益」是自洽的状态，与「buffs 数组整个缺了」（多半是写坏了）是两件事
        BuffTable single = BuffTable.FromJson(FromBuff(Good));

        Assert.Single(single.Buffs);
        Assert.Equal("buff_x", single.Buffs[0].Id);
    }

    // ── 数据文件本身 ────────────────────────────────────────────────

    [Fact]
    public void 数据文件里每条只有那五个字段_没有别的因素长进来()
    {
        // 加载器不认识的多余键会被静静忽略，只盯代码看不出来（同一个教训在 M3-2/M3-3/M3-5
        // 各写了一遍）：所以这里读**原始 JSON 的键集合**。
        // 多出一个 durationDays / percent / stackable 之类，就等于同一件事有了第二种写法——
        // 而哪一种生效取决于谁先读它
        using var document = JsonDocument.Parse(File.ReadAllText(FindDefaultFile()));

        JsonElement buffs = document.RootElement.GetProperty("buffs");
        Assert.Equal(3, buffs.GetArrayLength());

        string[] expected = { "durationMinutes", "id", "multiplier", "name", "target" };
        foreach (JsonElement buff in buffs.EnumerateArray())
        {
            Assert.Equal(
                expected,
                buff.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        }

        // 负向对照：判据本身有效——真多一个键进来，它会被认出来（上一条断言就是靠这个成立的）
        using var tampered = JsonDocument.Parse(
            """{ "buffs": [ { "id": "buff_x", "name": "增益", "target": "MoveSpeed", "multiplier": 1.5, "durationMinutes": 60, "durationDays": 1 } ] }""");
        Assert.NotEqual(
            expected,
            tampered.RootElement.GetProperty("buffs")[0].EnumerateObject()
                .Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>与 <c>CultivationSpeedTableTests.FindDefaultFile</c> 同款的上溯，只为读那份原始 JSON。</summary>
    private static string FindDefaultFile()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, BuffTable.DefaultRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException($"未找到 {BuffTable.DefaultRelativePath}，本用例会变成假绿灯");
    }
}
