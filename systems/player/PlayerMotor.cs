using System;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace XingGame.Systems.Player;

/// <summary>
/// 玩家移动手感参数。速度做成配置项而非硬编码（架构原则「数据驱动」、ADR-011），
/// Mod 改一个数字就能调走位手感。
/// </summary>
public sealed record PlayerConfig
{
    /// <summary>缺省速度，像素/秒。</summary>
    public const float DefaultMoveSpeed = 100f;

    /// <summary>缺省配置位置，相对工程根目录。</summary>
    public const string DefaultRelativePath = "data/config/player.json";

    public float MoveSpeed { get; init; } = DefaultMoveSpeed;

    /// <summary>从 <c>{ "moveSpeed": 100 }</c> 解析；缺键取默认值。</summary>
    public static PlayerConfig FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        float moveSpeed = new PlayerConfig().MoveSpeed;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, nameof(MoveSpeed), StringComparison.OrdinalIgnoreCase))
                moveSpeed = property.Value.GetSingle();
        }

        // 非正速度会让玩家卡死，NaN/∞ 会让位置直接发散——两种都不会报错，只会让操作变得莫名其妙。
        // 配置写错要在启动时就炸掉，而不是玩到一半才发现。
        if (!float.IsFinite(moveSpeed) || moveSpeed <= 0f)
            throw new InvalidDataException($"moveSpeed 必须为正的有限数，实际为 {moveSpeed}");

        return new PlayerConfig { MoveSpeed = moveSpeed };
    }

    public static PlayerConfig FromFile(string path) => FromJson(File.ReadAllText(path));

    /// <summary>
    /// 从构建输出目录逐级上溯找缺省配置。不依赖 csproj 的复制规则——测试宿主与 Godot
    /// 的 BaseDirectory 都埋在各自的 bin 下，上溯才能同时命中工程根目录的 data/。
    /// </summary>
    public static PlayerConfig LoadDefault()
    {
        string? path = FindDefaultFile();
        if (path is null)
            throw new FileNotFoundException($"未找到 {DefaultRelativePath}（已从 {AppContext.BaseDirectory} 逐级上溯）");

        return FromFile(path);
    }

    private static string? FindDefaultFile()
    {
        foreach (string root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, DefaultRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// 玩家移动手感：把原始输入轴换算成本帧期望速度。纯 C#，不认识 Godot（ADR-002），
/// 位置全交给引擎物理持有（ADR-011）——这里只出「想怎么走」。
/// </summary>
public sealed class PlayerMotor
{
    private readonly float _moveSpeed;

    /// <param name="moveSpeed">像素/秒</param>
    public PlayerMotor(float moveSpeed)
    {
        if (!float.IsFinite(moveSpeed) || moveSpeed <= 0f)
            throw new ArgumentOutOfRangeException(nameof(moveSpeed), moveSpeed, "速度必须为正的有限数");

        _moveSpeed = moveSpeed;
    }

    /// <summary>本帧期望速度（像素/秒）。零输入时为零向量。</summary>
    public Vector2 Velocity { get; private set; }

    /// <summary>
    /// 最后一次非零输入的方向（已归一化）。零输入时<b>保持不变</b>——
    /// 否则玩家一松手，朝向就跳回默认值，动画与交互朝向会闪。
    /// </summary>
    public Vector2 Facing { get; private set; }

    /// <summary>每帧由桥接层喂入原始输入轴（未归一化，各分量 -1..1）。</summary>
    public void Step(Vector2 input)
    {
        // 先挡零输入：Vector2.Normalize 对零向量会返回 NaN（0 除以 0），
        // 一旦写进 Facing 就再也洗不干净，而且零输入时朝向本就不该被改写。
        if (input == Vector2.Zero)
        {
            Velocity = Vector2.Zero;
            return;
        }

        // 归一化是「手感」的核心：不归一化的话斜向是直线速度的 √2 倍，
        // 玩家会本能地贴着斜线走。超范围输入（分量绝对值 > 1，某些设备会给）同样按方向缩放，
        // 结果长度恒为 1——故这里用归一化而不是先钳位再算。
        Vector2 direction = Vector2.Normalize(input);

        Velocity = direction * _moveSpeed;
        Facing = direction;
    }
}
