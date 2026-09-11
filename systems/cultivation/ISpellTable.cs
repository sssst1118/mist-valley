using System.Collections.Generic;

namespace XingGame.Systems.Cultivation;

/// <summary>
/// 生活法术表：每条法术的解锁层数、灵力消耗与作用范围。数据在 <c>data/cultivation/spells.json</c>，
/// 只读、进程内共享一份。
/// </summary>
/// <remarks>
/// <para>
/// <b>表只答「这条法术是什么样」，不答「玩家现在放不放得出」</b>：后者要拿玩家的层数与灵力去比，
/// 那是 <see cref="ICultivationSystem"/> 与 <see cref="ILifeSpellSystem"/> 的事。分开的理由同
/// <c>ISpiritPowerTable</c>：表是数据、判定是规则，改数值不该碰代码。
/// </para>
/// <para>
/// <b>§8.2 里另外两条法术刻意不录</b>：轻身术要有持续时间的增益/状态系统、小回春术要有「体力」
/// 这个属性——两者在这个游戏里都还不存在，录进来就是两条没有调用方的死数据（铁律 11）。
/// 它们各自的系统落地时再补进本表，见 <c>M3Audit_Cultivation</c> 同款的钉子。
/// </para>
/// </remarks>
public interface ISpellTable
{
    /// <summary>表里的全部法术，按文件顺序（也就是 §8.2 的解锁顺序：感知 → 浇灌 → 灵雨 → 灵锄）。</summary>
    IReadOnlyList<SpellDefinition> Spells { get; }

    /// <summary>按 id 取一条法术。</summary>
    /// <remarks>
    /// <b>id 认不出来是编程错误，当场抛</b>：法术 id 来自本表（桥接层的快捷键绑的就是它），
    /// 对不上就是 id 写错了——静默退回某条默认法术会让「按了 F 键却放出了灵雨术」这种事故
    /// 查无对证（同 <c>Farmland.TryPlant</c> 对未知种子 id 的处理）。
    /// </remarks>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">表里没有这个 id。</exception>
    SpellDefinition Get(string spellId);
}
