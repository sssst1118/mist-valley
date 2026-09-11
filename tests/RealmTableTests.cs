using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 境界表（M3-1）。逐条对 §8.1 九大境界总览（<c>docs/public/design.md</c> 458-473 行）与
/// §8.2 炼气期的分层带、三条游戏绑定（476-503 行）。
/// </summary>
/// <remarks>
/// 两条最容易被「顺手改掉」的东西在这里钉着：① 炼气期是 1-13 层而**不是**四小境界；
/// ② 渡劫期只有「待劫 / 渡劫中」，文档那一格写的是「—」。它们看着像不一致，
/// 所以下一个人很容易把它们「统一」成四个——那才是改错了文档。
/// </remarks>
public class RealmTableTests
{
    private static readonly RealmTable Realms = RealmTable.LoadDefault();

    // ── §8.1 九大境界 ────────────────────────────────────────────────

    [Fact]
    public void 九大境界_表序就是修炼顺序()
    {
        // 顺序不是排版，是含义：门槛比较（「至少炼气 N 层」）和将来的突破链都按它算
        Assert.Equal(
            new[]
            {
                "qi_refining", "foundation_establishment", "golden_core", "nascent_soul",
                "spirit_transformation", "void_refinement", "body_integration",
                "great_vehicle", "tribulation",
            },
            Realms.Realms.Select(realm => realm.Id));

        Assert.Equal(0, Realms.OrderOf("qi_refining"));
        Assert.Equal(8, Realms.OrderOf("tribulation"));
    }

    [Fact]
    public void 炼气期_十三层逐层的名字()
    {
        RealmDefinition qi = Realms.Get("qi_refining");

        Assert.Equal("炼气期", qi.Name);
        Assert.Equal(13, qi.StageCount);
        Assert.Equal(
            new[]
            {
                "一层", "二层", "三层", "四层", "五层", "六层", "七层",
                "八层", "九层", "十层", "十一层", "十二层", "十三层",
            },
            qi.Stages);
    }

    [Theory]
    [InlineData(1, "筑基期")]
    [InlineData(2, "金丹期")]
    [InlineData(3, "元婴期")]
    [InlineData(4, "化神期")]
    [InlineData(5, "炼虚期")]
    [InlineData(6, "合体期")]
    [InlineData(7, "大乘期")]
    public void 筑基到合体_四个小境界_且没有分层带(int order, string name)
    {
        RealmDefinition realm = Realms.Realms[order];

        Assert.Equal(name, realm.Name);
        Assert.Equal(new[] { "初期", "中期", "后期", "大圆满" }, realm.Stages);

        // §8.2 只给了炼气期的「能力 / 游戏表现」表，其余八个大境界「文档未给，待补」——
        // 是 null 不是空字符串：空字符串会让人以为是「文档写了没有能力」
        Assert.Null(realm.BandAt(1));
    }

    [Fact]
    public void 渡劫期_只有待劫与渡劫中_不是四小境界()
    {
        // §8.1 那一格写的是「—（只分“待劫”和“渡劫中”）」。本表不把「初期/中期/后期/大圆满」
        // 当成所有大境界的通则——那是**数据**，不是代码里的循环
        RealmDefinition tribulation = Realms.Get("tribulation");

        Assert.Equal("渡劫期", tribulation.Name);
        Assert.Equal(new[] { "待劫", "渡劫中" }, tribulation.Stages);
        Assert.Equal(2, tribulation.StageCount);
    }

    // ── §8.2 炼气期的分层带 ──────────────────────────────────────────

    [Theory]
    [InlineData(1, 1, 3)]
    [InlineData(2, 1, 3)]
    [InlineData(3, 1, 3)]
    [InlineData(4, 4, 6)]
    [InlineData(5, 4, 6)]
    [InlineData(6, 4, 6)]
    [InlineData(7, 7, 9)]
    [InlineData(8, 7, 9)]
    [InlineData(9, 7, 9)]
    [InlineData(10, 10, 12)]
    [InlineData(11, 10, 12)]
    [InlineData(12, 10, 12)]
    [InlineData(13, 13, 13)]
    public void 炼气期_每一层都落在对的档里(int stage, int fromStage, int toStage)
    {
        // 边界层（3 / 4、6 / 7、9 / 10、12 / 13）就在这 13 行里：档与档的接缝是
        // 最容易差一位的地方，而差一位会让「炼气三层也能灵气浇灌」这种破例出现
        RealmBand band = Realms.Get("qi_refining").BandAt(stage)!;

        Assert.Equal(fromStage, band.FromStage);
        Assert.Equal(toStage, band.ToStage);
    }

    [Theory]
    [InlineData(1, "初期", "身体略强于凡人，可感知灵气但无法施法", "解锁“灵气感知”界面，可看到灵田的灵气浓度")]
    [InlineData(4, "中期", "可催动低阶法器和符箓，灵气储备可维持短时施法", "解锁“灵气浇灌”（消耗灵力代替浇水），可装备一阶法器")]
    [InlineData(7, "后期", "可施展低阶法术（如轻身术、小回春术），可短时御剑飞行", "解锁“轻身术”（移动速度 +20%）、“小回春术”（恢复少量体力）")]
    [InlineData(10, "巅峰", "灵气储备质变，法术威力大幅提升，可长时间御剑飞行", "解锁“灵雨术”（范围浇水）、“灵锄术”（范围耕地）")]
    [InlineData(13, "大圆满", "传说中的极致层次，丹田灵气满溢，为筑基打下完美根基", "筑基成功率 +30%，解锁隐藏剧情“炼气十三层”")]
    public void 炼气期_五档的档名与文本照抄文档的分层表(int stage, string name, string ability, string gameEffect)
    {
        RealmBand band = Realms.Get("qi_refining").BandAt(stage)!;

        Assert.Equal(name, band.Name);
        Assert.Equal(ability, band.Ability);
        Assert.Equal(gameEffect, band.GameEffect);
    }

    [Fact]
    public void 分层带_只有炼气期有()
    {
        Assert.All(
            Realms.Realms,
            realm => Assert.Equal(realm.Id == "qi_refining", realm.Bands.Count > 0));
    }

    // ── §8.2 三条游戏绑定 ────────────────────────────────────────────

    [Theory]
    [InlineData(CultivationGate.SpiritPlanting, "种植灵植", "qi_refining", 4, "灵气浇灌")]
    [InlineData(CultivationGate.SecretRealm, "进入秘境", "qi_refining", 7, "可施展基础防护法术")]
    [InlineData(CultivationGate.OuterDisciple, "招募外门弟子", "qi_refining", 5, "玩家修为足够镇场")]
    public void 游戏绑定_三条门槛逐条对文档(
        CultivationGate gate, string name, string realmId, int stage, string reason)
    {
        CultivationGateRequirement requirement = Realms.RequirementOf(gate);

        // 门槛本身（枚举）与数据（层数、理由）分开：调用方问「哪一条」，层数由表回答一次
        Assert.Equal(gate, requirement.Gate);
        Assert.Equal(name, requirement.Name);
        Assert.Equal(realmId, requirement.RealmId);
        Assert.Equal(stage, requirement.Stage);
        Assert.Equal(reason, requirement.Reason);
    }

    [Fact]
    public void 游戏绑定_不多不少三条()
    {
        Assert.Equal(3, Realms.Gates.Count);
    }

    // ── 边界与查不到 ────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(14)]
    public void 层号越界_取层名与取分层带都抛(int stage)
    {
        // 0 层与 14 层都不是可达状态：静默夹到 1 或 13 会让「他明明没到 4 层却种得了灵植」查无对证
        Assert.Throws<ArgumentOutOfRangeException>(() => Realms.Get("qi_refining").StageName(stage));
        Assert.Throws<ArgumentOutOfRangeException>(() => Realms.Get("qi_refining").BandAt(stage));
    }

    [Fact]
    public void 层号越界_按各大境界自己的档数算()
    {
        // 渡劫期只有两层：3 层在它这里是越界，在炼气期却不是——判据必须跟着大境界走
        Assert.Throws<ArgumentOutOfRangeException>(() => Realms.Get("tribulation").StageName(3));
        Assert.Equal("十三层", Realms.Get("qi_refining").StageName(13));
    }

    [Fact]
    public void 查不到的境界与门槛_抛而不是给默认值()
    {
        Assert.Throws<KeyNotFoundException>(() => Realms.Get("realm_nope"));
        Assert.Throws<KeyNotFoundException>(() => Realms.OrderOf("realm_nope"));
        Assert.Throws<ArgumentNullException>(() => Realms.Get(null!));

        Assert.False(Realms.TryGet("realm_nope", out _));
        Assert.True(Realms.TryGet("golden_core", out RealmDefinition found));
        Assert.Equal("金丹期", found.Name);
    }

    [Fact]
    public void 门槛_枚举有而表里没录_查出来是缺数据而不是默认值()
    {
        // 只录一条门槛的表：另两条查出来必须抛——给一个「默认 1 层」等于让所有玩法都解锁
        RealmTable partial = RealmTable.FromJson(TableJson(RealmJson(), GateJson()));

        Assert.Equal(1, partial.RequirementOf(CultivationGate.SpiritPlanting).Stage);
        Assert.Throws<KeyNotFoundException>(() => partial.RequirementOf(CultivationGate.SecretRealm));
        Assert.Throws<KeyNotFoundException>(() => partial.RequirementOf(CultivationGate.OuterDisciple));
    }

    [Fact]
    public void 缺省数据_确实是从工程里那份文件读出来的()
    {
        // LoadDefault 靠「从输出目录逐级上溯」找文件（技术债，M8 改注入）。找不到会抛，
        // 所以这条顺带守住「数据文件没被误删/改名」
        Assert.Equal(9, Realms.Realms.Count);
        Assert.Equal("一层", Realms.Get("qi_refining").StageName(1));
        Assert.Null(Realms.Get("foundation_establishment").BandAt(4));
    }

    // ── 坏数据：加载即抛 ──────────────────────────────────────────────

    /// <summary>同灵根表：每条都断言异常消息里那句话，坏在哪要一眼看得出来。</summary>
    [Theory]
    [MemberData(nameof(BadTables))]
    public void 坏境界表_加载即抛且指出坏在哪(string expectedInMessage, string json)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => RealmTable.FromJson(json));

        Assert.Contains(expectedInMessage, exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> BadTables()
    {
        return new TheoryData<string, string>
        {
            { "缺少 realms 数组", """{ "gates": [] }""" },
            { "realms 是空的", """{ "realms": [], "gates": [] }""" },
            { "缺少 gates 数组", $$"""{ "realms": [ {{RealmJson()}} ] }""" },
            { "不是对象的条目", TableJson("1") },
            { "空 id", TableJson(RealmJson(id: "\"\"")) },
            { "缺少 name", TableJson(RealmJson(name: "null")) },
            { "缺少 stages 数组", TableJson(RealmJson(stages: "null")) },
            { "stages 是空的", TableJson(RealmJson(stages: "[]")) },
            { "空名字", TableJson(RealmJson(stages: "[\"一\", \"\"]")) },
            { "重复 id", TableJson($"{RealmJson()}, {RealmJson()}") },
            { "bands 不是数组", TableJson(RealmJson(extra: "\"bands\": 1")) },
            { "层号从 1 起", TableJson(RealmJson(extra: $"\"bands\": [ {BandJson(0, 2)} ]")) },
            { "区间倒挂", TableJson(RealmJson(extra: $"\"bands\": [ {BandJson(2, 1)} ]")) },
            { "超出它的", TableJson(RealmJson(extra: $"\"bands\": [ {BandJson(1, 3)} ]")) },
            { "断档或重叠", TableJson(RealmJson(stages: "[\"一\", \"二\", \"三\"]", extra: $"\"bands\": [ {BandJson(1, 1)}, {BandJson(3, 3)} ]")) },
            { "只铺到", TableJson(RealmJson(extra: $"\"bands\": [ {BandJson(1, 1)} ]")) },
            { "不是对象的条目", TableJson(RealmJson(extra: "\"bands\": [ 1 ]")) },
            { "认不出的门槛 id", TableJson(RealmJson(), GateJson(id: "\"SpiritPlant\"")) },
            { "认不出的门槛 id", TableJson(RealmJson(), GateJson(id: "\"0\"")) },
            { "空的门槛 id", TableJson(RealmJson(), GateJson(id: "null")) },
            { "重复的门槛", TableJson(RealmJson(), $"{GateJson()}, {GateJson()}") },
            { "不存在的大境界", TableJson(RealmJson(), GateJson(realmId: "\"realm_nope\"")) },
            { "只有 2 个小境界", TableJson(RealmJson(), GateJson(stage: "3")) },
            { "缺少文本字段 reason", TableJson(RealmJson(), GateJson(reason: "null")) },
        };
    }

    // ── 造表 ────────────────────────────────────────────────────────

    /// <summary>默认：两个小境界、没有分层带的大境界；门槛默认指着它 1 层。</summary>
    private static string RealmJson(
        string id = "\"r\"",
        string name = "\"界\"",
        string stages = "[\"一\", \"二\"]",
        string? extra = null)
    {
        string extraText = extra is null ? string.Empty : $", {extra}";

        return $$"""{ "id": {{id}}, "name": {{name}}, "stages": {{stages}}{{extraText}} }""";
    }

    private static string BandJson(int fromStage, int toStage) =>
        $$"""{ "fromStage": {{fromStage}}, "toStage": {{toStage}}, "name": "档", "ability": "能", "gameEffect": "效" }""";

    private static string GateJson(
        string id = "\"SpiritPlanting\"",
        string realmId = "\"r\"",
        string stage = "1",
        string name = "\"门槛\"",
        string reason = "\"理由\"")
    {
        return $$"""{ "id": {{id}}, "realmId": {{realmId}}, "stage": {{stage}}, "name": {{name}}, "reason": {{reason}} }""";
    }

    private static string TableJson(string realms, string gates = "[]") =>
        $$"""{ "realms": [ {{realms}} ], "gates": [ {{gates}} ] }""";
}
