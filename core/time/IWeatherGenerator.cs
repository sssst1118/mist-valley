namespace XingGame.Core.Time;

/// <summary>
/// 天气掷点。<b>纯函数</b>：同 (season, dayIndex, seed) 必得同结果——存档回放、Mod 复现与
/// 单元测试都依赖这一点，所以实现里不许出现 <c>Random.Shared</c> 或时钟（ADR-006）。
/// </summary>
public interface IWeatherGenerator
{
    /// <param name="dayIndex">自元年开始的 0 基绝对日序，跨季跨年唯一，避免「每年同一天同天气」。</param>
    Weather Roll(Season season, int dayIndex, int seed);
}
