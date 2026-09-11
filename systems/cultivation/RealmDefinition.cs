using System;
using System.Collections.Generic;
using System.Linq;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 一个大境界：名字 + 小境界列表 +（炼气期才有）分层带。数据在 <c>data/cultivation/realms.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>九个境界的小境界不是同一种划分</b>（§8.1，<c>docs/public/design.md</c> 461-473 行）：
/// 炼气期是 1-13 层逐层递进，筑基到合体是初期/中期/后期/大圆满四档，
/// <b>渡劫期只有「待劫」与「渡劫中」</b>——文档在那一格写的是「—」，它不受四档那套管。
/// 所以这里的小境界是一张**列表**而不是一个数字区间：层数由数据决定，代码不认识 13 这个数。
/// </para>
/// <para>
/// <b>层号从 1 起</b>（炼气一层 = 1），因为数据表、UI 与玩家说的都是「几层」。
/// 越界的层号一律当场抛，不返回 null、不夹到边界——0 层与 14 层都不是可达的状态，
/// 静默当成 1 层或 13 层会让「他明明没到 4 层却能种灵植」这种 bug 查无对证。
/// </para>
/// </remarks>
public sealed record RealmDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> Stages,
    IReadOnlyList<RealmBand> Bands)
{
    /// <summary>小境界的档数：炼气期 13、筑基到合体各 4、渡劫期 2。</summary>
    public int StageCount => Stages.Count;

    /// <summary>第 <paramref name="stage"/> 层的名字（「三层」「大圆满」「渡劫中」）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">层号不在 1..<see cref="StageCount"/> 内。</exception>
    public string StageName(int stage)
    {
        if (stage < 1 || stage > StageCount)
            throw new ArgumentOutOfRangeException(nameof(stage), stage, $"{Name}的层号必须在 1..{StageCount} 之间");

        return Stages[stage - 1];
    }

    /// <summary>
    /// 第 <paramref name="stage"/> 层落在哪一段带里。
    /// </summary>
    /// <returns>
    /// <b>没录分层带的大境界返回 null</b>（§8.2 只给了炼气期的表，筑基及以后「文档未给，待补」）。
    /// 「这一层没有带」与「层号越界」是两件事，所以越界仍然抛——把两者混成一个 null，
    /// 调用方就分不清「文档没写」和「我自己算错了」。
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">层号不在 1..<see cref="StageCount"/> 内。</exception>
    public RealmBand? BandAt(int stage)
    {
        StageName(stage);   // 复用同一条越界判据，别写两份

        return Bands.FirstOrDefault(band => band.Contains(stage));
    }
}
