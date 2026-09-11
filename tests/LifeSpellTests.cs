using System;
using System.Collections.Generic;
using XingGame.Core.Events;
using XingGame.Core.Save;
using XingGame.Core.Time;
using XingGame.Systems.Cultivation;
using XingGame.Systems.Farming;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 生活法术的规则（M3-4）：解锁看层数、消耗走灵力池、作用落在耕地上。
/// </summary>
/// <remarks>
/// <para>
/// 本切片的价值全在「法术真的动了地」这件事上，所以耕地用**真** <see cref="Farmland"/>、
/// 对接那两条还拉起**真** <see cref="FarmingSystem"/>（不是替身）——替身会让「法术浇的水算不算浇过」
/// 这类问题在用例里自动成立，而那正是要验的东西。
/// </para>
/// <para>
/// 灵力池、境界表、法术表都用真的：消耗与解锁是**差一位就破坏玩法**的数（4 层还是 3 层能浇、
/// 一次花 5 还是 50），用替身算出来的边界不能说明缺省数据对不对。
/// </para>
/// </remarks>
public class LifeSpellTests
{
    private const string Sense = "spell_spirit_sense";
    private const string Watering = "spell_spirit_watering";
    private const string Rain = "spell_spirit_rain";
    private const string Hoe = "spell_spirit_hoe";

    private const string SeedParsnip = "seed_parsnip";

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
    private static readonly ItemTable Items = ItemTable.FromJson(ItemsJson);
    private static readonly CropTable Crops = CropTable.FromJson(CropsJson, Items);

    /// <summary>目标格。桥接层按 M1-5 的规矩算出来的那一格（朝向相邻的那一格，不是脚下）。</summary>
    private static readonly TileCoord Center = new(10, 10);

    private readonly Farmland _farmland = new(Crops);

    private static GameTime At(int day) => new(1, Season.Spring, day, GameTime.FirstHour, 0);

    /// <summary>伪灵根（0.3x）、炼气期、指到第几层——层号是这几条用例唯一的自变量。</summary>
    private static CultivationSystem Cultivation(int stage) =>
        new(Roots, Realms, Speed, SpiritPower, "grade_false", rootId: null, "qi_refining", stage);

    private LifeSpellSystem NewSystem(CultivationSystem cultivation) =>
        new(Spells, cultivation, _farmland);

    /// <summary>把灵力花到只剩 <paramref name="leave"/> 点，好测「差一点」那条边界。</summary>
    private static void Drain(CultivationSystem cultivation, int leave) =>
        cultivation.TrySpendSpirit(cultivation.Spirit - leave);

    // ── 解锁：差一层 / 刚好 / 远超 ────────────────────────────────────

    [Fact]
    public void 灵气感知_一层就解锁_它是查询不是施法()
    {
        CultivationSystem cultivation = Cultivation(1);
        LifeSpellSystem spells = NewSystem(cultivation);

        // §8.2：1-3 层「可感知灵气但无法施法」——所以 1 层就有它，而它本来就不花灵力
        Assert.True(spells.IsUnlocked(Sense));

        // 它解锁的是一个界面（看灵田的灵气浓度），不是对着某一格放的东西。硬要一个落点就是编出来的：
        // 当场抛，而不是返回一个「成功了但什么都没发生」的 true——那会让桥接层以为该放特效
        Assert.Throws<NotSupportedException>(() => spells.TryCastAt(Sense, Center));

        Assert.Equal(cultivation.MaxSpirit, cultivation.Spirit);
    }

    [Fact]
    public void 灵气浇灌_三层不能放_四层能_再往上也能()
    {
        // §8.2 的 4-6 层那一档 ⇒ 解锁层是它的下沿 4（差一层与刚好各一条）
        Assert.False(NewSystem(Cultivation(3)).IsUnlocked(Watering));
        Assert.True(NewSystem(Cultivation(4)).IsUnlocked(Watering));
        Assert.True(NewSystem(Cultivation(5)).IsUnlocked(Watering));
        Assert.True(NewSystem(Cultivation(13)).IsUnlocked(Watering));
    }

    [Fact]
    public void 灵雨术与灵锄术_九层不能放_十层能_再往上也能()
    {
        // §8.2 的 10-12 层那一档 ⇒ 10
        foreach (string spellId in new[] { Rain, Hoe })
        {
            Assert.False(NewSystem(Cultivation(9)).IsUnlocked(spellId));
            Assert.True(NewSystem(Cultivation(10)).IsUnlocked(spellId));
            Assert.True(NewSystem(Cultivation(11)).IsUnlocked(spellId));
            Assert.True(NewSystem(Cultivation(13)).IsUnlocked(spellId));
        }
    }

    [Fact]
    public void 解锁判据落在层上_替身表写几层就几层解锁()
    {
        // 表里存的是「第几层」而不是「第几档」：换一条 2 层解锁的法术进去，1 层的玩家就放不出。
        // 这条同时是「解锁层数不在代码里」的行为性证明——系统没有替谁决定过几层
        var standIn = new FakeSpellTable(
            new SpellDefinition("spell_probe", "试灵术", SpellEffect.Till, "qi_refining", 2, 1, 1));

        var one = new LifeSpellSystem(standIn, Cultivation(1), new Farmland(Crops));
        var two = new LifeSpellSystem(standIn, Cultivation(2), new Farmland(Crops));

        Assert.False(one.IsUnlocked("spell_probe"));
        Assert.True(two.IsUnlocked("spell_probe"));
    }

    [Fact]
    public void 未解锁时_不扣灵力也不动地()
    {
        (int Stage, string SpellId)[] locked = { (3, Watering), (9, Rain), (9, Hoe) };

        foreach ((int stage, string spellId) in locked)
        {
            var farmland = new Farmland(Crops);
            CultivationSystem cultivation = Cultivation(stage);
            var spells = new LifeSpellSystem(Spells, cultivation, farmland);
            farmland.TryTill(Center);

            Assert.False(spells.TryCastAt(spellId, Center));

            Assert.Equal(cultivation.MaxSpirit, cultivation.Spirit);
            Assert.Equal(SoilState.Tilled, farmland.StateOf(Center));
        }
    }

    // ── 灵气浇灌（单格） ─────────────────────────────────────────────

    [Fact]
    public void 灵气浇灌_把目标格浇上水_邻格不动()
    {
        CultivationSystem cultivation = Cultivation(4);   // 上限 175
        LifeSpellSystem spells = NewSystem(cultivation);
        TileCoord neighbor = new(Center.X + 1, Center.Y);
        _farmland.TryTill(Center);
        _farmland.TryTill(neighbor);

        Assert.True(spells.TryCastAt(Watering, Center));

        Assert.Equal(SoilState.Watered, _farmland.StateOf(Center));
        Assert.Equal(SoilState.Tilled, _farmland.StateOf(neighbor));   // 单格法术不会顺手浇旁边
        Assert.Equal(175 - 5, cultivation.Spirit);                     // 备案 #75：5 灵力/格
    }

    [Fact]
    public void 灵气浇灌_未开垦的格浇不了_也不扣灵力()
    {
        // 与洒水壶的语义对齐：水浇在荒地上会流走（Farmland.TryWater 早就这么定的）。
        // 法术既然「代替浇水」，就不该在浇不动的时候还收钱
        CultivationSystem cultivation = Cultivation(4);
        LifeSpellSystem spells = NewSystem(cultivation);

        Assert.False(spells.TryCastAt(Watering, Center));

        Assert.Equal(SoilState.Untilled, _farmland.StateOf(Center));
        Assert.Equal(175, cultivation.Spirit);
    }

    [Fact]
    public void 灵气浇灌_已经浇过的格再浇一次_不扣灵力也不改状态()
    {
        CultivationSystem cultivation = Cultivation(4);
        LifeSpellSystem spells = NewSystem(cultivation);
        _farmland.TryTill(Center);

        Assert.True(spells.TryCastAt(Watering, Center));
        Assert.False(spells.TryCastAt(Watering, Center));   // 第二发没有可做的事

        Assert.Equal(175 - 5, cultivation.Spirit);   // 只扣过一次
        Assert.Equal(SoilState.Watered, _farmland.StateOf(Center));
    }

    [Fact]
    public void 灵气浇灌_灵力差一点时一格都不动()
    {
        // **这是最容易写错的地方**：先扣再查会留下一个被改小的池子，先浇再扣会白送一次浇水
        // （地块已经湿了、灵力没少）。两种都是静默错——玩家只会觉得法术时灵时不灵
        CultivationSystem cultivation = Cultivation(4);
        Drain(cultivation, leave: 4);   // 差一点
        LifeSpellSystem spells = NewSystem(cultivation);
        _farmland.TryTill(Center);

        Assert.False(spells.TryCastAt(Watering, Center));

        Assert.Equal(4, cultivation.Spirit);                       // 一分没扣
        Assert.Equal(SoilState.Tilled, _farmland.StateOf(Center));  // 一格没动
    }

    [Fact]
    public void 灵气浇灌_刚好够()
    {
        CultivationSystem cultivation = Cultivation(4);
        Drain(cultivation, leave: 5);
        LifeSpellSystem spells = NewSystem(cultivation);
        _farmland.TryTill(Center);

        Assert.True(spells.TryCastAt(Watering, Center));

        Assert.Equal(0, cultivation.Spirit);
        Assert.Equal(SoilState.Watered, _farmland.StateOf(Center));
    }

    // ── 灵雨术（3×3 浇水） ───────────────────────────────────────────

    [Fact]
    public void 灵雨术_只浇范围里开垦过的格_荒地原样不动()
    {
        CultivationSystem cultivation = Cultivation(10);   // 上限 325
        LifeSpellSystem spells = NewSystem(cultivation);

        // 九格里只在其中五格上开过荒
        var tilled = new[]
        {
            new TileCoord(9, 9), new TileCoord(10, 9), new TileCoord(11, 9),
            new TileCoord(9, 10), Center,
        };
        foreach (TileCoord tile in tilled) _farmland.TryTill(tile);

        Assert.True(spells.TryCastAt(Rain, Center));

        foreach (TileCoord tile in tilled) Assert.Equal(SoilState.Watered, _farmland.StateOf(tile));
        Assert.Equal(SoilState.Untilled, _farmland.StateOf(new TileCoord(11, 10)));
        Assert.Equal(SoilState.Untilled, _farmland.StateOf(new TileCoord(9, 11)));
        Assert.Equal(SoilState.Untilled, _farmland.StateOf(new TileCoord(10, 11)));
        Assert.Equal(SoilState.Untilled, _farmland.StateOf(new TileCoord(11, 11)));

        Assert.Equal(325 - 50, cultivation.Spirit);   // 备案 #75：一次施放 50，与浇到几格无关
    }

    [Fact]
    public void 灵雨术_范围里一格都动不了_不扣灵力()
    {
        // 一片荒地：放了等于没放，所以不返回成功也不收钱（否则就成了「按了键就掉灵力」的隐形税）
        CultivationSystem cultivation = Cultivation(10);
        LifeSpellSystem spells = NewSystem(cultivation);

        Assert.False(spells.TryCastAt(Rain, Center));

        Assert.Equal(325, cultivation.Spirit);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Assert.Equal(SoilState.Untilled, _farmland.StateOf(new TileCoord(Center.X + dx, Center.Y + dy)));
            }
        }
    }

    [Fact]
    public void 灵雨术_灵力差一点时九格一格都不动()
    {
        CultivationSystem cultivation = Cultivation(10);
        LifeSpellSystem spells = NewSystem(cultivation);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++) _farmland.TryTill(new TileCoord(Center.X + dx, Center.Y + dy));
        }

        Drain(cultivation, leave: 49);   // 差一点

        Assert.False(spells.TryCastAt(Rain, Center));

        Assert.Equal(49, cultivation.Spirit);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Assert.Equal(SoilState.Tilled, _farmland.StateOf(new TileCoord(Center.X + dx, Center.Y + dy)));
            }
        }
    }

    [Fact]
    public void 灵雨术_九格全浇过之后再放_不再扣灵力()
    {
        CultivationSystem cultivation = Cultivation(10);
        LifeSpellSystem spells = NewSystem(cultivation);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++) _farmland.TryTill(new TileCoord(Center.X + dx, Center.Y + dy));
        }

        Assert.True(spells.TryCastAt(Rain, Center));
        Assert.False(spells.TryCastAt(Rain, Center));

        Assert.Equal(325 - 50, cultivation.Spirit);
    }

    [Fact]
    public void 灵雨术_负坐标的中心也算得对()
    {
        // 耕地是稀疏字典，没有「地图边界」：负格一样能开荒、一样在 3×3 里
        TileCoord center = new(-3, -4);
        TileCoord corner = new(-4, -5);
        CultivationSystem cultivation = Cultivation(10);
        LifeSpellSystem spells = NewSystem(cultivation);
        _farmland.TryTill(center);
        _farmland.TryTill(corner);

        Assert.True(spells.TryCastAt(Rain, center));

        Assert.Equal(SoilState.Watered, _farmland.StateOf(center));
        Assert.Equal(SoilState.Watered, _farmland.StateOf(corner));   // 负方向的角也在 3×3 里
        Assert.Equal(SoilState.Untilled, _farmland.StateOf(new TileCoord(-5, -6)));   // 再远一格就不在了
    }

    // ── 灵锄术（3×3 开垦） ───────────────────────────────────────────

    [Fact]
    public void 灵锄术_九格荒地一次全开垦_再放一次没有可动的地()
    {
        CultivationSystem cultivation = Cultivation(10);
        LifeSpellSystem spells = NewSystem(cultivation);

        Assert.True(spells.TryCastAt(Hoe, Center));

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Assert.Equal(SoilState.Tilled, _farmland.StateOf(new TileCoord(Center.X + dx, Center.Y + dy)));
            }
        }

        // 范围外的一格不许被碰到：3×3 就是 3×3（多一格就等于悄悄改大了备案 #75 的范围）
        Assert.Equal(SoilState.Untilled, _farmland.StateOf(new TileCoord(Center.X + 2, Center.Y)));
        Assert.Equal(SoilState.Untilled, _farmland.StateOf(new TileCoord(Center.X, Center.Y - 2)));

        Assert.Equal(325 - 50, cultivation.Spirit);

        // 全都开垦过了：这一发没有任何可做的事
        Assert.False(spells.TryCastAt(Hoe, Center));
        Assert.Equal(325 - 50, cultivation.Spirit);
    }

    // ── 与种植系统的真实对接 ─────────────────────────────────────────

    [Fact]
    public void 对接_灵锄术开垦出来的九格真的能播种()
    {
        var farmland = new Farmland(Crops);
        var inventory = new Inventory(Items, slotCount: 4);
        inventory.Add(SeedParsnip, 3);

        // 真 FarmingSystem：法术开出来的地与锄头开出来的地在它眼里没有区别，所以这里必须验到播种
        using var farming = new FarmingSystem(new EventBus(), new FakeTimeService(), Crops, farmland, inventory);
        var spells = new LifeSpellSystem(Spells, Cultivation(10), farmland);

        Assert.True(spells.TryCastAt(Hoe, Center));

        Assert.True(farming.TryPlant(new TileCoord(Center.X - 1, Center.Y - 1), SeedParsnip));
        Assert.True(farming.TryPlant(Center, SeedParsnip));
        Assert.True(farming.TryPlant(new TileCoord(Center.X + 1, Center.Y + 1), SeedParsnip));

        Assert.Equal(0, inventory.Count(SeedParsnip));   // 种子真的被扣了，不是「看起来种上了」
    }

    [Fact]
    public void 对接_灵气浇灌的水与洒水壶的水_在跨日时是同一件事()
    {
        var farmland = new Farmland(Crops);
        var inventory = new Inventory(Items, slotCount: 4);
        inventory.Add(SeedParsnip, 1);
        var bus = new EventBus();
        var time = new FakeTimeService { Weather = Weather.Sunny };
        using var farming = new FarmingSystem(bus, time, Crops, farmland, inventory);
        var spells = new LifeSpellSystem(Spells, Cultivation(4), farmland);

        farmland.TryTill(Center);
        Assert.True(farming.TryPlant(Center, SeedParsnip));
        Assert.True(spells.TryCastAt(Watering, Center));

        // 跨日（不下雨）：浇过水的作物长一天、浇水状态清空——与洒水壶浇出来的一模一样
        bus.Publish(new DayStarted(At(2)));

        Assert.Equal(1, farmland.DaysGrown(Center));
        Assert.Equal(SoilState.Tilled, farmland.StateOf(Center));
    }

    // ── 数值不在代码里 / 不进存档 ────────────────────────────────────

    [Fact]
    public void 换一条替身表就换一套数_消耗与范围都跟着表走()
    {
        // 行为性的证明：系统里没有藏数值。替身表写 7 灵力、1 层解锁、3×3，同一个玩家状态下的
        // 结果就跟着变——缺省表里 3×3 的法术要 10 层、要 50 灵力
        var standIn = new FakeSpellTable(
            new SpellDefinition("spell_probe", "试灵术", SpellEffect.Till, "qi_refining", 1, 7, 3));

        CultivationSystem cultivation = Cultivation(1);   // 上限 100
        var farmland = new Farmland(Crops);
        var spells = new LifeSpellSystem(standIn, cultivation, farmland);

        Assert.True(spells.TryCastAt("spell_probe", Center));

        Assert.Equal(100 - 7, cultivation.Spirit);

        int tilled = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (farmland.StateOf(new TileCoord(Center.X + dx, Center.Y + dy)) == SoilState.Tilled) tilled++;
            }
        }

        Assert.Equal(9, tilled);
    }

    [Fact]
    public void 法术系统不进存档_学会了什么是从层数推出来的_刻意的()
    {
        // 「学会了哪些法术」派生自境界层数（IsUnlocked 现算），灵力也已经在 cultivation 那份档里。
        // 再存一份「学会了的法术列表」就是同一个事实存两处——境界表或解锁层数一改，两份就会对不上，
        // 而症状是「都四层了还浇不了地」这种玩家没法自己解决的问题。否定式决定没有行为可测，
        // 所以用反射钉：哪天有人给它加上 ISaveable，这条就红
        Assert.False(typeof(ISaveable).IsAssignableFrom(typeof(LifeSpellSystem)));

        // 负向对照：判据本身有效（灵力池就是进存档的，它带着 ISaveable）
        Assert.True(typeof(ISaveable).IsAssignableFrom(typeof(CultivationSystem)));
    }

    [Fact]
    public void 升层之后自然解锁_不需要谁去记一笔()
    {
        CultivationSystem cultivation = Cultivation(3);
        LifeSpellSystem spells = NewSystem(cultivation);
        _farmland.TryTill(Center);

        Assert.False(spells.IsUnlocked(Watering));

        // 打坐 10 小时：伪灵根 0.3x × 春季 1.10 = 3.3 修为/小时 → 33 点，跨过第 3 层的 30 点开销
        cultivation.Meditate(At(1), minutes: 600);
        Assert.Equal(4, cultivation.Stage);

        // 没有任何人「记录」过他学会了灵气浇灌——层数一上去解锁当场生效
        Assert.True(spells.IsUnlocked(Watering));
        Assert.True(spells.TryCastAt(Watering, Center));
        Assert.Equal(SoilState.Watered, _farmland.StateOf(Center));
    }

    [Fact]
    public void 未知法术_id_抛_KeyNotFoundException()
    {
        LifeSpellSystem spells = NewSystem(Cultivation(1));

        Assert.Throws<KeyNotFoundException>(() => spells.IsUnlocked("spell_nope"));
        Assert.Throws<KeyNotFoundException>(() => spells.TryCastAt("spell_nope", Center));
    }

    /// <summary>手写的替身表（不引 Moq）：证明系统读的是表，不是自己藏的常数。</summary>
    private sealed class FakeSpellTable : ISpellTable
    {
        private readonly List<SpellDefinition> _spells;

        public FakeSpellTable(params SpellDefinition[] spells) => _spells = new List<SpellDefinition>(spells);

        public IReadOnlyList<SpellDefinition> Spells => _spells;

        public SpellDefinition Get(string spellId)
        {
            foreach (SpellDefinition spell in _spells)
            {
                if (spell.Id == spellId) return spell;
            }

            throw new KeyNotFoundException($"法术表里没有 id 为「{spellId}」的法术");
        }
    }

    /// <summary>同 <c>FarmingSystemTests</c> 的替身：本切片不验时间系统，只要一个能报天气的钟。</summary>
    private sealed class FakeTimeService : ITimeService
    {
        public GameTime Now { get; set; } = new(1, Season.Spring, 1, GameTime.FirstHour, 0);

        public Weather Weather { get; set; } = Weather.Sunny;

        public bool IsPaused { get; set; }

        public void Advance(int gameMinutes) => throw new NotSupportedException("替身不推进时间");

        public void Sleep() => throw new NotSupportedException("替身不推进时间");
    }
}
