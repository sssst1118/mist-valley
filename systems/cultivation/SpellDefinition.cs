using System.Collections.Generic;
using XingGame.Systems.Farming;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 一条生活法术：解锁层数 + 灵力消耗 + 作用范围。数据在 <c>data/cultivation/spells.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>解锁记的是「第几层」，不是 §8.2 的「第几档」</b>：文档把炼气期排成 1-3 / 4-6 / 7-9 / 10-12 / 13
/// 五档，那是**排版**；而「他够不够格放这个法术」问的是层号。每条法术的档下沿就是它的解锁层
/// （灵气浇灌落在 4-6 那一档 ⇒ 4 层）。存成区间等于把「4-6 里那个 6」也变成一份没人读的数据，
/// 而且它会在有人把档改宽时悄悄过期。
/// </para>
/// <para>
/// <b>消耗是「一次施放的价钱」</b>：灵气浇灌 5（备案 #75 的 5 灵力/格，而它只作用于 1 格）、
/// 灵雨术与灵锄术各 50（备案 #70/#75）。范围里动得了几格不影响价钱——理由写在
/// <see cref="LifeSpellSystem.TryCastAt"/> 上。
/// </para>
/// </remarks>
public sealed record SpellDefinition(
    string Id,
    string Name,
    SpellEffect Effect,
    string UnlockRealmId,
    int UnlockStage,
    int SpiritCost,
    int AreaSize)
{
    /// <summary>
    /// 作用范围：以 <paramref name="center"/> 为中心、边长 <see cref="AreaSize"/> 的正方形里的所有格。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>中心是调用方给的「目标格」，而目标格按 M1-5 的规矩是朝向相邻的那一格</b>
    /// （ARCHITECTURE「目标格怎么算」：<c>FarmGrid.ToTile(玩家世界坐标 + Facing × TileSize)</c>）。
    /// 生活法术照同一条规矩：玩家站在要洒的那片地前面放法，一点都不用踩上去。备案 #75 那句
    /// 「以玩家为中心」说的是**不朝别处放**，而不是「以脚下那一格为中心」。
    /// </para>
    /// <para>
    /// <b>Y 在外、X 在内，顺序固定</b>：同一份输入永远产出同一串格。谁先生效要是取决于遍历顺序，
    /// 就是各表都专门拦过的那类幽灵 bug（更实际的是：用例里断言的顺序也会变得靠不住）。
    /// </para>
    /// </remarks>
    public IEnumerable<TileCoord> AreaFrom(TileCoord center)
    {
        int half = AreaSize / 2;

        for (int dy = -half; dy <= half; dy++)
        {
            for (int dx = -half; dx <= half; dx++)
            {
                yield return new TileCoord(center.X + dx, center.Y + dy);
            }
        }
    }
}
