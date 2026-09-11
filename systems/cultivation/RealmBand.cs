namespace XingGame.Systems.Cultivation;

/// <summary>
/// 大境界内的一段层次带：§8.2 炼气期的五行（<c>docs/public/design.md</c> 479-485 行）。
/// 段名（初期/中期/后期/巅峰/大圆满）取自 §8.1 的正文（473 行），能力与游戏表现取自 §8.2 的表。
/// </summary>
/// <remarks>
/// <b>为什么是「带」而不是「层」</b>：文档给炼气期写的是 1-3 / 4-6 / 7-9 / 10-12 / 13 层五档，
/// 逐层抄成 13 条会把同一句话复制 3 遍——改一档要改三行，而漏改一行就是三行数据互相矛盾，
/// 谁生效还取决于文件顺序。层的名字（一层…十三层）另存在
/// <see cref="RealmDefinition.Stages"/> 里，两者对齐检查在加载时做。
/// </remarks>
public sealed record RealmBand(int FromStage, int ToStage, string Name, string Ability, string GameEffect)
{
    /// <summary>这一层是否落在这段带里。边界算在内（「1-3 层」含 1 层与 3 层）。</summary>
    public bool Contains(int stage) => stage >= FromStage && stage <= ToStage;
}
