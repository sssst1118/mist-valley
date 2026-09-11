using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 灵根表（M3-1）。数值逐条对 §4.2（<c>docs/public/design.md</c> 162-171 行）与 §4.3 / §4.4（173-187 行）。
/// </summary>
/// <remarks>
/// 这是本文件的主要职责：**数据被「顺手调平」是最难发现的一类改动**——修炼速度从 0.3 改成 0.5、
/// 筑基概率从 5% 改成 10%，代码全都不报错，玩家也要几十小时后才会察觉「怎么这么快」。
/// 所以六档的每一列都在这里钉死，改数据必须连带改这里，改的人就会看到自己在改什么。
/// </remarks>
public class SpiritRootTableTests
{
    private static readonly SpiritRootTable Roots = SpiritRootTable.LoadDefault();

    // ── §4.2 六档品级 ─────────────────────────────────────────────────

    [Fact]
    public void 六档品级_档数与表序与文档一致()
    {
        // 表序就是 §4.2 的行序（由差到好）。顺序有意义：调档、比较都用它
        Assert.Equal(
            new[]
            {
                "grade_false", "grade_true_triple", "grade_true_dual",
                "grade_heaven", "grade_mutation", "grade_innate",
            },
            Roots.Grades.Select(grade => grade.Id));
    }

    [Theory]
    [InlineData("grade_false",       4, 5, 0.3, 5,   0.1, 0)]
    [InlineData("grade_true_triple", 3, 3, 0.6, 30,  5,   1)]
    [InlineData("grade_true_dual",   2, 2, 1.0, 60,  20,  5)]
    [InlineData("grade_heaven",      1, 1, 2.0, 95,  60,  20)]
    [InlineData("grade_mutation",    2, 2, 2.5, 95,  70,  30)]
    [InlineData("grade_innate",      1, 1, 4.0, 100, 90,  50)]
    public void 六档品级_逐档对文档的品级表(
        string id,
        int attributeCountMin,
        int attributeCountMax,
        double multiplier,
        double foundationPercent,
        double goldenCorePercent,
        double nascentSoulPercent)
    {
        SpiritRootGrade grade = Roots.GetGrade(id);

        Assert.Equal(attributeCountMin, grade.AttributeCountMin);
        Assert.Equal(attributeCountMax, grade.AttributeCountMax);
        Assert.Equal(multiplier, grade.CultivationSpeedMultiplier);
        Assert.Equal(foundationPercent, grade.FoundationSuccessPercent);
        Assert.Equal(goldenCorePercent, grade.GoldenCoreSuccessPercent);
        Assert.Equal(nascentSoulPercent, grade.NascentSoulSuccessPercent);
    }

    [Fact]
    public void 六档品级_名字照抄文档_含括号里的别名()
    {
        // 名字进 UI、也进玩家自己的话。括号里的别名不能省：§4.2 里「真灵根」是两档，
        // 名字（三灵根 / 双灵根）是它们唯一的区分
        Assert.Equal("伪灵根（五行杂灵根）", Roots.GetGrade("grade_false").Name);
        Assert.Equal("真灵根（三灵根）", Roots.GetGrade("grade_true_triple").Name);
        Assert.Equal("真灵根（双灵根）", Roots.GetGrade("grade_true_dual").Name);
        Assert.Equal("天灵根（单灵根）", Roots.GetGrade("grade_heaven").Name);
        Assert.Equal("变异灵根", Roots.GetGrade("grade_mutation").Name);
        Assert.Equal("先天异灵根", Roots.GetGrade("grade_innate").Name);
    }

    [Fact]
    public void 只有伪灵根_属性数量是个区间()
    {
        // 「4-5 种」是文档唯一给出的区间。这一条同时也是「区间不是万能字段」的证据：
        // 另外五档存上下限是为了形状一致，取值上就是文档写的那个定数
        Assert.True(Roots.GetGrade("grade_false").HasAttributeCountRange);
        Assert.Equal(4, Roots.GetGrade("grade_false").AttributeCountMin);
        Assert.Equal(5, Roots.GetGrade("grade_false").AttributeCountMax);

        Assert.All(
            Roots.Grades.Where(grade => grade.Id != "grade_false"),
            grade => Assert.False(grade.HasAttributeCountRange));
    }

    // ── §4.3 四种变异 + §4.4 四种先天异 ───────────────────────────────

    [Theory]
    [InlineData("root_thunder", "雷灵根",   "grade_mutation", "金 + 木", "杀伐极强，克制妖魔邪祟", null, "对妖兽和魔修伤害 +50%")]
    [InlineData("root_ice",     "冰灵根",   "grade_mutation", "水 + 金", "冰封控场，攻守兼备", null, "可冻结敌人，灵田可瞬间降温")]
    [InlineData("root_wind",    "风灵根",   "grade_mutation", "木 + 火", "速度冠绝同阶，擅长遁法", null, "移动速度 +40%，可短距离瞬移")]
    [InlineData("root_dark",    "暗灵根",   "grade_mutation", "土 + 金", "诡谲隐匿，魔道多见", null, "可隐身，夜间修炼速度翻倍")]
    [InlineData("root_sword",   "剑灵根",   "grade_innate", null, null, "剑道", "所有剑类法宝威力 +100%，可施展“剑意”技能")]
    [InlineData("root_space",   "空灵根",   "grade_innate", null, null, "空间之道", "解锁空间法术，可开辟个人洞天")]
    [InlineData("root_chaos",   "混沌灵根", "grade_innate", null, null, "混沌之道", "可修炼任何属性功法，无冲突")]
    [InlineData("root_star",    "星辰灵根", "grade_innate", null, null, "星辰之道", "夜间修炼速度 +200%，可引星辰之力")]
    public void 八种点过名的灵根_逐条对文档(
        string id,
        string name,
        string gradeId,
        string? mutationSource,
        string? trait,
        string? exclusiveDao,
        string gameEffect)
    {
        SpiritRootDefinition root = Roots.GetRoot(id);

        Assert.Equal(name, root.Name);
        Assert.Equal(gradeId, root.GradeId);
        Assert.Equal(mutationSource, root.MutationSource);
        Assert.Equal(trait, root.Trait);
        Assert.Equal(exclusiveDao, root.ExclusiveDao);
        Assert.Equal(gameEffect, root.GameEffect);
    }

    [Fact]
    public void 具体灵根_四种变异与四种先天异_普通品级一个都没有()
    {
        // §4.3 / §4.4 只点名了这八种。普通品级（伪 / 真三 / 真双 / 天）在文档里
        // 就是「几种属性」而不是某个名字——不替它编出「金灵根」「木灵根」
        Assert.Equal(8, Roots.Roots.Count);
        Assert.Equal(4, Roots.Roots.Count(root => root.GradeId == "grade_mutation"));
        Assert.Equal(4, Roots.Roots.Count(root => root.GradeId == "grade_innate"));
    }

    [Fact]
    public void 每种灵根都有特效文本_没有空条目()
    {
        // 特效本切片不实现机制，但文本必须有：§4.3/§4.4 每一行的存在意义就是那一格
        Assert.All(Roots.Roots, root => Assert.False(string.IsNullOrWhiteSpace(root.GameEffect)));
    }

    // ── 按 id 查 ──────────────────────────────────────────────────────

    [Fact]
    public void 按id查_查不到时抛且带上id()
    {
        Assert.Throws<KeyNotFoundException>(() => Roots.GetGrade("grade_nope"));
        Assert.Throws<KeyNotFoundException>(() => Roots.GetRoot("root_nope"));
        Assert.Throws<ArgumentNullException>(() => Roots.GetGrade(null!));

        Assert.False(Roots.TryGetGrade("grade_nope", out _));
        Assert.False(Roots.TryGetRoot("root_nope", out _));
        Assert.True(Roots.TryGetGrade("grade_heaven", out SpiritRootGrade found));
        Assert.Equal("天灵根（单灵根）", found.Name);
    }

    // ── 坏数据：加载即抛 ──────────────────────────────────────────────

    /// <summary>
    /// 每条都断言异常消息里那句话：这张表的错要能一眼看出是哪一行坏在哪，
    /// 只说「数据非法」是定位不到那一行的（同 <c>NpcTable</c> 的先例）。
    /// </summary>
    [Theory]
    [MemberData(nameof(BadTables))]
    public void 坏灵根表_加载即抛且指出坏在哪(string expectedInMessage, string json)
    {
        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(() => SpiritRootTable.FromJson(json));

        Assert.Contains(expectedInMessage, exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> BadTables()
    {
        // 每条都是「只坏一处」的完整表——坏一处而报另一处，说明校验顺序也有问题
        return new TheoryData<string, string>
        {
            { "缺少 grades 数组", """{ "roots": [] }""" },
            { "grades 是空的", TableJson() },
            { "空 id", TableJson(GradeList(GradeJson(id: "\"\""))) },
            { "空 id", TableJson(GradeList(GradeJson(id: "null"))) },
            { "缺少 name", TableJson(GradeList(GradeJson(name: "null"))) },
            { "不是对象的条目", TableJson(GradeList("1")) },
            { "小于 1", TableJson(GradeList(GradeJson(min: "0"))) },
            { "区间倒挂", TableJson(GradeList(GradeJson(min: "3", max: "2"))) },
            { "不是正数", TableJson(GradeList(GradeJson(multiplier: "0"))) },
            { "不是正数", TableJson(GradeList(GradeJson(multiplier: "-1.5"))) },
            { "百分数范围", TableJson(GradeList(GradeJson(foundation: "101"))) },
            { "百分数范围", TableJson(GradeList(GradeJson(nascentSoul: "-1"))) },
            { "重复 id", TableJson(GradeList($"{GradeJson()}, {GradeJson()}")) },
            { "不存在的品级", TableJson(GradeList(GradeJson()), RootList(RootJson(gradeId: "\"grade_nope\""))) },
            { "缺少 gameEffect", TableJson(GradeList(GradeJson()), RootList(RootJson(gameEffect: "null"))) },
            { "不是有效文本", TableJson(GradeList(GradeJson()), RootList(RootJson(extra: "\"mutationSource\": \"\""))) },
            { "共用了 id", TableJson(GradeList(GradeJson(id: "\"g\"")), RootList(RootJson(id: "\"g\"", gradeId: "\"g\""))) },
            { "缺少 roots 数组", $$"""{ "grades": [ {{GradeJson()}} ] }""" },
        };
    }

    // ── 造表 ────────────────────────────────────────────────────────

    private static string TableJson(string grades = "[]", string roots = "[]") =>
        $$"""{ "grades": {{grades}}, "roots": {{roots}} }""";

    private static string GradeList(string grades) => $"[ {grades} ]";

    private static string RootList(string roots) => $"[ {roots} ]";

    /// <summary>§4.2 的一行。参数按 JSON 字面量传，要测坏哪一处就换哪一处（<c>null</c> 表示这一格缺字段）。</summary>
    private static string GradeJson(
        string id = "\"g\"",
        string name = "\"档\"",
        string min = "1",
        string max = "1",
        string multiplier = "1.0",
        string foundation = "10",
        string goldenCore = "5",
        string nascentSoul = "1")
    {
        return $$"""
        { "id": {{id}}, "name": {{name}}, "attributeCountMin": {{min}}, "attributeCountMax": {{max}},
          "cultivationSpeedMultiplier": {{multiplier}}, "foundationSuccessPercent": {{foundation}},
          "goldenCoreSuccessPercent": {{goldenCore}}, "nascentSoulSuccessPercent": {{nascentSoul}} }
        """;
    }

    /// <summary>§4.3/§4.4 的一行。默认挂在 <c>GradeJson</c> 那一档下，所以「缺品级」之类的用例要自己换。</summary>
    private static string RootJson(
        string id = "\"r\"",
        string name = "\"雷灵根\"",
        string gradeId = "\"g\"",
        string gameEffect = "\"特效\"",
        string? extra = null)
    {
        string extraText = extra is null ? string.Empty : $", {extra}";

        return $$"""{ "id": {{id}}, "name": {{name}}, "gradeId": {{gradeId}}, "gameEffect": {{gameEffect}}{{extraText}} }""";
    }
}
