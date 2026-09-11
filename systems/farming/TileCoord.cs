namespace XingGame.Systems.Farming;

/// <summary>
/// 一格耕地。整数格坐标——耕地是逻辑概念，与渲染无关。
/// </summary>
/// <remarks>
/// 不用像素坐标：像素坐标归引擎物理与渲染（ADR-011），而地块的归属关系（哪一格种了什么）
/// 是纯逻辑，用像素表示反而要回答「格子边界在哪、踩在两个格中间算谁的」这种没有意义的问题。
/// </remarks>
public readonly record struct TileCoord(int X, int Y);
