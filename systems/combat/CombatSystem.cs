using System;
using System.Collections.Generic;
using XingGame.Systems.Items;

namespace XingGame.Systems.Combat;

/// <summary>
/// 战斗与矿洞：按层掷遭遇、结算一次遭遇、把掉落放进背包。
/// </summary>
/// <remarks>
/// <para>
/// <b>本类只做「可测的那一层」</b>：掷怪、回合结算、掉落入包。§7.2 的实时操作（轻攻击/重攻击/格挡/
/// 闪避/法术）与武器属性（速度/暴击率/击退）不进纯 C# 区——那些要么需要数值（文档一个都没给）、
/// 要么是手感，归桥接层与 M8 打磨。
/// </para>
/// <para>
/// <b>没有随机伤害</b>：一次结算的结果完全由「怪物数值 + 挑战者数值」决定，同参数必得同结果。
/// 掷怪才有种子，且用的是 (seed, layer) 定种构造的 <see cref="Random"/>，不碰 <c>Random.Shared</c>、
/// 不读时钟（照 <c>WeatherGenerator</c> 的先例）——不然存档回放与复现 bug 都无从谈起。
/// </para>
/// <para>
/// <b>为什么需要矿洞进度</b>：层号的合法区间是矿洞的属性（§7.1 的 120 层），不是怪物表的属性，
/// 所以本类从 <see cref="Progress"/> 里读它；顺带也让「掷第几层」有唯一的一份真相。
/// </para>
/// </remarks>
public sealed class CombatSystem : ICombatSystem
{
    private readonly IInventory _inventory;

    public CombatSystem(IMonsterTable monsters, MineProgress progress, IInventory inventory)
    {
        Monsters = monsters ?? throw new ArgumentNullException(nameof(monsters));
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public IMonsterTable Monsters { get; }

    public MineProgress Progress { get; }

    public MonsterDefinition? RollEncounter(int layer, int seed)
    {
        if (!Progress.Mine.Contains(layer))
            throw new ArgumentOutOfRangeException(
                nameof(layer), layer, $"矿洞层号必须在 1..{Progress.Mine.LayerCount} 之间");

        IReadOnlyList<MonsterDefinition> candidates = Monsters.ForLayer(layer);

        // 该层一只怪都没有是可能的（自定义数据把怪物都限定在别的层），不是错误：返回 null，让上层决定怎么办
        if (candidates.Count == 0) return null;

        int index = new Random(Mix(seed, layer)).Next(candidates.Count);
        return candidates[index];
    }

    public CombatResult Resolve(string monsterId, CombatStats challenger)
    {
        MonsterDefinition monster = Monsters.Get(monsterId);

        int challengerHealth = challenger.MaxHealth;
        int monsterHealth = monster.MaxHealth;
        int rounds = 0;
        bool victory;

        // 挑战者先出手。文档没规定谁先手（§7.2 只有武器速度这个属性、没有数值），
        // 取「玩家先手」是未定义项的取值，已报主会话备案。
        while (true)
        {
            rounds++;

            monsterHealth = Math.Max(0, monsterHealth - challenger.Attack);
            if (monsterHealth == 0)
            {
                victory = true;
                break;
            }

            challengerHealth = Math.Max(0, challengerHealth - monster.Attack);
            if (challengerHealth == 0)
            {
                victory = false;
                break;
            }
        }

        if (!victory)
        {
            // 没打赢就没有掉落（§7.2 的怪物掉落是击杀掉落）；血量也已被钳在 0，不存在负血
            return new CombatResult(
                monster.Id, false, challengerHealth, monsterHealth, rounds,
                Array.Empty<ItemStack>(), Array.Empty<ItemStack>());
        }

        (IReadOnlyList<ItemStack> loot, IReadOnlyList<ItemStack> overflow) = GrantDrops(monster);

        return new CombatResult(monster.Id, true, challengerHealth, monsterHealth, rounds, loot, overflow);
    }

    /// <summary>
    /// 把掉落装进背包，并<b>如实报出装不下的部分</b>。
    /// <para>
    /// 不走「先查背包够不够、再决定给不给」那条路：<c>Inventory.Add</c> 已经返回了没装下的数量，
    /// 按它分账比在本类里重算一遍堆叠规则可靠——重算就等于把背包的堆叠上限抄了第二份。
    /// </para>
    /// </summary>
    private (IReadOnlyList<ItemStack> Loot, IReadOnlyList<ItemStack> Overflow) GrantDrops(MonsterDefinition monster)
    {
        var loot = new List<ItemStack>();
        var overflow = new List<ItemStack>();

        foreach (ItemStack drop in monster.Drops)
        {
            int leftover = _inventory.Add(drop.ItemId, drop.Count);

            if (leftover < drop.Count) loot.Add(new ItemStack(drop.ItemId, drop.Count - leftover));
            if (leftover > 0) overflow.Add(new ItemStack(drop.ItemId, leftover));
        }

        return (loot.AsReadOnly(), overflow.AsReadOnly());
    }

    /// <summary>
    /// 把 (seed, layer) 混成随机种子。末尾的雪崩步是必要的：<see cref="Random"/> 的定种构造对相邻种子
    /// 会产生相关的首个输出，直接用线性组合会让相邻两层的遭遇高度雷同。
    /// <para>
    /// 与 <c>WeatherGenerator.Mix</c> 同款但各自一份：那边是私有方法，而 <c>core/</c> 是别的领域的文件
    /// （本切片不许碰），为共用十行而改公共接口不划算。两处混法一旦要统一，改的是这两份。
    /// </para>
    /// </summary>
    private static int Mix(int seed, int layer)
    {
        unchecked
        {
            int hash = seed;
            hash = (hash * 31) + layer;
            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= unchecked((int)0x846ca68b);
            hash ^= hash >> 16;
            return hash;
        }
    }
}
