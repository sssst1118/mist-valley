using System;
using System.Collections.Generic;
using System.Linq;
using XingGame.Core.Save;
using XingGame.Systems.Cultivation;
using XingGame.Systems.Farming;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 灵气感知（M3-5）：§8.2 那条「解锁『灵气感知』界面，可看到灵田的灵气浓度」——**解锁前后
/// 查得到的东西不一样**，这就是本文件要钉住的那一件事。
/// </summary>
/// <remarks>
/// <para>
/// 缺省法术表里灵气感知是 1 层解锁（§8.2 的 1-3 层「可感知灵气但无法施法」），所以拿真表测不出
/// 「没解锁」那一半——那一半用**替身法术表**（把感知挪到 4 层）来测。两者都要有：
/// 真表证「一层就看得见」对得上 §8.2，替身证门槛真的在起作用（而不是恒为真）。
/// </para>
/// <para>
/// 耕地用真的（同 <c>LifeSpellTests</c>）：感知系统要经 <see cref="ILifeSpellSystem.IsUnlocked"/>
/// 拿门槛，而那是生活法术系统的一部分——替身会让「门槛复用的是不是同一份判据」这个问题在用例里
/// 自动成立，而那正是要验的东西。
/// </para>
/// </remarks>
public class SpiritSenseTests
{
    private const string Sense = "spell_spirit_sense";

    /// <summary>§8.2 的「灵气浇灌」：它落在格子上，与感知不是一回事（下面对照用）。</summary>
    private const string Watering = "spell_spirit_watering";

    private const string ItemsJson = """
    {
      "items": [
        { "id": "seed_parsnip", "name": "防风草种子", "description": "春季播种。", "category": "Seed", "maxStack": 999, "buyPrice": 20, "sellPrice": 0 },
        { "id": "crop_parsnip", "name": "防风草",     "description": "春季作物。", "category": "Crop", "maxStack": 999, "buyPrice": 0,  "sellPrice": 35 }
      ]
    }
    """;

    private const string CropsJson = """
    {
      "crops": [
        { "seedId": "seed_parsnip", "cropId": "crop_parsnip", "growthDays": 4, "regrowable": false }
      ]
    }
    """;

    private static readonly SpiritRootTable Roots = SpiritRootTable.LoadDefault();
    private static readonly RealmTable Realms = RealmTable.LoadDefault();
    private static readonly CultivationSpeedTable Speed = CultivationSpeedTable.LoadDefault(Realms);
    private static readonly SpiritPowerTable SpiritPower = SpiritPowerTable.LoadDefault();
    private static readonly SpellTable Spells = SpellTable.LoadDefault(Realms);
    private static readonly SpiritLandTable Land = SpiritLandTable.LoadDefault();
    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);
    private static readonly CropTable Crops = CropTable.FromJson(CropsJson, Items);

    private readonly Farmland _farmland = new(Crops);

    private static CultivationSystem Cultivation(int stage, string realmId = "qi_refining") =>
        new(Roots, Realms, Speed, SpiritPower, new SpiritLandSystem(Land, "vein_micro", "land_1"),
            new NoSpeedBonus(),
            "grade_false", rootId: null, realmId, stage);

    private static SpiritLandSystem Farm(string veinId = "vein_micro") => new(Land, veinId, "land_1");

    private SpiritSenseSystem NewSystem(
        ISpellTable spells, CultivationSystem cultivation, SpiritLandSystem? farm = null) =>
        new(spells, new LifeSpellSystem(spells, cultivation, _farmland), farm ?? Farm());

    // ── 缺省表：一层就看得见 ────────────────────────────────────────

    [Fact]
    public void 缺省表_炼气一层就看得见_看到的是农场起点()
    {
        // §8.2 的 1-3 层：「可感知灵气但无法施法」——感知从一层起，而它不花灵力（消耗 0）。
        // 读数是农场级的：几级灵脉、几阶福地、浓度多少
        SpiritSenseSystem sense = NewSystem(Spells, Cultivation(1));

        Assert.True(sense.IsAvailable);

        SpiritSenseReading? reading = sense.Read();
        Assert.NotNull(reading);

        Assert.Equal("微型灵脉", reading!.Vein.Name);
        Assert.Equal(1.10, reading.ConcentrationMultiplier, precision: 10);
        Assert.Equal("福地", reading.Land.Name);
        Assert.Equal(1, reading.Land.Order);
    }

    [Fact]
    public void 读数是农场此刻的样子_灵脉升上去读数就变()
    {
        // 读数不带状态，是每次现取的：农场升到龙脉之后，同一次会话里再读就是龙脉那一档。
        // 若把读数缓存下来（或存进存档），玩家会看到「界面说微型灵脉、实际练得快得离谱」
        SpiritSenseSystem sense = NewSystem(Spells, Cultivation(1), Farm("vein_dragon"));

        SpiritSenseReading? reading = sense.Read();

        Assert.NotNull(reading);
        Assert.Equal("龙脉", reading!.Vein.Name);
        Assert.Equal(6.00, reading.ConcentrationMultiplier, precision: 10);
    }

    [Fact]
    public void 跨大境界也算解锁_筑基了照样看得见()
    {
        // 门槛比较交给 ICultivationSystem.Reaches：跨大境界时后一个境界一律算达到（§8.1 的表序）。
        // 比如「筑基初期」比「炼气十三层」高——这条走的是与 §8.2 三条绑定同一个判定
        SpiritSenseSystem sense = NewSystem(Spells, Cultivation(1, realmId: "foundation_establishment"));

        Assert.True(sense.IsAvailable);
        Assert.NotNull(sense.Read());
    }

    // ── 替身表：解锁前后查得到的东西不一样 ──────────────────────────

    [Fact]
    public void 没解锁_什么都查不到_解锁之后才有读数()
    {
        // 把感知挪到 §8.2 的 4 层那一档（灵气浇灌的层数），于是同一个玩家的前后两态可以对比：
        // 三层的他什么都看不到，四层的他看得到——「这条法术买的正是看得见本身」。
        // 这也是「门槛真的在起作用」的证据：恒为真的是另一回事（缺省表那一层挡不住这种写法）
        var late = new StandInSpellTable(Sense, unlockStage: 4);

        SpiritSenseSystem locked = NewSystem(late, Cultivation(3));
        Assert.False(locked.IsAvailable);
        Assert.Null(locked.Read());

        SpiritSenseSystem unlocked = NewSystem(late, Cultivation(4));
        Assert.True(unlocked.IsAvailable);
        Assert.NotNull(unlocked.Read());
    }

    [Fact]
    public void 门槛来自法术表_改表就改解锁层数()
    {
        // 同一条替身表换成 1 层：三层的他立刻看得见。门槛写在表上、不是写死在代码里的层号——
        // 若谁把「1 层」硬编码进感知系统，上一条会绿、这一条会红
        var early = new StandInSpellTable(Sense, unlockStage: 1);

        Assert.True(NewSystem(early, Cultivation(1)).IsAvailable);
    }

    [Fact]
    public void 表里没有感知法术_恒为看不见_而不是崩()
    {
        // 一条感知法术都没有时，「看不见」是唯一说得通的结果——不该让游戏起不来。
        // 症状（感知界面一直上锁）直接指向那张表
        SpiritSenseSystem sense = NewSystem(new StandInSpellTable(Watering, unlockStage: 1), Cultivation(13));

        Assert.False(sense.IsAvailable);
        Assert.Null(sense.Read());
    }

    // ── 边界 ────────────────────────────────────────────────────────

    [Fact]
    public void 构造_依赖为null_当场抛()
    {
        var lifeSpells = new LifeSpellSystem(Spells, Cultivation(1), _farmland);

        Assert.Throws<ArgumentNullException>(() => new SpiritSenseSystem(null!, lifeSpells, Farm()));
        Assert.Throws<ArgumentNullException>(() => new SpiritSenseSystem(Spells, null!, Farm()));
        Assert.Throws<ArgumentNullException>(() => new SpiritSenseSystem(Spells, lifeSpells, null!));
    }

    [Fact]
    public void 感知不带状态_不进存档()
    {
        // 解锁与浓度都是现算的（前者看层数、后者看灵脉等级），两者都已经在各自的存档里。
        // 再存一份「看得见什么」就是同一个事实存三处——而灵脉一升级，那份就错了
        Assert.False(typeof(ISaveable).IsAssignableFrom(typeof(SpiritSenseSystem)));
    }

    /// <summary>
    /// 替身法术表：只有一条法术，效果与解锁层数由构造参数给。只为「门槛真的在起作用」那几条存在。
    /// </summary>
    private sealed class StandInSpellTable : ISpellTable
    {
        private readonly List<SpellDefinition> _spells;

        public StandInSpellTable(string spellId, int unlockStage)
        {
            // 感知类法术的消耗与范围在真表上是 0 与 1（它不是对着格子放的），替身照抄这两点
            bool sense = spellId == Sense;

            _spells = new List<SpellDefinition>
            {
                new(spellId, sense ? "灵气感知" : "灵气浇灌",
                    sense ? SpellEffect.Sense : SpellEffect.Water,
                    "qi_refining", unlockStage, SpiritCost: 0, AreaSize: 1),
            };
        }

        public IReadOnlyList<SpellDefinition> Spells => _spells;

        public SpellDefinition Get(string spellId) =>
            _spells.Single(spell => spell.Id == spellId);
    }
}
