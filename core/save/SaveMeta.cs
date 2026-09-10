using System;

namespace XingGame.Core.Save;

/// <summary>
/// 存档位的展示信息（§16.1 存档列表要显示的东西）。
/// </summary>
/// <remarks>
/// 这一份单独住在 <c>meta</c> 表里，不跟各系统的 blob 混在一起——列存档列表要能在
/// 不构造任何系统、不解析任何 JSON 的前提下把列表画出来。
/// </remarks>
public sealed record SaveMeta(int WorldSeed, string FarmName, string GameTimeText);

public sealed record SaveSlotInfo(int Slot, SaveMeta Meta, DateTime SavedAtUtc, int SchemaVersion);
