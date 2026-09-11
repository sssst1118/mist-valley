using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using XingGame.Core.Time;
using XingGame.Systems.Buffs;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 限时增益（M3-6）的对抗性审计：**刻意不做**的那些事，用反射与存档形状钉住。
/// </summary>
/// <remarks>
/// <para>
/// 否定式决定没有行为可测——「南瓜派那条加法没有落点」这件事，跑什么代码都证明不了。但它们同样会被
/// 下一个人「顺手」加回来，而加的人只觉得「这不就是个字段吗」，直到玩法、数据表、存档全都要跟着动。
/// 所以用反射断言成员不存在（同 <c>M3Audit_Cultivation</c> 的心智与写法）。
/// </para>
/// <para>
/// <b>这个系统天然想做「通用修正器」，所以这一刀最难过的是铁律 11</b>：一个包含十几种属性的枚举
/// 做起来很顺手，而其中大部分今天没有任何消费者（体力、耕种、钓鱼、采矿、战斗……）。
/// 下面每一条钉子对应的都是「今天只建有消费者、且有文档出处的那一份」这个决定。
/// </para>
/// </remarks>
public class M3Audit_Buffs
{
    /// <summary>
    /// 目标属性**恰好**这两个——都是今天真有读者、文档又给了数的：<c>CultivationSpeed</c> 的读者是
    /// <c>CultivationSystem.SpeedMultiplierAt</c> 的第五项，<c>MoveSpeed</c> 的读者是 <c>PlayerMotor</c>。
    /// </summary>
    /// <remarks>
    /// <b>§12.4 的南瓜派（「恢复 200 体力，耕种 +2」）刻意没有对应成员（不是漏做）</b>：它加的是
    /// 耕种类技能等级，而这个游戏里连「耕种等级」这个属性都还不存在（同一句里的「体力」也一样）。
    /// 加一个成员出来只会得到一条永远查不到东西、也没有任何代码会读的数据——编出来的数与真数据
    /// 长得一模一样，将来没人分得清「文档写了」与「我们编的」。等耕作技能落地时再加，那一刻它自然
    /// 有调用方。**加回来那一刻这条就红**。
    /// </remarks>
    private static readonly string[] TargetNames = { "CultivationSpeed", "MoveSpeed" };

    /// <summary>
    /// 加法修正的关键词：今天一个消费者都没有（唯一那条加法连目标属性都还没有）。
    /// </summary>
    /// <remarks>
    /// <b>刻意不用 "Add" 一个词当判据</b>：每张表都有 <c>LoadDefault</c>，里面含 "adD"，而
    /// <see cref="HasMemberMatching"/> 是不分大小写的子串匹配——放它进来就成了一条永远为真的
    /// 判据（那种判据比不写还糟）。同 <c>M3Audit_Cultivation</c> 里「不用 "Age" 当判据，因为
    /// Stage / Stages 里就含 age」的取舍：换一个不会被误伤的写法，而不是把判据放宽。
    /// </remarks>
    private static readonly string[] FlatModifierTokens = { "Flat", "Delta", "Additive" };

    /// <summary>
    /// 时长与倍率的换算（天 / 小时 / 百分数）只许出现在**数据**里：表记的是游戏分钟与倍率，
    /// 系统只做「加到绝对时刻」与「连乘」两件算术。系统里出现 <c>SevenDays</c> 这类名字就是
    /// 把文档的数抄进了代码（同 <c>M3Audit_Cultivation</c> 对备案 #67/#68 的处理）。
    /// </summary>
    private static readonly string[] TableNumberTokens = { "Day", "Hour" };

    [Fact]
    public void 目标属性恰好两个_有消费者的那两个()
    {
        Assert.Equal(TargetNames, Enum.GetNames<BuffTarget>());

        // 负向对照：判据本身有效——多一个成员就会被认出来（「顺手把耕种加回来」先红的就是这条）
        Assert.NotEqual(TargetNames, new[] { "CultivationSpeed", "MoveSpeed", "Farming" });
    }

    [Fact]
    public void 增益只有乘数_没有加法修正()
    {
        // 今天有出处的两条收益全是乘数（聚气散 +50%、轻身术 +20%），而唯一那条加法（南瓜派 +2 耕种）
        // 连目标属性都不存在。所以「加法修正」在这个系统里没有任何消费者——建一个 Flat 字段或
        // Add 方法出来就是一条没人读的产出（铁律 11）。加回来那一刻这条就红
        Assert.False(HasMemberMatching(typeof(BuffDefinition), FlatModifierTokens));
        Assert.False(HasMemberMatching(typeof(IBuffSystem), FlatModifierTokens));
        Assert.False(HasMemberMatching(typeof(BuffTable), FlatModifierTokens));

        // 负向对照：判据本身有效（喂给真有这类成员的类型必须判违规）
        Assert.True(HasMemberMatching(typeof(StandIn.WithFlatBonus), FlatModifierTokens));
        Assert.False(HasMemberMatching(typeof(StandIn.WithMultiplier), FlatModifierTokens));
    }

    [Fact]
    public void 时长与倍率只在数据里_系统里没有天或小时的换算()
    {
        // 聚气散的「7 天」在表里就是 10080 分钟（= 7 × 24 × 60），系统只把时长加到绝对时刻上。
        // 系统里出现 SevenDays / HoursPerDay 这类名字，就是同一个数存了两处——两处迟早会漂
        Assert.False(HasMemberMatching(typeof(BuffSystem), TableNumberTokens));
        Assert.False(HasMemberMatching(typeof(IBuffSystem), TableNumberTokens));
        Assert.False(HasMemberMatching(typeof(ICultivationSpeedBonus), TableNumberTokens));

        // 负向对照
        Assert.True(HasMemberMatching(typeof(StandIn.WithHardcodedDuration), TableNumberTokens));
        Assert.False(HasMemberMatching(typeof(StandIn.WithMultiplier), TableNumberTokens));
    }

    [Fact]
    public void 存档是独立的一个键_每条只有两列()
    {
        // 增益不进 cultivation 那个 blob：玩家的修为与「他身上还有什么限时状态」是两件事，
        // 挤进去就得动 cultivation 的版本号与迁移（同 M3-5 灵脉那个键的理由）。它自己一个键、
        // 旧档缺了就是「没吃过任何增益」，天然兼容——所以这一刀**没有动** cultivation 的格式
        var buffs = new BuffSystem(BuffTable.LoadDefault());
        Assert.Equal("buffs", buffs.SaveKey);
        Assert.Equal(1, buffs.Version);

        buffs.Apply("buff_gather_qi_powder", new GameTime(1, Season.Spring, 1, 6, 0));

        using var document = JsonDocument.Parse(buffs.Serialize());
        JsonElement entry = document.RootElement.GetProperty("Active")[0];

        // 倍率、名字、目标属性都是表的输出；「还剩多久」是从这两列算出来的——存了就是同一个事实两处
        Assert.Equal(
            new[] { "BuffId", "ExpiresAtMinute" },
            entry.EnumerateObject().Select(property => property.Name));

        // 而修炼那一侧的存档格式一个字节都没动（本切片没有碰过它，所以旧档不必迁移）
        CultivationSystem cultivation = new(
            SpiritRootTable.LoadDefault(), RealmTable.LoadDefault(),
            CultivationSpeedTable.LoadDefault(RealmTable.LoadDefault()), SpiritPowerTable.LoadDefault(),
            new NoSpiritVein(), new NoSpeedBonus(), "grade_false", rootId: null, "qi_refining", stage: 1);

        Assert.Equal(3, cultivation.Version);
        Assert.DoesNotContain("buff", cultivation.Serialize(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 类型的所有成员里，有没有名字含任一关键词的（同 <c>M3Audit_Cultivation.HasMemberMatching</c>；
    /// 那个是私有的，所以这里照抄一份）。
    /// </summary>
    private static bool HasMemberMatching(Type type, IEnumerable<string> tokens) =>
        type.GetMembers().Any(
            member => tokens.Any(token => member.Name.Contains(token, StringComparison.OrdinalIgnoreCase)));

    /// <summary>只为负向对照存在的替身：判据喂给它必须判违规。</summary>
    private static class StandIn
    {
        /// <summary>「加法修正」的负向对照（南瓜派那条若被做成字段，长的就是这个样子）。</summary>
        public sealed class WithFlatBonus
        {
            public int FlatBonus => 2;
        }

        /// <summary>「把 7 天抄进代码」的负向对照。</summary>
        public sealed class WithHardcodedDuration
        {
            public const int SevenDays = 10080;
        }

        /// <summary>合法的那个形状：只有乘数、没有换算。</summary>
        public sealed class WithMultiplier
        {
            public double Multiplier { get; init; } = 1.5;
        }
    }
}

