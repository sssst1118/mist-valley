using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 灵力池的表（M3-3）：上限公式与两个恢复速率逐条对 ARCHITECTURE 未定义项备案 #69/#71，
/// 坏表在加载时就被拒。
/// </summary>
/// <remarks>
/// 这组数**不是设计文档直给**的（§8.2 只说炼气 4-6 层的「灵气储备可维持短时施法」），
/// 所以「对账」对的是备案——抄错一个系数的错与「两行数之间的关系不自洽」的错是两类，
/// 用例也分成两条（同 <see cref="CultivationSpeedTableTests"/> 对 #67/#68 的分工）。
/// </remarks>
public class SpiritPowerTableTests
{
    private static readonly SpiritPowerTable Table = SpiritPowerTable.LoadDefault();

    // ── 缺省表：逐条对备案 ───────────────────────────────────────────

    [Fact]
    public void 上限_一层一百_每层加二十五_十三层逐条对()
    {
        // 备案 #69：100 + 25 × (层 - 1)，三个锚点是 1 层 100、4 层 175、13 层 400。
        // 逐条走一遍而不是只对三个锚点：锚点对了、中间错了（比如分段线性、或者只在某几层加）在这里会现形
        Assert.Equal(100, Table.MaxSpiritAt(1));
        Assert.Equal(175, Table.MaxSpiritAt(4));
        Assert.Equal(400, Table.MaxSpiritAt(13));

        for (int stage = 1; stage <= 13; stage++)
            Assert.Equal(100 + 25 * (stage - 1), Table.MaxSpiritAt(stage));
    }

    [Fact]
    public void 上限_每往上一层都在涨_不是一条平线()
    {
        // 备案 #69 取值的含义是让 §8.2 的「短时施法」成立：4 层 175 点放得出几个低阶法术。
        // 写成常量上限（每层都返回 100）能满足上一条里的头一个锚点，却让升层对灵力毫无意义——
        // 这条是上一条的配对，专抓那种写法
        for (int stage = 2; stage <= 13; stage++)
        {
            Assert.True(
                Table.MaxSpiritAt(stage) > Table.MaxSpiritAt(stage - 1),
                $"第 {stage} 层的上限没比第 {stage - 1} 层高");
        }
    }

    [Fact]
    public void 恢复速率_清醒二_打坐五_打坐必须更快()
    {
        // 备案 #71：清醒 2/游戏小时、打坐 5/游戏小时。两个数对调的话「没灵力了就去打坐」这条
        // 循环就反了（站着忙活比入定回得快）——加载时由 RequireMeditationBeatsAwake 拦下，
        // 这里对的是缺省数据文件里的两个数
        Assert.Equal(2, Table.RecoveryPerHour(SpiritRecovery.Awake));
        Assert.Equal(5, Table.RecoveryPerHour(SpiritRecovery.Meditation));
        Assert.True(Table.RecoveryPerHour(SpiritRecovery.Meditation) > Table.RecoveryPerHour(SpiritRecovery.Awake));
    }

    [Fact]
    public void 恢复速率_枚举里每一档都要有_缺一档就是加载失败()
    {
        // 枚举有而数据没有是数据缺口：静默放行的话，缺的那一档在运行时变成「永远回不了灵力」，
        // 而症状离病根很远。改法只有补数据（同 CultivationSpeedTable 要求四季齐全）
        foreach (SpiritRecovery recovery in Enum.GetValues<SpiritRecovery>())
        {
            Assert.True(Table.RecoveryPerHour(recovery) > 0, $"缺了「{recovery}」的速率");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 层号不是从1起_当场抛(int stage)
    {
        // 「第 0 层」不存在：静默夹到 1 层会让写错的调用方拿到一个看着正常的数。
        // 上界不在这里管——层数的上下界是境界表的知识，由 CultivationSystem 那一侧守着
        Assert.Throws<ArgumentOutOfRangeException>(() => Table.MaxSpiritAt(stage));
    }

    // ── 坏表：系数 ──────────────────────────────────────────────────

    [Theory]
    [InlineData("0", "25")]        // 一层的上限是 0：池子从开局就放不出任何法术
    [InlineData("-100", "25")]     // 负上限更荒唐，还会让「够不够花」的判断恒为假
    [InlineData("100", "0")]       // 上限不随层数涨，与备案 #69 的形状不符
    [InlineData("100", "-25")]     // 负增量会让高层的池子比低层小
    public void 坏表_系数不是正数_当场抛(string baseValue, string perStage)
    {
        Assert.Throws<InvalidDataException>(() => SpiritPowerTable.FromJson(Json(baseValue, perStage)));
    }

    // ── 坏表：恢复档位 ──────────────────────────────────────────────

    [Theory]
    // 缺一档：两档都必须有
    [InlineData("""{ "recovery": "Awake", "perHour": 2 }""")]
    [InlineData("""{ "recovery": "Meditation", "perHour": 5 }""")]
    // 认不出的档位：睡眠是「一步回满」不是费率，它不在枚举里（顺带钉住这一点）
    [InlineData("""{ "recovery": "Sleep", "perHour": 9 }, { "recovery": "Awake", "perHour": 2 }, { "recovery": "Meditation", "perHour": 5 }""")]
    // 按序号写档位：Enum.TryParse 连 "0" 都收（解析成第一档），放行它等于允许往枚举中间插一档
    [InlineData("""{ "recovery": "0", "perHour": 2 }, { "recovery": "Meditation", "perHour": 5 }""")]
    // 同一档出现两次：谁生效取决于文件顺序
    [InlineData("""{ "recovery": "Awake", "perHour": 2 }, { "recovery": "Awake", "perHour": 5 }""")]
    // 速率非正：0 让这一档永远回不了灵力，负速率倒着扣
    [InlineData("""{ "recovery": "Awake", "perHour": 0 }, { "recovery": "Meditation", "perHour": 5 }""")]
    [InlineData("""{ "recovery": "Awake", "perHour": -2 }, { "recovery": "Meditation", "perHour": 5 }""")]
    // 打坐不比清醒快：备案 #71 那条循环就反了。相等也不行——「入定等于站着」同样是反的
    [InlineData("""{ "recovery": "Awake", "perHour": 9 }, { "recovery": "Meditation", "perHour": 5 }""")]
    [InlineData("""{ "recovery": "Awake", "perHour": 5 }, { "recovery": "Meditation", "perHour": 5 }""")]
    // 字段缺失或类型不对
    [InlineData("""{ "recovery": "Awake" }, { "recovery": "Meditation", "perHour": 5 }""")]
    [InlineData("""{ "perHour": 2 }, { "recovery": "Meditation", "perHour": 5 }""")]
    [InlineData("""{ "recovery": "", "perHour": 2 }, { "recovery": "Meditation", "perHour": 5 }""")]
    [InlineData("""{ "recovery": "Awake", "perHour": "2" }, { "recovery": "Meditation", "perHour": 5 }""")]
    // 条目根本不是对象
    [InlineData("""{ "recovery": "Awake", "perHour": 2 }, "Meditation" """)]
    public void 坏表_恢复档位有问题_当场抛(string entries)
    {
        Assert.Throws<InvalidDataException>(() => SpiritPowerTable.FromJson(Json(entries: entries)));
    }

    // ── 坏表：结构 ──────────────────────────────────────────────────

    [Theory]
    [InlineData("[]")]                                                                   // 不是对象
    [InlineData("null")]
    [InlineData("""{ "maxSpiritPerStage": 25, "recoveryPerHour": [] }""")]               // 缺 maxSpiritBase
    [InlineData("""{ "maxSpiritBase": 100, "recoveryPerHour": [] }""")]                  // 缺 maxSpiritPerStage
    [InlineData("""{ "maxSpiritBase": 100, "maxSpiritPerStage": 25 }""")]                // 缺 recoveryPerHour
    [InlineData("""{ "maxSpiritBase": "100", "maxSpiritPerStage": 25, "recoveryPerHour": [] }""")]   // 类型不对
    [InlineData("""{ "maxSpiritBase": 100, "maxSpiritPerStage": 25, "recoveryPerHour": 5 }""")]      // 不是数组
    [InlineData("""{ "maxSpiritBase": 100, "maxSpiritPerStage": 25, "recoveryPerHour": {} }""")]
    public void 坏表_结构或字段不对_当场抛(string json)
    {
        Assert.Throws<InvalidDataException>(() => SpiritPowerTable.FromJson(json));
    }

    // ── 数据文件本身 ────────────────────────────────────────────────

    [Fact]
    public void 数据文件_只有上限与恢复两组数_没有法术消耗也没有睡眠条目()
    {
        // 加载器不认识的多余键会被**静静忽略**，所以只盯代码看不出数据文件里多了什么。
        // 这里读原始 JSON 的键：法术的四个消耗（备案 #70）跟着法术那一刀走，
        // 睡眠是「一步回满」进不了按小时计价的费率表——两样都不该在这个文件里
        using var document = JsonDocument.Parse(File.ReadAllText(FindDefaultFile()));
        JsonElement root = document.RootElement;

        string[] keys = root.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(
            keys,
            key => key.Contains("Cost", StringComparison.OrdinalIgnoreCase)
                   || key.Contains("Spell", StringComparison.OrdinalIgnoreCase)
                   || key.Contains("Sleep", StringComparison.OrdinalIgnoreCase));

        string?[] names = root.GetProperty("recoveryPerHour")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("recovery").GetString())
            .ToArray();

        Assert.Equal(2, names.Length);
        Assert.DoesNotContain("Sleep", names);
    }

    /// <summary>
    /// 一份合法的表，按参数改坏一处——**单点故障**才算得清是哪条校验拦下的。
    /// </summary>
    private static string Json(
        string baseValue = "100",
        string perStage = "25",
        string entries = """{ "recovery": "Awake", "perHour": 2 }, { "recovery": "Meditation", "perHour": 5 }""") =>
        $$"""
        { "maxSpiritBase": {{baseValue}}, "maxSpiritPerStage": {{perStage}}, "recoveryPerHour": [ {{entries}} ] }
        """;

    /// <summary>与 <c>CultivationSpeedTableTests.FindDefaultFile</c> 同款的上溯，只为读那份原始 JSON 的键。</summary>
    private static string FindDefaultFile()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, SpiritPowerTable.DefaultRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException($"未找到 {SpiritPowerTable.DefaultRelativePath}，本用例会变成假绿灯");
    }
}
