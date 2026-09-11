using System;
using System.IO;
using System.Text.Json;
using XingGame.Core.Save;

namespace XingGame.Systems.Combat;

/// <summary>
/// 玩家在矿洞里的位置。纯 C#：不认识 Godot，也不认识事件总线——「什么时候下去」是桥接层的事，
/// 本类只负责「在矿洞里的哪一层」这个状态本身（同 <see cref="Farming.Farmland"/> 与 <c>Inventory</c> 的分工）。
/// </summary>
/// <remarks>
/// <para>
/// <b>地面是 0 层</b>，矿洞层号从 1 起（§7.1 的 120 层即 1..120）。用一个 0 表示「不在矿洞里」，
/// 比另设一个 bool 再和层号保持同步要可靠——两份状态早晚会有一天对不上。
/// </para>
/// <para>
/// <b>电梯的解释</b>：§7.1 只说「每 10 层有电梯」，没说谁能坐、能坐到哪。本类取最宽松的解释——
/// 电梯层之间直达，含从地面下去（否则读档后想回到第 40 层得重新走一遍）。
/// <b>这不是文档给的规则，是未定义项的取值</b>，已报给主会话备案；真要改成「只能去已到过的层」，
/// 改的是这一个方法。
/// </para>
/// </remarks>
public sealed class MineProgress : ISaveable
{
    /// <summary>地面。矿洞层号从 1 起，0 表示没在矿洞里。</summary>
    public const int Surface = 0;

    private readonly MineDefinition _mine;
    private int _currentLayer = Surface;

    public MineProgress(MineDefinition mine) =>
        _mine = mine ?? throw new ArgumentNullException(nameof(mine));

    public MineDefinition Mine => _mine;

    /// <summary>当前层；<see cref="Surface"/>（0）表示在地面。</summary>
    public int CurrentLayer => _currentLayer;

    public bool IsUnderground => _currentLayer > Surface;

    /// <summary>下矿：地面 → 第 1 层。已经在矿洞里返回 false（不改变层数）。</summary>
    public bool Enter()
    {
        if (IsUnderground) return false;

        _currentLayer = 1;
        return true;
    }

    /// <summary>下一层。不在矿洞里、或已经在最深层返回 false（不会越界到 121 层）。</summary>
    public bool Descend()
    {
        if (!IsUnderground || _currentLayer >= _mine.LayerCount) return false;

        _currentLayer++;
        return true;
    }

    /// <summary>回地面。已经在地面返回 false。</summary>
    public bool Leave()
    {
        if (!IsUnderground) return false;

        _currentLayer = Surface;
        return true;
    }

    /// <summary>该层有没有电梯（§7.1：每 10 层有电梯）。</summary>
    public bool HasElevatorAt(int layer) => _mine.IsElevatorLayer(layer);

    /// <summary>乘电梯直达某个电梯层。该层没有电梯则返回 false，且不改变当前位置。</summary>
    public bool TakeElevator(int layer)
    {
        if (!_mine.IsElevatorLayer(layer)) return false;

        _currentLayer = layer;
        return true;
    }

    public string SaveKey => "mine_progress";

    /// <summary>JSON 形态变了就 +1，并在 <see cref="Deserialize"/> 里按 <c>fromVersion</c> 迁移。</summary>
    public int Version => 1;

    /// <summary>
    /// 存矿洞 id 与当前层。<b>矿洞 id 必须存</b>：将来有第二座矿洞时，只有层数的话，
    /// 一份「在第 40 层」的存档既可能是这座矿洞的，也可能是那座矿洞的，读回来给谁都不对。
    /// </summary>
    public string Serialize() =>
        JsonSerializer.Serialize(new SavedProgress(_mine.Id, _currentLayer), SaveJsonOptions);

    public void Deserialize(string json, int fromVersion)
    {
        // 来自更新版本的存档不能猜着读：读错数据比读不出来更糟（ADR-009 同款理由）
        if (fromVersion > Version)
            throw new NotSupportedException($"矿洞进度存档版本 {fromVersion} 高于当前支持的 {Version}，无法读取");

        SavedProgress saved = JsonSerializer.Deserialize<SavedProgress>(json, SaveJsonOptions)
            ?? throw new InvalidDataException("矿洞进度存档内容为空");

        if (string.IsNullOrEmpty(saved.MineId))
            throw new InvalidDataException("矿洞进度存档缺少矿洞 id");

        // 别的矿洞的进度套到这座矿洞上，层号再合法也是错的：两座矿洞的层数与内容都不一样
        if (!string.Equals(saved.MineId, _mine.Id, StringComparison.Ordinal))
            throw new InvalidDataException($"矿洞进度存档记的是矿洞「{saved.MineId}」，当前的矿洞是「{_mine.Id}」");

        // 0（地面）是合法层号，所以「字段不在」必须与「字段是 0」分开——可空类型是这两者的唯一
        // 分界线（同 Wallet 的 SavedWallet）。静默读成 0 等于把矿洞里的档悄悄挪回地面
        if (saved.CurrentLayer is not int layer)
            throw new InvalidDataException("矿洞进度存档缺少 CurrentLayer 字段");

        // 层号越界当场抛：越界值渗进来之后，「第 121 层」会一路走到取怪物表那一步才炸
        if (layer < Surface || layer > _mine.LayerCount)
            throw new InvalidDataException(
                $"矿洞进度存档的层数为 {layer}，超出 {_mine.Id} 的 0..{_mine.LayerCount}");

        _currentLayer = layer;
    }

    /// <summary>
    /// 存档 JSON 的形态。私有：外部只该经 <see cref="Serialize"/>/<see cref="Deserialize"/> 碰它。
    /// <c>int?</c> 是为了把「CurrentLayer 字段不在」与「CurrentLayer 是 0（在地面）」分开。
    /// </summary>
    private sealed record SavedProgress(string MineId, int? CurrentLayer);

    /// <summary>字段名写出来，存档要能被人和 Mod 读懂（ADR-012）。</summary>
    private static readonly JsonSerializerOptions SaveJsonOptions = new() { WriteIndented = true };
}
