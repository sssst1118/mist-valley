using System;
using System.Numerics;

namespace XingGame.Systems.Farming;

/// <summary>
/// 世界坐标 ↔ 耕地格坐标的换算。放在纯 C# 区，因为「落在哪一格」是逻辑问题：
/// 桥接层只知道玩家在哪个像素，系统只认格子，而换算规则必须只有一份——
/// 两处各算一次，迟早有一处先改。
/// </summary>
public static class FarmGrid
{
    /// <summary>
    /// 一格边长（像素）。**设计文档未定义瓦片尺寸，待裁决**——§21.1 只有目录结构，
    /// §18 只讲美术风格。取 16 是像素风农场游戏的常见量级。
    /// </summary>
    /// <remarks>
    /// 改这个数<b>不影响存档</b>：存档记的是格坐标，不是像素。这是当初把耕地做成
    /// 格子概念（ADR-014）而不是存像素坐标的回报之一。
    /// </remarks>
    public const int TileSize = 16;

    /// <summary>
    /// 世界坐标落在哪一格。
    /// </summary>
    /// <remarks>
    /// 用向下取整而非四舍五入：一格代表 <c>[n*16, n*16+16)</c> 这块区域，
    /// 四舍五入会让格边界跑到格子中间去，玩家站在格的右半边就会瞄到隔壁。
    /// </remarks>
    public static TileCoord ToTile(Vector2 worldPosition) =>
        new((int)MathF.Floor(worldPosition.X / TileSize), (int)MathF.Floor(worldPosition.Y / TileSize));

    /// <summary>格子的中心点世界坐标——渲染和高亮都用它，免得各处自己算 <c>+ TileSize/2</c>。</summary>
    public static Vector2 ToWorldCenter(TileCoord tile) =>
        new((tile.X + 0.5f) * TileSize, (tile.Y + 0.5f) * TileSize);
}
