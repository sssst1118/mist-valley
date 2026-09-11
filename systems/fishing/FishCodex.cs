using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;

namespace XingGame.Systems.Fishing;

/// <summary>
/// 鱼类图鉴：记录每种鱼钓到过几条（§17.2「图鉴：作物、鱼类…」、第 1402 行「鱼类图鉴：记录已钓到鱼」）。
/// 纯 C#：不认识 Godot，也不认识事件总线——「什么时候记一笔」是 <see cref="FishingSystem"/> 的事。
/// </summary>
/// <remarks>
/// 要 <see cref="IFishTable"/> 是为了读档时能当场认出「这条鱼不在表里」：存档是外部输入，
/// 坏值当场抛、绝不猜着读（ADR-009）。留一条不经表校验的路，就等于给「图鉴里有一堆查不到的鱼」
/// 留了口子——那种症状要到 UI 画图鉴时才显形。
/// </remarks>
public sealed class FishCodex : ISaveable
{
    private readonly IFishTable _fish;

    /// <summary>首次钓到的顺序。图鉴列表照它画——按 id 排序会让玩家的发现过程变成字母表。</summary>
    private readonly List<string> _firstCaught = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly ReadOnlyCollection<string> _view;

    public FishCodex(IFishTable fish)
    {
        _fish = fish ?? throw new ArgumentNullException(nameof(fish));
        _view = _firstCaught.AsReadOnly();
    }

    public string SaveKey => "fishing";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    public int Version => 1;

    /// <summary>已钓到的鱼 id，按首次钓到的顺序。</summary>
    public IReadOnlyList<string> CaughtSpecies => _view;

    public bool HasCaught(string fishId) => _counts.ContainsKey(fishId);

    /// <summary>钓到过的条数；没钓到过为 0。不认识的 id 也返回 0——查询不是编程错误。</summary>
    public int CountOf(string fishId) => _counts.TryGetValue(fishId, out int count) ? count : 0;

    /// <summary>
    /// 记一笔。id 不在鱼表里是编程错误（同 <c>Inventory.Add</c> 对物品 id 的态度）：
    /// 图鉴只记表里有的鱼，记进来一条查不到的，UI 迟早要面对它。
    /// </summary>
    public void Record(string fishId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fishId);

        if (!_fish.TryGet(fishId, out _))
            throw new ArgumentException($"鱼表里没有 id 为「{fishId}」的鱼", nameof(fishId));

        if (_counts.TryGetValue(fishId, out int count))
        {
            _counts[fishId] = count + 1;
            return;
        }

        _counts[fishId] = 1;
        _firstCaught.Add(fishId);
    }

    /// <summary>
    /// 存鱼 id 字符串而非它在鱼表里的序号：往表里插一条新鱼就会让旧存档的序号全部错位（ADR-012）。
    /// </summary>
    public string Serialize()
    {
        var caught = new SavedEntry[_firstCaught.Count];
        for (int index = 0; index < caught.Length; index++)
            caught[index] = new SavedEntry(_firstCaught[index], _counts[_firstCaught[index]]);

        return JsonSerializer.Serialize(new SavedCodex(caught), _saveJsonOptions);
    }

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009 同款理由）
        if (fromVersion > Version)
            throw new NotSupportedException($"鱼类图鉴存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedCodex saved = JsonSerializer.Deserialize<SavedCodex>(json, _saveJsonOptions)
            ?? throw new InvalidDataException("鱼类图鉴存档内容为空");

        SavedEntry[] caught = saved.Caught
            ?? throw new InvalidDataException("鱼类图鉴存档缺少 caught 数组");

        // 先整份校验再落盘：坏存档不该让图鉴停在「读了一半」的状态（同 Inventory.Deserialize）
        var order = new List<string>(caught.Length);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (SavedEntry entry in caught)
        {
            // 数组里可以写 null，System.Text.Json 照收。不判就是下面第一行一个 NRE 冒出去——
            // 按 ADR-009 坏档该抛 InvalidDataException，将来 catch 坏档的代码要接得住
            if (entry is null)
                throw new InvalidDataException("鱼类图鉴存档里有一条空条目");

            if (string.IsNullOrWhiteSpace(entry.FishId))
                throw new InvalidDataException("鱼类图鉴存档出现空 fishId");

            // 数量为 0 的条目就是「没钓到过」，那它根本不该被写进存档
            if (entry.Count <= 0)
                throw new InvalidDataException($"鱼类图鉴存档里「{entry.FishId}」的数量为 {entry.Count}，必须为正");

            if (!_fish.TryGet(entry.FishId, out _))
                throw new InvalidDataException($"鱼类图鉴存档里的鱼「{entry.FishId}」不在鱼表里");

            // 重复条目会让条数取决于读的顺序，而且「总共钓到几条」会静默少算
            if (!counts.TryAdd(entry.FishId, entry.Count))
                throw new InvalidDataException($"鱼类图鉴存档里重复出现「{entry.FishId}」");

            order.Add(entry.FishId);
        }

        _counts.Clear();
        foreach (KeyValuePair<string, int> pair in counts) _counts[pair.Key] = pair.Value;

        _firstCaught.Clear();
        _firstCaught.AddRange(order);
    }

    /// <summary>存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。</summary>
    private sealed record SavedCodex(SavedEntry[] Caught);

    /// <summary>字段名写出来，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private sealed record SavedEntry(string FishId, int Count);

    private static readonly JsonSerializerOptions _saveJsonOptions = new() { WriteIndented = true };
}
