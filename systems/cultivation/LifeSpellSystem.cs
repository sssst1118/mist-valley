using System;
using System.Collections.Generic;
using XingGame.Systems.Farming;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 生活法术的规则：解锁看层数、消耗走灵力池、作用落在耕地上。
/// </summary>
/// <remarks>
/// <para>
/// <b>本类没有任何状态，所以什么都不进存档</b>：解锁是拿玩家现在的层数现算的
/// （<see cref="IsUnlocked"/>），而层数与灵力都已经在 <c>cultivation</c> 那份存档里。
/// 再存一份「学会了的法术列表」，就是同一个事实存两处——升级公式或境界表一改，两份就会对不上，
/// 而症状是「都四层了还浇不了地」这种玩家没法自己解决的问题（同 <c>worldSeed</c> 不许存两处的理由）。
/// </para>
/// <para>
/// <b>先确认、再扣灵力、最后才动手</b>：三条真法术的次序一律是「查解锁 → 挑出范围里真的动得了的格
/// → 一格都没有就返回 false → 扣灵力 → 动手」。反过来的话（先扣再查）会出现「灵力花了、地没动」
/// ——这是最难看的一种错：玩家只会觉得法术时灵时不灵，而查的人从现象看不出是次序问题。
/// </para>
/// <para>
/// <b>消耗是「一次施放的价钱」，与范围里动得了几格无关</b>：表上写 50 就是 50，不会因为 3×3 里
/// 只有两格是干的就少收。两条理由：① 那要和 <see cref="ICultivationSystem.TrySpendSpirit"/> 的
/// 「全有或全无」对齐——按格收费得先有「扣一半再按结果退款」这种原语，而池子里没有、也不该有
/// （退款语义一旦存在，谁都会顺手拿它做别的事）；② 价钱随地形浮动的话，法术表上那一列就不再是
/// UI 能给玩家看的价钱。范围里**一格都动不了**时才一分不扣——那一下放了等于没放。
/// </para>
/// <para>
/// <b>已浇水 / 已开垦的格既不被重复扣费也不被重置</b>：它们在挑目标时就被跳过了，而「这一格动得动」
/// 只由 <see cref="Farmland"/> 回答一次（<c>CanWater</c> / <c>CanTill</c>）——法术不自己留一份
/// 「这格能不能浇」的判据，两份判据迟早会漂，漂了就是「扣了灵力地没动」。
/// </para>
/// </remarks>
public sealed class LifeSpellSystem : ILifeSpellSystem
{
    private readonly ISpellTable _spells;
    private readonly ICultivationSystem _cultivation;
    private readonly Farmland _farmland;

    /// <param name="farmland">
    /// 只认 <see cref="Farmland"/> 而不是 <c>FarmingSystem</c>：三条法术要用的只有「开垦」与「浇水」
    /// 两个动作，而它们住在耕地上。多要一层（背包、事件总线）只会让这个系统欠下不欠它的构造顺序。
    /// </param>
    public LifeSpellSystem(ISpellTable spells, ICultivationSystem cultivation, Farmland farmland)
    {
        _spells = spells ?? throw new ArgumentNullException(nameof(spells));
        _cultivation = cultivation ?? throw new ArgumentNullException(nameof(cultivation));
        _farmland = farmland ?? throw new ArgumentNullException(nameof(farmland));
    }

    /// <summary>
    /// 这条法术解锁了没有：玩家的境界层数够不够它要求的第几层。
    /// </summary>
    /// <remarks>
    /// <b>比较交给 <see cref="ICultivationSystem.Reaches"/>，这里不自己比层号</b>：跨大境界时
    /// 「筑基初期」高过「炼气十三层」，那是境界表的表序知识。法术表只记「要求炼气的第几层」，
    /// 而「他到了没有」已经有一个处理过跨境界的人（同 <c>Meets</c> 的写法）。
    /// </remarks>
    public bool IsUnlocked(string spellId)
    {
        SpellDefinition spell = _spells.Get(spellId);
        return _cultivation.Reaches(spell.UnlockRealmId, spell.UnlockStage);
    }

    /// <summary>
    /// 对 <paramref name="center"/> 这一格施放法术：把范围里动得了的格一次做完，扣表上那笔灵力。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>中心是朝向那一格</b>（M1-5 的目标格规矩）：单格法术就作用在它上面，范围法术以它为中心。
    /// </para>
    /// <para>
    /// <b>灵气感知走不到这里</b>：它不是对着某一格放的东西，所以当场抛而不是返回一个
    /// 「成功了但什么都没发生」的 true——后者会让桥接层以为法术放出去了，从而去播放特效。
    /// </para>
    /// </remarks>
    public bool TryCastAt(string spellId, TileCoord center)
    {
        SpellDefinition spell = _spells.Get(spellId);

        if (spell.Effect == SpellEffect.Sense)
            throw new NotSupportedException(
                $"「{spell.Name}」不是对着格子放的法术——它只有解锁判定（IsUnlocked）；"
                + "它要看的灵气浓度属于灵脉/福地那一刀，本切片还没有那个数");

        if (!IsUnlocked(spellId)) return false;

        List<TileCoord> targets = TargetsIn(spell, center);

        // 一片荒地（或一片已经浇过的地）：没有任何事可做。不扣灵力、也不返回 true——
        // 那一下等于没放，收钱就成了「按了键就掉灵力」的隐形税
        if (targets.Count == 0) return false;

        // 灵力不够就一格都不动：先比较、后相减的顺序就是这条承诺本身（TrySpendSpirit 里同一句话）。
        // 消耗为 0 的法术不进这个分支——TrySpendSpirit 不收非正数，它专门为「花掉」而设
        if (spell.SpiritCost > 0 && !_cultivation.TrySpendSpirit(spell.SpiritCost)) return false;

        foreach (TileCoord tile in targets) Apply(spell.Effect, tile);
        return true;
    }

    /// <summary>
    /// 范围里**真的动得了**的格。只读：它不改任何状态，所以可以在动灵力之前先问一遍。
    /// </summary>
    /// <remarks>
    /// 合法性的判据在耕地那边、只有一份：法术这边不写「哪一格浇得动」。
    /// </remarks>
    private List<TileCoord> TargetsIn(SpellDefinition spell, TileCoord center)
    {
        var targets = new List<TileCoord>(spell.AreaSize * spell.AreaSize);

        foreach (TileCoord tile in spell.AreaFrom(center))
        {
            if (CanAffect(spell.Effect, tile)) targets.Add(tile);
        }

        return targets;
    }

    private bool CanAffect(SpellEffect effect, TileCoord tile) => effect switch
    {
        SpellEffect.Water => _farmland.CanWater(tile),
        SpellEffect.Till => _farmland.CanTill(tile),

        // Sense 在半路上就被拦下了，走到这里说明枚举新加了成员却没在这里接上——
        // 静默当作「动不了」会让一条新法术永远放不出来，而没有任何报错
        _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, "这个法术效果没有接上耕地动作"),
    };

    /// <summary>
    /// 动手。返回值刻意不查：挑目标时问的就是这两个方法的判据本身（<c>CanWater</c> / <c>CanTill</c>），
    /// 它们与 <c>TryWater</c> / <c>TryTill</c> 在 <see cref="Farmland"/> 里共用同一处，
    /// 所以「挑得中却做不成」不成立——真有那么一天，那是耕地那边的判据与动作漂了。
    /// </summary>
    private void Apply(SpellEffect effect, TileCoord tile)
    {
        switch (effect)
        {
            case SpellEffect.Water:
                _farmland.TryWater(tile);
                break;

            case SpellEffect.Till:
                _farmland.TryTill(tile);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(effect), effect, "这个法术效果没有接上耕地动作");
        }
    }
}
