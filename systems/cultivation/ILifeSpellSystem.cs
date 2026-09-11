using XingGame.Systems.Farming;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 生活法术：§8.2 里炼气期那几条能落地的东西——灵气浇灌（4 层起）、灵雨术与灵锄术（各 10 层起），
/// 外加只有解锁判定的灵气感知（1 层起）。
/// </summary>
/// <remarks>
/// <para>
/// <b>规则在这里，按键不在这里</b>（ADR-007）：本接口只收「哪条法术、瞄哪一格」，
/// 按哪个键、法术怎么选中、冷却条画在哪，都是桥接层的事。
/// </para>
/// <para>
/// <b>目标格按 M1-5 的规矩由调用方算好再传进来</b>：朝向相邻的那一格，不是脚下那一格
/// （ARCHITECTURE「目标格怎么算」）。范围法术以传进来的这一格为中心，所以玩家是站在要洒的
/// 那片地前面放法，而不是踩在地中间。
/// </para>
/// <para>
/// <b>「学会了哪些法术」没有单独的入口，也不进存档</b>：它派生自境界层数，见
/// <see cref="IsUnlocked"/>。这是刻意的——同一个事实存两处，早晚会对不上。
/// </para>
/// </remarks>
public interface ILifeSpellSystem
{
    /// <summary>
    /// 这条法术解锁了没有：玩家的境界层数够不够它要求的第几层（现算，不记录）。
    /// </summary>
    /// <remarks>
    /// 灵气感知的入口就是它：§8.2 说 1-3 层「可感知灵气但无法施法」，解锁的是一个界面，
    /// 而那个界面要看的东西（灵气浓度）属于灵脉/福地那一刀。本切片它就是一个查询。
    /// </remarks>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// 法术表里没有这个 id（见 <see cref="ISpellTable.Get"/>）——id 写错是编程错误，不当作「没解锁」。
    /// </exception>
    bool IsUnlocked(string spellId);

    /// <summary>
    /// 对 <paramref name="center"/> 这一格施放一条法术。成功施放返回 true。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>返回 false 的三种情形都不扣灵力、也不动地形</b>：层数不够、范围里一格都动不了、
    /// 灵力不够。三者混在一个 false 里是刻意的——调用方想知道「为什么放不出」有两个专门的查询
    /// （<see cref="IsUnlocked"/> 与灵力池），不该靠布尔值猜。
    /// </para>
    /// <para>
    /// <b>只有 Sense 类法术会抛</b>：灵气感知不是对着某一格放的东西，硬要一个落点就是编出来的。
    /// 与其返回一个「成功了但什么都没发生」的 true，不如当场说清它没有这条路。
    /// </para>
    /// </remarks>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">法术表里没有这个 id。</exception>
    /// <exception cref="System.NotSupportedException"><paramref name="spellId"/> 是 <see cref="SpellEffect.Sense"/> 类法术。</exception>
    bool TryCastAt(string spellId, TileCoord center);
}
