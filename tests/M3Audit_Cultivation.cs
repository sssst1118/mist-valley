using System;
using System.Collections.Generic;
using System.Linq;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 修仙骨架（M3-1）与打坐（M3-2）的对抗性审计：**刻意不做**的那些事，用反射钉住。
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

    /// <summary>
    /// §8.3 的表里**还没接上**的那六个因素（聚灵阵 / 风水 / 功法品阶 / 丹药 / 心境 / 双修）
    /// 一个字段都不许有。它们的系统都还不存在，见下面的用例。
    /// </summary>
    /// <remarks>
    /// <b>M3-5 起这张名单从七个减到六个，少的那一个是「灵脉等级」</b>——它接上了：§8.8 的
    /// 灵脉六级 → <c>SpiritLandSystem</c> → <c>SpeedMultiplierAt</c> 连乘的第四项。
    /// <b>缩法不是把关键词删掉</b>：灵脉那个词（<see cref="SpiritVeinTokens"/>）换了个守的对象，
    /// 仍在守着同一件事（数值只许有一个来源），见下面第二条用例。剩下六个一个都没放走：
    /// 判据本身一个字没动，还是逐个类型地查，负向对照（替身）也照旧。
    /// </remarks>
    private static readonly string[] UnconnectedFactorTokens =
    {
        "Formation",   // 聚灵阵
        "FengShui",    // 风水
        "Technique",   // 功法品阶
        "Pill",        // 丹药
        "Mood",        // 心境
        "Heart",
        "Dual",        // 双修
    };

    /// <summary>
    /// 灵脉等级（M3-5 起**已经接上**）：它的数值只许有一个来源——<c>spirit_land.json</c> 那六级，
    /// 由 <c>SpiritLandSystem</c> 读出来、经 <see cref="ISpiritVeinSource"/> 交给打坐。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么这个关键词留着，而不是从上面那张名单里删掉</b>：删掉之后**修炼速度表那两个类型**
    /// 就没人守了——把灵气浓度抄进 <c>cultivation_speed.json</c>（或让那张表自己乘一遍）就是同一个
    /// 事实存两处，而两份会漂；漂了以后「灵脉 +10%」到底算了几次，没人说得清。
    /// 留着，这一条就还守得住（那张表的键集合另有一条用例守着）。
    /// </para>
    /// <para>
    /// <b>系统那两个类型按领域豁免</b>（<c>exemptToken: "SpiritVein"</c>）：打坐系统**必须**持有
    /// 那个浓度来源——它是连乘的第四项。豁免按「SpiritVein」这一族开，不是按某个具体成员名开：
    /// 这样「顺手把 §8.8 的数字写进系统」（<c>VeinDragonMultiplier</c> 之类）仍然会被这条抓到名。
    /// 名字抓不到的硬编码由**行为**用例守：<c>SpiritLandTests</c> 的替身表——换个表就换一套数，
    /// 系统的成员名里藏不藏数字，只有行为验得出来（同 M3-3 对灵力那组数的做法）。
    /// </para>
    /// <para>
    /// <b>灵脉那几张表自己的类型不在名单里</b>（<c>SpiritVeinGrade</c> / <c>ISpiritLandTable</c> /
    /// <c>SpiritLandSystem</c>…）：它们就是那个「唯一的来源」，名字里当然有 Vein。
    /// 这条钉的是**别的类型里没有**。
    /// </para>
    /// </remarks>
    private static readonly string[] SpiritVeinTokens = { "Vein" };

    /// <summary>
    /// 灵脉那一族在系统类型上的豁免标记。**判据与用例共用这一个常量**：豁免要是被改宽
    /// （比如改成 "Spirit"，那会连 <c>SpiritFormationMultiplier</c> 一起放走），
    /// 「判据本身有效」那条里拿它喂聚灵阵替身的断言立刻红——豁免值本身也有用例守着。
    /// </summary>
    private const string SpiritVeinExemption = "SpiritVein";

    [Fact]
    public void 灵根与境界定义_没有每层所需修为这类字段_数值在专门的表上()
    {
        // M3-1 那轮这个数还没定（备案 #67/#68 当时是「待用户裁决」），所以一个字段都不建；
        // M3-2 定下来之后它落在 CultivationSpeedTable 上，**不是**落在灵根品级与境界定义里：
        // 档位管倍率、境界管名字与层次、进度曲线管开销——三者分开，改一处牵不动另两处。
        // 系统类也不许自己藏一份数值（修为是「本层已攒的进度」，阈值只有表答得出）
        Assert.False(HasMemberMatching(typeof(SpiritRootGrade), CultivationCostTokens));
        Assert.False(HasMemberMatching(typeof(SpiritRootDefinition), CultivationCostTokens));
        Assert.False(HasMemberMatching(typeof(RealmDefinition), CultivationCostTokens));
        Assert.False(HasMemberMatching(typeof(RealmBand), CultivationCostTokens));

        // M3-3 起灵力的消耗原语（TrySpendSpirit）也含 "Spend" 这个词，但它管的是**另一笔账**：
        // 灵力那组数在 spirit_power.json 上，由替身表用例行为性地钉着（SpiritPowerTests 的
        // 「换个替身表就换一套数」——系统的成员名里藏不藏数字，只有行为验得出来）。
        // 所以这里按「不涉灵力」筛一次，而不是把 Spend 从关键词里删掉：删了等于放走 TrySpendCultivation
        Assert.False(HasMemberMatching(typeof(CultivationSystem), CultivationCostTokens, exemptToken: "Spirit"));
        Assert.False(HasMemberMatching(typeof(ICultivationSystem), CultivationCostTokens, exemptToken: "Spirit"));
    }

    /// <summary>法术前的那批消耗常量（备案 #70）**刻意不录**：跟着法术那一刀走。</summary>
    private static readonly string[] SpellCostTokens = { "Cost", "Spell", "Cast" };

    [Fact]
    public void 灵力_只录上限与恢复_没有法术消耗的常量_刻意的()
    {
        // 备案 #70 的四个数（轻身术 10 / 小回春术 30 / 灵雨术 50 / 灵锄术 50）跟着**法术**那一刀走：
        // 本切片一个调用方都没有，录进来就是四个没人读的常量（铁律 11）。消耗的**原语**在系统上
        // （TrySpendSpirit），泛得只有「数量」一个参数——「花得起哪几个法术」该由法术表回答。
        // 数据文件那一半由 SpiritPowerTableTests 的「原始 JSON 里没有法术消耗的键」守着：
        // 加载器不认识的多余键会被静静忽略，只盯代码看不出来
        Assert.False(HasMemberMatching(typeof(ISpiritPowerTable), SpellCostTokens));
        Assert.False(HasMemberMatching(typeof(SpiritPowerTable), SpellCostTokens));

        // 睡眠也不是费率：备案 #71 的第三条是「一步回满」，没有时长可言，所以它不在枚举里
        // （它是 RecoverSpiritOnSleep 那个零参数入口）——加回来那一刻这条就红
        Assert.DoesNotContain("Sleep", Enum.GetNames<SpiritRecovery>());

        // 负向对照：判据本身有效，喂给真有这类成员的替身必须判违规
        Assert.True(HasMemberMatching(typeof(StandIn.WithSpellCost), SpellCostTokens));
        Assert.False(HasMemberMatching(typeof(StandIn.WithNeither), SpellCostTokens));
    }


    [Fact]
    public void 修炼速度_没接上的六个因素一个字段都没有_刻意的()
    {
        // §8.3 的表里除了灵根 / 季节 / 时辰，还列着聚灵阵、风水、功法品阶、丹药、心境、双修六行
        // （第七行「灵脉等级」在 M3-5 接上了，见下一条用例）。它们各自的系统都还不存在，
        // **先建字段就是建一批没人读的数**——而编出来的数与真数据长得一模一样，将来没人分得清
        // 「文档写了」与「我们编的」，所以连 TODO 常量都不留（铁律 11）。它们跟着各自的系统一起
        // 落地，加回来那一刻这条就红。
        //
        // M3-5 只把「灵脉」这一个从名单上拿走，**判据本身一个字没动**：还是逐个类型地查、
        // 还是同一套负向对照。灵脉那一侧不是不守了，而是换了守的对象（下一条用例）。
        // 数据文件那一半由 CultivationSpeedTableTests 的「数据文件里只有打坐的账」守着：
        // 加载器不认识的多余键会被静静忽略，只盯代码看不出来
        Assert.False(HasMemberMatching(typeof(CultivationSystem), UnconnectedFactorTokens));
        Assert.False(HasMemberMatching(typeof(ICultivationSystem), UnconnectedFactorTokens));
        Assert.False(HasMemberMatching(typeof(CultivationSpeedTable), UnconnectedFactorTokens));
        Assert.False(HasMemberMatching(typeof(ICultivationSpeedTable), UnconnectedFactorTokens));
    }

    [Fact]
    public void 灵脉_数值只有一个来源_别的类型里一个字段都没有_刻意的()
    {
        // 灵脉等级的数值只有一个来源：data/cultivation/spirit_land.json 的六级（§8.8 的
        // 「灵气浓度」列）。**修炼速度表那两个类型一个字段都不许有它**——加成要乘进去的地方只有
        // CultivationSystem.SpeedMultiplierAt 那一行，让那张表自己也带一份就是同一个事实存两处
        Assert.False(HasMemberMatching(typeof(CultivationSpeedTable), SpiritVeinTokens));
        Assert.False(HasMemberMatching(typeof(ICultivationSpeedTable), SpiritVeinTokens));

        // 系统那一侧**允许**持有浓度来源（它是连乘的第四项），按「SpiritVein」这一族豁免。
        // 豁免的是名字而不是判据：同一个类型里叫 VeinDragonMultiplier 的成员照旧被判违规
        Assert.False(HasMemberMatching(typeof(CultivationSystem), SpiritVeinTokens, exemptToken: SpiritVeinExemption));
        Assert.False(HasMemberMatching(typeof(ICultivationSystem), SpiritVeinTokens, exemptToken: SpiritVeinExemption));
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
        Assert.True(HasMemberMatching(typeof(StandIn.WithUnconnectedFactor), UnconnectedFactorTokens));
        Assert.False(HasMemberMatching(typeof(StandIn.WithNeither), CultivationCostTokens));
        Assert.False(HasMemberMatching(typeof(StandIn.WithNeither), EffectTokens));
        Assert.False(HasMemberMatching(typeof(StandIn.WithNeither), UnconnectedFactorTokens));

        // 上面几条「本类型里没有」的断言全是**否定式**：判据要是坏了（关键词写错、反射查错了类型），
        // 它们会整整齐齐地全绿。这条与下面那个替身就是防这个的
        Assert.False(HasMemberMatching(typeof(StandIn.WithUnconnectedFactor), EffectTokens));

        // 灵脉那一条（"Vein" + 按 SpiritVeinExemption 豁免）的两半都要成立，判据才叫有效：
        // 没有豁免时含 Vein 的成员照旧被判违规，带上豁免后放行
        Assert.True(HasMemberMatching(typeof(StandIn.WithSpiritVein), SpiritVeinTokens));
        Assert.False(HasMemberMatching(
            typeof(StandIn.WithSpiritVein), SpiritVeinTokens, exemptToken: SpiritVeinExemption));

        // 而豁免是按领域开的，不是「带 Vein 就放行」的漏洞：聚灵阵那个替身**带着同一个豁免**
        // 过一遍，仍然会被判违规（它含的是另一个关键词）——剩下六个就是这么还守着的。
        // 这一条同时守着豁免值本身：把它改宽（"Spirit"、"S" 之类）就会连这个替身一起放走
        Assert.True(HasMemberMatching(
            typeof(StandIn.WithUnconnectedFactor), UnconnectedFactorTokens, exemptToken: SpiritVeinExemption));
    }

    /// <summary>
    /// 类型的所有成员里，有没有名字含任一关键词的。
    /// </summary>
    /// <param name="exemptToken">
    /// 名字里含这一段的成员不参与判定：同一个关键词有时会**误伤另一个领域**（如 "Spend" 同时命中
    /// 修为的账与灵力的原语、"Vein" 同时命中「许不许有灵脉」与「灵脉那一族的合法成员」），
    /// 那时按领域筛一次，比把关键词删掉更安全——删掉等于给所有领域开口子。
    /// 这一条要**按域开、别按常见前缀开**：写成 "Spirit" 会把 "SpiritFormationMultiplier"（聚灵阵）
    /// 一起放走，而聚灵阵正是名单上还要守着的下一个（上面那条替身对照就是防这个的）。
    /// </param>
    private static bool HasMemberMatching(Type type, IEnumerable<string> tokens, string? exemptToken = null) =>
        type.GetMembers().Any(
            member => !(exemptToken is not null
                        && member.Name.Contains(exemptToken, StringComparison.OrdinalIgnoreCase))
                      && tokens.Any(token => member.Name.Contains(token, StringComparison.OrdinalIgnoreCase)));

    /// <summary>只为负向对照存在的替身：判据喂给它必须判违规。</summary>
    private static class StandIn
    {
        public sealed class WithCultivationCost
        {
            public int ExpPerStage => 0;
        }

        public sealed class WithSpellCost
        {
            public int LightBodyCost => 10;
        }

        public sealed class WithEffect
        {
            public double DamageBonus => 0;
        }

        /// <summary>还没接上的那六个的负向对照（聚灵阵）。</summary>
        public sealed class WithUnconnectedFactor
        {
            public double SpiritFormationMultiplier => 1.0;
        }

        /// <summary>灵脉那一族的负向对照：豁免没写对（漏了 "SpiritVein"）时它必须被判违规。</summary>
        public sealed class WithSpiritVein
        {
            public double SpiritVeinMultiplier => 1.1;
        }

        public sealed class WithNeither
        {
            public int Stage => 1;
        }
    }
}
