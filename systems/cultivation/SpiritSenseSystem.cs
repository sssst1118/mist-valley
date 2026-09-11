using System;
using System.Collections.Generic;
using System.Linq;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 灵气感知的规则：解锁了才读得到农场的灵脉等级、福地阶与灵气浓度。
/// </summary>
/// <remarks>
/// <para>
/// <b>本类没有任何状态，所以什么都不进存档</b>：解锁是拿玩家现在的层数现算的
/// （判据在 <see cref="ILifeSpellSystem.IsUnlocked"/>），浓度是从灵脉等级现算的
/// （判据在 <see cref="ISpiritLandSystem"/>）。存一份「看见过什么」既没用又会过期——
/// 灵脉一升级它就错了。
/// </para>
/// <para>
/// <b>「哪条法术是感知」由表回答，代码里不写死法术 id</b>：<c>data/cultivation/spells.json</c>
/// 的 <c>effect</c> 那一列（<see cref="SpellEffect.Sense"/>）就是这件事的定义，id 只是它这一行的
/// 名字。写死 id 的话，「再加一条感知法术」或「改个 id」都变成改代码——而它本该只是表里多一行
/// （同 <see cref="SpellEffect"/> 的注释：效果的种类进数据，不在代码里按 id 分支）。
/// </para>
/// <para>
/// <b>一条感知法术都没有时，本系统恒为「看不见」而不是崩溃</b>：表是外部输入，删掉一条法术不该让
/// 游戏起不来，玩家只会发现自己的感知界面一直是上锁的——而这个症状直接指向那张表。多条则以
/// **任意一条解锁**为准（感知是同一件事的多种写法，不是按顺序生效的规则）。
/// </para>
/// </remarks>
public sealed class SpiritSenseSystem : ISpiritSenseSystem
{
    private readonly List<SpellDefinition> _senseSpells;
    private readonly ILifeSpellSystem _lifeSpells;
    private readonly ISpiritLandSystem _land;

    public SpiritSenseSystem(ISpellTable spells, ILifeSpellSystem lifeSpells, ISpiritLandSystem land)
    {
        _lifeSpells = lifeSpells ?? throw new ArgumentNullException(nameof(lifeSpells));
        _land = land ?? throw new ArgumentNullException(nameof(land));

        ArgumentNullException.ThrowIfNull(spells);

        _senseSpells = spells.Spells.Where(spell => spell.Effect == SpellEffect.Sense).ToList();
    }

    public bool IsAvailable => _senseSpells.Exists(spell => _lifeSpells.IsUnlocked(spell.Id));

    public SpiritSenseReading? Read() =>
        IsAvailable ? new SpiritSenseReading(_land.Vein, _land.Land) : null;
}
