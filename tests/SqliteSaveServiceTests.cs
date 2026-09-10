using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using XingGame.Core.Save;

namespace XingGame.Tests;

public sealed class SqliteSaveServiceTests : IDisposable
{
    private const int CurrentSchemaVersion = 1;

    private readonly string _directory;
    private readonly SqliteSaveService _saves;

    public SqliteSaveServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xing-save-tests", Guid.NewGuid().ToString());
        _saves = new SqliteSaveService(_directory);
    }

    public void Dispose()
    {
        // 清目录失败不该让测试变红：要断言的是存档行为，不是临时目录的回收
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Constructor_CreatesMissingSaveDirectory()
    {
        var nested = Path.Combine(_directory, "not", "there", "yet");

        var saves = new SqliteSaveService(nested);

        Assert.True(Directory.Exists(nested));
        Assert.Empty(saves.ListSlots());
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsJsonAndVersion()
    {
        _saves.Save(1, Meta(), new ISaveable[] { new FakeSaveable("time", version: 3, payload: "{\"day\":7}") });

        var reloaded = new FakeSaveable("time", version: 99, payload: "{}");

        Assert.True(_saves.Load(1, new ISaveable[] { reloaded }));
        Assert.Equal("{\"day\":7}", reloaded.ReceivedJson);
        Assert.Equal(3, reloaded.ReceivedFromVersion);
    }

    [Fact]
    public void Save_OverwritesExistingSlot()
    {
        _saves.Save(1, Meta(seed: 111), new ISaveable[] { new FakeSaveable("time", 1, "旧数据") });
        _saves.Save(1, Meta(seed: 222), new ISaveable[] { new FakeSaveable("time", 1, "新数据") });

        var reloaded = new FakeSaveable("time", 1, "{}");
        Assert.True(_saves.Load(1, new ISaveable[] { reloaded }));

        Assert.Equal("新数据", reloaded.ReceivedJson);
        Assert.True(_saves.TryPeekWorldSeed(1, out var seed));
        Assert.Equal(222, seed);
    }

    [Fact]
    public void Save_WhenSaveableDropped_RemovesItsStaleRow()
    {
        _saves.Save(1, Meta(), new ISaveable[]
        {
            new FakeSaveable("time", 1, "时间"),
            new FakeSaveable("farm", 1, "农场"),
        });

        // 第二次存少了 farm（系统停用 / Mod 卸载）：那条旧行不该被后来的读档当成"存档里有"
        _saves.Save(1, Meta(), new ISaveable[] { new FakeSaveable("time", 1, "时间") });

        var farm = new FakeSaveable("farm", 1, "{}");
        Assert.True(_saves.Load(1, new ISaveable[] { farm }));

        Assert.Null(farm.ReceivedJson);
    }

    [Fact]
    public void Load_WhenSlotMissing_ReturnsFalse()
    {
        Assert.False(_saves.Load(4, new ISaveable[] { new FakeSaveable("time", 1, "x") }));
    }

    [Fact]
    public void TryPeekWorldSeed_ReadsMetaWithoutTouchingBlobs()
    {
        _saves.Save(2, Meta(seed: 20260911), new ISaveable[] { new FakeSaveable("time", 1, "时间") });

        // 把 blob 整个抹掉，peek 仍须拿到种子 —— 证明它只读 meta（TimeService 构造时就要种子）
        ExecuteSql(SlotFile(2), "DELETE FROM blob;");

        Assert.True(_saves.TryPeekWorldSeed(2, out var seed));
        Assert.Equal(20260911, seed);
    }

    [Fact]
    public void TryPeekWorldSeed_WhenSlotMissing_ReturnsFalse()
    {
        Assert.False(_saves.TryPeekWorldSeed(7, out var seed));
        Assert.Equal(0, seed);
    }

    [Fact]
    public void Slots_DoNotInterfereWithEachOther()
    {
        _saves.Save(1, Meta(seed: 1), new ISaveable[] { new FakeSaveable("time", 1, "一位") });
        _saves.Save(3, Meta(seed: 3), new ISaveable[] { new FakeSaveable("time", 1, "三位") });

        var first = new FakeSaveable("time", 1, "{}");
        var third = new FakeSaveable("time", 1, "{}");

        Assert.True(_saves.Load(1, new ISaveable[] { first }));
        Assert.True(_saves.Load(3, new ISaveable[] { third }));

        Assert.Equal("一位", first.ReceivedJson);
        Assert.Equal("三位", third.ReceivedJson);
        Assert.False(_saves.Exists(2));
        Assert.False(_saves.Load(2, new ISaveable[] { new FakeSaveable("time", 1, "{}") }));
    }

    [Fact]
    public void Delete_IsIdempotent()
    {
        _saves.Delete(5);
        _saves.Delete(5);

        Assert.False(_saves.Exists(5));
    }

    [Fact]
    public void Delete_RemovesExistingSlotAndAllowsSavingAgain()
    {
        _saves.Save(6, Meta(), new ISaveable[] { new FakeSaveable("time", 1, "旧的") });
        _saves.Delete(6);

        Assert.False(_saves.Exists(6));
        Assert.False(File.Exists(SlotFile(6)));
        Assert.Empty(_saves.ListSlots());

        _saves.Save(6, Meta(), new ISaveable[] { new FakeSaveable("time", 1, "新的") });

        var reloaded = new FakeSaveable("time", 1, "{}");
        Assert.True(_saves.Load(6, new ISaveable[] { reloaded }));
        Assert.Equal("新的", reloaded.ReceivedJson);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-1)]
    public void SlotOutOfRange_Throws(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _saves.Exists(slot));
        Assert.Throws<ArgumentOutOfRangeException>(() => _saves.Delete(slot));
        Assert.Throws<ArgumentOutOfRangeException>(() => _saves.TryPeekWorldSeed(slot, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => _saves.Save(slot, Meta(), Array.Empty<ISaveable>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _saves.Load(slot, Array.Empty<ISaveable>()));
    }

    [Fact]
    public void Load_WhenSaveComesFromNewerVersion_Throws()
    {
        _saves.Save(1, Meta(), new ISaveable[] { new FakeSaveable("time", 1, "时间") });
        ExecuteSql(SlotFile(1),
            $"UPDATE meta SET value = '999' WHERE \"key\" = 'schema_version';");

        var exception = Assert.Throws<InvalidDataException>(
            () => _saves.Load(1, new ISaveable[] { new FakeSaveable("time", 1, "{}") }));

        Assert.Contains("更新", exception.Message);
        Assert.Contains("999", exception.Message);
    }

    [Fact]
    public void Save_WhenSaveableThrows_LeavesPreviousContentIntact()
    {
        _saves.Save(1, Meta(), new ISaveable[] { new FakeSaveable("time", 1, "完好无损的旧档") });

        Assert.Throws<InvalidOperationException>(() => _saves.Save(1, Meta(), new ISaveable[]
        {
            new FakeSaveable("time", 1, "写了一半的新档"),
            new ThrowingSaveable(),
        }));

        Assert.True(_saves.Exists(1));

        var reloaded = new FakeSaveable("time", 1, "{}");
        Assert.True(_saves.Load(1, new ISaveable[] { reloaded }));
        Assert.Equal("完好无损的旧档", reloaded.ReceivedJson);
    }

    [Fact]
    public void Save_WhenFirstSaveThrows_LeavesNoSlotBehind()
    {
        Assert.Throws<InvalidOperationException>(
            () => _saves.Save(2, Meta(), new ISaveable[] { new ThrowingSaveable() }));

        Assert.False(_saves.Exists(2));
        Assert.False(File.Exists(SlotFile(2)));
        Assert.Empty(_saves.ListSlots());
    }

    [Fact]
    public void ListSlots_WhenNoSaves_ReturnsEmpty()
    {
        Assert.Empty(_saves.ListSlots());
    }

    [Fact]
    public void ListSlots_ListsOnlyExistingSlots()
    {
        Assert.Empty(_saves.ListSlots());

        _saves.Save(1, new SaveMeta(101, "雾谷一农场", "元年 春 1 日 6:00"),
            new ISaveable[] { new FakeSaveable("time", 1, "一") });
        _saves.Save(3, new SaveMeta(303, "雾谷三农场", "元年 秋 12 日 20:30"),
            new ISaveable[] { new FakeSaveable("time", 1, "三") });

        var slots = _saves.ListSlots();

        Assert.Equal(2, slots.Count);
        Assert.Equal(new[] { 1, 3 }, new[] { slots[0].Slot, slots[1].Slot });

        Assert.Equal(101, slots[0].Meta.WorldSeed);
        Assert.Equal("雾谷一农场", slots[0].Meta.FarmName);
        Assert.Equal("元年 春 1 日 6:00", slots[0].Meta.GameTimeText);
        Assert.Equal(CurrentSchemaVersion, slots[0].SchemaVersion);

        Assert.Equal(303, slots[1].Meta.WorldSeed);
        Assert.Equal("雾谷三农场", slots[1].Meta.FarmName);

        foreach (var info in slots)
        {
            Assert.Equal(DateTimeKind.Utc, info.SavedAtUtc.Kind);
            Assert.True((DateTime.UtcNow - info.SavedAtUtc).Duration() < TimeSpan.FromMinutes(5));
        }
    }

    [Fact]
    public void ListSlots_AfterDelete_DropsTheSlot()
    {
        _saves.Save(1, Meta(), new ISaveable[] { new FakeSaveable("time", 1, "一") });
        _saves.Save(2, Meta(), new ISaveable[] { new FakeSaveable("time", 1, "二") });

        _saves.Delete(1);

        var slots = _saves.ListSlots();

        Assert.Single(slots);
        Assert.Equal(2, slots[0].Slot);
    }

    private static SaveMeta Meta(int seed = 20260911) => new(seed, "雾谷农场", "元年 春 1 日 6:00");

    private string SlotFile(int slot) => Path.Combine(_directory, $"slot_{slot:00}.db");

    /// <summary>绕过服务直接改数据库，用来伪造服务自己写不出来的存档（比如更高版本）。</summary>
    private static void ExecuteSql(string path, string sql)
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

    private sealed class FakeSaveable : ISaveable
    {
        public FakeSaveable(string key, int version, string payload)
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

    private sealed class ThrowingSaveable : ISaveable
    {
        public string SaveKey => "boom";

        public int Version => 1;

        public string Serialize() => throw new InvalidOperationException("存档写到一半炸了（测试用）");

        public void Deserialize(string json, int fromVersion) =>
            throw new InvalidOperationException("不该被调用（测试用）");
    }
}
