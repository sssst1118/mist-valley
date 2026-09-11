using System;

namespace XingGame.Systems.Combat;

/// <summary>
/// 一方的战斗数值。<b>两个数都由调用方给</b>——设计文档没有给任何具体的血量与攻击力
/// （玩家的没有、怪物的也没有，见 <see cref="MonsterTable"/> 类注释），所以本模块不设默认值、
/// 不设成长公式，只负责拿这两个数把一场仗算完。
/// </summary>
/// <remarks>
/// 挑战者一侧来自玩家（M3 的修炼/战斗等级系统），怪物一侧来自 <c>monsters.json</c>。
/// 构造时就校验：攻击力为 0 会让回合永远打不完（两边的血都不掉），
/// 与其让调用方在运行期看到一个转不完的循环，不如在这里当场抛。
/// </remarks>
public readonly record struct CombatStats
{
    public CombatStats(int maxHealth, int attack)
    {
        if (maxHealth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHealth), maxHealth, "生命上限必须为正");

        // 攻击为 0 则打不死对方，「结算」会变成死循环——这是编程错误，不是运行时状况
        if (attack <= 0)
            throw new ArgumentOutOfRangeException(nameof(attack), attack, "攻击力必须为正，否则回合不会结束");

        MaxHealth = maxHealth;
        Attack = attack;
    }

    public int MaxHealth { get; }

    public int Attack { get; }
}
