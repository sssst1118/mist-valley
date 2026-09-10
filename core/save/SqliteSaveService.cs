using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace XingGame.Core.Save;

/// <summary>
/// SQLite 存档服务：一个存档位一个文件、meta / blob 两级结构（ADR-009）。
/// </summary>
/// <remarks>
/// 三条实现约定：
/// <list type="number">
///   <item><b>先序列化，后写库</b>。所有 <see cref="ISaveable.Serialize"/> 都在碰数据库之前跑完：
///         事务只挡得住「写了一半的库」，挡不住「一半系统写了新数据、一半还是旧的」——
///         后者照样是个读得出来但自相矛盾的档，比这次保存直接失败更糟。</item>
///   <item><b>不开连接池</b>。存档是低频操作，池省下的那点开销不值得换来「删了档文件未必真的没了」
///         这种不确定——池会攥着文件句柄不放，Windows 上删文件的成败就变得看运气。</item>
///   <item><b>schema_version 只管表结构</b>，各系统的 JSON 形态由 <see cref="ISaveable.Version"/>
///         各自负责（ADR-009），所以这里不设全局的「数据版本」。</item>
/// </list>
/// </remarks>
public sealed class SqliteSaveService : ISaveService
{
    private const int SlotCountMax = 10;        // §16.1
    private const int CurrentSchemaVersion = 1; // 基础设施（表结构）的版本

    private const string MetaKeySchemaVersion = "schema_version";
    private const string MetaKeyWorldSeed = "world_seed";
    private const string MetaKeyFarmName = "farm_name";
    private const string MetaKeyGameTimeText = "game_time_text";
    private const string MetaKeySavedAt = "saved_at";

    private const string SavedAtFormat = "o";

    private readonly string _saveDirectory;

    /// <param name="saveDirectory">存档目录，由桥接层注入（core/ 是纯 C#，解析不了 user://）。</param>
    public SqliteSaveService(string saveDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveDirectory);

        _saveDirectory = saveDirectory;

        // 首次存档时目录多半还不存在；让调用方每次自己建目录是能忘的，忘了就崩在写库上
        Directory.CreateDirectory(saveDirectory);
    }

    public int SlotCount => SlotCountMax;

    public bool Exists(int slot) => File.Exists(SlotPath(slot));

    public void Delete(int slot)
    {
        // 幂等：删不存在的位不抛——调用方不该为了删个档先去查它存不存在。
        // File.Delete 对不存在的文件本来就是空操作。
        File.Delete(SlotPath(slot));
    }

    public bool TryPeekWorldSeed(int slot, out int worldSeed)
    {
        var path = SlotPath(slot);
        worldSeed = 0;

        if (!File.Exists(path)) return false;

        try
        {
            using var connection = OpenConnection(path, SqliteOpenMode.ReadOnly);
            var meta = ReadMeta(connection);

            return meta.TryGetValue(MetaKeyWorldSeed, out var text)
                   && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out worldSeed);
        }
        catch (SqliteException)
        {
            // 文件在、内容却读不出来：按「这个位没有可用的存档」回报，让调用方走新建存档的路。
            // Try 方法不该用异常来回答一个问题。
            return false;
        }
    }

    public IReadOnlyList<SaveSlotInfo> ListSlots()
    {
        var slots = new List<SaveSlotInfo>();

        for (var slot = 1; slot <= SlotCountMax; slot++)
        {
            var path = SlotPath(slot);
            if (!File.Exists(path)) continue;

            // 列表是给 UI 画存档界面用的，而存档目录里可能躺着不是本服务写的文件
            // （用户手放进去的、上次崩在写库途中的）。一个坏文件不该让整个列表打不开：
            // 认得出就列，认不出就跳过——真要读它时 Load 会明确报错。
            try
            {
                var info = ReadSlotInfo(slot, path);
                if (info is not null) slots.Add(info);
            }
            catch (SqliteException)
            {
            }
        }

        return slots;
    }

    public void Save(int slot, SaveMeta meta, IEnumerable<ISaveable> saveables)
    {
        var path = SlotPath(slot);
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(saveables);

        var entries = SerializeAll(saveables);

        var isNewSlot = !File.Exists(path);
        try
        {
            WriteAll(path, meta, entries);
        }
        catch
        {
            // 这次保存没写成的自建文件必须删掉：留个半成品在那儿，Exists 与 ListSlots
            // 都会把它当成一个存在的存档位，玩家会看到一格点开就报错的空档。
            if (isNewSlot) TryDeleteFile(path);
            throw;
        }
    }

    public bool Load(int slot, IEnumerable<ISaveable> saveables)
    {
        var path = SlotPath(slot);
        ArgumentNullException.ThrowIfNull(saveables);

        if (!File.Exists(path)) return false;

        using var connection = OpenConnection(path, SqliteOpenMode.ReadOnly);

        var meta = ReadMeta(connection);
        if (!TryGetMetaInt(meta, MetaKeySchemaVersion, out var schemaVersion))
        {
            throw new InvalidDataException(
                $"存档位 {slot} 的文件里没有 {MetaKeySchemaVersion}，不是本服务写出的存档。");
        }

        if (schemaVersion > CurrentSchemaVersion)
        {
            // 绝不猜着读：读错数据比读不出来更糟——读不出来玩家只是失去一次进度，
            // 读错了会把错误的数据接着写回存档里
            throw new InvalidDataException(
                $"存档位 {slot} 的存档来自更新的版本（存档 schema_version={schemaVersion}，"
                + $"当前支持到 {CurrentSchemaVersion}），无法读取。");
        }

        var stored = ReadBlobs(connection);

        foreach (var saveable in saveables)
        {
            if (saveable is null)
            {
                throw new ArgumentException("saveables 中不能有 null 元素。", nameof(saveables));
            }

            // 存档里没有这个键：多半是加存档之后新加的系统（或 Mod 卸载后重装）。
            // 跳过、让它保持自己的初始状态，好过拿一个空 JSON 去喂 Deserialize。
            if (!stored.TryGetValue(saveable.SaveKey, out var blob)) continue;

            saveable.Deserialize(blob.Json, blob.Version);
        }

        return true;
    }

    /// <summary>
    /// 把各系统序列化出来，顺便把「键全局唯一」这条契约在写库前查掉——
    /// 让重复的键炸在 SQLite 的 UNIQUE 约束上，错误现场离病因太远。
    /// </summary>
    private static List<BlobEntry> SerializeAll(IEnumerable<ISaveable> saveables)
    {
        var entries = new List<BlobEntry>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var saveable in saveables)
        {
            if (saveable is null)
            {
                throw new ArgumentException("saveables 中不能有 null 元素。", nameof(saveables));
            }

            if (string.IsNullOrEmpty(saveable.SaveKey))
            {
                throw new ArgumentException(
                    $"{saveable.GetType().Name} 的 SaveKey 为空，契约要求它全局唯一。", nameof(saveables));
            }

            if (!seenKeys.Add(saveable.SaveKey))
            {
                throw new ArgumentException(
                    $"SaveKey 重复：\"{saveable.SaveKey}\"，契约要求它全局唯一。", nameof(saveables));
            }

            entries.Add(new BlobEntry(saveable.SaveKey, saveable.Version, saveable.Serialize()));
        }

        return entries;
    }

    private static void WriteAll(string path, SaveMeta meta, List<BlobEntry> entries)
    {
        using var connection = OpenConnection(path, SqliteOpenMode.ReadWriteCreate);

        // 整个写入是一个事务：崩在中途宁可丢掉这次保存，也不能留下一个半新半旧的档（ADR-009）
        using var transaction = connection.BeginTransaction();

        CreateSchema(connection, transaction);

        // 先清空：上次存过、这次没存的系统（停用的 Mod、被移除的系统）不该留下旧行，
        // 否则那些键会被后来的读档当成「存档里有」而误用。
        Execute(connection, transaction, "DELETE FROM meta;");
        Execute(connection, transaction, "DELETE FROM blob;");

        InsertMeta(connection, transaction, MetaKeySchemaVersion,
            CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
        InsertMeta(connection, transaction, MetaKeyWorldSeed,
            meta.WorldSeed.ToString(CultureInfo.InvariantCulture));
        InsertMeta(connection, transaction, MetaKeyFarmName, meta.FarmName ?? string.Empty);
        InsertMeta(connection, transaction, MetaKeyGameTimeText, meta.GameTimeText ?? string.Empty);
        InsertMeta(connection, transaction, MetaKeySavedAt,
            DateTime.UtcNow.ToString(SavedAtFormat, CultureInfo.InvariantCulture));

        foreach (var entry in entries)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO blob (\"key\", version, json) VALUES ($key, $version, $json);";
            command.Parameters.AddWithValue("$key", entry.Key);
            command.Parameters.AddWithValue("$version", entry.Version);
            command.Parameters.AddWithValue("$json", entry.Json);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>meta 表里只放展示用的信息，读它不需要构造任何系统，也不需要解析任何 JSON。</summary>
    private static void CreateSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        // 建表也在事务里：DDL 在 SQLite 里可回滚，于是「文件存在」就等价于「表结构齐全」
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS meta (
                "key" TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS blob (
                "key"     TEXT PRIMARY KEY,
                version   INTEGER NOT NULL,
                json      TEXT NOT NULL
            );
            """);
    }

    private static Dictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"key\", value FROM meta;";
        using var reader = command.ExecuteReader();

        while (reader.Read()) meta[reader.GetString(0)] = reader.GetString(1);

        return meta;
    }

    private static Dictionary<string, BlobEntry> ReadBlobs(SqliteConnection connection)
    {
        var blobs = new Dictionary<string, BlobEntry>(StringComparer.Ordinal);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"key\", version, json FROM blob;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var key = reader.GetString(0);
            blobs[key] = new BlobEntry(key, reader.GetInt32(1), reader.GetString(2));
        }

        return blobs;
    }

    private static SaveSlotInfo? ReadSlotInfo(int slot, string path)
    {
        using var connection = OpenConnection(path, SqliteOpenMode.ReadOnly);
        var meta = ReadMeta(connection);

        // 认不出存档的必要信息就当它不是存档：农场名之类的展示字段可以缺（旧档补写），
        // 但种子与保存时间是存档列表的骨架，缺了就没法列
        if (!TryGetMetaInt(meta, MetaKeySchemaVersion, out var schemaVersion)) return null;
        if (!TryGetMetaInt(meta, MetaKeyWorldSeed, out var worldSeed)) return null;
        if (!TryGetMetaUtc(meta, MetaKeySavedAt, out var savedAt)) return null;

        meta.TryGetValue(MetaKeyFarmName, out var farmName);
        meta.TryGetValue(MetaKeyGameTimeText, out var gameTimeText);

        return new SaveSlotInfo(
            slot,
            new SaveMeta(worldSeed, farmName ?? string.Empty, gameTimeText ?? string.Empty),
            savedAt,
            schemaVersion);
    }

    private static bool TryGetMetaInt(Dictionary<string, string> meta, string key, out int value)
    {
        value = 0;

        return meta.TryGetValue(key, out var text)
               && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetMetaUtc(Dictionary<string, string> meta, string key, out DateTime value)
    {
        value = default;

        if (!meta.TryGetValue(key, out var text)) return false;

        // 存的是 "o" 带 Z 的 UTC 时刻；解析失败不该让列表整个炸掉
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed)) return false;

        value = parsed.ToUniversalTime();
        return true;
    }

    private static void InsertMeta(
        SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO meta (\"key\", value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenConnection(string path, SqliteOpenMode mode)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private string SlotPath(int slot)
    {
        if (slot < 1 || slot > SlotCountMax)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"存档位取值 1..{SlotCountMax}（§16.1）。");
        }

        return Path.Combine(_saveDirectory, $"slot_{slot:00}.db");
    }

    private static void TryDeleteFile(string path)
    {
        // 清理失败不该盖住真正的原因——调用方要看到的是「保存为什么没成功」
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct BlobEntry(string Key, int Version, string Json);
}
