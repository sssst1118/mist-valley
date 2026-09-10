using System;
using System.Collections.Generic;

namespace XingGame.Core.Save;

/// <summary>
/// 多存档位读写（ADR-009：一个位一个 SQLite 文件）。
/// </summary>
/// <remarks>
/// <b>读档是两段式的，顺序被逼出来不能合并</b>：<see cref="TryPeekWorldSeed"/> 先单独取种子，
/// 调用方拿它构造 <c>TimeService</c>，然后才 <see cref="Load"/> 让各系统各自反序列化——
/// 因为构造时要用的参数只能在这两步之间拿到。
/// </remarks>
public interface ISaveService
{
    /// <summary>存档位数量，10（§16.1）。</summary>
    int SlotCount { get; }

    /// <summary>只列已存在的位。</summary>
    IReadOnlyList<SaveSlotInfo> ListSlots();

    bool Exists(int slot);

    void Delete(int slot);

    /// <summary>只读 meta、不碰 blob 的轻量窥视，位不存在时返回 <c>false</c>。</summary>
    bool TryPeekWorldSeed(int slot, out int worldSeed);

    void Save(int slot, SaveMeta meta, IEnumerable<ISaveable> saveables);

    /// <summary>位不存在返回 <c>false</c>。</summary>
    bool Load(int slot, IEnumerable<ISaveable> saveables);
}
