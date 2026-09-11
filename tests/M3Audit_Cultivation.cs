using System;
using System.Collections.Generic;
using System.Linq;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 修仙骨架（M3-1）的对抗性审计：本切片**刻意不做**的那些事，用反射钉住。
/// </summary>
/// <remarks>
/// <para>
/// 否定式决定没有行为可测——「表里没有修为字段」这件事，跑什么代码都证明不了。但它们同样会被
/// 下一个人「顺手」加回来，而加的人只觉得「这不就是个字段吗」，直到存档格式、数据表、UI 全都要跟着动。
/// 所以用反射断言成员不存在：加回来那一刻这条立刻红，注释明写这是**刻意**的、不是漏做。
/// 同 <c>AGENT-BRIEF</c> 的「用反射钉住不做某事的决定」。
/// </para>
/// <para>
/// 每套判据都配一条**负向对照**：反射断言最大的失败模式是查错了类型、判据写错，于是零断言绿灯——
/// 那比不写还糟（同 <c>BridgeContractTests</c> 的写法）。
/// </para>
/// </remarks>
public class M3Audit_Cultivation
{
    /// <summary>修为相关的一律不许有：每层所需修为、打坐收益、突破消耗……</summary>
    private static readonly string[] CultivationCostTokens = { "Exp", "Cost", "Progress", "Point", "Spend" };

    /// <summary>灵根特效的**机制**也不许有：本切片只录文本，不落成任何加成或行为。</summary>
    private static readonly string[] EffectTokens = { "Damage", "Freeze", "Teleport", "Invisible", "Bonus" };

    [Fact]
    public void 灵根与境界_没有每层所需修为这类字段_刻意的()
    {
        // 设计文档没给这个数；ARCHITECTURE 备案 #67/#68 里推导的那组（10 × n、10 修为/小时）
        // 至今仍是「待用户裁决」。本切片一个字段都不建，也不填 0 占位——
        // 编出来的数和真数据长得一模一样，将来没人分得清「文档没写」与「文档写了 0」
        Assert.False(HasMemberMatching(typeof(SpiritRootGrade), CultivationCostTokens));
        Assert.False(HasMemberMatching(typeof(SpiritRootDefinition), CultivationCostTokens));
        Assert.False(HasMemberMatching(typeof(RealmDefinition), CultivationCostTokens));
        Assert.False(HasMemberMatching(typeof(RealmBand), CultivationCostTokens));
        Assert.False(HasMemberMatching(typeof(CultivationSystem), CultivationCostTokens));
        Assert.False(HasMemberMatching(typeof(ICultivationSystem), CultivationCostTokens));
    }

    [Fact]
    public void 境界_没有寿元上限与游戏进度字段_本切片没有调用方()
    {
        // §8.1 的表里有这两列，但 M3-1 没有任何东西会读它们：寿元要等寿命系统，
        // 「对应游戏进度」（第 1 年春季…）是给开发看的排期注释，不是游戏数据。
        // 建了没人读的字段就是铁律 11 说的无用产出——等调用方出现时再从文档抄一次，成本更低。
        // （不用 "Age" 当判据：Stage / Stages 里就含 "age"，会误伤自己的成员）
        string[] tokens = { "Lifespan", "Progress", "Schedule", "Year" };

        Assert.False(HasMemberMatching(typeof(RealmDefinition), tokens));
        Assert.False(HasMemberMatching(typeof(ICultivationSystem), tokens));
        Assert.False(HasMemberMatching(typeof(CultivationSystem), tokens));
    }

    [Fact]
    public void 灵根特效_只是文本_没有落成任何机制_刻意的()
    {
        // §4.3/§4.4 的特效（对妖兽伤害 +50%、可冻结敌人、移动速度 +40%…）要等战斗 / 种植 /
        // 移动系统来读，本切片只把原文录进 GameEffect。这条钉住「没有偷偷做成加成」
        Assert.False(HasMemberMatching(typeof(SpiritRootDefinition), EffectTokens));
        Assert.False(HasMemberMatching(typeof(CultivationSystem), EffectTokens));

        // 负向对照的另一半：文本确实录了，不是「什么都没做」被判成通过
        Assert.All(
            SpiritRootTable.LoadDefault().Roots,
            root => Assert.False(string.IsNullOrWhiteSpace(root.GameEffect)));
    }

    [Fact]
    public void 判据本身有效_有这类成员的替身会被判出来()
    {
        Assert.True(HasMemberMatching(typeof(StandIn.WithCultivationCost), CultivationCostTokens));
        Assert.True(HasMemberMatching(typeof(StandIn.WithEffect), EffectTokens));
        Assert.False(HasMemberMatching(typeof(StandIn.WithNeither), CultivationCostTokens));
        Assert.False(HasMemberMatching(typeof(StandIn.WithNeither), EffectTokens));
    }

    /// <summary>类型的所有成员里，有没有名字含任一关键词的。</summary>
    private static bool HasMemberMatching(Type type, IEnumerable<string> tokens) =>
        type.GetMembers().Any(
            member => tokens.Any(token => member.Name.Contains(token, StringComparison.OrdinalIgnoreCase)));

    /// <summary>只为负向对照存在的替身：判据喂给它必须判违规。</summary>
    private static class StandIn
    {
        public sealed class WithCultivationCost
        {
            public int ExpPerStage => 0;
        }

        public sealed class WithEffect
        {
            public double DamageBonus => 0;
        }

        public sealed class WithNeither
        {
            public int Stage => 1;
        }
    }
}
