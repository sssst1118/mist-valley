using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;

namespace XingGame.Systems.Npc;

/// <summary>
/// 好感度记账。数值全部出自 §10.2：<c>心数：0-10 心，每心 250 点</c>，档位出自 §10.4。
/// </summary>
/// <remarks>
/// <para>
/// <b>只认 NPC 表里有的 id</b>：给一个表里没有的 NPC 加好感度是编程错误（id 写错了），
/// 不是运行时状况——照 <c>Inventory.Add</c> 凭空造物品同款处理，当场抛而不是默默记一笔。
/// </para>
/// <para>
/// 上限与下限都由本类夹住，<b>不靠调用方自觉</b>：封顶只在存档里留一道口子的话，
/// 一次没走存档的加值就能把心数顶到 11 心，而 UI 按 10 心画，玩家看到的是心数条溢出。
/// </para>
/// </remarks>
public sealed class FriendshipSystem : IFriendshipSystem, ISaveable
{
    /// <summary>§10.2「每心 250 点」。</summary>
    public const int PointsPerHeart = 250;

    /// <summary>§10.2「心数：0-10 心」。</summary>
    public const int MaxHearts = 10;

    /// <summary>2500 点 = 10 心。</summary>
    public const int MaxPoints = MaxHearts * PointsPerHeart;

    /// <summary>§10.2 的起点「0 心」。下限的存在理由与上限对称：掉好感不该掉成负数。</summary>
    public const int MinPoints = 0;

    private readonly INpcTable _npcs;

    /// <summary>只记「碰过的」NPC：没出现过的一律是 0，不必给 14 位各开一格。</summary>
    private readonly Dictionary<string, int> _points = new(StringComparer.Ordinal);

    public FriendshipSystem(INpcTable npcs) => _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));

    public int GetPoints(string npcId) =>
        _points.TryGetValue(Require(npcId), out int points) ? points : MinPoints;

    public int GetHearts(string npcId) => GetPoints(npcId) / PointsPerHeart;

    public FriendshipLevel GetLevel(string npcId) => FriendshipLevels.FromHearts(GetHearts(npcId));

    public int AddPoints(string npcId, int amount)
    {
        Require(npcId);

        int current = _points.TryGetValue(npcId, out int stored) ? stored : MinPoints;

        // 用 long 再夹：int.MinValue 直接加会溢出成一个大正数，于是「狠狠减好感」变成「加满」
        int next = (int)Math.Clamp((long)current + amount, MinPoints, MaxPoints);

        _points[npcId] = next;
        return next - current;
    }

    public int ReceiveGift(string npcId, string itemName, bool isBirthday)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            throw new ArgumentException("物品名不能为空", nameof(itemName));

        int points = GiftPoints.For(_npcs.Get(npcId).TasteOf(itemName));

        // §10.2「生日送礼：×8 倍」没写任何例外，所以讨厌物品的负数也照翻——文档没说的事
        // 不替它决定，改了就是悄悄给玩家减负
        if (isBirthday) points *= GiftPoints.BirthdayMultiplier;

        return AddPoints(npcId, points);
    }

    public string SaveKey => "friendship";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    public int Version => 1;

    /// <summary>
    /// <b>只存非零的 NPC</b>：这里没有背包那种「槽位位置本身就是信息」的约束（ADR-012），
    /// 存 14 行零没有意义，而存档是要被人和 Mod 读的。
    /// 键存 NPC id 字符串而不是它在表里的序号：往表里插一位就会让旧存档的序号全部错位。
    /// </summary>
    public string Serialize()
    {
        var entries = new List<SavedFriendshipEntry>();
        foreach (KeyValuePair<string, int> pair in _points)
        {
            if (pair.Value != MinPoints) entries.Add(new SavedFriendshipEntry(pair.Key, pair.Value));
        }

        return JsonSerializer.Serialize(new SavedFriendship(entries.ToArray()), _saveJsonOptions);
    }

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009）
        if (fromVersion > Version)
            throw new NotSupportedException($"好感度存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedFriendship saved = JsonSerializer.Deserialize<SavedFriendship>(json, _saveJsonOptions)
            ?? throw new InvalidDataException("好感度存档内容为空");

        SavedFriendshipEntry[] entries = saved.Points
            ?? throw new InvalidDataException("好感度存档缺少 points 数组");

        // 先整份校验再落盘：坏存档不该让好感度停在「读了一半」的状态（照 Inventory 先例）
        var restored = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (SavedFriendshipEntry entry in entries)
        {
            // 数组里可以写 null，System.Text.Json 照收。不判就是下面第一行一个 NRE 冒出去——
            // 按 ADR-009 坏档该抛 InvalidDataException，将来 catch 坏档的代码要接得住
            if (entry is null)
                throw new InvalidDataException("好感度存档里有一条空条目");

            if (string.IsNullOrWhiteSpace(entry.NpcId))
                throw new InvalidDataException("好感度存档里有一条没有 npcId");

            // 认不出的 NPC 不猜着读：多半是卸载了一个 Mod，而它的好感度接给谁是猜不出来的
            if (!_npcs.TryGet(entry.NpcId, out _))
                throw new InvalidDataException($"好感度存档里的 NPC id「{entry.NpcId}」不在 NPC 表里");

            // 0 点（0 心）是合法值，所以「字段不在」必须与「字段是 0」分开——可空类型是这两者的
            // 唯一分界线（同 Wallet 的 SavedWallet）。静默读成 0 会把好感度抹平，且下次 Serialize 就把 0 写死
            if (entry.Points is not int points)
                throw new InvalidDataException($"好感度存档里 NPC「{entry.NpcId}」缺少 points 字段");

            if (points < MinPoints || points > MaxPoints)
                throw new InvalidDataException($"NPC {entry.NpcId} 的好感度 {points} 超出 {MinPoints}..{MaxPoints}");

            // 同一位写两条，读回来是哪一条取决于文件顺序——两份都「合法」时才最危险
            if (!restored.TryAdd(entry.NpcId, points))
                throw new InvalidDataException($"好感度存档里 NPC「{entry.NpcId}」出现了两次");
        }

        _points.Clear();
        foreach (KeyValuePair<string, int> pair in restored) _points[pair.Key] = pair.Value;
    }

    /// <summary>表里没有这位就是编程错误：好感度是挂在他身上的，人都不认识，挂给谁？</summary>
    private string Require(string npcId)
    {
        if (npcId is null) throw new ArgumentNullException(nameof(npcId));

        _npcs.Get(npcId);
        return npcId;
    }

    /// <summary>存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。</summary>
    private sealed record SavedFriendship(SavedFriendshipEntry[] Points);

    /// <summary>
    /// 存档 JSON 的形态。<c>int?</c> 是为了把「字段不在」与「字段是 0」分开——
    /// 不可空的话缺字段会静默读成 0 点（合法值），那就是在猜着读了。
    /// </summary>
    private sealed record SavedFriendshipEntry(string NpcId, int? Points);

    /// <summary>字段名写全，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions _saveJsonOptions = new() { WriteIndented = true };
}
