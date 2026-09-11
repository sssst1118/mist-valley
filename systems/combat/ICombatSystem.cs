namespace XingGame.Systems.Combat;

/// <summary>
/// 遭遇与结算。桥接层按接口取用，不按具体类型取（M2-A 七条共同规矩第 2 条）。
/// </summary>
/// <remarks>
/// <b>本模块只做可测的回合式结算</b>：不做动画、不做实时操作、不引 Godot（§7.2 的轻攻击/重攻击/
/// 格挡/闪避/法术都属于操作层，M8 打磨时再在桥接层做）。掉落进背包，进不去的记在
/// <see cref="CombatResult.Overflow"/> 里。
/// </remarks>
public interface ICombatSystem
{
    IMonsterTable Monsters { get; }

    /// <summary>矿洞进度（当前层）。持久化在它自己身上（<c>ISaveable</c>）。</summary>
    MineProgress Progress { get; }

    /// <summary>
    /// 按矿洞层数掷一次遭遇。<b>同 (layer, seed) 必得同一结果</b>——不用 <c>Random.Shared</c>、
    /// 不读时钟，照 <c>WeatherGenerator</c> 的先例，便于测试与回放。
    /// </summary>
    /// <returns>该层可能出现的怪物之一；该层没有怪物时 null。</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">层号不在 1..矿洞层数 之间。</exception>
    MonsterDefinition? RollEncounter(int layer, int seed);

    /// <summary>结算一次遭遇：回合式，挑战者先出手，双方轮流打，谁的血先到 0 谁输。</summary>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">怪物 id 不在怪物表里。</exception>
    CombatResult Resolve(string monsterId, CombatStats challenger);
}
