namespace XingGame.Core.Time;

/// <summary>天气（设计文档 §3.3）。顺序即权重表 JSON 的键序，改动会改变同一 seed 的历史天气。</summary>
public enum Weather
{
    Sunny,
    Rainy,
    Storm,
    Snowy,
    Windy,
    Foggy,
}
