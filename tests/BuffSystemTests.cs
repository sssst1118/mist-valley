using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using XingGame.Core.Save;
using XingGame.Core.Time;
using XingGame.Systems.Buffs;
using XingGame.Systems.Cultivation;
using XingGame.Systems.Player;

namespace XingGame.Tests;

/// <summary>
/// 限时增益（M3-6）：施加 / 查询总修正 / 到期清理 / 移除，以及**两个真实消费者**——
/// 修炼速度（打坐的第五项）与移速（<see cref="PlayerMotor"/>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>本切片对外的价值全在「跨模块」那几条上</b>：在 M3-6 之前，§8.3 的聚气散、§8.2 的轻身术
/// 都只是文档里的两行字。所以这里不单测「乘出来是不是 1.5」（那只证明函数自己算得对），
/// 而是断言：吃了聚气散之后**同一柱香的收获真的多了**（11 → 17 点），穿上轻身术之后
/// **同一个输入真的走得更快**（100 → 120 像素/秒）。乘法是不是真的接上了，只有这种断言说得清
/// （同 M3-5 的灵脉那样）。
/// </para>
/// <para>
/// 时刻一律用 <see cref="GameTime"/> 明写，**不读时钟**：增益的生效与否是传入时刻的纯函数，
/// 用例才能靠「同参同果」。数值用**真表**（聚气散 1.5 / 7 天、轻身术 1.2 / 1 小时都是文档直给），
/// 换表那一侧的账用替身（同 <c>SpiritLandTests</c> 的分工）。
/// </para>
/// </remarks>
public sealed class BuffSystemTests : IDisposable
{
    private const int Slot = 1;
    private const int WorldSeed = 20260912;

    /// <summary>浮点比较容差（同 <c>PlayerMotorTests</c>）：移速是 float，1.2 在二进制里不精确。</summary>
    private const float Tolerance = 1e-3f;

    private static readonly BuffTable Table = BuffTable.LoadDefault();
    private static readonly SpiritRootTable Roots = SpiritRootTable.LoadDefault();
    private static readonly RealmTable Realms = RealmTable.LoadDefault();
    private static readonly CultivationSpeedTable Speed = CultivationSpeedTable.LoadDefault(Realms);
    private static readonly SpiritPowerTable SpiritPower = SpiritPowerTable.LoadDefault();

    private readonly string _saveDirectory =
        Path.Combine(Path.GetTempPath(), "xing-buff-save-" + Guid.NewGuid().ToString("N"));

    /// <summary>SQLite 连接池可能还攥着文件句柄，清不掉临时目录不该让测试失败。</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_saveDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static BuffSystem NewSystem() => new(Table);

    /// <summary>§3.1 的清晨（6:00）——季节与时辰的倍率都在这里显形。</summary>
    private static GameTime Morning(int day = 1) => new(1, Season.Spring, day, GameTime.FirstHour, 0);

    /// <summary>
    /// 双灵根（§4.2 的 1.0x）、灵脉喂替身（×1.0，见 <c>NoSpiritVein</c>）：于是打坐的账只剩
    /// 「10 点/小时 × 春 1.10 × 增益」——增益那一项是不是真的乘进去了，一眼对得上出处。
    /// </summary>
    private static CultivationSystem NewCultivation(ICultivationSpeedBonus speedBonus, int stage = 8) =>
        new(Roots, Realms, Speed, SpiritPower, new NoSpiritVein(), speedBonus,
            "grade_true_dual", rootId: null, "qi_refining", stage);

    private static void AssertClose(float expected, float actual, string what) =>
        Assert.True(MathF.Abs(expected - actual) <= Tolerance, $"{what}：期望 {expected}，实际 {actual}");

    // ── 施加与查询 ──────────────────────────────────────────────────

    [Fact]
    public void 没施加任何增益_是乘法的单位元()
    {
        // 1.0 而不是 0：它是直接乘进各自合成器的数，所以「没有增益」与「有增益」在调用方看来
        // 是同一形状——调用方不必为它写分支
        BuffSystem buffs = NewSystem();

        Assert.Equal(1.0, buffs.MultiplierFor(BuffTarget.CultivationSpeed, Morning()), precision: 10);
        Assert.Equal(1.0, buffs.MultiplierFor(BuffTarget.MoveSpeed, Morning()), precision: 10);
        Assert.Equal(1.0, buffs.MultiplierAt(Morning()), precision: 10);
    }

    [Fact]
    public void 施加之后_同一属性查得到_别的属性不受影响()
    {
        BuffSystem buffs = NewSystem();

        buffs.Apply("buff_gather_qi_powder", Morning());

        Assert.Equal(1.5, buffs.MultiplierFor(BuffTarget.CultivationSpeed, Morning()), precision: 10);
        Assert.Equal(1.5, buffs.MultiplierAt(Morning()), precision: 10);

        // 移速那一侧一格都没动：增益串味会让「吃了聚气散跑得更快」这种怪事出现
        Assert.Equal(1.0, buffs.MultiplierFor(BuffTarget.MoveSpeed, Morning()), precision: 10);
    }

    [Fact]
    public void 轻身术_只改移速_不改修炼()
    {
        BuffSystem buffs = NewSystem();

        buffs.Apply("buff_light_body", Morning());

        Assert.Equal(1.2, buffs.MultiplierFor(BuffTarget.MoveSpeed, Morning()), precision: 10);
        Assert.Equal(1.0, buffs.MultiplierAt(Morning()), precision: 10);
    }

    [Fact]
    public void 同一属性两条不同增益_相乘不相加()
    {
        // §8.3 把每一行都列成一个**独立的影响因素**，而合成器那边本来就是连乘不相加
        // （见 CultivationSystem.SpeedMultiplierAt）。所以 1.5 撞上 1.2 是 1.8，不是 1.7——
        // 相加会随因素增多越来越偏离文档，而偏差只在两条同时生效时才显形
        BuffSystem buffs = NewSystem();

        buffs.Apply("buff_gather_qi_powder", Morning());
        buffs.Apply("buff_spirit_grass_soup", Morning());

        double product = buffs.MultiplierFor(BuffTarget.CultivationSpeed, Morning());

        Assert.Equal(1.8, product, precision: 9);
        Assert.NotEqual(1.7, product, precision: 9);   // 相加的那个数是 1.5 + 0.2
    }

    // ── 到期：绝对时刻、右端不含 ────────────────────────────────────

    [Fact]
    public void 到期_右端不含_到期那一刻起就不再生效()
    {
        // 聚气散「持续 7 天」：第 1 日 6:00 吃下 → 第 8 日 6:00 到期。左闭右开（同 §8.3 时辰带
        // 与各表区间的写法），于是「同一时刻施加与到期」有唯一答案
        BuffSystem buffs = NewSystem();
        buffs.Apply("buff_gather_qi_powder", Morning());

        Assert.Equal(1.5, buffs.MultiplierAt(new GameTime(1, Season.Spring, 7, 23, 59)), precision: 10);
        Assert.Equal(1.0, buffs.MultiplierAt(new GameTime(1, Season.Spring, 8, 6, 0)), precision: 10);
    }

    [Fact]
    public void 到期清理_只清到期的_返回清掉的条数()
    {
        BuffSystem buffs = NewSystem();
        buffs.Apply("buff_gather_qi_powder", Morning());

        Assert.Equal(0, buffs.Purge(new GameTime(1, Season.Spring, 7, 23, 59)));   // 还没到
        Assert.Equal(1.5, buffs.MultiplierAt(new GameTime(1, Season.Spring, 7, 23, 59)), precision: 10);

        Assert.Equal(1, buffs.Purge(new GameTime(1, Season.Spring, 8, 6, 0)));     // 到期那一刻起算过期
        Assert.Equal(0, buffs.Purge(new GameTime(1, Season.Spring, 8, 6, 0)));     // 再清一次没有了
        Assert.Equal(1.0, buffs.MultiplierAt(new GameTime(1, Season.Spring, 8, 6, 0)), precision: 10);
    }

    [Fact]
    public void 查询即清理_查过之后就没有可清的了()
    {
        // 到期的那一条必须消失，否则它会永远留在存档里（这个游戏没有逐分钟事件可以定时来清）。
        // 所以清理挂在查询上——这条用例盯的就是那个决定：查询之后再 Purge，应该已经无事可做
        BuffSystem buffs = NewSystem();
        buffs.Apply("buff_light_body", Morning());   // 一小时：7:00 到期

        GameTime later = new(1, Season.Spring, 1, 9, 0);
        Assert.Equal(1.0, buffs.MultiplierFor(BuffTarget.MoveSpeed, later), precision: 10);

        Assert.Equal(0, buffs.Purge(later));   // 还留着的话这里会返回 1
    }

    [Fact]
    public void 施加也顺手清一次()
    {
        // 任何一次交互都可能是一天里唯一碰这个系统的那个入口，所以施加那条路也清
        BuffSystem buffs = NewSystem();
        buffs.Apply("buff_light_body", Morning());   // 7:00 到期

        GameTime later = new(1, Season.Spring, 1, 9, 0);
        buffs.Apply("buff_gather_qi_powder", later);

        Assert.Equal(0, buffs.Purge(later));   // 那条过期的已经被施加清掉了
        Assert.Equal(1.5, buffs.MultiplierAt(later), precision: 10);
    }

    [Fact]
    public void 还没到生效的时候_既查不到也不被清()
    {
        // 「施加时刻」是从「到期时刻 − 表上的时长」算出来的，所以查询是 (增益, 时刻) 的纯函数：
        // 传一个比施加还早的时刻，它既不该算生效、也不该被当成过期清掉
        BuffSystem buffs = NewSystem();
        buffs.Apply("buff_gather_qi_powder", Morning(day: 3));

        Assert.Equal(1.0, buffs.MultiplierAt(Morning(day: 1)), precision: 10);
        Assert.Equal(0, buffs.Purge(Morning(day: 1)));
        Assert.Equal(1.5, buffs.MultiplierAt(Morning(day: 4)), precision: 10);
    }

    [Fact]
    public void 跨午夜_按分钟算不按天算()
    {
        // 轻身术 1 小时，23:30 放出去。GameTime 的日字段在 6:00 才翻页，所以午夜之后
        // （0:29）仍属于同一个游戏日、而 TotalMinutes 已经涨过了 23:59——按「还剩几分钟」或者
        // 按天推进的实现会在这里错
        BuffSystem buffs = NewSystem();
        buffs.Apply("buff_light_body", new GameTime(1, Season.Spring, 1, 23, 30));

        Assert.Equal(1.2, buffs.MultiplierFor(BuffTarget.MoveSpeed, new GameTime(1, Season.Spring, 1, 23, 59)), precision: 10);
        Assert.Equal(1.2, buffs.MultiplierFor(BuffTarget.MoveSpeed, new GameTime(1, Season.Spring, 1, 0, 29)), precision: 10);
        Assert.Equal(1.0, buffs.MultiplierFor(BuffTarget.MoveSpeed, new GameTime(1, Season.Spring, 1, 0, 30)), precision: 10);
    }

    [Fact]
    public void 跨日界_不拿DayStarted当日推进()
    {
        // 灵芽羹「持续 1 天」，22:00 吃下 → 次日 22:00 到期。日界在 6:00（DayStarted 就在那一刻发），
        // 所以「6:00 之后还生效、22:00 起才失效」这件事只有按绝对时刻算才成立。
        // 谁要是拿 DayStarted 当一天来推进（清掉或减一天），这条立刻红
        BuffSystem buffs = NewSystem();
        buffs.Apply("buff_spirit_grass_soup", new GameTime(1, Season.Spring, 1, 22, 0));

        Assert.Equal(1.2, buffs.MultiplierAt(new GameTime(1, Season.Spring, 2, 6, 29)), precision: 10);
        Assert.Equal(1.2, buffs.MultiplierAt(new GameTime(1, Season.Spring, 2, 21, 59)), precision: 10);
        Assert.Equal(1.0, buffs.MultiplierAt(new GameTime(1, Season.Spring, 2, 22, 0)), precision: 10);
    }

    [Fact]
    public void 跨季_七天就是七天()
    {
        // 春 26 日吃下 → 夏 5 日 6:00 到期（春季只有 28 天）。跨季那一格是 TotalMinutes
        // 最容易算错的地方（季名与日号都换了），而玩家的丹药不该因为换季而少几天
        BuffSystem buffs = NewSystem();
        buffs.Apply("buff_gather_qi_powder", new GameTime(1, Season.Spring, 26, 6, 0));

        Assert.Equal(1.5, buffs.MultiplierAt(new GameTime(1, Season.Summer, 4, 23, 59)), precision: 10);
        Assert.Equal(1.0, buffs.MultiplierAt(new GameTime(1, Season.Summer, 5, 6, 0)), precision: 10);
    }

    // ── 叠加与刷新（文档没写，本切片定的规矩） ──────────────────────

    [Fact]
    public void 同一个id再施加_刷新时长不叠加强度也不累加时长()
    {
        // 第 1 日吃一颗聚气散（第 8 日到期），第 5 日又吃一颗（第 12 日到期）：
        // ① 第 9 日仍生效 —— 第二次施加确实**重新起算**了（不然第一次的到期日早过了）；
        // ② 强度仍是 1.5 —— 不是 1.5 × 1.5 = 2.25（同 id 不叠加强度）；
        // ③ 第 12 日失效 —— 不是「旧到期 + 7 天」（那会累加到第 15 日，等于攒丹药换无限续期）
        BuffSystem buffs = NewSystem();
        buffs.Apply("buff_gather_qi_powder", Morning(day: 1));
        buffs.Apply("buff_gather_qi_powder", Morning(day: 5));

        double ninth = buffs.MultiplierAt(Morning(day: 9));
        Assert.Equal(1.5, ninth, precision: 10);
        Assert.NotEqual(2.25, ninth, precision: 10);

        Assert.Equal(1.5, buffs.MultiplierAt(new GameTime(1, Season.Spring, 11, 23, 59)), precision: 10);
        Assert.Equal(1.0, buffs.MultiplierAt(Morning(day: 12)), precision: 10);
    }

    [Fact]
    public void 移除_本来在就摘掉_本来不在也不抛()
    {
        BuffSystem buffs = NewSystem();
        buffs.Apply("buff_light_body", Morning());

        Assert.True(buffs.Remove("buff_light_body"));
        Assert.Equal(1.0, buffs.MultiplierFor(BuffTarget.MoveSpeed, Morning()), precision: 10);

        // 「身上本来就没有」是正常结果，不是编程错误（同 Inventory.Remove 对不存在物品的处理）
        Assert.False(buffs.Remove("buff_light_body"));
        Assert.False(buffs.Remove("buff_nobody"));
    }

    // ── 非法输入 ────────────────────────────────────────────────────

    [Fact]
    public void 非法输入_照惯例拒绝()
    {
        BuffSystem buffs = NewSystem();

        Assert.Throws<ArgumentNullException>(() => buffs.Apply(null!, Morning()));
        Assert.Throws<ArgumentNullException>(() => buffs.Remove(null!));

        // 认不出的 id：调用方给的是数据里的 id，写错就是写错，当场抛
        Assert.Throws<KeyNotFoundException>(() => buffs.Apply("buff_nobody", Morning()));
        Assert.Throws<KeyNotFoundException>(() => buffs.Apply("", Morning()));

        // 没接上的目标属性：悄悄当成「无修正」会让新加的那个属性永远是 1.0，而没有任何报错
        Assert.Throws<ArgumentOutOfRangeException>(() => buffs.MultiplierFor((BuffTarget)99, Morning()));

        Assert.Throws<ArgumentNullException>(() => new BuffSystem(null!));
    }

    // ── 换个替身表就换一套数 ────────────────────────────────────────

    [Fact]
    public void 数值在表上_换个替身表就换一套数()
    {
        // 「数值不在系统里」这件事只有行为验得出来——成员名里藏不藏数字看不出来
        // （同 M3-3 / M3-5 对各组数的做法）
        var plain = new PlainBuffTable(
            new BuffDefinition("buff_plain", "替身增益", BuffTarget.CultivationSpeed, Multiplier: 3.0, DurationMinutes: 30));
        var buffs = new BuffSystem(plain);

        buffs.Apply("buff_plain", Morning());

        Assert.Equal(3.0, buffs.MultiplierAt(Morning()), precision: 10);
        Assert.Equal(1.0, buffs.MultiplierAt(new GameTime(1, Season.Spring, 1, 6, 30)), precision: 10);   // 30 分钟是表上的
    }

    // ── 跨模块：两个真实消费者 ──────────────────────────────────────

    [Fact]
    public void 跨模块_吃了聚气散之后同一柱香的收获更多()
    {
        // §8.3 的「丹药」是打坐速度的第五项：这个断言就是那一行字真的接上了的证明。
        // 双灵根 1.0x × 春 1.10 × 灵脉替身 1.0 = 11 点/小时；再乘聚气散 1.5 = 16.5 → 17（半分进位）
        BuffSystem buffs = NewSystem();
        CultivationSystem cultivation = NewCultivation(buffs);

        Assert.Equal(11, cultivation.Meditate(Morning(), 60));

        buffs.Apply("buff_gather_qi_powder", Morning());
        Assert.Equal(17, cultivation.Meditate(Morning(), 60));

        // 再来一道灵芽羹（×1.2）：10 × 1.10 × 1.5 × 1.2 = 19.8 → 20。两条增益是**相乘**的
        buffs.Apply("buff_spirit_grass_soup", Morning());
        Assert.Equal(20, cultivation.Meditate(Morning(), 60));

        // 到期之后自己就回到 11：增益不会被「记」在打坐系统里
        GameTime afterExpiry = new(1, Season.Spring, 9, 6, 0);
        Assert.Equal(1.0, buffs.MultiplierAt(afterExpiry), precision: 10);
        Assert.Equal(11, cultivation.Meditate(afterExpiry, 60));
    }

    [Fact]
    public void 跨模块_轻身术让同一份输入走得更快()
    {
        // §8.2 炼气 7-9 层的「轻身术（移动速度 +20%）」：桥接层每帧把修正问出来喂给马达
        // （world/Player.cs），这里走的就是那条路——问出来的数真的能让同一个输入更快
        BuffSystem buffs = NewSystem();
        var motor = new PlayerMotor(PlayerConfig.DefaultMoveSpeed);

        motor.Step(new Vector2(1f, 0f), (float)buffs.MultiplierFor(BuffTarget.MoveSpeed, Morning()));
        AssertClose(100f, motor.Velocity.X, "没有增益时的速度");

        buffs.Apply("buff_light_body", Morning());
        motor.Step(new Vector2(1f, 0f), (float)buffs.MultiplierFor(BuffTarget.MoveSpeed, Morning()));
        AssertClose(120f, motor.Velocity.X, "轻身术生效时的速度");

        // 一小时后到期：桥上每帧现问，所以自己就慢回来了（缓存下来会白快一整段路）
        motor.Step(new Vector2(1f, 0f), (float)buffs.MultiplierFor(BuffTarget.MoveSpeed, new GameTime(1, Season.Spring, 1, 7, 0)));
        AssertClose(100f, motor.Velocity.X, "到期之后的速度");

        // 而它一点都不影响打坐：那条账走的是另一个目标属性
        Assert.Equal(1.0, buffs.MultiplierAt(Morning()), precision: 10);
    }

    [Fact]
    public void 跨模块_到期之后打坐与移速各自回到原样()
    {
        // 「生效时更快」只说了一半：到期后两边都要**自己**回到原样（增益是限时的，不是永久的）
        BuffSystem buffs = NewSystem();
        CultivationSystem cultivation = NewCultivation(buffs);
        var motor = new PlayerMotor(PlayerConfig.DefaultMoveSpeed);
        GameTime expiry = new(1, Season.Spring, 8, 6, 0);   // 聚气散第 8 日 6:00 到期

        buffs.Apply("buff_gather_qi_powder", Morning());
        buffs.Apply("buff_light_body", Morning());
        Assert.Equal(17, cultivation.Meditate(Morning(), 60));

        Assert.Equal(1.0, buffs.MultiplierAt(expiry), precision: 10);
        Assert.Equal(11, cultivation.Meditate(expiry, 60));

        motor.Step(new Vector2(1f, 0f), (float)buffs.MultiplierFor(BuffTarget.MoveSpeed, expiry));
        AssertClose(100f, motor.Velocity.X, "到期之后的移速");
    }

    // ── 存档：往返、旧档 ────────────────────────────────────────────

    [Fact]
    public void 存档往返_到期时刻原样带回来()
    {
        // 存 → 读：读档方是另一个实例，证明结果来自存档。**到期时刻原样**是关键——
        // 存「还剩几天」再在读档时重新起算，会让「存档放了几天」变成白送的天数
        var saves = new SqliteSaveService(_saveDirectory);
        BuffSystem original = NewSystem();
        original.Apply("buff_gather_qi_powder", Morning());
        original.Apply("buff_light_body", Morning());

        saves.Save(Slot, new SaveMeta(WorldSeed, "新农场", "第 1 年 春 1 日 6:00"),
            new ISaveable[] { original });

        BuffSystem restored = NewSystem();
        Assert.True(saves.Load(Slot, new ISaveable[] { restored }));

        Assert.Equal(1.5, restored.MultiplierFor(BuffTarget.CultivationSpeed, Morning(day: 4)), precision: 10);
        Assert.Equal(1.0, restored.MultiplierFor(BuffTarget.CultivationSpeed, Morning(day: 8)), precision: 10);

        // 轻身术那一小时早就过了：读回来的是「过期了」，不是「重新起算一小时」
        Assert.Equal(1.0, restored.MultiplierFor(BuffTarget.MoveSpeed, Morning(day: 4)), precision: 10);

        // 读档之后照常能接着用（不是「读进来了但坏了」）
        restored.Apply("buff_light_body", Morning(day: 4));
        Assert.Equal(1.2, restored.MultiplierFor(BuffTarget.MoveSpeed, Morning(day: 4)), precision: 10);
    }

    [Fact]
    public void 旧档_没有buffs键_读档照常且不覆盖构造时的状态()
    {
        // M2 到 M3-5 的档里没有 buffs 这个键（那时还没有任何限时增益）。SqliteSaveService 对
        // 缺席的键是「跳过、让它保持自己的初始状态」——而那条路径正是「没吃过任何增益」，
        // 所以旧档不需要任何迁移分支（同 M3-5 灵脉那个键的处境）。
        // 这里写一份 M3-5 形态的档（有 cultivation、没有 buffs）来代表那几版
        var saves = new SqliteSaveService(_saveDirectory);
        CultivationSystem cultivation = NewCultivation(new NoSpeedBonus());

        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 3 日 16:00"),
            new ISaveable[] { cultivation });

        BuffSystem restored = NewSystem();
        Assert.True(saves.Load(Slot, new ISaveable[] { restored }));

        Assert.Equal(1.0, restored.MultiplierAt(Morning()), precision: 10);
        Assert.Equal(0, restored.Purge(Morning()));

        // 读档之后照常能修炼、也照常能拿到增益
        restored.Apply("buff_gather_qi_powder", Morning());
        Assert.Equal(1.5, restored.MultiplierAt(Morning()), precision: 10);
    }

    [Fact]
    public void 读旧档再存回去_buffs键写出来了()
    {
        // 迁移只做一半的常见形态：读旧档没问题，但保存时又把旧的形态写回去。
        // 这里读一份没有 buffs 的档、施加一条增益再存一遍，落盘的必须是新键
        var saves = new SqliteSaveService(_saveDirectory);
        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 3 日 16:00"),
            new ISaveable[] { NewCultivation(new NoSpeedBonus()) });

        BuffSystem migrated = NewSystem();
        Assert.True(saves.Load(Slot, new ISaveable[] { migrated }));
        migrated.Apply("buff_gather_qi_powder", Morning());
        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 4 日 16:00"), new ISaveable[] { migrated });

        Assert.Equal(1, migrated.Version);

        BuffSystem reloaded = NewSystem();
        Assert.True(saves.Load(Slot, new ISaveable[] { reloaded }));

        Assert.Equal(1.5, reloaded.MultiplierAt(Morning()), precision: 10);
        Assert.Equal(1.5, reloaded.MultiplierAt(Morning(day: 7)), precision: 10);   // 到期时刻仍是第 8 日
    }

    // ── 坏档 ────────────────────────────────────────────────────────

    [Fact]
    public void 坏档_版本过高_抛()
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009）
        Assert.Throws<NotSupportedException>(() => NewSystem().Deserialize("{}", fromVersion: 2));
    }

    [Fact]
    public void 坏档_内容不是合法JSON_抛JsonException()
    {
        // 照 TimeServiceSaveTests / CraftingSystemTests：畸形 JSON 是 JsonException，
        // 「形状不对但合法」才是 InvalidDataException
        Assert.Throws<System.Text.Json.JsonException>(() => NewSystem().Deserialize("这不是 JSON", fromVersion: 1));
    }

    public static TheoryData<string> BadSaves()
    {
        var cases = new TheoryData<string>();

        cases.Add("null");                                            // 内容为空
        cases.Add("{}");                                              // 缺 active 字段
        cases.Add("""{ "Active": null }""");                           // 字段在但是空的
        cases.Add("""{ "Active": [ { "ExpiresAtMinute": 60 } ] }""");  // 有到期时刻没有 id
        cases.Add("""{ "Active": [ { "BuffId": "  ", "ExpiresAtMinute": 60 } ] }""");          // 空 id
        cases.Add("""{ "Active": [ { "BuffId": "buff_light_body" } ] }""");                    // 缺到期时刻
        cases.Add("""{ "Active": [ { "BuffId": "buff_light_body", "ExpiresAtMinute": 0 } ] }""");      // 到期时刻是 0
        cases.Add("""{ "Active": [ { "BuffId": "buff_light_body", "ExpiresAtMinute": -60 } ] }""");    // 到期时刻是负数
        cases.Add("""{ "Active": [ { "BuffId": "buff_nobody", "ExpiresAtMinute": 60 } ] }""");         // 表里没有这个 id
        cases.Add("""{ "Active": [ { "BuffId": "buff_light_body", "ExpiresAtMinute": 60 }, { "BuffId": "buff_light_body", "ExpiresAtMinute": 120 } ] }""");

        return cases;
    }

    [Theory]
    [MemberData(nameof(BadSaves))]
    public void 坏档_形状不对_抛InvalidDataException(string json)
    {
        // 每一条都只坏在一处：读错数据比读不出来更糟，所以宁可当场抛（ADR-009）。
        // 最后一条是「同一个 id 两条」——同 id 再施加是刷新，所以两份并存自相矛盾，
        // 而哪一份生效会取决于文件顺序
        Assert.Throws<InvalidDataException>(() => NewSystem().Deserialize(json, fromVersion: 1));
    }

    [Fact]
    public void 坏档_已到期的条目是正常的_不是坏档()
    {
        // 存档躺在磁盘上的那段时间游戏时间也在走，所以「存的时候还在、读的时候已经过期」是
        // 正常结果。它读进来不参与连乘，并在第一次查询或施加时被清掉
        BuffSystem restored = NewSystem();

        restored.Deserialize(
            """{ "Active": [ { "BuffId": "buff_light_body", "ExpiresAtMinute": 30 } ] }""", fromVersion: 1);

        Assert.Equal(1.0, restored.MultiplierFor(BuffTarget.MoveSpeed, Morning(day: 2)), precision: 10);
    }

    // ── 替身表 ──────────────────────────────────────────────────────

    /// <summary>
    /// 手写的替身（不引第三方依赖）：只答它被构造时收到的那几条，用来验「数值在表上」。
    /// </summary>
    private sealed class PlainBuffTable : IBuffTable
    {
        private readonly Dictionary<string, BuffDefinition> _byId = new(StringComparer.Ordinal);

        public PlainBuffTable(params BuffDefinition[] buffs)
        {
            foreach (BuffDefinition buff in buffs) _byId.Add(buff.Id, buff);
        }

        public IReadOnlyList<BuffDefinition> Buffs => new List<BuffDefinition>(_byId.Values);

        public BuffDefinition Get(string buffId) => _byId.TryGetValue(buffId, out BuffDefinition? buff)
            ? buff
            : throw new KeyNotFoundException($"替身表里没有「{buffId}」");

        public bool TryGet(string buffId, out BuffDefinition buff)
        {
            if (buffId is not null && _byId.TryGetValue(buffId, out BuffDefinition? found))
            {
                buff = found;
                return true;
            }

            buff = null!;
            return false;
        }
    }
}
