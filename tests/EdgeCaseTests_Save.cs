using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using XingGame.Core.Events;
using XingGame.Core.Save;
using XingGame.Core.Time;
using XingGame.Systems.Items;

namespace XingGame.Tests;

/// <summary>
/// 存档基础设施的非法输入与坏文件（ADR-008、ADR-009）。
/// <see cref="SqliteSaveServiceTests"/> 已覆盖正常往返、原子性与版本拒绝，这里补的是
/// **ADR-009「绝不猜着读」的下半句**：坏数据到底**每一条**都被拦住了没有，
/// 以及「列表宽容、读档严格」这对刻意的不对称有没有用例守着。
/// </summary>
public sealed class EdgeCaseTests_Save : IDisposable
{
    private readonly string _directory;
    private readonly SqliteSaveService _saves;

    public EdgeCaseTests_Save()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xing-edge-save", Guid.NewGuid().ToString("N"));
        _saves = new SqliteSaveService(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ── 参数校验：非法输入要在写库之前就炸 ──────────────────────────────

    [Fact]
    public void 构造_存档目录为空或空白_当场抛()
    {
        // 空目录会变成「相对当前工作目录」的路径——存档写到哪全看进程从哪启动，
        // 这种静默的错误定位成本极高，宁可在装配期炸
        Assert.ThrowsAny<ArgumentException>(() => new SqliteSaveService(null!));
        Assert.ThrowsAny<ArgumentException>(() => new SqliteSaveService(string.Empty));
        Assert.ThrowsAny<ArgumentException>(() => new SqliteSaveService("   "));
    }

    [Fact]
    public void Save_SaveKey_重复_写库之前就抛()
    {
        // 契约：SaveKey 全局唯一（ISaveable 注释）。重复的键若放任下去，会在 SQLite 的
        // UNIQUE 约束上炸——那时错误现场离病因（两个系统撞名）隔着一整个事务，极难定位。
        var error = Assert.Throws<ArgumentException>(() => _saves.Save(1, Meta(), new ISaveable[]
        {
            new ProbeSaveable("inventory", 1, "甲"),
            new ProbeSaveable("inventory", 1, "乙"),
        }));

        Assert.Contains("inventory", error.Message);

        // 校验在碰数据库之前跑完：不该留下一个空壳存档位
        Assert.False(_saves.Exists(1));
        Assert.False(File.Exists(SlotFile(1)));
    }

    [Fact]
    public void Save_SaveKey_为空_当场抛()
    {
        var error = Assert.Throws<ArgumentException>(
            () => _saves.Save(1, Meta(), new ISaveable[] { new ProbeSaveable(string.Empty, 1, "{}") }));

        Assert.Contains("SaveKey", error.Message);
        Assert.False(_saves.Exists(1));
    }

    [Fact]
    public void Save_与_Load_遇到_null_参数或_null_元素_当场抛()
    {
        // null 参数是编程错误：Save 若接受 null saveables，就变成「一次什么都没存的保存」，
        // 而调用方以为存好了——这是最难查的一类静默失败
        Assert.Throws<ArgumentNullException>(() => _saves.Save(1, null!, Array.Empty<ISaveable>()));
        Assert.Throws<ArgumentNullException>(() => _saves.Save(1, Meta(), null!));
        Assert.Throws<ArgumentNullException>(() => _saves.Load(1, null!));

        // 集合里的 null 元素同理，且要在序列化阶段就拦下（Load 只在存档位存在时才看这个集合）
        Assert.Throws<ArgumentException>(() => _saves.Save(1, Meta(), new ISaveable[] { null! }));
        Assert.False(_saves.Exists(1));

        _saves.Save(1, Meta(), new ISaveable[] { new ProbeSaveable("time", 1, "{}") });
        Assert.Throws<ArgumentException>(() => _saves.Load(1, new ISaveable[] { null! }));
    }

    // ── 坏文件：读不出来要明确报错，不猜 ────────────────────────────────

    [Fact]
    public void Load_存档缺_schema_version_时明确报错_而不是当成老版本读下去()
    {
        // 少一个版本号就读，等于承认「任何文件都是我的存档」。ADR-009：读错数据比读不出来更糟，
        // 因为读错的数据会被接着写回存档里，错上加错。
        SaveThenRunSql("DELETE FROM meta WHERE \"key\" = 'schema_version';");

        var error = Assert.Throws<InvalidDataException>(() => _saves.Load(1, new ISaveable[] { Reload("time") }));

        Assert.Contains("schema_version", error.Message);
    }

    [Fact]
    public void Load_文件根本不是_SQLite_时明确报错()
    {
        // 存档目录里的文件不完全是自己写的（用户手放、上次崩在写库途中）。
        // Load 必须报错而不是返回 false——返回 false 会被调用方当成「这个位是空的」而新建存档，
        // 把玩家原有的文件原地覆盖掉。
        File.WriteAllText(SlotFile(1), "这不是数据库");

        Assert.True(_saves.Exists(1));
        Assert.Throws<SqliteException>(() => _saves.Load(1, new ISaveable[] { Reload("time") }));
    }

    [Fact]
    public void TryPeekWorldSeed_文件不是_SQLite_时返回_false_而不抛()
    {
        // 与 Load 刻意不对称：Try 前缀的方法不该用异常回答一个问题（代码注释里写明了的承诺）。
        // 调用方拿 false 去走「新建存档」的路，界面不该因为一个坏文件打不开。
        File.WriteAllText(SlotFile(1), "这不是数据库");

        Assert.False(_saves.TryPeekWorldSeed(1, out int seed));
        Assert.Equal(0, seed);
    }

    [Fact]
    public void ListSlots_跳过认不出的文件_其余照列()
    {
        // 存档列表是给 UI 画的：一个坏文件不该让整个列表打不开（同 TryPeekWorldSeed 的取舍）。
        // 坏的那个位仍然是「存在」的，真去读它时 Load 会明确报错——两条路各司其职。
        _saves.Save(2, Meta(seed: 222), new ISaveable[] { new ProbeSaveable("time", 1, "{}") });
        File.WriteAllText(SlotFile(3), "垃圾文件");

        var slots = _saves.ListSlots();

        Assert.Single(slots);
        Assert.Equal(2, slots[0].Slot);
        Assert.True(_saves.Exists(3));
    }

    [Fact]
    public void ListSlots_缺列表骨架字段的存档_跳过该位_但读档本身照常()
    {
        // 列表要的骨架（schema_version / world_seed / saved_at）不全时按「不是存档」跳过；
        // 而读档只需要 schema_version 和 blob——两条路的要求不同，这个不对称是刻意的：
        // 展示信息可以缺，读数据不能缺。
        _saves.Save(1, Meta(seed: 111), new ISaveable[] { new ProbeSaveable("time", 1, "完整") });
        RunSql(SlotFile(1), "DELETE FROM meta WHERE \"key\" = 'saved_at';");

        Assert.Empty(_saves.ListSlots());

        var reloaded = Reload("time");
        Assert.True(_saves.Load(1, new ISaveable[] { reloaded }));
        Assert.Equal("完整", reloaded.ReceivedJson);
    }

    [Fact]
    public void Load_存档位不存在_一个_saveable_都不碰()
    {
        // 返回 false 之外还得真的什么都没做：若先遍历调用 Deserialize 再发现文件不存在，
        // 调用方的状态会被一份空 JSON 覆盖掉，而函数却告诉你「没读成」
        var probe = Reload("time");

        Assert.False(_saves.Load(9, new ISaveable[] { probe }));
        Assert.Null(probe.ReceivedJson);
        Assert.Null(probe.ReceivedFromVersion);
    }

    // ── 跨模块：两个真实系统同存一档 ────────────────────────────────────

    [Fact]
    public void 时间与背包同存一档_两边都还原_且种子从_meta_取回()
    {
        // 用一个假 saveable 测出来的「往返正常」证明不了真系统之间没问题：两个真系统的
        // SaveKey 会不会撞、版本号会不会串、各自的 JSON 会不会互相污染，只有一起跑才知道。
        var table = Table();
        var time = new TimeService(new EventBus(), new WeatherGenerator(WeatherTable.LoadDefault()),
            worldSeed: 4242, start: new GameTime(1, Season.Spring, 1, GameTime.FirstHour, 0));
        var inventory = new Inventory(table, slotCount: 4);

        for (int day = 0; day < 9; day++) time.Sleep();   // 走一段，免得「还原」与初始值撞上
        inventory.Add("crop_parsnip", 7);                // 占两格：5 + 2，槽位布局也要能还原

        _saves.Save(1, new SaveMeta(4242, "雾谷农场", "春10日 6:00"), new ISaveable[] { time, inventory });

        // 两段式读档：种子只能从 meta 拿，拿不到就构造不出 TimeService（ADR-009）
        Assert.True(_saves.TryPeekWorldSeed(1, out int seed));
        Assert.Equal(4242, seed);

        var timeBack = new TimeService(new EventBus(), new WeatherGenerator(WeatherTable.LoadDefault()),
            worldSeed: seed);
        var inventoryBack = new Inventory(table, slotCount: 4);
        Assert.True(_saves.Load(1, new ISaveable[] { timeBack, inventoryBack }));

        Assert.Equal(time.Now, timeBack.Now);
        Assert.Equal(time.Weather, timeBack.Weather);
        Assert.Equal(7, inventoryBack.Count("crop_parsnip"));
        Assert.Equal(new ItemStack("crop_parsnip", 5), inventoryBack.Slots[0]);
        Assert.Equal(new ItemStack("crop_parsnip", 2), inventoryBack.Slots[1]);
    }

    [Fact]
    public void 读档之后继续走_天气序列与原档一模一样()
    {
        // 存档只带当下天气，不带历史；能重放的前提是「种子 + 绝对日序 → 天气」是纯函数。
        // 若哪天有人把种子写进 blob、或改用时序相关的随机源，读档后的天气就会与原档分岔，
        // 而玩家要过好几天才发现天气对不上——那时早已存过好几次档，原始序列再也找不回来。
        var time = new TimeService(new EventBus(), new WeatherGenerator(WeatherTable.LoadDefault()),
            worldSeed: 777, start: new GameTime(1, Season.Spring, 1, GameTime.FirstHour, 0));
        for (int day = 0; day < 30; day++) time.Sleep();

        _saves.Save(1, new SaveMeta(777, "农场", "春1日"), new ISaveable[] { time });

        var reopened = new TimeService(new EventBus(), new WeatherGenerator(WeatherTable.LoadDefault()),
            worldSeed: 777);
        Assert.True(_saves.Load(1, new ISaveable[] { reopened }));
        Assert.Equal(time.Weather, reopened.Weather);

        var original = new List<Weather>();
        var restored = new List<Weather>();
        for (int day = 0; day < 30; day++)
        {
            time.Sleep();
            reopened.Sleep();
            original.Add(time.Weather);
            restored.Add(reopened.Weather);
        }

        Assert.Equal(original, restored);
        Assert.Equal(time.Now, reopened.Now);
    }

    // ── 辅助 ────────────────────────────────────────────────────────────

    private static SaveMeta Meta(int seed = 20260911) => new(seed, "雾谷农场", "元年 春 1 日 6:00");

    private static ItemTable Table() => ItemTable.FromJson("""
    {
      "items": [
        { "id": "crop_parsnip", "name": "防风草", "description": "春季作物。", "category": "Crop", "maxStack": 5, "buyPrice": 0, "sellPrice": 35 }
      ]
    }
    """);

    private static ProbeSaveable Reload(string key) => new(key, version: 1, payload: "{}");

    private string SlotFile(int slot) => Path.Combine(_directory, $"slot_{slot:00}.db");

    /// <summary>存一份完好的档，再绕过服务改库——用来伪造服务自己写不出来的坏存档。</summary>
    private void SaveThenRunSql(string sql)
    {
        _saves.Save(1, Meta(), new ISaveable[] { new ProbeSaveable("time", 1, "{}") });
        RunSql(SlotFile(1), sql);
    }

    private static void RunSql(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());

        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>手写测试替身（不引 Moq）：只记录被喂了什么，用来验「该不该被调用」。</summary>
    private sealed class ProbeSaveable : ISaveable
    {
        public ProbeSaveable(string key, int version, string payload)
        {
            SaveKey = key;
            Version = version;
            Payload = payload;
        }

        public string SaveKey { get; }

        public int Version { get; }

        public string Payload { get; }

        public string? ReceivedJson { get; private set; }

        public int? ReceivedFromVersion { get; private set; }

        public string Serialize() => Payload;

        public void Deserialize(string json, int fromVersion)
        {
            ReceivedJson = json;
            ReceivedFromVersion = fromVersion;
        }
    }
}
