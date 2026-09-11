using System.Collections.Generic;

namespace XingGame.Systems.Buffs;

/// <summary>
/// 增益表：每条增益作用在哪个属性上、乘多少、持续多久。数据在 <c>data/buffs/buffs.json</c>，
/// 只读、进程内共享一份。
/// </summary>
/// <remarks>
/// <b>表只答「这条增益是什么样」，不答「现在生效的是哪几条、乘出来多少」</b>：后者要拿玩家此刻的
/// 游戏时刻去比，那是 <see cref="IBuffSystem"/> 的事。分开的理由同 <c>ISpiritPowerTable</c> 与
/// <c>ISpellTable</c>：表是数据、判定是规则，改数值不该碰代码。
/// </remarks>
public interface IBuffTable
{
    /// <summary>表里的全部增益，按文件顺序。</summary>
    IReadOnlyList<BuffDefinition> Buffs { get; }

    /// <summary>按 id 取一条增益。</summary>
    /// <remarks>
    /// <b>id 认不出来是编程错误，当场抛</b>：增益 id 由调用方给出（将来「吃下这颗丹」「放出这道
    /// 法术」的那张映射写在各自的数据文件里），对不上就是那份数据写错了——静默退回某条默认增益
    /// 会让「吃了聚气散却在加速移速」这种事查无对证（同 <c>SpellTable.Get</c> 的处理）。
    /// </remarks>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">表里没有这个 id。</exception>
    BuffDefinition Get(string buffId);

    /// <summary>
    /// 按 id 取一条增益，取不到返回 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// <b>读档那条路必须走这里</b>：存档和表都是**数据**，对不上是数据错误（ADR-009 的
    /// <see cref="System.IO.InvalidDataException"/>），而不是「谁把 id 写错了」那种编程错误。
    /// 两种错要抛两种异常，所以查表也得有两种问法（同 <c>ISpiritLandTable.TryGetVein</c> 的处境：
    /// 它也只有读档这一个调用方）。
    /// </remarks>
    bool TryGet(string buffId, out BuffDefinition buff);
}
