using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace XingGame.Systems.Npc;

/// <summary>
/// 一位 NPC 的静态定义：id、姓名、角色、是否可攻略、生日、喜爱的物品、讨厌的物品、日程。
/// 数据在 <c>data/npcs/npcs.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>字段全部取自附录 C「NPC 列表（完整）」</b>（<c>docs/public/design.md</c> 1676-1692 行），
/// 因为它比 §10.1 的列表多两列——<b>ID</b> 与<b>讨厌物品</b>——而 §10.1 结尾自己写着
/// 「完整 NPC 表见附录 C」。两份对得上的地方以附录 C 为准，对不上的两处见下。
/// </para>
/// <para>
/// <b>与 §10.1 的两处不一致（已采信附录 C，理由随行注明）</b>：
/// ① 妮娅的角色，§10.1 写「未知」、附录 C 写「神秘少女」——取附录 C，那张表自称完整且逐列齐全；
/// ② §10.1 的「姓名」列把称号一起写了（「镇长 罗威尔」），附录 C 把它拆成「姓名 + 角色」两列——
/// 取附录 C 的拆法，否则「角色」列就没地方放了，且 UI 上会出现「镇长 罗威尔」这种重复称号。
/// </para>
/// <para>
/// <b>条目数取附录 C 的 14 条</b>，虽然 §10.1 / §1 的概述写着「20+ NPC」：那两处是承诺的规模，
/// 附录 C 才是列得出的清单，现在多编 6 位将来要逐条删。
/// </para>
/// <para>
/// <b>喜爱 / 讨厌物品存的是中文物品名，不是物品 id</b>——文档给的就是名字（「南瓜派」「铱锭」）。
/// M2-A 不加物品，所以这里原样存名字，**待与物品表对接**：等物品表里真有了「南瓜派」这类条目，
/// 再决定是加一层「物品名 → id」的查表还是让文档改成 id。在那之前，本类不认识任何物品 id，
/// 也绝不为这些名字编 id。
/// </para>
/// </remarks>
public sealed record NpcDefinition(
    string Id,
    string Name,
    string Role,
    bool Romanceable,
    Birthday Birthday,
    IReadOnlyList<string> LovedItems,
    IReadOnlyList<string> HatedItems,
    NpcSchedule Schedule)
{
    /// <summary>
    /// 这个物品对这位 NPC 属于哪一档（§10.2 的五个档）。
    /// </summary>
    /// <remarks>
    /// <b>只判得出三档</b>：附录 C 只给了「喜爱物品」与「讨厌物品」两列，所以不在两个名单里的
    /// 一律算中立。**「喜欢」与「不喜欢」两档在 M2-A 没有数据来源**（要等物品表能按类别匹配，
    /// 例如「花」「矿石」这类泛称），这是文档未给的部分，不是漏判。
    /// </remarks>
    public GiftTaste TasteOf(string itemName)
    {
        // 先判喜爱：两个名单都不该同时收同一个物品，加载时已经拦下，这里只是顺序确定的兜底
        if (Contains(LovedItems, itemName)) return GiftTaste.Loved;
        if (Contains(HatedItems, itemName)) return GiftTaste.Hated;

        return GiftTaste.Neutral;
    }

    /// <summary>物品名比对用序数比较：中文名不存在大小写或区域变体，差一个字就是两样东西。</summary>
    private static bool Contains(IReadOnlyList<string> names, string itemName) =>
        names.Any(name => string.Equals(name, itemName, StringComparison.Ordinal));

    /// <summary>列表字段只读视图化，防止调用方拿到定义后改动共享的那份表。</summary>
    internal static IReadOnlyList<string> Freeze(IEnumerable<string> names) =>
        new ReadOnlyCollection<string>(names.ToArray());
}
