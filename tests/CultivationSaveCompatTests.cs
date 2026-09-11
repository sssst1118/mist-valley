using System;
using System.Collections.Generic;
using System.IO;
using XingGame.Core.Save;
using XingGame.Core.Time;
using XingGame.Systems.Cultivation;

namespace XingGame.Tests;

/// <summary>
/// 修仙存档的**向后兼容**（M3-2）：这一轮给 blob 加了「修为」一列，所以真正要证明的是
/// 「老存档读得回来」。SQLite 那部分跑在真文件上而非替身——兼容的错大多出在
/// 「存下去的和读回来的不是一回事」，而替身恰好会把这类错抹平（同 <c>TimeServiceSaveTests</c> 的做法）。
/// </summary>
/// <remarks>
/// <para>
/// 三种档一次验完，它们分别对应三种真实经历：
/// <list type="number">
///   <item><b>M2 的档</b>：那个世界里还没有修仙系统，blob 表里连 <c>cultivation</c> 这个键都没有。
///         读档时要照常读、不炸——玩家没做过的事不该被凭空补上。</item>
///   <item><b>M3-1 的档</b>：有灵根与境界、没有修为字段（那一版确实没有攒修为的入口）。
///         修为缺席读成 0——这是**迁移决定**，理由写在 <c>CultivationSystem.ReadCultivation</c> 上。</item>
///   <item><b>M3-2 的档</b>：修为与层数一起跨进程不变。</item>
/// </list>
/// </para>
/// <para>
/// 写老格式的档要用一个「替身可存档件」（<see cref="RawBlob"/>）：只有它能按指定的键、版本与
/// 原始 JSON 写进 blob 表——用真的系统写出来的永远是新格式，那种「兼容」是假兼容。
/// </para>
/// </remarks>
public sealed class CultivationSaveCompatTests : IDisposable
{
    private const int Slot = 1;
    private const int WorldSeed = 20260912;

    private static readonly GameTime SpringMorning = new(1, Season.Spring, 1, GameTime.FirstHour, 0);

    private static readonly SpiritRootTable Roots = SpiritRootTable.LoadDefault();
    private static readonly RealmTable Realms = RealmTable.LoadDefault();
    private static readonly CultivationSpeedTable Speed = CultivationSpeedTable.LoadDefault(Realms);

    private readonly string _saveDirectory =
        Path.Combine(Path.GetTempPath(), "xing-cultivation-save-" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void M2旧档_完全没有修仙_blob_读档照常且保留构造时的新档状态()
    {
        // M2 的存档里没有 cultivation 这个键（那时还没有修仙系统）。SqliteSaveService 对
        // 缺席的键是「跳过、让它保持自己的初始状态」，所以这里要验的是：读档整体成功、
        // 修仙状态停在构造时给的新档起点，而不是被空 JSON 喂出一个半死的状态
        var saves = new SqliteSaveService(_saveDirectory);
        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 3 日 16:00"),
            new ISaveable[] { new RawBlob("time", 1, """{"Year":1,"Season":"Spring","Day":3,"Hour":16,"Minute":0}""") });

        CultivationSystem cultivation = new(Roots, Realms, Speed, "grade_false", null, "qi_refining", 1);

        Assert.True(saves.Load(Slot, new ISaveable[] { cultivation }));

        Assert.Equal("grade_false", cultivation.Grade.Id);
        Assert.Equal(1, cultivation.Stage);
        Assert.Equal(0, cultivation.Cultivation);

        // 读档之后照常能修炼：这不是「读进来了但坏了」，是真的能接着玩
        Assert.Equal(3, cultivation.Meditate(SpringMorning, 60));
        Assert.Equal(1, cultivation.Stage);   // 3 点还不到一层的 10 点
    }

    [Fact]
    public void M3_1旧档_有灵根与境界没有修为字段_读成零并接着能修炼()
    {
        // M3-1 写下的真格式（Version 1，没有 Cultivation 这一列）。这是本轮唯一的兼容承诺：
        // 修为缺席读成 0 是迁移决定——那份存档里的玩家确实一点修为都没练过，而修为的初值本就该是 0
        const string version1 =
            """{ "GradeId": "grade_mutation", "RootId": "root_wind", "RealmId": "qi_refining", "Stage": 4 }""";

        var saves = new SqliteSaveService(_saveDirectory);
        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 3 日 16:00"),
            new ISaveable[] { new RawBlob("cultivation", 1, version1) });

        CultivationSystem cultivation = new(Roots, Realms, Speed, "grade_false", null, "qi_refining", 1);

        Assert.True(saves.Load(Slot, new ISaveable[] { cultivation }));

        Assert.Equal("grade_mutation", cultivation.Grade.Id);
        Assert.Equal("root_wind", cultivation.Root!.Id);
        Assert.Equal(4, cultivation.Stage);
        Assert.Equal(0, cultivation.Cultivation);

        // 接着修炼：变异灵根 2.5x × 春 1.10 = 27.5 → 28 点，四层要 40 点，还差一点
        Assert.Equal(28, cultivation.Meditate(SpringMorning, 60));
        Assert.Equal(4, cultivation.Stage);
        Assert.Equal(28, cultivation.Cultivation);
    }

    [Fact]
    public void M3_2新档_修为与层数跨进程不变()
    {
        // 存 → 读：读档方用另一个起点构造，证明结果来自存档而不是初始值恰好相同。
        // 修为要经得起「练到一半就退出游戏」——那正是玩家最常做的事
        var saves = new SqliteSaveService(_saveDirectory);

        CultivationSystem original = new(Roots, Realms, Speed, "grade_heaven", null, "qi_refining", 1);
        original.Meditate(SpringMorning, 60);
        Assert.Equal(2, original.Stage);          // 天灵根 2.0x × 春 1.10 = 22 点：一层要 10，余 12
        Assert.Equal(12, original.Cultivation);

        saves.Save(Slot, new SaveMeta(WorldSeed, "新农场", "第 1 年 春 1 日 6:00"),
            new ISaveable[] { original });

        CultivationSystem restored = new(Roots, Realms, Speed, "grade_false", null, "qi_refining", 1);

        Assert.True(saves.Load(Slot, new ISaveable[] { restored }));

        Assert.Equal("grade_heaven", restored.Grade.Id);
        Assert.Equal(2, restored.Stage);
        Assert.Equal(12, restored.Cultivation);
    }

    [Fact]
    public void 读档之后写回去_版本升到二且修为还在()
    {
        // 迁移只做一半的常见形态：读旧档没问题，但保存时又把旧格式写回去。
        // 这里读一份 Version 1 的档、再原样存一遍，落盘的那份必须是 Version 2 且带着修为
        var saves = new SqliteSaveService(_saveDirectory);
        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 3 日 16:00"),
            new ISaveable[]
            {
                new RawBlob("cultivation", 1,
                    """{ "GradeId": "grade_true_dual", "RootId": null, "RealmId": "qi_refining", "Stage": 4 }"""),
            });

        var migrated = new CultivationSystem(Roots, Realms, Speed, "grade_false", null, "qi_refining", 1);
        Assert.True(saves.Load(Slot, new ISaveable[] { migrated }));
        migrated.Meditate(SpringMorning, 60);   // 11 点

        saves.Save(Slot, new SaveMeta(WorldSeed, "旧农场", "第 1 年 春 4 日 16:00"), new ISaveable[] { migrated });

        CultivationSystem reloaded = new(Roots, Realms, Speed, "grade_false", null, "qi_refining", 1);
        Assert.True(saves.Load(Slot, new ISaveable[] { reloaded }));

        Assert.Equal(4, reloaded.Stage);
        Assert.Equal(11, reloaded.Cultivation);
        Assert.Equal(2, migrated.Version);
    }

    /// <summary>
    /// 按指定的键、版本与原始 JSON 往 blob 表里写一条——**只有替身写得出旧格式**：
    /// 用真系统写出来的永远是新格式，那样验的「兼容」是假兼容。
    /// </summary>
    private sealed class RawBlob : ISaveable
    {
        private readonly int _version;
        private readonly string _json;

        public RawBlob(string key, int version, string json)
        {
            SaveKey = key;
            _version = version;
            _json = json;
        }

        public string SaveKey { get; }

        public int Version => _version;

        public string Serialize() => _json;

        public void Deserialize(string json, int fromVersion) => throw new NotSupportedException(
            "替身只用来写旧格式的档，不参与读档");
    }
}
